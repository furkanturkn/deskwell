using System.Drawing.Drawing2D;

namespace HueDesk;

public sealed class ToggleSwitch : Control
{
    private bool isChecked;

    public ToggleSwitch()
    {
        DoubleBuffered = true;
        Size = new Size(46, 26);
        Cursor = Cursors.Hand;
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value)
            {
                return;
            }

            isChecked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetCheckedSilently(bool value)
    {
        isChecked = value;
        Invalidate();
    }

    protected override void OnClick(EventArgs eventArgs)
    {
        base.OnClick(eventArgs);
        if (Enabled)
        {
            Checked = !Checked;
        }
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs paintEventArgs)
    {
        var graphics = paintEventArgs.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var trackColor = isChecked ? Theme.Accent : Theme.Track;
        var knobColor = isChecked ? Theme.OnAccent : Theme.TextSecondary;
        if (!Enabled)
        {
            trackColor = Color.FromArgb(50, 50, 58);
            knobColor = Color.FromArgb(90, 90, 100);
        }

        using var trackPath = Theme.RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f);
        using var trackBrush = new SolidBrush(trackColor);
        graphics.FillPath(trackBrush, trackPath);

        var knobDiameter = Height - 8;
        var knobLeft = isChecked ? Width - knobDiameter - 4 : 4;
        using var knobBrush = new SolidBrush(knobColor);
        graphics.FillEllipse(knobBrush, knobLeft, 4, knobDiameter - 1, knobDiameter - 1);
    }
}

public sealed class ModernSlider : Control
{
    private const int ThumbDiameter = 18;
    private const int TrackHeight = 8;

    private int minimum;
    private int maximum = 100;
    private int value;
    private bool isDragging;
    private Color[]? gradient;

    public ModernSlider()
    {
        DoubleBuffered = true;
        Height = 26;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    public event EventHandler? ValueChanged;

    public event EventHandler? UserChanged;

    public int Minimum
    {
        get => minimum;
        set
        {
            minimum = value;
            Invalidate();
        }
    }

    public int Maximum
    {
        get => maximum;
        set
        {
            maximum = value;
            Invalidate();
        }
    }

    public Color[]? Gradient
    {
        get => gradient;
        set
        {
            gradient = value;
            Invalidate();
        }
    }

    public int Value => value;

    public bool IsDragging => isDragging;

    public void SetValueSilently(int newValue)
    {
        if (isDragging)
        {
            return;
        }

        ApplyValue(newValue, byUser: false);
    }

    protected override void OnMouseDown(MouseEventArgs mouseEventArgs)
    {
        base.OnMouseDown(mouseEventArgs);
        if (mouseEventArgs.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        Focus();
        isDragging = true;
        ApplyValue(ValueFromX(mouseEventArgs.X), byUser: true);
    }

    protected override void OnMouseMove(MouseEventArgs mouseEventArgs)
    {
        base.OnMouseMove(mouseEventArgs);
        if (isDragging)
        {
            ApplyValue(ValueFromX(mouseEventArgs.X), byUser: true);
        }
    }

    protected override void OnMouseUp(MouseEventArgs mouseEventArgs)
    {
        base.OnMouseUp(mouseEventArgs);
        isDragging = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs mouseEventArgs)
    {
        base.OnMouseWheel(mouseEventArgs);
        if (Enabled)
        {
            var step = Math.Max(1, (maximum - minimum) / 25);
            ApplyValue(value + Math.Sign(mouseEventArgs.Delta) * step, byUser: true);
        }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs keyEventArgs)
    {
        base.OnKeyDown(keyEventArgs);
        var step = Math.Max(1, (maximum - minimum) / 50);
        if (keyEventArgs.KeyCode == Keys.Left)
        {
            ApplyValue(value - step, byUser: true);
        }
        else if (keyEventArgs.KeyCode == Keys.Right)
        {
            ApplyValue(value + step, byUser: true);
        }
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs paintEventArgs)
    {
        var graphics = paintEventArgs.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var track = TrackBounds;
        var thumbCenterX = track.Left + Fraction * track.Width;
        var opacity = Enabled ? 255 : 90;

        using var trackPath = Theme.RoundedRectangle(track, TrackHeight / 2f);
        if (gradient is { Length: > 1 })
        {
            using var gradientBrush = new LinearGradientBrush(track, gradient[0], gradient[^1], LinearGradientMode.Horizontal);
            var blend = new ColorBlend(gradient.Length)
            {
                Colors = gradient.Select(color => Color.FromArgb(opacity, color)).ToArray(),
                Positions = Enumerable.Range(0, gradient.Length).Select(index => index / (float)(gradient.Length - 1)).ToArray(),
            };
            gradientBrush.InterpolationColors = blend;
            graphics.FillPath(gradientBrush, trackPath);
        }
        else
        {
            using var trackBrush = new SolidBrush(Color.FromArgb(opacity, Theme.Track));
            graphics.FillPath(trackBrush, trackPath);

            var filledWidth = thumbCenterX - track.Left;
            if (filledWidth > 1)
            {
                var filled = new RectangleF(track.Left, track.Top, filledWidth, track.Height);
                using var filledPath = Theme.RoundedRectangle(filled, TrackHeight / 2f);
                using var accentBrush = new SolidBrush(Color.FromArgb(opacity, Theme.Accent));
                graphics.FillPath(accentBrush, filledPath);
            }
        }

        var thumb = new RectangleF(thumbCenterX - ThumbDiameter / 2f, (Height - ThumbDiameter) / 2f, ThumbDiameter, ThumbDiameter);
        using var shadowBrush = new SolidBrush(Color.FromArgb(opacity / 3, 0, 0, 0));
        graphics.FillEllipse(shadowBrush, thumb.X - 1, thumb.Y + 1, thumb.Width + 2, thumb.Height + 2);
        using var thumbBrush = new SolidBrush(Color.FromArgb(opacity, 245, 245, 248));
        graphics.FillEllipse(thumbBrush, thumb);
    }

    private float Fraction => maximum == minimum ? 0 : (value - minimum) / (float)(maximum - minimum);

    private RectangleF TrackBounds => new(ThumbDiameter / 2f, (Height - TrackHeight) / 2f, Width - ThumbDiameter, TrackHeight);

    private int ValueFromX(int x)
    {
        var track = TrackBounds;
        var fraction = Math.Clamp((x - track.Left) / track.Width, 0f, 1f);
        return minimum + (int)Math.Round(fraction * (maximum - minimum));
    }

    private void ApplyValue(int newValue, bool byUser)
    {
        var clamped = Math.Clamp(newValue, minimum, maximum);
        if (clamped == value)
        {
            return;
        }

        value = clamped;
        Invalidate();
        ValueChanged?.Invoke(this, EventArgs.Empty);
        if (byUser)
        {
            UserChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class SegmentedControl : Control
{
    private readonly string[] items;
    private int selectedIndex;

    public SegmentedControl(params string[] items)
    {
        this.items = items;
        DoubleBuffered = true;
        Height = 30;
        Cursor = Cursors.Hand;
    }

    public event EventHandler? SelectedChanged;

    public int SelectedIndex => selectedIndex;

    protected override void OnMouseDown(MouseEventArgs mouseEventArgs)
    {
        base.OnMouseDown(mouseEventArgs);
        var clickedIndex = Math.Clamp(mouseEventArgs.X * items.Length / Math.Max(1, Width), 0, items.Length - 1);
        if (clickedIndex == selectedIndex)
        {
            return;
        }

        selectedIndex = clickedIndex;
        Invalidate();
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs paintEventArgs)
    {
        var graphics = paintEventArgs.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var backgroundPath = Theme.RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f);
        using var backgroundBrush = new SolidBrush(Theme.Chip);
        graphics.FillPath(backgroundBrush, backgroundPath);

        var segmentWidth = (Width - 1) / (float)items.Length;
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (var index = 0; index < items.Length; index++)
        {
            var segment = new RectangleF(index * segmentWidth, 0, segmentWidth, Height - 1);
            var isSelected = index == selectedIndex;
            if (isSelected)
            {
                var pill = RectangleF.Inflate(segment, -3, -3);
                using var pillPath = Theme.RoundedRectangle(pill, pill.Height / 2f);
                using var pillBrush = new SolidBrush(Theme.Accent);
                graphics.FillPath(pillBrush, pillPath);
            }

            using var textBrush = new SolidBrush(isSelected ? Theme.OnAccent : Theme.TextSecondary);
            graphics.DrawString(items[index], Theme.ChipFont, textBrush, segment, format);
        }
    }
}

public sealed class SwatchRow : Control
{
    private readonly Color[] colors;

    public SwatchRow(Color[] colors)
    {
        this.colors = colors;
        DoubleBuffered = true;
        Height = 26;
        Cursor = Cursors.Hand;
    }

    public event Action<int>? SwatchClicked;

    protected override void OnMouseDown(MouseEventArgs mouseEventArgs)
    {
        base.OnMouseDown(mouseEventArgs);
        if (!Enabled)
        {
            return;
        }

        var index = Math.Clamp(mouseEventArgs.X * colors.Length / Math.Max(1, Width), 0, colors.Length - 1);
        SwatchClicked?.Invoke(index);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs paintEventArgs)
    {
        var graphics = paintEventArgs.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var slotWidth = Width / (float)colors.Length;
        var diameter = Math.Min(Height - 2, slotWidth - 6);
        var opacity = Enabled ? 255 : 90;
        for (var index = 0; index < colors.Length; index++)
        {
            var left = index * slotWidth + (slotWidth - diameter) / 2;
            using var brush = new SolidBrush(Color.FromArgb(opacity, colors[index]));
            graphics.FillEllipse(brush, left, (Height - diameter) / 2, diameter, diameter);
        }
    }
}

public sealed class Chip : Control
{
    private bool isHovered;

    public Chip()
    {
        DoubleBuffered = true;
        Height = 30;
        Font = Theme.ChipFont;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        base.OnMouseEnter(eventArgs);
        isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        isHovered = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        base.OnEnabledChanged(eventArgs);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs paintEventArgs)
    {
        var graphics = paintEventArgs.Graphics;
        graphics.Clear(BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var path = Theme.RoundedRectangle(new RectangleF(0, 0, Width - 1, Height - 1), (Height - 1) / 2f);
        using var brush = new SolidBrush(isHovered && Enabled ? Theme.ChipHover : Theme.Chip);
        graphics.FillPath(brush, path);

        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var textBrush = new SolidBrush(Enabled ? Theme.TextPrimary : Theme.TextSecondary);
        graphics.DrawString(Text, Font, textBrush, new RectangleF(0, 0, Width, Height - 1), format);
    }
}
