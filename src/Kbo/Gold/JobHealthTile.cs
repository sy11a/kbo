namespace Kbo.Gold;

internal sealed record JobHealthTile(
    string Machine,
    string Agent,
    string Job,
    DateTimeOffset LastCompleted,
    double DaysSilent,
    string Status);
