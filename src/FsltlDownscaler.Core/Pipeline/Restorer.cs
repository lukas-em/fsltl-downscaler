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

        int restored = 0;
        bool sameVolume = PathResolver.SameVolume(session.FilesDir, pkg.Root);
        foreach (var backup in session.BackedUpFiles().ToList())
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
