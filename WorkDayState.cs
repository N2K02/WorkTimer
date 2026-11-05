using System.Text.Json;

namespace WorkTimer;

internal sealed class WorkDayState
{
    private static readonly string FilePath = ResolveFilePath();

    private static string ResolveFilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WorkTimer");
        var legacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClockInTray");
        var modern = Path.Combine(dir, "state.json");
        var legacy = Path.Combine(legacyDir, "state.json");
        if (!File.Exists(modern) && File.Exists(legacy))
        {
            try
            {
                Directory.CreateDirectory(dir);
                File.Copy(legacy, modern, overwrite: true);
            }
            catch { return legacy; }
        }
        return modern;
    }

    public static bool Exists => File.Exists(FilePath);

    public DateOnly Date { get; set; }
    public DateTime? ClockIn { get; set; }
    public DateTime? ClockOut { get; set; }
    public bool ClockOutAuto { get; set; }
    public bool ClockOutUnknownIssue { get; set; }
    public bool UserOffline { get; set; }
    public bool Prompted { get; set; }
    public DateTime? LastSeen { get; set; }

    public static WorkDayState Fresh() => new() { Date = DateOnly.FromDateTime(DateTime.Now) };

    // Returns the saved day, which may be a previous day that still needs closing.
    public static WorkDayState Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<WorkDayState>(File.ReadAllText(FilePath)) is { } state)
            {
                return state;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable state: start the day fresh.
        }

        return Fresh();
    }

    public WorkDayState Clone() => (WorkDayState)MemberwiseClone();

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
