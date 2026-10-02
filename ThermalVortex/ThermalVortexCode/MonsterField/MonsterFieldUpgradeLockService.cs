using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace ThermalVortex.ThermalVortexCode.MonsterField;

/// <summary>
/// Keeps a monster body's upgrade level stable from the start of its summon
/// until it physically leaves the monster field. Combat upgrade and downgrade
/// commands are accumulated as a target level and applied after removal.
/// </summary>
internal static class MonsterFieldUpgradeLockService
{
    private static readonly Dictionary<CardModel, UpgradeLockState> States =
        new(new ReferenceComparer<CardModel>());
    private static readonly HashSet<CardPile> AttachedPiles =
        new(new ReferenceComparer<CardPile>());
    private static readonly HashSet<CardModel> ReconcilingCards =
        new(new ReferenceComparer<CardModel>());

    internal static void ResetAll()
    {
        ResetCombat();
        AttachedPiles.Clear();
    }

    internal static void ResetCombat()
    {
        States.Clear();
        ReconcilingCards.Clear();
    }

    internal static void Attach(CardPile pile)
    {
        if (pile is null || !AttachedPiles.Add(pile))
            return;

        pile.CardAdded += MarkEnteredField;
        pile.CardRemoved += CompleteFieldRemoval;

        foreach (var card in pile.Cards)
            MarkEnteredField(card);
    }

    internal static void BeginAwaitingFieldEntry(CardModel card)
    {
        if (card is null)
            return;

        if (States.TryGetValue(card, out var existing))
        {
            if (MonsterFieldService.IsOnField(card))
                existing.Phase = UpgradeLockPhase.OnField;
            return;
        }

        States[card] = new UpgradeLockState(
            card.CurrentUpgradeLevel,
            card.CurrentUpgradeLevel,
            MonsterFieldService.IsOnField(card)
                ? UpgradeLockPhase.OnField
                : UpgradeLockPhase.AwaitingFieldEntry);
    }

    internal static void CopyForReplay(CardModel source, CardModel duplicate)
    {
        if (source is null
            || duplicate is null
            || !States.TryGetValue(source, out var sourceState))
        {
            return;
        }

        var maxUpgradeLevel = Math.Max(0, duplicate.MaxUpgradeLevel);
        States[duplicate] = new UpgradeLockState(
            duplicate.CurrentUpgradeLevel,
            Math.Clamp(sourceState.TargetLevel, 0, maxUpgradeLevel),
            UpgradeLockPhase.AwaitingFieldEntry);
    }

    internal static void CompleteAwaitingPileChange(CardModel card, PileType oldPileType)
    {
        if (card is null
            || oldPileType != PileType.Play
            || !States.TryGetValue(card, out var state)
            || state.Phase != UpgradeLockPhase.AwaitingFieldEntry)
        {
            return;
        }

        if (card.Pile?.Type == MonsterFieldPile.FieldPileType)
        {
            state.Phase = UpgradeLockPhase.OnField;
            return;
        }

        Complete(card, state);
    }

    internal static void CancelAwaitingFieldEntry(CardModel card)
    {
        if (card is not null
            && States.TryGetValue(card, out var state)
            && state.Phase == UpgradeLockPhase.AwaitingFieldEntry)
        {
            States.Remove(card);
        }
    }

    internal static bool TryDeferUpgrade(CardModel card)
    {
        if (!TryGetActiveState(card, out var state))
            return false;

        if (IsCombatEnding())
            return true;

        var maxUpgradeLevel = Math.Max(0, card.MaxUpgradeLevel);
        if (state.TargetLevel < maxUpgradeLevel)
            state.TargetLevel++;

        return true;
    }

    internal static bool TryDeferDowngrade(CardModel card)
    {
        if (!TryGetActiveState(card, out var state))
            return false;

        if (!IsCombatEnding())
            state.TargetLevel = 0;

        return true;
    }

    internal static bool TryGetProjectedUpgradeLevel(CardModel card, out int upgradeLevel)
    {
        upgradeLevel = 0;
        if (!TryGetActiveState(card, out var state))
            return false;

        upgradeLevel = state.TargetLevel;
        return true;
    }

    private static bool TryGetActiveState(CardModel card, out UpgradeLockState state)
    {
        state = null;
        if (card is null)
            return false;

        if (ReconcilingCards.Contains(card))
            return false;

        if (States.TryGetValue(card, out state))
            return true;

        if (!MonsterFieldService.IsOnField(card))
            return false;

        state = new UpgradeLockState(
            card.CurrentUpgradeLevel,
            card.CurrentUpgradeLevel,
            UpgradeLockPhase.OnField);
        States[card] = state;
        return true;
    }

    private static void MarkEnteredField(CardModel card)
    {
        if (card is null)
            return;

        if (States.TryGetValue(card, out var state))
        {
            state.Phase = UpgradeLockPhase.OnField;
            return;
        }

        States[card] = new UpgradeLockState(
            card.CurrentUpgradeLevel,
            card.CurrentUpgradeLevel,
            UpgradeLockPhase.OnField);
    }

    private static void CompleteFieldRemoval(CardModel card)
    {
        if (card is null
            || !States.TryGetValue(card, out var state)
            || state.Phase != UpgradeLockPhase.OnField)
        {
            return;
        }

        Complete(card, state);
    }

    private static void Complete(CardModel card, UpgradeLockState state)
    {
        // Remove the lock before using CardCmd so our own Harmony prefixes do
        // not defer the reconciliation a second time.
        States.Remove(card);
        if (IsCombatEnding())
            return;

        ReconcilingCards.Add(card);
        try
        {
            var targetLevel = Math.Clamp(state.TargetLevel, 0, Math.Max(0, card.MaxUpgradeLevel));
            if (card.CurrentUpgradeLevel > targetLevel)
                CardCmd.Downgrade(card);

            while (card.CurrentUpgradeLevel < targetLevel && card.IsUpgradable)
            {
                var previousLevel = card.CurrentUpgradeLevel;
                CardCmd.Upgrade(card, CardPreviewStyle.None);
                if (card.CurrentUpgradeLevel <= previousLevel)
                    break;
            }
        }
        finally
        {
            ReconcilingCards.Remove(card);
        }
    }

    private static bool IsCombatEnding()
    {
        try
        {
            return CombatManager.Instance?.IsEnding == true;
        }
        catch
        {
            return false;
        }
    }

    private enum UpgradeLockPhase
    {
        AwaitingFieldEntry,
        OnField
    }

    private sealed class UpgradeLockState(
        int lockedLevel,
        int targetLevel,
        UpgradeLockPhase phase)
    {
        internal int LockedLevel { get; } = lockedLevel;
        internal int TargetLevel { get; set; } = targetLevel;
        internal UpgradeLockPhase Phase { get; set; } = phase;
    }
}
