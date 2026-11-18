using FsltlDownscaler.Dds;

namespace FsltlDownscaler.Processing;

/// <summary>
/// Path A: drop the largest mip levels. No re-compression, so no quality loss.
/// </summary>
public static class MipStripper
{
    /// <summary>Number of top levels to drop so that the long side is at most <paramref name="target"/>.</summary>
    public static int LevelsToDrop(int width, int height, int target)
    {
        int k = 0;
        while (Math.Max(DdsTexture.LevelDim(width, k), DdsTexture.LevelDim(height, k)) > target)
        {
            k++;
        }

        return k;
    }

    public static bool CanStrip(DdsTexture tex, int target)
    {
        if (!tex.IsSupported)
        {
            return false;
        }

        int k = LevelsToDrop(tex.Width, tex.Height, target);
        return k > 0 && k < tex.MipCount;
    }

    public static long PredictSize(DdsTexture tex, int target)
    {
        int k = LevelsToDrop(tex.Width, tex.Height, target);
        long size = tex.Header.Length;
        for (int i = k; i < tex.MipCount; i++)
        {
            size += tex.LevelSize(i);
        }

        return size;
    }

    public static byte[] Strip(DdsTexture tex, byte[] file, int target)
    {
        if (!CanStrip(tex, target))
        {
            throw new InvalidOperationException("Texture cannot be reduced by dropping mip levels.");
        }

        if (file.LongLength != tex.FileLength)
        {
            throw new InvalidOperationException("File length changed since the header was read.");
        }

        int k = LevelsToDrop(tex.Width, tex.Height, target);
        int newW = tex.LevelWidth(k);
        int newH = tex.LevelHeight(k);
        int newCount = tex.MipCount - k;

        long start = tex.DataOffset + tex.LevelOffset(k);
        long end = tex.DataOffset + tex.DataLength;
        byte[] header = tex.BuildHeader(newW, newH, newCount);

        var result = new byte[header.Length + (end - start)];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(file, (int)start, result, header.Length, (int)(end - start));
        return result;
    }
}
