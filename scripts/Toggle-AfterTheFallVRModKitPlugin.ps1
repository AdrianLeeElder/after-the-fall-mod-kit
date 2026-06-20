param(
    [ValidateSet('status', 'enable', 'disable')]
    [string]$Action = 'status',

    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\After The Fall'
)

$ErrorActionPreference = 'Stop'

$plugin = Join-Path $GameRoot 'BepInEx\plugins\AfterTheFallVRModKit.dll'
$disabled = "$plugin.disabled"

switch ($Action) {
    'status' {
        if (Test-Path -LiteralPath $plugin) {
            Write-Host "After The Fall VR Mod Kit plugin is ENABLED: $plugin"
        } elseif (Test-Path -LiteralPath $disabled) {
            Write-Host "After The Fall VR Mod Kit plugin is DISABLED: $disabled"
        } else {
            Write-Host "After The Fall VR Mod Kit plugin is not installed in $GameRoot"
        }
    }
    'enable' {
        if (Test-Path -LiteralPath $plugin) {
            Write-Host "After The Fall VR Mod Kit plugin is already enabled."
        } elseif (Test-Path -LiteralPath $disabled) {
            Rename-Item -LiteralPath $disabled -NewName 'AfterTheFallVRModKit.dll'
            Write-Host "Enabled After The Fall VR Mod Kit plugin."
        } else {
            throw "Cannot enable because neither $plugin nor $disabled exists."
        }
    }
    'disable' {
        if (Test-Path -LiteralPath $disabled) {
            Write-Host "After The Fall VR Mod Kit plugin is already disabled."
        } elseif (Test-Path -LiteralPath $plugin) {
            Rename-Item -LiteralPath $plugin -NewName 'AfterTheFallVRModKit.dll.disabled'
            Write-Host "Disabled After The Fall VR Mod Kit plugin."
        } else {
            throw "Cannot disable because $plugin does not exist."
        }
    }
}
