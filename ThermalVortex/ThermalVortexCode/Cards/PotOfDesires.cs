using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class PotOfDesires : MainDeckCard
{
    private const int RequiredDrawPileCards = 10;
    private const int CardsToDraw = 2;

    public override string CustomPortraitPath => "pot_of_desires.png".BigCardImagePath();
    public override string PortraitPath => "pot_of_desires.png".CardImagePath();
    public override string BetaPortraitPath => "pot_of_desires.png".CardImagePath();

    public PotOfDesires() : base(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCards(CardsToDraw, 0);
        WithCostUpgradeBy(-1);
        WithExplanations(NativeKeywordExplanation(CardKeyword.Exhaust));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && CardSelectionHelper.HasCardsAvailableForDraw(Owner, RequiredDrawPileCards);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var owner = Owner;
        var isCombatValid = MonsterFieldService.CaptureMaterialUseValidity(owner);
        if (!isCombatValid())
            return;

        await CardSelectionHelper.RefillDrawPileFromDiscardIfNeeded(
            ctx,
            owner,
            RequiredDrawPileCards,
            this,
            isCombatValid);
        if (!isCombatValid())
            return;

        var topCards = CardSelectionHelper.DrawPileCards(owner)
            .Take(RequiredDrawPileCards)
            .ToList();
        if (topCards.Count < RequiredDrawPileCards)
            return;

        foreach (var card in topCards)
            await CardCmd.Exhaust(ctx, card, false, false);

        await CardPileCmd.Draw(ctx, CardsToDraw, owner, false);
    }
}
