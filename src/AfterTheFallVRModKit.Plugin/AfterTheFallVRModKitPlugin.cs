using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace AfterTheFallVRModKit.Plugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class AfterTheFallVRModKitPlugin : BasePlugin
    {
        public const string PluginGuid = "local.afterthefall.vrmodkit";
        public const string PluginName = "After The Fall VR Mod Kit";
        public const string PluginVersion = "0.6.19";

        private static readonly BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly TimeSpan ServerGameCleanupThrottle = TimeSpan.FromSeconds(15);
        private const string ComfortEnemyProxyName = "AfterTheFallVRModKit_ComfortEnemyProxy";
        private const string ComfortEnemyBoneBoundMarkerName = "AfterTheFallVRModKit_ComfortEnemyProxy_BoneBound";
        private const string ComfortEnemyMatrixMarkerName = "AfterTheFallVRModKit_ComfortEnemyProxy_MatrixDriven";
        private const string ComfortEnemyBoundsMarkerName = "AfterTheFallVRModKit_ComfortEnemyProxy_BoundsDriven";
        private const string ComfortEnemyTintMarkerName = "AfterTheFallVRModKit_ComfortEnemyProxy_TintOnly";

        private static ManualLogSource _log;
        private static DateTime _lastServerGameCleanupUtc = DateTime.MinValue;
        private static int _serverGameCleanupAttempts;
        private static bool _suppressClientBloodAndGore = true;
        private static bool _cleanupRetainedServerGame = true;
        private static bool _doorbellWaveSound = true;
        private static GameObject _doorbellAudioObject;
        private static AudioClip _doorbellAudioClip;
        private static bool _comfortEnemyVisuals;
        private static Material _comfortEnemyMaterial;
        private static Texture2D _comfortEnemyAlbedoTexture;
        private static Texture2D _comfortEnemyDarkTexture;
        private static Texture2D _comfortEnemyImpactTexture;
        private static Texture2D _comfortEnemyOnnrTexture;
        private static Texture2DArray _comfortEnemyAlbedoTextureArray;
        private static Texture2DArray _comfortEnemyOnnrTextureArray;
        private static readonly Dictionary<int, Material> _comfortEnemyMaterialsBySource = new Dictionary<int, Material>();
        private static readonly Dictionary<string, int> _shaderPropertyIdsByTypeAndName = new Dictionary<string, int>();
        private static readonly HashSet<string> _comfortEnemyDiagnosticsLogged = new HashSet<string>();
        private static readonly HashSet<string> _comfortEnemyShaderDiagnosticsLogged = new HashSet<string>();
        private static readonly HashSet<string> _comfortEnemyFallbackLogged = new HashSet<string>();
        private static readonly HashSet<string> _comfortEnemyMatrixLogged = new HashSet<string>();
        private static readonly HashSet<string> _comfortEnemyStateLogged = new HashSet<string>();
        private static readonly HashSet<string> _suppressedBloodFxLogged = new HashSet<string>();
        private static readonly HashSet<string> _doorbellAudioLogged = new HashSet<string>();
        private static readonly string[] SuppressedBloodFxNameFragments = new[]
        {
            "blood_burst",
            "blood_explosion",
            "particle_blood",
            "blood_splatter",
            "bloodsplatter",
            "blood_splat",
            "blood_rocketimpact",
            "blood_gib",
            "fxville blood",
            "particle blood",
            "standardzombieblood"
        };
        private static bool _comfortEnemyDiagnostics;
        private Harmony _harmony;

        public override void Load()
        {
            _log = Log;
            _harmony = new Harmony(PluginGuid);

            var disableInGameVoip = BindFeature("DisableInGameVoip", true, "Disable After The Fall's built-in VOIP handlers. Leave this on when using Discord or another voice chat.");
            _suppressClientBloodAndGore = BindFeature("SuppressClientBloodAndGore", true, "Skip client-side blood, decal, gib, and mutilation visual handlers.");
            _cleanupRetainedServerGame = BindFeature("CleanupRetainedServerGame", true, "After returning to the hub, dispose a retained ServerGame instance if the game leaves one in memory.");
            _doorbellWaveSound = BindFeature("DoorbellWaveSound", true, "Replace the harsh horde wave-start stinger with a short doorbell-like ding dong tone.");
            _comfortEnemyVisuals = BindFeature("ComfortEnemyVisuals", false, "Nephew Mode: make enemies look less scary with a bland plastic skin. Cosmetic only; gameplay, hitboxes, AI, damage, and networking are untouched.");
            _comfortEnemyDiagnostics = BindFeature("ComfortEnemyDiagnostics", false, "Log low-noise zombie visual state events for Nephew Mode development: crawling, limb mutilation, death, body IK, and ragdoll state. Advanced: leave off for normal play.");

            Log.LogInfo(PluginName + " " + PluginVersion + " loading. Features: DisableInGameVoip=" + disableInGameVoip + ", SuppressClientBloodAndGore=" + _suppressClientBloodAndGore + ", CleanupRetainedServerGame=" + _cleanupRetainedServerGame + ", DoorbellWaveSound=" + _doorbellWaveSound + ", NephewMode=" + _comfortEnemyVisuals + ", ComfortEnemyDiagnostics=" + _comfortEnemyDiagnostics + ".");
            Log.LogInfo("Gameplay networking and damage handling are left untouched.");

            if (disableInGameVoip)
            {
                PatchVoipSuppression();
            }
            else
            {
                Log.LogInfo("DisableInGameVoip is off; VOIP methods were not patched.");
            }

            if (_suppressClientBloodAndGore)
            {
                PatchClientBloodAndGoreSuppression();
            }
            else
            {
                Log.LogInfo("SuppressClientBloodAndGore is off; blood/gore visual methods were not patched.");
            }

            if (_doorbellWaveSound)
            {
                PatchDoorbellWaveSound();
            }
            else
            {
                Log.LogInfo("DoorbellWaveSound is off; wave-start audio was not patched.");
            }

            if (_comfortEnemyVisuals)
            {
                PatchPostfixNamedMethods("Vertigo.Snowbreed.Zombies.ZombieSkinRenderingModule",
                    nameof(PostZombieSkinRenderingModuleOnSpawned),
                    "OnSpawned");

                PatchPostfixNamedMethods("Vertigo.Snowbreed.Zombies.ZombieSkinRenderingModule",
                    nameof(PostZombieSkinRenderingModulePoseUpdate),
                    "ApplyMaterialPropertyBlock");

                Log.LogInfo("Nephew Mode uses the real animated zombie renderer with a bland plastic skin. Primitive proxy, skinning-matrix, and visibility hooks are disabled.");

                if (_comfortEnemyDiagnostics)
                {
                    PatchComfortEnemyStateDiagnostics();
                }
                else
                {
                    Log.LogInfo("ComfortEnemyDiagnostics is off; zombie state event diagnostics were not patched.");
                }
            }
            else
            {
                Log.LogInfo("Nephew Mode is off; zombie skin renderers were not changed.");
            }

            if (_cleanupRetainedServerGame)
            {
                PatchPostfixNamedMethods("Vertigo.Snowbreed.ClientSceneManager",
                    nameof(PostSceneLoadingFinishedCleanup),
                    "HandleOnSceneLoadingFinishedEvent");
            }
            else
            {
                Log.LogInfo("CleanupRetainedServerGame is off; hub-return ServerGame cleanup was not patched.");
            }

            Log.LogInfo(PluginName + " finished installing enabled patches.");
        }

        private bool BindFeature(string name, bool defaultValue, string description)
        {
            return Config.Bind("Features", name, defaultValue, new ConfigDescription(description)).Value;
        }

        private void PatchComfortEnemyStateDiagnostics()
        {
            PatchPostfixNamedMethods("Vertigo.Snowbreed.CrawlMovementModule",
                nameof(PostComfortEnemyStateDiagnostic),
                "OnIsCrawlingChanged");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.MutilatablePart",
                nameof(PostComfortEnemyStateDiagnostic),
                "Mutilate",
                "Kill");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.MutilationHealth",
                nameof(PostComfortEnemyStateDiagnostic),
                "Kill",
                "ApplyMutilationPartDamage");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.MutilationHealthModule",
                nameof(PostComfortEnemyStateDiagnostic),
                "Kill",
                "UpdateSevered",
                "HandleLegSeveredEvent");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.Shared.BipedalModule",
                nameof(PostComfortEnemyStateDiagnostic),
                "HandleIsBlockingChangedEvent");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.Client.ZombieAnimationSetModule",
                nameof(PostComfortEnemyStateDiagnostic),
                "HandlePartMutilatedEvent",
                "HandleIsCrawlingChangedEvent",
                "UpdateCurrentAnimationSet");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.Zombies.ZombieSkinBodyIKModule",
                nameof(PostComfortEnemyStateDiagnostic),
                "HandleOnDeathEvent",
                "HandleCrawlingChangedEvent",
                "HandlePartMutilatedEvent",
                "HandleIsOnFeetChangedEvent");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.Zombies.ZombieSkinRigModule",
                nameof(PostComfortEnemyStateDiagnostic),
                "OnSpawned",
                "OnDespawned");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.Zombies.ZombieSkinRagdollControllerModule",
                nameof(PostComfortEnemyStateDiagnostic),
                "HandlePhysicsStateChangedEvent");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.Zombies.RagdollZombieAnimationPhysicsBlender",
                nameof(PostComfortEnemyStateDiagnostic),
                "SetPhysicsControlled",
                "SetAnimationControlled",
                "BlendToAnimationControlled",
                "HandlePhysicsStateChangedEvent");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.Zombies.AnimationZombieAnimationPhysicsBlender",
                nameof(PostComfortEnemyStateDiagnostic),
                "SetPhysicsControlled",
                "SetAnimationControlled",
                "BlendToAnimationControlled");

            // ZombieMutilationView has fragile IL2CPP methods; use MutilationHealth and MutilatablePart events instead.
        }

        private void PatchVoipSuppression()
        {
            PatchAllNamedMethods("Vertigo.Voip.Fmod.FmodVoipRecorder",
                "Update",
                "UpdateRecordDriver",
                "VoipThread");

            PatchAllNamedMethods("Vertigo.Voip.VoipClient",
                "JoinChannel",
                "JoinBroadcastingChannel",
                "HandleVoipInitPacket",
                "HandleVoipInitResponsePacket",
                "SetVolumeOther");

            PatchAllNamedMethods("Vertigo.Voip.VoipRemotePeer",
                "HandleJoinedChannelPacket",
                "SetVolume");

            PatchAllNamedMethods("Vertigo.Snowbreed.Client.ClientSnowbreedVoipGameSystem",
                "OnGetUserInfo");
        }

        private void PatchClientBloodAndGoreSuppression()
        {
            PatchAllNamedMethods("Vertigo.Snowbreed.BloodPainter",
                "PaintBulletBlood",
                "PaintIndirectSplatterBlood",
                "PaintGibFloorBlood",
                "PaintGibSplatterBlood",
                "PaintSplatterBlood",
                "PaintImpactBlood",
                "PaintBloodPool",
                "PaintBloodDecal",
                "PaintBloodDecalNow",
                "PaintQueuedBloodDecal");

            PatchAllNamedMethods("BloodPoolPainterModule",
                "PaintBloodPoolOnHips",
                "PaintBloodGroundBelowPosition");

            PatchAllNamedMethods("Vertigo.Snowbreed.PaintBloodOnCollisionBehaviour",
                "TryPaintBlood");

            PatchAllNamedMethods("Vertigo.Snowbreed.Zombies.ZombieMutilationView",
                "PaintMutilationBlood");

            PatchAllNamedMethods("Vertigo.Snowbreed.Zombies.ZombieSkinHitImpactModule",
                "HandleZombieHitEvent",
                "OnImpact",
                "OnHitImpact",
                "ApplyMutilationEffect");

            PatchAllNamedMethods("Vertigo.Snowbreed.Zombies.ZombieBloodMaskPainter",
                "PaintBlood");

            PatchAllNamedMethods("Vertigo.Snowbreed.ClientEnemyNetworking",
                "HandleEnemyGibNetworkMessage");

            PatchBloodFxObjectSuppression();

            Log.LogInfo("Zombie skin hit impact and blood-mask painting are suppressed with the rest of the client-side blood/gore visuals.");
        }

        private void PatchBloodFxObjectSuppression()
        {
            PatchPostfixNamedMethods("UnityEngine.Object",
                nameof(PostInstantiateSuppressBloodFx),
                "Instantiate");

            PatchPrefixNamedMethods("UnityEngine.GameObject",
                nameof(PrefixSetActiveSuppressBloodFx),
                "SetActive");

            PatchPostfixNamedMethods("Vertigo.Snowbreed.ClientSceneManager",
                nameof(PostSceneLoadingFinishedSuppressBloodFx),
                "HandleOnSceneLoadingFinishedEvent");

            Log.LogInfo("Blood particle/prefab activation suppression is enabled for known zombie impact blood effects.");
        }

        private void PatchDoorbellWaveSound()
        {
            PatchPrefixNamedMethods("Vertigo.Audio.AudioUtils",
                nameof(PrefixDoorbellWaveAudio),
                "PlayOneShot",
                "PlayOneShotAt",
                "PlayOneShotAttached");

            Log.LogInfo("DoorbellWaveSound is watching wave/horde one-shot audio events.");
        }

        private void PatchAllNamedMethods(string typeName, params string[] methodNames)
        {
            var type = FindLoadedType(typeName);
            if (type == null)
            {
                Log.LogWarning("Type not found: " + typeName);
                return;
            }

            foreach (var methodName in methodNames)
            {
                var methods = AccessTools.GetDeclaredMethods(type)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    .Where(m => !m.IsAbstract)
                    .ToArray();

                if (methods.Length == 0)
                {
                    Log.LogWarning("Method not found: " + typeName + "." + methodName);
                    continue;
                }

                foreach (var method in methods)
                {
                    try
                    {
                        _harmony.Patch(method, prefix: new HarmonyMethod(typeof(AfterTheFallVRModKitPlugin), nameof(SkipSuppressedMethod)));
                        Log.LogInfo("Patched " + Describe(method));
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning("Could not patch " + Describe(method) + ": " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
        }

        private void PatchPrefixNamedMethods(string typeName, string prefixMethodName, params string[] methodNames)
        {
            var type = FindLoadedType(typeName);
            if (type == null)
            {
                Log.LogWarning("Type not found: " + typeName);
                return;
            }

            foreach (var methodName in methodNames)
            {
                var methods = AccessTools.GetDeclaredMethods(type)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    .Where(m => !m.IsAbstract)
                    .ToArray();

                if (methods.Length == 0)
                {
                    Log.LogWarning("Method not found: " + typeName + "." + methodName);
                    continue;
                }

                foreach (var method in methods)
                {
                    try
                    {
                        _harmony.Patch(method, prefix: new HarmonyMethod(typeof(AfterTheFallVRModKitPlugin), prefixMethodName));
                        Log.LogInfo("Prefix patched " + Describe(method));
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning("Could not prefix patch " + Describe(method) + ": " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
        }

        private void PatchPostfixNamedMethods(string typeName, string postfixMethodName, params string[] methodNames)
        {
            var type = FindLoadedType(typeName);
            if (type == null)
            {
                Log.LogWarning("Type not found: " + typeName);
                return;
            }

            foreach (var methodName in methodNames)
            {
                var methods = AccessTools.GetDeclaredMethods(type)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    .Where(m => !m.IsAbstract)
                    .ToArray();

                if (methods.Length == 0)
                {
                    Log.LogWarning("Method not found: " + typeName + "." + methodName);
                    continue;
                }

                foreach (var method in methods)
                {
                    try
                    {
                        _harmony.Patch(method, postfix: new HarmonyMethod(typeof(AfterTheFallVRModKitPlugin), postfixMethodName));
                        Log.LogInfo("Postfix patched " + Describe(method));
                    }
                    catch (Exception ex)
                    {
                        Log.LogWarning("Could not postfix patch " + Describe(method) + ": " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
        }

        private static Type FindLoadedType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try
                {
                    type = assembly.GetType(typeName, false);
                }
                catch
                {
                    // Some generated IL2CPP facade assemblies contain invalid reflection metadata.
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static bool SkipSuppressedMethod(MethodBase __originalMethod)
        {
            return false;
        }

        private static void PostInstantiateSuppressBloodFx(UnityEngine.Object __result)
        {
            try
            {
                if (!_suppressClientBloodAndGore || __result == null)
                {
                    return;
                }

                if (TryGetGameObject(__result, out var gameObject) && IsSuppressedBloodFxObject(gameObject))
                {
                    SuppressBloodFxGameObject(gameObject, "instantiate");
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("Blood FX instantiate suppression failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool PrefixSetActiveSuppressBloodFx(GameObject __instance, bool value)
        {
            try
            {
                if (!_suppressClientBloodAndGore || !value || __instance == null)
                {
                    return true;
                }

                if (!IsSuppressedBloodFxObject(__instance))
                {
                    return true;
                }

                SuppressBloodFxGameObject(__instance, "activate");
                return false;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Blood FX activation suppression failed: " + ex.GetType().Name + ": " + ex.Message);
                return true;
            }
        }

        private static void PostSceneLoadingFinishedSuppressBloodFx()
        {
            try
            {
                if (_suppressClientBloodAndGore)
                {
                    SuppressExistingBloodFxObjects();
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("Blood FX scene sweep failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool PrefixDoorbellWaveAudio(MethodBase __originalMethod, object[] __args)
        {
            try
            {
                if (!_doorbellWaveSound)
                {
                    return true;
                }

                var eventText = FindWaveAudioEventText(__args);
                if (eventText == null)
                {
                    return true;
                }

                PlayDoorbellTone();
                LogDoorbellAudioOnce(eventText, "Replaced wave audio event '" + eventText + "' from " + Describe(__originalMethod) + " with doorbell tone.");
                return false;
            }
            catch (Exception ex)
            {
                _log.LogWarning("DoorbellWaveSound prefix failed: " + ex.GetType().Name + ": " + ex.Message);
                return true;
            }
        }

        private static void SuppressExistingBloodFxObjects()
        {
            var objects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            foreach (var gameObject in objects)
            {
                if (gameObject != null && IsSuppressedBloodFxObject(gameObject))
                {
                    SuppressBloodFxGameObject(gameObject, "scene-sweep");
                }
            }
        }

        private static bool TryGetGameObject(UnityEngine.Object unityObject, out GameObject gameObject)
        {
            gameObject = unityObject as GameObject;
            if (gameObject != null)
            {
                return true;
            }

            var component = unityObject as Component;
            if (component != null)
            {
                gameObject = component.gameObject;
                return gameObject != null;
            }

            return false;
        }

        private static bool IsSuppressedBloodFxObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return false;
            }

            var current = gameObject.transform;
            var depth = 0;
            while (current != null && depth < 12)
            {
                if (ContainsAnyBloodFxFragment(SafeLower(current.name)))
                {
                    return true;
                }

                current = current.parent;
                depth++;
            }

            return false;
        }

        private static bool ContainsAnyBloodFxFragment(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (var fragment in SuppressedBloodFxNameFragments)
            {
                if (value.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SuppressBloodFxGameObject(GameObject gameObject, string source)
        {
            if (gameObject == null)
            {
                return;
            }

            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }

            var key = BuildGameObjectPath(gameObject);
            if (_suppressedBloodFxLogged.Add(source + "|" + key))
            {
                _log.LogInfo("Suppressed blood FX object via " + source + ": " + key);
            }
        }

        private static string BuildGameObjectPath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return "<null>";
            }

            var names = new List<string>();
            var current = gameObject.transform;
            while (current != null && names.Count < 12)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static string SafeLower(string value)
        {
            return value == null ? string.Empty : value.ToLowerInvariant();
        }

        private static string FindWaveAudioEventText(object[] args)
        {
            if (args == null)
            {
                return null;
            }

            foreach (var arg in args)
            {
                var match = FindWaveAudioEventText(arg, 2);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static string FindWaveAudioEventText(object value, int depth)
        {
            if (value == null)
            {
                return null;
            }

            var text = value as string;
            if (LooksLikeWaveScreechAudio(text))
            {
                return text;
            }

            try
            {
                text = value.ToString();
                if (LooksLikeWaveScreechAudio(text))
                {
                    return text;
                }
            }
            catch
            {
            }

            if (depth <= 0)
            {
                return null;
            }

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Quaternion))
            {
                return null;
            }

            foreach (var field in type.GetFields(InstanceFlags))
            {
                try
                {
                    var fieldValue = field.GetValue(value);
                    var match = FindWaveAudioEventText(fieldValue, depth - 1);
                    if (match != null)
                    {
                        return match;
                    }
                }
                catch
                {
                }
            }

            foreach (var property in type.GetProperties(InstanceFlags))
            {
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                try
                {
                    var propertyValue = property.GetValue(value);
                    var match = FindWaveAudioEventText(propertyValue, depth - 1);
                    if (match != null)
                    {
                        return match;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool LooksLikeWaveScreechAudio(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var lower = text.ToLowerInvariant();
            if (lower.Contains("luna_reminder_hordemode") || lower.Contains("hordeincoming") || lower.Contains("horde_incoming"))
            {
                return true;
            }

            var waveish = lower.Contains("horde") || lower.Contains("wave");
            if (!waveish)
            {
                return false;
            }

            return lower.Contains("incoming")
                || lower.Contains("start")
                || lower.Contains("stinger")
                || lower.Contains("screech")
                || lower.Contains("scream")
                || lower.Contains("alert")
                || lower.Contains("reminder")
                || lower.Contains("spawn");
        }

        private static void PlayDoorbellTone()
        {
            var clip = GetDoorbellAudioClip();
            if (clip == null)
            {
                return;
            }

            if (_doorbellAudioObject == null)
            {
                _doorbellAudioObject = new GameObject("AfterTheFallVRModKit_DoorbellAudio");
                UnityEngine.Object.DontDestroyOnLoad(_doorbellAudioObject);
            }

            var source = _doorbellAudioObject.GetComponent<AudioSource>();
            if (source == null)
            {
                source = _doorbellAudioObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                source.volume = 0.9f;
            }

            source.PlayOneShot(clip, 0.9f);
        }

        private static AudioClip GetDoorbellAudioClip()
        {
            if (_doorbellAudioClip != null)
            {
                return _doorbellAudioClip;
            }

            const int sampleRate = 44100;
            var sampleCount = (int)(sampleRate * 1.25f);
            var samples = new Il2CppStructArray<float>(sampleCount);
            for (var i = 0; i < sampleCount; i++)
            {
                var time = (float)i / sampleRate;
                var sample = DoorbellNote(time, 0.00f, 880f, 0.42f, 5.2f)
                    + DoorbellNote(time, 0.48f, 660f, 0.58f, 4.4f);
                samples[i] = Mathf.Clamp(sample * 0.38f, -1f, 1f);
            }

            _doorbellAudioClip = AudioClip.Create("AfterTheFallVRModKit_Doorbell", sampleCount, 1, sampleRate, false);
            _doorbellAudioClip.SetData(samples, 0);
            return _doorbellAudioClip;
        }

        private static float DoorbellNote(float time, float start, float frequency, float duration, float decay)
        {
            var local = time - start;
            if (local < 0f || local > duration)
            {
                return 0f;
            }

            var attack = 1f - (float)Math.Exp(-70f * local);
            var envelope = attack * (float)Math.Exp(-decay * local);
            var fundamental = Math.Sin(2.0 * Math.PI * frequency * local);
            var shimmer = 0.35 * Math.Sin(2.0 * Math.PI * frequency * 2.01 * local);
            var lowBody = 0.16 * Math.Sin(2.0 * Math.PI * frequency * 0.5 * local);
            return (float)((fundamental + shimmer + lowBody) * envelope);
        }

        private static void LogDoorbellAudioOnce(string key, string message)
        {
            if (_doorbellAudioLogged.Add(key))
            {
                _log.LogInfo(message);
            }
        }

        private static void PostZombieSkinRenderingModuleOnSpawned(object __instance, MethodBase __originalMethod)
        {
            try
            {
                if (!_comfortEnemyVisuals)
                {
                    return;
                }

                ApplyComfortEnemyVisual(__instance);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Nephew Mode visual postfix failed after " + Describe(__originalMethod) + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void PostZombieSkinRenderingModulePoseUpdate(object __instance)
        {
            try
            {
                if (!_comfortEnemyVisuals)
                {
                    return;
                }

                UpdateComfortEnemyVisual(__instance);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Nephew Mode visual pose update failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void PostZombieSkinRenderingModuleSkinningMatrices(object __instance, NativeArray<float4x4> __0)
        {
            try
            {
                if (!_comfortEnemyVisuals)
                {
                    return;
                }

                UpdateComfortEnemyVisualFromSkinningMatrices(__instance, __0);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Nephew Mode visual matrix update failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void PostZombieSkinRenderingModuleFrequentPoseUpdate(object __instance)
        {
            try
            {
                if (!_comfortEnemyVisuals)
                {
                    return;
                }

                UpdateComfortEnemyVisual(__instance);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Nephew Mode visual frequent update failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void PostZombieSkinRenderingModuleSetVisible(object __instance, bool __0)
        {
            try
            {
                if (!_comfortEnemyVisuals)
                {
                    return;
                }

                if (__0)
                {
                    ShowComfortEnemyVisual(__instance);
                }
                // The game also uses SetVisible(false) for renderer/culling transitions.
                // Do not treat it as death; hiding the proxy here can make nearby enemies invisible.
            }
            catch (Exception ex)
            {
                _log.LogWarning("Nephew Mode visual visibility update failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void UpdateComfortEnemyVisualFromSkinningMatrices(object zombieSkinRenderingModule, NativeArray<float4x4> matrices)
        {
            var meshRenderer = ReadInstanceProperty(zombieSkinRenderingModule, "MeshRenderer") as MeshRenderer;
            if (meshRenderer == null || meshRenderer.transform == null)
            {
                return;
            }

            var anchor = meshRenderer.transform;
            var marker = FindDirectChild(anchor, ComfortEnemyProxyName);
            if (marker == null)
            {
                marker = CreateComfortMarker(anchor, meshRenderer.gameObject.layer).transform;
            }

            var sourceBounds = meshRenderer.bounds;
            if (TryUpdateMatrixDrivenComfortProxy(marker, anchor, sourceBounds, matrices, meshRenderer.gameObject.layer, meshRenderer.gameObject.name))
            {
                CreateMarkerChild(marker, ComfortEnemyMatrixMarkerName);
                SetPrefixedDirectChildrenActive(marker, ComfortEnemyProxyName + "_Bounds", false);
                var tintMarker = FindDirectChild(marker, ComfortEnemyTintMarkerName);
                if (tintMarker != null)
                {
                    UnityEngine.Object.Destroy(tintMarker.gameObject);
                }

                marker.gameObject.SetActive(true);
                meshRenderer.enabled = false;
                LogMatrixDiagnosticsOnce(meshRenderer.gameObject.name + "|active", "matrix-driven proxy active; matrixCount=" + SafeNativeArrayLength(matrices) + ".");
                return;
            }

            if (FindDirectChild(marker, ComfortEnemyMatrixMarkerName) == null && FindDirectChild(marker, ComfortEnemyBoneBoundMarkerName) == null)
            {
                if (TryUpdateBoundsDrivenComfortProxy(meshRenderer, marker))
                {
                    CreateMarkerChild(marker, ComfortEnemyBoundsMarkerName);
                    meshRenderer.enabled = false;
                    LogComfortFallbackOnce(meshRenderer.gameObject.name + "|bounds", "Nephew Mode attached bounds-driven animated proxy to " + meshRenderer.gameObject.name + ".");
                    return;
                }

                CreateMarkerChild(marker, ComfortEnemyTintMarkerName);
                ApplyComfortEnemyTint(meshRenderer);
                meshRenderer.enabled = true;
                LogComfortFallbackOnce(meshRenderer.gameObject.name, "Nephew Mode could not build a matrix-driven proxy for " + meshRenderer.gameObject.name + "; kept the real animated mesh visible with a bland plastic skin.");
            }
        }

        private static void ShowComfortEnemyVisual(object zombieSkinRenderingModule)
        {
            var meshRenderer = ReadInstanceProperty(zombieSkinRenderingModule, "MeshRenderer") as MeshRenderer;
            if (meshRenderer == null || meshRenderer.transform == null)
            {
                return;
            }

            var marker = FindDirectChild(meshRenderer.transform, ComfortEnemyProxyName);
            if (marker == null)
            {
                ApplyComfortEnemyVisual(zombieSkinRenderingModule);
                return;
            }

            marker.gameObject.SetActive(true);
            SetNamedComfortPartsActive(meshRenderer.transform, true);
            ApplyExistingComfortEnemyVisual(meshRenderer, marker);
        }

        private static void HideComfortEnemyVisual(object zombieSkinRenderingModule)
        {
            var meshRenderer = ReadInstanceProperty(zombieSkinRenderingModule, "MeshRenderer") as MeshRenderer;
            if (meshRenderer == null || meshRenderer.transform == null)
            {
                return;
            }

            var marker = FindDirectChild(meshRenderer.transform, ComfortEnemyProxyName);
            if (marker != null)
            {
                marker.gameObject.SetActive(false);
            }

            SetNamedComfortPartsActive(meshRenderer.transform, false);
        }

        private static void ApplyComfortEnemyVisual(object zombieSkinRenderingModule)
        {
            var meshRenderer = ReadInstanceProperty(zombieSkinRenderingModule, "MeshRenderer") as MeshRenderer;
            if (meshRenderer == null)
            {
                _log.LogWarning("Nephew Mode could not read ZombieSkinRenderingModule.MeshRenderer.");
                return;
            }

            var anchor = meshRenderer.transform;
            if (anchor == null)
            {
                return;
            }

            DestroyComfortProxyObjects(anchor);
            ApplyRealRendererComfortSkin(zombieSkinRenderingModule, meshRenderer);
            LogComfortFallbackOnce(meshRenderer.gameObject.name + "|real-renderer", "Nephew Mode is using the real animated renderer for " + meshRenderer.gameObject.name + "; size, crawling, limb loss, and ragdoll stay game-driven.");
        }

        private static void UpdateComfortEnemyVisual(object zombieSkinRenderingModule)
        {
            var meshRenderer = ReadInstanceProperty(zombieSkinRenderingModule, "MeshRenderer") as MeshRenderer;
            if (meshRenderer == null)
            {
                return;
            }

            var anchor = meshRenderer.transform;
            if (anchor == null)
            {
                return;
            }

            DestroyComfortProxyObjects(anchor);
            ApplyRealRendererComfortSkin(zombieSkinRenderingModule, meshRenderer);
        }

        private static void ApplyRealRendererComfortSkin(object zombieSkinRenderingModule, MeshRenderer meshRenderer)
        {
            if (meshRenderer == null)
            {
                return;
            }

            meshRenderer.enabled = true;
            LogComfortRendererShaderDiagnostics(zombieSkinRenderingModule, meshRenderer);
            ApplyComfortEnemyTint(zombieSkinRenderingModule, meshRenderer);
        }

        private static void ApplyComfortEnemyMaterials(object zombieSkinRenderingModule, Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                return;
            }

            var changed = false;
            var replacements = new Material[materials.Length];
            for (var i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (source == null)
                {
                    replacements[i] = source;
                    continue;
                }

                if (source.name.StartsWith("AfterTheFallVRModKit_ComfortMaterial", StringComparison.Ordinal))
                {
                    ApplyComfortMaterialProperties(zombieSkinRenderingModule, source, color);
                    replacements[i] = source;
                    continue;
                }

                var key = source.GetInstanceID();
                if (!_comfortEnemyMaterialsBySource.TryGetValue(key, out var replacement) || replacement == null)
                {
                    replacement = new Material(source);
                    replacement.name = "AfterTheFallVRModKit_ComfortMaterial_" + source.name;
                    _comfortEnemyMaterialsBySource[key] = replacement;
                }

                ApplyComfortMaterialProperties(zombieSkinRenderingModule, replacement, color);
                replacements[i] = replacement;
                changed = true;
            }

            if (changed)
            {
                renderer.sharedMaterials = replacements;
            }
        }

        private static void ApplyComfortMaterialProperties(object zombieSkinRenderingModule, Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            SetMaterialColorIfPresent(material, "_Color", color);
            SetMaterialColorIfPresent(material, "_BaseColor", color);
            SetMaterialColorIfPresent(material, "_TintColor", color);
            SetMaterialColorIfPresent(material, "_DummyColor", color);
            SetMaterialColorIfPresent(material, "_dummyColor", color);
            SetMaterialFloatIfPresent(material, "_Metallic", 0f);
            SetMaterialFloatIfPresent(material, "_Smoothness", 0.16f);
            SetMaterialFloatIfPresent(material, "_Glossiness", 0.16f);
            SetMaterialFloatIfPresent(material, "_BumpScale", 0f);
            SetMaterialFloatIfPresent(material, "_DetailNormalMapScale", 0f);
            SetMaterialFloatIfPresent(material, "_OcclusionStrength", 0f);
            SetMaterialFloatIfPresent(material, "_Parallax", 0f);
            SetMaterialFloatIfPresent(material, "_ParallaxAmount", 0f);
            SetMaterialTextureIfPresent(material, "_MainTex", GetComfortEnemyAlbedoTexture());
            SetMaterialTextureIfPresent(material, "_BaseMap", GetComfortEnemyAlbedoTexture());
            SetMaterialTextureIfPresent(material, "_BaseColorMap", GetComfortEnemyAlbedoTexture());
            SetMaterialTextureIfPresent(material, "_DetailAlbedoMap", GetComfortEnemyAlbedoTexture());
            SetMaterialTextureIfPresent(material, "_BumpMap", GetComfortEnemyOnnrTexture());
            SetMaterialTextureIfPresent(material, "_NormalMap", GetComfortEnemyOnnrTexture());
            ApplyNamedComfortShaderMaterialProperties(material, color);

            if (TryReadShaderPropertyId(zombieSkinRenderingModule, "dummyColorShaderProperty", out var dummyColorProperty))
            {
                material.SetColor(dummyColorProperty, color);
            }

            if (TryReadShaderPropertyId(zombieSkinRenderingModule, "bloodOnnrShaderProperty", out var bloodOnnrProperty))
            {
                material.SetTexture(bloodOnnrProperty, GetComfortEnemyOnnrTexture());
            }
        }

        private static void DestroyComfortProxyObjects(Transform anchor)
        {
            if (anchor == null)
            {
                return;
            }

            var marker = FindDirectChild(anchor, ComfortEnemyProxyName);
            if (marker != null)
            {
                UnityEngine.Object.Destroy(marker.gameObject);
            }

            var searchRoot = FindBestHumanoidSearchRoot(anchor);
            var transforms = CollectDescendants(searchRoot, 500);
            foreach (var transform in transforms)
            {
                if (transform != null
                    && transform != anchor
                    && transform.gameObject != null
                    && transform.name.StartsWith(ComfortEnemyProxyName + "_", StringComparison.Ordinal))
                {
                    UnityEngine.Object.Destroy(transform.gameObject);
                }
            }
        }

        private static GameObject CreateComfortMarker(Transform anchor, int layer)
        {
            var marker = new GameObject(ComfortEnemyProxyName);
            marker.name = ComfortEnemyProxyName;
            marker.layer = layer;
            marker.transform.SetParent(anchor, false);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one;
            return marker;
        }

        private static void ApplyExistingComfortEnemyVisual(MeshRenderer meshRenderer, Transform marker)
        {
            marker.gameObject.SetActive(true);

            if (FindDirectChild(marker, ComfortEnemyMatrixMarkerName) != null || FindDirectChild(marker, ComfortEnemyBoneBoundMarkerName) != null)
            {
                meshRenderer.enabled = false;
                return;
            }

            if (FindDirectChild(marker, ComfortEnemyBoundsMarkerName) != null)
            {
                TryUpdateBoundsDrivenComfortProxy(meshRenderer, marker);
                meshRenderer.enabled = false;
                return;
            }

            ApplyComfortEnemyTint(meshRenderer);
            meshRenderer.enabled = true;
        }

        private static void CreateMarkerChild(Transform marker, string name)
        {
            if (FindDirectChild(marker, name) != null)
            {
                return;
            }

            var child = new GameObject(name);
            child.name = name;
            child.transform.SetParent(marker, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            child.SetActive(false);
        }

        private static bool TryUpdateMatrixDrivenComfortProxy(Transform marker, Transform anchor, Bounds sourceBounds, NativeArray<float4x4> matrices, int layer, string sourceName)
        {
            if (!TrySelectMatrixWorldPositions(anchor, sourceBounds, matrices, out var worldPositions))
            {
                LogMatrixDiagnosticsOnce(sourceName, "matrix positions did not align with renderer bounds; matrixCount=" + SafeNativeArrayLength(matrices) + ".");
                return false;
            }

            if (!TryBuildMatrixComfortPose(anchor, sourceBounds, worldPositions, out var pose))
            {
                LogMatrixDiagnosticsOnce(sourceName, "matrix positions aligned, but no usable humanoid pose could be derived; points=" + worldPositions.Count + ".");
                return false;
            }

            var material = GetComfortEnemyMaterial();
            var height = Math.Max(0.8f, pose.Height);
            var width = Math.Max(0.28f, pose.Width);
            var headDiameter = ClampFloat(height * 0.16f, 0.16f, 0.32f);
            var torsoDiameter = ClampFloat(width * 0.34f, 0.16f, 0.32f);
            var limbDiameter = ClampFloat(width * 0.12f, 0.055f, 0.13f);
            var jointDiameter = limbDiameter * 1.35f;

            SetSpherePart(marker, "MatrixHead", pose.Head, headDiameter, material, layer);
            SetSegmentPart(marker, "MatrixTorso", pose.Hips, pose.Chest, torsoDiameter, material, layer);
            SetCubePart(marker, "MatrixHips", pose.Hips, anchor.rotation, new Vector3(width * 0.34f, height * 0.045f, width * 0.18f), material, layer);
            SetSegmentPart(marker, "MatrixLeftArm", pose.Chest, pose.LeftHand, limbDiameter, material, layer);
            SetSegmentPart(marker, "MatrixRightArm", pose.Chest, pose.RightHand, limbDiameter, material, layer);
            SetSpherePart(marker, "MatrixLeftHand", pose.LeftHand, jointDiameter, material, layer);
            SetSpherePart(marker, "MatrixRightHand", pose.RightHand, jointDiameter, material, layer);
            SetSegmentPart(marker, "MatrixLeftLeg", pose.Hips, pose.LeftFoot, limbDiameter, material, layer);
            SetSegmentPart(marker, "MatrixRightLeg", pose.Hips, pose.RightFoot, limbDiameter, material, layer);

            return true;
        }

        private static bool TrySelectMatrixWorldPositions(Transform anchor, Bounds sourceBounds, NativeArray<float4x4> matrices, out List<Vector3> worldPositions)
        {
            worldPositions = new List<Vector3>();
            var length = SafeNativeArrayLength(matrices);
            if (length < 5)
            {
                return false;
            }

            var rawPositions = new List<Vector3>();
            var limit = Math.Min(length, 160);
            for (var i = 0; i < limit; i++)
            {
                var matrix = matrices[i];
                var raw = ExtractTranslation(matrix);
                if (IsFinite(raw) && raw.sqrMagnitude < 1000000f)
                {
                    rawPositions.Add(raw);
                }
            }

            if (rawPositions.Count < 5)
            {
                return false;
            }

            var rawCandidate = FilterPositionsNearBounds(rawPositions, sourceBounds);
            var transformedCandidate = FilterPositionsNearBounds(rawPositions.Select(anchor.TransformPoint), sourceBounds);
            var rawScore = ScoreMatrixPositionCandidate(rawCandidate, sourceBounds);
            var transformedScore = ScoreMatrixPositionCandidate(transformedCandidate, sourceBounds);

            worldPositions = transformedScore > rawScore ? transformedCandidate : rawCandidate;
            return Math.Max(rawScore, transformedScore) >= 5 && HasUsableVerticalRange(worldPositions);
        }

        private static int SafeNativeArrayLength(NativeArray<float4x4> matrices)
        {
            try
            {
                return matrices.IsCreated ? matrices.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static Vector3 ExtractTranslation(float4x4 matrix)
        {
            return new Vector3(matrix.c3.x, matrix.c3.y, matrix.c3.z);
        }

        private static List<Vector3> FilterPositionsNearBounds(IEnumerable<Vector3> positions, Bounds sourceBounds)
        {
            var expanded = sourceBounds;
            var expand = Math.Max(1.25f, sourceBounds.size.magnitude * 0.85f);
            expanded.Expand(expand);
            return positions
                .Where(IsFinite)
                .Where(position => expanded.Contains(position))
                .Distinct(new ApproxVector3Comparer())
                .Take(80)
                .ToList();
        }

        private static int ScoreMatrixPositionCandidate(List<Vector3> positions, Bounds sourceBounds)
        {
            if (positions.Count < 5)
            {
                return 0;
            }

            var bounds = BoundsFromPoints(positions);
            var horizontalDelta = HorizontalDistance(bounds.center, sourceBounds.center);
            var verticalDelta = Math.Abs(bounds.center.y - sourceBounds.center.y);
            var score = positions.Count;
            if (horizontalDelta > Math.Max(1.0f, sourceBounds.size.magnitude))
            {
                score -= 5;
            }

            if (verticalDelta > Math.Max(1.0f, sourceBounds.size.y))
            {
                score -= 5;
            }

            return Math.Max(0, score);
        }

        private static bool HasUsableVerticalRange(List<Vector3> positions)
        {
            if (positions.Count < 5)
            {
                return false;
            }

            var minY = positions.Min(position => position.y);
            var maxY = positions.Max(position => position.y);
            return maxY - minY >= 0.35f;
        }

        private static bool TryBuildMatrixComfortPose(Transform anchor, Bounds sourceBounds, List<Vector3> worldPositions, out MatrixComfortPose pose)
        {
            pose = null;
            if (worldPositions.Count < 5)
            {
                return false;
            }

            var localPositions = worldPositions
                .Select(position => new MatrixPosePoint(position, anchor.InverseTransformPoint(position)))
                .OrderBy(point => point.Local.y)
                .ToList();

            var minY = localPositions.First().Local.y;
            var maxY = localPositions.Last().Local.y;
            var height = maxY - minY;
            if (height < 0.35f)
            {
                return false;
            }

            var width = Math.Max(
                localPositions.Max(point => point.Local.x) - localPositions.Min(point => point.Local.x),
                localPositions.Max(point => point.Local.z) - localPositions.Min(point => point.Local.z));
            width = ClampFloat(width, Math.Max(sourceBounds.size.x, sourceBounds.size.z) * 0.35f, Math.Max(0.35f, Math.Max(sourceBounds.size.x, sourceBounds.size.z) * 1.2f));

            var head = localPositions.Last();
            var chest = PickClosestByHeight(localPositions, minY + height * 0.68f);
            var hips = PickClosestByHeight(localPositions, minY + height * 0.38f);
            var leftHand = PickLateralPoint(localPositions, minY + height * 0.25f, minY + height * 0.78f, true);
            var rightHand = PickLateralPoint(localPositions, minY + height * 0.25f, minY + height * 0.78f, false);
            var leftFoot = PickFootPoint(localPositions, true);
            var rightFoot = PickFootPoint(localPositions, false);

            if (leftHand == null || rightHand == null || leftFoot == null || rightFoot == null)
            {
                return false;
            }

            pose = new MatrixComfortPose
            {
                Head = head.World,
                Chest = chest.World,
                Hips = hips.World,
                LeftHand = leftHand.World,
                RightHand = rightHand.World,
                LeftFoot = leftFoot.World,
                RightFoot = rightFoot.World,
                Height = height,
                Width = width
            };
            return true;
        }

        private static MatrixPosePoint PickClosestByHeight(List<MatrixPosePoint> points, float targetY)
        {
            return points
                .OrderBy(point => Math.Abs(point.Local.y - targetY))
                .First();
        }

        private static MatrixPosePoint PickLateralPoint(List<MatrixPosePoint> points, float minY, float maxY, bool left)
        {
            var candidates = points
                .Where(point => point.Local.y >= minY && point.Local.y <= maxY)
                .ToList();
            if (candidates.Count == 0)
            {
                candidates = points;
            }

            return left
                ? candidates.OrderBy(point => point.Local.x).First()
                : candidates.OrderByDescending(point => point.Local.x).First();
        }

        private static MatrixPosePoint PickFootPoint(List<MatrixPosePoint> points, bool left)
        {
            var minY = points.Min(point => point.Local.y);
            var maxY = points.Max(point => point.Local.y);
            var lowCutoff = minY + ((maxY - minY) * 0.28f);
            var candidates = points
                .Where(point => point.Local.y <= lowCutoff)
                .ToList();
            if (candidates.Count == 0)
            {
                candidates = points.Take(Math.Max(1, points.Count / 4)).ToList();
            }

            return left
                ? candidates.OrderBy(point => point.Local.x).First()
                : candidates.OrderByDescending(point => point.Local.x).First();
        }

        private static Bounds BoundsFromPoints(IEnumerable<Vector3> points)
        {
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var point in points)
            {
                if (!hasBounds)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }

            return bounds;
        }

        private static void SetSpherePart(Transform parent, string name, Vector3 worldPosition, float diameter, Material material, int layer)
        {
            var part = EnsureComfortPart(parent, name, PrimitiveType.Sphere, material, layer);
            part.transform.position = worldPosition;
            part.transform.rotation = parent.rotation;
            part.transform.localScale = ScaledForParent(parent, new Vector3(diameter, diameter, diameter));
            ApplyComfortPartColor(part, GetAliveComfortColor());
            part.SetActive(true);
        }

        private static void SetCubePart(Transform parent, string name, Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale, Material material, int layer)
        {
            var part = EnsureComfortPart(parent, name, PrimitiveType.Cube, material, layer);
            part.transform.position = worldPosition;
            part.transform.rotation = worldRotation;
            part.transform.localScale = ScaledForParent(parent, worldScale);
            ApplyComfortPartColor(part, GetAliveComfortColor());
            part.SetActive(true);
        }

        private static void SetSegmentPart(Transform parent, string name, Vector3 start, Vector3 end, float diameter, Material material, int layer)
        {
            var delta = end - start;
            var length = delta.magnitude;
            if (length < 0.05f)
            {
                return;
            }

            var part = EnsureComfortPart(parent, name, PrimitiveType.Capsule, material, layer);
            part.transform.position = start + (delta * 0.5f);
            part.transform.rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            part.transform.localScale = ScaledForParent(parent, new Vector3(diameter, length * 0.5f, diameter));
            ApplyComfortPartColor(part, GetAliveComfortColor());
            part.SetActive(true);
        }

        private static GameObject EnsureComfortPart(Transform parent, string name, PrimitiveType primitiveType, Material material, int layer)
        {
            var existing = FindDirectChild(parent, name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            return CreateComfortPrimitive(parent, name, primitiveType, Vector3.zero, Quaternion.identity, Vector3.one, material, layer);
        }

        private static void SetDirectChildActive(Transform parent, string name, bool active)
        {
            var child = FindDirectChild(parent, name);
            if (child != null)
            {
                child.gameObject.SetActive(active);
            }
        }

        private static void SetPrefixedDirectChildrenActive(Transform parent, string namePrefix, bool active)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name.StartsWith(namePrefix, StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(active);
                }
            }
        }

        private static Color GetAliveComfortColor()
        {
            return new Color(0.84f, 0.96f, 1f, 1f);
        }

        private static void ApplyComfortPartColor(GameObject part, Color color)
        {
            if (part == null)
            {
                return;
            }

            var renderer = part.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            block.SetColor("_TintColor", color);
            block.SetFloat("_Metallic", 0f);
            block.SetFloat("_Smoothness", 0.16f);
            renderer.SetPropertyBlock(block);
        }

        private static void SetComfortProxyColor(Transform marker, Color color)
        {
            if (marker == null)
            {
                return;
            }

            var renderers = marker.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                block.SetColor("_TintColor", color);
                block.SetFloat("_Metallic", 0f);
                block.SetFloat("_Smoothness", 0.16f);
                renderer.SetPropertyBlock(block);
            }
        }

        private static bool TryUpdateBoundsDrivenComfortProxy(MeshRenderer meshRenderer, Transform marker)
        {
            if (meshRenderer == null || marker == null || !TryGetComfortSourceBounds(meshRenderer, out var bounds))
            {
                return false;
            }

            var material = GetComfortEnemyMaterial();
            var height = ClampFloat(bounds.size.y * 0.92f, 0.85f, 1.8f);
            var width = ClampFloat(Math.Max(bounds.size.x, bounds.size.z) * 0.82f, 0.28f, 0.78f);
            var center = bounds.center;
            var groundY = bounds.min.y + 0.025f;
            var forward = HorizontalDirection(meshRenderer.transform.forward, Vector3.forward);
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            if (!IsFinite(right) || right.sqrMagnitude < 0.01f)
            {
                right = HorizontalDirection(meshRenderer.transform.right, Vector3.right);
            }

            var root = new Vector3(center.x, groundY, center.z);
            var t = (Time.time * 9.0f) + ((meshRenderer.GetInstanceID() % 37) * 0.17f);
            var walk = (float)Math.Sin(t);
            var counterWalk = (float)Math.Sin(t + Math.PI);
            var shoulderX = width * 0.36f;
            var hipX = width * 0.18f;
            var stride = ClampFloat(width * 0.42f, 0.12f, 0.34f);
            var handLift = height * 0.06f;
            var footLift = height * 0.08f;

            var hips = root + (Vector3.up * (height * 0.36f));
            var chest = root + (Vector3.up * (height * 0.59f)) - (forward * (height * 0.025f));
            var head = root + (Vector3.up * (height * 0.74f)) - (forward * (height * 0.015f));
            var leftShoulder = chest - (right * shoulderX);
            var rightShoulder = chest + (right * shoulderX);
            var leftHand = root + (Vector3.up * ((height * 0.42f) + (counterWalk * handLift))) - (right * (width * 0.5f)) + (forward * (counterWalk * stride));
            var rightHand = root + (Vector3.up * ((height * 0.42f) + (walk * handLift))) + (right * (width * 0.5f)) + (forward * (walk * stride));
            var leftHip = hips - (right * hipX);
            var rightHip = hips + (right * hipX);
            var leftFoot = root - (right * hipX) + (forward * (walk * stride)) + (Vector3.up * ((height * 0.04f) + Math.Max(0f, walk) * footLift));
            var rightFoot = root + (right * hipX) + (forward * (counterWalk * stride)) + (Vector3.up * ((height * 0.04f) + Math.Max(0f, counterWalk) * footLift));
            var leftElbow = Vector3.Lerp(leftShoulder, leftHand, 0.52f) - (right * (width * 0.08f)) + (forward * (counterWalk * stride * 0.35f));
            var rightElbow = Vector3.Lerp(rightShoulder, rightHand, 0.52f) + (right * (width * 0.08f)) + (forward * (walk * stride * 0.35f));
            var leftKnee = Vector3.Lerp(leftHip, leftFoot, 0.48f) + (forward * (walk * stride * 0.45f)) + (Vector3.up * (height * 0.04f));
            var rightKnee = Vector3.Lerp(rightHip, rightFoot, 0.48f) + (forward * (counterWalk * stride * 0.45f)) + (Vector3.up * (height * 0.04f));

            var prefix = ComfortEnemyProxyName + "_Bounds";
            SetDirectChildActive(marker, prefix + "Neck", false);
            SetDirectChildActive(marker, prefix + "LeftArm", false);
            SetDirectChildActive(marker, prefix + "RightArm", false);
            SetDirectChildActive(marker, prefix + "LeftLeg", false);
            SetDirectChildActive(marker, prefix + "RightLeg", false);
            SetSpherePart(marker, prefix + "Head", head, ClampFloat(height * 0.18f, 0.18f, 0.34f), material, meshRenderer.gameObject.layer);
            SetSegmentPart(marker, prefix + "Torso", hips, chest, ClampFloat(width * 0.28f, 0.16f, 0.32f), material, meshRenderer.gameObject.layer);
            SetCubePart(marker, prefix + "Hips", hips, Quaternion.LookRotation(forward, Vector3.up), new Vector3(width * 0.42f, height * 0.055f, width * 0.22f), material, meshRenderer.gameObject.layer);
            SetSegmentPart(marker, prefix + "LeftUpperArm", leftShoulder, leftElbow, ClampFloat(width * 0.085f, 0.05f, 0.11f), material, meshRenderer.gameObject.layer);
            SetSegmentPart(marker, prefix + "LeftForeArm", leftElbow, leftHand, ClampFloat(width * 0.075f, 0.045f, 0.1f), material, meshRenderer.gameObject.layer);
            SetSegmentPart(marker, prefix + "RightUpperArm", rightShoulder, rightElbow, ClampFloat(width * 0.085f, 0.05f, 0.11f), material, meshRenderer.gameObject.layer);
            SetSegmentPart(marker, prefix + "RightForeArm", rightElbow, rightHand, ClampFloat(width * 0.075f, 0.045f, 0.1f), material, meshRenderer.gameObject.layer);
            SetSpherePart(marker, prefix + "LeftElbow", leftElbow, ClampFloat(width * 0.1f, 0.06f, 0.12f), material, meshRenderer.gameObject.layer);
            SetSpherePart(marker, prefix + "RightElbow", rightElbow, ClampFloat(width * 0.1f, 0.06f, 0.12f), material, meshRenderer.gameObject.layer);
            SetSpherePart(marker, prefix + "LeftHand", leftHand, ClampFloat(width * 0.13f, 0.075f, 0.15f), material, meshRenderer.gameObject.layer);
            SetSpherePart(marker, prefix + "RightHand", rightHand, ClampFloat(width * 0.13f, 0.075f, 0.15f), material, meshRenderer.gameObject.layer);
            SetSegmentPart(marker, prefix + "LeftUpperLeg", leftHip, leftKnee, ClampFloat(width * 0.105f, 0.06f, 0.13f), material, meshRenderer.gameObject.layer);
            SetSegmentPart(marker, prefix + "LeftLowerLeg", leftKnee, leftFoot, ClampFloat(width * 0.095f, 0.055f, 0.12f), material, meshRenderer.gameObject.layer);
            SetSegmentPart(marker, prefix + "RightUpperLeg", rightHip, rightKnee, ClampFloat(width * 0.105f, 0.06f, 0.13f), material, meshRenderer.gameObject.layer);
            SetSegmentPart(marker, prefix + "RightLowerLeg", rightKnee, rightFoot, ClampFloat(width * 0.095f, 0.055f, 0.12f), material, meshRenderer.gameObject.layer);
            SetSpherePart(marker, prefix + "LeftKnee", leftKnee, ClampFloat(width * 0.11f, 0.065f, 0.13f), material, meshRenderer.gameObject.layer);
            SetSpherePart(marker, prefix + "RightKnee", rightKnee, ClampFloat(width * 0.11f, 0.065f, 0.13f), material, meshRenderer.gameObject.layer);
            SetSpherePart(marker, prefix + "LeftFoot", leftFoot, ClampFloat(width * 0.15f, 0.08f, 0.18f), material, meshRenderer.gameObject.layer);
            SetSpherePart(marker, prefix + "RightFoot", rightFoot, ClampFloat(width * 0.15f, 0.08f, 0.18f), material, meshRenderer.gameObject.layer);

            marker.gameObject.SetActive(true);
            return true;
        }

        private static bool TryGetComfortSourceBounds(MeshRenderer meshRenderer, out Bounds bounds)
        {
            bounds = new Bounds();
            if (meshRenderer == null)
            {
                return false;
            }

            if (TryGetColliderBounds(meshRenderer.transform, true, out bounds))
            {
                return true;
            }

            if (TryGetColliderBounds(meshRenderer.transform, false, out bounds))
            {
                return true;
            }

            bounds = meshRenderer.bounds;
            if (IsUsableBounds(bounds))
            {
                return true;
            }

            return TryGetEstimatedEnemyBounds(meshRenderer, out bounds);
        }

        private static bool TryGetEstimatedEnemyBounds(MeshRenderer meshRenderer, out Bounds bounds)
        {
            bounds = new Bounds();
            if (meshRenderer == null || meshRenderer.transform == null)
            {
                return false;
            }

            var root = meshRenderer.transform;
            var basePosition = root.position;
            var hitTransform = FindDescendantByName(root, "Hit Collider");
            if (hitTransform != null && IsFinite(hitTransform.position))
            {
                basePosition.x = hitTransform.position.x;
                basePosition.z = hitTransform.position.z;
            }

            if (!IsFinite(basePosition))
            {
                return false;
            }

            var size = EstimateEnemyVisualSize(meshRenderer.gameObject == null ? string.Empty : meshRenderer.gameObject.name);
            bounds = new Bounds(basePosition + (Vector3.up * (size.y * 0.5f)), size);
            LogComfortFallbackOnce((meshRenderer.gameObject == null ? string.Empty : meshRenderer.gameObject.name) + "|estimated-bounds",
                "Nephew Mode estimated bounds for " + (meshRenderer.gameObject == null ? "<unknown>" : meshRenderer.gameObject.name)
                + " from transform position because Unity did not expose usable renderer/collider bounds.");
            return IsUsableBounds(bounds);
        }

        private static Vector3 EstimateEnemyVisualSize(string enemyName)
        {
            var name = (enemyName ?? string.Empty).ToLowerInvariant();
            if (name.Contains("juggernaut") || name.Contains("supersmasher"))
            {
                return new Vector3(0.9f, 2.2f, 0.9f);
            }

            if (name.Contains("brute") || name.Contains("smasher"))
            {
                return new Vector3(0.82f, 1.95f, 0.82f);
            }

            if (name.Contains("eater"))
            {
                return new Vector3(0.62f, 1.35f, 0.62f);
            }

            if (name.Contains("jock"))
            {
                return new Vector3(0.7f, 1.7f, 0.7f);
            }

            if (name.Contains("loot"))
            {
                return new Vector3(0.5f, 1.2f, 0.5f);
            }

            return new Vector3(0.62f, 1.55f, 0.62f);
        }

        private static bool TryGetColliderBounds(Transform root, bool hitOnly, out Bounds bounds)
        {
            bounds = new Bounds();
            if (root == null)
            {
                return false;
            }

            var hasBounds = false;
            var colliders = root.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                if (collider == null)
                {
                    continue;
                }

                var colliderName = collider.gameObject == null ? string.Empty : collider.gameObject.name;
                if (hitOnly && colliderName.IndexOf("hit", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var colliderBounds = collider.bounds;
                if (!IsUsableBounds(colliderBounds))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = colliderBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(colliderBounds);
                }
            }

            return hasBounds;
        }

        private static bool IsUsableBounds(Bounds bounds)
        {
            return IsFinite(bounds.center)
                && IsFinite(bounds.size)
                && bounds.size.y > 0.2f
                && Math.Max(bounds.size.x, bounds.size.z) > 0.05f;
        }

        private static Vector3 HorizontalDirection(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (!IsFinite(value) || value.sqrMagnitude < 0.001f)
            {
                value = fallback;
                value.y = 0f;
            }

            return value.sqrMagnitude < 0.001f ? Vector3.forward : value.normalized;
        }

        private static bool TryBuildBoneBoundComfortDummy(Transform marker, Transform anchor, Vector3 sourceSize, int layer, List<Renderer> createdRenderers)
        {
            var searchRoot = FindBestHumanoidSearchRoot(anchor);
            var transforms = CollectDescendants(searchRoot, 500);

            var head = FindNamedTransform(transforms, IsHeadName);
            var chest = FindNamedTransform(transforms, IsChestName);
            var hips = FindNamedTransform(transforms, IsHipsName);
            var leftUpperArm = FindNamedTransform(transforms, name => IsSideName(name, true) && IsUpperArmName(name));
            var rightUpperArm = FindNamedTransform(transforms, name => IsSideName(name, false) && IsUpperArmName(name));
            var leftForeArm = FindNamedTransform(transforms, name => IsSideName(name, true) && IsForeArmName(name));
            var rightForeArm = FindNamedTransform(transforms, name => IsSideName(name, false) && IsForeArmName(name));
            var leftHand = FindNamedTransform(transforms, name => IsSideName(name, true) && IsHandName(name));
            var rightHand = FindNamedTransform(transforms, name => IsSideName(name, false) && IsHandName(name));
            var leftUpperLeg = FindNamedTransform(transforms, name => IsSideName(name, true) && IsUpperLegName(name));
            var rightUpperLeg = FindNamedTransform(transforms, name => IsSideName(name, false) && IsUpperLegName(name));
            var leftLowerLeg = FindNamedTransform(transforms, name => IsSideName(name, true) && IsLowerLegName(name));
            var rightLowerLeg = FindNamedTransform(transforms, name => IsSideName(name, false) && IsLowerLegName(name));

            var matchedParts = CountNonNull(head, chest, hips, leftUpperArm, rightUpperArm, leftForeArm, rightForeArm, leftUpperLeg, rightUpperLeg, leftLowerLeg, rightLowerLeg);
            if (matchedParts < 5)
            {
                LogComfortBoneDiagnostics(searchRoot, matchedParts, transforms);
                return false;
            }

            marker.gameObject.SetActive(true);

            var height = ComfortVisualHeight(sourceSize);
            var width = ComfortVisualWidth(sourceSize);
            var material = GetComfortEnemyMaterial();
            var torsoDiameter = ClampFloat(width * 0.35f, 0.16f, 0.3f);
            var limbDiameter = ClampFloat(width * 0.12f, 0.05f, 0.11f);
            var jointDiameter = limbDiameter * 1.45f;
            var headDiameter = ClampFloat(height * 0.15f, 0.14f, 0.24f);

            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(head, "Head", PrimitiveType.Sphere, Vector3.zero, Quaternion.identity, new Vector3(headDiameter, headDiameter, headDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(chest, "Torso", PrimitiveType.Capsule, Vector3.zero, Quaternion.identity, new Vector3(torsoDiameter, height * 0.16f, torsoDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(hips, "Hips", PrimitiveType.Cube, Vector3.zero, Quaternion.identity, new Vector3(width * 0.34f, height * 0.045f, width * 0.22f), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(leftUpperArm, "LeftUpperArm", PrimitiveType.Capsule, Vector3.zero, Quaternion.identity, new Vector3(limbDiameter, height * 0.12f, limbDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(rightUpperArm, "RightUpperArm", PrimitiveType.Capsule, Vector3.zero, Quaternion.identity, new Vector3(limbDiameter, height * 0.12f, limbDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(leftForeArm ?? leftUpperArm, "LeftForeArm", PrimitiveType.Capsule, Vector3.zero, Quaternion.identity, new Vector3(limbDiameter, height * 0.11f, limbDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(rightForeArm ?? rightUpperArm, "RightForeArm", PrimitiveType.Capsule, Vector3.zero, Quaternion.identity, new Vector3(limbDiameter, height * 0.11f, limbDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(leftHand, "LeftHand", PrimitiveType.Sphere, Vector3.zero, Quaternion.identity, new Vector3(jointDiameter, jointDiameter, jointDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(rightHand, "RightHand", PrimitiveType.Sphere, Vector3.zero, Quaternion.identity, new Vector3(jointDiameter, jointDiameter, jointDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(leftUpperLeg, "LeftUpperLeg", PrimitiveType.Capsule, Vector3.zero, Quaternion.identity, new Vector3(limbDiameter, height * 0.15f, limbDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(rightUpperLeg, "RightUpperLeg", PrimitiveType.Capsule, Vector3.zero, Quaternion.identity, new Vector3(limbDiameter, height * 0.15f, limbDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(leftLowerLeg ?? leftUpperLeg, "LeftLowerLeg", PrimitiveType.Capsule, Vector3.zero, Quaternion.identity, new Vector3(limbDiameter, height * 0.14f, limbDiameter), material, layer));
            TrackCreatedRenderer(createdRenderers, CreateOptionalComfortPrimitive(rightLowerLeg ?? rightUpperLeg, "RightLowerLeg", PrimitiveType.Capsule, Vector3.zero, Quaternion.identity, new Vector3(limbDiameter, height * 0.14f, limbDiameter), material, layer));

            return createdRenderers.Count >= 5;
        }

        private static GameObject CreateOptionalComfortPrimitive(Transform parent, string name, PrimitiveType primitiveType, Vector3 localPosition, Quaternion localRotation, Vector3 worldScale, Material material, int layer)
        {
            if (parent == null)
            {
                return null;
            }

            var existing = FindDirectChild(parent, ComfortEnemyProxyName + "_" + name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            return CreateComfortPrimitive(parent, ComfortEnemyProxyName + "_" + name, primitiveType, localPosition, localRotation, worldScale, material, layer);
        }

        private static void TrackCreatedRenderer(List<Renderer> createdRenderers, GameObject part)
        {
            if (part == null)
            {
                return;
            }

            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                createdRenderers.Add(renderer);
            }
        }

        private static bool ValidateComfortProxyBounds(Bounds sourceBounds, List<Renderer> proxyRenderers, string sourceName)
        {
            if (proxyRenderers.Count < 5)
            {
                return false;
            }

            var hasBounds = false;
            var proxyBounds = new Bounds();
            foreach (var renderer in proxyRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    proxyBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    proxyBounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                return false;
            }

            var sourceHorizontalSize = Math.Max(sourceBounds.size.x, sourceBounds.size.z);
            var horizontalDelta = HorizontalDistance(sourceBounds.center, proxyBounds.center);
            var verticalDelta = Math.Abs(sourceBounds.center.y - proxyBounds.center.y);
            var maxHorizontalDelta = Math.Max(1.2f, sourceHorizontalSize * 1.8f);
            var maxVerticalDelta = Math.Max(1.6f, sourceBounds.size.y * 1.1f);

            if (horizontalDelta <= maxHorizontalDelta && verticalDelta <= maxVerticalDelta)
            {
                return true;
            }

            _log.LogWarning("Nephew Mode rejected bone-bound proxy for " + sourceName
                + ": sourceCenter=" + sourceBounds.center
                + ", proxyCenter=" + proxyBounds.center
                + ", horizontalDelta=" + horizontalDelta.ToString("0.00")
                + ", verticalDelta=" + verticalDelta.ToString("0.00") + ".");
            return false;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            var x = a.x - b.x;
            var z = a.z - b.z;
            return (float)Math.Sqrt((x * x) + (z * z));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void DestroyComfortRenderers(IEnumerable<Renderer> renderers)
        {
            foreach (var renderer in renderers)
            {
                if (renderer != null && renderer.gameObject != null)
                {
                    UnityEngine.Object.Destroy(renderer.gameObject);
                }
            }
        }

        private static void SetNamedComfortPartsActive(Transform anchor, bool active)
        {
            var searchRoot = FindBestHumanoidSearchRoot(anchor);
            var transforms = CollectDescendants(searchRoot, 500);
            foreach (var transform in transforms)
            {
                if (transform != null && transform.gameObject != null && transform.name.StartsWith(ComfortEnemyProxyName + "_", StringComparison.Ordinal))
                {
                    transform.gameObject.SetActive(active);
                }
            }
        }

        private static void ApplyComfortEnemyTint(Renderer renderer)
        {
            ApplyComfortEnemyTint(null, renderer);
        }

        private static void ApplyComfortEnemyTint(object zombieSkinRenderingModule, Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var color = GetAliveComfortColor();
            ApplyComfortEnemyMaterials(zombieSkinRenderingModule, renderer, color);

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            block.SetColor("_TintColor", color);
            block.SetColor("_DummyColor", color);
            block.SetColor("_dummyColor", color);
            block.SetFloat("_Metallic", 0f);
            block.SetFloat("_Smoothness", 0.16f);
            block.SetFloat("_Glossiness", 0.16f);
            block.SetFloat("_BumpScale", 0f);
            block.SetFloat("_DetailNormalMapScale", 0f);
            block.SetFloat("_OcclusionStrength", 0f);
            block.SetFloat("_Parallax", 0f);
            block.SetFloat("_ParallaxAmount", 0f);
            block.SetTexture("_MainTex", GetComfortEnemyAlbedoTexture());
            block.SetTexture("_BaseMap", GetComfortEnemyAlbedoTexture());
            block.SetTexture("_BaseColorMap", GetComfortEnemyAlbedoTexture());
            block.SetTexture("_DetailAlbedoMap", GetComfortEnemyAlbedoTexture());
            block.SetTexture("_BumpMap", GetComfortEnemyOnnrTexture());
            block.SetTexture("_NormalMap", GetComfortEnemyOnnrTexture());
            ApplyNamedComfortShaderBlockProperties(block, color);
            ApplyComfortShaderPropertyOverrides(zombieSkinRenderingModule, block, color);
            renderer.SetPropertyBlock(block);

            var materials = renderer.sharedMaterials;
            var materialCount = materials == null ? 0 : materials.Length;
            for (var i = 0; i < materialCount; i++)
            {
                renderer.SetPropertyBlock(block, i);
            }
        }

        private static void LogComfortRendererShaderDiagnostics(object zombieSkinRenderingModule, Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                var emptyKey = DescribeObject(zombieSkinRenderingModule) + "|" + renderer.name + "|<no-materials>";
                if (_comfortEnemyShaderDiagnosticsLogged.Add(emptyKey))
                {
                    _log.LogWarning("Nephew Mode shader diagnostic: renderer '" + renderer.name + "' has no shared materials.");
                }

                return;
            }

            var idSummary = DescribeComfortShaderPropertyIds(zombieSkinRenderingModule);
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                var shader = material == null ? null : material.shader;
                var shaderName = shader == null ? "<null-shader>" : shader.name;
                var materialName = material == null ? "<null-material>" : material.name;
                var key = DescribeObject(zombieSkinRenderingModule) + "|" + renderer.name + "|" + i + "|" + shaderName + "|" + materialName;
                if (!_comfortEnemyShaderDiagnosticsLogged.Add(key))
                {
                    continue;
                }

                _log.LogWarning("Nephew Mode shader diagnostic: renderer='" + renderer.name + "', material[" + i + "]='" + materialName + "', shader='" + shaderName + "', ids=[" + idSummary + "], shaderProperties=[" + DescribeShaderProperties(shader, 32) + "]");
            }
        }

        private static string DescribeComfortShaderPropertyIds(object zombieSkinRenderingModule)
        {
            var names = new[]
            {
                "dummyColorShaderProperty",
                "textureShaderProperty",
                "bloodMaskShaderProperty",
                "bloodOnnrShaderProperty",
                "noiseShaderProperty"
            };

            return string.Join(", ", names.Select(name =>
            {
                return TryReadShaderPropertyId(zombieSkinRenderingModule, name, out var propertyId)
                    ? name + "=" + propertyId
                    : name + "=<missing>";
            }));
        }

        private static string DescribeShaderProperties(Shader shader, int maxCount)
        {
            if (shader == null)
            {
                return "<null>";
            }

            try
            {
                var countMethod = FindInstanceMethod(shader.GetType(), "GetPropertyCount");
                var nameMethod = FindInstanceMethod(shader.GetType(), "GetPropertyName", typeof(int));
                var typeMethod = FindInstanceMethod(shader.GetType(), "GetPropertyType", typeof(int));
                if (countMethod == null || nameMethod == null)
                {
                    return "<property reflection unavailable>";
                }

                var propertyCount = Convert.ToInt32(countMethod.Invoke(shader, null));
                var descriptions = new List<string>();
                var count = Math.Min(propertyCount, maxCount);
                for (var i = 0; i < count; i++)
                {
                    var name = Convert.ToString(nameMethod.Invoke(shader, new object[] { i }));
                    var type = typeMethod == null ? null : typeMethod.Invoke(shader, new object[] { i });
                    var id = string.IsNullOrEmpty(name) ? 0 : Shader.PropertyToID(name);
                    descriptions.Add(type == null ? name + "#" + id : name + "#" + id + ":" + Convert.ToString(type));
                }

                if (propertyCount > maxCount)
                {
                    descriptions.Add("... +" + (propertyCount - maxCount) + " more");
                }

                return string.Join(", ", descriptions);
            }
            catch (Exception ex)
            {
                return "<property reflection failed: " + ex.GetType().Name + ">";
            }
        }

        private static void ApplyNamedComfortShaderMaterialProperties(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            SetMaterialTextureIfPresent(material, "_AlbedoArray", GetComfortEnemyAlbedoTextureArray());
            SetMaterialTextureIfPresent(material, "_ONNRArray", GetComfortEnemyOnnrTextureArray());
            SetMaterialTextureIfPresent(material, "_BloodTexture", GetComfortEnemyImpactTexture());
            SetMaterialTextureIfPresent(material, "_BloodONNR", GetComfortEnemyOnnrTexture());
            SetMaterialTextureIfPresent(material, "_Eater_FX", GetComfortEnemyDarkTexture());
            SetMaterialTextureIfPresent(material, "_Emission", GetComfortEnemyDarkTexture());

            SetMaterialColorIfPresent(material, "_EmissionColor", Color.black);
            SetMaterialColorIfPresent(material, "_VeinColorAtZeroAlpha", color);
            SetMaterialColorIfPresent(material, "_VeinColor", color);
            SetMaterialColorIfPresent(material, "_VeinColorExplode", color);
            SetMaterialColorIfPresent(material, "_FresnelColor", color);
            SetMaterialColorIfPresent(material, "_ColorEye", color);
            SetMaterialColorIfPresent(material, "_ColorEyeSpecial", color);

            SetMaterialFloatIfPresent(material, "_IS_SPECIAL", 0f);
            SetMaterialFloatIfPresent(material, "_HighlightStrength", 0f);
            SetMaterialFloatIfPresent(material, "_HighlightStrength1", 0f);
            SetMaterialFloatIfPresent(material, "_IceGlowIntensity", 0f);
            SetMaterialFloatIfPresent(material, "_VeinStrength", 0f);
            SetMaterialFloatIfPresent(material, "_FresnelIntensity", 0f);
            SetMaterialFloatIfPresent(material, "_Explosion", 0f);
            SetMaterialFloatIfPresent(material, "_StrengthEmmisive", 0f);
            SetMaterialFloatIfPresent(material, "_OveralStrenth", 0f);
            SetMaterialFloatIfPresent(material, "_CrawlerOrEater", 0f);
            SetMaterialFloatIfPresent(material, "_DeathAnim", 0f);
            SetMaterialFloatIfPresent(material, "_VSpecularIntensity", 0f);
            SetMaterialFloatIfPresent(material, "_VSpec", 0f);
            SetMaterialFloatIfPresent(material, "_Metallic", 0f);
            SetMaterialFloatIfPresent(material, "_Smoothness", 0.16f);
            SetMaterialFloatIfPresent(material, "_Glossiness", 0.16f);
            SetMaterialFloatIfPresent(material, "_BumpScale", 0f);
            SetMaterialFloatIfPresent(material, "_DetailNormalMapScale", 0f);
            SetMaterialFloatIfPresent(material, "_OcclusionStrength", 0f);
            SetMaterialFloatIfPresent(material, "_Parallax", 0f);
            SetMaterialFloatIfPresent(material, "_ParallaxAmount", 0f);
            SetMaterialFloatIfPresent(material, "_MinimumHighlightIntensity", 0f);
            SetMaterialFloatIfPresent(material, "_EmissionFogDistanceInfluence", 0f);
            SetMaterialFloatIfPresent(material, "_EaterFXStrength", 0f);
            SetMaterialFloatIfPresent(material, "_EaterFxStrength", 0f);
        }

        private static void ApplyNamedComfortShaderBlockProperties(MaterialPropertyBlock block, Color color)
        {
            if (block == null)
            {
                return;
            }

            block.SetTexture("_AlbedoArray", GetComfortEnemyAlbedoTextureArray());
            block.SetTexture("_ONNRArray", GetComfortEnemyOnnrTextureArray());
            block.SetTexture("_BloodTexture", GetComfortEnemyImpactTexture());
            block.SetTexture("_BloodONNR", GetComfortEnemyOnnrTexture());
            block.SetTexture("_Eater_FX", GetComfortEnemyDarkTexture());
            block.SetTexture("_Emission", GetComfortEnemyDarkTexture());

            block.SetColor("_EmissionColor", Color.black);
            block.SetColor("_VeinColorAtZeroAlpha", color);
            block.SetColor("_VeinColor", color);
            block.SetColor("_VeinColorExplode", color);
            block.SetColor("_FresnelColor", color);
            block.SetColor("_ColorEye", color);
            block.SetColor("_ColorEyeSpecial", color);

            block.SetFloat("_IS_SPECIAL", 0f);
            block.SetFloat("_HighlightStrength", 0f);
            block.SetFloat("_HighlightStrength1", 0f);
            block.SetFloat("_IceGlowIntensity", 0f);
            block.SetFloat("_VeinStrength", 0f);
            block.SetFloat("_FresnelIntensity", 0f);
            block.SetFloat("_Explosion", 0f);
            block.SetFloat("_StrengthEmmisive", 0f);
            block.SetFloat("_OveralStrenth", 0f);
            block.SetFloat("_CrawlerOrEater", 0f);
            block.SetFloat("_DeathAnim", 0f);
            block.SetFloat("_VSpecularIntensity", 0f);
            block.SetFloat("_VSpec", 0f);
            block.SetFloat("_Metallic", 0f);
            block.SetFloat("_Smoothness", 0.16f);
            block.SetFloat("_Glossiness", 0.16f);
            block.SetFloat("_BumpScale", 0f);
            block.SetFloat("_DetailNormalMapScale", 0f);
            block.SetFloat("_OcclusionStrength", 0f);
            block.SetFloat("_Parallax", 0f);
            block.SetFloat("_ParallaxAmount", 0f);
            block.SetFloat("_MinimumHighlightIntensity", 0f);
            block.SetFloat("_EmissionFogDistanceInfluence", 0f);
            block.SetFloat("_EaterFXStrength", 0f);
            block.SetFloat("_EaterFxStrength", 0f);
        }

        private static void SetMaterialColorIfPresent(Material material, string propertyName, Color color)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, color);
            }
        }

        private static void SetMaterialFloatIfPresent(Material material, string propertyName, float value)
        {
            if (material != null && material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void SetMaterialTextureIfPresent(Material material, string propertyName, Texture texture)
        {
            if (material != null && texture != null && material.HasProperty(propertyName))
            {
                material.SetTexture(propertyName, texture);
            }
        }

        private static void ApplyComfortShaderPropertyOverrides(object zombieSkinRenderingModule, MaterialPropertyBlock block, Color color)
        {
            if (block == null || zombieSkinRenderingModule == null)
            {
                return;
            }

            if (TryReadShaderPropertyId(zombieSkinRenderingModule, "dummyColorShaderProperty", out var dummyColorProperty))
            {
                block.SetColor(dummyColorProperty, color);
            }

            if (TryReadShaderPropertyId(zombieSkinRenderingModule, "bloodOnnrShaderProperty", out var bloodOnnrProperty))
            {
                block.SetTexture(bloodOnnrProperty, GetComfortEnemyOnnrTexture());
            }
        }

        private static bool TryReadShaderPropertyId(object target, string propertyName, out int propertyId)
        {
            propertyId = 0;
            if (target == null)
            {
                return false;
            }

            var type = target.GetType();
            var cacheKey = type.FullName + "." + propertyName;
            if (_shaderPropertyIdsByTypeAndName.TryGetValue(cacheKey, out propertyId))
            {
                return propertyId != 0;
            }

            if (!TryReadInstancePropertyQuiet(target, propertyName, out var raw) || raw == null)
            {
                raw = ReadStaticProperty(type, propertyName);
            }

            if (raw == null)
            {
                propertyId = 0;
                return false;
            }

            try
            {
                propertyId = Convert.ToInt32(raw);
                if (propertyId == 0)
                {
                    return false;
                }

                _shaderPropertyIdsByTypeAndName[cacheKey] = propertyId;
                return true;
            }
            catch
            {
                propertyId = 0;
                return false;
            }
        }

        private static Texture2D GetComfortEnemyAlbedoTexture()
        {
            if (_comfortEnemyAlbedoTexture != null)
            {
                return _comfortEnemyAlbedoTexture;
            }

            _comfortEnemyAlbedoTexture = CreateSolidTexture("AfterTheFallVRModKit_ComfortAlbedo", GetAliveComfortColor(), false);
            return _comfortEnemyAlbedoTexture;
        }

        private static Texture2D GetComfortEnemyDarkTexture()
        {
            if (_comfortEnemyDarkTexture != null)
            {
                return _comfortEnemyDarkTexture;
            }

            _comfortEnemyDarkTexture = CreateSolidTexture("AfterTheFallVRModKit_ComfortDark", new Color(0f, 0f, 0f, 1f), false);
            return _comfortEnemyDarkTexture;
        }

        private static Texture2D GetComfortEnemyImpactTexture()
        {
            if (_comfortEnemyImpactTexture != null)
            {
                return _comfortEnemyImpactTexture;
            }

            _comfortEnemyImpactTexture = CreateRainbowTexture("AfterTheFallVRModKit_ComfortImpactRainbow");
            return _comfortEnemyImpactTexture;
        }

        private static Texture2D GetComfortEnemyOnnrTexture()
        {
            if (_comfortEnemyOnnrTexture != null)
            {
                return _comfortEnemyOnnrTexture;
            }

            _comfortEnemyOnnrTexture = CreateSolidTexture("AfterTheFallVRModKit_ComfortONNR", new Color(0.5f, 0.5f, 1f, 1f), true);
            return _comfortEnemyOnnrTexture;
        }

        private static Texture2DArray GetComfortEnemyAlbedoTextureArray()
        {
            if (_comfortEnemyAlbedoTextureArray != null)
            {
                return _comfortEnemyAlbedoTextureArray;
            }

            _comfortEnemyAlbedoTextureArray = CreateSolidTextureArray("AfterTheFallVRModKit_ComfortAlbedoArray", GetAliveComfortColor());
            return _comfortEnemyAlbedoTextureArray;
        }

        private static Texture2DArray GetComfortEnemyOnnrTextureArray()
        {
            if (_comfortEnemyOnnrTextureArray != null)
            {
                return _comfortEnemyOnnrTextureArray;
            }

            _comfortEnemyOnnrTextureArray = CreateSolidTextureArray("AfterTheFallVRModKit_ComfortONNRArray", new Color(0.5f, 0.5f, 1f, 1f));
            return _comfortEnemyOnnrTextureArray;
        }

        private static Texture2DArray CreateSolidTextureArray(string name, Color color)
        {
            var texture = new Texture2DArray(1, 1, 32, TextureFormat.RGBA32, false);
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new[] { color };
            for (var i = 0; i < 32; i++)
            {
                texture.SetPixels(pixels, i);
            }

            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateSolidTexture(string name, Color color, bool linear)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, linear);
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private static Texture2D CreateRainbowTexture(string name)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false, false);
            texture.name = name;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var palette = new[]
            {
                new Color(0.1f, 1f, 0.35f, 1f),
                new Color(0.1f, 0.95f, 1f, 1f),
                new Color(0.2f, 0.45f, 1f, 1f),
                new Color(0.85f, 0.35f, 1f, 1f),
                new Color(1f, 0.95f, 0.1f, 1f)
            };

            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    texture.SetPixel(x, y, palette[(x + y) % palette.Length]);
                }
            }

            texture.Apply(false, true);
            return texture;
        }

        private static void LogComfortBoneDiagnostics(Transform searchRoot, int matchedParts, List<Transform> transforms)
        {
            var key = searchRoot == null ? "<null>" : searchRoot.name;
            if (!_comfortEnemyDiagnosticsLogged.Add(key))
            {
                return;
            }

            var names = transforms
                .Where(transform => transform != null)
                .Select(transform => transform.name)
                .Take(60)
                .ToArray();

            _log.LogWarning("Nephew Mode found only " + matchedParts + " humanoid bone-name matches under " + key + ". Sample descendants: " + string.Join(", ", names));
        }

        private static void LogComfortFallbackOnce(string sourceName, string message)
        {
            if (_comfortEnemyFallbackLogged.Add(sourceName))
            {
                _log.LogWarning(message);
            }
        }

        private static void LogMatrixDiagnosticsOnce(string sourceName, string message)
        {
            var key = sourceName + "|" + message;
            if (_comfortEnemyMatrixLogged.Add(key))
            {
                _log.LogWarning("Nephew Mode matrix path for " + sourceName + ": " + message);
            }
        }

        private static void PostComfortEnemyStateDiagnostic(object __instance, MethodBase __originalMethod)
        {
            try
            {
                if (!_comfortEnemyDiagnostics)
                {
                    return;
                }

                var methodName = __originalMethod == null
                    ? "<unknown>"
                    : ((__originalMethod.DeclaringType == null ? "<unknown>" : __originalMethod.DeclaringType.FullName) + "." + __originalMethod.Name);
                var state = DescribeComfortEnemyState(__instance);
                var key = methodName + "|" + GetDiagnosticObjectKey(__instance) + "|" + state;
                if (_comfortEnemyStateLogged.Add(key))
                {
                    _log.LogInfo("ComfortEnemyDiagnostics " + methodName + ": " + state);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("Nephew Mode state diagnostic failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string DescribeComfortEnemyState(object target)
        {
            var details = new List<string>
            {
                "target=" + DescribeObject(target) + "#" + GetDiagnosticObjectKey(target)
            };

            AddDiagnosticProperty(details, target, "IsCrawling");
            AddDiagnosticProperty(details, target, "ShouldCrawl");
            AddDiagnosticProperty(details, target, "CanCrawl");
            AddDiagnosticProperty(details, target, "IsOnFeet");
            AddDiagnosticProperty(details, target, "IsDead");
            AddDiagnosticProperty(details, target, "Health");
            AddDiagnosticProperty(details, target, "HealthFraction");
            AddDiagnosticProperty(details, target, "Part");
            AddDiagnosticProperty(details, target, "IsMutilated");
            AddDiagnosticProperty(details, target, "IsSevered");
            AddDiagnosticProperty(details, target, "HasLegSevered");
            AddDiagnosticProperty(details, target, "HasLeftArmSevered");
            AddDiagnosticProperty(details, target, "HasRightArmSevered");
            AddDiagnosticProperty(details, target, "CurrentAnimationSet");
            AddDiagnosticProperty(details, target, "MirrorMovementAnimation");
            AddDiagnosticProperty(details, target, "State");
            AddDiagnosticProperty(details, target, "PhysicsState");
            AddDiagnosticProperty(details, target, "IsIKEnabled");
            AddDiagnosticProperty(details, target, "IsFootPlacementEnabled");
            AddDiagnosticProperty(details, target, "CurrentLodLevel");
            AddDiagnosticProperty(details, target, "BloodPaintingEnabled");
            AddDiagnosticProperty(details, target, "HeadBone");
            AddDiagnosticProperty(details, target, "HipsBone");
            AddDiagnosticProperty(details, target, "ChestBone");
            AddDiagnosticProperty(details, target, "DefaultHitBone");
            AddDiagnosticProperty(details, target, "HasPhysicsRig");
            AddDiagnosticProperty(details, target, "IsPhysicsRigActive");
            AddDiagnosticProperty(details, target, "RigScale");

            AddNestedDiagnosticProperties(details, target, "MutilatableHealth", "health");
            AddNestedDiagnosticProperties(details, target, "mutilatableHealth", "health");
            AddNestedDiagnosticProperties(details, target, "mutilationHealth", "health");
            AddNestedDiagnosticProperties(details, target, "crawlMovementModule", "crawl");
            AddNestedDiagnosticProperties(details, target, "bipedalModule", "bipedal");
            AddNestedDiagnosticProperties(details, target, "animationPhysicsBlender", "physics");
            AddNestedDiagnosticProperties(details, target, "AnimationPhysicsBlender", "physics");

            return string.Join(", ", details);
        }

        private static void AddNestedDiagnosticProperties(List<string> details, object target, string propertyName, string labelPrefix)
        {
            if (!TryReadInstancePropertyQuiet(target, propertyName, out var nested) || nested == null)
            {
                return;
            }

            AddDiagnosticProperty(details, nested, "IsCrawling", labelPrefix + ".IsCrawling");
            AddDiagnosticProperty(details, nested, "IsOnFeet", labelPrefix + ".IsOnFeet");
            AddDiagnosticProperty(details, nested, "IsDead", labelPrefix + ".IsDead");
            AddDiagnosticProperty(details, nested, "Health", labelPrefix + ".Health");
            AddDiagnosticProperty(details, nested, "HealthFraction", labelPrefix + ".HealthFraction");
            AddDiagnosticProperty(details, nested, "HasLegSevered", labelPrefix + ".HasLegSevered");
            AddDiagnosticProperty(details, nested, "HasLeftArmSevered", labelPrefix + ".HasLeftArmSevered");
            AddDiagnosticProperty(details, nested, "HasRightArmSevered", labelPrefix + ".HasRightArmSevered");
            AddDiagnosticProperty(details, nested, "State", labelPrefix + ".State");
            AddDiagnosticProperty(details, nested, "PhysicsState", labelPrefix + ".PhysicsState");
            AddDiagnosticProperty(details, nested, "IsSleeping", labelPrefix + ".IsSleeping");
            AddDiagnosticProperty(details, nested, "UsesTransformBones", labelPrefix + ".UsesTransformBones");
        }

        private static void AddDiagnosticProperty(List<string> details, object target, string propertyName)
        {
            AddDiagnosticProperty(details, target, propertyName, propertyName);
        }

        private static void AddDiagnosticProperty(List<string> details, object target, string propertyName, string label)
        {
            if (TryReadInstancePropertyQuiet(target, propertyName, out var value))
            {
                details.Add(label + "=" + FormatDiagnosticValue(value));
            }
        }

        private static string FormatDiagnosticValue(object value)
        {
            if (value == null)
            {
                return "<null>";
            }

            if (value is float floatValue)
            {
                return floatValue.ToString("0.###");
            }

            if (value is double doubleValue)
            {
                return doubleValue.ToString("0.###");
            }

            return value.ToString();
        }

        private static string GetDiagnosticObjectKey(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is UnityEngine.Object unityObject)
            {
                return unityObject.GetInstanceID().ToString();
            }

            return value.GetHashCode().ToString();
        }

        private static Transform FindBestHumanoidSearchRoot(Transform anchor)
        {
            var best = anchor;
            var bestScore = -1;

            for (var current = anchor; current != null; current = current.parent)
            {
                var score = ScoreHumanoidNames(current, 500);
                if (score > bestScore)
                {
                    best = current;
                    bestScore = score;
                }

                if (score >= 9)
                {
                    break;
                }
            }

            return best;
        }

        private static int ScoreHumanoidNames(Transform root, int maxNodes)
        {
            var score = 0;
            var transforms = CollectDescendants(root, maxNodes);
            foreach (var transform in transforms)
            {
                var name = NormalizeTransformName(transform.name);
                if (IsHeadName(name) || IsChestName(name) || IsHipsName(name) || IsUpperArmName(name) || IsForeArmName(name) || IsHandName(name) || IsUpperLegName(name) || IsLowerLegName(name))
                {
                    score++;
                }
            }

            return score;
        }

        private static List<Transform> CollectDescendants(Transform root, int maxNodes)
        {
            var result = new List<Transform>();
            CollectDescendants(root, result, maxNodes);
            return result;
        }

        private static void CollectDescendants(Transform root, List<Transform> result, int maxNodes)
        {
            if (root == null || result.Count >= maxNodes)
            {
                return;
            }

            if (IsComfortProxyTransform(root))
            {
                return;
            }

            result.Add(root);
            for (var i = 0; i < root.childCount && result.Count < maxNodes; i++)
            {
                var child = root.GetChild(i);
                if (!IsComfortProxyTransform(child))
                {
                    CollectDescendants(child, result, maxNodes);
                }
            }
        }

        private static Transform FindNamedTransform(IEnumerable<Transform> transforms, Func<string, bool> predicate)
        {
            foreach (var transform in transforms)
            {
                if (transform != null && !IsComfortProxyTransform(transform) && predicate(NormalizeTransformName(transform.name)))
                {
                    return transform;
                }
            }

            return null;
        }

        private static bool IsComfortProxyTransform(Transform transform)
        {
            return transform != null && !string.IsNullOrEmpty(transform.name) && transform.name.StartsWith(ComfortEnemyProxyName, StringComparison.Ordinal);
        }

        private static string NormalizeTransformName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.ToLowerInvariant()
                .Replace("_", " ")
                .Replace(".", " ")
                .Replace("-", " ")
                .Replace(":", " ");
        }

        private static bool IsSideName(string name, bool left)
        {
            var padded = " " + name + " ";
            if (left)
            {
                return name.Contains("left") || padded.Contains(" l ");
            }

            return name.Contains("right") || padded.Contains(" r ");
        }

        private static bool IsHeadName(string name)
        {
            return name.Contains("head") && !name.Contains("headend") && !name.Contains("head end");
        }

        private static bool IsChestName(string name)
        {
            return name.Contains("chest") || name.Contains("spine2") || name.Contains("spine 2") || name.Contains("spine1") || name.Contains("spine 1") || name.Contains("spine");
        }

        private static bool IsHipsName(string name)
        {
            return name.Contains("hips") || name.Contains("hip") || name.Contains("pelvis");
        }

        private static bool IsUpperArmName(string name)
        {
            return name.Contains("upperarm") || name.Contains("upper arm") || (name.Contains("arm") && !name.Contains("fore") && !name.Contains("lower") && !name.Contains("hand"));
        }

        private static bool IsForeArmName(string name)
        {
            return name.Contains("forearm") || name.Contains("fore arm") || name.Contains("lowerarm") || name.Contains("lower arm");
        }

        private static bool IsHandName(string name)
        {
            return name.Contains("hand") && !name.Contains("finger");
        }

        private static bool IsUpperLegName(string name)
        {
            return name.Contains("upperleg") || name.Contains("upper leg") || name.Contains("thigh") || (name.Contains("leg") && !name.Contains("lower") && !name.Contains("calf") && !name.Contains("foot"));
        }

        private static bool IsLowerLegName(string name)
        {
            return name.Contains("lowerleg") || name.Contains("lower leg") || name.Contains("calf") || name.Contains("shin");
        }

        private static int CountNonNull(params Transform[] transforms)
        {
            var count = 0;
            foreach (var transform in transforms)
            {
                if (transform != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static void BuildFallbackComfortEnemyDummy(Transform parent, Vector3 sourceSize, int layer)
        {
            var height = ComfortVisualHeight(sourceSize);
            var width = ComfortVisualWidth(sourceSize);
            var material = GetComfortEnemyMaterial();

            var torsoLength = height * 0.46f;
            var torsoDiameter = ClampFloat(width * 0.42f, 0.22f, 0.42f);
            var limbDiameter = ClampFloat(width * 0.16f, 0.08f, 0.16f);
            var headDiameter = ClampFloat(height * 0.18f, 0.22f, 0.36f);
            var armLength = height * 0.42f;
            var legLength = height * 0.46f;
            var shoulderX = width * 0.33f;
            var handX = width * 0.48f;
            var hipX = width * 0.18f;

            CreateComfortPrimitive(parent,
                "Head",
                PrimitiveType.Sphere,
                new Vector3(0f, height * 0.38f, 0f),
                Quaternion.identity,
                new Vector3(headDiameter, headDiameter, headDiameter),
                material,
                layer);

            CreateComfortPrimitive(parent,
                "Torso",
                PrimitiveType.Capsule,
                new Vector3(0f, height * 0.05f, 0f),
                Quaternion.identity,
                new Vector3(torsoDiameter, torsoLength * 0.5f, torsoDiameter),
                material,
                layer);

            CreateComfortPrimitive(parent,
                "Hips",
                PrimitiveType.Cube,
                new Vector3(0f, -height * 0.23f, 0f),
                Quaternion.identity,
                new Vector3(width * 0.42f, height * 0.08f, width * 0.28f),
                material,
                layer);

            CreateComfortPrimitive(parent,
                "LeftArm",
                PrimitiveType.Capsule,
                new Vector3(-shoulderX, height * 0.02f, 0f),
                Quaternion.Euler(0f, 0f, -24f),
                new Vector3(limbDiameter, armLength * 0.5f, limbDiameter),
                material,
                layer);

            CreateComfortPrimitive(parent,
                "RightArm",
                PrimitiveType.Capsule,
                new Vector3(shoulderX, height * 0.02f, 0f),
                Quaternion.Euler(0f, 0f, 24f),
                new Vector3(limbDiameter, armLength * 0.5f, limbDiameter),
                material,
                layer);

            CreateComfortPrimitive(parent,
                "LeftLeg",
                PrimitiveType.Capsule,
                new Vector3(-hipX, -height * 0.55f, 0f),
                Quaternion.Euler(0f, 0f, 6f),
                new Vector3(limbDiameter, legLength * 0.5f, limbDiameter),
                material,
                layer);

            CreateComfortPrimitive(parent,
                "RightLeg",
                PrimitiveType.Capsule,
                new Vector3(hipX, -height * 0.55f, 0f),
                Quaternion.Euler(0f, 0f, -6f),
                new Vector3(limbDiameter, legLength * 0.5f, limbDiameter),
                material,
                layer);

            var jointDiameter = limbDiameter * 1.35f;
            CreateComfortPrimitive(parent,
                "LeftHand",
                PrimitiveType.Sphere,
                new Vector3(-handX, -height * 0.18f, 0f),
                Quaternion.identity,
                new Vector3(jointDiameter, jointDiameter, jointDiameter),
                material,
                layer);

            CreateComfortPrimitive(parent,
                "RightHand",
                PrimitiveType.Sphere,
                new Vector3(handX, -height * 0.18f, 0f),
                Quaternion.identity,
                new Vector3(jointDiameter, jointDiameter, jointDiameter),
                material,
                layer);
        }

        private static void UpdateFallbackComfortPose(Transform proxy)
        {
            if (proxy == null || proxy.childCount == 0)
            {
                return;
            }

            var t = Time.time * 5.5f;
            var swing = (float)Math.Sin(t) * 24f;
            var smallSwing = (float)Math.Sin(t + Math.PI) * 14f;

            SetChildLocalRotation(proxy, "LeftArm", Quaternion.Euler(swing, 0f, -24f));
            SetChildLocalRotation(proxy, "RightArm", Quaternion.Euler(-swing, 0f, 24f));
            SetChildLocalRotation(proxy, "LeftLeg", Quaternion.Euler(-smallSwing, 0f, 6f));
            SetChildLocalRotation(proxy, "RightLeg", Quaternion.Euler(smallSwing, 0f, -6f));
        }

        private static void SetChildLocalRotation(Transform parent, string childName, Quaternion rotation)
        {
            var child = FindDirectChild(parent, childName);
            if (child != null)
            {
                child.localRotation = rotation;
            }
        }

        private static GameObject CreateComfortPrimitive(Transform parent, string name, PrimitiveType primitiveType, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material, int layer)
        {
            var part = GameObject.CreatePrimitive(primitiveType);
            part.name = name;
            part.layer = layer;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = ScaledForParent(parent, localScale);

            var collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }

            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return part;
        }

        private static Vector3 ScaledForParent(Transform parent, Vector3 worldScale)
        {
            var scale = parent.lossyScale;
            return new Vector3(
                worldScale.x * SafeInverseScale(scale.x),
                worldScale.y * SafeInverseScale(scale.y),
                worldScale.z * SafeInverseScale(scale.z));
        }

        private static float ComfortVisualHeight(Vector3 sourceSize)
        {
            return ClampFloat(sourceSize.y * 0.58f, 0.75f, 1.35f);
        }

        private static float ComfortVisualWidth(Vector3 sourceSize)
        {
            return ClampFloat(Math.Max(sourceSize.x, sourceSize.z) * 0.82f, 0.28f, 0.68f);
        }

        private static Vector3 InverseLossyScale(Transform transform)
        {
            var scale = transform.lossyScale;
            return new Vector3(SafeInverseScale(scale.x), SafeInverseScale(scale.y), SafeInverseScale(scale.z));
        }

        private static float SafeInverseScale(float value)
        {
            var magnitude = Math.Abs(value);
            return magnitude < 0.001f ? 1f : 1f / magnitude;
        }

        private static float ClampFloat(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return min;
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child != null && string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static Transform FindDescendantByName(Transform parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null || IsComfortProxyTransform(child))
                {
                    continue;
                }

                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }

                var nested = FindDescendantByName(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static Material GetComfortEnemyMaterial()
        {
            if (_comfortEnemyMaterial != null)
            {
                return _comfortEnemyMaterial;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            _comfortEnemyMaterial = shader == null
                ? new Material(Shader.Find("Sprites/Default"))
                : new Material(shader);
            _comfortEnemyMaterial.name = "AfterTheFallVRModKit_ComfortEnemyMaterial";
            _comfortEnemyMaterial.color = GetAliveComfortColor();
            _comfortEnemyMaterial.SetFloat("_Metallic", 0f);
            _comfortEnemyMaterial.SetFloat("_Smoothness", 0.16f);

            return _comfortEnemyMaterial;
        }

        private sealed class MatrixComfortPose
        {
            public Vector3 Head;
            public Vector3 Chest;
            public Vector3 Hips;
            public Vector3 LeftHand;
            public Vector3 RightHand;
            public Vector3 LeftFoot;
            public Vector3 RightFoot;
            public float Height;
            public float Width;
        }

        private sealed class MatrixPosePoint
        {
            public readonly Vector3 World;
            public readonly Vector3 Local;

            public MatrixPosePoint(Vector3 world, Vector3 local)
            {
                World = world;
                Local = local;
            }
        }

        private sealed class ApproxVector3Comparer : IEqualityComparer<Vector3>
        {
            public bool Equals(Vector3 left, Vector3 right)
            {
                return Quantize(left.x) == Quantize(right.x)
                    && Quantize(left.y) == Quantize(right.y)
                    && Quantize(left.z) == Quantize(right.z);
            }

            public int GetHashCode(Vector3 value)
            {
                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + Quantize(value.x);
                    hash = (hash * 31) + Quantize(value.y);
                    hash = (hash * 31) + Quantize(value.z);
                    return hash;
                }
            }

            private static int Quantize(float value)
            {
                return (int)Math.Round(value * 100f);
            }
        }

        private static void PostSceneLoadingFinishedCleanup(object __instance, MethodBase __originalMethod)
        {
            try
            {
                if (!_cleanupRetainedServerGame)
                {
                    return;
                }

                TryCleanupServerGameLeak(__instance, Describe(__originalMethod));
            }
            catch (Exception ex)
            {
                _log.LogWarning("ServerGame cleanup postfix failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void TryCleanupServerGameLeak(object sceneManager, string reason)
        {
            if (sceneManager == null)
            {
                return;
            }

            if (!IsHubSceneManager(sceneManager, out var activeSceneName))
            {
                return;
            }

            var now = DateTime.UtcNow;
            var secondsSinceLastCleanup = now - _lastServerGameCleanupUtc;
            if (secondsSinceLastCleanup < ServerGameCleanupThrottle)
            {
                _log.LogInfo("ServerGame cleanup skipped; hub cleanup ran " + (int)secondsSinceLastCleanup.TotalSeconds + "s ago.");
                return;
            }

            var serverGameType = FindLoadedType("Vertigo.Snowbreed.Server.ServerGame");
            if (serverGameType == null)
            {
                _log.LogWarning("ServerGame cleanup could not find Vertigo.Snowbreed.Server.ServerGame.");
                return;
            }

            var existsWasRead = ReadStaticBoolProperty(serverGameType, "Exists", out var existsValue);
            if (existsWasRead && !existsValue)
            {
                _log.LogInfo("Hub scene load finished after " + reason + "; ActiveSceneName='" + activeSceneName + "', ServerGame.Exists=False.");
                return;
            }

            var exists = !existsWasRead || existsValue;
            var instance = ReadStaticProperty(serverGameType, "Instance");

            _log.LogInfo("Hub scene load finished after " + reason + "; ActiveSceneName='" + activeSceneName + "', ServerGame.Exists=" + exists + ", Instance=" + DescribeObject(instance) + ".");

            if (instance == null)
            {
                return;
            }

            _lastServerGameCleanupUtc = now;
            _serverGameCleanupAttempts++;

            if (instance == null)
            {
                _log.LogWarning("ServerGame cleanup saw Exists=true but Instance was null; skipping Dispose(false).");
                return;
            }

            if (InvokeDisposeFalse(instance))
            {
                _log.LogWarning("ServerGame cleanup attempt " + _serverGameCleanupAttempts + ": invoked ServerGame.Dispose(false) after returning to hub.");
                TryUnloadUnusedUnityAssets();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                _log.LogInfo("ServerGame cleanup attempt " + _serverGameCleanupAttempts + ": requested unused asset unload and managed GC.");
            }
            else
            {
                _log.LogWarning("ServerGame cleanup attempt " + _serverGameCleanupAttempts + ": could not invoke ServerGame.Dispose(false).");
            }
        }

        private static bool IsHubSceneManager(object sceneManager, out string activeSceneName)
        {
            activeSceneName = ReadInstanceStringProperty(sceneManager, "ActiveSceneName") ?? string.Empty;

            if (ReadInstanceBoolProperty(sceneManager, "IsActiveSceneHub", out var isActiveHub) && isActiveHub)
            {
                return true;
            }

            if (ReadInstanceBoolProperty(sceneManager, "IsActiveSceneOrSceneBeingLoadedHub", out var isActiveOrLoadingHub) && isActiveOrLoadingHub)
            {
                return true;
            }

            return activeSceneName.IndexOf("Hub", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool InvokeDisposeFalse(object instance)
        {
            try
            {
                var method = FindInstanceMethod(instance.GetType(), "Dispose", typeof(bool));
                if (method == null)
                {
                    return false;
                }

                method.Invoke(instance, new object[] { false });
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Dispose(false) invocation failed on " + DescribeObject(instance) + ": " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static void TryUnloadUnusedUnityAssets()
        {
            try
            {
                var resourcesType = FindLoadedType("UnityEngine.Resources");
                if (resourcesType == null)
                {
                    _log.LogWarning("Could not find UnityEngine.Resources for UnloadUnusedAssets().");
                    return;
                }

                var method = FindStaticMethod(resourcesType, "UnloadUnusedAssets");
                if (method == null)
                {
                    _log.LogWarning("Could not find UnityEngine.Resources.UnloadUnusedAssets().");
                    return;
                }

                method.Invoke(null, null);
                _log.LogInfo("Requested UnityEngine.Resources.UnloadUnusedAssets().");
            }
            catch (Exception ex)
            {
                _log.LogWarning("UnloadUnusedAssets invocation failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool ReadInstanceBoolProperty(object target, string propertyName, out bool value)
        {
            value = false;
            var raw = ReadInstanceProperty(target, propertyName);
            if (raw is bool boolValue)
            {
                value = boolValue;
                return true;
            }

            return raw != null && bool.TryParse(raw.ToString(), out value);
        }

        private static bool ReadStaticBoolProperty(Type type, string propertyName, out bool value)
        {
            value = false;
            var raw = ReadStaticProperty(type, propertyName);
            if (raw is bool boolValue)
            {
                value = boolValue;
                return true;
            }

            return raw != null && bool.TryParse(raw.ToString(), out value);
        }

        private static string ReadInstanceStringProperty(object target, string propertyName)
        {
            var raw = ReadInstanceProperty(target, propertyName);
            return raw == null ? null : raw.ToString();
        }

        private static object ReadInstanceProperty(object target, string propertyName)
        {
            try
            {
                var property = FindProperty(target.GetType(), propertyName, InstanceFlags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(target);
                }

                var getter = FindInstanceMethod(target.GetType(), "get_" + propertyName);
                return getter == null ? null : getter.Invoke(target, null);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not read " + DescribeObject(target) + "." + propertyName + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static bool TryReadInstancePropertyQuiet(object target, string propertyName, out object value)
        {
            value = null;
            if (target == null)
            {
                return false;
            }

            try
            {
                var property = FindProperty(target.GetType(), propertyName, InstanceFlags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    value = property.GetValue(target);
                    return true;
                }

                var field = FindField(target.GetType(), propertyName, InstanceFlags);
                if (field != null)
                {
                    value = field.GetValue(target);
                    return true;
                }

                var getter = FindInstanceMethod(target.GetType(), "get_" + propertyName);
                if (getter == null)
                {
                    return false;
                }

                value = getter.Invoke(target, null);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        private static object ReadStaticProperty(Type type, string propertyName)
        {
            try
            {
                var property = FindProperty(type, propertyName, StaticFlags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(null);
                }

                var field = FindField(type, propertyName, StaticFlags);
                if (field != null)
                {
                    return field.GetValue(null);
                }

                var getter = FindStaticMethod(type, "get_" + propertyName);
                return getter == null ? null : getter.Invoke(null, null);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not read static " + type.FullName + "." + propertyName + ": " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static bool InvokeInstanceMethod(object target, string methodName, params object[] args)
        {
            try
            {
                var parameterTypes = args.Select(arg => arg == null ? typeof(object) : arg.GetType()).ToArray();
                var method = FindInstanceMethod(target.GetType(), methodName, parameterTypes);
                if (method == null)
                {
                    return false;
                }

                method.Invoke(target, args);
                return true;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not invoke " + DescribeObject(target) + "." + methodName + ": " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static PropertyInfo FindProperty(Type type, string propertyName, BindingFlags flags)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var property = current.GetProperty(propertyName, flags);
                if (property != null)
                {
                    return property;
                }
            }

            return null;
        }

        private static FieldInfo FindField(Type type, string fieldName, BindingFlags flags)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(fieldName, flags);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static MethodInfo FindInstanceMethod(Type type, string methodName, params Type[] parameterTypes)
        {
            return FindMethod(type, methodName, InstanceFlags, parameterTypes);
        }

        private static MethodInfo FindStaticMethod(Type type, string methodName, params Type[] parameterTypes)
        {
            return FindMethod(type, methodName, StaticFlags, parameterTypes);
        }

        private static MethodInfo FindMethod(Type type, string methodName, BindingFlags flags, params Type[] parameterTypes)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var methods = current.GetMethods(flags)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal));

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != parameterTypes.Length)
                    {
                        continue;
                    }

                    var matches = true;
                    for (var i = 0; i < parameters.Length; i++)
                    {
                        if (parameters[i].ParameterType != parameterTypes[i])
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                    {
                        return method;
                    }
                }
            }

            return null;
        }

        private static string Describe(MethodBase method)
        {
            var parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name));
            var declaringType = method.DeclaringType == null ? "<unknown>" : method.DeclaringType.FullName;
            return declaringType + "." + method.Name + "(" + parameters + ")";
        }

        private static string DescribeObject(object value)
        {
            return value == null ? "<null>" : value.GetType().FullName;
        }
    }
}
