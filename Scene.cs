namespace Deskwell;

public sealed record Scene(string Name, byte Brightness, ushort? TemperatureMirek, double? Hue, double? Saturation, TimeOnly? AutoStart);

public static class Scenes
{
    public static readonly Scene Morning = new("Morning", 200, 350, null, null, new TimeOnly(6, 0));
    public static readonly Scene Work = new("Work", 254, 300, null, null, new TimeOnly(9, 0));
    public static readonly Scene Gaming = new("Gaming", 130, null, 265, 0.85, new TimeOnly(18, 0));
    public static readonly Scene Night = new("Night", 12, 500, null, null, new TimeOnly(23, 0));
    public static readonly Scene Relax = new("Relax", 110, 460, null, null, null);

    public static readonly IReadOnlyList<Scene> All = [Morning, Work, Gaming, Relax, Night];

    private static readonly IReadOnlyList<Scene> Scheduled = All
        .Where(scene => scene.AutoStart is not null)
        .OrderBy(scene => scene.AutoStart)
        .ToList();

    public static Scene ScheduledAt(TimeOnly time)
    {
        return Scheduled.LastOrDefault(scene => scene.AutoStart <= time) ?? Scheduled[^1];
    }
}
