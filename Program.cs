namespace WorkTimer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var silent = args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase));

        bool restart;
        using (var mutex = new Mutex(true, @"Local\WorkTimer.SingleInstance", out var isFirst))
        {
            if (!isFirst)
            {
                if (!silent)
                {
                    // Existing instance running: wake it and show setup/status window!
                    try
                    {
                        TrackerCommands.SendAsync(TrackerCommand.ShowSetup).GetAwaiter().GetResult();
                    }
                    catch { }
                }
                return;
            }

            ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001 // dark mode is experimental in .NET 10
            Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
            var context = new TrayApplicationContext(showSetup: !silent);
            Application.Run(context);
            restart = context.RestartRequested;
        }

        // Start the new instance only after the single-instance mutex is released.
        if (restart && Environment.ProcessPath is { } exe)
        {
            System.Diagnostics.Process.Start(exe);
        }
    }
}
