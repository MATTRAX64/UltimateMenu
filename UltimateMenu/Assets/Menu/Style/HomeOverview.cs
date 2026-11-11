using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UltimateMenu
{
    public partial class MenuUI
    {
        [Serializable] sealed class SupporterList { public string[] supporters; }
        string[] _supporters;

        static string[] ReadSupporters()
        {
            try
            {
                using var source = typeof(MenuUI).Assembly.GetManifestResourceStream("UltimateMenu.Assets.Menu.Supporters.json");
                if (source == null || source.Length > 32768) return Array.Empty<string>();
                using var reader = new StreamReader(source);
                var parsed = JsonUtility.FromJson<SupporterList>(reader.ReadToEnd());
                if (parsed?.supporters == null) return Array.Empty<string>();
                return parsed.supporters.Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => new string(n.Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(64).ToArray()).Trim())
                    .Where(n => n.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
            }
            catch (IOException) { return Array.Empty<string>(); }
            catch (ArgumentException) { return Array.Empty<string>(); }
        }

        void DrawSupporters()
        {
            _supporters ??= ReadSupporters();
            if (_supporters.Length == 0) return;
            Section(I18n.T("Core.supporters"));
            BeginCard();
            foreach (var name in _supporters) Label(name, Theme.Text, 11, true);
            EndCard();
            Space(10);
        }

        void DrawHomeOverview(int total, int active)
        {
            float unit = UI.Scale;
            float height = Mathf.Max(82f * unit, UI.Px(20) + UI.Px(10) + 26f * unit);
            Rect row = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(height), GUILayout.ExpandWidth(true));
            float gap = 10f * unit;
            float donationWidth = Mathf.Clamp(row.width * .22f, 78f * unit, 115f * unit);
            float combinedWidth = row.width - donationWidth - gap;
            float statWidth = combinedWidth * .5f;
            UI.DrawGlass(new Rect(row.x,row.y,combinedWidth,height),MenuStyle.Surface,Mathf.RoundToInt(16f*unit));
            Color separator = Theme.Text; separator.a = .2f;
            UI.DrawRounded(new Rect(row.x+statWidth-unit*.5f,row.y+16f*unit,unit,height-32f*unit),separator,Mathf.RoundToInt(unit*.5f));
            for (int i = 0; i < 2; i++)
            {
                Rect card = new Rect(row.x + i * statWidth, row.y, statWidth, height);
                float badgeSize = Mathf.Min(40f * unit, card.width * .27f);
                Rect badge = new Rect(card.x + 12f * unit, card.center.y - badgeSize * .5f, badgeSize, badgeSize);
                Color background = Theme.Accent; background.a = .18f;
                UI.DrawRounded(badge, background, Mathf.CeilToInt(badgeSize * .5f));
                float glyph = badgeSize * .64f;
                Rect glyphRect = new Rect(badge.center.x-glyph*.5f,badge.center.y-glyph*.5f,glyph,glyph);
                if (_navigationIcons[15+i] != null) GUI.DrawTexture(glyphRect,_navigationIcons[15+i],ScaleMode.ScaleToFit);
                float x = badge.xMax + 12f * unit;
                float numberHeight = UI.Px(20) + 4f * unit;
                float captionHeight = UI.Px(10) + 6f * unit;
                float y = card.center.y - (numberHeight + captionHeight) * .5f;
                var numberStyle = UI.Sty(Theme.Text, 20, true, TextAnchor.MiddleLeft);
                var captionStyle = UI.Sty(UI.SecondaryText, 10, false, TextAnchor.MiddleLeft);
                numberStyle.wordWrap = captionStyle.wordWrap = false;
                GUI.Label(new Rect(x,y,card.xMax-x-10f*unit,numberHeight),(i == 0 ? total : active).ToString(),numberStyle);
                GUI.Label(new Rect(x,y+numberHeight,card.xMax-x-10f*unit,captionHeight),I18n.T(i == 0 ? "Home.Mods" : "Home.Active"),captionStyle);
            }
            Rect donate = new Rect(row.xMax-donationWidth,row.y,donationWidth,height);
            bool hover = donate.Contains(Event.current.mousePosition);
            DrawDonatePulse(donate, 16f * unit, unit);
            UI.DrawGlass(donate,hover ? MenuStyle.Raised : MenuStyle.Surface,Mathf.RoundToInt(16f*unit));
            float heart = Mathf.Min(38f*unit,height*.45f);
            DrawMenuIcon(new Rect(donate.center.x-heart*.5f,donate.y+11f*unit,heart,heart),4);
            GUI.Label(new Rect(donate.x,donate.yMax-UI.Px(9)*2-8f*unit,donate.width,UI.Px(9)*2+4f*unit),I18n.T("Core.donate_caption"),UI.Sty(Theme.Text,9,true,TextAnchor.MiddleCenter));
            if (ControlButton(donate,new GUIContent("",I18n.T("Core.glass_support")),GUIStyle.none)) Application.OpenURL(DonateUrl);
        }

        static void DrawDonatePulse(Rect rect, float radius, float scale)
        {
            if (Event.current.type != EventType.Repaint || MenuStyle.FastInterface) return;
            float cycle = Mathf.Repeat(Time.unscaledTime / 2.5f, 1f);
            float pulse = Mathf.Sin(cycle * Mathf.PI);
            float spread = Mathf.Lerp(2f, 10f, pulse) * scale;
            Color glow = Theme.Accent;

            const int layers = 8;
            for (int layer = layers; layer >= 1; layer--)
            {
                float position = layer / (float)layers;
                float edge = spread * position;
                glow.a = Theme.Accent.a * pulse * Mathf.Lerp(.055f, .012f, position);
                UI.DrawOutline(rect, glow, Mathf.RoundToInt(radius), edge);
            }
        }
    }
}
