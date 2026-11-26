using System.Text.Json;

namespace FsltlDownscaler.Safety;

/// <summary>
/// Remembers every backup base folder the tool has created, in %APPDATA%\FSLTL-Downscaler\backup-locations.json.
/// This finds a backup again after the user moved the package to another folder or drive.
/// Only folder paths are stored, never anything from the package. Best effort: errors are ignored.
/// </summary>
public static class BackupLocations
{
    private static readonly object Lock = new();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FSLTL-Downscaler", "backup-locations.json");

    public static IReadOnlyList<string> Load()
    {
        lock (Lock)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [];
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Broken or unreadable: the default locations are still searched.
            }

            return [];
        }
    }

    public static void Remember(string baseDir)
    {
        lock (Lock)
        {
            try
            {
                string full = Path.GetFullPath(baseDir);
                var list = Load().ToList();
                if (list.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    return;
                }

                list.Add(full);
                Save(list);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Not critical.
            }
        }
    }

    /// <summary>Drops folders that no longer exist, e.g. after the last backup in them was deleted.</summary>
    public static void Prune()
    {
        lock (Lock)
        {
            try
            {
                var list = Load().ToList();
                int removed = list.RemoveAll(d => !Directory.Exists(d));
                if (removed > 0)
                {
                    Save(list);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not critical.
            }
        }
    }

    private static void Save(List<string> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
