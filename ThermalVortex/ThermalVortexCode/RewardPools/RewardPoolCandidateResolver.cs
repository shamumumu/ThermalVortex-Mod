using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.Character;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

internal enum RewardPoolCandidateTier
{
    StandardPassthrough,
    FixedPoolPassthrough,
    StrictSelectedSource,
    SelectedPoolWithFilter,
    SelectedPoolUnfiltered,
    Empty
}

internal readonly record struct RewardPoolCandidateResolution(
    IEnumerable<CardModel> Candidates,
    RewardGrantPolicy Policy,
    RewardPoolCandidateTier Tier);

/// <summary>
/// The single ordering and fallback authority for generic constructed rewards.
/// No method in this type consumes RNG.
/// </summary>
internal static class RewardPoolCandidateResolver
{
    internal static RewardPoolCandidateResolution ResolveCreationOptions(
        IEnumerable<CardModel> nativeCandidates,
        Player player,
        CardCreationOptions options,
        Func<CardModel, bool> isAllowed)
    {
        var policy = RewardGrantPolicies.ForCreationOptions(player, options);
        if (policy == RewardGrantPolicy.StandardLegacy)
        {
            return new RewardPoolCandidateResolution(
                nativeCandidates ?? [],
                policy,
                RewardPoolCandidateTier.StandardPassthrough);
        }

        if (RewardGrantPolicies.IsPassthrough(policy))
        {
            return new RewardPoolCandidateResolution(
                nativeCandidates ?? [],
                policy,
                RewardPoolCandidateTier.FixedPoolPassthrough);
        }

        var native = nativeCandidates?.Where(card => card is not null).ToList() ?? [];
        var stableMain = GetStableSelectedMainRewardCards(player)
            .Where(card => MatchesMultiplayerConstraint(card, GetMultiplayerConstraint(player)))
            .Where(card => isAllowed?.Invoke(card) != false)
            .ToList();
        var stableExtra = GetStableSelectedExtraRewardCards(player)
            .Where(card => MatchesMultiplayerConstraint(card, GetMultiplayerConstraint(player)))
            .Where(card => isAllowed?.Invoke(card) != false)
            .ToList();
        var nativeColorless = native
            .Where(IsOrdinaryColorlessRewardCandidate)
            .Where(card => MatchesMultiplayerConstraint(card, GetMultiplayerConstraint(player)))
            .Where(card => isAllowed?.Invoke(card) != false)
            .ToList();

        var hasPrimaryPool = options?.CardPools?.Any(pool => pool is ThermalVortexCardPool) == true;
        var hasExtraPool = options?.CardPools?.Any(pool => pool is ExtraDeckCardPool) == true;
        var hasKnownMainPool = options?.CardPools?.Any(pool => TryGetOrigin(pool).HasValue) == true;
        var hasColorlessPool = options?.CardPools?.Any(pool => pool is ColorlessCardPool) == true;
        var nativeHasColorless = native.Any(IsOrdinaryColorlessRewardCandidate);
        var nativeHasExtra = native.Any(IsCatalogExtraCard);
        var nativeHasCatalogMain = native.Any(card => IsCatalogMain(card, player));
        var isExtraOnlyRequest = hasExtraPool && !hasKnownMainPool
            || !hasKnownMainPool && nativeHasExtra && !nativeHasCatalogMain;
        var isColorlessOnlyRequest = hasColorlessPool && !hasKnownMainPool && !hasExtraPool
            || !hasKnownMainPool
            && native.Count > 0
            && native.All(IsOrdinaryColorlessRewardCandidate);
        var includesExtraRequest = hasExtraPool
            || !hasKnownMainPool && nativeHasExtra;
        var strictSource = GetStrictSourceCandidates(options, native, IsMultiplayer(player));

        var mainResolution = isExtraOnlyRequest || isColorlessOnlyRequest
            ? new RewardPoolCandidateResolution(
                [],
                policy,
                RewardPoolCandidateTier.Empty)
            : nativeHasColorless
                ? ResolveExactSelectedIntersection(stableMain, native, policy)
            : ResolveTieredMainCandidates(
                stableMain,
                strictSource,
                options?.CardPoolFilter,
                skipStrictSource: hasPrimaryPool,
                policy);

        var extra = ResolveStableExtraCandidates(
            stableExtra,
            native,
            allowFallbackToAllSelected: includesExtraRequest);
        var merged = MergeStable(mainResolution.Candidates, extra, nativeColorless);
        var tier = merged.Count == 0
            ? RewardPoolCandidateTier.Empty
            : mainResolution.Tier == RewardPoolCandidateTier.Empty
                ? extra.Count > 0 || nativeColorless.Count > 0
                    ? RewardPoolCandidateTier.StrictSelectedSource
                    : RewardPoolCandidateTier.Empty
                : mainResolution.Tier;

        return new RewardPoolCandidateResolution(merged, policy, tier);
    }

    internal static RewardPoolCandidateResolution ResolveMerchantCandidates(
        IEnumerable<CardModel> nativeCandidates,
        Player player,
        Func<CardModel, bool> isAllowed)
    {
        var policy = RewardGrantPolicies.ForGenericReward(player);
        if (policy == RewardGrantPolicy.StandardLegacy)
        {
            return new RewardPoolCandidateResolution(
                nativeCandidates ?? [],
                policy,
                RewardPoolCandidateTier.StandardPassthrough);
        }

        if (RewardGrantPolicies.IsPassthrough(policy))
        {
            return new RewardPoolCandidateResolution(
                nativeCandidates ?? [],
                policy,
                RewardPoolCandidateTier.FixedPoolPassthrough);
        }

        var native = nativeCandidates?.Where(card => card is not null).ToList() ?? [];
        var constraint = GetMultiplayerConstraint(player);
        var stableMain = GetStableSelectedMainRewardCards(player)
            .Where(card => MatchesMultiplayerConstraint(card, constraint))
            .Where(card => isAllowed?.Invoke(card) != false)
            .ToList();
        var stableExtra = GetStableSelectedExtraRewardCards(player)
            .Where(card => MatchesMultiplayerConstraint(card, constraint))
            .Where(card => isAllowed?.Invoke(card) != false)
            .ToList();
        var nativeColorless = native
            .Where(IsOrdinaryColorlessRewardCandidate)
            .Where(card => MatchesMultiplayerConstraint(card, constraint))
            .Where(card => isAllowed?.Invoke(card) != false)
            .ToList();

        var nativeHasMain = native.Any(card =>
            !IsOrdinaryColorlessRewardCandidate(card)
            && (IsCatalogMain(card, player)
                || card.GetType().Assembly == typeof(CardModel).Assembly));
        var nativeHasColorless = native.Any(IsOrdinaryColorlessRewardCandidate);
        var nativeHasExtra = native.Any(IsCatalogExtraCard);
        var isColorlessOnlyRequest = native.Count > 0
            && native.All(IsOrdinaryColorlessRewardCandidate);
        IEnumerable<CardModel> main;
        if (nativeHasExtra && !nativeHasMain || isColorlessOnlyRequest)
        {
            main = [];
        }
        else if (nativeHasColorless)
        {
            var nativeIds = native
                .Select(RewardPoolCatalog.GetId)
                .ToHashSet(StringComparer.Ordinal);
            main = stableMain.Where(card => nativeIds.Contains(RewardPoolCatalog.GetId(card)));
        }
        else
        {
            main = stableMain;
        }
        var extra = ResolveStableExtraCandidates(
            stableExtra,
            native,
            allowFallbackToAllSelected: nativeHasExtra && !nativeHasMain);
        var merged = MergeStable(main, extra, nativeColorless);
        return new RewardPoolCandidateResolution(
            merged,
            policy,
            merged.Count > 0
                ? RewardPoolCandidateTier.SelectedPoolUnfiltered
                : RewardPoolCandidateTier.Empty);
    }

    internal static RewardPoolCandidateResolution FilterDirectCandidates(
        IEnumerable<CardModel> nativeCandidates,
        Player player,
        Func<CardModel, bool> isAllowed)
    {
        var policy = RewardGrantPolicies.ForGenericReward(player);
        if (policy == RewardGrantPolicy.StandardLegacy)
        {
            return new RewardPoolCandidateResolution(
                nativeCandidates ?? [],
                policy,
                RewardPoolCandidateTier.StandardPassthrough);
        }

        if (RewardGrantPolicies.IsPassthrough(policy))
        {
            return new RewardPoolCandidateResolution(
                nativeCandidates ?? [],
                policy,
                RewardPoolCandidateTier.FixedPoolPassthrough);
        }

        var native = (nativeCandidates ?? [])
            .Where(card => card is not null)
            .ToList();
        var nativeIds = native
            .Select(RewardPoolCatalog.GetId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var main = GetStableSelectedMainRewardCards(player)
            .Where(card => nativeIds.Contains(RewardPoolCatalog.GetId(card)))
            .Where(card => isAllowed?.Invoke(card) != false);
        var extra = GetStableSelectedExtraRewardCards(player)
            .Where(card => nativeIds.Contains(RewardPoolCatalog.GetId(card)))
            .Where(card => isAllowed?.Invoke(card) != false);
        var nativeColorless = native
            .Where(IsOrdinaryColorlessRewardCandidate)
            .Where(card => MatchesMultiplayerConstraint(card, GetMultiplayerConstraint(player)))
            .Where(card => isAllowed?.Invoke(card) != false);
        var merged = MergeStable(main, extra, nativeColorless);
        return new RewardPoolCandidateResolution(
            merged,
            policy,
            merged.Count > 0
                ? RewardPoolCandidateTier.StrictSelectedSource
                : RewardPoolCandidateTier.Empty);
    }

    internal static RewardPoolCandidateResolution ResolveNeowCandidates(
        IEnumerable<CardModel> nativeCandidates,
        Player player,
        CardPoolModel sourcePool,
        CardMultiplayerConstraint constraint,
        Func<CardModel, bool> isAllowed)
    {
        var policy = RewardGrantPolicies.ForGenericReward(player);
        if (policy == RewardGrantPolicy.StandardLegacy)
        {
            return new RewardPoolCandidateResolution(
                nativeCandidates ?? [],
                policy,
                RewardPoolCandidateTier.StandardPassthrough);
        }

        if (!RewardGrantPolicies.RequiresSelectedPool(policy))
        {
            return new RewardPoolCandidateResolution(
                nativeCandidates ?? [],
                policy,
                RewardPoolCandidateTier.FixedPoolPassthrough);
        }

        var native = nativeCandidates?.Where(card => card is not null).ToList() ?? [];
        var stableMain = GetStableSelectedMainRewardCards(player)
            .Where(card => MatchesMultiplayerConstraint(card, constraint))
            .Where(card => isAllowed?.Invoke(card) != false)
            .ToList();
        var stableExtra = GetStableSelectedExtraRewardCards(player)
            .Where(card => MatchesMultiplayerConstraint(card, constraint))
            .Where(card => isAllowed?.Invoke(card) != false)
            .ToList();
        var nativeColorless = native
            .Where(IsOrdinaryColorlessRewardCandidate)
            .Where(card => MatchesMultiplayerConstraint(card, constraint))
            .Where(card => isAllowed?.Invoke(card) != false)
            .ToList();
        var isPrimaryPool = sourcePool is ThermalVortexCardPool;
        var isExtraPool = sourcePool is ExtraDeckCardPool;
        var isColorlessPool = sourcePool is ColorlessCardPool;
        var nativeHasColorless = native.Any(IsOrdinaryColorlessRewardCandidate);
        var sourceOrigin = TryGetOrigin(sourcePool);
        var nativeHasExtra = native.Any(IsCatalogExtraCard);
        var nativeHasCatalogMain = native.Any(card => IsCatalogMain(card, player));
        var isExtraOnlyRequest = isExtraPool
            || !isPrimaryPool
            && !sourceOrigin.HasValue
            && nativeHasExtra
            && !nativeHasCatalogMain;
        var isColorlessOnlyRequest = isColorlessPool
            || !isPrimaryPool
            && !isExtraPool
            && !sourceOrigin.HasValue
            && native.Count > 0
            && native.All(IsOrdinaryColorlessRewardCandidate);
        IReadOnlyCollection<CardModel> strictSource = sourceOrigin.HasValue
            ? RewardPoolCatalog.GetMainRewardCandidates(sourceOrigin.Value, IsMultiplayer(player))
            : native;
        var mainResolution = isExtraOnlyRequest || isColorlessOnlyRequest
            ? new RewardPoolCandidateResolution(
                [],
                policy,
                RewardPoolCandidateTier.Empty)
            : nativeHasColorless
                ? ResolveExactSelectedIntersection(stableMain, native, policy)
            : ResolveTieredMainCandidates(
                stableMain,
                strictSource,
                retainedFilter: null,
                skipStrictSource: isPrimaryPool,
                policy);
        var extra = ResolveStableExtraCandidates(
            stableExtra,
            native,
            allowFallbackToAllSelected: isExtraOnlyRequest);
        var selected = MergeStable(mainResolution.Candidates, extra, nativeColorless);
        return new RewardPoolCandidateResolution(
            selected,
            policy,
            selected.Count > 0
                ? mainResolution.Tier == RewardPoolCandidateTier.Empty
                    ? RewardPoolCandidateTier.StrictSelectedSource
                    : mainResolution.Tier
                : RewardPoolCandidateTier.Empty);
    }

    /// <summary>
    /// Pure, deterministic tier selection used by diagnostics as well as the
    /// runtime resolver. Input order is preserved and no game state is read.
    /// </summary>
    internal static RewardPoolCandidateResolution ResolveTieredMainCandidates(
        IReadOnlyList<CardModel> stableSelected,
        IReadOnlyCollection<CardModel> nativeCandidates,
        Func<CardModel, bool> retainedFilter,
        bool skipStrictSource,
        RewardGrantPolicy policy = RewardGrantPolicy.GenericRandomReward)
    {
        stableSelected ??= [];
        nativeCandidates ??= [];

        if (!skipStrictSource)
        {
            var nativeIds = nativeCandidates
                .Select(RewardPoolCatalog.GetId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            var strict = stableSelected
                .Where(card => nativeIds.Contains(RewardPoolCatalog.GetId(card)))
                .ToList();
            if (strict.Count > 0)
            {
                return new RewardPoolCandidateResolution(
                    strict,
                    policy,
                    RewardPoolCandidateTier.StrictSelectedSource);
            }
        }

        var filtered = stableSelected
            .Where(card => SafelyMatches(retainedFilter, card))
            .ToList();
        if (filtered.Count > 0)
        {
            return new RewardPoolCandidateResolution(
                filtered,
                policy,
                RewardPoolCandidateTier.SelectedPoolWithFilter);
        }

        var unfiltered = stableSelected.ToList();
        return new RewardPoolCandidateResolution(
            unfiltered,
            policy,
            unfiltered.Count > 0
                ? RewardPoolCandidateTier.SelectedPoolUnfiltered
                : RewardPoolCandidateTier.Empty);
    }

    private static RewardPoolCandidateResolution ResolveExactSelectedIntersection(
        IReadOnlyList<CardModel> stableSelected,
        IReadOnlyCollection<CardModel> nativeCandidates,
        RewardGrantPolicy policy)
    {
        var nativeIds = (nativeCandidates ?? [])
            .Select(RewardPoolCatalog.GetId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var selected = (stableSelected ?? [])
            .Where(card => nativeIds.Contains(RewardPoolCatalog.GetId(card)))
            .ToList();
        return new RewardPoolCandidateResolution(
            selected,
            policy,
            selected.Count > 0
                ? RewardPoolCandidateTier.StrictSelectedSource
                : RewardPoolCandidateTier.Empty);
    }

    internal static IReadOnlyList<CardModel> GetStableSelectedMainRewardCards(Player player)
    {
        var core = player?.GetRelic<ThermalVortexCore>();
        if (core?.IsConstructedRewardPoolEnabled != true)
            return [];

        return RewardPoolCatalog
            .GetMainRewardCandidates(IsMultiplayer(player))
            .Where(core.IsMainDeckRewardAllowed)
            .ToList();
    }

    internal static IReadOnlyList<CardModel> GetStableSelectedExtraRewardCards(Player player)
    {
        var core = player?.GetRelic<ThermalVortexCore>();
        if (core?.IsConstructedRewardPoolEnabled != true)
            return [];

        return RewardPoolCatalog
            .GetExtraCandidates()
            .Where(card => IsSelectedExtraRewardCandidate(card, core))
            .ToList();
    }

    internal static bool IsSelectedExtraRewardCandidate(
        CardModel card,
        ThermalVortexCore core) =>
        card is not null
        && core?.CurrentRewardPoolDefinition?.ContainsExtraReward(card) == true;

    /// <summary>
    /// Only native ordinary colorless reward cards are additive to a manually
    /// selected pool. Visual color, generated tokens, curses, statuses, and
    /// event-only pools do not qualify for this candidate-level exemption.
    /// </summary>
    internal static bool IsOrdinaryColorlessRewardCandidate(CardModel card) =>
        card is not null
        && card.Pool is ColorlessCardPool
        && card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare;

    internal static bool MatchesMultiplayerConstraint(
        CardModel card,
        CardMultiplayerConstraint requestedConstraint)
    {
        if (card is null)
            return false;

        return requestedConstraint switch
        {
            CardMultiplayerConstraint.MultiplayerOnly =>
                card.MultiplayerConstraint != CardMultiplayerConstraint.SingleplayerOnly,
            CardMultiplayerConstraint.SingleplayerOnly =>
                card.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly,
            _ => true
        };
    }

    private static CardMultiplayerConstraint GetMultiplayerConstraint(Player player) =>
        player?.RunState?.CardMultiplayerConstraint ?? CardMultiplayerConstraint.None;

    private static bool IsMultiplayer(Player player) =>
        GetMultiplayerConstraint(player) == CardMultiplayerConstraint.MultiplayerOnly
        || player?.GetRelic<ThermalVortexCore>()?.CurrentRewardPoolDefinition?.IsMultiplayerConstruction == true;

    internal static bool IsCatalogMain(CardModel card, Player player) => card is not null
        && RewardPoolCatalog.TryGetMainRewardCandidate(RewardPoolCatalog.GetId(card), IsMultiplayer(player), out _);

    private static IReadOnlyCollection<CardModel> GetStrictSourceCandidates(
        CardCreationOptions options,
        IReadOnlyCollection<CardModel> nativeCandidates,
        bool multiplayer)
    {
        var origins = (options?.CardPools ?? [])
            .Select(TryGetOrigin)
            .Where(origin => origin.HasValue)
            .Select(origin => origin.Value)
            .Distinct()
            .ToList();
        if (origins.Count == 0)
            return nativeCandidates;

        return origins
            .SelectMany(origin => RewardPoolCatalog.GetMainRewardCandidates(origin, multiplayer))
            .Where(card => SafelyMatches(options?.CardPoolFilter, card))
            .ToList();
    }

    private static RewardPoolCardOrigin? TryGetOrigin(CardPoolModel pool) => pool switch
    {
        ThermalVortexCardPool => RewardPoolCardOrigin.ThermalVortex,
        IroncladCardPool => RewardPoolCardOrigin.Ironclad,
        SilentCardPool => RewardPoolCardOrigin.Silent,
        DefectCardPool => RewardPoolCardOrigin.Defect,
        NecrobinderCardPool => RewardPoolCardOrigin.Necrobinder,
        RegentCardPool => RewardPoolCardOrigin.Regent,
        _ => null
    };

    private static IReadOnlyList<CardModel> ResolveStableExtraCandidates(
        IReadOnlyList<CardModel> stableSelected,
        IReadOnlyCollection<CardModel> nativeCandidates,
        bool allowFallbackToAllSelected)
    {
        if (!allowFallbackToAllSelected)
            return [];

        var nativeIds = nativeCandidates
            .Where(IsCatalogExtraCard)
            .Select(RewardPoolCatalog.GetId)
            .ToHashSet(StringComparer.Ordinal);
        var strict = stableSelected
            .Where(card => nativeIds.Contains(RewardPoolCatalog.GetId(card)))
            .ToList();
        return strict.Count > 0 ? strict : stableSelected.ToList();
    }

    private static bool IsCatalogExtraCard(CardModel card) =>
        card is not null
        && RewardPoolCatalog.TryGetExtraRewardCandidate(RewardPoolCatalog.GetId(card), out _);

    private static bool SafelyMatches(Func<CardModel, bool> filter, CardModel card)
    {
        if (filter is null)
            return true;

        try
        {
            return filter(card);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<CardModel> MergeStable(
        params IEnumerable<CardModel>[] groups)
    {
        var result = new List<CardModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups ?? [])
            AddUnique(group, result, seen);

        return result;
    }

    private static void AddUnique(
        IEnumerable<CardModel> cards,
        ICollection<CardModel> result,
        ISet<string> seen)
    {
        if (cards is null)
            return;

        foreach (var card in cards)
        {
            if (card is null)
                continue;

            var id = RewardPoolCatalog.GetId(card);
            if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                result.Add(card);
        }
    }
}
