using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace wb.Core
{
    /// <summary>
    /// High-performance GZip string decompressor for embedded internal templates and scriptlets.
    /// Reduces embedded binary metadata (#US heap) by up to 90% while maintaining
    /// sub-millisecond on-demand decompression speed.
    /// </summary>
    public static class CompressedStringHelper
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public static string Decompress(byte[] compressedBytes)
        {
            if (compressedBytes == null || compressedBytes.Length == 0) return string.Empty;
            using (MemoryStream ms = new MemoryStream(compressedBytes))
            using (GZipStream gz = new GZipStream(ms, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gz, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
