using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace UltimateMenu
{
    internal static class PlaylistAudioDecoder
    {
        const int AudioStream = -3;
        const int AllStreams = -2;
        static readonly Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        static readonly Guid Audio = new Guid("73647561-0000-0010-8000-00aa00389b71");
        static readonly Guid Pcm = new Guid("00000001-0000-0010-8000-00aa00389b71");
        static readonly Guid BitsPerSample = new Guid("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

        internal sealed class Track
        {
            public readonly float[] Samples;
            public readonly int Channels, Rate;
            public readonly byte[] Artwork;
            public Track(float[] samples, int channels, int rate, byte[] artwork)
            {
                Samples = samples;
                Channels = channels;
                Rate = rate;
                Artwork = artwork;
            }
        }

        [DllImport("mfplat.dll")] static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] static extern int MFShutdown();
        [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IntPtr type);
        [DllImport("mfplat.dll")] static extern int MFCreateWaveFormatExFromMFMediaType(IntPtr type, out IntPtr format, out int size, int flags);
        [DllImport("mfplat.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int MFCreateFile(int access, int openMode, int flags, string path, out IntPtr stream);
        [DllImport("mfreadwrite.dll")]
        static extern int MFCreateSourceReaderFromByteStream(IntPtr stream, IntPtr attributes, out IntPtr reader);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int StreamSelection(IntPtr self, int stream, int selected);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetType(IntPtr self, int stream, IntPtr reserved, IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetMediaType(IntPtr self, int stream, out IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetGuid(IntPtr self, ref Guid key, ref Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetUInt(IntPtr self, ref Guid key, int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ReadSample(IntPtr self, int stream, int control, out int actual, out int flags, out long timestamp, out IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetBuffer(IntPtr self, out IntPtr buffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int LockBuffer(IntPtr self, out IntPtr data, out int capacity, out int length);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int UnlockBuffer(IntPtr self);

        static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

        static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
        static void Release(IntPtr value) { if (value != IntPtr.Zero) Marshal.Release(value); }

        internal static string ExtendedPath(string path)
        {
            path = Path.GetFullPath(path);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            return path.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
        }

        internal static string DisplayPath(string path)
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path.Substring(8);
            return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path.Substring(4) : path;
        }

        internal static Track Decode(string path, CancellationToken cancellation)
        {
            Check(MFStartup(0x00020070, 0));
            IntPtr stream = IntPtr.Zero, reader = IntPtr.Zero, requested = IntPtr.Zero, actual = IntPtr.Zero, format = IntPtr.Zero;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                path = Path.GetFullPath(path);
                string nativePath = ExtendedPath(path);
                if (!File.Exists(nativePath)) throw new FileNotFoundException("Playlist file unavailable", path);
                Check(MFCreateFile(1, 0, 0, nativePath, out stream));
                Check(MFCreateSourceReaderFromByteStream(stream, IntPtr.Zero, out reader));
                var select = Method<StreamSelection>(reader, 4);
                Check(select(reader, AllStreams, 0));
                Check(select(reader, AudioStream, 1));
                Check(MFCreateMediaType(out requested));
                var setGuid = Method<SetGuid>(requested, 24);
                Guid key = MajorType, value = Audio;
                Check(setGuid(requested, ref key, ref value));
                key = Subtype; value = Pcm;
                Check(setGuid(requested, ref key, ref value));
                key = BitsPerSample;
                Check(Method<SetUInt>(requested, 21)(requested, ref key, 16));
                Check(Method<SetType>(reader, 7)(reader, AudioStream, IntPtr.Zero, requested));
                Check(Method<GetMediaType>(reader, 6)(reader, AudioStream, out actual));
                Check(MFCreateWaveFormatExFromMFMediaType(actual, out format, out int size, 0));
                if (size < 16 || Marshal.ReadInt16(format, 14) != 16)
                    throw new InvalidDataException("Expected 16-bit PCM");
                int channels = Marshal.ReadInt16(format, 2), rate = Marshal.ReadInt32(format, 4);
                if (channels < 1 || channels > 8 || rate < 8000 || rate > 192000)
                    throw new InvalidDataException("Unsupported audio format");

                using var pcm = new MemoryStream();
                byte[] chunk = Array.Empty<byte>();
                var read = Method<ReadSample>(reader, 9);
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Check(read(reader, AudioStream, 0, out _, out int flags, out _, out IntPtr sample));
                    try
                    {
                        if ((flags & 0x30) != 0) throw new InvalidDataException("Audio format changed while decoding");
                        if (sample != IntPtr.Zero)
                        {
                            Check(Method<GetBuffer>(sample, 41)(sample, out IntPtr buffer));
                            try
                            {
                                Check(Method<LockBuffer>(buffer, 3)(buffer, out IntPtr data, out _, out int length));
                                try
                                {
                                    if (length < 0 || pcm.Length + length > 96 * 1024 * 1024)
                                        throw new InvalidDataException("Audio exceeds the decoded memory limit");
                                    if (chunk.Length < length) chunk = new byte[length];
                                    Marshal.Copy(data, chunk, 0, length);
                                    pcm.Write(chunk, 0, length);
                                }
                                finally { Method<UnlockBuffer>(buffer, 4)(buffer); }
                            }
                            finally { Release(buffer); }
                        }
                    }
                    finally { Release(sample); }
                    if ((flags & 2) != 0) break;
                }
                int sampleCount = (int)pcm.Length / 2;
                if (sampleCount == 0 || pcm.Length % (channels * 2) != 0)
                    throw new InvalidDataException("Empty or incomplete audio");
                var samples = new float[sampleCount];
                byte[] bytes = pcm.GetBuffer();
                for (int i = 0; i < samples.Length; i++)
                {
                    if ((i & 0xffff) == 0) cancellation.ThrowIfCancellationRequested();
                    samples[i] = unchecked((short)(bytes[i * 2] | bytes[i * 2 + 1] << 8)) / 32768f;
                }
                return new Track(samples, channels, rate, ReadArtwork(nativePath));
            }
            finally
            {
                if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
                Release(actual);
                Release(requested);
                Release(reader);
                Release(stream);
                MFShutdown();
            }
        }

        static byte[] ReadArtwork(string path)
        {
            const int limit = 4 * 1024 * 1024;
            try
            {
                using (var reader = new BinaryReader(File.OpenRead(path)))
                {
                    byte[] header = reader.ReadBytes(10);
                    if (header.Length == 10 && header[0] == 'I' && header[1] == 'D' && header[2] == '3'
                        && (header[3] == 3 || header[3] == 4) && (header[5] & 0xc0) == 0)
                    {
                        int size = ReadId3Size(header, 6, true);
                        if (size > 0 && size <= limit)
                        {
                            byte[] tag = reader.ReadBytes(size);
                            for (int offset = 0; offset + 10 <= tag.Length;)
                            {
                                int length = ReadId3Size(tag, offset + 4, header[3] == 4);
                                int data = offset + 10;
                                if (length <= 0 || length > tag.Length - data) break;
                                if (tag[offset] == 'A' && tag[offset + 1] == 'P'
                                    && tag[offset + 2] == 'I' && tag[offset + 3] == 'C' && tag[offset + 9] == 0)
                                {
                                    int end = data + length;
                                    int mimeEnd = Array.IndexOf(tag, (byte)0, data + 1, length - 1);
                                    if (mimeEnd > data && mimeEnd + 2 < end)
                                    {
                                        int image = mimeEnd + 2;
                                        int step = tag[data] == 1 || tag[data] == 2 ? 2 : 1;
                                        while (image + step <= end && (tag[image] != 0 || step == 2 && tag[image + 1] != 0)) image += step;
                                        image += step;
                                        if (image < end) return new ReadOnlySpan<byte>(tag, image, end - image).ToArray();
                                    }
                                }
                                offset = data + length;
                            }
                        }
                    }
                }
                string directory = Path.GetDirectoryName(path);
                foreach (string name in new[] { "cover.jpg", "cover.png", "folder.jpg", "folder.png" })
                {
                    var file = new FileInfo(Path.Combine(directory, name));
                    if (file.Exists && file.Length > 0 && file.Length <= limit) return File.ReadAllBytes(file.FullName);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        static int ReadId3Size(byte[] bytes, int offset, bool syncSafe)
        {
            int shift = syncSafe ? 7 : 8;
            int result = 0;
            for (int i = 0; i < 4; i++)
                result = (result << shift) | (bytes[offset + i] & (syncSafe ? 127 : 255));
            return result;
        }
    }
}
