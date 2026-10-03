namespace Kbo.Gold;

internal sealed record LastSeenTile(
    string Machine,
    string Agent,
    DateTimeOffset LastEvent,
    double DaysSilent,
    string Status);
