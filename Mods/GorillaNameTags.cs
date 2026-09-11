using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UltimateMenu
{
    public class GorillaNameTags : MonoBehaviour
    {
        public static GorillaNameTags Instance { get; private set; }
        enum Platform { Quest, Steam }

        static readonly FieldInfo RankedSubTierPCField =
            typeof(VRRig).GetField("currentRankedSubTierPC", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(VRRig).GetField("currentRankedSubTierPC", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        static readonly PropertyInfo RankedSubTierPCProperty =
            RankedSubTierPCField == null
                ? typeof(VRRig).GetProperty("currentRankedSubTierPC", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                : null;

        enum IconMode { Off, Left, Right }

        /// <summary>
        /// Le mod est actif uniquement via le switch global de la page Mods (liste des mods),
        /// comme GorillaVoices / GorillaMocap. Quand désactivé, tous les tags existants sont
        /// détruits et recréés à la réactivation (pas de coût à les garder cachés en mémoire).
        /// </summary>
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("NameTags.Title");
        private bool _wasEnabled = true;

        bool _pageRegistered;

        // --- Réglages persistants ---
        float _baseSize = 0.3f;         // multiplicateur d'échelle global du nametag
        float _maxRenderDistance = 40f; // au-delà : le nametag est masqué
        float _heightOffset = 0.6f;     // hauteur au-dessus de la tête
        float _iconGap = 8f;            // distance (en pixels canvas) entre le texte et l'icône
        IconMode _iconMode = IconMode.Left;

        // Mise à l'échelle par distance : plus de sliders dédiés dans l'UI (page simplifiée),
        // mais le comportement (rétrécir progressivement avec l'éloignement) reste utile,
        // donc conservé en constantes fixes plutôt que retiré.
        const float NearDistance = 3f;   // en dessous : taille max (_baseSize)
        const float FarDistance = 20f;   // au-dessus : taille min (MinScaleMul)
        const float MinScaleMul = 0.4f;  // échelle mini appliquée à FarDistance et au-delà

        const float MinBaseSize = 0.1f, MaxBaseSize = 1f;
        const float MaxFar = 50f;
        const float MinHeightOffset = 0.5f, MaxHeightOffset = 1.5f;
        const float MinIconGap = 0f, MaxIconGap = 40f;
        const float IconSize = 28f;

        const string PREF_BASE_SIZE = "MUM_NT_BaseSize";
        const string PREF_MAXDIST = "MUM_NT_MaxDist";
        const string PREF_HEIGHTOFFSET = "MUM_NT_HeightOffset";
        const string PREF_ICONMODE = "MUM_NT_IconMode";
        const string PREF_ICONGAP = "MUM_NT_IconGap";

        // --- Cache tête des autres joueurs (même pattern que GorillaVoices) ---
        readonly Dictionary<VRRig, Transform> _headCache = new Dictionary<VRRig, Transform>();
        readonly Dictionary<VRRig, TagInstance> _tags = new Dictionary<VRRig, TagInstance>();
        readonly List<VRRig> _toRemove = new List<VRRig>();
        readonly HashSet<VRRig> _activeRigsSet = new HashSet<VRRig>();

        Transform _localHead;
        float _localHeadRefreshTimer;

        // --- Icônes plateforme (PNG embarqués dans la DLL, fond transparent déjà présent) ---
        const string METAICON_RESOURCE = "UltimateMenu.Assets.GorillaNameTags.Meta.png";
        const string STEAMICON_RESOURCE = "UltimateMenu.Assets.GorillaNameTags.Steam.png";
        static Sprite _metaIconSprite;
        static Sprite _steamIconSprite;
        static bool _platformIconsLoaded;

        // --- Police TMP à utiliser pour les nametags (résolue une seule fois) ---
        static TMP_FontAsset _font;
        static bool _fontResolved;

        class TagInstance
        {
            public GameObject Root;
            public Canvas Canvas;
            public TextMeshProUGUI Text;
            public TextMeshProUGUI ShadowText;
            public Image Icon;
            public RectTransform IconRect; // mis en cache : évite un GetComponent à chaque frame de changement
            public string LastName = "";
            public Platform? LastPlatform;
            public IconMode LastIconMode = (IconMode)(-1); // force une maj au premier passage
            public float LastIconGap = float.NaN;           // force une maj au premier passage
        }

        void Awake()
        {
            Instance = this;
            LoadSettings();
        }

        void LoadSettings()
        {
            _baseSize = PlayerPrefs.GetFloat(PREF_BASE_SIZE, 0.3f);
            _maxRenderDistance = PlayerPrefs.GetFloat(PREF_MAXDIST, 40f);
            _heightOffset = PlayerPrefs.GetFloat(PREF_HEIGHTOFFSET, 0.6f);
            _iconMode = (IconMode)PlayerPrefs.GetInt(PREF_ICONMODE, (int)IconMode.Left);
            _iconGap = PlayerPrefs.GetFloat(PREF_ICONGAP, 8f);
        }

        void SaveSettings()
        {
            PlayerPrefs.SetFloat(PREF_BASE_SIZE, _baseSize);
            PlayerPrefs.SetFloat(PREF_MAXDIST, _maxRenderDistance);
            PlayerPrefs.SetFloat(PREF_HEIGHTOFFSET, _heightOffset);
            PlayerPrefs.SetInt(PREF_ICONMODE, (int)_iconMode);
            PlayerPrefs.SetFloat(PREF_ICONGAP, _iconGap);
            PlayerPrefs.Save();
        }

        void Update()
        {
            if (!_pageRegistered && MenuUI.Instance != null)
            {
                MenuUI.Instance.RegisterPage("NameTags.Title", DrawPage, MenuCategory.Social);
                _pageRegistered = true;
            }

            _localHeadRefreshTimer -= Time.deltaTime;
            if (_localHead == null || _localHeadRefreshTimer <= 0f)
            {
                _localHeadRefreshTimer = 1f;
                RefreshLocalHead();
            }

            bool nowEnabled = Enabled;
            if (nowEnabled != _wasEnabled)
            {
                _wasEnabled = nowEnabled;
                if (!nowEnabled) DestroyAllTags();
            }

            if (!Enabled) return;
            EnsureFont();
            EnsurePlatformIcons();

            RefreshTags();
        }

        bool _localHeadLoggedOnce;
        void RefreshLocalHead()
        {
            var localRig = VRRig.LocalRig;
            if (localRig == null) return;
            var head = localRig.transform.Find("rig/head");
            if (head != null)
            {
                _localHead = head;
                if (!_localHeadLoggedOnce)
                {
                    _localHeadLoggedOnce = true;
                }
            }
        }

        Transform GetOtherHead(VRRig rig)
        {
            if (_headCache.TryGetValue(rig, out var cached) && cached != null) return cached;
            var head = rig.transform.Find("rig/head");
            if (head != null) _headCache[rig] = head;
            return head;
        }

        // --- Boucle principale : crée/maj/détruit un tag par rig actif ---
        void RefreshTags()
        {
            if (_localHead == null) return;

            // Construit le set des rigs actifs une seule fois par frame : évite un parcours
            // O(n) de VRRigCache.ActiveRigs pour chaque tag existant lors du nettoyage
            // ci-dessous (c'était O(n²) avant, sensible à partir d'une dizaine de joueurs).
            _activeRigsSet.Clear();

            foreach (var rig in VRRigCache.ActiveRigs)
            {
                if (rig == null || rig == GorillaTagger.Instance.offlineVRRig) continue;
                _activeRigsSet.Add(rig);

                var otherHead = GetOtherHead(rig);
                if (otherHead == null) continue;

                if (!_tags.TryGetValue(rig, out var tag) || tag.Root == null)
                {
                    tag = BuildTag(rig);
                    _tags[rig] = tag;
                }

                UpdateTag(rig, tag, otherHead);
            }

            // Nettoyage des tags dont le rig n'est plus actif (déco, changement de room, etc.)
            _toRemove.Clear();
            foreach (var kv in _tags)
                if (!_activeRigsSet.Contains(kv.Key)) _toRemove.Add(kv.Key);

            foreach (var rig in _toRemove)
            {
                if (_tags.TryGetValue(rig, out var tag) && tag.Root != null) Destroy(tag.Root);
                _tags.Remove(rig);
                _headCache.Remove(rig);
            }
        }

        void DestroyAllTags()
        {
            foreach (var kv in _tags) if (kv.Value.Root != null) Destroy(kv.Value.Root);
            _tags.Clear();
            _headCache.Clear();
        }

        // --- Résolution de la police TMP à utiliser ---
        static void EnsureFont()
        {
            if (_fontResolved) return;
            _fontResolved = true;

            _font = TMP_Settings.defaultFontAsset;
            if (_font == null)
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                if (all != null && all.Length > 0) _font = all[0];
            }
        }

        // --- Chargement des icônes plateforme depuis les ressources embarquées de la DLL ---
        static void EnsurePlatformIcons()
        {
            if (_platformIconsLoaded) return;
            _platformIconsLoaded = true;

            _metaIconSprite = LoadEmbeddedSprite(METAICON_RESOURCE, "Meta");
            _steamIconSprite = LoadEmbeddedSprite(STEAMICON_RESOURCE, "Steam");
        }

        static Sprite LoadEmbeddedSprite(string resourceName, string label)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        tex.LoadImage(ms.ToArray()); // détecte le PNG et redimensionne la texture automatiquement
                        tex.filterMode = FilterMode.Bilinear;
                        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        return sprite;
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Err("GorillaNameTags.LoadEmbeddedSprite(" + label + ")", e);
                return null;
            }
        }

        // --- Construction du GameObject nametag (Canvas WorldSpace + texte + icône) ---
        // Plus de fond : juste le texte (avec ombre portée pour la lisibilité) et l'icône
        // plateforme optionnelle.
        TagInstance BuildTag(VRRig rig)
        {
            Log.Info("[GorillaNameTags] BuildTag pour " + (rig != null && rig.Creator != null ? rig.Creator.NickName : "?"));
            var root = new GameObject("MUM_NameTag");
            var canvasGO = root;
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 500;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 24f;

            var canvasRect = canvasGO.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(400f, 100f);
            canvasRect.localScale = Vector3.one * 0.01f; // 1 unité canvas = 1cm en world space par défaut

            // Texte pseudo (TMP), centré, autoscale.
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(canvasRect, false);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            if (_font != null) tmp.font = _font;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 10f;
            tmp.fontSizeMax = 42f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color(0f, 0f, 0f, 0.9f);
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(340f, 44f);
            textRect.anchoredPosition = Vector2.zero;

            // Ombre portée additionnelle (en plus de l'outline) pour rester lisible sans fond.
            var shadowGO = new GameObject("TextShadow");
            shadowGO.transform.SetParent(canvasRect, false);
            shadowGO.transform.SetSiblingIndex(textGO.transform.GetSiblingIndex()); // juste avant le texte
            var shadowTmp = shadowGO.AddComponent<TextMeshProUGUI>();
            if (_font != null) shadowTmp.font = _font;
            shadowTmp.alignment = TextAlignmentOptions.Center;
            shadowTmp.enableAutoSizing = true;
            shadowTmp.fontSizeMin = 10f;
            shadowTmp.fontSizeMax = 42f;
            shadowTmp.fontStyle = FontStyles.Bold;
            shadowTmp.color = new Color(0f, 0f, 0f, 0.8f);
            shadowTmp.raycastTarget = false;
            shadowTmp.enableWordWrapping = false;
            shadowTmp.overflowMode = TextOverflowModes.Overflow;
            var shadowRect = shadowGO.GetComponent<RectTransform>();
            shadowRect.anchorMin = new Vector2(0.5f, 0.5f);
            shadowRect.anchorMax = new Vector2(0.5f, 0.5f);
            shadowRect.pivot = new Vector2(0.5f, 0.5f);
            shadowRect.sizeDelta = new Vector2(340f, 44f);
            shadowRect.anchoredPosition = new Vector2(1.5f, -1.5f);

            // Icône plateforme (Steam/Meta), masquée par défaut — activée/positionnée dans
            // UpdateTag selon _iconMode (Off / Left / Right) et GetPlatform.
            var iconGO = new GameObject("PlatformIcon");
            iconGO.transform.SetParent(canvasRect, false);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(IconSize, IconSize);
            iconGO.SetActive(false);

            return new TagInstance
            {
                Root = root,
                Canvas = canvas,
                Text = tmp,
                ShadowText = shadowTmp,
                Icon = iconImg,
                IconRect = iconRect
            };
        }

        void UpdateTag(VRRig rig, TagInstance tag, Transform otherHead)
        {
            if (tag.Root == null) return;

            var tagTransform = tag.Root.transform;
            Vector3 headPos = _localHead.position;
            Vector3 otherPos = otherHead.position;

            float dist = Vector3.Distance(headPos, otherPos);

            if (dist > _maxRenderDistance)
            {
                if (tag.Root.activeSelf) tag.Root.SetActive(false);
                return;
            }
            if (!tag.Root.activeSelf) tag.Root.SetActive(true);

            // Position au-dessus de la tête, hauteur configurable.
            tagTransform.position = otherPos + Vector3.up * _heightOffset;

            // Oriente le tag vers la tête LOCALE du joueur (comparaison tête-à-tête, cohérent
            // avec GorillaVoices). LookAt regarde le +Z vers la cible ; un Canvas WorldSpace
            // affiche son contenu sur sa face +Z par défaut donc on inverse via Rotate(0,180,0).
            tagTransform.LookAt(headPos);
            tagTransform.Rotate(0f, 180f, 0f);

            // Échelle : pleine taille sous NearDistance, décroît linéairement jusqu'à
            // MinScaleMul à FarDistance, puis reste constante au-delà.
            float t = Mathf.InverseLerp(NearDistance, FarDistance, dist);
            float scaleMul = Mathf.Lerp(1f, MinScaleMul, t);
            tagTransform.localScale = Vector3.one * (0.01f * _baseSize * scaleMul);

            string name = GetDisplayName(rig);
            Platform platform = GetPlatform(rig);
            bool nameChanged = tag.LastName != name;
            bool platformChanged = tag.LastPlatform != platform;
            bool iconModeChanged = tag.LastIconMode != _iconMode;
            bool iconGapChanged = !Mathf.Approximately(tag.LastIconGap, _iconGap);

            if (nameChanged)
            {
                tag.Text.text = name;
                if (tag.ShadowText != null) tag.ShadowText.text = name;
                tag.LastName = name;
            }

            if (nameChanged || platformChanged || iconModeChanged || iconGapChanged)
            {
                tag.LastPlatform = platform;
                tag.LastIconMode = _iconMode;
                tag.LastIconGap = _iconGap;

                Sprite iconSprite = platform == Platform.Steam ? _steamIconSprite : _metaIconSprite;
                bool showIcon = _iconMode != IconMode.Off && tag.Icon != null && iconSprite != null;

                if (tag.Icon != null)
                {
                    tag.Icon.sprite = iconSprite;
                    tag.Icon.gameObject.SetActive(showIcon);
                }

                if (showIcon)
                {
                    // Largeur RÉELLE du texte rendu (pas une estimation par nombre de
                    // caractères) : GetPreferredValues force TMP à calculer le layout avec la
                    // fontSize actuellement utilisée par l'autoscale, donc la valeur reflète
                    // exactement ce qui est affiché à l'écran, police/largeur de caractères
                    // comprises (un "iiii" et un "WWWW" n'ont pas du tout la même largeur).
                    // Ce calcul ne tourne que sur changement (nom/plateforme/mode/gap), pas
                    // chaque frame.
                    float half = tag.Text.GetPreferredValues(name, 0f, 0f).x * 0.5f;
                    float iconX = _iconMode == IconMode.Left
                        ? -half - _iconGap - IconSize * 0.5f
                        : half + _iconGap + IconSize * 0.5f;
                    tag.IconRect.anchoredPosition = new Vector2(iconX, 0f);
                }
            }
        }

        // Récupère le pseudo à afficher — passe par le même chemin que le nametag par défaut
        // du jeu (playerText1) pour rester cohérent avec ce que le joueur voit déjà sur le torse.
        string GetDisplayName(VRRig rig)
        {
            try
            {
                if (rig.playerText1 != null && !string.IsNullOrEmpty(rig.playerText1.text))
                    return rig.playerText1.text;
            }
            catch { }
            try { return rig.Creator.NickName; } catch { return "???"; }
        }

        bool _platformFieldLoggedOnce;

        Platform GetPlatform(VRRig rig)
        {
            try
            {
                object raw = null;
                if (RankedSubTierPCField != null)
                    raw = RankedSubTierPCField.IsStatic ? RankedSubTierPCField.GetValue(null) : RankedSubTierPCField.GetValue(rig);
                else if (RankedSubTierPCProperty != null)
                    raw = RankedSubTierPCProperty.GetValue(rig);

                if (!_platformFieldLoggedOnce)
                {
                    _platformFieldLoggedOnce = true;
                    Log.Info("[GorillaNameTags] currentRankedSubTierPC via réflexion : " +
                        (RankedSubTierPCField != null ? "field" : RankedSubTierPCProperty != null ? "property" : "introuvable") +
                        ", valeur=" + (raw != null ? raw.ToString() : "null"));
                }

                if (raw == null) return Platform.Quest;
                int value = System.Convert.ToInt32(raw);
                return value != 0 ? Platform.Steam : Platform.Quest;
            }
            catch (System.Exception e)
            {
                if (!_platformFieldLoggedOnce)
                {
                    _platformFieldLoggedOnce = true;
                    Log.Err("GorillaNameTags.GetPlatform", e);
                }
                return Platform.Quest;
            }
        }

        void OnDestroy() => DestroyAllTags();

        // --- Page de réglages dans le menu (simplifiée : moins de sliders, un seul sélecteur
        // pour l'icône, plus de réglage de fond puisqu'il n'y en a plus). ---
        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("NameTags.Section"));

            if (!Enabled)
            {
                ui.BeginCard();
                ui.Label(I18n.T("NameTags.EnableFromModsHint"), Theme.Dim, 9);
                ui.EndCard();
            }

            ui.Space(6);
            ui.BeginCard();
            ui.Label(I18n.T("NameTags.BaseSize"), Theme.Dim, 9);
            float newBase = ui.Slider(I18n.T("NameTags.BaseSizeShort"), _baseSize, MinBaseSize, MaxBaseSize, "0.00x");
            if (!Mathf.Approximately(newBase, _baseSize) && Enabled) { _baseSize = newBase; SaveSettings(); }

            ui.Space(4);
            ui.Label(I18n.T("NameTags.HeightOffset"), Theme.Dim, 9);
            float newHeight = ui.Slider(I18n.T("NameTags.HeightOffsetShort"), _heightOffset, MinHeightOffset, MaxHeightOffset, "0.00m");
            if (!Mathf.Approximately(newHeight, _heightOffset) && Enabled) { _heightOffset = newHeight; SaveSettings(); }

            ui.Space(4);
            ui.Label(I18n.T("NameTags.MaxRenderDistance"), Theme.Dim, 9);
            float newMaxDist = ui.Slider(I18n.T("NameTags.MaxRenderDistanceShort"), _maxRenderDistance, NearDistance, MaxFar, "0.0m");
            if (!Mathf.Approximately(newMaxDist, _maxRenderDistance) && Enabled) { _maxRenderDistance = newMaxDist; SaveSettings(); }
            ui.EndCard();

            ui.Space(4);
            ui.BeginCard();
            ui.Label(I18n.T("NameTags.IconMode"), Theme.Dim, 9);
            ui.Space(4);
            DrawIconModeSelector(ui);

            if (_iconMode != IconMode.Off)
            {
                ui.Space(4);
                ui.Label(I18n.T("NameTags.IconGap"), Theme.Dim, 9);
                float newGap = ui.Slider(I18n.T("NameTags.IconGapShort"), _iconGap, MinIconGap, MaxIconGap, "0.0");
                if (!Mathf.Approximately(newGap, _iconGap) && Enabled) { _iconGap = newGap; SaveSettings(); }
            }
            ui.EndCard();
        }

        // Sélecteur à 3 options (Off / Gauche / Droite) pour l'icône plateforme, sous forme de
        // trois boutons côte à côte plutôt qu'un slider brut — plus clair pour un choix discret.
        void DrawIconModeSelector(MenuUI ui)
        {
            GUILayout.BeginHorizontal();
            DrawIconModeButton(ui, IconMode.Off, I18n.T("NameTags.IconOff"));
            DrawIconModeButton(ui, IconMode.Left, I18n.T("NameTags.IconLeft"));
            DrawIconModeButton(ui, IconMode.Right, I18n.T("NameTags.IconRight"));
            GUILayout.EndHorizontal();
        }

        void DrawIconModeButton(MenuUI ui, IconMode mode, string label)
        {
            bool selected = _iconMode == mode;
            Color textColor = selected ? Theme.Accent : Theme.Dim;
            if (ui.Btn(label, ui.TB3, textColor) && Enabled && !selected)
            {
                _iconMode = mode;
                SaveSettings();
            }
        }
    }
}