using System.Collections;
using UnityEngine;

namespace UltimateMenu
{
    public class GorillaShaders : MonoBehaviour
    {
        public static GorillaShaders Instance { get; private set; }

        /// <summary>
        /// Le mod est actif uniquement via le switch global de la page Mods (liste des mods).
        /// Il n'y a plus de toggle "Actif/Inactif" propre à cette page.
        /// </summary>
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Filter.Title");

        const string PfxBright = "MUM_SCF_Bright";
        const string PfxWarmth = "MUM_SCF_Warmth";
        const string PfxVig = "MUM_SCF_Vignette";

        static readonly Color WarmColor = new Color(1f, 0.6f, 0.25f);
        static readonly Color ColdColor = new Color(0.25f, 0.55f, 1f);

        float _bright;
        float _warm;
        float _vign;

        Texture2D _white, _black, _radial;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadPrefs();

            _white = Texture2D.whiteTexture;
            _black = MakeSolid(Color.black);
            _radial = MakeRadialVignette(128);

            StartCoroutine(RegisterPageWhenReady());
        }

        IEnumerator RegisterPageWhenReady()
        {
            float t = 0f;
            while (MenuUI.Instance == null && t < 15f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (MenuUI.Instance == null)
            {
                yield break;
            }

            MenuUI.Instance.RegisterPage("Filter.Title", DrawPage, MenuCategory.Apparence);
        }

        static Texture2D MakeSolid(Color c)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            return tex;
        }

        static Texture2D MakeRadialVignette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.Alpha8, false);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float maxDist = center.magnitude;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                    float a = Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, (d - 0.35f) / 0.65f));
                    tex.SetPixel(x, y, new Color(0, 0, 0, a));
                }
            tex.Apply();
            return tex;
        }

        void OnGUI()
        {
            if (!Enabled || Event.current.type != EventType.Repaint) return;

            Rect full = new Rect(0, 0, Screen.width, Screen.height);

            if (Mathf.Abs(_bright) > 0.001f)
            {
                float a = Mathf.Abs(_bright) * 0.6f;
                GUI.color = new Color(1, 1, 1, a);
                GUI.DrawTexture(full, _bright > 0 ? _white : _black);
            }

            if (Mathf.Abs(_warm) > 0.001f)
            {
                Color c = _warm > 0 ? WarmColor : ColdColor;
                float a = Mathf.Abs(_warm) * 0.35f;
                GUI.color = new Color(c.r, c.g, c.b, a);
                GUI.DrawTexture(full, _white);
            }

            if (_vign > 0.001f)
            {
                GUI.color = new Color(0, 0, 0, _vign);
                GUI.DrawTexture(full, _radial);
            }

            GUI.color = Color.white;
        }

        void LoadPrefs()
        {
            _bright = Mathf.Clamp(PlayerPrefs.GetFloat(PfxBright, 0f), -0.35f, 0.35f);
            _warm = Mathf.Clamp(PlayerPrefs.GetFloat(PfxWarmth, 0f), -0.35f, 0.35f);
            _vign = PlayerPrefs.GetFloat(PfxVig, 0f);
        }

        void SavePrefs()
        {
            PlayerPrefs.SetFloat(PfxBright, _bright);
            PlayerPrefs.SetFloat(PfxWarmth, _warm);
            PlayerPrefs.SetFloat(PfxVig, _vign);
            PlayerPrefs.Save();
        }

        public void ReloadConfig() => LoadPrefs();

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Filter.Section"));

            ui.BeginCard();
            ui.Label(I18n.T("Filter.Settings"), Theme.Dim, 9, true);
            ui.Space(6);

            float br = ui.Slider(I18n.T("Filter.Brightness"), _bright, -0.35f, 0.35f, "0.00");
            if (Enabled && !Mathf.Approximately(br, _bright)) { _bright = br; SavePrefs(); }

            float wm = ui.Slider(I18n.T("Filter.Warmth"), _warm, -0.35f, 0.35f, "0.00");
            if (Enabled && !Mathf.Approximately(wm, _warm)) { _warm = wm; SavePrefs(); }

            float vig = ui.Slider(I18n.T("Filter.Vignette"), _vign, 0f, 1f, "0.00");
            if (Enabled && !Mathf.Approximately(vig, _vign)) { _vign = vig; SavePrefs(); }

            ui.EndCard();
        }
    }
}