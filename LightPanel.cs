using System.Drawing.Drawing2D;

namespace HueDesk;

public sealed class LightPanel : Panel
{
    private const int SendDelayMilliseconds = 60;
    private const int CardWidth = 348;
    private const int CardPadding = 16;
    private const int CaptionWidth = 76;
    private const int ValueWidth = 44;
    private const int RowPitch = 34;
    private const int FirstRowTop = 66;
    private const int ModeSwitchTop = 104;
    private const int PageTop = 144;
    private const int WhiteModeHeight = 196;
    private const int ColorModeHeight = 262;
    private const int CardCornerRadius = 16;
    private const int CardBottomMargin = 12;
    private const int NameTop = 12;
    private const int StatusTop = 36;
    private const int StatusWidth = 210;
    private const int StatusHeight = 18;
    private const int ToggleTop = 14;
    private const int MenuTop = 14;
    private const int MenuGap = 12;
    private const int MenuChipWidth = 30;
    private const int MenuChipHeight = 28;
    private const int ModeSwitchHeight = 30;
    private const int SwatchTopOffset = 2;
    private const int SwatchHeight = 26;
    private const int CaptionHeight = 22;
    private const int SliderHeight = 26;
    private const int SliderTopOffset = 2;
    private const int SliderRightGap = 4;
    private const int ValueHeight = 20;

    private static readonly Color[] RainbowStops =
        [Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red];

    private static readonly Color[] WarmthStops =
        [Color.FromArgb(190, 215, 255), Color.FromArgb(255, 244, 235), Color.FromArgb(255, 214, 170), Color.FromArgb(255, 160, 80)];

    private static readonly double[] SwatchHues = [0, 30, 55, 120, 180, 220, 275, 320];

    private readonly HueLight light;
    private readonly Label nameLabel = CreateLabel(Theme.HeadingFont, Theme.TextPrimary);
    private readonly Label statusLabel = CreateLabel(Theme.SmallFont, Theme.TextSecondary);
    private readonly PowerButton powerToggle = new() { Icon = PowerIcon.Bulb };
    private readonly Chip menuChip = new() { Text = "...", Size = new Size(MenuChipWidth, MenuChipHeight) };
    private readonly ContextMenuStrip menu = new();
    private readonly ModernSlider brightnessSlider = new() { Minimum = HueProtocol.MinimumBrightness, Maximum = HueProtocol.MaximumBrightness };
    private readonly ModernSlider warmthSlider = new() { Minimum = HueProtocol.MinimumTemperatureMirek, Maximum = HueProtocol.MaximumTemperatureMirek, Gradient = WarmthStops };
    private readonly ModernSlider hueSlider = new() { Minimum = 0, Maximum = 360, Gradient = RainbowStops };
    private readonly ModernSlider saturationSlider = new() { Minimum = 0, Maximum = 100 };
    private readonly SegmentedControl modeSwitch = new("White", "Color");
    private readonly SwatchRow swatchRow = new(SwatchHues.Select(hue => Theme.FromHsv(hue, 1, 1)).ToArray());
    private readonly Label brightnessValue = CreateValueLabel();
    private readonly Label warmthValue = CreateValueLabel();
    private readonly Label hueValue = CreateValueLabel();
    private readonly Label saturationValue = CreateValueLabel();
    private readonly List<Control> whiteControls = [];
    private readonly List<Control> colorControls = [];
    private readonly System.Windows.Forms.Timer sendTimer = new() { Interval = SendDelayMilliseconds };
    private bool hasPendingBrightness;
    private bool hasPendingWarmth;
    private bool hasPendingColor;
    private bool isSending;

    public LightPanel(HueLight light, Action<SavedLight> onRemoveRequested)
    {
        this.light = light;
        DoubleBuffered = true;
        BackColor = Theme.Card;
        Width = CardWidth;
        Height = WhiteModeHeight;
        Margin = new Padding(0, 0, 0, CardBottomMargin);

        nameLabel.Text = light.SavedLight.Name;
        nameLabel.Location = new Point(CardPadding, NameTop);
        statusLabel.Location = new Point(CardPadding, StatusTop);
        statusLabel.Size = new Size(StatusWidth, StatusHeight);
        powerToggle.Location = new Point(CardWidth - CardPadding - powerToggle.Width, ToggleTop);
        menuChip.Location = new Point(powerToggle.Left - MenuGap - menuChip.Width, MenuTop);
        menu.Items.Add("Reset light", null, (_, _) => _ = LightCommands.RunSafely(light, light.ResetToDefault));
        var connectionItem = new ToolStripMenuItem();
        connectionItem.Click += (_, _) => ChangeConnection(light.IsReleased);
        menu.Items.Add(connectionItem);
        menu.Opening += (_, _) => connectionItem.Text = light.IsReleased ? "Reconnect to this PC" : "Release (free for phone app)";
        menu.Items.Add("Remove light", null, (_, _) => onRemoveRequested(light.SavedLight));
        menuChip.Click += (_, _) => menu.Show(menuChip, new Point(0, menuChip.Height));

        AddSliderRow("Brightness", brightnessSlider, brightnessValue, FirstRowTop, whiteControls: null);
        modeSwitch.Location = new Point(CardPadding, ModeSwitchTop);
        modeSwitch.Size = new Size(CardWidth - 2 * CardPadding, ModeSwitchHeight);

        AddSliderRow("Warmth", warmthSlider, warmthValue, PageTop, whiteControls);
        AddSliderRow("Hue", hueSlider, hueValue, PageTop, colorControls);
        AddSliderRow("Saturation", saturationSlider, saturationValue, PageTop + RowPitch, colorControls);
        swatchRow.Location = new Point(CardPadding, PageTop + 2 * RowPitch + SwatchTopOffset);
        swatchRow.Size = new Size(CardWidth - 2 * CardPadding, SwatchHeight);
        colorControls.Add(swatchRow);

        Controls.AddRange([nameLabel, statusLabel, menuChip, powerToggle, modeSwitch, swatchRow]);

        saturationSlider.SetValueSilently(100);
        UpdateSaturationGradient();
        ShowMode(0);

        powerToggle.CheckedChanged += async (_, _) => await RunCommand(() => light.SetPower(powerToggle.Checked));
        modeSwitch.SelectedChanged += (_, _) => ShowMode(modeSwitch.SelectedIndex);
        brightnessSlider.UserChanged += (_, _) => QueueSend(brightness: true);
        warmthSlider.UserChanged += (_, _) => QueueSend(warmth: true);
        hueSlider.UserChanged += (_, _) => QueueSend(color: true);
        saturationSlider.UserChanged += (_, _) => QueueSend(color: true);
        hueSlider.ValueChanged += (_, _) => UpdateSaturationGradient();
        swatchRow.SwatchClicked += OnSwatchClicked;
        sendTimer.Tick += async (_, _) => await SendPending();

        brightnessSlider.ValueChanged += (_, _) => brightnessValue.Text = $"{(int)Math.Round(brightnessSlider.Value * 100.0 / HueProtocol.MaximumBrightness)}%";
        warmthSlider.ValueChanged += (_, _) => warmthValue.Text = $"{1_000_000 / Math.Max(1, warmthSlider.Value) / 100 * 100}K";
        hueSlider.ValueChanged += (_, _) => hueValue.Text = $"{hueSlider.Value}°";
        saturationSlider.ValueChanged += (_, _) => saturationValue.Text = $"{saturationSlider.Value}%";

        light.ConnectionChanged += () => BeginInvokeIfCreated(RefreshStatus);
        light.StateChanged += state => BeginInvokeIfCreated(() => ApplyLightState(state));
        RefreshStatus();
    }

    public event Action? LayoutHeightChanged;

    public SavedLight SavedLight => light.SavedLight;

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        RefreshStatus();
        if (light.LastState is { } lastState)
        {
            ApplyLightState(lastState);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs paintEventArgs)
    {
        var graphics = paintEventArgs.Graphics;
        graphics.Clear(Parent?.BackColor ?? Theme.Background);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = Theme.RoundedRectangle(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), CardCornerRadius);
        using var fillBrush = new SolidBrush(Theme.Card);
        using var borderPen = new Pen(Theme.CardBorder);
        graphics.FillPath(fillBrush, path);
        graphics.DrawPath(borderPen, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            sendTimer.Dispose();
            menu.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Label CreateLabel(Font font, Color color)
    {
        return new Label { AutoSize = true, Font = font, ForeColor = color, BackColor = Color.Transparent };
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            AutoSize = false,
            Size = new Size(ValueWidth, ValueHeight),
            TextAlign = ContentAlignment.MiddleRight,
            Font = Theme.SmallFont,
            ForeColor = Theme.TextSecondary,
            BackColor = Color.Transparent,
        };
    }

    private void AddSliderRow(string caption, ModernSlider slider, Label valueLabel, int top, List<Control>? whiteControls)
    {
        var captionLabel = CreateLabel(Theme.BodyFont, Theme.TextSecondary);
        captionLabel.Text = caption;
        captionLabel.AutoSize = false;
        captionLabel.Size = new Size(CaptionWidth, CaptionHeight);
        captionLabel.TextAlign = ContentAlignment.MiddleLeft;
        captionLabel.Location = new Point(CardPadding, top);

        var sliderLeft = CardPadding + CaptionWidth;
        slider.Location = new Point(sliderLeft, top - SliderTopOffset);
        slider.Size = new Size(CardWidth - CardPadding - ValueWidth - sliderLeft - SliderRightGap, SliderHeight);
        valueLabel.Location = new Point(CardWidth - CardPadding - ValueWidth, top);

        Controls.AddRange([captionLabel, slider, valueLabel]);
        whiteControls?.AddRange([captionLabel, slider, valueLabel]);
    }

    private void ShowMode(int modeIndex)
    {
        var isColorMode = modeIndex == 1;
        foreach (var control in whiteControls)
        {
            control.Visible = !isColorMode;
        }

        foreach (var control in colorControls)
        {
            control.Visible = isColorMode;
        }

        var newHeight = isColorMode ? ColorModeHeight : WhiteModeHeight;
        if (Height != newHeight)
        {
            Height = newHeight;
            LayoutHeightChanged?.Invoke();
        }
    }

    private void BeginInvokeIfCreated(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(action);
        }
    }

    private void RefreshStatus()
    {
        var isConnected = light.IsConnected;
        statusLabel.Text = light.Connection switch
        {
            ConnectionState.Connected => "● Connected",
            ConnectionState.Connecting => "● Connecting…",
            ConnectionState.Released => "● Released",
            _ => light.LastError is null ? "● Reconnecting…" : $"● Reconnecting… {light.LastError}",
        };
        statusLabel.ForeColor = isConnected ? Theme.Success : light.IsReleased ? Theme.TextSecondary : Theme.Warning;

        powerToggle.Enabled = isConnected;
        brightnessSlider.Enabled = isConnected;
        warmthSlider.Enabled = isConnected;
        hueSlider.Enabled = isConnected;
        saturationSlider.Enabled = isConnected;
        swatchRow.Enabled = isConnected;
    }

    private void ChangeConnection(bool shouldBeConnected)
    {
        if (shouldBeConnected)
        {
            light.StartKeepingConnected();
            return;
        }

        light.Release();
    }

    private void ApplyLightState(LightState state)
    {
        powerToggle.SetCheckedSilently(state.IsOn);
        brightnessSlider.SetValueSilently(state.Brightness);
        warmthSlider.SetValueSilently(state.TemperatureMirek);
    }

    private void UpdateSaturationGradient()
    {
        saturationSlider.Gradient = [Color.White, Theme.FromHsv(hueSlider.Value, 1, 1)];
    }

    private void OnSwatchClicked(int swatchIndex)
    {
        hueSlider.SetValueSilently((int)SwatchHues[swatchIndex]);
        saturationSlider.SetValueSilently(100);
        QueueSend(color: true);
    }

    private void QueueSend(bool brightness = false, bool warmth = false, bool color = false)
    {
        hasPendingBrightness |= brightness;
        hasPendingWarmth |= warmth;
        hasPendingColor |= color;
        sendTimer.Start();
    }

    private async Task SendPending()
    {
        sendTimer.Stop();
        if (isSending)
        {
            sendTimer.Start();
            return;
        }

        isSending = true;
        try
        {
            while (hasPendingBrightness || hasPendingWarmth || hasPendingColor)
            {
                var shouldSendBrightness = hasPendingBrightness;
                var shouldSendWarmth = hasPendingWarmth;
                var shouldSendColor = hasPendingColor;
                hasPendingBrightness = false;
                hasPendingWarmth = false;
                hasPendingColor = false;

                if (shouldSendBrightness)
                {
                    await RunCommand(() => light.SetBrightness((byte)brightnessSlider.Value));
                }

                if (shouldSendWarmth)
                {
                    await RunCommand(() => light.SetTemperature((ushort)warmthSlider.Value));
                }

                if (shouldSendColor)
                {
                    await RunCommand(() => light.SetColor(hueSlider.Value, saturationSlider.Value / 100.0));
                }
            }
        }
        finally
        {
            isSending = false;
        }
    }

    private async Task RunCommand(Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception exception)
        {
            Logger.Write($"{light.SavedLight.Name}: command failed", exception);
            statusLabel.Text = $"● {exception.Message}";
            statusLabel.ForeColor = Theme.Danger;
        }
    }
}

