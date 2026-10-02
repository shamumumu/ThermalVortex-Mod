using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

public sealed class RewardPoolDefinition
{
    public const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly HashSet<string> _mainCardIdSet;
    private readonly HashSet<string> _extraCardIdSet;

    private RewardPoolDefinition(
        int schemaVersion,
        RewardPoolBuildMode buildMode,
        IEnumerable<string> mainCardIds,
        IEnumerable<string> extraCardIds,
        RewardPoolConstructionMetadata construction = null,
        int rulesVersion = RewardPoolSizePolicy.CurrentRulesVersion)
    {
        SchemaVersion = schemaVersion;
        RulesVersion = rulesVersion;
        BuildMode = buildMode;
        Construction = (construction ?? new RewardPoolConstructionMetadata { RulesVersion = rulesVersion }).Snapshot();
        MainCardIds = RewardPoolCatalog.NormalizeIds(mainCardIds);
        ExtraCardIds = RewardPoolCatalog.NormalizeIds(extraCardIds);
        _mainCardIdSet = MainCardIds.ToHashSet(StringComparer.Ordinal);
        _extraCardIdSet = ExtraCardIds.ToHashSet(StringComparer.Ordinal);
    }

    public static RewardPoolDefinition Standard =>
        new(CurrentSchemaVersion, RewardPoolBuildMode.Standard, [], []);

    public int SchemaVersion { get; }
    public int RulesVersion { get; }
    public RewardPoolBuildMode BuildMode { get; }
    public RewardPoolConstructionMetadata Construction { get; }
    public bool IsEnabled => BuildMode == RewardPoolBuildMode.Manual;
    public bool IsManual => BuildMode == RewardPoolBuildMode.Manual;
    public IReadOnlyList<string> MainCardIds { get; }
    public IReadOnlyList<string> ExtraCardIds { get; }

    public string Serialize()
    {
        var payload = new Payload
        {
            SchemaVersion = SchemaVersion,
            RulesVersion = RulesVersion,
            BuildMode = BuildMode,
            MainCardIds = MainCardIds.ToList(),
            ExtraCardIds = ExtraCardIds.ToList(),
            Construction = Construction.Snapshot()
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static bool TryDeserialize(
        string json,
        out RewardPoolDefinition definition,
        out RewardPoolValidationResult validation)
    {
        definition = Standard;
        if (string.IsNullOrWhiteSpace(json))
        {
            validation = RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.SerializationError,
                "Reward-pool JSON is empty.");
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(json, JsonOptions);
            if (payload is null)
            {
                validation = RewardPoolValidationResult.Invalid(
                    RewardPoolValidationCode.SerializationError,
                    "Reward-pool JSON did not contain an object.");
                return false;
            }

            var legacy = payload.SchemaVersion is 1 or 2;
            var rulesVersion = legacy ? RewardPoolSizePolicy.LegacyRulesVersion : payload.RulesVersion;
            var construction = payload.Construction ?? new RewardPoolConstructionMetadata();
            if (legacy)
                construction = construction with { RulesVersion = rulesVersion };
            definition = new RewardPoolDefinition(
                payload.SchemaVersion,
                payload.BuildMode,
                payload.MainCardIds,
                payload.ExtraCardIds,
                construction,
                rulesVersion);
            validation = definition.Validate();
            if (validation.IsValid)
                return true;

            definition = Standard;
            return false;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            validation = RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.SerializationError,
                $"Reward-pool JSON could not be read: {ex.Message}");
            return false;
        }
    }

    public RewardPoolValidationResult Validate()
    {
        if (SchemaVersion is < 1 or > CurrentSchemaVersion
            || RulesVersion is < RewardPoolSizePolicy.LegacyRulesVersion or > RewardPoolSizePolicy.CurrentRulesVersion
            || (SchemaVersion < CurrentSchemaVersion && RulesVersion != RewardPoolSizePolicy.LegacyRulesVersion))
        {
            return RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.UnsupportedSchemaVersion,
                $"Unsupported reward-pool schema version {SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        if (!Enum.IsDefined(BuildMode))
        {
            return RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.UnsupportedBuildMode,
                $"Unsupported reward-pool build mode {(int)BuildMode}.");
        }

        if (BuildMode == RewardPoolBuildMode.Standard)
        {
            return MainCardIds.Count == 0 && ExtraCardIds.Count == 0
                && Construction.Method == RewardPoolConstructionMethod.Free
                && Construction.RulesVersion == RulesVersion
                ? RewardPoolValidationResult.Valid
                : RewardPoolValidationResult.Invalid(
                    RewardPoolValidationCode.StandardDefinitionContainsCards,
                    "A standard reward-pool definition must not contain card IDs.");
        }

        var validMainCount = RulesVersion == RewardPoolSizePolicy.LegacyRulesVersion
            ? MainCardIds.Count == RewardPoolSizePolicy.LegacyMainTotal
            : RewardPoolSizePolicy.IsCurrentMainCountValid(MainCardIds.Count, Construction.Method);
        if (!validMainCount)
        {
            return RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.IncorrectMainCardCount,
                $"The main-card count {MainCardIds.Count} does not satisfy rules {RulesVersion} for {Construction.Method}.");
        }

        if (ExtraCardIds.Count != RewardPoolCatalog.ExtraDeckSize)
        {
            return RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.IncorrectExtraCardCount,
                $"A manual reward pool requires exactly {RewardPoolCatalog.ExtraDeckSize} extra-card IDs; received {ExtraCardIds.Count}.");
        }

        if (MainCardIds.Any(string.IsNullOrWhiteSpace)
            || ExtraCardIds.Any(string.IsNullOrWhiteSpace))
        {
            return RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.EmptyCardId,
                "Reward-pool card IDs must not be empty.");
        }

        if (_mainCardIdSet.Count != MainCardIds.Count)
        {
            return RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.DuplicateMainCardId,
                "The manual main-card list contains a duplicate ModelId.");
        }

        if (_extraCardIdSet.Count != ExtraCardIds.Count)
        {
            return RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.DuplicateExtraCardId,
                "The manual extra-card list contains a duplicate ModelId.");
        }

        try
        {
            var requiredMain = RewardPoolCatalog.RequiredMainCardIds;
            var missingMain = requiredMain.FirstOrDefault(id => !_mainCardIdSet.Contains(id));
            if (missingMain is not null)
            {
                return RewardPoolValidationResult.Invalid(
                    RewardPoolValidationCode.MissingRequiredMainCard,
                    $"The manual reward pool is missing required main card '{missingMain}'.");
            }

            var requiredExtra = RewardPoolCatalog.RequiredExtraCardIds;
            var missingExtra = requiredExtra.FirstOrDefault(id => !_extraCardIdSet.Contains(id));
            if (missingExtra is not null)
            {
                return RewardPoolValidationResult.Invalid(
                    RewardPoolValidationCode.MissingRequiredExtraCard,
                    $"The manual reward pool is missing required extra card '{missingExtra}'.");
            }

            var mainCandidates = RewardPoolCatalog.GetMainRewardCandidates(IsMultiplayerConstruction);
            var mainById = mainCandidates.ToDictionary(RewardPoolCatalog.GetId, StringComparer.Ordinal);
            var allowedMain = requiredMain.Concat(mainById.Keys).ToHashSet(StringComparer.Ordinal);
            var extraById = RewardPoolCatalog.GetAllExtraRewardCandidates()
                .ToDictionary(RewardPoolCatalog.GetId, StringComparer.Ordinal);

            var invalidMain = MainCardIds.FirstOrDefault(id => !allowedMain.Contains(id));
            if (invalidMain is not null)
            {
                var code = extraById.ContainsKey(invalidMain)
                    ? RewardPoolValidationCode.MainCardInWrongCatalog
                    : RewardPoolValidationCode.UnknownMainCardId;
                return RewardPoolValidationResult.Invalid(
                    code,
                    $"'{invalidMain}' is not a legal main-card construction entry.");
            }

            var invalidExtra = ExtraCardIds.FirstOrDefault(id => !extraById.ContainsKey(id));
            if (invalidExtra is not null)
            {
                var code = allowedMain.Contains(invalidExtra)
                    ? RewardPoolValidationCode.ExtraCardInWrongCatalog
                    : RewardPoolValidationCode.UnknownExtraCardId;
                return RewardPoolValidationResult.Invalid(
                    code,
                    $"'{invalidExtra}' is not a legal extra-card construction entry.");
            }

            var selectedRewardCards = MainCardIds
                .Where(id => !RewardPoolCatalog.IsRequiredMainCard(id))
                .Select(id => mainById[id])
                .ToList();
            var rarityResult = ValidateRarity(
                selectedRewardCards,
                CardRarity.Common,
                RewardPoolValidationCode.InsufficientCommonCards);
            if (!rarityResult.IsValid)
                return rarityResult;

            rarityResult = ValidateRarity(
                selectedRewardCards,
                CardRarity.Uncommon,
                RewardPoolValidationCode.InsufficientUncommonCards);
            if (!rarityResult.IsValid)
                return rarityResult;

            rarityResult = ValidateRarity(
                selectedRewardCards,
                CardRarity.Rare,
                RewardPoolValidationCode.InsufficientRareCards);
            return rarityResult.IsValid ? RewardPoolConstruction.ValidateSnapshot(this) : rarityResult;
        }
        catch (Exception ex)
        {
            return RewardPoolValidationResult.Invalid(
                RewardPoolValidationCode.CatalogUnavailable,
                $"The reward-pool catalog is unavailable: {ex.Message}");
        }
    }

    public RewardPoolDefinition Clone() =>
        new(SchemaVersion, BuildMode, MainCardIds, ExtraCardIds, Construction, RulesVersion);

    public RewardPoolDefinition WithConstruction(RewardPoolConstructionMetadata construction) =>
        new(SchemaVersion, BuildMode, MainCardIds, ExtraCardIds,
            (construction ?? new RewardPoolConstructionMetadata()) with { RulesVersion = RulesVersion }, RulesVersion);

    public bool IsMultiplayerConstruction => Construction.Method == RewardPoolConstructionMethod.MultiplayerDraft;

    /// <summary>
    /// Tests membership in the reward-producing main cards. The five required
    /// starting cards are construction members but never rewards; Manual mode
    /// additionally requires the card to be in the saved selection.
    /// </summary>
    public bool ContainsMainReward(CardModel card) =>
        card is not null && ContainsMainReward(RewardPoolCatalog.GetId(card));

    public bool ContainsMainReward(ModelId cardId) =>
        ContainsMainReward(cardId?.ToString());

    public bool ContainsMainReward(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return false;
        var normalized = cardId.Trim();
        return !RewardPoolCatalog.IsRequiredMainCard(normalized)
            && (!IsEnabled || _mainCardIdSet.Contains(normalized));
    }

    public bool ContainsMainCard(CardModel card) =>
        card is not null && ContainsMainCard(RewardPoolCatalog.GetId(card));

    public bool ContainsMainCard(string cardId) =>
        !string.IsNullOrWhiteSpace(cardId)
        && (!IsEnabled || _mainCardIdSet.Contains(cardId.Trim()));

    public bool ContainsExtra(CardModel card) =>
        card is not null && ContainsExtra(RewardPoolCatalog.GetId(card));

    public bool ContainsExtra(ModelId cardId) =>
        ContainsExtra(cardId?.ToString());

    public bool ContainsExtra(string cardId) =>
        !string.IsNullOrWhiteSpace(cardId)
        && (!IsEnabled || _extraCardIdSet.Contains(cardId.Trim()));

    /// <summary>
    /// Tests membership in the reward-producing Extra Deck cards. Required
    /// starting Extra Deck cards are construction members but never rewards;
    /// Manual mode additionally requires the card to be in the saved build.
    /// </summary>
    public bool ContainsExtraReward(CardModel card) =>
        card is not null && ContainsExtraReward(RewardPoolCatalog.GetId(card));

    public bool ContainsExtraReward(ModelId cardId) =>
        ContainsExtraReward(cardId?.ToString());

    public bool ContainsExtraReward(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return false;

        var normalized = cardId.Trim();
        return !RewardPoolCatalog.IsRequiredExtraCard(normalized)
            && (!IsEnabled || _extraCardIdSet.Contains(normalized));
    }

    /// <summary>
    /// Returns whether this explicit manual definition contains at least one
    /// main card owned by the requested character pool. Standard mode has no
    /// explicit selection and therefore returns false.
    /// </summary>
    public bool ContainsOrigin(RewardPoolCardOrigin origin) =>
        IsEnabled
        && MainCardIds.Any(id =>
            RewardPoolCatalog.TryGetMainCardOrigin(id, out var actualOrigin)
            && actualOrigin == origin);

    /// <summary>
    /// Resolves the reward-producing main-card IDs to canonical models in
    /// the definition's saved order. Standard mode returns an empty list.
    /// </summary>
    public IReadOnlyList<CardModel> GetSelectedMainRewardCards()
    {
        if (!IsEnabled)
            return [];

        return MainCardIds
            .Where(id => !RewardPoolCatalog.IsRequiredMainCard(id))
            .Select(id => RewardPoolCatalog.GetMainRewardCandidate(id, IsMultiplayerConstruction))
            .Where(card => card is not null)
            .ToArray();
    }

    /// <summary>
    /// Resolves all main-card construction entries, including the five
    /// required starting cards. Standard mode returns an empty list.
    /// </summary>
    public IReadOnlyList<CardModel> GetSelectedMainCards()
    {
        if (!IsEnabled)
            return [];

        return MainCardIds
            .Select(id => RewardPoolCatalog.GetMainBuildCandidate(id, IsMultiplayerConstruction))
            .Where(card => card is not null)
            .ToArray();
    }

    /// <summary>
    /// Resolves all 10 selected extra-deck entries. Standard mode returns an
    /// empty list.
    /// </summary>
    public IReadOnlyList<CardModel> GetSelectedExtraCards()
    {
        if (!IsEnabled)
            return [];

        var cards = new List<CardModel>(ExtraCardIds.Count);
        foreach (var id in ExtraCardIds)
        {
            if (RewardPoolCatalog.TryGetExtraRewardCandidate(id, out var card))
                cards.Add(card);
        }

        return cards;
    }

    internal static RewardPoolDefinition CreateManualUnchecked(
        IEnumerable<string> mainCardIds,
        IEnumerable<string> extraCardIds,
        RewardPoolConstructionMetadata construction = null) =>
        new(CurrentSchemaVersion, RewardPoolBuildMode.Manual, mainCardIds, extraCardIds, construction);

    private static RewardPoolValidationResult ValidateRarity(
        IReadOnlyCollection<CardModel> cards,
        CardRarity rarity,
        RewardPoolValidationCode errorCode)
    {
        var count = cards.Count(card => card.Rarity == rarity);
        return count >= RewardPoolCatalog.MinimumPerRewardRarity
            ? RewardPoolValidationResult.Valid
            : RewardPoolValidationResult.Invalid(
                errorCode,
                $"A manual reward pool requires at least {RewardPoolCatalog.MinimumPerRewardRarity} {rarity} reward cards; received {count}.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class Payload
    {
        public int SchemaVersion { get; set; }
        public int RulesVersion { get; set; }
        public RewardPoolBuildMode BuildMode { get; set; }
        public List<string> MainCardIds { get; set; } = [];
        public List<string> ExtraCardIds { get; set; } = [];
        public RewardPoolConstructionMetadata Construction { get; set; }
    }
}
