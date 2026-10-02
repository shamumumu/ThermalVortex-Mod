using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class Terraforming : MainDeckCard
{
    public override string CustomPortraitPath => "terraforming.png".BigCardImagePath();
    public override string PortraitPath => "terraforming.png".CardImagePath();
    public override string BetaPortraitPath => "terraforming.png".CardImagePath();

    public Terraforming() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || PowerCardsInDrawPile().Any());

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var owner = Owner;
        var isCombatValid = MonsterFieldService.CaptureMaterialUseValidity(owner);
        if (!isCombatValid())
            return;

        var selected = await CardSelectionHelper.ChooseOne(
            ctx,
            owner,
            PowerCardsInDrawPile().ToList(),
            "THERMALVORTEX-TERRAFORMING.selectionPrompt",
            false);
        if (isCombatValid()
            && CardSelectionHelper.IsCurrentPileCard(owner, selected, PileType.Draw)
            && selected.Type == CardType.Power)
            await CardPileCmd.Add(selected, PileType.Hand, CardPilePosition.Top, this, false);
    }

    private IEnumerable<CardModel> PowerCardsInDrawPile() =>
        CardSelectionHelper.DrawPileCards(Owner).Where(card => card.Type == CardType.Power);

}
