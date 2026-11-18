using FsltlDownscaler.Dds;
using FsltlDownscaler.Package;
using FsltlDownscaler.Processing;

namespace FsltlDownscaler.Pipeline;

public enum PlanAction
{
    /// <summary>Already at or below target size.</summary>
    Keep,
    /// <summary>Path A: drop top mip levels.</summary>
    StripMips,
    /// <summary>Path B: decode, downscale, encode.</summary>
    Reencode,
    /// <summary>Cannot be processed. Stays untouched.</summary>
    Skip,
}

public sealed record TexturePlan(
    string FullPath,
    string RelPath,
    string Format,
    int Width,
    int Height,
    int MipCount,
    long OldSize,
    long NewSize,
    PlanAction Action,
    bool InLayout,
    string? Note);

public sealed class Plan
{
    public required int Target { get; init; }
    public required bool GenerateMips { get; init; }
    public required List<TexturePlan> Items { get; init; }

    public IEnumerable<TexturePlan> ToProcess => Items.Where(i => i.Action is PlanAction.StripMips or PlanAction.Reencode);
    public long SizeBefore => Items.Sum(i => i.OldSize);
    public long SizeAfter => Items.Sum(i => i.NewSize);
    public long Saving => SizeBefore - SizeAfter;
    public int Count(PlanAction a) => Items.Count(i => i.Action == a);
}

/// <summary>One DDS file as found on disk. Header only, no pixel data.</summary>
public sealed record TextureInfo(string FullPath, string RelPath, long Size, bool InLayout, DdsTexture? Texture, string? Error);

/// <summary>Result of reading all texture headers once. Plans for any target are derived from it without disk access.</summary>
public sealed class ScanResult
{
    public required List<TextureInfo> Items { get; init; }

    public int Supported => Items.Count(i => i.Texture is { IsSupported: true });

    public int LargerThan(int px) => Items.Count(i => i.Texture is { IsSupported: true } t && t.LongSide > px);
}

public static class Planner
{
    public static ScanResult Scan(PackageContext pkg, LayoutFile layout, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var files = pkg.EnumerateDdsFiles().ToList();
        var items = new TextureInfo[files.Count];
        int done = 0;

        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, i =>
        {
            items[i] = ScanOne(pkg, layout, files[i]);
            int n = Interlocked.Increment(ref done);
            if (n % 250 == 0)
            {
                progress?.Report(n);
            }
        });

        return new ScanResult
        {
            Items = items.OrderBy(p => p.RelPath, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    public static Plan PlanFor(ScanResult scan, int target, bool generateMips) => new()
    {
        Target = target,
        GenerateMips = generateMips,
        Items = scan.Items.Select(i => PlanOne(i, target, generateMips)).ToList(),
    };

    public static Plan Build(PackageContext pkg, LayoutFile layout, int target, bool generateMips, IProgress<int>? progress = null) =>
        PlanFor(Scan(pkg, layout, progress), target, generateMips);

    private static TextureInfo ScanOne(PackageContext pkg, LayoutFile layout, string path)
    {
        string rel = pkg.Relative(path);
        bool inLayout = layout.Contains(rel);
        long size = new FileInfo(path).Length;
        try
        {
            return new TextureInfo(path, rel, size, inLayout, DdsTexture.ReadHeader(path), null);
        }
        catch (Exception ex) when (ex is DdsException or IOException)
        {
            return new TextureInfo(path, rel, size, inLayout, null, ex.Message);
        }
    }

    private static TexturePlan PlanOne(TextureInfo info, int target, bool generateMips)
    {
        var tex = info.Texture;
        if (tex is null)
        {
            return new TexturePlan(info.FullPath, info.RelPath, "?", 0, 0, 0, info.Size, info.Size, PlanAction.Skip, info.InLayout, info.Error);
        }

        TexturePlan Make(PlanAction a, long newSize, string? note) =>
            new(info.FullPath, info.RelPath, tex.FormatName, tex.Width, tex.Height, tex.MipCount, info.Size, newSize, a, info.InLayout, note);

        if (!tex.IsSupported)
        {
            return Make(PlanAction.Skip, info.Size, tex.UnsupportedReason);
        }

        if (tex.LongSide <= target)
        {
            return Make(PlanAction.Keep, info.Size, null);
        }

        string? correction = tex.MipCountCorrected
            ? $"Header declares {tex.DeclaredMipCount} mip levels, file contains {tex.MipCount}."
            : null;

        if (MipStripper.CanStrip(tex, target))
        {
            return Make(PlanAction.StripMips, MipStripper.PredictSize(tex, target), correction);
        }

        if (Reencoder.CanReencode(tex, target, out var reason))
        {
            return Make(PlanAction.Reencode, Reencoder.PredictSize(tex, target, generateMips), correction);
        }

        return Make(PlanAction.Skip, info.Size, reason);
    }
}
