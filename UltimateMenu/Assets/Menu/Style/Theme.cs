using System;
using System.Collections.Generic;
using UnityEngine;
namespace UltimateMenu
{
    public static class Theme
    {
        public static Color Accent = new Color(0.10f, 0.90f, 0.40f, 1f);
        public static Color BG = new Color(0.11f, 0.11f, 0.115f, 0.94f);
        public static Color BG2 = new Color(0.16f, 0.16f, 0.17f, 0.9f);
        public static Color BG3 = new Color(0.22f, 0.22f, 0.23f, 0.95f);
        public static Color Text = new Color(0.95f, 0.95f, 0.96f, 1f);
        public static Color Dim = new Color(0.58f, 0.58f, 0.60f, 1f);

        public static readonly Color Green = new Color(0.10f, 0.80f, 0.30f, 1f);
        public static readonly Color Red = new Color(0.85f, 0.15f, 0.15f, 1f);
        public static readonly Color Yellow = new Color(0.85f, 0.70f, 0.05f, 1f);

        public static ThemePreset Current
        {
            get => (ThemePreset)MenuPrefs.GetInt("MUM_ThemePreset", (int)ThemePreset.SombreVert);
            set { MenuPrefs.SetInt("MUM_ThemePreset", (int)value); MenuPrefs.Save(); }
        }

        public static float BgAlpha
        {
            get => MenuPrefs.GetFloat("MUM_BgAlpha", 0.97f);
            set { MenuPrefs.SetFloat("MUM_BgAlpha", Mathf.Clamp01(value)); MenuPrefs.Save(); BG.a = Mathf.Clamp01(value); }
        }

        public static bool BoldText
        {
            get => MenuPrefs.GetInt("MUM_BoldText", 1) == 1;
            set { MenuPrefs.SetInt("MUM_BoldText", value ? 1 : 0); MenuPrefs.Save(); }
        }

        struct Preset { public Color Accent, BG, BG2, BG3, Text, Dim; }

        static Preset Get(ThemePreset p)
        {
            switch (p)
            {
                case ThemePreset.Clair:
                    return new Preset
                    {
                        Accent = new Color(0.15f, 0.55f, 0.95f, 1f),
                        BG = new Color(0.95f, 0.95f, 0.95f, 1f),
                        BG2 = new Color(0.85f, 0.85f, 0.85f, 1f),
                        BG3 = new Color(0.78f, 0.78f, 0.78f, 1f),
                        Text = new Color(0.08f, 0.08f, 0.08f, 1f),
                        Dim = new Color(0.35f, 0.35f, 0.35f, 1f)
                    };
                case ThemePreset.SombreViolet:
                    return new Preset
                    {
                        Accent = new Color(0.62f, 0.30f, 0.95f, 1f),
                        BG = new Color(0.08f, 0.08f, 0.10f, 0.97f),
                        BG2 = new Color(0.13f, 0.12f, 0.16f, 1f),
                        BG3 = new Color(0.19f, 0.17f, 0.23f, 1f),
                        Text = new Color(0.92f, 0.92f, 0.94f, 1f),
                        Dim = new Color(0.52f, 0.50f, 0.56f, 1f)
                    };
                case ThemePreset.SombreBleu:
                    return new Preset
                    {
                        Accent = new Color(0.08f, 0.34f, 0.92f, 1f),
                        BG = new Color(0.13f, 0.14f, 0.19f, 0.88f),
                        BG2 = new Color(0.18f, 0.22f, 0.31f, 0.90f),
                        BG3 = new Color(0.23f, 0.28f, 0.38f, 0.95f),
                        Text = new Color(0.90f, 0.93f, 0.96f, 1f),
                        Dim = new Color(0.48f, 0.54f, 0.58f, 1f)
                    };
                default:
                    return new Preset
                    {
                        Accent = new Color(0.10f, 0.90f, 0.40f, 1f),
                        BG = new Color(0.08f, 0.08f, 0.08f, 0.97f),
                        BG2 = new Color(0.12f, 0.12f, 0.12f, 1f),
                        BG3 = new Color(0.18f, 0.18f, 0.18f, 1f),
                        Text = new Color(0.92f, 0.92f, 0.92f, 1f),
                        Dim = new Color(0.50f, 0.50f, 0.50f, 1f)
                    };
            }
        }

        public static void Apply(ThemePreset p)
        {
            Current = p;
            if (p != ThemePreset.Custom)
            {
                var v = Get(p);
                Accent = v.Accent; BG = v.BG; BG2 = v.BG2; BG3 = v.BG3; Text = v.Text; Dim = v.Dim;
                BG.a = BgAlpha;
                SaveCustom();
            }
            MenuUI.Instance?.RebuildTex();
        }

        public static void SetSimpleMode(bool light)
        {
            Color accent = Accent;
            Apply(light ? ThemePreset.Clair : ThemePreset.SombreBleu);
            Accent = accent;
            MenuPrefs.SetInt("MUM_SimpleTheme", 1);
            MenuPrefs.SetInt("MUM_LightMode", light ? 1 : 0);
            ApplySimplePalette();
            SaveCustom(); MenuUI.Instance?.RebuildTex();
        }

        public static void SetSimpleColor(Color color)
        {
            Accent = color;
            MenuPrefs.SetInt("MUM_SimpleTheme", 1);
            ApplySimplePalette(); SaveCustom(); MenuUI.Instance?.RebuildTex();
        }
        static void ApplySimplePalette()
        {
            bool light = Current == ThemePreset.Clair;
            Color baseColor = light ? Color.white : new Color(.045f,.055f,.075f);
            BG = Color.Lerp(baseColor, Accent, light ? .10f : .16f); BG.a = light ? .45f : .55f;
            BG2 = Color.Lerp(baseColor, Accent, light ? .18f : .24f); BG2.a = .94f;
            BG3 = Color.Lerp(baseColor, Accent, light ? .28f : .34f); BG3.a = .96f;
            Text = light ? new Color(.06f,.07f,.09f) : new Color(.96f,.97f,.99f);
            Dim = Color.Lerp(Text, BG2, .3f);
        }

        public static void SetAccent(float h, float s, float v)
        { Accent = Color.HSVToRGB(h, s, v); Current = ThemePreset.Custom; SaveCustom(); MenuUI.Instance?.RebuildTex(); }

        public static bool WhiteText => MenuPrefs.GetInt("MUM_CustomWhiteText", 1) == 1;
        public static void SetCustom(Color color, bool whiteText)
        {
            Current = ThemePreset.Custom;
            MenuPrefs.SetInt("MUM_SimpleTheme", 0);
            MenuPrefs.SetInt("MUM_CustomWhiteText", whiteText ? 1 : 0);
            Accent = color;
            ApplyCustomPalette(); SaveCustom(); MenuUI.Instance?.RebuildTex();
        }
        static void ApplyCustomPalette()
        {
            Text = WhiteText ? Color.white : new Color(.06f,.06f,.06f);
            BG = Accent; BG.a = .65f;
            BG2 = Accent; BG2.a = .88f;
            Color opposite = WhiteText ? Color.black : Color.white;
            BG3 = Color.Lerp(opposite, Accent, .18f); BG3.a = .94f;
            Dim = Color.Lerp(Text, Accent, .25f);
        }

        public static void SaveCustom()
        {
            MenuPrefs.SetString("MUM_CAccent", ColorUtility.ToHtmlStringRGB(Accent));
            MenuPrefs.SetString("MUM_CBG", ColorUtility.ToHtmlStringRGB(BG));
            MenuPrefs.SetString("MUM_CBG2", ColorUtility.ToHtmlStringRGB(BG2));
            MenuPrefs.SetString("MUM_CBG3", ColorUtility.ToHtmlStringRGB(BG3));
            MenuPrefs.SetString("MUM_CText", ColorUtility.ToHtmlStringRGB(Text));
            MenuPrefs.SetString("MUM_CDim", ColorUtility.ToHtmlStringRGB(Dim));
            MenuPrefs.Save();
        }

        public static void LoadSaved()
        {
            var p = Current;
            if (p == ThemePreset.Custom)
            {
                Accent = LoadColor("MUM_CAccent", Accent);
                SetSimpleMode(!WhiteText);
                return;
            }
            if (p == ThemePreset.Custom)
            {
                Accent = LoadColor("MUM_CAccent", Accent);
                BG = LoadColor("MUM_CBG", BG);
                BG2 = LoadColor("MUM_CBG2", BG2);
                BG3 = LoadColor("MUM_CBG3", BG3);
                Text = LoadColor("MUM_CText", Text);
                Dim = LoadColor("MUM_CDim", Dim);
                BG.a = BgAlpha;
            }
            else
            {
                var v = Get(p);
                Accent = v.Accent; BG = v.BG; BG2 = v.BG2; BG3 = v.BG3; Text = v.Text; Dim = v.Dim;
                BG.a = BgAlpha;
            }
            if (MenuPrefs.GetInt("MUM_SimpleTheme", 0) == 1) Accent = LoadColor("MUM_CAccent", Accent);
            ApplySimplePalette();
        }

        static Color LoadColor(string key, Color fallback)
        {
            string hex = MenuPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString("#" + hex, out Color c) ? c : fallback;
        }
    }


}
