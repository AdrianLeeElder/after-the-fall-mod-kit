param(
    [int]$IntervalSeconds = 2,
    [string]$OutputDirectory = ".\captures",
    [string]$ProcessName = "AfterTheFall"
)

$ErrorActionPreference = "Stop"

if ($IntervalSeconds -lt 1) {
    throw "IntervalSeconds must be 1 or greater."
}

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$csvPath = Join-Path $OutputDirectory "afterthefall_perf_$stamp.csv"
$machine = Get-CimInstance Win32_ComputerSystem
$cpuCount = [Environment]::ProcessorCount
$nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue

function Get-ProcSnapshot {
    param([System.Diagnostics.Process]$Proc)

    $Proc.Refresh()
    [pscustomobject]@{
        Timestamp = Get-Date
        Id = $Proc.Id
        ProcessName = $Proc.ProcessName
        CPUSeconds = $Proc.TotalProcessorTime.TotalSeconds
        WorkingSetMB = [math]::Round($Proc.WorkingSet64 / 1MB, 1)
        PrivateMemoryMB = [math]::Round($Proc.PrivateMemorySize64 / 1MB, 1)
        PagedMemoryMB = [math]::Round($Proc.PagedMemorySize64 / 1MB, 1)
        Handles = $Proc.HandleCount
        Threads = $Proc.Threads.Count
    }
}

function Get-NvidiaSnapshot {
    if (-not $nvidiaSmi) {
        return [pscustomobject]@{
            GpuMemoryUsedMB = ""
            GpuUtilPercent = ""
            GpuTemperatureC = ""
        }
    }

    try {
        $rows = & $nvidiaSmi.Source --query-gpu=memory.used,utilization.gpu,temperature.gpu --format=csv,noheader,nounits 2>$null
        $mem = @()
        $util = @()
        $temp = @()
        foreach ($row in $rows) {
            $parts = $row -split "," | ForEach-Object { $_.Trim() }
            if ($parts.Count -ge 3) {
                $mem += $parts[0]
                $util += $parts[1]
                $temp += $parts[2]
            }
        }
        return [pscustomobject]@{
            GpuMemoryUsedMB = ($mem -join "|")
            GpuUtilPercent = ($util -join "|")
            GpuTemperatureC = ($temp -join "|")
        }
    }
    catch {
        return [pscustomobject]@{
            GpuMemoryUsedMB = ""
            GpuUtilPercent = ""
            GpuTemperatureC = ""
        }
    }
}

Write-Host "Waiting for $ProcessName.exe. Start After the Fall, then play the horde run normally."
Write-Host "Sampling every $IntervalSeconds second(s). Press Ctrl+C to stop. CSV: $csvPath"
Write-Host "System RAM: $([math]::Round($machine.TotalPhysicalMemory / 1GB, 1)) GB, logical CPUs: $cpuCount"

do {
    Start-Sleep -Seconds 1
    $proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
} until ($proc)

Write-Host "Attached to $ProcessName.exe PID $($proc.Id)."

$previous = Get-ProcSnapshot -Proc $proc
$start = $previous.Timestamp

"Timestamp,ElapsedSeconds,ProcessId,CPUPercent,WorkingSetMB,PrivateMemoryMB,PagedMemoryMB,Handles,Threads,GpuMemoryUsedMB,GpuUtilPercent,GpuTemperatureC" |
    Set-Content -LiteralPath $csvPath

while ($true) {
    Start-Sleep -Seconds $IntervalSeconds

    $proc = Get-Process -Id $previous.Id -ErrorAction SilentlyContinue
    if (-not $proc) {
        Write-Host "Process exited."
        break
    }

    $current = Get-ProcSnapshot -Proc $proc
    $gpu = Get-NvidiaSnapshot
    $elapsed = ($current.Timestamp - $start).TotalSeconds
    $dt = ($current.Timestamp - $previous.Timestamp).TotalSeconds
    $cpuPercent = 0

    if ($dt -gt 0) {
        $cpuPercent = (($current.CPUSeconds - $previous.CPUSeconds) / $dt / $cpuCount) * 100
    }

    $row = [pscustomobject]@{
        Timestamp = $current.Timestamp.ToString("o")
        ElapsedSeconds = [math]::Round($elapsed, 1)
        ProcessId = $current.Id
        CPUPercent = [math]::Round($cpuPercent, 1)
        WorkingSetMB = $current.WorkingSetMB
        PrivateMemoryMB = $current.PrivateMemoryMB
        PagedMemoryMB = $current.PagedMemoryMB
        Handles = $current.Handles
        Threads = $current.Threads
        GpuMemoryUsedMB = $gpu.GpuMemoryUsedMB
        GpuUtilPercent = $gpu.GpuUtilPercent
        GpuTemperatureC = $gpu.GpuTemperatureC
    }

    $row | ConvertTo-Csv -NoTypeInformation | Select-Object -Skip 1 | Add-Content -LiteralPath $csvPath
    Write-Host ("{0}s CPU {1}% RAM private {2} MB working {3} MB GPU mem {4} MB" -f $row.ElapsedSeconds, $row.CPUPercent, $row.PrivateMemoryMB, $row.WorkingSetMB, $row.GpuMemoryUsedMB)

    $previous = $current
}

Write-Host "Saved $csvPath"
