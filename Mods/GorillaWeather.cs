using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace UltimateMenu
{
    public class GorillaWeather : MonoBehaviour
    {
        private object _bdnm; private System.Type _bdnmType;
        private MethodInfo _setTimeMethod; private FieldInfo _currentTimeField, _weatherCycleField;
        private FieldInfo _instanceField;
        private GameObject _rainGO;
        private List<string> _uniqueNames = new List<string>();
        private List<int> _uniqueIndexes = new List<int>();
        private string _selectedTimeName = ""; private bool _rainEnabled;
        private bool _pageRegistered; private int _regTries;
        private int _targetTimeIndex = -1; private bool _lockTime;
        private int _retryCount; private const int MAX_RETRY = 5;
        private static readonly string[] _excluded = { "lightning" };

        private object _lastKnownInstance;
        private float _roomCheckTimer;
        private const float ROOM_CHECK_INTERVAL = 1f;

        void Start() { TryRegister(); Invoke(nameof(Init), 2f); }

        void Update()
        {
            if (!_pageRegistered && _regTries++ % 60 == 0 && _regTries < 600) TryRegister();

            _roomCheckTimer += Time.deltaTime;
            if (_roomCheckTimer >= ROOM_CHECK_INTERVAL)
            {
                _roomCheckTimer = 0f;
                CheckForRoomChange();
            }
        }

        void FixedUpdate()
        {
            if (_lockTime && _bdnm != null && _setTimeMethod != null && _targetTimeIndex >= 0)
                try { _setTimeMethod.Invoke(GetInstance(), new object[] { _targetTimeIndex, true }); } catch { }
        }

        void TryRegister()
        {
            if (!_pageRegistered && MenuUI.Instance != null)
            {
                MenuUI.Instance.RegisterPage("TimeWeather.Title", DrawPage, MenuCategory.Environnement);
                _pageRegistered = true;
            }
        }

        void Init()
        {
            var go = GameObject.Find("Gameplay Scripts/BetterDayNight");
            if (go == null) return;
            _bdnm = go.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name == "BetterDayNightManager");
            if (_bdnm == null) return;
            _bdnmType = _bdnm.GetType();

            _instanceField = _bdnmType.GetField("instance", BindingFlags.Public | BindingFlags.Static)
                          ?? _bdnmType.GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
            if (_instanceField != null) { var s = _instanceField.GetValue(null); if (s != null) _bdnm = s; }

            var namesField = _bdnmType.GetField("dayNightLightmapNames", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var raw = namesField != null
                ? (string[])namesField.GetValue(null)
                : new[] { "night", "sunrise", "sunrise", "10am", "noon", "3pm", "sunset", "sunset", "night", "night", "lightning" };

            var dict = new Dictionary<string, int>();
            for (int i = 0; i < raw.Length; i++) { string n = raw[i].Trim(); if (!_excluded.Contains(n)) dict[n] = i; }
            foreach (var kv in dict.OrderBy(kv => kv.Value)) { _uniqueNames.Add(kv.Key); _uniqueIndexes.Add(kv.Value); }

            _currentTimeField = _bdnmType.GetField("currentTimeOfDay", BindingFlags.Public | BindingFlags.Instance)
                              ?? _bdnmType.GetField("currentTimeOfDay", BindingFlags.NonPublic | BindingFlags.Instance);
            _setTimeMethod = _bdnmType.GetMethod("SetTimeOfDay", BindingFlags.Public | BindingFlags.Instance);
            _weatherCycleField = _bdnmType.GetField("weatherCycle", BindingFlags.Public | BindingFlags.Instance)
                              ?? _bdnmType.GetField("weatherCycle", BindingFlags.NonPublic | BindingFlags.Instance);
            _rainGO = GameObject.Find("Environment Objects/LocalObjects_Prefab/Forest/Environment/WeatherDayNight/rain");

            _lastKnownInstance = GetInstance();
            RefreshState();
        }

        object GetInstance()
        {
            if (_instanceField != null) { var s = _instanceField.GetValue(null); if (s != null) return s; }
            return _bdnm;
        }

        void CheckForRoomChange()
        {
            if (_bdnmType == null) return;

            var current = GetInstance();
            if (current == null)
            {
                Invoke(nameof(Init), 2f);
                return;
            }

            bool changed = !ReferenceEquals(current, _lastKnownInstance);
            if (!changed) return;

            _lastKnownInstance = current;
            Debug.Log("[TimeWeatherControl] Nouvelle room detectee, resynchronisation.");

            RefreshState();
            if (_lockTime && _targetTimeIndex >= 0)
            {
                _retryCount = 0;
                try { _setTimeMethod?.Invoke(current, new object[] { _targetTimeIndex, true }); } catch { }
                Invoke(nameof(VerifyAndRetry), 0.1f);
            }
            else
            {
                ApplyRain();
            }
        }

        void RefreshState()
        {
            var inst = GetInstance();
            if (_currentTimeField != null)
                try { int i = (int)_currentTimeField.GetValue(inst); int p = _uniqueIndexes.IndexOf(i); _selectedTimeName = p >= 0 ? _uniqueNames[p] : i.ToString(); } catch { _selectedTimeName = "?"; }
            if (_weatherCycleField != null)
                try { var c = (int[])_weatherCycleField.GetValue(inst); if (c?.Length > 1) _rainEnabled = c[1] == 1; } catch { }
        }

        void SetTime(int targetIdx, string targetName)
        {
            _targetTimeIndex = targetIdx; _selectedTimeName = targetName;
            _lockTime = true; _retryCount = 0;
            try { _setTimeMethod?.Invoke(GetInstance(), new object[] { targetIdx, true }); } catch { }
            Invoke(nameof(VerifyAndRetry), 0.1f);
        }

        void VerifyAndRetry()
        {
            if (_currentTimeField == null || _retryCount >= MAX_RETRY) return;
            try
            {
                var inst = GetInstance();
                int current = (int)_currentTimeField.GetValue(inst);
                if (current != _targetTimeIndex)
                {
                    _retryCount++;
                    try { _setTimeMethod?.Invoke(inst, new object[] { _targetTimeIndex, true }); } catch { }
                    Invoke(nameof(VerifyAndRetry), 0.1f);
                }
                else
                {
                    int pos = _uniqueIndexes.IndexOf(current);
                    _selectedTimeName = pos >= 0 ? _uniqueNames[pos] : current.ToString();
                    _retryCount = 0;
                    ApplyRain();
                }
            }
            catch { }
        }

        void ApplyRain()
        {
            if (_weatherCycleField == null) return;
            try
            {
                var inst = GetInstance();
                var cycle = (int[])_weatherCycleField.GetValue(inst);
                if (cycle == null) return;
                int val = _rainEnabled ? 1 : 0;
                for (int i = 1; i < cycle.Length; i++) cycle[i] = val;
                if (_rainGO != null) _rainGO.SetActive(_rainEnabled);
            }
            catch { }
        }

        void ToggleRain()
        {
            _rainEnabled = !_rainEnabled;
            ApplyRain();
        }

        void DrawPage(MenuUI ui)
        {
            if (_bdnm == null) { ui.Label(I18n.T("TimeWeather.NotFound"), Theme.Red); return; }
            RefreshState();

            ui.Section(I18n.T("TimeWeather.Section"));

            // --- Heure actuelle + verrouillage (switch Apple au lieu du bouton texte) ---
            ui.BeginCard();
            GUILayout.BeginHorizontal();
            ui.Label(I18n.T("TimeWeather.HourLabel") + _selectedTimeName, Theme.Accent, 13, true);
            GUILayout.EndHorizontal();
            ui.Space(6);
            bool newLock = ui.Toggle(_lockTime ? I18n.T("TimeWeather.Locked") : I18n.T("TimeWeather.Unlocked"), _lockTime, 140f);
            if (newLock != _lockTime) _lockTime = newLock;
            ui.EndCard();

            ui.Space(6);

            // --- Sélection de l'heure (grille de boutons) ---
            ui.BeginCard();
            ui.Label(I18n.T("TimeWeather.PickHour"), Theme.Dim, 9, true);
            ui.Space(6);
            for (int i = 0; i < _uniqueNames.Count; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (int j = 0; j < 3 && i + j < _uniqueNames.Count; j++)
                {
                    int idx = i + j;
                    bool isCurrent = _uniqueNames[idx] == _selectedTimeName;
                    if (ui.Btn(_uniqueNames[idx], ui.TB3, isCurrent ? Theme.Accent : Theme.Text, 70))
                        SetTime(_uniqueIndexes[idx], _uniqueNames[idx]);
                    GUILayout.Space(4);
                }
                GUILayout.EndHorizontal();
                ui.Space(4);
            }
            ui.EndCard();

            ui.Space(6);

            // --- Pluie (switch Apple au lieu du bouton texte) ---
            ui.BeginCard();
            bool newRain = ui.Toggle(I18n.T("TimeWeather.RainLabel"), _rainEnabled, 140f);
            if (newRain != _rainEnabled) ToggleRain();
            ui.EndCard();
        }
    }
}