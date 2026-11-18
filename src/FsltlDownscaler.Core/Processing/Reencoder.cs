using System.Runtime.InteropServices;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using FsltlDownscaler.Dds;

namespace FsltlDownscaler.Processing;

/// <summary>
/// Path B: textures without a usable mip chain.
/// Decode the top level, box-filter it down, optionally build a full mip chain,
/// and encode again in exactly the same format. The header (incl. DX10 part) is kept.
/// </summary>
public static class Reencoder
{
    public static bool CanReencode(DdsTexture tex, int target, out string? reason)
    {
        reason = null;
        if (!tex.IsSupported || tex.Format is null)
        {
            reason = tex.UnsupportedReason ?? "Unknown format.";
            return false;
        }

        if (tex.Format.Encode == EncodeKind.None)
        {
            reason = $"Format {tex.Format.Name} cannot be re-encoded.";
            return false;
        }

        int k = MipStripper.LevelsToDrop(tex.Width, tex.Height, target);
        if (k == 0)
        {
            reason = "Already at or below target size.";
            return false;
        }

        if (!ImageOps.CanBoxScale(tex.Width, tex.Height, tex.LevelWidth(k), tex.LevelHeight(k)))
        {
            reason = "Dimensions are not a power-of-two multiple of the target.";
            return false;
        }

        return true;
    }

    public static long PredictSize(DdsTexture tex, int target, bool generateMips)
    {
        int k = MipStripper.LevelsToDrop(tex.Width, tex.Height, target);
        int w = tex.LevelWidth(k);
        int h = tex.LevelHeight(k);
        long size = tex.Header.Length;
        foreach (var (lw, lh) in Chain(w, h, generateMips))
        {
            size += tex.Format!.LevelSize(lw, lh);
        }

        return size;
    }

    public static byte[] Reencode(DdsTexture tex, byte[] file, int target, bool generateMips)
    {
        if (!CanReencode(tex, target, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var format = tex.Format!;
        int k = MipStripper.LevelsToDrop(tex.Width, tex.Height, target);
        int w = tex.LevelWidth(k);
        int h = tex.LevelHeight(k);

        byte[] top = DecodeTopLevel(tex, file);
        byte[] current = ImageOps.BoxDownscale(top, tex.Width, tex.Height, w, h);
        top = [];

        bool bc1Alpha = format.Encode == EncodeKind.Bc1 && HasTransparency(current);
        var levels = new List<byte[]>();
        int cw = w;
        int ch = h;
        foreach (var (lw, lh) in Chain(w, h, generateMips))
        {
            if (lw != cw || lh != ch)
            {
                current = ImageOps.BoxDownscale(current, cw, ch, lw, lh);
                cw = lw;
                ch = lh;
            }

            byte[] encoded = EncodeLevel(format.Encode, current, cw, ch, bc1Alpha);
            long expected = format.LevelSize(cw, ch);
            if (encoded.LongLength != expected)
            {
                throw new InvalidOperationException($"Encoder returned {encoded.Length} bytes, expected {expected}.");
            }

            levels.Add(encoded);
        }

        byte[] header = tex.BuildHeader(w, h, levels.Count);
        var result = new byte[header.Length + levels.Sum(l => (long)l.Length)];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        int offset = header.Length;
        foreach (var level in levels)
        {
            Buffer.BlockCopy(level, 0, result, offset, level.Length);
            offset += level.Length;
        }

        return result;
    }

    private static IEnumerable<(int W, int H)> Chain(int w, int h, bool full)
    {
        yield return (w, h);
        if (!full)
        {
            yield break;
        }

        while (w > 1 || h > 1)
        {
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
            yield return (w, h);
        }
    }

    private static byte[] DecodeTopLevel(DdsTexture tex, byte[] file)
    {
        var format = tex.Format!;
        int length = checked((int)tex.LevelSize(0));
        var data = new ReadOnlyMemory<byte>(file, tex.DataOffset, length);

        if (format.Encode == EncodeKind.Raw32)
        {
            return data.ToArray();
        }

        var decoder = new BcDecoder();
        ColorRgba32[] pixels = decoder.DecodeRaw(data.ToArray(), tex.Width, tex.Height, ToCompression(format.Encode, alpha: true));
        return MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray();
    }

    private static byte[] EncodeLevel(EncodeKind kind, byte[] rgba, int w, int h, bool bc1Alpha)
    {
        if (kind == EncodeKind.Raw32)
        {
            return rgba;
        }

        var encoder = new BcEncoder();
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.OutputOptions.Format = ToCompression(kind, bc1Alpha);

        byte[][] result = encoder.EncodeToRawBytes(rgba, w, h, PixelFormat.Rgba32);
        return result[0];
    }

    private static CompressionFormat ToCompression(EncodeKind kind, bool alpha) => kind switch
    {
        EncodeKind.Bc1 => alpha ? CompressionFormat.Bc1WithAlpha : CompressionFormat.Bc1,
        EncodeKind.Bc2 => CompressionFormat.Bc2,
        EncodeKind.Bc3 => CompressionFormat.Bc3,
        EncodeKind.Bc4 => CompressionFormat.Bc4,
        EncodeKind.Bc5 => CompressionFormat.Bc5,
        EncodeKind.Bc7 => CompressionFormat.Bc7,
        _ => throw new NotSupportedException(kind.ToString()),
    };

    private static bool HasTransparency(byte[] rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] < 128)
            {
                return true;
            }
        }

        return false;
    }
}
