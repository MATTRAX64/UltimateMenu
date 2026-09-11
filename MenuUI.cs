using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Linq;

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
            Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            LoadClip(Sound.Aigue, "Assets.MenuSounds.Aigue.wav");
            LoadClip(Sound.Moyen, "Assets.MenuSounds.Moyen.wav");
            LoadClip(Sound.Grave, "Assets.MenuSounds.Grave.wav");
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

        // Décodeur WAV PCM minimal (mono/stéréo, 8/16/24/32 bits) — sans dépendance externe.
        static AudioClip WavToAudioClip(byte[] wav, string name)
        {
            try
            {
                int pos = 12; // skip "RIFF....WAVE"
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

        /// <summary>
        /// Joue un son du menu par son nom : "Aigue", "Moyen" ou "Grave".
        /// A appeler sur les interactions UI (boutons, sliders, toggles...).
        /// </summary>
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

        /// <summary>
        /// Si activé, force tout le texte du menu en gras pour améliorer la lisibilité.
        /// Activé par défaut.
        /// </summary>
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

        public static int Px(float refSize) => Mathf.Max(6, Mathf.RoundToInt(refSize * Scale));

        public static GUIStyle Sty(Color c, float s = 10, bool bold = false,
                                   TextAnchor align = TextAnchor.MiddleLeft) =>
            new GUIStyle(GUI.skin.label)
            {
                fontSize = Px(s),
                fontStyle = (bold || Theme.BoldText) ? FontStyle.Bold : FontStyle.Normal,
                alignment = align,
                wordWrap = true,
                font = BodyFont,
                normal = { textColor = c }
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

        public static void DrawShadow(Rect r, int radius = 20, float offsetY = 8f, float strength = 0.28f, float blur = 1.35f)
        {
            var prev = GUI.color;
            GUI.color = Color.white;
            const int layers = 5;
            for (int i = layers; i >= 1; i--)
            {
                float t = (float)i / layers;
                float grow = t * 10f * blur;
                float layerAlpha = strength * (1f - t) * 0.9f + strength * 0.12f;
                Rect layerRect = new Rect(r.x - grow, r.y - grow + offsetY * t, r.width + grow * 2f, r.height + grow * 2f);
                int layerRadius = Mathf.RoundToInt(radius + grow);
                var tex = MakeRoundedTex(new Color(0f, 0f, 0f, layerAlpha), Mathf.RoundToInt(layerRect.width), Mathf.RoundToInt(layerRect.height), layerRadius);
                GUI.DrawTexture(layerRect, tex);
            }
            GUI.color = prev;
        }

        public static void DrawRounded(Rect r, Color c, int radius = 14)
        {
            var tex = MakeRoundedTex(c, Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height), radius);
            var prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, tex);
            GUI.color = prev;
        }

        public static void DrawRoundedBorder(Rect r, Color c, int radius = 14, float thickness = 1f)
        {
            DrawRounded(r, c, radius);
            Rect inner = new Rect(r.x + thickness, r.y + thickness, r.width - thickness * 2, r.height - thickness * 2);
            if (inner.width > 2 && inner.height > 2)
            { var prev = GUI.color; GUI.color = Color.clear; GUI.color = prev; }
        }

        /// <summary>
        /// Dessine un contour plein et fiable autour d'un rect arrondi, en dessinant
        /// un rect légèrement plus grand dans la couleur du contour derrière la forme principale.
        /// A utiliser AVANT de dessiner le fond/la forme principale au même endroit.
        /// </summary>
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
            foreach (var kv in _roundedCache) if (kv.Value != null) Object.Destroy(kv.Value);
            _roundedCache.Clear();
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

        /// <summary>
        /// Détecte automatiquement la disposition clavier du système via le New Input System
        /// (Keyboard.current.keyboardLayout) et met à jour Current si elle change.
        /// À appeler régulièrement (ex: dans Update), car l'utilisateur peut changer de layout
        /// en cours de partie (ALT+SHIFT, changement de langue Windows, etc.).
        /// </summary>
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

        /// <summary>
        /// Convertit le nom de layout donné par l'OS/Input System en notre enum interne.
        /// Se base sur des mots-clés car le nom exact varie selon l'OS (Windows/Mac/Linux).
        /// </summary>
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

            return Layout.Unset; // layout inconnu -> on ne touche à rien, on garde la valeur précédente
        }

        // Tables complètes touche-physique -> label affiché, une par disposition.
        // La touche physique est toujours celle de référence QWERTY (comme KeyCode legacy
        // et comme Key du nouvel Input System quand basé sur la position physique).
        // Qwerty n'a pas besoin de table : c'est la référence, Display() renvoie kc.ToString().

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

        // Disposition russe ЙЦУКЕН (Jcuken) : très différente de Qwerty, lettres cyrilliques.
        // Mapping standard des touches physiques (position Qwerty) vers les lettres russes.
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
                default: return null; // Qwerty / Unset : pas de traduction, référence brute
            }
        }

        /// <summary>Touche physique -> label affiché, dans le layout ACTUEL (Current).</summary>
        public static string Display(KeyCode kc)
        {
            var table = TableFor(Current);
            if (table != null && table.TryGetValue(kc, out string mapped)) return mapped;
            return kc.ToString();
        }

        /// <summary>Touche physique -> label affiché, dans un layout DONNÉ (sans dépendre de Current).</summary>
        public static string Display(KeyCode kc, Layout layout)
        {
            var table = TableFor(layout);
            if (table != null && table.TryGetValue(kc, out string mapped)) return mapped;
            return kc.ToString();
        }

        /// <summary>
        /// Sens inverse : "je veux que la touche affiche ce label dans ce layout,
        /// quelle touche physique dois-je lire ?". Construit à partir de la même table
        /// que Display, donc ne peut jamais diverger — un seul endroit à maintenir.
        /// </summary>
        public static KeyCode Physical(string label, Layout layout)
        {
            var table = TableFor(layout);
            if (table != null)
                foreach (var kv in table)
                    if (kv.Value == label) return kv.Key;
            // Pas de traduction pour ce label dans ce layout : le label EST déjà le nom
            // de la touche physique de référence (cas Qwerty, ou lettre non retraduite).
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

    public class Page
    {
        public string Icon, Title;
        public MenuCategory Category;
        public bool IsCustom;
        public System.Action<MenuUI> Draw;

        public Page(string icon, string title, System.Action<MenuUI> draw,
                    MenuCategory category = MenuCategory.Gameplay, bool isCustom = false)
        { Icon = icon; Title = title; Draw = draw; Category = category; IsCustom = isCustom; }

        // Clé unique de sauvegarde pour l'état activé/désactivé de ce mod.
        // Basée sur le titre : si deux pages partagent le même titre, elles partageront cet état.
        string PrefKey => "MUM_ModEnabled_" + Title;

        /// <summary>
        /// Etat activé / désactivé du mod, persistant via PlayerPrefs.
        /// Activé par défaut (1) si jamais défini.
        /// </summary>
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
    public class MenuUI : MonoBehaviour
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
        private const float AnimDur = 0.22f;

        private bool _modsListAnimPlayed;
        private float _modsListAnimT;
        private const float ModsStagger = 0.09f;
        private const float ModsFadeDur = 0.35f;

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

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Trad.Register();
            _scale = PlayerPrefs.GetFloat("MUM_UIScale", 1f);
            _keyMenu = Keys.CreateKey("KeyMenu", KeyCode.F1);
            Theme.LoadSaved();
            RebuildTex();
        }

        void Start() { ComputeRect(); }

        public void RebuildTex()
        {
            UI.ClearRoundedCache();
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
        /// Permet à du code externe de connaître l'état d'un mod par son titre (avant traduction).
        /// </summary>
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

        public void StartRebind(string keyName) { RebindTarget = keyName; WaitingKey = true; }

        public void ToggleVisible()
        {
            bool willOpen = !_visible;
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
            float w = Mathf.Clamp(460f * _scale, 320, Screen.width > 0 ? Screen.width - 20 : 460);
            float h = Mathf.Clamp(540f * _scale, 380, Screen.height > 0 ? Screen.height - 20 : 540);

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
            // Détection auto du layout clavier — placée en tout début d'Update
            // pour que KeyLayout.Current soit toujours à jour avant tout le reste.
            KeyLayout.DetectAndApply();

            if (_visible && (Screen.width != _lastScreenW || Screen.height != _lastScreenH))
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

                if (_nav == -2 && !_modsListAnimPlayed) _modsListAnimT += Time.unscaledDeltaTime;
            }

            _keyMenu = Keys.GetKey("KeyMenu");
            if (Keyboard.current == null) return;

            Key menuKey = ToInputKey(_keyMenu);
            if (menuKey != Key.None && Keyboard.current[menuKey].wasPressedThisFrame) ToggleVisible();

            if (WaitingKey)
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

        const int WindowRadius = 22;
        const int PillRadius = 14;
        const int RowRadius = 12;

        void OnGUI()
        {
            if (!_visible) return;
            if (_rect.width < 10) ComputeRect();

            UI.Scale = _rect.height / UI.RefHeight;
            int winR = Mathf.RoundToInt(WindowRadius * UI.Scale);
            int pillR = Mathf.RoundToInt(PillRadius * UI.Scale);

            float rh = _rect.height, rw = _rect.width;
            float titleH = rh * 0.072f, tabH = rh * 0.052f, footH = rh * 0.034f;

            float animEase = 1f - Mathf.Pow(1f - Mathf.Clamp01(_animT), 3f);
            var prevMatrix = GUI.matrix;
            float slideOffset = (1f - animEase) * rw * 0.06f;
            float scaleAnim = Mathf.Lerp(0.96f, 1f, animEase);
            Vector2 pivot = new Vector2(_rect.x + _rect.width * 0.5f, _rect.y + _rect.height * 0.5f);
            GUI.matrix = Matrix4x4.TRS(new Vector3(pivot.x, pivot.y, 0), Quaternion.identity, Vector3.one)
                       * Matrix4x4.Scale(new Vector3(scaleAnim, scaleAnim, 1f))
                       * Matrix4x4.TRS(new Vector3(-pivot.x + slideOffset, -pivot.y, 0), Quaternion.identity, Vector3.one)
                       * prevMatrix;

            var ev = Event.current;
            if (animEase >= 0.999f)
            {
                Rect tdr = new Rect(_rect.x, _rect.y, _rect.width, titleH + 4f);
                if (ev != null && ev.type == EventType.MouseDown && tdr.Contains(ev.mousePosition))
                { _dragging = true; _dragOff = ev.mousePosition - new Vector2(_rect.x, _rect.y); ev.Use(); }
                if (_dragging && ev != null && ev.type == EventType.MouseDrag)
                { _rect.x = ev.mousePosition.x - _dragOff.x; _rect.y = ev.mousePosition.y - _dragOff.y; ev.Use(); }
                if (ev != null && ev.type == EventType.MouseUp && _dragging) { _dragging = false; SavePosition(); }
            }

            UI.DrawShadow(_rect, radius: winR, offsetY: 10f * UI.Scale, strength: 0.30f * animEase, blur: 1.4f);

            Color bgAnim = Theme.BG; bgAnim.a *= animEase;
            UI.DrawRounded(_rect, bgAnim, winR);

            GUI.Label(new Rect(_rect.x + 20, _rect.y + 6, _rect.width - 40, titleH),
                Log.PluginName ?? "UltimateMenu", UI.Sty(Theme.Text, Mathf.RoundToInt(rh * 0.032f), true));

            bool inPage = _nav >= 0;
            float tabsY = _rect.y + titleH + 2f;

            Rect tabsBarRect = new Rect(_rect.x + 16, tabsY, _rect.width - 32, tabH);
            UI.DrawRounded(tabsBarRect, Theme.BG2, pillR);

            string[] tabs = inPage
                ? new[] { I18n.T("Core.tab_home"), I18n.T("Core.tab_modes"), (_nav < _pages.Count ? I18n.T(_pages[_nav].Title) : "") }
                : new[] { I18n.T("Core.tab_home"), I18n.T("Core.tab_modes") };

            float tw = tabsBarRect.width / tabs.Length;
            int tfs = Mathf.RoundToInt(rh * 0.021f);
            int selIndex = inPage ? 2 : (_nav == -2 ? 1 : 0);

            Rect pillRect = new Rect(tabsBarRect.x + tw * selIndex + 3, tabsBarRect.y + 3, tw - 6, tabsBarRect.height - 6);
            UI.DrawRounded(pillRect, Theme.BG3, Mathf.Max(4, pillR - 4));
            Color pillAccent = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.16f);
            UI.DrawRounded(pillRect, pillAccent, Mathf.Max(4, pillR - 4));

            for (int i = 0; i < tabs.Length; i++)
            {
                bool sel = i == selIndex;
                Rect tr = new Rect(tabsBarRect.x + tw * i, tabsY, tw, tabH);
                GUI.Label(tr, tabs[i], new GUIStyle(GUI.skin.label) { fontSize = tfs, font = UI.BodyFont, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = sel ? Theme.Accent : Theme.Dim } });
                if (i < 2 && GUI.Button(tr, "", GUIStyle.none))
                {
                    bool wasOnModes = (_nav == -2);
                    SoundFX.PlaySound("Moyen");
                    if (i == 0) { _nav = -1; _scrollPage = Vector2.zero; } else { _nav = -2; }
                    if (_nav == -2 && !wasOnModes && !_modsListAnimPlayed) _modsListAnimT = 0.0001f;
                }
            }

            float cy = tabsY + tabH + 10f;
            Rect cr = new Rect(_rect.x + 14, cy, _rect.width - 28, _rect.height - footH - cy + _rect.y - 10f);
            GUILayout.BeginArea(cr);
            if (_nav == -1) DrawHome(cr);
            else if (_nav == -2) DrawModes(cr);
            else if (_nav >= 0 && _nav < _pages.Count) DrawActivePage(_pages[_nav], cr);
            else DrawHome(cr);
            GUILayout.EndArea();

            // --- Footer : texte centré sur toute la largeur, bouton Donate à droite ---
            float fy = _rect.y + _rect.height - footH - 8f;

            float donateW = Mathf.Clamp(70f * UI.Scale, 50f, 110f);
            float donateH = footH;

            string footText = "[" + KeyLayout.Display(_keyMenu) + "]  " + I18n.T("Core.close_footer") + "   ·   v" + (Log.PluginVersion ?? "?") + "   ·   MATTRAX";
            var footTextStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(rh * 0.0165f), font = UI.BodyFont, alignment = TextAnchor.MiddleCenter, normal = { textColor = Theme.Dim } };

            Rect footLabelRect = new Rect(_rect.x + 16, fy, _rect.width - 32, footH);
            GUI.Label(footLabelRect, footText, footTextStyle);

            Rect donateRect = new Rect(_rect.x + _rect.width - 16 - donateW, fy + (footH - donateH) * 0.5f, donateW, donateH);
            Color donateBase = Theme.Accent;
            bool donateHover = ev != null && donateRect.Contains(ev.mousePosition);
            Color donateCol = donateHover
                ? new Color(donateBase.r * 0.86f, donateBase.g * 0.86f, donateBase.b * 0.86f, donateBase.a)
                : donateBase;
            UI.DrawRounded(donateRect, donateCol, Mathf.RoundToInt(donateH * 0.4f));
            GUI.Label(donateRect, "♥ Donate", new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(rh * 0.016f),
                fontStyle = FontStyle.Bold,
                font = UI.BodyFont,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            });
            if (GUI.Button(donateRect, "", GUIStyle.none))
            {
                SoundFX.PlaySound("Moyen");
                Application.OpenURL(DonateUrl);
            }

            GUI.matrix = prevMatrix;
        }

        Vector2 BeginCustomScroll(Vector2 scroll, Rect outerRect, float measuredContentH)
        {
            float viewH = Mathf.Max(measuredContentH, outerRect.height + 1f);
            Rect viewRect = new Rect(0, 0, outerRect.width, viewH);
            scroll = GUI.BeginScrollView(new Rect(0, 0, outerRect.width, outerRect.height), scroll, viewRect, false, false,
                GUIStyle.none, GUIStyle.none);
            GUILayout.BeginArea(new Rect(0, 0, viewRect.width, viewH));
            GUILayout.BeginVertical();
            return scroll;
        }

        float EndCustomScroll(Vector2 scroll, Rect outerRect, ref float contentHRef, int scrollId)
        {
            GUILayout.EndVertical();
            Rect measured = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint && measured.height > 1f)
                contentHRef = measured.height;
            GUILayout.EndArea();
            GUI.EndScrollView();
            return contentHRef;
        }

        void DrawHome(Rect outerRect)
        {
            _scrollPage = BeginCustomScroll(_scrollPage, outerRect, _homeContentH);

            Section(I18n.T("Core.home_title"));
            BeginCard();
            Label(I18n.T("Core.home_desc"), Theme.Text, 11);
            Label(I18n.T("Core.home_author"), Theme.Accent, 10, true);
            EndCard();

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

            // --- Disposition clavier (détectée automatiquement, lecture seule) ---
            BeginCard();
            GUILayout.BeginHorizontal();
            Label(I18n.T("Core.keyboard_label"), Theme.Text, 11, true, TextAnchor.MiddleLeft);
            GUILayout.FlexibleSpace();
            Label(KeyLayout.Current.ToString(), Theme.Accent, 11, true, TextAnchor.MiddleRight);
            GUILayout.EndHorizontal();
            Space(4);
            Label(I18n.T("Core.keyboard_auto_hint"), Theme.Dim, 9);
            EndCard();

            // --- Thème ---
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

            // --- Langue ---
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

            // --- Texte en gras (réglage simple, sa propre carte) ---
            BeginCard();
            bool newBoldText = Toggle(I18n.T("Core.bold_text_label"), Theme.BoldText, 150f);
            if (newBoldText != Theme.BoldText) Theme.BoldText = newBoldText;
            EndCard();

            // --- Sons du menu (réglage simple, sa propre carte) ---
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

            Space(8); BeginCard();
            Label("[" + KeyLayout.Display(_keyMenu) + "] " + I18n.T("Core.open_close_hint") + "  |  " + _pages.Count + " " + I18n.T("Core.modules_suffix"), Theme.Dim, 9);
            EndCard();

            EndCustomScroll(_scrollPage, outerRect, ref _homeContentH, 1);
        }

        static string LangName(Lang l)
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
            if (_modsListAnimT <= 0f && !_modsListAnimPlayed) _modsListAnimT = 0.0001f;

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
            _search = GUI.TextField(searchInner, _search, searchFieldStyle);
            if (string.IsNullOrEmpty(_search))
                GUI.Label(searchInner, I18n.T("Core.search_label"), new GUIStyle(GUI.skin.label) { fontSize = UI.Px(10), fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal, font = UI.BodyFont, alignment = TextAnchor.MiddleLeft, normal = { textColor = Theme.Dim } });
            if (!string.IsNullOrEmpty(_search))
            {
                Rect xRect = new Rect(searchBg.x + searchBg.width - searchH, searchBg.y, searchH, searchH);
                if (GUI.Button(xRect, "×", new GUIStyle(GUI.skin.label) { fontSize = UI.Px(14), fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal, font = UI.BodyFont, alignment = TextAnchor.MiddleCenter, normal = { textColor = Theme.Dim } }))
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

                float rowEase = 1f;
                if (!_modsListAnimPlayed)
                {
                    float rowT = _modsListAnimT - shown * ModsStagger;
                    rowEase = Mathf.Clamp01(rowT / ModsFadeDur);
                    rowEase = Mathf.SmoothStep(0f, 1f, rowEase);
                }

                if (p.Category != lastCategory)
                {
                    lastCategory = p.Category;
                    shown++;
                    {
                        Space(shown > 1 ? 8f : 0f);
                        Color catTitleCol = Theme.Accent; catTitleCol.a = rowEase;
                        GUILayout.Space(Mathf.Lerp(14f, 0f, rowEase) * UI.Scale);
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
                GUILayout.Space(Mathf.Lerp(14f, 0f, rowEase) * UI.Scale);

                string pTitleDisplay = I18n.T(p.Title);

                // Dimensions réservées pour le toggle activé/désactivé
                float toggleD = 40f * UI.Scale; // largeur du switch style Apple
                float toggleRightMargin = 12f * UI.Scale;
                float rightReserved = toggleD + toggleRightMargin;

                Rect rowRect = GUILayoutUtility.GetRect(new GUIContent(pTitleDisplay),
                    new GUIStyle(GUI.skin.button)
                    {
                        fontSize = UI.Px(11),
                        fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal,
                        font = UI.BodyFont,
                        padding = new RectOffset(14, 14, 9, 9),
                        wordWrap = true
                    }, GUILayout.ExpandWidth(true), GUILayout.MinHeight(34 * UI.Scale));

                Color rowBg = Theme.BG3; rowBg.a *= rowEase;
                bool rowHover = Event.current != null && rowRect.Contains(Event.current.mousePosition) && rowEase > 0.95f;
                if (rowHover) rowBg = new Color(rowBg.r * 0.82f, rowBg.g * 0.82f, rowBg.b * 0.82f, rowBg.a);
                UI.DrawRounded(rowRect, rowBg, Mathf.RoundToInt(RowRadius * UI.Scale));

                Color rowText = Theme.Text; rowText.a = rowEase;
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

                // --- Toggle activé / désactivé (switch style Apple, cliquable) ---
                float swW = 40f * UI.Scale, swH = 22f * UI.Scale;
                Rect toggleRect = new Rect(
                    rowRect.x + rowRect.width - toggleD - toggleRightMargin,
                    rowRect.y + (rowRect.height - swH) * 0.5f,
                    swW, swH);

                bool modEnabled = p.Enabled;
                DrawSwitchAt(toggleRect, modEnabled, rowEase);

                bool toggleClicked = GUI.Button(toggleRect, "", GUIStyle.none) && rowEase > 0.95f;
                if (toggleClicked)
                {
                    p.Enabled = !p.Enabled;
                    SoundFX.PlaySound(p.Enabled ? "Aigue" : "Grave");
                }

                // Le clic sur le reste de la ligne ouvre la page, sauf si on vient de cliquer sur le toggle
                bool rowClicked = GUI.Button(rowRect, "", GUIStyle.none) && rowEase > 0.95f;
                if (rowClicked && !toggleClicked &&
                    (Event.current == null || !toggleRect.Contains(Event.current.mousePosition)))
                {
                    SoundFX.PlaySound("Moyen");
                    _nav = i; _scrollPage = Vector2.zero;
                }

                GUILayout.EndHorizontal();
                Space(4);
            }
            if (!any) { BeginCard(); Label(I18n.T("Core.no_mod_found"), Theme.Dim, 10); EndCard(); }

            if (!_modsListAnimPlayed &&
                (_modsListAnimT > 2f || (shown > 0 && _modsListAnimT > shown * ModsStagger + ModsFadeDur)))
                _modsListAnimPlayed = true;

            EndCustomScroll(_scrollModes, outerRect, ref _modesContentH, 2);
        }

        void DrawActivePage(Page page, Rect outerRect)
        {
            _scrollPage = BeginCustomScroll(_scrollPage, outerRect, _pageContentH);
            page.Draw(this);
            EndCustomScroll(_scrollPage, outerRect, ref _pageContentH, 3);
        }

        public void Section(string title)
        {
            GUILayout.Space(10);
            GUILayout.Label(title.ToUpperInvariant(), new GUIStyle(GUI.skin.label)
            {
                fontSize = UI.Px(9),
                fontStyle = FontStyle.Bold,
                font = UI.BodyFont,
                normal = { textColor = Theme.Dim },
                wordWrap = true
            });
            GUILayout.Space(4);
        }

        public void BeginCard(Color? bg = null) => GUILayout.BeginVertical(UI.CardRoundedS(bg ?? Theme.BG2, Mathf.RoundToInt(RowRadius * UI.Scale)));
        public void EndCard() { GUILayout.EndVertical(); GUILayout.Space(6); }

        /// <summary>
        /// Dessine un en-tête de section dépliable : simple rectangle cliquable avec label et valeur actuelle.
        /// Toute la barre sert de bouton — pas d'élément séparé à droite.
        /// Le fond change de couleur selon l'état (plus clair/accentué quand déplié) pour indiquer l'état.
        /// Utilise de préférence <see cref="ExpandableSection"/> qui gère tout automatiquement.
        /// </summary>
        public bool ExpandableHeader(string label, string valueText, bool isOpen, float labelWidth = 110f)
        {
            float rowH = 32f * UI.Scale;
            Rect headerRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));

            bool hover = Event.current != null && headerRect.Contains(Event.current.mousePosition);
            Color headerBg = isOpen ? Theme.BG3 : (hover ? Darken(Theme.BG3, -0.06f) : Theme.BG2);
            // Darken avec valeur négative éclaircit légèrement au survol pour un effet interactif discret
            UI.DrawRounded(headerRect, headerBg, Mathf.RoundToInt(RowRadius * UI.Scale * 0.8f));

            float pad = 12f * UI.Scale;
            var labelStyle = UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleLeft);
            var valueStyle = UI.Sty(Theme.Accent, 10, true, TextAnchor.MiddleRight);

            Rect labelRect = new Rect(headerRect.x + pad, headerRect.y, labelWidth * UI.Scale, headerRect.height);
            GUI.Label(labelRect, label, labelStyle);

            Rect valueRect = new Rect(labelRect.x + labelRect.width, headerRect.y,
                headerRect.width - labelRect.width - pad * 2f, headerRect.height);
            GUI.Label(valueRect, valueText, valueStyle);

            bool clicked = GUI.Button(headerRect, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound("Moyen");
            GUILayout.Space(2);
            return clicked ? !isOpen : isOpen;
        }

        /// <summary>Ouvre un encart légèrement retrait/assombri pour le contenu d'une section dépliée.</summary>
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

        /// <summary>
        /// Fonction englobante prête à l'emploi pour une section dépliable complète :
        /// gère la carte, l'en-tête (label + valeur + chevron vert/blanc) et le contenu déplié.
        /// Le contenu est fourni via un delegate, ce qui permet d'y mettre n'importe quels éléments UI.
        /// </summary>
        /// <param name="label">Nom affiché à gauche de l'en-tête.</param>
        /// <param name="valueText">Valeur actuelle affichée à droite (ex: thème actif, langue actuelle).</param>
        /// <param name="isOpen">Etat actuel (ouvert/fermé), passé par référence pour être mis à jour automatiquement.</param>
        /// <param name="drawContent">Delegate qui dessine le contenu à afficher quand la section est dépliée.</param>
        /// <param name="labelWidth">Largeur réservée au label, en pixels de référence (mise à l'échelle automatiquement).</param>
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
            => GUILayout.Label(text, UI.Sty(c ?? Theme.Text, size, bold, align));

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
                probe = GUILayoutUtility.GetRect(w, height);
            }
            else
            {
                probe = GUILayoutUtility.GetRect(new GUIContent(text), baseStyle);
            }
            bool hover = Event.current != null && probe.Contains(Event.current.mousePosition);
            Color drawCol = hover ? Darken(baseCol, 0.14f) : baseCol;
            var s = UI.BtnRoundedS(drawCol, tc ?? Color.white, 10, Mathf.RoundToInt(10 * UI.Scale));
            s.normal.background = s.hover.background = s.active.background = UI.MakeRoundedTex(drawCol, 80, 40, Mathf.RoundToInt(10 * UI.Scale));
            bool r = GUI.Button(probe, text, s);
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
            bool clicked = GUI.Button(r, "", GUIStyle.none);
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
            bool clicked = GUI.Button(r, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound("Moyen");
            return clicked;
        }

        public bool SmallBtn(string text, Texture2D bg = null, Color? tc = null)
        {
            var s = UI.BtnRoundedS(ResolveBg(bg), tc ?? Color.white, 9, Mathf.RoundToInt(7 * UI.Scale));
            s.padding = new RectOffset(4, 4, 2, 2);
            bool r = GUILayout.Button(text, s, GUILayout.Width(text.Length > 2 ? 52 : 22));
            if (r) SoundFX.PlaySound("Moyen");
            return r;
        }

        /// <summary>
        /// Dessine un switch style Apple (piste + pilule) avec un contour discret sur la piste
        /// et sur la pilule pour bien les distinguer du fond. Ne gère pas le clic.
        /// </summary>
        /// <param name="alpha">Opacité globale (utilisée pour les animations d'apparition).</param>
        public void DrawSwitchAt(Rect swRect, bool value, float alpha = 1f)
        {
            float swH = swRect.height;

            Color trackCol = value ? Theme.Green : Theme.BG3;
            trackCol.a *= alpha;
            bool hover = Event.current != null && swRect.Contains(Event.current.mousePosition);
            if (hover) trackCol = new Color(trackCol.r * 0.9f, trackCol.g * 0.9f, trackCol.b * 0.9f, trackCol.a);

            // Contour discret sur la piste
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

            // Contour discret sur la pilule pour la distinguer de la piste
            float knobBorder = Mathf.Max(1f, 1.5f * UI.Scale);
            Rect knobBorderRect = new Rect(
                knobRect.x - knobBorder, knobRect.y - knobBorder,
                knobRect.width + knobBorder * 2f, knobRect.height + knobBorder * 2f);
            Color knobBorderCol = new Color(0f, 0f, 0f, 0.22f * alpha);
            UI.DrawRounded(knobBorderRect, knobBorderCol, Mathf.RoundToInt(knobBorderRect.height * 0.5f));

            Color knobCol = new Color(1f, 1f, 1f, alpha);
            UI.DrawRounded(knobRect, knobCol, Mathf.RoundToInt(knobD * 0.5f));
        }

        /// <summary>
        /// Switch style Apple avec contour, en layout GUILayout (comme un Toggle classique).
        /// </summary>
        public bool Toggle(string label, bool value, float lw = 100f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UI.Sty(Theme.Text, 10), GUILayout.Width(lw * UI.Scale));
            GUILayout.FlexibleSpace();
            float swW = 44 * UI.Scale, swH = 24 * UI.Scale;
            Rect swRect = GUILayoutUtility.GetRect(swW, swH, GUILayout.Width(swW), GUILayout.Height(swH));
            DrawSwitchAt(swRect, value, 1f);
            bool clicked = GUI.Button(swRect, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound(value ? "Grave" : "Aigue");
            GUILayout.EndHorizontal();
            return clicked ? !value : value;
        }

        /// <summary>
        /// Bouton rond uni (sans glyphe), à la même taille que la pilule du switch Apple,
        /// avec une ombre légère et un double contour pour bien lui donner l'apparence d'un vrai bouton.
        /// Fonction de base réutilisée par PlusBtn / MinusBtn / ChevronBtn.
        /// </summary>
        public bool RoundBtn(Color color, float diameter)
        {
            Rect r = GUILayoutUtility.GetRect(diameter, diameter, GUILayout.Width(diameter), GUILayout.Height(diameter));
            bool hover = Event.current != null && r.Contains(Event.current.mousePosition);
            Color bgCol = hover ? Darken(color, 0.12f) : color;

            // Légère ombre portée pour un effet de relief (comme la pilule du switch)
            UI.DrawShadow(r, radius: Mathf.RoundToInt(diameter * 0.5f), offsetY: 1.2f * UI.Scale, strength: 0.20f, blur: 1f);

            // Contour extérieur clair (relief) + contour intérieur sombre (délimitation nette)
            float outerBorder = Mathf.Max(1f, 1.6f * UI.Scale);
            Rect outerRect = new Rect(r.x - outerBorder, r.y - outerBorder, r.width + outerBorder * 2f, r.height + outerBorder * 2f);
            UI.DrawRounded(outerRect, new Color(1f, 1f, 1f, 0.18f), Mathf.RoundToInt(outerRect.width * 0.5f));

            float innerBorder = Mathf.Max(1f, 1f * UI.Scale);
            Rect innerRect = new Rect(r.x - innerBorder, r.y - innerBorder, r.width + innerBorder * 2f, r.height + innerBorder * 2f);
            UI.DrawRounded(innerRect, new Color(0f, 0f, 0f, 0.35f), Mathf.RoundToInt(innerRect.width * 0.5f));

            UI.DrawRounded(r, bgCol, Mathf.RoundToInt(diameter * 0.5f));

            bool clicked = GUI.Button(r, "", GUIStyle.none);
            if (clicked)
            {
                if (color == Theme.Green) SoundFX.PlaySound("Aigue");
                else if (color == Theme.Red) SoundFX.PlaySound("Grave");
                else SoundFX.PlaySound("Moyen");
            }
            return clicked;
        }

        /// <summary>Diamètre standard des boutons ronds, identique à la pilule du switch Apple (swH - 4*Scale).</summary>
        public static float StandardRoundBtnSize => (22f - 4f) * UI.Scale;

        /// <summary>Bouton rond vert uni, pour incrémenter une valeur.</summary>
        public bool PlusBtn(float diameter = -1f) => RoundBtn(Theme.Green, diameter > 0 ? diameter : StandardRoundBtnSize);

        /// <summary>Bouton rond rouge uni, pour décrémenter une valeur.</summary>
        public bool MinusBtn(float diameter = -1f) => RoundBtn(Theme.Red, diameter > 0 ? diameter : StandardRoundBtnSize);

        /// <summary>Bouton rond blanc uni, pour les indicateurs d'ouverture/fermeture (›, ⌄).</summary>
        public bool ChevronBtn(float diameter = -1f) => RoundBtn(Color.white, diameter > 0 ? diameter : StandardRoundBtnSize);


        public float Slider(string label, float val, float min, float max, string fmt = "0.0", float lw = 90f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UI.Sty(Theme.Text, 10), GUILayout.Width(lw * UI.Scale));
            float nv = DrawStyledSlider(val, min, max);
            GUILayout.Label(nv.ToString(fmt), UI.Sty(Theme.Accent, 10), GUILayout.Width(40 * UI.Scale));
            GUILayout.EndHorizontal();
            return nv;
        }

        public int IntSlider(string label, int val, int min, int max, float lw = 90f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UI.Sty(Theme.Text, 10), GUILayout.Width(lw * UI.Scale));
            int nv = Mathf.RoundToInt(DrawStyledSlider(val, min, max));
            GUILayout.Label(nv.ToString(), UI.Sty(Theme.Accent, 10), GUILayout.Width(40 * UI.Scale));
            GUILayout.EndHorizontal();
            return nv;
        }

        float DrawStyledSlider(float val, float min, float max)
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

            return val;
        }

        public void StatusPill(string text, bool active)
            => GUILayout.Label((active ? "o " : "- ") + text, UI.Sty(active ? Theme.Green : Theme.Dim, 11, true));

        public bool RebindBtn(string name, KeyCode current, float nw = 110f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, UI.Sty(Theme.Text, 10), GUILayout.Width(nw * UI.Scale));
            var s = UI.BtnRoundedS(Theme.BG3, Theme.Accent, 10, Mathf.RoundToInt(8 * UI.Scale));
            bool r = GUILayout.Button(KeyLayout.Display(current), s, GUILayout.Width(130 * UI.Scale));
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
            return w > 0 ? GUILayout.TextField(val, s, GUILayout.Width(w)) : GUILayout.TextField(val, s);
        }

        public int ModePicker(string[] labels, int cur)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
            {
                bool sel = cur == i;
                var s = UI.BtnRoundedS(sel ? Theme.Accent : Theme.BG3, sel ? Color.white : Theme.Text, 9, Mathf.RoundToInt(7 * UI.Scale));
                s.padding = new RectOffset(6, 6, 3, 3);
                if (GUILayout.Button(labels[i], s, GUILayout.Width(62 * UI.Scale)))
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
                new GUIStyle(GUI.skin.label) { fontSize = fsBody, font = UI.BodyFont, normal = { textColor = Theme.Dim }, alignment = TextAnchor.MiddleLeft });

            GUI.matrix = om; GUI.color = oc;
            if (Event.current.type == EventType.MouseDown && capsule.Contains(Event.current.mousePosition))
            { MenuUI.Instance?.ToggleVisible(); Event.current.Use(); }
        }
    }
}