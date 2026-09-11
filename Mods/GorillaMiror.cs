using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltimateMenu
{
    public class GorillaMiror : MonoBehaviour
    {
        public static GorillaMiror Instance { get; private set; }

        Camera _mirrorCam, _mirrorCamGTFC;
        int _baseWidth, _baseHeight, _baseWidthGTFC, _baseHeightGTFC;
        int _quality = 4; // qualité choisie par l'utilisateur (mémorisée)
        bool _ready, _pageRegistered;
        bool _wasEnabled = true;

        const int MinQuality = 1, MaxQuality = 8;
        const int PerfWarningThreshold = 3;

        bool Enabled => MenuUI.Instance == null || MenuUI.Instance.IsModEnabled("Miror.Title");

        void Awake()
        {
            Instance = this;
            _quality = PlayerPrefs.GetInt("MUM_MirorQuality", 4);
        }
        void Start() { SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        void Update()
        {
            if (!_pageRegistered && MenuUI.Instance != null)
            {
                MenuUI.Instance.RegisterPage("Miror.Title", DrawPage, MenuCategory.Cosmetique);
                _pageRegistered = true;
            }

            // Dès que le mod est désactivé depuis la page Mods, on remet la qualité appliquée à x1
            // (performances minimales) sans perdre le réglage préféré de l'utilisateur.
            bool nowEnabled = Enabled;
            if (nowEnabled != _wasEnabled)
            {
                _wasEnabled = nowEnabled;
                if (nowEnabled) ApplyQuality(_quality);
                else ApplyQuality(MinQuality);
            }
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "City") return;

            var root = GameObject.Find("City_Pretty");
            if (root == null) return;

            var mirrorGTFC = FindChildRecursive(root.transform, "DressingRoom_Mirrors_Prefab");
            var mirror = root.transform.Find("CosmeticsRoomAnchor/nicegorillastore_prefab/DressingRoom_Mirrors_Prefab");
            if (mirror == null || mirrorGTFC == null) return;

            mirror.GetChild(1).gameObject.SetActive(false);
            _mirrorCam = mirror.GetComponentInChildren<Camera>();
            _mirrorCamGTFC = mirrorGTFC.GetComponentInChildren<Camera>();
            if (_mirrorCam == null || _mirrorCamGTFC == null) return;

            _mirrorCam.farClipPlane = 40f;
            _mirrorCam.targetTexture.filterMode = FilterMode.Point;
            _mirrorCamGTFC.targetTexture.filterMode = FilterMode.Point;

            if (_baseWidth == 0)
            {
                _baseWidth = _mirrorCam.targetTexture.width;
                _baseHeight = _mirrorCam.targetTexture.height;
                _baseWidthGTFC = _mirrorCamGTFC.targetTexture.width;
                _baseHeightGTFC = _mirrorCamGTFC.targetTexture.height;
            }

            SetLayersRecursive(root.transform);
            _ready = true;
            ApplyQuality(Enabled ? _quality : MinQuality);
        }

        /// <summary>
        /// Applique une résolution donnée au(x) miroir(s). Le paramètre <paramref name="qualityToApply"/>
        /// permet de distinguer la qualité réellement appliquée (peut être forcée à x1 si le mod est
        /// désactivé) de la préférence utilisateur stockée dans <see cref="_quality"/>.
        /// </summary>
        void ApplyQuality(int qualityToApply)
        {
            if (!_ready) return;
            int q = Mathf.Clamp(qualityToApply, MinQuality, MaxQuality);
            Resize(_mirrorCam.targetTexture, _baseWidth * q, _baseHeight * q);
            Resize(_mirrorCamGTFC.targetTexture, _baseWidthGTFC * q, _baseHeightGTFC * q);
        }

        static void Resize(RenderTexture rt, int width, int height)
        {
            if (rt.width == width && rt.height == height) return;
            bool wasActive = RenderTexture.active == rt;
            rt.Release();
            rt.width = width;
            rt.height = height;
            rt.Create();
            if (wasActive) RenderTexture.active = rt;
        }

        static void SetLayersRecursive(Transform t)
        {
            if (t.gameObject.layer == LayerMask.NameToLayer("NoMirror")) t.gameObject.layer = 0;
            foreach (Transform child in t) SetLayersRecursive(child);
        }

        static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Miror.Section"));

            // Si le miroir n'est pas encore chargé, on prévient mais on affiche quand même
            // les réglages : la qualité choisie sera appliquée dès que le miroir sera prêt.
            if (!_ready)
            {
                ui.BeginCard();
                ui.Label(I18n.T("Miror.NotLoaded"), Theme.Yellow, 9);
                ui.EndCard();
                ui.Space(6);
            }

            if (!Enabled)
            {
                ui.BeginCard();
                ui.Label(I18n.T("Miror.DisabledHint"), Theme.Dim, 9);
                ui.EndCard();
                ui.Space(6);
            }

            ui.BeginCard();
            int newQuality = ui.IntSlider(I18n.T("Miror.Quality"), _quality, MinQuality, MaxQuality, 100f);
            if (newQuality != _quality)
            {
                _quality = newQuality;
                PlayerPrefs.SetInt("MUM_MirorQuality", _quality);
                PlayerPrefs.Save();
                // Si le mod est désactivé, la préférence est mémorisée mais pas appliquée (reste à x1)
                if (Enabled) ApplyQuality(_quality);
            }

            // Avertissement performance dès que la qualité dépasse le seuil (x3)
            if (_quality > PerfWarningThreshold)
            {
                ui.Space(6); ui.Separator();
                ui.Space(6);
                ui.Label(I18n.T("Miror.PerfWarning"), Theme.Yellow, 9, true);
            }
            ui.EndCard();
        }
    }
}