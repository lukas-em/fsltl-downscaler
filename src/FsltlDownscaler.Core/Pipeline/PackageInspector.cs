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
    /// <summary>A run of this tool was interrupted. Restore is recommended.</summary>
    Interrupted,
}

public sealed record PackageStatus(
    PackageKind Kind,
    long PackageSize,
    int? Target,
    DateTime? WhenUtc,
    BackupSession? Restorable);

/// <summary>Works out what state a package is in, for a clear status line in the UI.</summary>
public static class PackageInspector
{
    public static PackageStatus Inspect(PackageContext pkg, ScanResult scan, string backupBase)
    {
        var (manifestText, _) = TextFiles.ReadUtf8(pkg.ManifestPath);
        long total = ManifestFile.ReadTotal(manifestText);

        var sessions = BackupSession.FindAll(backupBase, pkg.Root).ToList();
        var restorable = sessions.FirstOrDefault(s => s.Info.Status != "restored");

        if (sessions.Any(s => s.Info.Status == "running"))
        {
            return new PackageStatus(PackageKind.Interrupted, total, null, null, restorable);
        }

        string currentHash = BackupSession.Sha256(pkg.LayoutPath);
        var latest = sessions.FirstOrDefault(s => s.Info.Status is "completed" or "cancelled" or "failed");
        if (latest is not null && string.Equals(latest.Info.FinalLayoutSha256, currentHash, StringComparison.OrdinalIgnoreCase))
        {
            return new PackageStatus(PackageKind.DownscaledByTool, total, latest.Info.Target,
                latest.Info.FinishedUtc ?? latest.Info.StartedUtc, restorable);
        }

        int supported = scan.Supported;
        bool mostlySmall = supported > 0 && scan.LargerThan(1024) < supported * 0.02;
        return new PackageStatus(mostlySmall ? PackageKind.ReducedOther : PackageKind.Original, total, null, null, null);
    }
}
