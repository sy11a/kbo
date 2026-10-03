namespace Kbo.Gold;

/// <summary>
/// The tile's declared target: a value and which side of it is healthy.
/// </summary>
internal sealed record MirrorGoal(double Value, MirrorDirection Direction);
