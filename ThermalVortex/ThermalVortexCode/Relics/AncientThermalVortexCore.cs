using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Relics;

public class AncientThermalVortexCore : ThermalVortexCore
{
    public override RelicRarity Rarity => RelicRarity.Ancient;
    protected override string RelicIconFileName => "millennium_puzzle.png";
    protected override string RelicIconOutlineFileName => "millennium_puzzle_outline.png";

    public override async Task BeforeHandDrawLate(
        Player player,
        PlayerChoiceContext ctx,
        ICombatState combatState)
    {
        if (!ReferenceEquals(player, Owner))
            return;

        var fusionCards = CardPile.GetCards(player, PileType.Discard)
            .OfType<XyzSummon>()
            .Where(fusion => fusion.DeckVersion is XyzSummon)
            .ToList();
        if (fusionCards.Count == 0)
            return;

        Flash();
        for (var index = fusionCards.Count - 1; index >= 0; index--)
        {
            await CardPileCmd.Add(
                fusionCards[index],
                PileType.Draw,
                CardPilePosition.Top,
                this,
                false);
        }
    }

    public override RelicModel GetUpgradeReplacement() => null;
}
