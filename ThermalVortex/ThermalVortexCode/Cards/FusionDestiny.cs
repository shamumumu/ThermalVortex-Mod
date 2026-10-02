using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class FusionDestiny : MainDeckCard
{
    public override string CustomPortraitPath => "fusion_destiny.png".BigCardImagePath();
    public override string PortraitPath => "fusion_destiny.png".CardImagePath();
    public override string BetaPortraitPath => "fusion_destiny.png".CardImagePath();

    public FusionDestiny() : base(3, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-FUSION_CARD"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CommonActions.ApplySelf<FusionDestinyPower>(ctx, this, 1, false);
        Owner.Creature.GetPower<FusionDestinyPower>()?.RefreshDiscounts();
    }

}
