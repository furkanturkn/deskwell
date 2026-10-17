namespace HueDesk;

public static class Logger
{
    private static readonly object WriteLock = new();
    private static readonly string LogFilePath = Path.Combine(AppPaths.DataDirectory, "log.txt");

    public static void Write(string message, Exception? exception = null)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{(exception is null ? string.Empty : Environment.NewLine + exception)}";
        lock (WriteLock)
        {
            File.AppendAllText(LogFilePath, line + Environment.NewLine);
        }
    }
}
