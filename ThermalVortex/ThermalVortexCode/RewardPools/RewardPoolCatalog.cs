using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Character;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

public static class RewardPoolCatalog
{
    // Historical aliases are retained for readers of earlier saved definitions.
    public const int MainDeckSize = RewardPoolSizePolicy.LegacyMainTotal;
    public const int ExtraDeckSize = 10;
    public const int RequiredMainCardCount = 5;
    public const int RequiredExtraCardCount = 1;
    public const int SelectableMainCardCount = MainDeckSize - RequiredMainCardCount;
    public const int SelectableExtraCardCount = ExtraDeckSize - RequiredExtraCardCount;
    public const int MinimumPerRewardRarity = 3;
    public const int ExpectedExtraCandidateCount = 17;
    public const int ExpectedThermalVortexRewardCandidateCount = 83;

    private static readonly RewardPoolCardOrigin[] StableOriginOrder =
    [
        RewardPoolCardOrigin.ThermalVortex,
        RewardPoolCardOrigin.Ironclad,
        RewardPoolCardOrigin.Silent,
        RewardPoolCardOrigin.Defect,
        RewardPoolCardOrigin.Necrobinder,
        RewardPoolCardOrigin.Regent
    ];

    private static readonly object CacheLock = new();
    private static IReadOnlyList<CardModel> _requiredMainCards;
    private static MainCatalog _mainCatalog;
    private static MainCatalog _multiplayerMainCatalog;
    private static IReadOnlyList<CardModel> _extraCandidates;

    /// <summary>
    /// Character-pool order used by construction UI, filtering, and seeded selection.
    /// </summary>
    public static IReadOnlyList<RewardPoolCardOrigin> MainCardOriginOrder =>
        Array.AsReadOnly(StableOriginOrder);

    public static IReadOnlyList<string> RequiredMainCardIds =>
        GetRequiredMainCards().Select(GetId).ToArray();

    public static IReadOnlyList<string> RequiredExtraCardIds =>
        [GetId(ModelDb.Card<Detonation>())];

    public static IReadOnlyList<CardModel> GetRequiredMainCards()
    {
        lock (CacheLock)
        {
            return _requiredMainCards ??= Array.AsReadOnly<CardModel>(
            [
                ModelDb.Card<Strike>(),
                ModelDb.Card<Defend>(),
                ModelDb.Card<InternalCombustion>(),
                ModelDb.Card<SectionPole>(),
                ModelDb.Card<XyzSummon>()
            ]);
        }
    }

    /// <summary>
    /// Returns all freely selectable main reward cards. Sources are ordered
    /// ThermalVortex, Ironclad, Silent, Defect, Necrobinder, Regent; within
    /// each source cards are ordered Common, Uncommon, Rare, then full ModelId.
    /// </summary>
    public static IReadOnlyList<CardModel> GetMainRewardCandidates() =>
        GetMainCatalog().AllRewardCandidates;

    public static IReadOnlyList<CardModel> GetMainRewardCandidates(bool multiplayer) =>
        GetMainCatalog(multiplayer).AllRewardCandidates;

    public static IReadOnlyList<CardModel> GetMainRewardCandidates(RewardPoolCardOrigin origin) =>
        GetMainCatalog().ByOrigin.TryGetValue(origin, out var cards) ? cards : [];

    public static IReadOnlyList<CardModel> GetMainRewardCandidates(RewardPoolCardOrigin origin, bool multiplayer) =>
        GetMainCatalog(multiplayer).ByOrigin.TryGetValue(origin, out var cards) ? cards : [];

    public static IReadOnlyList<CardModel> GetSelectableMainCandidates() =>
        GetMainRewardCandidates();

    public static IReadOnlyList<CardModel> GetSelectableMainCandidates(bool multiplayer) =>
        GetMainRewardCandidates(multiplayer);

    public static IReadOnlyList<CardModel> GetAllMainBuildCandidates() =>
        GetRequiredMainCards().Concat(GetMainRewardCandidates()).ToArray();

    public static bool IsLegalMainRewardCandidate(CardModel card) =>
        card is not null && IsLegalMainRewardCandidate(GetId(card));

    public static bool IsLegalMainRewardCandidate(ModelId cardId) =>
        IsLegalMainRewardCandidate(cardId?.ToString());

    public static bool IsLegalMainRewardCandidate(string cardId) =>
        TryGetMainRewardCandidate(cardId, out _);

    public static bool TryGetMainRewardCandidate(ModelId cardId, out CardModel card) =>
        TryGetMainRewardCandidate(cardId?.ToString(), out card);

    public static bool TryGetMainRewardCandidate(string cardId, out CardModel card) =>
        TryGetMainRewardCandidate(cardId, false, out card);

    public static bool TryGetMainRewardCandidate(string cardId, bool multiplayer, out CardModel card)
    {
        card = null;
        return !string.IsNullOrWhiteSpace(cardId)
            && GetMainCatalog(multiplayer).RewardById.TryGetValue(cardId.Trim(), out card);
    }

    public static CardModel GetMainRewardCandidate(string cardId) =>
        TryGetMainRewardCandidate(cardId, out var card) ? card : null;

    public static CardModel GetMainRewardCandidate(string cardId, bool multiplayer) =>
        TryGetMainRewardCandidate(cardId, multiplayer, out var card) ? card : null;

    public static bool TryGetMainBuildCandidate(ModelId cardId, out CardModel card) =>
        TryGetMainBuildCandidate(cardId?.ToString(), out card);

    public static bool TryGetMainBuildCandidate(string cardId, out CardModel card)
    {
        card = null;
        return !string.IsNullOrWhiteSpace(cardId)
            && GetMainCatalog().BuildById.TryGetValue(cardId.Trim(), out card);
    }

    public static CardModel GetMainBuildCandidate(string cardId) =>
        TryGetMainBuildCandidate(cardId, out var card) ? card : null;

    public static CardModel GetMainBuildCandidate(string cardId, bool multiplayer) =>
        !string.IsNullOrWhiteSpace(cardId)
            && GetMainCatalog(multiplayer).BuildById.TryGetValue(cardId.Trim(), out var card) ? card : null;

    public static bool TryGetMainCardOrigin(CardModel card, out RewardPoolCardOrigin origin)
    {
        origin = default;
        return card is not null && TryGetMainCardOrigin(GetId(card), out origin);
    }

    public static bool TryGetMainCardOrigin(ModelId cardId, out RewardPoolCardOrigin origin) =>
        TryGetMainCardOrigin(cardId?.ToString(), out origin);

    public static bool TryGetMainCardOrigin(string cardId, out RewardPoolCardOrigin origin)
    {
        origin = default;
        return !string.IsNullOrWhiteSpace(cardId)
            && (GetMainCatalog().OriginById.TryGetValue(cardId.Trim(), out origin)
                || GetMainCatalog(true).OriginById.TryGetValue(cardId.Trim(), out origin));
    }

    public static bool DefinitionContainsOrigin(
        RewardPoolDefinition definition,
        RewardPoolCardOrigin origin) =>
        definition?.ContainsOrigin(origin) == true;

    /// <summary>
    /// Returns the 16 freely selectable extra-deck candidates. The required
    /// Detonation slot is deliberately absent.
    /// </summary>
    public static IReadOnlyList<CardModel> GetExtraCandidates()
    {
        var required = RequiredExtraCardIds.ToHashSet(StringComparer.Ordinal);
        return GetAllExtraRewardCandidates()
            .Where(card => !required.Contains(GetId(card)))
            .ToArray();
    }

    /// <summary>
    /// Returns all 17 extra-deck construction entries in their historical order.
    /// </summary>
    public static IReadOnlyList<CardModel> GetAllExtraRewardCandidates()
    {
        lock (CacheLock)
        {
            return _extraCandidates ??= Array.AsReadOnly<CardModel>(
            [
                ModelDb.Card<Vortex>(),
                ModelDb.Card<Detonation>(),
                ModelDb.Card<CircuitTalismanBeast>(),
                ModelDb.Card<FullArmorThunderLance>(),
                ModelDb.Card<DormantMagneticFieldBeast>(),
                ModelDb.Card<BrilliantRebootKnight>(),
                ModelDb.Card<DivineArsenalFurnaceGod>(),
                ModelDb.Card<ChimeratechOverdragon>(),
                ModelDb.Card<Relinquished>(),
                ModelDb.Card<CyberEndDragon>(),
                ModelDb.Card<CyberDragonInfinity>(),
                ModelDb.Card<WingedDragonOfRaSphereMode>(),
                ModelDb.Card<MillenniumGrandThief>(),
                ModelDb.Card<ExodiaSummoner>(),
                ModelDb.Card<MillenniumMasterKey>(),
                ModelDb.Card<EvilExodia>(),
                ModelDb.Card<ExodiaGuardian>()
            ]);
        }
    }

    public static IReadOnlyList<CardModel> GetSelectableExtraCandidates() =>
        GetExtraCandidates();

    public static bool TryGetExtraRewardCandidate(string cardId, out CardModel card)
    {
        card = null;
        if (string.IsNullOrWhiteSpace(cardId))
            return false;

        card = GetAllExtraRewardCandidates()
            .FirstOrDefault(candidate => string.Equals(GetId(candidate), cardId.Trim(), StringComparison.Ordinal));
        return card is not null;
    }

    public static bool IsRequiredMainCard(CardModel card) =>
        card is not null && IsRequiredMainCard(GetId(card));

    public static bool IsRequiredMainCard(string cardId) =>
        !string.IsNullOrWhiteSpace(cardId)
        && RequiredMainCardIds.Contains(cardId.Trim(), StringComparer.Ordinal);

    public static bool IsRequiredExtraCard(CardModel card) =>
        card is not null && IsRequiredExtraCard(GetId(card));

    public static bool IsRequiredExtraCard(string cardId) =>
        !string.IsNullOrWhiteSpace(cardId)
        && RequiredExtraCardIds.Contains(cardId.Trim(), StringComparer.Ordinal);

    public static bool CreateManualDefinition(
        IEnumerable<string> selectedMainCardIds,
        IEnumerable<string> selectedExtraCardIds,
        out RewardPoolDefinition definition,
        out RewardPoolValidationResult validation) =>
        CreateConstructedDefinition(selectedMainCardIds, selectedExtraCardIds,
            new RewardPoolConstructionMetadata(), out definition, out validation);

    public static bool CreateConstructedDefinition(
        IEnumerable<string> selectedMainCardIds,
        IEnumerable<string> selectedExtraCardIds,
        RewardPoolConstructionMetadata construction,
        out RewardPoolDefinition definition,
        out RewardPoolValidationResult validation)
    {
        var selectedMain = NormalizeIds(selectedMainCardIds);
        var selectedExtra = NormalizeIds(selectedExtraCardIds);

        construction ??= new RewardPoolConstructionMetadata();
        if (!RewardPoolSizePolicy.IsCurrentSelectionCountValid(selectedMain.Count, construction.Method))
        {
            definition = RewardPoolDefinition.Standard;
            validation = RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.IncorrectMainCardCount,
                $"The selectable main-card count {selectedMain.Count} is not valid for {construction.Method} construction.");
            return false;
        }

        if (selectedExtra.Count != SelectableExtraCardCount)
        {
            definition = RewardPoolDefinition.Standard;
            validation = RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.IncorrectExtraCardCount,
                $"Manual construction requires exactly {SelectableExtraCardCount} selectable extra cards; received {selectedExtra.Count}.");
            return false;
        }

        definition = RewardPoolDefinition.CreateManualUnchecked(
            RequiredMainCardIds.Concat(selectedMain),
            RequiredExtraCardIds.Concat(selectedExtra),
            construction);
        validation = definition.Validate();
        if (validation.IsValid)
            return true;

        definition = RewardPoolDefinition.Standard;
        return false;
    }

    public static bool CreateManualDefinition(
        IEnumerable<ModelId> selectedMainCardIds,
        IEnumerable<ModelId> selectedExtraCardIds,
        out RewardPoolDefinition definition,
        out RewardPoolValidationResult validation) =>
        CreateManualDefinition(
            selectedMainCardIds?.Select(id => id?.ToString()),
            selectedExtraCardIds?.Select(id => id?.ToString()),
            out definition,
            out validation);

    /// <summary>
    /// Performs a side-effect-free integrity check after ModelDb is ready.
    /// </summary>
    public static RewardPoolValidationResult ValidateCatalog()
    {
        try
        {
            var requiredMain = GetRequiredMainCards();
            var selectableMain = GetMainRewardCandidates();
            var allExtra = GetAllExtraRewardCandidates();
            var selectableExtra = GetExtraCandidates();

            if (requiredMain.Count != RequiredMainCardCount
                || selectableMain.Count < RewardPoolSizePolicy.MaxSelectableMain
                || GetMainRewardCandidates(RewardPoolCardOrigin.ThermalVortex).Count
                    != ExpectedThermalVortexRewardCandidateCount
                || allExtra.Count != ExpectedExtraCandidateCount
                || selectableExtra.Count != ExpectedExtraCandidateCount - RequiredExtraCardCount)
            {
                return RewardPoolValidationResult.Invalid(
                    RewardPoolValidationCode.CatalogCountMismatch,
                    $"Reward-pool catalog counts cannot satisfy the construction contract: requiredMain={requiredMain.Count}, selectableMain={selectableMain.Count}, allExtra={allExtra.Count}, selectableExtra={selectableExtra.Count}.");
            }

            var requiredMainIds = requiredMain.Select(GetId).ToArray();
            var selectableMainIds = selectableMain.Select(GetId).ToArray();
            var allExtraIds = allExtra.Select(GetId).ToArray();
            var selectableExtraIds = selectableExtra.Select(GetId).ToArray();
            if (HasDuplicate(requiredMainIds)
                || HasDuplicate(selectableMainIds)
                || HasDuplicate(allExtraIds)
                || HasDuplicate(selectableExtraIds))
            {
                return RewardPoolValidationResult.Invalid(
                    RewardPoolValidationCode.CatalogDuplicateId,
                    "The reward-pool catalog contains a duplicate full ModelId.");
            }

            var requiredMainIdSet = requiredMainIds.ToHashSet(StringComparer.Ordinal);
            var requiredExtraIdSet = RequiredExtraCardIds.ToHashSet(StringComparer.Ordinal);
            if (selectableMainIds.Any(requiredMainIdSet.Contains)
                || selectableExtraIds.Any(requiredExtraIdSet.Contains)
                || !requiredExtraIdSet.IsSubsetOf(allExtraIds))
            {
                return RewardPoolValidationResult.Invalid(
                    RewardPoolValidationCode.CatalogInvariantViolation,
                    "A required starting card leaked into a selectable catalog or is missing from the complete catalog.");
            }

            foreach (var origin in StableOriginOrder)
            {
                var sourceCards = GetMainRewardCandidates(origin);
                if (sourceCards.Count == 0)
                {
                    return RewardPoolValidationResult.Invalid(
                        RewardPoolValidationCode.CatalogInvariantViolation,
                        $"The {origin} reward-pool source is empty.");
                }

                var expectedOrder = sourceCards
                    .OrderBy(card => GetRaritySortIndex(card.Rarity))
                    .ThenBy(GetId, StringComparer.Ordinal)
                    .ToArray();
                if (!sourceCards.Select(GetId).SequenceEqual(expectedOrder.Select(GetId), StringComparer.Ordinal)
                    || sourceCards.Any(card => !TryGetMainCardOrigin(card, out var actual) || actual != origin))
                {
                    return RewardPoolValidationResult.Invalid(
                        RewardPoolValidationCode.CatalogInvariantViolation,
                        $"The {origin} reward-pool source has unstable ordering or incorrect source metadata.");
                }
            }

            if (selectableMain.Any(card =>
                    card.Rarity is not (CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
                    || card.Type is not (CardType.Attack or CardType.Skill or CardType.Power)
                    || card.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly)
                || GetMainRewardCandidates(RewardPoolCardOrigin.ThermalVortex).Any(card =>
                    card is not MainDeckCard || ThermalVortexGeneratedCards.IsRewardExcluded(card))
                || StableOriginOrder.Skip(1).SelectMany(GetMainRewardCandidates).Any(card =>
                    card.GetType().Assembly != typeof(CardModel).Assembly)
                || allExtra.Any(card => card is not XyzMonsterCard))
            {
                return RewardPoolValidationResult.Invalid(
                    RewardPoolValidationCode.CatalogInvariantViolation,
                    "The catalog contains an invalid rarity, deck class, generated ThermalVortex card, non-vanilla foreign card, or extra-deck class.");
            }

            foreach (var rarity in new[] { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare })
            {
                if (selectableMain.Count(card => card.Rarity == rarity) < MinimumPerRewardRarity)
                {
                    return RewardPoolValidationResult.Invalid(
                        RewardPoolValidationCode.CatalogInvariantViolation,
                        $"The catalog contains fewer than {MinimumPerRewardRarity} {rarity} main reward cards.");
                }
            }

            return RewardPoolValidationResult.Valid;
        }
        catch (Exception ex)
        {
            return RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.CatalogUnavailable,
                $"The reward-pool catalog could not be inspected: {ex.Message}");
        }
    }

    internal static string GetId(CardModel card) =>
        card?.Id?.ToString() ?? string.Empty;

    internal static IReadOnlyList<string> NormalizeIds(IEnumerable<string> ids) =>
        ids?.Select(id => id?.Trim() ?? string.Empty).ToArray() ?? [];

    private static MainCatalog GetMainCatalog(bool multiplayer = false)
    {
        lock (CacheLock)
        {
            return multiplayer
                ? _multiplayerMainCatalog ??= BuildMainCatalog(true)
                : _mainCatalog ??= BuildMainCatalog(false);
        }
    }

    private static MainCatalog BuildMainCatalog(bool multiplayer)
    {
        var requiredIds = GetRequiredMainCards().Select(GetId).ToHashSet(StringComparer.Ordinal);
        var byOrigin = new Dictionary<RewardPoolCardOrigin, IReadOnlyList<CardModel>>
        {
            [RewardPoolCardOrigin.ThermalVortex] = BuildSourceCards(
                ModelDb.CardPool<ThermalVortexCardPool>().AllCards,
                requiredIds,
                requireThermalMainDeckClass: true, multiplayer: multiplayer),
            [RewardPoolCardOrigin.Ironclad] = BuildSourceCards(
                ModelDb.CardPool<IroncladCardPool>().AllCards,
                requiredIds, multiplayer: multiplayer),
            [RewardPoolCardOrigin.Silent] = BuildSourceCards(
                ModelDb.CardPool<SilentCardPool>().AllCards,
                requiredIds, multiplayer: multiplayer),
            [RewardPoolCardOrigin.Defect] = BuildSourceCards(
                ModelDb.CardPool<DefectCardPool>().AllCards,
                requiredIds, multiplayer: multiplayer),
            [RewardPoolCardOrigin.Necrobinder] = BuildSourceCards(
                ModelDb.CardPool<NecrobinderCardPool>().AllCards,
                requiredIds, multiplayer: multiplayer),
            [RewardPoolCardOrigin.Regent] = BuildSourceCards(
                ModelDb.CardPool<RegentCardPool>().AllCards,
                requiredIds, multiplayer: multiplayer)
        };

        var all = StableOriginOrder.SelectMany(origin => byOrigin[origin]).ToArray();
        var rewardById = new Dictionary<string, CardModel>(StringComparer.Ordinal);
        var buildById = new Dictionary<string, CardModel>(StringComparer.Ordinal);
        var originById = new Dictionary<string, RewardPoolCardOrigin>(StringComparer.Ordinal);

        foreach (var required in GetRequiredMainCards())
        {
            var id = GetId(required);
            buildById.TryAdd(id, required);
            originById.TryAdd(id, RewardPoolCardOrigin.ThermalVortex);
        }

        foreach (var origin in StableOriginOrder)
        {
            foreach (var card in byOrigin[origin])
            {
                var id = GetId(card);
                rewardById.TryAdd(id, card);
                buildById.TryAdd(id, card);
                originById.TryAdd(id, origin);
            }
        }

        return new MainCatalog(
            Array.AsReadOnly(all),
            byOrigin,
            rewardById,
            buildById,
            originById);
    }

    private static IReadOnlyList<CardModel> BuildSourceCards(
        IEnumerable<CardModel> cards,
        IReadOnlySet<string> requiredIds,
        bool requireThermalMainDeckClass = false,
        bool multiplayer = false)
    {
        var vanillaAssembly = typeof(CardModel).Assembly;
        return Array.AsReadOnly(cards
            .Where(card => card is not null)
            .Where(card => card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
            .Where(card => card.Type is CardType.Attack or CardType.Skill or CardType.Power)
            .Where(card => card.MultiplayerConstraint != (multiplayer
                ? CardMultiplayerConstraint.SingleplayerOnly : CardMultiplayerConstraint.MultiplayerOnly))
            .Where(card => !requiredIds.Contains(GetId(card)))
            .Where(card => requireThermalMainDeckClass
                ? card is MainDeckCard && !ThermalVortexGeneratedCards.IsRewardExcluded(card)
                : card.GetType().Assembly == vanillaAssembly)
            .OrderBy(card => GetRaritySortIndex(card.Rarity))
            .ThenBy(GetId, StringComparer.Ordinal)
            .ToArray());
    }

    private static int GetRaritySortIndex(CardRarity rarity) => rarity switch
    {
        CardRarity.Common => 0,
        CardRarity.Uncommon => 1,
        CardRarity.Rare => 2,
        _ => int.MaxValue
    };

    private static bool HasDuplicate(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return ids.Any(id => !seen.Add(id));
    }

    private sealed record MainCatalog(
        IReadOnlyList<CardModel> AllRewardCandidates,
        IReadOnlyDictionary<RewardPoolCardOrigin, IReadOnlyList<CardModel>> ByOrigin,
        IReadOnlyDictionary<string, CardModel> RewardById,
        IReadOnlyDictionary<string, CardModel> BuildById,
        IReadOnlyDictionary<string, RewardPoolCardOrigin> OriginById);
}
