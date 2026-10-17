using System.Collections.Generic;
using UnityEngine;

namespace UltimateMenu
{
    internal static class MenuStyle
    {
        internal static bool FastInterface = MenuPrefs.GetInt("MUM_FastInterface", 1) == 1;
        internal const float Radius = 16f, ControlHeight = 36f, Gap = 8f;
        internal static bool Light => Theme.Current == ThemePreset.Clair;
        internal static bool Custom => Theme.Current == ThemePreset.Custom;
        internal static Color OnAccent => .2126f * Theme.Accent.r + .7152f * Theme.Accent.g + .0722f * Theme.Accent.b > .55f ? new Color(.06f,.06f,.06f) : Color.white;
        internal static Color Panel => Theme.BG;
        internal static Color Surface => Theme.BG2;
        internal static Color Raised => Theme.BG3;
        internal static Color Input => Color.Lerp(Theme.BG2, Light ? Color.white : Color.black, .18f);
        internal static Color ActiveSurface => Color.Lerp(Surface, Theme.Accent, .13f);

        sealed class Motion { public float value; public int frame = -1; }
        static readonly Dictionary<string, Motion> Motions = new Dictionary<string, Motion>();
        static readonly Dictionary<int, float> Pulses = new Dictionary<int, float>();
        internal static void Pulse(int id) { if (FastInterface) return; if (Pulses.Count > 512) Pulses.Clear(); Pulses[id] = Time.unscaledTime; }
        internal static float PressScale(int id)
        {
            if (FastInterface) return 1f;
            if (!Pulses.TryGetValue(id, out var started)) return 1f;
            float t = (Time.unscaledTime-started)/.20f;
            return t >= 1f ? 1f : 1f-.025f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(t));
        }
        internal static void ClearBackground(GUIStyle style)
        {
            style.normal.background = style.hover.background = style.active.background = style.focused.background = null;
            style.onNormal.background = style.onHover.background = style.onActive.background = style.onFocused.background = null;
        }
        internal static float Animate(string key, float target, float speed = 18f)
        {
            if (FastInterface) { Motions.Remove(key); return target; }
            if (!Motions.TryGetValue(key, out var motion))
            {
                if (Motions.Count > 1024) Motions.Clear();
                motion = new Motion { value = target }; Motions[key] = motion;
            }
            if (Event.current.type == EventType.Repaint && motion.frame != Time.frameCount)
            {
                motion.frame = Time.frameCount;
                motion.value = Mathf.Lerp(motion.value, target, 1f-Mathf.Exp(-speed*Mathf.Min(Time.unscaledDeltaTime,.05f)));
                if (Mathf.Abs(motion.value-target)<.001f) motion.value=target;
            }
            return motion.value;
        }
        static readonly Dictionary<int, Rect> Cards = new Dictionary<int, Rect>();
        static readonly Stack<int> CardStack = new Stack<int>();
        internal static void BeginCard(Color? fill = null)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            CardStack.Push(id);
            var layout = UI.CardRoundedS(fill ?? Surface, Mathf.RoundToInt(Radius*UI.Scale));
            if (Cards.TryGetValue(id,out var cached))
            {
                UI.DrawGlass(cached, fill ?? Surface, Mathf.RoundToInt(Radius*UI.Scale));
                layout.normal.background = null;
            }
            GUILayout.BeginVertical(layout);
        }
        internal static void EndCard()
        {
            GUILayout.EndVertical();
            if (CardStack.Count == 0) return;
            int id = CardStack.Pop();
            if (Event.current.type == EventType.Repaint)
            {
                if (Cards.Count > 512) Cards.Clear();
                Cards[id] = GUILayoutUtility.GetLastRect();
            }
        }
        internal static void ClearLayout() { Cards.Clear(); CardStack.Clear(); Motions.TryGetValue("navigation-tab", out var navigation); Motions.Clear(); if (navigation != null) Motions["navigation-tab"] = navigation; Pulses.Clear(); }
    }
}
