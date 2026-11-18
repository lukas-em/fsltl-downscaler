using FsltlDownscaler.Package;

namespace FsltlDownscaler.Pipeline;

public sealed record VerifyResult(
    int Entries,
    int DdsEntries,
    int DdsMismatches,
    int OtherMismatches,
    int MissingFiles,
    long LayoutSum,
    long ManifestTotal,
    bool TotalMatchesEntries,
    bool TotalMatchesEntriesPlusMeta,
    List<string> DdsMismatchSamples);

/// <summary>
/// Compares layout.json with the files on disk.
/// DDS mismatches are errors. Mismatches of text files are reported separately,
/// because the original FSLTL package already ships with them.
/// </summary>
public static class Verifier
{
    public static VerifyResult Run(PackageContext pkg)
    {
        var layout = LayoutFile.Load(pkg.LayoutPath);
        var (manifestText, _) = TextFiles.ReadUtf8(pkg.ManifestPath);
        long total = ManifestFile.ReadTotal(manifestText);

        int dds = 0, ddsBad = 0, otherBad = 0, missing = 0;
        var samples = new List<string>();
        foreach (var (path, size) in layout.Entries)
        {
            string full = Path.Combine(pkg.Root, path.Replace('/', Path.DirectorySeparatorChar));
            bool isDds = path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);
            if (isDds)
            {
                dds++;
            }

            var fi = new FileInfo(full);
            if (!fi.Exists)
            {
                missing++;
                continue;
            }

            if (fi.Length != size)
            {
                if (isDds)
                {
                    ddsBad++;
                    if (samples.Count < 10)
                    {
                        samples.Add($"{path} (layout {size}, disk {fi.Length})");
                    }
                }
                else
                {
                    otherBad++;
                }
            }
        }

        long sum = layout.SumOfSizes();
        long meta = new FileInfo(pkg.LayoutPath).Length + new FileInfo(pkg.ManifestPath).Length;
        return new VerifyResult(layout.Count, dds, ddsBad, otherBad, missing, sum, total,
            total == sum, total == sum + meta, samples);
    }
}
