# After The Fall Knowledge Base

Rule: Every word must earn its place.

## Working Theory

Horde lag is resource accumulation, not graphics settings.

Main suspects:
- Built-in VOIP churn.
- Blood, decals, gibs, mutilation, and related pools.
- Retained `ServerGame` after returning to hub.

## PC Path

Game:
- Unity `2021.3.8f1`.
- IL2CPP.
- BepInEx IL2CPP works on PC.

Useful PC patches:
- Disable in-game VOIP.
- Suppress client blood/gore handlers.
- Cleanup retained `ServerGame` on hub return.
- Optional disconnect-timeout launch option.

PC Nephew Mode enemy visuals:
- Old rig-bound proxy path caused invisible enemies.
- Symptom: one proxy in sky, other enemies invisible.
- Immediate fix: set `ComfortEnemyVisuals=false`.
- Safer build removes global `SetVisible(false)`.
- Floating fallback proxy was unsafe.
- Fallback proxy floated/slid and desynced from hitboxes.
- V3 rejects unaligned proxies.
- V3 uses bone-bound proxy only after bounds validation.
- V3 fallback is real animated mesh plus Nephew Mode tint.
- Runtime test showed regular zombies.
- Logs showed exposed enemy tree has no named bones.
- `SetCustomSkinningBoneMatrices` carries animation matrices.
- V4 tries matrix-driven ragdoll parts.
- V4 still showed regular zombies.
- V5 adds grounded bounds-driven proxy.
- V5 prefers hit collider bounds over renderer bounds.
- V5 hides original mesh when bounds proxy works.
- V5 worked only for `BackgroundCommon`.
- Real enemies exposed unusable bounds.
- V6 estimates body bounds from enemy transform.
- V6 ignores its own proxy in bone search.
- V6 proxy looked better.
- Head visual was too high.
- Limb motion looked stiff.
- V7 lowers/largens head target.
- V7 splits arms/legs into bent segments.
- V7 adds stronger synthetic limb swing.
- V7 tints proxy orange/red on hide/death.
- V7 could disappear nearby.
- Cause likely `SetVisible(false)` is culling, not death.
- V8 no-ops `SetVisible(false)` for proxy.
- V8 uses smoother plastic color.
- Long-term needs real zombie state hooks.
- Exact goal: match crawlers, limb loss, death, and pose.
- `ClientZombieSkinEntity` owns rig, animation, mutilation, body IK, ragdoll, and rendering modules.
- `CrawlMovementModule.OnIsCrawlingChangedEvent` is the crawler signal.
- `MutilationHealth.PartMutilatedEvent` and `MutilatablePart.PartSeveredEvent` are limb signals.
- `ZombieSkinBodyIKModule` reacts to death, crawling, mutilation, and on-feet changes.
- `AnimationPhysicsBlender.EState` tracks animation, ragdoll, and blend states.
- V9 fixes `SetCustomSkinningBoneMatrices` hook type to `NativeArray<float4x4>`.
- V9 lets matrix-driven proxy override bounds fallback.
- V9 logs low-noise crawler, limb, death, body IK, and ragdoll events.
- V9 startup crash likely came from fragile `ZombieMutilationView` diagnostics.
- V10 keeps diagnostics off by default.
- V10 removes `ZombieMutilationView` diagnostic patching.
- Claude Code review concluded separate primitive proxies cannot reach exact animation parity.
- Recommended path: reskin real animated renderer first.
- V11 disables active primitive proxy, matrix, and visibility hooks.
- V11 keeps real zombie renderer enabled and applies plastic Nephew Mode tint.
- V11 should restore true size, animation, crawler, limb-loss, and ragdoll parity.
- Later mannequin mesh needs identical bone/bindpose rig.
- V11 tint looked too normal in-game.
- V12 keeps real renderer and overrides neutral albedo, blood, ONNR, and noise textures.
- V12 should include brutes/eaters because it targets `ZombieSkinRenderingModule`.
- V12 loaded and fired but looked unchanged.
- V13 adds per-material-slot property blocks and cloned source-shader materials.
- V13 still looked unchanged.
- Claude review found likely bug: zombie shader IDs are static fields.
- V13 only searched instance members, so custom shader IDs stayed missing.
- V14 reads static shader IDs and logs renderer shader/property diagnostics.
- V14 live DLL installed for next PC launch.
- V14 proved `textureShaderProperty=559` maps to `_BloodTexture`.
- `_BloodTexture` is 2D, not a texture array.
- V14 spammed Unity dimension errors assigning array to `_BloodTexture`.
- V15 sets `_AlbedoArray` and `_ONNRArray` by name with arrays.
- V15 sets `_BloodTexture`, `_BloodMask`, and `_BloodONNR` with 2D textures.
- V15 avoids static texture IDs except known 2D blood mask/ONNR fields.
- V16 makes on-body impact splatter synthetic green.
- V16 leaves zombie hit impact and blood-mask painting active.
- V16 still suppresses world/ground blood painters and gib visuals.
- V16 stops overriding `_BloodMask`; the game keeps the hit pattern.
- V16 replaces `_BloodTexture` with a green 2D texture.
- V17 suppresses zombie hit impact and blood-mask painting again.
- V17 keeps flatter pastel Nephew Mode materials.
- Recoloring spray particles was abandoned; removing splatter is safer.
- Legacy `ATFNoVoip.dll` also patched blood; disable it.
- V18 renamed comfort visuals to Nephew Mode in UI/docs.
- V19 adds object-level blood FX suppression for known blood prefabs/particles.
- V19 adds `DoorbellWaveSound` for horde wave-start audio.
- Doorbell hook targets `Vertigo.Audio.AudioUtils` string/event text paths.

## Quest Identity

Package:
- `com.vertigogames.atf`.
- Verified Quest build: `versionCode=38148`, `versionName=1.38147.41947`.
- Official signer: `O=Vertigo Games`.

APK patch result:
- Re-signed APK installs.
- Re-signed APK fails online auth.
- Error shown by game: `10050`.
- Unity log says `OculusFirebaseAuthenticator.ObtainOculusUserProof()` fails.
- Meta auth log says: `Application does not have permission for this action`.

Conclusion:
- Re-signing the Quest APK breaks online auth.
- Quest online patches must preserve the official APK signature.

## Quest Data Surface

Official APK uses:
- `extractNativeLibs=true`.
- Native libs under `/data/app/.../lib/arm64`.
- `libil2cpp.so` is readable, not writable by ADB shell.
- `run-as` is blocked because the package is not debuggable.

Conclusion:
- In-place native code patching is blocked on stock Quest.
- OBB/data patching is the viable non-root path.
- Re-signed APKs are a research artifact, not the online path.

## Quest OBB

OBB:
- `/sdcard/Android/obb/com.vertigogames.atf/main.38148.com.vertigogames.atf.obb`
- Size: `3,755,837,717` bytes.
- ZIP archive.
- Contains Unity `assets/bin/Data/...` files.

Blood settings entries:
- `assets/bin/Data/541ba57ea63899e478c25da546f15ed9`
- `assets/bin/Data/eccc90d64e804de4ba7eb24708909a7b`

Useful fields:
- `bulletMinDecalSize`
- `bulletMaxDecalSize`
- `indirectMinDecalSize`
- `indirectMaxDecalSize`
- `bloodPoolTextures`
- `straightDecalTextures`
- `angledDecalTextures`
- `indirectSplatterDecalTextures`
- `zombieBloodDecalTextures`
- `gibFloorPaintDelay`
- `gibFloorBloodTextures`
- `minMaxGibFloorBloodSize`
- `maxGibSplatterRaycastDistance`
- `gibSplatterRaycastMask`
- `gibSplatterBloodTextures`
- `minMaxGibSplatterBloodSize`

Zombie death blood-pool entries:
- `assets/bin/Data/34371bffbaebc5b43be10f0d2ca3d2f0`
- `assets/bin/Data/5f8e8990ebe7b194c9b3f71f884e565d`

Useful fields:
- `bloodPoolMinSize`
- `bloodPoolMaxSize`
- `bloodPoolMinSpawnDuration`
- `bloodPoolMaxSpawnDuration`

## Quest OBB Patch

Do not modify the APK.

Patch only OBB data:
- Set blood/decal/gib size fields to `0.0`.
- Set gib splatter raycast distance to `0.0`.
- Set gib splatter raycast mask to `0`.
- Set gib floor paint delay to `0.0`.
- Empty blood/decal/gib texture arrays.
- Set zombie death blood-pool size and spawn duration to `0.0`.
- Set impact, mutilation, critical-hit, and gibbing numeric fields to `0`.
- V3 also tints `ZombieSkinCollection` color multipliers.

Goal:
- Keep online auth intact.
- Reduce persistent blood/decal buildup.
- Test whether horde lag improves on Quest.

Status:
- V1 OBB patch proved online auth works.
- V1 still showed visible blood.
- V2 OBB patch adds impact/mutilation suppression.
- V2 also empties known blood texture arrays.
- V2 headset test: no-ground-blood patch worked.
- V2 headset test: zombie visuals did not change.
- V3 local OBB patch verified readable and length-preserving.
- V3 patched `ZombieSkinCollection` tint rows: 6 in typed table, 10 in plain table.
- V3 manager uses `bloodless-v3` cache profile.
- V3 install skips push only on matching remote SHA-256, not file size.
- Local V2 OBB verified readable.
- Official Vertigo APK restored on headset.
- V1 patched OBB installed on headset.
- Remote original OBB backup exists.
- Tutorial progressed to `HasDoneHubIntro`.
- Persistence save returned `200`.
- Headset gameplay test still needed.

Limits:
- Quest Nephew Mode is tint-only so far.
- PC zombie replacement uses runtime Harmony hooks.
- Quest OBB-only patch cannot create runtime proxy geometry.
- Player/NPC prefab redirects look rig-incompatible.
- Quest OBB has enemy skins, not a clean training-dummy skin.
- Quest doorbell wave sound is not implemented.
- Quest audio path likely needs FMOD event/data redirection.
- Current saved settings show `IsGoreEnabled=true`.
- Internal PlayerPrefs are not editable by stock ADB.
