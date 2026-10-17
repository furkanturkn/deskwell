namespace HueDesk;

public static class ActivityLog
{
    private const int MaximumEntries = 10;

    private static readonly object WriteLock = new();
    private static readonly string LogFilePath = Path.Combine(AppPaths.DataDirectory, "activity.txt");

    public static void Write(string message)
    {
        var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        lock (WriteLock)
        {
            var entries = File.Exists(LogFilePath) ? File.ReadAllLines(LogFilePath).ToList() : [];
            entries.Add(entry);
            File.WriteAllLines(LogFilePath, entries.TakeLast(MaximumEntries));
        }
    }
}
