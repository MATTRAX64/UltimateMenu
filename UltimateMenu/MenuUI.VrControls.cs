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
        readonly List<VrScroll> _vrScrollViews = new List<VrScroll>();
        readonly Dictionary<string, int> _vrOccurrences = new Dictionary<string, int>();
        readonly Dictionary<string, Vector2> _vrScrollRequests = new Dictionary<string, Vector2>();
        readonly Stack<VrScroll> _vrScrollStack = new Stack<VrScroll>();
        bool _vrInDraw, _vrKeyboardDrawing, _vrEnsureVisible;
        string _vrFocus, _vrPress, _vrAdjustKey, _vrKeyboardTarget, _vrTextTarget, _vrTextResult;
        string _vrKeyboardValue = "";
        bool _vrUpper = true;
        int _vrAdjustment;
        string _vrPointerSlider;
        float _vrPointerRatio;
        string _vrSnapKey;
        bool VrActive => _vrInDraw && GorillaInterface.Instance != null && GorillaInterface.Instance.CastRequested;
        bool VrCanSelect => VrActive && (_vrKeyboardTarget == null || _vrKeyboardDrawing);

        internal void CancelVrInput()
        {
            if (_vrKeyboardTarget == "@rebind") { WaitingKey = false; RebindTarget = null; }
            _vrPress = _vrAdjustKey = _vrKeyboardTarget = _vrTextTarget = null;
            _vrAdjustment = 0; _vrScrollRequests.Clear();
            CancelVrSlide();
            _vrPointerSlider = null;
            _vrSnapKey = null;
        }
        internal Vector2 AssistVrPointer(Vector2 raw)
        {
            VrControl direct = null, previous = null;
            Rect previousVisible = default;
            for (int i = _vrControls.Count - 1; i >= 0; i--)
            {
                var candidate = _vrControls[i];
                Rect visible = candidate.Rect;
                for (var scope = candidate.Scroll; scope != null; scope = scope.Parent)
                    visible = Rect.MinMaxRect(Mathf.Max(visible.xMin, scope.Viewport.xMin), Mathf.Max(visible.yMin, scope.Viewport.yMin),
                        Mathf.Min(visible.xMax, scope.Viewport.xMax), Mathf.Min(visible.yMax, scope.Viewport.yMax));
                if (visible.width <= 0f || visible.height <= 0f) continue;
                if (direct == null && visible.Contains(raw)) direct = candidate;
                if (candidate.Key == _vrSnapKey) { previous = candidate; previousVisible = visible; }
            }
            if (previous != null && !previous.Slider && (direct == null || direct == previous))
            {
                float margin = Mathf.Min(12f * UI.Scale, previousVisible.height * 0.2f);
                Rect release = Rect.MinMaxRect(previousVisible.xMin - margin, previousVisible.yMin - margin,
                    previousVisible.xMax + margin, previousVisible.yMax + margin);
                if (release.Contains(raw)) return previousVisible.center;
            }
            _vrSnapKey = null;
            if (direct == null || direct.Slider) return raw;
            Rect target = direct.Rect;
            for (var scope = direct.Scroll; scope != null; scope = scope.Parent)
                target = Rect.MinMaxRect(Mathf.Max(target.xMin, scope.Viewport.xMin), Mathf.Max(target.yMin, scope.Viewport.yMin),
                    Mathf.Min(target.xMax, scope.Viewport.xMax), Mathf.Min(target.yMax, scope.Viewport.yMax));
            _vrSnapKey = direct.Key;
            return target.center;
        }
        internal bool PointVr(Vector2 position, bool pressed, bool held, float scroll)
        {
            VrControl hit = null;
            for (int i = _vrControls.Count - 1; i >= 0; i--)
            {
                var control = _vrControls[i];
                if (!control.Rect.Contains(position)) continue;
                bool visible = true;
                for (var scope = control.Scroll; scope != null; scope = scope.Parent)
                    if (scope.Viewport.width <= 0f || !scope.Viewport.Contains(position)) { visible = false; break; }
                if (visible) { hit = control; break; }
            }
            if (hit == null) { _vrPointerSlider = null; _vrSnapKey = null; return false; }
            _vrFocus = hit.Key; _vrEnsureVisible = false;
            if (hit.Slider && held)
            {
                _vrPointerSlider = hit.Key;
                _vrPointerRatio = Mathf.Clamp01((position.x - hit.Rect.x) / Mathf.Max(1f, hit.Rect.width));
            }
            else
            {
                _vrPointerSlider = null;
                if (pressed) _vrPress = hit.Key;
            }
            if (hit.Scroll != null && Mathf.Abs(scroll) > 0.2f)
            {
                Vector2 target = hit.Scroll.Position;
                target.y = Mathf.Max(0f, target.y - scroll * Time.unscaledDeltaTime * 450f);
                _vrScrollRequests[hit.Scroll.Key] = target;
            }
            return true;
        }
        string _vrSlideScroll, _vrSlidePress;
        Vector2 _vrSlideStart, _vrSlideLast;
        float _vrSlideTime;
        bool _vrSliding;
        string _vrInertiaScroll;
        float _vrSlideVelocity, _vrInertiaVelocity, _vrSlideSampleTime, _vrInertiaExpected;
        int _vrInertiaFrame = -1;
        bool _vrInertiaHasExpected;
        internal bool UpdateVrSlide(Vector2 raw, bool pressed, bool held)
        {
            if (pressed)
            {
                _vrInertiaScroll = null; _vrSlideVelocity = 0f; _vrInertiaHasExpected = false;
                _vrSlideSampleTime = Time.unscaledTime;
                _vrSlideScroll = _vrSlidePress = null; _vrSliding = false;
                for (int i = _vrControls.Count - 1; i >= 0; i--)
                {
                    var control = _vrControls[i];
                    if (!control.Rect.Contains(raw)) continue;
                    bool visible = true;
                    for (var scope = control.Scroll; scope != null; scope = scope.Parent)
                        if (!scope.Viewport.Contains(raw)) visible = false;
                    if (!visible) continue;
                    if (control.Slider) return false;
                    _vrSlideScroll = control.Scroll?.Key; _vrSlidePress = control.Key;
                    _vrSlideStart = _vrSlideLast = raw; _vrSlideTime = Time.unscaledTime;
                    break;
                }
                if(_vrSlideScroll==null&&_vrSlidePress==null)
                {
                    var scope=ScrollAt(raw);
                    if(scope!=null)
                    {
                        _vrSlideScroll=scope.Key;
                        _vrSlideStart=_vrSlideLast=raw;_vrSlideTime=Time.unscaledTime;
                    }
                }
            }
            if (_vrSlideScroll == null && _vrSlidePress == null) return false;
            if (!held)
            {
                bool shortPress = !_vrSliding && Time.unscaledTime - _vrSlideTime < 0.2f;
                if (shortPress) _vrPress = _vrSlidePress;
                else if (_vrSliding && _vrSlideScroll != null && Time.unscaledTime - _vrSlideSampleTime < 0.1f && Mathf.Abs(_vrSlideVelocity) > 15f)
                {
                    _vrInertiaScroll = _vrSlideScroll; _vrInertiaVelocity = _vrSlideVelocity;
                    _vrInertiaFrame = -1; _vrInertiaHasExpected = false;
                }
                _vrSlideScroll = _vrSlidePress = null; _vrSliding = false;
                return true;
            }
            if (!_vrSliding && (Time.unscaledTime - _vrSlideTime >= 0.2f ||
                (_vrSlideScroll != null && Mathf.Abs(raw.y - _vrSlideStart.y) >= 12f * UI.Scale)))
            {
                _vrSliding = true; _vrSnapKey = null; _vrPointerSlider = null;
            }
            if (_vrSliding && _vrSlideScroll != null)
            {
                VrScroll current = null;
                current=_vrScrollViews.Find(scope=>scope.Key==_vrSlideScroll);
                if (current == null) { _vrSlideScroll = _vrSlidePress = null; return true; }
                Vector2 value = _vrScrollRequests.TryGetValue(current.Key, out var pending) ? pending : current.Position;
                float dt = Mathf.Max(0.001f, Time.unscaledTime - _vrSlideSampleTime);
                float delta = -(raw.y - _vrSlideLast.y);
                _vrSlideVelocity = Mathf.Lerp(_vrSlideVelocity, Mathf.Clamp(delta / dt, -2200f, 2200f),
                    1f - Mathf.Exp(-dt / 0.04f));
                value.y = Mathf.Max(0f, value.y + delta);
                _vrScrollRequests[current.Key] = value;
            }
            _vrSlideLast = raw; _vrSlideSampleTime = Time.unscaledTime;
            return true;
        }
        internal void CancelVrSlide()
        {
            _vrInertiaScroll = null; _vrInertiaVelocity = _vrSlideVelocity = 0f; _vrInertiaHasExpected = false;
            _vrSlideScroll = _vrSlidePress = null; _vrSliding = false;
        }

        internal bool IsVrDragArea(Vector2 position)
            => new Rect(_rect.x, _rect.y, _rect.width - 64f * UI.Scale, 58f * UI.Scale).Contains(position);
        internal void ResetVrFocus()
        {
            CancelVrInput();
            string tab = _nav == -1 ? "Core.tab_home" : _nav == -2 ? "Core.tab_modes" : "Core.vr_settings";
            _vrFocus = _nav + ":button::" + I18n.T(tab) + ":0";
            _vrControls.Clear();
            _vrScrollViews.Clear();
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
            Instance?.MarkPcButton(rect);
            if (style.normal.background != null)
            {
                int motionId = GUIUtility.GetControlID(FocusType.Passive);
                float hover = MenuStyle.Animate("common-button:" + motionId, rect.Contains(Event.current.mousePosition) ? 1f : 0f);
                UI.DrawGlass(rect, Color.Lerp(MenuStyle.Surface, MenuStyle.Raised, hover), Mathf.RoundToInt(12f * UI.Scale));
                style = new GUIStyle(style);
                MenuStyle.ClearBackground(style);
                // This wrapper paints a Surface background, so its text follows that palette.
                style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = Theme.Text;
                style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = style.onFocused.textColor = Theme.Text;
            }
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
            Instance?.MarkPcButton(rect);
            bool result = GUI.Toggle(rect, value, text);
            var ui = Instance;
            if (ui == null || !ui._vrInDraw) return result;
            string key = ui.RegisterVrControl(rect, "toggle:" + text);
            return ui.ConsumeVrPress(key) ? !value : result;
        }
        float AdjustVrSlider(Rect rect, string label, float value, float min, float max, bool integer)
        {
            string key = RegisterVrControl(rect, "slider:" + label, true);
            if (VrCanSelect && GUI.enabled && Event.current.type == EventType.Layout && key == _vrPointerSlider)
            {
                value = Mathf.Lerp(min, max, _vrPointerRatio);
                if (integer) value = Mathf.Round(value);
                GUI.changed = true;
            }
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
        public static string ControlTextField(Rect rect, string value, GUIStyle style, bool drawBackground = true)
        {
            Instance?.IgnorePcGesture(rect);
            if (drawBackground) UI.DrawGlass(rect, MenuStyle.Input, Mathf.RoundToInt(10f * UI.Scale));
            style = new GUIStyle(style);
            MenuStyle.ClearBackground(style);
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
            if (key == _vrInertiaScroll && _vrInertiaFrame != Time.frameCount)
            {
                _vrInertiaFrame = Time.frameCount;
                // Unity clamps scroll positions at both ends; stop rather than pushing against a boundary.
                if (_vrInertiaHasExpected && Mathf.Abs(value.y - _vrInertiaExpected) > 1f)
                    _vrInertiaScroll = null;
                else
                {
                    float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                    float decay = Mathf.Exp(-7f * dt);
                    value.y = Mathf.Max(0f, value.y + _vrInertiaVelocity * (1f - decay) / 7f);
                    _vrInertiaVelocity *= decay;
                    _vrInertiaExpected = value.y; _vrInertiaHasExpected = true;
                    if (Mathf.Abs(_vrInertiaVelocity) < 5f || value.y <= 0f) _vrInertiaScroll = null;
                }
            }
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
                _vrScrollViews.Add(scope);
            }
        }
        VrScroll ScrollAt(Vector2 point)
        {
            VrScroll best=null;int bestDepth=-1;
            foreach(var view in _vrScrollViews)
            {
                bool inside=true;int depth=0;
                for(var scope=view;scope!=null;scope=scope.Parent)
                {depth++;if(scope.Viewport.width<=0||scope.Viewport.height<=0||!scope.Viewport.Contains(point)){inside=false;break;}}
                if(inside&&depth>bestDepth){best=view;bestDepth=depth;}
            }
            return best;
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

    public partial class GorillaInterface
    {
        const float PointerRange = 3f;
        Rect _pointerCrop;
        bool _pointerLeft, _pointerHasHit, _pointerTouch, _pointerTrigger, _pointerSmoothed, _pointerDragging, _pointerPinned;
        Vector3 _pointerDragOffset;
        Vector2 _pointerPosition;
        Vector3 _pointerDirection;
        bool _pointerDirectionReady;
        LineRenderer _fingerRay, _fingerRing, _fingerDot;
        Material _fingerMaterial;

        void EnsureFingerPointer()
        {
            if (_fingerRay != null) return;
            var shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            if (shader == null) return;
            _fingerMaterial = new Material(shader);
            _fingerMaterial.renderQueue = 3000;
            _fingerMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
            _fingerMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            _fingerRay = CreateFingerLine("UltimateMenu_FingerRay", 2, false);
            _fingerRing = CreateFingerLine("UltimateMenu_FingerCursor", 24, true);
            _fingerDot = CreateFingerLine("UltimateMenu_AimPoint", 12, true);
            _fingerDot.sortingOrder = 1003;
        }

        LineRenderer CreateFingerLine(string name, int count, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _fingerMaterial; line.useWorldSpace = true;
            line.positionCount = count; line.loop = loop;
            line.sortingOrder = loop ? 1002 : 1001;
            line.startColor = line.endColor = Color.white;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        void UpdateFingerPointer(bool visible, ControllerInputPoller input)
        {
            bool trigger = input != null && (_pointerLeft ? input.leftControllerTriggerButton : input.rightControllerTriggerButton);
            bool pressed = trigger && !_pointerTrigger;
            _pointerTrigger = trigger;
            if (!trigger) MenuUI.Instance?.UpdateVrSlide(Vector2.zero, false, false);
            if (!visible || MenuInputBlocked(input) || FrozenCast || _castImage == null || _pointerCrop.width <= 0f || VRRig.LocalRig == null)
            {
                HideFingerPointer(); return;
            }
            EnsureFingerPointer();
            if (_fingerRay == null || _fingerRing == null) return;
            var rig = VRRig.LocalRig;
            var index = _pointerLeft ? rig.leftIndex : rig.rightIndex;
            var hand = _pointerLeft ? rig.leftHandTransform : rig.rightHandTransform;
            if (hand == null) { HideFingerPointer(); return; }
            // A rigid wrist pose: squeezing the trigger must not steer the laser.
            Vector3 direction = hand.up;
            Vector3 origin = hand.position;
            Vector3 touchOrigin = origin;
            if (index != null && index.fingerBone2 != null && index.fingerBone3 != null)
            {
                Vector3 finger = index.fingerBone3.position - index.fingerBone2.position;
                if (finger.sqrMagnitude > 0.000001f)
                {
                    touchOrigin = index.fingerBone3.position + finger * 0.6f;
                }
            }
            float smoothing = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
            if (_pointerDragging)
            {
                if (!trigger) _pointerDragging = false;
                else
                {
                    SetMenuPose(origin + _pointerDragOffset, _root.transform.rotation);
                    _targetPos = _root.transform.position; _targetRot = _root.transform.rotation;
                    _fingerRay.enabled = true; _fingerRing.enabled = false; _fingerDot.enabled = false;
                    _fingerRay.SetPosition(0, origin); _fingerRay.SetPosition(1, _root.transform.position);
                    return;
                }
            }
            // Smooth the aim direction, keeping the beam attached to the wrist.
            float aimBlend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.025f);
            _pointerDirection = _pointerDirectionReady
                ? Vector3.Slerp(_pointerDirection, direction, aimBlend).normalized : direction;
            _pointerDirectionReady = true;
            var panel = _castImage.rectTransform;
            var plane = new Plane(panel.forward, panel.position);
            var ray = new Ray(origin, _pointerDirection);
            bool intersects = plane.Raycast(ray, out float distance) && distance <= PointerRange;
            Vector3 point = intersects ? ray.GetPoint(distance) : origin + _pointerDirection * PointerRange;
            Vector3 local = panel.InverseTransformPoint(point);
            bool hit = intersects && panel.rect.Contains(new Vector2(local.x, local.y));

            // Direct fingertip contact uses the panel's viewer-facing side and the same controls.
            Vector3 normal = Vector3.Dot(panel.forward, _playerHead.position - panel.position) >= 0f ? panel.forward : -panel.forward;
            float frontDistance = Vector3.Dot(touchOrigin - panel.position, normal);
            Vector3 touchPoint = touchOrigin - normal * frontDistance;
            Vector3 touchLocal = panel.InverseTransformPoint(touchPoint);
            bool touch = !trigger && frontDistance >= -0.008f && frontDistance <= (_pointerTouch ? 0.04f : 0.025f) &&
                panel.rect.Contains(new Vector2(touchLocal.x, touchLocal.y));
            if (touch) { hit = true; point = touchPoint; local = touchLocal; }
            bool touchPressed = touch && !_pointerTouch;
            _pointerTouch = touch;
            _pointerHasHit = hit;

            _fingerRay.enabled = true; _fingerRing.enabled = hit; _fingerDot.enabled = hit;
            _fingerRay.startWidth = _fingerRay.endWidth = 0.0015f * _userScale;
            _fingerRing.startWidth = _fingerRing.endWidth = 0.0015f * _userScale;
            _fingerDot.startWidth = _fingerDot.endWidth = 0.0025f * _userScale;
            Vector3 rawPoint = point;
            if (hit)
            {
                Vector2 screen = MapPanelPoint(panel.rect, _pointerCrop, new Vector2(local.x, local.y));
                bool slideGesture = !touch && !_pointerDragging && MenuUI.Instance != null &&
                    MenuUI.Instance.UpdateVrSlide(screen, pressed, trigger);
                Vector2 target = !slideGesture && !touch && MenuUI.Instance != null ? MenuUI.Instance.AssistVrPointer(screen) : screen;
                _pointerPosition = !touch && _pointerSmoothed ? Vector2.Lerp(_pointerPosition, target, smoothing) : screen;
                _pointerSmoothed = true;
                // The aim dot is raw; only the selection ring eases towards the magnetic target.
                Vector2 selectionPosition = _pointerPosition;
                // Draw the cursor at the exact smoothed point used for selection.
                float cursorU = (selectionPosition.x - _pointerCrop.x) / _pointerCrop.width;
                float cursorV = 1f - (selectionPosition.y - _pointerCrop.y) / _pointerCrop.height;
                point = panel.TransformPoint(new Vector3(panel.rect.xMin + cursorU * panel.rect.width,
                    panel.rect.yMin + cursorV * panel.rect.height, 0f));
                float scroll = input != null ? (_pointerLeft ? input.leftControllerPrimary2DAxis.y : input.rightControllerPrimary2DAxis.y) : 0f;
                if (pressed && MenuUI.Instance != null && MenuUI.Instance.IsVrDragArea(selectionPosition))
                {
                    _openingActive = false;
                    _pointerDragging = _pointerPinned = true;
                    _pointerDragOffset = _root.transform.position - origin;
                }
                bool onControl = MenuUI.Instance != null && MenuUI.Instance.PointVr(selectionPosition,
                    !_pointerDragging && !slideGesture && (pressed || touchPressed),
                    !_pointerDragging && !slideGesture && (trigger || touch), slideGesture ? 0f : scroll);
                _fingerRing.startColor = _fingerRing.endColor = onControl ? Theme.Accent : Color.white;
                Vector3 center = point + normal * (0.008f * _userScale);
                for (int i = 0; i < 24; i++)
                {
                    float angle = i * Mathf.PI * 2f / 24f;
                    _fingerRing.SetPosition(i, center + (panel.right * Mathf.Cos(angle) + panel.up * Mathf.Sin(angle)) * 0.008f * _userScale);
                }
                Vector3 dotCenter = rawPoint + normal * (0.009f * _userScale);
                for (int i = 0; i < 12; i++)
                {
                    float angle = i * Mathf.PI * 2f / 12f;
                    _fingerDot.SetPosition(i, dotCenter + (panel.right * Mathf.Cos(angle) + panel.up * Mathf.Sin(angle)) * 0.001f * _userScale);
                }
            }
            else
            {
                _pointerSmoothed = false;
                MenuUI.Instance?.PointVr(new Vector2(-100000f, -100000f), false, false, 0f);
            }
            _fingerRay.SetPosition(0, origin);
            _fingerRay.SetPosition(1, hit ? rawPoint + normal * (0.009f * _userScale) : rawPoint);
        }

        void HideFingerPointer()
        {
            if (_fingerRay != null) _fingerRay.enabled = false;
            if (_fingerRing != null) _fingerRing.enabled = false;
            if (_fingerDot != null) _fingerDot.enabled = false;
            _pointerHasHit = _pointerTouch = _pointerSmoothed = _pointerDirectionReady = false;
            _pointerDragging = false;
            MenuUI.Instance?.CancelVrSlide();
            MenuUI.Instance?.PointVr(new Vector2(-100000f, -100000f), false, false, 0f);
        }

        void DestroyFingerPointer()
        {
            if (_fingerMaterial != null) Destroy(_fingerMaterial);
        }

        internal static Vector2 MapPanelPoint(Rect panel, Rect crop, Vector2 local)
        {
            return new Vector2(crop.x + (local.x - panel.xMin) / panel.width * crop.width,
                crop.y + (1f - (local.y - panel.yMin) / panel.height) * crop.height);
        }

        float _menuInputBlockedUntil;
        void SelectPointerHand(ControllerInputPoller input)
        {
            bool next = _pointerLeft;
            if (_menuHeldHand != null) next = !_menuHeldLeft;
            else if (input != null)
            {
                if (input.leftControllerTriggerButton && !input.rightControllerTriggerButton) next = true;
                else if (input.rightControllerTriggerButton && !input.leftControllerTriggerButton) next = false;
            }
            if (next == _pointerLeft) return;
            _pointerLeft = next;
            _pointerTrigger = false; _pointerTouch = false;
            _pointerSmoothed = _pointerDirectionReady = false;
            _pointerDragging = false;
            _menuInputBlockedUntil = 0f;
            MenuUI.Instance?.CancelVrInput();
        }

        bool MenuInputBlocked(ControllerInputPoller input)
        {
            var rig = VRRig.LocalRig;
            bool gripping = input != null
                ? (_pointerLeft ? input.leftControllerGripFloat : input.rightControllerGripFloat) >= 0.5f
                : rig != null && (_pointerLeft ? rig.leftMiddle.gripValue : rig.rightMiddle.gripValue) >= 0.5f;
            bool holdingWithPointer = _menuHeldHand != null && _menuHeldLeft == _pointerLeft;
            if (gripping || holdingWithPointer) _menuInputBlockedUntil = Time.unscaledTime + 0.15f;
            return gripping || holdingWithPointer || Time.unscaledTime < _menuInputBlockedUntil;
        }

        Rigidbody _menuBody;
        BoxCollider _menuCollider;
        Transform _menuHeldHand;
        bool _menuHeldLeft, _menuLeftGrip, _menuRightGrip;
        Vector3 _menuHandOffset, _menuPreviousPosition, _menuThrowVelocity, _menuThrowSpin;
        Quaternion _menuRotationOffset, _menuPreviousRotation;
        float _menuDropDeadline;
        float _menuFadeAlpha = 1f;
        RenderTexture _frozenMenuTexture;
        Texture _liveMenuTexture;
        internal bool FrozenCast => _menuDropDeadline > 0f && _frozenMenuTexture != null;

        void FreezeMenuImage()
        {
            if (_castImage == null || !(_castImage.texture is RenderTexture source)) return;
            try
            {
                _liveMenuTexture = source;
                _frozenMenuTexture = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "UltimateMenu drop snapshot", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, useMipMap = false, autoGenerateMips = false
                };
                if (!_frozenMenuTexture.Create()) { RestoreFrozenMenu(); return; }
                Graphics.Blit(source, _frozenMenuTexture);
                _castImage.texture = _frozenMenuTexture;
                _vrBackdrop.enabled = false;
            }
            catch (Exception e) { RestoreFrozenMenu(); Log.Err("VR menu snapshot", e); }
        }

        void RestoreFrozenMenu()
        {
            _menuFadeAlpha = 1f;
            if (_castImage != null)
            {
                if (_frozenMenuTexture != null && _castImage.texture == _frozenMenuTexture) _castImage.texture = _liveMenuTexture;
                _castImage.color = Color.white;
            }
            if (_vrBackdrop != null) _vrBackdrop.enabled = true;
            if (_frozenMenuTexture != null) { _frozenMenuTexture.Release(); Destroy(_frozenMenuTexture); }
            _frozenMenuTexture = null; _liveMenuTexture = null;
        }

        void SetupMenuBody()
        {
            // Same collision layer as movable objects in GorillaSpawners.
            _root.layer = 8;
            _menuCollider = _root.AddComponent<BoxCollider>();
            _menuCollider.size = new Vector3(PanelW, PanelW, 20f);
            _menuCollider.enabled = false;
            _menuBody = _root.AddComponent<Rigidbody>();
            _menuBody.mass = 0.2f; _menuBody.isKinematic = true; _menuBody.useGravity = false;
            _menuBody.interpolation = RigidbodyInterpolation.None;
            _menuBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        // Keep the physics pose in sync: interpolation can otherwise restore an old pose.
        void SetMenuPose(Vector3 position, Quaternion rotation)
        {
            _root.transform.SetPositionAndRotation(position, rotation);
            if (_menuBody != null && _menuBody.isKinematic)
            {
                _menuBody.position = position;
                _menuBody.rotation = rotation;
            }
        }

        void ResetMenuBody()
        {
            RestoreFrozenMenu();
            _menuHeldHand = null; _menuDropDeadline = 0f;
            if (_menuBody != null)
            {
                if (!_menuBody.isKinematic) { _menuBody.linearVelocity = Vector3.zero; _menuBody.angularVelocity = Vector3.zero; }
                _menuBody.interpolation = RigidbodyInterpolation.None;
                _menuBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                _menuBody.isKinematic = true; _menuBody.useGravity = false;
                SetMenuPose(_root.transform.position, _root.transform.rotation);
            }
            if (_menuCollider != null) _menuCollider.enabled = false;
        }

        void UpdateMenuGrab(bool visible, ControllerInputPoller input)
        {
            var rig = VRRig.LocalRig;
            bool left = input != null ? input.leftControllerGripFloat >= 0.5f : rig != null && rig.leftMiddle.gripValue >= 0.5f;
            bool right = input != null ? input.rightControllerGripFloat >= 0.5f : rig != null && rig.rightMiddle.gripValue >= 0.5f;
            bool leftPressed = left && !_menuLeftGrip, rightPressed = right && !_menuRightGrip;
            _menuLeftGrip = left; _menuRightGrip = right;
            if (!visible || _menuBody == null) return;
            if (_menuDropDeadline > 0f && Time.unscaledTime >= _menuDropDeadline) { CloseMenu(); return; }
            _menuFadeAlpha = _menuDropDeadline > 0f ? Mathf.Clamp01(_menuDropDeadline - Time.unscaledTime) : 1f;
            if (_castImage != null) _castImage.color = new Color(1f, 1f, 1f, _menuFadeAlpha);
            if (_menuHeldHand != null)
            {
                if (!(_menuHeldLeft ? left : right)) { DropMenu(); return; }
                _pointerDragging = false;
                Vector3 position = _menuHeldHand.TransformPoint(_menuHandOffset);
                Quaternion rotation = _menuHeldHand.rotation * _menuRotationOffset;
                float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
                _menuThrowVelocity = Vector3.ClampMagnitude((position - _menuPreviousPosition) / dt, 10f);
                Quaternion delta = rotation * Quaternion.Inverse(_menuPreviousRotation);
                if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                _menuThrowSpin = angle > 0.01f && axis.sqrMagnitude > 0.01f ? Vector3.ClampMagnitude(axis.normalized * angle * Mathf.Deg2Rad / dt, 15f) : Vector3.zero;
                SetMenuPose(position, rotation);
                _menuPreviousPosition = position; _menuPreviousRotation = rotation;
                return;
            }
            if (rig == null) return;
            if (leftPressed && MenuNearHand(rig.leftHandTransform)) GrabMenu(rig.leftHandTransform, true);
            else if (rightPressed && MenuNearHand(rig.rightHandTransform)) GrabMenu(rig.rightHandTransform, false);
        }

        bool MenuNearHand(Transform hand)
        {
            if (hand == null || _castImage == null) return false;
            var panel = _castImage.rectTransform;
            Vector3 local = panel.InverseTransformPoint(hand.position);
            Vector3 closest = panel.TransformPoint(new Vector3(Mathf.Clamp(local.x, panel.rect.xMin, panel.rect.xMax),
                Mathf.Clamp(local.y, panel.rect.yMin, panel.rect.yMax), 0f));
            return Vector3.Distance(hand.position, closest) <= 0.12f;
        }

        void GrabMenu(Transform hand, bool left)
        {
            _openingActive = false;
            ResetMenuBody(); _menuHeldHand = hand; _menuHeldLeft = left;
            _pointerPinned = true; _pointerDragging = false;
            _menuHandOffset = hand.InverseTransformPoint(_root.transform.position);
            _menuRotationOffset = Quaternion.Inverse(hand.rotation) * _root.transform.rotation;
            _menuPreviousPosition = _root.transform.position; _menuPreviousRotation = _root.transform.rotation;
            _menuThrowVelocity = _menuThrowSpin = Vector3.zero;
        }

        void DropMenu()
        {
            _menuHeldHand = null; _pointerPinned = true; _pointerDragging = false;
            _menuCollider.enabled = true;
            SetMenuPose(_root.transform.position, _root.transform.rotation);
            _menuBody.isKinematic = false; _menuBody.useGravity = true;
            _menuBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _menuBody.linearVelocity = _menuThrowVelocity; _menuBody.angularVelocity = _menuThrowSpin;
            _menuDropDeadline = Time.unscaledTime + 3f;
            FreezeMenuImage();
        }
    }
}

