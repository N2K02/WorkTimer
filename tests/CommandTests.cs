using System.Collections.Concurrent;
using System.IO.Pipes;
using WorkTimer;

internal static class CommandTests
{
    public static void Run()
    {
        Check(TrackerCommands.IsLocalTrackerPath(@"C:\Apps\WorkTimer.exe"), "A local tracker path must be accepted.");
        Check(!TrackerCommands.IsLocalTrackerPath(@"\\server\share\WorkTimer.exe")
            && !TrackerCommands.IsLocalTrackerPath("WorkTimer.exe")
            && !TrackerCommands.IsLocalTrackerPath(@"C:\Apps\Other.exe"), "Tracker launch must reject remote, relative, and unrelated executables.");
        var pipeName = "WorkTimer-tests-" + Guid.NewGuid().ToString("N");
        var received = new ConcurrentQueue<TrackerCommand>();
        using var signal = new AutoResetEvent(false);
        using var server = new TrackerCommandServer(command => { received.Enqueue(command); signal.Set(); }, pipeName);
        foreach (var command in Enum.GetValues<TrackerCommand>())
        {
            TrackerCommands.SendAsync(command, pipeName: pipeName).GetAwaiter().GetResult();
            Check(signal.WaitOne(TimeSpan.FromSeconds(2)) && received.TryDequeue(out var actual) && actual == command,
                "A widget command must arrive unchanged at the tracker.");
        }

        using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly))
        {
            client.Connect(2000);
            client.WriteByte(255);
            Check(client.ReadByte() == 0, "An unknown command must be rejected.");
        }
        Check(received.IsEmpty, "Invalid input must never dispatch a tracker action.");
        TrackerCommands.SendAsync(TrackerCommand.OpenHistory, pipeName: pipeName).GetAwaiter().GetResult();
        Check(signal.WaitOne(TimeSpan.FromSeconds(2)), "The command server must recover after invalid input.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        try
        {
            TrackerCommands.SendAsync(TrackerCommand.ClockIn, timeout.Token, pipeName + "-missing").GetAwaiter().GetResult();
            throw new InvalidOperationException("An offline tracker must not report a successful command.");
        }
        catch (OperationCanceledException) { }
        server.Dispose();
        Check(server.Completion.Wait(TimeSpan.FromSeconds(2)), "Shutdown must cancel the pipe listener promptly.");
        Console.WriteLine("PASS: widget commands, invalid input rejection, offline timeout, and listener shutdown.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
