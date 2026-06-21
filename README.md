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
- Detect a connected Quest through ADB and inspect the installed Quest APK for matching IL2CPP patch targets.

## End User Install

1. Download `AfterTheFallVRModKitInstaller.exe` from the latest GitHub Release.
2. Run the exe.
3. If Windows blocks writes to the Steam folder, click `Restart as Admin`.
4. Pick the desired toggles and click `Apply`.

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

The package script emits:

- `dist\AfterTheFallVRModKitInstaller.exe`
- `dist\AfterTheFallVRModKit.zip`

The installer exe embeds the plugin and BepInEx payload, so end users only need to run the exe. The package script expects a local BepInEx IL2CPP payload under `downloads\bepinex_extract`. It can also bundle vrperfkit files from an existing game install when `dxgi.dll` and `vrperfkit.yml` are present.

## Release Builds

Pushing a tag that starts with `v` runs the release workflow and attaches the single-file installer to a GitHub Release:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

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

## Quest APK Support

The manager includes `ADB Status` and `Patch Quest APK` buttons. The current Quest path is a safe preflight:

- Detects an authorized ADB device.
- Finds the Quest package `com.vertigogames.atf`.
- Pulls the base APK into the user's Documents folder.
- Confirms the APK is Unity IL2CPP and checks for the same target names used by the PC plugin.

It does not yet modify, sign, or reinstall the APK. The Quest build requires Android ARM64 `libil2cpp.so` patching, then APK resign/reinstall handling. That should be treated carefully because changing the APK signature can affect store updates, saved data, entitlement checks, and online play.

## Notes

This is an unofficial experimental mod kit. Use it at your own risk, keep backups, and test with friends who know which toggles are enabled.
