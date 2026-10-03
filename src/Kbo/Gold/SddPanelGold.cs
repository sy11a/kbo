namespace Kbo.Gold;

/// <summary>SDD-practice panel (ADR-0040): spec-before-code ordering,
/// writes by content kind (machine-managed excluded and disclosed —
/// no-silent-caps), SDD-skill rate (empty + <paramref name="SkillConfigured"/>
/// false when the registry has no sdd block).</summary>
internal sealed record SddPanelGold(
    IReadOnlyList<SddOrderingRow> Ordering,
    SddOrderingSummary OrderingSummary,
    IReadOnlyList<SddWritesRow> WritesByKind,
    long MachineManagedWrites,
    IReadOnlyList<SddSkillRateRow> SkillRate,
    bool SkillConfigured);
