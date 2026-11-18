namespace FsltlDownscaler.Dds;

/// <summary>How a texture can be re-encoded if it has no usable mip chain (path B).</summary>
public enum EncodeKind
{
    None,
    Bc1,
    Bc2,
    Bc3,
    Bc4,
    Bc5,
    Bc7,
    Raw32,
}

/// <summary>Pixel format of a DDS texture and the size math that goes with it.</summary>
public sealed record DdsFormat(string Name, bool IsBlockCompressed, int BytesPerBlock, int BitsPerPixel, EncodeKind Encode)
{
    public long LevelSize(int width, int height)
    {
        if (IsBlockCompressed)
        {
            long bw = Math.Max(1, (width + 3) / 4);
            long bh = Math.Max(1, (height + 3) / 4);
            return bw * bh * BytesPerBlock;
        }

        long rowBytes = ((long)width * BitsPerPixel + 7) / 8;
        return rowBytes * height;
    }

    /// <summary>Value for dwPitchOrLinearSize: linear size of the top level for BC, row pitch otherwise.</summary>
    public uint PitchOrLinearSize(int width, int height)
    {
        if (IsBlockCompressed)
        {
            return (uint)LevelSize(width, height);
        }

        return (uint)(((long)width * BitsPerPixel + 7) / 8);
    }

    private static DdsFormat Bc(string name, int bpb, EncodeKind e) => new(name, true, bpb, 0, e);

    public static DdsFormat? FromFourCC(string fourCC) => fourCC switch
    {
        "DXT1" => Bc("BC1 (DXT1)", 8, EncodeKind.Bc1),
        "DXT2" => Bc("BC2 premultiplied (DXT2)", 16, EncodeKind.None),
        "DXT3" => Bc("BC2 (DXT3)", 16, EncodeKind.Bc2),
        "DXT4" => Bc("BC3 premultiplied (DXT4)", 16, EncodeKind.None),
        "DXT5" => Bc("BC3 (DXT5)", 16, EncodeKind.Bc3),
        "ATI1" or "BC4U" => Bc("BC4", 8, EncodeKind.Bc4),
        "BC4S" => Bc("BC4 SNORM", 8, EncodeKind.None),
        "ATI2" or "BC5U" => Bc("BC5", 16, EncodeKind.Bc5),
        "BC5S" => Bc("BC5 SNORM", 16, EncodeKind.None),
        _ => null,
    };

    public static DdsFormat? FromDxgi(uint dxgi) => dxgi switch
    {
        70 or 71 => Bc("BC1", 8, EncodeKind.Bc1),
        72 => Bc("BC1 SRGB", 8, EncodeKind.Bc1),
        73 or 74 => Bc("BC2", 16, EncodeKind.Bc2),
        75 => Bc("BC2 SRGB", 16, EncodeKind.Bc2),
        76 or 77 => Bc("BC3", 16, EncodeKind.Bc3),
        78 => Bc("BC3 SRGB", 16, EncodeKind.Bc3),
        79 or 80 => Bc("BC4", 8, EncodeKind.Bc4),
        81 => Bc("BC4 SNORM", 8, EncodeKind.None),
        82 or 83 => Bc("BC5", 16, EncodeKind.Bc5),
        84 => Bc("BC5 SNORM", 16, EncodeKind.None),
        94 or 95 or 96 => Bc("BC6H", 16, EncodeKind.None),
        97 or 98 => Bc("BC7", 16, EncodeKind.Bc7),
        99 => Bc("BC7 SRGB", 16, EncodeKind.Bc7),
        27 or 28 or 29 or 87 or 88 or 90 or 91 => new DdsFormat("RGBA 32 bit (DX10)", false, 0, 32, EncodeKind.Raw32),
        _ => null,
    };

    public static DdsFormat Uncompressed(int bitsPerPixel) =>
        new($"Uncompressed {bitsPerPixel} bit", false, 0, bitsPerPixel, bitsPerPixel == 32 ? EncodeKind.Raw32 : EncodeKind.None);
}
