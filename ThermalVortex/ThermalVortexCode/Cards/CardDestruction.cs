using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CardDestruction : MainDeckCard
{
    public override string CustomPortraitPath => "card_destruction.png".BigCardImagePath();
    public override string PortraitPath => "card_destruction.png".CardImagePath();
    public override string BetaPortraitPath => "card_destruction.png".CardImagePath();

    public CardDestruction() : base(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithKeyword(CardKeyword.Innate, UpgradeType.Add);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var hand = CardPile.GetCards(Owner, PileType.Hand)
            .Where(card => card != this)
            .ToList();
        if (hand.Count == 0)
            return;

        var count = hand.Count;
        await CardCmd.Discard(ctx, hand);
        await CardPileCmd.Draw(ctx, count, Owner, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
