using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Tavrynexo.Core;

public sealed class AppSettings
{
    public string Language { get; set; } = "";
    public bool CreateRestorePoint { get; set; } = true;
    public bool BackupRegistry { get; set; } = true;

    public static AppSettings Current { get; private set; } = new();

    private static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) { Logger.Error("Settings load failed", ex); }

        if (Current.Language != "ar" && Current.Language != "en")
            Current.Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "ar" : "en";
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Logger.Error("Settings save failed", ex); }
    }
}
