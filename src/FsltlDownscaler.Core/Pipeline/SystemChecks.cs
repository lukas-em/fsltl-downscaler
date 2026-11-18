using System.Diagnostics;
using FsltlDownscaler.Package;

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
    /// Same drive: originals are moved, only the new files need space.
    /// Other drive: originals are copied over as well.
    /// </summary>
    public static (long Required, long Available) CheckSpace(string packageRoot, string backupDir, Plan plan)
    {
        string pkgDrive = Path.GetPathRoot(Path.GetFullPath(packageRoot)) ?? string.Empty;
        string bakDrive = Path.GetPathRoot(Path.GetFullPath(backupDir)) ?? string.Empty;
        bool sameDrive = string.Equals(pkgDrive, bakDrive, StringComparison.OrdinalIgnoreCase);

        long newBytes = plan.ToProcess.Sum(i => i.NewSize);
        long oldBytes = plan.ToProcess.Sum(i => i.OldSize);
        long metaBytes = 64L * 1024 * 1024;
        long required = sameDrive ? newBytes + metaBytes : newBytes + oldBytes + metaBytes;
        required += required / 10; // 10 % margin

        long available = new DriveInfo(bakDrive).AvailableFreeSpace;
        return (required, available);
    }
}
