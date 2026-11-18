namespace FsltlDownscaler.Processing;

/// <summary>Simple, exact box filter on 4-channel 8-bit pixel buffers.</summary>
public static class ImageOps
{
    public static bool CanBoxScale(int srcW, int srcH, int dstW, int dstH) =>
        dstW > 0 && dstH > 0 && srcW % dstW == 0 && srcH % dstH == 0;

    /// <summary>
    /// Averages each block of (srcW/dstW) x (srcH/dstH) pixels. Channel order does not matter.
    /// </summary>
    public static byte[] BoxDownscale(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        if (!CanBoxScale(srcW, srcH, dstW, dstH))
        {
            throw new ArgumentException($"Cannot box-scale {srcW}x{srcH} to {dstW}x{dstH}.");
        }

        if (src.Length != (long)srcW * srcH * 4)
        {
            throw new ArgumentException("Pixel buffer has unexpected length.");
        }

        if (srcW == dstW && srcH == dstH)
        {
            return (byte[])src.Clone();
        }

        int fx = srcW / dstW;
        int fy = srcH / dstH;
        uint area = (uint)(fx * fy);
        uint half = area / 2;
        var dst = new byte[(long)dstW * dstH * 4];

        Parallel.For(0, dstH, y =>
        {
            for (int x = 0; x < dstW; x++)
            {
                uint c0 = 0, c1 = 0, c2 = 0, c3 = 0;
                for (int yy = 0; yy < fy; yy++)
                {
                    long i = (((long)(y * fy + yy) * srcW) + (long)x * fx) * 4;
                    for (int xx = 0; xx < fx; xx++)
                    {
                        c0 += src[i];
                        c1 += src[i + 1];
                        c2 += src[i + 2];
                        c3 += src[i + 3];
                        i += 4;
                    }
                }

                long o = ((long)y * dstW + x) * 4;
                dst[o] = (byte)((c0 + half) / area);
                dst[o + 1] = (byte)((c1 + half) / area);
                dst[o + 2] = (byte)((c2 + half) / area);
                dst[o + 3] = (byte)((c3 + half) / area);
            }
        });

        return dst;
    }
}
