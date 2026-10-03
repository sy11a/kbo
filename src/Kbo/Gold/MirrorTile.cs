namespace Kbo.Gold;

/// <summary>Practice mirror (2026-08-27 design session, calibrated in BL-033): the
/// first screen — six tiles, each judged against its own history corridor and a goal.
/// <paramref name="State"/> is the emoji, <paramref name="Status"/> the CSS class
/// (ok | sick | trend | acute | wait — "acute" is the only amber). <paramref name="Goal"/>
/// is the pre-rendered goal line; corridors and thresholds live here, never in the
/// renderer (zero computation, P2).</summary>
internal sealed record MirrorTile(
    string Label,
    string Value,
    string Trend,
    string Hint,
    string Status,
    string State = "",
    string? Goal = null,
    double? CorridorLow = null,
    double? CorridorHigh = null,
    double? Median = null,
    double? Mad = null,
    int HistoryWeeks = 0);
