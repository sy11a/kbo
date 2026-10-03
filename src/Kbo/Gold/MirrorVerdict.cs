namespace Kbo.Gold;

/// <summary>Calibration verdict for one mirror tile (BL-033). State is the emoji the
/// renderer prints; StatusClass is the CSS class. Amber ("acute") is the only alarm.</summary>
internal sealed record MirrorVerdict(
    string State,
    string StatusClass,
    double? CorridorLow,
    double? CorridorHigh,
    double? Median,
    double? Mad,
    double? SlopePerWeek,
    double? RobustZ,
    int HistoryWeeks);
