using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidBar.SDK;

namespace WorkTimer.Widget;

public sealed class ClockPlugin : WidgetPluginBase, IWidgetFlyoutLifecycle
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TextBlock? _times;
    private TextBlock? _remaining;
    private FontIcon? _previewClock;
    private bool _blinkRed;
    private TextBlock? _cardTimes;
    private StackPanel? _cardTimesRow;
    private TextBlock? _headline;
    private Border? _workedBadge;
    private TextBlock? _workedText;
    private TextBlock? _fallbackDetails;
    private Button? _clockIn;
    private Button? _clockOut;
    private Button? _history;
    private ToggleSwitch? _autoStart;
    private InfoBar? _notice;
    private FontIcon? _warning;
    private TimePickerFlyout? _timePickerFlyout;
    private bool _updatingControls;
    private bool _requestPending;
    private string? _commandError;
    private bool _trackerAvailable;
    private bool _flyoutShown;
    private DateTime _lastAutoStartAttempt = DateTime.MinValue;

    public override string Id => "com.n2k02.worktimer";
    public override string Name => "WorkTimer";
    public override int PreviewLogicalWidth => 172;
    public override int FlyoutWidth => 360;
    public override int FlyoutHeight => 355;

    public override async Task InitializeAsync(IWidgetContext context)
    {
        await base.InitializeAsync(context);
        EnsureTrackerRunningIfStarted();
        _timer.Tick += OnTick;
        context.PreviewVisibilityChanged += OnVisibilityChanged;
        UpdateTimer();
    }

    private static (DateTime ClockIn, DateTime? ClockOut)? GetActiveDayFromState()
    {
        var stateFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkTimer", "state.json");
        if (!File.Exists(stateFile))
        {
            var legacyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClockInTray", "state.json");
            if (File.Exists(legacyFile)) stateFile = legacyFile;
            else return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(stateFile));
            var root = doc.RootElement;
            if (root.TryGetProperty("Date", out var dateProp)
                && DateOnly.TryParse(dateProp.GetString(), out var date)
                && date == DateOnly.FromDateTime(DateTime.Now))
            {
                if (root.TryGetProperty("ClockIn", out var ciProp)
                    && ciProp.GetString() is { } ciStr
                    && DateTime.TryParse(ciStr, out var clockIn))
                {
                    DateTime? clockOut = null;
                    if (root.TryGetProperty("ClockOut", out var coProp)
                        && coProp.GetString() is { } coStr
                        && DateTime.TryParse(coStr, out var co))
                    {
                        clockOut = co;
                    }
                    return (clockIn, clockOut);
                }
            }
        }
        catch { }
        return null;
    }

    private void EnsureTrackerRunningIfStarted()
    {
        if (DateTime.UtcNow - _lastAutoStartAttempt < TimeSpan.FromSeconds(10)) return;

        var status = WidgetStatus.Read(WidgetStatus.FilePath, DateTime.UtcNow);
        if (status.IsAvailable) return;

        var active = GetActiveDayFromState();
        if (active is { ClockOut: null })
        {
            _lastAutoStartAttempt = DateTime.UtcNow;
            try
            {
                TrackerCommands.StartTracker();
            }
            catch { }
        }
    }

    public override UIElement CreatePreviewContent()
    {
        _times = new TextBlock { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _remaining = new TextBlock { FontSize = 12, Opacity = 0.8 };
        _previewClock = new FontIcon { Glyph = "\uE917", FontSize = 21, VerticalAlignment = VerticalAlignment.Center };
        var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_times);
        text.Children.Add(_remaining);
        var root = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 4, 0)
        };
        root.Children.Add(_previewClock);
        root.Children.Add(text);
        _warning = new FontIcon { Glyph = "\uE7BA", FontSize = 12, Visibility = Visibility.Collapsed };
        root.Children.Add(_warning);
        Refresh();
        return root;
    }

    public override UIElement CreateFlyoutContent()
    {
        var panel = new StackPanel { Spacing = 10, Padding = new Thickness(16, 14, 16, 14) };

        // 1. Status Hero Card (Times + Time Left on left, Worked badge on far right)
        var statusCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            BorderThickness = new Thickness(1),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush")
        };

        var cardStack = new StackPanel { Spacing = 6 };

        _cardTimesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var clockBadge = new FontIcon { Glyph = "\uE823", FontSize = 13, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center };
        _cardTimes = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center
        };
        _cardTimesRow.Children.Add(clockBadge);
        _cardTimesRow.Children.Add(_cardTimes);
        cardStack.Children.Add(_cardTimesRow);

        // Row with time left on left, worked time on far right (both vertically centered)
        var timeGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _headline = new TextBlock
        {
            FontSize = 26,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_headline, 0);
        timeGrid.Children.Add(_headline);

        _workedBadge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = ThemeBrush("SubtleFillColorSecondaryBrush") ?? ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed
        };
        _workedText = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Opacity = 0.9
        };
        _workedBadge.Child = _workedText;
        Grid.SetColumn(_workedBadge, 1);
        timeGrid.Children.Add(_workedBadge);

        cardStack.Children.Add(timeGrid);

        _fallbackDetails = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
            FontSize = 13,
            Visibility = Visibility.Collapsed
        };
        cardStack.Children.Add(_fallbackDetails);

        statusCard.Child = cardStack;
        panel.Children.Add(statusCard);

        // 2. Primary action buttons (Clock in / Edit clock-in & Clock out)
        _timePickerFlyout = new TimePickerFlyout { ClockIdentifier = "24HourClock", MinuteIncrement = 1 };
        _timePickerFlyout.TimePicked += async (_, e) =>
        {
            var editFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkTimer", "edit-clockin.txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(editFile)!);
                File.WriteAllText(editFile, e.NewTime.ToString(@"hh\:mm"));
            }
            catch { }
            await SendCommandAsync(TrackerCommand.ClockIn);
        };

        _clockIn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            MinHeight = 40,
            CornerRadius = new CornerRadius(6)
        };
        _clockOut = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 40,
            CornerRadius = new CornerRadius(6)
        };
        _clockIn.Click += async (_, _) =>
        {
            if (!_trackerAvailable)
            {
                _commandError = null;
                try { TrackerCommands.StartTracker(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                { _commandError = ex.Message; }
                Refresh();
            }
            else if (_clockIn.Flyout is not null)
            {
                // Has TimePickerFlyout (Edit clock-in) - opens automatically on click
            }
            else
            {
                // First clock-in: immediate clock in at current time!
                await SendCommandAsync(TrackerCommand.ClockIn);
            }
        };
        _clockOut.Click += async (_, _) => await SendCommandAsync(TrackerCommand.ClockOut);

        var actions = new Grid { ColumnSpacing = 10 };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(_clockIn, 0);
        Grid.SetColumn(_clockOut, 1);
        actions.Children.Add(_clockIn);
        actions.Children.Add(_clockOut);
        panel.Children.Add(actions);

        // 3. Notice / Warning banner
        _notice = new InfoBar { IsClosable = false, Severity = InfoBarSeverity.Warning, Visibility = Visibility.Collapsed };
        panel.Children.Add(_notice);

        // 4. Options Card (History button, auto-start toggle, restart/exit)
        var optionsCard = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(1),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush")
        };

        var optionsStack = new StackPanel { Spacing = 6 };

        var historyContent = new Grid();
        historyContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        historyContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        historyContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var histIcon = new FontIcon { Glyph = "\uE81C", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var histText = new TextBlock { Text = "Open history", VerticalAlignment = VerticalAlignment.Center };
        var histArrow = new FontIcon { Glyph = "\uE76C", FontSize = 11, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(histIcon, 0);
        Grid.SetColumn(histText, 1);
        Grid.SetColumn(histArrow, 2);
        historyContent.Children.Add(histIcon);
        historyContent.Children.Add(histText);
        historyContent.Children.Add(histArrow);

        _history = new Button
        {
            Content = historyContent,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 34,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(6)
        };
        if (Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var subtleStyle2) && subtleStyle2 is Style s2)
        {
            _history.Style = s2;
        }
        _history.Click += async (_, _) => await SendCommandAsync(TrackerCommand.OpenHistory);
        optionsStack.Children.Add(_history);

        var divider1 = new Border { Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 1, 0, 1), Background = ThemeBrush("CardStrokeColorDefaultBrush") };
        optionsStack.Children.Add(divider1);

        var autoStartRow = new Grid { VerticalAlignment = VerticalAlignment.Center };
        autoStartRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        autoStartRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var autoStartLabel = new TextBlock
        {
            Text = "Start tracker with Windows",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(autoStartLabel, 0);
        autoStartRow.Children.Add(autoStartLabel);

        _autoStart = new ToggleSwitch
        {
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        AutomationProperties.SetName(_autoStart, "Start tracker with Windows");
        _autoStart.Toggled += async (_, _) =>
        {
            if (!_updatingControls)
                await SendCommandAsync(_autoStart.IsOn ? TrackerCommand.EnableAutoStart : TrackerCommand.DisableAutoStart);
        };
        Grid.SetColumn(_autoStart, 1);
        autoStartRow.Children.Add(_autoStart);
        optionsStack.Children.Add(autoStartRow);

        var divider2 = new Border { Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 1, 0, 1), Background = ThemeBrush("CardStrokeColorDefaultBrush") };
        optionsStack.Children.Add(divider2);

        var trackerActions = new Grid { ColumnSpacing = 8 };
        trackerActions.ColumnDefinitions.Add(new ColumnDefinition());
        trackerActions.ColumnDefinitions.Add(new ColumnDefinition());

        var restartBtn = new Button
        {
            Content = CreateButtonContent("\uE777", "Restart tracker"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 30,
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 12,
            CornerRadius = new CornerRadius(6)
        };
        var exitBtn = new Button
        {
            Content = CreateButtonContent("\uE8BB", "Exit tracker"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 30,
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 12,
            CornerRadius = new CornerRadius(6)
        };
        if (Application.Current.Resources.TryGetValue("SubtleButtonStyle", out var subtleStyle3) && subtleStyle3 is Style s3)
        {
            restartBtn.Style = s3;
            exitBtn.Style = s3;
        }
        restartBtn.Click += async (_, _) => await SendCommandAsync(TrackerCommand.Restart);
        exitBtn.Click += async (_, _) => await SendCommandAsync(TrackerCommand.Exit);
        Grid.SetColumn(restartBtn, 0);
        Grid.SetColumn(exitBtn, 1);
        trackerActions.Children.Add(restartBtn);
        trackerActions.Children.Add(exitBtn);
        optionsStack.Children.Add(trackerActions);

        optionsCard.Child = optionsStack;
        panel.Children.Add(optionsCard);

        Refresh();
        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var res) && res is Brush brush ? brush : null;

    private static UIElement CreateButtonContent(string glyph, string text)
    {
        var sp = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        sp.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    private static void SetButtonContent(Button button, string glyph, string text)
    {
        if (button.Content is StackPanel sp && sp.Children.Count == 2
            && sp.Children[0] is FontIcon icon && sp.Children[1] is TextBlock tb)
        {
            if (icon.Glyph != glyph) icon.Glyph = glyph;
            if (tb.Text != text) tb.Text = text;
            return;
        }
        button.Content = CreateButtonContent(glyph, text);
    }

    private void UpdateDetailsCard(WidgetStatus status)
    {
        if (_workedBadge is null || _workedText is null || _fallbackDetails is null)
            return;

        var lines = (status.Details ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length >= 2 && lines.Any(l => l.StartsWith("Worked", StringComparison.OrdinalIgnoreCase)))
        {
            var workedLine = lines.First(l => l.StartsWith("Worked", StringComparison.OrdinalIgnoreCase));
            var workedParts = workedLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var duration = workedParts.Length > 1 ? workedParts[1] : workedLine;
            _workedText.Text = $"Worked {duration}";
            _workedBadge.Visibility = Visibility.Visible;
            _fallbackDetails.Visibility = Visibility.Collapsed;
        }
        else if (lines.Length >= 1 && lines[0].StartsWith("Clocked out", StringComparison.OrdinalIgnoreCase))
        {
            var workedLine = lines.FirstOrDefault(l => l.StartsWith("Worked", StringComparison.OrdinalIgnoreCase));
            if (workedLine is not null)
            {
                var parts = workedLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var duration = parts.Length > 1 ? parts[1] : workedLine;
                _workedText.Text = $"Worked {duration}";
                _workedBadge.Visibility = Visibility.Visible;
            }
            else
            {
                _workedBadge.Visibility = Visibility.Collapsed;
            }
            _fallbackDetails.Visibility = Visibility.Collapsed;
        }
        else
        {
            _workedBadge.Visibility = Visibility.Collapsed;
            _fallbackDetails.Text = string.Join("\n", lines);
            _fallbackDetails.Visibility = string.IsNullOrWhiteSpace(_fallbackDetails.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private async Task SendCommandAsync(TrackerCommand command)
    {
        if (_requestPending) return;
        _requestPending = true;
        _commandError = null;
        Refresh();
        try
        {
            await TrackerCommands.SendAsync(command);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            _commandError = "The tracker is unavailable. Start WorkTimer and try again.";
        }
        finally
        {
            _requestPending = false;
            Refresh();
        }
    }

    private void Refresh()
    {
        var status = WidgetStatus.Read(WidgetStatus.FilePath, DateTime.UtcNow);

        // Auto-resume if day was already started but tracker process is not currently running
        var activeDay = GetActiveDayFromState();
        if (!status.IsAvailable && activeDay is { ClockIn: { } ci, ClockOut: null })
        {
            EnsureTrackerRunningIfStarted();

            var elapsed = DateTime.Now - ci;
            var workDay = TimeSpan.FromHours(9);
            var remaining = workDay - elapsed;
            var done = remaining <= TimeSpan.Zero;
            var overtime = done ? -remaining : TimeSpan.Zero;
            if (done) remaining = TimeSpan.Zero;
            var iconRemaining = TimeSpan.FromMinutes(Math.Ceiling(remaining.TotalMinutes));
            var iconText = done
                ? $"{(int)overtime.TotalHours}:{overtime.Minutes:00} overtime"
                : $"{(int)iconRemaining.TotalHours}:{iconRemaining.Minutes:00} left";

            status = new WidgetStatus(
                $"{ci:HH:mm} - {ci + workDay:HH:mm} ({iconText})",
                $"{ci:HH:mm} - {ci + workDay:HH:mm}",
                iconText,
                done
                    ? $"Overtime {(int)overtime.TotalHours}:{overtime.Minutes:00}:{overtime.Seconds:00}\nWorked {(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00} (in at {ci:HH:mm})"
                    : $"Remaining {(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}\nWorked {(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00} (in at {ci:HH:mm})\nClock out at {ci + workDay:HH:mm}",
                DateTime.UtcNow)
            {
                IsAvailable = true,
                CanClockIn = true,
                CanClockOut = true,
                AutoStart = true
            };
        }

        if (status.IsAvailable && !_trackerAvailable) _commandError = null;
        _trackerAvailable = status.IsAvailable;

        bool isClockInRequired = status.IsAvailable && status.CanClockIn && !status.CanClockOut && status.Remaining != "Weekend";
        bool isClockedIn = status.IsAvailable && status.CanClockOut;
        bool isOvertime = isClockedIn && (status.Remaining?.Contains("overtime", StringComparison.OrdinalIgnoreCase) ?? false);
        bool isClockedOut = status.IsAvailable && !status.CanClockOut && status.Remaining == "Clocked out";

        // Taskbar preview text
        if (_times is not null)
        {
            _times.Text = isClockInRequired ? "ClockIn Required" : status.Times;
            AutomationProperties.SetName(_times, status.Title);
            ToolTipService.SetToolTip(_times, status.Details);
        }
        if (_remaining is not null)
        {
            _remaining.Text = isClockInRequired ? "Click to clock in" : status.Remaining;
        }

        // Taskbar preview clock icon: color & blink
        if (_previewClock is not null)
        {
            if (isClockInRequired)
            {
                _blinkRed = !_blinkRed;
                _previewClock.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 239, 68, 68)); // Red
                _previewClock.Opacity = _blinkRed ? 1.0 : 0.2;
            }
            else if (isOvertime)
            {
                _previewClock.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 245, 158, 11)); // Orange
                _previewClock.Opacity = 1.0;
            }
            else if (isClockedIn)
            {
                _previewClock.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 197, 94)); // Green
                _previewClock.Opacity = 1.0;
            }
            else if (isClockedOut)
            {
                _previewClock.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 156, 163, 175)); // Gray
                _previewClock.Opacity = 0.8;
            }
            else
            {
                _previewClock.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 156, 163, 175));
                _previewClock.Opacity = 0.5;
            }
        }

        if (_warning is not null)
        {
            _warning.Visibility = string.IsNullOrEmpty(status.Notice) ? Visibility.Collapsed : Visibility.Visible;
            ToolTipService.SetToolTip(_warning, status.Notice);
        }

        // Flyout card
        if (_cardTimes is not null && _cardTimesRow is not null)
        {
            if (status.Times.Contains(" - "))
            {
                _cardTimes.Text = status.Times.Replace(" - ", " \u2013 ");
                _cardTimesRow.Visibility = Visibility.Visible;
            }
            else
            {
                _cardTimes.Text = status.Times;
                _cardTimesRow.Visibility = status.Times is "WorkTimer" or "ClockInTray" or "ClockIn Required" or "Weekend" ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        if (_headline is not null)
        {
            _headline.Text = isClockInRequired ? "ClockIn Required" : status.Remaining;
        }

        UpdateDetailsCard(status);

        if (_notice is not null)
        {
            _notice.Message = _commandError ?? status.Notice ?? "";
            _notice.IsOpen = !string.IsNullOrEmpty(_notice.Message);
            _notice.Visibility = _notice.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_clockIn is not null)
        {
            _clockIn.IsEnabled = (!_trackerAvailable || status.CanClockIn) && !_requestPending;
            if (status.CanClockOut)
            {
                _clockIn.Flyout = _timePickerFlyout;
                SetButtonContent(_clockIn, "\uE70F", "Edit clock-in");
            }
            else
            {
                _clockIn.Flyout = null;
                SetButtonContent(_clockIn, !_trackerAvailable ? "\uE768" : "\uE825", !_trackerAvailable ? "Start tracker" : "Clock in");
            }
        }
        if (_clockOut is not null)
        {
            _clockOut.IsEnabled = status.IsAvailable && status.CanClockOut && !_requestPending;
            SetButtonContent(_clockOut, "\uE823", "Clock out");
        }
        if (_history is not null) _history.IsEnabled = status.IsAvailable && !_requestPending;
        if (_autoStart is not null)
        {
            _updatingControls = true;
            _autoStart.IsOn = status.AutoStart;
            _autoStart.IsEnabled = status.IsAvailable && !_requestPending;
            _updatingControls = false;
        }
    }

    private void OnTick(object? sender, object e) => Refresh();
    private void OnVisibilityChanged(object? sender, bool visible) => UpdateTimer();

    private void UpdateTimer()
    {
        Refresh();
        if ((Context?.IsPreviewVisible ?? true) || _flyoutShown) _timer.Start();
        else _timer.Stop();
    }

    public void OnFlyoutShown() { _flyoutShown = true; UpdateTimer(); }
    public void OnFlyoutHidden() { _flyoutShown = false; UpdateTimer(); }

    public override ValueTask DisposeAsync()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        if (Context is not null) Context.PreviewVisibilityChanged -= OnVisibilityChanged;
        return ValueTask.CompletedTask;
    }
}