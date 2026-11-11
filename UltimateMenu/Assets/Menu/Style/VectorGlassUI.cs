using System;
using System.IO;
using UnityEngine;

namespace UltimateMenu
{
    // Analytic rounded shapes: no bitmap background, camera capture or post-processing.
    internal static class VectorGlassUI
    {
        static AssetBundle _bundle;
        static Material _material, _polished;
        static AssetBundle _polishedBundle;
        static bool _polishedAttempted;
        static bool _attempted, _failed;
        internal static bool Draw(Rect rect, Color color, float radius, bool glass, bool panel = false)
        {
            if (Event.current.type != EventType.Repaint || rect.width <= 0 || rect.height <= 0) return true;
            if (!_attempted)
            {
                _attempted = true;
                try
                {
                    using (var stream = EmbeddedAssets.OpenPacked("UltimateMenu.Assets.Menu.Shaders.liquidglass-ui.bundle"))
                    using (var data = new MemoryStream())
                    {
                        if (stream == null) throw new FileNotFoundException("UI shader bundle missing");
                        stream.CopyTo(data); if (_bundle == null) _bundle = AssetBundle.LoadFromMemory(data.ToArray());
                    }
                    var shader = _bundle?.LoadAsset<Shader>("LiquidGlassUI");
                    if (shader == null || !shader.isSupported) throw new InvalidOperationException("UI shader unavailable");
                    _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                catch (Exception error) { _failed = true; Log.Err("Vector UI", error); }
            }
            if (_failed || _material == null) return false;
            var material = glass && !MenuStyle.LegacyGlass ? Polished() ?? _material : _material;
            float scale = Mathf.Max(0.5f, UI.Scale);
            float padding = glass ? 12f * scale : 2f;
            material.SetVector("_Size", new Vector4(rect.width, rect.height, radius, padding));
            material.SetVector("_Style", new Vector4(glass ? (panel && material == _polished ? 2f : 1f) : 0f, scale, 1f, MenuStyle.Light ? 1f : 0f));
            material.SetColor("_Fill", color * GUI.color);
            var expanded = new Rect(rect.x - padding, rect.y - padding, rect.width + 2 * padding, rect.height + 2 * padding);
            Graphics.DrawTexture(expanded, Texture2D.whiteTexture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, Color.white, material);
            return true;
        }
        static Material Polished()
        {
            if (_polishedAttempted) return _polished;
            _polishedAttempted = true;
            try
            {
                using var stream = typeof(MenuUI).Assembly.GetManifestResourceStream("UltimateMenu.Assets.Menu.Shaders.liquidglass-polished.bundle");
                if (stream == null) throw new FileNotFoundException("Polished glass bundle missing");
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                _polishedBundle = AssetBundle.LoadFromMemory(bytes.ToArray());
                var shader = _polishedBundle?.LoadAsset<Shader>("LiquidGlassPolished");
                if (shader != null && shader.isSupported) _polished = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            catch (Exception e) { Log.Err("Polished glass", e); }
            return _polished;
        }
        // Returns a shader of the bundle by its declared name, or null when the bundle (or that shader) is not available.
        internal static Shader FindShader(string declaredName)
        {
            try
            {
                if (_bundle == null)
                {
                    using (var stream = EmbeddedAssets.OpenPacked("UltimateMenu.Assets.Menu.Shaders.liquidglass-ui.bundle"))
                    using (var data = new MemoryStream())
                    {
                        if (stream == null) return null;
                        stream.CopyTo(data); _bundle = AssetBundle.LoadFromMemory(data.ToArray());
                    }
                }
                if (_bundle == null) return null;
                foreach (var shader in _bundle.LoadAllAssets<Shader>())
                    if (shader != null && shader.name == declaredName && shader.isSupported) return shader;
            }
            catch (Exception error) { Log.Err("Panel shader", error); }
            return null;
        }
        internal static void Dispose()
        {
            if (_polished != null) UnityEngine.Object.Destroy(_polished);
            if (_polishedBundle != null) _polishedBundle.Unload(false);
            _polished = null; _polishedBundle = null; _polishedAttempted = false;
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_bundle != null) _bundle.Unload(false);
            _material = null; _bundle = null; _attempted = _failed = false;
        }
    }
}

