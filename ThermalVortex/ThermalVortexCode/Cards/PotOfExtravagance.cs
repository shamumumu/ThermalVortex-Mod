using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class PotOfExtravagance : MainDeckCard
{
    private const int RequiredExtraDeckCards = 3;
    private const int CardsToDraw = 3;

    public override string CustomPortraitPath => "pot_of_extravagance.png".BigCardImagePath();
    public override string PortraitPath => "pot_of_extravagance.png".CardImagePath();
    public override string BetaPortraitPath => "pot_of_extravagance.png".CardImagePath();

    public PotOfExtravagance() : base(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCards(CardsToDraw, 0);
        WithCostUpgradeBy(-1);
        WithExplanations(
            ExtraDeckExplanation(),
            NativeKeywordExplanation(CardKeyword.Exhaust));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && TryGetCore()?.RemainingExtraDeckCardCount >= RequiredExtraDeckCards;

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var core = TryGetCore();
        if (core is null)
            return;

        var consumed = await core.ConsumeRandomExtraDeckCards(ctx, RequiredExtraDeckCards);
        if (consumed == RequiredExtraDeckCards)
            await CardPileCmd.Draw(ctx, CardsToDraw, Owner, false);
    }

    private ThermalVortexCore TryGetCore()
    {
        try
        {
            return Owner?.GetRelic<ThermalVortexCore>();
        }
        catch
        {
            return null;
        }
    }
}
