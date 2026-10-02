using System.Text.Json;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

internal enum RewardPoolPresetIssueCode
{
    InvalidPresetName,
    IncorrectMainCardCount,
    IncorrectExtraCardCount,
    EmptyCardId,
    DuplicateMainCardId,
    DuplicateExtraCardId,
    UnknownMainCardId,
    UnknownExtraCardId,
    MainCardInWrongCatalog,
    ExtraCardInWrongCatalog,
    InsufficientCommonCards,
    InsufficientUncommonCards,
    InsufficientRareCards,
    CatalogUnavailable,
    LibraryUnavailable,
    InvalidDefinition,
    InvalidConstruction,
    ConstructionBudgetExceeded
}

internal readonly record struct RewardPoolPresetIssue(
    RewardPoolPresetIssueCode Code,
    string Message);

internal enum RewardPoolPresetOperationCode
{
    None,
    InvalidPresetName,
    InvalidPresetId,
    DuplicatePresetId,
    PresetNotFound,
    MissingActivePreset,
    UnsupportedLibraryVersion,
    InvalidLibrary,
    NoSavedLibrary,
    PersistenceError
}

internal readonly record struct RewardPoolPresetOperationResult(
    bool IsValid,
    RewardPoolPresetOperationCode Code,
    string Message)
{
    internal static RewardPoolPresetOperationResult Valid { get; } =
        new(true, RewardPoolPresetOperationCode.None, string.Empty);

    internal static RewardPoolPresetOperationResult Invalid(
        RewardPoolPresetOperationCode code,
        string message) =>
        new(false, code, message ?? string.Empty);

    public override string ToString() =>
        IsValid ? "Valid" : $"{Code}: {Message}";
}

internal readonly record struct RewardPoolPresetReadState(
    RewardPoolPresetLibrary Library,
    RewardPoolPresetOperationResult? LoadFailure,
    bool StandardSelectedForProcess);

internal sealed record RewardPoolPresetDraft
{
    internal RewardPoolPresetDraft(
        Guid id,
        string name,
        IReadOnlyList<string> mainCardIds,
        IReadOnlyList<string> extraCardIds,
        DateTime createdUtc,
        DateTime updatedUtc,
        RewardPoolConstructionMethod constructionMethod = RewardPoolConstructionMethod.Free,
        int rulesVersion = RewardPoolSizePolicy.CurrentRulesVersion)
    {
        Id = id;
        Name = name?.Trim() ?? string.Empty;
        MainCardIds = SnapshotIds(mainCardIds);
        ExtraCardIds = SnapshotIds(extraCardIds);
        CreatedUtc = NormalizeUtc(createdUtc);
        UpdatedUtc = NormalizeUtc(updatedUtc);
        ConstructionMethod = constructionMethod;
        RulesVersion = rulesVersion;
    }

    internal Guid Id { get; }
    internal string Name { get; }
    internal IReadOnlyList<string> MainCardIds { get; }
    internal IReadOnlyList<string> ExtraCardIds { get; }
    internal DateTime CreatedUtc { get; }
    internal DateTime UpdatedUtc { get; }
    internal RewardPoolConstructionMethod ConstructionMethod { get; }
    internal int RulesVersion { get; }

    internal RewardPoolPresetDraft Snapshot() =>
        new(Id, Name, MainCardIds, ExtraCardIds, CreatedUtc, UpdatedUtc, ConstructionMethod, RulesVersion);

    private static IReadOnlyList<string> SnapshotIds(IEnumerable<string> ids) =>
        Array.AsReadOnly((ids ?? []).Select(id => id ?? string.Empty).ToArray());

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

internal sealed record RewardPoolPresetLibrary
{
    internal const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    internal RewardPoolPresetLibrary(
        int schemaVersion,
        Guid? activePresetId,
        IReadOnlyList<RewardPoolPresetDraft> presets)
    {
        SchemaVersion = schemaVersion;
        ActivePresetId = activePresetId;
        Presets = Array.AsReadOnly((presets ?? [])
            .Where(preset => preset is not null)
            .Select(preset => preset.Snapshot())
            .OrderByDescending(preset => preset.UpdatedUtc)
            .ThenBy(preset => preset.Id)
            .ToArray());
    }

    internal static RewardPoolPresetLibrary Empty { get; } =
        new(CurrentSchemaVersion, null, []);

    internal int SchemaVersion { get; }
    internal Guid? ActivePresetId { get; }
    internal IReadOnlyList<RewardPoolPresetDraft> Presets { get; }

    internal RewardPoolPresetLibrary Snapshot() =>
        new(SchemaVersion, ActivePresetId, Presets);

    internal string Serialize()
    {
        var payload = new LibraryPayload
        {
            SchemaVersion = SchemaVersion,
            ActivePresetId = ActivePresetId,
            Presets = Presets.Select(preset => new PresetPayload
            {
                Id = preset.Id,
                Name = preset.Name,
                MainCardIds = preset.MainCardIds.ToList(),
                ExtraCardIds = preset.ExtraCardIds.ToList(),
                CreatedUtc = preset.CreatedUtc,
                UpdatedUtc = preset.UpdatedUtc,
                ConstructionMethod = preset.ConstructionMethod,
                RulesVersion = preset.RulesVersion
            }).ToList()
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    internal static bool TryDeserialize(
        string json,
        out RewardPoolPresetLibrary library,
        out RewardPoolPresetOperationResult validation)
    {
        library = Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            validation = InvalidLibrary("Reward-pool preset library JSON is empty.");
            return false;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<LibraryPayload>(json, JsonOptions);
            if (payload is null)
            {
                validation = InvalidLibrary(
                    "Reward-pool preset library JSON did not contain an object.");
                return false;
            }

            if (payload.SchemaVersion is < 1 or > CurrentSchemaVersion)
            {
                validation = RewardPoolPresetOperationResult.Invalid(
                    RewardPoolPresetOperationCode.UnsupportedLibraryVersion,
                    $"Unsupported reward-pool preset library version {payload.SchemaVersion}; expected {CurrentSchemaVersion}.");
                return false;
            }

            if (payload.Presets is null)
            {
                validation = InvalidLibrary("Reward-pool preset library has no preset list.");
                return false;
            }

            var presets = new List<RewardPoolPresetDraft>(payload.Presets.Count);
            var ids = new HashSet<Guid>();
            foreach (var item in payload.Presets)
            {
                if (item is null)
                {
                    validation = InvalidLibrary("Reward-pool preset library contains a null preset.");
                    return false;
                }

                if (item.Id == Guid.Empty)
                {
                    validation = RewardPoolPresetOperationResult.Invalid(
                        RewardPoolPresetOperationCode.InvalidPresetId,
                        "Reward-pool preset IDs must not be empty.");
                    return false;
                }

                if (!ids.Add(item.Id))
                {
                    validation = RewardPoolPresetOperationResult.Invalid(
                        RewardPoolPresetOperationCode.DuplicatePresetId,
                        $"Reward-pool preset library contains duplicate ID '{item.Id}'.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    validation = RewardPoolPresetOperationResult.Invalid(
                        RewardPoolPresetOperationCode.InvalidPresetName,
                        $"Reward-pool preset '{item.Id}' has an empty name.");
                    return false;
                }

                if (item.MainCardIds is null || item.ExtraCardIds is null)
                {
                    validation = InvalidLibrary(
                        $"Reward-pool preset '{item.Id}' has a missing card-ID list.");
                    return false;
                }

                if (payload.SchemaVersion == CurrentSchemaVersion
                    && item.RulesVersion is < RewardPoolSizePolicy.LegacyRulesVersion or > RewardPoolSizePolicy.CurrentRulesVersion)
                {
                    validation = InvalidLibrary($"Reward-pool preset '{item.Id}' uses unsupported construction rules.");
                    return false;
                }

                if (item.CreatedUtc == default
                    || item.UpdatedUtc == default
                    || item.CreatedUtc.ToUniversalTime() > item.UpdatedUtc.ToUniversalTime())
                {
                    validation = InvalidLibrary(
                        $"Reward-pool preset '{item.Id}' has invalid timestamps.");
                    return false;
                }

                presets.Add(new RewardPoolPresetDraft(
                    item.Id,
                    item.Name,
                    item.MainCardIds,
                    item.ExtraCardIds,
                    item.CreatedUtc,
                    item.UpdatedUtc,
                    item.ConstructionMethod,
                    payload.SchemaVersion < CurrentSchemaVersion
                        ? RewardPoolSizePolicy.LegacyRulesVersion : item.RulesVersion));
            }

            if (payload.ActivePresetId.HasValue && !ids.Contains(payload.ActivePresetId.Value))
            {
                validation = RewardPoolPresetOperationResult.Invalid(
                    RewardPoolPresetOperationCode.MissingActivePreset,
                    $"Active reward-pool preset '{payload.ActivePresetId}' does not exist in the library.");
                return false;
            }

            library = new RewardPoolPresetLibrary(
                CurrentSchemaVersion,
                payload.ActivePresetId,
                presets);
            validation = RewardPoolPresetOperationResult.Valid;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            validation = InvalidLibrary(
                $"Reward-pool preset library JSON could not be read: {ex.Message}");
            return false;
        }
    }

    internal RewardPoolPresetOperationResult ValidateStructure()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            return RewardPoolPresetOperationResult.Invalid(
                RewardPoolPresetOperationCode.UnsupportedLibraryVersion,
                $"Unsupported reward-pool preset library version {SchemaVersion}; expected {CurrentSchemaVersion}.");
        }

        var ids = new HashSet<Guid>();
        foreach (var preset in Presets)
        {
            if (preset.RulesVersion is < RewardPoolSizePolicy.LegacyRulesVersion or > RewardPoolSizePolicy.CurrentRulesVersion)
                return InvalidLibrary($"Reward-pool preset '{preset.Id}' uses unsupported construction rules.");
            if (preset.Id == Guid.Empty)
            {
                return RewardPoolPresetOperationResult.Invalid(
                    RewardPoolPresetOperationCode.InvalidPresetId,
                    "Reward-pool preset IDs must not be empty.");
            }

            if (!ids.Add(preset.Id))
            {
                return RewardPoolPresetOperationResult.Invalid(
                    RewardPoolPresetOperationCode.DuplicatePresetId,
                    $"Reward-pool preset library contains duplicate ID '{preset.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(preset.Name))
            {
                return RewardPoolPresetOperationResult.Invalid(
                    RewardPoolPresetOperationCode.InvalidPresetName,
                    $"Reward-pool preset '{preset.Id}' has an empty name.");
            }

            if (preset.CreatedUtc == default
                || preset.UpdatedUtc == default
                || preset.CreatedUtc > preset.UpdatedUtc)
            {
                return InvalidLibrary(
                    $"Reward-pool preset '{preset.Id}' has invalid timestamps.");
            }
        }

        return ActivePresetId.HasValue && !ids.Contains(ActivePresetId.Value)
            ? RewardPoolPresetOperationResult.Invalid(
                RewardPoolPresetOperationCode.MissingActivePreset,
                $"Active reward-pool preset '{ActivePresetId}' does not exist in the library.")
            : RewardPoolPresetOperationResult.Valid;
    }

    private static RewardPoolPresetOperationResult InvalidLibrary(string message) =>
        RewardPoolPresetOperationResult.Invalid(
            RewardPoolPresetOperationCode.InvalidLibrary,
            message);

    private sealed class LibraryPayload
    {
        public int SchemaVersion { get; set; }
        public Guid? ActivePresetId { get; set; }
        public List<PresetPayload> Presets { get; set; }
    }

    private sealed class PresetPayload
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> MainCardIds { get; set; }
        public List<string> ExtraCardIds { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public RewardPoolConstructionMethod ConstructionMethod { get; set; }
        public int RulesVersion { get; set; } = RewardPoolSizePolicy.CurrentRulesVersion;
    }
}

internal sealed record RewardPoolPresetValidation
{
    internal RewardPoolPresetValidation(
        bool isValid,
        int mainCardCount,
        int extraCardCount,
        int commonCount,
        int uncommonCount,
        int rareCount,
        IReadOnlyList<RewardPoolPresetIssue> issues,
        RewardPoolDefinition definition)
    {
        IsValid = isValid;
        MainCardCount = mainCardCount;
        ExtraCardCount = extraCardCount;
        CommonCount = commonCount;
        UncommonCount = uncommonCount;
        RareCount = rareCount;
        Issues = Array.AsReadOnly((issues ?? []).ToArray());
        Definition = definition?.Clone();
    }

    internal static RewardPoolPresetValidation Standard { get; } =
        new(true, 0, 0, 0, 0, 0, [], RewardPoolDefinition.Standard);

    internal bool IsValid { get; }
    internal int MainCardCount { get; }
    internal int ExtraCardCount { get; }
    internal int CommonCount { get; }
    internal int UncommonCount { get; }
    internal int RareCount { get; }
    internal IReadOnlyList<RewardPoolPresetIssue> Issues { get; }
    internal RewardPoolDefinition Definition { get; }
}
