param(
    [ValidateSet("Status", "Disable", "Enable")]
    [string]$Action = "Status",
    [string]$GameDirectory = "C:\Program Files (x86)\Steam\steamapps\common\After The Fall"
)

$ErrorActionPreference = "Stop"

$activeDll = Join-Path $GameDirectory "dxgi.dll"
$disabledDll = Join-Path $GameDirectory "dxgi.dll.disabled"
$config = Join-Path $GameDirectory "vrperfkit.yml"

if (-not (Test-Path -LiteralPath $GameDirectory)) {
    throw "Game directory not found: $GameDirectory"
}

function Show-Status {
    $active = Test-Path -LiteralPath $activeDll
    $disabled = Test-Path -LiteralPath $disabledDll
    $hasConfig = Test-Path -LiteralPath $config

    [pscustomobject]@{
        GameDirectory = $GameDirectory
        VRPerfKitActive = $active
        DisabledBackupPresent = $disabled
        ConfigPresent = $hasConfig
        ActiveDll = $activeDll
        DisabledDll = $disabledDll
    } | Format-List
}

switch ($Action) {
    "Status" {
        Show-Status
    }
    "Disable" {
        if (Test-Path -LiteralPath $disabledDll) {
            throw "Cannot disable: $disabledDll already exists."
        }
        if (-not (Test-Path -LiteralPath $activeDll)) {
            throw "Cannot disable: active dxgi.dll was not found."
        }
        Rename-Item -LiteralPath $activeDll -NewName "dxgi.dll.disabled"
        Write-Host "Disabled vrperfkit injection."
        Show-Status
    }
    "Enable" {
        if (Test-Path -LiteralPath $activeDll) {
            throw "Cannot enable: active dxgi.dll already exists."
        }
        if (-not (Test-Path -LiteralPath $disabledDll)) {
            throw "Cannot enable: disabled backup was not found."
        }
        Rename-Item -LiteralPath $disabledDll -NewName "dxgi.dll"
        Write-Host "Enabled vrperfkit injection."
        Show-Status
    }
}
