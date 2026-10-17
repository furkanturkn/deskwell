namespace HueDesk;

public static class HueColor
{
    private const double SrgbLinearThreshold = 0.04045;
    private const double SrgbLinearDivisor = 12.92;
    private const double SrgbOffset = 0.055;
    private const double SrgbScale = 1.055;
    private const double SrgbGamma = 2.4;

    // Linear RGB to CIE XYZ matrix for the Philips Hue wide gamut.
    private static readonly double[] RedToXyz = [0.664511, 0.283881, 0.000088];
    private static readonly double[] GreenToXyz = [0.154324, 0.668433, 0.072310];
    private static readonly double[] BlueToXyz = [0.162028, 0.047685, 0.986039];

    public static (double Red, double Green, double Blue) HsvToRgb(double hue, double saturation, double value)
    {
        var normalizedHue = ((hue % 360) + 360) % 360 / 60;
        var sector = (int)Math.Floor(normalizedHue);
        var fraction = normalizedHue - sector;
        var low = value * (1 - saturation);
        var falling = value * (1 - saturation * fraction);
        var rising = value * (1 - saturation * (1 - fraction));
        return sector switch
        {
            0 => (value, rising, low),
            1 => (falling, value, low),
            2 => (low, value, rising),
            3 => (low, falling, value),
            4 => (rising, low, value),
            _ => (value, low, falling),
        };
    }

    public static (double X, double Y) ToChromaticity(double hue, double saturation)
    {
        var (red, green, blue) = HsvToRgb(hue, saturation, 1);
        var linearRed = ToLinear(red);
        var linearGreen = ToLinear(green);
        var linearBlue = ToLinear(blue);

        var cieX = linearRed * RedToXyz[0] + linearGreen * GreenToXyz[0] + linearBlue * BlueToXyz[0];
        var cieY = linearRed * RedToXyz[1] + linearGreen * GreenToXyz[1] + linearBlue * BlueToXyz[1];
        var cieZ = linearRed * RedToXyz[2] + linearGreen * GreenToXyz[2] + linearBlue * BlueToXyz[2];
        var sum = cieX + cieY + cieZ;
        return (cieX / sum, cieY / sum);
    }

    private static double ToLinear(double channel)
    {
        return channel > SrgbLinearThreshold ? Math.Pow((channel + SrgbOffset) / SrgbScale, SrgbGamma) : channel / SrgbLinearDivisor;
    }
}
