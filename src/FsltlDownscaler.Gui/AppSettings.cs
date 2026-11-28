using System.Text.Json;
using System.Text.Json.Serialization;
using FsltlDownscaler.Safety;

namespace FsltlDownscaler.Gui;

/// <summary>Small per-user settings file in %APPDATA%\FSLTL-Downscaler. Never inside the FSLTL package.</summary>
internal sealed class AppSettings
{
    /// <summary>Raise this when the disclaimer text changes in a way users should read again.</summary>
    public const int CurrentDisclaimerVersion = 1;

    public int DisclaimerAcceptedVersion { get; set; }

    /// <summary>Backup choice under Advanced options. Remembered for the next start.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<BackupMode>))]
    public BackupMode BackupMode { get; set; } = BackupMode.Folder;

    /// <summary>The user has picked a backup mode, either before the first downscale or under Advanced options.</summary>
    public bool BackupModeChosen { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FSLTL-Downscaler", "settings.json");

    public bool DisclaimerAccepted => DisclaimerAcceptedVersion >= CurrentDisclaimerVersion;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Broken or unreadable settings: start fresh.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not critical: the notice is shown again next time.
        }
    }
}
