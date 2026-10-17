namespace HueDesk;

public sealed class PurifierBoostScheduler : IDisposable
{
    private const int CheckIntervalMilliseconds = 20_000;

    private static readonly TimeOnly[] BoostTimes = [new(8, 0), new(2, 0)];
    private static readonly TimeSpan BoostDuration = TimeSpan.FromMinutes(15);

    private readonly AirPurifier purifier;
    private readonly AppSettings settings;
    private readonly System.Windows.Forms.Timer checkTimer = new() { Interval = CheckIntervalMilliseconds };
    private bool isChecking;

    public PurifierBoostScheduler(AirPurifier purifier, AppSettings settings)
    {
        this.purifier = purifier;
        this.settings = settings;
        checkTimer.Tick += async (_, _) => await Check();
        checkTimer.Start();
        _ = Check();
    }

    public event Action? Changed;

    public static string Describe(DateTime now, DateTime? boostEndsAt)
    {
        if (boostEndsAt is { } endsAt)
        {
            return $"Boost until {endsAt:HH:mm}, then back to Auto";
        }

        var nextStart = BoostTimes.Select(time => NextOccurrence(now, time)).Min();
        return $"Next boost {nextStart:HH:mm} ({BoostDuration.TotalMinutes:0} min Manual, then Auto)";
    }

    public void Dispose()
    {
        checkTimer.Dispose();
    }

    private static DateTime LatestOccurrence(DateTime now, TimeOnly time)
    {
        var candidate = now.Date + time.ToTimeSpan();
        return candidate > now ? candidate.AddDays(-1) : candidate;
    }

    private static DateTime NextOccurrence(DateTime now, TimeOnly time)
    {
        var candidate = now.Date + time.ToTimeSpan();
        return candidate > now ? candidate : candidate.AddDays(1);
    }

    private async Task Check()
    {
        if (isChecking)
        {
            return;
        }

        isChecking = true;
        try
        {
            var now = DateTime.Now;
            if (settings.PurifierBoostEndsAt is { } endsAt)
            {
                await FinishBoost(now, endsAt);
                return;
            }

            await StartBoostIfDue(now);
        }
        catch (Exception exception)
        {
            Logger.Write("purifier: scheduled boost failed", exception);
        }
        finally
        {
            isChecking = false;
        }
    }

    private async Task FinishBoost(DateTime now, DateTime endsAt)
    {
        if (now < endsAt)
        {
            return;
        }

        await purifier.SetMode(PurifierMode.Auto);
        ActivityLog.Write("Purifier boost finished, back to Auto");
        settings.PurifierBoostEndsAt = null;
        settings.Save();
        Changed?.Invoke();
    }

    private async Task StartBoostIfDue(DateTime now)
    {
        var latestStart = BoostTimes.Select(time => LatestOccurrence(now, time)).Max();
        var isDue = now - latestStart < BoostDuration && settings.PurifierBoostStartedAt != latestStart;
        if (!isDue)
        {
            return;
        }

        await purifier.SetPower(true);
        await purifier.SetMode(PurifierMode.Manual);
        ActivityLog.Write($"Purifier boost started, Manual for {BoostDuration.TotalMinutes:0} min");
        settings.PurifierBoostStartedAt = latestStart;
        settings.PurifierBoostEndsAt = latestStart + BoostDuration;
        settings.Save();
        Changed?.Invoke();
    }
}
