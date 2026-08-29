namespace CodexHp.App.Application;

public sealed record PollSchedule(
    TimeSpan Usage,
    TimeSpan TokenActivity,
    TimeSpan ServiceStatusProbe,
    TimeSpan Visibility,
    TimeSpan RefreshGauge)
{
    public static PollSchedule Default { get; } = new(
        Usage: TimeSpan.FromSeconds(15),
        TokenActivity: TimeSpan.FromSeconds(15),
        ServiceStatusProbe: TimeSpan.FromMinutes(1),
        Visibility: TimeSpan.FromSeconds(1),
        RefreshGauge: TimeSpan.FromSeconds(1));
}
