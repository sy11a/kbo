namespace Kbo.Gold;

internal sealed record WriteReadSummary(long Written, long Reused, double LoopRate);
