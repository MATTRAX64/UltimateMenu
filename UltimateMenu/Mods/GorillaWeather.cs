using System;
using UnityEngine;
using System.Reflection;
using System.Linq;

namespace UltimateMenu
{
    public class GorillaWeather : MonoBehaviour
    {
        object _bdnm; Type _bdnmType;
        FieldInfo _instF, _idxF, _setF, _cycF;
        MethodInfo _setM, _clrM, _mapsM, _lerpM;
        object _staticVal, _noneVal, _rainVal;

        GameObject _rainGO;
        Component _rainAudio;
        FieldInfo _myWeatherF, _volF;

        static readonly string[] _names = { "night", "sunrise", "10am", "noon", "3pm", "sunset" };
        static readonly int[] _idxs = { 0, 1, 2, 3, 4, 5 };

        string _selName = "";
        bool _rainOn;
        float _rainVol = 0.5f;

        bool _pReg; int _regTries;
        int _targetIdx = -1;
        bool _timeLock;

        object _lastInst;
        float _roomTimer, _weatherTimer;

        object _joinedHandler;

        bool Raining => _rainOn && !_timeLock;

        void Start() { TryRegister(); Invoke(nameof(Init), 2f); }
        void OnDestroy() => Unsub();

        void Update()
        {
            if (!_pReg && _regTries++ % 60 == 0 && _regTries < 600) TryRegister();

            _roomTimer += Time.deltaTime;
            if (_roomTimer >= 1f) { _roomTimer = 0f; CheckRoom(); }

            if (_timeLock && _bdnm != null && _setF != null && _staticVal != null)
            {
                try { if (!_setF.GetValue(Inst()).Equals(_staticVal)) ReapplyLock(); } catch { }
            }

            _weatherTimer += Time.deltaTime;
            if (_weatherTimer >= 0.5f) { _weatherTimer = 0f; ApplyWeather(Raining); }
        }

        void TryRegister()
        {
            if (!_pReg && MenuUI.Instance != null) { MenuUI.Instance.RegisterPage("TimeWeather.Title", DrawPage, MenuCategory.Environnement); _pReg = true; }
        }

        void Init()
        {
            var go = GameObject.Find("Gameplay Scripts/BetterDayNight");
            if (go == null) { Invoke(nameof(Init), 2f); return; }
            _bdnm = go.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name == "BetterDayNightManager");
            if (_bdnm == null) { Invoke(nameof(Init), 2f); return; }
            _bdnmType = _bdnm.GetType();

            _instF = _bdnmType.GetField("instance", BindingFlags.Public | BindingFlags.Static);
            if (_instF != null) { var s = _instF.GetValue(null); if (s != null) _bdnm = s; }

            _idxF = _bdnmType.GetField("currentTimeIndex", BindingFlags.Public | BindingFlags.Instance);
            _setF = _bdnmType.GetField("currentSetting", BindingFlags.Public | BindingFlags.Instance);
            _cycF = _bdnmType.GetField("weatherCycle", BindingFlags.Public | BindingFlags.Instance);
            _setM = _bdnmType.GetMethod("SetTimeOfDay", BindingFlags.Public | BindingFlags.Instance);
            _clrM = _bdnmType.GetMethod("ClearTimeOfDay", BindingFlags.Public | BindingFlags.Instance);
            _mapsM = _bdnmType.GetMethod("ChangeMaps", BindingFlags.NonPublic | BindingFlags.Instance);
            _lerpM = _bdnmType.GetMethod("ChangeLerps", BindingFlags.NonPublic | BindingFlags.Instance);

            if (_setF != null) try { _staticVal = Enum.Parse(_setF.FieldType, "Static"); } catch { }
            if (_cycF != null)
            {
                var weatherEnum = _cycF.FieldType.GetElementType();
                try { _noneVal = Enum.Parse(weatherEnum, "None"); _rainVal = Enum.Parse(weatherEnum, "Raining"); } catch { }
            }

            Sub();

            _rainGO = GameObject.Find("Environment Objects/LocalObjects_Prefab/Forest/Environment/WeatherDayNight/rain");
            SetupRainAudio();
            ApplyRain();

            _lastInst = Inst();
            Refresh();
        }

        object Inst()
        {
            if (_instF != null) { var s = _instF.GetValue(null); if (s != null) return s; }
            return _bdnm;
        }

        void Sub()
        {
            try
            {
                var rs = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("GorillaNetworking.RoomSystem")).FirstOrDefault(t => t != null);
                var evt = rs?.GetField("JoinedRoomEvent", BindingFlags.Public | BindingFlags.Static);
                if (evt == null) return;
                Action h = OnRoomJoined; _joinedHandler = h;
                var cur = evt.GetValue(null) as Action;
                evt.SetValue(null, cur == null ? h : cur + h);
            }
            catch { }
        }

        void Unsub()
        {
            try
            {
                if (_joinedHandler == null) return;
                var rs = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("GorillaNetworking.RoomSystem")).FirstOrDefault(t => t != null);
                var evt = rs?.GetField("JoinedRoomEvent", BindingFlags.Public | BindingFlags.Static);
                if (evt == null) return;
                var cur = evt.GetValue(null) as Action;
                if (cur != null) evt.SetValue(null, cur - (Action)_joinedHandler);
            }
            catch { }
        }

        void OnRoomJoined()
        {
            if (_timeLock && _targetIdx >= 0) ReapplyLock();
            ApplyWeather(Raining);
        }

        void SetupRainAudio()
        {
            if (_rainGO == null) return;
            _rainAudio = _rainGO.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name == "TimeOfDayDependentAudio");
            if (_rainAudio == null) return;
            var t = _rainAudio.GetType();
            _myWeatherF = t.GetField("myWeather", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _volF = t.GetField("volumes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            try { _myWeatherF?.SetValue(_rainAudio, "All"); } catch { }
            ApplyVolume();
        }

        void CheckRoom()
        {
            if (_bdnmType == null) return;
            var cur = Inst();
            if (cur == null) { Invoke(nameof(Init), 2f); return; }
            if (ReferenceEquals(cur, _lastInst)) return;

            _lastInst = cur;
            Refresh();
            _rainGO = GameObject.Find("Environment Objects/LocalObjects_Prefab/Forest/Environment/WeatherDayNight/rain");
            SetupRainAudio();
            ApplyRain();
        }

        void Refresh()
        {
            if (_timeLock || _idxF == null) return;
            try
            {
                int i = (int)_idxF.GetValue(Inst());
                int p = Array.IndexOf(_idxs, i);
                _selName = p >= 0 ? _names[p] : i.ToString();
            }
            catch { _selName = "?"; }
        }

        void SetTime(int idx, string name)
        {
            _targetIdx = idx; _selName = name; _timeLock = true;
            ReapplyLock();
            ApplyRain();
        }

        void ToggleLock()
        {
            _timeLock = !_timeLock;
            if (_timeLock && _targetIdx >= 0) ReapplyLock();
            else try { _clrM?.Invoke(Inst(), new object[] { true }); } catch { }
            ApplyRain();
        }

        void ReapplyLock()
        {
            if (_targetIdx < 0) return;
            var inst = Inst();
            try { _setM?.Invoke(inst, new object[] { _targetIdx, true }); } catch { }
            try { _mapsM?.Invoke(inst, new object[] { _targetIdx, _targetIdx }); } catch { }
            try { _lerpM?.Invoke(inst, new object[] { 0f }); } catch { }
            ApplyWeather(Raining);
        }

        void ApplyRain()
        {
            bool r = Raining;
            if (_rainGO != null) _rainGO.SetActive(r);
            ApplyWeather(r);
        }

        void ToggleRain() { _rainOn = !_rainOn; ApplyRain(); }

        void ApplyWeather(bool raining)
        {
            if (_cycF == null || _noneVal == null || _rainVal == null) return;
            try
            {
                var cycle = _cycF.GetValue(Inst()) as Array;
                if (cycle == null) return;
                object v = raining ? _rainVal : _noneVal;
                for (int i = 0; i < cycle.Length; i++) cycle.SetValue(v, i);
            }
            catch { }
        }

        void ApplyVolume()
        {
            if (_volF == null || _rainAudio == null) return;
            try
            {
                var vols = (float[])_volF.GetValue(_rainAudio);
                if (vols == null) return;
                for (int i = 0; i < vols.Length; i++) vols[i] = _rainVol;
            }
            catch { }
        }

        static string Cap(string n) => n switch
        {
            "night" => "Night",
            "sunrise" => "Sunrise",
            "10am" => "10 AM",
            "noon" => "Noon",
            "3pm" => "3 PM",
            "sunset" => "Sunset",
            _ => n
        };

        void DrawPage(MenuUI ui)
        {
            if (_bdnm == null) { ui.Label(I18n.T("TimeWeather.NotFound"), Theme.Red); return; }
            Refresh();

            ui.Section(I18n.T("TimeWeather.Section"));

            ui.BeginCard();
            GUILayout.BeginHorizontal();
            ui.Label(I18n.T("TimeWeather.HourLabel") + _selName, Theme.Accent, 13, true);
            GUILayout.EndHorizontal();
            ui.Space(6);
            bool nl = ui.Toggle(_timeLock ? I18n.T("TimeWeather.CustomTimeLabel") : I18n.T("TimeWeather.WeatherLabel"), _timeLock, 160f);
            if (nl != _timeLock) ToggleLock();
            ui.EndCard();

            ui.Space(6);

            if (_timeLock)
            {
                ui.BeginCard();
                for (int i = 0; i < _names.Length; i += 3)
                {
                    GUILayout.BeginHorizontal();
                    for (int j = 0; j < 3 && i + j < _names.Length; j++)
                    {
                        int idx = i + j;
                        bool cur = _names[idx] == _selName;
                        if (ui.Btn(Cap(_names[idx]), cur ? ui.TA : ui.TB3, cur ? Color.white : Theme.Text, 80))
                            SetTime(_idxs[idx], _names[idx]);
                        GUILayout.Space(6);
                    }
                    GUILayout.EndHorizontal();
                    ui.Space(6);
                }
                ui.EndCard();
            }
            else
            {
                ui.BeginCard();
                bool nr = ui.Toggle(I18n.T("TimeWeather.RainLabel"), _rainOn, 140f);
                if (nr != _rainOn) ToggleRain();

                if (_rainOn)
                {
                    ui.Space(4);
                    float nv = ui.Slider(I18n.T("TimeWeather.RainVolume"), _rainVol, 0f, 0.5f, "0.00");
                    if (!Mathf.Approximately(nv, _rainVol)) { _rainVol = nv; ApplyVolume(); }
                }
                ui.EndCard();
            }
        }
    }
}