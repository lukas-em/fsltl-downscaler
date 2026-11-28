using FsltlDownscaler.Dds;
using FsltlDownscaler.Package;
using FsltlDownscaler.Safety;

namespace FsltlDownscaler.Pipeline;

/// <summary>
/// For an interrupted run without backup. The originals are gone, so there is nothing to restore.
/// Instead layout.json and manifest.json are brought in line with the textures on disk, like a finished run does.
/// </summary>
public static class Repairer
{
    public static int Repair(PackageContext pkg, BackupSession session, Action<string> log)
    {
        if (!session.IsNoBackup)
        {
            throw new UserException("This run has a backup. Please use \"Restore original\" instead.");
        }

        string metaLayout = Path.Combine(session.MetaDir, "layout.json");
        string metaManifest = Path.Combine(session.MetaDir, "manifest.json");
        if (!File.Exists(metaLayout) || !File.Exists(metaManifest))
        {
            throw new UserException("The copy of layout.json or manifest.json from before the run is missing. Please reinstall FSLTL.");
        }

        // Totals as they were before the run. They tell whether total_package_size includes layout.json and manifest.json.
        var before = LayoutFile.Load(metaLayout);
        long beforeSum = before.SumOfSizes();
        long beforeLayoutBytes = new FileInfo(metaLayout).Length;
        var (beforeManifestText, _) = TextFiles.ReadUtf8(metaManifest);
        long beforeTotal = ManifestFile.ReadTotal(beforeManifestText);
        bool totalIncludesMeta = beforeTotal == beforeSum + beforeLayoutBytes + new FileInfo(metaManifest).Length;

        // Only DDS entries whose size differs from the file on disk. FSLTL ships all DDS sizes exactly,
        // so these are the textures the interrupted run replaced.
        var layout = LayoutFile.Load(pkg.LayoutPath);
        var broken = new List<string>();
        int updated = 0;
        foreach (var (path, size) in layout.Entries.ToList())
        {
            if (!path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fi = new FileInfo(Path.Combine(pkg.Root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!fi.Exists || fi.Length == size)
            {
                continue;
            }

            try
            {
                var tex = DdsTexture.ReadHeader(fi.FullName);
                if (!tex.IsSupported)
                {
                    throw new DdsException(tex.UnsupportedReason ?? "unknown format");
                }
            }
            catch (Exception ex) when (ex is DdsException or IOException)
            {
                broken.Add($"{path}: {ex.Message}");
                continue;
            }

            layout.Update(path, fi.Length, fi.LastWriteTimeUtc.ToFileTimeUtc());
            updated++;
        }

        if (broken.Count > 0)
        {
            foreach (string b in broken.Take(20))
            {
                log($"  Damaged: {b}");
            }

            throw new UserException(
                $"{broken.Count:N0} textures were damaged when the run stopped. They can't be repaired without the originals. " +
                "Please reinstall FSLTL with its installer.");
        }

        byte[] newLayout = layout.Serialize();
        long newTotal = beforeTotal + (layout.SumOfSizes() - beforeSum);
        if (totalIncludesMeta)
        {
            newTotal += newLayout.LongLength - beforeLayoutBytes;
        }

        var (manifestText, manifestBom) = TextFiles.ReadUtf8(pkg.ManifestPath);
        byte[] newManifest = TextFiles.EncodeUtf8(ManifestFile.WithTotal(manifestText, newTotal), manifestBom);
        session.WriteMetaFile(pkg.LayoutPath, newLayout);
        session.WriteMetaFile(pkg.ManifestPath, newManifest);

        int replaced = File.Exists(session.JournalPath)
            ? File.ReadLines(session.JournalPath).Count(l => l.Contains("\treplaced\t", StringComparison.Ordinal))
            : updated;
        session.Info.Status = "cancelled";
        session.Info.FinishedUtc ??= DateTime.UtcNow;
        session.Info.FilesReplaced = replaced;
        session.Info.FinalLayoutSha256 = BackupSession.Sha256(pkg.LayoutPath);
        session.Save();
        session.Journal($"repaired\t{updated} layout entries");
        session.CleanupTmp();
        log($"layout.json repaired: {updated:N0} entries updated, {replaced:N0} textures were downscaled before the run stopped.");
        return updated;
    }
}
