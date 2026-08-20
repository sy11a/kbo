namespace Kbo.Registry;

/// <summary>
/// Optional SDD-panel wiring (ADR-0040): the skill names that count as
/// spec/plan-writing practice. Null means the skill-rate metric is not
/// configured — a public tool ships no default skill list (ADR-0031
/// pattern); the panel then states the absence instead of guessing.
/// </summary>
public sealed record SddConfig(IReadOnlyList<string> Skills);
