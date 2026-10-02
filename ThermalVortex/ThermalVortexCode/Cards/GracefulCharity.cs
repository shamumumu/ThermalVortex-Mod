using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class GracefulCharity : MainDeckCard
{
    public override string CustomPortraitPath => "graceful_charity.png".BigCardImagePath();
    public override string PortraitPath => "graceful_charity.png".CardImagePath();
    public override string BetaPortraitPath => "graceful_charity.png".CardImagePath();

    public GracefulCharity() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithCards(3, 1);
        WithUpgradeVar("Discard", 2, 3);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var draw = DynamicVars.Cards.IntValue;
        var discard = UpgradeVarValue("Discard");
        await CardPileCmd.Draw(ctx, draw, Owner, false);
        await DiscardChosenCards(ctx, discard);
    }

    private async Task DiscardChosenCards(PlayerChoiceContext ctx, int count)
    {
        var candidates = CardPile.GetCards(Owner, PileType.Hand)
            .Where(card => card != this)
            .ToList();
        if (candidates.Count == 0)
            return;

        var selected = await CardSelectionHelper.ChooseMany(
            ctx,
            Owner,
            candidates,
            "THERMALVORTEX-GRACEFUL_CHARITY.selectionPrompt",
            Math.Min(count, candidates.Count),
            Math.Min(count, candidates.Count),
            false);
        if (selected.Count > 0)
            await CardCmd.Discard(ctx, selected);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
