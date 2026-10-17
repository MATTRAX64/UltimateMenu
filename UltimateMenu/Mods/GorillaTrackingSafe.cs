using GorillaLocomotion;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UltimateMenu
{
    public class GorillaTrackingSafe : MonoBehaviour
    {
        public static GorillaTrackingSafe Instance { get; private set; }
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Tracking.Title");
        bool _wsE = true;

        public float smoothDelay = 0.1f;

        public enum TrackingMode { Fangame, Smooth, Trackers, Custom }
        public TrackingMode currentMode = TrackingMode.Fangame;

        public float customOffsetX, customOffsetY, customOffsetZ;
        public bool customPitch=true,customYaw=true,customRoll=true;
        public float trackerOffsetX, trackerOffsetY, trackerOffsetZ;

        bool _calib;
        Quaternion _calibChest = Quaternion.identity, _calibHip = Quaternion.identity;
        bool _calibHasHip;
        Quaternion _calibHeadYaw = Quaternion.identity, _calibTurnParent = Quaternion.identity;
        string _cdText = "";
        bool _autoCalibDone;

        readonly GorillaTracking _backend=new GorillaTracking();
        bool _ready;
        float _devRefreshT;
        bool _backendFailed;

        TrackerData[] _tData = new TrackerData[64];
        uint[] _activeIds = new uint[64];
        int _activeCount, _lastActiveCount;
        static readonly Quaternion FlipCorr = Quaternion.Euler(0f, 180f, 0f);

        GameObject _trkParent, _trkGo, _chestF, _hipF;
        Harmony _harmony;

        float SmoothFactor() => 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(smoothDelay, 0.001f));

        void Awake()
        {
            Instance = this;
            LoadConfig();
            for (int i = 0; i < 64; i++) _tData[i] = new TrackerData { DeviceId = (uint)i };
            _harmony = new Harmony("com.mattrax.playertracking");
            _harmony.PatchAll(typeof(Patches));
        }

        void Start()
        {
            GorillaTagger.OnPlayerSpawned(OnPlayerSpawned);
            RefreshTrackers();
            MenuUI.Instance?.RegisterPage("Tracking.Title", DrawPage, MenuCategory.Tracking);
        }

        void OnDestroy() { StopAllCoroutines();_harmony?.UnpatchSelf();if(_trkParent!=null)Destroy(_trkParent);if(Instance==this)Instance=null; }
        void OnDisable() { StopAllCoroutines();_calib=false;_autoCalibDone=false; }

        void OnPlayerSpawned()
        {
            var turnParent = GTPlayer.Instance?.turnParent?.transform;
            if (turnParent == null) return;

            if(_trkParent!=null)Destroy(_trkParent);
            _trkParent = new GameObject("PT_TrackerParent");
            _trkParent.transform.SetParent(turnParent, false);
            _trkParent.transform.localPosition = Vector3.zero; _trkParent.transform.localRotation = Quaternion.identity;

            _trkGo = new GameObject("PT_ChestTracker");
            _trkGo.transform.SetParent(_trkParent.transform, false);
            _trkGo.transform.localPosition = Vector3.zero;

            _chestF = new GameObject("PT_ChestFollower");
            _chestF.transform.SetParent(_trkGo.transform, false);
            _chestF.transform.localPosition = Vector3.zero; _chestF.transform.localRotation = Quaternion.identity;

            _hipF = new GameObject("PT_HipFollower");
            _hipF.transform.SetParent(_trkParent.transform, false);
        }

        void Update()
        {
            bool nowEn = Enabled;
            if (nowEn != _wsE) { _wsE = nowEn; if (nowEn && currentMode == TrackingMode.Trackers) _autoCalibDone = false; }

            _devRefreshT += Time.deltaTime;
            if (_devRefreshT >= 3f) { _devRefreshT = 0f; if(currentMode!=TrackingMode.Trackers)RefreshTrackers(); }
            if(currentMode==TrackingMode.Trackers&&!_ready)UseSafeMode();

            if (nowEn && currentMode == TrackingMode.Trackers && !_autoCalibDone && _activeCount > 0) { _autoCalibDone = true; StartCalibration(); }

            if (!nowEn || _chestF == null) return;

            switch (currentMode)
            {
                case TrackingMode.Fangame: UpdateFangame(); break;
                case TrackingMode.Smooth: UpdateSmooth(); break;
                case TrackingMode.Trackers: UpdateTrackers(); break;
                case TrackingMode.Custom: UpdateCustom(); break;
            }
        }

        void UpdateFangame()
        {
            var rig = VRRig.LocalRig; if (rig == null) return;
            _chestF.transform.rotation = Quaternion.Slerp(_chestF.transform.rotation, GetHeadRotation(rig), SmoothFactor());
            _trkParent.transform.position = rig.transform.position;
        }

        void UpdateSmooth()
        {
            var rig = VRRig.LocalRig; if (rig == null) return;
            Quaternion target = Quaternion.Euler(0f, GetHeadRotation(rig).eulerAngles.y, 0f);
            _chestF.transform.rotation = Quaternion.Slerp(_chestF.transform.rotation, target, SmoothFactor());
            _trkParent.transform.position = rig.transform.position;
        }

        void UpdateTrackers()
        {
            if(!RefreshTrackers()){UseSafeMode();return;}

            var chest = GetTrackerByRole(TrackerRole.Chest);
            if (chest == null || !chest.IsConnected || !chest.IsValid || !_calib) return;

            Quaternion chestDelta = (chest.SmoothedRotation * FlipCorr) * Quaternion.Inverse(_calibChest);

            var turnT = _trkParent.transform.parent;
            Quaternion turnParentDelta = (turnT != null ? turnT.rotation : Quaternion.identity) * Quaternion.Inverse(_calibTurnParent);

            Quaternion target = Quaternion.Euler(trackerOffsetX, trackerOffsetY, trackerOffsetZ) * turnParentDelta * chestDelta * _calibHeadYaw;
            if (Quaternion.Dot(_chestF.transform.rotation, target) < 0) target = new Quaternion(-target.x, -target.y, -target.z, -target.w);

            _chestF.transform.rotation = Quaternion.Slerp(_chestF.transform.rotation, target, SmoothFactor());

            var rig = VRRig.LocalRig;
            if (rig != null) _trkParent.transform.position = rig.transform.position;
        }

        void UpdateCustom()
        {
            var rig = VRRig.LocalRig; if (rig == null) return;
            Vector3 angles=GetHeadRotation(rig).eulerAngles;
            Quaternion target = Quaternion.Euler(customPitch?angles.x:0f,customYaw?angles.y:0f,customRoll?angles.z:0f) * Quaternion.Euler(customOffsetX, customOffsetY, customOffsetZ);
            _chestF.transform.rotation = Quaternion.Slerp(_chestF.transform.rotation, target, SmoothFactor());
            _trkParent.transform.position = rig.transform.position;
        }

        void StartCalibration() => StartCoroutine(CalibrationCountdown());

        IEnumerator CalibrationCountdown()
        {
            _cdText = "3"; yield return new WaitForSeconds(1f);
            _cdText = "2"; yield return new WaitForSeconds(1f);
            _cdText = "1"; yield return new WaitForSeconds(1f);
            _cdText = "";

            var chest = GetTrackerByRole(TrackerRole.Chest);
            if (chest != null && chest.IsValid)
            {
                _calibChest = chest.SmoothedRotation * FlipCorr;

                var rig = VRRig.LocalRig;
                _calibHeadYaw = Quaternion.Euler(0f, rig != null ? GetHeadRotation(rig).eulerAngles.y : 0f, 0f);

                var turnT = _trkParent.transform.parent;
                _calibTurnParent = turnT != null ? turnT.rotation : Quaternion.identity;

                var hip = GetTrackerByRole(TrackerRole.Hip);
                if (hip != null && hip.IsValid) { _calibHip = hip.SmoothedRotation * FlipCorr; _calibHasHip = true; }
                else _calibHasHip = false;

                _calib = true;
                _cdText = I18n.T("Tracking.Calibrated");
                SaveConfig();
            }
            else _cdText = I18n.T("Tracking.CalibFailed");

            yield return new WaitForSeconds(2f);
            _cdText = "";
        }

        internal void ApplyTracking(VRRig rig)
        {
            if (!isActiveAndEnabled || !Enabled || rig == null || _chestF == null) return;
            rig.transform.rotation = _chestF.transform.rotation;
            rig.head.MapMine(rig.scaleFactor, rig.playerOffsetTransform);
            rig.rightHand.MapMine(rig.scaleFactor, rig.playerOffsetTransform);
            rig.leftHand.MapMine(rig.scaleFactor, rig.playerOffsetTransform);
            ApplySpine(rig);
        }

        void ApplySpine(VRRig rig)
        {
            Quaternion headRot = GetHeadRotation(rig);
            Quaternion hipRot = rig.transform.rotation;
            bool hasHip = false;

            if (currentMode == TrackingMode.Trackers && _calib && _calibHasHip)
            {
                var hip = GetTrackerByRole(TrackerRole.Hip);
                if (hip != null && hip.IsValid)
                {
                    Quaternion hipDelta = (hip.SmoothedRotation * FlipCorr) * Quaternion.Inverse(_calibHip);
                    var turnT = _trkParent.transform.parent;
                    Quaternion turnParentDelta = (turnT != null ? turnT.rotation : Quaternion.identity) * Quaternion.Inverse(_calibTurnParent);
                    hipRot = turnParentDelta * hipDelta * _calibHeadYaw;
                    hasHip = true;
                }
            }

            var spine = SpineIK.SolveSpine(_chestF.transform.rotation, hipRot, headRot, rig.transform.rotation, hasHip);
            var lowerSpine = rig.transform.Find("rig/body/spine");
            var upperSpine = rig.transform.Find("rig/body/spine/chest");
            if (lowerSpine != null) lowerSpine.rotation = spine.LowerSpineRotation;
            if (upperSpine != null) upperSpine.rotation = spine.UpperSpineRotation;

            if (Mathf.Abs(spine.HeadLeanAngle) > 0.5f)
            {
                var headBone = rig.transform.Find("rig/body/head");
                if (headBone != null) headBone.rotation = Quaternion.AngleAxis(spine.HeadLeanAngle, spine.HeadLeanAxis) * headBone.rotation;
            }
        }

        Quaternion GetHeadRotation(VRRig rig) => rig.head.rigTarget != null ? rig.head.rigTarget.rotation : rig.transform.rotation;

        bool RefreshTrackers()
        {
            if(_backendFailed)return false;
            try
            {
                _lastActiveCount=_activeCount;
                _activeCount=_backend.Read(_tData,_activeIds);
                _ready=_activeCount>0;
                float sf=1f/Mathf.Max(smoothDelay,.001f);
                for(int i=0;i<_activeCount;i++)_tData[_activeIds[i]].UpdateSmoothing(sf,sf);
                if(_activeCount!=_lastActiveCount)AutoAssignTrackers();
                return _ready;
            }
            catch(Exception error)
            {
                _backendFailed=true;_ready=false;_activeCount=0;_backend.Stop();
                Log.Err("Tracking unavailable; safe modes remain active",error);
                UseSafeMode();return false;
            }
        }
        void UseSafeMode()
        {
            _ready=false;_calib=false;_calibHasHip=false;_autoCalibDone=false;
            StopAllCoroutines();_cdText="";
            if(currentMode==TrackingMode.Trackers){currentMode=TrackingMode.Smooth;SaveConfig();}
        }

        TrackerData GetTrackerByRole(TrackerRole role)
        {
            for (int i = 0; i < _activeCount; i++) if (_tData[_activeIds[i]].Role == role) return _tData[_activeIds[i]];
            return null;
        }

        void AutoAssignTrackers()
        {
            for (int i = 0; i < 64; i++) _tData[i].Role = TrackerRole.None;
            var list = new List<TrackerData>();
            for (int i = 0; i < _activeCount; i++) { var t = _tData[_activeIds[i]]; if (t.IsValid) list.Add(t); }
            list.Sort((a, b) => b.RawPosition.y.CompareTo(a.RawPosition.y));
            if (list.Count >= 1) AssignRole(list[0].DeviceId, TrackerRole.Chest);
            if (list.Count >= 2) AssignRole(list[list.Count - 1].DeviceId, TrackerRole.Hip);
        }

        void AssignRole(uint deviceId, TrackerRole role)
        {
            for (int i = 0; i < 64; i++) if (_tData[i].Role == role) _tData[i].Role = TrackerRole.None;
            _tData[deviceId].Role = role;
        }

        void SaveConfig()
        {
            MenuPrefs.SetInt("MUM_TrackMode", (int)currentMode);
            MenuPrefs.SetInt("MUM_CustomPitch",customPitch?1:0);MenuPrefs.SetInt("MUM_CustomYaw",customYaw?1:0);MenuPrefs.SetInt("MUM_CustomRoll",customRoll?1:0);
            MenuPrefs.SetFloat("MUM_ChestSmooth", smoothDelay);
            MenuPrefs.SetFloat("MUM_CustomX", customOffsetX); MenuPrefs.SetFloat("MUM_CustomY", customOffsetY); MenuPrefs.SetFloat("MUM_CustomZ", customOffsetZ);
            MenuPrefs.SetFloat("MUM_TrkOffX", trackerOffsetX); MenuPrefs.SetFloat("MUM_TrkOffY", trackerOffsetY); MenuPrefs.SetFloat("MUM_TrkOffZ", trackerOffsetZ);
            MenuPrefs.Save();
        }

        void LoadConfig()
        {
            int storedMode=MenuPrefs.GetInt("MUM_TrackMode",0);
            currentMode=storedMode>=0&&storedMode<=3?(TrackingMode)storedMode:TrackingMode.Fangame;
            customPitch=MenuPrefs.GetInt("MUM_CustomPitch",1)==1;customYaw=MenuPrefs.GetInt("MUM_CustomYaw",1)==1;customRoll=MenuPrefs.GetInt("MUM_CustomRoll",1)==1;
            smoothDelay = MenuPrefs.GetFloat("MUM_ChestSmooth", 0.1f);
            customOffsetX = MenuPrefs.GetFloat("MUM_CustomX", 0f); customOffsetY = MenuPrefs.GetFloat("MUM_CustomY", 0f); customOffsetZ = MenuPrefs.GetFloat("MUM_CustomZ", 0f);
            trackerOffsetX = MenuPrefs.GetFloat("MUM_TrkOffX", 0f); trackerOffsetY = MenuPrefs.GetFloat("MUM_TrkOffY", 0f); trackerOffsetZ = MenuPrefs.GetFloat("MUM_TrkOffZ", 0f);
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Tracking.Section"));

            ui.BeginCard();
            ui.Label(I18n.T("Tracking.Mode"), Theme.Dim, 9, true);
            ui.Space(6);
            TrackingMode[] available=_ready?new[]{TrackingMode.Fangame,TrackingMode.Smooth,TrackingMode.Trackers,TrackingMode.Custom}:new[]{TrackingMode.Fangame,TrackingMode.Smooth,TrackingMode.Custom};
            string[] keys={"Tracking.ModeFangame","Tracking.ModeSmooth","Tracking.ModeTrackersShort","Tracking.ModeCustomShort"};
            string[] modeNames=Array.ConvertAll(available,mode=>I18n.T(keys[(int)mode]));
            int picked=ui.ModePicker(modeNames,Mathf.Max(0,Array.IndexOf(available,currentMode)));
            if(Enabled&&available[picked]!=currentMode){currentMode=available[picked];if(currentMode==TrackingMode.Trackers)_autoCalibDone=false;SaveConfig();}
            ui.Space(6);
            string[] modeDescs = { I18n.T("Tracking.ModeFangameDesc"), I18n.T("Tracking.ModeSmoothDesc"), I18n.T("Tracking.ModeTrackersDesc"), I18n.T("Tracking.ModeCustomDesc") };
            ui.Label(modeDescs[(int)currentMode], Theme.Dim, 8);
            ui.EndCard();

            ui.Space(6); ui.BeginCard();
            float ms = smoothDelay * 1000f;
            float newMs = ui.Slider(I18n.T("Tracking.Smooth"), ms, 10f, 500f, "0");
            if (Enabled && !Mathf.Approximately(newMs, ms)) { smoothDelay = newMs / 1000f; SaveConfig(); }
            ui.EndCard();

            if (currentMode == TrackingMode.Trackers)
            {
                ui.Space(6); ui.Section(I18n.T("Tracking.Detected"));
                ui.BeginCard();
                var chest = GetTrackerByRole(TrackerRole.Chest);
                bool chestOk = chest != null && chest.IsValid;
                ui.StatusPill(chestOk ? I18n.T("Tracking.Chest") + " OK" : I18n.T("Tracking.NotDetected"), chestOk);
                ui.Space(4);
                if (chestOk) ui.InfoRow(I18n.T("Tracking.Chest"), "[" + chest.DeviceId + "] " + chest.Serial, 80f);
                ui.InfoRow(I18n.T("Tracking.ActiveDevices"), _activeCount.ToString(), 80f);
                ui.Space(6);
                if (ui.Btn(I18n.T("Tracking.Reassign"), ui.TB3, Theme.Text) && Enabled) AutoAssignTrackers();
                ui.EndCard();

                ui.Space(6); ui.BeginCard();
                GUILayout.BeginHorizontal();
                ui.Label(I18n.T("Tracking.Calibration"), Theme.Dim, 9, true);
                GUILayout.FlexibleSpace();
                ui.StatusPill(_calib ? I18n.T("Tracking.Calibrated") : I18n.T("Tracking.NotCalibrated"), _calib);
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_cdText)) { ui.Space(8); ui.CenterLabel(_cdText, Theme.Accent, 22, true); ui.Space(4); }
                else ui.Space(6);

                GUILayout.BeginHorizontal();
                if (ui.Btn(I18n.T("Tracking.CalibrateBtn"), ui.TA, Color.white) && Enabled) StartCalibration();
                if (_calib) { GUILayout.Space(6); if (ui.Btn(I18n.T("Tracking.ResetCalib"), ui.TR, Color.white) && Enabled) _calib = false; }
                GUILayout.EndHorizontal();
                ui.Space(4);
                ui.Label(I18n.T("Tracking.CalibrateHint"), Theme.Dim, 8);
                ui.EndCard();

                ui.Space(6); ui.Section(I18n.T("Tracking.Offset"));
                ui.BeginCard();
                float nx = ui.Slider(I18n.T("Tracking.OffsetX"), trackerOffsetX, -180f, 180f, "0");
                float ny = ui.Slider(I18n.T("Tracking.OffsetY"), trackerOffsetY, -180f, 180f, "0");
                float nz = ui.Slider(I18n.T("Tracking.OffsetZ"), trackerOffsetZ, -180f, 180f, "0");
                if (Enabled && (nx != trackerOffsetX || ny != trackerOffsetY || nz != trackerOffsetZ)) { trackerOffsetX = nx; trackerOffsetY = ny; trackerOffsetZ = nz; SaveConfig(); }
                ui.Space(4);
                if (ui.Btn(I18n.T("Tracking.ResetOffset"), ui.TB3, Theme.Dim) && Enabled) { trackerOffsetX = trackerOffsetY = trackerOffsetZ = 0f; SaveConfig(); }
                ui.EndCard();
            }

            if (currentMode == TrackingMode.Custom)
            {
                ui.BeginCard();
                bool pitch=ui.Toggle(I18n.T("Tracking.FollowPitch"),customPitch);
                bool yaw=ui.Toggle(I18n.T("Tracking.FollowYaw"),customYaw);
                bool roll=ui.Toggle(I18n.T("Tracking.FollowRoll"),customRoll);
                if(Enabled&&(pitch!=customPitch||yaw!=customYaw||roll!=customRoll)){customPitch=pitch;customYaw=yaw;customRoll=roll;SaveConfig();}
                ui.EndCard();
                ui.Space(6); ui.Section(I18n.T("Tracking.CustomOffset"));
                ui.BeginCard();
                float nx = ui.Slider(I18n.T("Tracking.OffsetX"), customOffsetX, -180f, 180f, "0");
                float ny = ui.Slider(I18n.T("Tracking.OffsetY"), customOffsetY, -180f, 180f, "0");
                float nz = ui.Slider(I18n.T("Tracking.OffsetZ"), customOffsetZ, -180f, 180f, "0");
                if (Enabled && (nx != customOffsetX || ny != customOffsetY || nz != customOffsetZ)) { customOffsetX = nx; customOffsetY = ny; customOffsetZ = nz; SaveConfig(); }
                ui.Space(4);
                if (ui.Btn(I18n.T("Tracking.ResetOffset"), ui.TB3, Theme.Dim) && Enabled) { customOffsetX = customOffsetY = customOffsetZ = 0f; SaveConfig(); }
                ui.EndCard();
            }
        }

        static class Patches
        {
            [HarmonyPatch(typeof(VRRig), "PostTick")]
            [HarmonyPostfix]
            static void Postfix(VRRig __instance) { if (__instance.isOfflineVRRig && Instance != null) Instance.ApplyTracking(__instance); }
        }
    }

    public enum TrackerRole { None, Chest, Hip }

    public class TrackerData
    {
        public uint DeviceId; public TrackerRole Role; 
        public string Serial = ""; public bool IsConnected, IsValid;
        public Vector3 RawPosition, SmoothedPosition; public Quaternion RawRotation, SmoothedRotation;
        bool _init;

        public void UpdateSmoothing(float ps, float rs)
        {
            if (!_init) { SmoothedPosition = RawPosition; SmoothedRotation = RawRotation; _init = true; return; }
            SmoothedPosition = Vector3.Lerp(SmoothedPosition, RawPosition, Mathf.Clamp01(ps * Time.deltaTime));
            SmoothedRotation = Quaternion.Slerp(SmoothedRotation, RawRotation, Mathf.Clamp01(rs * Time.deltaTime));
        }
    }

    public struct SpineResult
    {
        public Quaternion ChestRotation, UpperSpineRotation, LowerSpineRotation;
        public float HeadLeanAngle; public Vector3 HeadLeanAxis;
    }

    public static class SpineIK
    {
        public static SpineResult SolveSpine(Quaternion chestRot, Quaternion hipRot, Quaternion headRot, Quaternion baseRot, bool hasHip)
        {
            var res = new SpineResult { ChestRotation = chestRot };
            var hip = hasHip ? hipRot : baseRot;
            res.LowerSpineRotation = Quaternion.Slerp(hip, chestRot, 0.35f);
            res.UpperSpineRotation = Quaternion.Slerp(hip, chestRot, 0.70f);
            Vector3 hFwd = headRot * Vector3.forward, cFwd = chestRot * Vector3.forward;
            Vector3 hFlat = new Vector3(hFwd.x, 0f, hFwd.z).normalized, cFlat = new Vector3(cFwd.x, 0f, cFwd.z).normalized;
            if (hFlat.sqrMagnitude < 0.001f || cFlat.sqrMagnitude < 0.001f) { res.HeadLeanAngle = 0f; res.HeadLeanAxis = Vector3.forward; return res; }
            float yaw = Vector3.SignedAngle(cFlat, hFlat, Vector3.up);
            float pitch = (Mathf.Asin(Mathf.Clamp(hFwd.y, -1f, 1f)) - Mathf.Asin(Mathf.Clamp(cFwd.y, -1f, 1f))) * Mathf.Rad2Deg;
            res.HeadLeanAngle = Mathf.Clamp(Mathf.Sqrt(yaw * yaw + pitch * pitch) * 0.7f, 0f, 35f);
            Vector3 axis = new Vector3(pitch, 0f, -yaw).normalized;
            res.HeadLeanAxis = axis.sqrMagnitude < 0.001f ? Vector3.forward : axis;
            return res;
        }
    }
}
