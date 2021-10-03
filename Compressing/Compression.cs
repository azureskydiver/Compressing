using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace Compressing
{
    public static class Compression
    {
        private static async Task<CompressionResult> ToCompressedStringAsync(string value,
                                                                             CompressionLevel level,
                                                                             string algorithm,
                                                                             Func<Stream, Stream> createCompressionStream)
        {
            var bytes = Encoding.Unicode.GetBytes(value);
            await using var input = new MemoryStream(bytes);
            await using var output = new MemoryStream();
            await using var stream = createCompressionStream(output);

            await input.CopyToAsync(stream);
            await stream.FlushAsync();

            var result = output.ToArray();

            return new CompressionResult(
                new(value),
                new(result.ToPackedUtf16()),
                level,
                algorithm);
        }

        public static async Task<CompressionResult> ToGzipAsync(this string value, CompressionLevel level = CompressionLevel.Fastest)
            => await ToCompressedStringAsync(value, level, "GZip", s => new GZipStream(s, level));
        
        public static async Task<CompressionResult> ToBrotliAsync(this string value, CompressionLevel level = CompressionLevel.Fastest)
            => await ToCompressedStringAsync(value, level, "Brotli", s => new BrotliStream(s, level));

        private static async Task<string> FromCompressedStringAsync(string value, Func<Stream, Stream> createDecompressionStream)
        {
            var bytes = value.PackedUtf16ToBytes().ToArray();
            await using var input = new MemoryStream(bytes);
            await using var output = new MemoryStream();
            await using var stream = createDecompressionStream(input);

            await stream.CopyToAsync(output);
            await output.FlushAsync();

            return Encoding.Unicode.GetString(output.ToArray());
        }

        public static async Task<string> FromGzipAsync(this string value)
            => await FromCompressedStringAsync(value, s => new GZipStream(s, CompressionMode.Decompress));
 
        public static async Task<string> FromBrotliAsync(this string value)
            => await FromCompressedStringAsync(value, s => new BrotliStream(s, CompressionMode.Decompress));

        private static bool IsSurrogate(byte b)
            => 0xD8 <= b && b <= 0xDF;

        public static string ToPackedUtf16(this byte[] data)
        {
            var pairs = data.Length / 2;
            bool isOdd = data.Length % 2 == 1;
            var sb = new StringBuilder(pairs + (isOdd ? 1 : 0));
            var bytePairs = pairs * 2;

            for (int i = 0; i < bytePairs; i += 2)
                EncodeBytes(low: data[i], high: data[i + 1]);

            if (isOdd)
                EncodeBytes(low: data[data.Length - 1], high: 0x00);

            return sb.ToString();

            void EncodeBytes(byte low, byte high)
            {
                if (IsSurrogate(high))
                {
                    AppendChar(low: low, high: 0xD8);
                    AppendChar(low: high, high: 0xDC);
                }
                else
                {
                    AppendChar(low: low, high: high);
                }
            }

            void AppendChar(byte low, byte high)
                => sb.Append((char)((high << 8) + low));
        }

        public static IEnumerable<byte> PackedUtf16ToBytes(this string packedUtf16)
        {
            foreach(char ch in packedUtf16)
            {
                byte low = (byte) (ch & 0xFF);
                byte high = (byte)((ch >> 8) & 0xFF);

                if (!IsSurrogate(high))
                    yield return high;

                yield return low;
            }
        }
    }

    public record CompressionResult(
        CompressionValue Original,
        CompressionValue Result,
        CompressionLevel Level,
        string Kind
    )
    {
        public int Difference =>
            Original.Size - Result.Size;

        public decimal Percent =>
          Math.Abs(Difference / (decimal) Original.Size);
    }

    public record CompressionValue(
        string Value
    )
    {
        public int Size => Value.Length;
    }
}