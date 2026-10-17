using System;
using System.Collections.Generic;
using UnityEngine;
namespace UltimateMenu
{
    public static class UI
    {
        private static Font _font;
        private static Font _boldFont;
        private static bool _fontLoadAttempted;
        private static AssetBundle _fontBundle;

        static void LoadEmbeddedFonts()
        {
            if (_fontLoadAttempted) return;
            _fontLoadAttempted = true;
            try
            {
                using (var stream = typeof(MenuUI).Assembly.GetManifestResourceStream("UltimateMenu.Assets.Menu.Fonts.ultimatemenu-fonts.bundle"))
                {
                    if (stream == null) throw new System.IO.FileNotFoundException("Embedded Inter bundle missing");
                    using (var buffer = new System.IO.MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        _fontBundle = AssetBundle.LoadFromMemory(buffer.ToArray());
                    }
                }
                if (_fontBundle == null) throw new InvalidOperationException("Cannot load embedded Inter bundle");
                _font = _fontBundle.LoadAsset<Font>("Inter-Regular");
                _boldFont = _fontBundle.LoadAsset<Font>("Inter-SemiBold");
                if (_font == null || _boldFont == null) throw new InvalidOperationException("Inter font assets missing");
                Log.Info("Menu font bundle loaded from DLL");
            }
            catch (Exception e) { _font = _boldFont = null; Log.Err("Inter fonts", e); }
        }
        public static readonly string[] FontFamilies = { "Inter", "Lora", "Caveat", "Permanent Marker", "Fredoka", "Bungee", "Bebas Neue", "Rubik", "Comfortaa" };
        static readonly string[] FontAssets = { "Inter", "Lora", "Caveat", "PermanentMarker", "Fredoka", "Bungee", "BebasNeue", "Rubik", "Comfortaa" };
        static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();
        public static int FontFamily
        {
            get { int index=Array.IndexOf(FontFamilies,MenuPrefs.GetString("MUM_FontFamily","Inter"));return index<0?0:index; }
            set { MenuPrefs.SetString("MUM_FontFamily",FontFamilies[Mathf.Clamp(value,0,FontFamilies.Length-1)]);MenuPrefs.Save(); }
        }
        public static Font FontForFamily(int family,bool bold)
        {
            LoadEmbeddedFonts();family=Mathf.Clamp(family,0,FontFamilies.Length-1);
            bool single=family==3||family==5||family==6;
            string name=FontAssets[family]+(bold&&!single?"-SemiBold":"-Regular");
            if(!FontCache.TryGetValue(name,out var font))
            {
                font=_fontBundle!=null?_fontBundle.LoadAsset<Font>(name):null;
                FontCache[name]=font;
            }
            if(font!=null)return font;
            if(bold&&_boldFont!=null)return _boldFont;
            if(_font!=null)return _font;
            try { _font=Font.CreateDynamicFontFromOSFont("Arial",14); }
            catch(Exception e){Log.Err("UI font fallback",e);}
            return _font!=null?_font:GUI.skin.font;
        }
        public static Font FontFor(bool bold) => FontForFamily(FontFamily,bold);
        public static Font BodyFont => FontFor(false);

        public static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply();
            t.filterMode = FilterMode.Point; return t;
        }

        public const float RefHeight = 540f;
        public static float Scale = 1f;

        public static float TextScale
        {
            get => Mathf.Clamp(MenuPrefs.GetFloat("MUM_TextScaleV2", 1f), .5f, 1.5f);
            set => MenuPrefs.SetFloat("MUM_TextScaleV2", Mathf.Clamp(value, .5f, 1.5f));
        }
        public static int Px(float refSize) => Mathf.Max(9, Mathf.RoundToInt(refSize * Scale * TextScale * 1.3f));

        public static Color SecondaryText => Color.Lerp(Theme.Dim, Theme.Text, 0.55f);

        public static GUIStyle Sty(Color c, float s = 10, bool bold = false,
                                   TextAnchor align = TextAnchor.MiddleLeft) =>
            new GUIStyle(GUI.skin.label)
            {
                fontSize = Px(s),
                fontStyle = FontStyle.Normal,
                alignment = align,
                wordWrap = true,
                font = FontFor(bold || Theme.BoldText),
                normal = { textColor = c == Theme.Dim ? SecondaryText : c }
            };

        public static GUIStyle BtnS(Texture2D bg, Color tc, float s = 10) =>
            new GUIStyle(GUI.skin.button)
            {
                fontSize = Px(s),
                fontStyle = FontStyle.Normal,
                font = FontFor(true),
                normal = { textColor = tc, background = bg },
                padding = new RectOffset(8, 8, 4, 4),
                wordWrap = true
            };

        public static GUIStyle CardS(Color bg) =>
            new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(bg) },
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 1, 1)
            };

        private static readonly Dictionary<(int w, int h, int r, int colorHash), Texture2D> _roundedCache
            = new Dictionary<(int, int, int, int), Texture2D>();

        public static Texture2D MakeRoundedTex(Color c, int w, int h, int r)
        {
            w = Mathf.Max(4, w); h = Mathf.Max(4, h); r = Mathf.Clamp(r, 0, Mathf.Min(w, h) / 2);
            int colorHash = c.GetHashCode();
            var key = (w, h, r, colorHash);
            if (_roundedCache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = new Color(c.r, c.g, c.b, c.a * CornerAlpha(x, y, w, h, r));

            tex.SetPixels(pixels);
            tex.Apply(false);
            _roundedCache[key] = tex;
            return tex;
        }

        static float CornerAlpha(int x, int y, int w, int h, int r)
        {
            if (r <= 0) return 1f;
            float px = x + 0.5f, py = y + 0.5f;
            float cx = Mathf.Clamp(px, r, w - r);
            float cy = Mathf.Clamp(py, r, h - r);
            float dx = px - cx, dy = py - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            return 1f - Mathf.Clamp01((dist - r + 2f) / 2f);
        }

        private static readonly Dictionary<int, GUIStyle> _roundedShapes = new Dictionary<int, GUIStyle>();

        public static void DrawShadow(Rect r, int radius = 20, float offsetY = 8f, float strength = 0.28f, float blur = 1.35f)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (MenuStyle.FastInterface) return;
            r.y += offsetY;
            DrawRounded(r, new Color(0f, 0f, 0f, strength * 0.55f), radius);
        }

        public static void DrawRounded(Rect r, Color c, int radius = 14)
        {
            if (Event.current.type != EventType.Repaint || r.width <= 0f || r.height <= 0f) return;
            if (MenuStyle.FastInterface)
            {
                Color tint = GUI.color;
                GUI.color = tint * c;
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = tint;
                return;
            }
            if (!MenuStyle.FastInterface && VectorGlassUI.Draw(r, c, radius, false)) return;
            radius = Mathf.Clamp(radius, 0, Mathf.Min(96, Mathf.FloorToInt(Mathf.Min(r.width, r.height) * 0.5f)));
            if (!_roundedShapes.TryGetValue(radius, out var shape))
            {
                int size = Mathf.Max(4, radius * 2 + 2);
                shape = new GUIStyle(GUIStyle.none)
                {
                    normal = { background = MakeRoundedTex(Color.white, size, size, radius) },
                    border = new RectOffset(radius, radius, radius, radius)
                };
                _roundedShapes[radius] = shape;
            }
            Color previous = GUI.color;
            GUI.color = previous * c;
            shape.Draw(r, GUIContent.none, false, false, false, false);
            GUI.color = previous;
        }

        public static void DrawRoundedBorder(Rect r, Color c, int radius = 14, float thickness = 1f)
        {
            DrawRounded(r, c, radius);
            Rect inner = new Rect(r.x + thickness, r.y + thickness, r.width - thickness * 2, r.height - thickness * 2);
            if (inner.width > 2 && inner.height > 2)
            { var prev = GUI.color; GUI.color = Color.clear; GUI.color = prev; }
        }

        public static void DrawGlass(Rect rect, Color fill, int radius)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (MenuStyle.FastInterface) { DrawRounded(rect, fill, radius); return; }
            if (VectorGlassUI.Draw(rect, fill, radius, true)) return;
            DrawOutline(rect, new Color(0.72f, 0.82f, 1f, 0.22f), radius, 1f);
            DrawRounded(rect, fill, radius);
            Rect inner = new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f);
            DrawRounded(inner, new Color(0.72f, 0.82f, 1f, 0.035f), Mathf.Max(0, radius - 2));
            // A clipped highlight follows the rounded top edge without changing hit rectangles.
            GUI.BeginGroup(new Rect(rect.x, rect.y, rect.width, Mathf.Min(5f, rect.height)));
            DrawRounded(new Rect(1f, 1f, rect.width - 2f, rect.height - 2f), new Color(0.82f, 0.90f, 1f, 0.11f), radius);
            GUI.EndGroup();
        }

        public static void DrawOutline(Rect r, Color outlineColor, int radius, float thickness)
        {
            if (MenuStyle.FastInterface) return;
            Rect outer = new Rect(r.x - thickness, r.y - thickness, r.width + thickness * 2f, r.height + thickness * 2f);
            DrawRounded(outer, outlineColor, Mathf.RoundToInt(radius + thickness));
        }

        public static GUIStyle CardRoundedS(Color bg, int radius = 14) =>
            new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeRoundedTex(bg, 80, 80, radius) },
                border = new RectOffset(radius, radius, radius, radius),
                padding = new RectOffset(14, 14, 12, 12),
                margin = new RectOffset(0, 0, 3, 3)
            };

        public static GUIStyle BtnRoundedS(Color bg, Color tc, float s = 10, int radius = 10) =>
            new GUIStyle(GUI.skin.button)
            {
                fontSize = Px(s),
                fontStyle = FontStyle.Normal,
                font = FontFor(true),
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = tc, background = MakeRoundedTex(bg, 80, 40, radius) },
                hover = { textColor = tc, background = MakeRoundedTex(bg, 80, 40, radius) },
                active = { textColor = tc, background = MakeRoundedTex(bg, 80, 40, radius) },
                border = new RectOffset(radius, radius, radius, radius),
                padding = new RectOffset(10, 10, 4, 4),
                margin = new RectOffset(0, 0, 0, 0),
                wordWrap = true
            };

        private static GUIStyle _emptyScrollStyle;
        public static GUIStyle EmptyScrollStyle
        {
            get
            {
                if (_emptyScrollStyle == null)
                    _emptyScrollStyle = new GUIStyle(GUIStyle.none) { fixedWidth = 0, fixedHeight = 0 };
                return _emptyScrollStyle;
            }
        }

        public static void ClearRoundedCache()
        {
            foreach (var kv in _roundedCache) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            _roundedCache.Clear();
            _roundedShapes.Clear();
        }
    }


}
