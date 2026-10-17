using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace HueDesk;

public sealed class ScreenSync : IDisposable
{
    private const int SampleIntervalMilliseconds = 300;
    private const int GridColumns = 32;
    private const int GridRows = 18;
    private const int BytesPerPixel = 4;
    private const int ColorMaximum = 255;
    private const double SmoothingFactor = 0.55;
    private const double SaturationBoost = 1.35;
    private const double MinimumColorWeight = 0.05;
    private const double LuminanceRed = 0.2126;
    private const double LuminanceGreen = 0.7152;
    private const double LuminanceBlue = 0.0722;
    private const double MinimumBrightnessFraction = 0.04;
    private const double HueChangeThresholdDegrees = 3;
    private const double SaturationChangeThreshold = 0.04;
    private const int BrightnessChangeThreshold = 6;

    private readonly Func<IEnumerable<HueLight>> connectedLights;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = SampleIntervalMilliseconds };
    private readonly Dictionary<HueLight, LightSnapshot> snapshots = [];
    private ScreenColor? smoothedColor;
    private LightOutput? lastOutput;
    private bool isSending;

    public ScreenSync(Func<IEnumerable<HueLight>> connectedLights)
    {
        this.connectedLights = connectedLights;
        timer.Tick += async (_, _) => await Sample();
    }

    public bool IsEnabled { get; private set; }

    public bool IsSuspended { get; set; }

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
        lastOutput = null;
        IsEnabled = true;
        timer.Start();
    }

    public void Stop()
    {
        if (!IsEnabled)
        {
            return;
        }

        timer.Stop();
        IsEnabled = false;
        foreach (var (light, snapshot) in snapshots)
        {
            _ = LightCommands.RunSafely(light, () => light.Restore(snapshot));
        }

        snapshots.Clear();
    }

    public void Dispose()
    {
        timer.Dispose();
    }

    private static ScreenColor CaptureScreenColor()
    {
        var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var bounds = screen.Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        }

        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            return AverageGrid(data, bounds.Width, bounds.Height);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static ScreenColor AverageGrid(BitmapData data, int width, int height)
    {
        double weightedRed = 0, weightedGreen = 0, weightedBlue = 0, totalWeight = 0, totalLuminance = 0;
        for (var row = 0; row < GridRows; row++)
        {
            var pixelY = (row * 2 + 1) * height / (GridRows * 2);
            for (var column = 0; column < GridColumns; column++)
            {
                var pixelX = (column * 2 + 1) * width / (GridColumns * 2);
                var pixel = Marshal.ReadInt32(data.Scan0, pixelY * data.Stride + pixelX * BytesPerPixel);
                var red = ((pixel >> 16) & 0xFF) / (double)ColorMaximum;
                var green = ((pixel >> 8) & 0xFF) / (double)ColorMaximum;
                var blue = (pixel & 0xFF) / (double)ColorMaximum;

                var colorfulness = Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue));
                var weight = MinimumColorWeight + colorfulness;
                weightedRed += red * weight;
                weightedGreen += green * weight;
                weightedBlue += blue * weight;
                totalWeight += weight;
                totalLuminance += LuminanceRed * red + LuminanceGreen * green + LuminanceBlue * blue;
            }
        }

        return new ScreenColor(weightedRed / totalWeight, weightedGreen / totalWeight, weightedBlue / totalWeight, totalLuminance / (GridColumns * GridRows));
    }

    private static LightOutput ToOutput(ScreenColor color)
    {
        var (hue, saturation, _) = HueColor.RgbToHsv(color.Red, color.Green, color.Blue);
        var boostedSaturation = Math.Min(1.0, saturation * SaturationBoost);
        var brightnessFraction = Math.Clamp(color.Luminance, MinimumBrightnessFraction, 1.0);
        var brightness = (byte)Math.Clamp(Math.Round(brightnessFraction * HueProtocol.MaximumBrightness), HueProtocol.MinimumBrightness, HueProtocol.MaximumBrightness);
        return new LightOutput(hue, boostedSaturation, brightness);
    }

    private static bool IsNoticeableChange(LightOutput? previous, LightOutput next)
    {
        if (previous is null)
        {
            return true;
        }

        var hueDistance = Math.Abs(previous.Hue - next.Hue);
        var circularHueDistance = Math.Min(hueDistance, 360 - hueDistance);
        return circularHueDistance > HueChangeThresholdDegrees
            || Math.Abs(previous.Saturation - next.Saturation) > SaturationChangeThreshold
            || Math.Abs(previous.Brightness - next.Brightness) > BrightnessChangeThreshold;
    }

    private async Task Sample()
    {
        if (!IsEnabled || IsSuspended || isSending)
        {
            return;
        }

        var captured = CaptureScreenColor();
        smoothedColor = smoothedColor is null ? captured : smoothedColor.Blend(captured, SmoothingFactor);
        var output = ToOutput(smoothedColor);
        if (!IsNoticeableChange(lastOutput, output))
        {
            return;
        }

        isSending = true;
        try
        {
            lastOutput = output;
            await Task.WhenAll(connectedLights().Select(light => LightCommands.RunSafely(light, () => Send(light, output))));
        }
        finally
        {
            isSending = false;
        }
    }

    private static async Task Send(HueLight light, LightOutput output)
    {
        await light.SetColor(output.Hue, output.Saturation);
        await light.SetBrightness(output.Brightness);
    }

    private sealed record ScreenColor(double Red, double Green, double Blue, double Luminance)
    {
        public ScreenColor Blend(ScreenColor target, double factor)
        {
            return new ScreenColor(
                Red + (target.Red - Red) * factor,
                Green + (target.Green - Green) * factor,
                Blue + (target.Blue - Blue) * factor,
                Luminance + (target.Luminance - Luminance) * factor);
        }
    }

    private sealed record LightOutput(double Hue, double Saturation, byte Brightness);
}
