using UnityEngine;

namespace UltimateMenu
{
    public partial class MenuUI
    {
        bool _pcContentTracking, _pcContentDragging, _pcContentButton;
        Vector2 _pcContentStart, _pcContentLast;
        float _pcContentTime, _pcContentSample, _pcContentVelocity;

        internal void MarkPcButton(Rect rect)
        {
            if (!_vrInDraw && _pcContentTracking && Event.current.type == EventType.MouseDown && GUI.enabled && rect.Contains(Event.current.mousePosition))
                _pcContentButton = true;
        }
        internal void IgnorePcGesture(Rect rect)
        {
            if (!_vrInDraw && Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                _pcContentTracking = _pcContentDragging = false;
        }
        Vector2 HandlePcContentGesture(Vector2 scroll, Rect outerRect)
        {
            if (_vrInDraw) return scroll;
            var ev = Event.current;
            EventType inputType = ev.type;
            Vector2 mouse = GUIUtility.GUIToScreenPoint(ev.mousePosition);
            Rect viewport = new Rect(GUIUtility.GUIToScreenPoint(Vector2.zero), outerRect.size);
            if (inputType == EventType.MouseDown && ev.button == 0)
            {
                _pcContentTracking = !_pcBarDragging && viewport.Contains(mouse);
                _pcContentDragging = _pcContentButton = false;
                _pcContentStart = _pcContentLast = mouse;
                _pcContentTime = _pcContentSample = Time.unscaledTime;
                _pcContentVelocity = 0f;
            }
            if (!_pcContentTracking) return scroll;
            // A slider/text/color control owns its own drag; leave it alone.
            if (!_pcContentButton && GUIUtility.hotControl != 0)
            {
                _pcContentTracking = false;
                return scroll;
            }
            bool longPress = Time.unscaledTime - _pcContentTime >= 0.2f;
            bool moved = Vector2.Distance(mouse, _pcContentStart) >= 8f * UI.Scale;
            if (inputType == EventType.MouseDrag && (longPress || moved)) _pcContentDragging = true;
            if (inputType == EventType.MouseUp && (longPress || moved)) _pcContentDragging = true;
            if (_pcContentDragging && inputType == EventType.MouseDrag)
            {
                GUIUtility.hotControl = 0;
                float dt = Mathf.Max(0.001f, Time.unscaledTime - _pcContentSample);
                float delta = _pcContentLast.y - mouse.y;
                scroll.y = Mathf.Max(0f, scroll.y + delta);
                float speed = Mathf.Clamp(delta / dt, -4000f * UI.Scale, 4000f * UI.Scale);
                _pcContentVelocity = Mathf.Lerp(_pcContentVelocity, speed, 1f - Mathf.Exp(-dt / 0.04f));
                _pcScrollExpectedValid = false;
                ev.Use();
            }
            if (inputType == EventType.MouseDrag || inputType == EventType.MouseDown)
            {
                _pcContentLast = mouse; _pcContentSample = Time.unscaledTime;
            }
            if (inputType == EventType.MouseUp || inputType == EventType.Ignore)
            {
                if (_pcContentDragging)
                {
                    GUIUtility.hotControl = 0;
                    _pcScrollVelocity = inputType == EventType.MouseUp && Time.unscaledTime - _pcContentSample < 0.1f ? _pcContentVelocity : 0f;
                    _pcScrollExpectedValid = false;
                    ev.Use();
                }
                _pcContentTracking = _pcContentDragging = false;
            }
            return scroll;
        }
    }
}
