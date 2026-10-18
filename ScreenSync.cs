using System.Diagnostics;
using System.Drawing.Imaging;

namespace Deskwell;

public sealed class ScreenSync : IDisposable
{
    private const int SampleIntervalMilliseconds = 60;
    private const double SnappiestFactorPerReference = 0.95;
    private const double FactorRangePerSmoothnessPoint = 0.0075;
    public const int DefaultSmoothness = 40;
    public const int MaximumSmoothness = 100;
    private const double SmoothingReferenceMilliseconds = 120;
    private const double MaximumElapsedMilliseconds = 500;
    private const double SaturationBoost = 1.35;
    private const double MinimumBrightnessFraction = 0.04;
    private const double HueChangeThresholdDegrees = 1.5;
    private const double SaturationChangeThreshold = 0.02;
    private const int BrightnessChangeThreshold = 3;
    private const int StallMilliseconds = 400;
    private const int SlowSendMilliseconds = 250;
    private static readonly TimeSpan CaptureRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LightSendTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StatisticsInterval = TimeSpan.FromSeconds(30);

    private readonly Func<IEnumerable<HueLight>> connectedLights;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = SampleIntervalMilliseconds };
    private readonly Dictionary<HueLight, LightSnapshot> snapshots = [];
    private readonly HashSet<HueLight> lightsReceivingSync = [];
    private DesktopDuplicationCapture? desktopCapture;
    private DateTime nextCaptureCreationAttempt = DateTime.MinValue;
    private long lastSampleTimestamp;
    private ScreenColor? latestColor;
    private ScreenColor? smoothedColor;
    private LightOutput? lastOutput;
    private bool isSending;
    private long lastTickTimestamp;
    private long statisticsWindowStart;
    private int sendCount;
    private int busyTickCount;
    private double totalSendMilliseconds;
    private double maximumSendMilliseconds;
    private double maximumTickGapMilliseconds;

    public ScreenSync(Func<IEnumerable<HueLight>> connectedLights)
    {
        this.connectedLights = connectedLights;
        timer.Tick += async (_, _) => await Sample();
    }

    public bool IsEnabled { get; private set; }

    public bool IsSuspended { get; set; }

    public int Smoothness { get; set; } = DefaultSmoothness;

    public void Start()
    {
        if (IsEnabled)
        {
            return;
        }

        foreach (var light in connectedLights())
        {
            if (light.CaptureSnapshot() is { } snapshot)
            {
                snapshots[light] = snapshot;
            }

            _ = LightCommands.RunSafely(light, () => light.SetPower(true));
        }

        smoothedColor = null;
        latestColor = null;
        lastOutput = null;
        lightsReceivingSync.Clear();
        lastSampleTimestamp = Stopwatch.GetTimestamp();
        lastTickTimestamp = 0;
        statisticsWindowStart = lastSampleTimestamp;
        ResetStatistics();
        IsEnabled = true;
        timer.Start();
    }

    public void Stop(bool restorePreviousState = true)
    {
        if (!IsEnabled)
        {
            return;
        }

        timer.Stop();
        IsEnabled = false;
        desktopCapture?.Dispose();
        desktopCapture = null;
        if (restorePreviousState)
        {
            foreach (var (light, snapshot) in snapshots)
            {
                _ = LightCommands.RunSafely(light, () => light.Restore(snapshot));
            }
        }

        snapshots.Clear();
    }

    public void Dispose()
    {
        timer.Dispose();
        desktopCapture?.Dispose();
    }

    private static ScreenColor CopyScreenColor()
    {
        var bounds = PrimaryScreen().Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            return ScreenGridSampler.Average(data.Scan0, data.Stride, bounds.Width, bounds.Height);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Screen PrimaryScreen()
    {
        return Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }

    private ScreenColor? CaptureScreenColor()
    {
        EnsureDesktopCapture();
        if (desktopCapture is null)
        {
            return CopyScreenColor();
        }

        var (status, color) = desktopCapture.Capture();
        if (status == CaptureStatus.Unavailable)
        {
            desktopCapture.Dispose();
            desktopCapture = null;
            nextCaptureCreationAttempt = DateTime.UtcNow + CaptureRetryDelay;
            return CopyScreenColor();
        }

        return status == CaptureStatus.Captured ? color : latestColor;
    }

    private void EnsureDesktopCapture()
    {
        if (desktopCapture is not null || DateTime.UtcNow < nextCaptureCreationAttempt)
        {
            return;
        }

        desktopCapture = DesktopDuplicationCapture.TryCreate(PrimaryScreen().DeviceName);
        nextCaptureCreationAttempt = DateTime.UtcNow + CaptureRetryDelay;
    }

    private double CalculateSmoothingFactor()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedMilliseconds = Math.Min(Stopwatch.GetElapsedTime(lastSampleTimestamp, now).TotalMilliseconds, MaximumElapsedMilliseconds);
        lastSampleTimestamp = now;
        var factorPerReference = SnappiestFactorPerReference - Math.Clamp(Smoothness, 0, MaximumSmoothness) * FactorRangePerSmoothnessPoint;
        return 1 - Math.Pow(1 - factorPerReference, elapsedMilliseconds / SmoothingReferenceMilliseconds);
    }

    private static LightOutput ToOutput(ScreenColor color)
    {
        var (hue, saturation, _) = HueColor.RgbToHsv(color.Red, color.Green, color.Blue);
        var boostedSaturation = Math.Min(1.0, saturation * SaturationBoost);
        var brightnessFraction = Math.Clamp(color.Luminance, MinimumBrightnessFraction, 1.0);
        var brightness = (byte)Math.Clamp(Math.Round(brightnessFraction * HueProtocol.MaximumBrightness), HueProtocol.MinimumBrightness, HueProtocol.MaximumBrightness);
        return new LightOutput(hue, boostedSaturation, brightness);
    }

    private static bool IsColorChange(LightOutput? previous, LightOutput next)
    {
        if (previous is null)
        {
            return true;
        }

        var hueDistance = Math.Abs(previous.Hue - next.Hue);
        var circularHueDistance = Math.Min(hueDistance, 360 - hueDistance);
        return circularHueDistance > HueChangeThresholdDegrees
            || Math.Abs(previous.Saturation - next.Saturation) > SaturationChangeThreshold;
    }

    private static bool IsBrightnessChange(LightOutput? previous, LightOutput next)
    {
        return previous is null || Math.Abs(previous.Brightness - next.Brightness) > BrightnessChangeThreshold;
    }

    private static LightOutput MergeSent(LightOutput? previous, LightOutput next, bool wasColorSent, bool wasBrightnessSent)
    {
        if (previous is null)
        {
            return next;
        }

        return new LightOutput(
            wasColorSent ? next.Hue : previous.Hue,
            wasColorSent ? next.Saturation : previous.Saturation,
            wasBrightnessSent ? next.Brightness : previous.Brightness);
    }

    private async Task Sample()
    {
        if (!IsEnabled)
        {
            return;
        }

        RecordTick();
        if (IsSuspended)
        {
            return;
        }

        if (isSending)
        {
            busyTickCount++;
            return;
        }

        var captured = CaptureScreenColor();
        if (captured is null)
        {
            return;
        }

        latestColor = captured;
        var smoothingFactor = CalculateSmoothingFactor();
        smoothedColor = smoothedColor is null ? captured : smoothedColor.Blend(captured, smoothingFactor);
        var output = ToOutput(smoothedColor);
        var lights = connectedLights().ToList();
        lightsReceivingSync.IntersectWith(lights);
        var hasLightWithoutCurrentColor = lights.Any(light => !lightsReceivingSync.Contains(light));
        var previousOutput = hasLightWithoutCurrentColor ? null : lastOutput;
        var shouldSendColor = IsColorChange(previousOutput, output);
        var shouldSendBrightness = IsBrightnessChange(previousOutput, output);
        if (!shouldSendColor && !shouldSendBrightness)
        {
            return;
        }

        isSending = true;
        var sendStartTimestamp = Stopwatch.GetTimestamp();
        try
        {
            lastOutput = MergeSent(previousOutput, output, shouldSendColor, shouldSendBrightness);
            lightsReceivingSync.UnionWith(lights);
            await Task.WhenAll(lights.Select(light => LightCommands.RunSafely(light, () => Send(light, output, shouldSendColor, shouldSendBrightness).WaitAsync(LightSendTimeout))));
        }
        finally
        {
            isSending = false;
            RecordSend(Stopwatch.GetElapsedTime(sendStartTimestamp).TotalMilliseconds);
        }
    }

    private void RecordTick()
    {
        var now = Stopwatch.GetTimestamp();
        if (lastTickTimestamp != 0)
        {
            var gapMilliseconds = Stopwatch.GetElapsedTime(lastTickTimestamp, now).TotalMilliseconds;
            maximumTickGapMilliseconds = Math.Max(maximumTickGapMilliseconds, gapMilliseconds);
            if (gapMilliseconds > StallMilliseconds)
            {
                Logger.Write($"Screen sync: UI thread stalled, {gapMilliseconds:0} ms between samples");
            }
        }

        lastTickTimestamp = now;
        ReportStatisticsIfDue(now);
    }

    private void RecordSend(double durationMilliseconds)
    {
        sendCount++;
        totalSendMilliseconds += durationMilliseconds;
        maximumSendMilliseconds = Math.Max(maximumSendMilliseconds, durationMilliseconds);
        if (durationMilliseconds > SlowSendMilliseconds)
        {
            Logger.Write($"Screen sync: slow send, {durationMilliseconds:0} ms");
        }
    }

    private void ReportStatisticsIfDue(long now)
    {
        if (Stopwatch.GetElapsedTime(statisticsWindowStart, now) < StatisticsInterval)
        {
            return;
        }

        var windowSeconds = Stopwatch.GetElapsedTime(statisticsWindowStart, now).TotalSeconds;
        var averageSendMilliseconds = sendCount == 0 ? 0 : totalSendMilliseconds / sendCount;
        Logger.Write($"Screen sync stats: {sendCount / windowSeconds:0.0} sends/s, send avg {averageSendMilliseconds:0} ms, max {maximumSendMilliseconds:0} ms, max sample gap {maximumTickGapMilliseconds:0} ms, skipped while sending {busyTickCount}");
        statisticsWindowStart = now;
        ResetStatistics();
    }

    private void ResetStatistics()
    {
        sendCount = 0;
        busyTickCount = 0;
        totalSendMilliseconds = 0;
        maximumSendMilliseconds = 0;
        maximumTickGapMilliseconds = 0;
    }

    private static async Task Send(HueLight light, LightOutput output, bool shouldSendColor, bool shouldSendBrightness)
    {
        var colorTask = shouldSendColor ? light.SetColor(output.Hue, output.Saturation, LightWriteMode.Fast) : Task.CompletedTask;
        var brightnessTask = shouldSendBrightness ? light.SetBrightness(output.Brightness, LightWriteMode.Fast) : Task.CompletedTask;
        await Task.WhenAll(colorTask, brightnessTask);
    }

    private sealed record LightOutput(double Hue, double Saturation, byte Brightness);
}
