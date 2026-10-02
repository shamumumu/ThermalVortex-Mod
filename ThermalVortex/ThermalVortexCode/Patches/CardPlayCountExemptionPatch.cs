using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Patches;

/// <summary>
/// Marks a real CardPlay as exempt only from per-turn play-count limits. The
/// normal CardPlay pipeline, history, targets, upgrades, and play hooks remain
/// untouched.
/// </summary>
internal static class CardPlayCountExemption
{
    private static readonly object Sync = new();
    private static readonly ConditionalWeakTable<CardModel, ScopeDepth> ScopedCards = new();
    private static readonly ConditionalWeakTable<CardPlay, object> ExemptCardPlays = new();

    internal static IDisposable Enter(CardModel card, bool enabled)
    {
        if (!enabled || card is null)
            return EmptyScope.Instance;

        lock (Sync)
        {
            var state = ScopedCards.GetValue(card, static _ => new ScopeDepth());
            state.Depth++;
        }

        return new Scope(card);
    }

    internal static bool IsExempt(CardModel card)
    {
        if (card is XyzMonsterCard { DoesNotCountTowardCardPlayLimit: true })
            return true;

        if (card is null)
            return false;

        lock (Sync)
        {
            return ScopedCards.TryGetValue(card, out var state)
                && state.Depth > 0;
        }
    }

    internal static bool IsExempt(CardPlay cardPlay) =>
        cardPlay is not null
        && (ExemptCardPlays.TryGetValue(cardPlay, out _)
            || IsExempt(cardPlay.Card));

    internal static void Remember(CardPlay cardPlay)
    {
        if (cardPlay is null || !IsExempt(cardPlay.Card))
            return;

        ExemptCardPlays.GetValue(cardPlay, static _ => new object());
    }

    private static void Exit(CardModel card)
    {
        lock (Sync)
        {
            if (!ScopedCards.TryGetValue(card, out var state))
                return;

            state.Depth--;
            if (state.Depth <= 0)
                ScopedCards.Remove(card);
        }
    }

    private sealed class ScopeDepth
    {
        internal int Depth { get; set; }
    }

    private sealed class Scope(CardModel card) : IDisposable
    {
        private CardModel _card = card;

        public void Dispose()
        {
            var cardToRelease = Interlocked.Exchange(ref _card, null);
            if (cardToRelease is not null)
                Exit(cardToRelease);
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        internal static readonly EmptyScope Instance = new();

        public void Dispose()
        {
        }
    }
}

[HarmonyPatch]
internal static class CardPlayCountExemptionPatch
{
    [HarmonyPatch(
        typeof(Hook),
        nameof(Hook.BeforeCardPlayed),
        [typeof(ICombatState), typeof(CardPlay)])]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void RememberExemptCardPlay(CardPlay __1) =>
        CardPlayCountExemption.Remember(__1);

    [HarmonyPatch(
        typeof(SlothPower),
        nameof(SlothPower.ShouldPlay),
        [typeof(CardModel), typeof(AutoPlayType)])]
    [HarmonyPrefix]
    private static bool AllowExemptCardThroughSloth(CardModel __0, ref bool __result) =>
        AllowExemptCard(__0, ref __result);

    [HarmonyPatch(
        typeof(SlothPower),
        nameof(SlothPower.BeforeCardPlayed),
        [typeof(CardPlay)])]
    [HarmonyPrefix]
    private static bool SkipSlothCount(CardPlay __0, ref Task __result) =>
        SkipExemptCount(__0, ref __result);

    [HarmonyPatch(
        typeof(VelvetChoker),
        nameof(VelvetChoker.ShouldPlay),
        [typeof(CardModel), typeof(AutoPlayType)])]
    [HarmonyPrefix]
    private static bool AllowExemptCardThroughVelvetChoker(CardModel __0, ref bool __result) =>
        AllowExemptCard(__0, ref __result);

    [HarmonyPatch(
        typeof(VelvetChoker),
        nameof(VelvetChoker.AfterCardPlayed),
        [typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext), typeof(CardPlay)])]
    [HarmonyPrefix]
    private static bool SkipVelvetChokerCount(CardPlay __1, ref Task __result) =>
        SkipExemptCount(__1, ref __result);

    [HarmonyPatch(
        typeof(Normality),
        nameof(Normality.ShouldPlay),
        [typeof(CardModel), typeof(AutoPlayType)])]
    [HarmonyPrefix]
    private static bool AllowExemptCardThroughNormality(CardModel __0, ref bool __result) =>
        AllowExemptCard(__0, ref __result);

    [HarmonyPatch(typeof(Normality), "get_CardsPlayedThisTurn")]
    [HarmonyPostfix]
    private static void ExcludeRememberedPlaysFromNormality(
        Normality __instance,
        ref int __result)
    {
        if (__result <= 0
            || __instance?.CombatState is not { } combatState
            || CombatManager.Instance?.History is not { } history)
        {
            return;
        }

        var exemptCount = history.CardPlaysStarted.Count(entry =>
            entry.HappenedThisTurn(combatState)
            && entry.CardPlay?.Card?.Owner == __instance.Owner
            && CardPlayCountExemption.IsExempt(entry.CardPlay));
        __result = Math.Max(0, __result - exemptCount);
    }

    private static bool AllowExemptCard(CardModel card, ref bool result)
    {
        if (!CardPlayCountExemption.IsExempt(card))
            return true;

        result = true;
        return false;
    }

    private static bool SkipExemptCount(CardPlay cardPlay, ref Task result)
    {
        if (!CardPlayCountExemption.IsExempt(cardPlay))
            return true;

        result = Task.CompletedTask;
        return false;
    }
}
