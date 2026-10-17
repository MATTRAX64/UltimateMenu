using BepInEx;using BepInEx.Logging;using HarmonyLib;using UltimateMenu;using System;using System.Linq;using System.Reflection;using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
namespace UltimateMenu
{
    [BepInPlugin("com.mattrax.ultimatemenu", "UltimateMenu", "1.7.114")]
    public class Log : BaseUnityPlugin
    {
        public static Log Instance { get; private set; }
        static ManualLogSource _log; Harmony harmony;
        public static string PluginName, PluginVersion, PluginGuid;
        public static string ReleaseStatusKey { get; private set; } = "Release.Checking";
        public static string PublishedVersion { get; private set; } = "";
        public const string ReleasesUrl = "https://github.com/MATTRAX64/UltimateMenu/releases";
        [Serializable] sealed class ReleaseInfo { public string tag_name = ""; public ReleaseAsset[] assets; }
        [Serializable] sealed class ReleaseAsset { public string name, browser_download_url; public int size; }
        static ReleaseAsset _releaseAsset;
        static Version _releaseVersion;
        public static string DownloadStatusKey { get; private set; } = "";
        public static bool CanDownload => ReleaseStatusKey == "Release.Update" && _releaseAsset != null && DownloadStatusKey != "Release.Downloading" && DownloadStatusKey != "Release.Restart";
        void Awake()
        {
            Instance = this; _log = Logger;
            var meta = MetadataHelper.GetMetadata(this);
            PluginName = meta.Name; PluginVersion = meta.Version.ToString(); PluginGuid = meta.GUID;
            try { ReleaseUpdater.EnsurePluginFolder(); } catch (Exception e) { Err("Plugin folder", e); }
            try { ReleaseUpdater.RetireOlderCopies(); } catch (Exception e) { Err("Older plugin copies", e); }
            try { if (ReleaseUpdater.ResumePending()) DownloadStatusKey = "Release.Restart"; }
            catch (Exception e) { DownloadStatusKey = "Release.DownloadFailed"; Err("Pending update", e); }
            try { MenuPrefs.Initialize(); } catch (Exception e) { Err("Profile init", e); }
            gameObject.AddComponent<CrashReporter>();
            try { (harmony = new Harmony(PluginGuid)).PatchAll(); } catch (Exception e) { Err("Optional patches", e); }
            try { ModConfig.Load(); } catch (Exception e) { Err("Config", e); }
            // An unavailable optional runtime must not prevent the menu from starting.
            Type[] modules;
            try { modules = Assembly.GetExecutingAssembly().GetTypes(); }
            catch (ReflectionTypeLoadException e)
            {
                modules = e.Types.Where(t => t != null).ToArray();
                foreach (var failure in e.LoaderExceptions) if (failure != null) Err("Optional module dependency", failure);
            }
            try { gameObject.AddComponent<MenuUI>(); } catch (Exception e) { Err("Menu startup", e); }
            foreach (var t in modules.Where(t => t.Namespace == typeof(Log).Namespace && t.IsClass && !t.IsAbstract && typeof(MonoBehaviour).IsAssignableFrom(t) && t != typeof(Log) && t != typeof(MenuUI) && t != typeof(CrashReporter) && t != typeof(GorillaInterface) && t != typeof(Notif)))
                try { gameObject.AddComponent(t); } catch (Exception e) { Err($"Skipping Mod - {t.Name}", e); }
        }
        void Update() { if (ModConfig.HasPending) ModConfig.ApplyPendingImport(); MenuPrefs.Tick(); }
        void Start() { StartCoroutine(CheckRelease()); StartCoroutine(ShowStartupNotification()); }
        IEnumerator ShowStartupNotification()
        {
            yield return null;
            gameObject.AddComponent<Notif>();
            Info("Startup notification created");
        }
        IEnumerator CheckRelease()
        {
            using (var request = UnityWebRequest.Get("https://api.github.com/repos/MATTRAX64/UltimateMenu/releases/latest"))
            {
                request.timeout = 10;
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "UltimateMenu/" + PluginVersion);
                yield return request.SendWebRequest();
                ReleaseStatusKey = "Release.Unavailable";
                if (request.result != UnityWebRequest.Result.Success) yield break;
                try
                {
                    string json = request.downloadHandler.text;
                    if (json.Length > 131072) yield break;
                    var release = JsonUtility.FromJson<ReleaseInfo>(json);
                    if (release == null || !TryReleaseVersion(release.tag_name, out var published) ||
                        !TryReleaseVersion(PluginVersion, out var installed)) yield break;
                    PublishedVersion = published.ToString(3);
                    int comparison = installed.CompareTo(published);
                    ReleaseStatusKey = comparison > 0 ? "Release.Test" : comparison < 0 ? "Release.Update" : "Release.Current";
                    _releaseVersion = published;
                    _releaseAsset = release.assets?.FirstOrDefault(asset => asset != null &&
                        asset.name != null && asset.name.StartsWith("UltimateMenu", StringComparison.OrdinalIgnoreCase) &&
                        asset.name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && asset.size >= 1024 && asset.size <= ReleaseUpdater.MaxBytes &&
                        asset.browser_download_url != null && asset.browser_download_url.StartsWith("https://github.com/MATTRAX64/UltimateMenu/releases/download/", StringComparison.OrdinalIgnoreCase));
                }
                catch (Exception e) { Err("GitHub version check", e); }
            }
        }
        public static void DownloadUpdate()
        {
            if (Instance == null || !CanDownload) return;
            DownloadStatusKey = "Release.Downloading";
            Instance.StartCoroutine(DownloadRelease());
        }
        static IEnumerator DownloadRelease()
        {
            using (var request = UnityWebRequest.Get(_releaseAsset.browser_download_url))
            {
                request.timeout = 60;
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (request.downloadedBytes > ReleaseUpdater.MaxBytes) { request.Abort(); break; }
                    yield return null;
                }
                DownloadStatusKey = "Release.DownloadFailed";
                if (request.result != UnityWebRequest.Result.Success) yield break;
                try
                {
                    if (request.downloadHandler.data.Length != _releaseAsset.size) throw new System.IO.InvalidDataException("Incomplete release asset");
                    ReleaseUpdater.Stage(request.downloadHandler.data, _releaseVersion);
                    DownloadStatusKey = "Release.Restart";
                }
                catch (Exception e) { Err("Download update", e); }
            }
        }
        internal static bool TryReleaseVersion(string value, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            if (string.IsNullOrEmpty(value) || value.Length > 32) return false;
            string text = value[0] == 'v' || value[0] == 'V' ? value.Substring(1) : value;
            for (int i = 0; i < text.Length; i++)
                if ((text[i] < '0' || text[i] > '9') && text[i] != '.') return false;
            if (!Version.TryParse(text, out var parsed)) return false;
            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
            return true;
        }
        void OnDestroy() { ModConfig.Save(); harmony?.UnpatchSelf(); }
        public static void Info(string m) => _log?.LogInfo(m);
        public static void Err(string m, Exception e) { CrashReporter.Record(m,e);_log?.LogError($"[ERR] {m} : {e.Message}\n{e.StackTrace}"); }
    }
}
