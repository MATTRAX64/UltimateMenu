#region GorillaMocaps
namespace UltimateMenu
{
    using GorillaLocomotion;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using UnityEngine;
    using UltimateMenu;
    using PhotonPlayer = Photon.Realtime.Player;

    internal class GorillaMocaps : MonoBehaviour
    {
        public static GorillaMocaps Instance { get; private set; }
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Mocap.Title");
        private bool _wasEnabled = true;
        public bool recording;
        public float sampleRate = 72f;
        public bool cameraRecording;
        public bool cameraSelected = true;
        public float cameraSampleRate = 72f;
        private float _cameraSampleTimer;
        private GameObject? _lckCamera;
        private float _nextCameraRefresh;
        private string _camStatus = "Mocap.CamNotFound";
        private static readonly Quaternion CameraRotationOffset = Quaternion.Euler(0f, 180f, 90f);
        public bool originSet;
        private Vector3 _originPos;
        private GameObject? _originMarker;
        private bool _autoOriginDone;
        private float _recordingStartTime = -1f;
        private uint _totalSamples;
        private volatile bool _isSaving;
        float _nextPlayersRefresh;
        private Thread? _saveThread;
        private string _statusMsg = "";
        private float _statusTimer;
        private List<string> _lastExportedFiles = new List<string>();
        private string _mocapFolder = "";
        private string _roomFileCode = "";
        private readonly List<GorillaMocapPlayerData> _dataList = new List<GorillaMocapPlayerData>();
        private GorillaMocapPlayerData? _camData;
        private const string BLENDER_ASSET_RESOURCE = "UltimateMenu.Assets.GorillaMocap.GorillaMocap-BlenderAsset.zip";
        private const string BLENDER_ASSET_FILENAME = "GorillaMocap-BlenderAsset.zip";

        private class PlayerEntry
        {
            public PhotonPlayer? photonPlayer; public VRRig? rig; public bool selected;
            public string displayLabel = ""; public string dataName = ""; public float sampleTimer;
            public readonly Transform?[] parts = new Transform?[4];
            public float nextPartRefresh;
            public MocapCosmeticSession? models;
        }
        private readonly List<PlayerEntry> _players = new List<PlayerEntry>();
        private bool _pageRegistered;
        private int _regTries;

        void Awake() { Instance = this;
            sampleRate = Mathf.Clamp(MenuPrefs.GetFloat("MUM_MocapSampleRate", 72f), 1f, 120f);
            cameraSampleRate = Mathf.Clamp(MenuPrefs.GetFloat("MUM_MocapCameraRate", 72f), 1f, 120f);
            if(MenuPrefs.GetInt("MUM_MocapDefault72",0)==0)
            {
                if(Mathf.Approximately(sampleRate,24f))sampleRate=72f;
                if(Mathf.Approximately(cameraSampleRate,24f))cameraSampleRate=72f;
                MenuPrefs.SetFloat("MUM_MocapSampleRate",sampleRate);MenuPrefs.SetFloat("MUM_MocapCameraRate",cameraSampleRate);
                MenuPrefs.SetInt("MUM_MocapDefault72",1);MenuPrefs.Save();
            }
            cameraSelected = MenuPrefs.GetInt("MUM_MocapCameraSelected", 1) == 1;
            CreateDataFolder(); EnsureBlenderAssetExtracted(); }
        void Start() { GorillaTagger.OnPlayerSpawned(OnPlayerSpawned); }
        void OnPlayerSpawned() { if(!recording&&!_isSaving)NewFileGroup(PhotonInRoom() ? PhotonRoomName() : "NoRoomCode"); RefreshPlayers(); AutoCalibrateOriginOnce(); }

        void Update()
        {
            if (!_pageRegistered && _regTries++ % 60 == 0 && _regTries < 600) TryRegister();
            if (_statusTimer > 0f) _statusTimer -= Time.deltaTime;
            if(cameraSelected&&!cameraRecording&&Time.unscaledTime>=_nextCameraRefresh)
            {_nextCameraRefresh=Time.unscaledTime+.5f;RefreshCamera();}
            UpdateSnapTest();
            bool nowEnabled = Enabled;
            if (nowEnabled == _wasEnabled) return;
            _wasEnabled = nowEnabled;
            if (!nowEnabled)
            {
                if (recording) StopRecording();
                if (cameraRecording) StopCameraRecordingInternal();
                if (_originMarker != null) _originMarker.SetActive(false);
            }
            else if (_originMarker != null) _originMarker.SetActive(true);
        }

        void TryRegister() { if (!_pageRegistered && MenuUI.Instance != null) { MenuUI.Instance.RegisterPage("Mocap.Title", DrawPage, MenuCategory.Mocap); _pageRegistered = true; } }

        private static Type? _photonNetworkType;

        static Type? GetPhotonNetworkType()
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

        bool PhotonInRoom() { var p = GetPhotonNetworkType()?.GetProperty("InRoom", BindingFlags.Public | BindingFlags.Static); return p != null && (bool)(p.GetValue(null) ?? false); }

        string PhotonRoomName()
        {
            var room = GetPhotonNetworkType()?.GetProperty("CurrentRoom", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            return room == null ? "NoRoomCode" : (room.GetType().GetProperty("Name")?.GetValue(room) as string ?? "NoRoomCode");
        }

        PhotonPlayer[] PhotonPlayerList() => GetPhotonNetworkType()?.GetProperty("PlayerList", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as PhotonPlayer[] ?? Array.Empty<PhotonPlayer>();
        PhotonPlayer? PhotonLocalPlayer() => GetPhotonNetworkType()?.GetProperty("LocalPlayer", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as PhotonPlayer;

        void RefreshPlayers()
        {
            if(recording||_isSaving)return;
            var oldSelection = new Dictionary<string, bool>();
            foreach (var p in _players) oldSelection[p.dataName] = p.selected;
            _players.Clear();
            var localRig = VRRig.LocalRig;
            var localPlayer = PhotonLocalPlayer();
            if (localRig != null)
            {
                string realName = localPlayer?.NickName;
                if(string.IsNullOrWhiteSpace(realName))realName=localRig.GetType().GetField("playerName")?.GetValue(localRig)?.ToString();
                if(string.IsNullOrWhiteSpace(realName))realName=MenuPrefs.GetString("playerName","Gorilla");
                _players.Add(new PlayerEntry { photonPlayer = localPlayer, rig = localRig, selected = oldSelection.TryGetValue(realName, out bool sel) ? sel : true, displayLabel = realName, dataName = realName });
            }
            if (PhotonInRoom() && GorillaGameManager.instance != null)
                foreach (var p in PhotonPlayerList())
                {
                    if (p == null || (localPlayer != null && p.ActorNumber == localPlayer.ActorNumber)) continue;
                    var rig = GorillaGameManager.instance.FindPlayerVRRig(p);
                    if (rig == null) continue;
                    _players.Add(new PlayerEntry { photonPlayer = p, rig = rig, selected = oldSelection.TryGetValue(p.NickName, out bool sel) ? sel : false, displayLabel = p.NickName, dataName = p.NickName });
                }
            SetStatus(_players.Count + " " + I18n.T("Mocap.PlayersFound"));
        }

        void AutoCalibrateOriginOnce()
        {
            if (_autoOriginDone || originSet || VRRig.LocalRig == null) return;
            SetOrigin();
            _autoOriginDone = true;
        }

        void SetOrigin()
        {
            var rig = VRRig.LocalRig;
            if (rig == null) { SetStatus(I18n.T("Mocap.PlayerNotFound")); return; }
            _originPos = rig.transform.position;
            if (_originMarker != null) Destroy(_originMarker);
            _originMarker = BuildOriginGizmo();
            _originMarker.transform.position = _originPos;
            _originMarker.transform.rotation = Quaternion.identity;
            if (!Enabled) _originMarker.SetActive(false);
            originSet = true;
            _autoOriginDone = true;
            SetStatus(I18n.T("Mocap.OriginSet"));
        }

        void ClearOrigin() { originSet = false; if (_originMarker != null) Destroy(_originMarker); _originMarker = null; SetStatus(I18n.T("Mocap.OriginCleared")); }

        GameObject BuildOriginGizmo()
        {
            var root = new GameObject("GorillaMocap_Origin");
            CreateAxis(root.transform, Vector3.right, Color.red, "X");
            CreateAxis(root.transform, Vector3.up, Color.green, "Y");
            CreateAxis(root.transform, Vector3.forward, Color.blue, "Z");
            var center = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            center.name = "Center";
            center.transform.SetParent(root.transform, false);
            center.transform.localScale = Vector3.one * 0.05f;
            var col = center.GetComponent<Collider>(); if (col != null) Destroy(col);
            ApplyUnlitColor(center, Color.white);
            return root;
        }

        void CreateAxis(Transform parent, Vector3 dir, Color color, string label)
        {
            float length = 0.25f, thickness = 0.015f;
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Axis_" + label;
            shaft.transform.SetParent(parent, false);
            shaft.transform.localScale = new Vector3(thickness, length * 0.5f, thickness);
            shaft.transform.localPosition = dir * (length * 0.5f);
            shaft.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            var shaftCol = shaft.GetComponent<Collider>(); if (shaftCol != null) Destroy(shaftCol);
            ApplyUnlitColor(shaft, color);
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Tip_" + label;
            tip.transform.SetParent(parent, false);
            tip.transform.localScale = Vector3.one * (thickness * 3f);
            tip.transform.localPosition = dir * length;
            var tipCol = tip.GetComponent<Collider>(); if (tipCol != null) Destroy(tipCol);
            ApplyUnlitColor(tip, color);
        }

        void ApplyUnlitColor(GameObject go, Color color)
        {
            var rend = go.GetComponent<Renderer>(); if (rend == null) return;
            Shader? shader = Shader.Find("GorillaTag/UberShader") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader != null) rend.material = new Material(shader);
            rend.material.color = color;
            if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", color);
        }

        Vector3 ApplyOrigin(Vector3 worldPos) => originSet ? worldPos - _originPos : worldPos;
        Quaternion RelativeToOrigin(Quaternion worldRot) => worldRot;

        public void StartRecording()
        {
            if (!Enabled || recording || _isSaving) return;
            RefreshPlayers();
            SetOrigin();
            if (cameraSelected) RefreshCamera();
            bool anySelected = false;
            foreach (var p in _players) if (p.selected) { anySelected = true; break; }
            bool camReady = cameraSelected && _lckCamera != null;
            if (!originSet) { SetStatus(I18n.T("Mocap.NoOrigin")); return; }
            if (!anySelected && !camReady) { SetStatus(I18n.T("Mocap.SelectPlayerOrCam")); return; }
            if (cameraSelected && !camReady) SetStatus(I18n.T("Mocap.CamNotFound"));
            NewFileGroup(PhotonInRoom() ? PhotonRoomName() : "NoRoomCode");
            foreach(var player in _players)
            {
                player.sampleTimer=0f;
                player.models=player.selected&&player.rig!=null?new MocapCosmeticSession(player.rig,player.displayLabel,_originPos):null;
            }
            recording = true;
            _dataList.Clear();
            _totalSamples=0;
            _recordingStartTime = Time.time;
            if (camReady) StartCameraRecordingInternal();
        }

        public void StopRecording() { if(!recording)return; recording=false; cameraRecording=false; foreach(var playerData in _dataList)playerData.models?.Finish(); if(_camData!=null){_dataList.Add(_camData);_camData=null;} SaveAsync(); }

        void RefreshCamera()
        {
            // Prefer the exact spectator-camera instance; a name lookup can be stale or find another tablet.
            var found=GorillaCamera.Instance!=null?GorillaCamera.Instance.LckCamera:null;
            if(found==null||!found.activeInHierarchy)found=GameObject.Find("LCKTablet(Clone)");
            if (found != null && found.activeInHierarchy) { _lckCamera = found; _camStatus = "Mocap.CamFound"; }
            else { _lckCamera = null; _camStatus = "Mocap.CamNotFound"; }
        }

        private Vector3 _snapSavedPos;
        private Quaternion _snapSavedRot;
        private bool _snapActive;
        private float _snapTimer;

        public void SnapCameraToPlayer()
        {
            if (!Enabled) return;
            if (cameraRecording) { SetStatus(I18n.T("Mocap.CantSnapRecording")); return; }
            if (_lckCamera == null) { SetStatus(I18n.T("Mocap.CamNotFound")); return; }
            var head = VRRig.LocalRig != null ? VRRig.LocalRig.transform.Find("rig/head") : null;
            if (head == null) { SetStatus(I18n.T("Mocap.PlayerNotFound")); return; }
            var camT = _lckCamera.transform;
            _snapSavedPos = camT.position;
            _snapSavedRot = camT.rotation;
            camT.SetPositionAndRotation(head.position, head.rotation);
            _snapActive = true;
            _snapTimer = 1.5f;
            SetStatus(I18n.T("Mocap.CamSnapped"));
        }

        void UpdateSnapTest()
        {
            if (!_snapActive) return;
            _snapTimer -= Time.deltaTime;
            if (_snapTimer <= 0f && _lckCamera != null) { _lckCamera.transform.SetPositionAndRotation(_snapSavedPos, _snapSavedRot); _snapActive = false; SetStatus(I18n.T("Mocap.CamRestored")); }
        }

        void StartCameraRecordingInternal()
        {
            if (cameraRecording || _lckCamera == null) return;
            AutoCalibrateOriginOnce();
            _camData = new GorillaMocapPlayerData("0_Camera", cameraSampleRate, Color.white, 1, Time.time - _recordingStartTimeOrNow());
            _camData.isCamera = true;
            _cameraSampleTimer = 0f;
            cameraRecording = true;
        }

        void StopCameraRecordingInternal()
        {
            if (!cameraRecording) return;
            cameraRecording = false;
            if (_camData != null) SaveSingleFileAsync(_camData);
            _camData = null;
        }

        float _recordingStartTimeOrNow() => _recordingStartTime >= 0f ? _recordingStartTime : Time.time;

        void FixedUpdate()
        {
            if (!Enabled) return;
            if (recording) foreach (var p in _players) { if (p.selected && p.rig != null) StorePlayerData(p); }
            if (cameraRecording) StoreCameraLive();
        }

        bool SampleReady(ref float timer, float rate)
        {
            timer += Time.deltaTime;
            if (timer >= 1f / Mathf.Max(rate, 1f)) { timer = 0f; return true; }
            return false;
        }

        void StorePlayerData(PlayerEntry p)
        {
            if (p.rig == null || !SampleReady(ref p.sampleTimer, sampleRate)) return;
            string key = (p.photonPlayer != null ? p.photonPlayer.ActorNumber : 0) + "_" + p.dataName;
            int idx = _dataList.FindIndex(d => d.name == key);
            float lastSeen = Time.time - _recordingStartTime;
            // Reuse the four slots and resolve missing bones at most once per second.
            var parts = p.parts;
            if (Time.unscaledTime >= p.nextPartRefresh)
            {
                p.nextPartRefresh = Time.unscaledTime + 1f;
                if (parts[0] == null) parts[0] = p.rig.transform.Find("rig/head");
                if (parts[1] == null) parts[1] = p.rig.transform.Find("rig/hand.L");
                if (parts[2] == null) parts[2] = p.rig.transform.Find("rig/hand.R");
                if (parts[3] == null) parts[3] = p.rig.transform.Find("rig/body_pivot/body/body_new");
            }
            // Keep all channels aligned if a rig is temporarily incomplete.
            for (int i = 0; i < parts.Length; i++) if (parts[i] == null) return;
            if (idx == -1) { _dataList.Add(new GorillaMocapPlayerData(key, sampleRate, GetColor(p.rig), 4, lastSeen){models=p.models});p.models=null;idx = _dataList.Count - 1; }
            var appearanceData=_dataList[idx];
            appearanceData.models?.Sample(appearanceData.objects[0].locations.Count);
            string currentName=p.photonPlayer?.NickName;
            if(string.IsNullOrWhiteSpace(currentName))currentName=p.displayLabel;
            Color currentColor=GetColor(p.rig);
            var previous=appearanceData.appearance.Count>0?appearanceData.appearance[appearanceData.appearance.Count-1]:null;
            if(previous==null||previous.Name!=currentName||previous.Color!=currentColor)
                appearanceData.appearance.Add(new GorillaMocapAppearance{Sample=appearanceData.objects[0].locations.Count,Name=currentName,Color=currentColor});
            _dataList[idx].lastSeenTime = lastSeen;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                _dataList[idx].objects[i].locations.Add(ApplyOrigin(parts[i].position));
                _dataList[idx].objects[i].rotations.Add(RelativeToOrigin(parts[i].rotation));
            }
            if (p.photonPlayer != null && p.photonPlayer.IsLocal)
            {
                _dataList[idx].fingers[0].values.Add(p.rig.leftIndex.triggerValue);
                _dataList[idx].fingers[1].values.Add(p.rig.leftMiddle.gripValue);
                _dataList[idx].fingers[2].values.Add(p.rig.leftThumb.secondaryButtonPress ? 1f : 0f);
                _dataList[idx].fingers[3].values.Add(p.rig.rightIndex.triggerValue);
                _dataList[idx].fingers[4].values.Add(p.rig.rightMiddle.gripValue);
                _dataList[idx].fingers[5].values.Add(p.rig.rightThumb.secondaryButtonPress ? 1f : 0f);
            }
            _dataList[idx].skinIndex.values.Add(DetectSkin(p.rig));
            _totalSamples++;
        }

        int DetectSkin(VRRig rig) { try { var tex = rig.mainSkin != null && rig.mainSkin.material != null ? rig.mainSkin.material.mainTexture : null; return tex != null && tex.name == "lavasmall" ? 1 : 0; } catch { return 0; } }
        Color GetColor(VRRig rig) { try { return rig.mainSkin.sharedMaterial.color; } catch { return Color.white; } }

        void StoreCameraLive()
        {
            if (_camData == null || _snapActive) return;
            if (_lckCamera == null || !_lckCamera.activeInHierarchy) { cameraRecording = false; SaveSingleFileAsync(_camData); _camData = null; SetStatus(I18n.T("Mocap.CamLost")); return; }
            if (!SampleReady(ref _cameraSampleTimer, cameraSampleRate)) return;
            var camT = _lckCamera.transform;
            _camData.lastSeenTime = Time.time - _recordingStartTimeOrNow();
            _camData.objects[0].locations.Add(ApplyOrigin(camT.position));
            _camData.objects[0].rotations.Add(RelativeToOrigin(camT.rotation) * CameraRotationOffset);
        }

        void CreateDataFolder()
        {
            _mocapFolder = Path.Combine(ReleaseUpdater.DataFolder, "GorillaMocap");
            if (!Directory.Exists(_mocapFolder)) Directory.CreateDirectory(_mocapFolder);
        }

        void EnsureBlenderAssetExtracted()
        {
            string targetPath = Path.Combine(_mocapFolder, BLENDER_ASSET_FILENAME);
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BLENDER_ASSET_RESOURCE);
                if (stream == null) { Debug.LogError("[GorillaMocap] Ressource embarquée introuvable : " + BLENDER_ASSET_RESOURCE + ". Vérifie que GorillaMocap-BlenderAsset.zip est bien marqué 'Embedded Resource' dans le projet."); return; }
                using var bundled = new MemoryStream();
                stream.CopyTo(bundled);
                byte[] bytes=bundled.ToArray();
                if(File.Exists(targetPath))
                {
                    using var hash=System.Security.Cryptography.SHA256.Create();
                    using var existing=File.OpenRead(targetPath);
                    if(Convert.ToBase64String(hash.ComputeHash(existing))==Convert.ToBase64String(hash.ComputeHash(bytes)))return;
                }
                string temporary=targetPath+".tmp";
                try { File.WriteAllBytes(temporary,bytes);File.Copy(temporary,targetPath,true); }
                finally { if(File.Exists(temporary))File.Delete(temporary); }
                Debug.Log("[GorillaMocap] GorillaMocap-BlenderAsset.zip extrait vers " + targetPath);
            }
            catch (Exception e) { Debug.LogError("[GorillaMocap] EnsureBlenderAssetExtracted: " + e); }
        }

        public void NewFileGroup(string roomCode) { if (string.IsNullOrEmpty(_mocapFolder)) CreateDataFolder(); _roomFileCode = MocapPaths.TakeCode(DateTime.Now); }

        public void SaveAsync()
        {
            if (_isSaving || _dataList.Count == 0) { SetStatus(I18n.T("Mocap.NothingToSave")); return; }
            _isSaving=true;
            _saveThread = new Thread(SaveThreaded);
            _saveThread.Start();
        }

        void SaveThreaded()
        {
            _isSaving = true;
            var files = new List<string>();
            try
            {
                if (string.IsNullOrEmpty(_mocapFolder)) CreateDataFolder();
                foreach (var data in _dataList)
                {
                    string folder=Path.Combine(_mocapFolder,MocapPaths.SafeName(data.name)+"_"+_roomFileCode);
                    Directory.CreateDirectory(folder);
                    string path=Path.Combine(folder,data.isCamera?"Camera.txt":"Movement.txt");
                    WritePlayerFile(path, data);
                    files.Add(path);
                }
            }
            catch (Exception e) { Debug.LogError("[GorillaMocap] Save: " + e); }
            _lastExportedFiles = files;
            _recordingStartTime = -1f;
            _totalSamples = 0;
            _dataList.Clear();
            _isSaving=false;
            SetStatus(files.Count + " " + I18n.T("Mocap.FilesExported"));
        }

        void SaveSingleFileAsync(GorillaMocapPlayerData data)
        {
            string takeCode=_roomFileCode;
            new Thread(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(_mocapFolder)) CreateDataFolder();
                    string folder=Path.Combine(_mocapFolder,MocapPaths.SafeName(data.name)+"_"+takeCode);
                    Directory.CreateDirectory(folder);
                    string path=Path.Combine(folder,data.isCamera?"Camera.txt":"Movement.txt");
                    WritePlayerFile(path, data);
                    _lastExportedFiles = new List<string> { path };
                    SetStatus(I18n.T("Mocap.CamExported") + " : " + Path.GetFileName(path));
                }
                catch (Exception e) { Debug.LogError("[GorillaMocap] SaveCam: " + e); }
            }).Start();
        }

        void WritePlayerFile(string path, GorillaMocapPlayerData data)
        {
            var sb = new StringBuilder();
            sb.Append('#').Append("2.1.8").Append('$').Append(data.framerate).Append('$').Append(data.name).Append('$').Append(data.color.ToString()).Append('$').Append(data.startTime).Append('$').Append(data.lastSeenTime).Append('$').Append(data.isCamera ? "camera" : "player").Append('\n');
            sb.Append('#').Append("room=").Append(PhotonInRoom() ? PhotonRoomName() : "Hors ligne").Append('$').Append("date=").Append(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")).Append('$').Append("origin=").Append(originSet ? "1" : "0").Append('\n');
            foreach(var appearance in data.appearance)
            {
                sb.Append("#appearance$").Append(appearance.Sample).Append('$')
                    .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(appearance.Name??"")));
                foreach(float component in new[]{appearance.Color.r,appearance.Color.g,appearance.Color.b,appearance.Color.a})
                    sb.Append('$').Append(component.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
            File.WriteAllText(path, sb.ToString());
            data.models?.Write(Path.GetDirectoryName(path));
            if (data.objects.Count == 0 || data.objects[0].locations.Count == 0) return;
            var lineBuilder = new StringBuilder();
            var fileSb = new StringBuilder();
            for (int j = 0; j < data.objects[0].locations.Count; j++)
            {
                lineBuilder.Clear();
                for (int o = 0; o < data.objects.Count; o++)
                {
                    if (o > 0) lineBuilder.Append('$');
                    lineBuilder.Append(data.objects[o].locations[j].ToString("F3")).Append('~').Append(data.objects[o].rotations[j].ToString("F3"));
                }
                if (!data.isCamera && data.fingers[0].values.Count > 0) foreach (var f in data.fingers) lineBuilder.Append('$').Append(f.values[j].ToString("F1"));
                if (!data.isCamera && data.skinIndex.values.Count > j) lineBuilder.Append('$').Append(data.skinIndex.values[j].ToString("F0"));
                fileSb.Append(lineBuilder).Append('\n');
            }
            File.AppendAllText(path, fileSb.ToString());
        }

        void SetStatus(string msg) { _statusMsg = msg; _statusTimer = 5f; }

        bool _advancedOpen;
        void DrawPage(MenuUI ui)
        {
            if(!recording&&!_isSaving&&Time.unscaledTime>=_nextPlayersRefresh){_nextPlayersRefresh=Time.unscaledTime+2f;RefreshPlayers();}
            ui.BeginCard();
            ui.StatusPill(recording?I18n.T("Mocap.Recording"):I18n.T("Mocap.Stopped"),recording);
            if(recording)ui.Label(FormatTime(Time.time-_recordingStartTime)+" · "+_totalSamples+" "+I18n.T("Mocap.Samples"),Theme.Text,10,true);
            if(ui.Btn(recording?I18n.T("Mocap.StopSave"):I18n.T("Mocap.StartRecord"),ui.TB3,Theme.Text)&&Enabled)
            {if(recording)StopRecording();else StartRecording();}
            if(_isSaving)ui.Label(I18n.T("Mocap.Saving"),Theme.Dim,9);
            if(_statusTimer>0&&!string.IsNullOrEmpty(_statusMsg))ui.Label(_statusMsg,Theme.Dim,9);
            ui.EndCard();
            ui.Section(I18n.T("Mocap.ToRecord"));
            if(ui.Btn(I18n.T("Mocap.CheckPlayers"),ui.TB3,Theme.Text)&&Enabled)RefreshPlayers();
            foreach(var player in _players)
            {
                string label=player.displayLabel.Length>64?player.displayLabel.Substring(0,64):player.displayLabel;
                bool selected=ui.SelectionRow(label,player.selected);
                if(!_isSaving)player.selected=selected;
            }
            bool camera=ui.SelectionRow(I18n.T("Mocap.SpectatorCam"),cameraSelected);
            if(!recording&&!_isSaving&&cameraSelected!=camera){cameraSelected=camera;MenuPrefs.SetInt("MUM_MocapCameraSelected",camera?1:0);}
            if(cameraSelected)
            {
                ui.Label(I18n.T("Mocap.SpectatorCam")+" · "+I18n.T(_camStatus),Theme.Dim,9);
                if(ui.Btn(I18n.T("Mocap.RefreshCam"),ui.TB3,Theme.Text)&&Enabled)RefreshCamera();
            }
            ui.ExpandableSection(I18n.T("VR.Advanced"),"",ref _advancedOpen,()=>
            {
                float rate=ui.Slider(I18n.T("Mocap.PlayerFPS"),sampleRate,1f,120f,"0");
                if(rate!=sampleRate){sampleRate=rate;MenuPrefs.SetFloat("MUM_MocapSampleRate",rate);}
                if(cameraSelected)
                {
                    float cameraRate=ui.Slider(I18n.T("Mocap.CamFPS"),cameraSampleRate,1f,120f,"0");
                    if(cameraRate!=cameraSampleRate){cameraSampleRate=cameraRate;MenuPrefs.SetFloat("MUM_MocapCameraRate",cameraRate);}
                    if(_lckCamera!=null&&ui.Btn(I18n.T("Mocap.AlignToMe"),ui.TB3,Theme.Text)&&Enabled)SnapCameraToPlayer();
                }
                ui.Label(I18n.T("Mocap.OriginAuto"),Theme.Dim,9);
                if(originSet&&ui.Btn(I18n.T("Mocap.ClearOrigin"),ui.TB3,Theme.Text)&&Enabled)ClearOrigin();
                if(File.Exists(Path.Combine(_mocapFolder,BLENDER_ASSET_FILENAME)))
                    ui.Label(I18n.T("Mocap.BlenderAssetHint"),Theme.Dim,9);
            });
            if(_lastExportedFiles.Count>0)
            {
                ui.Section(I18n.T("Mocap.LastExported"));
                ui.BeginCard();
                foreach(var file in _lastExportedFiles)ui.Label(Path.GetFileName(file),Theme.Text,9);
                ui.EndCard();
            }
        }
        string FormatTime(float t) => (int)(t / 60f) + ":" + ((int)(t % 60f)).ToString("00");
        void OnDestroy() { if (_originMarker != null) Destroy(_originMarker); }
    }

    internal class GorillaMocapObject { public List<Vector3> locations = new List<Vector3>(); public List<Quaternion> rotations = new List<Quaternion>(); }
    internal class GorillaMocapFloat { public List<float> values = new List<float>(); }

    internal class GorillaMocapAppearance { public int Sample; public string Name=""; public Color Color; }

    internal class GorillaMocapPlayerData
    {
        public readonly List<GorillaMocapAppearance> appearance=new List<GorillaMocapAppearance>();
        public MocapCosmeticSession? models;
        public string name;
        public float framerate;
        public Color color;
        public float startTime, lastSeenTime;
        public bool isCamera;
        public List<GorillaMocapObject> objects = new List<GorillaMocapObject>();
        public List<GorillaMocapFloat> fingers = new List<GorillaMocapFloat>();
        public GorillaMocapFloat skinIndex = new GorillaMocapFloat();

        public GorillaMocapPlayerData(string name, float framerate, Color color, int boneCount, float startTime)
        {
            this.name = name; this.framerate = framerate; this.color = color; this.startTime = startTime;
            for (int i = 0; i < boneCount; i++) objects.Add(new GorillaMocapObject());
            for (int i = 0; i < 6; i++) fingers.Add(new GorillaMocapFloat());
        }
    }
}
#endregion

#region MocapCosmeticSession
namespace UltimateMenu
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using UnityEngine;

    // Capture actual cosmetic animation, including physics, without running simulation in Blender.
    internal sealed class MocapCosmeticSession
    {
        internal const int Magic=0x32434D55;
        [Serializable] internal sealed class State
        {
            public string manifest="";
            public string[] nodes=Array.Empty<string>();
            public int itemCount;
        }
        [Serializable] internal sealed class Timeline
        {
            public int schema=1;
            public string motion="CosmeticMotion.bin.gz";
            public List<State> states=new List<State>();
        }
        readonly VRRig _rig;
        readonly string _player;
        readonly Vector3 _origin;
        readonly Timeline _timeline=new Timeline();
        readonly List<MocapModelExport> _snapshots=new List<MocapModelExport>();
        readonly Dictionary<string,int> _known=new Dictionary<string,int>();
        readonly MemoryStream _bytes=new MemoryStream();
        readonly BinaryWriter _writer;
        readonly GZipStream _zip;
        byte[] _finished;
        int _state;
        float _nextCheck;
        readonly MaterialPropertyBlock _block=new MaterialPropertyBlock();

        internal MocapCosmeticSession(VRRig rig,string player,Vector3 origin)
        {
            _rig=rig;_player=player;_origin=origin;
            _zip=new GZipStream(_bytes,CompressionMode.Compress,true);_writer=new BinaryWriter(_zip,Encoding.UTF8,true);
            _writer.Write(Magic); // UMC2, little endian: adds per-renderer blend shape weights
            SelectState();
        }
        string Signature()
        {
            var text=new StringBuilder();
            foreach(var renderer in MocapModelExport.Renderers(_rig))
            {
                if(renderer==_rig.mainSkin||!renderer.enabled||!renderer.gameObject.activeInHierarchy||!(renderer is MeshRenderer||renderer is SkinnedMeshRenderer)||renderer.GetComponentInParent<TMPro.TMP_Text>()!=null)continue;
                text.Append(renderer.GetInstanceID()).Append(':');
                Mesh mesh=renderer is SkinnedMeshRenderer skin?skin.sharedMesh:renderer.GetComponent<MeshFilter>()?.sharedMesh;
                text.Append(mesh!=null?mesh.GetInstanceID():0).Append(':');
                foreach(var material in renderer.sharedMaterials)
                {
                    if(material==null)continue;
                    text.Append(material.GetInstanceID()).Append('/');
                    foreach(string property in new[]{"_BaseMap","_MouthMap","_BaseMap_Atlas"})
                        if(material.HasProperty(property)){var image=material.GetTexture(property);text.Append(image!=null?image.GetInstanceID():0).Append(',');}
                    if(material.HasProperty("_BaseMap_AtlasSlice"))text.Append(material.GetFloat("_BaseMap_AtlasSlice")).Append(';');
                }
            }
            return text.ToString();
        }
        void SelectState()
        {
            string signature=Signature();
            if(_known.TryGetValue(signature,out _state))return;
            var snapshot=MocapModelExport.Capture(_rig,_player,_origin);
            _state=_snapshots.Count;_known[signature]=_state;_snapshots.Add(snapshot);
            _timeline.states.Add(new State{manifest="Wardrobe/"+_state.ToString("D3")+"/Models.json",nodes=snapshot.MotionPaths.Where(path=>snapshot.LiveNodes.ContainsKey(path)).ToArray(),itemCount=snapshot.Data.items.Count});
        }
        internal void Sample(int sample)
        {
            if(_finished!=null)return;
            if(Time.unscaledTime>=_nextCheck){_nextCheck=Time.unscaledTime+.2f;SelectState();}
            var snapshot=_snapshots[_state];var state=_timeline.states[_state];
            _writer.Write(sample);_writer.Write(_state);_writer.Write(state.nodes.Length);
            foreach(string path in state.nodes)
            {
                var node=snapshot.LiveNodes[path];
                var matrix=node!=null?Matrix4x4.Translate(-_origin)*node.localToWorldMatrix:Matrix4x4.identity;
                WritePose(_writer,matrix);
            }
            _writer.Write(state.itemCount);
            foreach(var item in snapshot.Data.items)
            {
                var renderer=snapshot.LiveItems[item.path];
                _writer.Write(item.kind=="body"||renderer!=null&&renderer.enabled&&renderer.gameObject.activeInHierarchy);
                var materials=snapshot.LiveMaterials[item.path];
                _writer.Write(item.materials.Length);
                for(int slot=0;slot<item.materials.Length;slot++)
                {
                    var material=slot<materials.Length?materials[slot]:null;
                    _block.Clear();
                    if(renderer!=null){renderer.GetPropertyBlock(_block,slot);if(_block.isEmpty)renderer.GetPropertyBlock(_block);}
                    foreach(string property in new[]{"_BaseMap_ST","_MouthMap_ST"})
                    {
                        var value=material!=null&&material.HasProperty(property)?material.GetVector(property):new Vector4(1,1,0,0);
                        if(_block.HasProperty(Shader.PropertyToID(property)))value=_block.GetVector(property);
                        _writer.Write(value.x);_writer.Write(value.y);_writer.Write(value.z);_writer.Write(value.w);
                    }
                }
                _writer.Write(item.blendShapes.Length);
                var skin=renderer as SkinnedMeshRenderer;
                foreach(int shape in item.blendShapes)
                {
                    float weight=skin!=null?skin.GetBlendShapeWeight(shape):0;
                    _writer.Write(float.IsNaN(weight)||float.IsInfinity(weight)?0:weight);
                }
            }
        }
        internal static void WritePose(BinaryWriter writer,Matrix4x4 matrix)
        {
            bool finite=true;
            for(int row=0;row<3;row++)for(int column=0;column<4;column++)
                if(float.IsNaN(matrix[row,column])||float.IsInfinity(matrix[row,column]))finite=false;
            if(!finite)matrix=Matrix4x4.identity;
            for(int row=0;row<3;row++)for(int column=0;column<4;column++)writer.Write(matrix[row,column]);
        }
        internal void Finish()
        {
            if(_finished!=null)return;
            _writer.Flush();_writer.Dispose();_zip.Dispose();_finished=_bytes.ToArray();_bytes.Dispose();
        }
        internal void Write(string folder)
        {
            if(_finished==null)throw new InvalidOperationException("Cosmetic capture must finish on the main thread");
            _snapshots[0].Write(folder);
            for(int i=0;i<_snapshots.Count;i++)_snapshots[i].Write(Path.Combine(folder,"Wardrobe",i.ToString("D3")));
            File.WriteAllBytes(Path.Combine(folder,_timeline.motion),_finished);
            File.WriteAllText(Path.Combine(folder,"CosmeticTimeline.json"),MocapModelExport.Json(_timeline),new UTF8Encoding(false));
        }
    }
}
#endregion

#region MocapModelExport
namespace UltimateMenu
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Collections;
    using System.Reflection;
    using UnityEngine;
    using UnityEngine.Rendering;

    // Unity objects are read on the main thread before recording. Saving only writes captured bytes.
    internal sealed class MocapModelExport
    {
        [Serializable] internal sealed class Node
        {
            public string path="",parent="";
            public Vector3 position,scale;
            public Quaternion rotation;
            public float[] matrix=Array.Empty<float>();
        }
        [Serializable] internal sealed class Surface
        {
            public string name="",shader="",texture="";
            public Color color=Color.white;
            public Vector2 offset,scale=Vector2.one;
            public bool sliceFromUv1;
            public int slice;
            public AtlasImage[] slices=Array.Empty<AtlasImage>();
            public string mouthTexture="",directTexture="";
            public Vector2 mouthOffset,mouthScale=Vector2.one;
            public TextureChannel[] channels=Array.Empty<TextureChannel>();
            public Parameter[] parameters=Array.Empty<Parameter>();
            public string[] keywords=Array.Empty<string>();
            public int renderQueue;
            public bool hasVertexColor;
        }
        [Serializable] internal sealed class Parameter {public string name="";public float[] values=Array.Empty<float>();}
        [Serializable] internal sealed class TextureChannel
        {
            public string property="",dimension="",format="";
            public Vector2 scale=Vector2.one,offset;
            public bool sliceFromUv1;
            public int slice;
            public AtlasImage[] images=Array.Empty<AtlasImage>();
        }
        [Serializable] internal sealed class UvChannel {public int channel;public Vector4[] values=Array.Empty<Vector4>();}
        [Serializable] internal sealed class AtlasImage {public int index;public string texture="";}
        [Serializable] internal sealed class Triangles { public int[] indices=Array.Empty<int>(); }
        [Serializable] internal sealed class Influence { public int[] bones=Array.Empty<int>();public float[] weights=Array.Empty<float>(); }
        [Serializable] internal sealed class ShapeFrame { public float weight; public Vector3[] delta=Array.Empty<Vector3>(); }
        [Serializable] internal sealed class Shape { public string name=""; public int index; public ShapeFrame[] frames=Array.Empty<ShapeFrame>(); }
        [Serializable] internal sealed class Geometry
        {
            public Vector3[] vertices=Array.Empty<Vector3>(),normals=Array.Empty<Vector3>();
            public Vector3[] restVertices=Array.Empty<Vector3>();
            public Vector2[] uv=Array.Empty<Vector2>();
            public float[] atlasSlices=Array.Empty<float>();
            public UvChannel[] uvChannels=Array.Empty<UvChannel>();
            public Color[] colors=Array.Empty<Color>();
            public Vector3[] restNormals=Array.Empty<Vector3>();
            public Triangles[] submeshes=Array.Empty<Triangles>();
            public string[] bones=Array.Empty<string>();
            public float[] bindposes=Array.Empty<float>();
            public Influence[] weights=Array.Empty<Influence>();
            public Shape[] shapes=Array.Empty<Shape>();
        }
        [Serializable] internal sealed class Item
        {
            public string name="",path="",attachment="",geometry="",preview="",kind="";
            public float[] matrix=Array.Empty<float>();
            public Surface[] materials=Array.Empty<Surface>();
            public int[] blendShapes=Array.Empty<int>();
        }
        [Serializable] internal sealed class Manifest
        {
            public int schema=1;
            public string player="",version="",captured="",coordinates="JSON: Unity left-handed, Y up, metres. Matrices: row-major. OBJ: X right, Y forward, Z up; import with forward Y / up Z.";
            public string pose="Start-of-take snapshot. Vertices are posed renderer-local coordinates. restVertices and bindposes retain source skinning when available. No shader baking or item animation is included.";
            public Vector3 origin;
            public List<Node> skeleton=new List<Node>();
            public List<Item> items=new List<Item>();
            public List<string> warnings=new List<string>();
        }
        readonly Dictionary<string,byte[]> _files=new Dictionary<string,byte[]>();
        readonly Dictionary<string,string> _textures=new Dictionary<string,string>();
        readonly Manifest _manifest=new Manifest();
        internal readonly Dictionary<string,Transform> LiveNodes=new Dictionary<string,Transform>();
        internal readonly Dictionary<string,Renderer> LiveItems=new Dictionary<string,Renderer>();
        internal readonly Dictionary<string,Material[]> LiveMaterials=new Dictionary<string,Material[]>();
        internal readonly HashSet<string> MotionPaths=new HashSet<string>();
        internal Manifest Data=>_manifest;
        static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
        static string Number(float value)=>value.ToString("R",Invariant);
        static string PathOf(Transform node,Transform root)
        {
            if(node==null)return "";
            var parts=new List<string>();
            while(node!=null){parts.Add(node.name+"["+node.GetSiblingIndex()+"]");if(node==root)break;node=node.parent;}
            parts.Reverse();return string.Join("/",parts);
        }
        static float[] Matrix(Matrix4x4 value)
        {
            var result=new float[16];for(int row=0;row<4;row++)for(int col=0;col<4;col++)result[row*4+col]=value[row,col];return result;
        }
        void Text(string path,string value)=>_files[path]=new UTF8Encoding(false).GetBytes(value);
        internal static string Json(object value)
        {
            var text=new StringBuilder();JsonValue(text,value);return text.ToString();
        }
        static void JsonString(StringBuilder text,string value)
        {
            text.Append('"');
            foreach(char c in value)
            {
                if(c=='"'||c=='\\')text.Append('\\').Append(c);
                else if(c<32||char.IsSurrogate(c))text.Append("\\u").Append(((int)c).ToString("x4",Invariant));
                else text.Append(c);
            }
            text.Append('"');
        }
        static void JsonValue(StringBuilder text,object value)
        {
            if(value==null){text.Append("null");return;}
            if(value is string str){JsonString(text,str);return;}
            if(value is bool flag){text.Append(flag?"true":"false");return;}
            if(value is float number){if(float.IsNaN(number)||float.IsInfinity(number))throw new InvalidDataException("Non-finite model data");text.Append(Number(number));return;}
            if(value is double dbl){if(double.IsNaN(dbl)||double.IsInfinity(dbl))throw new InvalidDataException("Non-finite model data");text.Append(dbl.ToString("R",Invariant));return;}
            if(value.GetType().IsPrimitive){text.Append(Convert.ToString(value,Invariant));return;}
            if(value is IEnumerable list)
            {
                text.Append('[');bool comma=false;foreach(var item in list){if(comma)text.Append(',');JsonValue(text,item);comma=true;}text.Append(']');return;
            }
            // Serialize DTO fields only: Unity properties can invoke native code and recurse.
            var type=value.GetType();
            if(type.DeclaringType!=typeof(MocapModelExport)&&type.DeclaringType!=typeof(MocapCosmeticSession)&&type!=typeof(Vector2)&&type!=typeof(Vector3)&&type!=typeof(Vector4)&&type!=typeof(Quaternion)&&type!=typeof(Color))
                throw new NotSupportedException("Model JSON type "+type.FullName);
            text.Append('{');bool separator=false;
            foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))
            {
                if(separator)text.Append(',');JsonString(text,field.Name);text.Append(':');JsonValue(text,field.GetValue(value));separator=true;
            }
            text.Append('}');
        }
        void Warning(string value){_manifest.warnings.Add(value);Log.Info("Mocap models: "+value);}

        internal static MocapModelExport Capture(VRRig rig,string player,Vector3 origin)
        {
            var export=new MocapModelExport();
            export._manifest.player=player;export._manifest.version=Log.PluginVersion??"";
            export._manifest.captured=DateTimeOffset.Now.ToString("O");export._manifest.origin=origin;
            try { export.Read(rig,origin); }
            catch(Exception error){export.Warning("Capture incomplete: "+error.Message);}
            export.Text("Models.json",Json(export._manifest));
            Log.Info("Mocap models: captured "+export._manifest.items.Count+" meshes for "+player+", warnings="+export._manifest.warnings.Count);
            return export;
        }
        static Transform _localHeadCosmetics;
        internal static Renderer[] Renderers(VRRig rig)
        {
            bool Valid(Renderer renderer)=>(renderer==rig.mainSkin||renderer.enabled)&&renderer.gameObject.activeInHierarchy&&(renderer is MeshRenderer||renderer is SkinnedMeshRenderer)&&renderer.GetComponentInParent<TMPro.TMP_Text>()==null;
            var original=rig.GetComponentsInChildren<Renderer>(true).Where(Valid).ToList();
            if(!rig.isOfflineVRRig)return original.ToArray();
            if(_localHeadCosmetics==null)
            {
                var head=GameObject.Find("Player Objects/Player VR Controller/GorillaPlayer/TurnParent/Main Camera/HeadCosmetics");
                _localHeadCosmetics=head!=null?head.transform:Camera.main!=null?Camera.main.transform.Find("HeadCosmetics"):null;
            }
            if(_localHeadCosmetics==null)return original.ToArray();
            // Prefer the rig copy when the same equipped cosmetic also has a first-person copy.
            var existing=new HashSet<string>();
            foreach(var renderer in original)
                for(var node=renderer.transform;node!=null&&node!=rig.transform;node=node.parent)existing.Add(node.name);
            foreach(Transform cosmetic in _localHeadCosmetics)
                if(!existing.Contains(cosmetic.name))original.AddRange(cosmetic.GetComponentsInChildren<Renderer>(true).Where(Valid));
            return original.Distinct().ToArray();
        }
        void Read(VRRig rig,Vector3 origin)
        {
            var root=rig.transform;
            var renderers=Renderers(rig);
            var bones=new HashSet<Transform>();
            if(rig.mainSkin!=null)foreach(var bone in rig.mainSkin.bones)if(bone!=null)bones.Add(bone);
            foreach(var bone in bones)
            {
                string name=new string(bone.name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
                if(new[]{"bodynew","headnew","handlnew","handrnew","forearmlnew","forearmrnew"}.Contains(name))MotionPaths.Add(PathOf(bone,root));
            }
            foreach(var skin in renderers.OfType<SkinnedMeshRenderer>())foreach(var bone in skin.bones)if(bone!=null)bones.Add(bone);
            var nodes=new HashSet<Transform>();
            foreach(var node in bones.Concat(renderers.Select(r=>r.transform)))
            {
                var current=node;
                while(current!=null){nodes.Add(current);if(current==root)break;current=current.parent;}
            }
            foreach(var node in nodes.OrderBy(t=>PathOf(t,root)))
            {
                LiveNodes[PathOf(node,root)]=node;
                _manifest.skeleton.Add(new Node{
                path=PathOf(node,root),parent=node==root?"":PathOf(node.parent,root),position=node.localPosition,
                rotation=node.localRotation,scale=node.localScale,matrix=Matrix(Matrix4x4.Translate(-origin)*node.localToWorldMatrix)});
            }
            int index=0;
            foreach(var renderer in renderers)
            {
                Mesh baked=null;
                try
                {
                    var skin=renderer as SkinnedMeshRenderer;
                    var filter=renderer.GetComponent<MeshFilter>();
                    Mesh source=skin!=null?skin.sharedMesh:filter!=null?filter.sharedMesh:null;
                    if(source==null)continue;
                    bool body=renderer==rig.mainSkin;
                    string directory=(body?"Body/":"Cosmetics/")+(index++).ToString("D3")+"_"+Safe(renderer.name);
                    string attach=FindAttachment(renderer.transform,bones,root);
                    if(!renderer.transform.IsChildOf(root))
                    {
                        var head=bones.FirstOrDefault(b=>b!=null&&new string(b.name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray())=="headnew");
                        if(head!=null)attach=PathOf(head,root);
                    }
                    var item=new Item{name=renderer.name,path=PathOf(renderer.transform,root),attachment=attach,
                        kind=body?"body":"cosmetic",geometry=directory+"/Mesh.json",preview=directory+"/Model.obj",
                        matrix=Matrix(Matrix4x4.Translate(-origin)*renderer.localToWorldMatrix)};
                    Geometry geometry;
                    if(skin!=null)
                    {
                        baked=new Mesh();skin.BakeMesh(baked,false);
                        geometry=ReadGeometry(baked);
                        geometry.bones=skin.bones.Select(b=>PathOf(b,root)).ToArray();
                        try
                        {
                            var original=ReadGeometry(source);geometry.restVertices=original.vertices;geometry.restNormals=original.normals;
                            geometry.bindposes=source.bindposes.SelectMany(Matrix).ToArray();
                            if(source.isReadable)
                                geometry.weights=source.boneWeights.Select(w=>new Influence{bones=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3},weights=new[]{w.weight0,w.weight1,w.weight2,w.weight3}}).ToArray();
                            else geometry.weights=GpuWeights(source);
                        }
                        catch(Exception e){Warning(item.path+": skin weights unavailable; posed mesh saved. "+e.Message);}
                    }
                    else geometry=ReadGeometry(source);
                    if(skin!=null && geometry.restVertices.Length==geometry.vertices.Length)
                    {
                        var shapes=new List<Shape>();
                        for(int shapeIndex=0;shapeIndex<source.blendShapeCount;shapeIndex++)
                        {
                            try
                            {
                                var frames=new List<ShapeFrame>();
                                for(int frameIndex=0;frameIndex<source.GetBlendShapeFrameCount(shapeIndex);frameIndex++)
                                {
                                    var delta=new Vector3[source.vertexCount];
                                    source.GetBlendShapeFrameVertices(shapeIndex,frameIndex,delta,null,null);
                                    frames.Add(new ShapeFrame{weight=source.GetBlendShapeFrameWeight(shapeIndex,frameIndex),delta=delta});
                                }
                                shapes.Add(new Shape{name=source.GetBlendShapeName(shapeIndex),index=shapeIndex,frames=frames.ToArray()});
                            }
                            catch(Exception e){Warning(item.path+": blend shape unavailable: "+e.Message);}
                        }
                        geometry.shapes=shapes.ToArray();item.blendShapes=shapes.Select(shape=>shape.index).ToArray();
                    }
                    item.materials=Materials(renderer,geometry);
                    string mtl=MakeMtl(item.materials);
                    Text(directory+"/Materials.mtl",mtl);
                    Text(item.geometry,Json(geometry));
                    Text(item.preview,Obj(geometry,renderer.localToWorldMatrix,origin));
                    _manifest.items.Add(item);
                    LiveItems[item.path]=renderer;
                    LiveMaterials[item.path]=renderer.sharedMaterials;
                    MotionPaths.Add(item.path);
                    foreach(var influence in geometry.weights)
                        for(int influenceIndex=0;influenceIndex<Math.Min(influence.bones.Length,influence.weights.Length);influenceIndex++)
                            if(influence.weights[influenceIndex]>0&&influence.bones[influenceIndex]>=0&&influence.bones[influenceIndex]<geometry.bones.Length)
                                MotionPaths.Add(geometry.bones[influence.bones[influenceIndex]]);
                }
                catch(Exception error){Warning(PathOf(renderer.transform,root)+": "+error.Message);}
                finally{if(baked!=null)UnityEngine.Object.Destroy(baked);}
            }
        }
        static string Safe(string name)
        {
            var value=new string((name??"Mesh").Take(48).Select(c=>char.IsLetterOrDigit(c)||c=='_'||c=='-'?c:'_').ToArray());
            return value.Length==0?"Mesh":value;
        }
        static string FindAttachment(Transform node,HashSet<Transform> bones,Transform root)
        {
            for(var current=node;current!=null;current=current.parent){if(bones.Contains(current))return PathOf(current,root);if(current==root)break;}
            return PathOf(node.parent??root,root);
        }
        static Geometry ReadGeometry(Mesh mesh)
        {
            if(mesh.isReadable)
            {
                var channels=new List<Vector4>();mesh.GetUVs(1,channels);
                var uvChannels=new List<UvChannel>();
                for(int channel=0;channel<8;channel++){var values=new List<Vector4>();mesh.GetUVs(channel,values);if(values.Count>0)uvChannels.Add(new UvChannel{channel=channel,values=values.ToArray()});}
                return new Geometry{vertices=mesh.vertices,normals=mesh.normals,uv=mesh.uv,atlasSlices=channels.Select(v=>v.z).ToArray(),uvChannels=uvChannels.ToArray(),colors=mesh.colors,
                    submeshes=Enumerable.Range(0,mesh.subMeshCount).Select(i=>new Triangles{indices=mesh.GetTopology(i)==MeshTopology.Triangles?mesh.GetTriangles(i):Array.Empty<int>()}).ToArray()};
            }
            // Imported cosmetic meshes are often not CPU-readable. Read their existing GPU buffers.
            var streams=new Dictionary<int,byte[]>();
            try
            {
                foreach(var attribute in mesh.GetVertexAttributes())if(!streams.ContainsKey(attribute.stream))
                    using(var buffer=mesh.GetVertexBuffer(attribute.stream)){streams[attribute.stream]=BufferBytes(buffer);}
                var result=new Geometry{vertices=new Vector3[mesh.vertexCount]};
                if(mesh.HasVertexAttribute(VertexAttribute.Normal))result.normals=new Vector3[mesh.vertexCount];
                if(mesh.HasVertexAttribute(VertexAttribute.TexCoord0))result.uv=new Vector2[mesh.vertexCount];
                if(mesh.HasVertexAttribute(VertexAttribute.TexCoord1))result.atlasSlices=new float[mesh.vertexCount];
                var uvChannels=new List<UvChannel>();
                for(int channel=0;channel<8;channel++)if(mesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0+channel)))uvChannels.Add(new UvChannel{channel=channel,values=new Vector4[mesh.vertexCount]});
                if(mesh.HasVertexAttribute(VertexAttribute.Color))result.colors=new Color[mesh.vertexCount];
                for(int vertex=0;vertex<mesh.vertexCount;vertex++)
                {
                    result.vertices[vertex]=Attribute(mesh,streams,VertexAttribute.Position,vertex);
                    if(result.normals.Length>0)result.normals[vertex]=Attribute(mesh,streams,VertexAttribute.Normal,vertex);
                    if(result.uv.Length>0){var uv=Attribute(mesh,streams,VertexAttribute.TexCoord0,vertex);result.uv[vertex]=new Vector2(uv.x,uv.y);}
                    if(result.atlasSlices.Length>0&&mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord1)>2)result.atlasSlices[vertex]=Component(mesh,streams,VertexAttribute.TexCoord1,vertex,2);
                    foreach(var channel in uvChannels)
                    {
                        var attribute=(VertexAttribute)((int)VertexAttribute.TexCoord0+channel.channel);var value=Vector4.zero;
                        for(int axis=0;axis<mesh.GetVertexAttributeDimension(attribute);axis++)value[axis]=Component(mesh,streams,attribute,vertex,axis);
                        channel.values[vertex]=value;
                    }
                    if(result.colors.Length>0)
                    {
                        var value=Color.white;for(int axis=0;axis<mesh.GetVertexAttributeDimension(VertexAttribute.Color);axis++)value[axis]=Component(mesh,streams,VertexAttribute.Color,vertex,axis);result.colors[vertex]=value;
                    }
                }
                result.uvChannels=uvChannels.ToArray();
                byte[] indices;using(var buffer=mesh.GetIndexBuffer())indices=BufferBytes(buffer);
                int size=mesh.indexFormat==IndexFormat.UInt32?4:2;
                result.submeshes=new Triangles[mesh.subMeshCount];
                for(int part=0;part<mesh.subMeshCount;part++)
                {
                    var sub=mesh.GetSubMesh(part);var values=new int[sub.topology==MeshTopology.Triangles?sub.indexCount:0];
                    for(int i=0;i<values.Length;i++){int offset=(sub.indexStart+i)*size;values[i]=(size==4?(int)BitConverter.ToUInt32(indices,offset):BitConverter.ToUInt16(indices,offset))+sub.baseVertex;}
                    result.submeshes[part]=new Triangles{indices=values};
                }
                return result;
            }
            finally{streams.Clear();}
        }
        static byte[] BufferBytes(GraphicsBuffer buffer)
        {
            var data=new byte[checked(buffer.count*buffer.stride)];buffer.GetData(data);return data;
        }
        static Influence[] GpuWeights(Mesh mesh)
        {
            if(!mesh.HasVertexAttribute(VertexAttribute.BlendIndices)||!mesh.HasVertexAttribute(VertexAttribute.BlendWeight))
                throw new NotSupportedException("GPU skin weights are not exposed by this mesh");
            var streams=new Dictionary<int,byte[]>();
            foreach(var attribute in new[]{VertexAttribute.BlendIndices,VertexAttribute.BlendWeight})
            {
                int stream=mesh.GetVertexAttributeStream(attribute);
                if(!streams.ContainsKey(stream))using(var buffer=mesh.GetVertexBuffer(stream))streams[stream]=BufferBytes(buffer);
            }
            int count=Math.Min(mesh.GetVertexAttributeDimension(VertexAttribute.BlendIndices),mesh.GetVertexAttributeDimension(VertexAttribute.BlendWeight));
            var result=new Influence[mesh.vertexCount];
            for(int vertex=0;vertex<result.Length;vertex++)
            {
                var influence=new Influence{bones=new int[count],weights=new float[count]};
                for(int i=0;i<count;i++)
                {
                    influence.bones[i]=(int)Component(mesh,streams,VertexAttribute.BlendIndices,vertex,i);
                    influence.weights[i]=Component(mesh,streams,VertexAttribute.BlendWeight,vertex,i);
                }
                result[vertex]=influence;
            }
            return result;
        }
        static Vector3 Attribute(Mesh mesh,Dictionary<int,byte[]> streams,VertexAttribute attribute,int vertex)
        {
            var result=Vector3.zero;
            for(int axis=0;axis<Math.Min(3,mesh.GetVertexAttributeDimension(attribute));axis++)result[axis]=Component(mesh,streams,attribute,vertex,axis);
            return result;
        }
        static float Component(Mesh mesh,Dictionary<int,byte[]> streams,VertexAttribute attribute,int vertex,int axis)
        {
            int stream=mesh.GetVertexAttributeStream(attribute),offset=vertex*mesh.GetVertexBufferStride(stream)+mesh.GetVertexAttributeOffset(attribute);
            var format=mesh.GetVertexAttributeFormat(attribute);var bytes=streams[stream];
            int size=format==VertexAttributeFormat.Float32||format==VertexAttributeFormat.UInt32||format==VertexAttributeFormat.SInt32?4:
                format==VertexAttributeFormat.Float16||format==VertexAttributeFormat.UNorm16||format==VertexAttributeFormat.SNorm16||format==VertexAttributeFormat.UInt16||format==VertexAttributeFormat.SInt16?2:1;
            int at=offset+axis*size;
            switch(format)
            {
                case VertexAttributeFormat.Float32:return BitConverter.ToSingle(bytes,at);
                case VertexAttributeFormat.Float16:return Mathf.HalfToFloat(BitConverter.ToUInt16(bytes,at));
                case VertexAttributeFormat.UNorm8:return bytes[at]/255f;
                case VertexAttributeFormat.SNorm8:return Math.Max(-1f,(sbyte)bytes[at]/127f);
                case VertexAttributeFormat.UNorm16:return BitConverter.ToUInt16(bytes,at)/65535f;
                case VertexAttributeFormat.SNorm16:return Math.Max(-1f,BitConverter.ToInt16(bytes,at)/32767f);
                case VertexAttributeFormat.UInt8:return bytes[at];
                case VertexAttributeFormat.SInt8:return (sbyte)bytes[at];
                case VertexAttributeFormat.UInt16:return BitConverter.ToUInt16(bytes,at);
                case VertexAttributeFormat.SInt16:return BitConverter.ToInt16(bytes,at);
                case VertexAttributeFormat.UInt32:return BitConverter.ToUInt32(bytes,at);
                case VertexAttributeFormat.SInt32:return BitConverter.ToInt32(bytes,at);
                default:throw new NotSupportedException("Vertex attribute format "+format);
            }
        }
        Surface[] Materials(Renderer renderer,Geometry geometry)
        {
            var materials=renderer.sharedMaterials;var result=new Surface[materials.Length];
            for(int i=0;i<materials.Length;i++)
            {
                var material=materials[i];var surface=new Surface{name=material!=null?material.name:"Material"};result[i]=surface;
                if(material==null)continue;
                surface.shader=material.shader!=null?material.shader.name:"";
                surface.keywords=material.shaderKeywords;surface.renderQueue=material.renderQueue;
                surface.hasVertexColor=geometry.colors.Length==geometry.vertices.Length;
                var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,i);if(block.isEmpty)renderer.GetPropertyBlock(block);
                string color=material.HasProperty("_BaseColor")?"_BaseColor":material.HasProperty("_Color")?"_Color":"";
                if(color.Length>0)surface.color=block.HasProperty(Shader.PropertyToID(color))?block.GetColor(color):material.GetColor(color);
                try
                {
                    Texture GetTexture(string property)
                    {
                        int id=Shader.PropertyToID(property);
                        var image=block.HasProperty(id)?block.GetTexture(id):null;
                        if(image==null&&material.HasProperty(id))image=material.GetTexture(id);
                        return image??Shader.GetGlobalTexture(id);
                    }
                    float GetFloat(string property){int id=Shader.PropertyToID(property);return block.HasProperty(id)?block.GetFloat(id):material.HasProperty(id)?material.GetFloat(id):Shader.GetGlobalFloat(id);}
                    var atlas=GetTexture("_BaseMap_Atlas") as Texture2DArray;
                    bool arrayMode=atlas!=null&&(material.IsKeywordEnabled("_USE_TEX_ARRAY_ATLAS")||Shader.IsKeywordEnabled("_USE_TEX_ARRAY_ATLAS")||GetTexture("_BaseMap")==null);
                    if(arrayMode)
                    {
                        surface.slice=Mathf.RoundToInt(GetFloat("_BaseMap_AtlasSlice"));
                        surface.sliceFromUv1=material.IsKeywordEnabled("_GT_BASE_MAP_ATLAS_SLICE_SOURCE__UV1_Z")||Shader.IsKeywordEnabled("_GT_BASE_MAP_ATLAS_SLICE_SOURCE__UV1_Z")||GetFloat("_BaseMap_AtlasSliceSource")>.5f;
                        var indices=surface.sliceFromUv1&&geometry.atlasSlices.Length==geometry.vertices.Length&&i<geometry.submeshes.Length?
                            geometry.submeshes[i].indices.Select(v=>Mathf.RoundToInt(geometry.atlasSlices[v])).Distinct().ToArray():new[]{surface.slice};
                        var images=new List<AtlasImage>();
                        foreach(int slice in indices)
                        {
                            if(slice<0||slice>=atlas.depth){Warning(material.name+": atlas slice out of range: "+slice);continue;}
                            images.Add(new AtlasImage{index=slice,texture=SaveTexture(atlas,slice)});
                        }
                        surface.slices=images.ToArray();if(images.Count>0)surface.texture=images[0].texture;
                    }
                    else
                    {
                        foreach(string property in new[]{"_BaseMap","_MainTex","_BaseMapArray","_FaceTex","_Texture"})
                        {
                            var image=GetTexture(property);
                            if(image is Texture2DArray array)
                            {
                                int slice=Mathf.Clamp(Mathf.RoundToInt(GetFloat("_BaseMapArrayIndex")),0,array.depth-1);
                                surface.texture=SaveTexture(array,slice);break;
                            }
                            if(image!=null&&image.dimension==TextureDimension.Tex2D){surface.texture=SaveTexture(image,-1);break;}
                        }
                    }
                    string st=material.HasProperty("_BaseMap_ST")?"_BaseMap_ST":material.HasProperty("_MainTex_ST")?"_MainTex_ST":"";
                    if(st.Length>0){var value=block.HasProperty(Shader.PropertyToID(st))?block.GetVector(st):material.GetVector(st);surface.scale=new Vector2(value.x,value.y);surface.offset=new Vector2(value.z,value.w);}
                    if(renderer.name.ToLowerInvariant().Contains("gorillaface"))
                    {
                        var direct=GetTexture("_BaseMap");
                        if(direct is Texture2D&&direct.name!="white"&&direct.name!="Default-White")surface.directTexture=SaveTexture(direct,-1);
                        var mouth=GetTexture("_MouthMap");
                        if(mouth!=null&&mouth.dimension==TextureDimension.Tex2D)surface.mouthTexture=SaveTexture(mouth,-1);
                        if(material.HasProperty("_MouthMap_ST"))
                        {
                            var value=block.HasProperty(Shader.PropertyToID("_MouthMap_ST"))?block.GetVector("_MouthMap_ST"):material.GetVector("_MouthMap_ST");
                            surface.mouthScale=new Vector2(value.x,value.y);surface.mouthOffset=new Vector2(value.z,value.w);
                        }
                        string faceBase=surface.directTexture.Length>0?surface.directTexture:surface.texture;
                        if(faceBase.Length>0){_files["Faces/Face_Base.png"]=_files[faceBase];}
                        if(surface.mouthTexture.Length>0){_files["Faces/Face_Mouth.png"]=_files[surface.mouthTexture];}
                    }
                    var parameters=new List<Parameter>();
                    if(material.shader!=null)for(int propertyIndex=0;propertyIndex<material.shader.GetPropertyCount();propertyIndex++)
                    {
                        string name=material.shader.GetPropertyName(propertyIndex);int id=Shader.PropertyToID(name);
                        var type=material.shader.GetPropertyType(propertyIndex);
                        if(type==ShaderPropertyType.Texture)continue;
                        if(type==ShaderPropertyType.Color)
                        {
                            var value=block.HasProperty(id)?block.GetColor(id):material.GetColor(id);
                            parameters.Add(new Parameter{name=name,values=new[]{value.r,value.g,value.b,value.a}});
                        }
                        else if(type==ShaderPropertyType.Vector)
                        {
                            var value=block.HasProperty(id)?block.GetVector(id):material.GetVector(id);
                            parameters.Add(new Parameter{name=name,values=new[]{value.x,value.y,value.z,value.w}});
                        }
                        else parameters.Add(new Parameter{name=name,values=new[]{GetFloat(name)}});
                    }
                    surface.parameters=parameters.ToArray();
                    var channels=new List<TextureChannel>();
                    var properties=material.GetTexturePropertyNames().Concat(new[]{"_BaseMap_Atlas","_EmissionMap_Atlas","_DeformMap_Atlas","_WeatherMap_Atlas","_MouthMap_Atlas"}).Distinct();
                    foreach(string property in properties)
                    {
                        var image=GetTexture(property);if(image==null)continue;
                        var channel=new TextureChannel{property=property,dimension=image.dimension.ToString()};
                        try
                        {
                            if(material.HasProperty(property))
                            {
                                string transform=(property.EndsWith("_Atlas")?property.Substring(0,property.Length-6):property)+"_ST";
                                if(material.HasProperty(transform)){var stValue=material.GetVector(transform);channel.scale=new Vector2(stValue.x,stValue.y);channel.offset=new Vector2(stValue.z,stValue.w);}
                                if(block.HasProperty(Shader.PropertyToID(transform))){var stValue=block.GetVector(transform);channel.scale=new Vector2(stValue.x,stValue.y);channel.offset=new Vector2(stValue.z,stValue.w);}
                            }
                            if(image is Texture2DArray array)
                            {
                                channel.format=array.format.ToString();
                                string sliceProperty=property+"Slice";
                                if(property.EndsWith("_Atlas"))sliceProperty=property+"Slice";
                                else if(property=="_BaseMapArray")sliceProperty="_BaseMapArrayIndex";
                                channel.slice=Mathf.RoundToInt(GetFloat(sliceProperty));
                                channel.sliceFromUv1=property=="_BaseMap_Atlas"&&surface.sliceFromUv1;
                                var indices=channel.sliceFromUv1&&geometry.atlasSlices.Length==geometry.vertices.Length&&i<geometry.submeshes.Length?
                                    geometry.submeshes[i].indices.Select(v=>Mathf.RoundToInt(geometry.atlasSlices[v])).Distinct().ToArray():new[]{channel.slice};
                                var images=new List<AtlasImage>();
                                foreach(int slice in indices)if(slice>=0&&slice<array.depth)images.Add(new AtlasImage{index=slice,texture=SaveTexture(array,slice)});
                                channel.images=images.ToArray();
                            }
                            else if(image.dimension==TextureDimension.Cube||image.dimension==TextureDimension.CubeArray)
                            {
                                channel.images=Enumerable.Range(0,6).Select(face=>new AtlasImage{index=face,texture=SaveTexture(image,face)}).ToArray();
                            }
                            else if(image is Texture3D volume)
                            {
                                channel.format=volume.format.ToString();channel.images=SaveVolume(volume);
                            }
                            else if(image.dimension==TextureDimension.Tex2D)
                            {
                                channel.format=image is Texture2D texture?texture.format.ToString():"RenderTexture";
                                channel.images=new[]{new AtlasImage{index=0,texture=SaveTexture(image,-1)}};
                            }
                            else Warning(renderer.name+"/"+property+": unsupported image dimension "+image.dimension);
                        }
                        catch(Exception error){Warning(renderer.name+"/"+property+": "+error.Message);}
                        channels.Add(channel);
                    }
                    surface.channels=channels.ToArray();
                    if(surface.texture.Length==0)Warning(renderer.name+"/"+material.name+": no albedo image found; texture properties="+string.Join(",",material.GetTexturePropertyNames()));
                }
                catch(Exception error){Warning(renderer.name+"/"+material.name+": texture unavailable: "+error.Message);}
            }
            return result;
        }
        string SaveTexture(Texture image,int slice)
        {
            string key=image.GetInstanceID()+":"+slice;
            if(_textures.TryGetValue(key,out string relative))return relative;
            relative="Textures/"+_textures.Count.ToString("D3")+"_"+Safe(image.name)+(slice>=0?"_slice"+slice:"")+".png";
            Texture2D copy=null;
            RenderTexture renderCopy=null;
            try
            {
                if(image is Texture2DArray array)
                {
                    copy=new Texture2D(array.width,array.height,array.format,false);
                    Graphics.CopyTexture(array,slice,0,copy,0,0);
                }
                else if(image is Cubemap cube)
                {copy=new Texture2D(cube.width,cube.height,cube.format,false);Graphics.CopyTexture(cube,slice,0,copy,0,0);}
                else if(image is CubemapArray cubes)
                {copy=new Texture2D(cubes.width,cubes.height,cubes.format,false);Graphics.CopyTexture(cubes,slice,0,copy,0,0);}
                else if(image is RenderTexture render&&render.dimension==TextureDimension.Cube)
                {
                    var descriptor=render.descriptor;descriptor.dimension=TextureDimension.Tex2D;descriptor.volumeDepth=1;descriptor.depthBufferBits=0;descriptor.msaaSamples=1;descriptor.mipCount=1;
                    renderCopy=RenderTexture.GetTemporary(descriptor);Graphics.CopyTexture(render,slice,0,renderCopy,0,0);
                }
                _files[relative]=TexturePng(copy!=null?copy:renderCopy!=null?(Texture)renderCopy:image);_textures[key]=relative;return relative;
            }
            finally{if(copy!=null)UnityEngine.Object.Destroy(copy);if(renderCopy!=null)RenderTexture.ReleaseTemporary(renderCopy);}
        }
        AtlasImage[] SaveVolume(Texture3D volume)
        {
            var request=AsyncGPUReadback.Request(volume,0,TextureFormat.RGBA32);request.WaitForCompletion();
            if(request.hasError)throw new IOException("GPU volume readback failed");
            var bytes=request.GetData<byte>().ToArray();int layerSize=checked(volume.width*volume.height*4);
            if(bytes.Length<layerSize*volume.depth)throw new IOException("Incomplete GPU volume readback");
            var result=new List<AtlasImage>();
            for(int slice=0;slice<volume.depth;slice++)
            {
                string key=volume.GetInstanceID()+":volume:"+slice;
                if(!_textures.TryGetValue(key,out string path))
                {
                    path="Textures/"+_textures.Count.ToString("D3")+"_"+Safe(volume.name)+"_z"+slice+".png";
                    var texture=new Texture2D(volume.width,volume.height,TextureFormat.RGBA32,false);
                    try{var layer=new byte[layerSize];Buffer.BlockCopy(bytes,slice*layerSize,layer,0,layerSize);texture.LoadRawTextureData(layer);texture.Apply();_files[path]=texture.EncodeToPNG();_textures[key]=path;}
                    finally{UnityEngine.Object.Destroy(texture);}
                }
                result.Add(new AtlasImage{index=slice,texture=path});
            }
            return result.ToArray();
        }
        static byte[] TexturePng(Texture image)
        {
            var previous=RenderTexture.active;
            var target=RenderTexture.GetTemporary(image.width,image.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            Texture2D readable=null;
            try
            {
                Graphics.Blit(image,target);RenderTexture.active=target;
                readable=new Texture2D(image.width,image.height,TextureFormat.RGBA32,false);
                readable.ReadPixels(new Rect(0,0,image.width,image.height),0,0);readable.Apply();return readable.EncodeToPNG();
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);if(readable!=null)UnityEngine.Object.Destroy(readable);}
        }
        static string MakeMtl(Surface[] surfaces)
        {
            var text=new StringBuilder();
            for(int i=0;i<surfaces.Length;i++)
            {
                var s=surfaces[i];text.Append("newmtl Material_").Append(i).Append('\n').Append("Kd ").Append(Number(s.color.r)).Append(' ').Append(Number(s.color.g)).Append(' ').Append(Number(s.color.b)).Append("\nd ").Append(Number(s.color.a)).Append("\nKs 0 0 0\nNs 0\n");
                if(s.texture.Length>0)text.Append("map_Kd -s ").Append(Number(s.scale.x)).Append(' ').Append(Number(s.scale.y)).Append(" 1 -o ").Append(Number(s.offset.x)).Append(' ').Append(Number(s.offset.y)).Append(" 0 ../../").Append(s.texture).Append('\n');
            }
            return text.ToString();
        }
        static string Obj(Geometry geometry,Matrix4x4 world,Vector3 origin)
        {
            var text=new StringBuilder("# Mocap posed snapshot, Blender coordinates, metres\nmtllib Materials.mtl\n");
            float determinant=world.m00*(world.m11*world.m22-world.m12*world.m21)-world.m01*(world.m10*world.m22-world.m12*world.m20)+world.m02*(world.m10*world.m21-world.m11*world.m20);
            bool reverse=determinant>=0f;
            foreach(var vertex in geometry.vertices)
            {
                var v=world.MultiplyPoint3x4(vertex)-origin;
                text.Append("v ").Append(Number(v.x)).Append(' ').Append(Number(v.z)).Append(' ').Append(Number(v.y)).Append('\n');
            }
            bool uv=geometry.uv.Length==geometry.vertices.Length;
            if(uv)foreach(var v in geometry.uv)text.Append("vt ").Append(Number(v.x)).Append(' ').Append(Number(v.y)).Append('\n');
            for(int part=0;part<geometry.submeshes.Length;part++)
            {
                text.Append("usemtl Material_").Append(part).Append('\n');var triangles=geometry.submeshes[part].indices;
                for(int i=0;i+2<triangles.Length;i+=3)
                {
                    text.Append('f');
                    // Swapping Y/Z reverses handedness; a mirrored model already reverses it too.
                    for(int corner=0;corner<3;corner++){int vertex=triangles[i+(reverse&&corner>0?3-corner:corner)]+1;text.Append(' ').Append(vertex);if(uv)text.Append('/').Append(vertex);}
                    text.Append('\n');
                }
            }
            return text.ToString();
        }
        internal void Write(string folder)
        {
            Directory.CreateDirectory(Path.Combine(folder,"Cosmetics"));Directory.CreateDirectory(Path.Combine(folder,"Body"));
            foreach(var file in _files){string path=Path.Combine(folder,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,file.Value);}
        }
    }
}
#endregion

#region MocapPaths
namespace UltimateMenu
{
    using System;
    using System.IO;
    using System.Text;

    internal static class MocapPaths
    {
        internal static string TakeCode(DateTime time)=>time.ToString("yyyy-MM-dd_HH-mm-ss-fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,8);
        internal static string SafeName(string name)
        {
            var text=new StringBuilder();
            foreach(char c in name??"")
            {
                if(text.Length>=48)break;
                text.Append(char.IsControl(c)||Array.IndexOf(Path.GetInvalidFileNameChars(),c)>=0?'_':c);
            }
            string safe=text.ToString().Trim().Trim('.');
            if(safe.Length==0)safe="Player";
            // Prefix also avoids Windows reserved device names.
            return "Player_"+safe;
        }
    }
}
#endregion
