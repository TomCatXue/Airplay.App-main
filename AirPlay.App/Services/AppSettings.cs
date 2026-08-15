namespace AirPlay.App.Services;

public class AppSettings
{
    public string ServiceName { get; set; } = "AirPlay Windows App";

    public ushort AirTunesPort { get; set; } = 5000;

    public ushort AirPlayPort { get; set; } = 7100;

    public string? NetworkAdapterId { get; set; }

    public bool StartWithWindows { get; set; }
}
