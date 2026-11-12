using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UltimateMenu
{
    static class MenuEmoji
    {
        // Treat joined emoji, modifiers, regional flags and keycaps as complete glyph runs.
        const string Regional = @"\uD83C[\uDDE6-\uDDFF]";
        const string Glyph = @"(?:\u00A9\uFE0F|\u00AE\uFE0F|[\u203C-\u3299]\uFE0F|[\u231A-\u231B\u23E9-\u23EC\u23F0\u23F3\u25FD\u25FE\u2600-\u27BF]|\uD83C[\uDC04\uDCCF\uDD70-\uDDFF\uDF00-\uDFFA]|\uD83D[\uDC00-\uDEFF]|\uD83E[\uDD00-\uDEFF])\uFE0F?(?:\uD83C[\uDFFB-\uDFFF])?";
        internal static readonly Regex Sequences = new Regex(Regional + Regional + @"|[#*0-9]\uFE0F?\u20E3|" + Glyph + @"(?:\u200D" + Glyph + @")*(?:\uDB40[\uDC20-\uDC7E])+\uDB40\uDC7F|" + Glyph + @"(?:\u200D" + Glyph + ")*");
        static readonly Dictionary<string, Texture2D?> Cache = new Dictionary<string, Texture2D?>();

        internal static Texture2D? Get(string emoji)
        {
            if (Cache.TryGetValue(emoji, out var texture)) return texture;
            if (Cache.Count >= 256 || Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return null;
            try
            {
                byte[] pixels = WindowsEmoji.Render(emoji);
                texture = new Texture2D(WindowsEmoji.Size, WindowsEmoji.Size, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.LoadRawTextureData(pixels);
                texture.Apply(false, true);
            }
            catch (Exception e) when (e is ExternalException || e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                texture = null;
            }
            Cache.Add(emoji, texture);
            return texture;
        }

        internal static void Clear()
        {
            foreach (var texture in Cache.Values)
                if (texture != null) UnityEngine.Object.Destroy(texture);
            Cache.Clear();
        }
    }

    static class WindowsEmoji
    {
        internal const int Size = 80;
        static readonly Guid WicClass = new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a");
        static readonly Guid WicInterface = new Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
        static readonly Guid PixelFormat = new Guid("6fddc324-4e03-4bfe-b185-3d77768dc910");
        static readonly Guid D2dInterface = new Guid("06152247-6f50-465a-9245-118bfd3b6007");
        static readonly Guid WriteInterface = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

        [StructLayout(LayoutKind.Sequential)]
        struct TargetProperties
        {
            public int Type, Format, Alpha;
            public float DpiX, DpiY;
            public int Usage, FeatureLevel;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Bounds
        {
            public float Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Rgba
        {
            public float R, G, B, A;
        }

        [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint flags);
        [DllImport("ole32.dll")] static extern void CoUninitialize();
        [DllImport("ole32.dll")] static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr factory);
        [DllImport("d2d1.dll")] static extern int D2D1CreateFactory(int type, in Guid iid, IntPtr options, out IntPtr factory);
        [DllImport("dwrite.dll")] static extern int DWriteCreateFactory(int type, in Guid iid, out IntPtr factory);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateBitmap(IntPtr self, uint w, uint h, in Guid format, int cache, out IntPtr bitmap);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateTarget(IntPtr self, IntPtr bitmap, in TargetProperties properties, out IntPtr target);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate int CreateFormat(IntPtr self, string family, IntPtr collection, int weight, int style, int stretch, float size, string locale, out IntPtr format);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetAlignment(IntPtr self, int alignment);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateBrush(IntPtr self, in Rgba color, IntPtr properties, out IntPtr brush);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void BeginDraw(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void ClearTarget(IntPtr self, in Rgba color);
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] delegate void DrawText(IntPtr self, string text, uint length, IntPtr format, in Bounds rect, IntPtr brush, uint options, int measuring);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EndDraw(IntPtr self, IntPtr tag1, IntPtr tag2);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CopyPixels(IntPtr self, IntPtr rect, uint stride, uint size, [Out] byte[] pixels);

        // Slots follow the SDK interface declarations, including the three IUnknown methods.
        static T Method<T>(IntPtr instance, int slot) where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

        static void Release(IntPtr pointer)
        {
            if (pointer != IntPtr.Zero) Marshal.Release(pointer);
        }

        internal static byte[] Render(string emoji)
        {
            int apartment = CoInitializeEx(IntPtr.Zero, 2);
            if (apartment != unchecked((int)0x80010106)) Marshal.ThrowExceptionForHR(apartment);
            IntPtr wic = IntPtr.Zero, d2d = IntPtr.Zero, write = IntPtr.Zero, bitmap = IntPtr.Zero;
            IntPtr target = IntPtr.Zero, format = IntPtr.Zero, brush = IntPtr.Zero;
            try
            {
                Marshal.ThrowExceptionForHR(CoCreateInstance(WicClass, IntPtr.Zero, 1, WicInterface, out wic));
                Marshal.ThrowExceptionForHR(D2D1CreateFactory(0, D2dInterface, IntPtr.Zero, out d2d));
                Marshal.ThrowExceptionForHR(DWriteCreateFactory(0, WriteInterface, out write));
                Marshal.ThrowExceptionForHR(Method<CreateBitmap>(wic, 17)(wic, Size, Size, PixelFormat, 2, out bitmap));
                var properties = new TargetProperties { Type = 1, Format = 87, Alpha = 1, DpiX = 96, DpiY = 96 };
                Marshal.ThrowExceptionForHR(Method<CreateTarget>(d2d, 13)(d2d, bitmap, properties, out target));
                Marshal.ThrowExceptionForHR(Method<CreateFormat>(write, 15)(write, "Segoe UI Emoji", IntPtr.Zero, 400, 0, 5, 57f, "en-us", out format));
                Marshal.ThrowExceptionForHR(Method<SetAlignment>(format, 3)(format, 2));
                Marshal.ThrowExceptionForHR(Method<SetAlignment>(format, 4)(format, 2));
                Marshal.ThrowExceptionForHR(Method<CreateBrush>(target, 8)(target, new Rgba { R = 1f, G = 1f, B = 1f, A = 1f }, IntPtr.Zero, out brush));
                Method<BeginDraw>(target, 48)(target);
                Method<ClearTarget>(target, 47)(target, default);
                Method<DrawText>(target, 27)(target, emoji, (uint)emoji.Length, format, new Bounds { Right = Size, Bottom = Size }, brush, 4, 0);
                Marshal.ThrowExceptionForHR(Method<EndDraw>(target, 49)(target, IntPtr.Zero, IntPtr.Zero));
                var bgra = new byte[Size * Size * 4];
                Marshal.ThrowExceptionForHR(Method<CopyPixels>(bitmap, 7)(bitmap, IntPtr.Zero, Size * 4, (uint)bgra.Length, bgra));
                var rgba = new byte[bgra.Length];
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int src = (y * Size + x) * 4;
                    int dst = ((Size - 1 - y) * Size + x) * 4;
                    int alpha = bgra[src + 3];
                    if (alpha == 0) continue;
                    rgba[dst] = (byte)Math.Min(255, bgra[src + 2] * 255 / alpha);
                    rgba[dst + 1] = (byte)Math.Min(255, bgra[src + 1] * 255 / alpha);
                    rgba[dst + 2] = (byte)Math.Min(255, bgra[src] * 255 / alpha);
                    rgba[dst + 3] = (byte)alpha;
                }
                return rgba;
            }
            finally
            {
                Release(brush);
                Release(format);
                Release(target);
                Release(bitmap);
                Release(write);
                Release(d2d);
                Release(wic);
                if (apartment >= 0) CoUninitialize();
            }
        }
    }
}
