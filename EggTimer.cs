namespace Deskwell;

public sealed record EggPreset(string Name, int Minutes)
{
    public TimeSpan Duration => TimeSpan.FromMinutes(Minutes);
}

public sealed class EggTimer : IDisposable
{
    private const int TickMilliseconds = 250;

    public static readonly IReadOnlyList<EggPreset> Presets =
    [
        new EggPreset("Soft", 4),
        new EggPreset("Medium", 6),
        new EggPreset("Hard", 9),
    ];

    private readonly System.Windows.Forms.Timer timer = new() { Interval = TickMilliseconds };
    private DateTime endTime;

    public EggTimer()
    {
        timer.Tick += (_, _) => OnTick();
    }

    public event Action? Changed;

    public event Action? Finished;

    public bool IsRunning { get; private set; }

    public TimeSpan Remaining => IsRunning ? TimeSpan.FromTicks(Math.Max(0, (endTime - DateTime.UtcNow).Ticks)) : TimeSpan.Zero;

    public void Start(TimeSpan duration)
    {
        endTime = DateTime.UtcNow + duration;
        IsRunning = true;
        timer.Start();
        Changed?.Invoke();
    }

    public void Cancel()
    {
        if (!IsRunning)
        {
            return;
        }

        Stop();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        timer.Dispose();
    }

    private void OnTick()
    {
        if (DateTime.UtcNow < endTime)
        {
            Changed?.Invoke();
            return;
        }

        Stop();
        Changed?.Invoke();
        Finished?.Invoke();
    }

    private void Stop()
    {
        timer.Stop();
        IsRunning = false;
    }
}
