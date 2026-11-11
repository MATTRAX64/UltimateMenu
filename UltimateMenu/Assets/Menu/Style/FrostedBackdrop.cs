using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace UltimateMenu
{
    // Copy the desktop camera after URP effects and before IMGUI, so the panel never samples itself.
    internal static class FrostedBackdrop
    {
        static readonly FieldInfo Features = typeof(ScriptableRenderer).GetField("m_RendererFeatures", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly HashSet<List<ScriptableRendererFeature>> Lists = new HashSet<List<ScriptableRendererFeature>>();
        static GlassCaptureFeature _feature;
        static AssetBundle _bundle;
        static Material _material;
        static RTHandle _image;
        static bool _hooked, _requested, _failed, _flipY;
        static int _frame = -10;

        internal static void Request(bool enabled)
        {
            _requested = enabled && !_failed;
            if (!_requested) _frame = -10;
            if (!_requested || _hooked) return;
            RenderPipelineManager.beginCameraRendering += OnCamera;
            _hooked = true;
        }
        static void OnCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!_requested || !Eligible(camera)) return;
            try
            {
                if (Features == null || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
                if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data)) return;
                var list = Features.GetValue(data.scriptableRenderer) as List<ScriptableRendererFeature>;
                if (list == null || !Lists.Add(list)) return;
                if (_feature == null) _feature = ScriptableObject.CreateInstance<GlassCaptureFeature>();
                list.Add(_feature);
            }
            catch (Exception e) { _failed = true; _requested = false; Log.Err("Liquid Glass capture", e); }
        }
        static bool Eligible(Camera camera) => camera != null && camera.cameraType == CameraType.Game && camera.targetTexture == null
            && !camera.stereoEnabled && camera.targetDisplay == 0
            && camera.rect == new Rect(0, 0, 1, 1);

        internal sealed class GlassCaptureFeature : ScriptableRendererFeature
        {
            CapturePass _pass;
            public override void Create() => _pass = new CapturePass();
            public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
            {
                if (_requested && !renderingData.cameraData.xr.enabled && renderingData.cameraData.resolveFinalTarget && Eligible(renderingData.cameraData.camera)) renderer.EnqueuePass(_pass);
            }
        }
        sealed class CapturePass : ScriptableRenderPass
        {
            internal CapturePass() { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing; requiresIntermediateTexture = true; }
            static void Allocate(RenderTextureDescriptor desc)
            {
                desc.width = Mathf.Max(1, desc.width); desc.height = Mathf.Max(1, desc.height);
                desc.depthBufferBits = 0; desc.msaaSamples = 1; desc.useDynamicScale = false;
                RenderingUtils.ReAllocateHandleIfNeeded(ref _image, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "UltimateMenu scene behind glass");
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var data = frame.Get<UniversalResourceData>();
                if (data.isActiveTargetBackBuffer || !data.activeColorTexture.IsValid()) return;
                Allocate(frame.Get<UniversalCameraData>().cameraTargetDescriptor);
                graph.AddBlitPass(data.activeColorTexture, graph.ImportTexture(_image), Vector2.one, Vector2.zero, passName: "UltimateMenu glass scene copy");
                MarkFrame(frame.Get<UniversalCameraData>().camera);
            }
            [Obsolete("URP compatibility mode")]
            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                var source = data.cameraData.renderer.cameraColorTargetHandle;
                if (source == null || source.rt == null) return;
                Allocate(data.cameraData.cameraTargetDescriptor);
                var command = CommandBufferPool.Get("UltimateMenu glass scene copy");
                try { Blitter.BlitCameraTexture(command, source, _image, 0f, true); context.ExecuteCommandBuffer(command); MarkFrame(data.cameraData.camera); }
                finally { CommandBufferPool.Release(command); }
            }
        }
        // URP's final blit inverts Y for the DX backbuffer, but not for an offscreen RenderTexture.
        static void MarkFrame(Camera camera)
        {
            _frame = Time.frameCount;
            _flipY = camera.targetTexture == null && SystemInfo.graphicsUVStartsAtTop;
        }

        static bool LoadBundle()
        {
            if (_bundle != null) return true;
            using var stream = typeof(FrostedBackdrop).Assembly.GetManifestResourceStream("UltimateMenu.Assets.Menu.Shaders.liquidglass-frosted.bundle");
            if (stream == null) throw new FileNotFoundException("Liquid Glass backdrop bundle");
            using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            _bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
            return _bundle != null;
        }

        internal static Material MakeWorldMaterial()
        {
            try
            {
                var shader = LoadBundle() ? _bundle.LoadAsset<Shader>("FrostedWorldPanel") : null;
                if (shader != null && shader.isSupported) return new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3999 };
            }
            catch (Exception e) { Log.Err("World glass", e); }
            return null;
        }

        internal static bool Draw(Rect rect, Color fill, int radius)
        {
            if (!_requested || _image?.rt == null || Time.frameCount != _frame) return false;
            if (Event.current.type != EventType.Repaint) return true;
            if (_material == null)
            {
                try
                {
                    var shader = LoadBundle() ? _bundle.LoadAsset<Shader>("FrostedBackdrop") : null;
                    if (shader == null || !shader.isSupported) throw new InvalidOperationException("Liquid Glass backdrop shader unavailable");
                    _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                catch (Exception e) { _failed = true; _requested = false; Log.Err("Liquid Glass backdrop", e); return false; }
            }
            _material.SetTexture("_Scene", _image.rt);
            _material.SetVector("_Panel", new Vector4(rect.width, rect.height, radius, UI.Scale));
            _material.SetVector("_Screen", new Vector4(1f / Screen.width, 1f / Screen.height, _flipY ? 1f : 0f, 0f));
            _material.SetColor("_Tint", fill);
            _material.SetFloat("_Opacity", GUI.color.a);
            _material.SetFloat("_Clip", 1f);
            _material.SetFloat("_LightTheme", MenuStyle.Light ? 1f : 0f);
            Graphics.DrawTexture(rect, Texture2D.whiteTexture, new Rect(0, 0, 1, 1), 0, 0, 0, 0, Color.white, _material);
            return true;
        }
        internal static void Dispose()
        {
            _requested = false;
            if (_hooked) RenderPipelineManager.beginCameraRendering -= OnCamera;
            _hooked = false;
            foreach (var list in Lists) list.Remove(_feature);
            Lists.Clear();
            if (_feature != null) UnityEngine.Object.Destroy(_feature);
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_bundle != null) _bundle.Unload(false);
            _feature = null; _material = null; _bundle = null;
            _image?.Release(); _image = null; _frame = -10; _failed = false;
        }
    }
}
