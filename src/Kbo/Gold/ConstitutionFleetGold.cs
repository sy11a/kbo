namespace Kbo.Gold;

internal sealed record ConstitutionFleetGold(int CurrentVersion, IReadOnlyList<FleetRepoTile> Repos, int Behind);
