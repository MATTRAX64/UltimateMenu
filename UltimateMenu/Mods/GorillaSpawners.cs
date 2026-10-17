#region GorillaSpawners
namespace UltimateMenu
{
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

    public partial class GorillaSpawners : MonoBehaviour
    {
        public static GorillaSpawners Instance { get; private set; }
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Spawner.Title");
        bool _wsE = true;

        string _dataFolder, _saveFile, _search = "";
        readonly List<string> _unresolvedObjects = new List<string>();
        readonly HashSet<Collider> _generatedFallbacks=new HashSet<Collider>();
        readonly List<SpawnedObject> _objects = new List<SpawnedObject>();
        readonly List<string> _availFiles = new List<string>();
        readonly Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();
        int _selFileIdx = -1;
        float _lastSpawnT = -999f;
        bool _handsReady;
        bool _spawnFrozen;
        const float GrabMinDist = 0.01f, GrabMaxDist = 1f, SpawnCooldown = 0.5f, FallTimeoutSeconds = 10f, FallVelocityThreshold = 0.05f;
        public KeyCode keyToggleGrabDebug = KeyCode.G;
        Transform _leftHand, _rightHand, _playerHead;
        static int _spawnCounter;
        Vector2 _scrollFiles, _scrollObjects;
        LayerMask _groundMask = -1;
        const float GroundCheckDistance = 50f;
        const string SafeShaderName = "Universal Render Pipeline/Lit";
        static Shader _cachedSafeShader;
        static bool _safeShaderSearched;
        static readonly string[] TexPropCandidates = { "_MainTex", "_BaseMap", "_BaseColorMap", "_AlbedoMap", "_Albedo", "_Tex", "_ColorMap", "_Diffuse" };
        static readonly string[] ColorPropCandidates = { "_Color", "_BaseColor", "_AlbedoColor", "_MainColor", "_TintColor" };

        const string AUTOBUNDLE_RESOURCE = "UltimateMenu.Assets.GorillaSpawners.AutoBundle.unitypackage";
        const string AUTOBUNDLE_FILENAME = "AutoBundle.unitypackage";

        void Awake() { Instance = this; InitializeObjectNetwork(); CreateDataFolder(); EnsureAutoBundleExtracted(); RefreshFileList(); }

        void Start()
        {
            MenuUI.Instance?.RegisterPage("Spawner.Title", DrawPage, MenuCategory.Experimental);
            InitGroundMask();
            LoadSavedObjects();
            GorillaTagger.OnPlayerSpawned(FindPlayerRig);
            InvokeRepeating(nameof(AutoSave), AutoSaveIntervalSeconds, AutoSaveIntervalSeconds);
        }

        const float AutoSaveIntervalSeconds = 5f;
        float _saveAfter=-1f;
        void RequestSave(){_saveAfter=Time.unscaledTime+.35f;}
        void AutoSave() { if (_objects.Count > 0 || _unresolvedObjects.Count > 0) SaveAll(); }
        void OnApplicationQuit()=>SaveAll();

        void OnDestroy()
        {
            CleanupObjectNetwork();
            SaveAll();
            foreach (var kv in _loadedBundles) if (kv.Value != null) kv.Value.Unload(false);
            _loadedBundles.Clear();
        }

        void InitGroundMask()
        {
            try
            {
                if (GorillaLocomotion.GTPlayer.Instance != null)
                {
                    _groundMask = GorillaLocomotion.GTPlayer.Instance.locomotionEnabledLayers;
                    foreach(var so in _objects)ConfigurePhysics(so);
                    Debug.Log("[GorillaSpawner] InitGroundMask: layer mask récupéré = " + _groundMask.value);
                }
                else { Debug.LogWarning("[GorillaSpawner] InitGroundMask: Player.Instance encore null, retry dans 1s."); Invoke(nameof(InitGroundMask), 1f); }
            }
            catch (Exception e) { Log.Err("Spawner.InitGroundMask", e); }
        }

        void CreateDataFolder()
        {
            string dir = ReleaseUpdater.DataFolder;
            _dataFolder = Path.Combine(dir, "GorillaSpawners");
            if (!Directory.Exists(_dataFolder)) Directory.CreateDirectory(_dataFolder);
            _saveFile = Path.Combine(_dataFolder, "saved_objects.txt");
        }

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
                        Log.Err("Spawner.EnsureAutoBundleExtracted", new Exception("Ressource embarquée introuvable : " + AUTOBUNDLE_RESOURCE + ". Vérifie que AutoBundle.unitypackage est bien marqué 'Embedded Resource' dans le projet."));
                        return;
                    }
                    using (var fileStream = File.Create(targetPath)) stream.CopyTo(fileStream);
                }
                Log.Info("[GorillaSpawner] AutoBundle.unitypackage extrait vers " + targetPath);
            }
            catch (Exception e) { Log.Err("Spawner.EnsureAutoBundleExtracted", e); }
        }

        void RefreshFileList()
        {
            _availFiles.Clear();
            if (!Directory.Exists(_dataFolder)) return;
            foreach (var f in Directory.GetFiles(_dataFolder, "*.bundle")) _availFiles.Add(f);
            _availFiles.Sort(StringComparer.OrdinalIgnoreCase);
        }

        void FindPlayerRig()
        {
            try
            {
                var rig = VRRig.LocalRig;
                if (rig != null)
                {
                    var head = rig.head.rigTarget ?? rig.transform.Find("rig/head");
                    var handL = rig.leftHand.rigTarget ?? rig.transform.Find("rig/hand.L");
                    var handR = rig.rightHand.rigTarget ?? rig.transform.Find("rig/hand.R");
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
            var device=InputDevices.GetDeviceAtXRNode(rightHand?XRNode.RightHand:XRNode.LeftHand);
            if(device.isValid)
            {
                if(device.TryGetFeatureValue(CommonUsages.gripButton,out bool pressed)&&pressed)return true;
                if(device.TryGetFeatureValue(CommonUsages.grip,out float grip)&&grip>=GripThreshold)return true;
            }
            var rig=VRRig.LocalRig;
            return rig!=null&&(rightHand?rig.rightMiddle.gripValue:rig.leftMiddle.gripValue)>=GripThreshold;
        }

        static Shader GetSafeShader()
        {
            if (_safeShaderSearched) return _cachedSafeShader;
            _safeShaderSearched = true;
            _cachedSafeShader = Shader.Find(SafeShaderName);
            if (_cachedSafeShader == null) Debug.LogError("[GorillaSpawner] Shader de secours '" + SafeShaderName + "' introuvable.");
            else Debug.Log("[GorillaSpawner] Shader de secours trouvé et mis en cache: " + SafeShaderName);
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
                    foreach (var candidate in TexPropCandidates)
                        if (mat.HasProperty(candidate)) { var t = mat.GetTexture(candidate); if (t != null) { sourceTex = t; break; } }
                    Color sourceColor = Color.white;
                    bool hasColor = false;
                    foreach (var candidate in ColorPropCandidates)
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
                        Debug.Log("[GorillaSpawner] -> Shader appliqué: " + safeShader.name + " sur " + r.name + " | texture=" + (sourceTex != null ? sourceTex.name : "aucune") + " | couleur=" + (hasColor ? sourceColor.ToString() : "défaut (blanc)"));
                    }
                    // Imported PBR defaults add polished highlights that do not match the matte map.
                    // Keep diffuse scene lighting and the imported albedo/colour.
                    foreach(var property in new[]{"_Metallic","_Smoothness","_Glossiness","_GlossMapScale","_SpecularHighlights","_EnvironmentReflections","_SmoothnessTextureChannel"})
                        if(mat.HasProperty(property))mat.SetFloat(property,0f);
                    if(mat.HasProperty("_SpecColor"))mat.SetColor("_SpecColor",Color.black);
                    foreach(var property in new[]{"_MetallicGlossMap","_SpecGlossMap"})
                        if(mat.HasProperty(property))mat.SetTexture(property,null);
                    mat.DisableKeyword("_METALLICGLOSSMAP");
                    mat.DisableKeyword("_SPECGLOSSMAP");
                    mat.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
                    mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    r.reflectionProbeUsage=UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }
                r.materials = mats;
            }
            foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
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
            if (meshFilters.Length == 0) Debug.LogWarning("[GorillaSpawner] AUCUN MeshFilter dans ce modèle : impossible de générer un collider, il n'y a aucun mesh exploitable. Vérifie que le bundle contient bien la géométrie (pas juste un objet vide/rig/os).");
            foreach (var mf in meshFilters)
            {
                string path = GetHierarchyPath(mf.transform, instance.transform);
                if (mf.sharedMesh == null) Debug.LogWarning("[GorillaSpawner]   - MeshFilter SANS mesh sur '" + path + "' (sharedMesh == null, ignoré).");
                else Debug.Log("[GorillaSpawner]   - MeshFilter OK sur '" + path + "' : mesh='" + mf.sharedMesh.name + "' verts=" + mf.sharedMesh.vertexCount + " hasCollider=" + (mf.gameObject.GetComponent<Collider>() != null));
            }
            if (existingColliders.Length > 0)
                foreach (var col in existingColliders)
                    Debug.Log("[GorillaSpawner]   - Collider existant '" + col.GetType().Name + "' sur '" + GetHierarchyPath(col.transform, instance.transform) + "' enabled=" + col.enabled);
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
                foreach (var col in existingColliders) { col.enabled = true; if (climbLayer >= 0) col.gameObject.layer = climbLayer; }
                bool hasNonConvexMesh = existingColliders.Any(c => c is MeshCollider mc && !mc.convex);
                if (hasNonConvexMesh) AddFallbackConvexCollider(instance);
                if (climbLayer >= 0) instance.layer = climbLayer;
                Debug.Log("[GorillaSpawner] AddMeshCollidersForClimbing: " + existingColliders.Length + " collider(s) intégré(s) au modèle réutilisé(s) sur '" + instance.name + "', layer climb=" + climbLayer + ", boxColliderSecours=" + hasNonConvexMesh);
                return;
            }
            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            int added = 0, skippedNoMesh = 0;
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) { Debug.LogWarning("[GorillaSpawner] AddMeshCollidersForClimbing: MeshFilter sans mesh sur " + mf.name); skippedNoMesh++; continue; }
                var go = mf.gameObject;
                if (go.GetComponent<Collider>() == null) { var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = false; added++; }
                if (climbLayer >= 0) go.layer = climbLayer;
            }
            AddFallbackConvexCollider(instance);
            if (climbLayer >= 0) instance.layer = climbLayer;
            if (added == 0 && filters.Length == 0)
                Debug.LogError("[GorillaSpawner] AddMeshCollidersForClimbing: ÉCHEC, aucun MeshFilter ni Collider trouvé sur '" + instance.name + "'. Le modèle est probablement vide, ou toute sa géométrie est dans des enfants inactifs/désactivés qui n'ont pas été réactivés.");
            else if (added == 0 && skippedNoMesh == filters.Length)
                Debug.LogError("[GorillaSpawner] AddMeshCollidersForClimbing: ÉCHEC, " + filters.Length + " MeshFilter trouvés mais tous avec sharedMesh == null sur '" + instance.name + "'.");
            Debug.Log("[GorillaSpawner] AddMeshCollidersForClimbing: aucun collider dans le bundle, " + added + " MeshCollider(s) généré(s) en fallback sur '" + instance.name + "', layer climb=" + climbLayer);
        }

        const int DefaultCollisionLayer = 8;
        void AddFallbackConvexCollider(GameObject instance)
        {
            _generatedFallbacks.RemoveWhere(collider=>collider==null);
            if (instance.GetComponent<BoxCollider>() != null) return;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { Debug.LogWarning("[GorillaSpawner] AddFallbackConvexCollider: aucun Renderer trouvé sur '" + instance.name + "', BoxCollider de secours non créé."); return; }
            Bounds localBounds=new Bounds();bool hasBounds=false;
            foreach(var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null)continue;
                Bounds bounds=filter.sharedMesh.bounds;
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 point=bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                    point=instance.transform.InverseTransformPoint(filter.transform.TransformPoint(point));
                    if(!hasBounds){localBounds=new Bounds(point,Vector3.zero);hasBounds=true;}else localBounds.Encapsulate(point);
                }
            }
            if(!hasBounds)
                foreach(var renderer in renderers)for(int corner=0;corner<8;corner++)
                {
                    var bounds=renderer.bounds;
                    Vector3 point=bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                    point=instance.transform.InverseTransformPoint(point);
                    if(!hasBounds){localBounds=new Bounds(point,Vector3.zero);hasBounds=true;}else localBounds.Encapsulate(point);
                }
            Vector3 localCenter=localBounds.center,localSize=localBounds.size;
            var box = instance.AddComponent<BoxCollider>();
            box.center = localCenter; box.size = Vector3.Max(localSize,Vector3.one*.01f);
            _generatedFallbacks.Add(box);
            Debug.Log("[GorillaSpawner] AddFallbackConvexCollider: BoxCollider ajouté sur '" + instance.name + "' center=" + localCenter + " size=" + localSize);
        }

        int GetMapClimbLayer()
        {
            if(GTPlayer.Instance!=null)_groundMask=GTPlayer.Instance.locomotionEnabledLayers;
            if(_groundMask.value==0||_groundMask.value==-1)return DefaultCollisionLayer;
            if((_groundMask.value&(1<<DefaultCollisionLayer))!=0)return DefaultCollisionLayer;
            for (int i = 0; i < 32; i++)
                if ((_groundMask.value & (1 << i)) != 0 && !string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return DefaultCollisionLayer;
        }
        int GetHeldLayer()
        {
            if(GTPlayer.Instance!=null)_groundMask=GTPlayer.Instance.locomotionEnabledLayers;
            // Locomotion uses raycasts, which do not respect IgnoreCollision pairs.
            // Held objects stay physical against the map but are excluded from hand locomotion rays.
            if((_groundMask.value&(1<<2))==0)return 2;
            for(int layer=0;layer<32;layer++)if((_groundMask.value&(1<<layer))==0)return layer;
            return 2;
        }
        void ConfigurePhysics(SpawnedObject so)
        {
            if(so.Root==null)return;
            int layer=so.Frozen?GetMapClimbLayer():GetHeldLayer();
            foreach(var t in so.Root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=layer;
            bool simulated=!so.Frozen;
            if(simulated&&so.Colliders!=null)foreach(var collider in so.Colliders)
                if(collider is MeshCollider mesh&&!mesh.convex)mesh.enabled=false;
            if(so.Rb!=null)
            {
                so.Rb.isKinematic=!simulated;
                so.Rb.useGravity=!so.Frozen&&so.HeldBy==null;
                so.Rb.collisionDetectionMode=so.Rb.isKinematic?CollisionDetectionMode.Discrete:CollisionDetectionMode.ContinuousDynamic;
                so.Rb.interpolation=RigidbodyInterpolation.Interpolate;
                so.Rb.solverIterations=12;so.Rb.solverVelocityIterations=8;
                so.Rb.maxDepenetrationVelocity=2f;
                so.Rb.detectCollisions=true;
                so.Rb.includeLayers=_groundMask.value==0||_groundMask.value==-1?0:_groundMask.value;so.Rb.excludeLayers=0;
            }
            // Keep the same primitive/convex geometry as the released object: imported concave meshes can have missing or one-sided surfaces.
            if(so.Colliders!=null)foreach(var collider in so.Colliders)if(collider!=null)
            {
                if(collider is MeshCollider concave&&!concave.convex){collider.enabled=false;collider.isTrigger=false;continue;}
                collider.isTrigger=false;
                collider.enabled=true;
            }
        }

        GameObject LoadFromBundle(string path)
        {
            Debug.Log("[GorillaSpawner] LoadFromBundle: START path=" + path);
            try
            {
                if (!File.Exists(path)) { Debug.LogError("[GorillaSpawner] LoadFromBundle: fichier introuvable " + path); Log.Info("Spawner.LoadFromBundle: fichier introuvable " + path); return null; }
                AssetBundle bundle;
                if (_loadedBundles.TryGetValue(path, out bundle) && bundle != null)
                    Debug.Log("[GorillaSpawner] LoadFromBundle: bundle déjà en cache pour " + path);
                else
                {
                    Debug.Log("[GorillaSpawner] LoadFromBundle: fichier trouvé (" + new FileInfo(path).Length + " octets), appel AssetBundle.LoadFromFile...");
                    try { bundle = AssetBundle.LoadFromFile(path); }
                    catch (Exception bundleEx) { Debug.LogError("[GorillaSpawner] LoadFromBundle: EXCEPTION LoadFromFile: " + bundleEx); Log.Err("Spawner.LoadFromBundle(LoadFromFile)", bundleEx); return null; }
                    if (bundle == null) { Debug.LogError("[GorillaSpawner] LoadFromBundle: LoadFromFile a renvoyé null. Vérifie que le bundle a été buildé avec Unity 6000.2.9f1 / StandaloneWindows64."); Log.Info("Spawner.LoadFromBundle: bundle null pour " + path); return null; }
                    _loadedBundles[path] = bundle;
                    Debug.Log("[GorillaSpawner] LoadFromBundle: bundle chargé OK, assetNames=[" + string.Join(", ", bundle.GetAllAssetNames()) + "]");
                }
                var assetNames = bundle.GetAllAssetNames();
                if (assetNames == null || assetNames.Length == 0) { Debug.LogError("[GorillaSpawner] LoadFromBundle: aucun asset dans " + path); Log.Info("Spawner.LoadFromBundle: aucun asset dans " + path); return null; }
                GameObject prefab = null; string usedAssetName = null;
                foreach (var assetName in assetNames) { var candidate = bundle.LoadAsset<GameObject>(assetName); if (candidate != null) { prefab = candidate; usedAssetName = assetName; break; } }
                if (prefab == null) { Debug.LogError("[GorillaSpawner] LoadFromBundle: aucun GameObject parmi " + assetNames.Length + " asset(s)"); Log.Info("Spawner.LoadFromBundle: aucun GameObject dans " + path); return null; }
                Debug.Log("[GorillaSpawner] LoadFromBundle: prefab trouvé, asset='" + usedAssetName + "', instanciation...");
                var instance = Instantiate(prefab);
                if (instance == null) { Debug.LogError("[GorillaSpawner] LoadFromBundle: Instantiate a renvoyé null"); Log.Info("Spawner.LoadFromBundle: Instantiate null pour " + path); return null; }
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
            catch (Exception e) { Debug.LogError("[GorillaSpawner] LoadFromBundle: EXCEPTION GÉNÉRALE " + e); Log.Err("Spawner.LoadFromBundle", e); return null; }
        }

        bool GetFrontOfPlayerPose(out Vector3 pos, out Quaternion rot)
        {
            if (_playerHead != null) { pos = _playerHead.position + _playerHead.forward * 1.2f; rot = Quaternion.LookRotation(_playerHead.forward, Vector3.up); return true; }
            var cam = UnityEngine.Camera.main;
            if (cam != null) { pos = cam.transform.position + cam.transform.forward * 1.2f; rot = cam.transform.rotation; Debug.LogWarning("[GorillaSpawner] GetFrontOfPlayerPose: tête joueur introuvable, fallback caméra"); return true; }
            pos = Vector3.zero; rot = Quaternion.identity;
            return false;
        }

        public void SpawnInFront(string path)
        {
            if (!Enabled) return;
            if (Time.time - _lastSpawnT < SpawnCooldown) { Debug.LogWarning("[GorillaSpawner] SpawnInFront: ignoré (cooldown), appel trop rapproché"); return; }
            _lastSpawnT = Time.time;
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
            if (go == null) { Debug.LogError("[GorillaSpawner] SpawnAt: LoadFromBundle null, annulé pour " + path); Log.Info("Spawner.SpawnAt: annulé"); return; }
            var activeScene = SceneManager.GetActiveScene();
            if (go.scene != activeScene) { Debug.LogWarning("[GorillaSpawner] SpawnAt: hors scène active, déplacement forcé."); SceneManager.MoveGameObjectToScene(go, activeScene); }
            go.transform.position = pos; go.transform.rotation = rot; go.transform.localScale = Vector3.one;
            var rb = go.GetComponent<Rigidbody>(); if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.useGravity = true; rb.isKinematic = _spawnFrozen;
            _objects.Add(new SpawnedObject { Root = go, Rb = rb, SourcePath = path, Frozen = _spawnFrozen, Colliders = go.GetComponentsInChildren<Collider>(true) });
            ConfigurePhysics(_objects[_objects.Count-1]);
            DontDestroyOnLoad(go);
            VerifySpawned(go);
            DiagnoseGroundBelow(go);
            SaveAll();
        }

        void DiagnoseGroundBelow(GameObject go)
        {
            Vector3 origin = go.transform.position;
            var hits = Physics.RaycastAll(origin, Vector3.down, 100f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            if (hits.Length == 0) { Debug.LogError("[GorillaSpawner] DiagnoseGroundBelow: AUCUN collider trouvé sous '" + go.name + "' depuis " + origin + " sur 100m vers le bas."); return; }
            var first = hits[0];
            bool isConvex = first.collider is MeshCollider mcFirst && mcFirst.convex;
            var ownColliders = go.GetComponentsInChildren<Collider>(true);
            bool ownHasNonConvexMesh = ownColliders.Any(c => c is MeshCollider mc && !mc.convex);
            bool groundIsNonConvexMesh = first.collider is MeshCollider mcGround && !mcGround.convex;
            Debug.Log("[GorillaSpawner] DIAGNOSTIC SOL sous '" + go.name + "' : touche '" + first.collider.name + "' (" + first.collider.GetType().Name + (isConvex ? ", convex" : "") + ") layer='" + LayerMask.LayerToName(first.collider.gameObject.layer) + "' a " + first.distance.ToString("0.00") + "m.");
            if (ownHasNonConvexMesh && groundIsNonConvexMesh) Debug.LogError("[GorillaSpawner] DiagnoseGroundBelow: INCOMPATIBILITÉ, MeshCollider non-convexe contre MeshCollider non-convexe entre '" + go.name + "' et '" + first.collider.name + "'.");
            Debug.DrawLine(origin, first.point, Color.red, 5f);
        }

        void VerifySpawned(GameObject go)
        {
            if (go == null) { Debug.LogError("[GorillaSpawner] VerifySpawned: ÉCHEC, GameObject null"); Log.Info("Spawner.VerifySpawned: ÉCHEC, GameObject null"); return; }
            var renderers = go.GetComponentsInChildren<MeshRenderer>();
            string sceneName = go.scene.IsValid() ? go.scene.name : "SCÈNE INVALIDE";
            string msg = "Spawner.VerifySpawned: nom='" + go.name + "' scène='" + sceneName + "' actif=" + go.activeInHierarchy + " pos=" + go.transform.position + " renderers=" + renderers.Length;
            Debug.Log("[GorillaSpawner] " + msg); Log.Info(msg);
            if (string.IsNullOrWhiteSpace(go.name)) Debug.LogError("[GorillaSpawner] VerifySpawned: ALERTE nom vide !");
            if (!(go.scene.IsValid() && go.scene.isLoaded)) Debug.LogError("[GorillaSpawner] VerifySpawned: ALERTE scène invalide !");
            if (renderers.Length == 0) Debug.LogError("[GorillaSpawner] VerifySpawned: ALERTE aucun MeshRenderer !");
            if (!IsFinite(go.transform.position)) Debug.LogError("[GorillaSpawner] VerifySpawned: ALERTE position non-finie !");
        }

        void HideAllObjects()
        {
            foreach (var so in _objects)
            {
                if (so.Root == null) continue;
                so.WasFrozenBeforeHide = so.Frozen;
                if (so.HeldBy != null) Release(so);
                if (so.Rb != null) { so.Rb.velocity = Vector3.zero; so.Rb.angularVelocity = Vector3.zero; so.Rb.isKinematic = true; }
                so.Root.SetActive(false);
            }
        }

        void ShowAllObjects()
        {
            foreach (var so in _objects)
            {
                if (so.Root == null) continue;
                so.Root.SetActive(true);
                ConfigurePhysics(so);
                so.LastPositionInit = false; so.LastStableInit = false;
            }
        }

        void Update()
        {
            UpdateObjectNetwork();
            if(_saveAfter>=0&&Time.unscaledTime>=_saveAfter)SaveAll();
            bool nowEn = Enabled;
            if (nowEn != _wsE) { _wsE = nowEn; if (nowEn) ShowAllObjects(); else HideAllObjects(); }

            if (Keyboard.current != null && Keyboard.current[ToInputKey(keyToggleGrabDebug)].wasPressedThisFrame) ToggleNearestToCamera();

            if (!nowEn) return;

            HandleGrabRaycast(_leftHand, false);
            HandleGrabRaycast(_rightHand, true);
        }

        void FixedUpdate()
        {
            MoveNetworkObjects();
            if(!Enabled)return;
            foreach(var so in _objects)
            {
                if(so.Root==null||so.Rb==null||so.HeldBy==null)continue;
                Vector3 target=so.HeldBy.TransformPoint(so.GrabOffset);
                if(!IsFinite(target))continue;
                Vector3 targetVelocity=so.GrabTargetInitialized?Vector3.ClampMagnitude((target-so.LastGrabTarget)/Mathf.Max(Time.fixedDeltaTime,.001f),GrabSpring.MaxSpeed):Vector3.zero;
                so.LastGrabTarget=target;so.GrabTargetInitialized=true;
                Vector3 error=target-so.Rb.position,velocity=so.Rb.velocity-targetVelocity;
                so.Rb.AddForce(new Vector3(GrabSpring.Acceleration(error.x,velocity.x),GrabSpring.Acceleration(error.y,velocity.y),GrabSpring.Acceleration(error.z,velocity.z)),ForceMode.Acceleration);
                Quaternion rotation=(so.HeldBy.rotation*so.GrabRotation)*Quaternion.Inverse(so.Rb.rotation);
                if(rotation.w<0)rotation=new Quaternion(-rotation.x,-rotation.y,-rotation.z,-rotation.w);
                rotation.ToAngleAxis(out float angle,out Vector3 axis);
                if(IsFinite(axis)&&!float.IsNaN(angle))
                    so.Rb.AddTorque(Vector3.ClampMagnitude(axis*(angle*Mathf.Deg2Rad*240f)-so.Rb.angularVelocity*24f,180f),ForceMode.Acceleration);
                // Moving the hand through a wall cannot accumulate unlimited speed in the held body.
                if(so.Rb.velocity.sqrMagnitude>GrabSpring.MaxSpeed*GrabSpring.MaxSpeed)so.Rb.velocity=Vector3.ClampMagnitude(so.Rb.velocity,GrabSpring.MaxSpeed);
            }
        }
        void HandleGrabRaycast(Transform hand, bool rightHand)
        {
            if (hand == null) return;
            bool grip = GripDown(rightHand);
            SpawnedObject held = null;
            for (int i = 0; i < _objects.Count; i++) if (_objects[i].HeldBy == hand) { held = _objects[i]; break; }
            if (held != null) { if (!grip) Release(held); return; }
            if (!grip) return;
            SpawnedObject best = null; float bestDistance=.12f*.12f;
            foreach (var so in _objects)
            {
                if (so.Root == null || so.Frozen || so.HeldBy != null) continue;
                if(so.Colliders==null)continue;
                foreach(var collider in so.Colliders)
                {
                    if(collider==null||!collider.enabled||!collider.gameObject.activeInHierarchy)continue;
                    // ClosestPoint supports primitive and convex colliders; use the mesh bounds for concave static meshes.
                    Vector3 contact=collider is MeshCollider mesh&&!mesh.convex?collider.bounds.ClosestPoint(hand.position):collider.ClosestPoint(hand.position);
                    float distance=(contact-hand.position).sqrMagnitude;
                    if(distance<=bestDistance){bestDistance=distance;best=so;}
                }
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
                if (so.Root == null || so.Frozen) continue;
                float d = Vector3.Distance(cam.transform.position, so.Root.transform.position);
                if (d < best) { best = d; nearest = so; }
            }
            if (nearest == null) return;
            if (nearest.HeldBy != null) Release(nearest); else Grab(nearest, cam.transform);
        }

        Key ToInputKey(KeyCode kc) => (kc >= KeyCode.A && kc <= KeyCode.Z) ? Key.A + (kc - KeyCode.A) : Key.G;

        public void Grab(SpawnedObject so, Transform hand)
        {
            if(so.Root==null||hand==null||so.Frozen||so.HeldBy!=null)return;
            so.GrabOffset=hand.InverseTransformPoint(so.Root.transform.position);
            so.GrabRotation=Quaternion.Inverse(hand.rotation)*so.Root.transform.rotation;
            so.GrabTargetInitialized=false;
            so.HeldBy = hand;
            if(so.Rb!=null){so.GrabConstraints=so.Rb.constraints;so.Rb.constraints=RigidbodyConstraints.None;}
            ConfigurePhysics(so);
            if(so.Rb!=null){so.Rb.velocity=Vector3.zero;so.Rb.angularVelocity=Vector3.zero;so.Rb.maxAngularVelocity=20f;so.Rb.WakeUp();}
            so.HandVelocityInit = false;
            so.HandVelocityTick0 = so.HandVelocityTick1 = Vector3.zero;
            var player=GTPlayer.Instance;
            var ownColliders=new List<Collider>();
            if(player!=null)ownColliders.AddRange(player.GetComponentsInChildren<Collider>(true));
            if(VRRig.LocalRig!=null)ownColliders.AddRange(VRRig.LocalRig.GetComponentsInChildren<Collider>(true));
            if(so.Colliders!=null)
                foreach(var collider in so.Colliders)if(collider!=null)
                    foreach(var own in ownColliders.Distinct())
                    {
                        if(own==null||own==collider)continue;
                        so.GrabIgnored.Add(new GrabCollisionPair{Object=collider,Player=own,WasIgnored=Physics.GetIgnoreCollision(collider,own)});
                        Physics.IgnoreCollision(collider,own,true);
                    }
        }

        public void Release(SpawnedObject so)
        {
            // Keep the simulated linear and angular momentum when the hand releases.
            Vector3 velocity=so.Rb!=null?so.Rb.velocity:Vector3.zero;
            Vector3 angularVelocity=so.Rb!=null?so.Rb.angularVelocity:Vector3.zero;
            so.HeldBy = null;
            ConfigurePhysics(so);
            foreach(var pair in so.GrabIgnored)if(pair.Object!=null&&pair.Player!=null)Physics.IgnoreCollision(pair.Object,pair.Player,pair.WasIgnored);
            so.GrabIgnored.Clear();
            if(so.Rb!=null)
            {
                so.Rb.constraints=so.GrabConstraints;
                if(!so.Rb.isKinematic){so.Rb.velocity=velocity;so.Rb.angularVelocity=angularVelocity;so.Rb.WakeUp();}
            }
            // Released objects keep their map contacts and fall under gravity.
            so.HandVelocityInit = false;
            so.HandVelocityTick0 = so.HandVelocityTick1 = Vector3.zero;
            SaveAll();
        }

        public void SetFrozen(SpawnedObject so, bool f)
        {
            if (!Enabled) return;
            if(f&&so.HeldBy!=null)Release(so);
            so.Frozen = f;
            if (f&&so.Rb!=null&&!so.Rb.isKinematic){so.Rb.velocity=Vector3.zero;so.Rb.angularVelocity=Vector3.zero;}
            ConfigurePhysics(so);SaveAll();
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
            foreach (var t in so.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        public void SetRotationEuler(SpawnedObject so, Vector3 euler) { if (so.Root != null) so.Root.transform.eulerAngles = euler; }

        public void SetScale(SpawnedObject so, Vector3 scale)
        {
            if (!Enabled || so.Root == null) return;
            scale = new Vector3(Mathf.Clamp(scale.x, 0.1f, 50f), Mathf.Clamp(scale.y, 0.1f, 50f), Mathf.Clamp(scale.z, 0.1f, 50f));
            so.Scale = scale;
            so.Root.transform.localScale = scale;
            RequestSave();
        }

        public void Respawn(SpawnedObject so)
        {
            if (!Enabled || so.Root == null) return;
            GetFrontOfPlayerPose(out Vector3 pos, out Quaternion rot);
            if (!IsFinite(pos)) pos = Vector3.zero;
            so.Root.transform.position = pos; so.Root.transform.rotation = rot;
            if (so.Rb != null) { so.Rb.velocity = Vector3.zero; so.Rb.angularVelocity = Vector3.zero; }
            so.LastPosition = pos; so.LastPositionInit = true;
            Debug.Log("[GorillaSpawner] Respawn: '" + so.Root.name + "' remis devant le joueur.");
            SaveAll();
        }

        public void DeleteObject(SpawnedObject so) { if (!Enabled) return; if (so.Root != null) Destroy(so.Root); _objects.Remove(so);SaveAll(); }

        public void DeleteAll()
        {
            if (!Enabled) return;
            _unresolvedObjects.Clear();
            foreach (var so in _objects.ToList()) if (so.Root != null) Destroy(so.Root);
            _objects.Clear();
            SaveAll();
        }

        public void SaveAll()
        {
            _saveAfter=-1f;
            try
            {
                var lines = new List<string>(_unresolvedObjects);
                foreach (var so in _objects)
                {
                    if (so.Root == null){if(!string.IsNullOrEmpty(so.SavedState))lines.Add(so.SavedState);continue;}
                    var p = so.Root.transform.position; var r = so.Root.transform.eulerAngles; var sc = so.Root.transform.localScale;
                    so.SavedState=string.Join("|", new[] { "@" + Path.GetFileName(so.SourcePath), p.x.ToString(Inv), p.y.ToString(Inv), p.z.ToString(Inv), r.x.ToString(Inv), r.y.ToString(Inv), r.z.ToString(Inv), sc.x.ToString(Inv), sc.y.ToString(Inv), sc.z.ToString(Inv), so.Frozen ? "1" : "0", so.NetworkShared?"1":"0" });
                    lines.Add(so.SavedState);
                }
                MenuPrefs.SetString("MUM_SpawnObjects", string.Join("\n", lines));
                MenuPrefs.Save();
            }
            catch (Exception e) { Log.Err("Spawner.SaveAll", e); }
        }

        void LoadSavedObjects()
        {

            try
            {
                string legacy = !MenuPrefs.HasKey("MUM_SpawnObjects") && File.Exists(_saveFile) ? File.ReadAllText(_saveFile) : "";
                string saved = MenuPrefs.GetString("MUM_SpawnObjects", legacy);
                var originalLines = saved.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                _unresolvedObjects.Clear();
                var stillValidLines = new List<string>();
                foreach (var raw in originalLines)
                {
                    var parts = raw.Split('|');
                    if (parts.Length < 11) continue;
                    string source = parts[0].StartsWith("@") ? Path.Combine(_dataFolder, Path.GetFileName(parts[0].Substring(1))) : parts[0];
                    if (!File.Exists(source)) source = Path.Combine(_dataFolder, Path.GetFileName(source));
                    if (!File.Exists(source)) { _unresolvedObjects.Add(raw); Debug.LogWarning("[GorillaSpawner] LoadSavedObjects: source manquante, supprimée: " + parts[0]); Log.Info("Spawner.LoadSavedObjects: source manquante: " + parts[0]); continue; }
                    var go = LoadFromBundle(source);
                    if (go == null) { _unresolvedObjects.Add(raw); continue; }
                    go.transform.position = new Vector3(F(parts[1]), F(parts[2]), F(parts[3]));
                    go.transform.eulerAngles = new Vector3(F(parts[4]), F(parts[5]), F(parts[6]));
                    Vector3 savedScale = new Vector3(F(parts[7]), F(parts[8]), F(parts[9]));
                    if (savedScale == Vector3.zero) savedScale = Vector3.one;
                    go.transform.localScale = savedScale;
                    bool frozen = parts[10] == "1";
                    var rb = go.GetComponent<Rigidbody>(); if (rb == null) rb = go.AddComponent<Rigidbody>();
                    rb.useGravity = true; rb.isKinematic = frozen;
                    _objects.Add(new SpawnedObject { Root = go, Rb = rb, SourcePath = source, Frozen = frozen, NetworkShared=parts.Length>11&&parts[11]=="1", Colliders = go.GetComponentsInChildren<Collider>(true), Scale = savedScale,SavedState=raw });
                    ConfigurePhysics(_objects[_objects.Count-1]);DontDestroyOnLoad(go);
                    VerifySpawned(go);
                    stillValidLines.Add(raw);
                }
                MenuPrefs.SetString("MUM_SpawnObjects", saved);

                if (!Enabled) HideAllObjects();
                _wsE = Enabled;
            }
            catch (Exception e) { Log.Err("Spawner.LoadSavedObjects", e); }
        }

        static float F(string s) => float.TryParse(s, System.Globalization.NumberStyles.Float, Inv, out float v) ? v : 0f;
        static System.Globalization.CultureInfo Inv => System.Globalization.CultureInfo.InvariantCulture;

        void DrawPage(MenuUI ui)
        {
            if(ui.Btn(I18n.T("Spawner.SaveState"),ui.TB3,Theme.Text))SaveAll();
            if (!_handsReady) { ui.BeginCard(); ui.Label(I18n.T("Spawner.VRWarning"), Theme.Red, 9); ui.EndCard(); ui.Space(4); }
            ui.Section(I18n.T("Spawner.Files") + " (" + _availFiles.Count + " " + I18n.T("Spawner.Found") + ")");
            ui.BeginCard();
            ui.Label(I18n.T("Spawner.Search"),Theme.Dim,9);
            GUILayout.BeginHorizontal();
            _search = ui.TextField(_search);
            if (ui.Btn(I18n.T("Spawner.Refresh"), ui.TB3, Theme.Text,100)) RefreshFileList();
            GUILayout.EndHorizontal();
            ui.Space(4);
            string query=_search.Trim().ToLowerInvariant();
            var filtered = _availFiles.Where(f => query.Length==0 || Path.GetFileName(f).ToLowerInvariant().Contains(query)).ToList();
            if (filtered.Count == 0) ui.Label(I18n.T("Spawner.NoFiles"), Theme.Dim, 9);
            else
            {

                for (int i = 0; i < filtered.Count; i++)
                {
                    bool sel = _availFiles.IndexOf(filtered[i]) == _selFileIdx;
                    if (ui.ChoiceRow(Path.GetFileNameWithoutExtension(filtered[i]), sel)) _selFileIdx = _availFiles.IndexOf(filtered[i]);
                }
            }
            ui.EndCard();

            bool canSpawn = _selFileIdx >= 0 && _selFileIdx < _availFiles.Count;
            if (ui.Btn(canSpawn ? I18n.T("Spawner.Spawn") : I18n.T("Spawner.SelectFile"), canSpawn ? ui.TA : ui.TB3, Color.white) && canSpawn && Enabled) SpawnInFront(_availFiles[_selFileIdx]);
            int heldCount = 0;
            for (int i = 0; i < _objects.Count; i++) if (_objects[i].HeldBy != null) heldCount++;
            ui.Space(6); ui.Section(I18n.T("Spawner.Objects") + " · " + _objects.Count);
            ui.BeginCard();
            if (_objects.Count == 0) ui.Label(I18n.T("Spawner.NoObjects"), Theme.Dim, 9);
            else
            {
                _scrollObjects = MenuUI.ControlBeginScrollView(_scrollObjects, GUILayout.Height(260 * UI.Scale));
                for (int i = _objects.Count - 1; i >= 0; i--)
                {
                    var so = _objects[i];
                    if (so.Root == null) { if(!string.IsNullOrEmpty(so.SavedState))_unresolvedObjects.Add(so.SavedState);_objects.RemoveAt(i); continue; }
                    ui.BeginCard(Theme.BG3);
                    GUILayout.BeginHorizontal();
                    ui.Label(so.Root.name, Theme.Accent, 10, true);
                    GUILayout.FlexibleSpace();
                    ui.Label(so.HeldBy != null ? I18n.T("Spawner.HeldBy") : I18n.T("Spawner.Free"), so.HeldBy != null ? Theme.Green : Theme.Dim, 9);
                    GUILayout.EndHorizontal();
                    bool fz = ui.Toggle(I18n.T("Spawner.Frozen"), so.Frozen, 90f); if (fz != so.Frozen && Enabled) SetFrozen(so, fz);
                    ui.Space(3);
                    float currentMult = so.Root.transform.localScale.x;
                    float sliderMult=ui.Slider(I18n.T("Spawner.Size"),currentMult,.5f,2f,"0.00x");
                    if (Enabled && !Mathf.Approximately(sliderMult, currentMult)) SetScale(so, new Vector3(sliderMult, sliderMult, sliderMult));
                    ui.Space(4);
                    GUILayout.BeginHorizontal();
                    if (ui.SmallBtn(I18n.T("Spawner.Respawn"), ui.TA, Color.white) && Enabled) Respawn(so);
                    if (ui.SmallBtn(I18n.T("Spawner.Delete"), ui.TR, Color.white) && Enabled) DeleteObject(so);
                    GUILayout.EndHorizontal();
                    ui.EndCard();
                }
                MenuUI.ControlEndScrollView();
            }
            ui.EndCard();
        }
    }

    public class SpawnedObject
    {
        public string SavedState;
        public GameObject Root;
        public Rigidbody Rb;
        public string SourcePath;
        public float FallTimer;
        public bool Frozen, LastPositionInit;
        public bool NetworkShared;
        public Transform HeldBy;
        public Vector3 GrabOffset,LastGrabTarget;
        public bool GrabTargetInitialized;
        public Quaternion GrabRotation=Quaternion.identity;
        public RigidbodyConstraints GrabConstraints;
        public readonly List<GrabCollisionPair> GrabIgnored=new List<GrabCollisionPair>();
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
        public bool WasFrozenBeforeHide;
    }
    public sealed class GrabCollisionPair
    {
        public Collider Object,Player;
        public bool WasIgnored;
    }
}
#endregion

#region GorillaSpawnersNetwork
namespace UltimateMenu
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading.Tasks;
    using ExitGames.Client.Photon;
    using Photon.Pun;
    using Photon.Realtime;
    using UnityEngine;

    public partial class GorillaSpawners
    {
        const string ObjectsKey="UltimateMenu.Objects1";
        const int MaxReplicas=48;
        sealed class NetworkFile { public string Path="",Hash="";public long Length,Ticks; }
        sealed class ObjectSample { public float Time;public Vector3 Position,Scale;public Quaternion Rotation; }
        sealed class ObjectReplica
        {
            internal float NextConnectAttempt;
            public SharedObjectState State=null!;
            public SpawnedObject? Object;
            public TMPro.TextMeshPro? Marker;
            public readonly List<ObjectSample> Samples=new List<ObjectSample>();
        }
        sealed class ObjectPeer
        {
            public int Actor;
            public string Name="";
            public object? Packet;
            public readonly Dictionary<int,ObjectReplica> Objects=new Dictionary<int,ObjectReplica>();
        }
        bool _objectNetwork => GorillaNetwork.SharingEnabled;
        readonly Dictionary<int,ObjectPeer> _objectPeers=new Dictionary<int,ObjectPeer>();
        readonly Dictionary<SpawnedObject,int> _objectIds=new Dictionary<SpawnedObject,int>();
        readonly Dictionary<SpawnedObject,float[]> _objectBounds=new Dictionary<SpawnedObject,float[]>();
        TMPro.TMP_FontAsset? _networkMarkerFont;
        Dictionary<string,NetworkFile> _networkFiles=new Dictionary<string,NetworkFile>(StringComparer.OrdinalIgnoreCase);
        Task<Dictionary<string,NetworkFile>>? _fileHashTask;
        Player? _objectPublisher;
        Room? _objectRoom;
        byte[]? _lastObjectPacket;
        int _nextObjectId,_objectRevision;
        float _nextObjectPoll,_nextObjectHeartbeat,_nextFileScan;
        SpawnedObject[] _networkUiLocal=Array.Empty<SpawnedObject>();
        KeyValuePair<ObjectPeer,ObjectReplica>[] _networkUiRemote=Array.Empty<KeyValuePair<ObjectPeer,ObjectReplica>>();

        void InitializeObjectNetwork() {}
        static string NetworkFilePath(string path)=>Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar,Path.DirectorySeparatorChar);

        void RefreshNetworkFiles()
        {
            if(_fileHashTask!=null||Time.unscaledTime<_nextFileScan)return;
            _nextFileScan=Time.unscaledTime+5;
            var files=Directory.Exists(_dataFolder)?Directory.GetFiles(_dataFolder,"*.bundle"):Array.Empty<string>();
            var previous=_networkFiles;
            _fileHashTask=Task.Run(()=>
            {
                var result=new Dictionary<string,NetworkFile>(StringComparer.OrdinalIgnoreCase);
                foreach(string found in files)
                {
                    try
                    {
                        string path=NetworkFilePath(found);
                        var info=new FileInfo(path);
                        if(previous.TryGetValue(path,out var cached)&&cached.Length==info.Length&&cached.Ticks==info.LastWriteTimeUtc.Ticks){result[path]=cached;continue;}
                        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                        using var sha=SHA256.Create();
                        result[path]=new NetworkFile{Path=path,Length=info.Length,Ticks=info.LastWriteTimeUtc.Ticks,Hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()};
                    }
                    catch(IOException){}catch(UnauthorizedAccessException){}
                }
                return result;
            });
        }

        void UpdateObjectNetwork()
        {
            if(_fileHashTask!=null&&_fileHashTask.IsCompleted)
            {
                if(_fileHashTask.Status==TaskStatus.RanToCompletion)_networkFiles=_fileHashTask.Result;
                else if(_fileHashTask.Exception!=null)Log.Err("Shared object file index",_fileHashTask.Exception);
                _fileHashTask=null;
            }
            if(Time.unscaledTime<_nextObjectPoll)return;
            _nextObjectPoll=Time.unscaledTime+.1f;
            try
            {
                bool active=_objectNetwork&&Enabled&&PhotonNetwork.InRoom&&GorillaNetwork.Instance!=null&&GorillaNetwork.Instance.SharingObjectsAllowed;
                if(!active){CleanupObjectNetwork();return;}
                if(_objectRoom!=PhotonNetwork.CurrentRoom){CleanupObjectNetwork();_objectRoom=PhotonNetwork.CurrentRoom;}
                RefreshNetworkFiles();PublishObjects();PollSharedObjects();
                // Load at most one available remote model per poll; missing files stay as labels.
                foreach(var peer in _objectPeers.Values)
                    foreach(var replica in peer.Objects.Values)
                        if(replica.Object==null && Time.unscaledTime>=replica.NextConnectAttempt && _networkFiles.Values.Any(f=>f.Hash==replica.State.Asset))
                        {replica.NextConnectAttempt=Time.unscaledTime+5f;ConnectReplica(replica);return;}
            }
            catch(Exception e){CleanupObjectNetwork();Log.Err("Shared objects",e);_nextObjectPoll=Time.unscaledTime+5;}
        }

        static string ObjectName(string path)
        {
            string name=Path.GetFileNameWithoutExtension(path);
            return new string(name.Where(c=>!char.IsControl(c)&&c!='<'&&c!='>'&&c!='/'&&c!='\\').Take(64).ToArray());
        }
        void PublishObjects()
        {
            var player=PhotonNetwork.LocalPlayer;if(player==null)return;
            _objectPublisher=player;
            foreach(var removed in _objectIds.Keys.Where(o=>o.Root==null||!_objects.Contains(o)).ToArray()){_objectIds.Remove(removed);_objectBounds.Remove(removed);}
            var states=new List<SharedObjectState>();
            foreach(var local in _objects)
            {
                if(states.Count>=SharedObjectPacket.MaxObjects)break;
                if(local.Root==null||!local.Root.activeInHierarchy||!_networkFiles.TryGetValue(NetworkFilePath(local.SourcePath),out var file))continue;
                if(!_objectIds.TryGetValue(local,out int id))_objectIds[local]=id=++_nextObjectId;
                var t=local.Root.transform;var p=t.position;var q=t.rotation;var s=t.localScale;
                if(!_objectBounds.TryGetValue(local,out var bounds))_objectBounds[local]=bounds=ObjectBounds(local.Root);
                var state=new SharedObjectState{Id=id,Asset=file.Hash,Name=ObjectName(file.Path),Bounds=bounds,Pose=new[]{p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z}};
                if(SharedObjectPacket.Valid(state))states.Add(state);
            }
            byte[] comparable=SharedObjectPacket.Encode(0,states);
            if(_lastObjectPacket!=null&&comparable.SequenceEqual(_lastObjectPacket)&&Time.unscaledTime<_nextObjectHeartbeat)return;
            if(player.SetCustomProperties(new Hashtable{{ObjectsKey,SharedObjectPacket.Encode(++_objectRevision,states)}}))
            {_lastObjectPacket=comparable;_nextObjectHeartbeat=Time.unscaledTime+2;}
        }

        static float[] ObjectBounds(GameObject root)
        {
            var bounds=new Bounds();bool initialized=false;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if(!(renderer is MeshRenderer||renderer is SkinnedMeshRenderer))continue;
                var box=renderer.localBounds;
                for(int i=0;i<8;i++)
                {
                    var corner=box.center+Vector3.Scale(box.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    var point=root.transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if(!initialized){bounds=new Bounds(point,Vector3.zero);initialized=true;}else bounds.Encapsulate(point);
                }
            }
            var c=bounds.center;var s=bounds.size;return new[]{c.x,c.y,c.z,s.x,s.y,s.z};
        }

        static ObjectSample ObjectPose(SharedObjectState state)
        {
            var p=state.Pose;
            return new ObjectSample{Time=Time.unscaledTime,Position=new Vector3(p[0],p[1],p[2]),Rotation=new Quaternion(p[3],p[4],p[5],p[6]).normalized,Scale=new Vector3(p[7],p[8],p[9])};
        }
        void PollSharedObjects()
        {
            var alive=new HashSet<int>();
            foreach(var player in PhotonNetwork.PlayerList)
            {
                if(player==null||player.IsLocal||GorillaNetwork.VersionFor(player.ActorNumber).Length==0)continue;
                object packet=player.CustomProperties[ObjectsKey];
                if(packet==null)continue;
                if(_objectPeers.TryGetValue(player.ActorNumber,out var peer)&&ReferenceEquals(peer.Packet,packet)){alive.Add(player.ActorNumber);continue;}
                if(!SharedObjectPacket.TryRead(packet,out _,out var states))continue;
                if(peer==null)
                {
                    if(_objectPeers.Count>=32)continue;
                    peer=new ObjectPeer{Actor=player.ActorNumber};_objectPeers[player.ActorNumber]=peer;
                }
                alive.Add(player.ActorNumber);peer.Packet=packet;peer.Name=NameContent.Plain(player.NickName??"");
                if(peer.Name.Length>32)peer.Name=peer.Name.Substring(0,32);
                var ids=new HashSet<int>();
                foreach(var state in states)
                {
                    ids.Add(state.Id);
                    if(peer.Objects.TryGetValue(state.Id,out var replica)&&replica.State.Asset!=state.Asset){DestroyReplica(replica);peer.Objects.Remove(state.Id);replica=null;}
                    if(replica==null){replica=new ObjectReplica();peer.Objects[state.Id]=replica;}
                    replica.State=state;if(replica.Marker!=null)replica.Marker.text=state.Name;replica.Samples.Add(ObjectPose(state));
                    if(replica.Samples.Count>16)replica.Samples.RemoveAt(0);
                }
                foreach(int id in peer.Objects.Keys.Where(id=>!ids.Contains(id)).ToArray()){DestroyReplica(peer.Objects[id]);peer.Objects.Remove(id);}
            }
            foreach(int actor in _objectPeers.Keys.Where(actor=>!alive.Contains(actor)).ToArray())
            {foreach(var replica in _objectPeers[actor].Objects.Values)DestroyReplica(replica);_objectPeers.Remove(actor);}
        }

        void ConnectReplica(ObjectReplica replica)
        {
            if(!_objectNetwork||!Enabled||!_objectPeers.Values.Any(p=>p.Objects.Values.Contains(replica))||replica.Object!=null||_objectPeers.Values.Sum(p=>p.Objects.Values.Count(r=>r.Object!=null))>=MaxReplicas)return;
            var file=_networkFiles.Values.FirstOrDefault(f=>f.Hash==replica.State.Asset);if(file==null)return;
            var go=LoadFromBundle(file.Path);if(go==null)return;
            // Remote replicas never enter _objects: no grabbing, local configuration, or persistence.
            foreach(var script in go.GetComponentsInChildren<MonoBehaviour>(true))script.enabled=false;
            foreach(var animator in go.GetComponentsInChildren<Animator>(true))animator.enabled=false;
            foreach(var animation in go.GetComponentsInChildren<Animation>(true))animation.enabled=false;
            foreach(var particles in go.GetComponentsInChildren<ParticleSystem>(true))particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach(var audio in go.GetComponentsInChildren<AudioSource>(true)){audio.Stop();audio.enabled=false;}
            foreach(var joint in go.GetComponentsInChildren<Joint>(true))Destroy(joint);
            foreach(var body in go.GetComponentsInChildren<Rigidbody>(true)){body.isKinematic=true;body.useGravity=false;if(body.gameObject!=go)Destroy(body);}
            var pose=ObjectPose(replica.State);go.transform.SetPositionAndRotation(pose.Position,pose.Rotation);go.transform.localScale=pose.Scale;
            var rb=go.GetComponent<Rigidbody>();if(rb==null)rb=go.AddComponent<Rigidbody>();
            var remote=new SpawnedObject{Root=go,Rb=rb,Frozen=true,Colliders=go.GetComponentsInChildren<Collider>(true)};
            ConfigurePhysics(remote);replica.Object=remote;
            rb.position=pose.Position;rb.rotation=pose.Rotation;
            foreach(var collider in remote.Colliders)
                if(collider!=null){collider.includeLayers=0;collider.excludeLayers=0;}
        }

        void MoveNetworkObjects()
        {
            if(!_objectNetwork||!Enabled){CleanupObjectNetwork();return;}
            float time=Time.unscaledTime-.15f;
            foreach(var peer in _objectPeers.Values)foreach(var replica in peer.Objects.Values)
            {
                var remote=replica.Object;if(remote!=null&&remote.Root==null){replica.Object=null;remote=null;}if(replica.Samples.Count==0)continue;
                var samples=replica.Samples;
                while(samples.Count>2&&samples[1].Time<=time)samples.RemoveAt(0);
                var a=samples[0];var b=samples[samples.Count>1?1:0];
                float t=a==b?1:Mathf.Clamp01((time-a.Time)/Mathf.Max(.001f,b.Time-a.Time));
                Vector3 position=Vector3.Lerp(a.Position,b.Position,t);Quaternion rotation=Quaternion.Slerp(a.Rotation,b.Rotation,t);
                Vector3 scale=Vector3.Lerp(a.Scale,b.Scale,t);
                if(remote==null||remote.Root==null)
                {
                    EnsureObjectMarker(replica);
                    if(replica.Marker!=null)
                    {
                        var center=replica.State.Bounds;
                        replica.Marker.transform.position=position+rotation*Vector3.Scale(new Vector3(center[0],center[1],center[2]),scale);
                        var camera=Camera.main;if(camera!=null)replica.Marker.transform.rotation=camera.transform.rotation;
                    }
                    continue;
                }
                if(replica.Marker!=null){Destroy(replica.Marker.gameObject);replica.Marker=null;}
                if((position-remote.Rb.position).sqrMagnitude>25){remote.Rb.position=position;remote.Rb.rotation=rotation;}
                else{remote.Rb.MovePosition(position);remote.Rb.MoveRotation(rotation);}
                remote.Root.transform.localScale=scale;
            }
        }

        void EnsureObjectMarker(ObjectReplica replica)
        {
            if(replica.Marker!=null)return;
            if(_networkMarkerFont==null)_networkMarkerFont=Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>().FirstOrDefault(font=>font!=null&&font.material!=null);
            if(_networkMarkerFont==null)return;
            var go=new GameObject("Shared object - "+replica.State.Name);
            var text=go.AddComponent<TMPro.TextMeshPro>();replica.Marker=text;
            text.font=_networkMarkerFont;text.text=replica.State.Name;text.richText=false;text.fontSize=3;
            text.alignment=TMPro.TextAlignmentOptions.Center;text.color=new Color(.4f,.85f,1f);text.outlineWidth=.2f;text.outlineColor=Color.black;
            text.rectTransform.sizeDelta=new Vector2(12,2);go.transform.localScale=Vector3.one*.1f;
        }
        static void DestroyReplica(ObjectReplica replica)
        {
            if(replica.Object?.Root!=null){replica.Object.Root.SetActive(false);Destroy(replica.Object.Root);}
            if(replica.Marker!=null){replica.Marker.gameObject.SetActive(false);Destroy(replica.Marker.gameObject);replica.Marker=null;}
            replica.Object=null;
        }
        void CleanupObjectNetwork()
        {
            if(_objectPublisher!=null&&PhotonNetwork.InRoom&&ReferenceEquals(_objectPublisher,PhotonNetwork.LocalPlayer))
                try{_objectPublisher.SetCustomProperties(new Hashtable{{ObjectsKey,null}});}catch(Exception e){Log.Err("Shared object cleanup",e);}
            _objectPublisher=null;_lastObjectPacket=null;_objectRoom=null;_nextObjectHeartbeat=0;
            foreach(var peer in _objectPeers.Values)foreach(var replica in peer.Objects.Values)DestroyReplica(replica);
            _objectPeers.Clear();
        }
        void OnDisable()=>CleanupObjectNetwork();

        internal void StopNetworkSharing()=>CleanupObjectNetwork();

    }
}
#endregion

#region GrabSpring
namespace UltimateMenu
{
    using System;

    internal static class GrabSpring
    {
        internal const float MaxAcceleration=180f,MaxSpeed=12f;
        internal static float Acceleration(float error,float velocity)
        {
            if(float.IsNaN(error)||float.IsInfinity(error)||float.IsNaN(velocity)||float.IsInfinity(velocity))return 0;
            return Math.Max(-MaxAcceleration,Math.Min(MaxAcceleration,error*900f-velocity*45f));
        }
    }
}
#endregion

#region SharedObjectPacket
namespace UltimateMenu
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    internal sealed class SharedObjectState
    {
        public int Id;
        public string Asset="", Name="";
        public float[] Pose=Array.Empty<float>(); // position, quaternion, scale; never file contents or a local path
        public float[] Bounds=new float[]{0,0,0,1,1,1};
    }

    internal static class SharedObjectPacket
    {
        internal const int MaxObjects=24, MaxBytes=8192;
        const int Magic=0x314f4d55;
        static readonly Encoding Utf8=new UTF8Encoding(false,true);
        internal static bool Valid(SharedObjectState state)
        {
            if(state==null||state.Id<=0||state.Asset==null||state.Asset.Length!=64||state.Name==null||state.Name.Length>64||state.Pose==null||state.Pose.Length!=10||state.Bounds==null||state.Bounds.Length!=6)return false;
            foreach(char c in state.Asset)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'))return false;
            foreach(char c in state.Name)if(char.IsControl(c)||c=='<'||c=='>'||c=='/'||c=='\\')return false;
            for(int i=0;i<10;i++)if(float.IsNaN(state.Pose[i])||float.IsInfinity(state.Pose[i]))return false;
            for(int i=0;i<6;i++)if(float.IsNaN(state.Bounds[i])||float.IsInfinity(state.Bounds[i])||Math.Abs(state.Bounds[i])>10000||i>=3&&state.Bounds[i]<0)return false;
            for(int i=0;i<3;i++)if(Math.Abs(state.Pose[i])>1000000||state.Pose[7+i]<.1f||state.Pose[7+i]>50)return false;
            double norm=0;for(int i=3;i<7;i++)norm+=(double)state.Pose[i]*state.Pose[i];
            return norm>.5&&norm<1.5;
        }
        internal static byte[] Encode(int revision,IReadOnlyList<SharedObjectState> objects)
        {
            if(objects.Count>MaxObjects)throw new InvalidDataException("Too many shared objects");
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream,Utf8,true);
            writer.Write(Magic);writer.Write(revision);writer.Write(objects.Count);
            var ids=new HashSet<int>();
            foreach(var state in objects)
            {
                if(!Valid(state)||!ids.Add(state.Id))throw new InvalidDataException("Invalid shared object");
                writer.Write(state.Id);
                for(int i=0;i<32;i++)writer.Write(Convert.ToByte(state.Asset.Substring(i*2,2),16));
                var name=Utf8.GetBytes(state.Name);writer.Write((ushort)name.Length);writer.Write(name);
                foreach(float value in state.Pose)writer.Write(value);
                foreach(float value in state.Bounds)writer.Write(value);
            }
            writer.Flush();if(stream.Length>MaxBytes)throw new InvalidDataException("Shared object packet too large");
            return stream.ToArray();
        }
        internal static bool TryRead(object value,out int revision,out List<SharedObjectState> objects)
        {
            revision=0;objects=new List<SharedObjectState>();
            if(!(value is byte[] bytes)||bytes.Length<12||bytes.Length>MaxBytes)return false;
            try
            {
                using var stream=new MemoryStream(bytes,false);using var reader=new BinaryReader(stream,Utf8);
                if(reader.ReadInt32()!=Magic)return false;revision=reader.ReadInt32();int count=reader.ReadInt32();
                if(count<0||count>MaxObjects)return false;
                var ids=new HashSet<int>();
                for(int i=0;i<count;i++)
                {
                    var state=new SharedObjectState{Id=reader.ReadInt32()};
                    byte[] hash=reader.ReadBytes(32);if(hash.Length!=32)return false;
                    state.Asset=BitConverter.ToString(hash).Replace("-","").ToLowerInvariant();
                    int size=reader.ReadUInt16();if(size>256)return false;
                    var name=reader.ReadBytes(size);if(name.Length!=size)return false;state.Name=Utf8.GetString(name);
                    state.Pose=new float[10];for(int j=0;j<10;j++)state.Pose[j]=reader.ReadSingle();
                    state.Bounds=new float[6];for(int j=0;j<6;j++)state.Bounds[j]=reader.ReadSingle();
                    if(!Valid(state)||!ids.Add(state.Id))return false;objects.Add(state);
                }
                return stream.Position==stream.Length;
            }
            catch(Exception e) when(e is IOException||e is ArgumentException||e is FormatException){objects.Clear();return false;}
        }
    }
}
#endregion
