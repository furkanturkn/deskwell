namespace HueDesk;

public static class HueColor
{
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

        var cieX = linearRed * 0.664511 + linearGreen * 0.154324 + linearBlue * 0.162028;
        var cieY = linearRed * 0.283881 + linearGreen * 0.668433 + linearBlue * 0.047685;
        var cieZ = linearRed * 0.000088 + linearGreen * 0.072310 + linearBlue * 0.986039;
        var sum = cieX + cieY + cieZ;
        return (cieX / sum, cieY / sum);
    }

    private static double ToLinear(double channel)
    {
        return channel > 0.04045 ? Math.Pow((channel + 0.055) / 1.055, 2.4) : channel / 12.92;
    }
}
