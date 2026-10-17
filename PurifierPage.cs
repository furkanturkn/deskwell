namespace HueDesk;

public sealed class PurifierPage : Panel
{
    public const int PageHeight = 192;

    private const int SideMargin = 16;
    private const int ContentWidth = 380;
    private const int InnerWidth = ContentWidth - 2 * SideMargin;
    private const int TitleTop = 12;
    private const int StatusTop = 40;
    private const int StatusHeight = 36;
    private const int ModeRowTop = 90;
    private const int ModeChipWidth = 84;
    private const int ModeChipHeight = 30;
    private const int ModeChipGap = 6;
    private const int RefreshIntervalMilliseconds = 5000;
    private const int SpeedRowTop = 138;
    private const int CaptionWidth = 76;
    private const int ValueWidth = 44;
    private const int ValueHeight = 20;
    private const int SliderHeight = 26;
    private const int SliderRightGap = 4;
    private const int SpeedSendDelayMilliseconds = 250;

    private readonly AirPurifier? purifier;
    private readonly PowerButton powerButton = new() { Icon = PowerIcon.Power };
    private readonly Label statusLabel = new() { AutoSize = false, Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent };
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = RefreshIntervalMilliseconds };
    private readonly List<Chip> modeChips = [];
    private readonly Label speedCaption = new() { Text = "Speed", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.BodyFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent, Visible = false };
    private readonly ModernSlider speedSlider = new() { Minimum = AirPurifier.MinimumFanLevel, Maximum = AirPurifier.MaximumFanLevel, Visible = false };
    private readonly Label speedValue = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Font = Theme.SmallFont, ForeColor = Theme.TextSecondary, BackColor = Color.Transparent, Visible = false };
    private readonly System.Windows.Forms.Timer speedSendTimer = new() { Interval = SpeedSendDelayMilliseconds };
    private bool isRefreshing;

    public PurifierPage(PurifierConfig? config)
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        Visible = false;
        BuildLayout();

        if (config is null)
        {
            ShowMissingConfiguration();
            return;
        }

        purifier = new AirPurifier(config);
        powerButton.CheckedChanged += (_, _) => _ = Run(() => purifier.SetPower(powerButton.Checked));
        refreshTimer.Tick += (_, _) => _ = Refresh();
        speedSlider.ValueChanged += (_, _) => speedValue.Text = $"{speedSlider.Value}";
        speedSlider.UserChanged += (_, _) =>
        {
            speedSendTimer.Stop();
            speedSendTimer.Start();
        };
        speedSendTimer.Tick += (_, _) =>
        {
            speedSendTimer.Stop();
            _ = Run(() => purifier.SetFanLevel(speedSlider.Value));
        };
    }

    public void SetActive(bool isActive)
    {
        if (purifier is null)
        {
            return;
        }

        refreshTimer.Enabled = isActive;
        if (isActive)
        {
            _ = Refresh();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refreshTimer.Dispose();
            speedSendTimer.Dispose();
            purifier?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var title = new Label { Text = "Air purifier", AutoSize = true, Font = Theme.BodyFont, ForeColor = Theme.TextPrimary, BackColor = Color.Transparent, Location = new Point(SideMargin, TitleTop) };
        powerButton.Location = new Point(ContentWidth - SideMargin - powerButton.Width, TitleTop - 4);
        statusLabel.Size = new Size(InnerWidth, StatusHeight);
        statusLabel.Location = new Point(SideMargin, StatusTop);
        Controls.AddRange([title, powerButton, statusLabel]);

        var sliderLeft = SideMargin + CaptionWidth;
        speedCaption.Size = new Size(CaptionWidth, SliderHeight);
        speedCaption.Location = new Point(SideMargin, SpeedRowTop);
        speedSlider.Location = new Point(sliderLeft, SpeedRowTop);
        speedSlider.Size = new Size(ContentWidth - SideMargin - ValueWidth - sliderLeft - SliderRightGap, SliderHeight);
        speedValue.Size = new Size(ValueWidth, ValueHeight);
        speedValue.Location = new Point(ContentWidth - SideMargin - ValueWidth, SpeedRowTop + 3);
        Controls.AddRange([speedCaption, speedSlider, speedValue]);

        foreach (var mode in Enum.GetValues<PurifierMode>())
        {
            var chip = new Chip { Text = mode.ToString(), Size = new Size(ModeChipWidth, ModeChipHeight), Location = new Point(SideMargin + modeChips.Count * (ModeChipWidth + ModeChipGap), ModeRowTop) };
            chip.Click += (_, _) => _ = Run(() => purifier!.SetMode(mode));
            modeChips.Add(chip);
            Controls.Add(chip);
        }
    }

    private void ShowMissingConfiguration()
    {
        powerButton.Enabled = false;
        foreach (var chip in modeChips)
        {
            chip.Enabled = false;
        }

        statusLabel.Text = $"Not set up. Create {PurifierConfig.FilePath} with your purifier's ip and token.";
    }

    private async Task Run(Func<Task> command)
    {
        try
        {
            await command();
            await Refresh();
        }
        catch (Exception exception)
        {
            Logger.Write("purifier: command failed", exception);
            ShowError();
        }
    }

    private async Task Refresh()
    {
        if (isRefreshing || purifier is null)
        {
            return;
        }

        isRefreshing = true;
        try
        {
            var state = await purifier.ReadState();
            if (!IsDisposed)
            {
                ShowState(state);
            }
        }
        catch (Exception exception)
        {
            Logger.Write("purifier: refresh failed", exception);
            if (!IsDisposed)
            {
                ShowError();
            }
        }
        finally
        {
            isRefreshing = false;
        }
    }

    private void ShowState(PurifierState state)
    {
        powerButton.Enabled = true;
        powerButton.SetCheckedSilently(state.IsOn);
        var density = state.ParticleDensity is { } value ? $"PM2.5 {value} µg/m³" : "PM2.5 –";
        var filter = state.FilterLifePercent is { } life ? $"Filter {life}%" : "Filter –";
        var mode = state.Mode?.ToString() ?? "–";
        var isManual = state.Mode == PurifierMode.Manual;
        speedCaption.Visible = isManual;
        speedSlider.Visible = isManual;
        speedValue.Visible = isManual;
        if (isManual && state.FanLevel is { } level && !speedSendTimer.Enabled)
        {
            speedSlider.SetValueSilently(level);
            speedValue.Text = $"{level}";
        }

        statusLabel.ForeColor = Theme.TextSecondary;
        statusLabel.Text = $"{(state.IsOn ? "On" : "Off")}  ·  {mode}\n{density}  ·  {filter}";
    }

    private void ShowError()
    {
        statusLabel.ForeColor = Theme.Warning;
        statusLabel.Text = "Can't reach the purifier. Check the IP and token, see log.txt.";
    }
}
