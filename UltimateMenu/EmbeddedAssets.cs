using System.IO;
using System.IO.Compression;

namespace UltimateMenu
{
    internal static class EmbeddedAssets
    {
        internal static Stream? OpenPacked(string name)
        {
            var stream = typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(name + ".gz");
            return stream == null ? null : new GZipStream(stream, CompressionMode.Decompress);
        }
    }
}
