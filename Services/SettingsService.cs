using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WorkCheck.Helpers;
using WorkCheck.Models;

namespace WorkCheck.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        var path = AppPaths.Settings;
        if (!File.Exists(path))
            MigrateLegacySettings(path);
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.Settings, json);
    }

    // Older versions kept settings.json next to the exe; the first run of this one copies it over
    private static void MigrateLegacySettings(string path)
    {
        if (!File.Exists(AppPaths.LegacySettings)) return;

        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.Copy(AppPaths.LegacySettings, path);
            Debug.WriteLine($"[Settings] Перенесены из {AppPaths.LegacySettings}");
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[Settings] Не удалось перенести настройки: {ex.Message}");
        }
    }
}
