using FsltlDownscaler.Package;
using FsltlDownscaler.Safety;

namespace FsltlDownscaler.Pipeline;

public static class Restorer
{
    /// <summary>
    /// Moves every backed-up original back and restores layout.json and manifest.json.
    /// Refuses if the package was changed after the run (e.g. FSLTL update), unless forced.
    /// </summary>
    public static int Restore(PackageContext pkg, BackupSession session, bool force, Action<string> log)
    {
        if (session.Info.Status == "restored")
        {
            throw new UserException("This backup has already been restored.");
        }

        if (session.Info.FinalLayoutSha256 is { } finalHash && !force)
        {
            string current = BackupSession.Sha256(pkg.LayoutPath);
            if (!string.Equals(current, finalHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new UserException(
                    "layout.json changed after the downscale run (FSLTL update?). " +
                    "Restoring would mix versions. Use --force only if you are sure.");
            }
        }

        string metaLayout = Path.Combine(session.MetaDir, "layout.json");
        string metaManifest = Path.Combine(session.MetaDir, "manifest.json");
        if (!File.Exists(metaLayout) || !File.Exists(metaManifest))
        {
            throw new UserException("Backup is incomplete: original layout.json or manifest.json missing.");
        }

        if (session.IsFromOtherFolder(pkg))
        {
            log($"The backup was made in {session.Info.PackageRoot}. The package has moved, restoring to {pkg.Root}.");
        }

        var files = session.BackedUpFiles().ToList();
        bool sameVolume = PathResolver.SameVolume(session.FilesDir, pkg.Root);
        if (!sameVolume)
        {
            // Across volumes the originals are copied back. Check the space up front instead of failing halfway.
            long needed = files.Sum(f => new FileInfo(f).Length);
            needed += needed / 10;
            long free = PathResolver.FreeBytes(pkg.Root);
            if (free < needed)
            {
                throw new UserException(
                    $"There isn't enough free disk space to restore. Needed: {needed / 1_000_000_000d:0.0} GB, " +
                    $"available: {free / 1_000_000_000d:0.0} GB on the drive of {pkg.Root}.");
            }
        }

        int restored = 0;
        foreach (var backup in files)
        {
            string rel = Path.GetRelativePath(session.FilesDir, backup);
            string target = Path.Combine(pkg.Root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            BackupSession.MoveOver(backup, target, sameVolume);
            restored++;
            if (restored % 250 == 0)
            {
                log($"  {restored} files restored");
            }
        }

        File.Copy(metaLayout, pkg.LayoutPath, overwrite: true);
        File.Copy(metaManifest, pkg.ManifestPath, overwrite: true);

        session.Info.Status = "restored";
        session.Save();
        session.Journal($"restored\t{restored} files");
        return restored;
    }
}
