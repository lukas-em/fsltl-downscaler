using System.Diagnostics;
using FsltlDownscaler.Package;
using FsltlDownscaler.Safety;

namespace FsltlDownscaler.Pipeline;

public static class SystemChecks
{
    private static readonly string[] SimProcessNames = ["FlightSimulator2024", "FlightSimulator"];

    public static void EnsureSimulatorNotRunning()
    {
        foreach (var name in SimProcessNames)
        {
            var procs = Process.GetProcessesByName(name);
            try
            {
                if (procs.Length > 0)
                {
                    throw new UserException("Microsoft Flight Simulator is running. Please close it first.");
                }
            }
            finally
            {
                foreach (var p in procs)
                {
                    p.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Same volume: originals are moved, only the new files need space.
    /// Other volume: originals are copied over as well.
    /// ZIP: the ZIP file is written next to the originals before they are removed.
    /// No backup: only the textures in progress need space for a moment.
    /// Volumes are compared by their mount point, so a drive mounted into a folder counts as its own volume.
    /// </summary>
    public static (long Required, long Available) CheckSpace(string packageRoot, string backupDir, Plan plan,
        BackupMode mode = BackupMode.Folder)
    {
        bool sameVolume = PathResolver.SameVolume(packageRoot, PathResolver.NearestExisting(backupDir));

        long newBytes = plan.ToProcess.Sum(i => i.NewSize);
        long oldBytes = plan.ToProcess.Sum(i => i.OldSize);
        long metaBytes = 64L * 1024 * 1024;
        long required = mode switch
        {
            BackupMode.None => metaBytes + plan.ToProcess.Select(i => i.NewSize).OrderByDescending(n => n)
                .Take(Math.Max(2, Environment.ProcessorCount)).Sum(),
            _ => (sameVolume ? newBytes : newBytes + oldBytes) + metaBytes
                 + (mode == BackupMode.Zip ? BackupSession.EstimateZipBytes(oldBytes) : 0),
        };
        required += required / 10; // 10 % margin

        long available = PathResolver.FreeBytes(backupDir);
        return (required, available);
    }
}
