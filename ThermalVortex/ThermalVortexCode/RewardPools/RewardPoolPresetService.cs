using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

/// <summary>
/// Owns durable preset-library state. Draft persistence, active selection, and
/// run staging are deliberately separate operations: invalid drafts remain
/// durable, while only a fully validated definition can enter the pending run
/// setup consumed by the launch patches.
/// </summary>
internal static class RewardPoolPresetService
{
    private static readonly object StateLock = new();
    private static IRewardPoolPresetStore _store;
    private static RewardPoolPresetLibrary _library = RewardPoolPresetLibrary.Empty;
    private static RewardPoolPresetOperationResult? _libraryLoadFailure;
    private static RewardPoolPendingContext _selectionContext = RewardPoolPendingContext.Unscoped;
    private static bool _initialized;
    // An explicit fallback survives pending-setup consumption and screen/run
    // transitions, but never converts an unreadable library into a writable one.
    private static bool _standardSelectedWhileUnavailable;

    internal static void Initialize()
    {
        lock (StateLock)
            InitializeLocked();
    }

    internal static RewardPoolPresetLibrary GetLibrarySnapshot()
    {
        lock (StateLock)
        {
            InitializeLocked();
            ThrowIfLibraryUnavailableLocked();
            return _library.Snapshot();
        }
    }

    internal static RewardPoolPresetReadState GetReadState()
    {
        lock (StateLock)
        {
            InitializeLocked();
            return new RewardPoolPresetReadState(
                _library.Snapshot(), _libraryLoadFailure, _standardSelectedWhileUnavailable);
        }
    }

    internal static bool TryGet(Guid id, out RewardPoolPresetDraft preset)
    {
        lock (StateLock)
        {
            InitializeLocked();
            var stored = _library.Presets.FirstOrDefault(candidate => candidate.Id == id);
            preset = stored?.Snapshot();
            return preset is not null;
        }
    }

    internal static bool TryGetActive(out RewardPoolPresetDraft preset)
    {
        lock (StateLock)
        {
            InitializeLocked();
            if (!_library.ActivePresetId.HasValue)
            {
                preset = null;
                return false;
            }

            var stored = _library.Presets.FirstOrDefault(
                candidate => candidate.Id == _library.ActivePresetId.Value);
            preset = stored?.Snapshot();
            return preset is not null;
        }
    }

    internal static string GetDefaultName()
    {
        lock (StateLock)
        {
            InitializeLocked();
            var names = _library.Presets
                .Select(preset => preset.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var prefix = GetLocalizedText("新预设", "New Preset");
            for (var index = 1; ; index++)
            {
                var candidate = $"{prefix} {index}";
                if (!names.Contains(candidate))
                    return candidate;
            }
        }
    }

    internal static RewardPoolPresetValidation Validate(RewardPoolPresetDraft preset)
    {
        if (preset is null)
        {
            var missing = new RewardPoolPresetIssue(
                RewardPoolPresetIssueCode.InvalidDefinition,
                "The reward-pool preset draft is missing.");
            return new RewardPoolPresetValidation(false, 0, 0, 0, 0, 0, [missing], null);
        }

        var mainIds = preset.MainCardIds ?? [];
        var extraIds = preset.ExtraCardIds ?? [];
        var issues = new List<RewardPoolPresetIssue>();
        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            issues.Add(new RewardPoolPresetIssue(
                RewardPoolPresetIssueCode.InvalidPresetName,
                "A reward-pool preset name must not be empty."));
        }

        if (!RewardPoolSizePolicy.IsCurrentSelectionCountValid(mainIds.Count, preset.ConstructionMethod))
        {
            issues.Add(new RewardPoolPresetIssue(
                RewardPoolPresetIssueCode.IncorrectMainCardCount,
                $"A preset requires {RewardPoolSizePolicy.MinSelectableMain}–{RewardPoolSizePolicy.MaxSelectableMain} selectable main cards; received {mainIds.Count}."));
        }

        if (extraIds.Count != RewardPoolCatalog.SelectableExtraCardCount)
        {
            issues.Add(new RewardPoolPresetIssue(
                RewardPoolPresetIssueCode.IncorrectExtraCardCount,
                $"A preset requires exactly {RewardPoolCatalog.SelectableExtraCardCount} selectable extra cards; received {extraIds.Count}."));
        }

        AddEmptyIdIssue(mainIds, "main", issues);
        AddEmptyIdIssue(extraIds, "extra", issues);
        AddDuplicateIssues(
            mainIds,
            RewardPoolPresetIssueCode.DuplicateMainCardId,
            "main-card",
            issues);
        AddDuplicateIssues(
            extraIds,
            RewardPoolPresetIssueCode.DuplicateExtraCardId,
            "extra-card",
            issues);

        var commonCount = 0;
        var uncommonCount = 0;
        var rareCount = 0;
        try
        {
            var mainCandidates = RewardPoolCatalog.GetSelectableMainCandidates();
            var mainById = mainCandidates.ToDictionary(RewardPoolCatalog.GetId, StringComparer.Ordinal);
            var selectableExtraIds = RewardPoolCatalog.GetSelectableExtraCandidates()
                .Select(RewardPoolCatalog.GetId)
                .ToHashSet(StringComparer.Ordinal);
            var allExtraIds = RewardPoolCatalog.GetAllExtraRewardCandidates()
                .Select(RewardPoolCatalog.GetId)
                .ToHashSet(StringComparer.Ordinal);
            var requiredMainIds = RewardPoolCatalog.RequiredMainCardIds
                .ToHashSet(StringComparer.Ordinal);
            var requiredExtraIds = RewardPoolCatalog.RequiredExtraCardIds
                .ToHashSet(StringComparer.Ordinal);

            foreach (var rawId in mainIds)
            {
                var id = rawId?.Trim() ?? string.Empty;
                if (id.Length == 0)
                    continue;

                if (mainById.TryGetValue(id, out var card))
                {
                    switch (card.Rarity)
                    {
                        case CardRarity.Common:
                            commonCount++;
                            break;
                        case CardRarity.Uncommon:
                            uncommonCount++;
                            break;
                        case CardRarity.Rare:
                            rareCount++;
                            break;
                    }
                    continue;
                }

                var code = allExtraIds.Contains(id)
                    ? RewardPoolPresetIssueCode.MainCardInWrongCatalog
                    : requiredMainIds.Contains(id)
                        ? RewardPoolPresetIssueCode.DuplicateMainCardId
                        : RewardPoolPresetIssueCode.UnknownMainCardId;
                var detail = requiredMainIds.Contains(id)
                    ? $"Required main card '{id}' is fixed and cannot occupy a selectable preset slot."
                    : allExtraIds.Contains(id)
                        ? $"Extra-deck card '{id}' cannot occupy a main-card preset slot."
                        : $"Main-card preset entry '{id}' is unavailable.";
                issues.Add(new RewardPoolPresetIssue(code, detail));
            }

            foreach (var rawId in extraIds)
            {
                var id = rawId?.Trim() ?? string.Empty;
                if (id.Length == 0 || selectableExtraIds.Contains(id))
                    continue;

                var isMain = mainById.ContainsKey(id) || requiredMainIds.Contains(id);
                var code = requiredExtraIds.Contains(id)
                    ? RewardPoolPresetIssueCode.DuplicateExtraCardId
                    : isMain
                        ? RewardPoolPresetIssueCode.ExtraCardInWrongCatalog
                        : RewardPoolPresetIssueCode.UnknownExtraCardId;
                var detail = requiredExtraIds.Contains(id)
                    ? $"Required extra card '{id}' is fixed and cannot occupy a selectable preset slot."
                    : isMain
                        ? $"Main-deck card '{id}' cannot occupy an extra-card preset slot."
                        : $"Extra-card preset entry '{id}' is unavailable.";
                issues.Add(new RewardPoolPresetIssue(code, detail));
            }

            AddRarityIssue(
                commonCount,
                CardRarity.Common,
                RewardPoolPresetIssueCode.InsufficientCommonCards,
                issues);
            AddRarityIssue(
                uncommonCount,
                CardRarity.Uncommon,
                RewardPoolPresetIssueCode.InsufficientUncommonCards,
                issues);
            AddRarityIssue(
                rareCount,
                CardRarity.Rare,
                RewardPoolPresetIssueCode.InsufficientRareCards,
                issues);
        }
        catch (Exception ex)
        {
            issues.Add(new RewardPoolPresetIssue(
                RewardPoolPresetIssueCode.CatalogUnavailable,
                $"The reward-pool catalog is unavailable: {ex.Message}"));
        }

        RewardPoolConstructionMetadata construction = new();
        if (preset.ConstructionMethod == RewardPoolConstructionMethod.Genesis)
        {
            try
            {
                construction = RewardPoolConstruction.Price(mainIds.Concat(extraIds));
                if (construction.TotalPoints > construction.Budget)
                    issues.Add(new RewardPoolPresetIssue(RewardPoolPresetIssueCode.ConstructionBudgetExceeded,
                        $"Genesis: {construction.TotalPoints}/{construction.Budget}."));
            }
            catch (Exception exception)
            {
                issues.Add(new RewardPoolPresetIssue(RewardPoolPresetIssueCode.InvalidConstruction, exception.Message));
            }
        }
        else if (preset.ConstructionMethod != RewardPoolConstructionMethod.Free)
        {
            issues.Add(new RewardPoolPresetIssue(RewardPoolPresetIssueCode.InvalidConstruction,
                GetLocalizedText("三选一必须完成本次选牌后开局；个人预设请保存为自由构筑。",
                    "Finish a new draft to embark. Personal presets must use Free or Genesis construction.")));
        }

        RewardPoolDefinition definition = null;
        if (issues.Count == 0
            && !RewardPoolCatalog.CreateConstructedDefinition(
                mainIds,
                extraIds,
                construction,
                out definition,
                out var strictValidation))
        {
            issues.Add(FromDefinitionValidation(strictValidation));
            definition = null;
        }

        return new RewardPoolPresetValidation(
            issues.Count == 0 && definition is not null,
            mainIds.Count,
            extraIds.Count,
            commonCount,
            uncommonCount,
            rareCount,
            issues,
            definition);
    }

    internal static bool TryCreate(
        string name,
        IEnumerable<string> mainCardIds,
        IEnumerable<string> extraCardIds,
        out RewardPoolPresetDraft preset,
        out RewardPoolPresetOperationResult result,
        RewardPoolConstructionMethod constructionMethod = RewardPoolConstructionMethod.Free)
    {
        preset = null;
        if (!TryNormalizeName(name, out var normalizedName, out result))
            return false;

        RewardPoolPresetDraft committed;
        lock (StateLock)
        {
            InitializeLocked();
            var now = NextTimestampLocked();
            committed = new RewardPoolPresetDraft(
                Guid.NewGuid(),
                normalizedName,
                (mainCardIds ?? []).ToArray(),
                (extraCardIds ?? []).ToArray(),
                now,
                now,
                constructionMethod);
            var candidate = NewLibrary(
                _library.ActivePresetId,
                _library.Presets.Append(committed));
            if (!TryCommitLocked(candidate, out result))
                return false;
        }

        preset = committed.Snapshot();
        return true;
    }

    internal static bool TrySave(
        RewardPoolPresetDraft preset,
        out RewardPoolPresetDraft saved,
        out RewardPoolPresetOperationResult result)
    {
        saved = null;
        if (preset is null)
        {
            result = RewardPoolPresetOperationResult.Invalid(
                RewardPoolPresetOperationCode.InvalidPresetId,
                "The reward-pool preset draft is missing.");
            return false;
        }

        if (preset.Id == Guid.Empty)
        {
            result = RewardPoolPresetOperationResult.Invalid(
                RewardPoolPresetOperationCode.InvalidPresetId,
                "A saved reward-pool preset requires a stable non-empty ID.");
            return false;
        }

        if (!TryNormalizeName(preset.Name, out var normalizedName, out result))
            return false;

        RewardPoolPresetDraft committed;
        RewardPoolPendingContext context;
        var synchronizeActive = false;
        lock (StateLock)
        {
            InitializeLocked();
            var existing = _library.Presets.FirstOrDefault(candidate => candidate.Id == preset.Id);
            if (existing is null)
            {
                result = PresetNotFound(preset.Id);
                return false;
            }

            committed = new RewardPoolPresetDraft(
                existing.Id,
                normalizedName,
                preset.MainCardIds,
                preset.ExtraCardIds,
                existing.CreatedUtc,
                NextTimestampLocked(),
                preset.ConstructionMethod,
                RewardPoolSizePolicy.CurrentRulesVersion);
            var presets = _library.Presets
                .Select(candidate => candidate.Id == existing.Id ? committed : candidate)
                .ToArray();
            var candidateLibrary = NewLibrary(_library.ActivePresetId, presets);
            if (!TryCommitLocked(candidateLibrary, out result))
                return false;

            synchronizeActive = _library.ActivePresetId == committed.Id;
            context = _selectionContext;
        }

        saved = committed.Snapshot();
        if (synchronizeActive)
            SynchronizePreset(saved, context, mirrorLegacy: true);
        return true;
    }

    internal static bool TryDuplicate(
        Guid id,
        out RewardPoolPresetDraft copy,
        out RewardPoolPresetOperationResult result)
    {
        copy = null;
        RewardPoolPresetDraft committed;
        lock (StateLock)
        {
            InitializeLocked();
            var source = _library.Presets.FirstOrDefault(candidate => candidate.Id == id);
            if (source is null)
            {
                result = PresetNotFound(id);
                return false;
            }

            var now = NextTimestampLocked();
            committed = new RewardPoolPresetDraft(
                Guid.NewGuid(),
                source.Name + GetLocalizedText(" - 副本", " - Copy"),
                source.MainCardIds,
                source.ExtraCardIds,
                now,
                now,
                source.ConstructionMethod,
                source.RulesVersion);
            var candidate = NewLibrary(
                _library.ActivePresetId,
                _library.Presets.Append(committed));
            if (!TryCommitLocked(candidate, out result))
                return false;
        }

        copy = committed.Snapshot();
        return true;
    }

    internal static bool TryRename(
        Guid id,
        string name,
        out RewardPoolPresetDraft renamed,
        out RewardPoolPresetOperationResult result)
    {
        renamed = null;
        if (!TryNormalizeName(name, out var normalizedName, out result))
            return false;

        RewardPoolPresetDraft committed;
        lock (StateLock)
        {
            InitializeLocked();
            var source = _library.Presets.FirstOrDefault(candidate => candidate.Id == id);
            if (source is null)
            {
                result = PresetNotFound(id);
                return false;
            }

            committed = new RewardPoolPresetDraft(
                source.Id,
                normalizedName,
                source.MainCardIds,
                source.ExtraCardIds,
                source.CreatedUtc,
                NextTimestampLocked(),
                source.ConstructionMethod,
                source.RulesVersion);
            var presets = _library.Presets
                .Select(candidate => candidate.Id == id ? committed : candidate)
                .ToArray();
            if (!TryCommitLocked(NewLibrary(_library.ActivePresetId, presets), out result))
                return false;
        }

        renamed = committed.Snapshot();
        return true;
    }

    internal static bool TryDelete(Guid id, out RewardPoolPresetOperationResult result)
    {
        RewardPoolPendingContext context;
        var deletedActive = false;
        lock (StateLock)
        {
            InitializeLocked();
            if (!_library.Presets.Any(candidate => candidate.Id == id))
            {
                result = PresetNotFound(id);
                return false;
            }

            deletedActive = _library.ActivePresetId == id;
            var activeId = deletedActive ? null : _library.ActivePresetId;
            var presets = _library.Presets.Where(candidate => candidate.Id != id).ToArray();
            if (!TryCommitLocked(NewLibrary(activeId, presets), out result))
                return false;
            context = _selectionContext;
        }

        if (deletedActive)
            RewardPoolSetupService.UseStandard(context);
        return true;
    }

    internal static bool TrySelect(
        Guid? id,
        out RewardPoolPresetValidation validation,
        out RewardPoolPresetOperationResult result) =>
        TrySelectCore(id, null, out validation, out result);

    internal static bool TrySelect(
        Guid? id,
        RewardPoolPendingContext context,
        out RewardPoolPresetValidation validation,
        out RewardPoolPresetOperationResult result) =>
        TrySelectCore(id, context, out validation, out result);

    internal static bool RestoreSelection(
        RewardPoolPendingContext context,
        out RewardPoolPresetValidation validation)
    {
        RewardPoolPresetDraft active;
        RewardPoolPresetOperationResult? loadFailure;
        bool standardSelected;
        lock (StateLock)
        {
            InitializeLocked();
            _selectionContext = context;
            loadFailure = _libraryLoadFailure;
            standardSelected = _standardSelectedWhileUnavailable;
            active = _library.ActivePresetId.HasValue
                ? _library.Presets.FirstOrDefault(
                    preset => preset.Id == _library.ActivePresetId.Value)?.Snapshot()
                : null;
        }

        if (loadFailure.HasValue && !standardSelected)
        {
            RewardPoolSetupService.Clear();
            validation = new RewardPoolPresetValidation(
                false,
                0,
                0,
                0,
                0,
                0,
                [new RewardPoolPresetIssue(
                    RewardPoolPresetIssueCode.LibraryUnavailable,
                    loadFailure.Value.Message)],
                null);
            return false;
        }

        if (active is null || loadFailure.HasValue)
        {
            validation = RewardPoolPresetValidation.Standard;
            RewardPoolSetupService.UseStandard(context);
            return true;
        }

        validation = Validate(active);
        return SynchronizePreset(active, context, mirrorLegacy: false);
    }

    private static bool TrySelectCore(
        Guid? id,
        RewardPoolPendingContext? explicitContext,
        out RewardPoolPresetValidation validation,
        out RewardPoolPresetOperationResult result)
    {
        RewardPoolPresetDraft selected = null;
        RewardPoolPendingContext context;
        lock (StateLock)
        {
            InitializeLocked();
            if (_libraryLoadFailure.HasValue && id.HasValue)
            {
                validation = Validate(null);
                result = _libraryLoadFailure.Value;
                return false;
            }
            if (id.HasValue)
            {
                selected = _library.Presets.FirstOrDefault(
                    candidate => candidate.Id == id.Value)?.Snapshot();
                if (selected is null)
                {
                    validation = Validate(null);
                    result = PresetNotFound(id.Value);
                    return false;
                }
            }

            var standardForProcess = _libraryLoadFailure.HasValue;
            var candidateLibrary = standardForProcess ? null : NewLibrary(id, _library.Presets);
            var commitResult = RewardPoolPresetOperationResult.Valid;
            if (!RewardPoolDraftService.TryDeselectForSelection(
                    () => standardForProcess || _store.TryCommit(candidateLibrary, out commitResult),
                    out var draftError))
            {
                validation = selected is null
                    ? RewardPoolPresetValidation.Standard
                    : Validate(selected);
                result = !commitResult.IsValid ? commitResult : RewardPoolPresetOperationResult.Invalid(
                    RewardPoolPresetOperationCode.PersistenceError, draftError);
                return false;
            }

            if (standardForProcess)
                _standardSelectedWhileUnavailable = true;
            else
                _library = candidateLibrary.Snapshot();
            result = RewardPoolPresetOperationResult.Valid;
            if (explicitContext.HasValue)
                _selectionContext = explicitContext.Value;
            context = _selectionContext;
        }

        if (selected is null)
        {
            validation = RewardPoolPresetValidation.Standard;
            RewardPoolSetupService.UseStandard(context);
            return true;
        }

        validation = Validate(selected);
        SynchronizePreset(selected, context, mirrorLegacy: validation.IsValid);
        return true;
    }

    private static bool SynchronizePreset(
        RewardPoolPresetDraft preset,
        RewardPoolPendingContext context,
        bool mirrorLegacy)
    {
        var validation = Validate(preset);
        if (!validation.IsValid || validation.Definition is null)
        {
            RewardPoolSetupService.Clear();
            return false;
        }

        if (!RewardPoolSetupService.StageManualTransient(
                validation.Definition,
                context,
                out var stageValidation))
        {
            RewardPoolSetupService.Clear();
            MainFile.Logger.Info(
                $"Active reward-pool preset could not be staged id={preset.Id} validation={stageValidation}");
            return false;
        }

        if (mirrorLegacy
            && !RewardPoolSetupService.SaveLast(validation.Definition, out var mirrorValidation))
        {
            // The preset library and pending setup are already authoritative.
            // The legacy single-build file is compatibility-only and must not
            // roll back a successful active-preset transaction.
            MainFile.Logger.Info(
                $"Active reward-pool preset legacy mirror failed id={preset.Id} validation={mirrorValidation}");
        }

        return true;
    }

    private static bool TryCommitLocked(
        RewardPoolPresetLibrary candidate,
        out RewardPoolPresetOperationResult validation)
    {
        if (_libraryLoadFailure.HasValue)
        {
            validation = _libraryLoadFailure.Value;
            return false;
        }

        if (!_store.TryCommit(candidate, out validation))
            return false;

        _library = candidate.Snapshot();
        _libraryLoadFailure = null;
        return true;
    }

    private static void InitializeLocked()
    {
        if (_initialized)
            return;

        _store ??= AtomicRewardPoolPresetStore.CreateDefault();
        _initialized = true;
        if (_store.TryLoad(out var stored, out var loadValidation))
        {
            _library = stored.Snapshot();
            _libraryLoadFailure = null;
            return;
        }

        _library = RewardPoolPresetLibrary.Empty;
        if (_store.HasStoredLibrary)
        {
            _libraryLoadFailure = loadValidation;
            MainFile.Logger.Info(
                $"Reward-pool preset library ignored validation={loadValidation}");
            return;
        }

        var initial = RewardPoolPresetLibrary.Empty;
        if (RewardPoolSetupService.TryGetLast(out var legacy, out _)
            && legacy.IsManual
            && legacy.Construction.Method is RewardPoolConstructionMethod.Free or RewardPoolConstructionMethod.Genesis)
        {
            var now = DateTime.UtcNow;
            var imported = new RewardPoolPresetDraft(
                Guid.NewGuid(),
                GetLocalizedText("上次构筑（已导入）", "Last Build (Imported)"),
                legacy.MainCardIds.Where(id => !RewardPoolCatalog.IsRequiredMainCard(id)).ToArray(),
                legacy.ExtraCardIds.Where(id => !RewardPoolCatalog.IsRequiredExtraCard(id)).ToArray(),
                now,
                now,
                legacy.Construction.Method,
                legacy.RulesVersion);
            initial = NewLibrary(imported.Id, [imported]);
        }

        if (TryCommitInitialLibrary(initial, out var commitValidation))
        {
            if (_library.ActivePresetId.HasValue)
                MainFile.Logger.Info("Migrated legacy reward-pool construction into preset library");
        }
        else
        {
            MainFile.Logger.Info(
                $"Reward-pool preset library initialization could not be persisted validation={commitValidation}");
        }
    }

    private static bool TryCommitInitialLibrary(
        RewardPoolPresetLibrary initial,
        out RewardPoolPresetOperationResult validation)
    {
        if (!TryCommitLocked(initial, out validation))
        {
            _libraryLoadFailure = validation;
            return false;
        }

        return true;
    }

    private static RewardPoolPresetLibrary NewLibrary(
        Guid? activePresetId,
        IEnumerable<RewardPoolPresetDraft> presets) =>
        new(
            RewardPoolPresetLibrary.CurrentSchemaVersion,
            activePresetId,
            (presets ?? []).ToArray());

    private static DateTime NextTimestampLocked()
    {
        var now = DateTime.UtcNow;
        var latest = _library.Presets.Count == 0
            ? DateTime.MinValue
            : _library.Presets.Max(preset => preset.UpdatedUtc);
        if (latest == DateTime.MaxValue)
            return latest;
        return now > latest ? now : latest.AddTicks(1);
    }

    private static bool TryNormalizeName(
        string name,
        out string normalized,
        out RewardPoolPresetOperationResult validation)
    {
        normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length > 0)
        {
            validation = RewardPoolPresetOperationResult.Valid;
            return true;
        }

        validation = RewardPoolPresetOperationResult.Invalid(
            RewardPoolPresetOperationCode.InvalidPresetName,
            "A reward-pool preset name must not be empty.");
        return false;
    }

    private static void AddEmptyIdIssue(
        IEnumerable<string> ids,
        string deckName,
        ICollection<RewardPoolPresetIssue> issues)
    {
        if ((ids ?? []).Any(string.IsNullOrWhiteSpace))
        {
            issues.Add(new RewardPoolPresetIssue(
                RewardPoolPresetIssueCode.EmptyCardId,
                $"The preset contains an empty {deckName}-card ID."));
        }
    }

    private static void AddDuplicateIssues(
        IEnumerable<string> ids,
        RewardPoolPresetIssueCode code,
        string entryName,
        ICollection<RewardPoolPresetIssue> issues)
    {
        foreach (var duplicate in (ids ?? [])
                     .Select(id => id?.Trim() ?? string.Empty)
                     .Where(id => id.Length > 0)
                     .GroupBy(id => id, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1)
                     .Select(group => group.Key))
        {
            issues.Add(new RewardPoolPresetIssue(
                code,
                $"The preset contains duplicate {entryName} ID '{duplicate}'."));
        }
    }

    private static void AddRarityIssue(
        int count,
        CardRarity rarity,
        RewardPoolPresetIssueCode code,
        ICollection<RewardPoolPresetIssue> issues)
    {
        if (count >= RewardPoolCatalog.MinimumPerRewardRarity)
            return;

        issues.Add(new RewardPoolPresetIssue(
            code,
            $"A preset requires at least {RewardPoolCatalog.MinimumPerRewardRarity} {rarity} reward cards; received {count}."));
    }

    private static RewardPoolPresetIssue FromDefinitionValidation(
        RewardPoolValidationResult validation)
    {
        var code = validation.Code switch
        {
            RewardPoolValidationCode.IncorrectMainCardCount =>
                RewardPoolPresetIssueCode.IncorrectMainCardCount,
            RewardPoolValidationCode.IncorrectExtraCardCount =>
                RewardPoolPresetIssueCode.IncorrectExtraCardCount,
            RewardPoolValidationCode.EmptyCardId => RewardPoolPresetIssueCode.EmptyCardId,
            RewardPoolValidationCode.DuplicateMainCardId =>
                RewardPoolPresetIssueCode.DuplicateMainCardId,
            RewardPoolValidationCode.DuplicateExtraCardId =>
                RewardPoolPresetIssueCode.DuplicateExtraCardId,
            RewardPoolValidationCode.UnknownMainCardId =>
                RewardPoolPresetIssueCode.UnknownMainCardId,
            RewardPoolValidationCode.UnknownExtraCardId =>
                RewardPoolPresetIssueCode.UnknownExtraCardId,
            RewardPoolValidationCode.MainCardInWrongCatalog =>
                RewardPoolPresetIssueCode.MainCardInWrongCatalog,
            RewardPoolValidationCode.ExtraCardInWrongCatalog =>
                RewardPoolPresetIssueCode.ExtraCardInWrongCatalog,
            RewardPoolValidationCode.InsufficientCommonCards =>
                RewardPoolPresetIssueCode.InsufficientCommonCards,
            RewardPoolValidationCode.InsufficientUncommonCards =>
                RewardPoolPresetIssueCode.InsufficientUncommonCards,
            RewardPoolValidationCode.InsufficientRareCards =>
                RewardPoolPresetIssueCode.InsufficientRareCards,
            RewardPoolValidationCode.CatalogUnavailable
                or RewardPoolValidationCode.CatalogCountMismatch
                or RewardPoolValidationCode.CatalogDuplicateId
                or RewardPoolValidationCode.CatalogInvariantViolation =>
                RewardPoolPresetIssueCode.CatalogUnavailable,
            _ => RewardPoolPresetIssueCode.InvalidDefinition
        };
        return new RewardPoolPresetIssue(code, validation.Message);
    }

    private static RewardPoolPresetOperationResult PresetNotFound(Guid id) =>
        RewardPoolPresetOperationResult.Invalid(
            RewardPoolPresetOperationCode.PresetNotFound,
            $"Reward-pool preset '{id}' does not exist.");

    private static void ThrowIfLibraryUnavailableLocked()
    {
        if (_libraryLoadFailure is { } failure)
        {
            throw new InvalidOperationException(
                $"Reward-pool preset library is unavailable: {failure}");
        }
    }

    private static string GetLocalizedText(string chinese, string english)
    {
        try
        {
            var locale = TranslationServer.GetLocale();
            return locale?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true
                ? chinese
                : english;
        }
        catch
        {
            return english;
        }
    }
}
