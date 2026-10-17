using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace UltimateMenu
{
    public enum ThemePreset { SombreVert = 0, Clair = 1, SombreViolet = 2, SombreBleu = 3, Custom = 4 }

    public enum MenuCategory
    {
        Gameplay,
        Camera,
        Apparence,
        Technique,
        Social,
        Environnement,
        Experimental,
        Mocap,
        Stream,
        Cosmetique,
        Tracking
    }

    public static class SoundFX
    {
        public enum Sound { Aigue, Moyen, Grave }

        private static readonly Dictionary<Sound, AudioClip> _clips = new Dictionary<Sound, AudioClip>();
        private static AudioSource _source;
        private static bool _loaded;

        public static bool Enabled
        {
            get => MenuPrefs.GetInt("MUM_SoundEnabled", 1) == 1;
            set { MenuPrefs.SetInt("MUM_SoundEnabled", value ? 1 : 0); MenuPrefs.Save(); }
        }

        public static float Volume
        {
            get => MenuPrefs.GetFloat("MUM_SoundVolume", 0.5f);
            set { MenuPrefs.SetFloat("MUM_SoundVolume", Mathf.Clamp01(value)); MenuPrefs.Save(); }
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            var go = new GameObject("MUM_SoundFX");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            LoadClip(Sound.Aigue, "Assets.Menu.Sounds.Aigue.wav");
            LoadClip(Sound.Moyen, "Assets.Menu.Sounds.Moyen.wav");
            LoadClip(Sound.Grave, "Assets.Menu.Sounds.Grave.wav");
        }

        static void LoadClip(Sound key, string logicalNameSuffix)
        {
            try
            {
                var asm = typeof(SoundFX).Assembly;
                string resName = null;
                foreach (var n in asm.GetManifestResourceNames())
                {
                    if (n.EndsWith(logicalNameSuffix, System.StringComparison.OrdinalIgnoreCase))
                    { resName = n; break; }
                }
                if (resName == null)
                {
                    Log.Err("SoundFX.LoadClip", new System.Exception("Ressource introuvable: " + logicalNameSuffix));
                    return;
                }

                byte[] data;
                using (var stream = asm.GetManifestResourceStream(resName))
                using (var ms = new System.IO.MemoryStream())
                {
                    stream.CopyTo(ms);
                    data = ms.ToArray();
                }

                var clip = WavToAudioClip(data, key.ToString());
                if (clip != null) _clips[key] = clip;
            }
            catch (System.Exception e) { Log.Err("SoundFX.LoadClip", e); }
        }

        static AudioClip WavToAudioClip(byte[] wav, string name)
        {
            try
            {
                int pos = 12;
                int channels = 1, bitsPerSample = 16, sampleRate = 44100;
                byte[] dataChunk = null;

                while (pos + 8 <= wav.Length)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
                    int chunkSize = System.BitConverter.ToInt32(wav, pos + 4);
                    int chunkStart = pos + 8;

                    if (chunkId == "fmt ")
                    {
                        channels = System.BitConverter.ToInt16(wav, chunkStart + 2);
                        sampleRate = System.BitConverter.ToInt32(wav, chunkStart + 4);
                        bitsPerSample = System.BitConverter.ToInt16(wav, chunkStart + 14);
                    }
                    else if (chunkId == "data")
                    {
                        dataChunk = new byte[chunkSize];
                        System.Array.Copy(wav, chunkStart, dataChunk, 0, chunkSize);
                    }

                    pos = chunkStart + chunkSize + (chunkSize % 2);
                }

                if (dataChunk == null) return null;

                int bytesPerSample = Mathf.Max(1, bitsPerSample / 8);
                int sampleCount = dataChunk.Length / bytesPerSample;
                var samples = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    int offset = i * bytesPerSample;
                    switch (bitsPerSample)
                    {
                        case 16:
                            samples[i] = System.BitConverter.ToInt16(dataChunk, offset) / 32768f;
                            break;
                        case 8:
                            samples[i] = (dataChunk[offset] - 128) / 128f;
                            break;
                        case 32:
                            samples[i] = System.BitConverter.ToInt32(dataChunk, offset) / 2147483648f;
                            break;
                        default:
                            samples[i] = 0f;
                            break;
                    }
                }

                var clip = AudioClip.Create(name, sampleCount / Mathf.Max(1, channels), Mathf.Max(1, channels), sampleRate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            catch (System.Exception e) { Log.Err("SoundFX.WavToAudioClip", e); return null; }
        }

        public static void PlaySound(string name)
        {
            if (Volume <= 0f) return;
            EnsureLoaded();
            if (_source == null) return;

            if (!System.Enum.TryParse(name, true, out Sound key)) return;
            if (!_clips.TryGetValue(key, out var clip) || clip == null) return;

            _source.PlayOneShot(clip, Volume);
        }
    }

    public static class CategoryNames
    {
        public static string Get(MenuCategory category) => I18n.T("Category." + category.ToString());
    }

    public static class Keys
    {
        static readonly Dictionary<string, KeyCode> _def = new Dictionary<string, KeyCode>();

        public static KeyCode CreateKey(string name, KeyCode def)
        {
            if (!_def.ContainsKey(name)) _def[name] = def;
            return GetKey(name);
        }

        public static void ModifyKey(string name, KeyCode kc)
        { MenuPrefs.SetString("MUM_" + name, kc.ToString()); MenuPrefs.Save(); }

        public static KeyCode GetKey(string name)
        {
            KeyCode def = _def.TryGetValue(name, out var d) ? d : KeyCode.None;
            string raw = MenuPrefs.GetString("MUM_" + name, def.ToString());
            try { var key=(KeyCode)System.Enum.Parse(typeof(KeyCode), raw);
                return name=="KeyMenu" && (key==KeyCode.None || !System.Enum.IsDefined(typeof(KeyCode),key)) ? KeyCode.F1 : key; }
            catch { return def; }
        }

        public static void ResetKey(string name)
        {
            if (_def.TryGetValue(name, out var def))
            { MenuPrefs.SetString("MUM_" + name, def.ToString()); MenuPrefs.Save(); }
        }

        public static IEnumerable<KeyValuePair<string, KeyCode>> All()
        {
            foreach (var kv in _def)
                yield return new KeyValuePair<string, KeyCode>(kv.Key, GetKey(kv.Key));
        }
    }

    public enum Layout { Unset = 0, Azerty = 1, Qwerty = 2, Qwertz = 3, Jcuken = 4 }

    public static class KeyLayout
    {
        public static Layout Current
        {
            get => (Layout)MenuPrefs.GetInt("MUM_Layout", (int)Layout.Unset);
            set { MenuPrefs.SetInt("MUM_Layout", (int)value); MenuPrefs.Save(); }
        }

        public static void DetectAndApply()
        {
            if (Keyboard.current == null) return;

            string w=Keyboard.current.wKey.displayName?.ToLowerInvariant();
            string a=Keyboard.current.aKey.displayName?.ToLowerInvariant();
            string z=Keyboard.current.zKey.displayName?.ToLowerInvariant();
            Layout physical=w=="z" && a=="q"?Layout.Azerty:z=="y"?Layout.Qwertz:w=="ц"?Layout.Jcuken:w=="w" && a=="a"?Layout.Qwerty:Layout.Unset;
            Layout named=FromSystemName(Keyboard.current.keyboardLayout??"");
            if(named!=Layout.Unset) physical=named;
            if(physical!=Layout.Unset){if(Current!=physical)Current=physical;return;}

            string layoutName = Keyboard.current.keyboardLayout;
            if (string.IsNullOrEmpty(layoutName)) return;

            Layout detected = FromSystemName(layoutName);
            if (detected == Layout.Unset) return;

            if (Current != detected)
                Current = detected;
        }

        static Layout FromSystemName(string name)
        {
            string n = name.ToLowerInvariant();
            if(n.Contains("canad")) return Layout.Qwerty;

            if (n.Contains("french") || n.Contains("français") || n.Contains("francais") || n.Contains("azerty") || n.Contains("belg"))
                return Layout.Azerty;
            if (n.Contains("german") || n.Contains("swiss") || n.Contains("austria") || n.Contains("qwertz"))
                return Layout.Qwertz;
            if (n.Contains("russian") || n.Contains("jcuken") || n.Contains("йцукен"))
                return Layout.Jcuken;
            if (n.Contains("us") || n.Contains("english") || n.Contains("united") || n.Contains("uk") || n.Contains("qwerty"))
                return Layout.Qwerty;

            return Layout.Unset;
        }

        static readonly Dictionary<KeyCode, string> _azerty = new Dictionary<KeyCode, string>
        {
            { KeyCode.Q, "A" }, { KeyCode.W, "Z" }, { KeyCode.A, "Q" }, { KeyCode.Z, "W" },
            { KeyCode.M, "," }, { KeyCode.Semicolon, "M" }, { KeyCode.Comma, ";" },
            { KeyCode.Period, ":" }, { KeyCode.Slash, "!" },
        };

        static readonly Dictionary<KeyCode, string> _qwertz = new Dictionary<KeyCode, string>
        {
            { KeyCode.Y, "Z" }, { KeyCode.Z, "Y" },
        };

        static readonly Dictionary<KeyCode, string> _jcuken = new Dictionary<KeyCode, string>
        {
            { KeyCode.Q, "Й" }, { KeyCode.W, "Ц" }, { KeyCode.E, "У" }, { KeyCode.R, "К" },
            { KeyCode.T, "Е" }, { KeyCode.Y, "Н" }, { KeyCode.U, "Г" }, { KeyCode.I, "Ш" },
            { KeyCode.O, "Щ" }, { KeyCode.P, "З" },
            { KeyCode.A, "Ф" }, { KeyCode.S, "Ы" }, { KeyCode.D, "В" }, { KeyCode.F, "А" },
            { KeyCode.G, "П" }, { KeyCode.H, "Р" }, { KeyCode.J, "О" }, { KeyCode.K, "Л" },
            { KeyCode.L, "Д" }, { KeyCode.Semicolon, "Ж" }, { KeyCode.Quote, "Э" },
            { KeyCode.Z, "Я" }, { KeyCode.X, "Ч" }, { KeyCode.C, "С" }, { KeyCode.V, "М" },
            { KeyCode.B, "И" }, { KeyCode.N, "Т" }, { KeyCode.M, "Ь" },
            { KeyCode.Comma, "Б" }, { KeyCode.Period, "Ю" },
        };

        static Dictionary<KeyCode, string> TableFor(Layout layout)
        {
            switch (layout)
            {
                case Layout.Azerty: return _azerty;
                case Layout.Qwertz: return _qwertz;
                case Layout.Jcuken: return _jcuken;
                default: return null;
            }
        }

        public static string Display(KeyCode kc)
        {
            var table = TableFor(Current);
            if (table != null && table.TryGetValue(kc, out string mapped)) return mapped;
            return kc.ToString();
        }

        public static string Display(KeyCode kc, Layout layout)
        {
            var table = TableFor(layout);
            if (table != null && table.TryGetValue(kc, out string mapped)) return mapped;
            return kc.ToString();
        }

        public static KeyCode Physical(string label, Layout layout)
        {
            var table = TableFor(layout);
            if (table != null)
                foreach (var kv in table)
                    if (kv.Value == label) return kv.Key;
            if (System.Enum.TryParse(label, true, out KeyCode direct)) return direct;
            return KeyCode.None;
        }
    }

    public static class ModConfig
    {
        private static string[] _pendingLines;
        public static bool HasPending => _pendingLines != null;

        public static void Load() => Log.Info("Config Loaded !");
        public static void Save() => MenuPrefs.Save();
        public static void SetKey(string name, KeyCode kc) => Keys.ModifyKey(name, kc);

        public static void Export()
        {
            var sb = new System.Text.StringBuilder("[MattraxUltimateMenu v1.2]\n");
            foreach (var kv in Keys.All()) sb.AppendLine("MUM_" + kv.Key + "=" + kv.Value);
            sb.AppendLine("MUM_CamSpeed=" + MenuPrefs.GetFloat("MUM_CamSpeed", 5f).ToString(Inv));
            sb.AppendLine("MUM_CamSens=" + MenuPrefs.GetFloat("MUM_CamSens", 2f).ToString(Inv));
            sb.AppendLine("MUM_names=" + MenuPrefs.GetString("MUM_names", "{}"));
            sb.AppendLine("MUM_UIScale=" + MenuPrefs.GetFloat("MUM_UIScale", 1f).ToString(Inv));
            sb.AppendLine("MUM_ThemePreset=" + MenuPrefs.GetInt("MUM_ThemePreset", 0));
            sb.AppendLine("MUM_BgAlpha=" + MenuPrefs.GetFloat("MUM_BgAlpha", 0.97f).ToString(Inv));
            sb.AppendLine("MUM_Layout=" + MenuPrefs.GetInt("MUM_Layout", 0));
            sb.AppendLine("MUM_Lang=" + MenuPrefs.GetInt("MUM_Lang", 0));
            string content = sb.ToString();
            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    var d = new System.Windows.Forms.SaveFileDialog
                    { Title = "Exporter config", FileName = "MattraxConfig.txt", Filter = "txt|*.txt|*|*.*" };
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    { System.IO.File.WriteAllText(d.FileName, content); Log.Info("Exporté : " + d.FileName); }
                }
                catch (System.Exception e) { Log.Err("Export", e); }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA); t.Start();
        }

        public static void Import()
        {
            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    var d = new System.Windows.Forms.OpenFileDialog
                    { Title = "Importer config", Filter = "txt|*.txt|*|*.*" };
                    if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        _pendingLines = System.IO.File.ReadAllLines(d.FileName);
                }
                catch (System.Exception e) { Log.Err("Import", e); }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA); t.Start();
        }

        public static void ApplyPendingImport()
        {
            if (_pendingLines == null) return;
            var lines = _pendingLines; _pendingLines = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (!line.Contains("=") || line.StartsWith("[")) continue;
                var p = line.Split(new char[] { '=' }, 2);
                string k = p[0].Trim(), v = p[1].Trim();
                if (!k.StartsWith("MUM_")) continue;
                string keyName = k.Substring(4);
                bool isKey = false;
                foreach (var kv in Keys.All()) { if (kv.Key == keyName) { isKey = true; break; } }
                if (isKey) { try { Keys.ModifyKey(keyName, (KeyCode)System.Enum.Parse(typeof(KeyCode), v)); } catch { } continue; }
                if (k == "MUM_CamSpeed" || k == "MUM_CamSens" || k == "MUM_UIScale" || k == "MUM_BgAlpha")
                { if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv)) MenuPrefs.SetFloat(k, fv); }
                else if (k == "MUM_ThemePreset" || k == "MUM_Layout" || k == "MUM_Lang") { if (int.TryParse(v, out int iv)) MenuPrefs.SetInt(k, iv); }
                else MenuPrefs.SetString(k, v);
            }
            MenuPrefs.Save();
            GorillaCamera.Instance?.ReloadConfig();
            Theme.LoadSaved();
            MenuUI.Instance?.RebuildTex();
            Log.Info("Config importée !");
        }
        static System.Globalization.CultureInfo Inv => System.Globalization.CultureInfo.InvariantCulture;
    }

    // ================================================================
    // SETTING — représentation UNIQUE d'un réglage de mod, indépendante du
    // rendu. Un mod déclare sa liste de Setting UNE FOIS via Page.Settings
    // (ou MenuUI.RegisterPageWithSettings), et PC (IMGUI, souris/clavier)
    // et VR (VRow, stick/trigger) l'affichent chacun avec leur propre
    // contrôle, mais depuis la même source de vérité (mêmes Get/Set).
    // ================================================================
    public class Setting
    {
        public enum Kind { Slider, Toggle, Button, Info, IntSlider }

        public Kind Type;
        public string Label;

        // Slider / IntSlider
        public Func<float> GetFloat;
        public Action<float> SetFloat;
        public float Min, Max;
        public string Format = "0.00";
        public float Step = 0.05f; // pas utilisé par les flèches VR (< >) et par IntSlider PC

        // Toggle
        public Func<bool> GetBool;
        public Action<bool> SetBool;

        // Button / Info
        public Func<string> GetValueText;
        public Action OnActivate;

        public static Setting Slider(string label, Func<float> get, Action<float> set, float min, float max, string fmt = "0.00", float step = 0.05f)
            => new Setting { Type = Kind.Slider, Label = label, GetFloat = get, SetFloat = set, Min = min, Max = max, Format = fmt, Step = step };

        public static Setting IntSlider(string label, Func<float> get, Action<float> set, int min, int max)
            => new Setting { Type = Kind.IntSlider, Label = label, GetFloat = get, SetFloat = set, Min = min, Max = max, Format = "0", Step = 1f };

        public static Setting Toggle(string label, Func<bool> get, Action<bool> set)
            => new Setting { Type = Kind.Toggle, Label = label, GetBool = get, SetBool = set };

        public static Setting Button(string label, Action onActivate, Func<string> getValueText = null)
            => new Setting { Type = Kind.Button, Label = label, OnActivate = onActivate, GetValueText = getValueText };

        public static Setting Info(string label, Func<string> getValueText)
            => new Setting { Type = Kind.Info, Label = label, GetValueText = getValueText };

        /// <summary>Rendu PC (IMGUI), via les helpers déjà exposés par MenuUI (ui.Slider, ui.Toggle, ui.Btn...).</summary>
        public void DrawPC(MenuUI ui)
        {
            switch (Type)
            {
                case Kind.Slider:
                    {
                        float nv = ui.Slider(Label, GetFloat(), Min, Max, Format);
                        if (!Mathf.Approximately(nv, GetFloat())) SetFloat(nv);
                        break;
                    }
                case Kind.IntSlider:
                    {
                        int nv = ui.IntSlider(Label, Mathf.RoundToInt(GetFloat()), Mathf.RoundToInt(Min), Mathf.RoundToInt(Max));
                        if (nv != Mathf.RoundToInt(GetFloat())) SetFloat(nv);
                        break;
                    }
                case Kind.Toggle:
                    {
                        bool nv = ui.Toggle(Label, GetBool());
                        if (nv != GetBool()) SetBool(nv);
                        break;
                    }
                case Kind.Button:
                    {
                        string vt = GetValueText != null ? GetValueText() : "";
                        if (ui.Btn(string.IsNullOrEmpty(vt) ? Label : Label + " : " + vt))
                            OnActivate?.Invoke();
                        break;
                    }
                case Kind.Info:
                    {
                        ui.InfoRow(Label, GetValueText());
                        break;
                    }
            }
        }

        /// <summary>Conversion en VRow pour le rendu VR (GorillaInterface) — même source de vérité (Get/Set).</summary>

    }

    public class Page
    {
        public string Icon, Title;
        public MenuCategory Category;
        public bool IsCustom;
        public bool HasActivation = true, PinToTop;
        public string DescriptionKey;
        public System.Func<bool> ReadActivation;
        public System.Action<bool> WriteActivation;
        public System.Action<MenuUI> Draw;

        /// <summary>
        /// Liste de réglages unifiés (optionnelle). Quand elle est renseignée, PC ET VR
        /// s'en servent automatiquement pour afficher les MÊMES paramètres, avec des
        /// contrôles adaptés à chaque plateforme (slider souris vs stick+flèches, etc.),
        /// sans que le mod ait à écrire deux fois son code de réglages.
        /// </summary>
        public List<Setting> Settings = new List<Setting>();

        public Page(string icon, string title, System.Action<MenuUI> draw,
                    MenuCategory category = MenuCategory.Gameplay, bool isCustom = false)
        { Icon = icon; Title = title; Draw = draw; Category = category; IsCustom = isCustom; }

        string PrefKey => "MUM_ModEnabled_" + Title;

        public bool Enabled
        {
            get => ReadActivation != null ? ReadActivation() : MenuPrefs.GetInt(PrefKey, 0) == 1;
            set
            {
                if(WriteActivation!=null){WriteActivation(value);return;}
                MenuPrefs.SetInt(PrefKey, value ? 1 : 0);
                MenuPrefs.Save();
            }
        }
    }

    [DefaultExecutionOrder(10000)]
    public partial class MenuUI : MonoBehaviour
    {
        public static MenuUI Instance { get; private set; }

        private List<Page> _pages = new List<Page>();
        private int _nav = -1;
        private Vector2 _scrollModes, _scrollPage;
        private bool _visible;
        private float _scale = 1f;
        private Rect _rect;
        private bool _dragging;
        private Vector2 _dragOff;
        private Vector2 _dragTarget;
        private bool _dragSettling;
        private int _lastScreenW, _lastScreenH;
        private bool _themeOpen;
        private bool _langOpen;
        private bool _fontOpen;

        private float _animT;
        private bool _animOpening;
        private const float AnimDur = 0.10f;

        private readonly Texture2D[] _navigationIcons = new Texture2D[21];
        private int? _pendingNav;
        private int _selectedConfig = -3; // -3: aucune, -4: general, >=0: mod
        private Vector2 _scrollSettings;
        private float _settingsContentH = 1f;
        private GUISkin _scrollSkin;
        private float _scrollSkinScale;
        private readonly Stack<GUISkin> _scrollSkinStack = new Stack<GUISkin>();

        public bool WaitingKey { get; private set; }
        public string RebindTarget { get; private set; }
        private List<string> _keyHistory = new List<string>();

        public Texture2D TA, TB, TB2, TB3, TW, TG, TR, TY;
        private KeyCode _keyMenu;

        private string _search = "";

        private float _homeContentH = 1f;
        private float _modesContentH = 1f;
        private float _pageContentH = 1f;

        // URL du Patreon, utilisée par le bouton Donate du footer
        private const string DonateUrl = "https://www.patreon.com/cw/MATTRAXER64xp";

        // NOTE (refactor) : GorillaInterface (menu VR in-game) est désormais une capacité
        // native de MenuUI plutôt qu'un mod séparé. Elle est instanciée ici et tourne en
        // permanence tant que MenuUI existe — elle n'apparaît plus dans GetAllPagesSorted().
        private GorillaInterface _gorillaInterface;

        void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; Destroy(this); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Trad.Register();
            _scale = MenuPrefs.GetFloat("MUM_UIScale", 1f);
            _keyMenu = Keys.CreateKey("KeyMenu", KeyCode.F1);
            if (MenuPrefs.GetInt("MUM_GlassLayout", 0) == 0)
            {
                Theme.Current = ThemePreset.SombreBleu;
                Theme.BgAlpha = 0.88f;
                MenuPrefs.SetInt("MUM_GlassLayout", 1);
            }
            Theme.LoadSaved();
            RebuildTex();
            LoadNavigationIcons();

            // Instanciation de la capacité VR native : un composant dédié sur un GameObject
            // séparé (persistant), démarré et piloté depuis MenuUI plutôt que déclaré comme
            // un mod à part dans la liste.
            _gorillaInterface = GorillaInterface.Instance;
            if (_gorillaInterface == null)
            {
                var giGO = new GameObject("MUM_GorillaInterface");
                DontDestroyOnLoad(giGO);
                _gorillaInterface = giGO.AddComponent<GorillaInterface>();
            }

            // NOTE : plus de RegisterPage ici — GorillaInterface fait maintenant partie
            // intégrante de MenuUI. Ses réglages ("Technique") sont dessinés directement
            // dans l'Accueil PC (voir DrawHome) et dans la section "Interface VR" de
            // l'Accueil VR, jamais dans la liste des mods PC ni VR.
        }

        void Start() { ComputeRect(); }

        public void ApplyWindowPreferences()
        {
            _scale = MenuPrefs.GetFloat("MUM_UIScale", 1f);
            ComputeRect();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (_gorillaInterface != null) Destroy(_gorillaInterface.gameObject);
            ReleaseVrTexture();
            VectorGlassUI.Dispose();
            if (_colorWheelTexture != null) Destroy(_colorWheelTexture);
            foreach (var icon in _navigationIcons) if (icon != null) Destroy(icon);
            if (_scrollSkin != null) Destroy(_scrollSkin);
        }

        public void RebuildTex()
        {
            if (_scrollSkin != null) { Destroy(_scrollSkin); _scrollSkin = null; }
            UI.ClearRoundedCache();
            foreach (var texture in new[] { TA, TB, TB2, TB3, TW, TG, TR, TY })
                if (texture != null) Destroy(texture);
            TA = UI.MakeTex(Theme.Accent); TB = UI.MakeTex(Theme.BG);
            TB2 = UI.MakeTex(Theme.BG2); TB3 = UI.MakeTex(Theme.BG3);
            TW = UI.MakeTex(Color.white); TG = UI.MakeTex(Theme.Green);
            TR = UI.MakeTex(Theme.Red); TY = UI.MakeTex(Theme.Yellow);
        }

        public void RegisterPage(string icon, string title, System.Action<MenuUI> draw, MenuCategory category = MenuCategory.Gameplay)
            => _pages.Add(new Page(icon, title, draw, category, false));
        public void RegisterPage(string icon, string title, System.Action<MenuUI> draw, MenuCategory category, bool isCustom)
            => _pages.Add(new Page(icon, title, draw, category, isCustom));
        public void RegisterPage(string title, System.Action<MenuUI> draw, MenuCategory category = MenuCategory.Gameplay, bool hasActivation = true)
            => _pages.Add(new Page("", title, draw, category, false) { HasActivation = hasActivation });

        /// <summary>
        /// Enregistre une page à partir d'une liste de Setting UNIQUEMENT : PC et VR
        /// affichent automatiquement les mêmes réglages (mêmes Get/Set), chacun avec son
        /// propre contrôle. Aucun code de rendu à écrire pour le mod. `draw` reste possible
        /// en plus (contenu libre affiché avant les Settings) mais n'est pas requis.
        /// </summary>
        public void RegisterPageWithSettings(string title, List<Setting> settings, MenuCategory category = MenuCategory.Gameplay, System.Action<MenuUI> draw = null)
        {
            var page = new Page("", title, draw, category, false);
            page.Settings = settings ?? new List<Setting>();
            _pages.Add(page);
        }

        public void ConfigurePageActivation(string title, System.Func<bool> read, System.Action<bool> write, string description)
        {
            var page=_pages.FirstOrDefault(p=>p.Title==title);
            if(page==null)return;
            page.HasActivation=true;page.PinToTop=true;page.ReadActivation=read;page.WriteActivation=write;page.DescriptionKey=description;
        }

        public bool IsModEnabled(string title)
        {
            var page = _pages.FirstOrDefault(p => p.Title == title);
            return page != null ? page.Enabled : MenuPrefs.GetInt("MUM_ModEnabled_" + title, 0) == 1;
        }

        public List<Page> GetAllPagesSorted()
        {
            var sorted = new List<Page>(_pages);
            sorted.Sort((a, b) =>
            {
                int catCmp = a.Category.CompareTo(b.Category);
                return catCmp != 0 ? catCmp : string.Compare(I18n.T(a.Title), I18n.T(b.Title), System.StringComparison.OrdinalIgnoreCase);
            });
            return sorted;
        }

        public void StartRebind(string keyName)
        {
            RebindTarget = keyName; WaitingKey = true;
            if (GorillaInterface.Instance != null && GorillaInterface.Instance.CastRequested)
            { _vrKeyboardTarget = "@rebind"; _vrFocus = null; }
        }

        public void ToggleVisible()
        {
            bool willOpen = !_visible || !_animOpening;
            SoundFX.PlaySound(willOpen ? "Aigue" : "Grave");
            if (willOpen)
            {
                _visible = true;
                _animOpening = true;
                ComputeRect();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                _animOpening = false;
            }
        }
        public void HideForCapture()
        {
            _animOpening=false;
            _gorillaInterface?.CloseForCapture();
        }
        internal bool ContainsScreenMenu(Vector2 point)=>_visible&&_rect.Contains(point);

        void ComputeRect()
        {
            _lastScreenW = Screen.width; _lastScreenH = Screen.height;
            float w = Mathf.Clamp(600f * _scale, Mathf.Min(360f, Mathf.Max(1f, Screen.width - 90f)), Screen.width > 0 ? Mathf.Max(1f, Screen.width - 90f) : 600);
            float h = Mathf.Clamp(660f * _scale, Mathf.Min(420f, Mathf.Max(1f, Screen.height - 20f)), Screen.height > 0 ? Mathf.Max(1f, Screen.height - 20f) : 660);

            float x, y;
            if (MenuPrefs.HasKey("MUM_PosX") && MenuPrefs.HasKey("MUM_PosY"))
            {
                x = Mathf.Clamp(MenuPrefs.GetFloat("MUM_PosX"), 0, Mathf.Max(0, Screen.width - w));
                y = Mathf.Clamp(MenuPrefs.GetFloat("MUM_PosY"), 0, Mathf.Max(0, Screen.height - h));
            }
            else
            {
                x = Screen.width * 0.05f;
                y = (Screen.height - h) / 2f;
            }
            float dockSpace = 0f;
            x = Mathf.Clamp(x, dockSpace + 10f, Mathf.Max(dockSpace + 10f, Screen.width - w - 10f));
            _rect = new Rect(x, y, w, h);
        }

        void SavePosition()
        {
            MenuPrefs.SetFloat("MUM_PosX", _rect.x);
            MenuPrefs.SetFloat("MUM_PosY", _rect.y);
            MenuPrefs.Save();
        }

        void Update()
        {
            if (_dragging || _dragSettling)
            {
                float blend = MenuStyle.FastInterface ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / .045f);
                _rect.position = Vector2.Lerp(_rect.position, _dragTarget, blend);
                if (!_dragging && Vector2.Distance(_rect.position, _dragTarget) < .2f)
                {
                    _rect.position = _dragTarget;
                    _dragSettling = false;
                    SavePosition();
                }
            }
            KeyLayout.DetectAndApply();

            if ((_visible || (GorillaInterface.Instance != null && GorillaInterface.Instance.CastRequested)) && (Screen.width != _lastScreenW || Screen.height != _lastScreenH))
            { _lastScreenW = Screen.width; _lastScreenH = Screen.height; ComputeRect(); }

            if (_visible)
            {
                float target = _animOpening ? 1f : 0f;
                _animT = MenuStyle.FastInterface ? target : Mathf.MoveTowards(_animT, target, Time.unscaledDeltaTime / AnimDur);
                if (!_animOpening && _animT <= 0f)
                {
                    _visible = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }


            }

            _keyMenu = Keys.GetKey("KeyMenu");
            if (Keyboard.current == null) return;

            Key menuKey = ToInputKey(_keyMenu);
            if (menuKey != Key.None && Keyboard.current[menuKey].wasPressedThisFrame) ToggleVisible();

            if (WaitingKey && _vrKeyboardTarget != "@rebind")
            {
                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                { WaitingKey = false; RebindTarget = null; return; }

                foreach (var control in Keyboard.current.allKeys)
                {
                    if (!control.wasPressedThisFrame) continue;
                    KeyCode kc = FromInputKey(control.keyCode);
                    if (kc == KeyCode.None) continue;

                    Keys.ModifyKey(RebindTarget, kc);
                    WaitingKey = false; RebindTarget = null;
                    _keyHistory.Insert(0, kc.ToString());
                    if (_keyHistory.Count > 5) _keyHistory.RemoveAt(5);
                    break;
                }
            }
        }

        static Key ToInputKey(KeyCode kc)
        {
            string n = kc.ToString();
            if (n.Length == 1 && char.IsLetter(n[0]))
                return (Key)System.Enum.Parse(typeof(Key), n.ToUpper());
            if (n.StartsWith("Alpha") && n.Length == 6)
                return (Key)System.Enum.Parse(typeof(Key), "Digit" + n.Substring(5));
            if (n.StartsWith("Keypad") && n.Length == 7 && char.IsDigit(n[6]))
                return (Key)System.Enum.Parse(typeof(Key), "Numpad" + n.Substring(6));
            switch (kc)
            {
                case KeyCode.F1: return Key.F1;
                case KeyCode.F2: return Key.F2;
                case KeyCode.F3: return Key.F3;
                case KeyCode.F4: return Key.F4;
                case KeyCode.F5: return Key.F5;
                case KeyCode.F6: return Key.F6;
                case KeyCode.F7: return Key.F7;
                case KeyCode.F8: return Key.F8;
                case KeyCode.F9: return Key.F9;
                case KeyCode.F10: return Key.F10;
                case KeyCode.F11: return Key.F11;
                case KeyCode.F12: return Key.F12;
                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.Delete: return Key.Delete;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.CapsLock: return Key.CapsLock;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
                case KeyCode.Slash: return Key.Slash;
                case KeyCode.Semicolon: return Key.Semicolon;
                case KeyCode.Quote: return Key.Quote;
                case KeyCode.LeftBracket: return Key.LeftBracket;
                case KeyCode.RightBracket: return Key.RightBracket;
                case KeyCode.Backslash: return Key.Backslash;
                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
                case KeyCode.BackQuote: return Key.Backquote;
                default: return Key.None;
            }
        }

        static KeyCode FromInputKey(Key k)
        {
            if (k >= Key.A && k <= Key.Z) return KeyCode.A + (k - Key.A);
            if (k >= Key.Digit0 && k <= Key.Digit9) return KeyCode.Alpha0 + (k - Key.Digit0);
            if (k >= Key.F1 && k <= Key.F12) return KeyCode.F1 + (k - Key.F1);
            if (k >= Key.Numpad0 && k <= Key.Numpad9) return KeyCode.Keypad0 + (k - Key.Numpad0);
            switch (k)
            {
                case Key.Space: return KeyCode.Space;
                case Key.Enter: case Key.NumpadEnter: return KeyCode.Return;
                case Key.Escape: return KeyCode.Escape;
                case Key.Tab: return KeyCode.Tab;
                case Key.Backspace: return KeyCode.Backspace;
                case Key.Delete: return KeyCode.Delete;
                case Key.LeftShift: return KeyCode.LeftShift;
                case Key.RightShift: return KeyCode.RightShift;
                case Key.LeftCtrl: return KeyCode.LeftControl;
                case Key.RightCtrl: return KeyCode.RightControl;
                case Key.LeftAlt: return KeyCode.LeftAlt;
                case Key.RightAlt: return KeyCode.RightAlt;
                case Key.UpArrow: return KeyCode.UpArrow;
                case Key.DownArrow: return KeyCode.DownArrow;
                case Key.LeftArrow: return KeyCode.LeftArrow;
                case Key.RightArrow: return KeyCode.RightArrow;
                case Key.CapsLock: return KeyCode.CapsLock;
                case Key.Comma: return KeyCode.Comma;
                case Key.Period: return KeyCode.Period;
                case Key.Slash: return KeyCode.Slash;
                case Key.Semicolon: return KeyCode.Semicolon;
                case Key.Quote: return KeyCode.Quote;
                case Key.LeftBracket: return KeyCode.LeftBracket;
                case Key.RightBracket: return KeyCode.RightBracket;
                case Key.Backslash: return KeyCode.Backslash;
                case Key.Minus: return KeyCode.Minus;
                case Key.Equals: return KeyCode.Equals;
                case Key.Backquote: return KeyCode.BackQuote;
                default: return KeyCode.None;
            }
        }

        const int WindowRadius = 28;
        const int RowRadius = 12;

        void LoadNavigationIcons()
        {
            string[] names = { "Home", "Mods", "Settings", "Arrow", "Donation", "Minus", "Plus", "Cross", "Download", "VR", "Sound", "Language", "Sun", "Moon", "Play", "StatCube", "StatActive", "OrbitDot", "Search", "Person", "RangeRing" };
            var assembly = typeof(MenuUI).Assembly;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    using (var stream = assembly.GetManifestResourceStream("UltimateMenu.Assets.Menu.Icons." + names[i] + ".png"))
                    {
                        if (stream == null)
                        {
                            Log.Err("MenuUI.LoadNavigationIcons", new System.IO.FileNotFoundException("Missing embedded icon: " + names[i]));
                            continue;
                        }
                        using (var bytes = new System.IO.MemoryStream())
                        {
                            stream.CopyTo(bytes);
                            var icon = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                            if (!icon.LoadImage(bytes.ToArray())) { Destroy(icon); continue; }
                            icon = CenterIconContent(icon);
                            icon.Apply(true, true);
                            icon.filterMode = FilterMode.Trilinear;
                            icon.wrapMode = TextureWrapMode.Clamp;
                            _navigationIcons[i] = icon;
                        }
                    }
                }
                catch (Exception e) { Log.Err("MenuUI.LoadNavigationIcons", e); }
            }
        }

        static Texture2D CenterIconContent(Texture2D source)
        {
            var pixels = source.GetPixels32();
            int left = source.width, right = -1, bottom = source.height, top = -1;
            for (int y = 0; y < source.height; y++)
                for (int x = 0; x < source.width; x++)
                    if (pixels[y * source.width + x].a > 8)
                    {
                        left = Mathf.Min(left, x); right = Mathf.Max(right, x);
                        bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
                    }
            if (right < left || (left == 0 && bottom == 0 && right == source.width - 1 && top == source.height - 1)) return source;
            var centered = new Texture2D(right - left + 1, top - bottom + 1, TextureFormat.RGBA32, true);
            centered.SetPixels(source.GetPixels(left, bottom, centered.width, centered.height));
            Destroy(source);
            return centered;
        }

        void DrawMenuIcon(Rect rect, int icon, float angle = 0f)
        {
            if (Event.current.type != EventType.Repaint || _navigationIcons[icon] == null) return;
            var previous = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = previousColor * Theme.Text;
            try
            {
                if (icon == 3 && Mathf.Approximately(angle, 180f))
                {
                    var texture = _navigationIcons[icon];
                    float scale = Mathf.Min(rect.width / texture.width, rect.height / texture.height);
                    var fitted = new Rect(rect.center.x - texture.width * scale * 0.5f,
                        rect.center.y - texture.height * scale * 0.5f, texture.width * scale, texture.height * scale);
                    GUI.DrawTextureWithTexCoords(fitted, texture, new Rect(1f, 0f, -1f, 1f), true);
                }
                else
                {
                    if (angle != 0f) GUIUtility.RotateAroundPivot(angle, rect.center);
                    GUI.DrawTexture(rect, _navigationIcons[icon], ScaleMode.ScaleToFit);
                }
            }
            finally { GUI.matrix = previous; GUI.color = previousColor; }
        }

        bool IconButton(Rect rect, int icon, string tooltip, float angle = 0f)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            UI.DrawRounded(rect, hover ? Theme.BG3 : Theme.BG2, Mathf.RoundToInt(rect.height * 0.5f));
            float inset = rect.height * 0.23f;
            DrawMenuIcon(new Rect(rect.x + inset, rect.y + inset, rect.width - 2f * inset, rect.height - 2f * inset), icon, angle);
            return ControlButton(rect, new GUIContent(_navigationIcons[icon] == null ? tooltip : "", tooltip), GUIStyle.none);
        }

        void NavigateTo(int target)
        {
            CancelVrInput();
            _pendingNav = target;
            GUI.FocusControl(null);
            SoundFX.PlaySound("Moyen");
        }

        void DrawNavigationItem(Rect rect, int index, string label, bool selected)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            if (selected || hover)
            {
                Color fill = selected ? Color.Lerp(Theme.BG3, Theme.Text, 0.12f) : Theme.BG3;
                UI.DrawOutline(rect, new Color(Theme.Text.r, Theme.Text.g, Theme.Text.b, selected ? 0.15f : 0.05f), Mathf.RoundToInt(rect.height * 0.5f), 1f);
                UI.DrawRounded(rect, fill, Mathf.RoundToInt(rect.height * 0.5f));
            }
            float iconSize = 23f * UI.Scale;
            var iconRect = new Rect(rect.center.x - iconSize * 0.5f, rect.center.y - iconSize * 0.5f, iconSize, iconSize);
            if (_navigationIcons[index] != null) DrawMenuIcon(iconRect, index);
            if (_navigationIcons[index] == null)
                GUI.Label(rect, label, UI.Sty(Theme.Text, 9, selected, TextAnchor.MiddleCenter));

            if (ControlButton(rect, new GUIContent("", label), GUIStyle.none))
                NavigateTo(index == 0 ? -1 : index == 1 ? -2 : (_selectedConfig == -3 ? -4 : _selectedConfig));

        }

        RenderTexture _vrMenuTexture;
        void ReleaseVrTexture()
        {
            if (_vrMenuTexture == null) return;
            _vrMenuTexture.Release(); Destroy(_vrMenuTexture); _vrMenuTexture = null;
        }
        void OnGUI()
        {
            var vr = GorillaInterface.Instance;
            bool cast = vr != null && vr.CastRequested;
            bool frozenCast = cast && vr.FrozenCast;
            if (!cast) ReleaseVrTexture();
            if (!_visible && !cast) return;
            if (!_visible && frozenCast) return;
            if (!_visible && Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint) return;
            RenderTexture previousTarget = RenderTexture.active;
            bool renderingCast = cast && !frozenCast && Event.current.type == EventType.Repaint;
            try
            {
                if (renderingCast)
                {
                    int width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
                    if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
                        throw new InvalidOperationException("VR menu texture exceeds GPU limits.");
                    if (_vrMenuTexture == null || _vrMenuTexture.width != width || _vrMenuTexture.height != height)
                    {
                        ReleaseVrTexture();
                        _vrMenuTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                        {
                            name = "UltimateMenu VR mirror", hideFlags = HideFlags.HideAndDontSave,
                            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                            useMipMap = false, autoGenerateMips = false, antiAliasing = 1
                        };
                    }
                    if (!_vrMenuTexture.IsCreated() && !_vrMenuTexture.Create())
                        throw new InvalidOperationException("Cannot create VR menu texture.");
                    RenderTexture.active = _vrMenuTexture;
                    GL.Clear(true, true, Color.clear);
                }
                _vrInDraw = cast;
                _vrOccurrences.Clear(); _vrScrollStack.Clear();
                if (Event.current.type == EventType.Repaint) {_vrControls.Clear();_vrScrollViews.Clear();}
                DrawMenu(cast);
                DrawVrKeyboard();
                FinishVrFrame();
                if (renderingCast)
                {
                    float unit = UI.Scale;
                    Rect crop = Rect.MinMaxRect(Mathf.Max(0, _rect.x - 6f * unit), Mathf.Max(0, _rect.y - 6f * unit),
                        Mathf.Min(Screen.width, _rect.xMax + 6f * unit), Mathf.Min(Screen.height, _rect.yMax + 6f * unit));
                    vr.PresentCast(_vrMenuTexture, crop, _rect);
                }
            }
            catch (ExitGUIException) { throw; }
            catch (Exception error)
            {
                if (!cast) throw;
                vr.FailCast(error);
            }
            finally { _vrInDraw = false; _vrScrollStack.Clear(); RenderTexture.active = previousTarget; }
            if (renderingCast && _visible && _vrMenuTexture != null)
            {
                Color color = GUI.color;
                try { GUI.color = Color.white; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _vrMenuTexture); }
                finally { GUI.color = color; }
            }
        }
        float _pageTransitionStarted = -100f;
        void DrawMenu(bool cast)
        {
            if (_rect.width < 10) ComputeRect();
            if (Event.current.type == EventType.Layout && _pendingNav.HasValue)
            {
                if (_nav != _pendingNav.Value)
                {
                    _pageTransitionStarted = Time.unscaledTime;
                    _sectionGeneration++;
                    _themeOpen = _pendingNav.Value == -4 && _settingsAnchor == "Core.theme_current";
                    _langOpen = _pendingNav.Value == -4 && _settingsAnchor == "Core.language";
                    if (_pendingNav.Value != -4) _settingsAnchor = null;
                }
                _nav = _pendingNav.Value;
                if (_nav == -4 || (_nav >= 0 && _nav < _pages.Count)) _selectedConfig = _nav;
                _pendingNav = null;
                _scrollPage = Vector2.zero;
                _pcScrollVelocity = 0f; _pcScrollExpectedValid = false; _pcBarDragging = false;
                _pcContentTracking = _pcContentDragging = false;
                MenuStyle.ClearLayout();
                ResetVrFocus();
            }
            UI.Scale = Mathf.Min(_rect.width / 600f, _rect.height / 660f);
            float unit = UI.Scale;
            UI.FontFor(true)?.RequestCharactersInTexture("v" + (Log.PluginVersion ?? "?") + " · Test Out date", UI.Px(13), FontStyle.Normal);
            float margin = 22f * unit;
            var ev = Event.current;
            Rect dragRect = new Rect(_rect.x, _rect.y, _rect.width, 58f * unit);
            if (ev.type == EventType.MouseDown && ev.button == 0 && dragRect.Contains(ev.mousePosition))
            { _dragging = true; _dragSettling = false; _dragTarget = _rect.position; _dragOff = ev.mousePosition - _rect.position; ev.Use(); }
            if (_dragging && ev.type == EventType.MouseDrag)
            {
                _dragTarget.x = Mathf.Clamp(ev.mousePosition.x - _dragOff.x, 10f * unit, Mathf.Max(10f * unit, Screen.width - _rect.width - 10f));
                _dragTarget.y = Mathf.Clamp(ev.mousePosition.y - _dragOff.y, 0f, Mathf.Max(0f, Screen.height - _rect.height));
                ev.Use();
            }
            if (_dragging && ev.type == EventType.MouseUp) { _dragging = false; _dragSettling = true; ev.Use(); }

            Color previousColor = GUI.color;
            GUISkin previousSkin = GUI.skin;
            Color previousContent = GUI.contentColor;
            Color previousBackground = GUI.backgroundColor;
            bool previousEnabled = GUI.enabled;
            GUI.contentColor = GUI.backgroundColor = Color.white;
            GUI.enabled = _vrKeyboardTarget == null;
            GUI.color = new Color(1f, 1f, 1f, cast ? 1f : Mathf.Clamp01(_animT));
            try
            {
                int radius = Mathf.RoundToInt(WindowRadius * unit);
                UI.DrawShadow(_rect, radius, 5f * unit, 0.20f);
                UI.DrawOutline(_rect, new Color(Theme.Text.r, Theme.Text.g, Theme.Text.b, 0.16f), radius, 1f);
                Color panelFill = MenuStyle.Panel;
                if (cast) panelFill.a = 1f;
                UI.DrawGlass(_rect, panelFill, radius);
                GUI.Label(new Rect(_rect.x + margin, _rect.y + 15f * unit, _rect.width - margin * 2f, 32f * unit),
                    Log.PluginName ?? "UltimateMenu", UI.Sty(Theme.Text, 18, true, TextAnchor.MiddleCenter));

                Rect bar = new Rect(_rect.x + margin, _rect.y + 58f * unit, _rect.width - margin * 2f, 44f * unit);
                UI.DrawGlass(bar, MenuStyle.Surface, Mathf.RoundToInt(22f * unit));
                int activeTab = _nav == -1 ? 0 : _nav == -2 ? 1 : 2;
                string[] labels = { I18n.T("Core.tab_home"), I18n.T("Core.tab_modes"), I18n.T("Core.vr_settings") };
                float tabWidth = (bar.width - 6f * unit) / 3f;
                float selectedPosition = MenuStyle.Animate("navigation-tab", activeTab, 20f);
                Rect selectedTab = new Rect(bar.x + 3f * unit + tabWidth * selectedPosition,
                    bar.y + 3f * unit, tabWidth, bar.height - 6f * unit);
                UI.DrawGlass(selectedTab, MenuStyle.Custom ? MenuStyle.Raised : Theme.Accent, Mathf.RoundToInt(selectedTab.height * 0.5f));
                for (int i = 0; i < 3; i++)
                {
                    Rect tab = new Rect(bar.x + 3f * unit + tabWidth * i, bar.y + 3f * unit, tabWidth, bar.height - 6f * unit);
                    GUI.Label(tab, labels[i], UI.Sty(i == activeTab && !MenuStyle.Custom ? MenuStyle.OnAccent : Theme.Text, 11, i == activeTab, TextAnchor.MiddleCenter));
                    if (ControlButton(tab, new GUIContent("", labels[i]), GUIStyle.none)) NavigateTo(i == 0 ? -1 : i == 1 ? -2 : -4);
                }
                float footerHeight = Mathf.Max(40f * unit, UI.Px(13f) + 16f * unit);
                Rect content = new Rect(_rect.x + margin, _rect.y + 118f * unit, _rect.width - margin * 2f,
                    _rect.height - 118f * unit - footerHeight - 10f * unit);
                float pageProgress = Mathf.Clamp01((Time.unscaledTime - _pageTransitionStarted) / 0.22f);
                float pageEase = 1f - Mathf.Pow(1f - pageProgress, 3f);
                Color pageColor = GUI.color;
                bool pageEnabled = GUI.enabled;
                GUI.color = new Color(pageColor.r, pageColor.g, pageColor.b, pageColor.a * pageEase);
                GUI.enabled = pageEnabled && pageProgress >= 0.35f;
                content.y += (1f - pageEase) * 10f * unit;
                GUILayout.BeginArea(content);
                try
                {
                    if (_nav == -2) DrawModes(content);
                    else if (_nav == -4) DrawPreferences(content);
                    else if (_nav == -3)
                    {
                        Section(I18n.T("Core.vr_settings"));
                        Label(I18n.T("Core.glass_select_mod"), Theme.Dim, 12);
                        Space(12);
                        if (Btn(I18n.T("Core.mods_list"))) NavigateTo(-2);
                    }
                    else if (_nav >= 0 && _nav < _pages.Count) DrawActivePage(_pages[_nav], content);
                    else DrawHome(content);
                }
                finally { GUILayout.EndArea(); GUI.color = pageColor; GUI.enabled = pageEnabled; }

                float footerY = _rect.yMax - footerHeight;
                DrawVersionFooter(footerY, unit);
                GUI.Label(new Rect(_rect.x + margin, footerY, 150f * unit, 20f * unit),
                    KeyLayout.Display(_keyMenu) + "  ·  " + I18n.T("Core.close_footer"), UI.Sty(Theme.Dim, 9));
                GUI.Label(new Rect(_rect.xMax-margin-76f*unit,footerY,76f*unit,28f*unit),FooterClock(),UI.Sty(Theme.Text,11,false,TextAnchor.MiddleRight));


            }
            finally
            {
                GUI.color = previousColor;
                GUI.skin = previousSkin;
                _scrollSkinStack.Clear();
                GUI.contentColor = previousContent;
                GUI.backgroundColor = previousBackground;
                GUI.enabled = previousEnabled;
            }
        }

        long _clockMinute=-1;
        string _clockText="";
        string FooterClock()
        {
            DateTime now=DateTime.Now;
            long minute=now.Ticks/TimeSpan.TicksPerMinute;
            if(minute!=_clockMinute){_clockMinute=minute;_clockText=now.ToString("H'h'mm",System.Globalization.CultureInfo.InvariantCulture);}
            return _clockText;
        }
        void DrawVersionFooter(float y, float unit)
        {
            bool test = Log.ReleaseStatusKey == "Release.Test";
            bool outdated = Log.ReleaseStatusKey == "Release.Update";
            Color color = test ? new Color(.76f, .55f, 1f) : outdated ? new Color(1f, .6f, .2f) : Theme.Text;
            string text = "v" + (Log.PluginVersion ?? "?") + (test ? " · Test" : outdated ? " · Out date" : "");
            if (Log.DownloadStatusKey.Length > 0) text = I18n.T(Log.DownloadStatusKey);
            var style = UI.Sty(color, 13, true, TextAnchor.MiddleCenter);
            var keyStyle = UI.Sty(Theme.Dim, 9);
            string keyText = KeyLayout.Display(_keyMenu) + "  ·  " + I18n.T("Core.close_footer");
            float minX = _rect.x + 22f * unit + Mathf.Max(150f * unit, keyStyle.CalcSize(new GUIContent(keyText)).x) + 12f * unit;
            float maxX = _rect.xMax - 22f * unit - 76f * unit - 12f * unit;
            float available = Mathf.Max(10f, maxX - minX);
            float iconSize = outdated ? 24f * unit : 0f;
            float gap = outdated ? 6f * unit : 0f;
            // Fit the full status in its reserved footer area, including the download icon.
            while (style.fontSize > 9 && style.CalcSize(new GUIContent(text)).x + iconSize + gap > available)
                style.fontSize--;
            style.padding = new RectOffset(0, 0, 0, 0);
            style.margin = new RectOffset(0, 0, 0, 0);
            style.wordWrap = false;
            style.clipping = TextClipping.Overflow;
            float width = Mathf.Ceil(style.CalcSize(new GUIContent(text)).x) + 4f * unit;
            float total = width + iconSize + gap;
            float left = Mathf.Clamp(_rect.center.x - total * .5f, minX, Mathf.Max(minX, maxX - total));
            float labelHeight = Mathf.Max(style.CalcHeight(new GUIContent(text), width), 28f * unit);
            GUI.Label(new Rect(left, y, width, labelHeight + 4f * unit), text, style);
            Rect versionLink = new Rect(left, y, width, labelHeight + 4f * unit);
            if (ControlButton(versionLink, new GUIContent("", "GitHub"), GUIStyle.none))
                Application.OpenURL("https://github.com/MATTRAX64/UltimateMenu");
            if (outdated)
            {
                Rect icon = new Rect(left + width + gap, y, iconSize, iconSize);
                Color old = GUI.color;
                GUI.color = new Color(color.r, color.g, color.b, old.a);
                try { DrawMenuIcon(icon, 8); }
                finally { GUI.color = old; }
                if (ControlButton(icon, new GUIContent("", I18n.T("Release.Download")), GUIStyle.none))
                {
                    if (Log.CanDownload) Log.DownloadUpdate();
                    else Application.OpenURL(Log.ReleasesUrl);
                }
            }
        }

        GUISkin GetScrollSkin()
        {
            if (_scrollSkin != null && Mathf.Approximately(_scrollSkinScale, UI.Scale)) return _scrollSkin;
            if (_scrollSkin != null) Destroy(_scrollSkin);
            _scrollSkin = Instantiate(GUI.skin);
            _scrollSkin.hideFlags = HideFlags.HideAndDontSave;
            _scrollSkinScale = UI.Scale;
            int radius = Mathf.Max(3, Mathf.RoundToInt(4f * UI.Scale));
            int size = radius * 2 + 4;
            var track = new GUIStyle(GUIStyle.none)
            {
                name = "verticalScrollbar",
                fixedWidth = Mathf.Max(8f, 9f * UI.Scale),
                stretchHeight = true,
                margin = new RectOffset(Mathf.RoundToInt(10f * UI.Scale), 0, 2, 2),
                border = new RectOffset(radius, radius, radius, radius)
            };
            var thumb = new GUIStyle(GUIStyle.none)
            {
                name = "verticalScrollbarThumb",
                stretchHeight = true,
                border = new RectOffset(radius, radius, radius, radius)
            };
            var trackTexture = UI.MakeRoundedTex(Color.Lerp(Theme.BG, Theme.Text, 0.07f), size, size, radius);
            var thumbTexture = UI.MakeRoundedTex(Color.Lerp(Theme.BG3, Theme.Text, 0.35f), size, size, radius);
            var hoverTexture = UI.MakeRoundedTex(Color.Lerp(Theme.BG3, Theme.Text, 0.55f), size, size, radius);
            var activeTexture = UI.MakeRoundedTex(Theme.Accent, size, size, radius);
            track.normal.background = track.hover.background = track.active.background = track.focused.background = trackTexture;
            thumb.normal.background = thumb.onNormal.background = thumbTexture;
            thumb.hover.background = thumb.onHover.background = hoverTexture;
            thumb.active.background = thumb.onActive.background = activeTexture;
            thumb.focused.background = thumb.onFocused.background = hoverTexture;
            _scrollSkin.verticalScrollbar = track;
            _scrollSkin.verticalScrollbarThumb = thumb;
            _scrollSkin.verticalScrollbarUpButton = new GUIStyle(GUIStyle.none) { fixedHeight = 0f, stretchHeight = false };
            _scrollSkin.verticalScrollbarDownButton = new GUIStyle(GUIStyle.none) { fixedHeight = 0f, stretchHeight = false };
            return _scrollSkin;
        }

        float _pcScrollVelocity, _pcScrollExpected;
        float _pcBarVelocity, _pcBarPosition, _pcBarSampleTime;
        bool _pcBarDragging;
        int _pcScrollFrame = -1;
        bool _pcScrollExpectedValid;
        Vector2 SmoothPcScroll(Vector2 scroll, Rect outerRect)
        {
            if (_vrInDraw || MenuStyle.FastInterface) { _pcScrollVelocity = 0f; _pcScrollExpectedValid = false; return scroll; }
            var ev = Event.current;
            Rect viewport = new Rect(GUIUtility.GUIToScreenPoint(Vector2.zero), outerRect.size);
            Vector2 mouse = GUIUtility.GUIToScreenPoint(ev.mousePosition);
            if (ev.type == EventType.MouseDown) { _pcScrollVelocity = 0f; _pcScrollExpectedValid = false; }
            if (ev.type == EventType.ScrollWheel && viewport.Contains(mouse))
            {
                _pcScrollVelocity = Mathf.Clamp(_pcScrollVelocity + ev.delta.y * 650f * UI.Scale, -4000f * UI.Scale, 4000f * UI.Scale);
                _pcScrollExpectedValid = false;
                ev.Use();
            }
            if (!_pcBarDragging && !_pcContentTracking && ev.type == EventType.Layout && _pcScrollFrame != Time.frameCount)
            {
                _pcScrollFrame = Time.frameCount;
                if (_pcScrollExpectedValid && Mathf.Abs(scroll.y - _pcScrollExpected) > 1f) _pcScrollVelocity = 0f;
                float decay = Mathf.Exp(-9f * Mathf.Min(Time.unscaledDeltaTime, 0.05f));
                scroll.y = Mathf.Max(0f, scroll.y + _pcScrollVelocity * (1f - decay) / 9f);
                _pcScrollVelocity *= decay;
                if (Mathf.Abs(_pcScrollVelocity) < 5f || scroll.y <= 0f) _pcScrollVelocity = 0f;
                _pcScrollExpected = scroll.y; _pcScrollExpectedValid = true;
            }
            return scroll;
        }

        Vector2 BeginCustomScroll(Vector2 scroll, Rect outerRect, float measuredContentH)
        {
            // Unity measures the full layout, including wrapped labels and dynamic mod controls.
            // Preserve Unity input handling; only the scrollbar visuals are customized.
            GUISkin scrollSkin = GetScrollSkin();
            _scrollSkinStack.Push(GUI.skin);
            GUI.skin = scrollSkin;
            EventType inputType = Event.current.type;
            if (!_vrInDraw && inputType == EventType.MouseDown && Event.current.button == 0)
            {
                Rect viewport = new Rect(GUIUtility.GUIToScreenPoint(Vector2.zero), outerRect.size);
                Vector2 mouse = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                _pcBarDragging = viewport.Contains(mouse) && mouse.x >= viewport.xMax - 22f * UI.Scale;
                _pcBarVelocity = 0f; _pcBarPosition = scroll.y; _pcBarSampleTime = Time.unscaledTime;
            }
            scroll = HandlePcContentGesture(scroll, outerRect);
            scroll = SmoothPcScroll(scroll, outerRect);
            scroll = BeginVrScrollState(scroll);
            scroll = GUILayout.BeginScrollView(scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Width(outerRect.width), GUILayout.Height(outerRect.height));
            if (!_vrInDraw && _pcBarDragging)
            {
                _pcScrollExpectedValid = false;
                if (inputType == EventType.MouseDrag)
                {
                    float dt = Mathf.Max(0.001f, Time.unscaledTime - _pcBarSampleTime);
                    float speed = Mathf.Clamp((scroll.y - _pcBarPosition) / dt, -4000f * UI.Scale, 4000f * UI.Scale);
                    _pcBarVelocity = Mathf.Lerp(_pcBarVelocity, speed, 1f - Mathf.Exp(-dt / 0.04f));
                    _pcBarPosition = scroll.y; _pcBarSampleTime = Time.unscaledTime;
                }
                if (inputType == EventType.MouseUp || inputType == EventType.Ignore)
                {
                    _pcBarDragging = false;
                    _pcScrollVelocity = Time.unscaledTime - _pcBarSampleTime < 0.1f ? _pcBarVelocity : 0f;
                }
            }
            // Keep the glass rim and shadow inside the scroll viewport on both sides.
            int inset = Mathf.CeilToInt(12f * UI.Scale);
            var contentStyle = new GUIStyle(GUIStyle.none)
            {
                padding = new RectOffset(inset, inset, inset, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            GUILayout.BeginVertical(contentStyle, GUILayout.ExpandWidth(true));
            return scroll;
        }

        float EndCustomScroll(Vector2 scroll, Rect outerRect, ref float contentHRef, int scrollId)
        {
            GUILayout.Space(12f * UI.Scale);
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint)
                contentHRef = GUILayoutUtility.GetLastRect().height;
            GUILayout.EndScrollView();
            EndVrScrollState();
            GUI.skin = _scrollSkinStack.Pop();
            return contentHRef;
        }

        void DrawHome(Rect outerRect)
        {
            _scrollPage = BeginCustomScroll(_scrollPage, outerRect, _homeContentH);
            Space(4);
            Label(I18n.T("Core.glass_welcome"), Theme.Text, 25, true);
            Space(20);
            DrawHomeOverview(_pages.Count, _pages.Count(page => page.Enabled));
            Space(18);
            DrawHomeShortcut(1, I18n.T("Core.mods_list"), I18n.T("Core.glass_mods_hint"), -2);
            Space(8);
            DrawHomeShortcut(2, I18n.T("Core.glass_general_settings"), I18n.T("Core.glass_settings_hint"), -4);
            Space(18);
            Label(I18n.T("Core.quick_access"), Theme.Dim, 9);
            Space(6);

            DrawQuickToggle(I18n.T("Core.quick_vr"), MenuPrefs.GetInt("MUM_VREnabled", 1) == 1, value => {
                MenuPrefs.SetInt("MUM_VREnabled", value ? 1 : 0);
                if (!value) GorillaInterface.Instance?.CloseCast();
            }, 9);
            DrawQuickAction(I18n.T("Core.language"), LangName(I18n.Current), () => { _langOpen = true; _settingsAnchor = "Core.language"; NavigateTo(-4); }, 11);
            DrawQuickAction(I18n.T("Core.quick_theme"), ThemeName(Theme.Current), () => { _themeOpen = true; _settingsAnchor = "Core.theme_current"; NavigateTo(-4); }, Theme.Current == ThemePreset.Clair ? 12 : 13);
            Space(14);
            Label(I18n.T("Core.home_author"), Theme.Dim, 9);
            EndCustomScroll(_scrollPage, outerRect, ref _homeContentH, 1);
        }

        void DrawQuickToggle(string label, bool value, Action<bool> change, int icon = -1)
        {
            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(39f * UI.Scale), GUILayout.ExpandWidth(true));
            UI.DrawGlass(row, MenuStyle.Surface, Mathf.RoundToInt(row.height * 0.5f));
            if (icon >= 0) DrawMenuIcon(new Rect(row.x + 12f * UI.Scale, row.center.y - 9f * UI.Scale, 18f * UI.Scale, 18f * UI.Scale), icon);
            GUI.Label(new Rect(row.x + (icon >= 0 ? 39f : 13f) * UI.Scale, row.y, row.width - 98f * UI.Scale, row.height), label, UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleLeft));
            DrawSwitchAt(new Rect(row.xMax - 59f * UI.Scale, row.y + 8f * UI.Scale, 46f * UI.Scale, 23f * UI.Scale), value);
            if (ControlButton(row, new GUIContent("", label), GUIStyle.none)) change(!value);
            Space(5);
        }
        void DrawQuickAction(string label, string value, Action action, int icon = -1)
        {
            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(39f * UI.Scale), GUILayout.ExpandWidth(true));
            UI.DrawGlass(row, MenuStyle.Surface, Mathf.RoundToInt(row.height * 0.5f));
            if (icon >= 0) DrawMenuIcon(new Rect(row.x + 12f * UI.Scale, row.center.y - 9f * UI.Scale, 18f * UI.Scale, 18f * UI.Scale), icon);
            GUI.Label(new Rect(row.x + (icon >= 0 ? 39f : 13f) * UI.Scale, row.y, row.width * 0.4f - 26f * UI.Scale, row.height), label, UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleLeft));
            GUI.Label(new Rect(row.x + row.width * 0.4f, row.y, row.width * 0.6f - 18f * UI.Scale, row.height), value + "  ›", UI.Sty(Theme.Dim, 10, false, TextAnchor.MiddleRight));
            if (ControlButton(row, new GUIContent("", label), GUIStyle.none)) action();
            Space(5);
        }

        void DrawHomeShortcut(int icon, string title, string subtitle, int target)
        {
            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(76f * UI.Scale), GUILayout.ExpandWidth(true));
            bool hover = row.Contains(Event.current.mousePosition);
            UI.DrawGlass(row, hover ? MenuStyle.Raised : MenuStyle.Surface, Mathf.RoundToInt(20f * UI.Scale));
            float size = 30f * UI.Scale;
            var image = new Rect(row.x + 18f * UI.Scale, row.center.y - size * 0.5f, size, size);
            DrawMenuIcon(image, icon);
            float x = image.xMax + 16f * UI.Scale;
            GUI.Label(new Rect(x, row.y + 13f * UI.Scale, row.xMax - x - 30f * UI.Scale, 25f * UI.Scale), title, UI.Sty(Theme.Text, 13, true));
            GUI.Label(new Rect(x, row.y + 39f * UI.Scale, row.xMax - x - 30f * UI.Scale, 23f * UI.Scale), subtitle, UI.Sty(Theme.Dim, 10));
            DrawMenuIcon(new Rect(row.xMax - 30f * UI.Scale, row.center.y - 9f * UI.Scale, 18f * UI.Scale, 18f * UI.Scale), 3, 180f);
            if (ControlButton(row, new GUIContent("", title), GUIStyle.none)) NavigateTo(target);
        }

        string _settingsAnchor;
        void DrawPreferences(Rect outerRect)
        {
            _scrollSettings = BeginCustomScroll(_scrollSettings, outerRect, _settingsContentH);

            Space(8); Section(I18n.T("Core.personalization"));
            BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("Core.size_label"), UI.Sty(Theme.Text, 11), GUILayout.Width(55 * UI.Scale));
            if (MinusBtn()) { _scale = Mathf.Max(0.5f, _scale - 0.1f); MenuPrefs.SetFloat("MUM_UIScale", _scale); MenuPrefs.Save(); ComputeRect(); }
            GUILayout.Label(_scale.ToString("0.0x"), UI.Sty(Theme.Text, 11, true, TextAnchor.MiddleCenter), GUILayout.Width(40 * UI.Scale));
            if (PlusBtn()) { _scale = Mathf.Min(2f, _scale + 0.1f); MenuPrefs.SetFloat("MUM_UIScale", _scale); MenuPrefs.Save(); ComputeRect(); }
            GUILayout.EndHorizontal();
            Space(4);
            UI.TextScale = Slider(I18n.T("Core.text_size"), UI.TextScale, .5f, 1.5f, "0.00x", 120f);
            Space(4);
            if (WaitingKey) { Label(I18n.T("Core.waiting_key"), Theme.Accent, 12, true); if (Btn(I18n.T("Core.cancel"), TB3, Color.white)) { WaitingKey = false; RebindTarget = null; } }
            else { if (RebindBtn(I18n.T("Core.menu_key"), _keyMenu)) StartRebind("KeyMenu"); }
            if (_keyHistory.Count > 0) { Space(4); Label(I18n.T("Core.last_key") + " " + _keyHistory[0], Theme.Accent, 10, true); }
            EndCard();

            Space(6);

            ExpandableSection(I18n.T("Core.theme_current"), ThemeName(Theme.Current), ref _themeOpen, DrawThemeChoices);

            ExpandableSection(I18n.T("Core.language"), LangName(I18n.Current), ref _langOpen, () =>
            {
                foreach (Lang lang in System.Enum.GetValues(typeof(Lang)))
                {
                    bool sel = I18n.Current == lang;
                    if (Btn(LangName(lang), TB3, sel ? Theme.Accent : Theme.Text))
                        I18n.Current = lang;
                    Space(6);
                }
            });

            ExpandableSection(I18n.T("Core.font_family"), UI.FontFamilies[UI.FontFamily], ref _fontOpen, () =>
            {
                for(int family=0;family<UI.FontFamilies.Length;family++)
                {
                    if(Btn(UI.FontFamilies[family],TB3,UI.FontFamily==family?Theme.Accent:Theme.Text,font:UI.FontForFamily(family,true)))UI.FontFamily=family;
                    Space(6);
                }
            });

            BeginCard();
            bool newBoldText = Toggle(I18n.T("Core.bold_text_label"), Theme.BoldText, 150f);
            if (newBoldText != Theme.BoldText) Theme.BoldText = newBoldText;
            EndCard();

            BeginCard();
            SoundFX.Volume = Slider(I18n.T("Core.sound_volume_label"), SoundFX.Volume, 0f, 1f, "0.00", 130f);
            EndCard();

            // ---- Interface VR (menu in-game) : réglages intégrés directement dans
            // les Reglages PC, jamais une page de mod separee. ----
            if (_gorillaInterface != null)
            {
                Space(8);
                _gorillaInterface.DrawSettingsPage(this);
            }



            CrashReporter.Instance?.Draw(this);
            EndCustomScroll(_scrollSettings, outerRect, ref _settingsContentH, 1);
        }

        internal static string LangName(Lang l)
        {
            switch (l)
            {
                case Lang.Fr: return "Français";
                case Lang.En: return "English";
                case Lang.De: return "Deutsch";
                case Lang.Es: return "Español";
                case Lang.It: return "Italiano";
                case Lang.Pt: return "Português";
                case Lang.Ja: return "日本語";
                case Lang.Ru: return "Русский";
                case Lang.Nl: return "Nederlands";
                case Lang.Pl: return "Polski";
                case Lang.Tr: return "Türkçe";
                case Lang.Uk: return "Українська";
                case Lang.Cs: return "Čeština";
                case Lang.Ro: return "Română";
                case Lang.Sv: return "Svenska";
                case Lang.Da: return "Dansk";
                case Lang.Fi: return "Suomi";
                case Lang.Hu: return "Magyar";
                default: return "English";
            }
        }

        static string ThemeName(ThemePreset p)
        {
            if (p != ThemePreset.Custom) return I18n.T(p == ThemePreset.Clair ? "Core.theme_light" : "Core.theme_dark");
            switch (p)
            {
                case ThemePreset.SombreVert: return I18n.T("Core.theme_sombre_vert");
                case ThemePreset.Clair: return I18n.T("Core.theme_clair");
                case ThemePreset.SombreViolet: return I18n.T("Core.theme_sombre_violet");
                case ThemePreset.SombreBleu: return I18n.T("Core.theme_sombre_bleu");
                default: return I18n.T("Core.theme_custom");
            }
        }

        Color ColorField(string label, Color c)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UI.Sty(Theme.Text, 9), GUILayout.Width(90 * UI.Scale));
            GUI.color = c; GUILayout.Box("", GUILayout.Width(20), GUILayout.Height(16)); GUI.color = Color.white;
            GUILayout.EndHorizontal();
            c.r = Slider("R", c.r, 0f, 1f, "0.00", 20f);
            c.g = Slider("G", c.g, 0f, 1f, "0.00", 20f);
            c.b = Slider("B", c.b, 0f, 1f, "0.00", 20f);
            return c;
        }

        void DrawModes(Rect outerRect)
        {

            _scrollModes = BeginCustomScroll(_scrollModes, outerRect, _modesContentH);

            Space(12);
            Label(I18n.T("Core.mods_list"), Theme.Text, 16, true);
            Space(10);

            float searchH = Mathf.Max(42f * UI.Scale, UI.Px(11) + 20f * UI.Scale);
            GUILayout.BeginHorizontal(GUILayout.Height(searchH));
            Rect searchBg = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(searchH));
            float focus = MenuStyle.Animate("search-focus", GUI.GetNameOfFocusedControl() == "mum_search" ? 1f : 0f);
            UI.DrawGlass(searchBg, Color.Lerp(MenuStyle.Input, MenuStyle.ActiveSurface, focus * .5f), Mathf.RoundToInt(searchH * 0.5f));
            float iconSize = 19f * UI.Scale;
            DrawMenuIcon(new Rect(searchBg.x + 14f * UI.Scale, searchBg.center.y - iconSize * .5f, iconSize, iconSize), 18);
            GUILayout.EndHorizontal();
            Rect searchInner = new Rect(searchBg.x + 44f * UI.Scale, searchBg.y, Mathf.Max(10f, searchBg.width - 54f * UI.Scale - searchH), searchBg.height);
            GUI.SetNextControlName("mum_search");
            var searchFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = UI.Px(11),
                fontStyle = FontStyle.Normal,
                font = UI.FontFor(Theme.BoldText),
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Theme.Text, background = null },
                focused = { textColor = Theme.Text, background = null },
                hover = { textColor = Theme.Text, background = null },
                active = { textColor = Theme.Text, background = null },
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            searchFieldStyle.onNormal.textColor = Theme.Text;
            searchFieldStyle.onFocused.textColor = Theme.Text;
            searchFieldStyle.onHover.textColor = Theme.Text;
            searchFieldStyle.onActive.textColor = Theme.Text;
            _search = ControlTextField(searchInner, _search, searchFieldStyle, false);
            if (string.IsNullOrEmpty(_search))
                GUI.Label(searchInner, I18n.T("Core.search_label"), UI.Sty(UI.SecondaryText, 11, false, TextAnchor.MiddleLeft));
            if (!string.IsNullOrEmpty(_search))
            {
                Rect xRect = new Rect(searchBg.x + searchBg.width - searchH, searchBg.y, searchH, searchH);
                if (IconButton(xRect, 7, I18n.T("Core.clear_search")))
                    _search = "";
            }
            Space(10);

            string s = _search.Trim().ToLowerInvariant();
            var indices = new List<int>();
            for (int i = 0; i < _pages.Count; i++)
            {
                var p = _pages[i];
                string catDisplay = CategoryNames.Get(p.Category).ToLower();
                string titleDisplay = I18n.T(p.Title).ToLower();
                if (!string.IsNullOrEmpty(s) &&
                    !titleDisplay.Contains(s) &&
                    !catDisplay.Contains(s)) continue;
                indices.Add(i);
            }
            indices.Sort((a, b) =>
            {
                int pinned = _pages[b].PinToTop.CompareTo(_pages[a].PinToTop);
                if(pinned!=0)return pinned;
                int catCmp = _pages[a].Category.CompareTo(_pages[b].Category);
                return catCmp != 0 ? catCmp : string.Compare(I18n.T(_pages[a].Title), I18n.T(_pages[b].Title), System.StringComparison.OrdinalIgnoreCase);
            });

            bool any = indices.Count > 0;
            int shown = 0;
            MenuCategory lastCategory = (MenuCategory)(-1);
            foreach (int i in indices)
            {
                var p = _pages[i];

                if (p.Category != lastCategory)
                {
                    lastCategory = p.Category;
                    shown++;
                    {
                        Space(shown > 1 ? 8f : 0f);
                        Color catTitleCol = UI.SecondaryText;
                        GUILayout.Label(CategoryNames.Get(p.Category), new GUIStyle(GUI.skin.label)
                        {
                            fontSize = UI.Px(10),
                            fontStyle = FontStyle.Normal,
                            font = UI.FontFor(true),
                            normal = { textColor = catTitleCol }
                        });
                        Space(2);
                    }
                }
                else shown++;

                GUILayout.BeginHorizontal();

                string pTitleDisplay = I18n.T(p.Title);

                float rightReserved = 96f * UI.Scale;

                Rect rowRect = GUILayoutUtility.GetRect(new GUIContent(pTitleDisplay),
                    new GUIStyle(GUI.skin.button)
                    {
                        fontSize = UI.Px(11),
                        fontStyle = FontStyle.Normal,
                        font = UI.FontFor(Theme.BoldText),
                        padding = new RectOffset(14, 14, 9, 9),
                        wordWrap = true
                    }, GUILayout.ExpandWidth(true), GUILayout.MinHeight(46 * UI.Scale));

                bool modEnabled = p.Enabled;
                Color rowBg = modEnabled ? MenuStyle.ActiveSurface : MenuStyle.Surface;
                bool rowHover = Event.current != null && rowRect.Contains(Event.current.mousePosition);
                if (rowHover) rowBg = Color.Lerp(rowBg, Theme.Text, 0.06f);
                UI.DrawGlass(rowRect, rowBg, Mathf.RoundToInt(MenuStyle.Radius * UI.Scale));

                Color rowText = Theme.Text;
                var rowStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = UI.Px(11),
                    fontStyle = FontStyle.Normal,
                    font = UI.FontFor(Theme.BoldText),
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(14, Mathf.RoundToInt(rightReserved + 6f * UI.Scale), 9, 9),
                    wordWrap = true,
                    normal = { textColor = rowText }
                };
                GUI.Label(rowRect, pTitleDisplay, rowStyle);

                Rect stateRect = new Rect(rowRect.xMax - rightReserved + 8f * UI.Scale, rowRect.center.y - 11f * UI.Scale, 42f * UI.Scale, 22f * UI.Scale);
                if(p.HasActivation) DrawSwitchAt(stateRect, modEnabled);
                if (p.HasActivation && ControlButton(stateRect, new GUIContent("", p.Title + ": toggle"), GUIStyle.none))
                {
                    p.Enabled = !modEnabled;
                    SoundFX.PlaySound(p.Enabled ? "Aigue" : "Grave");
                    if (Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint) Event.current.Use();
                }
                DrawMenuIcon(new Rect(rowRect.xMax - 25f * UI.Scale, rowRect.center.y - 8f * UI.Scale, 16f * UI.Scale, 16f * UI.Scale), 3, 180f);
                Rect settingsRect = new Rect(rowRect.x,rowRect.y,Mathf.Max(0f,stateRect.x-rowRect.x-4f*UI.Scale),rowRect.height);
                Rect arrowRect = new Rect(stateRect.xMax+4f*UI.Scale,rowRect.y,rowRect.xMax-stateRect.xMax-4f*UI.Scale,rowRect.height);
                bool openSettings = ControlButton(settingsRect, new GUIContent("", p.Title), GUIStyle.none);
                bool openArrow = ControlButton(arrowRect, new GUIContent("", p.Title + ": settings"), GUIStyle.none);
                if (openSettings || openArrow) NavigateTo(i);

                GUILayout.EndHorizontal();
                Space(4);
            }
            if (!any) { BeginCard(); Label(I18n.T("Core.no_mod_found"), Theme.Dim, 10); EndCard(); }

            EndCustomScroll(_scrollModes, outerRect, ref _modesContentH, 2);
        }

        string _drawingPageTitle;

        void DrawActivePage(Page page, Rect outerRect)
        {
            string backLabel = I18n.T("Core.vr_back_mods");
            var titleStyle = UI.Sty(Theme.Text, 22, true);
            titleStyle.wordWrap = false;
            float titleWidth = Mathf.Max(40f, outerRect.width - 176f * UI.Scale);
            string title = I18n.T(page.Title);
            while (titleStyle.fontSize > 11 && titleStyle.CalcSize(new GUIContent(title)).x > titleWidth) titleStyle.fontSize--;
            float titleHeight = Mathf.Max(60f * UI.Scale, titleStyle.CalcHeight(new GUIContent(title), titleWidth) + 24f * UI.Scale);
            Rect header = new Rect(12f * UI.Scale, 4f * UI.Scale, outerRect.width - 24f * UI.Scale, titleHeight);
            UI.DrawGlass(header, MenuStyle.Surface, Mathf.RoundToInt(18f * UI.Scale));
            float backHeight = 36f * UI.Scale;
            Rect back = new Rect(header.x + 10f * UI.Scale, header.center.y - backHeight * .5f, backHeight, backHeight);
            if (IconButton(back, 3, backLabel)) NavigateTo(-2);
            Color divider = Theme.Text; divider.a = .18f;
            UI.DrawRounded(new Rect(back.xMax + 7f * UI.Scale, header.center.y - 12f * UI.Scale, UI.Scale, 24f * UI.Scale), divider, 0);
            titleStyle.padding = new RectOffset(0,0,0,0);
            GUI.Label(new Rect(back.xMax + 20f * UI.Scale, header.y, titleWidth, header.height), title, titleStyle);
            Rect toggle = new Rect(header.xMax - 62f * UI.Scale, header.center.y - 13f * UI.Scale, 48f * UI.Scale, 26f * UI.Scale);
            bool enabled = page.Enabled;
            if(page.HasActivation) DrawSwitchAt(toggle, enabled);
            if (page.HasActivation && ControlButton(toggle, new GUIContent("", I18n.T("Core.vr_mod_enabled")), GUIStyle.none))
            {
                page.Enabled = !enabled;
                SoundFX.PlaySound(page.Enabled ? "Aigue" : "Grave");
            }
            float headerHeight = header.yMax + 26f * UI.Scale;
            Rect body = new Rect(0f, headerHeight, outerRect.width, Mathf.Max(40f, outerRect.height - headerHeight));
            GUILayout.BeginArea(body);
            _scrollPage = BeginCustomScroll(_scrollPage, body, _pageContentH);
            DrawModPresentation(page.Title);

            _drawingPageTitle = page.Title;
            try { page.Draw?.Invoke(this); }
            finally { _drawingPageTitle = null; }

            // Rendu automatique des Settings unifiés (même source que le VR) : chaque mod
            // qui utilise RegisterPageWithSettings voit ses réglages apparaître ici sans
            // code IMGUI à écrire.
            if (page.Settings != null && page.Settings.Count > 0)
            {
                BeginCard();
                foreach (var s in page.Settings)
                {
                    s.DrawPC(this);
                    Space(4);
                }
                EndCard();
            }
            EndCustomScroll(_scrollPage, body, ref _pageContentH, 3);
            GUILayout.EndArea();
        }

        Texture2D _colorWheelTexture;
        public bool ColorPickerHSV(ref Color color, string label = "Couleur", float sv = 200f, float hw = 20f, bool compact = false)
        {
            Label(label, Theme.Text, 10);
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            if (_colorWheelTexture == null)
            {
                const int size = 128;
                _colorWheelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    Vector2 delta = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f);
                    float radius = delta.magnitude / (size / 2f - 1f);
                    Color pixel = Color.HSVToRGB(Mathf.Repeat(Mathf.Atan2(delta.y, delta.x) / (2f * Mathf.PI), 1f), Mathf.Clamp01(radius), 1f);
                    pixel.a = Mathf.Clamp01((1f - radius) * 64f); pixels[y * size + x] = pixel;
                }
                _colorWheelTexture.SetPixels(pixels); _colorWheelTexture.Apply(false, true);
                _colorWheelTexture.filterMode = FilterMode.Bilinear;
            }
            float diameter = 140f * UI.Scale;
            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(diameter), GUILayout.ExpandWidth(true));
            Rect wheel = new Rect(row.center.x - diameter / 2f, row.y, diameter, diameter);
            GUI.DrawTexture(wheel, _colorWheelTexture);
            if (compact)
            {
                Rect brightness = new Rect(wheel.xMax + 20f * UI.Scale, wheel.y, 22f * UI.Scale, wheel.height);
                float nextValue = VerticalBrightness(brightness, value, Color.HSVToRGB(hue, saturation, 1f));
                if (!Mathf.Approximately(value, nextValue)) { value = nextValue; color = Color.HSVToRGB(hue, saturation, value); }
            }
            IgnorePcGesture(wheel);
            int control = GUIUtility.GetControlID(FocusType.Passive, wheel);
            var ev = Event.current;
            bool changed = compact && color != Theme.Accent;
            if (!_vrInDraw && ev.type == EventType.MouseDown && ev.button == 0 && Vector2.Distance(ev.mousePosition, wheel.center) <= diameter / 2f) GUIUtility.hotControl = control;
            if (!_vrInDraw && GUIUtility.hotControl == control && (ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag))
            {
                Vector2 delta = ev.mousePosition - wheel.center;
                hue = Mathf.Repeat(Mathf.Atan2(-delta.y, delta.x) / (2f * Mathf.PI), 1f);
                saturation = Mathf.Clamp01(delta.magnitude / (diameter / 2f));
                changed = true; ev.Use();
            }
            if (!_vrInDraw && ev.type == EventType.MouseUp && GUIUtility.hotControl == control) { GUIUtility.hotControl = 0; ev.Use(); }
            // Small hue targets provide the same wheel through the VR ray controls.
            if (_vrInDraw) for (int i = 0; i < 12; i++)
            {
                float h = i / 12f, angle = h * Mathf.PI * 2f;
                Vector2 center = wheel.center + new Vector2(Mathf.Cos(angle), -Mathf.Sin(angle)) * diameter * 0.38f;
                Rect target = new Rect(center.x - diameter * 0.09f, center.y - diameter * 0.09f, diameter * 0.18f, diameter * 0.18f);
                if (ControlButton(target, new GUIContent("", label + " " + i), GUIStyle.none)) { hue = h; saturation = 0.8f; changed = true; }
            }
            float newSaturation = compact ? saturation : Slider(I18n.T("Core.color_saturation"), saturation, 0f, 1f, "0.00");
            float newValue = compact ? value : Slider(I18n.T("Core.color_brightness"), value, 0.15f, 1f, "0.00");
            float angleSelected = hue * Mathf.PI * 2f;
            Vector2 marker = wheel.center + new Vector2(Mathf.Cos(angleSelected), -Mathf.Sin(angleSelected)) * saturation * diameter * .5f;
            UI.DrawRounded(new Rect(marker.x - 5f * UI.Scale, marker.y - 5f * UI.Scale, 10f * UI.Scale, 10f * UI.Scale), Color.white, UI.Px(5));
            UI.DrawRounded(new Rect(marker.x - 3f * UI.Scale, marker.y - 3f * UI.Scale, 6f * UI.Scale, 6f * UI.Scale), Color.HSVToRGB(hue, saturation, value), UI.Px(3));
            changed |= !Mathf.Approximately(newSaturation, saturation) || !Mathf.Approximately(newValue, value);
            color = Color.HSVToRGB(hue, newSaturation, newValue);
            return changed;
        }

        public Texture2D T(Color c) => UI.MakeTex(c);
        public float WinW => _rect.width;
        public float WinH => _rect.height;
    }

    // ================================================================
    // GORILLAINTERFACE — menu VR world-space, fusionné dans ce même fichier
    // à la demande : plus qu'un seul fichier pour tout UltimateMenu.
    // ================================================================
    [DefaultExecutionOrder(10001)]
    public partial class GorillaInterface : MonoBehaviour
    {
        public static GorillaInterface Instance { get; private set; }
        public bool Enabled => true;
        const string PREF_SCALE = "MUM_GI_Scale", PREF_DISTANCE = "MUM_GI_Distance", PREF_ANIMSPEED = "MUM_GI_AnimSpeed", PREF_STICKSENS = "MUM_GI_StickSens";
        const string PREF_SHAKE_THRESHOLD = "MUM_GI_ShakeThreshold", PREF_SHAKE_WINDOW = "MUM_GI_ShakeWindow";
        const float MinUserScale = 0.75f, MaxUserScale = 1.25f, MinDistance = 0.5f, MaxDistance = 1.25f;
        const float MinAnimSpeed = 1f, MaxAnimSpeed = 8f, MinStickSens = 0.3f, MaxStickSens = 0.9f;
        const float MinShakeThreshold = 0.2f, MaxShakeThreshold = 0.9f, MinShakeWindow = 0.1f, MaxShakeWindow = 1f;
        const float StickDeadzone = 0.2f, PanelW = 640f;
        float _userScale = .55f, _spawnDistance = 1f, _animSpeed = 3.5f, _stickSens = 0.6f;
        public bool MenuLeft => MenuPrefs.GetInt("MUM_VR_MenuLeft", 0) == 1;
        public void SetMenuLeft(bool left)
        {
            MenuPrefs.SetInt("MUM_VR_MenuLeft", left ? 1 : 0);
            _shakeState = ShakeState.Neutral; _shakeStateTime = 0f;
        }
        float _shakeThreshold = 0.5f, _shakeWindow = 0.35f, _shakeStateTime, _repeat;
        readonly float _followSmooth = 8f, _heightOffset = -0.05f;
        enum ShakeState { Neutral, WentDown, WentUp }
        ShakeState _shakeState;
        bool _open, _anchored, _triggerHeld, _backHeld;
        Transform _playerHead;
        bool _openingActive;
        float _openingTime;
        Vector3 _targetPos;
        Quaternion _targetRot;
        GameObject _root;
        RawImage _castImage;
        Image _vrBackdrop;
        Material _vrPanelMaterial;
        Sprite _vrBackdropSprite;
        Vector2 _debugStickL, _debugStickR;
        internal bool CastRequested => _open && _playerHead != null && MenuPrefs.GetInt("MUM_VREnabled", 1) == 1;
        internal void CloseCast() => CloseMenu();
        void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; Destroy(this); return; }
            Instance = this; DontDestroyOnLoad(gameObject); LoadSettings();
            _pointerLeft = MenuPrefs.GetInt("MUM_VR_PointerLeft", 0) == 1;
        }
        void Start()
        {
            _root = new GameObject("UltimateMenu_VR", typeof(RectTransform), typeof(Canvas));
            DontDestroyOnLoad(_root);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.overrideSorting = true; canvas.sortingOrder = 1000;
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(PanelW, PanelW);
            var backdrop = new GameObject("MenuBackdrop", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(_root.transform, false);
            _vrBackdrop = backdrop.GetComponent<Image>(); _vrBackdrop.raycastTarget = false;
            var roundTexture = UI.MakeRoundedTex(Color.white, 64, 64, 8);
            _vrBackdropSprite = Sprite.Create(roundTexture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(8f, 8f, 8f, 8f));
            _vrBackdrop.sprite = _vrBackdropSprite; _vrBackdrop.type = Image.Type.Sliced;
            var image = new GameObject("MenuUI", typeof(RectTransform), typeof(RawImage));
            image.transform.SetParent(_root.transform, false);
            _castImage = image.GetComponent<RawImage>(); _castImage.raycastTarget = false;
            // Private material: do not change Unity's shared UI material or other mods.
            _vrPanelMaterial = new Material(_castImage.material) { renderQueue = 3000 };
            _vrPanelMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            _castImage.material = _vrPanelMaterial; _vrBackdrop.material = _vrPanelMaterial;
            SetupMenuBody();
            _root.SetActive(false);
            GorillaTagger.OnPlayerSpawned(FindHead); FindHead();
        }
        internal void PresentCast(RenderTexture texture, Rect crop, Rect menuRect)
        {
            if (_castImage == null || !CastRequested || crop.width <= 0 || crop.height <= 0) return;
            _castImage.texture = texture;
            _castImage.uvRect = new Rect(crop.x / texture.width, 1f - crop.yMax / texture.height, crop.width / texture.width, crop.height / texture.height);
            _castImage.rectTransform.sizeDelta = new Vector2(PanelW, PanelW * crop.height / crop.width);
            _pointerCrop = crop;
            var panelRect = _castImage.rectTransform.rect;
            _vrBackdrop.rectTransform.sizeDelta = new Vector2(menuRect.width / crop.width * panelRect.width,
                menuRect.height / crop.height * panelRect.height);
            _vrBackdrop.rectTransform.anchoredPosition = new Vector2(
                panelRect.xMin + (menuRect.center.x - crop.x) / crop.width * panelRect.width,
                panelRect.yMin + (1f - (menuRect.center.y - crop.y) / crop.height) * panelRect.height);
            _vrBackdrop.color = new Color(Theme.BG.r, Theme.BG.g, Theme.BG.b, 1f);
            _menuCollider.size = new Vector3(panelRect.width, panelRect.height, 20f);
        }
        internal void FailCast(Exception error)
        {
            Log.Err("VR menu projection", error); CloseMenu();
        }
        void Update()
        {
            if (_root == null) return;
            if (_playerHead == null && !IsInvoking(nameof(FindHead))) FindHead();
            var input = ControllerInputPoller.instance;
            _debugStickL = input != null ? ApplyDeadzone(input.leftControllerPrimary2DAxis) : Vector2.zero;
            _debugStickR = input != null ? ApplyDeadzone(input.rightControllerPrimary2DAxis) : Vector2.zero;
            SelectPointerHand(input);
            bool menuInputBlocked = MenuInputBlocked(input);
            if (menuInputBlocked) MenuUI.Instance?.CancelVrInput();
            if (!menuInputBlocked && !_pointerHasHit) UpdateShakeToggle(MenuLeft ? _debugStickL : _debugStickR);
            bool trigger = input != null && input.leftControllerTriggerButton;
            bool back = input != null && input.leftControllerPrimaryButton;
            if (CastRequested && !FrozenCast && !menuInputBlocked && _menuHeldHand == null)
            {
                if (back && !_backHeld) MenuUI.Instance?.BackVr();
            }
            _triggerHeld = trigger; _backHeld = back;
            bool visible = CastRequested && _castImage.texture != null;
            _root.SetActive(visible);
            if (visible) { UpdateFollow(); UpdateOpeningAnimation(); _root.transform.localScale = Vector3.one * (0.001f * _userScale); }
            UpdateFingerPointer(visible, input);
            UpdateMenuGrab(visible, input);
            if(_open&&_anchored&&_playerHead!=null&&
                (_playerHead.position-_root.transform.position).sqrMagnitude>20f*20f)
                CloseMenu();
        }
        void OpenMenu()
        {
            ResetMenuBody();
            _pointerPinned = _pointerDragging = false;
            _open = true; _anchored = false; _repeat = 0;
            _openingActive = true; _openingTime = -1f;
            MenuUI.Instance?.ResetVrFocus(); SoundFX.PlaySound("Aigue");
        }
        internal void CloseForCapture(){if(_open)CloseMenu();}
        void CloseMenu()
        {
            ResetMenuBody();
            _open = false; _openingActive = false;
            if (_root != null) _root.SetActive(false);
            MenuUI.Instance?.CancelVrInput(); SoundFX.PlaySound("Grave");
        }
        public void RebuildTheme() { } // The shared PC renderer reads Theme directly.
        void OnDestroy()
        {
            DestroyFingerPointer();
            RestoreFrozenMenu();
            if (_vrPanelMaterial != null) Destroy(_vrPanelMaterial);
            if (_vrBackdropSprite != null) Destroy(_vrBackdropSprite);
            CancelInvoke(); if (Instance == this) Instance = null;
            if (_root != null) { _root.SetActive(false); Destroy(_root); }
        }
        void LoadSettings()
        {
            _userScale = .55f * Mathf.Clamp(MenuPrefs.GetFloat("MUM_GI_ScaleFactor", 1f), MinUserScale, MaxUserScale);
            _spawnDistance = Mathf.Clamp(MenuPrefs.GetFloat(PREF_DISTANCE, 1f), MinDistance, MaxDistance);
            _animSpeed = MenuPrefs.GetFloat(PREF_ANIMSPEED, 3.5f);
            _stickSens = MenuPrefs.GetFloat(PREF_STICKSENS, 0.6f);
            _shakeThreshold = MenuPrefs.GetFloat(PREF_SHAKE_THRESHOLD, 0.5f);
            _shakeWindow = MenuPrefs.GetFloat(PREF_SHAKE_WINDOW, 0.35f);
        }
        void SaveSettings()
        {
            MenuPrefs.SetFloat("MUM_GI_ScaleFactor", _userScale / .55f);
            MenuPrefs.SetFloat(PREF_DISTANCE, _spawnDistance);
            MenuPrefs.SetFloat(PREF_ANIMSPEED, _animSpeed);
            MenuPrefs.SetFloat(PREF_STICKSENS, _stickSens);
            MenuPrefs.SetFloat(PREF_SHAKE_THRESHOLD, _shakeThreshold);
            MenuPrefs.SetFloat(PREF_SHAKE_WINDOW, _shakeWindow);
            MenuPrefs.Save();
        }
        void FindHead()
        {
            if (this == null || Instance != this) return;
            CancelInvoke(nameof(FindHead));
            _anchored = false;
            try
            {
                var rig = VRRig.LocalRig;
                if (rig != null)
                {
                    var head = rig.transform.Find("rig/head");
                    if (head != null) { _playerHead = head; return; }
                }
                var tagger = GorillaTagger.Instance;
                if (tagger != null && tagger.headCollider != null) _playerHead = tagger.headCollider.transform;
            }
            catch (Exception e) { Log.Err("GorillaInterface.FindHead", e); }
            if (_playerHead == null) Invoke(nameof(FindHead), 1f);
        }
        void UpdateFollow()
        {
            if (_pointerPinned) return;
            if (_playerHead == null) return;

            Vector3 flatForward = _playerHead.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            flatForward.Normalize();

            Vector3 desiredPos = _playerHead.position + flatForward * _spawnDistance + Vector3.up * _heightOffset;
            Quaternion desiredRot = Quaternion.LookRotation(flatForward, Vector3.up);

            if (!_anchored || MenuStyle.FastInterface)
            {
                _targetPos = desiredPos;
                _targetRot = desiredRot;
                _anchored = true;
            }
            else
            {
                _targetPos = Vector3.Lerp(_targetPos, desiredPos, Time.unscaledDeltaTime * _followSmooth * (_animSpeed / 3.5f));
                _targetRot = Quaternion.Slerp(_targetRot, desiredRot, Time.unscaledDeltaTime * _followSmooth * (_animSpeed / 3.5f));
            }

            SetMenuPose(_targetPos, _targetRot);
            _pointerPinned = true;
        }
        void UpdateOpeningAnimation()
        {
            if (MenuStyle.FastInterface) { _openingActive = false; if (_anchored) SetMenuPose(_targetPos, _targetRot); return; }
            if (!_openingActive || !_anchored || _menuHeldHand != null || _menuDropDeadline > 0f) return;
            if (_openingTime < 0f) _openingTime = Time.unscaledTime;
            float t = Mathf.Clamp01((Time.unscaledTime - _openingTime) / 0.22f);
            float remaining = Mathf.Pow(1f - t, 3f);
            SetMenuPose(_targetPos - Vector3.up * (0.12f * remaining), _targetRot);
            if (t >= 1f) _openingActive = false;
        }

        void UpdateShakeToggle(Vector2 stick)
        {
            if (_shakeStateTime > 0f)
            {
                _shakeStateTime -= Time.unscaledDeltaTime;
                if (_shakeStateTime <= 0f) _shakeState = ShakeState.Neutral;
            }

            float y = stick.y;

            switch (_shakeState)
            {
                case ShakeState.Neutral:
                    if (y <= -_shakeThreshold)
                    {
                        _shakeState = ShakeState.WentDown;
                        _shakeStateTime = _shakeWindow;
                    }
                    else if (y >= _shakeThreshold)
                    {
                        _shakeState = ShakeState.WentUp;
                        _shakeStateTime = _shakeWindow;
                    }
                    break;

                case ShakeState.WentDown:
                    if (y >= _shakeThreshold)
                    {
                        _shakeState = ShakeState.Neutral;
                        _shakeStateTime = 0f;
                        if (!_open) OpenMenu();
                    }
                    break;

                case ShakeState.WentUp:
                    if (y <= -_shakeThreshold)
                    {
                        _shakeState = ShakeState.Neutral;
                        _shakeStateTime = 0f;
                        if (_open) CloseMenu();
                    }
                    break;
            }
        }
        Vector2 ApplyDeadzone(Vector2 raw)
        {
            float mag = raw.magnitude;
            if (mag < StickDeadzone) return Vector2.zero;
            float scaled = (mag - StickDeadzone) / (1f - StickDeadzone);
            return raw.normalized * scaled;
        }
        bool _advancedSettingsOpen;
        public void DrawSettingsPage(MenuUI ui)
        {
            ui.Section(I18n.T("Core.vr_section"));

            ui.BeginCard();



            ui.Space(4);
            float newScale = ui.Slider(I18n.T("Core.vr_size_short"), _userScale / .55f, MinUserScale, MaxUserScale, "0.00x");
            if (!Mathf.Approximately(newScale * .55f, _userScale)) { _userScale = newScale * .55f; SaveSettings(); }

            ui.Space(4);
            float newDist = ui.Slider(I18n.T("Core.vr_distance_short"), _spawnDistance, MinDistance, MaxDistance, "0.00m");
            if (!Mathf.Approximately(newDist, _spawnDistance)) { _spawnDistance = newDist; SaveSettings(); }
            ui.EndCard();

            ui.ExpandableSection(I18n.T("VR.Advanced"), "", ref _advancedSettingsOpen, () =>
            {
                float anim = ui.Slider(I18n.T("Core.vr_animspeed_short"), _animSpeed, MinAnimSpeed, MaxAnimSpeed, "0.0");
                float threshold = ui.Slider(I18n.T("VR.GestureThreshold"), _shakeThreshold, MinShakeThreshold, MaxShakeThreshold, "0.00", 135f);
                float window = ui.Slider(I18n.T("VR.GestureDelay"), _shakeWindow, MinShakeWindow, MaxShakeWindow, "0.00s", 135f);
                if (!Mathf.Approximately(anim, _animSpeed) || !Mathf.Approximately(threshold, _shakeThreshold) || !Mathf.Approximately(window, _shakeWindow))
                { _animSpeed = anim; _shakeThreshold = threshold; _shakeWindow = window; SaveSettings(); }
            });
            ui.Space(4);
            ui.BeginCard();
            ui.DrawJoystickTutorial(_debugStickL, _debugStickR);
            ui.EndCard();
        }
    }
}
