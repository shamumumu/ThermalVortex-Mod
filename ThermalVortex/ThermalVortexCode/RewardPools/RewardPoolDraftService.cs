using RandomNumberGenerator = System.Security.Cryptography.RandomNumberGenerator;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

public enum RewardPoolDraftStage
{
    Packages,
    Main,
    Extra,
    Complete,
    Consumed,
    GenericPackages
}

public sealed record RewardPoolDraftGenericOffer(string Id, IReadOnlyList<string> CardIds);
public sealed record RewardPoolDraftThemeOffer(string Id, IReadOnlyList<string> CardIds);

/// <summary>A persisted choice session. Lists contain selectable IDs, never fixed starter entries.</summary>
public sealed record RewardPoolDraftState
{
    public string SessionId { get; init; } = string.Empty;
    public int RulesVersion { get; init; }
    public string PackageVersion { get; init; } = string.Empty;
    public bool IsLegacy { get; init; }
    public RewardPoolDraftStage Stage { get; init; }
    public int PackagePicks { get; init; }
    public IReadOnlyList<string> MainCardIds { get; init; } = [];
    public IReadOnlyList<string> ExtraCardIds { get; init; } = [];
    public IReadOnlyList<string> OfferIds { get; init; } = [];
    public IReadOnlyList<string> SelectedPackageIds { get; init; } = [];
    public bool PackageVariantsEnabled { get; init; }
    public int PackageVariantVersion { get; init; }
    public IReadOnlyList<RewardPoolDraftThemeOffer> PackageOffers { get; init; } = [];
    public IReadOnlyList<RewardPoolDraftThemeOffer> SelectedPackageOffers { get; init; } = [];
    public bool GenericPackagePicked { get; init; }
    public string SelectedGenericPackageId { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectedGenericCardIds { get; init; } = [];
    public IReadOnlyList<RewardPoolDraftGenericOffer> GenericPackageOffers { get; init; } = [];
    // Missing fields in schema 2 retain that session's single twenty-card round.
    public int GenericRoundCount { get; init; } = 1;
    public int GenericCardsPerRound { get; init; } = 20;
    public int GenericRoundsCompleted => GenericCardsPerRound > 0
        ? SelectedGenericCardIds.Count / GenericCardsPerRound : 0;

    internal RewardPoolDraftState Snapshot() => this with
    {
        MainCardIds = Array.AsReadOnly(MainCardIds.ToArray()),
        ExtraCardIds = Array.AsReadOnly(ExtraCardIds.ToArray()),
        OfferIds = Array.AsReadOnly(OfferIds.ToArray()),
        SelectedPackageIds = Array.AsReadOnly(SelectedPackageIds.ToArray()),
        PackageOffers = Array.AsReadOnly(PackageOffers.Select(offer =>
            new RewardPoolDraftThemeOffer(offer.Id, Array.AsReadOnly(offer.CardIds.ToArray()))).ToArray()),
        SelectedPackageOffers = Array.AsReadOnly(SelectedPackageOffers.Select(offer =>
            new RewardPoolDraftThemeOffer(offer.Id, Array.AsReadOnly(offer.CardIds.ToArray()))).ToArray()),
        SelectedGenericCardIds = Array.AsReadOnly(SelectedGenericCardIds.ToArray()),
        GenericPackageOffers = Array.AsReadOnly(GenericPackageOffers.Select(offer =>
            new RewardPoolDraftGenericOffer(offer.Id, Array.AsReadOnly(offer.CardIds.ToArray()))).ToArray())
    };
}

/// <summary>
/// Draft randomness belongs to this persisted session, never the run RNG. An offer is
/// drawn once and durably committed with the preceding pick before it becomes visible.
/// </summary>
public static class RewardPoolDraftService
{
    public const int PackageRoundCount = 3;
    public const int GenericTotalCardCount = 20;
    // New sessions snapshot this default; supported round counts divide the fixed total.
    public const int DefaultGenericRoundCount = 4;
    public const int GenericPackageSize = GenericTotalCardCount / DefaultGenericRoundCount;
    public const int MinPackageCardCount = 45;
    public const int MaxPackageCardCount = 65;
    private const string FileName = "ThermalVortex.reward_pool_draft.json";
    private static readonly object StateLock = new();
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly CardRarity[] RequiredRarities =
        [CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare];
    private static DraftPayload _payload;
    private static bool _initialized;
    private static string _path;
    private static string _committedJson;
    private static string _loadError;
    private static string _lastError;

    public static bool IsSelected
    {
        get { lock (StateLock) { InitializeLocked(); return _payload?.Selected == true; } }
    }

    public static string LoadError
    {
        get { lock (StateLock) { InitializeLocked(); return _loadError ?? string.Empty; } }
    }

    public static string LastError
    {
        get { lock (StateLock) { InitializeLocked(); return _lastError ?? string.Empty; } }
    }

    public static RewardPoolDraftState GetState()
    {
        lock (StateLock)
        {
            InitializeLocked();
            return _payload?.State.Snapshot();
        }
    }

    public static bool TryStartNew(out string error) => TryStartNew(DefaultGenericRoundCount, out error);

    public static bool TryStartNew(int genericRoundCount, out string error)
    {
        lock (StateLock)
        {
            if (!CanWriteLocked(out error))
                return false;
            if (genericRoundCount <= 0 || GenericTotalCardCount % genericRoundCount != 0)
            {
                error = "The generic round count must be a positive divisor of twenty.";
                return Fail(error);
            }
            try
            {
                if (RewardPoolConstructionCatalog.PackageVariantVersion <= 0)
                {
                    error = "Theme core and accessory profiles are unavailable.";
                    return Fail(error);
                }
                var candidate = new DraftPayload
                {
                    SchemaVersion = 4,
                    Selected = true,
                    RandomState = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))),
                    State = new RewardPoolDraftState
                    {
                        SessionId = Guid.NewGuid().ToString("N"),
                        RulesVersion = RewardPoolSizePolicy.CurrentRulesVersion,
                        PackageVersion = RewardPoolConstructionCatalog.PackageVersion,
                        PackageVariantsEnabled = true,
                        PackageVariantVersion = RewardPoolConstructionCatalog.PackageVariantVersion,
                        GenericRoundCount = genericRoundCount,
                        GenericCardsPerRound = GenericTotalCardCount / genericRoundCount
                    }
                };
                if (!TryPrepareNextOffer(candidate, out error))
                    return Fail(error);
                return CommitLocked(candidate, out error);
            }
            catch (Exception ex)
            {
                error = $"The draft catalog could not be prepared: {ex.Message}";
                return Fail(error);
            }
        }
    }

    public static bool TryChoose(string optionId, out string error)
    {
        lock (StateLock)
        {
            if (!CanWriteLocked(out error))
                return false;
            if (_payload?.State.IsLegacy == true)
            {
                error = "This draft uses earlier rules. View it, copy its cards to Free mode, or start a new draft.";
                return Fail(error);
            }
            if (_payload is null || !_payload.State.OfferIds.Contains(optionId, StringComparer.Ordinal))
            {
                error = "This choice is not in the current saved offer.";
                return Fail(error);
            }

            try
            {
                var candidate = _payload.Snapshot();
                var state = candidate.State;
                if (!ValidateCurrentCatalog(state, out error))
                    return Fail(error);
                switch (state.Stage)
                {
                    case RewardPoolDraftStage.Packages:
                    {
                        var package = EligiblePackageDefinitions().FirstOrDefault(pack => pack.Id == optionId);
                        var packageOffer = state.PackageVariantsEnabled
                            ? state.PackageOffers.FirstOrDefault(offer => offer.Id == optionId) : null;
                        var pickedCards = state.PackageVariantsEnabled ? packageOffer?.CardIds : package?.CardIds;
                        var cards = MainCardsById();
                        if (package is null || pickedCards is null
                            || (state.PackageVariantsEnabled && !IsValidThemeOffer(packageOffer, package, cards))
                            || !CanPickPackage(state, package, EligiblePackageDefinitions(cards), cards, pickedCards))
                        {
                            error = "This package can no longer complete a legal draft. Start a new draft.";
                            return Fail(error);
                        }
                        state = state with
                        {
                            PackagePicks = state.PackagePicks + 1,
                            SelectedPackageIds = state.SelectedPackageIds.Append(package.Id).ToArray(),
                            MainCardIds = AddUnique(state.MainCardIds, pickedCards),
                            SelectedPackageOffers = state.PackageVariantsEnabled
                                ? state.SelectedPackageOffers.Append(new RewardPoolDraftThemeOffer(package.Id, pickedCards.ToArray())).ToArray()
                                : state.SelectedPackageOffers
                        };
                        break;
                    }
                    case RewardPoolDraftStage.GenericPackages:
                    {
                        var offer = state.GenericPackageOffers.FirstOrDefault(item => item.Id == optionId);
                        if (offer is null || !IsValidGenericRoundOffer(state, offer, MainCardsById()))
                        {
                            error = "This generic package can no longer complete a legal draft. Start a new draft.";
                            return Fail(error);
                        }
                        state = state with
                        {
                            MainCardIds = AddUnique(state.MainCardIds, offer.CardIds),
                            GenericPackagePicked = state.SelectedGenericCardIds.Count + offer.CardIds.Count == GenericTotalCardCount,
                            SelectedGenericPackageId = offer.Id,
                            SelectedGenericCardIds = state.SelectedGenericCardIds.Concat(offer.CardIds).ToArray(),
                            GenericPackageOffers = []
                        };
                        break;
                    }
                    case RewardPoolDraftStage.Extra:
                        if (!RewardPoolCatalog.GetSelectableExtraCandidates().Any(card => RewardPoolCatalog.GetId(card) == optionId)
                            || state.ExtraCardIds.Contains(optionId, StringComparer.Ordinal))
                        {
                            error = "This extra card is no longer available. Start a new draft.";
                            return Fail(error);
                        }
                        state = state with { ExtraCardIds = state.ExtraCardIds.Append(optionId).ToArray() };
                        break;
                    default:
                        error = "This draft has no remaining choices.";
                        return Fail(error);
                }

                candidate.State = state with { OfferIds = [], PackageOffers = [] };
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

    public static bool TrySelect(out string error)
    {
        lock (StateLock)
        {
            if (!CanWriteLocked(out error))
                return false;
            if (_payload is null || _payload.State.IsLegacy || _payload.State.Stage == RewardPoolDraftStage.Consumed)
            {
                error = "Start a new draft before selecting this mode.";
                return Fail(error);
            }
            if (_payload.Selected)
                return true;
            var candidate = _payload.Snapshot();
            candidate.Selected = true;
            return CommitLocked(candidate, out error);
        }
    }

    public static void Deselect()
    {
        lock (StateLock)
        {
            if (!CanWriteLocked(out _) || _payload?.Selected != true)
                return;
            var candidate = _payload.Snapshot();
            candidate.Selected = false;
            CommitLocked(candidate, out _);
        }
    }

    /// <summary>
    /// Keeps the previous draft selection and files when the following preset
    /// selection cannot be saved. Callers hold the preset lock before this lock.
    /// </summary>
    internal static bool TryDeselectForSelection(Func<bool> commitSelection, out string error)
    {
        ArgumentNullException.ThrowIfNull(commitSelection);
        lock (StateLock)
        {
            InitializeLocked();
            // An unreadable, unselected draft does not need to be rewritten to
            // select a healthy preset or the explicit standard-mode fallback.
            if (_payload?.Selected != true)
            {
                if (!TryCommitSelection(commitSelection, out error))
                    return Fail(error);
                return true;
            }
            if (!CanWriteLocked(out error))
                return false;
            var candidate = _payload.Snapshot();
            candidate.Selected = false;
            return CommitLocked(candidate, out error, commitSelection);
        }
    }

    public static bool TryGetReadyDefinition(
        out RewardPoolDefinition definition,
        out RewardPoolValidationResult validation)
    {
        lock (StateLock)
        {
            InitializeLocked();
            definition = RewardPoolDefinition.Standard;
            if (!string.IsNullOrEmpty(_loadError))
            {
                validation = Invalid(_loadError);
                return false;
            }
            if (_payload?.Selected != true || _payload.State.IsLegacy || _payload.State.Stage != RewardPoolDraftStage.Complete)
            {
                validation = Invalid("Complete and select a fresh draft before starting a run.");
                return false;
            }
            try
            {
                if (!ValidateCurrentCatalog(_payload.State, out var error))
                {
                    validation = Invalid(error);
                    return false;
                }
                if (!RewardPoolCatalog.CreateManualDefinition(
                        _payload.State.MainCardIds,
                        _payload.State.ExtraCardIds,
                        out definition,
                        out validation))
                    return false;

                definition = definition.WithConstruction(new RewardPoolConstructionMetadata
                {
                    Method = RewardPoolConstructionMethod.Draft,
                    DraftSessionId = _payload.State.SessionId
                });
                validation = definition.Validate();
                return validation.IsValid;
            }
            catch (Exception ex)
            {
                validation = Invalid($"The draft catalog is unavailable: {ex.Message}");
                return false;
            }
        }
    }

    public static bool TryMarkConsumed(string sessionId, out string error)
    {
        lock (StateLock)
        {
            if (!CanWriteLocked(out error))
                return false;
            if (_payload is null || _payload.State.IsLegacy || !string.Equals(_payload.State.SessionId, sessionId, StringComparison.Ordinal))
            {
                error = "The launched draft does not match the saved draft session.";
                return Fail(error);
            }
            if (_payload.State.Stage == RewardPoolDraftStage.Consumed)
                return true;
            if (_payload.State.Stage != RewardPoolDraftStage.Complete)
            {
                error = "An unfinished draft cannot be consumed.";
                return Fail(error);
            }
            var candidate = _payload.Snapshot();
            candidate.Selected = true;
            candidate.State = candidate.State with { Stage = RewardPoolDraftStage.Consumed, OfferIds = [] };
            return CommitLocked(candidate, out error);
        }
    }

    private static bool TryPrepareNextOffer(DraftPayload candidate, out string error)
    {
        error = string.Empty;
        var state = candidate.State;
        var randomState = candidate.RandomState;
        List<string> offer;
        if (state.PackagePicks < PackageRoundCount)
        {
            var cards = MainCardsById();
            var definitions = EligiblePackageDefinitions(cards);
            var packages = definitions.Where(package => CanPickPackage(state, package, definitions, cards)).ToList();
            Shuffle(packages, ref randomState);
            var selected = new List<RewardPoolPackage>();
            foreach (var package in packages)
            {
                if (selected.All(existing => existing.Origin != package.Origin))
                    selected.Add(package);
                if (selected.Count == 3)
                    break;
            }
            foreach (var package in packages)
            {
                if (selected.Count == 3)
                    break;
                if (selected.All(existing => existing.Id != package.Id))
                    selected.Add(package);
            }
            offer = selected.Select(package => package.Id).ToList();
            var themeOffers = new List<RewardPoolDraftThemeOffer>();
            if (state.PackageVariantsEnabled)
                foreach (var package in selected)
                    themeOffers.Add(CreateThemeOffer(state, package, definitions, cards, ref randomState));
            state = state with { Stage = RewardPoolDraftStage.Packages, GenericPackageOffers = [], PackageOffers = themeOffers.ToArray() };
        }
        else if (!state.GenericPackagePicked)
        {
            if (!TryCreateGenericRoundOffers(state, MainCardsById(), ref randomState, out var genericOffers))
            {
                error = "Three distinct generic packages cannot complete this draft.";
                return false;
            }
            offer = genericOffers.Select(item => item.Id).ToList();
            state = state with { Stage = RewardPoolDraftStage.GenericPackages, GenericPackageOffers = genericOffers };
        }
        else if (state.ExtraCardIds.Count < RewardPoolCatalog.SelectableExtraCardCount)
        {
            var candidates = RewardPoolCatalog.GetSelectableExtraCandidates().Select(RewardPoolCatalog.GetId)
                .Where(id => !state.ExtraCardIds.Contains(id, StringComparer.Ordinal))
                .OrderBy(id => id, StringComparer.Ordinal).ToList();
            if (candidates.Count < RewardPoolCatalog.SelectableExtraCardCount - state.ExtraCardIds.Count)
            {
                error = "The extra-card catalog cannot complete this draft.";
                return false;
            }
            Shuffle(candidates, ref randomState);
            offer = candidates.Take(3).ToList();
            state = state with { Stage = RewardPoolDraftStage.Extra, GenericPackageOffers = [] };
        }
        else
        {
            if (!RewardPoolCatalog.CreateManualDefinition(state.MainCardIds, state.ExtraCardIds, out _, out var validation))
            {
                error = validation.Message;
                return false;
            }
            candidate.State = state with { Stage = RewardPoolDraftStage.Complete, OfferIds = [], GenericPackageOffers = [] };
            return true;
        }

        if (offer.Count != 3)
        {
            error = "Three available choices cannot complete a legal draft.";
            return false;
        }
        candidate.State = state with { OfferIds = offer.ToArray() };
        candidate.RandomState = randomState;
        return true;
    }

    private static bool CanPickPackage(RewardPoolDraftState state, RewardPoolPackage package)
    {
        var cards = MainCardsById();
        return CanPickPackage(state, package, EligiblePackageDefinitions(cards), cards);
    }

    private static bool CanPickPackage(RewardPoolDraftState state, RewardPoolPackage package,
        IReadOnlyList<RewardPoolPackage> packages, IReadOnlyDictionary<string, CardModel> cards,
        IReadOnlyList<string> members = null)
    {
        if (state.SelectedPackageIds.Contains(package.Id, StringComparer.Ordinal))
            return false;
        return CanCompletePackages(AddUnique(state.MainCardIds, members ?? package.CardIds),
            state.SelectedPackageIds.Append(package.Id).ToHashSet(StringComparer.Ordinal),
            packages, PackageRoundCount - state.PackagePicks - 1, cards,
            state.GenericRoundCount, state.GenericCardsPerRound);
    }

    private static RewardPoolDraftThemeOffer CreateThemeOffer(RewardPoolDraftState state, RewardPoolPackage package,
        IReadOnlyList<RewardPoolPackage> packages, IReadOnlyDictionary<string, CardModel> cards, ref ulong randomState)
    {
        var count = package.CardIds.Count - package.CoreCardIds.Count;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var accessories = package.AccessoryCardIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
            Shuffle(accessories, ref randomState);
            var members = package.CoreCardIds.Concat(accessories.Take(count)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var offer = new RewardPoolDraftThemeOffer(package.Id, members);
            if (IsValidThemeOffer(offer, package, cards) && CanPickPackage(state, package, packages, cards, members))
                return offer;
        }
        // The original package remains a complete, fixed fallback, never a trimmed variant.
        return new RewardPoolDraftThemeOffer(package.Id, package.CardIds.ToArray());
    }

    private static bool IsValidThemeProfile(RewardPoolDraftThemeOffer offer, RewardPoolPackage package)
    {
        if (offer is null || package is null || package.IsSupport || offer.Id != package.Id
            || !ValidIds(offer.CardIds) || offer.CardIds.Count != package.CardIds.Count
            || package.CoreCardIds.Count == 0 || package.CoreCardIds.Any(id => !offer.CardIds.Contains(id, StringComparer.Ordinal)))
            return false;
        var allowed = package.CoreCardIds.Concat(package.AccessoryCardIds).ToHashSet(StringComparer.Ordinal);
        return offer.CardIds.All(allowed.Contains);
    }

    private static bool IsValidThemeOffer(RewardPoolDraftThemeOffer offer, RewardPoolPackage package,
        IReadOnlyDictionary<string, CardModel> cards) => IsValidThemeProfile(offer, package)
        && offer.CardIds.All(id => cards.TryGetValue(id, out var card) && RequiredRarities.Contains(card.Rarity)
            && RewardPoolCatalog.TryGetMainCardOrigin(card, out var origin) && origin == package.Origin);

    // Require three viable options at every remaining round, not just one path
    // to a final count. Every displayed choice therefore preserves a full draft.
    private static bool CanCompletePackages(IReadOnlyList<string> selected, HashSet<string> selectedPackages,
        IReadOnlyList<RewardPoolPackage> packages, int rounds, IReadOnlyDictionary<string, CardModel> cards,
        int genericRounds, int genericSize)
    {
        if (selected.Count > MaxPackageCardCount)
            return false;
        if (rounds == 0)
            return CanFinishWithGeneric(selected, cards)
                && (genericRounds == 1 || CanFinishGenericRounds(selected, genericRounds, genericSize, cards));
        var viable = 0;
        foreach (var package in packages)
        {
            if (!selectedPackages.Add(package.Id))
                continue;
            var possible = CanCompletePackages(AddUnique(selected, package.CardIds), selectedPackages,
                packages, rounds - 1, cards, genericRounds, genericSize);
            selectedPackages.Remove(package.Id);
            if (possible && ++viable == 3)
                return true;
        }
        return false;
    }

    private static bool CanFinishWithGeneric(IEnumerable<string> selected, IReadOnlyDictionary<string, CardModel> cards)
    {
        var ids = selected.ToHashSet(StringComparer.Ordinal);
        if (ids.Count is < MinPackageCardCount or > MaxPackageCardCount || ids.Any(id => !cards.ContainsKey(id)))
            return false;
        var available = GenericCandidates(ids, cards);
        // At least two spare cards keep three distinct twenty-card options possible.
        if (available.Count < GenericTotalCardCount + 2)
            return false;
        var shortages = 0;
        foreach (var rarity in RequiredRarities)
        {
            var missing = Math.Max(0, RewardPoolCatalog.MinimumPerRewardRarity - ids.Count(id => cards[id].Rarity == rarity));
            if (available.Count(id => cards[id].Rarity == rarity) < missing)
                return false;
            shortages += missing;
        }
        return shortages <= GenericTotalCardCount;
    }

    private static List<string> GenericCandidates(IEnumerable<string> selected, IReadOnlyDictionary<string, CardModel> cards)
    {
        var excluded = selected.ToHashSet(StringComparer.Ordinal);
        return RewardPoolConstructionCatalog.GenericCardIds.Where(id => cards.ContainsKey(id) && !excluded.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    private static bool TryCreateGenericOffers(IReadOnlyList<string> selected, IReadOnlyDictionary<string, CardModel> cards,
        ref ulong randomState, out IReadOnlyList<RewardPoolDraftGenericOffer> offers)
    {
        var result = new List<RewardPoolDraftGenericOffer>();
        offers = result;
        if (!CanFinishWithGeneric(selected, cards))
            return false;
        var available = GenericCandidates(selected, cards);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var attempt = 0; attempt < 64 && result.Count < 3; attempt++)
        {
            var chosen = new List<string>();
            foreach (var rarity in RequiredRarities)
            {
                var missing = Math.Max(0, RewardPoolCatalog.MinimumPerRewardRarity - selected.Count(id => cards[id].Rarity == rarity));
                var matching = available.Where(id => cards[id].Rarity == rarity).ToList();
                Shuffle(matching, ref randomState);
                chosen.AddRange(matching.Take(missing));
            }
            var remainder = available.Except(chosen, StringComparer.Ordinal).ToList();
            Shuffle(remainder, ref randomState);
            chosen.AddRange(remainder.Take(GenericTotalCardCount - chosen.Count));
            chosen.Sort(StringComparer.Ordinal);
            if (seen.Add(string.Join("\n", chosen)))
                result.Add(new RewardPoolDraftGenericOffer("generic." + (result.Count + 1), chosen.ToArray()));
        }
        offers = result.ToArray();
        return result.Count == 3;
    }

    private static bool IsValidGenericOffer(IEnumerable<string> selected, RewardPoolDraftGenericOffer offer,
        IReadOnlyDictionary<string, CardModel> cards)
    {
        var ids = selected.ToHashSet(StringComparer.Ordinal);
        var generic = RewardPoolConstructionCatalog.GenericCardIds.ToHashSet(StringComparer.Ordinal);
        return offer is not null && ValidIds(offer.CardIds) && offer.CardIds.Count == GenericTotalCardCount
            && offer.CardIds.All(id => cards.ContainsKey(id) && generic.Contains(id) && !ids.Contains(id))
            && HasRequiredRarities(ids.Concat(offer.CardIds), cards);
    }

    private static bool TryCreateGenericRoundOffers(RewardPoolDraftState state,
        IReadOnlyDictionary<string, CardModel> cards, ref ulong randomState,
        out IReadOnlyList<RewardPoolDraftGenericOffer> offers)
    {
        // Preserve schema 2's generator and RNG consumption, including its saved twenty-card offers.
        if (state.GenericRoundCount == 1)
            return TryCreateGenericOffers(state.MainCardIds, cards, ref randomState, out offers);

        var result = new List<RewardPoolDraftGenericOffer>();
        offers = result;
        var roundsLeft = state.GenericRoundCount - state.GenericRoundsCompleted;
        if (!CanFinishGenericRounds(state.MainCardIds, roundsLeft, state.GenericCardsPerRound, cards))
            return false;
        var available = GenericCandidates(state.MainCardIds, cards);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(IReadOnlyList<string> chosen)
        {
            var ids = chosen.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var offer = new RewardPoolDraftGenericOffer(
                GenericOptionId(state, state.GenericRoundsCompleted + 1, result.Count + 1), ids);
            if (IsValidGenericRoundOffer(state, offer, cards) && seen.Add(string.Join("\n", ids)))
                result.Add(offer);
        }
        for (var attempt = 0; attempt < 64 && result.Count < 3; attempt++)
        {
            var shuffled = available.ToList();
            Shuffle(shuffled, ref randomState);
            Add(shuffled.Take(state.GenericCardsPerRound).ToArray());
        }
        // Random sampling must not strand a legal sparse pool. Enumerate only enough
        // concrete sets from viable rarity counts to fill the three distinct choices.
        if (result.Count < 3)
        {
            var groups = Enumerable.Range(0, 4)
                .Select(index => available.Where(id => GenericRarityIndex(cards[id]) == index).ToArray()).ToArray();
            var counts = groups.Select(group => group.Length).ToArray();
            var deficits = GenericRarityDeficits(state.MainCardIds, cards);
            var memo = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var picked in GenericCountChoices(counts, state.GenericCardsPerRound))
            {
                var remaining = counts.Zip(picked, (count, take) => count - take).ToArray();
                var missing = deficits.Select((count, index) => Math.Max(0, count - picked[index])).ToArray();
                if (!CanFinishGenericCounts(remaining, missing, roundsLeft - 1, state.GenericCardsPerRound, memo))
                    continue;
                foreach (var chosen in GenericSelections(groups, picked, 0))
                {
                    Add(chosen);
                    if (result.Count == 3)
                        break;
                }
                if (result.Count == 3)
                    break;
            }
        }
        offers = result.ToArray();
        return result.Count == 3;
    }

    private static string GenericOptionId(RewardPoolDraftState state, int round, int slot) =>
        state.GenericRoundCount == 1 ? $"generic.{slot}" : $"generic.{round}.{slot}";

    private static bool IsGenericOptionId(RewardPoolDraftState state, string id, int round) =>
        Enumerable.Range(1, 3).Any(slot => id == GenericOptionId(state, round, slot));

    private static bool IsValidGenericRoundOffer(RewardPoolDraftState state, RewardPoolDraftGenericOffer offer,
        IReadOnlyDictionary<string, CardModel> cards)
    {
        if (state.GenericRoundCount == 1)
            return IsValidGenericOffer(state.MainCardIds, offer, cards);
        var generic = RewardPoolConstructionCatalog.GenericCardIds.ToHashSet(StringComparer.Ordinal);
        if (offer is null || !ValidIds(offer.CardIds) || offer.CardIds.Count != state.GenericCardsPerRound
            || !IsGenericOptionId(state, offer.Id, state.GenericRoundsCompleted + 1)
            || offer.CardIds.Any(id => !cards.ContainsKey(id) || !generic.Contains(id)
                || state.MainCardIds.Contains(id, StringComparer.Ordinal)))
            return false;
        return CanFinishGenericRounds(state.MainCardIds.Concat(offer.CardIds),
            state.GenericRoundCount - state.GenericRoundsCompleted - 1, state.GenericCardsPerRound, cards);
    }

    private static int GenericRarityIndex(CardModel card)
    {
        for (var index = 0; index < RequiredRarities.Length; index++)
            if (card.Rarity == RequiredRarities[index])
                return index;
        return RequiredRarities.Length;
    }

    private static int[] GenericRarityDeficits(IEnumerable<string> selected, IReadOnlyDictionary<string, CardModel> cards)
    {
        var ids = selected.ToHashSet(StringComparer.Ordinal);
        return RequiredRarities.Select(rarity => Math.Max(0, RewardPoolCatalog.MinimumPerRewardRarity
            - ids.Count(id => cards[id].Rarity == rarity))).ToArray();
    }

    private static bool CanFinishGenericRounds(IEnumerable<string> selected, int rounds, int size,
        IReadOnlyDictionary<string, CardModel> cards)
    {
        var ids = selected.ToHashSet(StringComparer.Ordinal);
        if (ids.Any(id => !cards.ContainsKey(id)))
            return false;
        var available = GenericCandidates(ids, cards);
        var counts = Enumerable.Range(0, 4)
            .Select(index => available.Count(id => GenericRarityIndex(cards[id]) == index)).ToArray();
        return CanFinishGenericCounts(counts, GenericRarityDeficits(ids, cards), rounds, size,
            new Dictionary<string, bool>(StringComparer.Ordinal));
    }

    private static bool CanFinishGenericCounts(int[] available, int[] deficits, int rounds, int size,
        Dictionary<string, bool> memo)
    {
        if (rounds == 0)
            return deficits.All(count => count == 0);
        if (rounds < 0 || available.Sum() < rounds * size || deficits.Sum() > rounds * size
            || deficits.Where((count, index) => count > available[index]).Any())
            return false;
        if (deficits.All(count => count == 0) && available.Sum() >= rounds * size + 2)
            return true;
        var key = rounds + ":" + string.Join(",", available) + ":" + string.Join(",", deficits);
        if (memo.TryGetValue(key, out var cached))
            return cached;
        var viable = 0;
        foreach (var picked in GenericCountChoices(available, size))
        {
            var remaining = available.Zip(picked, (count, take) => count - take).ToArray();
            var missing = deficits.Select((count, index) => Math.Max(0, count - picked[index])).ToArray();
            if (!CanFinishGenericCounts(remaining, missing, rounds - 1, size, memo))
                continue;
            var combinations = 1;
            for (var index = 0; index < picked.Length; index++)
            {
                var count = picked[index] == 0 || picked[index] == available[index] ? 1
                    : available[index] == 2 ? 2 : 3;
                combinations = Math.Min(3, combinations * count);
            }
            viable += combinations;
            if (viable >= 3)
                return memo[key] = true;
        }
        return memo[key] = false;
    }

    private static IEnumerable<int[]> GenericCountChoices(int[] available, int size)
    {
        for (var common = 0; common <= Math.Min(size, available[0]); common++)
        for (var uncommon = 0; uncommon <= Math.Min(size - common, available[1]); uncommon++)
        for (var rare = 0; rare <= Math.Min(size - common - uncommon, available[2]); rare++)
        {
            var other = size - common - uncommon - rare;
            if (other <= available[3])
                yield return [common, uncommon, rare, other];
        }
    }

    private static IEnumerable<string[]> GenericSelections(string[][] groups, int[] counts, int group)
    {
        if (group == groups.Length)
        {
            yield return [];
            yield break;
        }
        foreach (var prefix in GenericCombinations(groups[group], counts[group], 0).Take(3))
        foreach (var suffix in GenericSelections(groups, counts, group + 1).Take(3))
            yield return prefix.Concat(suffix).ToArray();
    }

    private static IEnumerable<string[]> GenericCombinations(string[] ids, int count, int start)
    {
        if (count == 0)
        {
            yield return [];
            yield break;
        }
        for (var index = start; index <= ids.Length - count; index++)
        foreach (var rest in GenericCombinations(ids, count - 1, index + 1))
            yield return new[] { ids[index] }.Concat(rest).ToArray();
    }

    private static bool HasRequiredRarities(IEnumerable<string> selected, IReadOnlyDictionary<string, CardModel> cards)
    {
        var ids = selected.ToHashSet(StringComparer.Ordinal);
        return ids.All(cards.ContainsKey) && RequiredRarities.All(rarity =>
            ids.Count(id => cards[id].Rarity == rarity) >= RewardPoolCatalog.MinimumPerRewardRarity);
    }

    private static IReadOnlyList<RewardPoolPackage> EligiblePackageDefinitions() => EligiblePackageDefinitions(MainCardsById());

    private static IReadOnlyList<RewardPoolPackage> EligiblePackageDefinitions(IReadOnlyDictionary<string, CardModel> cards) =>
        RewardPoolConstructionCatalog.Packages
            .Where(package => !package.IsSupport && package.Axes.Count == 2 && package.BridgeCardIds.Count > 0
                && package.CardIds.Count > 0 && package.CardIds.All(cards.ContainsKey))
            .OrderBy(package => package.Id, StringComparer.Ordinal).ToArray();

    private static Dictionary<string, CardModel> MainCardsById() =>
        RewardPoolCatalog.GetSelectableMainCandidates().ToDictionary(RewardPoolCatalog.GetId, StringComparer.Ordinal);

    private static IReadOnlyList<string> AddUnique(IEnumerable<string> existing, IEnumerable<string> additions) =>
        existing.Concat(additions).Distinct(StringComparer.Ordinal).ToArray();

    private static bool ValidateCurrentCatalog(RewardPoolDraftState state, out string error)
    {
        error = "The saved draft uses earlier rules or a different package catalog. Start a new draft.";
        if (state.IsLegacy || state.RulesVersion != RewardPoolSizePolicy.CurrentRulesVersion
            || state.PackageVersion != RewardPoolConstructionCatalog.PackageVersion
            || (state.PackageVariantsEnabled && state.PackageVariantVersion != RewardPoolConstructionCatalog.PackageVariantVersion))
            return false;
        var cards = MainCardsById();
        var packages = EligiblePackageDefinitions(cards).ToDictionary(package => package.Id, StringComparer.Ordinal);
        if (state.SelectedPackageIds.Any(id => !packages.ContainsKey(id)))
            return false;
        if (state.PackageVariantsEnabled && state.SelectedPackageOffers.Any(offer =>
            !packages.TryGetValue(offer.Id, out var package) || !IsValidThemeOffer(offer, package, cards)))
            return false;
        var packageCards = (state.PackageVariantsEnabled
            ? state.SelectedPackageOffers.SelectMany(offer => offer.CardIds)
            : state.SelectedPackageIds.SelectMany(id => packages[id].CardIds)).ToHashSet(StringComparer.Ordinal);
        var expected = packageCards.Concat(state.SelectedGenericCardIds).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(state.MainCardIds) || state.MainCardIds.Any(id => !cards.ContainsKey(id)))
            return false;
        if (state.PackagePicks == PackageRoundCount && !CanFinishWithGeneric(packageCards, cards))
            return false;
        var generic = RewardPoolConstructionCatalog.GenericCardIds.ToHashSet(StringComparer.Ordinal);
        if (state.SelectedGenericCardIds.Any(id => !generic.Contains(id) || packageCards.Contains(id))
            || (state.GenericPackagePicked && !HasRequiredRarities(state.MainCardIds, cards))
            || (state.PackagePicks == PackageRoundCount && state.GenericRoundCount > 1
                && !CanFinishGenericRounds(state.MainCardIds, state.GenericRoundCount - state.GenericRoundsCompleted,
                    state.GenericCardsPerRound, cards)))
            return false;
        if (state.Stage == RewardPoolDraftStage.Packages
            && state.OfferIds.Any(id => !packages.TryGetValue(id, out var pack)
                || (state.PackageVariantsEnabled
                    ? !IsValidThemeOffer(state.PackageOffers.FirstOrDefault(offer => offer.Id == id), pack, cards)
                        || !CanPickPackage(state, pack, packages.Values.ToArray(), cards,
                            state.PackageOffers.First(offer => offer.Id == id).CardIds)
                    : !CanPickPackage(state, pack, packages.Values.ToArray(), cards))))
            return false;
        if (state.Stage == RewardPoolDraftStage.GenericPackages
            && state.GenericPackageOffers.Any(offer => !IsValidGenericRoundOffer(state, offer, cards)))
            return false;
        var extra = RewardPoolCatalog.GetSelectableExtraCandidates().Select(RewardPoolCatalog.GetId).ToHashSet(StringComparer.Ordinal);
        if (state.ExtraCardIds.Any(id => !extra.Contains(id))
            || (state.Stage == RewardPoolDraftStage.Extra && state.OfferIds.Any(id => !extra.Contains(id) || state.ExtraCardIds.Contains(id, StringComparer.Ordinal))))
            return false;
        error = string.Empty;
        return true;
    }

    private static void Shuffle<T>(IList<T> values, ref ulong randomState)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var other = NextIndex(ref randomState, index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }

    private static int NextIndex(ref ulong state, int count)
    {
        var bound = (ulong)count;
        var threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do
        {
            state = unchecked(state + 0x9E3779B97F4A7C15UL);
            value = state;
            value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
            value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
            value ^= value >> 31;
        } while (value < threshold);
        return (int)(value % bound);
    }

    private static void InitializeLocked()
    {
        if (_initialized)
            return;
        _initialized = true;
        try
        {
            _path = Path.Combine(OS.GetUserDataDir(), "mod_configs", FileName);
            var backup = _path + ".bak";
            if (!File.Exists(_path) && !File.Exists(backup))
                return;
            if (TryRead(_path, out var payload, out var serialized, out var error))
            {
                _payload = payload;
                _committedJson = serialized;
                if (File.Exists(backup) && !TryRead(backup, out _, out _, out var backupError))
                    _loadError = $"The draft backup is unreadable. Original files were preserved. {backupError}";
                return;
            }
            // A backup is useful for inspection, but never roll back a consumed
            // session or overwrite a corrupt primary during automatic recovery.
            if (TryRead(backup, out var recovered, out _, out _))
                _payload = recovered;
            _loadError = $"The saved draft cannot be safely updated. Original files were preserved. {error}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            _loadError = $"The saved draft could not be loaded: {ex.Message}";
        }
    }

    private static bool CanWriteLocked(out string error)
    {
        InitializeLocked();
        error = _loadError ?? string.Empty;
        if (error.Length > 0)
            return Fail(error);
        return true;
    }

    private static bool CommitLocked(DraftPayload candidate, out string error,
        Func<bool> commitSelection = null)
    {
        if (!ValidateStructure(candidate, out error))
            return Fail(error);
        var snapshot = candidate.Snapshot();
        var serialized = JsonSerializer.Serialize(snapshot, JsonOptions);
        var directory = Path.GetDirectoryName(_path);
        var transaction = Guid.NewGuid().ToString("N");
        var temporary = _path + "." + transaction + ".tmp";
        var temporaryBackup = temporary + ".bak";
        var oldPrimary = temporary + ".old";
        var oldBackup = temporaryBackup + ".old";
        var primaryChanged = false;
        var backupChanged = false;
        var primaryExisted = false;
        var backupExisted = false;
        var preserveRecovery = false;
        try
        {
            primaryExisted = File.Exists(_path);
            backupExisted = File.Exists(_path + ".bak");
            if (primaryExisted != (_committedJson is not null)
                || (primaryExisted && !string.Equals(File.ReadAllText(_path, Encoding.UTF8), _committedJson, StringComparison.Ordinal)))
                throw new IOException("The draft file changed outside this session. Restart to read the current file.");
            Directory.CreateDirectory(directory);
            WriteDurably(temporary, serialized);
            WriteDurably(temporaryBackup, serialized);
            VerifyExact(temporary, serialized);
            VerifyExact(temporaryBackup, serialized);
            if (primaryExisted)
                File.Replace(temporary, _path, oldPrimary, true);
            else
                File.Move(temporary, _path);
            primaryChanged = true;
            VerifyExact(_path, serialized);
            if (backupExisted)
                File.Replace(temporaryBackup, _path + ".bak", oldBackup, true);
            else
                File.Move(temporaryBackup, _path + ".bak");
            backupChanged = true;
            VerifyExact(_path + ".bak", serialized);
            if (commitSelection is not null && !TryCommitSelection(commitSelection, out error))
                throw new IOException(error);
            _payload = snapshot;
            _committedJson = serialized;
            _lastError = string.Empty;
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            error = $"The draft could not be saved: {ex.Message}";
            try
            {
                if (backupChanged)
                    RestoreFile(_path + ".bak", oldBackup, backupExisted);
                if (primaryChanged)
                    RestoreFile(_path, oldPrimary, primaryExisted);
            }
            catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                preserveRecovery = true;
                _loadError = error + $" Recovery also failed; preserved transaction files: {restoreException.Message}";
                error = _loadError;
            }
            return Fail(error);
        }
        finally
        {
            DeleteBestEffort(temporary);
            DeleteBestEffort(temporaryBackup);
            if (!preserveRecovery)
            {
                DeleteBestEffort(oldPrimary);
                DeleteBestEffort(oldBackup);
            }
        }
    }

    private static bool TryCommitSelection(Func<bool> commitSelection, out string error)
    {
        try
        {
            if (commitSelection())
            {
                error = string.Empty;
                return true;
            }
            error = "The preset selection could not be committed.";
        }
        catch (Exception exception)
        {
            error = $"The preset selection could not be committed: {exception.Message}";
        }
        return false;
    }

    private static void RestoreFile(string path, string previous, bool existed)
    {
        if (existed)
            File.Replace(previous, path, null, true);
        else
            File.Delete(path);
    }

    private static void WriteDurably(string path, string serialized)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        var bytes = new UTF8Encoding(false).GetBytes(serialized);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static bool TryRead(string path, out DraftPayload payload, out string serialized, out string error)
    {
        payload = null;
        serialized = null;
        try
        {
            if (!File.Exists(path))
            {
                error = "The primary draft file is missing.";
                return false;
            }
            serialized = File.ReadAllText(path, Encoding.UTF8);
            payload = JsonSerializer.Deserialize<DraftPayload>(serialized, JsonOptions);
            if (!ValidateStructure(payload, out error))
                return false;
            payload.State = payload.State with
            {
                IsLegacy = payload.State.IsLegacy || payload.SchemaVersion < 2
                    || payload.State.RulesVersion != RewardPoolSizePolicy.CurrentRulesVersion
                    || payload.State.PackageVersion != RewardPoolConstructionCatalog.PackageVersion
                    || (payload.State.PackageVariantsEnabled && payload.State.PackageVariantVersion != RewardPoolConstructionCatalog.PackageVariantVersion)
            };
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void VerifyExact(string path, string serialized)
    {
        if (!TryRead(path, out _, out var actual, out var error)
            || !string.Equals(actual, serialized, StringComparison.Ordinal))
            throw new IOException("The saved draft did not match its prepared snapshot. " + error);
    }

    private static bool ValidateStructure(DraftPayload payload, out string error)
    {
        error = "The saved draft structure is invalid.";
        var state = payload?.State;
        if (payload is null || payload.SchemaVersion is < 1 or > 4 || state is null
            || !Guid.TryParseExact(state.SessionId, "N", out _)
            || !Enum.IsDefined(state.Stage) || state.PackagePicks < 0 || state.PackagePicks > PackageRoundCount
            || !ValidIds(state.MainCardIds) || !ValidIds(state.ExtraCardIds)
            || !ValidIds(state.OfferIds) || !ValidIds(state.SelectedPackageIds)
            || state.SelectedPackageIds.Count != state.PackagePicks
            || state.ExtraCardIds.Count > RewardPoolCatalog.SelectableExtraCardCount)
            return false;

        // Missing newer fields keep their empty defaults, including in schema 1.
        // Explicit nulls and malformed offers cannot produce a safe snapshot.
        if (state.PackageOffers is null || state.SelectedPackageOffers is null || state.GenericPackageOffers is null
            || state.SelectedGenericCardIds is null
            || state.PackageOffers.Any(offer => offer?.CardIds is null)
            || state.SelectedPackageOffers.Any(offer => offer?.CardIds is null)
            || state.GenericPackageOffers.Any(offer => offer?.CardIds is null))
            return false;

        // Schema 1 sessions remain inspectable and replaceable. Their old size
        // contract is intentionally independent of today's 70–90-card policy.
        if (payload.SchemaVersion == 1)
            return ValidateLegacyStructure(state, out error);

        if (state.PackageOffers is null || state.SelectedPackageOffers is null || !ValidIds(state.SelectedGenericCardIds)
            || (payload.SchemaVersion < 4 && (state.PackageVariantsEnabled || state.PackageVariantVersion != 0
                || state.PackageOffers.Count != 0 || state.SelectedPackageOffers.Count != 0))
            || (payload.SchemaVersion == 4 && (!state.PackageVariantsEnabled || state.PackageVariantVersion <= 0)))
            return false;
        if (state.PackageVariantsEnabled)
        {
            if (state.SelectedPackageOffers.Count != state.PackagePicks
                || state.SelectedPackageOffers.Any(offer => offer is null || !ValidIds(offer.CardIds))
                || !state.SelectedPackageIds.SequenceEqual(state.SelectedPackageOffers.Select(offer => offer.Id), StringComparer.Ordinal)
                || (state.Stage == RewardPoolDraftStage.Packages
                    ? state.PackageOffers.Count != 3 || state.PackageOffers.Any(offer => offer is null || !ValidIds(offer.CardIds))
                        || !state.OfferIds.SequenceEqual(state.PackageOffers.Select(offer => offer.Id), StringComparer.Ordinal)
                        || state.PackageOffers.Any(offer => state.SelectedPackageIds.Contains(offer.Id, StringComparer.Ordinal))
                    : state.PackageOffers.Count != 0)
                || !state.SelectedPackageOffers.SelectMany(offer => offer.CardIds).Concat(state.SelectedGenericCardIds)
                    .ToHashSet(StringComparer.Ordinal).SetEquals(state.MainCardIds))
                return false;
            if (state.PackageVariantVersion == RewardPoolConstructionCatalog.PackageVariantVersion
                && state.PackageVersion == RewardPoolConstructionCatalog.PackageVersion)
            {
                var profiles = RewardPoolConstructionCatalog.Packages.Where(package => !package.IsSupport)
                    .ToDictionary(package => package.Id, StringComparer.Ordinal);
                if (state.PackageOffers.Concat(state.SelectedPackageOffers).Any(offer =>
                    !profiles.TryGetValue(offer.Id, out var package) || !IsValidThemeProfile(offer, package)))
                    return false;
            }
        }

        if (state.RulesVersion <= 0 || string.IsNullOrWhiteSpace(state.PackageVersion)
            || !ValidIds(state.SelectedGenericCardIds) || state.GenericPackageOffers is null
            || state.GenericRoundCount <= 0 || state.GenericRoundCount > GenericTotalCardCount
            || state.GenericCardsPerRound <= 0 || state.GenericCardsPerRound > GenericTotalCardCount
            || state.GenericRoundCount * state.GenericCardsPerRound != GenericTotalCardCount
            || (payload.SchemaVersion == 2 && (state.GenericRoundCount != 1 || state.GenericCardsPerRound != GenericTotalCardCount))
            || state.SelectedGenericCardIds.Count > GenericTotalCardCount
            || state.SelectedGenericCardIds.Count % state.GenericCardsPerRound != 0
            || state.MainCardIds.Count > RewardPoolSizePolicy.MaxSelectableMain
            || state.GenericPackagePicked != (state.SelectedGenericCardIds.Count == GenericTotalCardCount)
            || (state.SelectedGenericCardIds.Count > 0
                ? !IsGenericOptionId(state, state.SelectedGenericPackageId, state.GenericRoundsCompleted)
                : !string.IsNullOrEmpty(state.SelectedGenericPackageId))
            || state.SelectedGenericCardIds.Any(id => !state.MainCardIds.Contains(id, StringComparer.Ordinal)))
            return false;

        var complete = state.Stage is RewardPoolDraftStage.Complete or RewardPoolDraftStage.Consumed;
        if (complete ? state.OfferIds.Count != 0 : state.OfferIds.Count != 3)
            return false;
        var packageCount = state.MainCardIds.Count - state.SelectedGenericCardIds.Count;
        if (packageCount > MaxPackageCardCount
            || (state.PackagePicks == PackageRoundCount && packageCount < MinPackageCardCount))
            return false;
        var mainFull = state.GenericPackagePicked
            && state.MainCardIds.Count is >= RewardPoolSizePolicy.MinSelectableMain and <= RewardPoolSizePolicy.MaxSelectableMain;
        var extraFull = state.ExtraCardIds.Count == RewardPoolCatalog.SelectableExtraCardCount;
        var stageMatches = state.Stage switch
        {
            RewardPoolDraftStage.Packages => state.PackagePicks < PackageRoundCount && state.SelectedGenericCardIds.Count == 0 && state.ExtraCardIds.Count == 0,
            RewardPoolDraftStage.GenericPackages => state.PackagePicks == PackageRoundCount && !state.GenericPackagePicked && state.ExtraCardIds.Count == 0,
            RewardPoolDraftStage.Extra => state.PackagePicks == PackageRoundCount && mainFull && !extraFull,
            RewardPoolDraftStage.Complete or RewardPoolDraftStage.Consumed => state.PackagePicks == PackageRoundCount && mainFull && extraFull,
            _ => false
        };
        if (!stageMatches)
            return false;
        if (state.Stage == RewardPoolDraftStage.GenericPackages)
        {
            if (state.GenericPackageOffers.Count != 3
                || state.GenericPackageOffers.Any(offer => offer is null || !ValidIds(offer.CardIds)
                    || !IsGenericOptionId(state, offer.Id, state.GenericRoundsCompleted + 1)
                    || offer.CardIds.Count != state.GenericCardsPerRound || offer.CardIds.Any(id => state.MainCardIds.Contains(id, StringComparer.Ordinal)))
                || !state.OfferIds.SequenceEqual(state.GenericPackageOffers.Select(offer => offer.Id), StringComparer.Ordinal)
                || state.GenericPackageOffers.Select(offer => string.Join("\n", offer.CardIds.OrderBy(id => id, StringComparer.Ordinal)))
                    .Distinct(StringComparer.Ordinal).Count() != 3)
                return false;
        }
        else if (state.GenericPackageOffers.Count != 0)
            return false;
        error = string.Empty;
        return true;
    }

    private static bool ValidateLegacyStructure(RewardPoolDraftState state, out string error)
    {
        error = "The legacy draft structure is invalid.";
        const int legacySelectableMain = 55;
        if (state.MainCardIds.Count > legacySelectableMain)
            return false;
        var complete = state.Stage is RewardPoolDraftStage.Complete or RewardPoolDraftStage.Consumed;
        if (complete ? state.OfferIds.Count != 0 : state.OfferIds.Count is < 1 or > 3)
            return false;
        var mainFull = state.MainCardIds.Count == legacySelectableMain;
        var extraFull = state.ExtraCardIds.Count == RewardPoolCatalog.SelectableExtraCardCount;
        var valid = state.Stage switch
        {
            RewardPoolDraftStage.Packages => state.PackagePicks < PackageRoundCount && state.ExtraCardIds.Count == 0,
            RewardPoolDraftStage.Main => state.PackagePicks == PackageRoundCount && !mainFull && state.ExtraCardIds.Count == 0,
            RewardPoolDraftStage.Extra => state.PackagePicks == PackageRoundCount && mainFull && !extraFull,
            RewardPoolDraftStage.Complete or RewardPoolDraftStage.Consumed => state.PackagePicks == PackageRoundCount && mainFull && extraFull,
            _ => false
        };
        if (valid)
            error = string.Empty;
        return valid;
    }

    private static bool ValidIds(IReadOnlyList<string> ids) => ids is not null
        && ids.All(id => !string.IsNullOrWhiteSpace(id) && id == id.Trim())
        && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count;

    private static void DeleteBestEffort(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool Fail(string error)
    {
        _lastError = error;
        return false;
    }

    private static RewardPoolValidationResult Invalid(string error) =>
        RewardPoolValidationResult.Invalid(RewardPoolValidationCode.MissingDefinition, error);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class DraftPayload
    {
        public int SchemaVersion { get; set; } = 1;
        public bool Selected { get; set; }
        public ulong RandomState { get; set; }
        public RewardPoolDraftState State { get; set; }

        internal DraftPayload Snapshot() => new()
        {
            SchemaVersion = SchemaVersion,
            Selected = Selected,
            RandomState = RandomState,
            State = State.Snapshot()
        };
    }
}
