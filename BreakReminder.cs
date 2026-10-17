namespace HueDesk;

public sealed class BreakReminder : IDisposable
{
    private const int CheckIntervalMilliseconds = 30_000;
    private const int BlinkCount = 3;
    private const int BlinkStepMilliseconds = 450;

    private static readonly TimeOnly WorkdayStart = new(9, 0);
    private static readonly TimeOnly WorkdayEnd = new(18, 0);
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(50);

    private readonly Func<IEnumerable<HueLight>> connectedLights;
    private readonly Func<bool> isPaused;
    private readonly System.Windows.Forms.Timer checkTimer = new() { Interval = CheckIntervalMilliseconds };
    private DateTime? nextReminderAt;
    private bool isBlinking;

    public BreakReminder(Func<IEnumerable<HueLight>> connectedLights, Func<bool> isPaused)
    {
        this.connectedLights = connectedLights;
        this.isPaused = isPaused;
        checkTimer.Tick += async (_, _) => await Check();
        checkTimer.Start();
    }

    public void Dispose()
    {
        checkTimer.Dispose();
    }

    private static bool IsWorkingTime(DateTime now)
    {
        var isWeekday = now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        var time = TimeOnly.FromDateTime(now);
        return isWeekday && time >= WorkdayStart && time < WorkdayEnd;
    }

    private async Task Check()
    {
        var now = DateTime.Now;
        if (!IsWorkingTime(now))
        {
            nextReminderAt = null;
            return;
        }

        nextReminderAt ??= now + ReminderInterval;
        if (now < nextReminderAt || isBlinking || isPaused())
        {
            return;
        }

        nextReminderAt = now + ReminderInterval;
        var lights = connectedLights().Where(light => light.LastState is { IsOn: true }).ToList();
        isBlinking = true;
        try
        {
            await Task.WhenAll(lights.Select(light => LightCommands.RunSafely(light, () => Blink(light))));
        }
        finally
        {
            isBlinking = false;
        }
    }

    private static async Task Blink(HueLight light)
    {
        var originalBrightness = light.LastState!.Brightness;
        var contrastBrightness = originalBrightness > HueProtocol.MaximumBrightness / 2 ? HueProtocol.MinimumBrightness : HueProtocol.MaximumBrightness;
        try
        {
            for (var blink = 0; blink < BlinkCount; blink++)
            {
                await light.SetBrightness(contrastBrightness);
                await Task.Delay(BlinkStepMilliseconds);
                await light.SetBrightness(originalBrightness);
                await Task.Delay(BlinkStepMilliseconds);
            }
        }
        catch
        {
            await light.SetBrightness(originalBrightness);
            throw;
        }
    }
}
