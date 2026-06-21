param(
    [string]$ApkPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'quest-apk\com.vertigogames.atf-base.apk'),
    [string]$OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'quest-apk\patched'),
    [string]$KeystorePath,
    [switch]$Force,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'

$libEntryName = 'lib/arm64-v8a/libil2cpp.so'
$metadataEntryName = 'assets/bin/Data/Managed/Metadata/global-metadata.dat'
$retBytes = [byte[]](0xC0, 0x03, 0x5F, 0xD6)

$targets = @(
    @{ Category = 'Blood'; Class = 'BloodPoolPainterModule'; Method = 'PaintBloodGroundBelowPosition'; Offset = 0x41FF6C8; Expected = [byte[]](0xFF, 0xC3, 0x02, 0xD1) },
    @{ Category = 'Blood'; Class = 'BloodPoolPainterModule'; Method = 'PaintBloodPoolOnHips'; Offset = 0x41FF420; Expected = [byte[]](0xFF, 0x83, 0x01, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintBloodDecal'; Offset = 0x48D1F90; Expected = [byte[]](0xFF, 0x03, 0x05, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintBloodDecal'; Offset = 0x48D2FFC; Expected = [byte[]](0xFF, 0x03, 0x06, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintBloodDecalNow'; Offset = 0x48D1568; Expected = [byte[]](0xFC, 0x6B, 0xBB, 0xA9) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintBloodPool'; Offset = 0x48D2C90; Expected = [byte[]](0xFF, 0xC3, 0x05, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintBulletBlood'; Offset = 0x48D19E4; Expected = [byte[]](0xFF, 0x83, 0x02, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintGibFloorBlood'; Offset = 0x48D263C; Expected = [byte[]](0xFF, 0xC3, 0x01, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintGibSplatterBlood'; Offset = 0x48D27A8; Expected = [byte[]](0xFF, 0xC3, 0x01, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintImpactBlood'; Offset = 0x48D2928; Expected = [byte[]](0xFF, 0xC3, 0x02, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintIndirectSplatterBlood'; Offset = 0x48D2104; Expected = [byte[]](0xFF, 0xC3, 0x01, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintQueuedBloodDecal'; Offset = 0x48D17DC; Expected = [byte[]](0xFF, 0x03, 0x02, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.BloodPainter'; Method = 'PaintSplatterBlood'; Offset = 0x48D2264; Expected = [byte[]](0xFF, 0xC3, 0x02, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.ClientEnemyNetworking'; Method = 'HandleEnemyGibNetworkMessage'; Offset = 0x421F828; Expected = [byte[]](0xFF, 0x83, 0x02, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.PaintBloodOnCollisionBehaviour'; Method = 'TryPaintBlood'; Offset = 0x42135F8; Expected = [byte[]](0xFF, 0x03, 0x03, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.Zombies.ZombieBloodMaskPainter'; Method = 'PaintBlood'; Offset = 0x42FEAFC; Expected = [byte[]](0xEE, 0x0F, 0x17, 0xFC) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.Zombies.ZombieMutilationView'; Method = 'PaintMutilationBlood'; Offset = 0x42DE26C; Expected = [byte[]](0xFF, 0x43, 0x04, 0xD1) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.Zombies.ZombieSkinHitImpactModule'; Method = 'ApplyMutilationEffect'; Offset = 0x42F2570; Expected = [byte[]](0xEF, 0x3B, 0xB6, 0x6D) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.Zombies.ZombieSkinHitImpactModule'; Method = 'HandleZombieHitEvent'; Offset = 0x42F0618; Expected = [byte[]](0xFC, 0x5B, 0xBD, 0xA9) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.Zombies.ZombieSkinHitImpactModule'; Method = 'OnHitImpact'; Offset = 0x42F1B04; Expected = [byte[]](0xEF, 0x3B, 0xB6, 0x6D) },
    @{ Category = 'Blood'; Class = 'Vertigo.Snowbreed.Zombies.ZombieSkinHitImpactModule'; Method = 'OnImpact'; Offset = 0x42F06C0; Expected = [byte[]](0xFC, 0x0F, 0x1D, 0xF8) },
    @{ Category = 'VOIP'; Class = 'Vertigo.Voip.Fmod.FmodVoipRecorder'; Method = 'Update'; Offset = 0x5C109A4; Expected = [byte[]](0xFF, 0x83, 0x02, 0xD1) },
    @{ Category = 'VOIP'; Class = 'Vertigo.Voip.Fmod.FmodVoipRecorder'; Method = 'UpdateRecordDriver'; Offset = 0x5C100D4; Expected = [byte[]](0xF5, 0x53, 0xBE, 0xA9) },
    @{ Category = 'VOIP'; Class = 'Vertigo.Voip.Fmod.FmodVoipRecorder'; Method = 'UpdateRecordDriver'; Offset = 0x5C10138; Expected = [byte[]](0xFF, 0x43, 0x01, 0xD1) },
    @{ Category = 'VOIP'; Class = 'Vertigo.Voip.Fmod.FmodVoipRecorder'; Method = 'VoipThread'; Offset = 0x5C10840; Expected = [byte[]](0xF5, 0x53, 0xBE, 0xA9) },
    @{ Category = 'VOIP'; Class = 'Vertigo.Voip.VoipClient'; Method = 'HandleVoipInitPacket'; Offset = 0x5C09724; Expected = [byte[]](0xFF, 0x03, 0x03, 0xD1) },
    @{ Category = 'VOIP'; Class = 'Vertigo.Voip.VoipClient'; Method = 'HandleVoipInitResponsePacket'; Offset = 0x5C0A6D4; Expected = [byte[]](0xFF, 0x83, 0x02, 0xD1) },
    @{ Category = 'VOIP'; Class = 'Vertigo.Voip.VoipClient'; Method = 'JoinChannel'; Offset = 0x5C0757C; Expected = [byte[]](0xF9, 0x63, 0xBC, 0xA9) },
    @{ Category = 'VOIP'; Class = 'Vertigo.Voip.VoipClient'; Method = 'SetVolumeOther'; Offset = 0x5C07110; Expected = [byte[]](0xFF, 0x03, 0x01, 0xD1) },
    @{ Category = 'VOIP'; Class = 'Vertigo.Voip.VoipRemotePeer'; Method = 'HandleJoinedChannelPacket'; Offset = 0x5C0C46C; Expected = [byte[]](0xFB, 0x6B, 0xBB, 0xA9) },
    @{ Category = 'VOIP'; Class = 'Vertigo.Voip.VoipRemotePeer'; Method = 'SetVolume'; Offset = 0x5C06F7C; Expected = [byte[]](0xFF, 0x43, 0x01, 0xD1) }
)

function Format-HexBytes {
    param([byte[]]$Bytes)
    return (($Bytes | ForEach-Object { $_.ToString('X2') }) -join ' ')
}

function Test-BytesEqual {
    param([byte[]]$Left, [byte[]]$Right)
    if ($Left.Length -ne $Right.Length) {
        return $false
    }

    for ($i = 0; $i -lt $Left.Length; $i++) {
        if ($Left[$i] -ne $Right[$i]) {
            return $false
        }
    }

    return $true
}

function Find-AndroidBuildTool {
    param([string]$FileName)

    $sdkRoots = New-Object System.Collections.Generic.List[string]
    foreach ($root in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk'))) {
        if ($root -and (Test-Path -LiteralPath $root) -and -not $sdkRoots.Contains($root)) {
            $sdkRoots.Add($root)
        }
    }

    foreach ($sdkRoot in $sdkRoots) {
        $buildToolsRoot = Join-Path $sdkRoot 'build-tools'
        if (-not (Test-Path -LiteralPath $buildToolsRoot)) {
            continue
        }

        $dirs = Get-ChildItem -LiteralPath $buildToolsRoot -Directory | Sort-Object Name -Descending
        foreach ($dir in $dirs) {
            $candidate = Join-Path $dir.FullName $FileName
            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }

    throw "Could not find Android build-tools $FileName. Install Android SDK build-tools first."
}

function Find-ApkSignerJar {
    $sdkRoots = New-Object System.Collections.Generic.List[string]
    foreach ($root in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, (Join-Path $env:LOCALAPPDATA 'Android\Sdk'))) {
        if ($root -and (Test-Path -LiteralPath $root) -and -not $sdkRoots.Contains($root)) {
            $sdkRoots.Add($root)
        }
    }

    foreach ($sdkRoot in $sdkRoots) {
        $buildToolsRoot = Join-Path $sdkRoot 'build-tools'
        if (-not (Test-Path -LiteralPath $buildToolsRoot)) {
            continue
        }

        $dirs = Get-ChildItem -LiteralPath $buildToolsRoot -Directory | Sort-Object Name -Descending
        foreach ($dir in $dirs) {
            foreach ($candidate in @((Join-Path $dir.FullName 'lib\apksigner.jar'), (Join-Path $dir.FullName 'apksigner.jar'))) {
                if (Test-Path -LiteralPath $candidate) {
                    return $candidate
                }
            }
        }
    }

    throw "Could not find apksigner.jar. Install Android SDK build-tools first."
}

function Find-Java {
    $cmd = Get-Command java.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    foreach ($root in @($env:JAVA_HOME, 'C:\Program Files\Java', 'C:\Program Files\Eclipse Adoptium', 'C:\Program Files (x86)\Amazon Corretto')) {
        if (-not $root -or -not (Test-Path -LiteralPath $root)) {
            continue
        }

        if (Test-Path -LiteralPath (Join-Path $root 'bin\java.exe')) {
            return (Join-Path $root 'bin\java.exe')
        }

        $candidate = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin\java.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) {
            return $candidate
        }
    }

    throw "Could not find java.exe. Install a JDK first."
}

function Find-Jar {
    $cmd = Get-Command jar.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    foreach ($root in @($env:JAVA_HOME, 'C:\Program Files\Java', 'C:\Program Files\Eclipse Adoptium', 'C:\Program Files (x86)\Amazon Corretto')) {
        if (-not $root -or -not (Test-Path -LiteralPath $root)) {
            continue
        }

        if (Test-Path -LiteralPath (Join-Path $root 'bin\jar.exe')) {
            return (Join-Path $root 'bin\jar.exe')
        }

        $candidate = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin\jar.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) {
            return $candidate
        }
    }

    throw "Could not find jar.exe. Install a JDK first."
}

function Find-Keytool {
    $cmd = Get-Command keytool.exe -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    foreach ($root in @($env:JAVA_HOME, 'C:\Program Files\Java', 'C:\Program Files\Eclipse Adoptium', 'C:\Program Files (x86)\Amazon Corretto')) {
        if (-not $root -or -not (Test-Path -LiteralPath $root)) {
            continue
        }

        if (Test-Path -LiteralPath (Join-Path $root 'bin\keytool.exe')) {
            return (Join-Path $root 'bin\keytool.exe')
        }

        $candidate = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'bin\keytool.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) {
            return $candidate
        }
    }

    throw "Could not find keytool.exe. Install a JDK first."
}

function Test-ApkSignatureEntry {
    param([string]$EntryName)

    if (-not $EntryName.StartsWith('META-INF/', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $fileName = [System.IO.Path]::GetFileName($EntryName)
    return $fileName -eq 'MANIFEST.MF' -or $fileName.EndsWith('.SF') -or $fileName.EndsWith('.RSA') -or $fileName.EndsWith('.DSA') -or $fileName.EndsWith('.EC')
}

function Test-NativeLibraryEntry {
    param([string]$EntryName)

    return $EntryName.StartsWith('lib/', [System.StringComparison]::OrdinalIgnoreCase) -and $EntryName.EndsWith('.so', [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-StoredApkEntry {
    param([string]$EntryName)

    return (Test-NativeLibraryEntry $EntryName) -or $EntryName.Equals('resources.arsc', [System.StringComparison]::OrdinalIgnoreCase)
}

function New-QuestKeystore {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        return
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $keytool = Find-Keytool
    & $keytool -genkeypair -v -keystore $Path -storepass android -alias afterthefallvrmodkit -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname 'CN=After The Fall VR Mod Kit'
    if ($LASTEXITCODE -ne 0) {
        throw "keytool failed with exit code $LASTEXITCODE"
    }
}

function Copy-ZipEntryToFile {
    param(
        [System.IO.Compression.ZipArchiveEntry]$Entry,
        [string]$Path
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $inputStream = $Entry.Open()
    try {
        $outputStream = [System.IO.File]::Create($Path)
        try {
            $inputStream.CopyTo($outputStream)
        } finally {
            $outputStream.Dispose()
        }
    } finally {
        $inputStream.Dispose()
    }
}

function Copy-Stream {
    param(
        [System.IO.Stream]$InputStream,
        [System.IO.Stream]$OutputStream
    )

    $buffer = New-Object byte[] 1048576
    while (($read = $InputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
        $OutputStream.Write($buffer, 0, $read)
    }
}

function Expand-ApkForRepack {
    param(
        [string]$SourceApk,
        [string]$Destination
    )

    $destinationFullPath = [System.IO.Path]::GetFullPath($Destination).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $archive = [System.IO.Compression.ZipFile]::OpenRead($SourceApk)
    try {
        foreach ($entry in $archive.Entries) {
            if (Test-ApkSignatureEntry $entry.FullName) {
                continue
            }

            $relativeEntryPath = $entry.FullName.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            $target = [System.IO.Path]::GetFullPath((Join-Path $Destination $relativeEntryPath))
            if (-not $target.StartsWith($destinationFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "APK contains an invalid path: $($entry.FullName)"
            }

            if ([string]::IsNullOrEmpty($entry.Name)) {
                New-Item -ItemType Directory -Force -Path $target | Out-Null
                continue
            }

            Copy-ZipEntryToFile -Entry $entry -Path $target
        }
    } finally {
        $archive.Dispose()
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$resolvedApk = (Resolve-Path -LiteralPath $ApkPath).ProviderPath
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (-not $KeystorePath) {
    $KeystorePath = Join-Path $OutputDir 'afterthefall-quest-debug.keystore'
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$workDir = Join-Path $OutputDir "_work-$timestamp"
$patchedLib = Join-Path $workDir 'libil2cpp.so'
$repackDir = Join-Path $workDir 'apk-repack'
$unsignedApk = Join-Path $OutputDir "com.vertigogames.atf-modded-$timestamp-unsigned.apk"
$alignedApk = Join-Path $OutputDir "com.vertigogames.atf-modded-$timestamp-aligned.apk"
$signedApk = Join-Path $OutputDir "com.vertigogames.atf-modded-$timestamp-signed.apk"
$reportPath = Join-Path $OutputDir "patch-report-$timestamp.csv"

New-Item -ItemType Directory -Force -Path $workDir | Out-Null

$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedApk)
try {
    $libEntry = $archive.GetEntry($libEntryName)
    $metadataEntry = $archive.GetEntry($metadataEntryName)
    if (-not $libEntry) {
        throw "APK is missing $libEntryName"
    }
    if (-not $metadataEntry) {
        throw "APK is missing $metadataEntryName"
    }

    Copy-ZipEntryToFile -Entry $libEntry -Path $patchedLib
} finally {
    $archive.Dispose()
}

$patchReport = New-Object System.Collections.Generic.List[object]
$fileStream = [System.IO.File]::Open($patchedLib, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite)
try {
    foreach ($target in $targets) {
        if ($target.Offset + $retBytes.Length -gt $fileStream.Length) {
            throw "Patch offset 0x$('{0:X}' -f $target.Offset) is outside libil2cpp.so"
        }

        $fileStream.Position = $target.Offset
        $original = New-Object byte[] 4
        [void]$fileStream.Read($original, 0, $original.Length)

        $alreadyPatched = Test-BytesEqual -Left $original -Right $retBytes
        $matchesExpected = Test-BytesEqual -Left $original -Right $target.Expected
        if (-not $alreadyPatched -and -not $matchesExpected -and -not $Force) {
            throw "Unexpected bytes at $($target.Class)::$($target.Method) offset 0x$('{0:X}' -f $target.Offset). Expected $(Format-HexBytes $target.Expected), found $(Format-HexBytes $original). This APK build is probably not the verified Quest build."
        }

        if (-not $alreadyPatched) {
            $fileStream.Position = $target.Offset
            $fileStream.Write($retBytes, 0, $retBytes.Length)
        }

        $patchReport.Add([pscustomobject]@{
            Category = $target.Category
            Class = $target.Class
            Method = $target.Method
            Offset = ('0x{0:X}' -f $target.Offset)
            OriginalBytes = Format-HexBytes $original
            PatchedBytes = Format-HexBytes $retBytes
            Status = if ($alreadyPatched) { 'already patched' } else { 'patched' }
        })
    }
} finally {
    $fileStream.Dispose()
}

$patchReport | Export-Csv -NoTypeInformation -Path $reportPath

Expand-ApkForRepack -SourceApk $resolvedApk -Destination $repackDir
$patchedLibTarget = Join-Path $repackDir ($libEntryName.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $patchedLibTarget) | Out-Null
Copy-Item -LiteralPath $patchedLib -Destination $patchedLibTarget -Force

$jar = Find-Jar
& $jar cf0M $unsignedApk -C $repackDir .
if ($LASTEXITCODE -ne 0) {
    throw "jar repack failed with exit code $LASTEXITCODE"
}

$zipalign = Find-AndroidBuildTool 'zipalign.exe'
$apksignerJar = Find-ApkSignerJar
$java = Find-Java
New-QuestKeystore -Path $KeystorePath

& $zipalign -p -f 4 $unsignedApk $alignedApk
if ($LASTEXITCODE -ne 0) {
    throw "zipalign failed with exit code $LASTEXITCODE"
}

& $java -jar $apksignerJar sign --ks $KeystorePath --ks-key-alias afterthefallvrmodkit --ks-pass pass:android --key-pass pass:android --out $signedApk $alignedApk
if ($LASTEXITCODE -ne 0) {
    throw "apksigner.jar sign failed with exit code $LASTEXITCODE"
}

& $java -jar $apksignerJar verify --verbose $signedApk
if ($LASTEXITCODE -ne 0) {
    throw "apksigner.jar verify failed with exit code $LASTEXITCODE"
}

Write-Host "Patched APK: $signedApk"
Write-Host "Patch report: $reportPath"
Write-Host "Patched targets: $($patchReport.Count)"

if ($Install) {
    $adb = Get-Command adb.exe -ErrorAction Stop
    & $adb.Source install -r $signedApk
    if ($LASTEXITCODE -ne 0) {
        throw "adb install failed with exit code $LASTEXITCODE. If this reports a signature mismatch, Android is refusing to update the store-signed app with this debug-signed APK."
    }
}
