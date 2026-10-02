using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class FusionDestinyPower : ThermalVortexPower
{
    private static readonly PileType[] CombatPileTypes =
    [
        PileType.Hand,
        PileType.Draw,
        PileType.Discard,
        PileType.Play,
        PileType.Exhaust
    ];

    private Dictionary<CardModel, int> _appliedDiscounts = [];

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _appliedDiscounts = new Dictionary<CardModel, int>(_appliedDiscounts);
    }

    public override Task AfterApplied(Creature target, CardModel source)
    {
        ApplyToCombatPiles();
        return Task.CompletedTask;
    }

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        if (card?.Pile is not null && CombatPileTypes.Contains(card.Pile.Type))
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

    internal void RefreshDiscounts() => ApplyToCombatPiles();

    internal void CopyAppliedDiscount(CardModel source, CardModel clone)
    {
        if (source is null || clone is null
            || source.Owner?.Creature != Owner || clone.Owner?.Creature != Owner
            || !_appliedDiscounts.TryGetValue(source, out var appliedDiscount))
            return;

        // Native combat clones already inherit the local cost modifiers. Copy
        // their bookkeeping before any entry/generated hooks can run again.
        _appliedDiscounts[clone] = appliedDiscount;
    }

    private void ApplyToCombatPiles()
    {
        if (Owner?.Player is null)
            return;

        foreach (var pileType in CombatPileTypes)
        {
            foreach (var card in CardPile.GetCards(Owner.Player, pileType))
                ApplyToOwnedCard(card);
        }
    }

    private void ApplyToOwnedCard(CardModel card)
    {
        if (!IsDiscountTarget(card) || card.Owner?.Creature != Owner)
            return;

        var desiredDiscount = Math.Max(0, Amount);
        _appliedDiscounts.TryGetValue(card, out var appliedDiscount);
        var additionalDiscount = desiredDiscount - appliedDiscount;
        if (additionalDiscount <= 0)
            return;

        card.EnergyCost.AddThisCombat(-additionalDiscount, false);
        _appliedDiscounts[card] = desiredDiscount;
    }

    private static bool IsDiscountTarget(CardModel card)
    {
        if (card is null)
            return false;

        return ContainsFusion(card.Title)
            || ContainsFusion(card.GetType().Name);
    }

    private static bool ContainsFusion(string text) =>
        !string.IsNullOrEmpty(text)
        && (text.Contains("融合", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Fusion", StringComparison.OrdinalIgnoreCase));
}
