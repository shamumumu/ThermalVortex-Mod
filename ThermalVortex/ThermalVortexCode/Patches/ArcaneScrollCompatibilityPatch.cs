using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(ArcaneScroll), nameof(ArcaneScroll.AfterObtained))]
internal static class ArcaneScrollCompatibilityPatch
{
    private static bool Prefix(ArcaneScroll __instance, ref Task __result)
    {
        if (!ArcaneScrollCompatibility.ShouldHandle(__instance))
            return true;

        __result = ArcaneScrollCompatibility.AfterObtained(__instance);
        return false;
    }
}

internal static class ArcaneScrollCompatibility
{
    private const int DefaultRewardRollCount = 1;

    internal static bool ShouldHandle(ArcaneScroll scroll) =>
        scroll?.Owner?.Character is ThermalVortexCharacter;

    internal static async Task AfterObtained(ArcaneScroll scroll)
    {
        var player = scroll.Owner;
        if (player?.RunState is not RunState runState)
        {
            MainFile.Logger.Info("ArcaneScrollCompatibility skipped reason=missing_run_state");
            return;
        }

        var rewardRollCount = GetRewardRollCount(scroll);
        var rewards = CreateRareRewardResults(player, rewardRollCount);
        if (rewards.Count == 0)
        {
            MainFile.Logger.Info("ArcaneScrollCompatibility skipped reason=no_rare_rewards");
            return;
        }

        var chosen = rewards.FirstOrDefault()?.Card;
        if (chosen is null)
        {
            MainFile.Logger.Info($"ArcaneScrollCompatibility skipped reason=no_created_card options={rewards.Count}");
            return;
        }

        var addResult = await CardPileCmd.Add(chosen, PileType.Deck, CardPilePosition.Bottom, null, false);
        CardCmd.PreviewCardPileAdd(addResult, 1.2f, CardPreviewStyle.HorizontalLayout);
        scroll.Flash();
        MainFile.Logger.Info($"ArcaneScrollCompatibility added rare_card={chosen.Id} rolled={rewards.Count}");
    }

    internal static IReadOnlyList<CardModel> GetRareRewardCandidates(Player player)
    {
        if (player?.Character?.CardPool is null)
            return [];

        var multiplayerConstraint = player.RunState?.CardMultiplayerConstraint ?? CardMultiplayerConstraint.None;
        var seenIds = new HashSet<string>();
        var core = player.GetRelic<ThermalVortexCore>();
        var sourceCards = core?.IsConstructedRewardPoolEnabled == true
            ? ThermalVortexRewardFilters.GetStableSelectedMainRewardCards(player)
            : player.Character.CardPool.GetUnlockedCards(player.UnlockState, multiplayerConstraint);
        return sourceCards
            .Where(card => ThermalVortexRewardFilters.MatchesMultiplayerConstraint(card, multiplayerConstraint))
            .Where(card => IsValidRareRewardCandidate(card, player))
            .Where(card => seenIds.Add(card.Id?.ToString() ?? card.GetType().FullName ?? card.GetHashCode().ToString()))
            .ToList();
    }

    private static IReadOnlyList<CardCreationResult> CreateRareRewardResults(Player player, int count)
    {
        var candidates = GetRareRewardCandidates(player);
        if (candidates.Count == 0)
            return [];

        var nativeResults = TryCreateNativeRareRewardResults(player, candidates, count);
        if (nativeResults.Count > 0)
            return nativeResults;

        return CreateFallbackRareRewardResults(player, candidates, count);
    }

    private static IReadOnlyList<CardCreationResult> TryCreateNativeRareRewardResults(
        Player player,
        IReadOnlyList<CardModel> candidates,
        int count)
    {
        try
        {
            var options = new CardCreationOptions(candidates, CardCreationSource.Other, CardRarityOddsType.Uniform)
                .WithFlags(CardCreationFlags.NoUpgradeRoll);
            return CardFactory.CreateForReward(player, Math.Min(count, candidates.Count), options)
                .Where(result => result?.Card is not null && IsValidRareRewardCandidate(result.Card, player))
                .Take(count)
                .ToList();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"ArcaneScrollCompatibility native_reward_failed error={ex.GetType().Name} message={ex.Message}");
            return [];
        }
    }

    private static IReadOnlyList<CardCreationResult> CreateFallbackRareRewardResults(
        Player player,
        IReadOnlyList<CardModel> candidates,
        int count)
    {
        if (player.RunState is not RunState runState)
            return [];

        var remaining = candidates.ToList();
        var results = new List<CardCreationResult>();
        var rng = player.PlayerRng?.Rewards;
        while (results.Count < count && remaining.Count > 0)
        {
            var selected = SelectFallback(rng, remaining);
            if (selected is null)
                break;

            remaining.Remove(selected);
            results.Add(new CardCreationResult(runState.CreateCard(selected, player)));
        }

        return results;
    }

    private static int GetRewardRollCount(ArcaneScroll scroll)
    {
        try
        {
            return Math.Max(DefaultRewardRollCount, scroll.DynamicVars.Cards.IntValue);
        }
        catch
        {
            return DefaultRewardRollCount;
        }
    }

    private static bool IsValidRareRewardCandidate(CardModel card, Player player)
    {
        if (!ThermalVortexRewardFilters.IsMainRewardCardForCurrentMode(card, player)
            || card is ExtraDeckCard
            || card is XyzSummon
            || ThermalVortexGeneratedCards.IsRewardExcluded(card))
            return false;

        return card.Rarity == CardRarity.Rare
            && ThermalVortexRewardFilters.IsAllowedDirectReward(card, player, CardCreationSource.Other);
    }

    private static CardModel SelectFallback(Rng rng, IReadOnlyList<CardModel> options)
    {
        if (options.Count == 0)
            return null;

        try
        {
            return rng?.NextItem(options) ?? options[0];
        }
        catch
        {
            return options[0];
        }
    }
}
