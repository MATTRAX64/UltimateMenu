using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UltimateMenu
{
    public class GorillaVoices : MonoBehaviour
    {
        public static GorillaVoices Instance { get; private set; }
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Voice.Title");
        bool _wsE = true, _pgR;
        float _inR = 5f, _outR = 15f, _outV = 0.1f;
        const float MinR = 0.5f, MaxR = 50f;

        const float UpdateInterval = 0.1f;
        readonly Dictionary<VRRig, AudioSource> _cache = new Dictionary<VRRig, AudioSource>();
        readonly Dictionary<VRRig, Transform> _hCache = new Dictionary<VRRig, Transform>();
        readonly Dictionary<VRRig, float> _nextT = new Dictionary<VRRig, float>();
        readonly HashSet<VRRig> _setup = new HashSet<VRRig>();
        readonly List<VRRig> _rigs = new List<VRRig>();
        static readonly FieldInfo VAF = typeof(VRRig).GetField("voiceAudio", BindingFlags.NonPublic | BindingFlags.Instance) ?? typeof(VRRig).GetField("voiceAudio", BindingFlags.Public | BindingFlags.Instance);

        const int SampleWindow = 256;
        readonly float[] _buf = new float[SampleWindow];

        const float PreviewHold = 3f;
        float _pvT, _lHeadT;
        GameObject _pvIn, _pvOut;
        Transform _lHead;

        void Awake() { Instance = this; _inR = PlayerPrefs.GetFloat("MUM_VC_Inner", 5f); _outR = PlayerPrefs.GetFloat("MUM_VC_Outer", 15f); _outV = PlayerPrefs.GetFloat("MUM_VC_OuterVol", 0.1f); }

        void SaveSettings() { PlayerPrefs.SetFloat("MUM_VC_Inner", _inR); PlayerPrefs.SetFloat("MUM_VC_Outer", _outR); PlayerPrefs.SetFloat("MUM_VC_OuterVol", _outV); PlayerPrefs.Save(); }

        void Update()
        {
            if (!_pgR && MenuUI.Instance != null) { MenuUI.Instance.RegisterPage("Voice.Title", DrawPage, MenuCategory.Gameplay); _pgR = true; }

            _lHeadT -= Time.deltaTime;
            if (_lHead == null || _lHeadT <= 0f) { _lHeadT = 1f; RefreshLocalHead(); }

            bool nowEn = Enabled;
            if (nowEn != _wsE) { _wsE = nowEn; if (!nowEn) { foreach (var kv in _cache) RestoreRig(kv.Key); HidePreview(); } }

            UpdatePreview();
        }

        void RefreshLocalHead() { var lr = VRRig.LocalRig; if (lr == null) return; var h = lr.transform.Find("rig/head"); if (h != null) _lHead = h; }

        Transform GetOtherHead(VRRig rig)
        {
            if (_hCache.TryGetValue(rig, out var c) && c != null) return c;
            var h = rig.transform.Find("rig/head"); if (h != null) _hCache[rig] = h;
            return h;
        }

        void NotifySettingsChanged() { _pvT = PreviewHold; EnsurePreviewObjects(); }

        void EnsurePreviewObjects() { if (_pvIn == null) _pvIn = BuildPreviewSphere("MUM_VoicePreview_Inner"); if (_pvOut == null) _pvOut = BuildPreviewSphere("MUM_VoicePreview_Outer"); }

        GameObject BuildPreviewSphere(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name = name;
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);

            var rend = go.GetComponent<Renderer>();
            Shader sh = Shader.Find("GorillaTag/UberTransparent") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            if (sh != null) rend.material = new Material(sh);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
            return go;
        }

        void UpdatePreview()
        {
            if (_pvT <= 0f) { if (_pvIn != null && _pvIn.activeSelf) HidePreview(); return; }
            _pvT -= Time.deltaTime;

            if (_lHead == null || !Enabled) { HidePreview(); return; }

            EnsurePreviewObjects();
            float fade = Mathf.Clamp01(_pvT / 0.5f);
            PositionPreviewSphere(_pvIn, _inR, new Color(0.15f, 0.95f, 0.55f, 0.16f * fade), ref _lastInR);
            PositionPreviewSphere(_pvOut, _outR, new Color(0.95f, 0.45f, 0.10f, 0.10f * fade), ref _lastOutR);
        }

        float _lastInR = -1f, _lastOutR = -1f, _lastFade = -1f;

        void PositionPreviewSphere(GameObject go, float radius, Color color, ref float lastRadius)
        {
            if (go == null) return;
            if (!go.activeSelf) go.SetActive(true);
            go.transform.position = _lHead.position;
            if (!Mathf.Approximately(lastRadius, radius))
            {
                go.transform.localScale = Vector3.one * (radius * 2f);
                lastRadius = radius;
            }
            var rend = go.GetComponent<Renderer>();
            if (rend != null && rend.material != null)
            {
                rend.material.color = color;
                if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", color);
            }
        }

        void HidePreview() { _pvT = 0f; if (_pvIn != null) _pvIn.SetActive(false); if (_pvOut != null) _pvOut.SetActive(false); }

        float MeasureLevel(AudioSource src)
        {
            if (!src.isPlaying) return 0f;
            src.GetOutputData(_buf, 0);
            float sum = 0f; for (int i = 0; i < _buf.Length; i++) sum += _buf[i] * _buf[i];
            return Mathf.Sqrt(sum / _buf.Length);
        }

        float ComputeVolumeFromDistance(float distance)
        {
            if (distance <= _inR) return 1f;
            if (distance >= _outR) return _outV;
            return Mathf.Lerp(1f, _outV, (distance - _inR) / Mathf.Max(_outR - _inR, 0.01f));
        }

        public void ApplyToRig(VRRig rig)
        {
            if (rig == null || !Enabled || VAF == null || _lHead == null) return;
            if (!_cache.TryGetValue(rig, out var src) || src == null) { src = VAF.GetValue(rig) as AudioSource; if (src == null) return; _cache[rig] = src; _rigs.Add(rig); }

            if (_setup.Add(rig)) { src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 1000f; src.maxDistance = 1001f; }

            float now = Time.time;
            if (_nextT.TryGetValue(rig, out float t) && now < t) return;
            _nextT[rig] = now + UpdateInterval;

            var oh = GetOtherHead(rig);
            float dist = Vector3.Distance(_lHead.position, oh != null ? oh.position : rig.transform.position);
            src.volume = ComputeVolumeFromDistance(dist);
        }

        public void RestoreRig(VRRig rig)
        {
            if (rig == null || VAF == null) return;
            _setup.Remove(rig); _nextT.Remove(rig);
            var src = VAF.GetValue(rig) as AudioSource; if (src == null) return;
            src.rolloffMode = AudioRolloffMode.Logarithmic; src.minDistance = 1f; src.maxDistance = 15f; src.volume = 1f;
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Voice.Section"));

            if (!Enabled) { ui.BeginCard(); ui.Label(I18n.T("Voice.EnableFromModsHint"), Theme.Dim, 9); ui.EndCard(); }

            ui.Space(6); ui.BeginCard();
            float nIn = ui.Slider(I18n.T("Voice.RadiusNear"), _inR, MinR, _outR - 0.5f, "0.0m");
            if (!Mathf.Approximately(nIn, _inR) && Enabled) { _inR = nIn; SaveSettings(); NotifySettingsChanged(); }

            ui.Space(4);
            float nOut = ui.Slider(I18n.T("Voice.RadiusFar"), _outR, _inR + 0.5f, MaxR, "0.0m");
            if (!Mathf.Approximately(nOut, _outR) && Enabled) { _outR = nOut; SaveSettings(); NotifySettingsChanged(); }

            ui.Space(4);
            float nVol = ui.Slider(I18n.T("Voice.FarVolume"), _outV, 0f, 1f, "0.00");
            if (!Mathf.Approximately(nVol, _outV) && Enabled) { _outV = nVol; SaveSettings(); NotifySettingsChanged(); }
            ui.EndCard();

            ui.Space(4); ui.BeginCard(); ui.Label(I18n.T("Voice.PreviewHint"), Theme.Dim, 8, true); ui.EndCard();
        }
    }

    [HarmonyPatch(typeof(VRRig))]
    [HarmonyPatch("PostTick")]
    internal class VoiceChatPatch { static void Postfix(VRRig __instance) => GorillaVoices.Instance?.ApplyToRig(__instance); }
}