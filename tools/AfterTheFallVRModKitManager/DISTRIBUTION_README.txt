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
  Disables client-side blood, decal, gib, mutilation, and known blood particle effects.

- Doorbell wave sound
  Replaces the harsh horde wave-start screech with a short ding dong doorbell tone.

- Nephew Mode
  PC-only cosmetic mode that makes enemies look less scary with a bland plastic skin while keeping game hitboxes and behavior intact.

- Clean retained ServerGame on hub return
  Experimental cleanup for the ServerGame memory leak reported by the game after horde.

- vrperfkit injection
  Enables or disables dxgi.dll based vrperfkit injection when bundled or already installed.

- Set network disconnect timeout
  Adds or removes Steam launch option -disconnectTimeout. The default 30 second setting writes -disconnectTimeout 30000.

- ADB Status / Create Quest OBB / Install Quest OBB
  Detects a connected Quest, builds an OBB-only blood/decal/gore tuning patch plus conservative Quest Nephew Mode skin tinting, and installs it through ADB. Installation reuses the newest cached patched OBB for the current patch profile and skips the push only when the headset OBB SHA-256 already matches. It backs up the current headset OBB before replacing it. Long OBB pulls and pushes show percent, speed, and ETA. The APK is not modified.

Feature toggles are written to:

BepInEx\config\local.afterthefall.vrmodkit.cfg

They take effect the next time the game starts.
