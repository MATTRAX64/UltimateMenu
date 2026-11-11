using UnityEngine;

namespace UltimateMenu
{
    public partial class MenuUI
    {
        void DrawThemeChoices()
        {
            int selected = Theme.Current == ThemePreset.Clair ? 0 : 1;
            string[] labels = { I18n.T("Core.theme_light"), I18n.T("Core.theme_dark") };
            Rect bar = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(42f * UI.Scale), GUILayout.ExpandWidth(true));
            UI.DrawGlass(bar, MenuStyle.Input, Mathf.RoundToInt(bar.height / 2));
            float width = (bar.width - 6f * UI.Scale) / 2f;
            float position = MenuStyle.Animate("theme-selection", selected);
            Rect pill = new Rect(bar.x + 3f * UI.Scale + position * width, bar.y + 3f * UI.Scale, width, bar.height - 6f * UI.Scale);
            UI.DrawGlass(pill, MenuStyle.Custom ? MenuStyle.Raised : Theme.Accent, Mathf.RoundToInt(pill.height / 2));
            for (int i = 0; i < 2; i++)
            {
                Rect button = new Rect(bar.x + 3f * UI.Scale + i * width, pill.y, width, pill.height);
                GUI.Label(button, labels[i], UI.Sty(i == selected && !MenuStyle.Custom ? MenuStyle.OnAccent : Theme.Text, 10, i == selected, TextAnchor.MiddleCenter));
                if (ControlButton(button, new GUIContent("", labels[i]), GUIStyle.none))
                {
                    Theme.SetSimpleMode(i == 0);
                }
            }
            Space(10);
            Color color = Theme.Accent;
            bool changed = ColorPickerHSV(ref color, I18n.T("Core.color_accent"), compact: true);
            if (changed) Theme.SetSimpleColor(color);
        }

        float VerticalBrightness(Rect rect, float value, Color hue)
        {
            // Draw strips directly: no texture regeneration or camera effect.
            for (int i = 0; i < 32; i++)
            {
                Rect strip = new Rect(rect.x, rect.y + rect.height * i / 32f, rect.width, rect.height / 32f + 1f);
                GUI.DrawTexture(strip, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f,
                    Color.Lerp(hue, Color.black, i / 31f), 0f, 0f);
            }
            IgnorePcGesture(rect);
            float result = GUI.VerticalSlider(rect, value, 1f, 0f, GUIStyle.none, GUIStyle.none);
            if (_vrInDraw) for (int i = 0; i < 10; i++)
            {
                Rect target = new Rect(rect.x, rect.y + rect.height * i / 10f, rect.width, rect.height / 10f);
                if (ControlButton(target, "", GUIStyle.none)) result = 1f - i / 9f;
            }
            float y = rect.y + (1f - result) * rect.height;
            UI.DrawRounded(new Rect(rect.x - 3f * UI.Scale, y - 3f * UI.Scale, rect.width + 6f * UI.Scale, 6f * UI.Scale), Color.white, 3);
            return result;
        }
    }
}
