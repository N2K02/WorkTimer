using System.Text.Json;

namespace WorkTimer;

internal sealed record WidgetStatus(string Title, string Times, string Remaining, string Details, DateTime UpdatedUtc)
{
    public bool IsAvailable { get; init; } = true;
    public bool CanClockIn { get; init; }
    public bool CanClockOut { get; init; }
    public bool AutoStart { get; init; }
    public string? Notice { get; init; }
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkTimer", "widget-status.json");

    public static WidgetStatus Read(string path, DateTime utcNow)
    {
        try
        {
            if (!File.Exists(path))
            {
                var legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClockInTray", "widget-status.json");
                if (File.Exists(legacyPath)) path = legacyPath;
            }

            var status = JsonSerializer.Deserialize<WidgetStatus>(File.ReadAllText(path));
            if (status is not null && !string.IsNullOrWhiteSpace(status.Times) && !string.IsNullOrWhiteSpace(status.Remaining)
                && status.Details is not null && status.Title is not null
                && utcNow - status.UpdatedUtc < TimeSpan.FromMinutes(3) && status.UpdatedUtc <= utcNow.AddMinutes(1))
            {
                return status;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // The tracker may be starting or the status file may be temporarily unavailable.
        }

        return new("WorkTimer", "WorkTimer", "Tracker unavailable",
            "Start WorkTimer from its normal shortcut to use the widget controls.", utcNow) { IsAvailable = false };
    }
}

internal sealed class WidgetStatusPublisher
{
    private WidgetStatus? _lastStatus;
    private DateTime _lastWrite;

    public void Publish(WidgetStatus status, string path)
    {
        var signature = status with { Details = "", UpdatedUtc = default };
        if (signature == _lastStatus && status.UpdatedUtc - _lastWrite < TimeSpan.FromSeconds(30)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(status));
            File.Move(path + ".tmp", path, overwrite: true);
            _lastStatus = signature;
            _lastWrite = status.UpdatedUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A widget status failure must not interrupt clock-in tracking; retry on the next tick.
        }
    }
}
