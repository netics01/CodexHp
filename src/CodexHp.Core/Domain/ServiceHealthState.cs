namespace CodexHp.Core.Domain;

public enum ServiceHealthState
{
    Operational,
    Issue,
    Unknown,
}

public sealed record ServiceStatusComponentGroup(
    string Name,
    IReadOnlyList<string> Components);
