using System.Buffers.Binary;
using System.Text;

namespace FsltlDownscaler.Dds;

public sealed class DdsException(string message) : Exception(message);

/// <summary>
/// Parsed DDS header plus the mip level layout of the file.
/// Only the header is needed. The pixel data is never touched here.
/// </summary>
public sealed class DdsTexture
{
    public const int BaseHeaderSize = 128; // "DDS " + 124 byte DDS_HEADER
    public const int Dx10HeaderSize = 20;
    public const int MaxHeaderSize = BaseHeaderSize + Dx10HeaderSize;

    private const uint Magic = 0x20534444; // "DDS "

    private const uint DDSD_CAPS = 0x1;
    private const uint DDSD_HEIGHT = 0x2;
    private const uint DDSD_WIDTH = 0x4;
    private const uint DDSD_PITCH = 0x8;
    private const uint DDSD_PIXELFORMAT = 0x1000;
    private const uint DDSD_MIPMAPCOUNT = 0x20000;
    private const uint DDSD_LINEARSIZE = 0x80000;

    private const uint DDPF_FOURCC = 0x4;

    private const uint DDSCAPS_COMPLEX = 0x8;
    private const uint DDSCAPS_TEXTURE = 0x1000;
    private const uint DDSCAPS_MIPMAP = 0x400000;

    private readonly long[] _levelOffsets;
    private readonly long[] _levelSizes;

    private DdsTexture(byte[] header, int width, int height, int declaredMipCount, int mipCount,
        DdsFormat? format, long fileLength, long[] levelOffsets, long[] levelSizes, string? unsupportedReason)
    {
        Header = header;
        Width = width;
        Height = height;
        DeclaredMipCount = declaredMipCount;
        MipCount = mipCount;
        Format = format;
        FileLength = fileLength;
        _levelOffsets = levelOffsets;
        _levelSizes = levelSizes;
        UnsupportedReason = unsupportedReason;
    }

    /// <summary>Raw header bytes (128, or 148 with DX10 extension).</summary>
    public byte[] Header { get; }
    public int DataOffset => Header.Length;
    public int Width { get; }
    public int Height { get; }
    public int DeclaredMipCount { get; }
    /// <summary>Number of mip levels actually present in the file.</summary>
    public int MipCount { get; }
    public DdsFormat? Format { get; }
    public long FileLength { get; }
    public string? UnsupportedReason { get; }

    public bool IsSupported => UnsupportedReason is null && Format is not null;
    public bool MipCountCorrected => IsSupported && MipCount != DeclaredMipCount;
    public bool HasMips => MipCount > 1;
    public int LongSide => Math.Max(Width, Height);
    public string FormatName => Format?.Name ?? "unknown";

    public long LevelOffset(int level) => _levelOffsets[level];
    public long LevelSize(int level) => _levelSizes[level];
    public long DataLength => _levelSizes.Sum();

    public static int LevelDim(int size, int level) => Math.Max(1, size >> level);
    public int LevelWidth(int level) => LevelDim(Width, level);
    public int LevelHeight(int level) => LevelDim(Height, level);

    /// <summary>Number of levels of a complete chain down to 1x1.</summary>
    public static int FullChainLength(int width, int height)
    {
        int n = 1;
        int m = Math.Max(width, height);
        while (m > 1)
        {
            m >>= 1;
            n++;
        }

        return n;
    }

    public static DdsTexture ReadHeader(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None);
        var buf = new byte[MaxHeaderSize];
        int read = fs.ReadAtLeast(buf, Math.Min(MaxHeaderSize, (int)Math.Min(fs.Length, MaxHeaderSize)), throwOnEndOfStream: false);
        return Parse(buf.AsSpan(0, read), fs.Length);
    }

    public static DdsTexture Parse(ReadOnlySpan<byte> data, long fileLength)
    {
        if (data.Length < BaseHeaderSize)
        {
            throw new DdsException("File is too small for a DDS header.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
        {
            throw new DdsException("Missing DDS signature.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 124)
        {
            throw new DdsException("Invalid DDS header size.");
        }

        uint flags = U32(data, 8);
        int height = (int)U32(data, 12);
        int width = (int)U32(data, 16);
        uint mipField = U32(data, 28);
        uint pfFlags = U32(data, 80);
        string fourCC = Encoding.ASCII.GetString(data.Slice(84, 4));
        int rgbBitCount = (int)U32(data, 88);
        uint caps2 = U32(data, 112);

        if (width <= 0 || height <= 0)
        {
            throw new DdsException("Invalid texture dimensions.");
        }

        bool dx10 = (pfFlags & DDPF_FOURCC) != 0 && fourCC == "DX10";
        int headerSize = dx10 ? MaxHeaderSize : BaseHeaderSize;
        if (data.Length < headerSize)
        {
            throw new DdsException("DX10 header is incomplete.");
        }

        byte[] header = data[..headerSize].ToArray();
        int declared = (int)Math.Max(1u, mipField);
        string? reason = null;
        DdsFormat? format;

        if (dx10)
        {
            uint dxgi = U32(data, 128);
            uint dimension = U32(data, 132);
            uint arraySize = U32(data, 140);
            format = DdsFormat.FromDxgi(dxgi);
            if (format is null)
            {
                reason = $"Unsupported DXGI format {dxgi}.";
            }
            else if (dimension != 3 || arraySize > 1)
            {
                reason = "Texture arrays and non-2D textures are not supported.";
            }
        }
        else if ((pfFlags & DDPF_FOURCC) != 0)
        {
            format = DdsFormat.FromFourCC(fourCC);
            if (format is null)
            {
                reason = $"Unsupported FourCC '{fourCC}'.";
            }
        }
        else
        {
            format = rgbBitCount is 8 or 16 or 24 or 32 ? DdsFormat.Uncompressed(rgbBitCount) : null;
            if (format is null)
            {
                reason = $"Unsupported uncompressed bit depth {rgbBitCount}.";
            }
        }

        if (reason is null && caps2 != 0)
        {
            reason = "Cube maps and volume textures are not supported.";
        }

        if (reason is not null || format is null)
        {
            return new DdsTexture(header, width, height, declared, declared, format, fileLength, [], [], reason);
        }

        // Walk the declared chain. Some FSLTL files carry one more level than declared.
        long dataLength = fileLength - headerSize;
        int fullChain = FullChainLength(width, height);
        var offsets = new List<long>();
        var sizes = new List<long>();
        long cumulative = 0;
        int level = 0;

        while (level < declared)
        {
            long size = format.LevelSize(LevelDim(width, level), LevelDim(height, level));
            offsets.Add(cumulative);
            sizes.Add(size);
            cumulative += size;
            level++;
        }

        if (cumulative > dataLength)
        {
            reason = "File is shorter than its header declares.";
        }
        else if (cumulative < dataLength)
        {
            while (level < fullChain && cumulative < dataLength)
            {
                long size = format.LevelSize(LevelDim(width, level), LevelDim(height, level));
                offsets.Add(cumulative);
                sizes.Add(size);
                cumulative += size;
                level++;
            }

            if (cumulative != dataLength)
            {
                reason = "Unexpected amount of pixel data in file.";
            }
        }

        if (reason is not null)
        {
            return new DdsTexture(header, width, height, declared, declared, format, fileLength, [], [], reason);
        }

        return new DdsTexture(header, width, height, declared, sizes.Count, format, fileLength,
            offsets.ToArray(), sizes.ToArray(), null);
    }

    /// <summary>
    /// Builds a header for a new texture with the same format, other dimensions and mip count.
    /// All other fields (pixel format, DX10 extension) are copied unchanged.
    /// </summary>
    public byte[] BuildHeader(int width, int height, int mipCount)
    {
        if (Format is null)
        {
            throw new InvalidOperationException("Format unknown.");
        }

        var h = (byte[])Header.Clone();
        var span = h.AsSpan();

        uint flags = U32(span, 8) | DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT;
        if (Format.IsBlockCompressed)
        {
            flags = (flags | DDSD_LINEARSIZE) & ~DDSD_PITCH;
        }
        else
        {
            flags = (flags | DDSD_PITCH) & ~DDSD_LINEARSIZE;
        }

        uint caps = U32(span, 108) | DDSCAPS_TEXTURE;
        if (mipCount > 1)
        {
            flags |= DDSD_MIPMAPCOUNT;
            caps |= DDSCAPS_COMPLEX | DDSCAPS_MIPMAP;
        }
        else
        {
            caps &= ~(DDSCAPS_COMPLEX | DDSCAPS_MIPMAP);
        }

        W32(span, 8, flags);
        W32(span, 12, (uint)height);
        W32(span, 16, (uint)width);
        W32(span, 20, Format.PitchOrLinearSize(width, height));
        W32(span, 28, (uint)mipCount);
        W32(span, 108, caps);
        return h;
    }

    private static uint U32(ReadOnlySpan<byte> d, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(d[offset..]);
    private static void W32(Span<byte> d, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(d[offset..], value);
}
