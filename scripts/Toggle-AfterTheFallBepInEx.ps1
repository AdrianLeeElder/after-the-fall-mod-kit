param(
    [ValidateSet('status', 'enable', 'disable')]
    [string]$Action = 'status',

    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\After The Fall'
)

$ErrorActionPreference = 'Stop'

$loader = Join-Path $GameRoot 'winhttp.dll'
$disabled = "$loader.disabled"

switch ($Action) {
    'status' {
        if (Test-Path -LiteralPath $loader) {
            Write-Host "BepInEx loader is ENABLED: $loader"
        } elseif (Test-Path -LiteralPath $disabled) {
            Write-Host "BepInEx loader is DISABLED: $disabled"
        } else {
            Write-Host "BepInEx loader is not installed in $GameRoot"
        }
    }
    'enable' {
        if (Test-Path -LiteralPath $loader) {
            Write-Host "BepInEx loader is already enabled."
        } elseif (Test-Path -LiteralPath $disabled) {
            Rename-Item -LiteralPath $disabled -NewName 'winhttp.dll'
            Write-Host "Enabled BepInEx loader."
        } else {
            throw "Cannot enable because neither $loader nor $disabled exists."
        }
    }
    'disable' {
        if (Test-Path -LiteralPath $disabled) {
            Write-Host "BepInEx loader is already disabled."
        } elseif (Test-Path -LiteralPath $loader) {
            Rename-Item -LiteralPath $loader -NewName 'winhttp.dll.disabled'
            Write-Host "Disabled BepInEx loader."
        } else {
            throw "Cannot disable because $loader does not exist."
        }
    }
}
