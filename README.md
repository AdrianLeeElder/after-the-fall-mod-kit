# After The Fall VR Mod Kit

Windows mod kit for experimenting with After The Fall VR performance fixes.

Maintained by DonkeySlayer.

This repository contains:

- A BepInEx IL2CPP plugin with configurable performance-related patches.
- A Windows Forms manager that installs and toggles the plugin, BepInEx, feature flags, and optional vrperfkit injection.
- PowerShell scripts for building packages and collecting performance data.

## Current Features

- Disable the game's built-in VOIP handlers for players using external voice chat.
- Suppress client-side blood, decal, gib, mutilation, and known blood particle effects.
- Replace the harsh horde wave-start stinger with a short doorbell-like tone.
- Enable PC-only Nephew Mode enemy visuals that keep the real animated enemy renderer and apply a bland plastic skin.
- Run an experimental retained `ServerGame` cleanup after returning to the hub.
- Toggle BepInEx and vrperfkit by enabling or disabling their loader DLLs.
- Set the Steam `-disconnectTimeout` launch option from the manager UI.
- Detect a connected Quest through ADB and build an OBB-only Quest patch for blood/decal/gore tuning and conservative Nephew Mode skin tinting.
- Install the patched Quest OBB through ADB without modifying or re-signing the official APK.

## End User Install

1. Download `AfterTheFallVRModKitInstaller.exe` from the latest GitHub Release.
2. Run the exe.
3. If Windows blocks writes to the Steam folder, click `Restart as Admin`.
4. Pick the desired toggles and click `Apply`.

Close After The Fall before applying file changes. Feature settings take effect the next time the game starts.

The network disconnect timeout control writes Steam launch options for app `751630`. The default experimental value is `30` seconds, which becomes `-disconnectTimeout 30000`.

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
DoorbellWaveSound = true
ComfortEnemyVisuals = false
CleanupRetainedServerGame = true
```

`ComfortEnemyVisuals` is the compatibility config key for Nephew Mode.

## Quest OBB Support

The manager includes `ADB Status`, `Create Quest OBB`, and `Install Quest OBB` buttons. The Quest path currently supports the verified Quest build `versionCode=38148` / `versionName=1.38147.41947`:

- Detects an authorized ADB device.
- Finds the Quest package `com.vertigogames.atf`.
- Pulls `main.38148.com.vertigogames.atf.obb`.
- Patches known blood, decal, gib, zombie death blood-pool, impact, and mutilation tuning tables.
- Empties known blood/decal/gib texture arrays to reduce ground blood even when gore settings are restored from saved data.
- Applies a conservative Quest Nephew Mode tint to serialized zombie skin collections while preserving official enemy rigs, animation, hitboxes, crawling, limb behavior, and online auth identity.
- Keeps the official APK untouched so Meta/Vertigo auth identity is preserved.
- Backs up the headset OBB to `/sdcard/Download/AfterTheFallVRModKit/obb-backup` before pushing the patched OBB.

PC Nephew Mode uses runtime BepInEx/Harmony hooks. The current PC path keeps the real animated zombie renderer active so size, hitboxes, crawling, limb loss, and ragdoll behavior stay game-driven, then overrides the renderer's bland plastic skin properties. The current Quest online path is OBB-only: it can tune serialized data and skin tinting, but does not run the PC Harmony hooks because re-signing or native APK changes broke online auth during testing.

The earlier APK patch route is not recommended for online Quest play because re-signing the APK caused game auth error `10050`. The standalone OBB patch script is also available:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Patch-AfterTheFallQuestObb.ps1
```

## Notes

This is an unofficial experimental mod kit. Use it at your own risk, keep backups, and test with friends who know which toggles are enabled.
