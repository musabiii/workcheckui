using System.IO;
using System.Text.Json;
using WorkCheck.Models;

namespace WorkCheck.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string GetSettingsPath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public AppSettings Load()
    {
        var path = GetSettingsPath();
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
        var path = GetSettingsPath();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }
}
