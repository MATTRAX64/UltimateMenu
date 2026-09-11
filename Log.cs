using BepInEx;using BepInEx.Logging;using HarmonyLib;using UltimateMenu;using System;using System.Linq;using System.Reflection;using UnityEngine;
namespace UltimateMenu
{
    [BepInPlugin("com.mattrax.ultimatemenu", "UltimateMenu", "1.5")]
    public class Log : BaseUnityPlugin
    {
        public static Log Instance { get; private set; }
        static ManualLogSource _log; Harmony harmony;
        public static string PluginName, PluginVersion, PluginGuid;
        void Awake()
        {
            Instance = this; _log = Logger;
            var meta = MetadataHelper.GetMetadata(this);
            PluginName = meta.Name; PluginVersion = meta.Version.ToString(); PluginGuid = meta.GUID;
            (harmony = new Harmony(PluginGuid)).PatchAll();
            try { ModConfig.Load(); } catch (Exception e) { Err("Config", e); }
            foreach (var t in Assembly.GetExecutingAssembly().GetTypes().Where(t => t.Namespace == typeof(Log).Namespace && t.IsClass && !t.IsAbstract && typeof(MonoBehaviour).IsAssignableFrom(t) && t != typeof(Log)))
                try { gameObject.AddComponent(t); } catch (Exception e) { Err($"Skipping Mod - {t.Name}", e); }
        }
        void Update() { if (ModConfig.HasPending) ModConfig.ApplyPendingImport(); }
        void OnDestroy() { ModConfig.Save(); harmony?.UnpatchSelf(); }
        public static void Info(string m) => _log?.LogInfo(m);
        public static void Err(string m, Exception e) => _log?.LogError($"[ERR] {m} : {e.Message}\n{e.StackTrace}");
    }
}