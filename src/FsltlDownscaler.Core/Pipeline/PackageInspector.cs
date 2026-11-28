using FsltlDownscaler.Package;
using FsltlDownscaler.Safety;

namespace FsltlDownscaler.Pipeline;

public enum PackageKind
{
    /// <summary>Full-size textures, as installed by FSLTL.</summary>
    Original,
    /// <summary>Downscaled by this tool. A matching backup exists.</summary>
    DownscaledByTool,
    /// <summary>Textures are already small, but not by this tool (or the backup is gone).</summary>
    ReducedOther,
    /// <summary>A run of this tool was interrupted or could not update layout.json. Restore is recommended.</summary>
    Interrupted,
}

public sealed record PackageStatus(
    PackageKind Kind,
    long PackageSize,
    int? Target,
    DateTime? WhenUtc,
    BackupSession? Restorable)
{
    /// <summary>Backups of this package that still hold original textures, newest first.</summary>
    public IReadOnlyList<BackupSession> Backups { get; init; } = [];

    /// <summary>Bytes these backups take on disk.</summary>
    public long BackupBytes { get; init; }

    /// <summary>Original textures in these backups.</summary>
    public int BackupFiles { get; init; }

    /// <summary>
    /// Folders of runs that were already restored. They only hold a copy of the old layout.json and the journal,
    /// no textures. Not shown to the user as backups and removed quietly.
    /// </summary>
    public IReadOnlyList<BackupSession> Leftovers { get; init; } = [];

    /// <summary>
    /// Backups from before an FSLTL update or reinstall. The package no longer matches them,
    /// so they can't be restored safely. They only take up disk space.
    /// </summary>
    public IReadOnlyList<BackupSession> Outdated { get; init; } = [];

    /// <summary>Bytes the outdated backups take on disk.</summary>
    public long OutdatedBytes { get; init; }

    /// <summary>The package version the outdated backups were made of, e.g. "1.7.0".</summary>
    public string? OutdatedVersion => Outdated.FirstOrDefault()?.Info.PackageVersion;

    /// <summary>Set when the restorable backup was made while the package was in another folder.</summary>
    public string? MovedFrom { get; init; }

    /// <summary>An interrupted run without backup. It can't be restored, but layout.json can be repaired.</summary>
    public BackupSession? Repairable { get; init; }

    /// <summary>The last run was made without a backup. "Restore original" is not possible.</summary>
    public bool LastRunWithoutBackup { get; init; }

    /// <summary>Backups that keep their originals in a folder and could be packed into a ZIP file.</summary>
    public IEnumerable<BackupSession> Compressible =>
        Backups.Where(b => !b.IsZip && b.Info.FilesReplaced > 0 && b.Info.Status is "completed" or "cancelled" or "failed");
}

/// <summary>Works out what state a package is in, for a clear status line in the UI.</summary>
public static class PackageInspector
{
    public static PackageStatus Inspect(PackageContext pkg, ScanResult scan)
    {
        var (manifestText, _) = TextFiles.ReadUtf8(pkg.ManifestPath);
        long total = ManifestFile.ReadTotal(manifestText);
        string currentHash = BackupSession.Sha256(pkg.LayoutPath);

        var sessions = BackupSession.FindForPackage(pkg, currentHash);
        var sizes = sessions.ToDictionary(s => s.Dir, s => s.SizeBytes(), StringComparer.OrdinalIgnoreCase);

        // Runs without backup hold no originals. They only tell what was done to the package.
        var real = sessions.Where(s => s.Info.Status != "restored" && !s.IsNoBackup).ToList();
        var leftovers = sessions.Where(s => s.Info.Status == "restored").ToList();
        long backupBytes = real.Sum(s => sizes[s.Dir]);

        PackageStatus Make(PackageKind kind, int? target, DateTime? when, BackupSession? r, IReadOnlyList<BackupSession> outdated,
            IReadOnlyList<BackupSession> unusedRuns, BackupSession? repairable = null, bool noBackup = false) =>
            new(kind, total, target, when, r)
            {
                Backups = real,
                BackupBytes = backupBytes,
                BackupFiles = real.Sum(s => s.Info.FilesReplaced),
                Leftovers = [.. leftovers, .. unusedRuns],
                Outdated = outdated,
                OutdatedBytes = outdated.Sum(s => sizes[s.Dir]),
                MovedFrom = r is not null && r.IsFromOtherFolder(pkg) ? r.Info.PackageRoot : null,
                Repairable = repairable,
                LastRunWithoutBackup = noBackup,
            };

        var interrupted = sessions.FirstOrDefault(s => s.Info.Status is "running" or "incomplete");
        if (interrupted is not null)
        {
            return interrupted.IsNoBackup
                ? Make(PackageKind.Interrupted, null, null, null, [], [], repairable: interrupted, noBackup: true)
                : Make(PackageKind.Interrupted, null, null, interrupted, [], []);
        }

        var latest = sessions.FirstOrDefault(s => s.Info.Status is "completed" or "cancelled" or "failed");
        if (latest is not null && string.Equals(latest.Info.FinalLayoutSha256, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            // Restore walks back one run at a time. A run without backup ends the way back.
            var older = OlderThanChain(sessions, latest);
            return Make(PackageKind.DownscaledByTool, latest.Info.Target, latest.Info.FinishedUtc ?? latest.Info.StartedUtc,
                latest.IsNoBackup ? null : latest,
                older.Where(s => !s.IsNoBackup).ToList(), older.Where(s => s.IsNoBackup).ToList(), noBackup: latest.IsNoBackup);
        }

        // The package doesn't match the last run any more. layout.json only changes like this when
        // FSLTL was updated or reinstalled. The originals in these backups belong to the old installation.
        var finished = sessions.Where(s => s.Info.Status is "completed" or "cancelled" or "failed").ToList();

        int supported = scan.Supported;
        bool mostlySmall = supported > 0 && scan.LargerThan(1024) < supported * 0.02;
        return Make(mostlySmall ? PackageKind.ReducedOther : PackageKind.Original, null, null, null,
            finished.Where(s => !s.IsNoBackup).ToList(), finished.Where(s => s.IsNoBackup).ToList());
    }

    /// <summary>
    /// Runs on top of each other (e.g. 2048 px, later 1024 px) form a chain: each run started from the
    /// layout.json the one before it left behind. Restore walks back along this chain. Backups that are not
    /// part of it are from before an FSLTL update and can't be restored any more.
    /// </summary>
    private static List<BackupSession> OlderThanChain(List<BackupSession> sessions, BackupSession latest)
    {
        var candidates = sessions
            .Where(s => s != latest && s.Info.Status is "completed" or "cancelled" or "failed")
            .OrderByDescending(s => s.Info.StartedUtc)
            .ToList();

        var chain = new HashSet<BackupSession> { latest };
        string expected = latest.Info.OriginalLayoutSha256;
        foreach (var s in candidates.Where(s => s.Info.StartedUtc < latest.Info.StartedUtc))
        {
            if (!string.Equals(s.Info.FinalLayoutSha256, expected, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            chain.Add(s);
            expected = s.Info.OriginalLayoutSha256;
        }

        return candidates.Where(s => !chain.Contains(s)).ToList();
    }
}
