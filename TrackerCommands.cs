using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Win32;

namespace WorkTimer;

internal enum TrackerCommand : byte
{
    ClockIn = 1, ClockOut, OpenHistory, EnableAutoStart, DisableAutoStart, Restart, Exit
}

internal static class TrackerCommands
{
    private const string SettingsKey = @"Software\WorkTimer";
    private const string LegacySettingsKey = @"Software\ClockInTray";

    public static void RegisterExecutable(string path)
    {
        if (!IsLocalTrackerPath(path)) return;
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key.SetValue("ExecutablePath", path);
    }

    internal static bool IsLocalTrackerPath(string path) => Path.IsPathFullyQualified(path)
        && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && (string.Equals(Path.GetFileName(path), "WorkTimer.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(path), "ClockInTray.exe", StringComparison.OrdinalIgnoreCase));

    public static void StartTracker()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey) ?? Registry.CurrentUser.OpenSubKey(LegacySettingsKey);
        if (key?.GetValue("ExecutablePath") is not string path || !IsLocalTrackerPath(path) || !File.Exists(path))
            throw new IOException("Start WorkTimer once from its installed location to reconnect the widget.");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! });
    }

    public static string PipeName
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return $"WorkTimer.{identity.User!.Value}.{Process.GetCurrentProcess().SessionId}";
        }
    }

    public static async Task SendAsync(TrackerCommand command, CancellationToken cancellationToken = default, string? pipeName = null)
    {
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        await pipe.WriteAsync(new byte[] { (byte)command }, timeout.Token);
        var response = new byte[1];
        if (await pipe.ReadAsync(response, timeout.Token) != 1 || response[0] != 1)
            throw new IOException("The tracker did not accept this action.");
    }
}

internal sealed class TrackerCommandServer : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly Action<TrackerCommand> _dispatch;
    private readonly string _pipeName;
    private int _disposed;
    public Task Completion { get; }

    public TrackerCommandServer(Action<TrackerCommand> dispatch, string? pipeName = null)
    {
        _dispatch = dispatch;
        _pipeName = pipeName ?? TrackerCommands.PipeName;
        Completion = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        var stop = _stopping.Token;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stop);
                    using var request = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    request.CancelAfter(TimeSpan.FromSeconds(5));
                    var message = new byte[1];
                    if (await pipe.ReadAsync(message, request.Token) != 1) continue;
                    var command = (TrackerCommand)message[0];
                    var valid = Enum.IsDefined(command);
                    await pipe.WriteAsync(new byte[] { valid ? (byte)1 : (byte)0 }, request.Token);
                    if (valid) _dispatch(command);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (!stop.IsCancellationRequested) await Task.Delay(250, stop);
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { _stopping.Dispose(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _stopping.Cancel();
    }
}
