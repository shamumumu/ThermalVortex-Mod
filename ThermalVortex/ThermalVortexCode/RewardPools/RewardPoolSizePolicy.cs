namespace ThermalVortex.ThermalVortexCode.RewardPools;

/// <summary>Construction sizes include the five fixed starting main cards.</summary>
public static class RewardPoolSizePolicy
{
    public const int LegacyRulesVersion = 1;
    public const int CurrentRulesVersion = 2;
    public const int LegacyMainTotal = 60;
    public const int MinMainTotal = 70;
    public const int MaxMainTotal = 90;
    public const int MinSelectableMain = MinMainTotal - RewardPoolCatalog.RequiredMainCardCount;
    public const int MaxSelectableMain = MaxMainTotal - RewardPoolCatalog.RequiredMainCardCount;
    public const int MultiplayerMainTotal = 80;
    public const int MultiplayerSelectableMain = MultiplayerMainTotal - RewardPoolCatalog.RequiredMainCardCount;

    public static bool IsCurrentMainCountValid(int total, RewardPoolConstructionMethod method) => method switch
    {
        RewardPoolConstructionMethod.MultiplayerDraft => total == MultiplayerMainTotal,
        RewardPoolConstructionMethod.Free or RewardPoolConstructionMethod.Genesis or RewardPoolConstructionMethod.Draft
            => total is >= MinMainTotal and <= MaxMainTotal,
        _ => false
    };

    public static bool IsCurrentSelectionCountValid(int selected, RewardPoolConstructionMethod method) =>
        IsCurrentMainCountValid(selected + RewardPoolCatalog.RequiredMainCardCount, method);
}
