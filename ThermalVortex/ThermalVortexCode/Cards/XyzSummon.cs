using MegaCrit.Sts2.Core.Entities.Cards;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Character;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Cards;

[Pool(typeof(ThermalVortexCardPool))]
public class XyzSummon : MainDeckCard
{
    public override string CustomPortraitPath => "xyz_summon.png".BigCardImagePath();
    public override string PortraitPath => "xyz_summon.png".CardImagePath();
    public override string BetaPortraitPath => "xyz_summon.png".CardImagePath();

    public XyzSummon() : base(0, CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
        WithExplanations(
            ExtraDeckExplanation(),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-MATERIAL"),
            KeywordExplanation("THERMALVORTEX-FUSION_MATERIAL"),
            KeywordExplanation("THERMALVORTEX-XYZ"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable
        && (CurrentUpgradeLevel > 0
            ? TryGetFurnace()?.CanUpgradedXyzSummon
            : TryGetFurnace()?.CanXyzSummon) == true;

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var furnace = Owner.GetRelic<ThermalVortexCore>();
        if (furnace is not null)
            await furnace.XyzSummon(ctx, CurrentUpgradeLevel > 0);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();

    private ThermalVortexCore TryGetFurnace()
    {
        try
        {
            return Owner?.GetRelic<ThermalVortexCore>();
        }
        catch
        {
            // Card library renders canonical cards before they have an owner.
            return null;
        }
    }
}
