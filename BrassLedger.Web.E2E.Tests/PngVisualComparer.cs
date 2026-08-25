using System.Buffers.Binary;
using System.IO.Compression;

namespace BrassLedger.Web.E2E.Tests;

internal static class PngVisualComparer
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static double CalculatePsnr(byte[] expected, byte[] actual)
    {
        var left = Decode(expected);
        var right = Decode(actual);
        if (left.Width != right.Width || left.Height != right.Height)
        {
            return 0d;
        }

        double squaredError = 0d;
        for (var index = 0; index < left.Rgb.Length; index++)
        {
            var difference = left.Rgb[index] - right.Rgb[index];
            squaredError += difference * difference;
        }

        if (squaredError == 0d)
        {
            return double.PositiveInfinity;
        }

        var meanSquaredError = squaredError / left.Rgb.Length;
        return 10d * Math.Log10(255d * 255d / meanSquaredError);
    }

    private static DecodedPng Decode(byte[] png)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new InvalidDataException("The visual baseline is not a PNG image.");
        }

        var offset = Signature.Length;
        var width = 0;
        var height = 0;
        var colorType = 0;
        using var compressed = new MemoryStream();
        while (offset + 12 <= png.Length)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var dataOffset = offset + 8;
            if (dataOffset + length + 4 > png.Length) throw new InvalidDataException("The PNG contains a truncated chunk.");
            if (type == "IHDR")
            {
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(dataOffset, 4)));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(dataOffset + 4, 4)));
                if (png[dataOffset + 8] != 8 || png[dataOffset + 12] != 0) throw new InvalidDataException("Only non-interlaced 8-bit PNG baselines are supported.");
                colorType = png[dataOffset + 9];
            }
            else if (type == "IDAT")
            {
                compressed.Write(png, dataOffset, length);
            }
            else if (type == "IEND")
            {
                break;
            }

            offset = dataOffset + length + 4;
        }

        var bytesPerPixel = colorType switch { 2 => 3, 6 => 4, _ => throw new InvalidDataException($"Unsupported PNG color type {colorType}.") };
        if (width <= 0 || height <= 0) throw new InvalidDataException("The PNG has invalid dimensions.");
        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            zlib.CopyTo(decompressed);
        }

        var source = decompressed.ToArray();
        var stride = checked(width * bytesPerPixel);
        if (source.Length != checked((stride + 1) * height)) throw new InvalidDataException("The PNG scanline length is invalid.");
        var pixels = new byte[checked(stride * height)];
        for (var row = 0; row < height; row++)
        {
            var sourceOffset = row * (stride + 1);
            var targetOffset = row * stride;
            var filter = source[sourceOffset];
            for (var column = 0; column < stride; column++)
            {
                var raw = source[sourceOffset + column + 1];
                var left = column >= bytesPerPixel ? pixels[targetOffset + column - bytesPerPixel] : 0;
                var above = row > 0 ? pixels[targetOffset + column - stride] : 0;
                var upperLeft = row > 0 && column >= bytesPerPixel ? pixels[targetOffset + column - stride - bytesPerPixel] : 0;
                pixels[targetOffset + column] = filter switch
                {
                    0 => raw,
                    1 => unchecked((byte)(raw + left)),
                    2 => unchecked((byte)(raw + above)),
                    3 => unchecked((byte)(raw + ((left + above) / 2))),
                    4 => unchecked((byte)(raw + Paeth(left, above, upperLeft))),
                    _ => throw new InvalidDataException($"Unsupported PNG filter {filter}.")
                };
            }
        }

        if (bytesPerPixel == 3) return new DecodedPng(width, height, pixels);
        var rgb = new byte[checked(width * height * 3)];
        for (int sourceIndex = 0, targetIndex = 0; sourceIndex < pixels.Length; sourceIndex += 4)
        {
            rgb[targetIndex++] = pixels[sourceIndex];
            rgb[targetIndex++] = pixels[sourceIndex + 1];
            rgb[targetIndex++] = pixels[sourceIndex + 2];
        }

        return new DecodedPng(width, height, rgb);
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        var estimate = left + above - upperLeft;
        var leftDistance = Math.Abs(estimate - left);
        var aboveDistance = Math.Abs(estimate - above);
        var upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance ? left : aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private sealed record DecodedPng(int Width, int Height, byte[] Rgb);
}
