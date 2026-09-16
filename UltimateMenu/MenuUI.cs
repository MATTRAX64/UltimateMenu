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
            get => PlayerPrefs.GetInt("MUM_SoundEnabled", 1) == 1;
            set { PlayerPrefs.SetInt("MUM_SoundEnabled", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static float Volume
        {
            get => PlayerPrefs.GetFloat("MUM_SoundVolume", 0.6f);
            set { PlayerPrefs.SetFloat("MUM_SoundVolume", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
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
            if (!Enabled) return;
            EnsureLoaded();
            if (_source == null) return;

            if (!System.Enum.TryParse(name, true, out Sound key)) return;
            if (!_clips.TryGetValue(key, out var clip) || clip == null) return;

            _source.PlayOneShot(clip, Volume);
        }
    }

    public static class Theme
    {
        public static Color Accent = new Color(0.10f, 0.90f, 0.40f, 1f);
        public static Color BG = new Color(0.11f, 0.11f, 0.115f, 0.94f);
        public static Color BG2 = new Color(0.16f, 0.16f, 0.17f, 0.9f);
        public static Color BG3 = new Color(0.22f, 0.22f, 0.23f, 0.95f);
        public static Color Text = new Color(0.95f, 0.95f, 0.96f, 1f);
        public static Color Dim = new Color(0.58f, 0.58f, 0.60f, 1f);

        public static readonly Color Green = new Color(0.10f, 0.80f, 0.30f, 1f);
        public static readonly Color Red = new Color(0.85f, 0.15f, 0.15f, 1f);
        public static readonly Color Yellow = new Color(0.85f, 0.70f, 0.05f, 1f);

        public static ThemePreset Current
        {
            get => (ThemePreset)PlayerPrefs.GetInt("MUM_ThemePreset", (int)ThemePreset.SombreVert);
            set { PlayerPrefs.SetInt("MUM_ThemePreset", (int)value); PlayerPrefs.Save(); }
        }

        public static float BgAlpha
        {
            get => PlayerPrefs.GetFloat("MUM_BgAlpha", 0.97f);
            set { PlayerPrefs.SetFloat("MUM_BgAlpha", Mathf.Clamp01(value)); PlayerPrefs.Save(); BG.a = Mathf.Clamp01(value); }
        }

        public static bool BoldText
        {
            get => PlayerPrefs.GetInt("MUM_BoldText", 1) == 1;
            set { PlayerPrefs.SetInt("MUM_BoldText", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        struct Preset { public Color Accent, BG, BG2, BG3, Text, Dim; }

        static Preset Get(ThemePreset p)
        {
            switch (p)
            {
                case ThemePreset.Clair:
                    return new Preset
                    {
                        Accent = new Color(0.15f, 0.55f, 0.95f, 1f),
                        BG = new Color(0.95f, 0.95f, 0.95f, 1f),
                        BG2 = new Color(0.85f, 0.85f, 0.85f, 1f),
                        BG3 = new Color(0.78f, 0.78f, 0.78f, 1f),
                        Text = new Color(0.08f, 0.08f, 0.08f, 1f),
                        Dim = new Color(0.35f, 0.35f, 0.35f, 1f)
                    };
                case ThemePreset.SombreViolet:
                    return new Preset
                    {
                        Accent = new Color(0.62f, 0.30f, 0.95f, 1f),
                        BG = new Color(0.08f, 0.08f, 0.10f, 0.97f),
                        BG2 = new Color(0.13f, 0.12f, 0.16f, 1f),
                        BG3 = new Color(0.19f, 0.17f, 0.23f, 1f),
                        Text = new Color(0.92f, 0.92f, 0.94f, 1f),
                        Dim = new Color(0.52f, 0.50f, 0.56f, 1f)
                    };
                case ThemePreset.SombreBleu:
                    return new Preset
                    {
                        Accent = new Color(0.20f, 0.60f, 0.95f, 1f),
                        BG = new Color(0.07f, 0.09f, 0.11f, 0.97f),
                        BG2 = new Color(0.11f, 0.14f, 0.17f, 1f),
                        BG3 = new Color(0.16f, 0.20f, 0.24f, 1f),
                        Text = new Color(0.90f, 0.93f, 0.96f, 1f),
                        Dim = new Color(0.48f, 0.54f, 0.58f, 1f)
                    };
                default:
                    return new Preset
                    {
                        Accent = new Color(0.10f, 0.90f, 0.40f, 1f),
                        BG = new Color(0.08f, 0.08f, 0.08f, 0.97f),
                        BG2 = new Color(0.12f, 0.12f, 0.12f, 1f),
                        BG3 = new Color(0.18f, 0.18f, 0.18f, 1f),
                        Text = new Color(0.92f, 0.92f, 0.92f, 1f),
                        Dim = new Color(0.50f, 0.50f, 0.50f, 1f)
                    };
            }
        }

        public static void Apply(ThemePreset p)
        {
            Current = p;
            if (p != ThemePreset.Custom)
            {
                var v = Get(p);
                Accent = v.Accent; BG = v.BG; BG2 = v.BG2; BG3 = v.BG3; Text = v.Text; Dim = v.Dim;
                BG.a = BgAlpha;
                SaveCustom();
            }
            MenuUI.Instance?.RebuildTex();
        }

        public static void SetAccent(float h, float s, float v)
        { Accent = Color.HSVToRGB(h, s, v); Current = ThemePreset.Custom; SaveCustom(); MenuUI.Instance?.RebuildTex(); }

        public static void SaveCustom()
        {
            PlayerPrefs.SetString("MUM_CAccent", ColorUtility.ToHtmlStringRGB(Accent));
            PlayerPrefs.SetString("MUM_CBG", ColorUtility.ToHtmlStringRGB(BG));
            PlayerPrefs.SetString("MUM_CBG2", ColorUtility.ToHtmlStringRGB(BG2));
            PlayerPrefs.SetString("MUM_CBG3", ColorUtility.ToHtmlStringRGB(BG3));
            PlayerPrefs.SetString("MUM_CText", ColorUtility.ToHtmlStringRGB(Text));
            PlayerPrefs.SetString("MUM_CDim", ColorUtility.ToHtmlStringRGB(Dim));
            PlayerPrefs.Save();
        }

        public static void LoadSaved()
        {
            var p = Current;
            if (p == ThemePreset.Custom)
            {
                Accent = LoadColor("MUM_CAccent", Accent);
                BG = LoadColor("MUM_CBG", BG);
                BG2 = LoadColor("MUM_CBG2", BG2);
                BG3 = LoadColor("MUM_CBG3", BG3);
                Text = LoadColor("MUM_CText", Text);
                Dim = LoadColor("MUM_CDim", Dim);
                BG.a = BgAlpha;
            }
            else
            {
                var v = Get(p);
                Accent = v.Accent; BG = v.BG; BG2 = v.BG2; BG3 = v.BG3; Text = v.Text; Dim = v.Dim;
                BG.a = BgAlpha;
            }
        }

        static Color LoadColor(string key, Color fallback)
        {
            string hex = PlayerPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString("#" + hex, out Color c) ? c : fallback;
        }
    }

    public static class UI
    {
        private static Font _font;

        public static Font BodyFont
        {
            get
            {
                if (_font != null) return _font;
                try { _font = Font.CreateDynamicFontFromOSFont("Arial", 14); }
                catch (System.Exception e) { Log.Err("UI.BodyFont", e); }
                return _font != null ? _font : GUI.skin.font;
            }
        }

        public static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply();
            t.filterMode = FilterMode.Point; return t;
        }

        public const float RefHeight = 540f;
        public static float Scale = 1f;

        public static int Px(float refSize) => Mathf.Max(11, Mathf.RoundToInt(refSize * Scale));

        public static Color SecondaryText => Color.Lerp(Theme.Dim, Theme.Text, 0.55f);

        public static GUIStyle Sty(Color c, float s = 10, bool bold = false,
                                   TextAnchor align = TextAnchor.MiddleLeft) =>
            new GUIStyle(GUI.skin.label)
            {
                fontSize = Px(s),
                fontStyle = (bold || Theme.BoldText) ? FontStyle.Bold : FontStyle.Normal,
                alignment = align,
                wordWrap = true,
                font = BodyFont,
                normal = { textColor = c == Theme.Dim ? SecondaryText : c }
            };

        public static GUIStyle BtnS(Texture2D bg, Color tc, float s = 10) =>
            new GUIStyle(GUI.skin.button)
            {
                fontSize = Px(s),
                fontStyle = FontStyle.Bold,
                font = BodyFont,
                normal = { textColor = tc, background = bg },
                padding = new RectOffset(8, 8, 4, 4),
                wordWrap = true
            };

        public static GUIStyle CardS(Color bg) =>
            new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(bg) },
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 1, 1)
            };

        private static readonly Dictionary<(int w, int h, int r, int colorHash), Texture2D> _roundedCache
            = new Dictionary<(int, int, int, int), Texture2D>();

        public static Texture2D MakeRoundedTex(Color c, int w, int h, int r)
        {
            w = Mathf.Max(4, w); h = Mathf.Max(4, h); r = Mathf.Clamp(r, 0, Mathf.Min(w, h) / 2);
            int colorHash = c.GetHashCode();
            var key = (w, h, r, colorHash);
            if (_roundedCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = new Color(c.r, c.g, c.b, c.a * CornerAlpha(x, y, w, h, r));

            tex.SetPixels(pixels);
            tex.Apply(false);
            _roundedCache[key] = tex;
            return tex;
        }

        static float CornerAlpha(int x, int y, int w, int h, int r)
        {
            if (r <= 0) return 1f;
            float cx = Mathf.Clamp(x, r, w - r);
            float cy = Mathf.Clamp(y, r, h - r);
            float dx = x - cx, dy = y - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            return 1f - Mathf.Clamp01((dist - r + 2f) / 2f);
        }

        private static readonly Dictionary<int, GUIStyle> _roundedShapes = new Dictionary<int, GUIStyle>();

        public static void DrawShadow(Rect r, int radius = 20, float offsetY = 8f, float strength = 0.28f, float blur = 1.35f)
        {
            if (Event.current.type != EventType.Repaint) return;
            r.y += offsetY;
            DrawRounded(r, new Color(0f, 0f, 0f, strength * 0.55f), radius);
        }

        public static void DrawRounded(Rect r, Color c, int radius = 14)
        {
            if (Event.current.type != EventType.Repaint || r.width <= 0f || r.height <= 0f) return;
            radius = Mathf.Clamp(radius, 0, Mathf.Min(96, Mathf.FloorToInt(Mathf.Min(r.width, r.height) * 0.5f)));
            if (!_roundedShapes.TryGetValue(radius, out var shape))
            {
                int size = Mathf.Max(4, radius * 2 + 2);
                shape = new GUIStyle(GUIStyle.none)
                {
                    normal = { background = MakeRoundedTex(Color.white, size, size, radius) },
                    border = new RectOffset(radius, radius, radius, radius)
                };
                _roundedShapes[radius] = shape;
            }
            Color previous = GUI.color;
            GUI.color = previous * c;
            shape.Draw(r, GUIContent.none, false, false, false, false);
            GUI.color = previous;
        }

        public static void DrawRoundedBorder(Rect r, Color c, int radius = 14, float thickness = 1f)
        {
            DrawRounded(r, c, radius);
            Rect inner = new Rect(r.x + thickness, r.y + thickness, r.width - thickness * 2, r.height - thickness * 2);
            if (inner.width > 2 && inner.height > 2)
            { var prev = GUI.color; GUI.color = Color.clear; GUI.color = prev; }
        }

        public static void DrawOutline(Rect r, Color outlineColor, int radius, float thickness)
        {
            Rect outer = new Rect(r.x - thickness, r.y - thickness, r.width + thickness * 2f, r.height + thickness * 2f);
            DrawRounded(outer, outlineColor, Mathf.RoundToInt(radius + thickness));
        }

        public static GUIStyle CardRoundedS(Color bg, int radius = 14) =>
            new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeRoundedTex(bg, 80, 80, radius) },
                border = new RectOffset(radius, radius, radius, radius),
                padding = new RectOffset(14, 14, 12, 12),
                margin = new RectOffset(0, 0, 3, 3)
            };

        public static GUIStyle BtnRoundedS(Color bg, Color tc, float s = 10, int radius = 10) =>
            new GUIStyle(GUI.skin.button)
            {
                fontSize = Px(s),
                fontStyle = FontStyle.Bold,
                font = BodyFont,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = tc, background = MakeRoundedTex(bg, 80, 40, radius) },
                hover = { textColor = tc, background = MakeRoundedTex(bg, 80, 40, radius) },
                active = { textColor = tc, background = MakeRoundedTex(bg, 80, 40, radius) },
                border = new RectOffset(radius, radius, radius, radius),
                padding = new RectOffset(10, 10, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = true
            };

        private static GUIStyle _emptyScrollStyle;
        public static GUIStyle EmptyScrollStyle
        {
            get
            {
                if (_emptyScrollStyle == null)
                    _emptyScrollStyle = new GUIStyle(GUIStyle.none) { fixedWidth = 0, fixedHeight = 0 };
                return _emptyScrollStyle;
            }
        }

        public static void ClearRoundedCache()
        {
            foreach (var kv in _roundedCache) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            _roundedCache.Clear();
            _roundedShapes.Clear();
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
        { PlayerPrefs.SetString("MUM_" + name, kc.ToString()); PlayerPrefs.Save(); }

        public static KeyCode GetKey(string name)
        {
            KeyCode def = _def.TryGetValue(name, out var d) ? d : KeyCode.None;
            string raw = PlayerPrefs.GetString("MUM_" + name, def.ToString());
            try { return (KeyCode)System.Enum.Parse(typeof(KeyCode), raw); }
            catch { return def; }
        }

        public static void ResetKey(string name)
        {
            if (_def.TryGetValue(name, out var def))
            { PlayerPrefs.SetString("MUM_" + name, def.ToString()); PlayerPrefs.Save(); }
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
            get => (Layout)PlayerPrefs.GetInt("MUM_Layout", (int)Layout.Unset);
            set { PlayerPrefs.SetInt("MUM_Layout", (int)value); PlayerPrefs.Save(); }
        }

        public static void DetectAndApply()
        {
            if (Keyboard.current == null) return;

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

            if (n.Contains("french") || n.Contains("azerty") || n.Contains("belg"))
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
        public static void Save() => PlayerPrefs.Save();
        public static void SetKey(string name, KeyCode kc) => Keys.ModifyKey(name, kc);

        public static void Export()
        {
            var sb = new System.Text.StringBuilder("[MattraxUltimateMenu v1.2]\n");
            foreach (var kv in Keys.All()) sb.AppendLine("MUM_" + kv.Key + "=" + kv.Value);
            sb.AppendLine("MUM_CamSpeed=" + PlayerPrefs.GetFloat("MUM_CamSpeed", 5f).ToString(Inv));
            sb.AppendLine("MUM_CamSens=" + PlayerPrefs.GetFloat("MUM_CamSens", 2f).ToString(Inv));
            sb.AppendLine("MUM_names=" + PlayerPrefs.GetString("MUM_names", "{}"));
            sb.AppendLine("MUM_UIScale=" + PlayerPrefs.GetFloat("MUM_UIScale", 1f).ToString(Inv));
            sb.AppendLine("MUM_ThemePreset=" + PlayerPrefs.GetInt("MUM_ThemePreset", 0));
            sb.AppendLine("MUM_BgAlpha=" + PlayerPrefs.GetFloat("MUM_BgAlpha", 0.97f).ToString(Inv));
            sb.AppendLine("MUM_Layout=" + PlayerPrefs.GetInt("MUM_Layout", 0));
            sb.AppendLine("MUM_Lang=" + PlayerPrefs.GetInt("MUM_Lang", 0));
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
                { if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv)) PlayerPrefs.SetFloat(k, fv); }
                else if (k == "MUM_ThemePreset" || k == "MUM_Layout" || k == "MUM_Lang") { if (int.TryParse(v, out int iv)) PlayerPrefs.SetInt(k, iv); }
                else PlayerPrefs.SetString(k, v);
            }
            PlayerPrefs.Save();
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
            get => PlayerPrefs.GetInt(PrefKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
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
        private int _lastScreenW, _lastScreenH;
        private bool _themeOpen;
        private bool _langOpen;

        private float _animT;
        private bool _animOpening;
        private const float AnimDur = 0.10f;

        private readonly Texture2D[] _navigationIcons = new Texture2D[8];
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
            _scale = PlayerPrefs.GetFloat("MUM_UIScale", 1f);
            _keyMenu = Keys.CreateKey("KeyMenu", KeyCode.F1);
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
            _scale = PlayerPrefs.GetFloat("MUM_UIScale", 1f);
            ComputeRect();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (_gorillaInterface != null) Destroy(_gorillaInterface.gameObject);
            ReleaseVrTexture();
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
        public void RegisterPage(string title, System.Action<MenuUI> draw, MenuCategory category = MenuCategory.Gameplay)
            => _pages.Add(new Page("", title, draw, category, false));

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

        public bool IsModEnabled(string title)
        {
            var page = _pages.FirstOrDefault(p => p.Title == title);
            return page == null || page.Enabled;
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

        void ComputeRect()
        {
            _lastScreenW = Screen.width; _lastScreenH = Screen.height;
            float w = Mathf.Clamp(600f * _scale, Mathf.Min(360f, Mathf.Max(1f, Screen.width - 90f)), Screen.width > 0 ? Mathf.Max(1f, Screen.width - 90f) : 600);
            float h = Mathf.Clamp(660f * _scale, Mathf.Min(420f, Mathf.Max(1f, Screen.height - 20f)), Screen.height > 0 ? Mathf.Max(1f, Screen.height - 20f) : 660);

            float x, y;
            if (PlayerPrefs.HasKey("MUM_PosX") && PlayerPrefs.HasKey("MUM_PosY"))
            {
                x = Mathf.Clamp(PlayerPrefs.GetFloat("MUM_PosX"), 0, Mathf.Max(0, Screen.width - w));
                y = Mathf.Clamp(PlayerPrefs.GetFloat("MUM_PosY"), 0, Mathf.Max(0, Screen.height - h));
            }
            else
            {
                x = Screen.width * 0.05f;
                y = (Screen.height - h) / 2f;
            }
            float dockSpace = 70f * Mathf.Min(w / 600f, h / 660f);
            x = Mathf.Clamp(x, dockSpace + 10f, Mathf.Max(dockSpace + 10f, Screen.width - w - 10f));
            _rect = new Rect(x, y, w, h);
        }

        void SavePosition()
        {
            PlayerPrefs.SetFloat("MUM_PosX", _rect.x);
            PlayerPrefs.SetFloat("MUM_PosY", _rect.y);
            PlayerPrefs.Save();
        }

        void Update()
        {
            KeyLayout.DetectAndApply();

            if ((_visible || (GorillaInterface.Instance != null && GorillaInterface.Instance.CastRequested)) && (Screen.width != _lastScreenW || Screen.height != _lastScreenH))
            { _lastScreenW = Screen.width; _lastScreenH = Screen.height; ComputeRect(); }

            if (_visible)
            {
                float target = _animOpening ? 1f : 0f;
                _animT = Mathf.MoveTowards(_animT, target, Time.unscaledDeltaTime / AnimDur);
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
            string[] names = { "Home", "Mods", "Settings", "Arrow", "Donation", "Minus", "Plus", "Cross" };
            var assembly = typeof(MenuUI).Assembly;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    using (var stream = assembly.GetManifestResourceStream("UltimateMenu.Assets.Menu.Icos." + names[i] + ".png"))
                    {
                        if (stream == null)
                        {
                            Log.Err("MenuUI.LoadNavigationIcons", new System.IO.FileNotFoundException("Missing embedded icon: " + names[i]));
                            continue;
                        }
                        using (var bytes = new System.IO.MemoryStream())
                        {
                            stream.CopyTo(bytes);
                            var icon = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                            if (!icon.LoadImage(bytes.ToArray())) { Destroy(icon); continue; }
                            icon.filterMode = FilterMode.Bilinear;
                            icon.wrapMode = TextureWrapMode.Clamp;
                            _navigationIcons[i] = icon;
                        }
                    }
                }
                catch (Exception e) { Log.Err("MenuUI.LoadNavigationIcons", e); }
            }
        }

        void DrawMenuIcon(Rect rect, int icon, float angle = 0f)
        {
            if (Event.current.type != EventType.Repaint || _navigationIcons[icon] == null) return;
            var previous = GUI.matrix;
            try
            {
                if (angle != 0f) GUIUtility.RotateAroundPivot(angle, rect.center);
                GUI.DrawTexture(rect, _navigationIcons[icon], ScaleMode.ScaleToFit);
            }
            finally { GUI.matrix = previous; }
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
            if (_navigationIcons[index] != null) GUI.DrawTexture(iconRect, _navigationIcons[index], ScaleMode.ScaleToFit);
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
            if (!cast) ReleaseVrTexture();
            if (!_visible && !cast) return;
            if (!_visible && Event.current.type != EventType.Layout && Event.current.type != EventType.Repaint) return;
            RenderTexture previousTarget = RenderTexture.active;
            bool renderingCast = cast && Event.current.type == EventType.Repaint;
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
                        _vrMenuTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                        {
                            name = "UltimateMenu VR mirror", hideFlags = HideFlags.HideAndDontSave,
                            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                            useMipMap = false, autoGenerateMips = false, antiAliasing = 1
                        };
                    }
                    if (!_vrMenuTexture.IsCreated() && !_vrMenuTexture.Create())
                        throw new InvalidOperationException("Cannot create VR menu texture.");
                    RenderTexture.active = _vrMenuTexture;
                    GL.Clear(false, true, Color.clear);
                }
                _vrInDraw = true;
                _vrOccurrences.Clear(); _vrScrollStack.Clear();
                if (Event.current.type == EventType.Repaint) _vrControls.Clear();
                DrawMenu(cast);
                DrawVrKeyboard();
                FinishVrFrame();
                if (renderingCast)
                {
                    float unit = UI.Scale;
                    Rect crop = Rect.MinMaxRect(Mathf.Max(0, _rect.x - 74f * unit), Mathf.Max(0, _rect.y - 6f * unit),
                        Mathf.Min(Screen.width, _rect.xMax + 6f * unit), Mathf.Min(Screen.height, _rect.yMax + 6f * unit));
                    vr.PresentCast(_vrMenuTexture, crop);
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
        void DrawMenu(bool cast)
        {
            if (_rect.width < 10) ComputeRect();
            if (Event.current.type == EventType.Layout && _pendingNav.HasValue)
            {
                _nav = _pendingNav.Value;
                if (_nav == -4 || (_nav >= 0 && _nav < _pages.Count)) _selectedConfig = _nav;
                _pendingNav = null;
                _scrollPage = Vector2.zero;
                ResetVrFocus();
            }
            UI.Scale = Mathf.Min(_rect.width / 600f, _rect.height / 660f);
            float unit = UI.Scale;
            float margin = 22f * unit;
            var ev = Event.current;
            Rect closeRect = new Rect(_rect.xMax - margin - 28f * unit, _rect.y + 18f * unit, 28f * unit, 28f * unit);
            Rect dragRect = new Rect(_rect.x, _rect.y, _rect.width - 64f * unit, 58f * unit);
            if (ev.type == EventType.MouseDown && ev.button == 0 && dragRect.Contains(ev.mousePosition))
            { _dragging = true; _dragOff = ev.mousePosition - _rect.position; ev.Use(); }
            if (_dragging && ev.type == EventType.MouseDrag)
            {
                _rect.x = Mathf.Clamp(ev.mousePosition.x - _dragOff.x, 70f * unit + 10f, Mathf.Max(70f * unit + 10f, Screen.width - _rect.width - 10f));
                _rect.y = Mathf.Clamp(ev.mousePosition.y - _dragOff.y, 0f, Mathf.Max(0f, Screen.height - _rect.height));
                ev.Use();
            }
            if (_dragging && ev.type == EventType.MouseUp) { _dragging = false; SavePosition(); ev.Use(); }

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
                UI.DrawRounded(_rect, Theme.BG, radius);
                GUI.Label(new Rect(_rect.x + margin, _rect.y + 15f * unit, _rect.width - 150f * unit, 32f * unit),
                    Log.PluginName ?? "UltimateMenu", UI.Sty(Theme.Text, 18, true));
                GUI.Label(new Rect(closeRect.x - 76f * unit, closeRect.y, 66f * unit, closeRect.height),
                    "v" + (Log.PluginVersion ?? "?"), UI.Sty(Theme.Dim, 10, false, TextAnchor.MiddleRight));
                if (IconButton(closeRect, 7, I18n.T("Core.close_footer"))) { if (cast) GorillaInterface.Instance.CloseCast(); else ToggleVisible(); }

                int tabCount = cast || _selectedConfig == -4 || (_selectedConfig >= 0 && _selectedConfig < _pages.Count) ? 3 : 2;
                Rect bar = new Rect(_rect.x - 68f * unit, _rect.y + 58f * unit, 54f * unit, ((tabCount + 1) * 50f + 12f) * unit);
                UI.DrawShadow(bar, Mathf.RoundToInt(27f * unit), 3f * unit, 0.18f);
                UI.DrawOutline(bar, new Color(Theme.Text.r, Theme.Text.g, Theme.Text.b, 0.12f), Mathf.RoundToInt(29f * unit), 1f);
                UI.DrawRounded(bar, Theme.BG2, Mathf.RoundToInt(29f * unit));
                int activeTab = _nav == -1 ? 0 : _nav == -2 ? 1 : 2;
                string[] labels = { I18n.T("Core.tab_home"), I18n.T("Core.tab_modes"), I18n.T("Core.vr_settings") };
                for (int i = 0; i < tabCount; i++)
                    DrawNavigationItem(new Rect(bar.x + 5f * unit, bar.y + (6f + 50f * i) * unit, 44f * unit, 44f * unit), i, labels[i], i == activeTab);

                Rect support = new Rect(bar.x + 5f * unit, bar.y + (6f + 50f * tabCount) * unit, 44f * unit, 44f * unit);
                if (IconButton(support, 4, I18n.T("Core.glass_support")))
                    Application.OpenURL(DonateUrl);

                Rect content = new Rect(_rect.x + margin, _rect.y + 70f * unit, _rect.width - margin * 2f,
                    _rect.height - 116f * unit);
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
                finally { GUILayout.EndArea(); }

                float footerY = _rect.yMax - 34f * unit;
                GUI.Label(new Rect(_rect.x + margin, footerY, 150f * unit, 20f * unit),
                    KeyLayout.Display(_keyMenu) + "  ·  " + I18n.T("Core.close_footer"), UI.Sty(Theme.Dim, 9));
                if (!string.IsNullOrEmpty(GUI.tooltip))
                {
                    var tip = new Rect(_rect.x + margin, _rect.y + 49f * unit, _rect.width - margin * 2f, 16f * unit);
                    GUI.Label(tip, GUI.tooltip, UI.Sty(Theme.Dim, 9, false, TextAnchor.MiddleCenter));
                }

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

        Vector2 BeginCustomScroll(Vector2 scroll, Rect outerRect, float measuredContentH)
        {
            // Unity measures the full layout, including wrapped labels and dynamic mod controls.
            // Preserve Unity input handling; only the scrollbar visuals are customized.
            GUISkin scrollSkin = GetScrollSkin();
            _scrollSkinStack.Push(GUI.skin);
            GUI.skin = scrollSkin;
            scroll = BeginVrScrollState(scroll);
            scroll = GUILayout.BeginScrollView(scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Width(outerRect.width), GUILayout.Height(outerRect.height));
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            return scroll;
        }

        float EndCustomScroll(Vector2 scroll, Rect outerRect, ref float contentHRef, int scrollId)
        {
            GUILayout.Space(12f * UI.Scale);
            GUILayout.EndVertical();
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
            Space(6);
            Label(I18n.T("Core.home_desc"), Theme.Dim, 12);
            Space(20);
            GUILayout.BeginHorizontal();
            DrawHomeStat(_pages.Count.ToString(), I18n.T("Core.tab_modes"));
            GUILayout.Space(10f * UI.Scale);
            DrawHomeStat(_pages.Count(page => page.Enabled).ToString(), I18n.T("Core.vr_active"));
            GUILayout.EndHorizontal();
            Space(18);
            DrawHomeShortcut(1, I18n.T("Core.mods_list"), I18n.T("Core.glass_mods_hint"), -2);
            Space(8);
            DrawHomeShortcut(2, I18n.T("Core.glass_general_settings"), I18n.T("Core.glass_settings_hint"), -4);
            Space(18);
            Label(I18n.T("Core.home_author"), Theme.Dim, 10);
            EndCustomScroll(_scrollPage, outerRect, ref _homeContentH, 1);
        }

        void DrawHomeStat(string value, string caption)
        {
            GUILayout.BeginVertical(UI.CardRoundedS(Theme.BG2, Mathf.RoundToInt(18f * UI.Scale)), GUILayout.ExpandWidth(true));
            Label(value, Theme.Text, 26, true);
            Label(caption, Theme.Dim, 11);
            GUILayout.EndVertical();
        }

        void DrawHomeShortcut(int icon, string title, string subtitle, int target)
        {
            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(76f * UI.Scale), GUILayout.ExpandWidth(true));
            bool hover = row.Contains(Event.current.mousePosition);
            UI.DrawRounded(row, hover ? Theme.BG3 : Theme.BG2, Mathf.RoundToInt(18f * UI.Scale));
            float size = 30f * UI.Scale;
            var image = new Rect(row.x + 18f * UI.Scale, row.center.y - size * 0.5f, size, size);
            if (_navigationIcons[icon] != null) GUI.DrawTexture(image, _navigationIcons[icon], ScaleMode.ScaleToFit);
            float x = image.xMax + 16f * UI.Scale;
            GUI.Label(new Rect(x, row.y + 13f * UI.Scale, row.xMax - x - 30f * UI.Scale, 25f * UI.Scale), title, UI.Sty(Theme.Text, 13, true));
            GUI.Label(new Rect(x, row.y + 39f * UI.Scale, row.xMax - x - 30f * UI.Scale, 23f * UI.Scale), subtitle, UI.Sty(Theme.Dim, 10));
            DrawMenuIcon(new Rect(row.xMax - 30f * UI.Scale, row.center.y - 9f * UI.Scale, 18f * UI.Scale, 18f * UI.Scale), 3, 180f);
            if (ControlButton(row, new GUIContent("", target.ToString()), GUIStyle.none)) NavigateTo(target);
        }

        void DrawPreferences(Rect outerRect)
        {
            _scrollSettings = BeginCustomScroll(_scrollSettings, outerRect, _settingsContentH);

            Space(8); Section(I18n.T("Core.personalization"));
            BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("Core.size_label"), UI.Sty(Theme.Text, 11), GUILayout.Width(55 * UI.Scale));
            if (MinusBtn()) { _scale = Mathf.Max(0.5f, _scale - 0.1f); PlayerPrefs.SetFloat("MUM_UIScale", _scale); PlayerPrefs.Save(); ComputeRect(); }
            GUILayout.Label(_scale.ToString("0.0x"), UI.Sty(Theme.Accent, 11, true, TextAnchor.MiddleCenter), GUILayout.Width(40 * UI.Scale));
            if (PlusBtn()) { _scale = Mathf.Min(2f, _scale + 0.1f); PlayerPrefs.SetFloat("MUM_UIScale", _scale); PlayerPrefs.Save(); ComputeRect(); }
            GUILayout.EndHorizontal();
            Space(4);
            if (WaitingKey) { Label(I18n.T("Core.waiting_key"), Theme.Accent, 12, true); if (Btn(I18n.T("Core.cancel"), TB3, Color.white)) { WaitingKey = false; RebindTarget = null; } }
            else { if (RebindBtn(I18n.T("Core.menu_key"), _keyMenu)) StartRebind("KeyMenu"); }
            if (_keyHistory.Count > 0) { Space(4); Label(I18n.T("Core.last_key") + " " + _keyHistory[0], Theme.Accent, 10, true); }
            EndCard();

            Space(6);

            BeginCard();
            GUILayout.BeginHorizontal();
            Label(I18n.T("Core.keyboard_label"), Theme.Text, 11, true, TextAnchor.MiddleLeft);
            GUILayout.FlexibleSpace();
            Label(KeyLayout.Current.ToString(), Theme.Accent, 11, true, TextAnchor.MiddleRight);
            GUILayout.EndHorizontal();
            Space(4);
            Label(I18n.T("Core.keyboard_auto_hint"), Theme.Dim, 9);
            EndCard();

            ExpandableSection(I18n.T("Core.theme_current"), ThemeName(Theme.Current), ref _themeOpen, () =>
            {
                foreach (ThemePreset preset in System.Enum.GetValues(typeof(ThemePreset)))
                {
                    bool sel = Theme.Current == preset;
                    if (Btn(ThemeName(preset), TB3, sel ? Theme.Accent : Theme.Text))
                        Theme.Apply(preset);
                    Space(6);
                }

                if (Theme.Current == ThemePreset.Custom)
                {
                    Space(6); Separator();
                    Label(I18n.T("Core.custom_colors"), Theme.Accent, 10, true);
                    Space(4);
                    Theme.Accent = ColorField(I18n.T("Core.color_accent"), Theme.Accent);
                    Theme.BG2 = ColorField(I18n.T("Core.color_bg2"), Theme.BG2);
                    Theme.BG3 = ColorField(I18n.T("Core.color_bg3"), Theme.BG3);
                    Theme.Text = ColorField(I18n.T("Core.color_text"), Theme.Text);
                    Theme.Dim = ColorField(I18n.T("Core.color_dim"), Theme.Dim);
                    Space(4);
                    float newAlpha = Slider(I18n.T("Core.bg_transparency"), Theme.BgAlpha, 0.2f, 1f, "0.00");
                    if (!Mathf.Approximately(newAlpha, Theme.BgAlpha)) Theme.BgAlpha = newAlpha;
                    Space(4);
                    if (Btn(I18n.T("Core.save_colors"), TA, Color.white)) { Theme.SaveCustom(); RebuildTex(); }
                }
            });

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

            BeginCard();
            bool newBoldText = Toggle(I18n.T("Core.bold_text_label"), Theme.BoldText, 150f);
            if (newBoldText != Theme.BoldText) Theme.BoldText = newBoldText;
            EndCard();

            BeginCard();
            bool newSoundEnabled = Toggle(I18n.T("Core.sound_enabled_label"), SoundFX.Enabled, 150f);
            if (newSoundEnabled != SoundFX.Enabled) SoundFX.Enabled = newSoundEnabled;
            if (SoundFX.Enabled)
            {
                Space(4);
                float newVol = Slider(I18n.T("Core.sound_volume_label"), SoundFX.Volume, 0f, 1f, "0.00");
                if (!Mathf.Approximately(newVol, SoundFX.Volume)) SoundFX.Volume = newVol;
            }
            EndCard();

            // ---- Interface VR (menu in-game) : réglages intégrés directement dans
            // les Reglages PC, jamais une page de mod separee. ----
            if (_gorillaInterface != null)
            {
                Space(8);
                _gorillaInterface.DrawSettingsPage(this);
            }

            Space(8); BeginCard();
            Label("[" + KeyLayout.Display(_keyMenu) + "] " + I18n.T("Core.open_close_hint") + "  |  " + _pages.Count + " " + I18n.T("Core.modules_suffix"), Theme.Dim, 9);
            EndCard();

            EndCustomScroll(_scrollSettings, outerRect, ref _settingsContentH, 1);
        }

        internal static string LangName(Lang l)
        {
            switch (l)
            {
                case Lang.Fr: return "Francais";
                case Lang.En: return "English";
                case Lang.De: return "Deutsch";
                case Lang.Es: return "Espanol";
                case Lang.It: return "Italiano";
                case Lang.Pt: return "Portugues";
                case Lang.Ja: return "日本語";
                default: return "Русский";
            }
        }

        static string ThemeName(ThemePreset p)
        {
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

            Section(I18n.T("Core.mods_list"));

            float searchH = Mathf.Max(26f, 30f * UI.Scale);
            GUILayout.BeginHorizontal(GUILayout.Height(searchH));
            Rect searchBg = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(searchH));
            UI.DrawRounded(searchBg, Theme.BG3, Mathf.RoundToInt(searchH * 0.5f));
            GUILayout.EndHorizontal();
            Rect searchInner = new Rect(searchBg.x + 12 * UI.Scale, searchBg.y, searchBg.width - 24 * UI.Scale - searchH, searchBg.height);
            GUI.SetNextControlName("mum_search");
            var searchFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = UI.Px(10),
                fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal,
                font = UI.BodyFont,
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
            _search = ControlTextField(searchInner, _search, searchFieldStyle);
            if (string.IsNullOrEmpty(_search))
                GUI.Label(searchInner, I18n.T("Core.search_label"), new GUIStyle(GUI.skin.label) { fontSize = UI.Px(10), fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal, font = UI.BodyFont, alignment = TextAnchor.MiddleLeft, normal = { textColor = UI.SecondaryText } });
            if (!string.IsNullOrEmpty(_search))
            {
                Rect xRect = new Rect(searchBg.x + searchBg.width - searchH, searchBg.y, searchH, searchH);
                if (IconButton(xRect, 7, I18n.T("Core.clear_search")))
                    _search = "";
            }
            Space(10);

            string s = _search.ToLower();
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
                            fontStyle = FontStyle.Bold,
                            font = UI.BodyFont,
                            normal = { textColor = catTitleCol }
                        });
                        Space(2);
                    }
                }
                else shown++;

                GUILayout.BeginHorizontal();

                string pTitleDisplay = I18n.T(p.Title);

                float rightReserved = 74f * UI.Scale;

                Rect rowRect = GUILayoutUtility.GetRect(new GUIContent(pTitleDisplay),
                    new GUIStyle(GUI.skin.button)
                    {
                        fontSize = UI.Px(11),
                        fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal,
                        font = UI.BodyFont,
                        padding = new RectOffset(14, 14, 9, 9),
                        wordWrap = true
                    }, GUILayout.ExpandWidth(true), GUILayout.MinHeight(46 * UI.Scale));

                bool modEnabled = p.Enabled;
                Color neutral = Color.Lerp(Theme.BG2, new Color(0.45f, 0.45f, 0.45f, Theme.BG2.a), 0.12f);
                Color rowBg = modEnabled ? Color.Lerp(neutral, Theme.Green, 0.22f) : neutral;
                bool rowHover = Event.current != null && rowRect.Contains(Event.current.mousePosition);
                if (rowHover) rowBg = Color.Lerp(rowBg, Theme.Text, 0.06f);
                UI.DrawRounded(rowRect, rowBg, Mathf.RoundToInt(RowRadius * UI.Scale));

                Color rowText = Theme.Text;
                var rowStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = UI.Px(11),
                    fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal,
                    font = UI.BodyFont,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(14, Mathf.RoundToInt(rightReserved + 6f * UI.Scale), 9, 9),
                    wordWrap = true,
                    normal = { textColor = rowText }
                };
                GUI.Label(rowRect, pTitleDisplay, rowStyle);

                Rect stateRect = new Rect(rowRect.xMax - rightReserved, rowRect.y, rightReserved - 26f * UI.Scale, rowRect.height);
                GUI.Label(stateRect, modEnabled ? "ON" : "OFF", UI.Sty(Theme.Text, 9, true, TextAnchor.MiddleRight));
                DrawMenuIcon(new Rect(rowRect.xMax - 25f * UI.Scale, rowRect.center.y - 8f * UI.Scale, 16f * UI.Scale, 16f * UI.Scale), 3, 180f);
                if (ControlButton(rowRect, new GUIContent("", p.Title), GUIStyle.none)) NavigateTo(i);

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
            float backHeight = 36f * UI.Scale;
            if (IconButton(new Rect(0f, 0f, backHeight, backHeight), 3, backLabel)) NavigateTo(-2);
            var titleStyle = UI.Sty(Theme.Text, 22, true);
            float titleY = backHeight + 12f * UI.Scale;
            float titleHeight = titleStyle.CalcHeight(new GUIContent(I18n.T(page.Title)), outerRect.width);
            GUI.Label(new Rect(0f, titleY, outerRect.width, titleHeight), I18n.T(page.Title), titleStyle);
            float stateY = titleY + titleHeight + 8f * UI.Scale;
            float stateHeight = 44f * UI.Scale;
            Rect state = new Rect(0f, stateY, outerRect.width, stateHeight);
            UI.DrawRounded(state, Theme.BG2, Mathf.RoundToInt(12f * UI.Scale));
            string status = I18n.T("Core.vr_mod_enabled") + " · " + CategoryNames.Get(page.Category);
            GUI.Label(new Rect(12f * UI.Scale, stateY, outerRect.width - 82f * UI.Scale, stateHeight), status, UI.Sty(Theme.Text, 11));
            Rect toggle = new Rect(outerRect.width - 58f * UI.Scale, stateY + 10f * UI.Scale, 44f * UI.Scale, 24f * UI.Scale);
            bool enabled = page.Enabled;
            DrawSwitchAt(toggle, enabled);
            if (ControlButton(toggle, new GUIContent("", I18n.T("Core.vr_mod_enabled")), GUIStyle.none))
            {
                page.Enabled = !enabled;
                SoundFX.PlaySound(page.Enabled ? "Aigue" : "Grave");
            }
            float headerHeight = stateY + stateHeight + 14f * UI.Scale;
            Rect body = new Rect(0f, headerHeight, outerRect.width, Mathf.Max(40f, outerRect.height - headerHeight));
            GUILayout.BeginArea(body);
            _scrollPage = BeginCustomScroll(_scrollPage, body, _pageContentH);

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

        public void Section(string title)
        {
            if (_drawingPageTitle != null && title == I18n.T(_drawingPageTitle)) return;
            GUILayout.Space(14f * UI.Scale);
            GUILayout.Label(title.ToUpperInvariant(), new GUIStyle(GUI.skin.label)
            {
                fontSize = UI.Px(9),
                fontStyle = FontStyle.Bold,
                font = UI.BodyFont,
                normal = { textColor = UI.SecondaryText },
                wordWrap = true
            });
            GUILayout.Space(4);
        }

        public void BeginCard(Color? bg = null) { GUILayout.BeginVertical(UI.CardRoundedS(bg ?? Theme.BG2, Mathf.RoundToInt(RowRadius * UI.Scale))); }
        public void EndCard() { GUILayout.EndVertical(); GUILayout.Space(6); }

        public bool ExpandableHeader(string label, string valueText, bool isOpen, float labelWidth = 110f)
        {
            float rowH = 32f * UI.Scale;
            Rect headerRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));

            bool hover = Event.current != null && headerRect.Contains(Event.current.mousePosition);
            Color headerBg = isOpen ? Theme.BG3 : (hover ? Darken(Theme.BG3, -0.06f) : Theme.BG2);
            UI.DrawRounded(headerRect, headerBg, Mathf.RoundToInt(RowRadius * UI.Scale * 0.8f));

            float pad = 12f * UI.Scale;
            var labelStyle = UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleLeft);
            var valueStyle = UI.Sty(Theme.Accent, 10, true, TextAnchor.MiddleRight);

            Rect labelRect = new Rect(headerRect.x + pad, headerRect.y, labelWidth * UI.Scale, headerRect.height);
            GUI.Label(labelRect, label, labelStyle);

            Rect valueRect = new Rect(labelRect.x + labelRect.width, headerRect.y,
                headerRect.width - labelRect.width - pad * 2f, headerRect.height);
            GUI.Label(valueRect, valueText, valueStyle);

            bool clicked = ControlButton(headerRect, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound("Moyen");
            GUILayout.Space(2);
            return clicked ? !isOpen : isOpen;
        }

        public void BeginExpandedContent()
        {
            GUILayout.Space(4);
            GUILayout.BeginVertical(UI.CardRoundedS(Theme.BG3, Mathf.RoundToInt(RowRadius * UI.Scale * 0.8f)));
        }

        public void EndExpandedContent()
        {
            GUILayout.EndVertical();
            GUILayout.Space(4);
        }

        public void ExpandableSection(string label, string valueText, ref bool isOpen, System.Action drawContent, float labelWidth = 110f)
        {
            BeginCard();
            isOpen = ExpandableHeader(label, valueText, isOpen, labelWidth);
            if (isOpen)
            {
                BeginExpandedContent();
                drawContent?.Invoke();
                EndExpandedContent();
            }
            EndCard();
        }

        public void Label(string text, Color? c = null, int size = 10, bool bold = false,
                          TextAnchor align = TextAnchor.MiddleLeft)
        {
            GUILayout.Label(text, UI.Sty(c ?? Theme.Text, size, bold, align));
        }

        public void BoldLabel(string text, Color? c = null, int size = 10)
            => Label(text, c, size, true);
        public void CenterLabel(string text, Color? c = null, int size = 10, bool bold = false)
            => Label(text, c, size, bold, TextAnchor.MiddleCenter);
        public void RightLabel(string text, Color? c = null, int size = 10)
            => Label(text, c, size, false, TextAnchor.MiddleRight);

        public void Space(float h = 8f) => GUILayout.Space(h);

        Color ResolveBg(Texture2D bg)
        {
            if (bg == null || bg == TA) return Theme.Accent;
            if (bg == TB3) return Theme.BG3;
            if (bg == TB2) return Theme.BG2;
            if (bg == TG) return Theme.Green;
            if (bg == TR) return Theme.Red;
            if (bg == TY) return Theme.Yellow;
            return Theme.Accent;
        }

        public bool Btn(string text, Texture2D bg = null, Color? tc = null, float w = 0)
        {
            Color baseCol = ResolveBg(bg);
            var baseStyle = UI.BtnRoundedS(baseCol, tc ?? Color.white, 10, Mathf.RoundToInt(10 * UI.Scale));
            Rect probe;
            if (w > 0)
            {
                float height = baseStyle.CalcHeight(new GUIContent(text), w);
                probe = GUILayoutUtility.GetRect(w, Mathf.Max(height, 32f * UI.Scale));
            }
            else
            {
                probe = GUILayoutUtility.GetRect(new GUIContent(text), baseStyle, GUILayout.MinHeight(32f * UI.Scale));
            }
            bool hover = Event.current != null && probe.Contains(Event.current.mousePosition);
            Color drawCol = hover ? Darken(baseCol, 0.14f) : baseCol;
            var s = UI.BtnRoundedS(drawCol, tc ?? Color.white, 10, Mathf.RoundToInt(10 * UI.Scale));
            s.normal.background = s.hover.background = s.active.background = UI.MakeRoundedTex(drawCol, 80, 40, Mathf.RoundToInt(10 * UI.Scale));
            bool r = ControlButton(probe, text, s);
            if (r) SoundFX.PlaySound("Moyen");

            return r;
        }

        static Color Darken(Color c, float amount) => new Color(
            Mathf.Clamp01(c.r * (1f - amount)),
            Mathf.Clamp01(c.g * (1f - amount)),
            Mathf.Clamp01(c.b * (1f - amount)),
            c.a);

        public bool CircleBtn(string glyph, float diameter, Texture2D bg = null, Color? tc = null, float glyphSize = 12)
        {
            Color baseCol = ResolveBg(bg);
            Rect r = GUILayoutUtility.GetRect(diameter, diameter, GUILayout.Width(diameter), GUILayout.Height(diameter));
            bool hover = Event.current != null && r.Contains(Event.current.mousePosition);
            Color drawCol = hover ? Darken(baseCol, 0.14f) : baseCol;
            UI.DrawRounded(r, drawCol, Mathf.RoundToInt(diameter * 0.5f));
            GUI.Label(r, glyph, new GUIStyle(GUI.skin.label)
            {
                fontSize = UI.Px(glyphSize),
                fontStyle = FontStyle.Bold,
                font = UI.BodyFont,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = tc ?? Theme.Text }
            });
            bool clicked = ControlButton(r, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound("Moyen");
            return clicked;
        }

        public bool SquareBtn(string glyph, float side, Texture2D bg = null, Color? tc = null, float glyphSize = 13)
        {
            Color baseCol = ResolveBg(bg);
            Rect r = GUILayoutUtility.GetRect(side, side, GUILayout.Width(side), GUILayout.Height(side));
            bool hover = Event.current != null && r.Contains(Event.current.mousePosition);
            Color drawCol = hover ? Darken(baseCol, 0.14f) : baseCol;
            UI.DrawRounded(r, drawCol, Mathf.RoundToInt(side * 0.32f));
            GUI.Label(r, glyph, new GUIStyle(GUI.skin.label)
            {
                fontSize = UI.Px(glyphSize),
                fontStyle = FontStyle.Bold,
                font = UI.BodyFont,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = tc ?? Theme.Text }
            });
            bool clicked = ControlButton(r, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound("Moyen");
            return clicked;
        }

        public bool SmallBtn(string text, Texture2D bg = null, Color? tc = null)
        {
            var s = UI.BtnRoundedS(ResolveBg(bg), tc ?? Color.white, 9, Mathf.RoundToInt(7 * UI.Scale));
            s.padding = new RectOffset(4, 4, 2, 2);
            float width = Mathf.Max(30f * UI.Scale, s.CalcSize(new GUIContent(text)).x + 16f * UI.Scale);
            bool r = ControlLayoutButton(text, s, GUILayout.MinWidth(width), GUILayout.MinHeight(30f * UI.Scale));
            if (r) SoundFX.PlaySound("Moyen");
            return r;
        }

        public void DrawSwitchAt(Rect swRect, bool value, float alpha = 1f)
        {
            float swH = swRect.height;

            Color trackCol = value ? Theme.Green : Theme.BG3;
            trackCol.a *= alpha;
            bool hover = Event.current != null && swRect.Contains(Event.current.mousePosition);
            if (hover) trackCol = new Color(trackCol.r * 0.9f, trackCol.g * 0.9f, trackCol.b * 0.9f, trackCol.a);

            float trackBorder = Mathf.Max(1f, 1f * UI.Scale);
            Rect trackBorderRect = new Rect(
                swRect.x - trackBorder, swRect.y - trackBorder,
                swRect.width + trackBorder * 2f, swRect.height + trackBorder * 2f);
            Color trackBorderCol = new Color(1f, 1f, 1f, 0.14f * alpha);
            UI.DrawRounded(trackBorderRect, trackBorderCol, Mathf.RoundToInt(trackBorderRect.height * 0.5f));

            UI.DrawRounded(swRect, trackCol, Mathf.RoundToInt(swH * 0.5f));

            float knobD = swH - 4f * UI.Scale;
            float knobX = value
                ? swRect.x + swRect.width - knobD - 2f * UI.Scale
                : swRect.x + 2f * UI.Scale;
            Rect knobRect = new Rect(knobX, swRect.y + 2f * UI.Scale, knobD, knobD);

            float knobBorder = Mathf.Max(1f, 1.5f * UI.Scale);
            Rect knobBorderRect = new Rect(
                knobRect.x - knobBorder, knobRect.y - knobBorder,
                knobRect.width + knobBorder * 2f, knobRect.height + knobBorder * 2f);
            Color knobBorderCol = new Color(0f, 0f, 0f, 0.22f * alpha);
            UI.DrawRounded(knobBorderRect, knobBorderCol, Mathf.RoundToInt(knobBorderRect.height * 0.5f));

            Color knobCol = new Color(1f, 1f, 1f, alpha);
            UI.DrawRounded(knobRect, knobCol, Mathf.RoundToInt(knobD * 0.5f));
        }

        public bool Toggle(string label, bool value, float lw = 100f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UI.Sty(Theme.Text, 10), GUILayout.Width(lw * UI.Scale));
            GUILayout.FlexibleSpace();
            float swW = 44 * UI.Scale, swH = 24 * UI.Scale;
            Rect swRect = GUILayoutUtility.GetRect(swW, swH, GUILayout.Width(swW), GUILayout.Height(swH));
            DrawSwitchAt(swRect, value, 1f);
            bool clicked = ControlButton(swRect, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound(value ? "Grave" : "Aigue");
            bool result = clicked ? !value : value;

            GUILayout.EndHorizontal();
            return result;
        }

        public bool RoundBtn(Color color, float diameter)
        {
            string label = color == Theme.Green ? "+" : color == Theme.Red ? "-" : ">";
            Rect rect = GUILayoutUtility.GetRect(diameter, diameter, GUILayout.Width(diameter), GUILayout.Height(diameter));
            int icon = color == Theme.Green ? 6 : color == Theme.Red ? 5 : 3;
            bool clicked = IconButton(rect, icon, label, icon == 3 ? 180f : 0f);
            if (clicked) SoundFX.PlaySound(color == Theme.Green ? "Aigue" : color == Theme.Red ? "Grave" : "Moyen");
            return clicked;
        }

        public static float StandardRoundBtnSize => 28f * UI.Scale;

        public bool PlusBtn(float diameter = -1f) => RoundBtn(Theme.Green, diameter > 0 ? diameter : StandardRoundBtnSize);

        public bool MinusBtn(float diameter = -1f) => RoundBtn(Theme.Red, diameter > 0 ? diameter : StandardRoundBtnSize);

        public bool ChevronBtn(float diameter = -1f)
        {
            float size = diameter > 0 ? diameter : StandardRoundBtnSize;
            Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            bool clicked = IconButton(rect, 3, I18n.T("Core.vr_settings"), 180f);
            if (clicked) SoundFX.PlaySound("Moyen");
            return clicked;
        }

        public float Slider(string label, float val, float min, float max, string fmt = "0.0", float lw = 90f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UI.Sty(Theme.Text, 10), GUILayout.Width(lw * UI.Scale));
            float value = DrawStyledSlider(val, min, max, label);
            GUILayout.Label(value.ToString(fmt), UI.Sty(Theme.Accent, 10), GUILayout.Width(40 * UI.Scale));
            GUILayout.EndHorizontal();
            return value;
        }

        public int IntSlider(string label, int val, int min, int max, float lw = 90f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UI.Sty(Theme.Text, 10), GUILayout.Width(lw * UI.Scale));
            int value = Mathf.RoundToInt(DrawStyledSlider(val, min, max, label, true));
            GUILayout.Label(value.ToString(), UI.Sty(Theme.Accent, 10), GUILayout.Width(40 * UI.Scale));
            GUILayout.EndHorizontal();
            return Mathf.RoundToInt(value);
        }

        float DrawStyledSlider(float val, float min, float max, string label, bool integer = false)
        {
            float trackH = Mathf.Max(5f, 6f * UI.Scale);
            float knobD = Mathf.Max(16f, 18f * UI.Scale);

            int id = GUIUtility.GetControlID(FocusType.Passive);

            Rect full = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(knobD));
            Rect track = new Rect(full.x + knobD * 0.5f, full.y + (full.height - trackH) * 0.5f, full.width - knobD, trackH);

            float t = max > min ? Mathf.Clamp01((val - min) / (max - min)) : 0f;

            Event ev = Event.current;
            EventType et = ev.GetTypeForControl(id);

            switch (et)
            {
                case EventType.MouseDown:
                    if (full.Contains(ev.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        float nt0 = Mathf.Clamp01((ev.mousePosition.x - track.x) / Mathf.Max(1f, track.width));
                        t = nt0;
                        val = Mathf.Lerp(min, max, t);
                        ev.Use();
                        GUI.changed = true;
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        float nt1 = Mathf.Clamp01((ev.mousePosition.x - track.x) / Mathf.Max(1f, track.width));
                        t = nt1;
                        val = Mathf.Lerp(min, max, t);
                        ev.Use();
                        GUI.changed = true;
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        ev.Use();
                        SoundFX.PlaySound("Moyen");
                    }
                    break;
            }

            UI.DrawRounded(track, Theme.BG3, Mathf.RoundToInt(trackH * 0.5f));
            if (t > 0.001f)
            {
                Rect fill = new Rect(track.x, track.y, track.width * t, track.height);
                UI.DrawRounded(fill, Theme.Accent, Mathf.RoundToInt(trackH * 0.5f));
            }
            float knobX = track.x + track.width * t - knobD * 0.5f;
            Rect knob = new Rect(knobX, full.y + (full.height - knobD) * 0.5f, knobD, knobD);
            UI.DrawShadow(knob, radius: Mathf.RoundToInt(knobD * 0.5f), offsetY: 1.5f, strength: 0.22f, blur: 1f);
            UI.DrawRounded(knob, Color.white, Mathf.RoundToInt(knobD * 0.5f));

            return AdjustVrSlider(full, label, val, min, max, integer);
        }

        public void StatusPill(string text, bool active)
            => Label((active ? "o " : "- ") + text, active ? Theme.Green : Theme.Dim, 11, true);

        public bool RebindBtn(string name, KeyCode current, float nw = 110f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, UI.Sty(Theme.Text, 10), GUILayout.Width(nw * UI.Scale));
            var s = UI.BtnRoundedS(Theme.BG3, Theme.Accent, 10, Mathf.RoundToInt(8 * UI.Scale));
            bool r = ControlLayoutButton(KeyLayout.Display(current), s, GUILayout.Width(130 * UI.Scale));
            if (r) SoundFX.PlaySound("Moyen");
            GUILayout.EndHorizontal();
            return r;
        }

        public string TextField(string val, float w = 0)
        {
            var s = new GUIStyle(GUI.skin.textField)
            {
                fontSize = UI.Px(10),
                font = UI.BodyFont,
                normal = { textColor = Theme.Text, background = UI.MakeRoundedTex(Theme.BG3, 64, 32, Mathf.RoundToInt(8 * UI.Scale)) },
                focused = { textColor = Theme.Text, background = UI.MakeRoundedTex(Theme.BG3, 64, 32, Mathf.RoundToInt(8 * UI.Scale)) },
                hover = { textColor = Theme.Text, background = UI.MakeRoundedTex(Theme.BG3, 64, 32, Mathf.RoundToInt(8 * UI.Scale)) },
                border = new RectOffset(Mathf.RoundToInt(8 * UI.Scale), Mathf.RoundToInt(8 * UI.Scale), Mathf.RoundToInt(8 * UI.Scale), Mathf.RoundToInt(8 * UI.Scale)),
                padding = new RectOffset(10, 10, 4, 4)
            };
            return w > 0 ? ControlLayoutTextField(val, s, GUILayout.Width(w * UI.Scale), GUILayout.MinHeight(32f * UI.Scale)) : ControlLayoutTextField(val, s, GUILayout.MinHeight(32f * UI.Scale));
        }

        public int ModePicker(string[] labels, int cur)
        {

            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
            {
                bool sel = cur == i;
                var s = UI.BtnRoundedS(sel ? Theme.Accent : Theme.BG3, sel ? Color.white : Theme.Text, 9, Mathf.RoundToInt(7 * UI.Scale));
                s.padding = new RectOffset(6, 6, 3, 3);
                if (ControlLayoutButton(labels[i], s, GUILayout.Width(62 * UI.Scale)))
                {
                    cur = i;
                    SoundFX.PlaySound("Moyen");
                }
            }
            GUILayout.EndHorizontal(); return cur;
        }

        public void ColorBar(Color c, float h = 18f)
        { GUI.color = c; GUILayout.Box("", GUILayout.Height(h), GUILayout.ExpandWidth(true)); GUI.color = Color.white; }

        public void Separator(float a = 0.2f)
        { GUI.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, a); GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true)); GUI.color = Color.white; }

        public void InfoRow(string k, string v, float kw = 110f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(k, UI.Sty(Theme.Dim, 9), GUILayout.Width(kw));
            GUILayout.Label(v, UI.Sty(Theme.Text, 9));
            GUILayout.EndHorizontal();
        }

        public bool ColorPickerHSV(ref Color color, string label = "Couleur", float sv = 200f, float hw = 20f)
        { Label(label + " (non disponible)", Theme.Dim, 9); return false; }

        public Texture2D T(Color c) => UI.MakeTex(c);
        public float WinW => _rect.width;
        public float WinH => _rect.height;
    }

    public class Notif : MonoBehaviour
    {
        private float _t; private const float DUR = 5f, FI = 0.4f, FO = 0.6f; private float _s, _a;

        void Update()
        {
            _t += Time.deltaTime;
            if (_t < FI) { float p = _t / FI; _s = _a = Mathf.SmoothStep(0f, 1f, p); }
            else if (_t < DUR - FO) { _s = _a = 1f; }
            else if (_t < DUR) { float p = (_t - (DUR - FO)) / FO; _a = Mathf.SmoothStep(1f, 0f, p); _s = 1f; }
            else { Destroy(this); }
        }

        void OnGUI()
        {
            if (_t >= DUR) return;
            float sh = Screen.height, sx = sh * 0.02f, sy = sh * 0.02f, sw = sh * 0.20f;
            float capH = sh * 0.075f;
            int r = Mathf.RoundToInt(sh * 0.02f);
            var om = GUI.matrix; var oc = GUI.color;
            GUI.matrix = Matrix4x4.TRS(new Vector3(sx * (1f - _s), sy * (1f - _s), 0), Quaternion.identity, new Vector3(_s, _s, 1f));

            Rect capsule = new Rect(sx, sy, sw, capH);
            UI.DrawShadow(capsule, radius: r, offsetY: 4f, strength: 0.25f * _a, blur: 1.3f);
            UI.DrawRounded(capsule, new Color(0.11f, 0.11f, 0.115f, 0.92f * _a), r);

            int fsTitle = Mathf.RoundToInt(sh * 0.017f);
            int fsBody = Mathf.RoundToInt(sh * 0.0115f);
            GUI.Label(new Rect(capsule.x + 16, capsule.y + capH * 0.14f, sw - 32, capH * 0.4f), Log.PluginName ?? "UltimateMenu",
                new GUIStyle(GUI.skin.label) { fontSize = fsTitle, fontStyle = FontStyle.Bold, font = UI.BodyFont, normal = { textColor = Theme.Text }, alignment = TextAnchor.MiddleLeft });
            GUI.Label(new Rect(capsule.x + 16, capsule.y + capH * 0.52f, sw - 32, capH * 0.4f),
                I18n.T("Core.open_notif") + " [" + KeyLayout.Display(Keys.GetKey("KeyMenu")) + "]",
                new GUIStyle(GUI.skin.label) { fontSize = fsBody, font = UI.BodyFont, normal = { textColor = UI.SecondaryText }, alignment = TextAnchor.MiddleLeft });

            GUI.matrix = om; GUI.color = oc;
            if (Event.current.type == EventType.MouseDown && capsule.Contains(Event.current.mousePosition))
            { MenuUI.Instance?.ToggleVisible(); Event.current.Use(); }
        }
    }
    // ================================================================
    // GORILLAINTERFACE — menu VR world-space, fusionné dans ce même fichier
    // à la demande : plus qu'un seul fichier pour tout UltimateMenu.
    // ================================================================
    [DefaultExecutionOrder(10001)]
    public class GorillaInterface : MonoBehaviour
    {
        public static GorillaInterface Instance { get; private set; }
        public bool Enabled => true;
        const string PREF_SCALE = "MUM_GI_Scale", PREF_DISTANCE = "MUM_GI_Distance", PREF_ANIMSPEED = "MUM_GI_AnimSpeed", PREF_STICKSENS = "MUM_GI_StickSens";
        const string PREF_SHAKE_THRESHOLD = "MUM_GI_ShakeThreshold", PREF_SHAKE_WINDOW = "MUM_GI_ShakeWindow";
        const float MinUserScale = 0.5f, MaxUserScale = 2f, MinDistance = 0.4f, MaxDistance = 1.5f;
        const float MinAnimSpeed = 1f, MaxAnimSpeed = 8f, MinStickSens = 0.3f, MaxStickSens = 0.9f;
        const float MinShakeThreshold = 0.2f, MaxShakeThreshold = 0.9f, MinShakeWindow = 0.1f, MaxShakeWindow = 1f;
        const float StickDeadzone = 0.2f, PanelW = 640f;
        float _userScale = 1f, _spawnDistance = 0.75f, _animSpeed = 3.5f, _stickSens = 0.6f;
        float _shakeThreshold = 0.5f, _shakeWindow = 0.35f, _shakeStateTime, _repeat;
        readonly float _followSmooth = 8f, _heightOffset = -0.05f;
        enum ShakeState { Neutral, WentDown, WentUp }
        ShakeState _shakeState;
        bool _open, _anchored, _triggerHeld, _backHeld;
        Transform _playerHead;
        Vector3 _targetPos;
        Quaternion _targetRot;
        GameObject _root;
        RawImage _castImage;
        Vector2 _debugStickL, _debugStickR;
        internal bool CastRequested => _open && _playerHead != null;
        internal void CloseCast() => CloseMenu();
        void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; Destroy(this); return; }
            Instance = this; DontDestroyOnLoad(gameObject); LoadSettings();
        }
        void Start()
        {
            _root = new GameObject("UltimateMenu_VR", typeof(RectTransform), typeof(Canvas));
            DontDestroyOnLoad(_root);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.sortingOrder = 600;
            _root.GetComponent<RectTransform>().sizeDelta = new Vector2(PanelW, PanelW);
            var image = new GameObject("MenuUI", typeof(RectTransform), typeof(RawImage));
            image.transform.SetParent(_root.transform, false);
            _castImage = image.GetComponent<RawImage>(); _castImage.raycastTarget = false;
            _root.SetActive(false);
            GorillaTagger.OnPlayerSpawned(FindHead); FindHead();
        }
        internal void PresentCast(RenderTexture texture, Rect crop)
        {
            if (_castImage == null || !CastRequested || crop.width <= 0 || crop.height <= 0) return;
            _castImage.texture = texture;
            _castImage.uvRect = new Rect(crop.x / texture.width, 1f - crop.yMax / texture.height, crop.width / texture.width, crop.height / texture.height);
            _castImage.rectTransform.sizeDelta = new Vector2(PanelW, PanelW * crop.height / crop.width);
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
            UpdateShakeToggle(_debugStickR);
            bool trigger = input != null && input.leftControllerTriggerButton;
            bool back = input != null && input.leftControllerPrimaryButton;
            if (CastRequested)
            {
                _repeat -= Time.unscaledDeltaTime;
                if (_debugStickL.magnitude < _stickSens) _repeat = 0;
                else if (_repeat <= 0)
                {
                    MenuUI.Instance?.NavigateVr(_debugStickL);
                    _repeat = 0.22f;
                }
                if (trigger && !_triggerHeld) MenuUI.Instance?.ActivateVr();
                if (back && !_backHeld) MenuUI.Instance?.BackVr();
            }
            _triggerHeld = trigger; _backHeld = back;
            bool visible = CastRequested && _castImage.texture != null;
            _root.SetActive(visible);
            if (visible) { UpdateFollow(); _root.transform.localScale = Vector3.one * (0.001f * _userScale); }
        }
        void OpenMenu()
        {
            _open = true; _anchored = false; _repeat = 0;
            MenuUI.Instance?.ResetVrFocus(); SoundFX.PlaySound("Aigue");
        }
        void CloseMenu()
        {
            _open = false;
            if (_root != null) _root.SetActive(false);
            MenuUI.Instance?.CancelVrInput(); SoundFX.PlaySound("Grave");
        }
        public void RebuildTheme() { } // The shared PC renderer reads Theme directly.
        void OnDestroy()
        {
            CancelInvoke(); if (Instance == this) Instance = null;
            if (_root != null) { _root.SetActive(false); Destroy(_root); }
        }
        void LoadSettings()
        {
            _userScale = PlayerPrefs.GetFloat(PREF_SCALE, 1f);
            _spawnDistance = PlayerPrefs.GetFloat(PREF_DISTANCE, 0.75f);
            _animSpeed = PlayerPrefs.GetFloat(PREF_ANIMSPEED, 3.5f);
            _stickSens = PlayerPrefs.GetFloat(PREF_STICKSENS, 0.6f);
            _shakeThreshold = PlayerPrefs.GetFloat(PREF_SHAKE_THRESHOLD, 0.5f);
            _shakeWindow = PlayerPrefs.GetFloat(PREF_SHAKE_WINDOW, 0.35f);
        }
        void SaveSettings()
        {
            PlayerPrefs.SetFloat(PREF_SCALE, _userScale);
            PlayerPrefs.SetFloat(PREF_DISTANCE, _spawnDistance);
            PlayerPrefs.SetFloat(PREF_ANIMSPEED, _animSpeed);
            PlayerPrefs.SetFloat(PREF_STICKSENS, _stickSens);
            PlayerPrefs.SetFloat(PREF_SHAKE_THRESHOLD, _shakeThreshold);
            PlayerPrefs.SetFloat(PREF_SHAKE_WINDOW, _shakeWindow);
            PlayerPrefs.Save();
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
            if (_playerHead == null) return;

            Vector3 flatForward = _playerHead.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
            flatForward.Normalize();

            Vector3 desiredPos = _playerHead.position + flatForward * _spawnDistance + Vector3.up * _heightOffset;
            Quaternion desiredRot = Quaternion.LookRotation(flatForward, Vector3.up);

            if (!_anchored)
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

            _root.transform.position = _targetPos;
            _root.transform.rotation = _targetRot;
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
        public void DrawSettingsPage(MenuUI ui)
        {
            ui.Section(I18n.T("Core.vr_section"));
            ui.Label(I18n.T("Core.vr_controls"), Theme.Text, 10);



            ui.Space(4);
            ui.BeginCard();
            float newScale = ui.Slider(I18n.T("Core.vr_size_short"), _userScale, MinUserScale, MaxUserScale, "0.00x");
            if (!Mathf.Approximately(newScale, _userScale)) { _userScale = newScale; SaveSettings(); }

            ui.Space(4);
            float newDist = ui.Slider(I18n.T("Core.vr_distance_short"), _spawnDistance, MinDistance, MaxDistance, "0.00m");
            if (!Mathf.Approximately(newDist, _spawnDistance)) { _spawnDistance = newDist; SaveSettings(); }
            ui.EndCard();

            ui.Space(4);
            ui.BeginCard();
            float newAnim = ui.Slider(I18n.T("Core.vr_animspeed_short"), _animSpeed, MinAnimSpeed, MaxAnimSpeed, "0.0");
            if (!Mathf.Approximately(newAnim, _animSpeed)) { _animSpeed = newAnim; SaveSettings(); }

            ui.Space(4);
            float newSens = ui.Slider(I18n.T("Core.vr_sticksens_short"), _stickSens, MinStickSens, MaxStickSens, "0.00");
            if (!Mathf.Approximately(newSens, _stickSens)) { _stickSens = newSens; SaveSettings(); }
            ui.EndCard();

            ui.Space(4);
            ui.BeginCard();
            float newShakeThreshold = ui.Slider(I18n.T("Core.vr_shaketreshold_short"), _shakeThreshold, MinShakeThreshold, MaxShakeThreshold, "0.00");
            if (!Mathf.Approximately(newShakeThreshold, _shakeThreshold)) { _shakeThreshold = newShakeThreshold; SaveSettings(); }

            ui.Space(4);
            float newShakeWindow = ui.Slider(I18n.T("Core.vr_shakewindow_short"), _shakeWindow, MinShakeWindow, MaxShakeWindow, "0.00");
            if (!Mathf.Approximately(newShakeWindow, _shakeWindow)) { _shakeWindow = newShakeWindow; SaveSettings(); }
            ui.EndCard();

            ui.Space(4);
            ui.BeginCard();
            ui.Label($"L:({_debugStickL.x:0.00};{_debugStickL.y:0.00}) - R:({_debugStickR.x:0.00};{_debugStickR.y:0.00})", Theme.Dim, 10);
            ui.EndCard();
        }
    }
}
