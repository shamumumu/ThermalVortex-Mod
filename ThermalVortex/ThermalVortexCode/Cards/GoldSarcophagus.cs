using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class GoldSarcophagus : MainDeckCard
{
    private const int ReturnTurns = 2;
    public override string CustomPortraitPath => "gold_sarcophagus.png".BigCardImagePath();
    public override string PortraitPath => "gold_sarcophagus.png".CardImagePath();
    public override string BetaPortraitPath => "gold_sarcophagus.png".CardImagePath();

    public GoldSarcophagus() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.Remove);
        WithExplanations(NativeKeywordExplanation(CardKeyword.Exhaust));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || CardSelectionHelper.DrawPileCards(Owner).Any());

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var owner = Owner;
        var isCombatValid = MonsterFieldService.CaptureMaterialUseValidity(owner);
        if (!isCombatValid())
            return;

        var candidates = CardSelectionHelper.DrawPileCards(owner);
        var selected = await CardSelectionHelper.ChooseOne(
            ctx,
            owner,
            candidates,
            "THERMALVORTEX-GOLD_SARCOPHAGUS.selectionPrompt",
            false);
        if (!isCombatValid()
            || !CardSelectionHelper.IsCurrentPileCard(owner, selected, PileType.Draw))
            return;

        var history = CombatManager.Instance.History;
        var historyCount = history.Entries.Count();
        await CardCmd.Exhaust(ctx, selected, false, false);
        // Exhaust callbacks can move the original card again. Only this
        // exhaust event, not the card's final pile, authorizes its return.
        if (!isCombatValid()
            || !history.Entries.Skip(historyCount)
                .OfType<CardExhaustedEntry>()
                .Any(entry => ReferenceEquals(entry.Card, selected)))
        {
            return;
        }

        var returnPower = (GoldSarcophagusReturnPower)ModelDb.Power<GoldSarcophagusReturnPower>().ToMutable(0);
        // Bind before application so the independent icon has its card name
        // and countdown as soon as it is created.
        returnPower.Bind(selected, ReturnTurns);
        await PowerCmd.Apply(ctx, returnPower, owner.Creature, 1, owner.Creature, this, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
