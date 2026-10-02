using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class CyberRevolutionSystemPower : ThermalVortexPower
{
    private static readonly PileType[] CombatPileTypes =
    [
        PileType.Hand,
        PileType.Draw,
        PileType.Discard,
        PileType.Play,
        PileType.Exhaust
    ];

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override Task AfterApplied(Creature target, CardModel source)
    {
        ApplyToCombatPiles();
        return Task.CompletedTask;
    }

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        if (card?.Pile is not null && CombatPileTypes.Contains(card.Pile.Type))
            ApplyToCard(card);

        return Task.CompletedTask;
    }

    public override Task AfterCardEnteredCombat(CardModel card)
    {
        ApplyToCard(card);
        return Task.CompletedTask;
    }

    public override Task AfterCardGeneratedForCombat(CardModel card, Player creator)
    {
        ApplyToCard(card);
        return Task.CompletedTask;
    }

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        ApplyToCard(cardPlay.Card);
        return Task.CompletedTask;
    }

    private void ApplyToCombatPiles()
    {
        if (Owner?.Player is null)
            return;

        foreach (var pileType in CombatPileTypes)
        {
            foreach (var card in CardPile.GetCards(Owner.Player, pileType))
                ApplyToCard(card);
        }
    }

    private void ApplyToCard(CardModel card)
    {
        if (card is not CyberLarva || card.Owner?.Creature != Owner)
            return;

        var cost = card.EnergyCost.GetResolved();
        if (cost > 0)
            card.EnergyCost.AddThisCombat(-cost, false);
    }
}
