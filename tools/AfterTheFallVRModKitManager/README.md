# After The Fall VR Mod Kit Manager

Small Windows Forms manager for the After The Fall VR Mod Kit.

It can:

- Detect the Steam install folder for After The Fall.
- Install or toggle BepInEx.
- Install or toggle `AfterTheFallVRModKit.dll`.
- Write feature flags for the plugin:
  - `DisableInGameVoip`
  - `SuppressClientBloodAndGore`
  - `CleanupRetainedServerGame`
- Toggle vrperfkit when `dxgi.dll` / `dxgi.dll.disabled` is present, or install it when a package payload is bundled.

Build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\AfterTheFallVRModKitManager\build.ps1
```

For release builds, pass `-EmbeddedPayloadZip` to embed the installer payload into the exe.
