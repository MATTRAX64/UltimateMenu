using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltimateMenu
{
    public class GorillaMiror : MonoBehaviour
    {
        public static GorillaMiror Instance { get; private set; }
        Camera mc, mcG;
        int bw, bh, bwG, bhG, q;
        bool rdy, pReg, wasEn = true;

        const int MinQ = 1, MaxQ = 8;

        bool Enabled => MenuUI.Instance == null || MenuUI.Instance.IsModEnabled("Miror.Title");

        void Awake() { Instance = this; q = PlayerPrefs.GetInt("MUM_MirorQuality", 2); }
        void Start() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void Update()
        {
            if (!pReg && MenuUI.Instance != null) { MenuUI.Instance.RegisterPage("Miror.Title", DrawPage, MenuCategory.Cosmetique); pReg = true; }

            bool now = Enabled;
            if (now == wasEn) return;
            wasEn = now;

            if (now) { if (!rdy) Setup(); Apply(q); }
            else Apply(MinQ);
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m) { if (s.name == "City") Setup(); }

        void Setup()
        {
            var root = GameObject.Find("City_Pretty");
            if (root == null) return;

            var mG = FindChild(root.transform, "DressingRoom_Mirrors_Prefab");
            var m = root.transform.Find("CosmeticsRoomAnchor/nicegorillastore_prefab/DressingRoom_Mirrors_Prefab");
            if (m == null || mG == null) return;

            m.GetChild(1).gameObject.SetActive(false);
            mc = m.GetComponentInChildren<Camera>();
            mcG = mG.GetComponentInChildren<Camera>();
            if (mc == null || mcG == null) return;
            if (mc.targetTexture == null || mcG.targetTexture == null) return;

            mc.farClipPlane = 40f;
            mc.targetTexture.filterMode = mcG.targetTexture.filterMode = FilterMode.Point;

            if (bw == 0)
            {
                bw = mc.targetTexture.width; bh = mc.targetTexture.height;
                bwG = mcG.targetTexture.width; bhG = mcG.targetTexture.height;
            }

            SetLayers(root.transform);
            rdy = true;
            Apply(Enabled ? q : MinQ);
        }

        void Apply(int qty)
        {
            if (!rdy || mc == null || mcG == null) { rdy = false; return; }
            int v = Mathf.Clamp(qty, MinQ, MaxQ);
            Resize(mc.targetTexture, bw * v, bh * v);
            Resize(mcG.targetTexture, bwG * v, bhG * v);
        }

        static void Resize(RenderTexture rt, int w, int h)
        {
            if (rt.width == w && rt.height == h) return;
            bool active = RenderTexture.active == rt;
            rt.Release(); rt.width = w; rt.height = h; rt.Create();
            if (active) RenderTexture.active = rt;
        }

        static void SetLayers(Transform t)
        {
            if (t.gameObject.layer == LayerMask.NameToLayer("NoMirror")) t.gameObject.layer = 0;
            foreach (Transform c in t) SetLayers(c);
        }

        static Transform FindChild(Transform p, string name)
        {
            foreach (Transform c in p)
            {
                if (c.name == name) return c;
                var f = FindChild(c, name);
                if (f != null) return f;
            }
            return null;
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Miror.Section"));

            if (!rdy) { ui.BeginCard(); ui.Label(I18n.T("Miror.NotLoaded"), Theme.Yellow, 9); ui.EndCard(); ui.Space(6); }
            if (!Enabled) { ui.BeginCard(); ui.Label(I18n.T("Miror.DisabledHint"), Theme.Dim, 9); ui.EndCard(); ui.Space(6); }

            ui.BeginCard();
            int nq = ui.IntSlider(I18n.T("Miror.Quality"), q, MinQ, MaxQ, 100f);
            if (nq != q)
            {
                q = nq;
                PlayerPrefs.SetInt("MUM_MirorQuality", q);
                PlayerPrefs.Save();
                if (Enabled) Apply(q);
            }

            if (q > 3)
            {
                ui.Space(6); ui.Separator(); ui.Space(6);
                ui.Label(I18n.T("Miror.PerfWarning"), Theme.Yellow, 9, true);
            }
            ui.EndCard();
        }
    }
}