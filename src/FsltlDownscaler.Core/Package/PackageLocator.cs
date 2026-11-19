using System.Text.RegularExpressions;

namespace FsltlDownscaler.Package;

/// <summary>An fsltl-traffic-base folder found through the settings of MSFS 2020 or MSFS 2024.</summary>
/// <param name="Path">The folder inside the Community folder. Can be a link.</param>
/// <param name="RealPath">The real folder behind <paramref name="Path"/>.</param>
/// <param name="Simulator">"MSFS 2024", "MSFS 2020" or both, if they share one package.</param>
public sealed record PackageCandidate(string Path, string RealPath, string Simulator);

/// <summary>
/// Finds the fsltl-traffic-base folder via UserCfg.opt (InstalledPackagesPath) of MSFS 2024 and MSFS 2020.
/// Only a suggestion. The user can always pick another folder.
/// </summary>
public static partial class PackageLocator
{
    public const string FolderName = "fsltl-traffic-base";

    [GeneratedRegex("^\\s*InstalledPackagesPath\\s+\"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex InstalledPathRegex();

    /// <summary>MSFS 2024 first, then MSFS 2020. Packages shared by both simulators are listed once.</summary>
    public static IReadOnlyList<PackageCandidate> FindCandidates()
    {
        var result = new List<PackageCandidate>();
        foreach (var (sim, cfg) in UserCfgLocations())
        {
            foreach (string path in PackagePathsFrom(cfg))
            {
                if (!File.Exists(System.IO.Path.Combine(path, "manifest.json")))
                {
                    continue;
                }

                string real = PathResolver.TryResolveFinal(path);
                int existing = result.FindIndex(c => string.Equals(c.RealPath, real, StringComparison.OrdinalIgnoreCase));
                if (existing < 0)
                {
                    result.Add(new PackageCandidate(path, real, sim));
                }
                else if (!result[existing].Simulator.Contains(sim, StringComparison.Ordinal))
                {
                    result[existing] = result[existing] with { Simulator = $"{result[existing].Simulator} / {sim}" };
                }
            }
        }

        return result;
    }

    /// <summary>The simulator label for a folder, if it is one of the detected packages.</summary>
    public static string? SimulatorFor(string packageRoot, IEnumerable<PackageCandidate> candidates)
    {
        string real = PathResolver.TryResolveFinal(packageRoot);
        return candidates.FirstOrDefault(c => string.Equals(c.RealPath, real, StringComparison.OrdinalIgnoreCase))?.Simulator;
    }

    private static IEnumerable<string> PackagePathsFrom(string cfg)
    {
        var paths = new List<string>();
        try
        {
            if (!File.Exists(cfg))
            {
                return paths;
            }

            var match = InstalledPathRegex().Match(File.ReadAllText(cfg));
            if (match.Success)
            {
                string packages = match.Groups[1].Value;
                paths.Add(System.IO.Path.Combine(packages, "Community", FolderName));
                paths.Add(System.IO.Path.Combine(packages, "Community2024", FolderName));
            }

            // Default when InstalledPackagesPath is not set.
            paths.Add(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(cfg)!, "Packages", "Community", FolderName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Best effort only.
        }

        return paths;
    }

    private static IEnumerable<(string Simulator, string Cfg)> UserCfgLocations()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // MSFS 2024: Steam, then Microsoft Store / Xbox app
        yield return ("MSFS 2024", System.IO.Path.Combine(roaming, "Microsoft Flight Simulator 2024", "UserCfg.opt"));
        yield return ("MSFS 2024", System.IO.Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"));

        // MSFS 2020: Steam, then Microsoft Store / Xbox app
        yield return ("MSFS 2020", System.IO.Path.Combine(roaming, "Microsoft Flight Simulator", "UserCfg.opt"));
        yield return ("MSFS 2020", System.IO.Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt"));
    }
}
