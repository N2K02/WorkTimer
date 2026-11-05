using Microsoft.Win32;

namespace WorkTimer;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan WindowStart = new(7, 30, 0);
    private static readonly TimeSpan WindowEnd = new(10, 30, 0);
    private static readonly TimeSpan WorkDay = TimeSpan.FromHours(9);
    private static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HistoryRetryInterval = TimeSpan.FromSeconds(10);

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "WorkTimer";
    private const string HistoryLockedNotice = "Close WorkTimerHistory.csv in Excel; it will be saved automatically.";

    private readonly object _sync = new();
    private readonly Control _ui = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly List<WorkDayState> _pendingHistory = [];
    private readonly WidgetStatusPublisher _widgetPublisher = new();
    private readonly TrackerCommandServer _commands;
    private bool _autoStartEnabled;
    private string? _notice;
    private WidgetStatus? _currentStatus;
    private bool _canClockIn;
    private bool _canClockOut;

    public bool RestartRequested { get; private set; }

    private WorkDayState _state;
    private DateTime? _lastTick;
    private DateTime _lastSaved = DateTime.MinValue;
    private DateTime _lastHistoryAttempt = DateTime.MinValue;
    private DateTime _nextReminder = DateTime.MinValue;
    private TimeDialog? _openDialog;
    private bool _dialogOpen;

    public TrayApplicationContext()
    {
        var firstRun = !WorkDayState.Exists;
        _state = WorkDayState.Load();
        _ = _ui.Handle;

        _autoStartEnabled = IsAutoStartEnabled();
        if (Environment.ProcessPath is { } path) TrackerCommands.RegisterExecutable(path);
        _commands = new TrackerCommandServer(QueueCommand);

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.SessionEnding += OnSessionEnding;

        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        if (firstRun)
        {
            SetAutoStart(true);
            _autoStartEnabled = true;
        }

        // Defer so the message loop is running before showing the dialog.
        var startup = new System.Windows.Forms.Timer { Interval = 500 };
        startup.Tick += (_, _) =>
        {
            startup.Stop();
            startup.Dispose();
            OnStartup();
        };
        startup.Start();
    }

    private void OnStartup()
    {
        lock (_sync)
        {
            RolloverIfNeeded(DateTime.Now);
            _state.Save();
            if (_state.ClockIn is not null)
            {
                RecordHistory(_state);
            }
            UpdateDisplay(DateTime.Now);
        }
        Tick();
    }

    private static bool IsInPromptWindow(DateTime now) =>
        now.TimeOfDay >= WindowStart && now.TimeOfDay <= WindowEnd;

    private static bool IsWeekend(DateTime now) =>
        now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private bool CanClockIn(DateTime now) => _state.ClockIn is not null || !IsWeekend(now);
    private bool CanClockOut => _state.ClockIn is not null;

    private void QueueCommand(TrackerCommand command)
    {
        if (_ui.IsDisposed) return;
        try { _ui.BeginInvoke(() => ExecuteCommand(command)); }
        catch (InvalidOperationException) { }
    }

    private void ExecuteCommand(TrackerCommand command)
    {
        if (_ui.IsDisposed)
        {
            return;
        }

        _notice = null;
        try
        {
            lock (_sync) RolloverIfNeeded(DateTime.Now);
            switch (command)
            {
                case TrackerCommand.ClockIn:
                    if (CanClockIn(DateTime.Now))
                    {
                        var editFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkTimer", "edit-clockin.txt");
                        if (!File.Exists(editFile))
                        {
                            var legacyEditFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClockInTray", "edit-clockin.txt");
                            if (File.Exists(legacyEditFile)) editFile = legacyEditFile;
                        }

                        var clockInTime = DateTime.Now;
                        if (File.Exists(editFile))
                        {
                            try
                            {
                                var text = File.ReadAllText(editFile).Trim();
                                if (TimeSpan.TryParse(text, out var ts))
                                {
                                    clockInTime = DateTime.Today.Add(ts);
                                }
                                File.Delete(editFile);
                            }
                            catch (Exception) { }
                        }

                        lock (_sync)
                        {
                            _state.ClockIn = clockInTime;
                            _state.Prompted = true;
                            _nextReminder = DateTime.MinValue;
                            if (_state.ClockOut <= _state.ClockIn)
                            {
                                _state.ClockOut = null;
                                _state.ClockOutAuto = false;
                                _state.ClockOutUnknownIssue = false;
                            }
                            _state.Save();
                            RecordHistory(_state);
                        }
                        Tick();
                    }
                    break;
                case TrackerCommand.ClockOut:
                    if (CanClockOut)
                    {
                        lock (_sync)
                        {
                            _state.ClockOut = DateTime.Now;
                            _state.ClockOutAuto = false;
                            _state.ClockOutUnknownIssue = false;
                            _state.Save();
                            RecordHistory(_state);
                        }
                        Tick();
                    }
                    break;
                case TrackerCommand.OpenHistory:
                    OpenHistory();
                    break;
                case TrackerCommand.EnableAutoStart:
                case TrackerCommand.DisableAutoStart:
                    var enabled = command == TrackerCommand.EnableAutoStart;
                    SetAutoStart(enabled);
                    _autoStartEnabled = enabled;
                    break;
                case TrackerCommand.Restart:
                    RestartRequested = true;
                    ExitThread();
                    return;
                case TrackerCommand.Exit:
                    ExitThread();
                    return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _notice = ex.Message;
        }

        lock (_sync) UpdateDisplay(DateTime.Now);
    }

    private void PromptClockIn()
    {
        if (_dialogOpen || (IsWeekend(DateTime.Now) && _state.ClockIn is null))
        {
            return;
        }

        DateOnly dialogDate;
        DateTime defaultTime;
        lock (_sync)
        {
            dialogDate = _state.Date;
            _state.Prompted = true;
            defaultTime = _state.ClockIn ?? DateTime.Now;
        }

        _dialogOpen = true;
        using var dialog = new TimeDialog(
            "Clock in", "What time are you clocking in today?", defaultTime, DateTime.Today, "Start timer", "Not now");
        _openDialog = dialog;
        UpdateDisplay(DateTime.Now);
        var result = dialog.ShowDialog();
        _openDialog = null;
        _dialogOpen = false;

        lock (_sync)
        {
            if (_state.Date == dialogDate && result != DialogResult.Abort)
            {
                if (result == DialogResult.OK)
                {
                    _state.ClockIn = dialog.Time;
                    _nextReminder = DateTime.MinValue;
                    if (_state.ClockOut <= _state.ClockIn)
                    {
                        _state.ClockOut = null;
                        _state.ClockOutAuto = false;
                        _state.ClockOutUnknownIssue = false;
                    }
                }

                _state.Save();
                RecordHistory(_state);
            }
        }

        Tick();
    }

    private void PromptClockOut(bool reminder)
    {
        if (_dialogOpen)
        {
            return;
        }

        DateOnly dialogDate;
        DateTime clockIn;
        DateTime defaultTime;
        lock (_sync)
        {
            if (_state.ClockIn is not { } ci)
            {
                _notice = "You haven't clocked in today.";
                return;
            }

            dialogDate = _state.Date;
            clockIn = ci;
            defaultTime = _state.ClockOut ?? DateTime.Now;
        }

        var message = reminder
            ? $"You've reached 9 hours (in at {clockIn:HH:mm}).\nWhat time are you clocking out?"
            : $"You clocked in at {clockIn:HH:mm}.\nWhat time are you clocking out?";

        _dialogOpen = true;
        using var dialog = new TimeDialog("Time to clock out", message, defaultTime, clockIn, "Clock out");
        _openDialog = dialog;
        dialog.EnableLive(clockIn, WorkDay, message);
        UpdateDisplay(DateTime.Now);
        var result = dialog.ShowDialog();
        _openDialog = null;
        _dialogOpen = false;

        lock (_sync)
        {
            if (_state.Date == dialogDate)
            {
                if (result == DialogResult.OK)
                {
                    _state.ClockOut = dialog.Time;
                    _state.ClockOutAuto = false;
                    _state.ClockOutUnknownIssue = false;
                    _state.Save();
                    RecordHistory(_state);
                }
                else if (result == DialogResult.Cancel && reminder)
                {
                    _nextReminder = DateTime.Now + ReminderInterval;
                }
            }
        }

        Tick();
    }

    private void Tick()
    {
        bool promptClockIn;
        bool promptClockOut;
        lock (_sync)
        {
            var now = DateTime.Now;

            // A long gap between ticks means the PC slept (lid closed): clock out at the last moment we were awake.
            if (_lastTick is { } last && now - last > SleepGap)
            {
                AutoClockOutIfDue(last);
            }

            RolloverIfNeeded(now);

            _lastTick = now;
            _state.LastSeen = now;
            if (now - _lastSaved >= SaveInterval)
            {
                _state.Save();
                _lastSaved = now;
            }

            if (_pendingHistory.Count > 0 && now - _lastHistoryAttempt >= HistoryRetryInterval)
            {
                FlushHistory(silent: false);
            }

            UpdateDisplay(now);
            promptClockIn = false;

            promptClockOut = false;

        }

        if (promptClockIn)
        {
            PromptClockIn();
        }
        else if (promptClockOut)
        {
            PromptClockOut(reminder: true);
        }
    }

    // Closes a previous day that was never clocked out, using the last time the app saw the PC awake.
    private void RolloverIfNeeded(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (_state.Date >= today)
        {
            return;
        }

        CloseOpenDialog();

        if (_state.ClockIn is { } clockIn && _state.ClockOut is null)
        {
            // The app never saw the PC leave (crash or power cut), so the day is closed with an error marker.
            // Even then the clock-out is never earlier than the 9 hours.
            var planned = clockIn + WorkDay;
            var sameDaySeen = _state.LastSeen is { } seen && DateOnly.FromDateTime(seen) == _state.Date && seen > planned
                ? seen
                : planned;
            _state.ClockOut = sameDaySeen;
            _state.ClockOutAuto = true;
            _state.ClockOutUnknownIssue = true;
        }
        else if (_state.ClockIn is null && _state.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            _state.UserOffline = true;
        }

        if (_state.ClockIn is not null || _state.UserOffline)
        {
            QueueHistory(_state);
        }

        for (var d = _state.Date.AddDays(1); d < today; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            QueueHistory(new WorkDayState { Date = d, UserOffline = true });
        }

        if (_pendingHistory.Count > 0)
        {
            FlushHistory(silent: true);
        }

        _state = new WorkDayState { Date = today };
        _nextReminder = DateTime.MinValue;
        _state.Save();
    }

    // Registers an automatic clock-out when 9 hours were already reached at the given moment.
    private bool AutoClockOutIfDue(DateTime when)
    {
        if (_state.ClockIn is not { } clockIn || _state.ClockOut is not null || when < clockIn + WorkDay
            || DateOnly.FromDateTime(when) != _state.Date)
        {
            return false;
        }

        _state.ClockOut = when;
        _state.ClockOutAuto = true;
        _state.Save();
        RecordHistory(_state);
        CloseOpenDialog();
        return true;
    }

    // The dialog belongs to the UI thread, and this can be called from a system-events thread.
    private void CloseOpenDialog()
    {
        var dialog = _openDialog;
        if (dialog is null)
        {
            return;
        }

        try
        {
            _ui.BeginInvoke(() =>
            {
                if (!dialog.IsDisposed)
                {
                    dialog.Abort();
                }
            });
        }
        catch (InvalidOperationException)
        {
            // UI is already shutting down.
        }
    }

    private void QueueHistory(WorkDayState day)
    {
        _pendingHistory.RemoveAll(p => p.Date == day.Date);
        _pendingHistory.Add(day.Clone());
    }

    private void RecordHistory(WorkDayState day)
    {
        QueueHistory(day);
        FlushHistory(silent: true);
    }

    // Writes pending days; stays pending while Excel has the file open and is retried on later ticks.
    private void FlushHistory(bool silent)
    {
        _lastHistoryAttempt = DateTime.Now;
        try
        {
            foreach (var day in _pendingHistory)
            {
                ClockInHistory.Upsert(day, WorkDay);
            }

            _pendingHistory.Clear();
            if (_notice == HistoryLockedNotice) _notice = null;
        }
        catch (IOException)
        {
            if (!silent) _notice = HistoryLockedNotice;
        }
    }

    private void UpdateDisplay(DateTime now)
    {
        var weekendOff = _state.ClockIn is null && IsWeekend(now);
        _canClockIn = !_dialogOpen && CanClockIn(now);
        _canClockOut = !_dialogOpen && CanClockOut;

        if (weekendOff)
        {
            SetDisplay("WE", Color.Gray, "Weekend: the clock is off", "WorkTimer: weekend, clock is off");
            return;
        }

        if (_state.ClockIn is not { } clockIn)
        {
            SetDisplay("--", Color.Gray, "Not clocked in", "WorkTimer: not clocked in");
            return;
        }

        if (_state.ClockOut is { } clockOut)
        {
            var worked = clockOut - clockIn;
            var how = _state.ClockOutUnknownIssue ? "unknown issue" : _state.ClockOutAuto ? "auto" : "manual";
            var summary = $"Clocked out at {clockOut:HH:mm} ({how})\nWorked {FormatDuration(worked)} (in at {clockIn:HH:mm})";
            SetDisplay("OUT", Color.SteelBlue, summary.Replace("\n", " | "), summary,
                $"{clockIn:HH:mm} - {clockOut:HH:mm} (clocked out)");
            return;
        }

        var elapsed = now - clockIn;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var remaining = WorkDay - elapsed;
        var done = remaining <= TimeSpan.Zero;
        var overtime = done ? -remaining : TimeSpan.Zero;
        if (done)
        {
            remaining = TimeSpan.Zero;
        }

        // Round up to the minute so the countdown reaches 0:00 exactly after nine hours.
        var iconRemaining = TimeSpan.FromMinutes(Math.Ceiling(remaining.TotalMinutes));
        var iconText = $"{(int)iconRemaining.TotalHours}:{iconRemaining.Minutes:00}";
        if (done)
        {
            iconText = $"{(int)overtime.TotalHours}:{overtime.Minutes:00}";
        }

        var color = done ? Color.Firebrick : remaining < TimeSpan.FromHours(1) ? Color.DarkOrange : Color.SeaGreen;

        var details = done
            ? $"Overtime {FormatDuration(overtime)}\nWorked {FormatDuration(elapsed)} (in at {clockIn:HH:mm})"
            : $"Remaining {FormatDuration(remaining)}\nWorked {FormatDuration(elapsed)} (in at {clockIn:HH:mm})\nClock out at {clockIn + WorkDay:HH:mm}";
        SetDisplay(iconText, color, details.Replace("\n", " | "), details,
            $"{clockIn:HH:mm} - {clockIn + WorkDay:HH:mm} ({iconText} {(done ? "overtime" : "left")})");
    }

    private void SetDisplay(string iconText, Color color, string menuText, string tooltip, string? widgetTitle = null)
    {
        var title = widgetTitle ?? $"WorkTimer - {menuText}";
        var times = _state.ClockIn is { } ci ? $"{ci:HH:mm} - {_state.ClockOut ?? ci + WorkDay:HH:mm}" : (IsWeekend(DateTime.Now) ? "Weekend" : "ClockIn Required");
        var remaining = iconText switch
        {
            "--" => "Not clocked in",
            "WE" => "Weekend",
            "OUT" => "Clocked out",
            _ => $"{iconText} {(color == Color.Firebrick ? "overtime" : "left")}"
        };
        _currentStatus = new WidgetStatus(title, times, remaining, tooltip, DateTime.UtcNow)
        {
            CanClockIn = _canClockIn,
            CanClockOut = _canClockOut,
            AutoStart = _autoStartEnabled,
            Notice = _notice
        };
        _widgetPublisher?.Publish(_currentStatus, WidgetStatus.FilePath);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            ClockOutOnLeaving();
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            ClockOutOnLeaving();
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => ClockOutOnLeaving();

    // Runs on a system-events thread: touch only state and files here, never UI.
    private void ClockOutOnLeaving()
    {
        lock (_sync)
        {
            AutoClockOutIfDue(DateTime.Now);
        }
    }

    private void OpenHistory()
    {
        lock (_sync)
        {
            FlushHistory(silent: true);
        }

        if (!ClockInHistory.Exists)
        {
            _notice = "No history yet.";
            return;
        }

        ClockInHistory.Open();
    }

    private static string FormatDuration(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

    private static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is not null || key?.GetValue("ClockInTray") is not null;
    }

    private static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled && Environment.ProcessPath is { } exe)
        {
            key.SetValue(RunValueName, $"\"{exe}\"");
            key.DeleteValue("ClockInTray", throwOnMissingValue: false);
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            key.DeleteValue("ClockInTray", throwOnMissingValue: false);
        }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.SessionEnding -= OnSessionEnding;
        _timer.Stop();
        _commands.Dispose();
        if (_currentStatus is not null)
            _widgetPublisher.Publish(_currentStatus with
            { IsAvailable = false, CanClockIn = false, CanClockOut = false, Remaining = "Tracker stopped", UpdatedUtc = DateTime.UtcNow }, WidgetStatus.FilePath);
        base.ExitThreadCore();
    }
}
