# After The Fall VR Mod Kit Plugin

BepInEx IL2CPP plugin for After The Fall.

Current feature flags:

- `DisableInGameVoip`
- `SuppressClientBloodAndGore`
- `DoorbellWaveSound`
- `ComfortEnemyVisuals` for Nephew Mode
- `CleanupRetainedServerGame`

The plugin config is written by the manager to:

```text
BepInEx\config\local.afterthefall.vrmodkit.cfg
```

The built plugin DLL is:

```text
AfterTheFallVRModKit.dll
```

Build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\AfterTheFallVRModKit.Plugin\build.ps1
```
