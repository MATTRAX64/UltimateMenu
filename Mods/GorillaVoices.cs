using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UltimateMenu
{
    public class GorillaVoices : MonoBehaviour
    {
        public static GorillaVoices Instance { get; private set; }

        /// <summary>
        /// Le mod est actif uniquement via le switch global de la page Mods (liste des mods).
        /// Plus de toggle local : quand désactivé, la page reste visible mais tous les
        /// contrôles sont bloqués (comme GorillaRooms / GorillaMocap).
        /// </summary>
        public bool Enabled => MenuUI.Instance != null && MenuUI.Instance.IsModEnabled("Voice.Title");
        private bool _wasEnabled = true;

        bool _pageRegistered;

        float _innerRadius = 5f;
        float _outerRadius = 15f;
        float _outerVolume = 0.1f;

        const float MinRadius = 0.5f, MaxRadius = 50f;

        // --- Nouveau : distance calculée manuellement tête-à-tête ---
        // On ignore volontairement l'AudioListener (qui peut être positionné différemment
        // du head réel du rig local, ex: caméra VR décalée). On calcule nous-mêmes la
        // distance entre la tête du joueur LOCAL (rig/head, la même référence utilisée
        // pour la bulle de preview) et la tête de CHAQUE autre joueur (rig/head de son
        // propre VRRig). On pilote ensuite le volume nous-mêmes plutôt que de laisser
        // Unity calculer via minDistance/maxDistance/AudioListener.
        readonly Dictionary<VRRig, AudioSource> _cache = new Dictionary<VRRig, AudioSource>();
        readonly Dictionary<VRRig, Transform> _headCache = new Dictionary<VRRig, Transform>();
        readonly List<VRRig> _knownRigs = new List<VRRig>();
        static readonly FieldInfo VoiceAudioField = typeof(VRRig).GetField("voiceAudio", BindingFlags.NonPublic | BindingFlags.Instance)
                                                  ?? typeof(VRRig).GetField("voiceAudio", BindingFlags.Public | BindingFlags.Instance);

        const int SampleWindow = 256;
        readonly float[] _sampleBuf = new float[SampleWindow];

        // --- Aperçu visuel des rayons pendant l'ajustement ---
        // Deux sphères semi-transparentes centrées sur la tête du joueur local (rig/head),
        // qui le suivent en live tant qu'on modifie un réglage, puis disparaissent après un
        // court délai pour ne jamais gêner le jeu.
        const float PreviewHoldDuration = 3f;
        float _previewTimer;
        GameObject _previewInner;
        GameObject _previewOuter;

        // La tête locale : Player Objects/Local VRRig/Local Gorilla Player/rig/head.
        // On passe par VRRig.LocalRig plutôt que GorillaTagger.headCollider pour être
        // strictement cohérent avec la référence utilisée côté "autres joueurs"
        // (rig.transform.Find("rig/head")) : comparaison tête à tête, même chemin de
        // hiérarchie des deux côtés, donc aucun décalage introduit par un autre point
        // d'écoute (caméra/AudioListener) potentiellement mal aligné.
        Transform _localHead;
        float _localHeadRefreshTimer;

        void Awake()
        {
            Instance = this;
            _innerRadius = PlayerPrefs.GetFloat("MUM_VC_Inner", 5f);
            _outerRadius = PlayerPrefs.GetFloat("MUM_VC_Outer", 15f);
            _outerVolume = PlayerPrefs.GetFloat("MUM_VC_OuterVol", 0.1f);
        }

        void SaveSettings()
        {
            PlayerPrefs.SetFloat("MUM_VC_Inner", _innerRadius);
            PlayerPrefs.SetFloat("MUM_VC_Outer", _outerRadius);
            PlayerPrefs.SetFloat("MUM_VC_OuterVol", _outerVolume);
            PlayerPrefs.Save();
        }

        void Update()
        {
            if (!_pageRegistered && MenuUI.Instance != null)
            {
                MenuUI.Instance.RegisterPage("Voice.Title", DrawPage, MenuCategory.Gameplay);
                _pageRegistered = true;
            }

            // On revalide périodiquement _localHead (pas seulement si null) : après un
            // respawn/teleport/recalibration, le VRRig.LocalRig ou son enfant "rig/head"
            // peut changer d'instance sans que la référence stockée devienne null, ce qui
            // gèlerait silencieusement la bulle et le calcul de distance sur une position
            // périmée.
            _localHeadRefreshTimer -= Time.deltaTime;
            if (_localHead == null || _localHeadRefreshTimer <= 0f)
            {
                _localHeadRefreshTimer = 1f;
                RefreshLocalHead();
            }

            // Détecte l'activation/désactivation du mod : si désactivé, on restaure
            // immédiatement le son par défaut et on masque l'aperçu visuel en cours.
            bool nowEnabled = Enabled;
            if (nowEnabled != _wasEnabled)
            {
                _wasEnabled = nowEnabled;
                if (!nowEnabled)
                {
                    foreach (var kv in _cache) RestoreRig(kv.Key);
                    HidePreview();
                }
            }

            UpdatePreview();
        }

        void RefreshLocalHead()
        {
            var localRig = VRRig.LocalRig;
            if (localRig == null) return;
            var head = localRig.transform.Find("rig/head");
            if (head != null) _localHead = head;
        }

        Transform GetOtherHead(VRRig rig)
        {
            if (_headCache.TryGetValue(rig, out var cached) && cached != null) return cached;
            var head = rig.transform.Find("rig/head");
            if (head != null) _headCache[rig] = head;
            return head;
        }

        // --- Aperçu visuel des distances ---

        /// <summary>A appeler dès qu'un réglage (rayon proche/loin/volume) vient de changer.</summary>
        void NotifySettingsChanged()
        {
            _previewTimer = PreviewHoldDuration;
            EnsurePreviewObjects();
        }

        void EnsurePreviewObjects()
        {
            if (_previewInner == null) _previewInner = BuildPreviewSphere("MUM_VoicePreview_Inner");
            if (_previewOuter == null) _previewOuter = BuildPreviewSphere("MUM_VoicePreview_Outer");
        }

        GameObject BuildPreviewSphere(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = go.GetComponent<Renderer>();
            Shader shader = Shader.Find("GorillaTag/UberTransparent") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            if (shader != null) rend.material = new Material(shader);
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            return go;
        }

        void UpdatePreview()
        {
            bool showing = _previewTimer > 0f;
            if (showing) _previewTimer -= Time.deltaTime;

            if (!showing || _localHead == null || !Enabled)
            {
                HidePreview();
                return;
            }

            EnsurePreviewObjects();

            // Fondu de sortie sur la dernière demi-seconde pour une disparition douce.
            float fade = Mathf.Clamp01(_previewTimer / 0.5f);

            PositionPreviewSphere(_previewInner, _innerRadius, new Color(0.15f, 0.95f, 0.55f, 0.16f * fade));
            PositionPreviewSphere(_previewOuter, _outerRadius, new Color(0.95f, 0.45f, 0.10f, 0.10f * fade));
        }

        void PositionPreviewSphere(GameObject go, float radius, Color color)
        {
            if (go == null) return;
            if (!go.activeSelf) go.SetActive(true);
            go.transform.position = _localHead.position;
            go.transform.localScale = Vector3.one * (radius * 2f); // sphère unitaire = diamètre 1
            var rend = go.GetComponent<Renderer>();
            if (rend != null && rend.material != null)
            {
                rend.material.color = color;
                if (rend.material.HasProperty("_BaseColor")) rend.material.SetColor("_BaseColor", color);
            }
        }

        void HidePreview()
        {
            _previewTimer = 0f;
            if (_previewInner != null) _previewInner.SetActive(false);
            if (_previewOuter != null) _previewOuter.SetActive(false);
        }

        float MeasureLevel(AudioSource src)
        {
            if (!src.isPlaying) return 0f;
            src.GetOutputData(_sampleBuf, 0);
            float sum = 0f;
            for (int i = 0; i < _sampleBuf.Length; i++) sum += _sampleBuf[i] * _sampleBuf[i];
            return Mathf.Sqrt(sum / _sampleBuf.Length);
        }

        /// <summary>
        /// Calcule le volume manuellement à partir de la distance tête-à-tête (au lieu de
        /// laisser Unity le déduire via minDistance/maxDistance/AudioListener). Volume plein
        /// jusqu'à _innerRadius, décroissance linéaire jusqu'à _outerVolume à _outerRadius,
        /// puis _outerVolume constant au-delà.
        /// </summary>
        float ComputeVolumeFromDistance(float distance)
        {
            if (distance <= _innerRadius) return 1f;
            if (distance >= _outerRadius) return _outerVolume;
            float t = (distance - _innerRadius) / Mathf.Max(_outerRadius - _innerRadius, 0.01f);
            return Mathf.Lerp(1f, _outerVolume, t);
        }

        public void ApplyToRig(VRRig rig)
        {
            if (rig == null || !Enabled || VoiceAudioField == null || _localHead == null) return;

            if (!_cache.TryGetValue(rig, out var src) || src == null)
            {
                src = VoiceAudioField.GetValue(rig) as AudioSource;
                if (src == null) return;
                _cache[rig] = src;
                _knownRigs.Add(rig);
            }

            var otherHead = GetOtherHead(rig);
            // Si on ne trouve pas rig/head sur cet autre joueur, on retombe sur la position
            // du VRRig lui-même plutôt que de ne rien faire, pour rester robuste face à une
            // hiérarchie légèrement différente selon la version du jeu.
            Vector3 otherPos = otherHead != null ? otherHead.position : rig.transform.position;

            float distance = Vector3.Distance(_localHead.position, otherPos);
            float volume = ComputeVolumeFromDistance(distance);

            // On garde la spatialisation 3D d'Unity pour la direction du son (gauche/droite/
            // devant/derrière), mais on désactive son atténuation automatique par distance
            // (qui dépend de l'AudioListener, potentiellement décalé du vrai head local) en
            // gardant minDistance/maxDistance très larges et en pilotant le volume nous-mêmes
            // chaque frame réseau via le calcul tête-à-tête ci-dessus.
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1000f;
            src.maxDistance = 1001f; // hors de portée pratique : l'atténuation auto d'Unity ne joue quasi aucun rôle
            src.volume = volume;
        }

        public void RestoreRig(VRRig rig)
        {
            if (rig == null || VoiceAudioField == null) return;
            var src = VoiceAudioField.GetValue(rig) as AudioSource;
            if (src == null) return;

            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 1f;
            src.maxDistance = 15f;
            src.volume = 1f;
        }

        void DrawPage(MenuUI ui)
        {
            ui.Section(I18n.T("Voice.Section"));

            if (!Enabled)
            {
                ui.BeginCard();
                ui.Label(I18n.T("Voice.EnableFromModsHint"), Theme.Dim, 9);
                ui.EndCard();
            }

            ui.Space(6);
            ui.BeginCard();
            ui.Label(I18n.T("Voice.RadiusNear"), Theme.Dim, 9);
            float newInner = ui.Slider(I18n.T("Voice.RadiusNearShort"), _innerRadius, MinRadius, _outerRadius - 0.5f, "0.0m");
            if (!Mathf.Approximately(newInner, _innerRadius) && Enabled)
            {
                _innerRadius = newInner;
                SaveSettings();
                NotifySettingsChanged();
            }

            ui.Space(4);
            ui.Label(I18n.T("Voice.RadiusFar"), Theme.Dim, 9);
            float newOuter = ui.Slider(I18n.T("Voice.RadiusFarShort"), _outerRadius, _innerRadius + 0.5f, MaxRadius, "0.0m");
            if (!Mathf.Approximately(newOuter, _outerRadius) && Enabled)
            {
                _outerRadius = newOuter;
                SaveSettings();
                NotifySettingsChanged();
            }

            ui.Space(4);
            ui.Label(I18n.T("Voice.FarVolume"), Theme.Dim, 9);
            float newVol = ui.Slider(I18n.T("Voice.FarVolumeShort"), _outerVolume, 0f, 1f, "0.00");
            if (!Mathf.Approximately(newVol, _outerVolume) && Enabled)
            {
                _outerVolume = newVol;
                SaveSettings();
                NotifySettingsChanged();
            }
            ui.EndCard();

            ui.Space(4);
            ui.BeginCard();
            ui.Label(I18n.T("Voice.PreviewHint"), Theme.Dim, 8, true);
            ui.EndCard();
        }
    }

    [HarmonyPatch(typeof(VRRig))]
    [HarmonyPatch("PostTick")]
    internal class VoiceChatPatch
    {
        static void Postfix(VRRig __instance)
        {
            GorillaVoices.Instance?.ApplyToRig(__instance);
        }
    }
}