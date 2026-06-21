After The Fall VR Mod Kit
==========================

Maintained by DonkeySlayer.

1. Run AfterTheFallVRModKitInstaller.exe.
2. If Windows blocks writes to the Steam folder, click "Restart as Admin".
3. Confirm the game folder points at the folder containing AfterTheFall.exe.
4. Choose the toggles you want and click Apply.

Close After The Fall before applying changes.

Toggles:

- BepInEx mod loader
  Installs or enables the BepInEx IL2CPP loader.

- After The Fall VR Mod Kit
  Installs or enables the bundled AfterTheFallVRModKit.dll plugin.

- Disable in-game VOIP
  Disables the game's own VOIP handlers. Use Discord or another voice chat.

- Suppress client blood/gore visuals
  Disables client-side blood, decal, gib, and mutilation visual handlers.

- Clean retained ServerGame on hub return
  Experimental cleanup for the ServerGame memory leak reported by the game after horde.

- vrperfkit injection
  Enables or disables dxgi.dll based vrperfkit injection when bundled or already installed.

Feature toggles are written to:

BepInEx\config\local.afterthefall.vrmodkit.cfg

They take effect the next time the game starts.
