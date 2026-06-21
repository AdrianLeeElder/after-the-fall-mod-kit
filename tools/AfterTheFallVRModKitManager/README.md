# After The Fall VR Mod Kit Manager

Small Windows Forms manager for the After The Fall VR Mod Kit.

It can:

- Detect the Steam install folder for After The Fall.
- Install or toggle BepInEx.
- Install or toggle `AfterTheFallVRModKit.dll`.
- Write feature flags for the plugin:
  - `DisableInGameVoip`
  - `SuppressClientBloodAndGore`
  - `DoorbellWaveSound`
  - `ComfortEnemyVisuals` for Nephew Mode
  - `CleanupRetainedServerGame`
- Set or remove the Steam `-disconnectTimeout` launch option for app `751630`.
- Toggle vrperfkit when `dxgi.dll` / `dxgi.dll.disabled` is present, or install it when a package payload is bundled.
- Check ADB status and create a bloodless plus conservative Nephew Mode tinted Quest OBB artifact for the verified `com.vertigogames.atf` Quest release.
- Install the patched Quest OBB with a remote backup while leaving the official APK untouched.
- Reuse the newest cached patched Quest OBB for the current patch profile and skip install only when the headset SHA-256 already matches it.
- Show Quest OBB pull/push transfer percent, speed, and ETA during long ADB operations.
- Keep PC-only Nephew Mode visuals separate from Quest OBB patching; Quest online auth requires the official APK signature.

Build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\AfterTheFallVRModKitManager\build.ps1
```

For release builds, pass `-EmbeddedPayloadZip` to embed the installer payload into the exe.
