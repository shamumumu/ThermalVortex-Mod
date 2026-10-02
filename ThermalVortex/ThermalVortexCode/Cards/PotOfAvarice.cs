using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class PotOfAvarice : MainDeckCard
{
    public override string CustomPortraitPath => "pot_of_avarice.png".BigCardImagePath();
    public override string PortraitPath => "pot_of_avarice.png".CardImagePath();
    public override string BetaPortraitPath => "pot_of_avarice.png".CardImagePath();

    public PotOfAvarice() : base(3, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCards(1, 0);
        WithUpgradeVar("ReturnCards", 2, 3);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || CardSelectionHelper.ExhaustPileMonsters(Owner).Count >= RequiredReturnCards);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var isCombatValid = MonsterFieldService.CaptureMaterialUseValidity(Owner);
        if (!isCombatValid())
            return;

        var requiredReturnCards = RequiredReturnCards;
        var selected = await CardSelectionHelper.ChooseMany(
            ctx,
            Owner,
            CardSelectionHelper.ExhaustPileMonsters(Owner),
            "THERMALVORTEX-POT_OF_AVARICE.selectionPrompt",
            requiredReturnCards,
            requiredReturnCards,
            false,
            sourceCard: this);
        if (!isCombatValid()
            || selected.Count != requiredReturnCards
            || selected.Any(card => !CardSelectionHelper.IsCurrentPileCard(Owner, card, PileType.Exhaust)))
            return;

        foreach (var card in selected)
        {
            if (!isCombatValid()
                || !CardSelectionHelper.IsCurrentPileCard(Owner, card, PileType.Exhaust)
                || !await CardSelectionHelper.ReturnToDeck(card, CardPilePosition.Random, this))
            {
                return;
            }
        }

        if (isCombatValid())
            await CardPileCmd.Draw(ctx, 1, Owner, false);
    }

    private int RequiredReturnCards => UpgradeVarValue("ReturnCards");

    protected override void OnUpgrade() => ConstructedUpgrade();
}
