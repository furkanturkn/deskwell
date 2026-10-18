namespace Deskwell;

public static class AppPaths
{
    public static readonly string DataDirectory = CreateDataDirectory();

    private static string CreateDataDirectory()
    {
        var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(applicationData, "Deskwell");
        var legacyDirectory = Path.Combine(applicationData, "HueDesk");
        if (!Directory.Exists(directory) && Directory.Exists(legacyDirectory))
        {
            try
            {
                Directory.Move(legacyDirectory, directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                File.AppendAllText(Path.Combine(legacyDirectory, "log.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} Could not move data to {directory}, using the old folder{Environment.NewLine}{exception}{Environment.NewLine}");
                return legacyDirectory;
            }
        }

        Directory.CreateDirectory(directory);
        return directory;
    }
}
