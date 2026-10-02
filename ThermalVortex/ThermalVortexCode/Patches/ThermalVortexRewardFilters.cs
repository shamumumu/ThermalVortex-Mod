using System.Diagnostics;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortexCardPool = ThermalVortex.ThermalVortexCode.Character.ThermalVortexCardPool;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal static class ThermalVortexRewardFilters
{
    internal const string MainDeckCardRewardDescriptionKey = "THERMALVORTEX-MAIN_DECK_CARD_REWARD.description";
    internal const string ExtraDeckCardRewardDescriptionKey = "THERMALVORTEX-EXTRA_DECK_CARD_REWARD.description";

    internal static bool IsGeneratedOnlyDirectRewardExcluded(CardModel card) =>
        card is not null && ThermalVortexGeneratedCards.IsRewardExcluded(card);

    internal static bool IsAlwaysDirectRewardExcluded(CardModel card) =>
        IsGeneratedOnlyDirectRewardExcluded(card)
        || IsCard<XyzSummon>(card)
        || RewardPoolCatalog.IsRequiredMainCard(card)
        || RewardPoolCatalog.IsRequiredExtraCard(card);

    internal static bool IsExtraDeckRewardCard(CardModel card) =>
        card is XyzMonsterCard || IsKnownExtraDeckDisplayCard(card);

    internal static bool ShouldExcludeExtraDeckForReward(
        CardModel card,
        Player player,
        CardCreationOptions options)
    {
        return ShouldExcludeExtraDeckForReward(card, player, options?.Source ?? CardCreationSource.Other);
    }

    internal static bool ShouldExcludeExtraDeckForReward(
        CardModel card,
        Player player,
        CardCreationSource source)
    {
        if (!IsExtraDeckRewardCard(card))
            return false;

        if (source == CardCreationSource.Encounter)
            return true;

        return IsExtraDeckFull(player);
    }

    internal static bool IsAllowedDirectReward(
        CardModel card,
        Player player,
        CardCreationOptions options)
    {
        if (card is null)
            return false;

        var policy = RewardGrantPolicies.ForCreationOptions(player, options);
        if (RewardGrantPolicies.IsPassthrough(policy))
        {
            return true;
        }

        if (policy == RewardGrantPolicy.StandardLegacy
            && RewardPoolCatalog.IsRequiredExtraCard(card))
        {
            return false;
        }

        if (IsAlwaysDirectRewardExcluded(card))
            return false;

        if (ShouldExcludeExtraDeckForReward(card, player, options))
            return false;

        return IsAllowedByConstructedPool(card, player);
    }

    internal static IEnumerable<CardModel> FilterDirectRewardCandidates(
        IEnumerable<CardModel> cards,
        Player player,
        CardCreationOptions options)
    {
        if (RewardGrantPolicies.IsPassthrough(
                RewardGrantPolicies.ForCreationOptions(player, options)))
        {
            return cards ?? [];
        }

        return FilterDirectRewardCandidates(cards, player, options?.Source ?? CardCreationSource.Other);
    }

    internal static IEnumerable<CardModel> FilterDirectRewardCandidates(
        IEnumerable<CardModel> cards,
        Player player,
        CardCreationSource source)
    {
        if (cards is null)
            return [];

        var policy = RewardGrantPolicies.ForGenericReward(player);
        if (RewardGrantPolicies.IsPassthrough(policy))
            return cards;

        if (RewardGrantPolicies.RequiresSelectedPool(policy))
        {
            return RewardPoolCandidateResolver.FilterDirectCandidates(
                cards,
                player,
                card => IsAllowedDirectReward(card, player, source)).Candidates;
        }

        return cards
            .Where(card => IsAllowedDirectReward(card, player, source))
            .ToList();
    }

    internal static IEnumerable<CardModel> FilterOrExpandPossibleCards(
        IEnumerable<CardModel> cards,
        Player player,
        CardCreationOptions options)
    {
        var policy = RewardGrantPolicies.ForCreationOptions(player, options);
        if (policy == RewardGrantPolicy.StandardLegacy)
            return FilterDirectRewardCandidates(cards, player, options);

        if (RewardGrantPolicies.IsPassthrough(policy))
            return cards ?? [];

        return RewardPoolCandidateResolver.ResolveCreationOptions(
            cards,
            player,
            options,
            card => IsAllowedDirectReward(card, player, options)).Candidates;
    }

    internal static IEnumerable<CardModel> GetStableSelectedMainRewardCards(Player player)
        => RewardPoolCandidateResolver.GetStableSelectedMainRewardCards(player);

    internal static IEnumerable<CardModel> GetStableSelectedExtraRewardCards(Player player)
        => RewardPoolCandidateResolver.GetStableSelectedExtraRewardCards(player);

    internal static bool IsMainRewardCardForCurrentMode(CardModel card, Player player)
    {
        if (card is null)
            return false;

        if (player?.GetRelic<ThermalVortexCore>()?.IsConstructedRewardPoolEnabled == true)
        {
            return RewardPoolCandidateResolver.IsCatalogMain(card, player)
                || RewardPoolCandidateResolver.IsOrdinaryColorlessRewardCandidate(card);
        }

        // This is the pre-construction-mode type check used by the compatibility
        // fallbacks. Keeping it here avoids narrowing standard mode to the new
        // catalog and therefore preserves the legacy candidate set.
        return card is MainDeckCard && card is not ExtraDeckCard;
    }

    internal static bool MatchesMultiplayerConstraint(
        CardModel card,
        CardMultiplayerConstraint requestedConstraint) =>
        RewardPoolCandidateResolver.MatchesMultiplayerConstraint(card, requestedConstraint);

    internal static bool IsAllowedDirectReward(
        CardModel card,
        Player player,
        CardCreationSource source)
    {
        if (card is null)
            return false;

        var policy = RewardGrantPolicies.ForGenericReward(player);
        if (RewardGrantPolicies.IsPassthrough(policy))
        {
            return true;
        }

        if (policy == RewardGrantPolicy.StandardLegacy
            && RewardPoolCatalog.IsRequiredExtraCard(card))
        {
            return false;
        }

        if (IsAlwaysDirectRewardExcluded(card))
            return false;

        if (ShouldExcludeExtraDeckForReward(card, player, source))
            return false;

        return IsAllowedByConstructedPool(card, player);
    }

    internal static IEnumerable<CardModel> FilterNeowDirectRewardPool(
        IEnumerable<CardModel> cards,
        Player player)
    {
        if (cards is null)
            return [];

        return cards
            .Where(card => !IsAlwaysDirectRewardExcluded(card))
            .Where(card => !IsExtraDeckFull(player) || !IsExtraDeckRewardCard(card))
            .ToList();
    }

    internal static string GetCardRewardDescriptionKey(CardReward reward)
    {
        if (reward?.Player?.Character is not ThermalVortexCharacter)
            return null;

        var cards = GetRewardCards(reward);
        if (cards.Count == 0)
            return null;

        if (cards.All(IsKnownExtraDeckDisplayCard))
            return ExtraDeckCardRewardDescriptionKey;

        if (cards.All(card => !IsKnownExtraDeckDisplayCard(card)))
            return MainDeckCardRewardDescriptionKey;

        return null;
    }

    private static bool IsExtraDeckFull(Player player) =>
        player?.GetRelic<ThermalVortexCore>()?.CanAcceptExtraDeckReward == false;

    private static bool IsAllowedByConstructedPool(CardModel card, Player player)
    {
        var core = player?.GetRelic<ThermalVortexCore>();
        if (core?.IsConstructedRewardPoolEnabled != true)
            return true;

        if (RewardPoolCandidateResolver.IsOrdinaryColorlessRewardCandidate(card))
            return true;

        return IsExtraDeckRewardCard(card)
            ? RewardPoolCandidateResolver.IsSelectedExtraRewardCandidate(card, core)
            : RewardPoolCandidateResolver.IsCatalogMain(card, player)
              && core.IsMainDeckRewardAllowed(card);
    }

    private static IReadOnlyList<CardModel> GetRewardCards(CardReward reward)
    {
        try
        {
            return reward.Cards?.ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static bool IsKnownExtraDeckDisplayCard(CardModel card)
    {
        try
        {
            return ThermalVortexCore.IsExtraDeckDisplayCard(card);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCard<T>(CardModel card)
        where T : CardModel
    {
        if (card is T)
            return true;

        try
        {
            return card?.Id?.Entry == ModelDb.Card<T>().Id.Entry;
        }
        catch
        {
            return false;
        }
    }
}

[HarmonyPatch(typeof(CardReward), "get_Description")]
internal static class CardRewardThermalVortexDescriptionPatch
{
    [HarmonyPostfix]
    private static void LabelThermalVortexCardReward(CardReward __instance, ref LocString __result)
    {
        var key = ThermalVortexRewardFilters.GetCardRewardDescriptionKey(__instance);
        if (key is not null)
            __result = new LocString("cards", key);
    }
}

[HarmonyPatch(
    typeof(CardCreationOptions),
    nameof(CardCreationOptions.GetPossibleCards),
    [typeof(Player)])]
internal static class CardCreationOptionsThermalVortexRewardFilterPatch
{
    [HarmonyPostfix]
    private static void FilterThermalVortexRewardCandidates(
        Player player,
        CardCreationOptions __instance,
        ref IEnumerable<CardModel> __result)
    {
        if (player?.Character is not ThermalVortexCharacter)
            return;

        __result = ThermalVortexRewardFilters.FilterOrExpandPossibleCards(__result, player, __instance);
    }
}

[HarmonyPatch(
    typeof(CardPoolModel),
    nameof(CardPoolModel.GetUnlockedCards),
    [typeof(MegaCrit.Sts2.Core.Unlocks.UnlockState), typeof(CardMultiplayerConstraint)])]
internal static class ThermalVortexNeowDirectCardPoolFilterPatch
{
    [HarmonyPostfix]
    private static void FilterNeowDirectRewardPool(
        CardPoolModel __instance,
        CardMultiplayerConstraint __1,
        ref IEnumerable<CardModel> __result)
    {
        var player = ThermalVortexNeowRewardFilterScope.CurrentPlayer;
        var isThermalVortexPlayer = player?.Character is ThermalVortexCharacter;
        var isThermalVortexPool = __instance is ThermalVortexCardPool;

        if (!isThermalVortexPlayer && (!isThermalVortexPool || !ThermalVortexNeowRewardFilterScope.IsNeowRewardStack()))
            return;

        var effectivePlayer = player ?? ThermalVortexNeowRewardFilterScope.TryResolveConstructedPoolPlayer();
        if (RewardGrantPolicies.ForGenericReward(effectivePlayer)
            == RewardGrantPolicy.StandardLegacy)
        {
            // Preserve the exact pre-construction-mode Neow filtering path.
            __result = ThermalVortexRewardFilters.FilterNeowDirectRewardPool(__result, player);
            return;
        }

        __result = RewardPoolCandidateResolver.ResolveNeowCandidates(
            __result,
            effectivePlayer,
            __instance,
            __1,
            card => ThermalVortexRewardFilters.IsAllowedDirectReward(
                card,
                effectivePlayer,
                CardCreationSource.Other)).Candidates;
    }
}

[HarmonyPatch(
    typeof(MegaCrit.Sts2.Core.Models.Relics.NeowsTalisman),
    nameof(MegaCrit.Sts2.Core.Models.Relics.NeowsTalisman.AfterObtained))]
internal static class NeowsTalismanRewardFilterScopePatch
{
    [HarmonyPrefix]
    private static void Enter(
        MegaCrit.Sts2.Core.Models.Relics.NeowsTalisman __instance,
        out IDisposable __state)
    {
        __state = ThermalVortexNeowRewardFilterScope.Enter(__instance?.Owner);
    }

    [HarmonyFinalizer]
    private static Exception Exit(
        ref Task __result,
        IDisposable __state,
        Exception __exception)
    {
        if (__state is null)
            return __exception;

        if (__exception is not null || __result is null)
        {
            __state.Dispose();
            return __exception;
        }

        __result = DisposeScopeWhenComplete(__result, __state);
        return null;
    }

    private static async Task DisposeScopeWhenComplete(Task task, IDisposable scope)
    {
        try
        {
            await task;
        }
        finally
        {
            scope.Dispose();
        }
    }
}

internal static class ThermalVortexNeowRewardFilterScope
{
    private static readonly AsyncLocal<Player> CurrentPlayerState = new();
    private static readonly AsyncLocal<int> DepthState = new();

    internal static Player CurrentPlayer => DepthState.Value > 0 ? CurrentPlayerState.Value : null;

    internal static Player TryResolveConstructedPoolPlayer()
    {
        try
        {
            var candidates = RunManager.Instance?
                .DebugOnlyGetState()?
                .Players
                .Where(player => player?.Character is ThermalVortexCharacter)
                .Take(2)
                .ToList();
            if (candidates is not { Count: 1 })
                return null;

            var player = candidates[0];
            return player.GetRelic<ThermalVortexCore>()?.IsConstructedRewardPoolEnabled == true
                ? player
                : null;
        }
        catch
        {
            return null;
        }
    }

    internal static void ResetForRunTransition()
    {
        CurrentPlayerState.Value = null;
        DepthState.Value = 0;
        RewardGrantPolicies.ResetScopesForRunTransition();
        RewardGrantDiagnostics.ResetScopesForRunTransition();
    }

    internal static IDisposable Enter(Player player)
    {
        DepthState.Value++;
        if (player is not null)
            CurrentPlayerState.Value = player;

        return new Scope();
    }

    internal static bool IsNeowRewardStack()
    {
        if (DepthState.Value > 0)
            return true;

        try
        {
            return new StackTrace(false)
                .GetFrames()
                ?.Any(frame => IsNeowType(frame.GetMethod()?.DeclaringType?.FullName)) == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsNeowType(string typeName) =>
        typeName is not null
        && (typeName == "MegaCrit.Sts2.Core.Models.Events.Neow"
            || typeName.StartsWith("MegaCrit.Sts2.Core.Models.Events.Neow+", StringComparison.Ordinal)
            || typeName == "MegaCrit.Sts2.Core.Models.Relics.NeowsTalisman"
            || typeName.StartsWith("MegaCrit.Sts2.Core.Models.Relics.NeowsTalisman+", StringComparison.Ordinal));

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            DepthState.Value = Math.Max(0, DepthState.Value - 1);
            if (DepthState.Value == 0)
                CurrentPlayerState.Value = null;
        }
    }
}
