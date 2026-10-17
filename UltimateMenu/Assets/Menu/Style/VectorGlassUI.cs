using System;
using System.IO;
using UnityEngine;

namespace UltimateMenu
{
    // Analytic rounded shapes: no bitmap background, camera capture or post-processing.
    internal static class VectorGlassUI
    {
        static AssetBundle _bundle;
        static Material _material;
        static bool _attempted, _failed;
        internal static bool Draw(Rect rect, Color color, float radius, bool glass)
        {
            if (Event.current.type != EventType.Repaint || rect.width <= 0 || rect.height <= 0) return true;
            if (!_attempted)
            {
                _attempted = true;
                try
                {
                    using (var stream = typeof(MenuUI).Assembly.GetManifestResourceStream("UltimateMenu.Assets.Menu.Shaders.liquidglass-ui.bundle"))
                    using (var data = new MemoryStream())
                    {
                        if (stream == null) throw new FileNotFoundException("UI shader bundle missing");
                        stream.CopyTo(data); _bundle = AssetBundle.LoadFromMemory(data.ToArray());
                    }
                    var shader = _bundle?.LoadAsset<Shader>("LiquidGlassUI");
                    if (shader == null || !shader.isSupported) throw new InvalidOperationException("UI shader unavailable");
                    _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                catch (Exception error) { _failed = true; Log.Err("Vector UI", error); }
            }
            if (_failed || _material == null) return false;
            float scale = Mathf.Max(0.5f, UI.Scale);
            float padding = glass ? 12f * scale : 2f;
            _material.SetVector("_Size", new Vector4(rect.width, rect.height, radius, padding));
            _material.SetVector("_Style", new Vector4(glass ? 1f : 0f, scale, 1f, 0f));
            _material.SetColor("_Fill", color * GUI.color);
            var expanded = new Rect(rect.x - padding, rect.y - padding, rect.width + 2 * padding, rect.height + 2 * padding);
            Graphics.DrawTexture(expanded, Texture2D.whiteTexture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, Color.white, _material);
            return true;
        }
        internal static void Dispose()
        {
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_bundle != null) _bundle.Unload(false);
            _material = null; _bundle = null; _attempted = _failed = false;
        }
    }
}
