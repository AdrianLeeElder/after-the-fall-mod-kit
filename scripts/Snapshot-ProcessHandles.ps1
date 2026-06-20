param(
    [string]$ProcessName = "AfterTheFall",
    [int]$TargetPid = 0,
    [string]$OutputDirectory = ".\captures"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$nativeSource = @"
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class NativeHandleSnapshot
{
    private const int SystemExtendedHandleInformation = 64;
    private const int ObjectTypeInformation = 2;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);
    private const int STATUS_BUFFER_TOO_SMALL = unchecked((int)0xC0000023);
    private const int PROCESS_DUP_HANDLE = 0x0040;
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint DUPLICATE_SAME_ACCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX
    {
        public IntPtr Object;
        public UIntPtr UniqueProcessId;
        public UIntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    public sealed class HandleEntry
    {
        public int ProcessId { get; set; }
        public ulong HandleValue { get; set; }
        public int ObjectTypeIndex { get; set; }
        public string TypeName { get; set; }
        public string HandleHex { get; set; }
        public uint GrantedAccess { get; set; }
        public string GrantedAccessHex { get; set; }
        public uint HandleAttributes { get; set; }
    }

    public sealed class SnapshotResult
    {
        public int ProcessId { get; set; }
        public List<HandleEntry> Handles { get; set; }
        public int DuplicateFailures { get; set; }
        public int TypeNameFailures { get; set; }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        ref int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryObject(
        IntPtr handle,
        int objectInformationClass,
        IntPtr objectInformation,
        int objectInformationLength,
        ref int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        int desiredAccess,
        bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcessHandle,
        IntPtr sourceHandle,
        IntPtr targetProcessHandle,
        out IntPtr targetHandle,
        uint desiredAccess,
        bool inheritHandle,
        uint options);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static SnapshotResult Snapshot(int pid)
    {
        List<HandleEntry> handles = EnumerateProcessHandles(pid);
        Dictionary<int, string> typeNames = new Dictionary<int, string>();
        int duplicateFailures = 0;
        int typeNameFailures = 0;

        IntPtr sourceProcess = OpenProcess(PROCESS_DUP_HANDLE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (sourceProcess == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed for PID " + pid.ToString());
        }

        try
        {
            for (int i = 0; i < handles.Count; i++)
            {
                HandleEntry entry = handles[i];
                if (typeNames.ContainsKey(entry.ObjectTypeIndex))
                {
                    continue;
                }

                IntPtr duplicate;
                bool duplicated = DuplicateHandle(
                    sourceProcess,
                    new IntPtr(unchecked((long)entry.HandleValue)),
                    GetCurrentProcess(),
                    out duplicate,
                    0,
                    false,
                    DUPLICATE_SAME_ACCESS);

                if (!duplicated)
                {
                    duplicateFailures++;
                    continue;
                }

                try
                {
                    string typeName = QueryTypeName(duplicate);
                    if (String.IsNullOrEmpty(typeName))
                    {
                        typeNameFailures++;
                        typeName = "UnknownTypeIndex" + entry.ObjectTypeIndex.ToString();
                    }
                    typeNames[entry.ObjectTypeIndex] = typeName;
                }
                finally
                {
                    CloseHandle(duplicate);
                }
            }
        }
        finally
        {
            CloseHandle(sourceProcess);
        }

        for (int i = 0; i < handles.Count; i++)
        {
            HandleEntry entry = handles[i];
            string typeName;
            if (!typeNames.TryGetValue(entry.ObjectTypeIndex, out typeName))
            {
                typeName = "UnknownTypeIndex" + entry.ObjectTypeIndex.ToString();
            }
            entry.TypeName = typeName;
        }

        return new SnapshotResult
        {
            ProcessId = pid,
            Handles = handles,
            DuplicateFailures = duplicateFailures,
            TypeNameFailures = typeNameFailures
        };
    }

    private static List<HandleEntry> EnumerateProcessHandles(int pid)
    {
        int length = 0x100000;
        IntPtr buffer = IntPtr.Zero;

        try
        {
            while (true)
            {
                buffer = Marshal.AllocHGlobal(length);
                int returnLength = 0;
                int status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, length, ref returnLength);
                if (status == 0)
                {
                    break;
                }

                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;

                if (status != STATUS_INFO_LENGTH_MISMATCH && status != STATUS_BUFFER_TOO_SMALL)
                {
                    throw new InvalidOperationException("NtQuerySystemInformation failed: 0x" + status.ToString("X8"));
                }

                length = returnLength > length ? returnLength + 0x10000 : length * 2;
            }

            long handleCount = Marshal.ReadIntPtr(buffer).ToInt64();
            IntPtr entryPtr = IntPtr.Add(buffer, IntPtr.Size * 2);
            int entrySize = Marshal.SizeOf(typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX));
            List<HandleEntry> handles = new List<HandleEntry>();

            for (long i = 0; i < handleCount; i++)
            {
                IntPtr current = IntPtr.Add(entryPtr, checked((int)(i * entrySize)));
                SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX native =
                    (SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX)Marshal.PtrToStructure(current, typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX));

                ulong processId = native.UniqueProcessId.ToUInt64();
                if (processId != (ulong)pid)
                {
                    continue;
                }

                ulong handleValue = native.HandleValue.ToUInt64();
                handles.Add(new HandleEntry
                {
                    ProcessId = pid,
                    HandleValue = handleValue,
                    HandleHex = "0x" + handleValue.ToString("X"),
                    ObjectTypeIndex = native.ObjectTypeIndex,
                    TypeName = "",
                    GrantedAccess = native.GrantedAccess,
                    GrantedAccessHex = "0x" + native.GrantedAccess.ToString("X8"),
                    HandleAttributes = native.HandleAttributes
                });
            }

            return handles;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static string QueryTypeName(IntPtr handle)
    {
        int length = 0x1000;
        IntPtr buffer = IntPtr.Zero;

        try
        {
            buffer = Marshal.AllocHGlobal(length);
            int returnLength = 0;
            int status = NtQueryObject(handle, ObjectTypeInformation, buffer, length, ref returnLength);

            if ((status == STATUS_INFO_LENGTH_MISMATCH || status == STATUS_BUFFER_TOO_SMALL) && returnLength > length)
            {
                Marshal.FreeHGlobal(buffer);
                length = returnLength;
                buffer = Marshal.AllocHGlobal(length);
                returnLength = 0;
                status = NtQueryObject(handle, ObjectTypeInformation, buffer, length, ref returnLength);
            }

            if (status != 0)
            {
                return "";
            }

            UNICODE_STRING typeName =
                (UNICODE_STRING)Marshal.PtrToStructure(buffer, typeof(UNICODE_STRING));

            if (typeName.Length == 0 || typeName.Buffer == IntPtr.Zero)
            {
                return "";
            }

            return Marshal.PtrToStringUni(typeName.Buffer, typeName.Length / 2);
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
"@

if (-not ("NativeHandleSnapshot" -as [type])) {
    Add-Type -TypeDefinition $nativeSource -Language CSharp
}

if ($TargetPid -ne 0) {
    $target = Get-Process -Id $TargetPid -ErrorAction Stop
} else {
    $target = Get-Process -Name $ProcessName -ErrorAction Stop | Select-Object -First 1
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$safeName = $target.ProcessName -replace '[^\w.-]', '_'
$summaryPath = Join-Path $OutputDirectory "$safeName-handles-summary-$stamp.csv"
$rawPath = Join-Path $OutputDirectory "$safeName-handles-raw-$stamp.csv"

Write-Host "Snapshotting handles for $($target.ProcessName).exe PID $($target.Id)..."
$result = [NativeHandleSnapshot]::Snapshot([int]$target.Id)
$handles = @($result.Handles)

$summary = $handles |
    Group-Object ObjectTypeIndex, TypeName |
    ForEach-Object {
        $first = $_.Group[0]
        [pscustomobject]@{
            ObjectTypeIndex = $first.ObjectTypeIndex
            TypeName = $first.TypeName
            Count = $_.Count
        }
    } |
    Sort-Object Count -Descending

$summary | Export-Csv -NoTypeInformation -LiteralPath $summaryPath
$handles | Export-Csv -NoTypeInformation -LiteralPath $rawPath

Write-Host "Total handles: $($handles.Count)"
Write-Host "Duplicate failures while resolving type names: $($result.DuplicateFailures)"
Write-Host "Summary: $summaryPath"
Write-Host "Raw: $rawPath"
Write-Host ""
$summary | Select-Object -First 30 | Format-Table -AutoSize
