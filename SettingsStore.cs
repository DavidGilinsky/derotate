using System.IO;
using System.Text.Json;

namespace Derotate;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as <c>settings.json</c> in the
/// directory containing the executable.
/// </summary>
public static class SettingsStore
{
    /// <summary>Absolute path of the settings file.</summary>
    public static string FilePath { get; } =
        Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Load settings from disk. Missing file or unreadable JSON yields a fresh
    /// <see cref="AppSettings"/> with defaults; we never throw to the caller.
    /// </summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            // Corrupt file — fall back to defaults rather than wedge startup.
            return new AppSettings();
        }
    }

    /// <summary>Persist <paramref name="settings"/> to disk. Throws on I/O errors.</summary>
    public static void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(FilePath, json);
    }
}
