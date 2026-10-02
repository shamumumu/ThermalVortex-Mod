using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class MacroCosmosPower : ThermalVortexPower
{
    private static readonly PileType[] CombatPileTypes =
    [
        PileType.Hand,
        PileType.Draw,
        PileType.Discard,
        PileType.Exhaust,
        PileType.Play
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
        if (card?.Pile is not null
            && (CombatPileTypes.Contains(card.Pile.Type)
                || card.Pile.Type == MonsterFieldPile.FieldPileType))
            ApplyToOwnedCard(card);

        return Task.CompletedTask;
    }

    public override Task AfterCardEnteredCombat(CardModel card)
    {
        ApplyToOwnedCard(card);
        return Task.CompletedTask;
    }

    public override Task AfterCardGeneratedForCombat(CardModel card, Player creator)
    {
        ApplyToOwnedCard(card);
        return Task.CompletedTask;
    }

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        ApplyToOwnedCard(cardPlay.Card);
        return Task.CompletedTask;
    }

    private void ApplyToCombatPiles()
    {
        if (Owner?.Player is null)
            return;

        foreach (var pileType in CombatPileTypes)
        {
            foreach (var card in CardPile.GetCards(Owner.Player, pileType))
            {
                ApplyToOwnedCard(card);
            }
        }

        foreach (var card in MonsterFieldService.GetMonsters(Owner.Player))
            ApplyToOwnedCard(card);
    }

    private void ApplyToOwnedCard(CardModel card)
    {
        if (card?.Owner?.Creature != Owner || card.Keywords.Contains(CardKeyword.Exhaust))
            return;

        card.AddKeyword(CardKeyword.Exhaust);
    }
}
