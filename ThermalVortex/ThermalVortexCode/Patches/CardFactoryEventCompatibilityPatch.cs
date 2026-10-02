using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(
    typeof(CardFactory),
    "CreateForReward",
    [typeof(Player), typeof(IEnumerable<CardModel>), typeof(CardCreationOptions)])]
internal static class CardFactoryEventCompatibilityPatch
{
    private static readonly HashSet<CardRarity> EncounterRewardRarities =
    [
        CardRarity.Common,
        CardRarity.Uncommon,
        CardRarity.Rare
    ];

    [HarmonyPrefix]
    private static void AllowRepeatedEventOptionsWhenPoolIsExhausted(
        Player player,
        ref IEnumerable<CardModel> blacklist,
        CardCreationOptions options)
    {
        if (player.Character is not ThermalVortexCharacter)
            return;

        if (RewardGrantPolicies.IsPassthrough(
                RewardGrantPolicies.ForCreationOptions(player, options)))
        {
            return;
        }

        var thermalVortexBlacklist = AddThermalVortexBlacklist(player, blacklist, options).ToList();
        blacklist = thermalVortexBlacklist;

        if (options.Source == CardCreationSource.Encounter)
        {
            if (!HasExhaustedEncounterRarity(player, thermalVortexBlacklist, options))
                return;

            blacklist = AddThermalVortexBlacklist(player, [], options).ToList();
            return;
        }

        if (options.Source == CardCreationSource.Shop)
            return;

        var blacklistedIds = thermalVortexBlacklist.Select(card => card.Id).ToHashSet();
        if (options.GetPossibleCards(player).Any(card =>
                IsNativeRewardRarity(card.Rarity)
                && !blacklistedIds.Contains(card.Id)))
            return;

        blacklist = AddThermalVortexBlacklist(player, [], options);
    }

    private static bool IsNativeRewardRarity(CardRarity rarity) =>
        rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;

    private static bool HasExhaustedEncounterRarity(
        Player player,
        IReadOnlyCollection<CardModel> blacklist,
        CardCreationOptions options)
    {
        var blacklistedIds = blacklist.Select(card => card.Id).ToHashSet();
        return options.GetPossibleCards(player)
            .Where(card => IsMainDeckEncounterRewardCard(card, player))
            .GroupBy(card => card.Rarity)
            .Any(group => group.All(card => blacklistedIds.Contains(card.Id)));
    }

    private static bool IsMainDeckEncounterRewardCard(CardModel card, Player player) =>
        EncounterRewardRarities.Contains(card.Rarity)
        && card is not ExtraDeckCard
        && ThermalVortexRewardFilters.IsMainRewardCardForCurrentMode(card, player)
        && !ThermalVortexRewardFilters.IsAlwaysDirectRewardExcluded(card);

    private static IEnumerable<CardModel> AddThermalVortexBlacklist(
        Player player,
        IEnumerable<CardModel> blacklist,
        CardCreationOptions options)
    {
        foreach (var card in blacklist)
            yield return card;

        foreach (var requiredMain in RewardPoolCatalog.GetRequiredMainCards())
            yield return requiredMain;

        yield return ModelDb.Card<ElectromagneticCircle>();
        yield return ModelDb.Card<PrimalGodFara>();
        yield return ModelDb.Card<CyberDragon>();
        yield return ModelDb.Card<CyberLarva>();
        yield return ModelDb.Card<ExodiaLeftArm>();
        yield return ModelDb.Card<ExodiaRightArm>();
        yield return ModelDb.Card<ExodiaLeftLeg>();
        yield return ModelDb.Card<ExodiaRightLeg>();
        yield return ModelDb.Card<ExodiaTorso>();

        if (!ShouldBlacklistExtraDeckCards(player, options))
            yield break;

        foreach (var card in GetPossibleExtraDeckCards(player, options))
            yield return card;
    }

    private static bool ShouldBlacklistExtraDeckCards(Player player, CardCreationOptions options) =>
        options.Source == CardCreationSource.Encounter
        || player.GetRelic<ThermalVortexCore>()?.CanAcceptExtraDeckReward == false;

    private static IEnumerable<CardModel> GetPossibleExtraDeckCards(Player player, CardCreationOptions options) =>
        options.GetPossibleCards(player).Where(card => card is XyzMonsterCard);
}

[HarmonyPatch(
    typeof(CardFactory),
    nameof(CardFactory.CreateForReward),
    [typeof(Player), typeof(int), typeof(CardCreationOptions)])]
internal static class CardFactoryRewardCountCompatibilityPatch
{
    [HarmonyFinalizer]
    private static Exception FallbackWhenRewardPoolIsExhausted(
        Player player,
        int cardCount,
        CardCreationOptions options,
        ref IEnumerable<CardCreationResult> __result,
        Exception __exception)
    {
        if (__exception is null)
            return null;

        if (!RewardFallback.TryCreate(player, cardCount, options, __exception, out var fallbackResults))
            return __exception;

        __result = fallbackResults;
        return null;
    }
}

[HarmonyPatch(
    typeof(CardFactory),
    "CreateForReward",
    [typeof(Player), typeof(IEnumerable<CardModel>), typeof(CardCreationOptions)])]
internal static class CardFactoryPrivateRewardCompatibilityPatch
{
    [HarmonyFinalizer]
    private static Exception FallbackWhenPrivateRewardPoolIsExhausted(
        Player player,
        IEnumerable<CardModel> blacklist,
        CardCreationOptions options,
        ref CardModel __result,
        Exception __exception)
    {
        if (__exception is null)
            return null;

        if (!RewardFallback.TryCreate(player, 1, options, __exception, out var fallbackResults))
            return __exception;

        __result = fallbackResults.FirstOrDefault()?.Card;
        return __result is null ? __exception : null;
    }
}

internal static class RewardFallback
{
    internal static bool TryCreate(
        Player player,
        int cardCount,
        CardCreationOptions options,
        Exception exception,
        out IReadOnlyList<CardCreationResult> results)
    {
        results = [];

        if (!IsRewardGenerationFailure(exception)
            || player?.Character is not ThermalVortexCharacter
            || player.RunState is not RunState runState)
            return false;

        var candidates = GetCandidates(player, options).ToList();
        if (candidates.Count == 0)
            return false;

        var created = new List<CardCreationResult>();
        var rng = options?.RngOverride ?? player.PlayerRng.Rewards;
        for (var i = 0; i < Math.Max(1, cardCount); i++)
        {
            var selected = SelectFallback(rng, candidates);
            if (selected is null)
                break;

            created.Add(new CardCreationResult(runState.CreateCard(selected, player)));
            if (candidates.Count > 1)
                candidates.Remove(selected);
        }

        if (created.Count == 0)
            return false;

        results = created;
        return true;
    }

    private static IEnumerable<CardModel> GetCandidates(Player player, CardCreationOptions options)
    {
        var policy = RewardGrantPolicies.ForCreationOptions(player, options);
        if (RewardGrantPolicies.IsPassthrough(policy))
            yield break;

        var possibleCards = TryGetPossibleCards(player, options).ToList();
        IEnumerable<CardModel> sourceCards;
        if (RewardGrantPolicies.RequiresSelectedPool(policy))
        {
            sourceCards = RewardPoolCandidateResolver.ResolveCreationOptions(
                possibleCards,
                player,
                options,
                card => ThermalVortexRewardFilters.IsAllowedDirectReward(card, player, options)).Candidates;
        }
        else
        {
            // This is the exact legacy fallback used before constructed pools.
            sourceCards = possibleCards.Count > 0
                ? possibleCards
                : GetUnlockedCharacterCards(player);
        }

        var seenIds = new HashSet<string>();
        foreach (var card in sourceCards)
        {
            if (!seenIds.Add(card.Id?.ToString() ?? card.GetType().FullName ?? card.GetHashCode().ToString()))
                continue;

            if (IsValidRewardCandidate(card, player, options))
                yield return card;
        }
    }

    private static IEnumerable<CardModel> TryGetPossibleCards(Player player, CardCreationOptions options)
    {
        try
        {
            return options?.GetPossibleCards(player) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<CardModel> GetUnlockedCharacterCards(Player player)
    {
        var cardPool = player.Character.CardPool;
        if (cardPool is null)
            return [];

        var multiplayerConstraint = player.RunState?.CardMultiplayerConstraint ?? CardMultiplayerConstraint.None;
        return cardPool.GetUnlockedCards(player.UnlockState, multiplayerConstraint);
    }

    private static bool IsValidRewardCandidate(CardModel card, Player player, CardCreationOptions options)
    {
        if (RewardGrantPolicies.RequiresSelectedPool(
                RewardGrantPolicies.ForCreationOptions(player, options)))
        {
            if (!RewardPoolCandidateResolver.IsCatalogMain(card, player)
                && !ThermalVortexRewardFilters.IsExtraDeckRewardCard(card)
                && !RewardPoolCandidateResolver.IsOrdinaryColorlessRewardCandidate(card))
            {
                return false;
            }
        }
        else if (card is not MainDeckCard
                 && !ThermalVortexRewardFilters.IsExtraDeckRewardCard(card))
        {
            return false;
        }

        if (!ThermalVortexRewardFilters.IsAllowedDirectReward(card, player, options))
            return false;

        if (card.Rarity is not CardRarity.Common and not CardRarity.Uncommon and not CardRarity.Rare)
            return false;

        return true;
    }

    private static bool IsRewardGenerationFailure(Exception exception) =>
        exception is InvalidOperationException
        && exception.Message.Contains("valid rarity", StringComparison.OrdinalIgnoreCase);

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

[HarmonyPatch(
    typeof(MerchantCardEntry),
    MethodType.Constructor,
    [typeof(Player), typeof(MerchantInventory), typeof(IEnumerable<CardModel>), typeof(CardType)])]
internal static class MerchantCardEntryTypeConstructedPoolPatch
{
    [HarmonyPrefix]
    private static void ExpandStoredCharacterPool(
        Player __0,
        ref IEnumerable<CardModel> __2)
    {
        __2 = MerchantConstructedPoolCompatibility.ExpandCharacterPool(__0, __2);
    }
}

[HarmonyPatch(
    typeof(MerchantCardEntry),
    MethodType.Constructor,
    [typeof(Player), typeof(MerchantInventory), typeof(IEnumerable<CardModel>), typeof(CardRarity)])]
internal static class MerchantCardEntryRarityConstructedPoolPatch
{
    [HarmonyPrefix]
    private static void ExpandStoredCharacterPool(
        Player __0,
        ref IEnumerable<CardModel> __2)
    {
        __2 = MerchantConstructedPoolCompatibility.ExpandCharacterPool(__0, __2);
    }
}

internal static class MerchantConstructedPoolCompatibility
{
    internal static IEnumerable<CardModel> ExpandCharacterPool(
        Player player,
        IEnumerable<CardModel> originalOptions)
    {
        if (player?.Character is not ThermalVortexCharacter)
            return originalOptions;

        return RewardPoolCandidateResolver.ResolveMerchantCandidates(
            originalOptions,
            player,
            card => ThermalVortexRewardFilters.IsAllowedDirectReward(
                card,
                player,
                CardCreationSource.Shop)).Candidates;
    }
}

[HarmonyPatch(
    typeof(CardFactory),
    nameof(CardFactory.CreateForMerchant),
    [typeof(Player), typeof(IEnumerable<CardModel>), typeof(CardType)])]
internal static class CardFactoryMerchantTypeCompatibilityPatch
{
    [HarmonyPrefix]
    private static void RemoveThermalVortexSpecialCards(
        Player player,
        ref IEnumerable<CardModel> options)
    {
        if (player?.Character is not ThermalVortexCharacter)
            return;

        options = ThermalVortexRewardFilters.FilterDirectRewardCandidates(
            options,
            player,
            CardCreationSource.Shop);
    }

    [HarmonyFinalizer]
    private static Exception FallbackWhenMerchantTypePoolIsExhausted(
        Player player,
        IEnumerable<CardModel> options,
        CardType type,
        ref CardCreationResult __result,
        Exception __exception)
    {
        if (__exception is null)
            return null;

        if (!MerchantFallback.TryCreate(player, options, card => card.Type == type, __exception, out var fallbackResult))
            return __exception;

        __result = fallbackResult;
        return null;
    }
}

[HarmonyPatch(
    typeof(CardFactory),
    nameof(CardFactory.CreateForMerchant),
    [typeof(Player), typeof(IEnumerable<CardModel>), typeof(CardRarity)])]
internal static class CardFactoryMerchantRarityCompatibilityPatch
{
    [HarmonyPrefix]
    private static void RemoveThermalVortexSpecialCards(
        Player player,
        ref IEnumerable<CardModel> options)
    {
        if (player?.Character is not ThermalVortexCharacter)
            return;

        options = ThermalVortexRewardFilters.FilterDirectRewardCandidates(
            options,
            player,
            CardCreationSource.Shop);
    }

    [HarmonyFinalizer]
    private static Exception FallbackWhenMerchantRarityPoolIsExhausted(
        Player player,
        IEnumerable<CardModel> options,
        CardRarity rarity,
        ref CardCreationResult __result,
        Exception __exception)
    {
        if (__exception is null)
            return null;

        if (!MerchantFallback.TryCreate(player, options, card => card.Rarity == rarity, __exception, out var fallbackResult))
            return __exception;

        __result = fallbackResult;
        return null;
    }
}

internal static class MerchantFallback
{
    internal static bool TryCreate(
        Player player,
        IEnumerable<CardModel> options,
        Func<CardModel, bool> preferredFilter,
        Exception exception,
        out CardCreationResult result)
    {
        result = null;

        if (!IsMerchantGenerationFailure(exception)
            || player?.Character is not ThermalVortexCharacter
            || player.RunState is not RunState runState)
            return false;

        var validOptions = RewardPoolCandidateResolver
            .ResolveMerchantCandidates(
                options,
                player,
                card => ThermalVortexRewardFilters.IsAllowedDirectReward(
                    card,
                    player,
                    CardCreationSource.Shop))
            .Candidates
            .Where(card => ThermalVortexRewardFilters.IsAllowedDirectReward(
                card,
                player,
                CardCreationSource.Shop))
            .Where(card => ThermalVortexRewardFilters.IsMainRewardCardForCurrentMode(card, player)
                || ThermalVortexRewardFilters.IsExtraDeckRewardCard(card))
            .ToList();
        if (validOptions.Count == 0)
            return false;

        var preferredOptions = validOptions
            .Where(preferredFilter)
            .ToList();
        var selected = SelectFallback(player, preferredOptions.Count > 0 ? preferredOptions : validOptions);
        if (selected is null)
            return false;

        result = new CardCreationResult(runState.CreateCard(selected, player));
        return true;
    }

    private static bool IsMerchantGenerationFailure(Exception exception) =>
        exception is InvalidOperationException
        && exception.Message.Contains("merchant card options", StringComparison.OrdinalIgnoreCase);

    private static CardModel SelectFallback(Player player, IReadOnlyList<CardModel> options)
    {
        if (options.Count == 0)
            return null;

        try
        {
            return player.PlayerRng.Shops.NextItem(options);
        }
        catch
        {
            return options[0];
        }
    }
}

[HarmonyPatch(
    typeof(CardFactory),
    nameof(CardFactory.CreateRandomCardForTransform),
    [typeof(CardModel), typeof(bool), typeof(Rng)])]
internal static class CardFactoryThermalVortexDefaultTransformPatch
{
    [HarmonyPrefix]
    private static bool UseSelectedPoolForPermanentTransform(
        CardModel __0,
        bool __1,
        Rng __2,
        ref CardModel __result,
        out bool __state)
    {
        __state = ThermalVortexTransformFallback.TryCreateSelectedPoolTransform(
            __0,
            __1,
            __2,
            out var selected);
        if (__state)
            __result = selected;

        return !__state;
    }

    [HarmonyPostfix]
    private static void KeepThermalVortexTransformsInPool(
        CardModel __0,
        bool __1,
        Rng __2,
        bool __state,
        ref CardModel __result)
    {
        if (!__state)
            __result = ThermalVortexTransformFallback.Select(__0, __1, __2, __result);
    }
}

[HarmonyPatch(
    typeof(CardFactory),
    nameof(CardFactory.CreateRandomCardForTransform),
    [typeof(CardModel), typeof(IEnumerable<CardModel>), typeof(bool), typeof(Rng)])]
internal static class CardFactoryThermalVortexExplicitTransformPatch
{
    [HarmonyPrefix]
    private static bool UseSelectedPoolForPermanentTransform(
        CardModel __0,
        bool __2,
        Rng __3,
        ref CardModel __result,
        out bool __state)
    {
        __state = ThermalVortexTransformFallback.TryCreateSelectedPoolTransform(
            __0,
            __2,
            __3,
            out var selected);
        if (__state)
            __result = selected;

        return !__state;
    }

    [HarmonyPostfix]
    private static void KeepThermalVortexTransformsInPool(
        CardModel __0,
        bool __2,
        Rng __3,
        bool __state,
        ref CardModel __result)
    {
        if (!__state)
            __result = ThermalVortexTransformFallback.Select(__0, __2, __3, __result);
    }
}

internal static class ThermalVortexTransformFallback
{
    /// <summary>
    /// Intercepts only permanent transforms in manual construction mode. The
    /// native overload is skipped so an empty selected pool cannot silently
    /// fall back to an unselected card or consume transform RNG.
    /// </summary>
    internal static bool TryCreateSelectedPoolTransform(
        CardModel source,
        bool isInCombat,
        Rng rng,
        out CardModel result)
    {
        result = null;
        if (!ShouldUseSelectedPool(source, isInCombat))
            return false;

        var candidates = GetSelectedPoolCandidates(source);
        if (candidates.Count == 0)
        {
            RewardGrantDiagnostics.ObserveNoCandidateTransform(source);
            result = CloneTransformSource(source);
            return true;
        }

        var selected = SelectFallback(rng, candidates);
        result = CreateTransformResult(source, selected);
        if (result is not null)
            return true;

        RewardGrantDiagnostics.ObserveNoCandidateTransform(source);
        result = CloneTransformSource(source);
        return true;
    }

    internal static CardModel Select(
        CardModel source,
        bool isInCombat,
        Rng rng,
        CardModel current)
    {
        if (!TryGetLegacyCandidates(source, isInCombat, out var candidates))
            return current;

        if (IsSelectedFromCandidates(current, candidates))
            return current;

        var selected = SelectFallback(rng, candidates);
        return CreateTransformResult(source, selected) ?? current;
    }

    internal static bool TryGetCandidates(
        CardModel source,
        bool isInCombat,
        out IReadOnlyList<CardModel> candidates)
    {
        candidates = [];

        var player = source?.Owner;
        if (player?.Character is not ThermalVortexCharacter)
            return false;

        if (ShouldUseSelectedPool(source, isInCombat))
        {
            candidates = GetSelectedPoolCandidates(source);
            return true;
        }

        return TryGetLegacyCandidates(source, isInCombat, out candidates);
    }

    private static bool ShouldUseSelectedPool(CardModel source, bool isInCombat)
    {
        var player = source?.Owner;
        return !isInCombat
            && source is not ExtraDeckCard
            && player?.Character is ThermalVortexCharacter
            && RewardGrantPolicies.IsManualMode(player);
    }

    private static IReadOnlyList<CardModel> GetSelectedPoolCandidates(CardModel source)
    {
        var player = source?.Owner;
        if (player is null)
            return [];

        var allSelected = RewardPoolCandidateResolver
            .GetStableSelectedMainRewardCards(player)
            .Where(candidate => IsValidTransformCandidate(source, candidate, isInCombat: false))
            .ToList();
        var selectedFromSameSource = allSelected
            .Where(candidate => SameCardPool(source, candidate))
            .ToList();
        return selectedFromSameSource.Count > 0
            ? selectedFromSameSource
            : allSelected;
    }

    private static bool TryGetLegacyCandidates(
        CardModel source,
        bool isInCombat,
        out IReadOnlyList<CardModel> candidates)
    {
        candidates = [];

        var player = source?.Owner;
        if (player?.Character is not ThermalVortexCharacter)
            return false;

        var isManualMode = RewardGrantPolicies.IsManualMode(player);
        if (isManualMode && source is ExtraDeckCard)
            return false;

        // Preserve the legacy ThermalVortex transform pool in standard mode.
        // A card selected from another character may use its native pool only
        // while the explicit manual construction that introduced it is active.
        var useForeignNativePool = isManualMode
            && source.Pool is not null
            && source.Pool is not ThermalVortex.ThermalVortexCode.Character.ThermalVortexCardPool;
        var cardPool = useForeignNativePool
            ? source.Pool
            : player.Character.CardPool;
        if (cardPool is null)
            return false;

        var multiplayerConstraint = player.RunState?.CardMultiplayerConstraint ?? CardMultiplayerConstraint.None;
        candidates = cardPool
            .GetUnlockedCards(player.UnlockState, multiplayerConstraint)
            .Where(card => IsValidTransformCandidate(source, card, isInCombat))
            .ToList();

        return candidates.Count > 0;
    }

    private static bool SameCardPool(CardModel source, CardModel candidate)
    {
        var sourcePool = source?.Pool;
        var candidatePool = candidate?.Pool;
        return sourcePool is not null
            && candidatePool is not null
            && (ReferenceEquals(sourcePool, candidatePool)
                || sourcePool.GetType() == candidatePool.GetType());
    }

    private static bool IsValidTransformCandidate(
        CardModel source,
        CardModel candidate,
        bool isInCombat)
    {
        var isSupportedMainCard = candidate is MainDeckCard
            || candidate.GetType().Assembly == typeof(CardModel).Assembly;
        if (!isSupportedMainCard
            || candidate is ExtraDeckCard
            || ThermalVortexRewardFilters.IsAlwaysDirectRewardExcluded(candidate))
            return false;

        if (!candidate.IsTransformable)
            return false;

        if (!AllowsAnyTransformRarity(source) && !IsStandardTransformRarity(candidate.Rarity))
            return false;

        if (isInCombat && !candidate.CanBeGeneratedInCombat)
            return false;

        if (source?.Id is not null && source.Id.Equals(candidate.Id))
            return false;

        return true;
    }

    private static bool AllowsAnyTransformRarity(CardModel source) =>
        source?.Rarity is CardRarity.Status or CardRarity.Curse;

    private static bool IsStandardTransformRarity(CardRarity rarity) =>
        rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;

    private static bool IsSelectedFromCandidates(CardModel selected, IReadOnlyList<CardModel> candidates)
    {
        if (selected?.Id is null)
            return false;

        return candidates.Any(candidate => candidate?.Id is not null && candidate.Id.Equals(selected.Id));
    }

    private static CardModel CreateTransformResult(CardModel source, CardModel selected)
    {
        if (source?.Owner is null || selected is null)
            return null;

        return source.CardScope.CreateCard(selected, source.Owner);
    }

    private static CardModel CloneTransformSource(CardModel source)
    {
        if (source is null)
            return null;

        try
        {
            return source.CardScope.CloneCard(source) ?? source;
        }
        catch
        {
            return source;
        }
    }

    private static CardModel SelectFallback(Rng rng, IReadOnlyList<CardModel> candidates)
    {
        if (candidates.Count == 0)
            return null;

        try
        {
            return rng?.NextItem(candidates) ?? candidates[0];
        }
        catch
        {
            return candidates[0];
        }
    }
}
