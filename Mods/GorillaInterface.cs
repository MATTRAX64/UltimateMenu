using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace UltimateMenu
{
    [DefaultExecutionOrder(10001)]
    public class GorillaInterface : MonoBehaviour
    {
        public static GorillaInterface Instance { get; private set; }

        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("GorillaInterface.Title");

        const string PREF_SCALE = "MUM_GI_Scale";
        const string PREF_DISTANCE = "MUM_GI_Distance";
        const string PREF_ANIMSPEED = "MUM_GI_AnimSpeed";
        const string PREF_MAXDIST = "MUM_GI_MaxDist";
        const string PREF_STICKSENS = "MUM_GI_StickSens";

        float _userScale = 1f;
        float _spawnDistance = 0.75f;
        float _animSpeed = 3.5f;
        readonly float _followSmooth = 8f;
        float _maxDistance = 1.6f;
        float _stickSens = 0.6f;
        readonly float _heightOffset = -0.05f;

        const float MinUserScale = 0.5f, MaxUserScale = 2f;
        const float MinDistance = 0.4f, MaxDistance = 1.5f;
        const float MinAnimSpeed = 1f, MaxAnimSpeed = 8f;
        const float MinMaxDist = 1f, MaxMaxDist = 4f;
        const float MinStickSens = 0.3f, MaxStickSens = 0.9f;

        void LoadSettings()
        {
            _userScale = PlayerPrefs.GetFloat(PREF_SCALE, 1f);
            _spawnDistance = PlayerPrefs.GetFloat(PREF_DISTANCE, 0.75f);
            _animSpeed = PlayerPrefs.GetFloat(PREF_ANIMSPEED, 3.5f);
            _maxDistance = PlayerPrefs.GetFloat(PREF_MAXDIST, 1.6f);
            _stickSens = PlayerPrefs.GetFloat(PREF_STICKSENS, 0.6f);
            _shakeThreshold = PlayerPrefs.GetFloat(PREF_SHAKE_THRESHOLD, 0.5f);
            _shakeWindow = PlayerPrefs.GetFloat(PREF_SHAKE_WINDOW, 0.35f);
        }
        void SaveSettings()
        {
            PlayerPrefs.SetFloat(PREF_SCALE, _userScale);
            PlayerPrefs.SetFloat(PREF_DISTANCE, _spawnDistance);
            PlayerPrefs.SetFloat(PREF_ANIMSPEED, _animSpeed);
            PlayerPrefs.SetFloat(PREF_MAXDIST, _maxDistance);
            PlayerPrefs.SetFloat(PREF_STICKSENS, _stickSens);
            PlayerPrefs.SetFloat(PREF_SHAKE_THRESHOLD, _shakeThreshold);
            PlayerPrefs.SetFloat(PREF_SHAKE_WINDOW, _shakeWindow);
            PlayerPrefs.Save();
        }
        enum HPage { Home = 0, ModList = 1, ModPage = 2 }

        bool _open;
        float _openT;
        HPage _page = HPage.ModList;
        int _selectedIndex = 0;
        bool _focusOnTabs = false;
        int _tabSelected = 1;

        Transform _playerHead;
        Vector3 _targetPos;
        Quaternion _targetRot;
        bool _anchored;
        const float PanelW = 640f;
        const float PanelH = 420f;
        const float TitleH = 56f;
        const float TabsH = 40f;
        const float FooterH = 36f;
        const float Pad = 16f;

        GameObject _root;
        Canvas _canvas;
        RectTransform _panel;
        Image _panelBg;
        RectTransform _tabsBar;
        readonly GameObject[] _tabGOs = new GameObject[3];
        readonly Image[] _tabBgs = new Image[3];
        readonly TextMeshProUGUI[] _tabLabels = new TextMeshProUGUI[3];
        GameObject _homePageGO;
        RectTransform _homeViewport;
        RectTransform _homeContent;
        readonly List<VRow> _homeRows = new List<VRow>();
        readonly List<GameObject> _homeRowGOs = new List<GameObject>();
        int _homeSelected = 0;
        float _homeScrollY = 0f;
        GameObject _listPageGO;
        RectTransform _listViewport;
        RectTransform _listContent;
        readonly List<GameObject> _listRows = new List<GameObject>();
        readonly List<float> _listRowY = new List<float>();
        readonly List<float> _listRowH = new List<float>();
        float _listScrollY = 0f;
        GameObject _modPageGO;
        TextMeshProUGUI _modPageTitle;
        RectTransform _modViewport;
        RectTransform _modContent;
        readonly List<VRow> _modRows = new List<VRow>();
        readonly List<GameObject> _modRowGOs = new List<GameObject>();
        int _modSelected = 0;
        float _modScrollY = 0f;
        int _modPageBuiltForIndex = -1;
        float _stickVCooldown, _stickHCooldown, _stickHRepeatCooldown;
        const float StickRepeatDelay = 0.28f;
        const float StickHRepeatDelay = 0.16f;
        bool _lastTriggerL, _lastTriggerR;

        // ---- Geste stick bas->haut (ouvrir) / haut->bas (fermer) pour toggle le menu ----
        const string PREF_SHAKE_THRESHOLD = "MUM_GI_ShakeThreshold";
        const string PREF_SHAKE_WINDOW = "MUM_GI_ShakeWindow";
        float _shakeThreshold = 0.5f;   // à partir de quelle valeur de Y on considère "bas" / "haut"
        float _shakeWindow = 0.35f;     // temps max (secondes) entre les deux mouvements opposés
        const float MinShakeThreshold = 0.2f, MaxShakeThreshold = 0.9f;
        const float MinShakeWindow = 0.1f, MaxShakeWindow = 1f;

        enum ShakeState { Neutral, WentDown, WentUp }
        ShakeState _shakeState = ShakeState.Neutral;
        float _shakeStateTime = 0f;

        // ---- Valeurs dédiées, purement pour affichage debug du stick (visible dans DrawSettingsPage) ----
        Vector2 _debugStickL;
        Vector2 _debugStickR;


        void Awake()
        {
            Instance = this;
            LoadSettings();
        }

        void Start()
        {
            MenuUI.Instance?.RegisterPage("GorillaInterface.Title", DrawSettingsPage, MenuCategory.Technique);
            BuildUI();
            SetOpenImmediate(false);
            GorillaTagger.OnPlayerSpawned(FindHead);
        }

        void FindHead()
        {
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

        // ================================================================
        // BUILD UI
        // ================================================================
        void BuildUI()
        {
            _root = new GameObject("MUM_GorillaInterface_Root");
            DontDestroyOnLoad(_root);

            var canvasGO = new GameObject("Canvas");
            canvasGO.transform.SetParent(_root.transform, false);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 600;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 20f;
            canvasGO.AddComponent<GraphicRaycaster>();

            var canvasRect = canvasGO.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(PanelW, PanelH);
            canvasRect.localScale = Vector3.one * 0.001f; // 1 unité canvas ~ 1mm world

            // ---- Panneau de fond : occupe TOUT le canvas, rien ne peut dépasser de lui ----
            var panelGO = MakeStretchImage(canvasRect, "Panel", Theme.BG, Vector2.zero, Vector2.zero);
            _panel = panelGO.GetComponent<RectTransform>();
            _panelBg = panelGO.GetComponent<Image>();
            // Le panel a un Mask : tout enfant qui dépasserait est tronqué proprement plutôt
            // que de déborder visuellement à l'écran.
            panelGO.AddComponent<RectMask2D>();

            // ---- Barre de titre (stretch horizontal, hauteur fixe, collée en haut) ----
            var titleBg = MakeTopBar(_panel, "TitleBar", Theme.BG2, TitleH, 0f);
            MakeText(titleBg, "Title", Log.PluginName ?? "UltimateMenu", 22, Theme.Text, TextAlignmentOptions.MidlineLeft,
                new Vector2(Pad, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-Pad, 0f))
                .overflowMode = TextOverflowModes.Ellipsis;

            // ---- Barre d'onglets fixe, juste sous le titre ----
            BuildTabsBar(_panel);

            // ---- Zone de contenu (entre onglets et footer), même contrainte stretch ----
            var contentArea = MakeStretchPanel(_panel, "Content", new Vector2(0f, FooterH), new Vector2(0f, TitleH + TabsH));

            BuildHomePage(contentArea);
            BuildListPage(contentArea);
            BuildModPage(contentArea);

            // ---- Barre du bas (stretch horizontal, hauteur fixe, collée en bas) ----
            var footBg = MakeBottomBar(_panel, "Footer", Theme.BG2, FooterH);
            MakeText(footBg, "Hint", "Onglets: </> puis Trigger  |  Contenu: Haut/Bas selection, Trigger valider",
                12, Theme.Dim, TextAlignmentOptions.Midline, new Vector2(Pad, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-Pad, 0f))
                .overflowMode = TextOverflowModes.Ellipsis;

            ShowPage(HPage.ModList);
            RefreshTabsVisual();
        }

        // ---------------------------------------------------------------
        // BARRE D'ONGLETS (toujours visible en haut, 3 boutons fixes)
        // ---------------------------------------------------------------
        void BuildTabsBar(RectTransform parent)
        {
            var barGO = new GameObject("TabsBar", typeof(RectTransform), typeof(Image));
            _tabsBar = barGO.GetComponent<RectTransform>();
            _tabsBar.SetParent(parent, false);
            _tabsBar.anchorMin = new Vector2(0f, 1f);
            _tabsBar.anchorMax = new Vector2(1f, 1f);
            _tabsBar.pivot = new Vector2(0.5f, 1f);
            _tabsBar.anchoredPosition = new Vector2(0f, -TitleH);
            _tabsBar.sizeDelta = new Vector2(0f, TabsH);
            var barImg = barGO.GetComponent<Image>();
            barImg.color = Theme.BG2;
            barImg.raycastTarget = false;

            string[] names = { "Accueil", "Mods", "Mod" };
            for (int i = 0; i < 3; i++)
            {
                var tabGO = new GameObject("Tab_" + i, typeof(RectTransform), typeof(Image));
                var tabRt = tabGO.GetComponent<RectTransform>();
                tabRt.SetParent(_tabsBar, false);
                tabRt.anchorMin = new Vector2(i / 3f, 0f);
                tabRt.anchorMax = new Vector2((i + 1) / 3f, 1f);
                tabRt.offsetMin = new Vector2(3f, 3f);
                tabRt.offsetMax = new Vector2(-3f, -3f);
                var tabImg = tabGO.GetComponent<Image>();
                tabImg.color = Theme.BG3;
                tabImg.raycastTarget = false;

                var label = MakeText(tabRt, "Label", names[i], 14, Theme.Text, TextAlignmentOptions.Midline,
                    Vector2.zero, Vector2.one, new Vector2(4f, 0f), new Vector2(-4f, 0f));
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;

                _tabGOs[i] = tabGO;
                _tabBgs[i] = tabImg;
                _tabLabels[i] = label;
            }
        }

        /// <summary>Met à jour l'apparence des 3 onglets (couleur de fond/texte selon focus + sélection + page active).</summary>
        void RefreshTabsVisual()
        {
            if (_tabBgs[0] == null) return;

            var pages = GetSortedPages();
            string modName = (_selectedIndex >= 0 && _selectedIndex < pages.Count) ? I18n.T(pages[_selectedIndex].Title) : "-";
            _tabLabels[2].text = modName.Length > 10 ? modName.Substring(0, 9) + "…" : modName;

            for (int i = 0; i < 3; i++)
            {
                bool isActivePage = (int)_page == i;
                bool isFocused = _focusOnTabs && _tabSelected == i;

                Color bg = isFocused ? Theme.Accent : (isActivePage ? Theme.BG : Theme.BG3);
                Color txt = isFocused ? Color.white : (isActivePage ? Theme.Accent : Theme.Dim);

                _tabBgs[i].color = bg;
                _tabLabels[i].color = txt;
                _tabLabels[i].fontStyle = (isActivePage || isFocused) ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        // ---------------------------------------------------------------
        // PAGE ACCUEIL (Home) — reconstruit tous les réglages généraux du PC
        // ---------------------------------------------------------------
        void BuildHomePage(RectTransform parent)
        {
            _homePageGO = new GameObject("HomePage", typeof(RectTransform));
            var rt = FillParent(_homePageGO.GetComponent<RectTransform>(), parent);

            MakeText(rt, "HomeTitle", "ACCUEIL", 18, Theme.Accent, TextAlignmentOptions.TopLeft,
                new Vector2(Pad, 1f), new Vector2(1f, 1f), new Vector2(0f, -Pad), new Vector2(-Pad, -Pad - 26f));

            // Viewport masqué : le contenu défile dedans sans jamais déborder du panneau.
            var viewportGO = new GameObject("HomeViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            _homeViewport = viewportGO.GetComponent<RectTransform>();
            _homeViewport.SetParent(rt, false);
            _homeViewport.anchorMin = new Vector2(0f, 0f);
            _homeViewport.anchorMax = new Vector2(1f, 1f);
            _homeViewport.offsetMin = new Vector2(Pad, 0f);
            _homeViewport.offsetMax = new Vector2(-Pad, -Pad - 34f);
            var viewportImg = viewportGO.GetComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0.001f);
            viewportImg.raycastTarget = false;

            var contentGO = new GameObject("HomeContent", typeof(RectTransform));
            _homeContent = contentGO.GetComponent<RectTransform>();
            _homeContent.SetParent(_homeViewport, false);
            _homeContent.anchorMin = new Vector2(0f, 1f);
            _homeContent.anchorMax = new Vector2(1f, 1f);
            _homeContent.pivot = new Vector2(0.5f, 1f);
            _homeContent.anchoredPosition = Vector2.zero;
            _homeContent.sizeDelta = new Vector2(0f, 10f);

            RebuildHomeRows();
        }

        /// <summary>
        /// Construit la liste des VRow de l'accueil : les mêmes réglages que la page
        /// Accueil du menu PC (taille UI, thème, langue, gras, sons), adaptés en
        /// contrôles pilotables au stick + trigger. Le rebind de touche clavier n'a
        /// volontairement pas sa place ici (aucun clavier en VR) : il reste PC-only.
        /// </summary>
        void RebuildHomeRows()
        {
            _homeRows.Clear();

            _homeRows.Add(VRow.SliderRow("Taille menu (PC)",
                () => PlayerPrefs.GetFloat("MUM_UIScale", 1f).ToString("0.0") + "x",
                dir => {
                    float v = Mathf.Clamp(PlayerPrefs.GetFloat("MUM_UIScale", 1f) + dir * 0.1f, 0.5f, 2f);
                    PlayerPrefs.SetFloat("MUM_UIScale", v); PlayerPrefs.Save();
                }));

            _homeRows.Add(VRow.SliderRow("Vitesse anim. menu VR",
                () => _animSpeed.ToString("0.0"),
                dir => { _animSpeed = Mathf.Clamp(_animSpeed + dir * 0.5f, MinAnimSpeed, MaxAnimSpeed); SaveSettings(); }));

            _homeRows.Add(VRow.SliderRow("Taille menu VR",
                () => _userScale.ToString("0.00") + "x",
                dir => { _userScale = Mathf.Clamp(_userScale + dir * 0.05f, MinUserScale, MaxUserScale); SaveSettings(); }));

            _homeRows.Add(VRow.SliderRow("Distance menu VR",
                () => _spawnDistance.ToString("0.00") + "m",
                dir => { _spawnDistance = Mathf.Clamp(_spawnDistance + dir * 0.05f, MinDistance, MaxDistance); SaveSettings(); }));

            _homeRows.Add(VRow.SliderRow("Distance max VR",
                () => _maxDistance.ToString("0.00") + "m",
                dir => { _maxDistance = Mathf.Clamp(_maxDistance + dir * 0.1f, MinMaxDist, MaxMaxDist); SaveSettings(); }));

            _homeRows.Add(VRow.SliderRow("Sensibilite stick",
                () => _stickSens.ToString("0.00"),
                dir => { _stickSens = Mathf.Clamp(_stickSens + dir * 0.05f, MinStickSens, MaxStickSens); SaveSettings(); }));

            _homeRows.Add(VRow.ButtonRow("Theme",
                () => ThemeShortName(Theme.Current),
                () => {
                    var values = (ThemePreset[])Enum.GetValues(typeof(ThemePreset));
                    int idx = Array.IndexOf(values, Theme.Current);
                    idx = (idx + 1) % values.Length;
                    Theme.Apply(values[idx]);
                    RebuildTheme();
                }));

            _homeRows.Add(VRow.ButtonRow("Langue",
                () => I18n.Current.ToString(),
                () => {
                    var values = (Lang[])Enum.GetValues(typeof(Lang));
                    int idx = Array.IndexOf(values, I18n.Current);
                    idx = (idx + 1) % values.Length;
                    I18n.Current = values[idx];
                }));

            _homeRows.Add(VRow.ToggleRow("Texte en gras",
                () => Theme.BoldText,
                v => Theme.BoldText = v));

            _homeRows.Add(VRow.ToggleRow("Sons du menu",
                () => SoundFX.Enabled,
                v => SoundFX.Enabled = v));

            _homeRows.Add(VRow.SliderRow("Volume sons",
                () => SoundFX.Volume.ToString("0.00"),
                dir => SoundFX.Volume = Mathf.Clamp01(SoundFX.Volume + dir * 0.05f)));

            if (_homeSelected >= _homeRows.Count) _homeSelected = Mathf.Max(0, _homeRows.Count - 1);

            RenderRows(_homeContent, _homeRows, _homeRowGOs, _homeSelected, !_focusOnTabs && _page == HPage.Home, out float totalH);
            _homeContent.sizeDelta = new Vector2(0f, Mathf.Max(totalH, 10f));
            AutoScrollToSelection(_homeContent, _homeViewport, _homeRows.Count, _homeSelected, ref _homeScrollY);
        }

        static string ThemeShortName(ThemePreset p)
        {
            switch (p)
            {
                case ThemePreset.SombreVert: return "Sombre Vert";
                case ThemePreset.Clair: return "Clair";
                case ThemePreset.SombreViolet: return "Sombre Violet";
                case ThemePreset.SombreBleu: return "Sombre Bleu";
                default: return "Custom";
            }
        }

        // ---------------------------------------------------------------
        // PAGE LISTE DES MODS (ModList) — auto-générée depuis MenuUI.GetAllPagesSorted()
        // ---------------------------------------------------------------
        void BuildListPage(RectTransform parent)
        {
            _listPageGO = new GameObject("ListPage", typeof(RectTransform));
            var rt = FillParent(_listPageGO.GetComponent<RectTransform>(), parent);

            MakeText(rt, "ListTitle", "LISTE DES MODS", 18, Theme.Accent, TextAlignmentOptions.TopLeft,
                new Vector2(Pad, 1f), new Vector2(1f, 1f), new Vector2(0f, -Pad), new Vector2(-Pad, -Pad - 26f));

            // Viewport masqué : tout ce qui dépasse à l'intérieur est proprement coupé (clip),
            // au lieu de déborder hors du panneau comme avant.
            var viewportGO = new GameObject("ListViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            _listViewport = viewportGO.GetComponent<RectTransform>();
            _listViewport.SetParent(rt, false);
            _listViewport.anchorMin = new Vector2(0f, 0f);
            _listViewport.anchorMax = new Vector2(1f, 1f);
            _listViewport.offsetMin = new Vector2(Pad, 0f);
            _listViewport.offsetMax = new Vector2(-Pad, -Pad - 34f);
            var viewportImg = viewportGO.GetComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0.001f); // quasi invisible, juste pour activer le raycast/mask proprement
            viewportImg.raycastTarget = false;

            var contentGO = new GameObject("ListContent", typeof(RectTransform));
            _listContent = contentGO.GetComponent<RectTransform>();
            _listContent.SetParent(_listViewport, false);
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            _listContent.anchoredPosition = Vector2.zero;
            _listContent.sizeDelta = new Vector2(0f, 10f);
        }

        // ---------------------------------------------------------------
        // PAGE DU MOD SÉLECTIONNÉ (ModPage) — reconstruite avec des VRow
        // ---------------------------------------------------------------
        void BuildModPage(RectTransform parent)
        {
            _modPageGO = new GameObject("ModPage", typeof(RectTransform));
            var rt = FillParent(_modPageGO.GetComponent<RectTransform>(), parent);

            _modPageTitle = MakeText(rt, "ModTitle", "Nom du mod", 20, Theme.Accent, TextAlignmentOptions.TopLeft,
                new Vector2(Pad, 1f), new Vector2(1f, 1f), new Vector2(0f, -Pad), new Vector2(-Pad, -Pad - 28f));
            _modPageTitle.overflowMode = TextOverflowModes.Ellipsis;
            _modPageTitle.enableWordWrapping = false;

            var viewportGO = new GameObject("ModViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            _modViewport = viewportGO.GetComponent<RectTransform>();
            _modViewport.SetParent(rt, false);
            _modViewport.anchorMin = new Vector2(0f, 0f);
            _modViewport.anchorMax = new Vector2(1f, 1f);
            _modViewport.offsetMin = new Vector2(Pad, 0f);
            _modViewport.offsetMax = new Vector2(-Pad, -Pad - 34f);
            var viewportImg = viewportGO.GetComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0.001f);
            viewportImg.raycastTarget = false;

            var contentGO = new GameObject("ModContent", typeof(RectTransform));
            _modContent = contentGO.GetComponent<RectTransform>();
            _modContent.SetParent(_modViewport, false);
            _modContent.anchorMin = new Vector2(0f, 1f);
            _modContent.anchorMax = new Vector2(1f, 1f);
            _modContent.pivot = new Vector2(0.5f, 1f);
            _modContent.anchoredPosition = Vector2.zero;
            _modContent.sizeDelta = new Vector2(0f, 10f);
        }

        /// <summary>
        /// Construit les VRow de la page d'un mod sélectionné : le mod ON/OFF en premier
        /// (auto-assigné pour TOUS les mods enregistrés dans MenuUI, sans configuration
        /// manuelle), puis d'éventuels réglages spécifiques enregistrés via RegisterVrRows.
        /// Aucun accès au rebind clavier n'est exposé ici (PC-only).
        /// </summary>
        void RebuildModPageRows(Page pg)
        {
            _modRows.Clear();

            _modRows.Add(VRow.ToggleRow("Mod actif",
                () => pg.Enabled,
                v => { pg.Enabled = v; SoundFX.PlaySound(v ? "Aigue" : "Grave"); }));

            // Réglages spécifiques enregistrés par mod (voir RegisterVrRows plus bas) —
            // c'est ce mécanisme qui permet l'auto-assignation : tout mod qui s'enregistre
            // avec RegisterVrRows apparaît automatiquement ici sans toucher à ce fichier.
            if (_customModRows.TryGetValue(pg.Title, out var extra))
            {
                foreach (var row in extra(pg)) _modRows.Add(row);
            }
            else
            {
                _modRows.Add(VRow.InfoRow("Reglages detailles", "voir menu PC"));
            }

            if (_modSelected >= _modRows.Count) _modSelected = Mathf.Max(0, _modRows.Count - 1);

            RenderRows(_modContent, _modRows, _modRowGOs, _modSelected, !_focusOnTabs && _page == HPage.ModPage, out float totalH);
            _modContent.sizeDelta = new Vector2(0f, Mathf.Max(totalH, 10f));
            AutoScrollToSelection(_modContent, _modViewport, _modRows.Count, _modSelected, ref _modScrollY);
        }

        /// <summary>
        /// Registre extensible : un mod peut fournir ses propres VRow pour la page VR
        /// en s'enregistrant ici par titre (le même Title utilisé pour RegisterPage côté MenuUI).
        /// Dès qu'un mod appelle cette méthode (typiquement dans son propre Start()), sa page VR
        /// est automatiquement enrichie — aucune modification de GorillaInterface nécessaire.
        /// Exemple :
        ///   GorillaInterface.RegisterVrRows("MonMod.Title", pg => new[] {
        ///       VRow.SliderRow("Vitesse", () => MonMod.Speed.ToString("0.0"), d => MonMod.Speed += d * 0.5f)
        ///   });
        /// </summary>
        static readonly Dictionary<string, Func<Page, IEnumerable<VRow>>> _customModRows
            = new Dictionary<string, Func<Page, IEnumerable<VRow>>>();

        public static void RegisterVrRows(string pageTitle, Func<Page, IEnumerable<VRow>> factory)
            => _customModRows[pageTitle] = factory;

        // ---- Helpers de layout : tout est en stretch anchors + offsets, jamais en position/taille absolue mêlée à un autre pivot ----

        RectTransform FillParent(RectTransform rt, RectTransform parent)
        {
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Panneau qui occupe le parent moins des marges (bottom, top) en pixels canvas.</summary>
        RectTransform MakeStretchPanel(RectTransform parent, string name, Vector2 marginBottom, Vector2 marginTop)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0f, marginBottom.y);
            rt.offsetMax = new Vector2(0f, -marginTop.y);
            return rt;
        }

        GameObject MakeStretchImage(RectTransform parent, string name, Color color, Vector2 marginMin, Vector2 marginMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = marginMin;
            rt.offsetMax = -marginMax;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        RectTransform MakeTopBar(RectTransform parent, string name, Color color, float height, float fromTop)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -fromTop);
            rt.sizeDelta = new Vector2(0f, height);
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = false;
            return rt;
        }

        RectTransform MakeBottomBar(RectTransform parent, string name, Color color, float height)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, height);
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = false;
            return rt;
        }

        GameObject MakeFixedImage(RectTransform parent, string name, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchor; rt.anchorMax = anchor;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        /// <summary>
        /// Crée un texte contraint par stretch anchors (offsetMin/offsetMax), jamais par une
        /// taille absolue : il ne peut donc jamais dépasser de son parent quelle que soit la
        /// longueur du texte (le word-wrap / ellipsis gère l'intérieur).
        /// </summary>
        TextMeshProUGUI MakeText(RectTransform parent, string name, string text, float size, Color color, TextAlignmentOptions align,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Truncate;
            return tmp;
        }

        // ================================================================
        // SYSTEME DE LIGNES VIRTUELLES (VRow) — indépendant de MenuUI
        // ================================================================

        /// <summary>
        /// Type de contrôle représenté par une ligne virtuelle dans une page VR
        /// (Accueil ou Page de mod). Ne dépend d'aucune classe de MenuUI. C'est ce système,
        /// combiné à RegisterVrRows, qui permet l'auto-génération des contrôles par mod.
        /// </summary>
        public class VRow
        {
            public enum Kind { Slider, Toggle, Button, Info }
            public Kind Type;
            public string Label;
            public Func<string> GetValueText;         // affichage texte de la valeur actuelle (slider/bouton/info)
            public Func<bool> GetToggleValue;          // pour Toggle
            public Action<bool> SetToggleValue;        // pour Toggle
            public Action<int> OnSliderStep;           // pour Slider : dir = -1 ou +1
            public Action OnActivate;                  // pour Button/Toggle (trigger)

            public static VRow SliderRow(string label, Func<string> getValueText, Action<int> onStep) => new VRow
            { Type = Kind.Slider, Label = label, GetValueText = getValueText, OnSliderStep = onStep };

            public static VRow ToggleRow(string label, Func<bool> get, Action<bool> set) => new VRow
            {
                Type = Kind.Toggle,
                Label = label,
                GetToggleValue = get,
                SetToggleValue = set,
                OnActivate = () => set(!get())
            };

            public static VRow ButtonRow(string label, Func<string> getValueText, Action onActivate) => new VRow
            { Type = Kind.Button, Label = label, GetValueText = getValueText, OnActivate = onActivate };

            public static VRow InfoRow(string label, string valueText) => new VRow
            { Type = Kind.Info, Label = label, GetValueText = () => valueText };
        }

        const float RowH = 32f, RowGap = 6f;

        /// <summary>
        /// Dessine une liste de VRow dans un content donné, en recréant les GameObjects.
        /// La ligne sélectionnée n'est mise en surbrillance que si focusInContent est vrai
        /// (c-à-d que le focus n'est pas actuellement sur la barre d'onglets).
        /// </summary>
        void RenderRows(RectTransform content, List<VRow> rows, List<GameObject> existingGOs, int selectedIndex, bool focusInContent, out float totalH)
        {
            foreach (var go in existingGOs) if (go != null) Destroy(go);
            existingGOs.Clear();

            float y = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                bool selected = focusInContent && i == selectedIndex;
                Color rowBg = selected ? Theme.BG3 : Theme.BG2;

                var rowGO = new GameObject("VRow_" + i, typeof(RectTransform), typeof(Image));
                var rowRt = rowGO.GetComponent<RectTransform>();
                rowRt.SetParent(content, false);
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.anchoredPosition = new Vector2(0f, -y);
                rowRt.sizeDelta = new Vector2(0f, RowH);
                var rowImg = rowGO.GetComponent<Image>();
                rowImg.color = rowBg;
                rowImg.raycastTarget = false;
                existingGOs.Add(rowGO);

                var labelCol = selected ? Theme.Accent : Theme.Text;
                var label = MakeText(rowRt, "Label", row.Label, 14, labelCol, TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 0f), new Vector2(0.55f, 1f), new Vector2(12f, 0f), new Vector2(0f, 0f));
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;

                switch (row.Type)
                {
                    case VRow.Kind.Slider:
                        {
                            var valText = MakeText(rowRt, "Value", row.GetValueText(), 14, Theme.Accent, TextAlignmentOptions.MidlineRight,
                                new Vector2(0.55f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-34f, 0f));
                            valText.overflowMode = TextOverflowModes.Ellipsis;
                            valText.enableWordWrapping = false;
                            MakeText(rowRt, "Arrows", "< >", 12, Theme.Dim, TextAlignmentOptions.MidlineRight,
                                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-30f, 0f), new Vector2(-8f, 0f));
                            break;
                        }
                    case VRow.Kind.Toggle:
                        {
                            bool val = row.GetToggleValue();
                            MakeFixedImage(rowRt, "Dot", val ? Theme.Green : Theme.Red, new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(14f, 14f));
                            var valText = MakeText(rowRt, "Value", val ? "ON" : "OFF", 12, val ? Theme.Green : Theme.Red, TextAlignmentOptions.MidlineRight,
                                new Vector2(0.55f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-46f, 0f));
                            valText.overflowMode = TextOverflowModes.Ellipsis;
                            break;
                        }
                    case VRow.Kind.Button:
                        {
                            string vt = row.GetValueText != null ? row.GetValueText() : "";
                            var valText = MakeText(rowRt, "Value", vt, 13, Theme.Accent, TextAlignmentOptions.MidlineRight,
                                new Vector2(0.55f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-12f, 0f));
                            valText.overflowMode = TextOverflowModes.Ellipsis;
                            valText.enableWordWrapping = false;
                            break;
                        }
                    case VRow.Kind.Info:
                        {
                            var valText = MakeText(rowRt, "Value", row.GetValueText(), 12, Theme.Dim, TextAlignmentOptions.MidlineRight,
                                new Vector2(0.55f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-12f, 0f));
                            valText.enableWordWrapping = true;
                            break;
                        }
                }

                y += RowH + RowGap;
            }

            totalH = Mathf.Max(0f, y - RowGap);
        }

        /// <summary>
        /// Décale le content verticalement pour que la ligne sélectionnée reste toujours
        /// visible dans le viewport : si elle dépasse en bas on décale vers le bas, si elle
        /// dépasse en haut (au-dessus du viewport) on décale vers le haut. Ne bouge sinon pas.
        /// </summary>
        void AutoScrollToSelection(RectTransform content, RectTransform viewport, int rowCount, int selectedIndex, ref float scrollY)
        {
            if (rowCount == 0) { scrollY = 0f; content.anchoredPosition = Vector2.zero; return; }
            selectedIndex = Mathf.Clamp(selectedIndex, 0, rowCount - 1);

            float rowTop = selectedIndex * (RowH + RowGap);
            float rowBottom = rowTop + RowH;
            float viewH = viewport.rect.height;

            float visibleTop = rowTop - scrollY;
            float visibleBottom = rowBottom - scrollY;

            if (visibleTop < 0f) scrollY = rowTop;
            else if (visibleBottom > viewH) scrollY = rowBottom - viewH;

            float maxScroll = Mathf.Max(0f, content.sizeDelta.y - viewH);
            scrollY = Mathf.Clamp(scrollY, 0f, maxScroll);

            content.anchoredPosition = new Vector2(0f, scrollY);
        }

        // ================================================================
        // UPDATE
        // ================================================================
        void Update()
        {
            if (_root == null) return;

            bool modEnabled = Enabled;
            if (!modEnabled && _open) _open = false;

            ReadInputsAndNavigate(modEnabled);

            // ---- Valeurs dédiées, purement pour affichage debug (ne servent à rien d'autre) ----
            var pollerDebug = ControllerInputPoller.instance;
            if (pollerDebug != null)
            {
                _debugStickL = pollerDebug.leftControllerPrimary2DAxis;
                _debugStickR = pollerDebug.rightControllerPrimary2DAxis;
            }

            float target = _open ? 1f : 0f;
            _openT = Mathf.MoveTowards(_openT, target, Time.deltaTime * _animSpeed);
            bool visible = _openT > 0.001f;
            _root.SetActive(visible);

            if (visible)
            {
                UpdateFollow();
                float ease = 1f - Mathf.Pow(1f - Mathf.Clamp01(_openT), 3f);
                _root.transform.localScale = Vector3.one * (ease * _userScale);

                if (_playerHead != null)
                {
                    float dist = Vector3.Distance(_playerHead.position, _root.transform.position);
                    if (dist > _maxDistance) _open = false;
                }

                RefreshDynamicTexts();
            }

            if (_stickVCooldown > 0f) _stickVCooldown -= Time.deltaTime;
            if (_stickHCooldown > 0f) _stickHCooldown -= Time.deltaTime;
            if (_stickHRepeatCooldown > 0f) _stickHRepeatCooldown -= Time.deltaTime;
        }

        void SetOpenImmediate(bool open)
        {
            _open = open;
            _openT = open ? 1f : 0f;
            if (_root != null) _root.SetActive(open);
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
                _targetPos = Vector3.Lerp(_targetPos, desiredPos, Time.deltaTime * _followSmooth);
                _targetRot = Quaternion.Slerp(_targetRot, desiredRot, Time.deltaTime * _followSmooth);
            }

            _root.transform.position = _targetPos;
            _root.transform.rotation = _targetRot;
        }

        // ================================================================
        // INPUT
        // ================================================================
        void ReadInputsAndNavigate(bool modEnabled)
        {
            Vector2 stick = ReadAnyStick();
            bool triggerPressed = ReadAnyTriggerDown();

            if (!modEnabled) { _open = false; return; }

            // ---- Ouverture / fermeture par geste stick bas->haut / haut->bas ----
            UpdateShakeToggle(stick);

            if (!_open) return;

            bool vEdge = _stickVCooldown <= 0f && Mathf.Abs(stick.y) >= _stickSens;
            bool hEdge = _stickHCooldown <= 0f && Mathf.Abs(stick.x) >= _stickSens;
            bool up = stick.y > 0f;
            bool right = stick.x > 0f;

            // ---- Focus sur la barre d'onglets ----
            if (_focusOnTabs)
            {
                if (hEdge)
                {
                    _stickHCooldown = StickRepeatDelay;
                    _tabSelected = Mathf.Clamp(_tabSelected + (right ? 1 : -1), 0, 2);
                    SoundFX.PlaySound("Moyen");
                    RefreshTabsVisual();
                }

                if (vEdge && !up)
                {
                    // Stick vers le bas depuis les onglets -> redescend dans le contenu de la page active.
                    _stickVCooldown = StickRepeatDelay;
                    _focusOnTabs = false;
                    SoundFX.PlaySound("Moyen");
                    RefreshTabsVisual();
                    RefreshContentFocus();
                }

                if (triggerPressed)
                {
                    HPage target = (HPage)_tabSelected;
                    if (target == HPage.ModPage && GetSortedPages().Count == 0)
                    {
                        // Pas de mod sélectionnable : on reste sur la liste.
                        target = HPage.ModList;
                        _tabSelected = (int)HPage.ModList;
                    }
                    _page = target;
                    ShowPage(_page);
                    SoundFX.PlaySound("Moyen");
                }

                return;
            }

            // ---- Focus dans le contenu de la page active ----
            if (_page == HPage.ModList)
            {
                if (vEdge)
                {
                    _stickVCooldown = StickRepeatDelay;
                    int count = GetSortedPages().Count;
                    if (up && _selectedIndex == 0)
                    {
                        // Tout en haut de la liste + stick vers le haut -> remonte vers les onglets.
                        _focusOnTabs = true;
                        _tabSelected = (int)HPage.ModList;
                        SoundFX.PlaySound("Moyen");
                        RefreshTabsVisual();
                        RebuildListUI();
                    }
                    else if (count > 0)
                    {
                        _selectedIndex = up ? Mathf.Max(0, _selectedIndex - 1) : Mathf.Min(count - 1, _selectedIndex + 1);
                        SoundFX.PlaySound("Moyen");
                        RefreshTabsVisual();
                    }
                }

                if (triggerPressed)
                {
                    var pages = GetSortedPages();
                    if (_selectedIndex >= 0 && _selectedIndex < pages.Count)
                    {
                        var pg = pages[_selectedIndex];
                        pg.Enabled = !pg.Enabled;
                        SoundFX.PlaySound(pg.Enabled ? "Aigue" : "Grave");
                    }
                }
            }
            else if (_page == HPage.Home)
            {
                HandleContentPage(vEdge, up, hEdge, stick.x, triggerPressed, _homeRows, ref _homeSelected, RebuildHomeRows);
            }
            else if (_page == HPage.ModPage)
            {
                var pgs = GetSortedPages();
                Page currentPg = (_selectedIndex >= 0 && _selectedIndex < pgs.Count) ? pgs[_selectedIndex] : null;
                HandleContentPage(vEdge, up, hEdge, stick.x, triggerPressed, _modRows, ref _modSelected,
                    () => { if (currentPg != null) RebuildModPageRows(currentPg); });
            }
        }

        /// <summary>
        /// Logique commune de navigation dans le contenu d'une page à VRow (Accueil / Page mod) :
        /// haut/bas déplace la sélection (ou remonte vers les onglets si on est tout en haut),
        /// gauche/droite ajuste un slider sélectionné, trigger active la ligne.
        /// </summary>
        void HandleContentPage(bool vEdge, bool up, bool hEdge, float stickX, bool triggerPressed,
            List<VRow> rows, ref int selected, Action rebuild)
        {
            if (vEdge)
            {
                _stickVCooldown = StickRepeatDelay;
                if (up && selected == 0)
                {
                    _focusOnTabs = true;
                    _tabSelected = (int)_page;
                    SoundFX.PlaySound("Moyen");
                    RefreshTabsVisual();
                    rebuild?.Invoke();
                }
                else if (rows.Count > 0)
                {
                    selected = up ? Mathf.Max(0, selected - 1) : Mathf.Min(rows.Count - 1, selected + 1);
                    SoundFX.PlaySound("Moyen");
                    rebuild?.Invoke();
                }
            }

            bool sliderSelected = selected >= 0 && selected < rows.Count && rows[selected].Type == VRow.Kind.Slider;
            if (sliderSelected && _stickHRepeatCooldown <= 0f && Mathf.Abs(stickX) >= _stickSens)
            {
                _stickHRepeatCooldown = StickHRepeatDelay;
                int dir = stickX > 0f ? 1 : -1;
                rows[selected].OnSliderStep?.Invoke(dir);
                SoundFX.PlaySound("Moyen");
                rebuild?.Invoke();
            }

            if (triggerPressed && selected >= 0 && selected < rows.Count && rows[selected].OnActivate != null)
            {
                rows[selected].OnActivate.Invoke();
                SoundFX.PlaySound("Moyen");
                rebuild?.Invoke();
            }
        }

        /// <summary>Force le focus dans le contenu à repartir sur la première ligne visible, sans changer la sélection mémorisée.</summary>
        void RefreshContentFocus()
        {
            if (_page == HPage.ModList) RebuildListUI();
            else if (_page == HPage.Home) RebuildHomeRows();
            else if (_page == HPage.ModPage)
            {
                var pages = GetSortedPages();
                if (_selectedIndex >= 0 && _selectedIndex < pages.Count) RebuildModPageRows(pages[_selectedIndex]);
            }
        }

        /// <summary>
        /// Détecte un geste bas->haut (ouvre le menu) ou haut->bas (ferme le menu) sur
        /// n'importe quel stick (gauche ou droit). On n'a pas besoin de repasser par zéro :
        /// on regarde juste que Y dépasse -_shakeThreshold puis +_shakeThreshold (ou l'inverse)
        /// dans une fenêtre de _shakeWindow secondes.
        /// </summary>
        void UpdateShakeToggle(Vector2 stick)
        {
            if (_shakeStateTime > 0f)
            {
                _shakeStateTime -= Time.deltaTime;
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
                        // Bas -> Haut : ouvre le menu
                        _shakeState = ShakeState.Neutral;
                        _shakeStateTime = 0f;
                        if (!_open) OpenMenu();
                    }
                    break;

                case ShakeState.WentUp:
                    if (y <= -_shakeThreshold)
                    {
                        // Haut -> Bas : ferme le menu
                        _shakeState = ShakeState.Neutral;
                        _shakeStateTime = 0f;
                        if (_open) CloseMenu();
                    }
                    break;
            }
        }

        void OpenMenu()
        {
            _open = true;
            _page = HPage.ModList;
            _focusOnTabs = false;
            ShowPage(_page);
            SoundFX.PlaySound("Aigue");
        }

        void CloseMenu()
        {
            _open = false;
            SoundFX.PlaySound("Grave");
        }

        // ================================================================
        // LECTURE DU STICK — via ControllerInputPoller (le champ officiel
        // utilisé par le jeu lui-même), avec une petite deadzone pour
        // ignorer le drift matériel du stick au repos.
        // ================================================================
        const float StickDeadzone = 0.2f; // ajuste si besoin selon le drift observé chez toi

        Vector2 ApplyDeadzone(Vector2 raw)
        {
            float mag = raw.magnitude;
            if (mag < StickDeadzone) return Vector2.zero;
            float scaled = (mag - StickDeadzone) / (1f - StickDeadzone);
            return raw.normalized * scaled;
        }

        Vector2 ReadAnyStick()
        {
            var poller = ControllerInputPoller.instance;
            if (poller == null) return _pcTestStick;

            Vector2 lAxis = ApplyDeadzone(poller.leftControllerPrimary2DAxis);
            Vector2 rAxis = ApplyDeadzone(poller.rightControllerPrimary2DAxis);

            if (lAxis.sqrMagnitude > 0.01f) return lAxis;
            if (rAxis.sqrMagnitude > 0.01f) return rAxis;

            return _pcTestStick;
        }

        bool ReadAnyTriggerDown()
        {
            var poller = ControllerInputPoller.instance;
            if (poller == null) return _pcTestTriggerDown;

            bool lTrig = poller.leftControllerTriggerButton;
            bool rTrig = poller.rightControllerTriggerButton;

            bool downEdge = (lTrig && !_lastTriggerL) || (rTrig && !_lastTriggerR);
            _lastTriggerL = lTrig; _lastTriggerR = rTrig;

            bool trig = downEdge || _pcTestTriggerDown;
            _pcTestTriggerDown = false;
            return trig;
        }

        static UnityEngine.XR.InputDevice? GetDevice(bool rightHand)
        {
            var chars = InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller | (rightHand ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left);
            var devices = new List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(chars, devices);
            return devices.Count > 0 ? devices[0] : (UnityEngine.XR.InputDevice?)null;
        }

        // ================================================================
        // Pages / affichage
        // ================================================================
        void ShowPage(HPage page)
        {
            _homePageGO.SetActive(page == HPage.Home);
            _listPageGO.SetActive(page == HPage.ModList);
            _modPageGO.SetActive(page == HPage.ModPage);

            if (page == HPage.ModList) RebuildListUI();
            if (page == HPage.Home) RebuildHomeRows();
            if (page == HPage.ModPage)
            {
                var pages = GetSortedPages();
                if (_selectedIndex >= 0 && _selectedIndex < pages.Count)
                {
                    if (_modPageBuiltForIndex != _selectedIndex) _modSelected = 0;
                    RebuildModPageRows(pages[_selectedIndex]);
                    _modPageBuiltForIndex = _selectedIndex;
                }
            }

            RefreshTabsVisual();
        }

        List<Page> GetSortedPages() => MenuUI.Instance != null ? MenuUI.Instance.GetAllPagesSorted() : new List<Page>();

        void RebuildListUI()
        {
            foreach (var row in _listRows) if (row != null) Destroy(row);
            _listRows.Clear();
            _listRowY.Clear();
            _listRowH.Clear();

            // Auto-génération : la liste vient directement de MenuUI.GetAllPagesSorted(), donc
            // TOUT mod qui appelle MenuUI.Instance.RegisterPage(...) apparaît ici automatiquement,
            // sans configuration manuelle dans GorillaInterface.
            var pages = GetSortedPages();
            if (_selectedIndex >= pages.Count) _selectedIndex = Mathf.Max(0, pages.Count - 1);

            float rowH = 30f, gap = 4f, catH = 22f, y = 0f;
            MenuCategory lastCat = (MenuCategory)(-1);

            bool focusInList = !_focusOnTabs && _page == HPage.ModList;

            for (int i = 0; i < pages.Count; i++)
            {
                var pg = pages[i];
                if (pg.Category != lastCat)
                {
                    lastCat = pg.Category;
                    var catGO = MakeText(_listContent, "Cat_" + pg.Category, CategoryNames.Get(pg.Category), 13, Theme.Accent, TextAlignmentOptions.TopLeft,
                        new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(y + catH)), new Vector2(0f, -y));
                    _listRows.Add(catGO.gameObject);
                    y += catH + 2f;
                }

                bool selected = focusInList && i == _selectedIndex;
                Color rowBg = selected ? Theme.BG3 : Theme.BG2;

                var rowGO = new GameObject("Row_" + i, typeof(RectTransform), typeof(Image));
                var rowRt = rowGO.GetComponent<RectTransform>();
                rowRt.SetParent(_listContent, false);
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.anchoredPosition = new Vector2(0f, -y);
                rowRt.sizeDelta = new Vector2(0f, rowH);
                var rowImg = rowGO.GetComponent<Image>();
                rowImg.color = rowBg;
                rowImg.raycastTarget = false;
                _listRows.Add(rowGO);

                _listRowY.Add(y);
                _listRowH.Add(rowH);

                MakeFixedImage(rowRt, "Dot", pg.Enabled ? Theme.Green : Theme.Red, new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(14f, 14f));

                var label = MakeText(rowRt, "Label", I18n.T(pg.Title), 14,
                    selected ? Theme.Accent : Theme.Text, TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(12f, 0f), new Vector2(-40f, 0f));
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;

                y += rowH + gap;
            }

            _listContent.sizeDelta = new Vector2(0f, Mathf.Max(y, 10f));

            if (_selectedIndex >= 0 && _selectedIndex < _listRowY.Count)
            {
                float rowTop = _listRowY[_selectedIndex];
                float rowBottom = rowTop + _listRowH[_selectedIndex];
                float viewH = _listViewport.rect.height;

                float visibleTop = rowTop - _listScrollY;
                float visibleBottom = rowBottom - _listScrollY;

                if (visibleTop < 0f) _listScrollY = rowTop;
                else if (visibleBottom > viewH) _listScrollY = rowBottom - viewH;

                float maxScroll = Mathf.Max(0f, _listContent.sizeDelta.y - viewH);
                _listScrollY = Mathf.Clamp(_listScrollY, 0f, maxScroll);
            }
            else
            {
                _listScrollY = 0f;
            }

            _listContent.anchoredPosition = new Vector2(0f, _listScrollY);
        }

        void RefreshDynamicTexts()
        {
            if (_page == HPage.ModPage)
            {
                var pages = GetSortedPages();
                if (_selectedIndex >= 0 && _selectedIndex < pages.Count)
                {
                    var pg = pages[_selectedIndex];
                    if (_modPageTitle != null) _modPageTitle.text = I18n.T(pg.Title);
                }
            }
            else if (_page == HPage.ModList)
            {
                RebuildListUI();
            }

            if (_panelBg != null) _panelBg.color = Theme.BG;
        }

        public void RebuildTheme()
        {
            if (_panelBg != null) _panelBg.color = Theme.BG;
            RebuildListUI();
            if (_page == HPage.Home) RebuildHomeRows();
            if (_page == HPage.ModPage)
            {
                var pages = GetSortedPages();
                if (_selectedIndex >= 0 && _selectedIndex < pages.Count) RebuildModPageRows(pages[_selectedIndex]);
            }
            RefreshTabsVisual();
        }

        // ================================================================
        // Boutons PC de test
        // ================================================================
        Vector2 _pcTestStick;
        bool _pcTestTriggerDown;

        void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }

        // ================================================================
        // Page de réglages (dans le menu IMGUI principal) — simplifiée au maximum.
        // Aucun rebind clavier ici : ce mod ne lit que le stick et le trigger VR.
        // ================================================================
        void DrawSettingsPage(MenuUI ui)
        {
            ui.Section(I18n.T("GorillaInterface.Section"));

            if (!Enabled)
            {
                ui.BeginCard();
                ui.Label(I18n.T("NameTags.EnableFromModsHint"), Theme.Dim, 9);
                ui.EndCard();
            }

            ui.Space(6);
            ui.BeginCard();
            ui.Label(I18n.T("GorillaInterface.Desc"), Theme.Text, 10);
            ui.EndCard();

            ui.Space(4);
            ui.BeginCard();
            float newScale = ui.Slider(I18n.T("GorillaInterface.SizeShort"), _userScale, MinUserScale, MaxUserScale, "0.00x");
            if (!Mathf.Approximately(newScale, _userScale)) { _userScale = newScale; SaveSettings(); }

            ui.Space(4);
            float newDist = ui.Slider(I18n.T("GorillaInterface.DistanceShort"), _spawnDistance, MinDistance, MaxDistance, "0.00m");
            if (!Mathf.Approximately(newDist, _spawnDistance)) { _spawnDistance = newDist; SaveSettings(); }
            ui.EndCard();

            ui.Space(4);
            ui.BeginCard();
            float newAnim = ui.Slider(I18n.T("GorillaInterface.AnimSpeedShort"), _animSpeed, MinAnimSpeed, MaxAnimSpeed, "0.0");
            if (!Mathf.Approximately(newAnim, _animSpeed)) { _animSpeed = newAnim; SaveSettings(); }

            ui.Space(4);
            float newMaxDist = ui.Slider(I18n.T("GorillaInterface.MaxDistShort"), _maxDistance, MinMaxDist, MaxMaxDist, "0.00m");
            if (!Mathf.Approximately(newMaxDist, _maxDistance)) { _maxDistance = newMaxDist; SaveSettings(); }

            ui.Space(4);
            float newSens = ui.Slider(I18n.T("GorillaInterface.StickSensShort"), _stickSens, MinStickSens, MaxStickSens, "0.00");
            if (!Mathf.Approximately(newSens, _stickSens)) { _stickSens = newSens; SaveSettings(); }
            ui.EndCard();

            // ---- Réglages du geste bas->haut / haut->bas pour ouvrir/fermer le menu ----
            ui.Space(4);
            ui.BeginCard();
            float newShakeThreshold = ui.Slider("Seuil geste (bas/haut)", _shakeThreshold, MinShakeThreshold, MaxShakeThreshold, "0.00");
            if (!Mathf.Approximately(newShakeThreshold, _shakeThreshold)) { _shakeThreshold = newShakeThreshold; SaveSettings(); }

            ui.Space(4);
            float newShakeWindow = ui.Slider("Fenetre geste (secondes)", _shakeWindow, MinShakeWindow, MaxShakeWindow, "0.00");
            if (!Mathf.Approximately(newShakeWindow, _shakeWindow)) { _shakeWindow = newShakeWindow; SaveSettings(); }
            ui.EndCard();

            // ---- Debug stick en temps réel, affiché directement dans le menu PC ----
            ui.Space(4);
            ui.BeginCard();
            ui.Label($"L:({_debugStickL.x:0.00};{_debugStickL.y:0.00}) - R:({_debugStickR.x:0.00};{_debugStickR.y:0.00})", Theme.Dim, 10);
            ui.EndCard();
        }
    }
}