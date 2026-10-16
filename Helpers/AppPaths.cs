using System.IO;

namespace WorkCheck.Helpers;

// User data lives outside the app folder, so rebuilds and publish.bat (which wipes its output) keep it
public static class AppPaths
{
    // WORKCHECK_DATA_DIR points a test run at a copy of the data instead of the real one
    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("WORKCHECK_DATA_DIR") is { Length: > 0 } dataDir
            ? dataDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkCheck");

    public static string Database => Path.Combine(DataDir, "statistics.db");
    public static string Settings => Path.Combine(DataDir, "settings.json");

    // Where settings.json was kept before it moved to DataDir
    public static string LegacySettings => Path.Combine(AppContext.BaseDirectory, "settings.json");
}
