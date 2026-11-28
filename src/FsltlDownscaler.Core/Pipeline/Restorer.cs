using System.IO.Compression;
using FsltlDownscaler.Package;
using FsltlDownscaler.Safety;

namespace FsltlDownscaler.Pipeline;

public static class Restorer
{
    /// <summary>
    /// Puts every backed-up original back (from files/ or files.zip) and restores layout.json and manifest.json.
    /// Refuses if the package was changed after the run (e.g. FSLTL update), unless forced.
    /// </summary>
    public static int Restore(PackageContext pkg, BackupSession session, bool force, Action<string> log,
        IProgress<(int Done, int Total)>? progress = null)
    {
        if (session.Info.Status == "restored")
        {
            throw new UserException("This backup has already been restored.");
        }

        if (session.IsNoBackup)
        {
            throw new UserException(
                "This run was made without a backup, so the original textures can't be restored. " +
                "To get them back, reinstall FSLTL with its installer.");
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

        session.TidyUp();
        bool fromZip = session.IsZip && File.Exists(session.ZipPath);
        int restored = fromZip ? RestoreFromZip(pkg, session, log, progress) : RestoreFromFolder(pkg, session, log, progress);

        File.Copy(metaLayout, pkg.LayoutPath, overwrite: true);
        File.Copy(metaManifest, pkg.ManifestPath, overwrite: true);

        session.Info.Status = "restored";
        session.Save();
        session.Journal($"restored\t{restored} files");

        if (fromZip)
        {
            // Every original is back in the package, the ZIP is no longer needed.
            session.TidyUp();
            log("The ZIP backup was removed after restoring.");
        }

        return restored;
    }

    private static int RestoreFromFolder(PackageContext pkg, BackupSession session, Action<string> log,
        IProgress<(int Done, int Total)>? progress)
    {
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
        progress?.Report((0, files.Count));
        foreach (var backup in files)
        {
            string rel = Path.GetRelativePath(session.FilesDir, backup);
            string target = Path.Combine(pkg.Root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            BackupSession.MoveOver(backup, target, sameVolume);
            restored++;
            if (restored % 10 == 0 || restored == files.Count)
            {
                progress?.Report((restored, files.Count));
            }

            if (restored % 250 == 0)
            {
                log($"  {restored} files restored");
            }
        }

        return restored;
    }

    /// <summary>
    /// Extracts every original to the tmp folder of the backup and moves it over the downscaled texture.
    /// Can be run again after an interruption: files that are already back are simply written once more.
    /// </summary>
    private static int RestoreFromZip(PackageContext pkg, BackupSession session, Action<string> log,
        IProgress<(int Done, int Total)>? progress)
    {
        using var zip = ZipFile.OpenRead(session.ZipPath);
        var times = session.ReadZipIndex();
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

        // The originals need their full size on the drive of the package, minus the downscaled files they replace.
        long needed = 0;
        foreach (var e in entries)
        {
            string target = TargetFor(pkg, e.FullName);
            long current = File.Exists(target) ? new FileInfo(target).Length : 0;
            needed += Math.Max(0, e.Length - current);
        }

        needed += needed / 10 + 64L * 1024 * 1024;
        long free = PathResolver.FreeBytes(pkg.Root);
        if (free < needed)
        {
            throw new UserException(
                $"There isn't enough free disk space to unpack the original textures. Needed: {needed / 1_000_000_000d:0.0} GB, " +
                $"available: {free / 1_000_000_000d:0.0} GB on the drive of {pkg.Root}. " +
                "Free up some space or reinstall FSLTL with its installer.");
        }

        Directory.CreateDirectory(session.TmpDir);
        bool sameVolume = PathResolver.SameVolume(session.TmpDir, pkg.Root);
        log($"Unpacking {entries.Count:N0} original textures from {session.ZipPath} ...");
        int restored = 0;
        progress?.Report((0, entries.Count));
        foreach (var e in entries)
        {
            string target = TargetFor(pkg, e.FullName);
            string tmp = Path.Combine(session.TmpDir, Guid.NewGuid().ToString("N") + ".dds");
            e.ExtractToFile(tmp, overwrite: true);
            if (new FileInfo(tmp).Length != e.Length)
            {
                File.Delete(tmp);
                throw new IOException($"{e.FullName} could not be unpacked completely.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            BackupSession.ClearReadOnly(target);
            BackupSession.MoveOver(tmp, target, sameVolume);
            if (times.TryGetValue(e.FullName, out long fileTime))
            {
                File.SetLastWriteTimeUtc(target, DateTime.FromFileTimeUtc(fileTime));
            }

            restored++;
            if (restored % 10 == 0 || restored == entries.Count)
            {
                progress?.Report((restored, entries.Count));
            }

            if (restored % 250 == 0)
            {
                log($"  {restored} files restored");
            }
        }

        session.CleanupTmp();
        return restored;
    }

    /// <summary>Path in the package for a ZIP entry. Entries that would point outside the package are refused.</summary>
    private static string TargetFor(PackageContext pkg, string entryName)
    {
        string target = Path.GetFullPath(Path.Combine(pkg.Root, entryName.Replace('/', Path.DirectorySeparatorChar)));
        if (!BackupSession.IsInside(target, pkg.Root))
        {
            throw new UserException($"The ZIP backup contains an invalid path: {entryName}. Nothing more was restored.");
        }

        return target;
    }
}
