namespace Arienlabs.IpLocationClient;

/// <summary>Gotowość baz i stan automatycznej aktualizacji.</summary>
public sealed record IpLocationHealth
{
    public required bool Ready { get; init; }
    public bool UpdateEnabled { get; init; }
    public string? LastError { get; init; }
    public string? Generation { get; init; }
    public DateTimeOffset? CityBuild { get; init; }
    public DateTimeOffset? AsnBuild { get; init; }
    public DateTimeOffset? LastAttempt { get; init; }
    public DateTimeOffset? LastSuccessfulCheck { get; init; }
}
