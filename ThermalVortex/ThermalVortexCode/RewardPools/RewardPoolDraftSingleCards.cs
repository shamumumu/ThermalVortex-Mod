using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

public static partial class RewardPoolDraftService
{
    public const int TransitionPackageSize = 8;
    public const int GenericCardOfferCount = 10;
    public const int GenericCardPickCount = 3;

    private static readonly string[] TransitionOptionIds =
        ["transition.start", "transition.defense", "transition.sustain"];

    public static bool TryChooseGenericCards(
        string sessionId,
        int round,
        IReadOnlyList<string> cardIds,
        out string error)
    {
        lock (StateLock)
        {
            if (!CanWriteLocked(out error))
                return false;
            var state = _payload?.State;
            if (state is not { UsesSingleCardDraft: true, Stage: RewardPoolDraftStage.GenericCards }
                || state.SessionId != sessionId || state.GenericRoundsCompleted != round)
            {
                error = "The current draft is no longer showing this offer.";
                return Fail(error);
            }
            var selected = cardIds?.ToArray();
            if (!ValidIds(selected) || selected.Length != GenericCardPickCount
                || selected.Any(id => !state.OfferIds.Contains(id, StringComparer.Ordinal)))
            {
                error = "Choose three different cards from the current offer.";
                return Fail(error);
            }
            try
            {
                if (!ValidateCurrentCatalog(state, out error))
                    return Fail(error);
                // Apply the complete selection and save the next offer in one transaction.
                var picked = state.OfferIds.Where(id => selected.Contains(id, StringComparer.Ordinal)).ToArray();
                var candidate = _payload.Snapshot();
                candidate.State = state with
                {
                    MainCardIds = AddUnique(state.MainCardIds, picked),
                    SelectedGenericCardIds = state.SelectedGenericCardIds.Concat(picked).ToArray(),
                    GenericPackagePicked = state.SelectedGenericCardIds.Count + picked.Length == GenericTotalCardCount,
                    OfferIds = []
                };
                if (!TryPrepareNextOffer(candidate, out error))
                    return Fail(error);
                return CommitLocked(candidate, out error);
            }
            catch (Exception ex)
            {
                error = $"The draft choice could not be prepared: {ex.Message}";
                return Fail(error);
            }
        }
    }

    private static bool CanOfferSingleCardDraft(IEnumerable<string> picked, IReadOnlyDictionary<string, CardModel> cards)
    {
        var ids = picked.ToHashSet(StringComparer.Ordinal);
        if (ids.Count is < MinPackageCardCount or > MaxPackageCardCount || ids.Any(id => !cards.ContainsKey(id)))
            return false;
        var available = GenericCandidates(ids, cards);
        var deficits = GenericRarityDeficits(ids, cards);
        // Fit the rarity guarantees into the transition pack. After that every
        // individual choice is legal, and the last round still needs ten cards.
        var minimumSupply = GenericTotalCardCount - GenericCardPickCount + GenericCardOfferCount;
        return available.Count >= minimumSupply && deficits.Sum() <= TransitionPackageSize
            && RequiredRarities.Select((rarity, index) => available.Count(id => cards[id].Rarity == rarity) >= deficits[index]).All(enough => enough);
    }

    private static bool TryPrepareSingleCardOffer(RewardPoolDraftState state, ref ulong randomState,
        out RewardPoolDraftState next, out List<string> offerIds)
    {
        next = state;
        offerIds = [];
        var cards = MainCardsById();
        var available = GenericCandidates(state.MainCardIds, cards);
        if (state.SelectedGenericCardIds.Count == 0)
        {
            if (!CanOfferSingleCardDraft(state.MainCardIds, cards))
                return false;
            var offers = CreateTransitionOffers(state.MainCardIds, available, cards, ref randomState);
            next = state with { Stage = RewardPoolDraftStage.TransitionPackages, GenericPackageOffers = offers };
            offerIds = offers.Select(offer => offer.Id).ToList();
            return offers.Count == TransitionOptionIds.Length;
        }

        if (!CanFinishSingleCardRounds(state, available.Count, cards))
            return false;
        offerIds = CreateSingleCardOffer(state, available, cards, ref randomState);
        next = state with { Stage = RewardPoolDraftStage.GenericCards, GenericPackageOffers = [] };
        return true;
    }

    private static bool CanFinishSingleCardRounds(RewardPoolDraftState state, int available,
        IReadOnlyDictionary<string, CardModel> cards)
    {
        var picksRemaining = GenericTotalCardCount - state.SelectedGenericCardIds.Count;
        return picksRemaining >= GenericCardPickCount
            && available >= picksRemaining - GenericCardPickCount + GenericCardOfferCount
            && HasRequiredRarities(state.MainCardIds, cards);
    }

    private static IReadOnlyList<RewardPoolDraftGenericOffer> CreateTransitionOffers(IReadOnlyList<string> picked,
        IReadOnlyList<string> available, IReadOnlyDictionary<string, CardModel> cards, ref ulong randomState)
    {
        var deficits = GenericRarityDeficits(picked, cards);
        var offers = new List<RewardPoolDraftGenericOffer>(TransitionOptionIds.Length);
        foreach (var optionId in TransitionOptionIds)
        {
            var shuffled = available.ToList();
            Shuffle(shuffled, ref randomState);
            var ordered = shuffled.OrderByDescending(id => TransitionPreference(optionId, cards[id])).ToArray();
            var chosen = new List<string>(TransitionPackageSize);
            for (var index = 0; index < RequiredRarities.Length; index++)
                chosen.AddRange(ordered.Where(id => cards[id].Rarity == RequiredRarities[index]).Take(deficits[index]));
            var requiredCount = chosen.Count;
            chosen.AddRange(ordered.Except(chosen, StringComparer.Ordinal).Take(TransitionPackageSize - requiredCount).ToArray());
            if (HasSameTransitionCards(offers, chosen))
            {
                // At minimum supply there are still multiple substitutes for
                // the last non-required card, without changing the rarity quota.
                var spare = ordered.FirstOrDefault(id => !chosen.Contains(id, StringComparer.Ordinal)
                    && !HasSameTransitionCards(offers, chosen.Take(TransitionPackageSize - 1).Append(id)));
                if (spare is null || requiredCount >= TransitionPackageSize)
                    return offers;
                chosen[^1] = spare;
            }
            offers.Add(new RewardPoolDraftGenericOffer(optionId, chosen.ToArray()));
        }
        return offers.ToArray();
    }

    private static bool HasSameTransitionCards(IEnumerable<RewardPoolDraftGenericOffer> offers, IEnumerable<string> cards)
    {
        var ids = cards.ToHashSet(StringComparer.Ordinal);
        return offers.Any(offer => ids.SetEquals(offer.CardIds));
    }

    private static bool TransitionPreference(string optionId, CardModel card) => optionId switch
    {
        "transition.start" => card.Type == CardType.Skill
            && (card.DynamicVars.ContainsKey("Cards") || card.DynamicVars.ContainsKey("Energy")),
        "transition.defense" => card.Type == CardType.Attack || card.GainsBlock
            || card.DynamicVars.ContainsKey("Weak"),
        "transition.sustain" => card.Type == CardType.Power
            || card.DynamicVars.ContainsKey("Heal") || card.DynamicVars.ContainsKey("Regeneration"),
        _ => false
    };

    private static List<string> CreateSingleCardOffer(RewardPoolDraftState state, List<string> available,
        IReadOnlyDictionary<string, CardModel> cards, ref ulong randomState)
    {
        var pickedPackages = state.SelectedPackageIds.ToHashSet(StringComparer.Ordinal);
        var packages = RewardPoolConstructionCatalog.Packages.Where(package => pickedPackages.Contains(package.Id)).ToArray();
        var related = packages.SelectMany(package => package.CardIds.Concat(package.AccessoryCardIds))
            .ToHashSet(StringComparer.Ordinal);
        var origins = packages.Select(package => package.Origin).ToHashSet();
        Shuffle(available, ref randomState);

        var offer = available.Where(related.Contains).Take(3).ToList();
        var other = available.Except(offer, StringComparer.Ordinal)
            .Where(id => RewardPoolCatalog.TryGetMainCardOrigin(cards[id], out var origin) && !origins.Contains(origin))
            .OrderByDescending(id => cards[id].Rarity == CardRarity.Rare).Take(2).ToArray();
        offer.AddRange(available.Except(offer.Concat(other), StringComparer.Ordinal)
            .Take(GenericCardOfferCount - offer.Count - other.Length).ToArray());
        offer.AddRange(other);
        Shuffle(offer, ref randomState);
        return offer;
    }

    private static bool IsValidTransitionOffer(IReadOnlyList<string> picked, RewardPoolDraftGenericOffer offer,
        IReadOnlyDictionary<string, CardModel> cards)
    {
        if (!TransitionOptionIds.Contains(offer.Id, StringComparer.Ordinal) || !ValidIds(offer.CardIds)
            || offer.CardIds.Count != TransitionPackageSize)
            return false;
        var available = GenericCandidates(picked, cards).ToHashSet(StringComparer.Ordinal);
        return offer.CardIds.All(available.Contains) && HasRequiredRarities(picked.Concat(offer.CardIds), cards)
            && available.Count - offer.CardIds.Count >= DefaultGenericRoundCount * GenericCardPickCount
                - GenericCardPickCount + GenericCardOfferCount;
    }

    private static bool ValidateSingleCardCatalog(RewardPoolDraftState state, IReadOnlySet<string> packageCards,
        IReadOnlyDictionary<string, CardModel> cards)
    {
        if (state.Stage == RewardPoolDraftStage.TransitionPackages)
            return state.GenericPackageOffers.All(offer => IsValidTransitionOffer(state.MainCardIds, offer, cards));
        if (state.SelectedGenericCardIds.Count < TransitionPackageSize)
            return state.Stage == RewardPoolDraftStage.Packages;
        var transition = state.SelectedGenericCardIds.Take(TransitionPackageSize);
        if (!HasRequiredRarities(packageCards.Concat(transition), cards))
            return false;
        if (state.Stage != RewardPoolDraftStage.GenericCards)
            return true;
        var available = GenericCandidates(state.MainCardIds, cards).ToHashSet(StringComparer.Ordinal);
        return state.OfferIds.All(available.Contains) && CanFinishSingleCardRounds(state, available.Count, cards);
    }

    private static bool ValidateSingleCardStructure(RewardPoolDraftState state, out string error)
    {
        error = "The saved draft structure is invalid.";
        var genericCount = state.SelectedGenericCardIds.Count;
        var packageCount = state.MainCardIds.Count - genericCount;
        if (state.GenericRoundCount != DefaultGenericRoundCount || state.GenericCardsPerRound != GenericCardPickCount
            || packageCount > MaxPackageCardCount
            || (state.PackagePicks == PackageRoundCount && packageCount < MinPackageCardCount)
            || (genericCount == 0
                ? !string.IsNullOrEmpty(state.SelectedGenericPackageId)
                : genericCount < TransitionPackageSize || (genericCount - TransitionPackageSize) % GenericCardPickCount != 0
                    || !TransitionOptionIds.Contains(state.SelectedGenericPackageId, StringComparer.Ordinal)))
            return false;

        var extraFull = state.ExtraCardIds.Count == RewardPoolCatalog.SelectableExtraCardCount;
        var valid = state.Stage switch
        {
            RewardPoolDraftStage.Packages => state.PackagePicks < PackageRoundCount && genericCount == 0
                && state.ExtraCardIds.Count == 0 && state.OfferIds.Count == 3,
            RewardPoolDraftStage.TransitionPackages => state.PackagePicks == PackageRoundCount && genericCount == 0
                && state.ExtraCardIds.Count == 0 && state.OfferIds.SequenceEqual(TransitionOptionIds, StringComparer.Ordinal),
            RewardPoolDraftStage.GenericCards => state.PackagePicks == PackageRoundCount
                && genericCount >= TransitionPackageSize && genericCount < GenericTotalCardCount
                && state.ExtraCardIds.Count == 0 && state.OfferIds.Count == GenericCardOfferCount
                && !state.OfferIds.Intersect(state.MainCardIds, StringComparer.Ordinal).Any(),
            RewardPoolDraftStage.Extra => state.PackagePicks == PackageRoundCount && state.GenericPackagePicked
                && !extraFull && state.OfferIds.Count == 3,
            RewardPoolDraftStage.Complete or RewardPoolDraftStage.Consumed => state.PackagePicks == PackageRoundCount
                && state.GenericPackagePicked && extraFull && state.OfferIds.Count == 0,
            _ => false
        };
        if (!valid)
            return false;

        if (state.Stage == RewardPoolDraftStage.TransitionPackages)
        {
            var offers = state.GenericPackageOffers;
            if (!state.OfferIds.SequenceEqual(offers.Select(offer => offer.Id), StringComparer.Ordinal)
                || offers.Any(offer => !ValidIds(offer.CardIds) || offer.CardIds.Count != TransitionPackageSize
                    || offer.CardIds.Intersect(state.MainCardIds, StringComparer.Ordinal).Any())
                || offers.Select(offer => string.Join("\n", offer.CardIds.OrderBy(id => id, StringComparer.Ordinal)))
                    .Distinct(StringComparer.Ordinal).Count() != TransitionOptionIds.Length)
                return false;
        }
        else if (state.GenericPackageOffers.Count != 0)
            return false;
        error = string.Empty;
        return true;
    }
}
