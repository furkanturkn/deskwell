using System.Runtime.InteropServices;

namespace HueDesk;

public static class ScreenGridSampler
{
    private const int GridColumns = 32;
    private const int GridRows = 18;
    private const int BytesPerPixel = 4;
    private const int ColorMaximum = 255;
    private const double MinimumColorWeight = 0.05;
    private const double LuminanceRed = 0.2126;
    private const double LuminanceGreen = 0.7152;
    private const double LuminanceBlue = 0.0722;

    public static ScreenColor Average(IntPtr pixels, int stride, int width, int height)
    {
        double weightedRed = 0, weightedGreen = 0, weightedBlue = 0, totalWeight = 0, totalLuminance = 0;
        for (var row = 0; row < GridRows; row++)
        {
            var pixelY = (row * 2 + 1) * height / (GridRows * 2);
            for (var column = 0; column < GridColumns; column++)
            {
                var pixelX = (column * 2 + 1) * width / (GridColumns * 2);
                var pixel = Marshal.ReadInt32(pixels, pixelY * stride + pixelX * BytesPerPixel);
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
}
