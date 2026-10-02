namespace ThermalVortex.ThermalVortexCode.RewardPools;

/// <summary>
/// Identifies the character card pool that owns a legal constructed-pool card.
/// The numeric order is part of the stable catalog ordering contract.
/// </summary>
public enum RewardPoolCardOrigin
{
    ThermalVortex = 0,
    Ironclad = 1,
    Silent = 2,
    Defect = 3,
    Necrobinder = 4,
    Regent = 5
}
