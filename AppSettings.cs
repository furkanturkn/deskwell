using System.Text.Json;

namespace HueDesk;

public sealed class AppSettings
{
    private const string SettingsFileName = "settings.json";

    private static readonly string SettingsFilePath = Path.Combine(AppPaths.DataDirectory, SettingsFileName);

    public bool AutoScenesEnabled { get; set; } = true;

    public string? LastAppliedSceneName { get; set; }

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
