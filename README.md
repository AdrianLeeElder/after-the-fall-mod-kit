# After The Fall VR Mod Kit

Windows mod kit for experimenting with After The Fall VR performance fixes.

Maintained by DonkeySlayer.

This repository contains:

- A BepInEx IL2CPP plugin with configurable performance-related patches.
- A Windows Forms manager that installs and toggles the plugin, BepInEx, feature flags, and optional vrperfkit injection.
- PowerShell scripts for building packages and collecting performance data.

## Current Features

- Disable the game's built-in VOIP handlers for players using external voice chat.
- Suppress client-side blood, decal, gib, and mutilation visual handlers.
- Run an experimental retained `ServerGame` cleanup after returning to the hub.
- Toggle BepInEx and vrperfkit by enabling or disabling their loader DLLs.

## End User Install

1. Download a packaged release zip.
2. Extract it outside the game folder.
3. Run `AfterTheFallVRModKitManager.exe`.
4. If Windows blocks writes to the Steam folder, click `Restart as Admin`.
5. Pick the desired toggles and click `Apply`.

Close After The Fall before applying file changes. Feature settings take effect the next time the game starts.

## Build From Source

Build the plugin:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\AfterTheFallVRModKit.Plugin\build.ps1
```

Build the manager:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\AfterTheFallVRModKitManager\build.ps1
```

Build a distributable package:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-AfterTheFallVRModKitPackage.ps1
```

The package script expects a local BepInEx IL2CPP payload under `downloads\bepinex_extract`. It can also bundle vrperfkit files from an existing game install when `dxgi.dll` and `vrperfkit.yml` are present.

## Project Layout

- `src\AfterTheFallVRModKit.Plugin` - BepInEx IL2CPP plugin source.
- `tools\AfterTheFallVRModKitManager` - Windows Forms manager source.
- `scripts` - build, toggle, monitoring, and log analysis scripts.

## Plugin Config

The manager writes feature flags to:

```text
BepInEx\config\local.afterthefall.vrmodkit.cfg
```

Supported feature flags:

```ini
[Features]
DisableInGameVoip = true
SuppressClientBloodAndGore = true
CleanupRetainedServerGame = true
```

## Notes

This is an unofficial experimental mod kit. Use it at your own risk, keep backups, and test with friends who know which toggles are enabled.
