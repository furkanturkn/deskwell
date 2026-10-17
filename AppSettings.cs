using System.Text.Json;

namespace HueDesk;

public sealed class AppSettings
{
    private static readonly string SettingsFilePath = Path.Combine(AppPaths.DataDirectory, "settings.json");

    public bool AutoScenesEnabled { get; set; } = true;

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath)) ?? new AppSettings();
    }

    public void Save()
    {
        File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
