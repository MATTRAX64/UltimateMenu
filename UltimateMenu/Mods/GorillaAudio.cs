using System;
using Photon.Voice.Unity;
using Photon.Voice.Unity.UtilityScripts;
using UnityEngine;

namespace UltimateMenu
{
    public sealed class GorillaAudio : MonoBehaviour
    {
        const string Title = "Audio - Volume et micro", Pref = "MUM_Audio_";
        bool _registered, _active, _mute, _ownsAmplifier, _dirty, _errorLogged;
        float _volume = 1f, _gain = 1f, _originalVolume, _lastVolume, _nextFind, _saveAt;
        float _originalGain, _originalBoost, _lastGain;
        bool _originalEnabled;
        Recorder? _recorder;
        MicAmplifier? _amplifier;
        string _status = "Micro en attente";

        void Awake()
        {
            _volume = Mathf.Clamp01(MenuPrefs.GetFloat(Pref + "Volume", 1f));
            _gain = Mathf.Clamp(MenuPrefs.GetFloat(Pref + "Gain", 1f), 0.5f, 1.5f);
            // Never persist mute: reconnecting should not silently keep a previous mute.
        }

        void Update()
        {
            if (!_registered && MenuUI.Instance != null)
            {
                MenuUI.Instance.RegisterPage(Title, DrawPage, MenuCategory.Social); _registered = true;
            }
            bool active = _registered && MenuUI.Instance != null && MenuUI.Instance.IsModEnabled(Title);
            if (active && !_active)
            {
                _originalVolume = AudioListener.volume; _active = true; ApplyVolume(); _nextFind = 0f;
            }
            else if (!active && _active) Restore();
            if (_active && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 0.5f; FindMicrophone();
            }
            if (_dirty && Time.unscaledTime >= _saveAt) Save();
        }

        void FindMicrophone()
        {
            try
            {
                var recorder = GorillaTagger.Instance != null ? GorillaTagger.Instance.myRecorder : null;
                if (recorder == _recorder && _amplifier != null) return;
                ReleaseMicrophone();
                if (recorder == null) { _status = "Micro en attente"; return; }
                _recorder = recorder;
                _amplifier = recorder.GetComponent<MicAmplifier>();
                _ownsAmplifier = _amplifier == null;
                if (_ownsAmplifier) _amplifier = recorder.gameObject.AddComponent<MicAmplifier>();
                _originalGain = _amplifier!.AmplificationFactor;
                _originalBoost = _amplifier.BoostValue; _originalEnabled = _amplifier.enabled;
                ApplyGain();
                if (recorder.IsRecording) recorder.RestartRecording(true);
                _status = "Micro connecte";
            }
            catch (Exception e)
            {
                ReleaseMicrophone(); _status = "Micro indisponible : voir le journal BepInEx";
                if (!_errorLogged) { _errorLogged = true; Log.Err("GorillaAudio microphone", e); }
            }
        }

        void ApplyVolume() { if (_active) { _lastVolume = _volume; AudioListener.volume = _lastVolume; } }
        void ApplyGain()
        {
            if (_amplifier == null) return;
            _lastGain = _mute ? 0f : _gain;
            _amplifier.AmplificationFactor = _lastGain;
            _amplifier.BoostValue = 0f; _amplifier.enabled = true;
        }

        void ReleaseMicrophone()
        {
            if (_amplifier != null)
            {
                if (_ownsAmplifier)
                {
                    // The existing audio processor becomes neutral before deferred destruction.
                    _amplifier.AmplificationFactor = 1f; _amplifier.BoostValue = 0f;
                    _amplifier.enabled = false; Destroy(_amplifier);
                }
                else if (Mathf.Approximately(_amplifier.AmplificationFactor, _lastGain) && _amplifier.BoostValue == 0f)
                {
                    _amplifier.AmplificationFactor = _originalGain;
                    _amplifier.BoostValue = _originalBoost; _amplifier.enabled = _originalEnabled;
                }
            }
            _amplifier = null; _recorder = null; _ownsAmplifier = false;
        }

        void Restore()
        {
            if (_active && Mathf.Approximately(AudioListener.volume, _lastVolume)) AudioListener.volume = _originalVolume;
            ReleaseMicrophone(); _active = false; _status = "Reglages desactives";
        }
        void Changed() { _dirty = true; _saveAt = Time.unscaledTime + 0.5f; }
        void Save()
        {
            MenuPrefs.SetFloat(Pref + "Volume", _volume); MenuPrefs.SetFloat(Pref + "Gain", _gain);
            MenuPrefs.Save(); _dirty = false;
        }

        void DrawPage(MenuUI ui)
        {
            ui.BeginCard();
            float volume=ui.Slider(I18n.T("Volume (%)"),_volume*100f,0f,100f,"0")/100f;
            if(!Mathf.Approximately(volume,_volume)){_volume=volume;ApplyVolume();Changed();}
            ui.EndCard();
            ui.BeginCard();
            ui.Label(I18n.T("UI.Micro"),Theme.Text,11,true);
            ui.Label(I18n.T(_status),Theme.Dim,9);
            float gain=ui.Slider(I18n.T("Gain du micro"),_gain,0.5f,1.5f,"0.0x");
            if(!Mathf.Approximately(gain,_gain)){_gain=gain;ApplyGain();Changed();}
            bool mute=ui.Toggle(I18n.T("Couper mon micro"),_mute);
            if(mute!=_mute){_mute=mute;ApplyGain();}
            if(_gain>1f)ui.Label(I18n.T("Un gain fort peut saturer le son. Commence avec 1.2x."),Theme.Dim,9);
            if(ui.Btn(I18n.T("Micro normal (1x)"),ui.TB3,Theme.Text)){_gain=1f;_mute=false;ApplyGain();Changed();}
            ui.EndCard();            if (ui.Btn(I18n.T("Retrouver les reglages d'origine")))
            {
                _volume=_active?_originalVolume:1f;
                _gain=1f;_mute=false;ApplyVolume();ApplyGain();Changed();
            }
        }

        void OnDisable() { Restore(); if (_dirty) Save(); }
        void OnDestroy() { Restore(); if (_dirty) Save(); }
    }
}
