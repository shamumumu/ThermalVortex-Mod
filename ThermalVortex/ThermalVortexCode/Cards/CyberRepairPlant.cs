using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberRepairPlant : MainDeckCard
{
    public override string CustomPortraitPath => "cyber_repair_plant.png".BigCardImagePath();
    public override string PortraitPath => "cyber_repair_plant.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_repair_plant.png".CardImagePath();

    public CyberRepairPlant() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-CYBER_MONSTER"),
            CardPreviewExplanation<CyberLarva>());
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || CardSelectionHelper.DiscardPileCyberMonsters(Owner).Count > 0);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var owner = Owner;
        var isCombatValid = MonsterFieldService.CaptureMaterialUseValidity(owner);
        if (!isCombatValid())
            return;

        var cyberMonsters = CardSelectionHelper.DiscardPileCyberMonsters(owner);
        if (cyberMonsters.Count == 0)
            return;

        IReadOnlyList<CardModel> cardsToReturn = cyberMonsters;
        if (CurrentUpgradeLevel == 0)
        {
            var selected = await CardSelectionHelper.ChooseOne(
                ctx,
                owner,
                cyberMonsters,
                "THERMALVORTEX-CYBER_REPAIR_PLANT.selectionPrompt",
                false);
            cardsToReturn = selected is null ? [] : [selected];
        }

        var returnedCount = 0;
        foreach (var card in cardsToReturn)
        {
            if (!isCombatValid())
                return;

            if (!CardSelectionHelper.IsCurrentPileCard(owner, card, PileType.Discard)
                || !CyberSeries.IsCyberMonster(card))
            {
                continue;
            }

            if (await CardSelectionHelper.ReturnToDeck(card, CardPilePosition.Random, this))
                returnedCount++;
        }

        if (returnedCount > 0 && isCombatValid())
            await CyberSeries.GenerateCyberLarvaToDiscard(owner, this);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
