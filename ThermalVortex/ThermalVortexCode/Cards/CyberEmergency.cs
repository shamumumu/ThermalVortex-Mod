using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberEmergency : MainDeckCard
{
    public override string CustomPortraitPath => "cyber_emergency.png".BigCardImagePath();
    public override string PortraitPath => "cyber_emergency.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_emergency.png".CardImagePath();

    public CyberEmergency() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(
            CardPreviewExplanation<CyberDragon>(),
            CardPreviewExplanation<CyberLarva>());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CyberSeries.GenerateCyberDragon(Owner, PileType.Hand, this, CardPilePosition.Top);
        await CyberSeries.GenerateCyberLarvaToDiscard(Owner, this);
    }

}
