using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace AfterTheFallVRModKit.Plugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class AfterTheFallVRModKitPlugin : BasePlugin
    {
        public const string PluginGuid = "local.afterthefall.vrmodkit";
        public const string PluginName = "After The Fall VR Mod Kit";
        public const string PluginVersion = "0.5.0";

        private static readonly BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly TimeSpan ServerGameCleanupThrottle = TimeSpan.FromSeconds(15);

        private static ManualLogSource _log;
        private static DateTime _lastServerGameCleanupUtc = DateTime.MinValue;
        private static int _serverGameCleanupAttempts;
        private static bool _cleanupRetainedServerGame = true;
        private Harmony _harmony;

        public override void Load()
        {
            _log = Log;
            _harmony = new Harmony(PluginGuid);

            var disableInGameVoip = BindFeature("DisableInGameVoip", true, "Disable After The Fall's built-in VOIP handlers. Leave this on when using Discord or another voice chat.");
            var suppressClientBloodAndGore = BindFeature("SuppressClientBloodAndGore", true, "Skip client-side blood, decal, gib, and mutilation visual handlers.");
            _cleanupRetainedServerGame = BindFeature("CleanupRetainedServerGame", true, "After returning to the hub, dispose a retained ServerGame instance if the game leaves one in memory.");

            Log.LogInfo(PluginName + " " + PluginVersion + " loading. Features: DisableInGameVoip=" + disableInGameVoip + ", SuppressClientBloodAndGore=" + suppressClientBloodAndGore + ", CleanupRetainedServerGame=" + _cleanupRetainedServerGame + ".");
            Log.LogInfo("Gameplay networking and damage handling are left untouched.");

            if (disableInGameVoip)
            {
                PatchVoipSuppression();
            }
            else
            {
                Log.LogInfo("DisableInGameVoip is off; VOIP methods were not patched.");
            }

            if (suppressClientBloodAndGore)
            {
                PatchClientBloodAndGoreSuppression();
            }
            else
            {
                Log.LogInfo("SuppressClientBloodAndGore is off; blood/gore visual methods were not patched.");
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
            PatchAllNamedMethods("Vertigo.Snowbreed.Zombies.ZombieSkinHitImpactModule",
                "HandleZombieHitEvent",
                "OnImpact",
                "OnHitImpact",
                "ApplyMutilationEffect");

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

            PatchAllNamedMethods("Vertigo.Snowbreed.Zombies.ZombieBloodMaskPainter",
                "PaintBlood");

            PatchAllNamedMethods("Vertigo.Snowbreed.ClientEnemyNetworking",
                "HandleEnemyGibNetworkMessage");
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

        private static object ReadStaticProperty(Type type, string propertyName)
        {
            try
            {
                var property = FindProperty(type, propertyName, StaticFlags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    return property.GetValue(null);
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
