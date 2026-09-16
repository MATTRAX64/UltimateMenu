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

namespace UltimateMenu
{
    internal class GorillaMocaps : MonoBehaviour
    {
        public static GorillaMocaps Instance { get; private set; }
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Mocap.Title");
        private bool _wasEnabled = true;
        public bool recording;
        public float sampleRate = 24f;
        private string _fpsInput = "24";
        public bool cameraRecording;
        public bool cameraSelected = true;
        public float cameraSampleRate = 24f;
        private string _camFpsInput = "24";
        private float _cameraSampleTimer;
        private GameObject? _lckCamera;
        private string _camStatus = "Non trouvee";
        private static readonly Quaternion CameraRotationOffset = Quaternion.Euler(0f, 180f, 90f);
        public bool originSet;
        private Vector3 _originPos;
        private GameObject? _originMarker;
        private bool _autoOriginDone;
        private float _recordingStartTime = -1f;
        private uint _totalSamples;
        private bool _isSaving;
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
        }
        private readonly List<PlayerEntry> _players = new List<PlayerEntry>();
        private bool _pageRegistered;
        private int _regTries;

        void Awake() { Instance = this; CreateDataFolder(); EnsureBlenderAssetExtracted(); }
        void Start() { GorillaTagger.OnPlayerSpawned(OnPlayerSpawned); }
        void OnPlayerSpawned() { NewFileGroup(PhotonInRoom() ? PhotonRoomName() : "NoRoomCode"); RefreshPlayers(); AutoCalibrateOriginOnce(); }

        void Update()
        {
            if (!_pageRegistered && _regTries++ % 60 == 0 && _regTries < 600) TryRegister();
            if (_statusTimer > 0f) _statusTimer -= Time.deltaTime;
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
            var oldSelection = new Dictionary<string, bool>();
            foreach (var p in _players) oldSelection[p.dataName] = p.selected;
            _players.Clear();
            var localRig = VRRig.LocalRig;
            var localPlayer = PhotonLocalPlayer();
            if (localRig != null)
            {
                string realName = localPlayer != null ? localPlayer.NickName : "Local";
                _players.Add(new PlayerEntry { photonPlayer = localPlayer, rig = localRig, selected = oldSelection.TryGetValue(realName, out bool sel) ? sel : true, displayLabel = "(Moi) " + realName, dataName = realName });
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
            if (!Enabled) return;
            SetOrigin();
            if (cameraSelected) RefreshCamera();
            bool anySelected = false;
            foreach (var p in _players) if (p.selected) { anySelected = true; break; }
            bool camReady = cameraSelected && _lckCamera != null;
            if (!originSet) { SetStatus(I18n.T("Mocap.NoOrigin")); return; }
            if (!anySelected && !camReady) { SetStatus(I18n.T("Mocap.SelectPlayerOrCam")); return; }
            if (cameraSelected && !camReady) SetStatus(I18n.T("Mocap.CamNotFound"));
            NewFileGroup(PhotonInRoom() ? PhotonRoomName() : "NoRoomCode");
            recording = true;
            if (_recordingStartTime < 0f) _recordingStartTime = Time.time;
            if (camReady) StartCameraRecordingInternal();
        }

        public void StopRecording() { recording = false; SaveAsync(); StopCameraRecordingInternal(); }

        void RefreshCamera()
        {
            var found = GameObject.Find("LCKTablet(Clone)");
            if (found != null && found.activeInHierarchy) { _lckCamera = found; _camStatus = I18n.T("Mocap.CamFound"); }
            else { _lckCamera = null; _camStatus = I18n.T("Mocap.CamNotFound"); }
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
            var parts = new[] { p.rig.transform.Find("rig/head"), p.rig.transform.Find("rig/hand.L"), p.rig.transform.Find("rig/hand.R"), p.rig.transform.Find("rig/body_pivot/body/body_new") };
            if (idx == -1) { _dataList.Add(new GorillaMocapPlayerData(key, sampleRate, GetColor(p.rig), 4, lastSeen)); idx = _dataList.Count - 1; }
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
        Color GetColor(VRRig rig) { try { return rig.mainSkin.material.color; } catch { return Color.white; } }

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
            _mocapFolder = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Directory.GetCurrentDirectory(), "GorillaMocap");
            if (!Directory.Exists(_mocapFolder)) Directory.CreateDirectory(_mocapFolder);
        }

        void EnsureBlenderAssetExtracted()
        {
            string targetPath = Path.Combine(_mocapFolder, BLENDER_ASSET_FILENAME);
            if (File.Exists(targetPath)) return;
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BLENDER_ASSET_RESOURCE);
                if (stream == null) { Debug.LogError("[GorillaMocap] Ressource embarquée introuvable : " + BLENDER_ASSET_RESOURCE + ". Vérifie que GorillaMocap-BlenderAsset.zip est bien marqué 'Embedded Resource' dans le projet."); return; }
                using (var fileStream = File.Create(targetPath)) stream.CopyTo(fileStream);
                Debug.Log("[GorillaMocap] GorillaMocap-BlenderAsset.zip extrait vers " + targetPath);
            }
            catch (Exception e) { Debug.LogError("[GorillaMocap] EnsureBlenderAssetExtracted: " + e); }
        }

        public void NewFileGroup(string roomCode) { if (string.IsNullOrEmpty(_mocapFolder)) CreateDataFolder(); _roomFileCode = (string.IsNullOrEmpty(roomCode) ? "NoRoomCode" : roomCode) + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"); }

        public void SaveAsync()
        {
            if (_isSaving || _dataList.Count == 0) { SetStatus(I18n.T("Mocap.NothingToSave")); return; }
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
                    string path = Path.Combine(_mocapFolder, _roomFileCode + "_" + data.name + ".txt");
                    WritePlayerFile(path, data);
                    files.Add(path);
                }
            }
            catch (Exception e) { Debug.LogError("[GorillaMocap] Save: " + e); }
            _isSaving = false;
            _lastExportedFiles = files;
            _recordingStartTime = -1f;
            _totalSamples = 0;
            _dataList.Clear();
            SetStatus(files.Count + " " + I18n.T("Mocap.FilesExported"));
        }

        void SaveSingleFileAsync(GorillaMocapPlayerData data)
        {
            new Thread(() =>
            {
                try
                {
                    if (string.IsNullOrEmpty(_mocapFolder)) CreateDataFolder();
                    string path = Path.Combine(_mocapFolder, _roomFileCode + "_" + data.name + ".txt");
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
            File.WriteAllText(path, sb.ToString());
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

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Mocap.Title"));
            ui.BeginCard();
            ui.StatusPill(recording ? I18n.T("Mocap.Recording") : I18n.T("Mocap.Stopped"), recording);
            if (recording) ui.Label(FormatTime(Time.time - _recordingStartTime) + "  -  " + _totalSamples + " " + I18n.T("Mocap.Samples"), Theme.Accent, 10, true);
            ui.Space(4);
            if (ui.Btn(recording ? I18n.T("Mocap.StopSave") : I18n.T("Mocap.StartRecord"), recording ? ui.TR : ui.TA, Color.white) && Enabled) { if (recording) StopRecording(); else StartRecording(); }
            if (_isSaving) ui.Label(I18n.T("Mocap.Saving"), Theme.Accent, 9, true);
            if (!string.IsNullOrEmpty(_statusMsg) && _statusTimer > 0f) ui.Label(_statusMsg, Theme.Green, 9, true);
            ui.EndCard();
            ui.Space();
            ui.BeginCard();
            ui.Label(I18n.T("Mocap.PlayerFPS"), Theme.Dim, 9);
            GUILayout.BeginHorizontal();
            _fpsInput = MenuUI.ControlLayoutTextField(_fpsInput, GUILayout.Width(60));
            if (Enabled && float.TryParse(_fpsInput, out float parsed) && parsed > 0f) sampleRate = parsed;
            ui.Label(sampleRate.ToString("0.0") + " fps", Theme.Accent, 9);
            GUILayout.EndHorizontal();
            ui.EndCard();
            ui.Space(); ui.Section(I18n.T("Mocap.ToRecord"));
            ui.BeginCard();
            if (ui.Btn(I18n.T("Mocap.CheckPlayers"), ui.TB3, Theme.Text) && Enabled) RefreshPlayers();
            ui.Space(4);
            if (_players.Count == 0) ui.Label(I18n.T("Mocap.NoPlayers"), Theme.Dim, 9);
            foreach (var p in _players)
            {
                GUILayout.BeginHorizontal();
                bool newSel = MenuUI.ControlLayoutToggle(p.selected, "");
                GUILayout.Label(p.displayLabel, new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = p.selected ? Theme.Green : Theme.Dim } });
                GUILayout.EndHorizontal();
                if (newSel != p.selected && Enabled) p.selected = newSel;
            }
            ui.Space(4);
            GUILayout.BeginHorizontal();
            bool newCamSel = MenuUI.ControlLayoutToggle(cameraSelected, "");
            GUILayout.Label(I18n.T("Mocap.SpectatorCam") + " (" + _camStatus + ")", new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = cameraSelected ? Theme.Green : Theme.Dim } });
            GUILayout.EndHorizontal();
            if (newCamSel != cameraSelected && Enabled) cameraSelected = newCamSel;
            if (cameraSelected)
            {
                GUILayout.BeginHorizontal();
                if (ui.Btn(I18n.T("Mocap.RefreshCam"), ui.TB3, Theme.Text) && Enabled) RefreshCamera();
                if (_lckCamera != null && ui.Btn(I18n.T("Mocap.AlignToMe"), ui.TB3, Theme.Text) && Enabled) SnapCameraToPlayer();
                GUILayout.EndHorizontal();
                ui.Label(I18n.T("Mocap.CamFPS"), Theme.Dim, 9);
                GUILayout.BeginHorizontal();
                _camFpsInput = MenuUI.ControlLayoutTextField(_camFpsInput, GUILayout.Width(60));
                if (Enabled && float.TryParse(_camFpsInput, out float camParsed) && camParsed > 0f) cameraSampleRate = camParsed;
                ui.Label(cameraSampleRate.ToString("0.0") + " fps", Theme.Accent, 9);
                GUILayout.EndHorizontal();
                ui.Label(I18n.T("Mocap.CamHint"), Theme.Dim, 8, true);
                ui.Label(I18n.T("Mocap.AlignHint"), Theme.Dim, 8, true);
            }
            ui.EndCard();
            ui.Space(); ui.Section(I18n.T("Mocap.Origin"));
            ui.BeginCard();
            ui.StatusPill(originSet ? I18n.T("Mocap.OriginSet") : I18n.T("Mocap.OriginNotSet"), originSet);
            if (Enabled && originSet)
            {
                ui.Space(4);
                ui.InfoRow("X", _originPos.x.ToString("F3"), 30f);
                ui.InfoRow("Y", _originPos.y.ToString("F3"), 30f);
                ui.InfoRow("Z", _originPos.z.ToString("F3"), 30f);
            }
            ui.Space(4);
            ui.Label(I18n.T("Mocap.OriginAuto"), Theme.Dim, 8, true);
            ui.Label(I18n.T("Mocap.OriginGizmo"), Theme.Dim, 8, true);
            if (originSet && ui.Btn(I18n.T("Mocap.ClearOrigin"), ui.TR, Color.white) && Enabled) ClearOrigin();
            ui.EndCard();
            if (_lastExportedFiles.Count > 0)
            {
                ui.Space(); ui.Section(I18n.T("Mocap.LastExported"));
                ui.BeginCard();
                foreach (var f in _lastExportedFiles) ui.Label(Path.GetFileName(f), Theme.Accent, 9, true);
                ui.Label(I18n.T("Mocap.Folder") + " : GorillaMocap/", Theme.Dim, 8);
                ui.EndCard();
            }
            if (File.Exists(Path.Combine(_mocapFolder, BLENDER_ASSET_FILENAME)))
            {
                ui.Space(6);
                ui.BeginCard();
                ui.Label(I18n.T("Mocap.BlenderAssetHelp"), Theme.Dim, 9, true);
                ui.Space(2);
                ui.Label(BLENDER_ASSET_FILENAME, Theme.Accent, 9);
                ui.Space(2);
                ui.Label(I18n.T("Mocap.BlenderAssetHint"), Theme.Dim, 8);
                ui.EndCard();
            }
        }

        string FormatTime(float t) => (int)(t / 60f) + ":" + ((int)(t % 60f)).ToString("00");
        void OnDestroy() { if (_originMarker != null) Destroy(_originMarker); }
    }

    internal class GorillaMocapObject { public List<Vector3> locations = new List<Vector3>(); public List<Quaternion> rotations = new List<Quaternion>(); }
    internal class GorillaMocapFloat { public List<float> values = new List<float>(); }

    internal class GorillaMocapPlayerData
    {
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