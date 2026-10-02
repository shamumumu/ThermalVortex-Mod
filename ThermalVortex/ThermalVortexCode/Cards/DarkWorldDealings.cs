using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class DarkWorldDealings : MainDeckCard
{
    public override string CustomPortraitPath => "dark_world_dealings.png".BigCardImagePath();
    public override string PortraitPath => "dark_world_dealings.png".CardImagePath();
    public override string BetaPortraitPath => "dark_world_dealings.png".CardImagePath();

    public DarkWorldDealings() : base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithCards(1, 1);
        WithUpgradeVar("Discard", 1, 2);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var amount = DynamicVars.Cards.IntValue;
        await CardPileCmd.Draw(ctx, amount, Owner, false);
        var candidates = CardPile.GetCards(Owner, PileType.Hand)
            .Where(card => card != this)
            .ToList();
        if (candidates.Count == 0)
            return;

        var selected = await CardSelectionHelper.ChooseMany(
            ctx,
            Owner,
            candidates,
            "THERMALVORTEX-DARK_WORLD_DEALINGS.selectionPrompt",
            Math.Min(amount, candidates.Count),
            Math.Min(amount, candidates.Count),
            false);
        if (selected.Count > 0)
            await CardCmd.Discard(ctx, selected);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
