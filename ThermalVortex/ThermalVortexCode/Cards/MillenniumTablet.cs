using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MillenniumTablet : MainDeckCard, IMillenniumCard
{
    public override string CustomPortraitPath => "millennium_tablet.png".BigCardImagePath();
    public override string PortraitPath => "millennium_tablet.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_tablet.png".CardImagePath();

    public MillenniumTablet() : base(3, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-SEALED_MILLENNIUM_PIECE"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await MillenniumSeries.AddChosenSealedPieceToHand(ctx, Owner, this);
    }

}
