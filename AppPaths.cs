namespace HueDesk;

public static class AppPaths
{
    public static readonly string DataDirectory = CreateDataDirectory();

    private static string CreateDataDirectory()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HueDesk");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
