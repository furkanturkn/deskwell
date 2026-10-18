using System.Drawing.Drawing2D;

namespace Deskwell;

public static class Theme
{
    public const float UiScale = 1.3f;

    public static readonly Color Background = Color.FromArgb(24, 24, 29);
    public static readonly Color Card = Color.FromArgb(35, 35, 42);
    public static readonly Color CardBorder = Color.FromArgb(52, 52, 62);
    public static readonly Color Track = Color.FromArgb(62, 62, 72);
    public static readonly Color Chip = Color.FromArgb(48, 48, 57);
    public static readonly Color ChipHover = Color.FromArgb(66, 66, 78);
    public static readonly Color TextPrimary = Color.FromArgb(240, 240, 245);
    public static readonly Color TextSecondary = Color.FromArgb(150, 150, 166);
    public static readonly Color Accent = Color.FromArgb(255, 190, 80);
    public static readonly Color OnAccent = Color.FromArgb(30, 24, 10);
    public static readonly Color Success = Color.FromArgb(80, 210, 130);
    public static readonly Color Warning = Color.FromArgb(255, 170, 60);
    public static readonly Color Danger = Color.FromArgb(240, 100, 100);

    public static readonly Font TitleFont = new("Segoe UI Semibold", 15f * UiScale);
    public static readonly Font HeadingFont = new("Segoe UI Semibold", 11.5f * UiScale);
    public static readonly Font BodyFont = new("Segoe UI", 9.5f * UiScale);
    public static readonly Font SmallFont = new("Segoe UI", 8.5f * UiScale);
    public static readonly Font ChipFont = new("Segoe UI Semibold", 9f * UiScale);

    public static int Scale(int pixels)
    {
        return (int)Math.Round(pixels * UiScale);
    }

    public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color FromHsv(double hue, double saturation, double value)
    {
        var (red, green, blue) = HueColor.HsvToRgb(hue, saturation, value);
        return Color.FromArgb((int)Math.Round(red * 255), (int)Math.Round(green * 255), (int)Math.Round(blue * 255));
    }
}
