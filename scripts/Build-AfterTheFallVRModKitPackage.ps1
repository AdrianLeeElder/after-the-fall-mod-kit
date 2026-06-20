param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\After The Fall',
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist')
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$managerRoot = Join-Path $repoRoot 'tools\AfterTheFallVRModKitManager'
$packageRoot = Join-Path $OutputRoot 'AfterTheFallVRModKit'
$payloadRoot = Join-Path $packageRoot 'payload'
$bepInExPayload = Join-Path $payloadRoot 'bepinex'
$vrPerfKitPayload = Join-Path $payloadRoot 'vrperfkit'

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'src\AfterTheFallVRModKit.Plugin\build.ps1') -GameRoot $GameRoot
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $managerRoot 'build.ps1')

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $payloadRoot | Out-Null

Copy-Item -LiteralPath (Join-Path $managerRoot 'bin\Release\AfterTheFallVRModKitManager.exe') -Destination (Join-Path $packageRoot 'AfterTheFallVRModKitManager.exe') -Force
Copy-Item -LiteralPath (Join-Path $managerRoot 'DISTRIBUTION_README.txt') -Destination (Join-Path $packageRoot 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'src\AfterTheFallVRModKit.Plugin\bin\AfterTheFallVRModKit.dll') -Destination (Join-Path $payloadRoot 'AfterTheFallVRModKit.dll') -Force

$bepInExSource = Join-Path $repoRoot 'downloads\bepinex_extract'
if (Test-Path -LiteralPath $bepInExSource) {
    Copy-Item -LiteralPath $bepInExSource -Destination $bepInExPayload -Recurse -Force
} else {
    Write-Warning "BepInEx payload not found: $bepInExSource"
}

$vrPerfKitDll = Join-Path $GameRoot 'dxgi.dll'
$vrPerfKitConfig = Join-Path $GameRoot 'vrperfkit.yml'
if ((Test-Path -LiteralPath $vrPerfKitDll) -or (Test-Path -LiteralPath $vrPerfKitConfig)) {
    New-Item -ItemType Directory -Force -Path $vrPerfKitPayload | Out-Null
    if (Test-Path -LiteralPath $vrPerfKitDll) {
        Copy-Item -LiteralPath $vrPerfKitDll -Destination (Join-Path $vrPerfKitPayload 'dxgi.dll') -Force
    }
    if (Test-Path -LiteralPath $vrPerfKitConfig) {
        Copy-Item -LiteralPath $vrPerfKitConfig -Destination (Join-Path $vrPerfKitPayload 'vrperfkit.yml') -Force
    }
} else {
    Write-Warning "vrperfkit payload not found in $GameRoot"
}

$zipPath = Join-Path $OutputRoot 'AfterTheFallVRModKit.zip'
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

$tar = Get-Command tar.exe -ErrorAction SilentlyContinue
if ($tar) {
    & $tar.Source -a -cf $zipPath -C $OutputRoot 'AfterTheFallVRModKit'
    if ($LASTEXITCODE -ne 0) {
        throw "tar.exe failed with exit code $LASTEXITCODE"
    }
} else {
    Compress-Archive -Path $packageRoot -DestinationPath $zipPath -Force
}

Write-Host "Package folder: $packageRoot"
Write-Host "Package zip:    $zipPath"
