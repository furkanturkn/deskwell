namespace HueDesk;

public sealed record ScreenColor(double Red, double Green, double Blue, double Luminance)
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
