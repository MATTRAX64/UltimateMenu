using UnityEngine;

namespace UltimateMenu
{
    public class Notif : MonoBehaviour
    {
        const float Duration = 5f, FadeIn = .28f, FadeOut = .35f;
        float _elapsed;
        bool _hasRendered;

        void Update()
        {
            // Loading the game must not consume the notification's visible lifetime.
            if (!_hasRendered) return;
            _elapsed += Mathf.Min(Time.unscaledDeltaTime, .05f);
            if (_elapsed >= Duration) Destroy(this);
        }

        void OnGUI()
        {
            if (_elapsed >= Duration) return;
            if (Screen.width < 100 || Screen.height < 100) return;
            if (!_hasRendered && Event.current.type != EventType.Repaint) return;
            if (!_hasRendered)
            {
                _hasRendered = true; _elapsed = .05f;
                Log.Info("Startup notification first screen draw");
            }
            float enter = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_elapsed / FadeIn));
            float exit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_elapsed - Duration + FadeOut) / FadeOut));
            float opacity = enter * (1f - exit);
            float unit = Mathf.Clamp(Screen.height / 900f, .7f, 1.6f);
            float width = Mathf.Min(320f * unit, Screen.width - 32f * unit);
            Rect panel = new Rect(20f * unit, (20f - 10f * (1f - enter) - 8f * exit) * unit,
                width, 76f * unit);
            Color previousColor = GUI.color;
            Color previousContent = GUI.contentColor;
            Matrix4x4 previousMatrix = GUI.matrix;
            int previousDepth = GUI.depth;
            float previousScale = UI.Scale;
            try
            {
                UI.Scale = unit;
                GUI.depth = -100;
                GUI.matrix = Matrix4x4.identity;
                GUI.contentColor = Color.white;
                GUI.color = new Color(1f, 1f, 1f, opacity);
                UI.DrawGlass(panel, MenuStyle.Panel, Mathf.RoundToInt(20f * unit));
                UI.DrawRounded(new Rect(panel.x + 15f * unit, panel.y + 23f * unit, 4f * unit, 30f * unit),
                    Theme.Accent, Mathf.RoundToInt(2f * unit));
                GUI.Label(new Rect(panel.x + 30f * unit, panel.y + 12f * unit, width - 45f * unit, 27f * unit),
                    (Log.PluginName ?? "UltimateMenu") + "  v" + (Log.PluginVersion ?? "?"), UI.Sty(Theme.Text, 15f, true));
                GUI.Label(new Rect(panel.x + 30f * unit, panel.y + 40f * unit, width - 45f * unit, 23f * unit),
                    I18n.T("Core.open_notif") + " [" + KeyLayout.Display(Keys.GetKey("KeyMenu")) + "]",
                    UI.Sty(UI.SecondaryText, 11f));
            }
            finally
            {
                GUI.color = previousColor; GUI.contentColor = previousContent;
                GUI.matrix = previousMatrix; GUI.depth = previousDepth; UI.Scale = previousScale;
            }
            if (opacity > .5f && Event.current.type == EventType.MouseDown && Event.current.button == 0
                && panel.Contains(Event.current.mousePosition))
            {
                MenuUI.Instance?.ToggleVisible();
                _elapsed = Mathf.Max(_elapsed, Duration - FadeOut);
                Event.current.Use();
            }
        }
    }
}
