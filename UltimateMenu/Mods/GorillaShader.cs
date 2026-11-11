using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using URP = UnityEngine.Rendering.Universal;

namespace UltimateMenu
{
    // Fog volumétrique + rayons de lumière (URP). Un quad plein écran (shader du bundle gorillavolumetric.bundle)
    // est dessiné par CHAQUE caméra, y compris les deux yeux en VR. Il lit la depth de la caméra :
    //  - le fog suit la distance à la surface visible,
    //  - les rayons partent du soleil et sont bloqués par tout ce qui est devant (depth), sans shadow map.
    // Le mod ne crée aucune lumière et ne touche plus aux réglages d'ombres : tout est restauré à la désactivation.
    public class GorillaShader : MonoBehaviour
    {
        internal const string Title = "Shader.Title";
        internal const string FogKey = "MUM_VisualFog", PostFxKey = "MUM_VisualPostFx";
        bool _fogOpen, _postFxOpen;
        const string BundleSuffix = "gorillavolumetric.bundle";
        const string MatName = "gorillavolumetric";
        const string P = "MUM_VolFog_";
        const float UpdateInterval = 0.2f;

        bool _registered, _applied, _hooked;
        float _nextUpdate;
        AssetBundle _bundle;

        // --- Réglages ---
        bool _ambientFog = true, _skyByQueue = true;
        readonly Dictionary<Material, int> _queueBackup = new Dictionary<Material, int>();
        bool _inspectMeshes;
        MeshRenderer _fogRenderer;
        float _debugMode, _maskThreshold = 0.999f;

        // --- Détecteur de meshes autour du joueur (masque debug) ---
        float _maskRadius = 60f, _nextMaskScan, _alphaMin = 0.1f;
        bool _fovOnly = true, _biggestOnly = true, _showSky = true;
        float _skySphereMin = 10f;   // diamètre mini (m) pour tester si un mesh est une sphère / demi-sphère à faces inversées (= ciel)
        const int MaxMaskItems = 600, AlphaBuckets = 4;
        sealed class MaskItem
        {
            public MeshRenderer R; public Mesh M; public int Subs, Layer, Bucket;
            public float Alpha = 1f, Dist; public bool Visible, Sky;
            public Renderer[] Parents;
            public LODGroup Lod; public int LodIdx; public float[] LodThresholds;   // calculés une seule fois
        }
        struct MatInfo { public bool Visible; public float Alpha; }

        readonly Material[] _maskMats = new Material[AlphaBuckets];   // 1 matériau par palier d'opacité => plus de MPB
        Material _maskMat, _pickMat;
        readonly Dictionary<int, MaskItem> _known = new Dictionary<int, MaskItem>();   // null = rejeté définitivement
        readonly Dictionary<Material, MatInfo> _matCache = new Dictionary<Material, MatInfo>();
        readonly List<MaskItem> _cache = new List<MaskItem>();
        readonly List<MaskItem> _maskItems = new List<MaskItem>(MaxMaskItems + 16);
        static readonly List<Material> _tmpMats = new List<Material>();
        static readonly Plane[] _planes = new Plane[6];
        static readonly System.Comparison<MaskItem> ByDist = (a, b) => a.Dist.CompareTo(b.Dist);
        float _nextCacheRefresh; int _sliceCursor, _lastMaskSig;
        float _density = 0.02f, _scatter = 0.35f, _aniso = 0.6f, _ambient = 0.15f, _maxDist = 80f, _steps = 32f;
        float _sunIntensity = 1.5f, _sunPitch = 25f, _sunYaw = 40f, _skyDist = 2000f;
        float _streakStr = 0.3f, _streakCount = 48f, _shaftFocus = 6f;
        Color _fogColor = new Color(0.85f, 0.65f, 0.42f);
        Color _sunColor = new Color(1f, 0.72f, 0.40f);

        // --- Objets créés ---
        GameObject _quadGo; Mesh _quadMesh; Material _mat;
        bool _shaderIsNew, _stereoKeyword;

        // --- Ciel : objets dont le nom (en minuscules) contient "sky" => la lumière passe à travers ---
        List<Renderer> _skyRenderers;
        float _nextSkyScan, _autoSky = float.MaxValue;
        int _lastSkyCount = -1;
        float _minSkyDist = 100f; // garde-fou : jamais en dessous, pour qu'un mauvais objet ne transforme pas toute la map en ciel

        // --- Caméras : la depth texture est forcée (valeur d'origine restaurée) ---
        readonly HashSet<int> _cams = new HashSet<int>();

        void Awake()
        {
            if (!MenuPrefs.HasKey("MUM_VisualCombined"))
            {
                string master = "MUM_ModEnabled_" + Title;
                bool fresh = !MenuPrefs.HasKey(master) && !MenuPrefs.HasKey("MUM_ModEnabled_Filter.Title");
                bool fog = MenuPrefs.GetInt(master, 0) == 1 && MenuPrefs.GetInt(P + "On", 1) == 1;
                bool post = MenuPrefs.GetInt("MUM_ModEnabled_Filter.Title", fresh ? 1 : 0) == 1;
                bool active = fog || post;
                MenuPrefs.SetInt(FogKey, fog || !active ? 1 : 0);
                MenuPrefs.SetInt(PostFxKey, post || !active ? 1 : 0);
                MenuPrefs.SetInt(master, active ? 1 : 0);
                MenuPrefs.SetInt("MUM_VisualCombined", 1);
                MenuPrefs.Save();
            }
            _ambientFog = MenuPrefs.GetInt(P + "AmbientFog", 1) == 1;
            _skyByQueue = MenuPrefs.GetInt(P + "SkyQueue", 1) == 1;
            _density = Mathf.Clamp(MenuPrefs.GetFloat(P + "Density", 0.02f), 0.001f, 0.1f);
            _scatter = MenuPrefs.GetFloat(P + "Scatter", 0.35f);
            _aniso = MenuPrefs.GetFloat(P + "Aniso", 0.6f);
            _ambient = MenuPrefs.GetFloat(P + "Ambient", 0.15f);
            _maxDist = MenuPrefs.GetFloat(P + "MaxDist", 80f);
            _skyDist = MenuPrefs.GetFloat(P + "SkyDist", 2000f);
            _minSkyDist = MenuPrefs.GetFloat(P + "MinSkyDist", 100f);
            _maskThreshold = MenuPrefs.GetFloat(P + "MaskThreshold", 0.999f);
            _maskRadius = MenuPrefs.GetFloat(P + "MaskRadius", 60f);
            _alphaMin = MenuPrefs.GetFloat(P + "AlphaMin", 0.1f);
            _fovOnly = MenuPrefs.GetInt(P + "FovOnly", 1) == 1;
            _biggestOnly = MenuPrefs.GetInt(P + "BiggestOnly", 1) == 1;
            _showSky = MenuPrefs.GetInt(P + "ShowSky", 1) == 1;
            _skySphereMin = MenuPrefs.GetFloat(P + "SkySphereMin", 10f);
            _streakStr = MenuPrefs.GetFloat(P + "StreakStr", 0.3f);
            _streakCount = MenuPrefs.GetFloat(P + "StreakCount", 48f);
            _shaftFocus = MenuPrefs.GetFloat(P + "ShaftFocus", 6f);
            _steps = MenuPrefs.GetFloat(P + "Steps", 32f);
            _sunIntensity = Mathf.Clamp(MenuPrefs.GetFloat(P + "SunInt", 1.5f), 1f, 10f);
            _sunPitch = MenuPrefs.GetFloat(P + "SunPitch", 25f);
            _sunYaw = MenuPrefs.GetFloat(P + "SunYaw", 40f);
            _fogColor = new Color(MenuPrefs.GetFloat(P + "FogR", 0.85f), MenuPrefs.GetFloat(P + "FogG", 0.65f), MenuPrefs.GetFloat(P + "FogB", 0.42f));
            _sunColor = new Color(MenuPrefs.GetFloat(P + "SunR", 1f), MenuPrefs.GetFloat(P + "SunG", 0.72f), MenuPrefs.GetFloat(P + "SunB", 0.40f));

            // Ordre des langues : Fr, En, ... (les langues non renseignées retombent sur En)
            I18n.Register(Title, "Fog volumétrique", "Volumetric fog");
            I18n.Register(Title, "Gorilla Shader · Effets visuels", "Gorilla Shader · Visual effects");
            I18n.Register("Shader.Looks", "Ambiances", "Looks");
            I18n.Register("Shader.Natural", "Naturel", "Natural");
            I18n.Register("Shader.Golden", "Heure dorée", "Golden hour");
            I18n.Register("Shader.Blue", "Brume bleue", "Blue mist");
            I18n.Register("Shader.NaturalDesc", "Couleurs douces · éclat léger", "Soft colors · gentle glow");
            I18n.Register("Shader.GoldenDesc", "Tons chauds · lumière dorée", "Warm tones · golden light");
            I18n.Register("Shader.BlueDesc", "Tons froids · brume légère", "Cool tones · light mist");
            I18n.Register("Shader.MyLooks", "Mes presets", "My presets");
            I18n.Register("Shader.MyLooksHint", "Enregistre le brouillard, les rayons et le post-processing actuels.", "Save the current fog, rays and post-processing.");
            I18n.Register("Shader.FogToggle", "Brouillard et rayons", "Fog and rays");
            I18n.Register("Shader.PostToggle", "Post-processing", "Post-processing");
            I18n.Register("Shader.Section", "Brouillard", "Fog");
            I18n.Register("Shader.On", "Activer le fog volumétrique", "Enable volumetric fog");
            I18n.Register("Shader.AmbientFog", "Voile de brouillard", "Ambient fog");
            I18n.Register("Shader.Density", "Densité du fog", "Fog density");
            I18n.Register("Shader.Scatter", "Intensité des rayons (lumière diffusée)", "Light scattering");
            I18n.Register("Shader.Aniso", "Directivité (rayons vers le soleil)", "Anisotropy (towards sun)");
            I18n.Register("Shader.Ambient", "Brume ambiante", "Ambient fog");
            I18n.Register("Shader.MaxDist", "Distance max (m)", "Max distance (m)");
            I18n.Register("Shader.SkyDist", "Distance du ciel (m) : au-delà, la lumière passe", "Sky distance (m): beyond it, light passes");
            I18n.Register("Shader.Steps", "Qualité (pas des rayons)", "Quality (ray steps)");
            I18n.Register("Shader.StreakStr", "Contraste des faisceaux", "Streak contrast");
            I18n.Register("Shader.InspectMeshes", "Inspecter les meshes (diagnostic séparé)", "Inspect meshes (separate diagnostic)");
            I18n.Register("Shader.DebugMask", "Masque des rayons (rouge = lumière bloquée)", "Ray occlusion mask (red = blocked light)");
            I18n.Register("Shader.MinSkyDist", "Détection du ciel : distance mini (m)", "Sky detection: min distance (m)");
            I18n.Register("Shader.SkyQueue", "Ciel = meshes \"sky\" ignorés par le masque (recommandé)", "Sky = \"sky\" meshes ignored by the mask (recommended)");
            I18n.Register("Shader.MaskRadius", "Détecteur : rayon autour du joueur (m)", "Detector: radius around the player (m)");
            I18n.Register("Shader.AlphaMin", "Détecteur : opacité minimale pour apparaître (0 = tout)", "Detector: min opacity to show (0 = everything)");
            I18n.Register("Shader.FovOnly", "Détecteur : champ de vision seulement (plus léger)", "Detector: field of view only (lighter)");
            I18n.Register("Shader.BiggestOnly", "Détecteur : seulement le plus gros objet à l'écran + ses morceaux (beaucoup plus léger)", "Detector: only the biggest object on screen + attached parts (much lighter)");
            I18n.Register("Shader.ShowSky", "Détecteur : afficher aussi les objets sky", "Detector: also show sky objects");
            I18n.Register("Shader.SkySphereMin", "Détecteur : ciel = sphère à faces vers l'intérieur, taille mini (m)", "Detector: sky = inward-facing sphere, min size (m)");
            I18n.Register("Shader.Pick", "Détecteur de visée (jaune = mesh visé)", "Aim detector (yellow = aimed mesh)");
            I18n.Register("Shader.PickIdx", "Détecteur : candidat (0 = le plus précis)", "Detector: candidate (0 = most precise)");
            I18n.Register("Shader.StreakCount", "Nombre de faisceaux", "Number of streaks");
            I18n.Register("Shader.ShaftFocus", "Concentration autour du soleil", "Concentration around the sun");
            I18n.Register("Shader.FogColor", "Couleur du fog", "Fog color");
            I18n.Register("Shader.SunSection", "Soleil (source des rayons)", "Sun (light shaft source)");
            I18n.Register("Shader.SunInt", "Intensité du soleil", "Sun intensity");
            I18n.Register("Shader.SunPitch", "Soleil : hauteur (°)", "Sun: elevation (°)");
            I18n.Register("Shader.SunYaw", "Soleil : direction (°)", "Sun: direction (°)");
            I18n.Register("Shader.SunColor", "Couleur du soleil", "Sun color");
            I18n.Register("Shader.Status", "Statut", "Status");
            I18n.Register("Shader.Hint",
                "Les rayons viennent du soleil : visibles seulement dehors, là où rien ne passe devant (tout mesh qui cache le ciel les bloque). Regarde vers le soleil pour les voir.",
                "Shafts come from the sun and only show outdoors, where nothing is in the way (any mesh hiding the sky blocks them). Look towards the sun to see them.");
        }

        void Update()
        {
            if (!_registered && MenuUI.Instance != null)
            {
                MenuUI.Instance.RegisterPage(Title, DrawPage, MenuCategory.Environnement);
                _registered = true;
            }

            bool active = _registered && MenuUI.Instance != null && MenuUI.Instance.IsModEnabled(Title) && MenuPrefs.GetInt(FogKey, 1) == 1;
            if (!active) { if (_applied) Restore(); return; }

            if (!_applied)
            {
                _applied = true; _nextUpdate = 0f;
                Activate();
            }

            if (Time.unscaledTime >= _nextUpdate) { _nextUpdate = Time.unscaledTime + UpdateInterval; Apply(); }
            UpdateSky();
            UpdateMaskMeshes();
        }

        // ---------- ciel (objets "sky") ----------
        // Le shader ne connaît que la depth, pas les noms d'objets. Le mod cherche donc les Renderers dont le nom, passé en minuscules,
        // contient "sky", calcule la distance du plus proche (dôme qui entoure la caméra ou objet éloigné) et la donne au shader
        // via _SkyDistance : tout ce qui est à cette distance ou au-delà compte comme ciel, donc la lumière passe.
        void UpdateSky()
        {
            if (Time.unscaledTime >= _nextSkyScan)
            {
                _nextSkyScan = Time.unscaledTime + 3f;
                var list = new List<Renderer>();
                foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r != null && r.enabled && r.gameObject.name.ToLowerInvariant().Contains("sky")) list.Add(r);
                _skyRenderers = list;
                ApplySkyQueue();
                if (list.Count != _lastSkyCount)
                {
                    _lastSkyCount = list.Count;
                    var names = new List<string>();
                    foreach (var r in list) names.Add(r.gameObject.name);
                    Log.Info("[VolFog] objets sky : " + list.Count + (list.Count > 0 ? " (" + string.Join(", ", names.ToArray()) + ")" : ""));
                }
            }

            float best = float.MaxValue;
            var head = Camera.main;
            if (head != null && _skyRenderers != null)
            {
                Vector3 cam = head.transform.position;
                foreach (var r in _skyRenderers)
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                    Bounds b = r.bounds;
                    float d;
                    if (b.Contains(cam))
                    {
                        // Dôme autour de la caméra : distance jusqu'à la paroi la plus proche.
                        Vector3 e = b.extents, o = cam - b.center;
                        d = Mathf.Max(e.x, Mathf.Max(e.y, e.z)) - o.magnitude;
                    }
                    else d = Mathf.Sqrt(b.SqrDistance(cam));
                    best = Mathf.Min(best, Mathf.Max(_minSkyDist, d));
                }
            }
            _autoSky = best;
        }

        // ---------- réglages par caméra ----------
        // Recompute the sky distance for each player, fly or spectator camera.
        void ApplyPerCamera(Camera cam)
        {
            if (_mat == null) return;
            Vector3 pos = cam.transform.position;

            // Le fog est un quad de la scène : une caméra ne le dessine que si sa couche est dans son culling mask (le filtre, lui, est un Volume
            // et n'a pas ce problème). Avant son rendu, le quad prend donc une couche que CETTE caméra affiche (ex. caméra LCK / fly cam).
            if (_quadGo != null && cam.cullingMask != 0 && (cam.cullingMask & (1 << _quadGo.layer)) == 0)
                for (int l = 0; l < 32; l++)
                    if ((cam.cullingMask & (1 << l)) != 0) { _quadGo.layer = l; break; }

            _mat.SetFloat("_SkyDistance", _skyByQueue ? 1e6f : Mathf.Min(_skyDist, SkyDistanceFrom(pos) * 0.97f));
        }

        // Les meshes "sky" sont passés dans la file de rendu transparente (> 2500) : URP copie la depth juste après les opaques,
        // donc le ciel n'y apparaît plus. Résultat : pixel sans depth = ciel (bleu), tout mesh opaque de la map = rouge. Restauré à la désactivation.
        void ApplySkyQueue()
        {
            if (!_skyByQueue) { RestoreSkyQueue(); return; }
            if (_skyRenderers == null) return;
            foreach (var r in _skyRenderers)
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || _queueBackup.ContainsKey(m) || m.renderQueue > 2500) continue;
                    _queueBackup[m] = m.renderQueue;
                    m.renderQueue = 2501;
                    Log.Info("[VolFog] sky '" + r.gameObject.name + "' materiau '" + m.name + "' queue " + _queueBackup[m] + " -> 2501");
                }
            }
        }

        // ---------- détecteur de meshes autour du joueur ----------
        // Toutes les secondes : liste des MeshRenderer à moins de _maskRadius mètres de la tête (les plus proches d'abord, max 600),
        // sans les meshes "sky" ni ceux qui ne projettent que de l'ombre. Chaque frame, ces meshes sont redessinés en rouge 50 % transparent :
        // le rouge = la partie de la map qui bloquera la lumière. Transparents inclus (contrairement à la depth).
        void ClearMaskCache() { _known.Clear(); _cache.Clear(); _matCache.Clear(); _maskItems.Clear(); _nextCacheRefresh = 0f; }

        void UpdateMaskMeshes()
        {
            if (!_inspectMeshes || _maskMat == null)
            {
                if (_maskItems.Count > 0 || _cache.Count > 0) ClearMaskCache();   // libère la mémoire quand le détecteur est coupé
                _pickR = null; return;
            }
            if (Time.unscaledTime >= _nextMaskScan) { _nextMaskScan = Time.unscaledTime + (_fovOnly ? 0.25f : 0.5f); ScanMaskMeshes(); }
            UpdatePick();

            int n = _maskItems.Count;
            // Revérification de l'état (parent coupé, objet désactivé...) par tranches de 64 items/frame au lieu de tout, chaque frame.
            for (int k = 0, slice = Mathf.Min(64, n); k < slice; k++)
            {
                _sliceCursor = (_sliceCursor + 1) % n;
                var it = _maskItems[_sliceCursor];
                it.Visible = StateOk(it, 0);
            }

            for (int i = 0; i < n; i++)
            {
                var it = _maskItems[i];
                if (!it.Visible || it.R == null) continue;
                Matrix4x4 m = it.R.localToWorldMatrix;
                Material mat = (it.R == _pickR && _pickMat != null) ? _pickMat : _maskMats[it.Bucket];
                // castShadows/receiveShadows/lightProbes = false : sinon chaque mesh rouge est aussi dessiné dans les shadow maps.
                for (int s = 0; s < it.Subs; s++) Graphics.DrawMesh(it.M, m, mat, it.Layer, null, s, null, false, false, false);
            }
        }

        MatInfo GetMatInfo(Material m)
        {
            MatInfo mi;
            if (_matCache.TryGetValue(m, out mi)) return mi;
            if (m.shader != null)
            {
                string sn = m.shader.name;
                bool hidden = sn.StartsWith("Hidden/") || sn.IndexOf("invisible", System.StringComparison.OrdinalIgnoreCase) >= 0;
                mi.Visible = !hidden && HasColorPass(m);
                mi.Alpha = MatAlpha(m);
            }
            _matCache[m] = mi;
            return mi;
        }

        // Analyse LOURDE, faite une seule fois par renderer (null = ne jamais l'afficher).
        MaskItem Analyze(MeshRenderer mr)
        {
            if (mr.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return null;
            if (mr.gameObject == _quadGo) return null;
            bool sky = mr.gameObject.name.IndexOf("sky", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (sky && !_showSky) return null;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null) return null;
            var mesh = mf.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0 || mesh.subMeshCount == 0) return null;
            if (IsGiantSphere(mr, mesh))
            {
                Log.Info("[VolFog] sphère à faces inversées ignorée (ciel) : '" + mr.gameObject.name + "' taille " + mr.bounds.size.ToString("0.0"));
                return null;
            }

            mr.GetSharedMaterials(_tmpMats);
            float best = -1f;
            for (int i = 0; i < _tmpMats.Count; i++)
            {
                if (_tmpMats[i] == null) continue;
                var mi = GetMatInfo(_tmpMats[i]);
                if (!mi.Visible || mi.Alpha < _alphaMin) continue;
                if (mi.Alpha > best) best = mi.Alpha;
            }
            if (best < 0f) return null;

            bool hid;
            var it = new MaskItem
            {
                R = mr, M = mesh, Subs = mesh.subMeshCount, Layer = mr.gameObject.layer, Alpha = best, Sky = sky,
                Bucket = Mathf.Clamp(Mathf.RoundToInt(best * (AlphaBuckets - 1)), 0, AlphaBuckets - 1),
                Parents = CollectParents(mr, out hid)
            };
            var lg = mr.GetComponentInParent<LODGroup>();
            if (lg != null)
            {
                var lods = lg.GetLODs(); int idx = -1;
                for (int i = 0; i < lods.Length && idx < 0; i++)
                    if (lods[i].renderers != null) foreach (var lr in lods[i].renderers) if (lr == mr) { idx = i; break; }
                if (idx >= 0)
                {
                    it.Lod = lg; it.LodIdx = idx; it.LodThresholds = new float[lods.Length];
                    for (int i = 0; i < lods.Length; i++) it.LodThresholds[i] = lods[i].screenRelativeTransitionHeight;
                }
            }
            return it;
        }

        // Un seul FindObjects toutes les 5 s ; les renderers déjà connus ne sont PAS ré-analysés.
        void RefreshMaskCache()
        {
            _cache.Clear();
            if (_known.Count > 20000) _known.Clear();   // évite que le dico grossisse avec les objets détruits
            foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (mr == null) continue;
                int id = mr.GetInstanceID();
                MaskItem it;
                if (!_known.TryGetValue(id, out it)) { it = Analyze(mr); _known[id] = it; }
                if (it != null) _cache.Add(it);
            }
        }

        // Contrôles d'état bon marché (pas d'allocation).
        static bool StateOk(MaskItem it, int camMask)
        {
            var mr = it.R;
            if (mr == null || it.M == null) return false;
            if (!mr.enabled || mr.forceRenderingOff || !mr.gameObject.activeInHierarchy) return false;
            if (camMask != 0 && (camMask & (1 << it.Layer)) == 0) return false;
            return !ParentsHidden(it.Parents);
        }

        // Vrai si le mesh est dans un LODGroup dont le niveau actif (à la distance de la tête) n'est PAS celui de ce renderer :
        // le renderer est "enabled" mais Unity ne le dessine pas. Même calcul que Unity (taille relative à l'écran * lodBias).
        static bool LodCulled(MaskItem it, Camera head)
        {
            var lg = it.Lod;
            if (lg == null || !lg.enabled || head.orthographic) return false;
            Vector3 sc = lg.transform.lossyScale;
            float size = lg.size * Mathf.Max(sc.x, Mathf.Max(sc.y, sc.z));
            float dist = Vector3.Distance(lg.transform.TransformPoint(lg.localReferencePoint), head.transform.position);
            float rel = size * QualitySettings.lodBias / Mathf.Max(0.0001f, dist * 2f * Mathf.Tan(head.fieldOfView * 0.5f * Mathf.Deg2Rad));
            int active = -1;
            for (int i = 0; i < it.LodThresholds.Length; i++) if (rel >= it.LodThresholds[i]) { active = i; break; }
            return active != it.LodIdx; // active = -1 : tout le groupe est coupé (trop loin)
        }

        // Faux si le shader n'a AUCUNE passe qui dessine de la couleur (seulement ShadowCaster / DepthOnly / Meta...) : mesh invisible à l'écran.
        static readonly ShaderTagId LightModeTag = new ShaderTagId("LightMode");
        static readonly ShaderTagId[] NoColorModes =
        {
            new ShaderTagId("ShadowCaster"), new ShaderTagId("DepthOnly"), new ShaderTagId("DepthNormals"),
            new ShaderTagId("Meta"), new ShaderTagId("MotionVectors"), new ShaderTagId("Universal2D"),
        };
        static bool HasColorPass(Material m)
        {
            int n = m.passCount;
            if (n <= 0) return true;
            for (int i = 0; i < n; i++)
            {
                var t = m.shader.FindPassTagValue(i, LightModeTag);
                if (t == ShaderTagId.none) return true; // pas de LightMode = passe par défaut, dessine
                bool noColor = false;
                for (int k = 0; k < NoColorModes.Length; k++) if (t == NoColorModes[k]) { noColor = true; break; }
                if (!noColor) return true;
            }
            return false;
        }

        // Opacité réelle d'un matériau. Les matériaux opaques / cutout (queue < 2500) ignorent l'alpha de leur couleur : ils comptent pour 1.
        // Sinon on lit l'alpha de la première propriété couleur connue (URP _BaseColor, Standard _Color, particules _TintColor...).
        static readonly string[] AlphaProps = { "_BaseColor", "_Color", "_TintColor", "_MainColor" };
        static float MatAlpha(Material m)
        {
            if (m.renderQueue < 2500) return 1f;
            for (int i = 0; i < AlphaProps.Length; i++)
                if (m.HasProperty(AlphaProps[i])) return Mathf.Clamp01(m.GetColor(AlphaProps[i]).a);
            return 1f;
        }

        void ScanMaskMeshes()
        {
            _maskItems.Clear();
            var head = Camera.main;
            if (head == null) return;
            if (Time.unscaledTime >= _nextCacheRefresh) { _nextCacheRefresh = Time.unscaledTime + (_biggestOnly ? 10f : 5f); RefreshMaskCache(); }

            Vector3 c = head.transform.position;
            float r2 = _maskRadius * _maskRadius;
            int camMask = head.cullingMask, rejFov = 0, rejState = 0, rejLod = 0;
            if (_fovOnly) GeometryUtility.CalculateFrustumPlanes(head, _planes);

            for (int i = 0; i < _cache.Count; i++)
            {
                var it = _cache[i];
                var mr = it.R;
                if (mr == null) continue;
                Bounds b = mr.bounds;
                float d2 = b.SqrDistance(c);
                if (d2 > r2) continue;                                    // le plus bon marché d'abord
                if (b.size.sqrMagnitude < 1e-6f) { rejState++; continue; }
                if (_fovOnly) { b.Expand(2f); if (!GeometryUtility.TestPlanesAABB(_planes, b)) { rejFov++; continue; } }
                if (!StateOk(it, camMask)) { rejState++; continue; }
                if (LodCulled(it, head)) { rejLod++; continue; }
                it.Dist = d2; it.Visible = true;
                _maskItems.Add(it);
            }
            if (_biggestOnly) KeepBiggest(head);
            _maskItems.Sort(ByDist);
            if (_maskItems.Count > MaxMaskItems) _maskItems.RemoveRange(MaxMaskItems, _maskItems.Count - MaxMaskItems);

            int sig = _maskItems.Count * 31 + rejFov * 7 + rejState * 13 + rejLod * 17;   // la string n'est construite que si ça change
            if (sig != _lastMaskSig)
            {
                _lastMaskSig = sig;
                Log.Info("[VolFog] scan masque : " + _maskItems.Count + " meshes | hors champ=" + rejFov + " état=" + rejState + " LOD=" + rejLod);
            }
        }

        // ---------- sphères géantes = ciel ----------
        // Les dômes du ciel sont des icosphères / demi-sphères énormes. Test en 3 temps, fait une seule fois par mesh :
        //  1) bounds : diamètre horizontal >= _skySphereMin, rond vu de dessus (x ~ z), hauteur ~ diamètre (sphère) ou ~ diamètre/2 (demi-sphère) ;
        //  2) sommets : au moins 80 % à ±10 % du rayon d'un centre candidat (centre des bounds, ou milieu de la base / du sommet pour une demi-sphère) ;
        //  3) faces : au moins 80 % des triangles ont leur normale (d'après l'ordre des sommets, signe du scale pris en compte) tournée VERS ce centre.
        //     Une sphère normale (planète, boule de map) a ses faces vers l'extérieur et n'est jamais retirée, même géante.
        //  Mesh illisible (pas de Read/Write) : on ne peut pas vérifier, on ne retire que s'il fait 100 m ou plus.
        static bool IsGiantSphere(MeshRenderer mr, Mesh mesh, float minSize)
        {
            if (minSize <= 10f) minSize = 10f;
            Bounds b = mr.bounds; Vector3 s = b.size;
            float horiz = Mathf.Max(s.x, s.z);
            if (horiz < minSize) return false;
            if (Mathf.Min(s.x, s.z) < horiz * 0.75f) return false;
            float h = s.y / horiz;
            if (h < 0.35f || h > 1.3f) return false;
            if (!mesh.isReadable) return horiz >= 100f;
            var v = mesh.vertices;
            if (v.Length < 8) return false;
            int step = Mathf.Max(1, v.Length / 300);
            float r = horiz * 0.5f;
            Vector3 c = b.center;
            Vector3[] centers = { c, new Vector3(c.x, b.max.y, c.z), new Vector3(c.x, b.min.y, c.z) };
            for (int k = 0; k < centers.Length; k++)
                if (SphereFit(mr.transform, v, step, centers[k], r))
                    return FacesInward(mr.transform, v, mesh.triangles, centers[k]);
            return false;
        }

        // Vrai si la majorité des triangles regardent vers le centre (sphère retournée). Unity : normale = Cross(b-a, c-a) pour un
        // triangle horaire ; un scale négatif (nombre impair d'axes) inverse l'ordre une fois transformé, d'où le signe.
        static bool FacesInward(Transform tr, Vector3[] v, int[] t, Vector3 center)
        {
            Vector3 ls = tr.lossyScale;
            float sign = (ls.x * ls.y * ls.z) < 0f ? -1f : 1f;
            int triCount = t.Length / 3, step = Mathf.Max(1, triCount / 200), inward = 0, n = 0;
            for (int i = 0; i < triCount; i += step)
            {
                Vector3 a = tr.TransformPoint(v[t[i * 3]]), b = tr.TransformPoint(v[t[i * 3 + 1]]), c = tr.TransformPoint(v[t[i * 3 + 2]]);
                Vector3 nrm = Vector3.Cross(b - a, c - a) * sign;
                Vector3 toCenter = center - (a + b + c) / 3f;
                float nl = nrm.magnitude, tl = toCenter.magnitude;
                if (nl < 1e-9f || tl < 1e-6f) continue;
                if (Vector3.Dot(nrm / nl, toCenter / tl) > 0.2f) inward++;
                n++;
            }
            return n > 0 && inward >= n * 0.8f;
        }

        bool IsGiantSphere(MeshRenderer mr, Mesh mesh) { return IsGiantSphere(mr, mesh, _skySphereMin); }

        static bool SphereFit(Transform tr, Vector3[] v, int step, Vector3 center, float r)
        {
            int ok = 0, n = 0;
            for (int i = 0; i < v.Length; i += step)
            {
                float d = (tr.TransformPoint(v[i]) - center).magnitude;
                if (Mathf.Abs(d - r) < r * 0.1f) ok++;
                n++;
            }
            return n > 0 && ok >= n * 0.8f;
        }

        // ---------- plus gros objet à l'écran ----------
        // Parmi les meshes déjà filtrés (rayon, champ de vision, état, LOD), on garde celui qui couvre le plus de surface d'écran,
        // plus les meshes dont les bounds le touchent (morceaux qui dépassent / pièces collées). Le reste n'est ni dessiné ni suivi.
        const float TouchMargin = 0.5f;
        const int MaxTouching = 64;
        static readonly Vector3[] _corners = new Vector3[8];
        static readonly Vector3[] _clipPts = new Vector3[24];
        static readonly int[] _edgeA = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
        static readonly int[] _edgeB = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };
        static readonly List<MaskItem> _keep = new List<MaskItem>(MaxTouching + 1);

        void KeepBiggest(Camera head)
        {
            if (_maskItems.Count <= 1) return;
            MaskItem biggest = null; float bestArea = 0f;
            for (int i = 0; i < _maskItems.Count; i++)
            {
                var it = _maskItems[i];
                if (it.R == null || it.Sky) continue;   // le ciel ne compte pas pour le "plus gros"
                float a = ScreenCoverage(head, it.R.bounds);
                if (a > bestArea) { bestArea = a; biggest = it; }
            }
            _keep.Clear();
            if (biggest != null)
            {
                _keep.Add(biggest);
                Bounds zone = biggest.R.bounds; zone.Expand(TouchMargin * 2f);
                for (int i = 0; i < _maskItems.Count && _keep.Count <= MaxTouching; i++)
                {
                    var it = _maskItems[i];
                    if (it == biggest || it.R == null || it.Sky) continue;
                    if (zone.Intersects(it.R.bounds)) _keep.Add(it);
                }
            }
            for (int i = 0; i < _maskItems.Count; i++)   // les objets sky restent toujours affichés
                if (_maskItems[i].Sky && _maskItems[i].R != null) _keep.Add(_maskItems[i]);
            _maskItems.Clear();
            _maskItems.AddRange(_keep);
        }

        // Part de l'écran (0-1) couverte par les bounds : les 8 coins sont passés dans l'espace caméra, coupés au plan proche
        // (sinon un coin derrière la tête donne des coordonnées inversées), projetés, puis limités à l'écran.
        static float ScreenCoverage(Camera cam, Bounds b)
        {
            Vector3 c = b.center, e = b.extents;
            Matrix4x4 w2c = cam.worldToCameraMatrix;
            for (int i = 0; i < 8; i++)
                _corners[i] = w2c.MultiplyPoint3x4(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
            float zn = -cam.nearClipPlane;   // la caméra regarde vers -Z en espace caméra
            int n = 0;
            for (int i = 0; i < 8; i++) if (_corners[i].z <= zn) _clipPts[n++] = _corners[i];
            for (int k = 0; k < 12; k++)
            {
                Vector3 p0 = _corners[_edgeA[k]], p1 = _corners[_edgeB[k]];
                if ((p0.z <= zn) != (p1.z <= zn))
                    _clipPts[n++] = Vector3.Lerp(p0, p1, (zn - p0.z) / (p1.z - p0.z));
            }
            if (n == 0) return 0f;
            Matrix4x4 proj = cam.projectionMatrix;
            float minX = 2f, minY = 2f, maxX = -2f, maxY = -2f;
            for (int i = 0; i < n; i++)
            {
                Vector4 v = proj * new Vector4(_clipPts[i].x, _clipPts[i].y, _clipPts[i].z, 1f);
                if (v.w <= 1e-5f) continue;
                float x = Mathf.Clamp(v.x / v.w, -1f, 1f), y = Mathf.Clamp(v.y / v.w, -1f, 1f);
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            if (maxX < minX || maxY < minY) return 0f;
            return (maxX - minX) * (maxY - minY) * 0.25f;
        }

        // ---------- détecteur de visée ----------
        // Regarde un objet rouge : le détecteur prend le point touché au centre de la vue (raycast physique, sinon rayon contre les bounds)
        // et liste les meshes du masque qui contiennent ce point, du plus petit (le plus précis) au plus grand. Le curseur "cible" choisit
        // lequel inspecter ; il passe en jaune, et le menu + le log donnent son chemin (avec [OFF] sur les parents coupés), son layer,
        // ses matériaux, son éventuel LODGroup, etc.
        Renderer _pickR, _lastPickR;
        bool _pickOn = true;
        float _pickIndex;
        string _pickHead = "", _pickInfo = "";
        struct PickCand { public MaskItem It; public float Key; }
        static readonly System.Comparison<PickCand> ByKey = (x, y) => x.Key.CompareTo(y.Key);
        readonly List<PickCand> _pickCands = new List<PickCand>();

        void UpdatePick()
        {
            _pickR = null;
            if (!_pickOn) { _pickHead = ""; _pickInfo = ""; _lastPickR = null; return; }
            var head = Camera.main;
            if (head == null) return;
            Ray ray = new Ray(head.transform.position, head.transform.forward);
            RaycastHit rh;
            bool havePt = Physics.Raycast(ray, out rh, _maskRadius, ~0, QueryTriggerInteraction.Ignore);
            _pickCands.Clear();
            for (int i = 0; i < _maskItems.Count; i++)
            {
                var it = _maskItems[i];
                if (it.R == null) continue;
                Bounds b = it.R.bounds; b.Expand(0.1f);
                float key;
                if (havePt) { if (!b.Contains(rh.point)) continue; Vector3 sz = b.size; key = sz.x * sz.y * sz.z; }
                else { float dd; if (!b.IntersectRay(ray, out dd)) continue; key = dd; }
                _pickCands.Add(new PickCand { It = it, Key = key });
            }
            if (_pickCands.Count == 0)
            {
                _pickHead = "Aucun mesh du masque au centre de la vue";
                _pickInfo = havePt ? "Collider touché : " + rh.collider.name + " (pas de mesh du masque à cet endroit)" : "";
                _lastPickR = null;
                return;
            }
            _pickCands.Sort(ByKey);
            int idx = Mathf.Clamp(Mathf.RoundToInt(_pickIndex), 0, _pickCands.Count - 1);
            var r = _pickCands[idx].It.R;
            _pickR = r;
            _pickHead = "Cible " + (idx + 1) + "/" + _pickCands.Count + " (curseur = autre candidat)";
            if (r != _lastPickR)
            {
                _lastPickR = r;
                _pickInfo = DescribeRenderer(r);
                Log.Info("[VolFog] détecteur : " + _pickInfo.Replace("\n", " | "));
            }
        }

        static string DescribeRenderer(Renderer r)
        {
            var sb = new System.Text.StringBuilder();
            var chain = new List<string>();
            for (Transform t = r.transform; t != null; t = t.parent)
            {
                var go = t.gameObject;
                string tag = go.activeSelf ? "" : "[OFF]";
                if (go != r.gameObject)
                {
                    var pr = go.GetComponent<Renderer>();
                    if (pr != null) tag += (pr.enabled && !pr.forceRenderingOff) ? "(rend)" : "(rend OFF)";
                }
                chain.Add(go.name + tag);
            }
            chain.Reverse();
            sb.Append("Chemin : ").Append(string.Join("/", chain.ToArray()));
            sb.Append("\nLayer : ").Append(LayerMask.LayerToName(r.gameObject.layer)).Append(" (").Append(r.gameObject.layer).Append(")");
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) sb.Append(" | mesh '").Append(mf.sharedMesh.name).Append("' ").Append(mf.sharedMesh.vertexCount).Append(" v");
            sb.Append(" | taille ").Append(r.bounds.size.ToString("0.0"));
            sb.Append("\nOmbre : ").Append(r.shadowCastingMode).Append(" | collider : ").Append(r.GetComponent<Collider>() != null ? "oui" : "non");
            sb.Append("\nVisible par une caméra : ").Append(r.isVisible ? "oui" : "NON (LOD coupé, hors champ ou occlus)");
            var lg = r.GetComponentInParent<LODGroup>();
            if (lg != null)
            {
                int lodIdx = -1;
                var lods = lg.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                    if (lods[i].renderers != null) foreach (var lr in lods[i].renderers) if (lr == r) lodIdx = i;
                sb.Append(" | LODGroup '").Append(lg.name).Append("' niveau ").Append(lodIdx).Append("/").Append(lods.Length);
            }
            var comps = r.GetComponents<Component>();
            var names = new List<string>();
            foreach (var c in comps) if (c != null) names.Add(c.GetType().Name);
            sb.Append("\nComposants : ").Append(string.Join(", ", names.ToArray()));
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) { sb.Append("\nMat ").Append(i).Append(" : (aucun)"); continue; }
                sb.Append("\nMat ").Append(i).Append(" : ").Append(m.name).Append(" | shader ").Append(m.shader != null ? m.shader.name : "?").Append(" | queue ").Append(m.renderQueue);
            }
            return sb.ToString();
        }

        static readonly List<Renderer> _tmpParents = new List<Renderer>();

        // Renderers des ancêtres (sans le mesh lui-même, objets inactifs inclus). hidden = au moins un ancêtre a son renderer coupé.
        static Renderer[] CollectParents(Renderer self, out bool hidden)
        {
            hidden = false;
            _tmpParents.Clear();
            self.GetComponentsInParent(true, _tmpParents);
            var res = new List<Renderer>(_tmpParents.Count);
            for (int i = 0; i < _tmpParents.Count; i++)
            {
                var p = _tmpParents[i];
                if (p == null || p == self) continue;
                if (!p.enabled || p.forceRenderingOff) hidden = true;
                res.Add(p);
            }
            return res.ToArray();
        }

        static bool ParentsHidden(Renderer[] parents)
        {
            if (parents == null) return false;
            for (int i = 0; i < parents.Length; i++)
            {
                var p = parents[i];
                if (p != null && (!p.enabled || p.forceRenderingOff)) return true;
            }
            return false;
        }

        void RestoreSkyQueue()
        {
            foreach (var kv in _queueBackup) if (kv.Key != null) kv.Key.renderQueue = kv.Value;
            _queueBackup.Clear();
        }

        // Distance depuis une position jusqu'au ciel "sky" le plus proche (float.MaxValue s'il n'y en a pas).
        float SkyDistanceFrom(Vector3 cam)
        {
            float best = float.MaxValue;
            if (_skyRenderers == null) return best;
            foreach (var r in _skyRenderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                float d;
                if (b.Contains(cam))
                {
                    Vector3 e = b.extents, o = cam - b.center;
                    d = Mathf.Max(e.x, Mathf.Max(e.y, e.z)) - o.magnitude;
                }
                else d = Mathf.Sqrt(b.SqrDistance(cam));
                best = Mathf.Min(best, Mathf.Max(_minSkyDist, d));
            }
            return best;
        }

        void OnDisable() { if (_applied) Restore(); }
        void OnDestroy() { if (_applied) Restore(); }

        // ---------- activation / restauration ----------
        void Activate()
        {
            LoadBundle();
            BuildQuad();
            if (!_hooked) { RenderPipelineManager.beginCameraRendering += OnBeginCamera; _hooked = true; }
            Log.Info("[VolFog] Actif. VR=" + UnityEngine.XR.XRSettings.isDeviceActive + " shader_ok=" + (_mat != null));
            Apply();
        }

        void Restore()
        {
            _applied = false;
            if (_hooked) { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; _hooked = false; }
            RestoreSkyQueue();
            RestoreCameras();
            foreach (var entry in _depthCopyModes)
                if (Equals(DepthCopyField.GetValue(entry.Key), URP.CopyDepthMode.AfterOpaques)) DepthCopyField.SetValue(entry.Key, entry.Value);
            _depthCopyModes.Clear();
            if (_quadGo != null) Destroy(_quadGo); _quadGo = null;
            _fogRenderer = null;
            if (_quadMesh != null) Destroy(_quadMesh); _quadMesh = null;
            if (_mat != null) Destroy(_mat); _mat = null;
            for (int i = 0; i < _maskMats.Length; i++)
            {
                if (_maskMats[i] != null) Destroy(_maskMats[i]);
                _maskMats[i] = null;
            }
            _maskMat = null;
            ClearMaskCache();
            if (_pickMat != null) Destroy(_pickMat); _pickMat = null;
            _pickR = null; _lastPickR = null;
            _maskItems.Clear();
            if (_bundle != null) { _bundle.Unload(true); _bundle = null; }
        }

        // The fog quad is transparent. URP may otherwise copy depth only after it has rendered.
        static readonly System.Reflection.FieldInfo DepthCopyField = typeof(URP.UniversalRenderer).GetField("m_CopyDepthMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        readonly Dictionary<URP.UniversalRenderer, URP.CopyDepthMode> _depthCopyModes = new Dictionary<URP.UniversalRenderer, URP.CopyDepthMode>();
        void EnsureEarlyDepth(URP.ScriptableRenderer renderer)
        {
            if (!(renderer is URP.UniversalRenderer forward) || DepthCopyField == null || _depthCopyModes.ContainsKey(forward)) return;
            if (DepthCopyField.GetValue(forward) is URP.CopyDepthMode mode && mode == URP.CopyDepthMode.AfterTransparents)
            {
                DepthCopyField.SetValue(forward, URP.CopyDepthMode.AfterOpaques);
                _depthCopyModes.Add(forward, mode);
            }
        }

        // ---------- caméras : depth texture sur toutes les caméras (écran, VR, spectateur...) ----------
        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!_applied || _fogRenderer == null || cam == null) return;
            var data = cam.cameraType == CameraType.Game ? URP.CameraExtensions.GetUniversalAdditionalCameraData(cam) : null;
            _fogRenderer.forceRenderingOff = data == null || data.renderType == URP.CameraRenderType.Overlay;
            if (_fogRenderer.forceRenderingOff) return;
            SetStereoKeyword(cam.stereoEnabled && UnityEngine.XR.XRSettings.stereoRenderingMode == UnityEngine.XR.XRSettings.StereoRenderingMode.SinglePassInstanced);
            int id = cam.GetInstanceID();
            if (_cams.Add(id))
            {
                VisualCameraDepth.Acquire(id, data);
                Log.Info("[VolFog] caméra '" + cam.name + "' stereo=" + cam.stereoEnabled + " rt=" + (cam.targetTexture != null) + " depth(avant)=" + data.requiresDepthTexture + " mask=" + cam.cullingMask + " pos=" + cam.transform.position);
            }
            EnsureEarlyDepth(data.scriptableRenderer);
            if (!data.requiresDepthTexture) data.requiresDepthTexture = true;
            ApplyPerCamera(cam);
        }

        void SetStereoKeyword(bool on)
        {
            if (_mat == null || on == _stereoKeyword) return;
            _stereoKeyword = on;
            if (on) _mat.EnableKeyword("UM_STEREO"); else _mat.DisableKeyword("UM_STEREO");
            for (int i = 0; i < _maskMats.Length; i++)
                if (_maskMats[i] != null) { if (on) _maskMats[i].EnableKeyword("UM_STEREO"); else _maskMats[i].DisableKeyword("UM_STEREO"); }
            if (_pickMat != null) { if (on) _pickMat.EnableKeyword("UM_STEREO"); else _pickMat.DisableKeyword("UM_STEREO"); }
        }

        void RestoreCameras()
        {
            foreach (int id in _cams) VisualCameraDepth.Release(id);
            _cams.Clear();
        }

        // ---------- bundle + quad plein écran ----------
        void LoadBundle()
        {
            if (_bundle != null) return;
            try
            {
                var asm = typeof(GorillaShader).Assembly;
                string res = System.Linq.Enumerable.FirstOrDefault(asm.GetManifestResourceNames(),
                    n => n.EndsWith(BundleSuffix, System.StringComparison.OrdinalIgnoreCase));
                if (res == null) { Log.Info("[VolFog] " + BundleSuffix + " introuvable dans les ressources (build le bundle dans Unity puis recompile le mod)."); return; }
                byte[] data;
                using (var s = asm.GetManifestResourceStream(res))
                using (var ms = new System.IO.MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                _bundle = AssetBundle.LoadFromMemory(data);
                if (_bundle == null) { Log.Info("[VolFog] LoadFromMemory a renvoyé null."); return; }
                Log.Info("[VolFog] Bundle chargé : " + string.Join(", ", _bundle.GetAllAssetNames()));

                // Matériau du bundle si présent, sinon on en crée un à partir du premier shader trouvé.
                var template = _bundle.LoadAsset<Material>(MatName);
                if (template != null) _mat = new Material(template);
                else
                {
                    Shader found = null;
                    foreach (var sh in _bundle.LoadAllAssets<Shader>()) { if (sh.name == "UltimateMenu/VolumetricFog") { found = sh; break; } }
                    if (found == null) { Log.Info("[VolFog] Ni matériau '" + MatName + "' ni shader dans le bundle."); return; }
                    _mat = new Material(found);
                }
                // Overlay rouge 50 % du détecteur de meshes (shader du bundle, sinon Sprites/Default).
                Shader overlay = null;
                foreach (var sh in _bundle.LoadAllAssets<Shader>()) if (sh.name == "UltimateMenu/MaskOverlay") { overlay = sh; break; }
                if (overlay == null) { overlay = Shader.Find("Sprites/Default"); Log.Info("[VolFog] MaskOverlay absent du bundle : fallback " + (overlay != null ? overlay.name : "aucun")); }
                if (overlay != null)
                {
                    for (int b = 0; b < AlphaBuckets; b++)
                    {
                        var mm = new Material(overlay) { enableInstancing = true };
                        mm.SetColor("_Color", new Color(1f, 0f, 0f, Mathf.Lerp(0.1f, 0.5f, b / (float)(AlphaBuckets - 1))));
                        _maskMats[b] = mm;
                    }
                    _maskMat = _maskMats[AlphaBuckets - 1];
                    // Même overlay mais jaune presque opaque : sert au détecteur de visée (mesh visé).
                    _pickMat = new Material(overlay) { enableInstancing = true };
                    _pickMat.SetColor("_Color", new Color(1f, 0.9f, 0f, 0.85f));
                }
                _shaderIsNew = _mat.HasProperty("_SunDir");
                _stereoKeyword = false;
                Log.Info("[VolFog] Shader : " + _mat.shader.name + " (supporté : " + _mat.shader.isSupported + ", version rayons écran : " + _shaderIsNew + ")");
                if (!_shaderIsNew) Log.Info("[VolFog] Ancien shader dans le bundle : rebuild gorillavolumetric.bundle avec GorillaVolumetric.shader.");
            }
            catch (System.Exception e) { Log.Err("VolFog bundle", e); }
        }

        void BuildQuad()
        {
            if (_mat == null || _quadGo != null) return;
            // Quad dont l'UV (0..1) sert de position clip dans le vertex shader => il couvre tout l'écran de chaque caméra.
            _quadMesh = new Mesh { name = "GorillaVolQuad" };
            _quadMesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
            _quadMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            _quadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f); // jamais éliminé par le frustum culling

            _quadGo = new GameObject("GorillaShader_Volumetric");
            DontDestroyOnLoad(_quadGo);
            _quadGo.AddComponent<MeshFilter>().sharedMesh = _quadMesh;
            var mr = _fogRenderer = _quadGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;
            Log.Info("[VolFog] Quad plein écran créé.");
        }

        void Apply()
        {
            if (_mat != null)
            {
                // Direction vers le soleil = opposée à la direction de la lumière (même convention qu'une Directional Light).
                Vector3 towardsSun = -(Quaternion.Euler(_sunPitch, _sunYaw, 0f) * Vector3.forward);
                _mat.SetColor("_FogColor", _fogColor);
                _mat.SetFloat("_Density", _density);
                _mat.SetFloat("_Scattering", _scatter);
                _mat.SetFloat("_Anisotropy", _aniso);
                _mat.SetFloat("_Ambient", _ambientFog ? _ambient : 0f);
                _mat.SetFloat("_Outdoor", 1f);
                _mat.SetFloat("_MaxDistance", _maxDist);
                _mat.SetFloat("_SkyDistance", _skyByQueue ? 1e6f : Mathf.Min(_skyDist, _autoSky * 0.97f));
                _mat.SetFloat("_Steps", Mathf.Round(_steps));
                _mat.SetFloat("_StreakStrength", _streakStr);
                _mat.SetFloat("_DebugMask", _debugMode);
                _mat.SetFloat("_SkyThreshold", _maskThreshold);
                _mat.SetFloat("_StreakCount", Mathf.Round(_streakCount));
                _mat.SetFloat("_ShaftFocus", _shaftFocus);
                _mat.SetFloat("_SunRadius", 1.1f * Mathf.Deg2Rad);
                _mat.SetVector("_SunDir", new Vector4(towardsSun.x, towardsSun.y, towardsSun.z, 0f));
                _mat.SetColor("_SunColor", _sunColor);
                _mat.SetFloat("_SunIntensity", _sunIntensity);
            }
        }

        // ---------- menu ----------
        bool SliderF(MenuUI ui, string label, ref float value, float min, float max, string fmt, string key)
        {
            float v = ui.Slider(label, value, min, max, fmt);
            if (Mathf.Approximately(v, value)) return false;
            value = v; MenuPrefs.SetFloat(P + key, v); MenuPrefs.Save();
            return true;
        }

        bool ToggleB(MenuUI ui, string label, ref bool value, string key)
        {
            bool v = ui.Toggle(label, value);
            if (v == value) return false;
            value = v; MenuPrefs.SetInt(P + key, v ? 1 : 0); MenuPrefs.Save();
            return true;
        }

        void ColorPick(MenuUI ui, string label, ref Color c, string key)
        {
            Color before = c;
            ui.ColorPickerHSV(ref c, label, compact: true);
            if (Mathf.Approximately(before.r, c.r) && Mathf.Approximately(before.g, c.g) && Mathf.Approximately(before.b, c.b)) return;
            MenuPrefs.SetFloat(P + key + "R", c.r); MenuPrefs.SetFloat(P + key + "G", c.g); MenuPrefs.SetFloat(P + key + "B", c.b);
            MenuPrefs.Save();
        }

        void ApplyLook(int look)
        {
            _density = look == 1 ? .022f : look == 2 ? .029f : .013f;
            _scatter = look == 1 ? .48f : look == 2 ? .32f : .29f;
            _ambient = .10f;
            _ambientFog = look == 2;
            _aniso = .6f;
            _sunIntensity = look == 1 ? 1.85f : look == 2 ? 1.2f : 1.4f;
            _fogColor = look == 1 ? new Color(.96f,.68f,.4f) : look == 2 ? new Color(.43f,.62f,.88f) : new Color(.72f,.77f,.83f);
            _sunColor = look == 1 ? new Color(1f,.94f,.85f) : look == 2 ? new Color(.96f,.98f,1f) : new Color(1f,.98f,.94f);
            SaveFogLook();
            MenuPrefs.SetInt(FogKey, 1); MenuPrefs.SetInt(PostFxKey, 1);
            MenuPrefs.SetInt("MUM_ModEnabled_" + Title, 1);
            var post = GorillaFilter.Instance;
            if (post != null) post.ApplyLook(look);
            MenuPrefs.Save();
            Apply();
        }

        [System.Serializable]
        sealed class FogLook
        {
            public float density, scatter, ambient, aniso, intensity, pitch, yaw;
            public Color fog, sun;
            public bool veil, fogEnabled, postEnabled;
        }

        internal string CaptureFogLook() => JsonUtility.ToJson(new FogLook
        {
            density = _density, scatter = _scatter, ambient = _ambient, aniso = _aniso,
            intensity = _sunIntensity, pitch = _sunPitch, yaw = _sunYaw, fog = _fogColor, sun = _sunColor,
            veil = _ambientFog, fogEnabled = MenuPrefs.GetInt(FogKey, 1) == 1, postEnabled = MenuPrefs.GetInt(PostFxKey, 1) == 1
        });

        internal bool RestoreFogLook(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 4096) return false;
            FogLook saved;
            try { saved = JsonUtility.FromJson<FogLook>(json); }
            catch (System.ArgumentException) { return false; }
            if (saved == null) return false;
            _density = Mathf.Clamp(saved.density, .001f, .1f); _scatter = Mathf.Clamp(saved.scatter, 0f, 2f);
            _ambient = Mathf.Clamp01(saved.ambient); _aniso = Mathf.Clamp(saved.aniso, 0f, .95f);
            _sunIntensity = Mathf.Clamp(saved.intensity, 1f, 10f); _sunPitch = Mathf.Clamp(saved.pitch, 5f, 90f);
            _sunYaw = Mathf.Clamp(saved.yaw, -180f, 180f); _fogColor = saved.fog; _sunColor = saved.sun;
            _ambientFog = saved.veil;
            MenuPrefs.SetInt(FogKey, saved.fogEnabled ? 1 : 0);
            MenuPrefs.SetInt(PostFxKey, saved.postEnabled ? 1 : 0);
            SaveFogLook(); Apply();
            return true;
        }

        void SaveFogLook()
        {
            MenuPrefs.SetInt(P + "AmbientFog", _ambientFog ? 1 : 0);
            MenuPrefs.SetFloat(P + "SunPitch", _sunPitch); MenuPrefs.SetFloat(P + "SunYaw", _sunYaw);
            MenuPrefs.SetFloat(P + "Density", _density); MenuPrefs.SetFloat(P + "Scatter", _scatter);
            MenuPrefs.SetFloat(P + "Ambient", _ambient); MenuPrefs.SetFloat(P + "Aniso", _aniso);
            MenuPrefs.SetFloat(P + "SunInt", _sunIntensity);
            MenuPrefs.SetFloat(P + "FogR", _fogColor.r); MenuPrefs.SetFloat(P + "FogG", _fogColor.g); MenuPrefs.SetFloat(P + "FogB", _fogColor.b);
            MenuPrefs.SetFloat(P + "SunR", _sunColor.r); MenuPrefs.SetFloat(P + "SunG", _sunColor.g); MenuPrefs.SetFloat(P + "SunB", _sunColor.b);
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Shader.Looks"));
            ui.BeginCard();
            if (ui.AmbianceButton(I18n.T("Shader.Natural"), I18n.T("Shader.NaturalDesc"), new Color(.56f,.78f,.68f))) ApplyLook(0);
            ui.Space(4);
            if (ui.AmbianceButton(I18n.T("Shader.Golden"), I18n.T("Shader.GoldenDesc"), new Color(1f,.65f,.25f))) ApplyLook(1);
            ui.Space(4);
            if (ui.AmbianceButton(I18n.T("Shader.Blue"), I18n.T("Shader.BlueDesc"), new Color(.42f,.66f,.98f))) ApplyLook(2);
            ui.EndCard();
            var post = GorillaFilter.Instance;
            if (post != null) post.DrawPresets(ui, this);
            ui.BeginCard();
            DrawEffectToggle(ui, "Shader.FogToggle", FogKey);
            DrawEffectToggle(ui, "Shader.PostToggle", PostFxKey);
            ui.EndCard();
            ui.ExpandableSection(I18n.T("Shader.FogToggle"), "", ref _fogOpen, () => DrawFog(ui));
            if (post != null) ui.ExpandableSection(I18n.T("Shader.PostToggle"), "", ref _postFxOpen, () => post.DrawControls(ui));
        }

        static void DrawEffectToggle(MenuUI ui, string label, string key)
        {
            bool before = MenuPrefs.GetInt(key, 1) == 1;
            bool after = ui.Toggle(I18n.T(label), before);
            if (before != after) { MenuPrefs.SetInt(key, after ? 1 : 0); MenuPrefs.Save(); }
        }

        void DrawFog(MenuUI ui)
        {
            ui.Section(I18n.T("Shader.Section"));
            ui.BeginCard();
            ToggleB(ui, I18n.T("Shader.AmbientFog"), ref _ambientFog, "AmbientFog");
            SliderF(ui, I18n.T("Shader.Density"), ref _density, 0.001f, 0.1f, "0.000", "Density");
            if (_ambientFog) ColorPick(ui, I18n.T("Shader.FogColor"), ref _fogColor, "Fog");
            ui.EndCard();

            ui.Space(6);
            ui.Section(I18n.T("Shader.SunSection"));
            ui.BeginCard();
            SliderF(ui, I18n.T("Shader.SunInt"), ref _sunIntensity, 1f, 10f, "0.0", "SunInt");
            SliderF(ui, I18n.T("Shader.SunPitch"), ref _sunPitch, 5f, 90f, "0", "SunPitch");
            SliderF(ui, I18n.T("Shader.SunYaw"), ref _sunYaw, -180f, 180f, "0", "SunYaw");
            ColorPick(ui, I18n.T("Shader.SunColor"), ref _sunColor, "Sun");
            ui.EndCard();
        }
    }
}

#region GorillaFilter
namespace UltimateMenu
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using URP = UnityEngine.Rendering.Universal;
    using UnityEngine;

    // Screen filter = real URP post-processing.
    // Enabling the mod loads gorillafilter.bundle (global Volume + PostProcessData) and forces post-processing on every game camera.
    public class GorillaFilter : MonoBehaviour
    {
        public static GorillaFilter Instance { get; private set; }
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled(GorillaShader.Title) && MenuPrefs.GetInt(GorillaShader.PostFxKey, 1) == 1;

        const string P = "MUM_SCF_V2_";
        const string PresetListKey = P + "PresetList";
        const string BundleSuffix = "gorillafilter.bundle";
        const string PrefabName = "GorillaPostFX";
        const float StartupDelay = 5f;

        // ---------- slider table ----------
        const int FExposure = 0, FContrast = 1, FSaturation = 2, FHue = 3, FTemp = 4, FTint = 5, FShadows = 6, FMidtones = 7, FHighlights = 8,
                  FBloomI = 9, FBloomT = 10, FBloomS = 11, FVigI = 12, FVigS = 13, FChroma = 14, FGrain = 15, FLens = 16, FPanini = 17, FMotion = 18, FBlur = 19;
        const int ColorFirst = 0, ColorLast = 8, TweakFirst = 9, TweakLast = 19;
        struct Def
        {
            public string key, fmt; public float min, max, def; public int group;
            public Def(string k, float mn, float mx, float d, string f, int g) { key = k; min = mn; max = mx; def = d; fmt = f; group = g; }
        }
        static readonly Def[] Defs =
        {
            new Def("Filter.Brightness", -3f, 3f, 0f, "0.00", 0),
            new Def("Filter.Contrast", -100f, 100f, 10f, "0", 0),
            new Def("Filter.Saturation", -100f, 100f, 10f, "0", 0),
            new Def("Filter.FxHue", -180f, 180f, 0f, "0", 0),
            new Def("Filter.Warmth", -100f, 100f, 0f, "0", 0),
            new Def("Filter.FxTint", -100f, 100f, 0f, "0", 0),
            new Def("Filter.FxShadows", -1f, 1f, 0f, "0.00", 0),
            new Def("Filter.FxMidtones", -1f, 1f, 0f, "0.00", 0),
            new Def("Filter.FxHighlights", -1f, 1f, 0f, "0.00", 0),
            new Def("Filter.FxBloomI", 0f, 3f, 0.8f, "0.00", 1),
            new Def("Filter.FxBloomT", 0f, 2f, 0.9f, "0.00", 1),
            new Def("Filter.FxBloomS", 0f, 1f, 0.7f, "0.00", 1),
            new Def("Filter.Vignette", 0f, 1f, 0.25f, "0.00", 2),
            new Def("Filter.VignetteSoftness", 0.01f, 1f, 0.4f, "0.00", 2),
            new Def("Filter.FxChroma", 0f, 1f, 0f, "0.00", 2),
            new Def("Filter.Grain", 0f, 1f, 0f, "0.00", 2),
            new Def("Filter.FxLens", -1f, 1f, 0f, "0.00", 2),
            new Def("Filter.FxPanini", 0f, 1f, 0f, "0.00", 2),
            new Def("Filter.FxMotion", 0f, 1f, 0f, "0.00", 2),
            new Def("Filter.FxBlur", 0f, 1f, 0f, "0.00", 2),
        };
        static readonly string[] GroupKeys = { "", "Filter.FxGroupLight", "Filter.FxGroupLens" };
        static float[] DefaultValues() { var a = new float[Defs.Length]; for (int i = 0; i < a.Length; i++) a[i] = Defs[i].def; return a; }

        // ---------- state ----------
        float[] _v = DefaultValues();
        Color _colorFilter = Color.white;
        float _weight = 1f;                  // master strength of the Volume
        bool _active, _failed, _hooked, _applyDue;
        bool _presetsOpen, _colorOpen, _tweaksOpen;
        float _activateAfter, _saveDue;
        AssetBundle _bundle;
        GameObject _volumeGo;
        UnityEngine.Rendering.Volume _vol;
        string _newPresetName = "";
        readonly List<string> _presets = new List<string>();
        sealed class CamState { public Camera cam; public bool post; public LayerMask mask; public object mode; }
        readonly Dictionary<int, CamState> _cams = new Dictionary<int, CamState>();

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadState();
            LoadPresetList();
            _activateAfter = Time.unscaledTime + StartupDelay;
        }
        void Update()
        {
            bool on = Enabled;
            if (on && !_active && !_failed && Time.unscaledTime >= _activateAfter) Activate();
            else if (!on) { if (_active) Deactivate(); _failed = false; }
            if (_applyDue) { _applyDue = false; ApplyEffects(); _saveDue = Time.unscaledTime + 0.6f; }
            if (_saveDue > 0f && Time.unscaledTime >= _saveDue) { _saveDue = 0f; SaveState(); }
        }
        void OnDestroy()
        {
            if (_active) Deactivate();
            if (_bundle != null) _bundle.Unload(true);
            if (Instance == this) Instance = null;
        }
        public void ReloadConfig() { LoadState(); LoadPresetList(); _applyDue = true; }

        // ---------- activation ----------
        void Activate()
        {
            if (!LoadBundle()) { _failed = true; return; }
            EnsureRendererPostProcessing();
            var prefab = _bundle.LoadAsset<GameObject>(PrefabName);
            if (prefab == null) { Log.Info("[PostFX] Prefab " + PrefabName + " not found in bundle."); _failed = true; return; }
            _volumeGo = Instantiate(prefab);
            DontDestroyOnLoad(_volumeGo);
            _vol = _volumeGo.GetComponent<UnityEngine.Rendering.Volume>();
            if (_vol == null)
            {
                Log.Info("[PostFX] Volume script missing on prefab (URP version mismatch?).");
                Destroy(_volumeGo); _volumeGo = null; _failed = true; return;
            }
            if (!_hooked) { UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera; _hooked = true; }
            _active = true;
            ApplyEffects();
            Log.Info("[PostFX] Active.");
        }
        void Deactivate()
        {
            _active = false;
            if (_hooked) { UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera; _hooked = false; }
            RestoreCameras();
            if (_volumeGo != null) Destroy(_volumeGo);
            _volumeGo = null; _vol = null;
            Log.Info("[PostFX] Disabled.");
        }
        bool LoadBundle()
        {
            if (_bundle != null) return true;
            try
            {
                var asm = typeof(GorillaFilter).Assembly;
                string res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(BundleSuffix, System.StringComparison.OrdinalIgnoreCase));
                if (res == null) throw new System.IO.FileNotFoundException("Embedded resource not found: " + BundleSuffix);
                byte[] data;
                using (var s = asm.GetManifestResourceStream(res))
                using (var ms = new System.IO.MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                _bundle = AssetBundle.LoadFromMemory(data);
                if (_bundle == null) throw new System.Exception("AssetBundle.LoadFromMemory returned null");
                Log.Info("[PostFX] Bundle loaded: " + string.Join(", ", _bundle.GetAllAssetNames()));
                return true;
            }
            catch (System.Exception e) { Log.Err("PostFX bundle", e); return false; }
        }
        // The game's URP renderer ships without post-processing data: give it the one from the bundle, then rebuild the renderers.
        void EnsureRendererPostProcessing()
        {
            try
            {
                var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                if (asset == null) { Log.Info("[PostFX] No render pipeline asset."); return; }
                const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var listField = asset.GetType().GetFields(all).FirstOrDefault(f => f.FieldType.IsArray && f.FieldType.GetElementType().Name == "ScriptableRendererData");
                if (listField == null) { Log.Info("[PostFX] Renderer data list not found."); return; }
                var datas = listField.GetValue(asset) as System.Array;
                if (datas == null) return;
                var ppd = _bundle != null ? _bundle.LoadAllAssets().FirstOrDefault(a => a != null && a.GetType().Name == "PostProcessData") : null;
                bool changed = false;
                foreach (var rd in datas)
                {
                    if (rd == null) continue;
                    var f = rd.GetType().GetFields(all).FirstOrDefault(x => x.FieldType.Name == "PostProcessData");
                    if (f == null || f.GetValue(rd) != null) continue;
                    if (ppd != null && f.FieldType.IsAssignableFrom(ppd.GetType())) { f.SetValue(rd, ppd); changed = true; Log.Info("[PostFX] postProcessData assigned from bundle."); }
                }
                if (changed)
                {
                    var destroy = asset.GetType().GetMethod("DestroyRenderers", all);
                    if (destroy != null) destroy.Invoke(asset, null);
                }
            }
            catch (System.Exception e) { Log.Err("PostFX renderer", e); }
        }

        // ---------- cameras ----------
        // Volume update mode lives in a non-public field in this URP version: reached by reflection (found by type name).
        static FieldInfo _volModeField;
        static bool _volModeSearched;
        static FieldInfo VolModeField()
        {
            if (!_volModeSearched)
            {
                _volModeSearched = true;
                _volModeField = typeof(URP.UniversalAdditionalCameraData)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(f => f.FieldType.Name == "VolumeFrameworkUpdateMode");
                if (_volModeField == null) Log.Info("[PostFX] volume update mode field NOT FOUND");
            }
            return _volModeField;
        }
        static object GetVolMode(URP.UniversalAdditionalCameraData d)
        {
            try { var f = VolModeField(); return f != null ? f.GetValue(d) : null; } catch { return null; }
        }
        static void SetVolMode(URP.UniversalAdditionalCameraData d, object mode)
        {
            try { var f = VolModeField(); if (f != null && mode != null) f.SetValue(d, mode); } catch { }
        }
        static void ForceEveryFrame(URP.UniversalAdditionalCameraData d)
        {
            try
            {
                var f = VolModeField();
                if (f == null) return;
                var every = System.Enum.Parse(f.FieldType, "EveryFrame");
                if (!every.Equals(f.GetValue(d))) f.SetValue(d, every);
            }
            catch { }
        }
        static string PathOf(Transform t)
        {
            string s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
        // Runs right before every camera renders: post-processing and per-frame volume updates are forced ON, whatever the game did.
        void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            if (!_active || cam == null || cam.cameraType != CameraType.Game) return;
            var data = URP.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            if (data == null || data.renderType == URP.CameraRenderType.Overlay) return;
            int id = cam.GetInstanceID();
            if (!_cams.ContainsKey(id))
            {
                var mode = GetVolMode(data);
                _cams[id] = new CamState { cam = cam, post = data.renderPostProcessing, mask = data.volumeLayerMask, mode = mode };
                VisualCameraDepth.Acquire(id, data);
                Log.Info("[PostFX] camera '" + PathOf(cam.transform) + "' rt=" + (cam.targetTexture != null) + " post(before)=" + data.renderPostProcessing + " volMode(before)=" + (mode != null ? mode.ToString() : "?"));
            }
            data.renderPostProcessing = true;
            data.requiresDepthTexture = true;
            data.volumeLayerMask = ~0;
            ForceEveryFrame(data);
        }
        void RestoreCameras()
        {
            foreach (var entry in _cams)
            {
                VisualCameraDepth.Release(entry.Key);
                var s = entry.Value;
                if (s.cam == null) continue;
                var d = s.cam.GetComponent<URP.UniversalAdditionalCameraData>();
                if (d == null) continue;
                d.renderPostProcessing = s.post; d.volumeLayerMask = s.mask;
                SetVolMode(d, s.mode);
            }
            _cams.Clear();
        }

        // ---------- effects (real URP volume overrides) ----------
        T Comp<T>(UnityEngine.Rendering.VolumeProfile p) where T : UnityEngine.Rendering.VolumeComponent
        {
            T c;
            if (p.TryGet(out c) && c != null) return c;
            return p.Add<T>(true);
        }
        void ApplyEffects()
        {
            if (_vol == null) return;
            _vol.weight = _weight;
            var p = _vol.profile;
            if (p == null) return;
            try
            {
                var ca = Comp<URP.ColorAdjustments>(p);
                ca.active = true;
                ca.postExposure.Override(_v[FExposure]); ca.contrast.Override(_v[FContrast]);
                ca.saturation.Override(_v[FSaturation]); ca.hueShift.Override(_v[FHue]); ca.colorFilter.Override(_colorFilter);
                var wb = Comp<URP.WhiteBalance>(p);
                wb.active = Mathf.Abs(_v[FTemp]) > 0.01f || Mathf.Abs(_v[FTint]) > 0.01f;
                wb.temperature.Override(_v[FTemp]); wb.tint.Override(_v[FTint]);
                var lgg = Comp<URP.LiftGammaGain>(p);
                lgg.active = Mathf.Abs(_v[FShadows]) > 0.001f || Mathf.Abs(_v[FMidtones]) > 0.001f || Mathf.Abs(_v[FHighlights]) > 0.001f;
                lgg.lift.Override(new Vector4(1f, 1f, 1f, _v[FShadows]));
                lgg.gamma.Override(new Vector4(1f, 1f, 1f, _v[FMidtones]));
                lgg.gain.Override(new Vector4(1f, 1f, 1f, _v[FHighlights]));
                var tm = Comp<URP.Tonemapping>(p);
                tm.active = false;   // no tonemapping: the bundle profile ships ACES, so it is switched off explicitly
                tm.mode.Override(URP.TonemappingMode.None);

                var bloom = Comp<URP.Bloom>(p);
                bloom.active = _v[FBloomI] > 0.001f;
                bloom.intensity.Override(_v[FBloomI]); bloom.threshold.Override(_v[FBloomT]); bloom.scatter.Override(_v[FBloomS]);
                var vig = Comp<URP.Vignette>(p);
                vig.active = _v[FVigI] > 0.001f;
                vig.intensity.Override(_v[FVigI]); vig.smoothness.Override(_v[FVigS]);
                var chr = Comp<URP.ChromaticAberration>(p);
                chr.active = _v[FChroma] > 0.001f; chr.intensity.Override(_v[FChroma]);
                var gr = Comp<URP.FilmGrain>(p);
                gr.active = _v[FGrain] > 0.001f; gr.type.Override(URP.FilmGrainLookup.Medium1); gr.intensity.Override(_v[FGrain]);
                var ld = Comp<URP.LensDistortion>(p);
                ld.active = Mathf.Abs(_v[FLens]) > 0.001f; ld.intensity.Override(_v[FLens]);
                var pa = Comp<URP.PaniniProjection>(p);
                pa.active = _v[FPanini] > 0.001f; pa.distance.Override(_v[FPanini]);
                var mb = Comp<URP.MotionBlur>(p);
                mb.active = _v[FMotion] > 0.001f; mb.mode.Override(URP.MotionBlurMode.CameraOnly); mb.intensity.Override(_v[FMotion]);
                var dof = Comp<URP.DepthOfField>(p);
                dof.active = _v[FBlur] > 0.001f;
                dof.mode.Override(URP.DepthOfFieldMode.Gaussian);
                dof.gaussianStart.Override(0f); dof.gaussianEnd.Override(0.01f);
                dof.gaussianMaxRadius.Override(Mathf.Lerp(0.5f, 1.5f, _v[FBlur]));
                dof.highQualitySampling.Override(true);
            }
            catch (System.Exception e) { Log.Err("PostFX effects", e); }
        }

        // ---------- saved state ----------
        void SaveState()
        {
            for (int i = 0; i < _v.Length; i++) MenuPrefs.SetFloat(P + "V" + i, _v[i]);
            MenuPrefs.SetFloat(P + "Weight", _weight);
            MenuPrefs.SetFloat(P + "CfR", _colorFilter.r); MenuPrefs.SetFloat(P + "CfG", _colorFilter.g); MenuPrefs.SetFloat(P + "CfB", _colorFilter.b);
            MenuPrefs.SetInt(P + "Init", 1);
            MenuPrefs.Save();
        }
        void LoadState()
        {
            if (!MenuPrefs.HasKey(P + "Init")) return;
            for (int i = 0; i < _v.Length; i++) _v[i] = Mathf.Clamp(MenuPrefs.GetFloat(P + "V" + i, Defs[i].def), Defs[i].min, Defs[i].max);
            _weight = Mathf.Clamp01(MenuPrefs.GetFloat(P + "Weight", 1f));
            _colorFilter = new Color(MenuPrefs.GetFloat(P + "CfR", 1f), MenuPrefs.GetFloat(P + "CfG", 1f), MenuPrefs.GetFloat(P + "CfB", 1f));
        }
        void ResetAll()
        {
            _v = DefaultValues(); _weight = 1f; _colorFilter = Color.white;
            _applyDue = true;
        }

        // ---------- custom presets (empty by default) ----------
        static string PresetKey(string name) { return P + "Pr_" + name + "_"; }
        void LoadPresetList()
        {
            _presets.Clear();
            string raw = MenuPrefs.GetString(PresetListKey, "");
            if (string.IsNullOrEmpty(raw)) return;
            _presets.AddRange(raw.Split('\n').Where(n => !string.IsNullOrWhiteSpace(n)));
            // Import every preset, including ones not loaded in this session.
            foreach (string name in _presets)
            {
                string k = PresetKey(name);
                for (int i = 0; i < Defs.Length; i++) MenuPrefs.GetFloat(k + "V" + i, Defs[i].def);
                MenuPrefs.GetFloat(k + "CfR", 1f); MenuPrefs.GetFloat(k + "CfG", 1f); MenuPrefs.GetFloat(k + "CfB", 1f);
                if (MenuPrefs.HasKey(k + "Visual")) MenuPrefs.GetString(k + "Visual", "");
                if (MenuPrefs.HasKey(k + "Weight")) MenuPrefs.GetFloat(k + "Weight", 1f);
            }
        }
        void SavePresetList() { MenuPrefs.SetString(PresetListKey, string.Join("\n", _presets)); MenuPrefs.Save(); }
        void SavePreset(string name, GorillaShader shader)
        {
            name = new string(name.Where(c => !char.IsControl(c)).Take(64).ToArray()).Trim();
            if (name.Length == 0 || (_presets.Count >= 64 && !_presets.Contains(name))) return;
            string k = PresetKey(name);
            for (int i = 0; i < _v.Length; i++) MenuPrefs.SetFloat(k + "V" + i, _v[i]);
            MenuPrefs.SetFloat(k + "CfR", _colorFilter.r); MenuPrefs.SetFloat(k + "CfG", _colorFilter.g); MenuPrefs.SetFloat(k + "CfB", _colorFilter.b);
            MenuPrefs.SetFloat(k + "Weight", _weight);
            MenuPrefs.SetString(k + "Visual", shader.CaptureFogLook());
            if (!_presets.Contains(name)) _presets.Add(name);
            SavePresetList();
        }
        void LoadPreset(string name, GorillaShader shader)
        {
            string k = PresetKey(name);
            if (!MenuPrefs.HasKey(k + "V0")) return;
            for (int i = 0; i < _v.Length; i++) _v[i] = Mathf.Clamp(MenuPrefs.GetFloat(k + "V" + i, Defs[i].def), Defs[i].min, Defs[i].max);
            _colorFilter = new Color(MenuPrefs.GetFloat(k + "CfR", 1f), MenuPrefs.GetFloat(k + "CfG", 1f), MenuPrefs.GetFloat(k + "CfB", 1f));
            if (MenuPrefs.HasKey(k + "Weight")) _weight = Mathf.Clamp01(MenuPrefs.GetFloat(k + "Weight", 1f));
            if (!shader.RestoreFogLook(MenuPrefs.GetString(k + "Visual", ""))) MenuPrefs.SetInt(GorillaShader.PostFxKey, 1);
            MenuPrefs.SetInt("MUM_ModEnabled_" + GorillaShader.Title, 1);
            SaveState();
            _applyDue = true;
        }
        void DeletePreset(string name)
        {
            string k = PresetKey(name);
            for (int i = 0; i < Defs.Length; i++) MenuPrefs.DeleteKey(k + "V" + i);
            foreach (var s in new[] { "CfR", "CfG", "CfB", "Weight", "Visual" }) MenuPrefs.DeleteKey(k + s);
            _presets.Remove(name);
            SavePresetList();
        }

        // ---------- menu page ----------
        void DrawSliders(MenuUI ui, int from, int to)
        {
            int lastGroup = -1;
            for (int i = from; i <= to; i++)
            {
                var d = Defs[i];
                if (d.group != 0 && d.group != lastGroup) { lastGroup = d.group; ui.Label(I18n.T(GroupKeys[d.group]), Theme.Dim, 9); }
                float v = ui.Slider(I18n.T(d.key), _v[i], d.min, d.max, d.fmt);
                if (!Mathf.Approximately(v, _v[i])) { _v[i] = v; _applyDue = true; }
            }
        }
        internal void ApplyLook(int look)
        {
            _v = DefaultValues(); _weight = 1f; _colorFilter = Color.white;
            _v[FExposure] = look == 2 ? -.15f : 0f;
            _v[FContrast] = look == 1 ? 15f : look == 2 ? 12f : 7f;
            _v[FSaturation] = look == 1 ? 12f : look == 2 ? -14f : 4f;
            _v[FTemp] = look == 1 ? 23f : look == 2 ? -22f : 0f;
            _v[FBloomI] = look == 1 ? 1.15f : look == 2 ? .85f : .65f;
            _v[FBloomT] = look == 1 ? .84f : look == 2 ? .88f : .9f;
            _v[FBloomS] = look == 1 ? .73f : .65f;
            _v[FVigI] = look == 2 ? .2f : look == 1 ? .13f : .05f;
            _applyDue = true;
            SaveState();
        }

        internal void DrawPresets(MenuUI ui, GorillaShader shader)
        {
            ui.ExpandableSection(I18n.T("Shader.MyLooks"), _presets.Count.ToString(), ref _presetsOpen, () =>
            {
                GUILayout.BeginHorizontal();
                _newPresetName = ui.TextField(_newPresetName);
                if (ui.SmallBtn(I18n.T("Filter.SaveAs"), ui.TB3, Theme.Text) && !string.IsNullOrWhiteSpace(_newPresetName))
                { SavePreset(_newPresetName, shader); _newPresetName = ""; }
                GUILayout.EndHorizontal();
                ui.Label(I18n.T("Shader.MyLooksHint"), Theme.Dim, 9);
                if (_presets.Count == 0) ui.Label(I18n.T("Filter.NoCustomPresets"), Theme.Dim, 9);
                for (int i = 0; i < _presets.Count; i++)
                {
                    string name = _presets[i];
                    GUILayout.BeginHorizontal();
                    if (ui.Btn(name, ui.TB3, Theme.Text)) LoadPreset(name, shader);
                    bool delete = ui.SmallBtn(I18n.T("Filter.Delete"), ui.TB3, Theme.Text);
                    GUILayout.EndHorizontal();
                    if (delete) { DeletePreset(name); break; }
                }
            });
        }

        internal void DrawControls(MenuUI ui)
        {
            // 2) strength + reset
            ui.BeginCard();
            float w = ui.Slider(I18n.T("Filter.Intensity"), _weight, 0f, 1f, "0.00");
            if (!Mathf.Approximately(w, _weight)) { _weight = w; _applyDue = true; }
            if (ui.Btn(I18n.T("Filter.Reset"), ui.TB3, Theme.Text)) ResetAll();
            if (_failed) ui.Label(I18n.T("Filter.FxFail"), Theme.Dim, 9);
            ui.EndCard();
            // 3) color correction
            ui.ExpandableSection(I18n.T("Filter.FxColorSection"), "", ref _colorOpen, () =>
            {
                DrawSliders(ui, ColorFirst, ColorLast);
                Color before = _colorFilter;
                ui.ColorPickerHSV(ref _colorFilter, I18n.T("Filter.CustomTint"), compact: true);
                if (!Mathf.Approximately(before.r, _colorFilter.r) || !Mathf.Approximately(before.g, _colorFilter.g) || !Mathf.Approximately(before.b, _colorFilter.b)) _applyDue = true;
            });
            // 4) tweaks
            ui.ExpandableSection(I18n.T("Filter.Effects"), "", ref _tweaksOpen, () => DrawSliders(ui, TweakFirst, TweakLast));
        }
    }
}
#endregion

namespace UltimateMenu
{
    // Both visual effects may request camera depth; restore the original override after the last one stops.
    internal static class VisualCameraDepth
    {
        sealed class Request
        {
            internal UnityEngine.Rendering.Universal.UniversalAdditionalCameraData CameraData;
            internal UnityEngine.Rendering.Universal.CameraOverrideOption Original;
            internal int Count;
        }
        static readonly System.Collections.Generic.Dictionary<int, Request> Requests = new System.Collections.Generic.Dictionary<int, Request>();
        internal static void Acquire(int id, UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data)
        {
            if (!Requests.TryGetValue(id, out var request) || request.CameraData != data)
                Requests[id] = request = new Request { CameraData = data, Original = data.requiresDepthOption };
            request.Count++;
        }
        internal static void Release(int id)
        {
            if (!Requests.TryGetValue(id, out var request) || --request.Count > 0) return;
            Requests.Remove(id);
            if (request.CameraData != null) request.CameraData.requiresDepthOption = request.Original;
        }
    }
}
