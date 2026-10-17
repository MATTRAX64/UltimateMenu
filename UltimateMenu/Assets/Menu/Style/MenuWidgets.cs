using System;
using System.Collections.Generic;
using UnityEngine;
namespace UltimateMenu
{
    public partial class MenuUI
    {
        public void Section(string title)
        {
            if (_drawingPageTitle != null && title == I18n.T(_drawingPageTitle)) return;
            GUILayout.Space(14f * UI.Scale);
            GUILayout.Label(title.ToUpperInvariant(), new GUIStyle(GUI.skin.label)
            {
                fontSize = UI.Px(9),
                fontStyle = FontStyle.Normal,
                font = UI.FontFor(true),
                normal = { textColor = UI.SecondaryText },
                wordWrap = true
            });
            GUILayout.Space(4);
        }

        public void BeginCard(Color? bg = null) { MenuStyle.BeginCard(bg); }
        public void EndCard() { MenuStyle.EndCard(); GUILayout.Space(MenuStyle.Gap * UI.Scale); }

        public bool ExpandableHeader(string label, string valueText, bool isOpen, float labelWidth = 110f)
        {
            float rowH = 32f * UI.Scale;
            Rect headerRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(rowH));
            if (_nav == -4 && _settingsAnchor != null && label == I18n.T(_settingsAnchor) && Event.current.type == EventType.Repaint)
            {
                _scrollSettings.y = Mathf.Max(0f, headerRect.y - 12f * UI.Scale);
                _pcScrollVelocity = 0f;
                _pcScrollExpectedValid = false;
                _settingsAnchor = null;
            }

            bool hover = Event.current != null && headerRect.Contains(Event.current.mousePosition);
            float headerMotion = MenuStyle.Animate("header:" + label, hover || isOpen ? 1f : 0f);
            Color headerBg = Color.Lerp(MenuStyle.Surface, MenuStyle.Raised, headerMotion);
            UI.DrawGlass(headerRect, headerBg, Mathf.RoundToInt(RowRadius * UI.Scale * 0.8f));

            float pad = 12f * UI.Scale;
            var labelStyle = UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleLeft);
            var valueStyle = UI.Sty(Theme.Text, 10, true, TextAnchor.MiddleRight);

            Rect labelRect = new Rect(headerRect.x + pad, headerRect.y, labelWidth * UI.Scale, headerRect.height);
            GUI.Label(labelRect, label, labelStyle);

            Rect valueRect = new Rect(labelRect.x + labelRect.width, headerRect.y,
                headerRect.width - labelRect.width - pad * 2f, headerRect.height);
            GUI.Label(valueRect, valueText, valueStyle);

            bool clicked = ControlButton(headerRect, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound("Moyen");
            GUILayout.Space(2);
            return clicked ? !isOpen : isOpen;
        }

        public void BeginExpandedContent()
        {
            GUILayout.Space(4);
            MenuStyle.BeginCard(MenuStyle.Raised);
        }

        public void EndExpandedContent()
        {
            MenuStyle.EndCard();
            GUILayout.Space(4);
        }

        readonly Dictionary<string, float> _expandedHeights = new Dictionary<string, float>();
        readonly Dictionary<string, float> _expandedWidths = new Dictionary<string, float>();
        readonly Dictionary<string, int> _sectionVisits = new Dictionary<string, int>();
        int _sectionGeneration;
        public void ExpandableSection(string label, string valueText, ref bool isOpen, System.Action drawContent, float labelWidth = 110f)
        {
            string visitKey = _nav + ":" + label;
            if (!_sectionVisits.TryGetValue(visitKey, out int generation) || generation != _sectionGeneration)
            {
                isOpen = _nav == -4 && _settingsAnchor != null && label == I18n.T(_settingsAnchor);
                _sectionVisits[visitKey] = _sectionGeneration;
            }
            BeginCard();
            isOpen = ExpandableHeader(label, valueText, isOpen, labelWidth);
            string animation = "expand:" + label;
            if (!_expandedHeights.ContainsKey(label))
            {
                _expandedHeights[label] = 300f * UI.Scale;
                _expandedWidths[label] = Mathf.Max(100f, _rect.width - 100f * UI.Scale);
                MenuStyle.Animate(animation, 0f);
            }
            float progress = MenuStyle.Animate(animation, isOpen ? 1f : 0f, 18f);
            if (isOpen || progress > .001f)
            {
                float fullHeight = _expandedHeights[label];
                Rect viewport = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.Height(fullHeight * progress), GUILayout.ExpandWidth(true));
                if (!_expandedWidths.ContainsKey(label)) _expandedWidths[label] = Mathf.Max(100f, _rect.width - 100f * UI.Scale);
                // GetRect returns a dummy width during Layout. Never feed that into a nested area:
                // Unity would lay out every child at one pixel, then clip it during Repaint.
                float contentWidth = _expandedWidths[label];
                if (Event.current.type == EventType.Repaint && viewport.width > 10f)
                    _expandedWidths[label] = viewport.width;
                Color saved = GUI.color;
                bool enabled = GUI.enabled;
                GUI.BeginGroup(viewport);
                GUILayout.BeginArea(new Rect(0f, 0f, contentWidth, Mathf.Max(fullHeight, 2000f * UI.Scale)));
                try
                {
                    GUI.color = new Color(saved.r, saved.g, saved.b, saved.a * progress);
                    GUI.enabled = enabled && isOpen && progress > .8f;
                    GUILayout.BeginVertical(GUILayout.ExpandHeight(false));
                    BeginExpandedContent();
                    drawContent?.Invoke();
                    EndExpandedContent();
                    GUILayout.EndVertical();
                    if (Event.current.type == EventType.Repaint)
                        _expandedHeights[label] = GUILayoutUtility.GetLastRect().height;
                }
                finally
                {
                    GUILayout.EndArea(); GUI.EndGroup(); GUI.color = saved; GUI.enabled = enabled;
                }
            }
            EndCard();
        }

        public void Label(string text, Color? c = null, int size = 10, bool bold = false,
                          TextAnchor align = TextAnchor.MiddleLeft)
        {
            GUILayout.Label(text, UI.Sty(c ?? Theme.Text, size, bold, align));
        }

        public void BoldLabel(string text, Color? c = null, int size = 10)
            => Label(text, c, size, true);
        public void CenterLabel(string text, Color? c = null, int size = 10, bool bold = false)
            => Label(text, c, size, bold, TextAnchor.MiddleCenter);
        public void RightLabel(string text, Color? c = null, int size = 10)
            => Label(text, c, size, false, TextAnchor.MiddleRight);

        public void Space(float h = 8f) => GUILayout.Space(h);

        Color ResolveBg(Texture2D bg)
        {
            if (bg == null || bg == TA) return Theme.Accent;
            if (bg == TB3) return MenuStyle.Raised;
            if (bg == TB2) return MenuStyle.Surface;
            if (bg == TG) return Theme.Green;
            if (bg == TR) return Theme.Red;
            if (bg == TY) return Theme.Yellow;
            return Theme.Accent;
        }

        public bool Btn(string text, Texture2D bg = null, Color? tc = null, float w = 0, Font font = null)
        {
            Color baseCol = ResolveBg(bg);
            var baseStyle = UI.BtnRoundedS(baseCol, tc ?? Theme.Text, 10, Mathf.RoundToInt(10 * UI.Scale));
            if(font!=null)baseStyle.font=font;
            Rect probe;
            if (w > 0)
            {
                float height = baseStyle.CalcHeight(new GUIContent(text), w);
                probe = GUILayoutUtility.GetRect(w, Mathf.Max(height, 32f * UI.Scale));
            }
            else
            {
                probe = GUILayoutUtility.GetRect(new GUIContent(text), baseStyle, GUILayout.MinHeight(32f * UI.Scale));
            }
            bool hover = Event.current != null && probe.Contains(Event.current.mousePosition);
            int motionId = GUIUtility.GetControlID(FocusType.Passive);
            float hoverT = MenuStyle.Animate("button:" + motionId, hover ? 1f : 0f);
            Color drawCol = Color.Lerp(baseCol, Color.Lerp(baseCol, Theme.Text, .10f), hoverT);
            var s = UI.BtnRoundedS(drawCol, tc ?? Theme.Text, 10, Mathf.RoundToInt(10 * UI.Scale));
            if(font!=null)s.font=font;
            MenuStyle.ClearBackground(s);
            float pressScale = MenuStyle.PressScale(motionId);
            Rect visual = new Rect(probe.center.x - probe.width * pressScale / 2f, probe.center.y - probe.height * pressScale / 2f, probe.width * pressScale, probe.height * pressScale);
            UI.DrawGlass(visual, drawCol, Mathf.RoundToInt(12f * UI.Scale));
            bool r = ControlButton(probe, text, s);
            if (r) { MenuStyle.Pulse(motionId); SoundFX.PlaySound("Moyen"); }

            return r;
        }

        static Color Darken(Color c, float amount) => new Color(
            Mathf.Clamp01(c.r * (1f - amount)),
            Mathf.Clamp01(c.g * (1f - amount)),
            Mathf.Clamp01(c.b * (1f - amount)),
            c.a);

        public bool CircleBtn(string glyph, float diameter, Texture2D bg = null, Color? tc = null, float glyphSize = 12)
        {
            Color baseCol = ResolveBg(bg);
            Rect r = GUILayoutUtility.GetRect(diameter, diameter, GUILayout.Width(diameter), GUILayout.Height(diameter));
            bool hover = Event.current != null && r.Contains(Event.current.mousePosition);
            Color drawCol = hover ? Darken(baseCol, 0.14f) : baseCol;
            UI.DrawRounded(r, drawCol, Mathf.RoundToInt(diameter * 0.5f));
            GUI.Label(r, glyph, new GUIStyle(GUI.skin.label)
            {
                fontSize = UI.Px(glyphSize),
                fontStyle = FontStyle.Normal,
                font = UI.FontFor(true),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = tc ?? Theme.Text }
            });
            bool clicked = ControlButton(r, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound("Moyen");
            return clicked;
        }

        public bool SquareBtn(string glyph, float side, Texture2D bg = null, Color? tc = null, float glyphSize = 13)
        {
            Color baseCol = ResolveBg(bg);
            Rect r = GUILayoutUtility.GetRect(side, side, GUILayout.Width(side), GUILayout.Height(side));
            bool hover = Event.current != null && r.Contains(Event.current.mousePosition);
            Color drawCol = hover ? Darken(baseCol, 0.14f) : baseCol;
            UI.DrawRounded(r, drawCol, Mathf.RoundToInt(side * 0.32f));
            GUI.Label(r, glyph, new GUIStyle(GUI.skin.label)
            {
                fontSize = UI.Px(glyphSize),
                fontStyle = FontStyle.Normal,
                font = UI.FontFor(true),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = tc ?? Theme.Text }
            });
            bool clicked = ControlButton(r, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound("Moyen");
            return clicked;
        }

        public bool SmallBtn(string text, Texture2D bg = null, Color? tc = null)
        {
            var s = UI.BtnRoundedS(ResolveBg(bg), tc ?? Theme.Text, 9, Mathf.RoundToInt(7 * UI.Scale));
            s.padding = new RectOffset(4, 4, 2, 2);
            float width = Mathf.Max(30f * UI.Scale, s.CalcSize(new GUIContent(text)).x + 16f * UI.Scale);
            bool r = ControlLayoutButton(text, s, GUILayout.MinWidth(width), GUILayout.MinHeight(30f * UI.Scale));
            if (r) SoundFX.PlaySound("Moyen");
            return r;
        }

        public void DrawSwitchAt(Rect swRect, bool value, float alpha = 1f)
        {
            float swH = swRect.height;

            int motionId = GUIUtility.GetControlID(FocusType.Passive);
            float progress = MenuStyle.Animate("switch:" + motionId, value ? 1f : 0f, 16f);
            Color trackCol = Color.Lerp(MenuStyle.Raised, Theme.Accent, progress);
            trackCol.a *= alpha;
            bool hover = Event.current != null && swRect.Contains(Event.current.mousePosition);
            if (hover) trackCol = new Color(trackCol.r * 0.9f, trackCol.g * 0.9f, trackCol.b * 0.9f, trackCol.a);

            float trackBorder = Mathf.Max(1f, 1f * UI.Scale);
            Rect trackBorderRect = new Rect(
                swRect.x - trackBorder, swRect.y - trackBorder,
                swRect.width + trackBorder * 2f, swRect.height + trackBorder * 2f);
            Color trackBorderCol = new Color(1f, 1f, 1f, 0.14f * alpha);
            UI.DrawRounded(trackBorderRect, trackBorderCol, Mathf.RoundToInt(trackBorderRect.height * 0.5f));

            UI.DrawGlass(swRect, trackCol, Mathf.RoundToInt(swH * 0.5f));

            float knobD = swH - 4f * UI.Scale;
            float knobX = Mathf.Lerp(swRect.x + 2f * UI.Scale, swRect.xMax - knobD - 2f * UI.Scale, progress);
            Rect knobRect = new Rect(knobX, swRect.y + 2f * UI.Scale, knobD, knobD);

            float knobBorder = Mathf.Max(1f, 1.5f * UI.Scale);
            Rect knobBorderRect = new Rect(
                knobRect.x - knobBorder, knobRect.y - knobBorder,
                knobRect.width + knobBorder * 2f, knobRect.height + knobBorder * 2f);
            Color knobBorderCol = new Color(0f, 0f, 0f, 0.22f * alpha);
            UI.DrawRounded(knobBorderRect, knobBorderCol, Mathf.RoundToInt(knobBorderRect.height * 0.5f));

            Color knobCol = new Color(1f, 1f, 1f, alpha);
            UI.DrawRounded(knobRect, knobCol, Mathf.RoundToInt(knobD * 0.5f));
        }

        public bool Toggle(string label, bool value, float lw = 100f)
        {
            GUILayout.BeginHorizontal();
            var labelStyle = UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleLeft);
            labelStyle.padding = new RectOffset(0,0,0,0);
            float width = Mathf.Max(lw * UI.Scale, _rect.width * .55f);
            float rowHeight = Mathf.Max(30f * UI.Scale, labelStyle.CalcHeight(new GUIContent(label),width));
            GUILayout.Label(label, labelStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(rowHeight));
            float swW = 44 * UI.Scale, swH = 24 * UI.Scale;
            Rect cell = GUILayoutUtility.GetRect(swW, rowHeight, GUILayout.Width(swW), GUILayout.Height(rowHeight));
            Rect swRect = new Rect(cell.x,cell.center.y-swH*.5f,swW,swH);
            DrawSwitchAt(swRect, value, 1f);
            bool clicked = ControlButton(swRect, "", GUIStyle.none);
            if (clicked) SoundFX.PlaySound(value ? "Grave" : "Aigue");
            bool result = clicked ? !value : value;

            GUILayout.EndHorizontal();
            return result;
        }

        public bool ChoiceRow(string label,bool selected)
            =>ChoiceRow(label,selected,false,out _);
        public bool ChoiceRow(string label,bool selected,bool deletable,out bool remove)
        {
            Rect row=GUILayoutUtility.GetRect(GUIContent.none,GUIStyle.none,GUILayout.ExpandWidth(true),GUILayout.Height(Mathf.Max(44f*UI.Scale,UI.Px(10)+20f*UI.Scale)));
            UI.DrawGlass(row,selected?MenuStyle.ActiveSurface:MenuStyle.Surface,Mathf.RoundToInt(12*UI.Scale));
            var text=UI.Sty(Theme.Text,10,selected,TextAnchor.MiddleLeft);text.richText=false;
            GUI.Label(new Rect(row.x+12*UI.Scale,row.y,row.width-50*UI.Scale,row.height),label,text);
            remove=deletable&&IconButton(new Rect(row.xMax-40*UI.Scale,row.center.y-15*UI.Scale,30*UI.Scale,30*UI.Scale),7,I18n.T("Spawner.Delete"));
            if(!deletable&&selected)UI.DrawRounded(new Rect(row.xMax-27*UI.Scale,row.center.y-4*UI.Scale,8*UI.Scale,8*UI.Scale),Theme.Accent,4);
            GUILayout.Space(6*UI.Scale);
            return ControlButton(new Rect(row.x,row.y,row.width-(deletable?44*UI.Scale:0),row.height),new GUIContent("",label),GUIStyle.none);
        }

        public bool FavoriteRow(string label,out bool remove)
        {
            Rect row=GUILayoutUtility.GetRect(GUIContent.none,GUIStyle.none,GUILayout.ExpandWidth(true),GUILayout.Height(46*UI.Scale));
            UI.DrawGlass(row,MenuStyle.Surface,Mathf.RoundToInt(12*UI.Scale));
            GUI.Label(new Rect(row.x+14*UI.Scale,row.y,row.width-100*UI.Scale,row.height),label,UI.Sty(Theme.Text,11,true,TextAnchor.MiddleLeft));
            remove=IconButton(new Rect(row.xMax-42*UI.Scale,row.center.y-15*UI.Scale,30*UI.Scale,30*UI.Scale),7,I18n.T("Spawner.Delete"));
            bool join=IconButton(new Rect(row.xMax-78*UI.Scale,row.center.y-12*UI.Scale,24*UI.Scale,24*UI.Scale),3,I18n.T("Rooms.Join"));
            GUILayout.Space(7*UI.Scale);
            return ControlButton(new Rect(row.x,row.y,row.width-82*UI.Scale,row.height),new GUIContent("",label),GUIStyle.none)||join;
        }

        public bool SelectionRow(string label,bool selected)
        {
            float height=Mathf.Max(44f*UI.Scale,UI.Px(10)+20f*UI.Scale);
            Rect row=GUILayoutUtility.GetRect(GUIContent.none,GUIStyle.none,GUILayout.ExpandWidth(true),GUILayout.Height(height));
            UI.DrawGlass(row,selected?MenuStyle.ActiveSurface:MenuStyle.Surface,Mathf.RoundToInt(12f*UI.Scale));
            var style=UI.Sty(Theme.Text,10,false,TextAnchor.MiddleLeft);style.richText=false;
            GUI.Label(new Rect(row.x+12f*UI.Scale,row.y,row.width-80f*UI.Scale,row.height),label,style);
            DrawSwitchAt(new Rect(row.xMax-56f*UI.Scale,row.center.y-12f*UI.Scale,44f*UI.Scale,24f*UI.Scale),selected);
            bool click=ControlButton(row,new GUIContent("",label),GUIStyle.none);
            if(click)SoundFX.PlaySound(selected?"Grave":"Aigue");
            GUILayout.Space(6f*UI.Scale);
            return click?!selected:selected;
        }

        public bool RoundBtn(Color color, float diameter)
        {
            string label = color == Theme.Green ? "+" : color == Theme.Red ? "-" : ">";
            Rect rect = GUILayoutUtility.GetRect(diameter, diameter, GUILayout.Width(diameter), GUILayout.Height(diameter));
            int icon = color == Theme.Green ? 6 : color == Theme.Red ? 5 : 3;
            bool clicked = IconButton(rect, icon, label, icon == 3 ? 180f : 0f);
            if (clicked) SoundFX.PlaySound(color == Theme.Green ? "Aigue" : color == Theme.Red ? "Grave" : "Moyen");
            return clicked;
        }

        public static float StandardRoundBtnSize => 28f * UI.Scale;

        public bool PlusBtn(float diameter = -1f) => RoundBtn(Theme.Green, diameter > 0 ? diameter : StandardRoundBtnSize);

        public bool MinusBtn(float diameter = -1f) => RoundBtn(Theme.Red, diameter > 0 ? diameter : StandardRoundBtnSize);

        public bool ChevronBtn(float diameter = -1f)
        {
            float size = diameter > 0 ? diameter : StandardRoundBtnSize;
            Rect rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            bool clicked = IconButton(rect, 3, I18n.T("Core.vr_settings"), 180f);
            if (clicked) SoundFX.PlaySound("Moyen");
            return clicked;
        }

        public float Slider(string label, float val, float min, float max, string fmt = "0.0", float lw = 90f)
        {
            lw = Mathf.Max(lw, 150f);
            GUILayout.BeginHorizontal();
            var labelStyle = UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleLeft);
            var valueStyle = UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleRight);
            float rowH = Mathf.Max(28f * UI.Scale, labelStyle.CalcHeight(new GUIContent(label), lw * UI.Scale));
            GUILayout.Label(label, labelStyle, GUILayout.Width(lw * UI.Scale), GUILayout.Height(rowH));
            float value = DrawStyledSlider(val, min, max, label, false, rowH);
            float valueWidth = Mathf.Max(70f * UI.Scale, valueStyle.CalcSize(new GUIContent(value.ToString(fmt))).x);
            GUILayout.Label(value.ToString(fmt), valueStyle, GUILayout.Width(valueWidth), GUILayout.Height(rowH));
            GUILayout.EndHorizontal();
            return value;
        }

        public int IntSlider(string label, int val, int min, int max, float lw = 90f)
        {
            lw = Mathf.Max(lw, 150f);
            GUILayout.BeginHorizontal();
            var labelStyle = UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleLeft);
            float rowH = Mathf.Max(28f * UI.Scale, labelStyle.CalcHeight(new GUIContent(label), lw * UI.Scale));
            GUILayout.Label(label, labelStyle, GUILayout.Width(lw * UI.Scale), GUILayout.Height(rowH));
            int value = Mathf.RoundToInt(DrawStyledSlider(val, min, max, label, true, rowH));
            GUILayout.Label(value.ToString(), UI.Sty(Theme.Text, 10, false, TextAnchor.MiddleRight), GUILayout.Width(70 * UI.Scale), GUILayout.Height(rowH));
            GUILayout.EndHorizontal();
            return Mathf.RoundToInt(value);
        }

        float DrawStyledSlider(float val, float min, float max, string label, bool integer = false, float rowHeight = 0f)
        {
            float trackH = Mathf.Max(5f, 6f * UI.Scale);
            float knobD = Mathf.Max(16f, 18f * UI.Scale);

            int id = GUIUtility.GetControlID(FocusType.Passive);

            Rect full = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(Mathf.Max(knobD, rowHeight)));
            Rect track = new Rect(full.x + knobD * 0.5f, full.y + (full.height - trackH) * 0.5f, full.width - knobD, trackH);

            float t = max > min ? Mathf.Clamp01((val - min) / (max - min)) : 0f;

            Event ev = Event.current;
            EventType et = ev.GetTypeForControl(id);

            switch (et)
            {
                case EventType.MouseDown:
                    if (full.Contains(ev.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        float nt0 = Mathf.Clamp01((ev.mousePosition.x - track.x) / Mathf.Max(1f, track.width));
                        t = nt0;
                        val = Mathf.Lerp(min, max, t);
                        ev.Use();
                        GUI.changed = true;
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        float nt1 = Mathf.Clamp01((ev.mousePosition.x - track.x) / Mathf.Max(1f, track.width));
                        t = nt1;
                        val = Mathf.Lerp(min, max, t);
                        ev.Use();
                        GUI.changed = true;
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        ev.Use();
                        SoundFX.PlaySound("Moyen");
                    }
                    break;
            }

            float visualT = MenuStyle.Animate("slider:" + id, t, 24f);
            UI.DrawRounded(track, MenuStyle.Raised, Mathf.RoundToInt(trackH * 0.5f));
            if (visualT > 0.001f)
            {
                Rect fill = new Rect(track.x, track.y, track.width * visualT, track.height);
                UI.DrawRounded(fill, Theme.Accent, Mathf.RoundToInt(trackH * 0.5f));
            }
            float knobX = track.x + track.width * visualT - knobD * 0.5f;
            Rect knob = new Rect(knobX, full.y + (full.height - knobD) * 0.5f, knobD, knobD);
            UI.DrawShadow(knob, radius: Mathf.RoundToInt(knobD * 0.5f), offsetY: 1.5f, strength: 0.22f, blur: 1f);
            UI.DrawRounded(knob, Color.white, Mathf.RoundToInt(knobD * 0.5f));

            return AdjustVrSlider(full, label, val, min, max, integer);
        }

        public void StatusPill(string text, bool active)
            => Label((active ? "o " : "- ") + text, active ? Theme.Green : Theme.Dim, 11, true);

        public bool RebindBtn(string name, KeyCode current, float nw = 110f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, UI.Sty(Theme.Text, 10), GUILayout.Width(nw * UI.Scale));
            var s = UI.BtnRoundedS(Theme.BG3, Theme.Accent, 10, Mathf.RoundToInt(8 * UI.Scale));
            bool r = ControlLayoutButton(KeyLayout.Display(current), s, GUILayout.Width(130 * UI.Scale));
            if (r) SoundFX.PlaySound("Moyen");
            GUILayout.EndHorizontal();
            return r;
        }

        public string TextField(string val, float w = 0)
        {
            var s = new GUIStyle(GUI.skin.textField)
            {
                fontSize = UI.Px(10),
                font = UI.FontFor(Theme.BoldText),
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Theme.Text, background = UI.MakeRoundedTex(Theme.BG3, 64, 32, Mathf.RoundToInt(8 * UI.Scale)) },
                focused = { textColor = Theme.Text, background = UI.MakeRoundedTex(Theme.BG3, 64, 32, Mathf.RoundToInt(8 * UI.Scale)) },
                hover = { textColor = Theme.Text, background = UI.MakeRoundedTex(Theme.BG3, 64, 32, Mathf.RoundToInt(8 * UI.Scale)) },
                border = new RectOffset(Mathf.RoundToInt(8 * UI.Scale), Mathf.RoundToInt(8 * UI.Scale), Mathf.RoundToInt(8 * UI.Scale), Mathf.RoundToInt(8 * UI.Scale)),
                padding = new RectOffset(10, 10, 4, 4)
            };
            return w > 0 ? ControlLayoutTextField(val, s, GUILayout.Width(w * UI.Scale), GUILayout.MinHeight(32f * UI.Scale)) : ControlLayoutTextField(val, s, GUILayout.MinHeight(32f * UI.Scale));
        }

        public int ModePicker(string[] labels,int cur,int columns=4)
        {
            if(labels==null || labels.Length==0)return cur;
            columns=Mathf.Clamp(columns,1,labels.Length);cur=Mathf.Clamp(cur,-1,labels.Length-1);
            int id=GUIUtility.GetControlID(FocusType.Passive);
            int rows=Mathf.CeilToInt((float)labels.Length/columns);
            float cellHeight=Mathf.Max(40f*UI.Scale,UI.Px(10)+20f*UI.Scale);
            float pad=4f*UI.Scale,gap=4f*UI.Scale;
            Rect frame=GUILayoutUtility.GetRect(GUIContent.none,GUIStyle.none,GUILayout.ExpandWidth(true),GUILayout.Height(rows*cellHeight+(rows-1)*gap+pad*2f));
            UI.DrawGlass(frame,MenuStyle.Input,Mathf.RoundToInt(20f*UI.Scale));
            float width=(frame.width-pad*2f)/columns;
            if(cur>=0)
            {
                float x=MenuStyle.Animate("picker-x:"+id,cur%columns,20f);
                float y=MenuStyle.Animate("picker-y:"+id,cur/columns,20f);
                UI.DrawGlass(new Rect(frame.x+pad+width*x,frame.y+pad+(cellHeight+gap)*y,width,cellHeight),Theme.Accent,Mathf.RoundToInt(cellHeight*.5f));
            }
            int result=cur;
            for(int i=0;i<labels.Length;i++)
            {
                Rect button=new Rect(frame.x+pad+width*(i%columns),frame.y+pad+(cellHeight+gap)*(i/columns),width,cellHeight);
                var style=UI.Sty(i==cur?MenuStyle.OnAccent:Theme.Text,10,i==cur,TextAnchor.MiddleCenter);
                style.wordWrap=false;style.padding=new RectOffset(0,0,0,0);
                while(style.fontSize>10 && style.CalcSize(new GUIContent(labels[i])).x>button.width-12f*UI.Scale)style.fontSize--;
                GUI.Label(button,labels[i],style);
                if(ControlButton(button,new GUIContent("",labels[i]),GUIStyle.none)){result=i;SoundFX.PlaySound("Moyen");}
            }
            return result;
        }        public void ColorBar(Color c, float h = 18f)
        { GUI.color = c; GUILayout.Box("", GUILayout.Height(h), GUILayout.ExpandWidth(true)); GUI.color = Color.white; }

        public void Separator(float a = 0.2f)
        { GUI.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, a); GUILayout.Box("", GUILayout.Height(1), GUILayout.ExpandWidth(true)); GUI.color = Color.white; }

        public void InfoRow(string k, string v, float kw = 110f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(k, UI.Sty(Theme.Dim, 9), GUILayout.Width(kw));
            GUILayout.Label(v, UI.Sty(Theme.Text, 9));
            GUILayout.EndHorizontal();
        }


    }
}
