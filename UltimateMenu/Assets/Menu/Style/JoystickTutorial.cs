using System.IO;
using UnityEngine;

namespace UltimateMenu
{
    public partial class MenuUI
    {
        // Images embarquées : Assets/Menu/Icons/{LC,RC,CJ}.png
        static Texture2D _tutLeftPad, _tutRightPad, _tutStickCap;
        static bool _tutImagesLoaded;

        static Texture2D LoadEmbeddedPng(string name)
        {
            try
            {
                using (var stream = typeof(MenuUI).Assembly.GetManifestResourceStream("UltimateMenu.Assets.Menu.Icons." + name + ".png"))
                {
                    if (stream == null) { Log.Info("[Tutorial] Missing embedded icon: " + name); return null; }
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                        if (!tex.LoadImage(buffer.ToArray())) return null;
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = FilterMode.Trilinear;
                        tex.hideFlags = HideFlags.HideAndDontSave;
                        return tex;
                    }
                }
            }
            catch (System.Exception e) { Log.Info("[Tutorial] Icon load failed (" + name + ") : " + e.Message); return null; }
        }

        static void EnsureTutorialImages()
        {
            if (_tutImagesLoaded) return;
            _tutImagesLoaded = true;
            _tutLeftPad = LoadEmbeddedPng("LC");
            _tutRightPad = LoadEmbeddedPng("RC");
            _tutStickCap = LoadEmbeddedPng("CJ");
        }

        // ───────────────────────── Réglages : test des sticks + vidéo ─────────────────────────
        public void DrawJoystickTutorial(Vector2 left, Vector2 right)
        {
            EnsureTutorialImages();
            float unit = UI.Scale;
            var interfaceMenu = GorillaInterface.Instance;
            bool useRight = interfaceMenu == null || !interfaceMenu.MenuLeft;

            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(262f * unit), GUILayout.ExpandWidth(true));
            float gap = 14f * unit;
            float leftWidth = row.width * .42f;
            Rect leftCol = new Rect(row.x, row.y, leftWidth, row.height);
            Rect rightCol = new Rect(row.x + leftWidth + gap, row.y, row.width - leftWidth - gap, row.height);

            DrawStickTest(leftCol, useRight, useRight ? right : left, unit, interfaceMenu);
            DrawOpenMenuVideo(rightCol, unit);
        }

        void DrawStickTest(Rect col, bool useRight, Vector2 stick, float unit, GorillaInterface interfaceMenu)
        {
            GUI.Label(new Rect(col.x, col.y, col.width, 24f * unit), I18n.T("VR.StickTest"), UI.Sty(Theme.Text, 10, true, TextAnchor.MiddleCenter));
            DrawPadWithStick(new Rect(col.x, col.y + 28f * unit, col.width, 150f * unit), useRight, stick, unit, .06f);
            DrawHandSelector(new Rect(col.x + 8f * unit, col.y + 184f * unit, col.width - 16f * unit, 34f * unit), useRight, unit, interfaceMenu);
            GUI.Label(new Rect(col.x, col.y + 224f * unit, col.width, 26f * unit),
                "Quest 3 · " + I18n.T(useRight ? "VR.Right" : "VR.Left"), UI.Sty(Theme.Text, 9, false, TextAnchor.MiddleCenter));
        }

        void DrawOpenMenuVideo(Rect col, float unit)
        {
            GUI.Label(new Rect(col.x, col.y, col.width, 24f * unit), I18n.T("VR.OpenGesture"), UI.Sty(Theme.Text, 10, true, TextAnchor.MiddleCenter));

            Rect frame = new Rect(col.x, col.y + 28f * unit, col.width, 192f * unit);
            UI.DrawGlass(frame, MenuStyle.Input, Mathf.RoundToInt(14f * unit));
            Rect inner = new Rect(frame.x + 5f * unit, frame.y + 5f * unit, frame.width - 10f * unit, frame.height - 10f * unit);
            TutorialVideos.Draw(inner, "OpenMenu");

            Rect close = new Rect(col.x + 8f * unit, col.y + 226f * unit, col.width - 16f * unit, 28f * unit);
            UI.DrawGlass(close, MenuStyle.Surface, Mathf.RoundToInt(14f * unit));
            GUI.Label(close, I18n.T("VR.CloseGesture"), UI.Sty(Theme.Text, 9, true, TextAnchor.MiddleCenter));
        }

        // ───────────────────────── Premier lancement : écran affiché à la place du menu ─────────────────────────
        // Dessiné dans la fenêtre du menu 3D, devant le joueur, jusqu'à ce que le geste d'ouverture soit réussi.
        internal void DrawFirstRunTutorial(GorillaInterface vr)
        {
            EnsureTutorialImages();
            float unit = UI.Scale;
            float margin = 22f * unit;
            Rect window = _rect;
            bool useRight = !vr.TutorialLeft;

            GUI.Label(new Rect(window.x + margin, window.y + 56f * unit, window.width - margin * 2f, 30f * unit),
                I18n.T("VR.TutorialWelcome"), UI.Sty(Theme.Text, 14, true, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(window.x + margin * 1.6f, window.y + 88f * unit, window.width - margin * 3.2f, 46f * unit),
                I18n.T("VR.TutorialHint"), UI.Sty(UI.SecondaryText, 10, false, TextAnchor.MiddleCenter));

            // Exemple : la manette utilisée, avec le stick qui montre le geste (bas puis haut)
            DrawPadWithStick(new Rect(window.x + margin, window.y + 140f * unit, window.width - margin * 2f, 296f * unit),
                useRight, DemoStick(), unit, .085f);

            // Étapes : s'allume selon ce que fait vraiment le stick
            int step = vr.TutorialStep;
            Rect steps = new Rect(window.x + margin * 2f, window.y + 450f * unit, window.width - margin * 4f, 42f * unit);
            float half = (steps.width - 12f * unit) * .5f;
            DrawStepPill(new Rect(steps.x, steps.y, half, steps.height), I18n.T("VR.TutorialDown"), step == 0, step > 0, unit);
            DrawStepPill(new Rect(steps.x + half + 12f * unit, steps.y, half, steps.height), I18n.T("VR.TutorialUp"), step == 1, false, unit);

            GUI.Label(new Rect(window.x + margin, window.y + 506f * unit, window.width - margin * 2f, 24f * unit),
                "Quest 3 · " + I18n.T(useRight ? "VR.Right" : "VR.Left"), UI.Sty(Theme.Text, 9, false, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(window.x + margin, window.yMax - 44f * unit, window.width - margin * 2f, 26f * unit),
                I18n.T("VR.TutorialSkip"), UI.Sty(UI.SecondaryText, 9, false, TextAnchor.MiddleCenter));
        }

        // Geste d'ouverture animé : bas, puis haut, puis retour au centre.
        static Vector2 DemoStick()
        {
            float t = Time.unscaledTime % 3.4f, y;
            if (t < .5f) y = 0f;
            else if (t < 1f) y = -Mathf.SmoothStep(0f, 1f, (t - .5f) / .5f);
            else if (t < 1.25f) y = -1f;
            else if (t < 1.9f) y = Mathf.Lerp(-1f, 1f, Mathf.SmoothStep(0f, 1f, (t - 1.25f) / .65f));
            else if (t < 2.2f) y = 1f;
            else if (t < 2.7f) y = Mathf.Lerp(1f, 0f, Mathf.SmoothStep(0f, 1f, (t - 2.2f) / .5f));
            else y = 0f;
            return new Vector2(0f, y);
        }

        static void DrawStepPill(Rect rect, string label, bool active, bool done, float unit)
        {
            Color fill = active ? Theme.Accent : done ? MenuStyle.Raised : MenuStyle.Surface;
            UI.DrawGlass(rect, fill, Mathf.RoundToInt(rect.height * .5f));
            Color text = active && !MenuStyle.Custom ? MenuStyle.OnAccent : Theme.Text;
            GUI.Label(rect, label, UI.Sty(text, 11, true, TextAnchor.MiddleCenter));
        }

        // ───────────────────────── Éléments communs ─────────────────────────
        // Manette (LC/RC) avec le capuchon CJ posé sur le trou du joystick et déplacé selon `stick`.
        void DrawPadWithStick(Rect imageArea, bool useRight, Vector2 stick, float unit, float travel)
        {
            Texture2D pad = useRight ? _tutRightPad : _tutLeftPad;
            if (pad == null)
            {
                DrawStick(imageArea.center, Mathf.Min(36f * unit, imageArea.width * .25f), stick);
                return;
            }
            float aspect = (float)pad.width / pad.height;
            float h = imageArea.height, w = h * aspect;
            if (w > imageArea.width) { w = imageArea.width; h = w / aspect; }
            Rect img = new Rect(imageArea.center.x - w * .5f, imageArea.y + (imageArea.height - h) * .5f, w, h);
            GUI.DrawTexture(img, pad, ScaleMode.StretchToFill, true);

            // Centre du trou mesuré sur les PNG 300x500 : LC = (99,95), RC = (209.5,95.5)
            Vector2 hole = new Vector2(img.x + img.width * (useRight ? .698f : .330f), img.y + img.height * .190f);
            float capSize = img.width * .243f;
            stick = Vector2.ClampMagnitude(stick, 1f);
            Vector2 cap = hole + new Vector2(stick.x, -stick.y) * (img.width * travel);
            if (_tutStickCap != null)
                GUI.DrawTexture(new Rect(cap.x - capSize * .5f, cap.y - capSize * .5f, capSize, capSize), _tutStickCap, ScaleMode.StretchToFill, true);
            else
                Disc(cap, capSize * .5f, Theme.Text);
        }

        void DrawHandSelector(Rect selector, bool useRight, float unit, GorillaInterface interfaceMenu)
        {
            UI.DrawGlass(selector, MenuStyle.Input, Mathf.RoundToInt(selector.height / 2f));
            float selected = MenuStyle.Animate("joystick-hand", useRight ? 1f : 0f);
            float tabWidth = (selector.width - 6f * unit) / 2f;
            UI.DrawGlass(new Rect(selector.x + 3f * unit + selected * tabWidth, selector.y + 3f * unit,
                tabWidth, selector.height - 6f * unit), MenuStyle.Custom ? MenuStyle.Raised : Theme.Accent, Mathf.RoundToInt(16f * unit));
            for (int i = 0; i < 2; i++)
            {
                string label = I18n.T(i == 0 ? "VR.Left" : "VR.Right");
                Rect tab = new Rect(selector.x + 3f * unit + i * tabWidth, selector.y, tabWidth, selector.height);
                Color text = (useRight == (i == 1)) && !MenuStyle.Custom ? MenuStyle.OnAccent : Theme.Text;
                GUI.Label(tab, label, UI.Sty(text, 9, true, TextAnchor.MiddleCenter));
                if (ControlButton(tab, new GUIContent("", label), GUIStyle.none)) interfaceMenu?.SetMenuLeft(i == 0);
            }
        }

        static void DrawStick(Vector2 center, float radius, Vector2 position)
        {
            position = Vector2.ClampMagnitude(position, 1f);
            Disc(center, radius, Theme.Dim);
            Disc(center, radius - 2f * UI.Scale, MenuStyle.Input);
            Vector2 marker = center + new Vector2(position.x, -position.y) * (radius * .6f);
            Disc(marker, radius * .34f, Theme.Text);
        }

        static void Disc(Vector2 center, float radius, Color color)
            => UI.DrawRounded(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), color, Mathf.CeilToInt(radius));
    }
}
