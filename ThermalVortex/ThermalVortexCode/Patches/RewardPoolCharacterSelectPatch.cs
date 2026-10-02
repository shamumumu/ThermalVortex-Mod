using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

/// <summary>
/// Owns the reward-pool entry beneath the Thermal Vortex character portrait.
/// Preset persistence, validation, and the full-screen manager remain isolated
/// in their respective services/sessions; this class keeps the selected preset
/// and pending run setup synchronized with the current character-select generation.
/// </summary>
internal static class RewardPoolCharacterSelectUi
{
    private const string EntryNodeName = "ThermalVortexRewardPoolEntry";
    private const string LocalizationPrefix = "THERMALVORTEX-THERMAL_VORTEX.rewardPool.";

    private static readonly ConditionalWeakTable<NCharacterSelectScreen, ScreenState> States = new();

    internal static void OnSubmenuOpened(NCharacterSelectScreen screen)
    {
        if (screen is null)
            return;

        var state = GetOrCreateState(screen);
        InvalidateGeneration(state);
        ClearActiveBuilder(state);
        state.IsOpen = true;
        state.IsManaging = false;

        var selectedCharacter = FindSelectedCharacter(screen);
        state.LastSelectedCharacterId = GetCharacterId(selectedCharacter);
        if (IsEligible(screen, selectedCharacter))
            RestorePersistedSelection(state, selectedCharacter);
        else
            RewardPoolSetupService.Clear();

        Refresh(state);
    }

    internal static bool IsRunStarting(NCharacterSelectScreen screen)
    {
        try
        {
            return screen?.Lobby?.IsAboutToBeginGame() == true;
        }
        catch
        {
            return false;
        }
    }

    internal static void OnSubmenuClosed(NCharacterSelectScreen screen, bool isRunStarting)
    {
        if (screen is not null && States.TryGetValue(screen, out var state))
        {
            InvalidateGeneration(state);
            ClearActiveBuilder(state);
            state.IsOpen = false;
            state.IsManaging = false;
            HideEntry(state);
            RelinquishEmbarkBlock(state);
        }

        // The active preset is durable, but PendingSetup belongs only to this
        // character-select generation. Preserve it solely during embark.
        if (!isRunStarting)
            RewardPoolSetupService.Clear();
    }

    internal static void OnCharacterSelected(NCharacterSelectScreen screen, CharacterModel character)
    {
        if (screen is null)
            return;

        var state = GetOrCreateState(screen);
        character ??= FindSelectedCharacter(screen);
        var characterId = GetCharacterId(character);

        if (!string.Equals(characterId, state.LastSelectedCharacterId, StringComparison.Ordinal))
        {
            InvalidateGeneration(state);
            ClearActiveBuilder(state);
            state.IsManaging = false;
            state.LastSelectedCharacterId = characterId;
        }

        if (IsEligible(screen, character))
            RestorePersistedSelection(state, character);
        else
            RewardPoolSetupService.Clear();

        Refresh(state);
    }

    internal static bool CanEmbark(NCharacterSelectScreen screen)
    {
        var selectedCharacter = FindSelectedCharacter(screen);
        if (!IsEligible(screen, selectedCharacter))
            return true;

        var state = GetOrCreateState(screen);
        if (state.IsManaging)
        {
            ReportEntryError(
                state,
                Localize("errorManagerOpen"),
                "Reward-pool embark rejected while the preset manager is open");
            SetEmbarkBlocked(state, true);
            return false;
        }

        if (RewardPoolDraftService.IsSelected)
        {
            if (!RestoreDraftSelection(state, selectedCharacter, out var draftValidation)
                || !RewardPoolSetupService.ValidatePending(CreatePendingContext(state, selectedCharacter), out draftValidation))
            {
                ReportEntryError(state, DraftStatusText(false) + "\n" + draftValidation.Message,
                    "Draft embark rejected: " + draftValidation);
                SetEmbarkBlocked(state, true);
                return false;
            }
            Refresh(state);
            return true;
        }

        // Rebuild PendingSetup from the persisted active selection immediately
        // before embark. An invalid active draft clears PendingSetup in the
        // service and therefore cannot accidentally reuse an earlier valid one.
        RewardPoolPresetValidation presetValidation;
        try
        {
            if (!RewardPoolPresetService.RestoreSelection(
                    CreatePendingContext(state, selectedCharacter),
                    out presetValidation))
            {
                Refresh(state);
                ReportEntryError(
                    state,
                    FormatPresetIssues(presetValidation),
                    "Reward-pool embark rejected because the active preset is invalid");
                SetEmbarkBlocked(state, true);
                return false;
            }
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("ThermalVortex reward-pool selection restore failed: " + exception);
            RewardPoolSetupService.Clear();
            SetEntryTooltip(state, Localize("errorPresetLibrary"));
            SetEmbarkBlocked(state, true);
            return false;
        }

        if (RewardPoolSetupService.ValidatePending(
                CreatePendingContext(state, selectedCharacter),
                out var validation))
        {
            Refresh(state);
            return true;
        }

        ReportEntryError(
            state,
            FormatValidationError(validation),
            "Reward-pool embark rejected because the pending setup is invalid");
        SetEmbarkBlocked(state, true);
        return false;
    }

    private static ScreenState GetOrCreateState(NCharacterSelectScreen screen)
    {
        if (States.TryGetValue(screen, out var existing))
            return existing;

        var state = CreateState(screen);
        States.Add(screen, state);
        return state;
    }

    private static ScreenState CreateState(NCharacterSelectScreen screen)
    {
        var state = new ScreenState(
            screen,
            screen.GetNodeOrNull<NConfirmButton>("ConfirmButton"))
        {
            EntryTooltip = Localize("entryLabel")
        };
        EnsureEntry(state);
        return state;
    }

    private static async Task OpenPresetManagerAsync(ScreenState state)
    {
        if (!CanInteract(state))
            return;

        state.IsManaging = true;
        var generation = state.Generation;
        var cancellation = state.BuilderCancellation.Token;
        string failureStatus = null;
        Refresh(state);

        try
        {
            await Task.Yield();
            if (!CanContinueManager(state, generation, cancellation))
                return;

            var manager = RewardPoolBuilderSession.Open(
                state.Screen,
                RewardPoolCatalog.GetMainRewardCandidates(),
                RewardPoolCatalog.GetExtraCandidates());
            state.ActiveBuilder = manager;
            await manager.Completion;

            if (!CanContinueManager(state, generation, cancellation))
                return;

            RestorePersistedSelection(state, FindSelectedCharacter(state.Screen));
        }
        catch (OperationCanceledException)
        {
            // Screen closure and character switches deliberately invalidate this session.
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("ThermalVortex reward-pool preset manager failed: " + exception);
            if (state.Generation == generation)
            {
                failureStatus = exception is InvalidOperationException
                    ? Localize("errorPresetLibrary")
                    : Localize("errorUnexpected");
            }
        }
        finally
        {
            if (state.Generation == generation)
            {
                state.IsManaging = false;
                try
                {
                    Refresh(state);
                    if (!string.IsNullOrWhiteSpace(failureStatus))
                    {
                        ReportEntryError(
                            state,
                            failureStatus,
                            "Reward-pool preset manager closed after an error");
                    }
                }
                finally
                {
                    DisposeActiveBuilder(state);
                    DeferFocus(state.EntryButton);
                }
            }
        }
    }

    private static void RestorePersistedSelection(ScreenState state, CharacterModel character)
    {
        try
        {
            if (RewardPoolDraftService.IsSelected)
            {
                RestoreDraftSelection(state, character, out _);
                return;
            }
            RewardPoolPresetService.RestoreSelection(
                CreatePendingContext(state, character),
                out _);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("ThermalVortex active reward-pool restore failed: " + exception);
            RewardPoolSetupService.Clear();
            SetEntryTooltip(state, Localize("errorPresetLibrary"));
        }
    }

    private static bool RestoreDraftSelection(ScreenState state, CharacterModel character,
        out RewardPoolValidationResult validation)
    {
        if (RewardPoolDraftService.TryGetReadyDefinition(out var definition, out validation)
            && RewardPoolSetupService.StageManualTransient(definition,
                CreatePendingContext(state, character), out validation))
            return true;
        RewardPoolSetupService.Clear();
        return false;
    }

    private static string DraftStatusText(bool ready) =>
        TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? ready ? "三选一奖励池已完成，仅供本次新局。" : "请完成新的三选一奖励池后出发。"
            : ready ? "Draft reward pool ready for one new run." : "Complete a new draft reward pool before embarking.";

    private static void ClearActiveBuilder(ScreenState state)
    {
        var builder = state?.ActiveBuilder;
        if (builder is null)
            return;

        try
        {
            builder.RequestCancel();
            builder.Dispose();
        }
        finally
        {
            if (ReferenceEquals(state.ActiveBuilder, builder))
                state.ActiveBuilder = null;
        }
    }

    private static void DisposeActiveBuilder(ScreenState state)
    {
        var builder = state?.ActiveBuilder;
        if (builder is null)
            return;

        try
        {
            builder.Dispose();
        }
        finally
        {
            if (ReferenceEquals(state.ActiveBuilder, builder))
                state.ActiveBuilder = null;
        }
    }

    internal static bool TryCancelActiveBuilderFromInput(
        NCharacterSelectScreen screen,
        InputEvent inputEvent)
    {
        if (screen is null
            || inputEvent is null
            || !IsCancelInput(inputEvent)
            || !States.TryGetValue(screen, out var state)
            || state.ActiveBuilder is null)
        {
            return false;
        }

        var canceled = state.ActiveBuilder.RequestCancel();
        MainFile.Logger.Info(
            $"Reward-pool manager cancellation requested source=input result={canceled}");
        screen.GetViewport()?.SetInputAsHandled();
        return true;
    }

    private static bool IsCancelInput(InputEvent inputEvent) =>
        inputEvent.IsActionPressed("ui_cancel", false, false)
        || inputEvent.IsActionPressed("mega_pause_and_back", false, false)
        || inputEvent.IsActionPressed("mega_back", false, false);

    private static bool CanInteract(ScreenState state) =>
        state.IsOpen
        && !state.IsManaging
        && IsValid(state.Screen)
        && state.Screen.IsInsideTree()
        && IsEligible(state.Screen, FindSelectedCharacter(state.Screen));

    private static bool CanContinueManager(
        ScreenState state,
        long generation,
        CancellationToken cancellationToken) =>
        state.IsOpen
        && state.IsManaging
        && state.Generation == generation
        && !cancellationToken.IsCancellationRequested
        && IsValid(state.Screen)
        && state.Screen.IsInsideTree()
        && IsEligible(state.Screen, FindSelectedCharacter(state.Screen));

    private static void Refresh(ScreenState state)
    {
        var selectedCharacter = FindSelectedCharacter(state.Screen);
        var eligible = IsEligible(state.Screen, selectedCharacter);
        SetEntryVisible(state, state.IsOpen && eligible && !state.IsManaging);
        if (!eligible)
        {
            // SelectCharacter and submenu lifecycle code own the button state
            // outside our eligible character/mode. Never force-enable it here.
            RelinquishEmbarkBlock(state);
            return;
        }

        // Validation and embark blocking deliberately do not depend on the entry
        // node. A missing or externally-freed UI node must never make an invalid
        // active preset embarkable.
        try
        {
            if (RewardPoolDraftService.IsSelected)
            {
                var ready = RewardPoolDraftService.TryGetReadyDefinition(out _, out var draftValidation);
                SetEntryTooltip(state, DraftStatusText(ready)
                    + (ready ? string.Empty : "\n" + draftValidation.Message));
                SetEmbarkBlocked(state, state.IsManaging || !ready);
                return;
            }
            var readState = RewardPoolPresetService.GetReadState();
            if (readState.LoadFailure.HasValue)
            {
                SetEntryTooltip(state, Localize(readState.StandardSelectedForProcess
                    ? "managerStandardSessionSelected"
                    : "errorPresetLibrary"));
                SetEmbarkBlocked(state, state.IsManaging || !readState.StandardSelectedForProcess);
                return;
            }

            var library = readState.Library;
            if (!library.ActivePresetId.HasValue)
            {
                SetEntryTooltip(state, Localize("entryLabel"));
                SetEmbarkBlocked(state, state.IsManaging);
                return;
            }

            if (!RewardPoolPresetService.TryGetActive(out var active))
            {
                ReportEntryError(
                    state,
                    Localize("errorPresetMissing"),
                    "Reward-pool active preset is missing");
                SetEmbarkBlocked(state, true);
                return;
            }

            var validation = RewardPoolPresetService.Validate(active);
            if (validation.IsValid)
            {
                SetEntryTooltip(state, Localize("entryLabel"));
            }
            else
            {
                ReportEntryError(
                    state,
                    FormatPresetIssues(validation),
                    $"Reward-pool active preset '{active.Name}' is invalid");
            }
            SetEmbarkBlocked(state, state.IsManaging || !validation.IsValid);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("ThermalVortex reward-pool validation refresh failed: " + exception);
            SetEntryTooltip(state, Localize("errorPresetLibrary"));
            SetEmbarkBlocked(state, true);
        }
    }

    private static void SetEntryVisible(ScreenState state, bool visible)
    {
        if (!visible)
        {
            HideEntry(state);
            return;
        }

        if (!EnsureEntry(state))
            return;

        state.EntryButton.TooltipText = state.EntryTooltip;
        state.EntryButton.Visible = true;
        LinkEntryNavigation(state);
    }

    private static bool EnsureEntry(ScreenState state)
    {
        if (IsValid(state.EntryButton)
            && IsValid(state.CharacterButton)
            && ReferenceEquals(state.EntryButton.GetParent(), state.CharacterButton))
        {
            return true;
        }

        DestroyEntry(state);
        var characterButton = FindThermalCharacterButton(state.Screen);
        if (!IsValid(characterButton))
        {
            LogEntryMountFailureOnce(
                state,
                "Thermal Vortex character button was not found under CharSelectButtons/ButtonContainer");
            return false;
        }

        Button entry = null;
        try
        {
            entry = CreateEntryButton();
            characterButton.AddChild(entry);

            state.CharacterButton = characterButton;
            state.EntryButton = entry;
            state.EntryPath = entry.GetPath();
            state.CharacterFocusNeighborBottom = characterButton.FocusNeighborBottom;
            state.EntryMountFailureLogged = false;

            entry.Pressed += () => _ = OpenPresetManagerAsync(state);
            entry.TreeExiting += () =>
            {
                if (ReferenceEquals(state.EntryButton, entry))
                    RestoreEntryNavigation(state);
            };
            return true;
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(state.EntryButton, entry))
            {
                DestroyEntry(state);
            }
            else if (IsValid(entry))
            {
                var parent = entry.GetParent();
                if (IsValid(parent))
                    parent.RemoveChild(entry);
                entry.QueueFree();
            }

            SetEntryTooltip(state, Localize("errorPresetLibrary"));
            LogEntryMountFailureOnce(
                state,
                "Reward-pool entry creation failed: " + exception);
            return false;
        }
    }

    private static NCharacterSelectButton FindThermalCharacterButton(
        NCharacterSelectScreen screen)
    {
        var container = screen?.GetNodeOrNull<Control>("CharSelectButtons/ButtonContainer");
        if (!IsValid(container))
            return null;

        return container.GetChildren()
            .OfType<NCharacterSelectButton>()
            .FirstOrDefault(button => IsThermalVortex(button.Character));
    }

    private static void LinkEntryNavigation(ScreenState state)
    {
        var characterButton = state.CharacterButton;
        var entry = state.EntryButton;
        if (!IsValid(characterButton) || !IsValid(entry))
            return;

        var entryPath = entry.GetPath();
        var characterPath = characterButton.GetPath();
        if (entryPath.IsEmpty || characterPath.IsEmpty)
            return;

        if (characterButton.FocusNeighborBottom != entryPath)
            state.CharacterFocusNeighborBottom = characterButton.FocusNeighborBottom;

        state.EntryPath = entryPath;
        characterButton.FocusNeighborBottom = entryPath;
        entry.FocusNeighborTop = characterPath;
        entry.FocusNeighborBottom = entryPath;
        entry.FocusNeighborLeft = entryPath;
        entry.FocusNeighborRight = entryPath;
    }

    private static void HideEntry(ScreenState state)
    {
        var entry = state?.EntryButton;
        if (IsValid(entry))
        {
            if (entry.HasFocus())
            {
                if (IsValid(state.CharacterButton)
                    && state.CharacterButton.IsInsideTree()
                    && state.CharacterButton.IsVisibleInTree())
                {
                    state.CharacterButton.GrabFocus();
                }
                else
                {
                    entry.ReleaseFocus();
                }
            }

            entry.Visible = false;
        }

        RestoreEntryNavigation(state);
    }

    private static void RestoreEntryNavigation(ScreenState state)
    {
        if (state is null)
            return;

        var entryPath = state.EntryPath;
        if (IsValid(state.CharacterButton)
            && entryPath is not null
            && !entryPath.IsEmpty
            && state.CharacterButton.FocusNeighborBottom == entryPath)
        {
            state.CharacterButton.FocusNeighborBottom = state.CharacterFocusNeighborBottom;
        }

        if (IsValid(state.EntryButton))
        {
            state.EntryButton.FocusNeighborTop = default;
            state.EntryButton.FocusNeighborBottom = default;
            state.EntryButton.FocusNeighborLeft = default;
            state.EntryButton.FocusNeighborRight = default;
        }
    }

    private static void DestroyEntry(ScreenState state)
    {
        if (state is null)
            return;

        HideEntry(state);
        var entry = state.EntryButton;
        if (IsValid(entry))
        {
            var parent = entry.GetParent();
            if (IsValid(parent))
                parent.RemoveChild(entry);
            entry.QueueFree();
        }

        state.EntryButton = null;
        state.CharacterButton = null;
        state.EntryPath = default;
        state.CharacterFocusNeighborBottom = default;
    }

    private static void SetEntryTooltip(ScreenState state, string text)
    {
        if (state is null)
            return;

        state.EntryTooltip = string.IsNullOrWhiteSpace(text)
            ? Localize("entryLabel")
            : text;
        if (IsValid(state.EntryButton))
            state.EntryButton.TooltipText = state.EntryTooltip;
    }

    private static void ReportEntryError(ScreenState state, string text, string logContext)
    {
        SetEntryTooltip(state, text);
        MainFile.Logger.Info($"{logContext}: {text}");
    }

    private static void LogEntryMountFailureOnce(ScreenState state, string message)
    {
        if (state.EntryMountFailureLogged)
            return;

        state.EntryMountFailureLogged = true;
        MainFile.Logger.Info(message);
    }

    private static void SetEmbarkBlocked(ScreenState state, bool blocked)
    {
        var button = state.EmbarkButton;
        if (!IsValid(button))
        {
            button = state.Screen?.GetNodeOrNull<NConfirmButton>("ConfirmButton");
            state.EmbarkButton = button;
        }

        if (!IsValid(button))
            return;

        if (blocked)
        {
            if (!state.EmbarkDisabledByUs)
            {
                state.EmbarkWasEnabledBeforeBlock = button.IsEnabled;
                state.EmbarkTooltipBeforeBlock = button.TooltipText;
                state.EmbarkBlockTooltip = Localize("embarkBlockedTooltip");
            }
            button.Disable();
            button.TooltipText = state.EmbarkBlockTooltip;
            state.EmbarkDisabledByUs = true;
        }
        else if (state.EmbarkDisabledByUs)
        {
            var restoreEnabled = state.EmbarkWasEnabledBeforeBlock;
            if (string.Equals(
                    button.TooltipText,
                    state.EmbarkBlockTooltip,
                    StringComparison.Ordinal))
            {
                button.TooltipText = state.EmbarkTooltipBeforeBlock;
            }
            state.EmbarkDisabledByUs = false;
            state.EmbarkWasEnabledBeforeBlock = false;
            state.EmbarkTooltipBeforeBlock = string.Empty;
            state.EmbarkBlockTooltip = string.Empty;
            button.SetEnabled(restoreEnabled);
        }
    }

    private static void RelinquishEmbarkBlock(ScreenState state)
    {
        if (!state.EmbarkDisabledByUs)
            return;

        if (IsValid(state.EmbarkButton)
            && string.Equals(
                state.EmbarkButton.TooltipText,
                state.EmbarkBlockTooltip,
                StringComparison.Ordinal))
        {
            state.EmbarkButton.TooltipText = state.EmbarkTooltipBeforeBlock;
        }
        state.EmbarkDisabledByUs = false;
        state.EmbarkWasEnabledBeforeBlock = false;
        state.EmbarkTooltipBeforeBlock = string.Empty;
        state.EmbarkBlockTooltip = string.Empty;
    }

    private static bool IsEligible(NCharacterSelectScreen screen, CharacterModel selectedCharacter)
    {
        var lobby = screen?.Lobby;
        return IsThermalVortex(selectedCharacter)
            && lobby?.GameMode == GameMode.Standard
            && lobby.NetService?.Type == NetGameType.Singleplayer;
    }

    private static bool IsThermalVortex(CharacterModel character)
    {
        if (character is null)
            return false;

        return character is ThermalVortexCharacter
            || character.GetType().FullName == typeof(ThermalVortexCharacter).FullName
            || string.Equals(character.Id?.Entry, ThermalVortexCharacter.CharacterId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(character.Id?.ToString(), ThermalVortexCharacter.CharacterId, StringComparison.OrdinalIgnoreCase);
    }

    private static CharacterModel FindSelectedCharacter(Node root)
    {
        if (root is null)
            return null;

        foreach (var child in root.GetChildren())
        {
            if (child is NCharacterSelectButton { IsSelected: true } button)
                return button.Character;

            if (child is Node node)
            {
                var found = FindSelectedCharacter(node);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    private static RewardPoolPendingContext CreatePendingContext(
        ScreenState state,
        CharacterModel character)
    {
        var lobby = state?.Screen?.Lobby;
        return new RewardPoolPendingContext(
            GetCharacterId(character),
            state?.Generation ?? 0L,
            lobby?.GameMode ?? GameMode.None,
            lobby?.NetService?.Type ?? NetGameType.None);
    }

    private static string GetCharacterId(CharacterModel character) =>
        character?.Id?.ToString()
        ?? character?.GetType().FullName
        ?? string.Empty;

    private static long NextGeneration(long current) =>
        current >= long.MaxValue ? 1L : Math.Max(1L, current + 1L);

    private static void InvalidateGeneration(ScreenState state)
    {
        state.BuilderCancellation.Cancel();
        state.BuilderCancellation.Dispose();
        state.BuilderCancellation = new CancellationTokenSource();
        state.Generation = NextGeneration(state.Generation);
    }

    private static string FormatPresetIssues(RewardPoolPresetValidation validation)
    {
        var messages = validation?.Issues?
            .Select(FormatPresetIssue)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        if (messages.Length == 0)
            return Localize("errorInvalid");

        const int visibleIssueCount = 2;
        var visible = string.Join("\n", messages.Take(visibleIssueCount));
        if (messages.Length <= visibleIssueCount)
            return visible;

        return visible + "\n" + LocalizeCount(
            "moreIssues",
            ("Count", messages.Length - visibleIssueCount));
    }

    private static string FormatPresetIssue(RewardPoolPresetIssue issue)
    {
        if (issue.Code is RewardPoolPresetIssueCode.ConstructionBudgetExceeded or RewardPoolPresetIssueCode.InvalidConstruction)
            return issue.Message;
        var key = issue.Code switch
        {
            RewardPoolPresetIssueCode.InvalidPresetName => "errorPresetName",
            RewardPoolPresetIssueCode.IncorrectMainCardCount => "errorMainCount",
            RewardPoolPresetIssueCode.IncorrectExtraCardCount => "errorExtraCount",
            RewardPoolPresetIssueCode.DuplicateMainCardId or
                RewardPoolPresetIssueCode.DuplicateExtraCardId => "errorDuplicate",
            RewardPoolPresetIssueCode.EmptyCardId or
                RewardPoolPresetIssueCode.UnknownMainCardId or
                RewardPoolPresetIssueCode.UnknownExtraCardId or
                RewardPoolPresetIssueCode.MainCardInWrongCatalog or
                RewardPoolPresetIssueCode.ExtraCardInWrongCatalog => "errorUnknownCard",
            RewardPoolPresetIssueCode.InsufficientCommonCards or
                RewardPoolPresetIssueCode.InsufficientUncommonCards or
                RewardPoolPresetIssueCode.InsufficientRareCards => "errorRarity",
            RewardPoolPresetIssueCode.CatalogUnavailable => "errorCatalog",
            RewardPoolPresetIssueCode.LibraryUnavailable => "errorPresetLibrary",
            _ => "errorInvalid"
        };
        return Localize(key);
    }

    private static string FormatValidationError(RewardPoolValidationResult validation)
    {
        if (validation.Code is RewardPoolValidationCode.ConstructionBudgetExceeded or RewardPoolValidationCode.InvalidConstruction)
            return validation.Message;
        var key = validation.Code switch
        {
            RewardPoolValidationCode.NoSavedManualDefinition => "errorNoLast",
            RewardPoolValidationCode.IncorrectMainCardCount => "errorMainCount",
            RewardPoolValidationCode.IncorrectExtraCardCount => "errorExtraCount",
            RewardPoolValidationCode.DuplicateMainCardId or
                RewardPoolValidationCode.DuplicateExtraCardId => "errorDuplicate",
            RewardPoolValidationCode.MissingRequiredMainCard or
                RewardPoolValidationCode.MissingRequiredExtraCard => "errorRequired",
            RewardPoolValidationCode.EmptyCardId or
                RewardPoolValidationCode.UnknownMainCardId or
                RewardPoolValidationCode.UnknownExtraCardId or
                RewardPoolValidationCode.MainCardInWrongCatalog or
                RewardPoolValidationCode.ExtraCardInWrongCatalog => "errorUnknownCard",
            RewardPoolValidationCode.InsufficientCommonCards or
                RewardPoolValidationCode.InsufficientUncommonCards or
                RewardPoolValidationCode.InsufficientRareCards => "errorRarity",
            RewardPoolValidationCode.CatalogUnavailable => "errorCatalog",
            RewardPoolValidationCode.ConfigPersistenceError or
                RewardPoolValidationCode.SerializationError => "errorPersistence",
            _ => "errorInvalid"
        };
        return Localize(key);
    }

    private static string Localize(string key) =>
        new LocString("characters", LocalizationPrefix + key).GetFormattedText();

    private static string LocalizeCount(
        string key,
        params (string Name, int Value)[] values)
    {
        var text = new LocString("characters", LocalizationPrefix + key);
        foreach (var (name, value) in values)
            text.Add(name, (decimal)value);
        return text.GetFormattedText();
    }

    private static Button CreateEntryButton()
    {
        var button = new Button
        {
            Name = EntryNodeName,
            Text = Localize("entryLabel"),
            TooltipText = Localize("entryLabel"),
            CustomMinimumSize = new Vector2(116f, 36f),
            AnchorLeft = 0.5f,
            AnchorTop = 0f,
            AnchorRight = 0.5f,
            AnchorBottom = 0f,
            OffsetLeft = -58f,
            OffsetTop = 150f,
            OffsetRight = 58f,
            OffsetBottom = 186f,
            Alignment = HorizontalAlignment.Center,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 20,
            ZAsRelative = true,
            Visible = false
        };
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeStyleboxOverride("normal", CreateEntryButtonStyle(new Color(0.09f, 0.055f, 0.17f), new Color(0.58f, 0.43f, 0.78f), 1));
        button.AddThemeStyleboxOverride("hover", CreateEntryButtonStyle(new Color(0.18f, 0.09f, 0.29f), new Color(0.88f, 0.67f, 0.24f), 2));
        button.AddThemeStyleboxOverride("pressed", CreateEntryButtonStyle(new Color(0.27f, 0.12f, 0.39f), new Color(1f, 0.78f, 0.3f), 2));
        button.AddThemeStyleboxOverride("focus", CreateEntryButtonStyle(new Color(0.13f, 0.07f, 0.23f), new Color(0.73f, 0.4f, 1f), 2));
        return button;
    }

    private static StyleBoxFlat CreateEntryButtonStyle(
        Color background,
        Color border,
        int borderWidth) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = borderWidth,
        BorderWidthTop = borderWidth,
        BorderWidthRight = borderWidth,
        BorderWidthBottom = borderWidth,
        CornerRadiusTopLeft = 8,
        CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8,
        CornerRadiusBottomRight = 8,
        ContentMarginLeft = 6f,
        ContentMarginRight = 6f,
        ContentMarginTop = 4f,
        ContentMarginBottom = 4f
    };

    private static void DeferFocus(Control control)
    {
        Callable.From(() =>
        {
            if (IsValid(control)
                && control.IsInsideTree()
                && control.IsVisibleInTree())
            {
                control.GrabFocus();
            }
        }).CallDeferred();
    }

    private static bool IsValid(GodotObject value) =>
        value is not null && GodotObject.IsInstanceValid(value);

    private sealed class ScreenState(
        NCharacterSelectScreen screen,
        NConfirmButton embarkButton)
    {
        internal NCharacterSelectScreen Screen { get; } = screen;
        internal NCharacterSelectButton CharacterButton { get; set; }
        internal Button EntryButton { get; set; }
        internal NodePath EntryPath { get; set; }
        internal NodePath CharacterFocusNeighborBottom { get; set; }
        internal string EntryTooltip { get; set; } = string.Empty;
        internal bool EntryMountFailureLogged { get; set; }
        internal NConfirmButton EmbarkButton { get; set; } = embarkButton;
        internal RewardPoolBuilderSession ActiveBuilder { get; set; }
        internal bool EmbarkDisabledByUs { get; set; }
        internal bool EmbarkWasEnabledBeforeBlock { get; set; }
        internal string EmbarkTooltipBeforeBlock { get; set; } = string.Empty;
        internal string EmbarkBlockTooltip { get; set; } = string.Empty;
        internal bool IsManaging { get; set; }
        internal string LastSelectedCharacterId { get; set; } = string.Empty;
        internal long Generation { get; set; }
        internal bool IsOpen { get; set; }
        internal CancellationTokenSource BuilderCancellation { get; set; } = new();
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
internal static class RewardPoolCharacterSelectOpenPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        RewardPoolCharacterSelectUi.OnSubmenuOpened(__instance);
        RewardPoolMultiplayerLobby.Open(__instance);
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuClosed))]
internal static class RewardPoolCharacterSelectClosePatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(NCharacterSelectScreen __instance, out bool __state) =>
        __state = RewardPoolCharacterSelectUi.IsRunStarting(__instance);

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(NCharacterSelectScreen __instance, bool __state)
    {
        RewardPoolCharacterSelectUi.OnSubmenuClosed(__instance, __state);
        RewardPoolMultiplayerLobby.Close(__instance, __state);
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Input))]
internal static class RewardPoolCharacterSelectCancelInputPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(NCharacterSelectScreen __instance, InputEvent __0) =>
        !RewardPoolMultiplayerLobby.HandleInput(__instance, __0)
        && !RewardPoolCharacterSelectUi.TryCancelActiveBuilderFromInput(__instance, __0);
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
internal static class RewardPoolCharacterSelectionChangedPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(
        NCharacterSelectScreen __instance,
        NCharacterSelectButton __0,
        CharacterModel __1) =>
        RewardPoolCharacterSelectUi.OnCharacterSelected(__instance, __0?.Character ?? __1);
}

[HarmonyPatch(typeof(NCharacterSelectScreen), "OnEmbarkPressed")]
internal static class RewardPoolCharacterSelectEmbarkPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(NCharacterSelectScreen __instance) =>
        RewardPoolMultiplayerLobby.CanEmbark(__instance)
        && RewardPoolCharacterSelectUi.CanEmbark(__instance);
}
