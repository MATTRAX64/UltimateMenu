using UnityEngine;

namespace UltimateMenu
{
    public partial class MenuUI
    {
        public void DrawJoystickTutorial(Vector2 left, Vector2 right)
        {
            float unit = UI.Scale;
            var interfaceMenu = GorillaInterface.Instance;
            bool _testRight = interfaceMenu == null || !interfaceMenu.MenuLeft;
            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(185f * unit), GUILayout.ExpandWidth(true));
            float half = row.width * .5f;
            GUI.Label(new Rect(row.x, row.y, half, 28f * unit), I18n.T("VR.StickTest"), UI.Sty(Theme.Text, 10, true, TextAnchor.MiddleCenter));
            float radius = Mathf.Min(36f * unit, half * .25f);
            Vector2 center = new Vector2(row.x + half * .5f, row.y + 77f * unit);
            DrawStick(center, radius, _testRight ? right : left);
            float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 3f);
            float ring = radius + (7f + 5f * pulse) * unit;
            Color halo = Theme.Text; halo.a = .2f + .25f * pulse;
            for (int i = 0; i < 96; i++)
            {
                float angle = i * Mathf.PI * 2f / 96f;
                Disc(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ring, 1.4f * unit, halo);
            }
            Rect selector = new Rect(row.x + 8f * unit, row.y + 130f * unit, half - 16f * unit, 35f * unit);
            UI.DrawGlass(selector, MenuStyle.Input, Mathf.RoundToInt(selector.height / 2f));
            float selected = MenuStyle.Animate("joystick-hand", _testRight ? 1f : 0f);
            float tabWidth = (selector.width - 6f * unit) / 2f;
            UI.DrawGlass(new Rect(selector.x + 3f * unit + selected * tabWidth, selector.y + 3f * unit,
                tabWidth, selector.height - 6f * unit), MenuStyle.Custom ? MenuStyle.Raised : Theme.Accent, Mathf.RoundToInt(16f * unit));
            for (int i = 0; i < 2; i++)
            {
                string label = I18n.T(i == 0 ? "VR.Left" : "VR.Right");
                Rect tab = new Rect(selector.x + 3f * unit + i * tabWidth, selector.y, tabWidth, selector.height);
                Color text = (_testRight == (i == 1)) && !MenuStyle.Custom ? MenuStyle.OnAccent : Theme.Text;
                GUI.Label(tab, label, UI.Sty(text, 9, true, TextAnchor.MiddleCenter));
                if (ControlButton(tab, new GUIContent("", label), GUIStyle.none)) interfaceMenu?.SetMenuLeft(i == 0);
            }

            Rect demo = new Rect(row.x + half + 12f * unit, row.y, half - 12f * unit, row.height);
            float cycle = Time.unscaledTime % 7f;
            bool opening = cycle < 3.5f;
            string action = I18n.T(opening ? "VR.OpenGesture" : "VR.CloseGesture");
            GUI.Label(new Rect(demo.x, demo.y, demo.width, 28f * unit), action, UI.Sty(Theme.Text, 10, true, TextAnchor.MiddleCenter));
            // Resolution-independent controller silhouette and pulse, drawn analytically.
            Vector2 top = new Vector2(demo.x + 37f * unit, demo.y + 71f * unit);
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-12f, top);
            UI.DrawRounded(new Rect(top.x - 19f * unit, top.y + 9f * unit, 31f * unit, 69f * unit),
                new Color(.78f,.81f,.85f), Mathf.RoundToInt(15f * unit));
            GUI.matrix = matrix;
            UI.DrawRounded(new Rect(top.x-32f*unit,top.y-25f*unit,64f*unit,50f*unit), new Color(.97f,.98f,1f),Mathf.RoundToInt(25f*unit));
            UI.DrawRounded(new Rect(top.x-28f*unit,top.y-21f*unit,56f*unit,42f*unit), new Color(.86f,.89f,.93f),Mathf.RoundToInt(21f*unit));
            float gestureTime = cycle % 3.5f;
            float direction = opening ? -1f : 1f;
            float y = 0f;
            if (gestureTime >= .55f && gestureTime < .72f) y = direction * Mathf.SmoothStep(0f, 1f, (gestureTime-.55f)/.17f);
            else if (gestureTime >= .72f && gestureTime < .95f) y = Mathf.Lerp(direction, -direction, Mathf.SmoothStep(0f,1f,(gestureTime-.72f)/.23f));
            else if (gestureTime >= .95f && gestureTime < 1.15f) y = -direction * (1f-Mathf.SmoothStep(0f,1f,(gestureTime-.95f)/.2f));
            DrawStick(top + new Vector2(-10f,-5f)*unit, 12f * unit, new Vector2(0f,y));
            Disc(top + new Vector2(15f,-5f)*unit,5f*unit,new Color(.15f,.17f,.2f));
            Disc(top + new Vector2(7f,12f)*unit,5f*unit,new Color(.15f,.17f,.2f));
            float show = Mathf.SmoothStep(0f,1f,Mathf.Clamp01((cycle-.95f)/.25f))
                * (1f-Mathf.SmoothStep(0f,1f,Mathf.Clamp01((cycle-4.45f)/.25f)));
            Color old = GUI.color;
            GUI.color = new Color(old.r,old.g,old.b,old.a*show);
            Rect menu = new Rect(demo.x + 75f * unit, demo.y + 44f * unit + (1f-show)*10f*unit,
                Mathf.Max(35f * unit,demo.width-80f*unit), 93f * unit);
            UI.DrawGlass(menu, MenuStyle.Input, Mathf.RoundToInt(12f * unit));
            GUI.Label(new Rect(menu.x,menu.y+5f*unit,menu.width,24f*unit), "Menu", UI.Sty(Theme.Text,9,true,TextAnchor.MiddleCenter));
            for (int i=0;i<3;i++) UI.DrawRounded(new Rect(menu.x+8f*unit,menu.y+(34f+i*15f)*unit,menu.width-16f*unit,8f*unit), MenuStyle.Surface, 4);
            GUI.color = old;
            GUI.Label(new Rect(demo.x,demo.yMax-28f*unit,demo.width,28f*unit), "Quest 3 · " + I18n.T(_testRight ? "VR.Right" : "VR.Left"), UI.Sty(Theme.Text,9,false,TextAnchor.MiddleCenter));
        }
        static void DrawStick(Vector2 center, float radius, Vector2 position)
        {
            position = Vector2.ClampMagnitude(position,1f);
            Disc(center,radius,Theme.Dim);
            Disc(center,radius-2f*UI.Scale,MenuStyle.Input);
            Vector2 marker = center + new Vector2(position.x,-position.y)*(radius*.6f);
            Disc(marker,radius*.34f,Theme.Text);
        }
        static void Disc(Vector2 center,float radius,Color color)
            => UI.DrawRounded(new Rect(center.x-radius,center.y-radius,radius*2f,radius*2f),color,Mathf.CeilToInt(radius));
    }
}
