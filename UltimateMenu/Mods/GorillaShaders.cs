using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace UltimateMenu
{
    public class GorillaShaders : MonoBehaviour
    {
        public static GorillaShaders Instance { get; private set; }
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Filter.Title");
        const string P = "MUM_SCF_";
        const string CustomListKey = P + "CustomList";
        float _bright, _warm, _vign, _vignSoft = 0.5f, _contrast, _saturation, _tint, _grain, _sepia, _gray, _pulse;
        Color _tintColor = Color.white;
        Texture2D _white, _black, _radial, _grainTex;
        float _grainTimer, _pulseT, _vignRedrawTimer;
        bool _vignSoftDirty;
        string _newPresetName = "";
        List<string> _customPresets = new List<string>();
        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadPrefs();
            LoadCustomList();
            _white = Texture2D.whiteTexture;
            _black = Solid(Color.black);
            _radial = Vignette(128, _vignSoft);
            _grainTex = Grain(96);
            StartCoroutine(RegisterPageWhenReady());
        }
        IEnumerator RegisterPageWhenReady()
        {
            for (float t = 0f; MenuUI.Instance == null && t < 15f; t += Time.unscaledDeltaTime) yield return null;
            if (MenuUI.Instance != null) MenuUI.Instance.RegisterPage("Filter.Title", DrawPage, MenuCategory.Apparence);
        }
        void Update()
        {
            if (!Enabled) return;
            if (_grain > 0.001f && (_grainTimer += Time.unscaledDeltaTime) >= 0.05f) { _grainTimer = 0f; RandomizeGrain(_grainTex); }
            if (_pulse > 0.001f) _pulseT += Time.unscaledDeltaTime;
            if (_vignSoftDirty && (_vignRedrawTimer += Time.unscaledDeltaTime) >= 0.08f) { _vignRedrawTimer = 0f; _vignSoftDirty = false; RedrawVignetteRef(); }
        }
        static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
        static Texture2D Vignette(int size, float softness)
        {
            var tex = new Texture2D(size, size, TextureFormat.Alpha8, false);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = center.magnitude, start = Mathf.Lerp(0.15f, 0.55f, softness);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, new Color(0, 0, 0, Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, (Vector2.Distance(new Vector2(x, y), center) / maxDist - start) / (1f - start)))));
            tex.Apply();
            return tex;
        }
        static Texture2D Grain(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            RandomizeGrain(t);
            return t;
        }
        static void RandomizeGrain(Texture2D tex)
        {
            var px = new Color[tex.width * tex.height];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, Random.value);
            tex.SetPixels(px);
            tex.Apply();
        }
        void OnGUI()
        {
            if (!Enabled || Event.current.type != EventType.Repaint) return;
            Rect full = new Rect(0, 0, Screen.width, Screen.height);
            if (Mathf.Abs(_bright) > 0.001f) { GUI.color = new Color(1, 1, 1, Mathf.Abs(_bright) * 0.6f); GUI.DrawTexture(full, _bright > 0 ? _white : _black); }
            if (Mathf.Abs(_contrast) > 0.001f) { GUI.color = new Color(0.5f, 0.5f, 0.5f, Mathf.Abs(_contrast) * 0.4f); GUI.DrawTexture(full, _contrast < 0 ? _black : _white); }
            if (Mathf.Abs(_warm) > 0.001f) { Color c = _warm > 0 ? new Color(1f, 0.6f, 0.25f) : new Color(0.25f, 0.55f, 1f); GUI.color = new Color(c.r, c.g, c.b, Mathf.Abs(_warm) * 0.35f); GUI.DrawTexture(full, _white); }
            if (_tint > 0.001f) { GUI.color = new Color(_tintColor.r, _tintColor.g, _tintColor.b, _tint * 0.5f); GUI.DrawTexture(full, _white); }
            if (_sepia > 0.001f) { GUI.color = new Color(0.44f, 0.26f, 0.08f, _sepia * 0.55f); GUI.DrawTexture(full, _white); }
            if (_saturation > 0.001f) { GUI.color = new Color(0.5f, 0.5f, 0.5f, _saturation * 0.5f); GUI.DrawTexture(full, _white); }
            if (_gray > 0.001f) { GUI.color = new Color(0.5f, 0.5f, 0.5f, _gray * 0.7f); GUI.DrawTexture(full, _white); }
            if (_pulse > 0.001f) { float p = (Mathf.Sin(_pulseT * 2f) * 0.5f + 0.5f) * _pulse * 0.25f; GUI.color = new Color(1, 1, 1, p); GUI.DrawTexture(full, _white); }
            if (_grain > 0.001f) { GUI.color = new Color(1, 1, 1, _grain * 0.5f); GUI.DrawTextureWithTexCoords(full, _grainTex, new Rect(0, 0, Screen.width / 48f, Screen.height / 48f)); }
            if (_vign > 0.001f) { GUI.color = new Color(0, 0, 0, _vign); GUI.DrawTexture(full, _radial); }
            GUI.color = Color.white;
        }
        void LoadPrefs()
        {
            if (!PlayerPrefs.HasKey(P + "Init")) { ApplyDefaults(); PlayerPrefs.SetInt(P + "Init", 1); SavePrefs(); return; }
            _bright = Mathf.Clamp(PlayerPrefs.GetFloat(P + "Bright", 0f), -0.35f, 0.35f);
            _warm = Mathf.Clamp(PlayerPrefs.GetFloat(P + "Warmth", 0f), -0.35f, 0.35f);
            _vign = PlayerPrefs.GetFloat(P + "Vig", 0f);
            _vignSoft = PlayerPrefs.GetFloat(P + "VigSoft", 0.5f);
            _contrast = Mathf.Clamp(PlayerPrefs.GetFloat(P + "Contrast", 0f), -1f, 1f);
            _saturation = Mathf.Clamp01(PlayerPrefs.GetFloat(P + "Sat", 0f));
            _tint = Mathf.Clamp01(PlayerPrefs.GetFloat(P + "Tint", 0f));
            _tintColor = new Color(PlayerPrefs.GetFloat(P + "TintR", 1f), PlayerPrefs.GetFloat(P + "TintG", 1f), PlayerPrefs.GetFloat(P + "TintB", 1f));
            _grain = Mathf.Clamp01(PlayerPrefs.GetFloat(P + "Grain", 0f));
            _sepia = Mathf.Clamp01(PlayerPrefs.GetFloat(P + "Sepia", 0f));
            _gray = Mathf.Clamp01(PlayerPrefs.GetFloat(P + "Gray", 0f));
            _pulse = Mathf.Clamp01(PlayerPrefs.GetFloat(P + "Pulse", 0f));
        }
        void SavePrefs()
        {
            PlayerPrefs.SetFloat(P + "Bright", _bright); PlayerPrefs.SetFloat(P + "Warmth", _warm);
            PlayerPrefs.SetFloat(P + "Vig", _vign); PlayerPrefs.SetFloat(P + "VigSoft", _vignSoft);
            PlayerPrefs.SetFloat(P + "Contrast", _contrast); PlayerPrefs.SetFloat(P + "Sat", _saturation);
            PlayerPrefs.SetFloat(P + "Tint", _tint); PlayerPrefs.SetFloat(P + "TintR", _tintColor.r);
            PlayerPrefs.SetFloat(P + "TintG", _tintColor.g); PlayerPrefs.SetFloat(P + "TintB", _tintColor.b);
            PlayerPrefs.SetFloat(P + "Grain", _grain); PlayerPrefs.SetFloat(P + "Sepia", _sepia);
            PlayerPrefs.SetFloat(P + "Gray", _gray); PlayerPrefs.SetFloat(P + "Pulse", _pulse);
            PlayerPrefs.Save();
        }
        public void ReloadConfig() => LoadPrefs();
        void ApplyDefaults() { _bright = _warm = _vign = _contrast = _saturation = _tint = _grain = _sepia = _gray = _pulse = 0f; _vignSoft = 0.5f; _tintColor = Color.white; }
        void ResetAll() { ApplyDefaults(); RedrawVignetteRef(); SavePrefs(); }
        void RedrawVignetteRef()
        {
            int size = _radial.width;
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = center.magnitude, start = Mathf.Lerp(0.15f, 0.55f, _vignSoft);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = new Color(0, 0, 0, Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, (Vector2.Distance(new Vector2(x, y), center) / maxDist - start) / (1f - start))));
            _radial.SetPixels(px);
            _radial.Apply();
        }

        void LoadCustomList()
        {
            _customPresets.Clear();
            string raw = PlayerPrefs.GetString(CustomListKey, "");
            if (string.IsNullOrEmpty(raw)) return;
            _customPresets.AddRange(raw.Split('\n'));
        }
        void SaveCustomList() { PlayerPrefs.SetString(CustomListKey, string.Join("\n", _customPresets)); PlayerPrefs.Save(); }

        void SaveCustomPreset(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            string k = P + "C_" + name + "_";
            PlayerPrefs.SetFloat(k + "Bright", _bright); PlayerPrefs.SetFloat(k + "Warmth", _warm);
            PlayerPrefs.SetFloat(k + "Vig", _vign); PlayerPrefs.SetFloat(k + "VigSoft", _vignSoft);
            PlayerPrefs.SetFloat(k + "Contrast", _contrast); PlayerPrefs.SetFloat(k + "Sat", _saturation);
            PlayerPrefs.SetFloat(k + "Tint", _tint); PlayerPrefs.SetFloat(k + "TintR", _tintColor.r);
            PlayerPrefs.SetFloat(k + "TintG", _tintColor.g); PlayerPrefs.SetFloat(k + "TintB", _tintColor.b);
            PlayerPrefs.SetFloat(k + "Grain", _grain); PlayerPrefs.SetFloat(k + "Sepia", _sepia);
            PlayerPrefs.SetFloat(k + "Gray", _gray); PlayerPrefs.SetFloat(k + "Pulse", _pulse);
            if (!_customPresets.Contains(name)) _customPresets.Add(name);
            PlayerPrefs.SetString(CustomListKey, string.Join("\n", _customPresets));
            PlayerPrefs.Save();
        }
        void LoadCustomPreset(string name)
        {
            string k = P + "C_" + name + "_";
            if (!PlayerPrefs.HasKey(k + "Bright")) return;
            _bright = PlayerPrefs.GetFloat(k + "Bright"); _warm = PlayerPrefs.GetFloat(k + "Warmth");
            _vign = PlayerPrefs.GetFloat(k + "Vig"); _vignSoft = PlayerPrefs.GetFloat(k + "VigSoft");
            _contrast = PlayerPrefs.GetFloat(k + "Contrast"); _saturation = PlayerPrefs.GetFloat(k + "Sat");
            _tint = PlayerPrefs.GetFloat(k + "Tint");
            _tintColor = new Color(PlayerPrefs.GetFloat(k + "TintR"), PlayerPrefs.GetFloat(k + "TintG"), PlayerPrefs.GetFloat(k + "TintB"));
            _grain = PlayerPrefs.GetFloat(k + "Grain"); _sepia = PlayerPrefs.GetFloat(k + "Sepia");
            _gray = PlayerPrefs.GetFloat(k + "Gray"); _pulse = PlayerPrefs.GetFloat(k + "Pulse");
            RedrawVignetteRef();
            SavePrefs();
        }
        void DeleteCustomPreset(string name)
        {
            string k = P + "C_" + name + "_";
            foreach (var suffix in new[] { "Bright", "Warmth", "Vig", "VigSoft", "Contrast", "Sat", "Tint", "TintR", "TintG", "TintB", "Grain", "Sepia", "Gray", "Pulse" })
                PlayerPrefs.DeleteKey(k + suffix);
            _customPresets.Remove(name);
            SaveCustomList();
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Filter.Section"));
            ui.BeginCard();
            if (ui.Btn(I18n.T("Filter.Reset"))) ResetAll();
            ui.EndCard();

            ui.Space(6); ui.Section(I18n.T("Filter.CustomPresets") + " (" + _customPresets.Count + ")");
            ui.BeginCard();
            GUILayout.BeginHorizontal();
            _newPresetName = ui.TextField(_newPresetName, 0);
            if (ui.Btn(I18n.T("Filter.SaveAs")) && !string.IsNullOrEmpty(_newPresetName.Trim())) { SaveCustomPreset(_newPresetName.Trim()); _newPresetName = ""; }
            GUILayout.EndHorizontal();
            if (_customPresets.Count == 0)
            {
                ui.Space(4);
                ui.Label(I18n.T("Filter.NoCustomPresets"), Theme.Dim, 9);
            }
            else
            {
                ui.Space(4);
                foreach (var name in new List<string>(_customPresets))
                    DrawPresetRow(ui, name);
            }
            ui.EndCard();

            ui.BeginCard();
            ui.Label(I18n.T("Filter.Settings"), Theme.Dim, 9, true);
            ui.Space(6);
            _bright = SliderSave(ui, "Filter.Brightness", _bright, -0.35f, 0.35f);
            _contrast = SliderSave(ui, "Filter.Contrast", _contrast, -1f, 1f);
            _warm = SliderSave(ui, "Filter.Warmth", _warm, -0.35f, 0.35f);
            _saturation = SliderSave(ui, "Filter.Saturation", _saturation, 0f, 1f);
            ui.EndCard();

            ui.BeginCard();
            ui.Label(I18n.T("Filter.Effects"), Theme.Dim, 9, true);
            ui.Space(6);
            _vign = SliderSave(ui, "Filter.Vignette", _vign, 0f, 1f);
            float vs = ui.Slider(I18n.T("Filter.VignetteSoftness"), _vignSoft, 0f, 1f, "0.00");
            if (Enabled && !Mathf.Approximately(vs, _vignSoft))
            {
                _vignSoft = vs;
                _vignSoftDirty = true;
                _vignRedrawTimer = 0f;
                SavePrefs();
            }
            _grain = SliderSave(ui, "Filter.Grain", _grain, 0f, 1f);
            _sepia = SliderSave(ui, "Filter.Sepia", _sepia, 0f, 1f);
            _gray = SliderSave(ui, "Filter.Grayscale", _gray, 0f, 1f);
            _pulse = SliderSave(ui, "Filter.Pulse", _pulse, 0f, 1f);
            ui.EndCard();

            ui.BeginCard();
            ui.Label(I18n.T("Filter.CustomTint"), Theme.Dim, 9, true);
            ui.Space(6);
            _tint = SliderSave(ui, "Filter.TintIntensity", _tint, 0f, 1f);
            float tr = ui.Slider(I18n.T("Filter.TintR"), _tintColor.r, 0f, 1f, "0.00");
            float tg = ui.Slider(I18n.T("Filter.TintG"), _tintColor.g, 0f, 1f, "0.00");
            float tb = ui.Slider(I18n.T("Filter.TintB"), _tintColor.b, 0f, 1f, "0.00");
            if (Enabled && (!Mathf.Approximately(tr, _tintColor.r) || !Mathf.Approximately(tg, _tintColor.g) || !Mathf.Approximately(tb, _tintColor.b))) { _tintColor = new Color(tr, tg, tb); SavePrefs(); }
            ui.EndCard();
        }

        /// <summary>
        /// Row de preset : un seul rect de fond avec hover, le nom à gauche,
        /// un bouton rond rouge en overlay à droite (même ombre/contour que le MinusBtn du menu)
        /// pour supprimer, et un clic sur le reste de la row charge le preset.
        /// </summary>
        void DrawPresetRow(MenuUI ui, string name)
        {
            GUILayout.BeginHorizontal();

            float rowH = 34f * UI.Scale;
            float btnD = MenuUI.StandardRoundBtnSize + 6f * UI.Scale;
            float btnRightMargin = 10f * UI.Scale;
            float rightReserved = btnD + btnRightMargin;

            Rect rowRect = GUILayoutUtility.GetRect(new GUIContent(name),
                new GUIStyle(GUI.skin.button)
                {
                    fontSize = UI.Px(11),
                    fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal,
                    font = UI.BodyFont,
                    padding = new RectOffset(14, 14, 9, 9),
                    wordWrap = true
                }, GUILayout.ExpandWidth(true), GUILayout.MinHeight(rowH));

            bool hover = Event.current != null && rowRect.Contains(Event.current.mousePosition);
            Color rowBg = Theme.BG3;
            if (hover) rowBg = new Color(rowBg.r * 0.82f, rowBg.g * 0.82f, rowBg.b * 0.82f, rowBg.a);
            UI.DrawRounded(rowRect, rowBg, Mathf.RoundToInt(12 * UI.Scale));

            var rowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = UI.Px(11),
                fontStyle = Theme.BoldText ? FontStyle.Bold : FontStyle.Normal,
                font = UI.BodyFont,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, Mathf.RoundToInt(rightReserved + 6f * UI.Scale), 9, 9),
                wordWrap = true,
                normal = { textColor = Theme.Text }
            };
            GUI.Label(rowRect, name, rowStyle);

            Rect btnRect = new Rect(
                rowRect.x + rowRect.width - btnD - btnRightMargin,
                rowRect.y + (rowRect.height - btnD) * 0.5f,
                btnD, btnD);

            bool btnHover = Event.current != null && btnRect.Contains(Event.current.mousePosition);
            Color btnCol = btnHover ? new Color(Theme.Red.r * 0.86f, Theme.Red.g * 0.86f, Theme.Red.b * 0.86f, Theme.Red.a) : Theme.Red;

            UI.DrawShadow(btnRect, radius: Mathf.RoundToInt(btnD * 0.5f), offsetY: 1.2f * UI.Scale, strength: 0.20f, blur: 1f);
            float outerBorder = Mathf.Max(1f, 1.6f * UI.Scale);
            Rect outerRect = new Rect(btnRect.x - outerBorder, btnRect.y - outerBorder, btnRect.width + outerBorder * 2f, btnRect.height + outerBorder * 2f);
            UI.DrawRounded(outerRect, new Color(1f, 1f, 1f, 0.18f), Mathf.RoundToInt(outerRect.width * 0.5f));
            float innerBorder = Mathf.Max(1f, 1f * UI.Scale);
            Rect innerRect = new Rect(btnRect.x - innerBorder, btnRect.y - innerBorder, btnRect.width + innerBorder * 2f, btnRect.height + innerBorder * 2f);
            UI.DrawRounded(innerRect, new Color(0f, 0f, 0f, 0.35f), Mathf.RoundToInt(innerRect.width * 0.5f));
            UI.DrawRounded(btnRect, btnCol, Mathf.RoundToInt(btnD * 0.5f));

            bool deleteClicked = MenuUI.ControlButton(btnRect, "", GUIStyle.none) && Enabled;
            if (deleteClicked) { SoundFX.PlaySound("Grave"); DeleteCustomPreset(name); }

            bool rowClicked = MenuUI.ControlButton(rowRect, "", GUIStyle.none) && Enabled;
            if (rowClicked && !deleteClicked &&
                (Event.current == null || !btnRect.Contains(Event.current.mousePosition)))
            {
                SoundFX.PlaySound("Moyen");
                LoadCustomPreset(name);
            }

            GUILayout.EndHorizontal();
            ui.Space(4);
        }
        float SliderSave(MenuUI ui, string key, float val, float min, float max)
        {
            float v = ui.Slider(I18n.T(key), val, min, max, "0.00");
            if (Enabled && !Mathf.Approximately(v, val)) SavePrefs();
            return v;
        }
    }
}