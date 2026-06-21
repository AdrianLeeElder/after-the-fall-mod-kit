param(
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\After The Fall',
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist')
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$managerRoot = Join-Path $repoRoot 'tools\AfterTheFallVRModKitManager'
$pluginRoot = Join-Path $repoRoot 'src\AfterTheFallVRModKit.Plugin'
$packageRoot = Join-Path $OutputRoot 'AfterTheFallVRModKit'
$payloadRoot = Join-Path $OutputRoot '_embedded-payload'
$bepInExPayload = Join-Path $payloadRoot 'bepinex'
$vrPerfKitPayload = Join-Path $payloadRoot 'vrperfkit'
$payloadZip = Join-Path $OutputRoot 'AfterTheFallVRModKitPayload.zip'
$installerExe = Join-Path $OutputRoot 'AfterTheFallVRModKitInstaller.exe'
$packageZip = Join-Path $OutputRoot 'AfterTheFallVRModKit.zip'

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pluginRoot 'build.ps1') -GameRoot $GameRoot

foreach ($path in @($packageRoot, $payloadRoot)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

foreach ($path in @($payloadZip, $installerExe, $packageZip)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

New-Item -ItemType Directory -Force -Path $payloadRoot | Out-Null

Copy-Item -LiteralPath (Join-Path $pluginRoot 'bin\AfterTheFallVRModKit.dll') -Destination (Join-Path $payloadRoot 'AfterTheFallVRModKit.dll') -Force

$bepInExSource = Join-Path $repoRoot 'downloads\bepinex_extract'
if (-not (Test-Path -LiteralPath $bepInExSource)) {
    if ((Test-Path -LiteralPath (Join-Path $GameRoot 'BepInEx\core')) -and (Test-Path -LiteralPath (Join-Path $GameRoot 'winhttp.dll'))) {
        $bepInExSource = $GameRoot
    }
}

if (Test-Path -LiteralPath $bepInExSource) {
    Copy-Item -LiteralPath $bepInExSource -Destination $bepInExPayload -Recurse -Force
} else {
    throw "BepInEx payload not found. Expected downloads\bepinex_extract or an installed BepInEx copy at $GameRoot"
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
    Write-Warning "vrperfkit payload not bundled because dxgi.dll and vrperfkit.yml were not found in $GameRoot"
}

$tar = Get-Command tar.exe -ErrorAction SilentlyContinue
if ($tar) {
    & $tar.Source -a -cf $payloadZip -C $payloadRoot .
    if ($LASTEXITCODE -ne 0) {
        throw "tar.exe failed with exit code $LASTEXITCODE while creating embedded payload"
    }
} else {
    Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $payloadZip -Force
}

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $managerRoot 'build.ps1') -EmbeddedPayloadZip $payloadZip

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $managerRoot 'bin\Release\AfterTheFallVRModKitManager.exe') -Destination $installerExe -Force
Copy-Item -LiteralPath $installerExe -Destination (Join-Path $packageRoot 'AfterTheFallVRModKitInstaller.exe') -Force
Copy-Item -LiteralPath (Join-Path $managerRoot 'DISTRIBUTION_README.txt') -Destination (Join-Path $packageRoot 'README.txt') -Force

if ($tar) {
    & $tar.Source -a -cf $packageZip -C $OutputRoot 'AfterTheFallVRModKit'
    if ($LASTEXITCODE -ne 0) {
        throw "tar.exe failed with exit code $LASTEXITCODE while creating package zip"
    }
} else {
    Compress-Archive -Path $packageRoot -DestinationPath $packageZip -Force
}

Write-Host "Installer exe:  $installerExe"
Write-Host "Package folder: $packageRoot"
Write-Host "Package zip:    $packageZip"
