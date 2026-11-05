using System.Reflection;
using System.Runtime.CompilerServices;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var assembly = Assembly.Load("WorkTimer");
        var contextType = assembly.GetType("WorkTimer.TrayApplicationContext", throwOnError: true)!;
        var dialogType = assembly.GetType("WorkTimer.TimeDialog", throwOnError: true)!;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var context = RuntimeHelpers.GetUninitializedObject(contextType);
        using var ui = new Control();
        _ = ui.Handle;
        contextType.GetField("_ui", flags)!.SetValue(context, ui);
        var openDialog = contextType.GetField("_openDialog", flags)!;
        var close = contextType.GetMethod("CloseOpenDialog", flags)!;

        Form NewDialog() => (Form)Activator.CreateInstance(dialogType,
            "Clock in", "Test", DateTime.Now, DateTime.Today, "Start timer", "Not now")!;

        using var morning = NewDialog();
        close.Invoke(context, null);
        openDialog.SetValue(context, morning);
        Application.DoEvents();
        Check(morning.DialogResult != DialogResult.Abort, "Rollover must not abort the new morning popup.");

        using var previous = NewDialog();
        using var replacement = NewDialog();
        openDialog.SetValue(context, previous);
        close.Invoke(context, null);
        openDialog.SetValue(context, replacement);
        Application.DoEvents();
        Check(previous.DialogResult == DialogResult.Abort, "The previous dialog must be aborted.");
        Check(replacement.DialogResult != DialogResult.Abort, "A queued close must not abort a replacement dialog.");

        openDialog.SetValue(context, previous);
        close.Invoke(context, null);
        previous.Dispose();
        Application.DoEvents();
        Console.WriteLine("PASS: morning popup, stale dialog, replacement dialog, and disposed dialog.");

        var stateType = assembly.GetType("WorkTimer.WorkDayState", throwOnError: true)!;
        var state = Activator.CreateInstance(stateType)!;
        contextType.GetField("_state", flags)!.SetValue(context, state);
        var updateDisplay = contextType.GetMethod("UpdateDisplay", flags)!;
        var weekday = new DateTime(2026, 11, 5, 8, 0, 0);
        updateDisplay.Invoke(context, [weekday]);
        var display = contextType.GetField("_currentStatus", flags)!;
        WorkTimer.WidgetStatus Status() => (WorkTimer.WidgetStatus)display.GetValue(context)!;
        Check(!Status().CanClockOut && Status().CanClockIn, "Clock-out must be disabled before clock-in.");
        updateDisplay.Invoke(context, [weekday.AddDays(2)]);
        Check(!Status().CanClockIn && !Status().CanClockOut, "Clock actions must be disabled on weekends before clock-in.");
        stateType.GetProperty("ClockIn")!.SetValue(state, weekday);
        updateDisplay.Invoke(context, [weekday.AddHours(1)]);
        Check(Status().CanClockOut && Status().CanClockIn, "Clock-out and clock-in editing must be enabled after clock-in.");
        contextType.GetField("_dialogOpen", flags)!.SetValue(context, true);
        updateDisplay.Invoke(context, [weekday]);
        Check(!Status().CanClockIn && !Status().CanClockOut, "A pending time dialog must disable duplicate actions.");
        contextType.GetField("_dialogOpen", flags)!.SetValue(context, false);
        Console.WriteLine("PASS: widget action availability before/after clock-in, weekends, and pending dialogs.");

        var snapshotType = assembly.GetType("WorkTimer.WidgetStatus", throwOnError: true)!;
        var publisherType = assembly.GetType("WorkTimer.WidgetStatusPublisher", throwOnError: true)!;
        var publisher = Activator.CreateInstance(publisherType)!;
        var snapshotDirectory = Path.Combine(Path.GetTempPath(), "WorkTimer-tests-" + Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(snapshotDirectory, "widget-status.json");
        var publish = publisherType.GetMethod("Publish")!;
        var read = snapshotType.GetMethod("Read")!;
        var utcNow = DateTime.UtcNow;
        object Snapshot(string title, DateTime timestamp) => Activator.CreateInstance(snapshotType,
            [title, "07:42 - 16:42", "4:18 left", "Remaining 4:18", timestamp])!;
        object ReadSnapshot(DateTime now) => read.Invoke(null, [snapshotPath, now])!;
        string Remaining(object snapshot) => (string)snapshotType.GetProperty("Remaining")!.GetValue(snapshot)!;
        try
        {
            Check(Remaining(ReadSnapshot(utcNow)) == "Tracker unavailable", "A missing snapshot must not invent a countdown.");
            publish.Invoke(publisher, [Snapshot("07:42 - 16:42 (4:18 left)", utcNow), snapshotPath]);
            Check(Remaining(ReadSnapshot(utcNow)) == "4:18 left", "The widget must read the tracker's published countdown.");
            var original = File.ReadAllText(snapshotPath);
            publish.Invoke(publisher, [Snapshot("07:42 - 16:42 (4:18 left)", utcNow.AddSeconds(10)), snapshotPath]);
            Check(File.ReadAllText(snapshotPath) == original, "Unchanged display must not rewrite the snapshot every tick.");
            publish.Invoke(publisher, [Snapshot("07:42 - 16:42 (4:18 left)", utcNow.AddSeconds(30)), snapshotPath]);
            Check(File.ReadAllText(snapshotPath) != original, "A stable countdown must still publish a heartbeat.");
            publish.Invoke(publisher, [Snapshot("Changed title", utcNow.AddSeconds(31)), snapshotPath]);
            Check(File.ReadAllText(snapshotPath).Contains("Changed title"), "State changes must publish immediately.");
            Check(Remaining(ReadSnapshot(utcNow.AddMinutes(4))) == "Tracker unavailable", "A stopped tracker must not leave a live-looking countdown.");
            Check(Remaining(ReadSnapshot(utcNow.AddMinutes(-5))) == "Tracker unavailable", "A snapshot from the future must not be trusted.");
            File.WriteAllText(snapshotPath, "invalid json");
            Check(Remaining(ReadSnapshot(utcNow)) == "Tracker unavailable", "Corrupt status must be handled without crashing.");
            File.WriteAllText(snapshotPath, "{}");
            Check(Remaining(ReadSnapshot(utcNow)) == "Tracker unavailable", "An incomplete snapshot must not produce blank widget content.");
            using var locked = new FileStream(snapshotPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            publish.Invoke(publisher, [Snapshot("Locked status", utcNow.AddMinutes(1)), snapshotPath]);
            Check(Remaining(ReadSnapshot(utcNow)) == "Tracker unavailable", "A locked status file must not crash the widget.");
        }
        finally
        {
            File.Delete(snapshotPath);
            File.Delete(snapshotPath + ".tmp");
            if (Directory.Exists(snapshotDirectory)) Directory.Delete(snapshotDirectory);
        }
        Console.WriteLine("PASS: widget status exchange, heartbeat, stale data, corrupt data, and locked files.");
        CommandTests.Run();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
