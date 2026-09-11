using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GorillaLocomotion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using Quaternion = UnityEngine.Quaternion;
using CommonUsages = UnityEngine.XR.CommonUsages;
namespace UltimateMenu
{
    public class GorillaSpawners : MonoBehaviour
    {
        public static GorillaSpawners Instance { get; private set; }

        /// <summary>
        /// Le mod est actif uniquement via le switch global de la page Mods (liste des mods).
        /// Quand désactivé, tous les objets spawnés sont cachés (SetActive(false)) et figés,
        /// puis réaffichés avec leur état d'origine restauré à la réactivation.
        /// </summary>
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Spawner.Title");
        private bool _wasEnabled = true;

        string _dataFolder, _saveFile, _search = "";
        readonly List<SpawnedObject> _objects = new List<SpawnedObject>();
        readonly List<string> _availableFiles = new List<string>();
        readonly Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();
        int _selectedFileIndex = -1;
        float _lastSpawnTime = -999f;
        bool _handsReady;
        bool _spawnFrozen = false;
        const float GrabMinDist = 0.01f, GrabMaxDist = 1f, SpawnCooldown = 0.5f, FallTimeoutSeconds = 10f, FallVelocityThreshold = 0.05f;
        public KeyCode keyToggleGrabDebug = KeyCode.G;
        Transform _leftHand, _rightHand, _playerHead;
        static int _spawnCounter = 0;
        Vector2 _scrollFiles, _scrollObjects;
        LayerMask _groundMask = -1;
        const float GroundCheckDistance = 50f;
        const string SafeShaderName = "Universal Render Pipeline/Lit";
        static Shader _cachedSafeShader;
        static bool _safeShaderSearched;
        static readonly string[] TexturePropertyCandidates = { "_MainTex", "_BaseMap", "_BaseColorMap", "_AlbedoMap", "_Albedo", "_Tex", "_ColorMap", "_Diffuse" };
        static readonly string[] ColorPropertyCandidates = { "_Color", "_BaseColor", "_AlbedoColor", "_MainColor", "_TintColor" };

        // Nom logique de la ressource embarquée dans le DLL (voir configuration du .csproj).
        private const string AUTOBUNDLE_RESOURCE = "UltimateMenu.Assets.GorillaSpawners.AutoBundle.unitypackage";
        private const string AUTOBUNDLE_FILENAME = "AutoBundle.unitypackage";

        void Awake()
        {
            Instance = this;
            CreateDataFolder();
            EnsureAutoBundleExtracted();
            RefreshFileList();
        }
        void Start()
        {
            MenuUI.Instance?.RegisterPage("Spawner.Title", DrawPage, MenuCategory.Experimental);
            LoadSavedObjects();
            GorillaTagger.OnPlayerSpawned(FindPlayerRig);
            InitGroundMask();
            InvokeRepeating(nameof(AutoSave), AutoSaveIntervalSeconds, AutoSaveIntervalSeconds);
        }
        const float AutoSaveIntervalSeconds = 5f;
        void AutoSave() { if (_objects.Count > 0) SaveAll(); }
        void OnDestroy()
        {
            SaveAll();
            foreach (var kv in _loadedBundles)
                if (kv.Value != null) kv.Value.Unload(false);
            _loadedBundles.Clear();
        }
        void InitGroundMask()
        {
            try
            {
                if (GorillaLocomotion.GTPlayer.Instance != null)
                {
                    _groundMask = GorillaLocomotion.GTPlayer.Instance.locomotionEnabledLayers;
                    Debug.Log("[GorillaSpawner] InitGroundMask: layer mask récupéré = " + _groundMask.value);
                }
                else
                {
                    Debug.LogWarning("[GorillaSpawner] InitGroundMask: Player.Instance encore null, retry dans 1s.");
                    Invoke(nameof(InitGroundMask), 1f);
                }
            }
            catch (Exception e) { Log.Err("Spawner.InitGroundMask", e); }
        }
        void CreateDataFolder()
        {
            string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory();
            _dataFolder = Path.Combine(dir, "GorillaSpawners");
            if (!Directory.Exists(_dataFolder)) Directory.CreateDirectory(_dataFolder);
            _saveFile = Path.Combine(_dataFolder, "saved_objects.txt");
        }

        /// <summary>
        /// Extrait AutoBundle.unitypackage depuis les ressources embarquées du DLL vers le dossier
        /// GorillaSpawner, uniquement si le fichier n'existe pas déjà sur le disque. Ce package aide
        /// l'utilisateur à créer ses propres .bundle compatibles avec ce mod.
        /// </summary>
        void EnsureAutoBundleExtracted()
        {
            string targetPath = Path.Combine(_dataFolder, AUTOBUNDLE_FILENAME);
            if (File.Exists(targetPath)) return;

            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(AUTOBUNDLE_RESOURCE))
                {
                    if (stream == null)
                    {
                        Log.Err("Spawner.EnsureAutoBundleExtracted",
                            new Exception("Ressource embarquée introuvable : " + AUTOBUNDLE_RESOURCE +
                                           ". Vérifie que AutoBundle.unitypackage est bien marqué 'Embedded Resource' dans le projet."));
                        return;
                    }

                    using (var fileStream = File.Create(targetPath))
                        stream.CopyTo(fileStream);
                }
                Log.Info("[GorillaSpawner] AutoBundle.unitypackage extrait vers " + targetPath);
            }
            catch (Exception e)
            {
                Log.Err("Spawner.EnsureAutoBundleExtracted", e);
            }
        }
        void RefreshFileList()
        {
            _availableFiles.Clear();
            if (!Directory.Exists(_dataFolder)) return;
            foreach (var f in Directory.GetFiles(_dataFolder, "*.bundle")) _availableFiles.Add(f);
            _availableFiles.Sort(StringComparer.OrdinalIgnoreCase);
        }
        void FindPlayerRig()
        {
            try
            {
                var rig = VRRig.LocalRig;
                if (rig != null)
                {
                    var head = rig.transform.Find("rig/head");
                    var handL = rig.transform.Find("rig/hand.L");
                    var handR = rig.transform.Find("rig/hand.R");
                    if (head != null) _playerHead = head;
                    if (handL != null) _leftHand = handL;
                    if (handR != null) _rightHand = handR;
                }
                if (_playerHead == null || _leftHand == null || _rightHand == null)
                {
                    var tagger = GorillaTagger.Instance;
                    if (tagger != null)
                    {
                        if (_playerHead == null && tagger.headCollider != null) _playerHead = tagger.headCollider.transform;
                        if (_leftHand == null && tagger.leftHandTransform != null) _leftHand = tagger.leftHandTransform;
                        if (_rightHand == null && tagger.rightHandTransform != null) _rightHand = tagger.rightHandTransform;
                    }
                }
                _handsReady = _playerHead != null && _leftHand != null && _rightHand != null;
                if (!_handsReady) Invoke(nameof(FindPlayerRig), 2f);
            }
            catch (Exception e) { Log.Err("Spawner.FindPlayerRig", e); Invoke(nameof(FindPlayerRig), 2f); }
        }
        static UnityEngine.XR.InputDevice? GetDevice(bool rightHand)
        {
            var chars = InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Controller | (rightHand ? InputDeviceCharacteristics.Right : InputDeviceCharacteristics.Left);
            var devices = new List<UnityEngine.XR.InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(chars, devices);
            return devices.Count > 0 ? devices[0] : (UnityEngine.XR.InputDevice?)null;
        }
        const float GripThreshold = 0.5f;
        static bool GripDown(bool rightHand)
        {
            var rig = VRRig.LocalRig;
            if (rig != null)
            {
                try
                {
                    float v = rightHand ? rig.rightMiddle.gripValue : rig.leftMiddle.gripValue;
                    return v >= GripThreshold;
                }
                catch { }
            }
            var dev = GetDevice(rightHand);
            return dev.HasValue && dev.Value.TryGetFeatureValue(CommonUsages.gripButton, out bool p) && p;
        }
        static Shader GetSafeShader()
        {
            if (_safeShaderSearched) return _cachedSafeShader;
            _safeShaderSearched = true;
            _cachedSafeShader = Shader.Find(SafeShaderName);
            if (_cachedSafeShader == null)
                Debug.LogError("[GorillaSpawner] Shader de secours '" + SafeShaderName + "' introuvable.");
            else
                Debug.Log("[GorillaSpawner] Shader de secours trouvé et mis en cache: " + SafeShaderName);
            return _cachedSafeShader;
        }
        void FixMaterialsAndVisibility(GameObject instance)
        {
            var safeShader = GetSafeShader();
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                r.enabled = true;
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) { Debug.LogWarning("[GorillaSpawner] Matériau null sur " + r.name + " (slot " + i + ")"); continue; }
                    Texture sourceTex = null;
                    foreach (var candidate in TexturePropertyCandidates)
                        if (mat.HasProperty(candidate)) { var t = mat.GetTexture(candidate); if (t != null) { sourceTex = t; break; } }
                    Color sourceColor = Color.white;
                    bool hasColor = false;
                    foreach (var candidate in ColorPropertyCandidates)
                        if (mat.HasProperty(candidate)) { sourceColor = mat.GetColor(candidate); hasColor = true; break; }
                    if (safeShader != null && mat.shader != safeShader)
                    {
                        mat.shader = safeShader;
                        if (sourceTex != null)
                        {
                            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", sourceTex);
                            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", sourceTex);
                        }
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", hasColor ? sourceColor : Color.white);
                        if (mat.HasProperty("_Color")) mat.SetColor("_Color", hasColor ? sourceColor : Color.white);
                        if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
                        if (mat.HasProperty("_CullMode")) mat.SetInt("_CullMode", (int)UnityEngine.Rendering.CullMode.Back);
                        Debug.Log("[GorillaSpawner] -> Shader appliqué: " + safeShader.name + " sur " + r.name
                            + " | texture=" + (sourceTex != null ? sourceTex.name : "aucune")
                            + " | couleur=" + (hasColor ? sourceColor.ToString() : "défaut (blanc)"));
                    }
                }
                r.materials = mats;
            }
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                t.gameObject.SetActive(true);
            Debug.Log("[GorillaSpawner] FixMaterialsAndVisibility: " + renderers.Length + " renderer(s) traité(s) sur '" + instance.name + "'");
        }
        void LogColliderDiagnostic(GameObject instance)
        {
            var allTransforms = instance.GetComponentsInChildren<Transform>(true);
            var meshFilters = instance.GetComponentsInChildren<MeshFilter>(true);
            var existingColliders = instance.GetComponentsInChildren<Collider>(true);
            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            Debug.Log("[GorillaSpawner] === DIAGNOSTIC COLLISIONS pour '" + instance.name + "' ===");
            Debug.Log("[GorillaSpawner] Total objets dans la hiérarchie : " + allTransforms.Length);
            Debug.Log("[GorillaSpawner] MeshRenderer trouvés : " + renderers.Length);
            Debug.Log("[GorillaSpawner] MeshFilter trouvés : " + meshFilters.Length);
            Debug.Log("[GorillaSpawner] Collider déjà présents AVANT ajout : " + existingColliders.Length);
            if (meshFilters.Length == 0)
            {
                Debug.LogWarning("[GorillaSpawner] AUCUN MeshFilter dans ce modèle : impossible de générer un collider, il n'y a aucun mesh exploitable. Vérifie que le bundle contient bien la géométrie (pas juste un objet vide/rig/os).");
            }
            foreach (var mf in meshFilters)
            {
                string path = GetHierarchyPath(mf.transform, instance.transform);
                if (mf.sharedMesh == null)
                    Debug.LogWarning("[GorillaSpawner]   - MeshFilter SANS mesh sur '" + path + "' (sharedMesh == null, ignoré).");
                else
                    Debug.Log("[GorillaSpawner]   - MeshFilter OK sur '" + path + "' : mesh='" + mf.sharedMesh.name + "' verts=" + mf.sharedMesh.vertexCount + " hasCollider=" + (mf.gameObject.GetComponent<Collider>() != null));
            }
            if (existingColliders.Length > 0)
            {
                foreach (var col in existingColliders)
                {
                    string path = GetHierarchyPath(col.transform, instance.transform);
                    Debug.Log("[GorillaSpawner]   - Collider existant '" + col.GetType().Name + "' sur '" + path + "' enabled=" + col.enabled);
                }
            }
            Debug.Log("[GorillaSpawner] === FIN DIAGNOSTIC ===");
        }
        static string GetHierarchyPath(Transform t, Transform root)
        {
            if (t == root) return t.name;
            var stack = new List<string>();
            var cur = t;
            while (cur != null && cur != root) { stack.Add(cur.name); cur = cur.parent; }
            stack.Reverse();
            return string.Join("/", stack);
        }
        void AddMeshCollidersForClimbing(GameObject instance)
        {
            LogColliderDiagnostic(instance);
            int climbLayer = GetMapClimbLayer();
            var existingColliders = instance.GetComponentsInChildren<Collider>(true);
            if (existingColliders.Length > 0)
            {
                foreach (var col in existingColliders)
                {
                    col.enabled = true;
                    if (climbLayer >= 0) col.gameObject.layer = climbLayer;
                }
                bool hasNonConvexMesh = existingColliders.Any(c => c is MeshCollider mc && !mc.convex);
                if (hasNonConvexMesh) AddFallbackConvexCollider(instance);
                if (climbLayer >= 0) instance.layer = climbLayer;
                Debug.Log("[GorillaSpawner] AddMeshCollidersForClimbing: " + existingColliders.Length
                    + " collider(s) intégré(s) au modèle réutilisé(s) sur '" + instance.name + "', layer climb=" + climbLayer
                    + ", boxColliderSecours=" + hasNonConvexMesh);
                return;
            }
            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            int added = 0, skippedNoMesh = 0;
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) { Debug.LogWarning("[GorillaSpawner] AddMeshCollidersForClimbing: MeshFilter sans mesh sur " + mf.name); skippedNoMesh++; continue; }
                var go = mf.gameObject;
                if (go.GetComponent<Collider>() == null)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                    added++;
                }
                if (climbLayer >= 0) go.layer = climbLayer;
            }
            AddFallbackConvexCollider(instance);
            if (climbLayer >= 0) instance.layer = climbLayer;
            if (added == 0 && filters.Length == 0)
            {
                Debug.LogError("[GorillaSpawner] AddMeshCollidersForClimbing: ÉCHEC, aucun MeshFilter ni Collider trouvé sur '" + instance.name + "'. Le modèle est probablement vide, ou toute sa géométrie est dans des enfants inactifs/désactivés qui n'ont pas été réactivés.");
            }
            else if (added == 0 && skippedNoMesh == filters.Length)
            {
                Debug.LogError("[GorillaSpawner] AddMeshCollidersForClimbing: ÉCHEC, " + filters.Length + " MeshFilter trouvés mais tous avec sharedMesh == null sur '" + instance.name + "'.");
            }
            else
            {
            }
            Debug.Log("[GorillaSpawner] AddMeshCollidersForClimbing: aucun collider dans le bundle, " + added
                + " MeshCollider(s) généré(s) en fallback sur '" + instance.name + "', layer climb=" + climbLayer);
        }
        const int DefaultCollisionLayer = 8;
        void AddFallbackConvexCollider(GameObject instance)
        {
            if (instance.GetComponent<BoxCollider>() != null) return;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[GorillaSpawner] AddFallbackConvexCollider: aucun Renderer trouvé sur '" + instance.name + "', BoxCollider de secours non créé.");
                return;
            }
            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);
            Vector3 localCenter = instance.transform.InverseTransformPoint(worldBounds.center);
            Vector3 localSize = instance.transform.InverseTransformVector(worldBounds.size);
            localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
            var box = instance.AddComponent<BoxCollider>();
            box.center = localCenter;
            box.size = localSize;
            Debug.Log("[GorillaSpawner] AddFallbackConvexCollider: BoxCollider ajouté sur '" + instance.name + "' center=" + localCenter + " size=" + localSize);
        }
        int GetMapClimbLayer()
        {
            if (_groundMask.value == 0) return -1;
            for (int i = 0; i < 32; i++)
                if ((_groundMask.value & (1 << i)) != 0 && !string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                    return i;
            return -1;
        }
        GameObject LoadFromBundle(string path)
        {
            Debug.Log("[GorillaSpawner] LoadFromBundle: START path=" + path);
            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogError("[GorillaSpawner] LoadFromBundle: fichier introuvable " + path);
                    Log.Info("Spawner.LoadFromBundle: fichier introuvable " + path);
                    return null;
                }
                AssetBundle bundle;
                if (_loadedBundles.TryGetValue(path, out bundle) && bundle != null)
                {
                    Debug.Log("[GorillaSpawner] LoadFromBundle: bundle déjà en cache pour " + path);
                }
                else
                {
                    Debug.Log("[GorillaSpawner] LoadFromBundle: fichier trouvé (" + new FileInfo(path).Length + " octets), appel AssetBundle.LoadFromFile...");
                    try { bundle = AssetBundle.LoadFromFile(path); }
                    catch (Exception bundleEx)
                    {
                        Debug.LogError("[GorillaSpawner] LoadFromBundle: EXCEPTION LoadFromFile: " + bundleEx);
                        Log.Err("Spawner.LoadFromBundle(LoadFromFile)", bundleEx);
                        return null;
                    }
                    if (bundle == null)
                    {
                        Debug.LogError("[GorillaSpawner] LoadFromBundle: LoadFromFile a renvoyé null. Vérifie que le bundle a été buildé avec Unity 6000.2.9f1 / StandaloneWindows64.");
                        Log.Info("Spawner.LoadFromBundle: bundle null pour " + path);
                        return null;
                    }
                    _loadedBundles[path] = bundle;
                    Debug.Log("[GorillaSpawner] LoadFromBundle: bundle chargé OK, assetNames=[" + string.Join(", ", bundle.GetAllAssetNames()) + "]");
                }
                var assetNames = bundle.GetAllAssetNames();
                if (assetNames == null || assetNames.Length == 0)
                {
                    Debug.LogError("[GorillaSpawner] LoadFromBundle: aucun asset dans " + path);
                    Log.Info("Spawner.LoadFromBundle: aucun asset dans " + path);
                    return null;
                }
                GameObject prefab = null; string usedAssetName = null;
                foreach (var assetName in assetNames)
                {
                    var candidate = bundle.LoadAsset<GameObject>(assetName);
                    if (candidate != null) { prefab = candidate; usedAssetName = assetName; break; }
                }
                if (prefab == null)
                {
                    Debug.LogError("[GorillaSpawner] LoadFromBundle: aucun GameObject parmi " + assetNames.Length + " asset(s)");
                    Log.Info("Spawner.LoadFromBundle: aucun GameObject dans " + path);
                    return null;
                }
                Debug.Log("[GorillaSpawner] LoadFromBundle: prefab trouvé, asset='" + usedAssetName + "', instanciation...");
                var instance = Instantiate(prefab);
                if (instance == null)
                {
                    Debug.LogError("[GorillaSpawner] LoadFromBundle: Instantiate a renvoyé null");
                    Log.Info("Spawner.LoadFromBundle: Instantiate null pour " + path);
                    return null;
                }
                _spawnCounter++;
                string baseName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(baseName)) baseName = "Objet";
                instance.name = baseName + "_" + _spawnCounter;
                instance.SetActive(true);
                var renderers = instance.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length == 0) Debug.LogWarning("[GorillaSpawner] LoadFromBundle: aucun MeshRenderer, objet invisible.");
                FixMaterialsAndVisibility(instance);
                AddMeshCollidersForClimbing(instance);
                Debug.Log("[GorillaSpawner] LoadFromBundle: TERMINÉ OK, nom='" + instance.name + "', renderers=" + renderers.Length);
                Log.Info("Spawner.LoadFromBundle: OK " + Path.GetFileName(path) + " nom=" + instance.name + " asset=" + usedAssetName);
                return instance;
            }
            catch (Exception e)
            {
                Debug.LogError("[GorillaSpawner] LoadFromBundle: EXCEPTION GÉNÉRALE " + e);
                Log.Err("Spawner.LoadFromBundle", e);
                return null;
            }
        }
        bool GetFrontOfPlayerPose(out Vector3 pos, out Quaternion rot)
        {
            if (_playerHead != null)
            {
                pos = _playerHead.position + _playerHead.forward * 1.2f;
                rot = Quaternion.LookRotation(_playerHead.forward, Vector3.up);
                return true;
            }
            var cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                pos = cam.transform.position + cam.transform.forward * 1.2f;
                rot = cam.transform.rotation;
                Debug.LogWarning("[GorillaSpawner] GetFrontOfPlayerPose: tête joueur introuvable, fallback caméra");
                return true;
            }
            pos = Vector3.zero;
            rot = Quaternion.identity;
            return false;
        }
        public void SpawnInFront(string path)
        {
            if (!Enabled) return;
            if (Time.time - _lastSpawnTime < SpawnCooldown)
            {
                Debug.LogWarning("[GorillaSpawner] SpawnInFront: ignoré (cooldown), appel trop rapproché");
                return;
            }
            _lastSpawnTime = Time.time;
            GetFrontOfPlayerPose(out Vector3 pos, out Quaternion rot);
            if (!IsFinite(pos)) { Debug.LogError("[GorillaSpawner] SpawnInFront: position invalide, spawn à l'origine."); pos = Vector3.zero; }
            SpawnAt(path, pos, rot);
        }
        static bool IsFinite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        void SpawnAt(string path, Vector3 pos, Quaternion rot)
        {
            Debug.Log("[GorillaSpawner] SpawnAt: appelé pour " + Path.GetFileName(path) + " @ " + pos);
            Log.Info("Spawner.SpawnAt: appelé pour " + Path.GetFileName(path) + " @ " + pos);
            var go = LoadFromBundle(path);
            if (go == null)
            {
                Debug.LogError("[GorillaSpawner] SpawnAt: LoadFromBundle null, annulé pour " + path);
                Log.Info("Spawner.SpawnAt: annulé");
                return;
            }
            var activeScene = SceneManager.GetActiveScene();
            if (go.scene != activeScene)
            {
                Debug.LogWarning("[GorillaSpawner] SpawnAt: hors scène active, déplacement forcé.");
                SceneManager.MoveGameObjectToScene(go, activeScene);
            }
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.transform.localScale = Vector3.one;
            go.layer = _spawnFrozen ? 0 : DefaultCollisionLayer;
            var rb = go.GetComponent<Rigidbody>(); if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.isKinematic = _spawnFrozen;
            var newObj = new SpawnedObject
            {
                Root = go,
                Rb = rb,
                SourcePath = path,
                Frozen = _spawnFrozen,
                Colliders = go.GetComponentsInChildren<Collider>(true)
            };
            _objects.Add(newObj);
            VerifySpawned(go);
            DiagnoseGroundBelow(go);
        }
        void DiagnoseGroundBelow(GameObject go)
        {
            Vector3 origin = go.transform.position;
            float maxDist = 100f;
            var hits = Physics.RaycastAll(origin, Vector3.down, maxDist, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            if (hits.Length == 0)
            {
                Debug.LogError("[GorillaSpawner] DiagnoseGroundBelow: AUCUN collider trouvé sous '" + go.name + "' depuis " + origin + " sur " + maxDist + "m vers le bas.");
                return;
            }
            var first = hits[0];
            string layerName = LayerMask.LayerToName(first.collider.gameObject.layer);
            string colType = first.collider.GetType().Name;
            bool isConvex = first.collider is MeshCollider mcFirst && mcFirst.convex;
            var ownColliders = go.GetComponentsInChildren<Collider>(true);
            bool ownHasNonConvexMesh = ownColliders.Any(c => c is MeshCollider mc && !mc.convex);
            bool groundIsNonConvexMesh = first.collider is MeshCollider mcGround && !mcGround.convex;
            string msg = "DIAGNOSTIC SOL sous '" + go.name + "' : touche '" + first.collider.name
                + "' (" + colType + (isConvex ? ", convex" : "") + ") layer='" + layerName + "' a " + first.distance.ToString("0.00") + "m.";
            Debug.Log("[GorillaSpawner] " + msg);
            if (ownHasNonConvexMesh && groundIsNonConvexMesh)
            {
                Debug.LogError("[GorillaSpawner] DiagnoseGroundBelow: INCOMPATIBILITÉ, MeshCollider non-convexe contre MeshCollider non-convexe entre '" + go.name + "' et '" + first.collider.name + "'.");
            }
            else
            {
            }
            Debug.DrawLine(origin, first.point, Color.red, 5f);
        }
        void VerifySpawned(GameObject go)
        {
            if (go == null) { Debug.LogError("[GorillaSpawner] VerifySpawned: ÉCHEC, GameObject null"); Log.Info("Spawner.VerifySpawned: ÉCHEC, GameObject null"); return; }
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            bool nameOk = !string.IsNullOrWhiteSpace(go.name);
            bool sceneOk = go.scene.IsValid() && go.scene.isLoaded;
            string sceneName = go.scene.IsValid() ? go.scene.name : "SCÈNE INVALIDE";
            string msg = "Spawner.VerifySpawned: nom='" + go.name + "' scène='" + sceneName + "' actif=" + go.activeInHierarchy + " pos=" + go.transform.position + " renderers=" + renderers.Length;
            Debug.Log("[GorillaSpawner] " + msg); Log.Info(msg);
            if (!nameOk) Debug.LogError("[GorillaSpawner] VerifySpawned: ALERTE nom vide !");
            if (!sceneOk) Debug.LogError("[GorillaSpawner] VerifySpawned: ALERTE scène invalide !");
            if (renderers.Length == 0) Debug.LogError("[GorillaSpawner] VerifySpawned: ALERTE aucun MeshRenderer !");
            if (!IsFinite(go.transform.position)) Debug.LogError("[GorillaSpawner] VerifySpawned: ALERTE position non-finie !");
        }

        /// <summary>
        /// Cache tous les objets spawnés et fige leur physique, en sauvegardant leur état
        /// (frozen/isKinematic) pour pouvoir le restaurer exactement à la réactivation.
        /// </summary>
        void HideAllObjects()
        {
            foreach (var so in _objects)
            {
                if (so.Root == null) continue;
                so.WasFrozenBeforeHide = so.Frozen;
                if (so.HeldBy != null) Release(so);
                if (so.Rb != null)
                {
                    so.Rb.velocity = Vector3.zero;
                    so.Rb.angularVelocity = Vector3.zero;
                    so.Rb.isKinematic = true;
                }
                so.Root.SetActive(false);
            }
        }

        /// <summary>
        /// Réaffiche tous les objets précédemment cachés et restaure leur état physique d'origine.
        /// </summary>
        void ShowAllObjects()
        {
            foreach (var so in _objects)
            {
                if (so.Root == null) continue;
                so.Root.SetActive(true);
                if (so.Rb != null) so.Rb.isKinematic = so.WasFrozenBeforeHide;
                so.LastPositionInit = false;
                so.LastStableInit = false;
            }
        }

        void Update()
        {
            bool nowEnabled = Enabled;
            if (nowEnabled != _wasEnabled)
            {
                _wasEnabled = nowEnabled;
                if (nowEnabled) ShowAllObjects();
                else HideAllObjects();
            }

            if (Keyboard.current != null && Keyboard.current[ToInputKey(keyToggleGrabDebug)].wasPressedThisFrame) ToggleNearestToCamera();

            if (!nowEnabled) return;

            foreach (var so in _objects)
            {
                if (so.Root == null || so.HeldBy == null) continue;
                so.Root.transform.position = so.HeldBy.position;
                so.Root.transform.rotation = so.HeldBy.rotation;
                if (!so.HandVelocityInit)
                {
                    so.HandPrevPosition = so.HeldBy.position;
                    so.HandPrevRotation = so.HeldBy.rotation;
                    so.HandVelocityInit = true;
                }
                else
                {
                    float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                    Vector3 vel = (so.HeldBy.position - so.HandPrevPosition) / dt;
                    Quaternion deltaRot = so.HeldBy.rotation * Quaternion.Inverse(so.HandPrevRotation);
                    deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
                    if (angleDeg > 180f) angleDeg -= 360f;
                    so.HandAngularVelocity = float.IsNaN(axis.x) ? Vector3.zero : axis * (angleDeg * Mathf.Deg2Rad / dt);
                    so.HandVelocityTick1 = so.HandVelocityTick0;
                    so.HandVelocityTick0 = vel;
                    so.HandPrevPosition = so.HeldBy.position;
                    so.HandPrevRotation = so.HeldBy.rotation;
                }
            }
            HandleGrabRaycast(_leftHand, false);
            HandleGrabRaycast(_rightHand, true);
            CheckFalling();
        }
        const float LostGroundTimeout = 1f;
        const int StableCheckInterval = 4;
        int _fallCheckFrameCounter;
        void CheckFalling()
        {
            _fallCheckFrameCounter++;
            foreach (var so in _objects)
            {
                if (so.Root == null) continue;
                if (so.HeldBy != null || so.Frozen)
                {
                    so.FallTimer = 0f; so.LastPositionInit = false;
                    so.LastGroundObject = null; so.LastGroundLostTime = -1f;
                    so.LastStablePosition = so.Root.transform.position;
                    so.LastStableRotation = so.Root.transform.rotation;
                    so.LastStableInit = true;
                    continue;
                }
                Vector3 pos = so.Root.transform.position;
                if (!so.LastPositionInit) { so.LastPosition = pos; so.LastPositionInit = true; so.FallTimer = 0f; }
                if (!so.LastStableInit) { so.LastStablePosition = pos; so.LastStableRotation = so.Root.transform.rotation; so.LastStableInit = true; }
                float verticalSpeed = (pos.y - so.LastPosition.y) / Mathf.Max(Time.deltaTime, 0.0001f);
                so.LastPosition = pos;
                bool mustCheckThisFrame = so.FallTimer > 0f
                    || Mathf.Abs(verticalSpeed) >= FallVelocityThreshold
                    || (_fallCheckFrameCounter + so.Root.GetInstanceID()) % StableCheckInterval == 0;
                if (!mustCheckThisFrame) continue;
                bool groundHit = Physics.Raycast(pos, Vector3.down, out RaycastHit hit, GroundCheckDistance, ~0);
                if (groundHit)
                {
                    so.LastGroundObject = hit.collider.gameObject;
                    so.LastGroundLostTime = -1f;
                    so.FallTimer = 0f;
                    if (Mathf.Abs(verticalSpeed) < FallVelocityThreshold)
                    {
                        so.LastStablePosition = pos;
                        so.LastStableRotation = so.Root.transform.rotation;
                    }
                    continue;
                }
                if (verticalSpeed < -FallVelocityThreshold)
                {
                    so.FallTimer += Time.deltaTime;
                    if (so.LastGroundLostTime < 0f) so.LastGroundLostTime = Time.time;
                    bool lastGroundStillActive = so.LastGroundObject != null && so.LastGroundObject.activeInHierarchy;
                    float timeSinceLost = Time.time - so.LastGroundLostTime;
                    if (!lastGroundStillActive || timeSinceLost >= LostGroundTimeout)
                    {
                        if (!so.Rb.isKinematic)
                        {
                            so.Rb.isKinematic = true;
                            so.Rb.velocity = Vector3.zero;
                            so.Rb.angularVelocity = Vector3.zero;
                            so.Root.transform.position = so.LastStablePosition;
                            so.Root.transform.rotation = so.LastStableRotation;
                            Debug.LogWarning("[GorillaSpawner] CheckFalling: '" + so.Root.name + "' figé + replacé à sa dernière position stable ("
                                + (!lastGroundStillActive ? "sol d'origine désactivé" : "aucun sol depuis " + timeSinceLost.ToString("0.0") + "s") + ").");
                        }
                        so.LastPosition = so.LastStablePosition;
                    }
                }
                else so.FallTimer = 0f;
            }
        }
        void LateUpdate()
        {
            if (!Enabled) return;
            foreach (var so in _objects)
            {
                if (so.Root == null || so.HeldBy != null || so.Frozen) continue;
                if (!so.Rb.isKinematic || so.LastGroundObject == null) continue;
                bool groundBackActive = so.LastGroundObject.activeInHierarchy;
                bool groundConfirmed = Physics.Raycast(so.Root.transform.position, Vector3.down, GroundCheckDistance, ~0);
                if (groundBackActive && groundConfirmed)
                {
                    so.Rb.isKinematic = false;
                    so.LastGroundLostTime = -1f;
                    Debug.Log("[GorillaSpawner] LateUpdate: '" + so.Root.name + "' - sol de retour, physique réactivée.");
                }
            }
        }
        void RelocateInFrontOfPlayer(SpawnedObject so)
        {
            GetFrontOfPlayerPose(out Vector3 pos, out Quaternion rot);
            if (!IsFinite(pos)) pos = Vector3.zero;
            so.Root.transform.position = pos;
            so.Root.transform.rotation = rot;
            if (so.Rb != null) { so.Rb.velocity = Vector3.zero; so.Rb.angularVelocity = Vector3.zero; }
            so.LastPosition = pos;
            so.LastPositionInit = true;
            VerifySpawned(so.Root);
        }
        void HandleGrabRaycast(Transform hand, bool rightHand)
        {
            if (hand == null) return;
            bool grip = GripDown(rightHand);
            SpawnedObject held = null;
            for (int i = 0; i < _objects.Count; i++) if (_objects[i].HeldBy == hand) { held = _objects[i]; break; }
            if (held != null) { if (!grip) Release(held); return; }
            if (!grip) return;
            SpawnedObject best = null; float bestVolume = float.MaxValue;
            foreach (var so in _objects)
            {
                if (so.Root == null || so.HeldBy != null) continue;
                var box = so.Root.GetComponent<BoxCollider>();
                Bounds grabBounds;
                if (box != null)
                {
                    Vector3 worldCenter = so.Root.transform.TransformPoint(box.center);
                    Vector3 worldSize = Vector3.Scale(box.size, so.Root.transform.lossyScale) * 1.1f;
                    grabBounds = new Bounds(worldCenter, worldSize);
                }
                else
                {
                    float d = Vector3.Distance(hand.position, so.Root.transform.position);
                    if (d < GrabMinDist || d > GrabMaxDist) continue;
                    grabBounds = new Bounds(so.Root.transform.position, Vector3.one * GrabMaxDist * 0.01f);
                }
                if (!grabBounds.Contains(hand.position)) continue;
                float volume = grabBounds.size.x * grabBounds.size.y * grabBounds.size.z;
                if (volume < bestVolume) { bestVolume = volume; best = so; }
            }
            if (best != null) Grab(best, hand);
        }
        void ToggleNearestToCamera()
        {
            if (!Enabled) return;
            var cam = UnityEngine.Camera.main;
            if (cam == null || _objects.Count == 0) return;
            SpawnedObject nearest = null; float best = float.MaxValue;
            foreach (var so in _objects)
            {
                if (so.Root == null) continue;
                float d = Vector3.Distance(cam.transform.position, so.Root.transform.position);
                if (d < best) { best = d; nearest = so; }
            }
            if (nearest == null) return;
            if (nearest.HeldBy != null) Release(nearest); else Grab(nearest, cam.transform);
        }
        Key ToInputKey(KeyCode kc) => (kc >= KeyCode.A && kc <= KeyCode.Z) ? Key.A + (kc - KeyCode.A) : Key.G;
        public void Grab(SpawnedObject so, Transform hand)
        {
            so.HeldBy = hand;
            if (so.Rb != null) { so.Rb.isKinematic = true; so.Rb.velocity = Vector3.zero; so.Rb.angularVelocity = Vector3.zero; }
            so.HandVelocityInit = false;
            so.HandVelocityTick0 = so.HandVelocityTick1 = Vector3.zero;
            if (so.Colliders != null)
                foreach (var col in so.Colliders) if (col != null) col.enabled = false;
        }
        public void Release(SpawnedObject so)
        {
            so.HeldBy = null;
            if (so.Colliders != null)
                foreach (var col in so.Colliders) if (col != null) col.enabled = true;
            if (so.Rb != null)
            {
                so.Rb.isKinematic = so.Frozen;
                if (!so.Frozen)
                {
                    Vector3 throwVelocity = (so.HandVelocityTick0 + so.HandVelocityTick1) * 0.5f;
                    so.Rb.velocity = throwVelocity;
                    so.Rb.angularVelocity = so.HandAngularVelocity;
                }
            }
            so.HandVelocityInit = false;
            so.HandVelocityTick0 = so.HandVelocityTick1 = Vector3.zero;
        }
        public void SetFrozen(SpawnedObject so, bool f)
        {
            if (!Enabled) return;
            so.Frozen = f;
            if (so.Rb != null && so.HeldBy == null)
            {
                so.Rb.isKinematic = f;
                if (f) { so.Rb.velocity = Vector3.zero; so.Rb.angularVelocity = Vector3.zero; }
            }
            if (so.Root != null)
                foreach (var t in so.Root.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = f ? 0 : DefaultCollisionLayer;
        }

        public void SetPosition(SpawnedObject so, Vector3 pos)
        {
            if (so.Root == null || !IsFinite(pos)) return;
            so.Root.transform.position = pos;
            if (so.Rb != null) { so.Rb.velocity = Vector3.zero; so.Rb.angularVelocity = Vector3.zero; }
            so.LastPosition = pos;
        }
        public void SetObjectLayer(SpawnedObject so, int layer)
        {
            if (so.Root == null) return;
            layer = Mathf.Clamp(layer, 0, 31);
            foreach (var t in so.Root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }
        public void SetRotationEuler(SpawnedObject so, Vector3 euler)
        {
            if (so.Root == null) return;
            so.Root.transform.eulerAngles = euler;
        }
        public void SetScale(SpawnedObject so, Vector3 scale)
        {
            if (!Enabled) return;
            if (so.Root == null) return;
            scale = new Vector3(Mathf.Clamp(scale.x, 0.1f, 50f), Mathf.Clamp(scale.y, 0.1f, 50f), Mathf.Clamp(scale.z, 0.1f, 50f));
            so.Scale = scale;
            so.Root.transform.localScale = scale;
        }
        public void Respawn(SpawnedObject so)
        {
            if (!Enabled) return;
            if (so.Root == null) return;
            GetFrontOfPlayerPose(out Vector3 pos, out Quaternion rot);
            if (!IsFinite(pos)) pos = Vector3.zero;
            so.Root.transform.position = pos;
            so.Root.transform.rotation = rot;
            if (so.Rb != null) { so.Rb.velocity = Vector3.zero; so.Rb.angularVelocity = Vector3.zero; }
            so.LastPosition = pos;
            so.LastPositionInit = true;
            Debug.Log("[GorillaSpawner] Respawn: '" + so.Root.name + "' remis devant le joueur.");
        }
        public void DeleteObject(SpawnedObject so)
        {
            if (!Enabled) return;
            if (so.Root != null) Destroy(so.Root); _objects.Remove(so);
        }
        public void DeleteAll()
        {
            if (!Enabled) return;
            foreach (var so in _objects.ToList()) if (so.Root != null) Destroy(so.Root);
            _objects.Clear();
        }
        public void SaveAll()
        {
            try
            {
                var lines = new List<string>();
                foreach (var so in _objects)
                {
                    if (so.Root == null) continue;
                    var p = so.Root.transform.position; var r = so.Root.transform.eulerAngles; var sc = so.Root.transform.localScale;
                    lines.Add(string.Join("|", new[] {
                        so.SourcePath,
                        p.x.ToString(Inv), p.y.ToString(Inv), p.z.ToString(Inv),
                        r.x.ToString(Inv), r.y.ToString(Inv), r.z.ToString(Inv),
                        sc.x.ToString(Inv), sc.y.ToString(Inv), sc.z.ToString(Inv),
                        so.Frozen ? "1" : "0"
                    }));
                }
                File.WriteAllLines(_saveFile, lines);
            }
            catch (Exception e) { Log.Err("Spawner.SaveAll", e); }
        }
        void LoadSavedObjects()
        {
            if (!File.Exists(_saveFile)) return;
            try
            {
                var originalLines = File.ReadAllLines(_saveFile);
                var stillValidLines = new List<string>();
                foreach (var raw in originalLines)
                {
                    var parts = raw.Split('|');
                    if (parts.Length < 11) continue;
                    if (!File.Exists(parts[0]))
                    {
                        Debug.LogWarning("[GorillaSpawner] LoadSavedObjects: source manquante, supprimée: " + parts[0]);
                        Log.Info("Spawner.LoadSavedObjects: source manquante: " + parts[0]);
                        continue;
                    }
                    var go = LoadFromBundle(parts[0]);
                    if (go == null) continue;
                    go.transform.position = new Vector3(F(parts[1]), F(parts[2]), F(parts[3]));
                    go.transform.eulerAngles = new Vector3(F(parts[4]), F(parts[5]), F(parts[6]));
                    Vector3 savedScale = new Vector3(F(parts[7]), F(parts[8]), F(parts[9]));
                    if (savedScale == Vector3.zero) savedScale = Vector3.one;
                    go.transform.localScale = savedScale;
                    bool frozen = parts[10] == "1";
                    var rb = go.GetComponent<Rigidbody>(); if (rb == null) rb = go.AddComponent<Rigidbody>();
                    rb.useGravity = true; rb.isKinematic = frozen;
                    foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = frozen ? 0 : DefaultCollisionLayer;
                    _objects.Add(new SpawnedObject
                    {
                        Root = go,
                        Rb = rb,
                        SourcePath = parts[0],
                        Frozen = frozen,
                        Colliders = go.GetComponentsInChildren<Collider>(true),
                        Scale = savedScale
                    });
                    VerifySpawned(go);
                    stillValidLines.Add(raw);
                }
                if (stillValidLines.Count != originalLines.Length) File.WriteAllLines(_saveFile, stillValidLines);

                // Si le mod est désactivé au chargement (ex: réglage persistant), cache immédiatement
                // les objets rechargés pour rester cohérent avec l'état du switch de la page Mods.
                if (!Enabled) HideAllObjects();
                _wasEnabled = Enabled;
            }
            catch (Exception e) { Log.Err("Spawner.LoadSavedObjects", e); }
        }
        static float F(string s) => float.TryParse(s, System.Globalization.NumberStyles.Float, Inv, out float v) ? v : 0f;
        static System.Globalization.CultureInfo Inv => System.Globalization.CultureInfo.InvariantCulture;
        void DrawPage(MenuUI ui)
        {
            if (!_handsReady)
            {
                ui.BeginCard();
                ui.Label(I18n.T("Spawner.VRWarning"), Theme.Red, 9);
                ui.EndCard();
                ui.Space(4);
            }
            ui.Section(I18n.T("Spawner.Files") + " (" + _availableFiles.Count + " " + I18n.T("Spawner.Found") + ")");
            ui.BeginCard();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("Spawner.Search"), UI.Sty(Theme.Dim, 10), GUILayout.Width(75 * UI.Scale));
            _search = ui.TextField(_search);
            if (ui.SmallBtn(I18n.T("Spawner.Refresh"), ui.TB3, Theme.Text)) RefreshFileList();
            GUILayout.EndHorizontal();
            ui.Space(4);
            var filtered = _availableFiles.Where(f => string.IsNullOrEmpty(_search) || Path.GetFileName(f).ToLower().Contains(_search.ToLower())).ToList();
            if (filtered.Count == 0) ui.Label(I18n.T("Spawner.NoFiles"), Theme.Dim, 9);
            else
            {
                _scrollFiles = GUILayout.BeginScrollView(_scrollFiles, GUILayout.Height(100));
                for (int i = 0; i < filtered.Count; i++)
                {
                    bool sel = _availableFiles.IndexOf(filtered[i]) == _selectedFileIndex;
                    if (ui.Btn((sel ? "> " : "   ") + Path.GetFileName(filtered[i]), sel ? ui.TA : ui.TB3, sel ? Color.white : Theme.Text))
                        _selectedFileIndex = _availableFiles.IndexOf(filtered[i]);
                }
                GUILayout.EndScrollView();
            }
            ui.EndCard();

            if (File.Exists(Path.Combine(_dataFolder, AUTOBUNDLE_FILENAME)))
            {
                ui.Space(6);
                ui.BeginCard();
                ui.Label(I18n.T("Spawner.AutoBundleHelp"), Theme.Dim, 9, true);
                ui.Space(2);
                ui.Label(AUTOBUNDLE_FILENAME, Theme.Accent, 9);
                ui.Space(2);
                ui.Label(I18n.T("Spawner.AutoBundleHint"), Theme.Dim, 8);
                ui.EndCard();
            }
            ui.Space(6);
            bool canSpawn = _selectedFileIndex >= 0 && _selectedFileIndex < _availableFiles.Count;
            if (ui.Btn(canSpawn ? I18n.T("Spawner.Spawn") : I18n.T("Spawner.SelectFile"), canSpawn ? ui.TA : ui.TB3, Color.white) && canSpawn && Enabled)
                SpawnInFront(_availableFiles[_selectedFileIndex]);
            int heldCount = 0;
            for (int i = 0; i < _objects.Count; i++) if (_objects[i].HeldBy != null) heldCount++;
            ui.Space(6); ui.Section(I18n.T("Spawner.Objects") + " (" + _objects.Count + " · " + heldCount + " " + I18n.T("Spawner.Held") + ")");
            ui.BeginCard();
            if (_objects.Count == 0) ui.Label(I18n.T("Spawner.NoObjects"), Theme.Dim, 9);
            else
            {
                _scrollObjects = GUILayout.BeginScrollView(_scrollObjects, GUILayout.Height(260));
                for (int i = _objects.Count - 1; i >= 0; i--)
                {
                    var so = _objects[i];
                    if (so.Root == null) { _objects.RemoveAt(i); continue; }
                    ui.BeginCard(Theme.BG3);
                    GUILayout.BeginHorizontal();
                    ui.Label(so.Root.name, Theme.Accent, 10, true);
                    GUILayout.FlexibleSpace();
                    ui.Label(so.HeldBy != null ? I18n.T("Spawner.HeldBy") : I18n.T("Spawner.Free"), so.HeldBy != null ? Theme.Green : Theme.Dim, 9);
                    GUILayout.EndHorizontal();
                    bool fz = ui.Toggle(I18n.T("Spawner.Frozen"), so.Frozen, 90f); if (fz != so.Frozen && Enabled) SetFrozen(so, fz);
                    ui.Space(3);
                    var scale = so.Root.transform.localScale;
                    float currentMult = scale.x;
                    GUILayout.BeginHorizontal();
                    ui.Label(I18n.T("Spawner.Size"), Theme.Dim, 8, false, TextAnchor.MiddleLeft);
                    float sliderMult = GUILayout.HorizontalSlider(currentMult, 0.5f, 2f, GUILayout.ExpandWidth(true));
                    ui.Label("x" + currentMult.ToString("0.00"), Theme.Accent, 9, true, TextAnchor.MiddleRight);
                    GUILayout.EndHorizontal();
                    if (Enabled && !Mathf.Approximately(sliderMult, currentMult))
                        SetScale(so, new Vector3(sliderMult, sliderMult, sliderMult));
                    ui.Space(4);
                    GUILayout.BeginHorizontal();
                    if (ui.SmallBtn(I18n.T("Spawner.Respawn"), ui.TA, Color.white) && Enabled) Respawn(so);
                    if (ui.SmallBtn(I18n.T("Spawner.Delete"), ui.TR, Color.white) && Enabled) DeleteObject(so);
                    GUILayout.EndHorizontal();
                    ui.EndCard();
                }
                GUILayout.EndScrollView();
            }
            ui.EndCard();
        }
    }
    public class SpawnedObject
    {
        public GameObject Root;
        public Rigidbody Rb;
        public string SourcePath;
        public float FallTimer = 0f;
        public bool Frozen, LastPositionInit = false;
        public Transform HeldBy;
        public Vector3 LastPosition;
        public Collider[] Colliders;
        public Vector3 Scale = Vector3.one;
        public GameObject LastGroundObject;
        public float LastGroundLostTime = -1f;
        public Vector3 LastStablePosition;
        public Quaternion LastStableRotation = Quaternion.identity;
        public bool LastStableInit;
        public Vector3 HandVelocityTick0, HandVelocityTick1;
        public Vector3 HandPrevPosition;
        public Quaternion HandPrevRotation;
        public Vector3 HandAngularVelocity;
        public bool HandVelocityInit;

        /// <summary>Etat "frozen" mémorisé juste avant que le mod soit désactivé, pour être restauré exactement à la réactivation.</summary>
        public bool WasFrozenBeforeHide;
    }
}