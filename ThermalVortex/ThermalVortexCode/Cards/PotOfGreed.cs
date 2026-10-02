using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class PotOfGreed : MainDeckCard
{
    public override string CustomPortraitPath => "pot_of_greed.png".BigCardImagePath();
    public override string PortraitPath => "pot_of_greed.png".CardImagePath();
    public override string BetaPortraitPath => "pot_of_greed.png".CardImagePath();

    public PotOfGreed() : base(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCards(2, 0);
        WithEnergy(1, 1);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CardPileCmd.Draw(ctx, 2, Owner, false);
        await PlayerCmd.GainEnergy(CurrentUpgradeLevel > 0 ? 2 : 1, Owner);
    }
}
