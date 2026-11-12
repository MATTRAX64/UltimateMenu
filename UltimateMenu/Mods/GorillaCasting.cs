using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using PhotonPlayer = Photon.Realtime.Player;

namespace UltimateMenu
{
    // GorillaCasting : camera de casting pour les matchs / competitions.
    // - Joueurs : on coche un ou plusieurs joueurs, changement auto (toutes les X secondes) ou manuel
    // - Vue joueur : la camera suit la position du joueur OU la rotation de sa tete, avec un decalage de
    //   position (X / Y / distance), un decalage de rotation (haut-bas / gauche-droite) et un anti-mur simple
    // - Vue map : camera du ciel, centree sur la moyenne des joueurs coches, assez haute pour les garder tous dans le cadre
    internal class GorillaCasting : MonoBehaviour
    {
        public static GorillaCasting Instance { get; private set; }
        const string PageKey = "Casting.Title";
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled(PageKey);
        public bool Active => _wasEnabled && _cam != null;

        enum ViewMode { Player = 0, Map = 1 }
        enum FollowMode { Fixed = 0, Smooth = 1 }

        // ---- Reglages (sauvegardes dans MenuPrefs) ----
        ViewMode _view = ViewMode.Player;
        bool _autoSwitch; float _switchEvery = 10f;
        FollowMode _follow = FollowMode.Smooth;
        bool _followHead = true;                    // false = suit la position, true = suit aussi la rotation de la tete
        float _posSpeed = 8f, _rotSpeed = 6f;
        float _offX, _offY = 0.3f, _distance = 2.5f;
        float _pitchOff, _yawOff;
        bool _avoid = true;
        float _mapYaw, _mapMargin = 3f, _mapMinHeight = 6f, _mapSpeed = 3f;

        const float SnapDistance = 8f;     // au dela (tp, respawn, cible lointaine) : pas de glissement
        const float WallRadius = 0.15f;    // epaisseur du test anti-mur
        const float WallMargin = 0.15f;    // la camera s'arrete un peu avant le mur

        // ---- Etat ----
        const int LocalActor = -1;
        class Entry { public int actor; public string name = ""; public VRRig rig; public Transform head; }
        List<Entry> _players = new List<Entry>();
        List<Entry> _sel = new List<Entry>();                       // joueurs coches presents dans la salle
        HashSet<int> _selected = new HashSet<int> { LocalActor };   // acteurs coches (le joueur local par defaut)
        int _currentActor = LocalActor;                             // joueur filme en vue joueur
        float _nextRefresh, _nextSwitch;

        GameObject _cam;
        Camera _lens;
        bool _spawning;
        Transform _tabletParent;
        bool _tabletWasParented;
        Vector3 _tabletPosition;
        Quaternion _tabletRotation;
        Vector3 _tabletScale;
        float _nextFind, _nextSpawnTry, _spawnDeadline;

        bool _wasEnabled, _pageRegistered;
        string _statusKey = ""; float _statusTimer;

        // Vue joueur
        bool _init;
        Quaternion _frame = Quaternion.identity;
        Vector3 _pivot;
        float _lastYaw;
        float _wallK = 1f;                 // 1 = camera a sa place, < 1 = rapprochee du joueur a cause d'un mur
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        // Vue map
        bool _mapInit;
        Vector3 _mapPos;
        Quaternion _mapRot = Quaternion.identity;
        readonly List<Vector3> _mapPts = new List<Vector3>();

        // ================= Cycle de vie =================
        void Awake()
        {
            Instance = this;
            LoadConfig();
        }

        void Start() { TryRegister(); }

        void TryRegister()
        {
            if (_pageRegistered || MenuUI.Instance == null) return;
            MenuUI.Instance.RegisterPage(PageKey, DrawPage, MenuCategory.Camera);
            _pageRegistered = true;
        }

        void Update()
        {
            if (!_pageRegistered) TryRegister();
            if (_statusTimer > 0f) _statusTimer -= Time.deltaTime;
            bool now = Enabled;
            if (now != _wasEnabled)
            {
                _wasEnabled = now;
                if (now) BeginCasting(); else EndCasting();
            }
            if (!now) return;
            if (Time.unscaledTime >= _nextRefresh) { _nextRefresh = Time.unscaledTime + 1.5f; RefreshPlayers(); }
            MaintainCamera();
            ReleaseCameraMod();

            if (_view == ViewMode.Player && _autoSwitch && _sel.Count > 1)
            {
                if (Time.unscaledTime >= _nextSwitch) CycleTarget(1);
            }
            else _nextSwitch = Time.unscaledTime + _switchEvery;
        }

        void BeginCasting()
        {
            _spawning = false; _nextFind = 0f; _nextSpawnTry = 0f;
            ResetView();
            _nextSwitch = Time.unscaledTime + _switchEvery;
            RefreshPlayers();
        }

        void EndCasting()
        {
            ReleaseTablet();
            _spawning = false;
            ResetView();
            MenuPrefs.Save();
        }

        void OnDestroy()
        {
            ReleaseTablet();
            MenuPrefs.Save();
            if (Instance == this) Instance = null;
        }

        void ReleaseTablet()
        {
            if (_cam != null && (!_tabletWasParented || _tabletParent != null))
            {
                var tablet = _cam.transform;
                tablet.SetParent(_tabletParent, false);
                tablet.SetLocalPositionAndRotation(_tabletPosition, _tabletRotation);
                tablet.localScale = _tabletScale;
            }
            _cam = null;
            _lens = null;
            _tabletParent = null;
        }

        void ResetView() { _init = false; _mapInit = false; _wallK = 1f; }
        void SetStatus(string key) { _statusKey = key; _statusTimer = 4f; }

        // ================= Camera (meme tablette LCK que GorillaCamera) =================
        GameObject FindExisting()
        {
            var fromCameraMod = GorillaCamera.Instance != null ? GorillaCamera.Instance.LckCamera : null;
            if (fromCameraMod != null && fromCameraMod.activeInHierarchy) return fromCameraMod;
            var found = GameObject.Find("LCKTablet(Clone)");
            return found != null && found.activeInHierarchy ? found : null;
        }

        void MaintainCamera()
        {
            if (_cam != null && !_cam.activeInHierarchy) { ReleaseTablet(); ResetView(); }
            if (_cam != null || Time.unscaledTime < _nextFind) return;
            _nextFind = Time.unscaledTime + 0.5f;
            var found = FindExisting();
            if (found != null) { Adopt(found); return; }
            if (_spawning && Time.unscaledTime < _spawnDeadline) return;
            _spawning = false;
            if (Time.unscaledTime >= _nextSpawnTry) { _nextSpawnTry = Time.unscaledTime + 3f; TriggerSpawn(); }
        }

        // Le mod Camera (clavier / souris / keyframes) pilote la meme tablette : on coupe son controle PC
        // sur notre camera, sinon les deux se battent a chaque frame.
        void ReleaseCameraMod()
        {
            var gc = GorillaCamera.Instance;
            if (gc == null || _cam == null || !gc.Active || gc.LckCamera != _cam) return;
            gc.Disable();
        }

        void Adopt(GameObject go)
        {
            _cam = go;
            var tablet = go.transform;
            _tabletParent = tablet.parent;
            _tabletWasParented = _tabletParent != null;
            _tabletPosition = tablet.localPosition;
            _tabletRotation = tablet.localRotation;
            _tabletScale = tablet.localScale;
            tablet.SetParent(null);
            _lens = null;
            _spawning = false;
            ResetView();
            SetStatus("Casting.CamReady");
        }

        void TriggerSpawn()
        {
            try
            {
                // Si le mod Camera est actif, on lui laisse faire le spawn pour ne pas creer deux tablettes.
                var gc = GorillaCamera.Instance;
                if (gc != null && gc.Enabled)
                {
                    gc.SpawnCameraClone();
                    _spawning = true; _spawnDeadline = Time.unscaledTime + 10f;
                    SetStatus("Casting.CamSpawning");
                    return;
                }
                var wallGO = GameObject.Find("WallSpawner");
                if (wallGO == null) { SetStatus("Casting.CamMissing"); return; }
                var wallComp = wallGO.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name == "LckWallCameraSpawner");
                if (wallComp == null) { SetStatus("Casting.CamMissing"); return; }
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                var getOrCreate = wallComp.GetType().GetMethod("GetOrCreateBodyCameraSpawner", flags);
                if (getOrCreate == null) return;
                var bodySpawner = getOrCreate.Invoke(wallComp, null);
                if (bodySpawner == null) return;
                var spawnMethod = bodySpawner.GetType().GetMethod("SpawnCamera", flags);
                if (spawnMethod == null) return;
                try { spawnMethod.Invoke(bodySpawner, new object[spawnMethod.GetParameters().Length]); } catch { }
                _spawning = true; _spawnDeadline = Time.unscaledTime + 10f;
                SetStatus("Casting.CamSpawning");
            }
            catch (Exception e) { Log.Err("GorillaCasting.TriggerSpawn", e); }
        }

        // ================= Joueurs (meme source que GorillaMocaps) =================
        static Type _photonNetworkType;
        static Type GetPhotonNetworkType()
        {
            if (_photonNetworkType != null) return _photonNetworkType;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types) if (t.Name == "PhotonNetwork") { _photonNetworkType = t; return t; }
            }
            return null;
        }
        static bool PhotonInRoom() { var p = GetPhotonNetworkType()?.GetProperty("InRoom", BindingFlags.Public | BindingFlags.Static); return p != null && (bool)(p.GetValue(null) ?? false); }
        static PhotonPlayer[] PhotonPlayerList() => GetPhotonNetworkType()?.GetProperty("PlayerList", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as PhotonPlayer[] ?? Array.Empty<PhotonPlayer>();
        static PhotonPlayer PhotonLocalPlayer() => GetPhotonNetworkType()?.GetProperty("LocalPlayer", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as PhotonPlayer;

        void RefreshPlayers()
        {
            var list = new List<Entry>();
            var present = new HashSet<int>();
            bool inRoom = PhotonInRoom();
            var localRig = VRRig.LocalRig;
            if (localRig != null) list.Add(new Entry { actor = LocalActor, name = I18n.T("Casting.Me"), rig = localRig });
            if (inRoom && GorillaGameManager.instance != null)
            {
                var localPlayer = PhotonLocalPlayer();
                foreach (var p in PhotonPlayerList())
                {
                    if (p == null) continue;
                    present.Add(p.ActorNumber);
                    if (localPlayer != null && p.ActorNumber == localPlayer.ActorNumber) continue;
                    var rig = GorillaGameManager.instance.FindPlayerVRRig(p);
                    if (rig == null) continue;
                    list.Add(new Entry { actor = p.ActorNumber, name = p.NickName ?? "", rig = rig });
                }
                _selected.RemoveWhere(a => a != LocalActor && !present.Contains(a));
            }
            else if (!inRoom)
            {
                _selected.RemoveWhere(a => a != LocalActor);
            }
            _players = list;
            RebuildSelection();
        }

        Transform HeadOf(Entry e)
        {
            if (e == null || e.rig == null) return null;
            if (e.head == null)
            {
                e.head = e.rig.transform.Find("rig/head");
                if (e.head == null) e.head = e.rig.transform;
            }
            return e.head;
        }

        // ================= Selection + changement de joueur =================
        void RebuildSelection()
        {
            if (_selected.Count == 0) _selected.Add(LocalActor);
            _sel.Clear();
            for (int i = 0; i < _players.Count; i++) if (_selected.Contains(_players[i].actor)) _sel.Add(_players[i]);
        }

        void SetSelected(int actor, bool on)
        {
            if (on) _selected.Add(actor);
            else if (_selected.Count > 1) _selected.Remove(actor);   // il en reste toujours au moins un
            RebuildSelection();
        }

        void SelectAll()
        {
            for (int i = 0; i < _players.Count; i++) _selected.Add(_players[i].actor);
            RebuildSelection();
        }

        void SelectOnlyMe()
        {
            _selected.Clear(); _selected.Add(LocalActor);
            RebuildSelection();
            SetCurrent(LocalActor);
        }

        void SetCurrent(int actor)
        {
            _currentActor = actor; _wallK = 1f;
            _nextSwitch = Time.unscaledTime + _switchEvery;
        }

        // Joueur filme en vue joueur. S'il n'est plus coche / plus la, on passe au premier joueur coche.
        Entry CurrentEntry()
        {
            Entry first = null;
            for (int i = 0; i < _sel.Count; i++)
            {
                var e = _sel[i];
                if (e.rig == null) continue;
                if (e.actor == _currentActor) return e;
                if (first == null) first = e;
            }
            if (first != null) { SetCurrent(first.actor); return first; }
            for (int i = 0; i < _players.Count; i++) if (_players[i].actor == LocalActor && _players[i].rig != null) return _players[i];
            return null;
        }

        string NameOf(int actor)
        {
            for (int i = 0; i < _players.Count; i++) if (_players[i].actor == actor) return _players[i].name;
            return "";
        }

        public void CycleTarget(int dir)
        {
            if (_sel.Count == 0) return;
            int idx = 0;
            for (int i = 0; i < _sel.Count; i++) if (_sel[i].actor == _currentActor) { idx = i; break; }
            idx = ((idx + dir) % _sel.Count + _sel.Count) % _sel.Count;
            SetCurrent(_sel[idx].actor);
        }

        void SetView(ViewMode v)
        {
            _view = v; MenuPrefs.SetInt("MUM_CastView", (int)v);
            ResetView();
            _nextSwitch = Time.unscaledTime + _switchEvery;
        }

        // ================= Suivi de la camera =================
        void LateUpdate()
        {
            if (!_wasEnabled || _cam == null) return;
            if (_view == ViewMode.Map) UpdateMapView(Time.deltaTime);
            else UpdatePlayerView(Time.deltaTime);
        }

        static float Rate(float speed, float dt) => 1f - Mathf.Exp(-speed * dt);

        // ---------- Vue joueur ----------
        // La camera se place a (X, Y, -distance) du joueur, dans le repere du joueur (ou du monde si on ne suit que la position),
        // regarde la tete du joueur, puis on ajoute le decalage de rotation.
        void UpdatePlayerView(float dt)
        {
            var head = HeadOf(CurrentEntry());
            if (head == null) return;
            bool smooth = _follow == FollowMode.Smooth;

            Quaternion frameTarget = FrameRotationOf(head);
            Vector3 pivotTarget = head.position;
            if (!_init || !smooth || Vector3.Distance(_pivot, pivotTarget) > SnapDistance)
            {
                _frame = frameTarget; _pivot = pivotTarget; _init = true; _wallK = 1f;
            }
            else
            {
                _frame = Quaternion.Slerp(_frame, frameTarget, Rate(_rotSpeed, dt));
                _pivot = Vector3.Lerp(_pivot, pivotTarget, Rate(_posSpeed, dt));
            }

            Vector3 desired = _pivot + _frame * new Vector3(_offX, _offY, -_distance);

            // Anti-mur : un seul test. Si un mur est entre le joueur et la camera, on rapproche la camera.
            float k = (_avoid && _distance > 0.3f) ? WallFactor(_pivot, desired) : 1f;
            _wallK = Mathf.Lerp(_wallK, k, Rate(k < _wallK ? 25f : 4f, dt));   // rapide vers le joueur, lent pour revenir
            Vector3 pos = _pivot + (desired - _pivot) * _wallK;

            Vector3 toPivot = _pivot - pos;
            Quaternion look = toPivot.sqrMagnitude > 0.0025f ? Quaternion.LookRotation(toPivot.normalized, Vector3.up) : _frame;
            _cam.transform.SetPositionAndRotation(pos, look * Quaternion.Euler(_pitchOff, _yawOff, 0f));
        }

        // 1 = rien ne gene, sinon part de la distance (0..1) a laquelle la camera peut aller avant le mur.
        float WallFactor(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.05f) return 1f;
            int n = Physics.SphereCastNonAlloc(from, WallRadius, d / len, Hits, len, Mask(), QueryTriggerInteraction.Ignore);
            float nearest = len;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || h.distance <= 0.001f) continue;          // deja dans un collider au depart : on ignore
                if (h.collider.GetComponentInParent<VRRig>() != null) continue;     // on ignore les joueurs
                if (h.distance < nearest) nearest = h.distance;
            }
            return Mathf.Clamp01((nearest - WallMargin) / len);
        }

        static int Mask()
        {
            int mask = ~0;
            var player = GorillaLocomotion.GTPlayer.Instance;
            if (player != null && player.locomotionEnabledLayers.value != 0) mask = player.locomotionEnabledLayers.value;
            return mask;
        }

        Quaternion FrameRotationOf(Transform head)
        {
            if (!_followHead) return Quaternion.identity;
            // Rotation de la tete a plat (yaw seul) : stable meme quand le joueur regarde en haut / en bas.
            Vector3 f = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (f.sqrMagnitude < 0.02f)
            {
                f = Vector3.ProjectOnPlane(head.up, Vector3.up);
                if (head.forward.y > 0f) f = -f;
            }
            if (f.sqrMagnitude > 0.0001f) _lastYaw = Quaternion.LookRotation(f.normalized, Vector3.up).eulerAngles.y;
            return Quaternion.Euler(0f, _lastYaw, 0f);
        }

        // ---------- Vue map (vue du ciel) ----------
        void ReadLens(out float vfov, out float aspect)
        {
            vfov = 60f; aspect = 16f / 9f;
            if (_lens == null && _cam != null)
            {
                // La tablette peut contenir plusieurs Camera : on prend celle qui filme vers la texture d'enregistrement.
                foreach (var c in _cam.GetComponentsInChildren<Camera>(true))
                {
                    if (_lens == null) _lens = c;
                    if (c.targetTexture != null) { _lens = c; break; }
                }
            }
            if (_lens == null) return;
            vfov = Mathf.Clamp(_lens.fieldOfView, 20f, 120f);
            float a = _lens.aspect;
            if (a > 0.5f && a < 4f) aspect = a;
        }

        // Cible : au milieu (moyenne) des joueurs coches, regardant vers le bas, a la hauteur minimale
        // qui garde chaque joueur (plus la marge) dans le champ de vision.
        bool ComputeMapTarget(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.Euler(90f, _mapYaw, 0f);
            _mapPts.Clear();
            for (int i = 0; i < _sel.Count; i++)
            {
                var h = HeadOf(_sel[i]);
                if (h != null) _mapPts.Add(h.position);
            }
            if (_mapPts.Count == 0)
            {
                var h = HeadOf(CurrentEntry());
                if (h == null) return false;
                _mapPts.Add(h.position);
            }

            Vector3 center = Vector3.zero;
            for (int i = 0; i < _mapPts.Count; i++) center += _mapPts[i];
            center /= _mapPts.Count;

            // Pendant le smooth la camera est en retard sur le centre : on cadre depuis sa position reelle.
            Vector3 from = (_follow == FollowMode.Smooth && _mapInit) ? _mapPos : center;

            ReadLens(out float vfov, out float aspect);
            float tanV = Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * aspect;
            Vector3 right = rot * Vector3.right, up = rot * Vector3.up;

            float height = float.NegativeInfinity, highest = float.NegativeInfinity;
            for (int i = 0; i < _mapPts.Count; i++)
            {
                Vector3 p = _mapPts[i];
                Vector3 d = p - from; d.y = 0f;
                float needX = (Mathf.Abs(Vector3.Dot(d, right)) + _mapMargin) / tanH;
                float needZ = (Mathf.Abs(Vector3.Dot(d, up)) + _mapMargin) / tanV;
                height = Mathf.Max(height, p.y + Mathf.Max(needX, needZ));
                highest = Mathf.Max(highest, p.y);
            }
            height = Mathf.Clamp(Mathf.Max(height, highest + _mapMinHeight), -1000f, 1000f);
            pos = new Vector3(center.x, height, center.z);
            return true;
        }

        void UpdateMapView(float dt)
        {
            if (!ComputeMapTarget(out Vector3 targetPos, out Quaternion targetRot)) return;
            bool smooth = _follow == FollowMode.Smooth;
            if (!_mapInit)
            {
                _mapInit = true;
                if (smooth) { _mapPos = _cam.transform.position; _mapRot = _cam.transform.rotation; }
                else { _mapPos = targetPos; _mapRot = targetRot; }
            }
            else if (!smooth) { _mapPos = targetPos; _mapRot = targetRot; }
            else
            {
                Vector3 p = _mapPos;
                float k = Rate(_mapSpeed, dt);
                p.x = Mathf.Lerp(p.x, targetPos.x, k);
                p.z = Mathf.Lerp(p.z, targetPos.z, k);
                // On dezoome plus vite qu'on ne zoome : un joueur qui part ne sort jamais du cadre.
                p.y = Mathf.Lerp(p.y, targetPos.y, Rate(targetPos.y > p.y ? _mapSpeed * 2f : _mapSpeed, dt));
                _mapPos = p;
                _mapRot = Quaternion.Slerp(_mapRot, targetRot, Rate(_mapSpeed * 1.5f, dt));
            }
            _cam.transform.SetPositionAndRotation(_mapPos, _mapRot);
        }

        // ================= Config =================
        void LoadConfig()
        {
            _view = (ViewMode)Mathf.Clamp(MenuPrefs.GetInt("MUM_CastView", 0), 0, 1);
            _autoSwitch = MenuPrefs.GetInt("MUM_CastAuto", 0) == 1;
            _switchEvery = MenuPrefs.GetFloat("MUM_CastEvery", 10f);
            _follow = (FollowMode)Mathf.Clamp(MenuPrefs.GetInt("MUM_CastFollow", 1), 0, 1);
            _followHead = MenuPrefs.GetInt("MUM_CastHead", 1) == 1;
            _posSpeed = MenuPrefs.GetFloat("MUM_CastPosSpeed", 8f);
            _rotSpeed = MenuPrefs.GetFloat("MUM_CastRotSpeed", 6f);
            _offX = MenuPrefs.GetFloat("MUM_CastOffX", 0f);
            _offY = MenuPrefs.GetFloat("MUM_CastOffY", 0.3f);
            _distance = MenuPrefs.GetFloat("MUM_CastDist", 2.5f);
            _pitchOff = MenuPrefs.GetFloat("MUM_CastPitchOff", 0f);
            _yawOff = MenuPrefs.GetFloat("MUM_CastYawOff", 0f);
            _avoid = MenuPrefs.GetInt("MUM_CastAvoid", 1) == 1;
            _mapYaw = MenuPrefs.GetFloat("MUM_CastMapYaw", 0f);
            _mapMargin = MenuPrefs.GetFloat("MUM_CastMapMargin", 3f);
            _mapMinHeight = MenuPrefs.GetFloat("MUM_CastMapMinH", 6f);
            _mapSpeed = MenuPrefs.GetFloat("MUM_CastMapSpeed", 3f);
        }

        void SaveAll()
        {
            MenuPrefs.SetInt("MUM_CastView", (int)_view); MenuPrefs.SetInt("MUM_CastAuto", _autoSwitch ? 1 : 0); MenuPrefs.SetFloat("MUM_CastEvery", _switchEvery);
            MenuPrefs.SetInt("MUM_CastFollow", (int)_follow); MenuPrefs.SetInt("MUM_CastHead", _followHead ? 1 : 0);
            MenuPrefs.SetFloat("MUM_CastPosSpeed", _posSpeed); MenuPrefs.SetFloat("MUM_CastRotSpeed", _rotSpeed);
            MenuPrefs.SetFloat("MUM_CastOffX", _offX); MenuPrefs.SetFloat("MUM_CastOffY", _offY); MenuPrefs.SetFloat("MUM_CastDist", _distance);
            MenuPrefs.SetFloat("MUM_CastPitchOff", _pitchOff); MenuPrefs.SetFloat("MUM_CastYawOff", _yawOff);
            MenuPrefs.SetInt("MUM_CastAvoid", _avoid ? 1 : 0);
            MenuPrefs.SetFloat("MUM_CastMapYaw", _mapYaw); MenuPrefs.SetFloat("MUM_CastMapMargin", _mapMargin);
            MenuPrefs.SetFloat("MUM_CastMapMinH", _mapMinHeight); MenuPrefs.SetFloat("MUM_CastMapSpeed", _mapSpeed);
        }

        void ResetSettings()
        {
            _view = ViewMode.Player; _autoSwitch = false; _switchEvery = 10f;
            _follow = FollowMode.Smooth; _followHead = true; _posSpeed = 8f; _rotSpeed = 6f;
            _offX = 0f; _offY = 0.3f; _distance = 2.5f; _pitchOff = 0f; _yawOff = 0f; _avoid = true;
            _mapYaw = 0f; _mapMargin = 3f; _mapMinHeight = 6f; _mapSpeed = 3f;
            SaveAll(); ResetView();
            _nextSwitch = Time.unscaledTime + _switchEvery;
        }

        // ================= Page du menu =================
        bool Sl(MenuUI ui, string key, ref float value, float min, float max, string fmt, string pref)
        {
            float v = ui.Slider(I18n.T(key), value, min, max, fmt);
            if (!Enabled || Mathf.Approximately(v, value)) return false;
            value = v; MenuPrefs.SetFloat(pref, v);
            return true;
        }

        List<Entry>? _layoutPlayers;
        bool _layoutPlayerView, _layoutSmooth, _layoutAutoSwitch, _layoutCountdown, _layoutSpawn;
        string? _layoutStatus;

        void DrawPage(MenuUI ui)
        {
            // Unity reuses the Layout control tree for input and Repaint.
            if (Event.current.type == EventType.Layout || _layoutPlayers == null)
            {
                _layoutPlayers = _players;
                _layoutPlayerView = _view == ViewMode.Player;
                _layoutSmooth = _follow == FollowMode.Smooth;
                _layoutAutoSwitch = _autoSwitch;
                _layoutCountdown = _sel.Count > 1;
                _layoutSpawn = _cam == null;
                _layoutStatus = _statusTimer > 0f ? _statusKey : null;
            }
            bool playerView = _layoutPlayerView;

            // --- Etat + camera ---
            ui.BeginCard();
            ui.StatusPill(Active ? I18n.T("Casting.Active") : I18n.T("Casting.Inactive"), Active);
            if (!string.IsNullOrEmpty(_layoutStatus)) ui.Label(I18n.T(_layoutStatus), Theme.Dim, 9);
            if (_layoutSpawn && ui.Btn(I18n.T("Casting.SpawnCam"), ui.TB3, Theme.Text) && Enabled) { _nextFind = 0f; _nextSpawnTry = 0f; }
            ui.EndCard();

            // --- Vue ---
            ui.Space(6); ui.Section(I18n.T("Casting.View"));
            ui.BeginCard();
            int view = ui.ModePicker(new[] { I18n.T("Casting.ViewPlayer"), I18n.T("Casting.ViewMap") }, (int)_view);
            if (Enabled && view != (int)_view) SetView((ViewMode)view);
            ui.EndCard();

            // --- Joueurs coches ---
            ui.Space(6); ui.Section(I18n.T("Casting.Players") + " (" + _sel.Count + ")");
            ui.BeginCard();
            ui.Label(I18n.T("Casting.SelectionHint"), Theme.Dim, 8);
            var list = _layoutPlayers;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                string label = e.name.Length > 28 ? e.name.Substring(0, 28) : e.name;
                bool on = _selected.Contains(e.actor);
                if (playerView && on && e.actor == _currentActor) label = "> " + label;
                bool now = ui.Toggle(label, on);
                if (Enabled && now != on) SetSelected(e.actor, now);
            }
            ui.Space(4);
            GUILayout.BeginHorizontal();
            if (ui.SmallBtn(I18n.T("Casting.SelectAll"), ui.TB3, Theme.Text) && Enabled) SelectAll();
            GUILayout.Space(4);
            if (ui.SmallBtn(I18n.T("Casting.SelectMe"), ui.TB3, Theme.Text) && Enabled) SelectOnlyMe();
            GUILayout.EndHorizontal();
            ui.EndCard();

            // --- Changement de joueur (vue joueur) ---
            if (playerView)
            {
                ui.Space(6); ui.Section(I18n.T("Casting.Switching"));
                ui.BeginCard();
                ui.Label(I18n.T("Casting.Current") + " : " + NameOf(_currentActor), Theme.Dim, 9);
                bool auto = ui.Toggle(I18n.T("Casting.AutoSwitch"), _autoSwitch);
                if (Enabled && auto != _autoSwitch) { _autoSwitch = auto; MenuPrefs.SetInt("MUM_CastAuto", auto ? 1 : 0); _nextSwitch = Time.unscaledTime + _switchEvery; }
                if (_layoutAutoSwitch)
                {
                    if (Sl(ui, "Casting.SwitchEvery", ref _switchEvery, 2f, 120f, "0", "MUM_CastEvery"))
                        _nextSwitch = Mathf.Min(_nextSwitch, Time.unscaledTime + _switchEvery);
                    if (_layoutCountdown)
                        ui.Label(I18n.T("Casting.NextIn") + " " + Mathf.CeilToInt(Mathf.Max(0f, _nextSwitch - Time.unscaledTime)) + " s", Theme.Dim, 8);
                }
                ui.Space(4);
                GUILayout.BeginHorizontal();
                if (ui.SmallBtn(I18n.T("Casting.Prev"), ui.TB3, Theme.Text) && Enabled) CycleTarget(-1);
                GUILayout.Space(4);
                if (ui.SmallBtn(I18n.T("Casting.Next"), ui.TB3, Theme.Text) && Enabled) CycleTarget(1);
                GUILayout.EndHorizontal();
                ui.EndCard();
            }

            // --- Mouvement (fixe / smooth) ---
            ui.Space(6); ui.Section(I18n.T("Casting.Follow"));
            ui.BeginCard();
            int f = ui.ModePicker(new[] { I18n.T("Casting.FollowFixed"), I18n.T("Casting.FollowSmooth") }, (int)_follow);
            if (Enabled && f != (int)_follow) { _follow = (FollowMode)f; MenuPrefs.SetInt("MUM_CastFollow", f); ResetView(); }
            if (_layoutSmooth)
            {
                if (playerView)
                {
                    Sl(ui, "Casting.PosSpeed", ref _posSpeed, 1f, 30f, "0.0", "MUM_CastPosSpeed");
                    Sl(ui, "Casting.RotSpeed", ref _rotSpeed, 1f, 30f, "0.0", "MUM_CastRotSpeed");
                }
                else Sl(ui, "Casting.MapSpeed", ref _mapSpeed, 0.5f, 20f, "0.0", "MUM_CastMapSpeed");
            }
            ui.EndCard();

            if (!playerView)
            {
                ui.Space(6); ui.Section(I18n.T("Casting.ViewMap"));
                ui.BeginCard();
                ui.Label(I18n.T("Casting.MapHint"), Theme.Dim, 8);
                Sl(ui, "Casting.MapYaw", ref _mapYaw, -180f, 180f, "0", "MUM_CastMapYaw");
                Sl(ui, "Casting.MapMargin", ref _mapMargin, 0f, 15f, "0.0", "MUM_CastMapMargin");
                Sl(ui, "Casting.MapMinHeight", ref _mapMinHeight, 2f, 60f, "0", "MUM_CastMapMinH");
                ui.EndCard();
            }
            else
            {
                // --- Ce que suit la camera ---
                ui.Space(6); ui.Section(I18n.T("Casting.FollowTarget"));
                ui.BeginCard();
                int fm = ui.ModePicker(new[] { I18n.T("Casting.FollowPosition"), I18n.T("Casting.FollowHead") }, _followHead ? 1 : 0);
                if (Enabled && (fm == 1) != _followHead) { _followHead = fm == 1; MenuPrefs.SetInt("MUM_CastHead", fm); _init = false; }
                ui.Label(I18n.T("Casting.FollowHint"), Theme.Dim, 8);
                ui.EndCard();

                // --- Decalage de position (de - a +) ---
                ui.Space(6); ui.Section(I18n.T("Casting.OffsetPos"));
                ui.BeginCard();
                Sl(ui, "Casting.OffX", ref _offX, -3f, 3f, "0.00", "MUM_CastOffX");
                Sl(ui, "Casting.OffY", ref _offY, -2f, 4f, "0.00", "MUM_CastOffY");
                Sl(ui, "Casting.Distance", ref _distance, 0f, 10f, "0.0", "MUM_CastDist");
                ui.EndCard();

                // --- Decalage de rotation (de - a +) ---
                ui.Space(6); ui.Section(I18n.T("Casting.OffsetRot"));
                ui.BeginCard();
                Sl(ui, "Casting.PitchOff", ref _pitchOff, -45f, 45f, "0", "MUM_CastPitchOff");
                Sl(ui, "Casting.YawOff", ref _yawOff, -45f, 45f, "0", "MUM_CastYawOff");
                bool av = ui.Toggle(I18n.T("Casting.Avoid"), _avoid);
                if (Enabled && av != _avoid) { _avoid = av; MenuPrefs.SetInt("MUM_CastAvoid", av ? 1 : 0); _wallK = 1f; }
                ui.EndCard();
            }

            ui.Space(6);
            ui.BeginCard();
            if (ui.Btn(I18n.T("Casting.Reset"), ui.TB3, Theme.Text) && Enabled) ResetSettings();
            ui.EndCard();
        }
    }
}
