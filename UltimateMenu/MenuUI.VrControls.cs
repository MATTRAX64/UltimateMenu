using System;
using System.Collections.Generic;
using UnityEngine;

namespace UltimateMenu
{
    public partial class MenuUI
    {
        sealed class VrScroll
        {
            public string Key;
            public Vector2 Position;
            public Rect Viewport;
            public VrScroll Parent;
        }
        sealed class VrControl
        {
            public string Key;
            public Rect Rect;
            public bool Slider;
            public VrScroll Scroll;
        }
        readonly List<VrControl> _vrControls = new List<VrControl>();
        readonly Dictionary<string, int> _vrOccurrences = new Dictionary<string, int>();
        readonly Dictionary<string, Vector2> _vrScrollRequests = new Dictionary<string, Vector2>();
        readonly Stack<VrScroll> _vrScrollStack = new Stack<VrScroll>();
        bool _vrInDraw, _vrKeyboardDrawing, _vrEnsureVisible;
        string _vrFocus, _vrPress, _vrAdjustKey, _vrKeyboardTarget, _vrTextTarget, _vrTextResult;
        string _vrKeyboardValue = "";
        bool _vrUpper = true;
        int _vrAdjustment;
        bool VrActive => _vrInDraw && GorillaInterface.Instance != null && GorillaInterface.Instance.CastRequested;
        bool VrCanSelect => VrActive && (_vrKeyboardTarget == null || _vrKeyboardDrawing);

        internal void CancelVrInput()
        {
            if (_vrKeyboardTarget == "@rebind") { WaitingKey = false; RebindTarget = null; }
            _vrPress = _vrAdjustKey = _vrKeyboardTarget = _vrTextTarget = null;
            _vrAdjustment = 0; _vrScrollRequests.Clear();
        }
        internal void ResetVrFocus()
        {
            CancelVrInput();
            string tab = _nav == -1 ? "Core.tab_home" : _nav == -2 ? "Core.tab_modes" : "Core.vr_settings";
            _vrFocus = _nav + ":button::" + I18n.T(tab) + ":0";
            _vrControls.Clear();
        }
        internal void BackVr()
        {
            if (_vrKeyboardTarget != null) { CancelVrInput(); _vrFocus = null; return; }
            _pendingNav = _nav >= 0 ? -2 : -1;
        }
        internal void ActivateVr()
        {
            if (_vrFocus != null) _vrPress = _vrFocus;
        }
        internal void NavigateVr(Vector2 direction)
        {
            if (_vrControls.Count == 0) return;
            var current = _vrControls.Find(c => c.Key == _vrFocus) ?? _vrControls[0];
            bool horizontal = Mathf.Abs(direction.x) > Mathf.Abs(direction.y);
            if (horizontal && current.Slider)
            {
                _vrAdjustKey = current.Key; _vrAdjustment = direction.x > 0 ? 1 : -1;
                return;
            }
            Vector2 axis = horizontal ? new Vector2(Mathf.Sign(direction.x), 0) : new Vector2(0, -Mathf.Sign(direction.y));
            VrControl best = null;
            float bestScore = float.MaxValue;
            foreach (var candidate in _vrControls)
            {
                if (candidate == current) continue;
                Vector2 delta = candidate.Rect.center - current.Rect.center;
                float forward = Vector2.Dot(delta, axis);
                if (forward <= 2f) continue;
                float lateral = Mathf.Abs(horizontal ? delta.y : delta.x);
                float score = forward + lateral * 4f;
                if (score < bestScore) { best = candidate; bestScore = score; }
            }
            if (best == null) return;
            _vrFocus = best.Key; _vrEnsureVisible = true;
            SoundFX.PlaySound("Moyen");
        }
        string VrKey(string label)
        {
            label = label ?? "";
            _vrOccurrences.TryGetValue(label, out int occurrence);
            _vrOccurrences[label] = occurrence + 1;
            return _nav + ":" + label + ":" + occurrence;
        }
        string RegisterVrControl(Rect rect, string label, bool slider = false)
        {
            string key = VrKey(label);
            if (!VrCanSelect || !GUI.enabled) return key;
            if (Event.current.type == EventType.Repaint)
            {
                var screen = new Rect(GUIUtility.GUIToScreenPoint(rect.position), rect.size);
                _vrControls.Add(new VrControl { Key = key, Rect = screen, Slider = slider,
                    Scroll = _vrScrollStack.Count > 0 ? _vrScrollStack.Peek() : null });
                if (_vrFocus == null) _vrFocus = key;
                if (_vrFocus == key)
                {
                    Color old = GUI.color;
                    GUI.color = Color.white;
                    float w = Mathf.Max(2f, UI.Scale * 2f);
                    GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, w), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.x, rect.yMax - w, rect.width, w), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.x, rect.y, w, rect.height), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(rect.xMax - w, rect.y, w, rect.height), Texture2D.whiteTexture);
                    GUI.color = old;
                }
            }
            return key;
        }
        bool ConsumeVrPress(string key)
        {
            if (!VrCanSelect || !GUI.enabled || Event.current.type != EventType.Layout || _vrPress != key) return false;
            _vrPress = null; GUI.changed = true; return true;
        }
        public static bool IsVrActivation { get; private set; }
        public static bool ControlButton(Rect rect, string text, GUIStyle style)
            => ControlButton(rect, new GUIContent(text), style);
        public static bool ControlButton(Rect rect, GUIContent text, GUIStyle style)
        {
            IsVrActivation = false;
            bool clicked = GUI.Button(rect, text, style);
            var ui = Instance;
            if (ui == null || !ui._vrInDraw) return clicked;
            string key = ui.RegisterVrControl(rect, "button:" + text.text + ":" + text.tooltip);
            IsVrActivation = ui.ConsumeVrPress(key);
            return IsVrActivation || clicked;
        }
        public static bool ControlLayoutButton(string text, GUIStyle style, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), style, options);
            return ControlButton(rect, text, style);
        }
        public static bool ControlLayoutToggle(bool value, string text, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), GUI.skin.toggle, options);
            bool result = GUI.Toggle(rect, value, text);
            var ui = Instance;
            if (ui == null || !ui._vrInDraw) return result;
            string key = ui.RegisterVrControl(rect, "toggle:" + text);
            return ui.ConsumeVrPress(key) ? !value : result;
        }
        float AdjustVrSlider(Rect rect, string label, float value, float min, float max, bool integer)
        {
            string key = RegisterVrControl(rect, "slider:" + label, true);
            if (VrCanSelect && GUI.enabled && Event.current.type == EventType.Layout && key == _vrAdjustKey)
            {
                value = Mathf.Clamp(value + _vrAdjustment * (integer ? 1f : (max - min) / 50f), min, max);
                _vrAdjustKey = null; _vrAdjustment = 0; GUI.changed = true;
            }
            return value;
        }
        public static float ControlLayoutSlider(float value, float min, float max, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.horizontalSlider, options);
            value = GUI.HorizontalSlider(rect, value, min, max);
            return Instance != null && Instance._vrInDraw ? Instance.AdjustVrSlider(rect, "", value, min, max, false) : value;
        }
        public static string ControlTextField(Rect rect, string value, GUIStyle style)
        {
            string result = GUI.TextField(rect, value, style);
            var ui = Instance;
            if (ui == null || !ui._vrInDraw) return result;
            string key = ui.RegisterVrControl(rect, "text");
            if (ui._vrTextTarget == key && Event.current.type == EventType.Layout)
            {
                result = ui._vrTextResult;
                ui._vrTextTarget = null; GUI.changed = true;
            }
            if (ui.ConsumeVrPress(key))
            {
                ui._vrKeyboardTarget = key; ui._vrKeyboardValue = result; ui._vrFocus = null;
            }
            return result;
        }
        public static string ControlLayoutTextField(string value, params GUILayoutOption[] options)
            => ControlLayoutTextField(value, GUI.skin.textField, options);
        public static string ControlLayoutTextField(string value, GUIStyle style, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(value), style, options);
            return ControlTextField(rect, value, style);
        }
        void DrawVrKeyboard()
        {
            if (_vrKeyboardTarget == null) return;
            _vrKeyboardDrawing = true;
            bool enabled = GUI.enabled; GUI.enabled = true;
            try
            {
                Rect box = new Rect(_rect.x + 12 * UI.Scale, _rect.y + 80 * UI.Scale, _rect.width - 24 * UI.Scale, _rect.height - 100 * UI.Scale);
                UI.DrawRounded(box, Theme.BG2, Mathf.RoundToInt(18 * UI.Scale));
                if (_vrKeyboardTarget == "@rebind") { DrawVrKeyBinding(box); return; }
                GUI.Label(new Rect(box.x + 12, box.y + 8, box.width - 24, 45 * UI.Scale), _vrKeyboardValue, UI.Sty(Theme.Text, 14));
                string[] rows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM", "-_.,@/:" };
                float cell = (box.width - 24) / 10f, h = Mathf.Min(44 * UI.Scale, (box.height - 100 * UI.Scale) / 7f);
                for (int row = 0; row < rows.Length; row++)
                    for (int col = 0; col < rows[row].Length; col++)
                    {
                        string key = rows[row][col].ToString();
                        if (!_vrUpper) key = key.ToLowerInvariant();
                        if (ControlButton(new Rect(box.x + 12 + col * cell, box.y + 60 * UI.Scale + row * h, cell - 3, h - 3), key, UI.BtnS(TB3, Theme.Text)))
                            _vrKeyboardValue += key;
                    }
                string[] actions = { "Aa", I18n.T("Core.vr_space"), I18n.T("Core.vr_erase"), I18n.T("Core.vr_confirm"), I18n.T("Core.cancel") };
                for (int i = 0; i < actions.Length; i++)
                    if (ControlButton(new Rect(box.x + 12 + i * (box.width - 24) / 5, box.y + 60 * UI.Scale + 5 * h,
                        (box.width - 24) / 5 - 3, h), actions[i], UI.BtnS(TB3, Theme.Text)))
                    {
                        if (i == 0) _vrUpper = !_vrUpper;
                        else if (i == 1) _vrKeyboardValue += " ";
                        else if (i == 2 && _vrKeyboardValue.Length > 0) _vrKeyboardValue = _vrKeyboardValue.Substring(0, _vrKeyboardValue.Length - 1);
                        else if (i >= 3)
                        {
                            if (i == 3) { _vrTextTarget = _vrKeyboardTarget; _vrTextResult = _vrKeyboardValue; }
                            _vrKeyboardTarget = null; _vrFocus = null;
                        }
                    }
            }
            finally { GUI.enabled = enabled; _vrKeyboardDrawing = false; }
        }
        void DrawVrKeyBinding(Rect box)
        {
            GUI.Label(new Rect(box.x + 12, box.y + 8, box.width - 24, 35 * UI.Scale), I18n.T("Core.waiting_key"), UI.Sty(Theme.Text, 12));
            var keys = new List<KeyCode>();
            for (int i = 0; i < 26; i++) keys.Add((KeyCode)((int)KeyCode.A + i));
            for (int i = 0; i < 10; i++) keys.Add((KeyCode)((int)KeyCode.Alpha0 + i));
            for (int i = 0; i < 12; i++) keys.Add((KeyCode)((int)KeyCode.F1 + i));
            keys.AddRange(new[] { KeyCode.Space, KeyCode.Tab, KeyCode.Return, KeyCode.Backspace, KeyCode.Delete,
                KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.RightAlt,
                KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown });
            float width = (box.width - 24) / 6f;
            float height = (box.height - 85 * UI.Scale) / Mathf.Ceil(keys.Count / 6f);
            for (int i = 0; i < keys.Count; i++)
                if (ControlButton(new Rect(box.x + 12 + (i % 6) * width, box.y + 45 * UI.Scale + (i / 6) * height,
                    width - 3, height - 3), KeyLayout.Display(keys[i]), UI.BtnS(TB3, Theme.Text, 9)))
                {
                    Keys.ModifyKey(RebindTarget, keys[i]);
                    _keyHistory.Insert(0, keys[i].ToString());
                    if (_keyHistory.Count > 5) _keyHistory.RemoveAt(5);
                    CancelVrInput(); _vrFocus = null;
                }
            if (ControlButton(new Rect(box.x + 12, box.yMax - 34 * UI.Scale, box.width - 24, 28 * UI.Scale), I18n.T("Core.cancel"), UI.BtnS(TB3, Theme.Text)))
            { CancelVrInput(); _vrFocus = null; }
        }

        Vector2 BeginVrScrollState(Vector2 value)
        {
            if (!_vrInDraw) return value;
            string key = VrKey("scroll");
            if (_vrScrollRequests.TryGetValue(key, out Vector2 pending)) { value = pending; _vrScrollRequests.Remove(key); }
            _vrScrollStack.Push(new VrScroll { Key = key, Position = value, Parent = _vrScrollStack.Count > 0 ? _vrScrollStack.Peek() : null });
            return value;
        }
        void EndVrScrollState()
        {
            if (!_vrInDraw || _vrScrollStack.Count == 0) return;
            var scope = _vrScrollStack.Pop();
            if (Event.current.type == EventType.Repaint)
            {
                Rect rect = GUILayoutUtility.GetLastRect();
                scope.Viewport = new Rect(GUIUtility.GUIToScreenPoint(rect.position), rect.size);
            }
        }
        public static Vector2 ControlBeginScrollView(Vector2 value, params GUILayoutOption[] options)
        {
            if (Instance != null) value = Instance.BeginVrScrollState(value);
            return GUILayout.BeginScrollView(value, options);
        }
        public static void ControlEndScrollView()
        {
            GUILayout.EndScrollView(); Instance?.EndVrScrollState();
        }
        void FinishVrFrame()
        {
            if (Event.current.type == EventType.Layout)
            {
                // Commands live for one Layout only; disappearing controls cannot receive stale input.
                _vrPress = _vrAdjustKey = null;
            }
            if (Event.current.type != EventType.Repaint) return;
            var selected = _vrControls.Find(c => c.Key == _vrFocus);
            if (selected == null) { _vrFocus = _vrControls.Count > 0 ? _vrControls[0].Key : null; return; }
            if (!_vrEnsureVisible) return;
            _vrEnsureVisible = false;
            Rect target = selected.Rect;
            for (VrScroll scope = selected.Scroll; scope != null; scope = scope.Parent)
            {
                float delta = target.yMin < scope.Viewport.yMin ? target.yMin - scope.Viewport.yMin - 5 :
                    target.yMax > scope.Viewport.yMax ? target.yMax - scope.Viewport.yMax + 5 : 0;
                if (Mathf.Abs(delta) < 1) continue;
                Vector2 position = scope.Position;
                position.y = Mathf.Max(0, position.y + delta);
                _vrScrollRequests[scope.Key] = position;
                target.y -= position.y - scope.Position.y;
            }
        }
    }
}

