using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.RewardPools;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal enum VirtualPileKind
{
    ExtraDeck,
    RewardPool
}

[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
internal static class ExtraDeckTopBarInitializePatch
{
    private static void Postfix(NTopBar __instance)
    {
        ExtraDeckTopBarUi.Attach(__instance);
    }
}

[HarmonyPatch(typeof(NTopBar), nameof(NTopBar._ExitTree))]
internal static class ExtraDeckTopBarExitTreePatch
{
    private static void Prefix(NTopBar __instance)
    {
        ExtraDeckTopBarUi.Detach(__instance, removeButton: false);
    }
}

[HarmonyPatch(typeof(NTopBarDeckButton), "get_Hotkeys")]
internal static class ExtraDeckTopBarHotkeysPatch
{
    private static bool Prefix(NTopBarDeckButton __instance, ref string[] __result)
    {
        if (!ExtraDeckTopBarUi.IsVirtualPileButton(__instance))
            return true;

        __result = [];
        return false;
    }
}

[HarmonyPatch(typeof(NTopBarDeckButton), "OnRelease")]
internal static class ExtraDeckTopBarReleasePatch
{
    private static bool Prefix(NTopBarDeckButton __instance)
    {
        return ExtraDeckTopBarUi.BeforeDeckButtonReleased(__instance);
    }

    private static void Postfix(NTopBarDeckButton __instance)
    {
        ExtraDeckTopBarUi.AfterDeckButtonReleased(__instance);
    }

    private static Exception Finalizer(NTopBarDeckButton __instance, Exception __exception)
    {
        ExtraDeckTopBarUi.AfterDeckButtonReleased(__instance);
        return __exception;
    }
}

[HarmonyPatch(typeof(NTopBarDeckButton), "IsOpen")]
internal static class ExtraDeckTopBarIsOpenPatch
{
    private static void Postfix(NTopBarDeckButton __instance, ref bool __result)
    {
        ExtraDeckTopBarUi.OverrideDeckButtonOpenState(__instance, ref __result);
    }
}

[HarmonyPatch(typeof(NTopBarDeckButton), "OnFocus")]
internal static class ExtraDeckTopBarFocusPatch
{
    private static void Postfix(NTopBarDeckButton __instance)
    {
        ExtraDeckTopBarUi.ReplaceVirtualPileHoverTip(__instance);
    }
}

[HarmonyPatch(typeof(NTopBarDeckButton), nameof(NTopBarDeckButton._Process))]
internal static class ExtraDeckTopBarProcessPatch
{
    private static void Postfix(NTopBarDeckButton __instance)
    {
        ExtraDeckTopBarUi.RefreshButton(__instance);
    }
}

[HarmonyPatch(typeof(CardPile), nameof(CardPile.Get), typeof(PileType), typeof(Player))]
internal static class ExtraDeckTopBarCardPileGetPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(PileType __0, Player __1, ref CardPile __result)
    {
        if (!ExtraDeckTopBarUi.TryResolveOpeningPile(__0, __1, out var pile))
            return true;

        __result = pile;
        return false;
    }
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen._EnterTree))]
internal static class ExtraDeckViewEnterTreePatch
{
    private static void Prefix(NDeckViewScreen __instance)
    {
        ExtraDeckTopBarUi.RegisterOpeningScreen(__instance);
    }

    private static void Postfix(NDeckViewScreen __instance)
    {
        ExtraDeckTopBarUi.ValidateOpeningScreen(__instance);
    }
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen._Ready))]
internal static class ExtraDeckViewReadyPatch
{
    private static void Postfix(NDeckViewScreen __instance)
    {
        ExtraDeckTopBarUi.LocalizeVirtualPileScreen(__instance);
    }
}

[HarmonyPatch(typeof(NDeckViewScreen), "DisplayCards")]
internal static class ExtraDeckViewDisplayCardsPatch
{
    private static void Postfix(NDeckViewScreen __instance)
    {
        ExtraDeckTopBarUi.RefreshExtraDeckUsageBadges(__instance);
    }
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen._ExitTree))]
internal static class ExtraDeckViewExitTreePatch
{
    private static void Postfix(NDeckViewScreen __instance)
    {
        ExtraDeckTopBarUi.UnregisterScreen(__instance);
    }
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen.AfterCapstoneClosed))]
internal static class ExtraDeckViewClosedPatch
{
    private static void Postfix(NDeckViewScreen __instance)
    {
        ExtraDeckTopBarUi.RefreshClosedScreenButton(__instance);
    }
}

internal static class ExtraDeckTopBarUi
{
    internal const string ButtonNodeName = "ThermalVortexExtraDeckButton";
    internal const string RewardPoolButtonNodeName = "ThermalVortexRewardPoolButton";

    private const string ExtraDeckButtonMeta = "thermal_vortex_extra_deck_button";
    private const string RewardPoolButtonMeta = "thermal_vortex_reward_pool_button";
    private const string ExtraDeckTopBarIconFileName = "extra_deck_top_bar.png";
    private const int DuplicateWithoutSignals = 14;
    private const float ButtonSpacing = 8f;
    private static readonly Color RewardPoolGold = new(1f, 0.74f, 0.24f, 1f);
    private static readonly FieldInfo TopBarPlayerField = AccessTools.Field(typeof(NTopBar), "_player");

    private static readonly Dictionary<NTopBar, VirtualPileTopBarState> TopBarStates = [];
    private static readonly Dictionary<NTopBarDeckButton, VirtualPileButtonState> ButtonStates = [];
    private static readonly Dictionary<NDeckViewScreen, VirtualPileButtonState> VirtualPileScreens = [];

    [ThreadStatic]
    private static VirtualPileOpenContext _openingContext;

    internal static void Attach(NTopBar topBar)
    {
        if (!IsValid(topBar))
            return;

        Detach(topBar, removeButton: true);
        RemoveOrphanedVirtualPileNodes(topBar);

        var player = TopBarPlayerField?.GetValue(topBar) as Player;
        if (player is null || TryGetCore(player) is null || !IsValid(topBar.Deck))
            return;

        var vanillaDeckButton = topBar.Deck;
        if (!TryCloneDeckSlot(
                vanillaDeckButton,
                out var extraMountParent,
                out var extraMountNode,
                out var extraDeckButton,
                out var extraInsertIndex,
                out var extraUsesRightSideHBox))
        {
            MainFile.Logger.Info("Virtual-pile top-bar buttons could not clone the Extra Deck slot");
            return;
        }

        if (!TryCloneDeckSlot(
                vanillaDeckButton,
                out var rewardMountParent,
                out var rewardMountNode,
                out var rewardPoolButton,
                out var rewardInsertIndex,
                out var rewardUsesRightSideHBox))
        {
            FreeDetachedNode(extraMountNode);
            MainFile.Logger.Info("Virtual-pile top-bar buttons could not clone the reward-pool slot");
            return;
        }

        if (!ReferenceEquals(extraMountParent, rewardMountParent)
            || extraUsesRightSideHBox != rewardUsesRightSideHBox)
        {
            FreeDetachedNode(extraMountNode);
            FreeDetachedNode(rewardMountNode);
            MainFile.Logger.Info("Virtual-pile top-bar buttons resolved inconsistent mount parents");
            return;
        }

        ConfigureVirtualPileButton(
            VirtualPileKind.ExtraDeck,
            extraMountNode,
            extraDeckButton);
        ConfigureVirtualPileButton(
            VirtualPileKind.RewardPool,
            rewardMountNode,
            rewardPoolButton);

        var topBarState = new VirtualPileTopBarState(topBar, vanillaDeckButton, player);
        var extraDeckState = new VirtualPileButtonState(
            topBarState,
            VirtualPileKind.ExtraDeck,
            extraDeckButton,
            extraMountParent,
            extraMountNode,
            extraInsertIndex,
            extraUsesRightSideHBox,
            player);
        var rewardPoolState = new VirtualPileButtonState(
            topBarState,
            VirtualPileKind.RewardPool,
            rewardPoolButton,
            rewardMountParent,
            rewardMountNode,
            rewardInsertIndex,
            rewardUsesRightSideHBox,
            player);
        topBarState.SetButtons(rewardPoolState, extraDeckState);

        TopBarStates[topBar] = topBarState;
        ButtonStates[extraDeckButton] = extraDeckState;
        ButtonStates[rewardPoolButton] = rewardPoolState;

        try
        {
            // Both clones target the vanilla deck index. Mounting Extra Deck first and
            // reward pool second leaves the final visual order Reward Pool, Extra Deck,
            // vanilla Deck in both HBox and fallback layouts.
            extraDeckState.Mount();
            rewardPoolState.Mount();
            InitializeVirtualPileButton(extraDeckState);
            InitializeVirtualPileButton(rewardPoolState);
            topBarState.RefreshAll(force: true);
            Callable.From(topBarState.FinishLayoutAndNavigation).CallDeferred();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Virtual-pile top-bar button attach failed error={ex}");
            Detach(topBar, removeButton: true);
        }
    }

    internal static void Detach(NTopBar topBar, bool removeButton)
    {
        if (topBar is null || !TopBarStates.Remove(topBar, out var topBarState))
            return;

        foreach (var state in topBarState.Buttons)
            ButtonStates.Remove(state.Button);

        foreach (var (screen, state) in VirtualPileScreens.ToArray())
        {
            if (ReferenceEquals(state.TopBarState, topBarState))
            {
                VirtualPileScreens.Remove(screen);
                if (state.Kind == VirtualPileKind.ExtraDeck)
                    CardSelectionLocationBadgeUi.RefreshScreenDeferred(screen);
            }
        }

        topBarState.Dispose(removeButton);
    }

    internal static void DetachAll()
    {
        foreach (var topBar in TopBarStates.Keys.ToArray())
            Detach(topBar, removeButton: true);

        VirtualPileScreens.Clear();
        _openingContext = null;
    }

    internal static bool IsExtraDeckButton(NTopBarDeckButton button) =>
        IsValid(button) && button.HasMeta(ExtraDeckButtonMeta);

    internal static bool IsVirtualPileButton(NTopBarDeckButton button) =>
        TryGetVirtualPileKind(button, out _);

    internal static bool BeforeDeckButtonReleased(NTopBarDeckButton button)
    {
        VirtualPileButtonState clickedState = null;
        var clickedVirtualPile = TryGetVirtualPileKind(button, out var clickedKind);
        if (clickedVirtualPile
            && (!ButtonStates.TryGetValue(button, out clickedState) || !clickedState.IsAvailable))
        {
            return false;
        }

        var requestedKind = clickedVirtualPile ? clickedKind : (VirtualPileKind?)null;
        var capstone = TryGetCapstoneContainer();
        if (capstone?.CurrentCapstoneScreen is NDeckViewScreen currentDeckScreen)
        {
            var currentKind = VirtualPileScreens.TryGetValue(currentDeckScreen, out var currentState)
                ? currentState.Kind
                : (VirtualPileKind?)null;
            if (currentKind != requestedKind)
                capstone.Close();
        }

        if (clickedState is not null)
            BeginOpening(clickedState, buttonInitialization: false);

        return true;
    }

    internal static void AfterDeckButtonReleased(NTopBarDeckButton button)
    {
        if (_openingContext is not null && ReferenceEquals(_openingContext.Button, button))
            _openingContext = null;
    }

    internal static void OverrideDeckButtonOpenState(NTopBarDeckButton button, ref bool isOpen)
    {
        var capstone = TryGetCapstoneContainer();
        if (capstone?.CurrentCapstoneScreen is not NDeckViewScreen screen)
            return;

        if (VirtualPileScreens.TryGetValue(screen, out var activeState))
        {
            isOpen = TryGetVirtualPileKind(button, out var buttonKind)
                && buttonKind == activeState.Kind;
            return;
        }

        if (IsVirtualPileButton(button))
            isOpen = false;
    }

    internal static void ReplaceVirtualPileHoverTip(NTopBarDeckButton button)
    {
        if (!ButtonStates.TryGetValue(button, out var state) || !state.IsAvailable)
            return;

        var core = state.Core;
        if (core is null)
            return;

        NHoverTipSet.Remove(button);

        var titleKey = state.Kind switch
        {
            VirtualPileKind.RewardPool => "THERMALVORTEX_CARDPILE_REWARD_POOL.title",
            _ => "THERMALVORTEX_CARDPILE_EXTRA_DECK.title"
        };
        LocString description;
        if (state.Kind == VirtualPileKind.RewardPool)
        {
            description = new LocString(
                "static_hover_tips",
                "THERMALVORTEX_CARDPILE_REWARD_POOL.description");
            AddRewardPoolCountArgs(description, core);
        }
        else
        {
            var descriptionKey = core.OwnedExtraDeckCardCount == 0
                ? "THERMALVORTEX_CARDPILE_EXTRA_DECK.empty"
                : core.IsCombatExtraDeckPreparedForDisplay
                    ? "THERMALVORTEX_CARDPILE_EXTRA_DECK.combatDescription"
                    : "THERMALVORTEX_CARDPILE_EXTRA_DECK.description";
            description = new LocString("static_hover_tips", descriptionKey);
            description.Add("Remaining", (decimal)core.DisplayRemainingExtraDeckCardCount);
            description.Add("Owned", (decimal)core.OwnedExtraDeckCardCount);
        }

        var hoverTipSet = NHoverTipSet.CreateAndShow(
            button,
            new HoverTip(
                new LocString("static_hover_tips", titleKey),
                description,
                null),
            HoverTipAlignment.None);

        if (hoverTipSet is not null)
        {
            hoverTipSet.SetGlobalPosition(
                button.GlobalPosition + new Vector2(
                    button.Size.X - hoverTipSet.Size.X,
                    button.Size.Y + 20f),
                false);
        }
    }

    internal static void RefreshButton(NTopBarDeckButton button)
    {
        if (ButtonStates.TryGetValue(button, out var state))
            state.TopBarState.RefreshButton(state, force: false);
    }

    internal static bool TryResolveOpeningPile(PileType pileType, Player player, out CardPile pile)
    {
        pile = null;
        var context = _openingContext;
        if (context is null
            || context.WasResolved
            || pileType != PileType.Deck
            || !ReferenceEquals(context.Player, player))
        {
            return false;
        }

        context.WasResolved = true;
        pile = context.Pile;
        return true;
    }

    internal static void RegisterOpeningScreen(NDeckViewScreen screen)
    {
        var context = _openingContext;
        if (context is null)
            return;

        VirtualPileScreens[screen] = context.State;
    }

    internal static void ValidateOpeningScreen(NDeckViewScreen screen)
    {
        var context = _openingContext;
        if (context is null || !context.WasResolved)
        {
            if (VirtualPileScreens.Remove(screen, out var invalidState)
                && invalidState.Kind == VirtualPileKind.ExtraDeck)
            {
                CardSelectionLocationBadgeUi.RefreshScreenDeferred(screen);
            }
            return;
        }

        RefreshExtraDeckUsageBadges(screen);
    }

    internal static bool TryGetExtraDeckUsage(Node node, CardModel card, out bool used)
    {
        used = false;
        if (card is not XyzMonsterCard { IsCanonical: false, ExtraDeckViewOwnedEntryIndex: >= 0 } extraCard)
            return false;

        for (var current = node; current is not null; current = current.GetParent())
        {
            if (current is not NDeckViewScreen screen)
                continue;

            // The model marker alone is not sufficient: copied cards may also
            // appear in inspection and selection UIs outside this virtual pile.
            if (!VirtualPileScreens.TryGetValue(screen, out var state)
                || state.Kind != VirtualPileKind.ExtraDeck
                || state.Core is not { } core
                || extraCard.ExtraDeckViewOwnedEntryIndex >= core.OwnedExtraDeckCardCount)
            {
                return false;
            }

            used = !core.IsOwnedExtraDeckEntryAvailableForDisplay(extraCard.ExtraDeckViewOwnedEntryIndex);
            return true;
        }

        return false;
    }

    internal static void RefreshExtraDeckUsageBadges(NDeckViewScreen screen)
    {
        if (VirtualPileScreens.TryGetValue(screen, out var state)
            && state.Kind == VirtualPileKind.ExtraDeck)
        {
            CardSelectionLocationBadgeUi.RefreshScreenDeferred(screen);
        }
    }

    internal static void LocalizeVirtualPileScreen(NDeckViewScreen screen)
    {
        if (!VirtualPileScreens.TryGetValue(screen, out var state))
            return;

        var key = state.Kind switch
        {
            VirtualPileKind.RewardPool => "THERMALVORTEX_CARDPILE_REWARD_POOL.screenInfo",
            _ => "THERMALVORTEX_CARDPILE_EXTRA_DECK.screenInfo"
        };
        var info = new LocString("static_hover_tips", key);
        if (state.Kind == VirtualPileKind.RewardPool)
            AddRewardPoolCountArgs(info, state.Core);

        var bottomLabel = screen.GetNodeOrNull<RichTextLabel>("%BottomLabel");
        if (bottomLabel is not null)
            bottomLabel.Text = info.GetFormattedText();
    }

    internal static void UnregisterScreen(NDeckViewScreen screen)
    {
        if (VirtualPileScreens.Remove(screen, out var state) && state.Kind == VirtualPileKind.ExtraDeck)
            CardSelectionLocationBadgeUi.RefreshScreenDeferred(screen);
    }

    internal static void RefreshClosedScreenButton(NDeckViewScreen screen)
    {
        if (VirtualPileScreens.TryGetValue(screen, out var state) && IsValid(state.Button))
            state.Button.ToggleAnimState();
    }

    internal static CardPile CreatePreviewPile(
        VirtualPileKind kind,
        ThermalVortexCore core,
        Player player)
    {
        var pile = new CardPile(PileType.Deck);
        if (core is null)
            return pile;

        if (kind == VirtualPileKind.ExtraDeck)
        {
            foreach (var card in core.OwnedExtraDeckViewCards)
                pile.AddInternal(card, pile.Cards.Count, false);
            return pile;
        }

        var definition = core.CurrentRewardPoolDefinition;
        var validation = definition?.Validate();
        if (definition?.IsManual != true || validation?.IsValid != true)
        {
            MainFile.Logger.Info(
                $"Reward-pool virtual pile rejected unavailable definition validation={validation}");
            return pile;
        }

        AddMutablePreviewCards(pile, definition.GetSelectedMainRewardCards(), player);
        AddMutablePreviewCards(pile, definition.GetSelectedExtraCards(), player);
        return pile;
    }

    internal static string FormatButtonCount(VirtualPileKind kind, ThermalVortexCore core)
    {
        if (core is null)
            return "0";

        if (kind == VirtualPileKind.RewardPool)
        {
            return $"{SelectedMainRewardCount(core)}/{core.CurrentRewardPoolDefinition?.ExtraCardIds.Count ?? 0}";
        }

        return core.IsCombatExtraDeckPreparedForDisplay
            ? $"{core.DisplayRemainingExtraDeckCardCount}/{core.OwnedExtraDeckCardCount}"
            : core.OwnedExtraDeckCardCount.ToString();
    }

    private static void InitializeVirtualPileButton(VirtualPileButtonState state)
    {
        // Initialize against an isolated empty pile so neither virtual button subscribes
        // to (or animates for) changes in the ordinary draw deck. The real preview pile
        // is rebuilt from the authoritative relic snapshot only when the screen opens.
        BeginOpening(state, buttonInitialization: true);
        try
        {
            state.Button.Initialize(state.Player);
        }
        finally
        {
            AfterDeckButtonReleased(state.Button);
        }
    }

    private static void BeginOpening(VirtualPileButtonState state, bool buttonInitialization)
    {
        var core = state?.Core;
        if (core is null || (!buttonInitialization && !state.IsAvailable))
        {
            _openingContext = null;
            return;
        }

        var pile = buttonInitialization
            ? new CardPile(PileType.Deck)
            : CreatePreviewPile(state.Kind, core, state.Player);
        _openingContext = new VirtualPileOpenContext(
            state,
            state.Button,
            state.Player,
            pile);
    }

    private static void AddMutablePreviewCards(
        CardPile pile,
        IEnumerable<CardModel> canonicalCards,
        Player player)
    {
        foreach (var canonical in canonicalCards)
        {
            if (canonical?.ToMutable() is not CardModel preview)
                continue;

            if (player is not null)
                preview.Owner = player;
            pile.AddInternal(preview, pile.Cards.Count, false);
        }
    }

    private static int SelectedMainRewardCount(ThermalVortexCore core) =>
        core?.CurrentRewardPoolDefinition?.MainCardIds.Count(id => !RewardPoolCatalog.IsRequiredMainCard(id)) ?? 0;

    private static void AddRewardPoolCountArgs(LocString text, ThermalVortexCore core)
    {
        text.Add("Main", (decimal)SelectedMainRewardCount(core));
        text.Add("Extra", (decimal)(core?.CurrentRewardPoolDefinition?.ExtraCardIds.Count ?? 0));
    }

    private static bool TryGetVirtualPileKind(
        NTopBarDeckButton button,
        out VirtualPileKind kind)
    {
        kind = default;
        if (!IsValid(button))
            return false;

        if (ButtonStates.TryGetValue(button, out var state))
        {
            kind = state.Kind;
            return true;
        }

        if (button.HasMeta(RewardPoolButtonMeta))
        {
            kind = VirtualPileKind.RewardPool;
            return true;
        }

        if (button.HasMeta(ExtraDeckButtonMeta))
        {
            kind = VirtualPileKind.ExtraDeck;
            return true;
        }

        return false;
    }

    private static void ConfigureVirtualPileButton(
        VirtualPileKind kind,
        Node mountNode,
        NTopBarDeckButton button)
    {
        var nodeName = kind == VirtualPileKind.RewardPool
            ? RewardPoolButtonNodeName
            : ButtonNodeName;
        mountNode.Name = $"{nodeName}Slot";
        button.Name = nodeName;
        button.UniqueNameInOwner = false;
        button.SetMeta(
            kind == VirtualPileKind.RewardPool ? RewardPoolButtonMeta : ExtraDeckButtonMeta,
            true);
        ConfigureVirtualPileIcon(kind, button);
    }

    private static void ConfigureVirtualPileIcon(VirtualPileKind kind, NTopBarDeckButton button)
    {
        var icon = button.GetNodeOrNull<TextureRect>("Control/Icon");
        if (icon is null)
            return;

        if (icon.Material is not null)
            icon.Material = icon.Material.Duplicate(true) as Material;

        if (kind == VirtualPileKind.RewardPool)
        {
            icon.SelfModulate = RewardPoolGold;
            return;
        }

        icon.SelfModulate = Colors.White;
        TryApplyExtraDeckIcon(icon);
    }

    private static void TryApplyExtraDeckIcon(TextureRect icon)
    {
        var iconPath = ExtraDeckTopBarIconFileName.CharacterUiPath();

        try
        {
            if (!ResourceLoader.Exists(iconPath))
            {
                MainFile.Logger.Info(
                    $"Extra-deck top-bar icon unavailable path={iconPath}; using white vanilla icon");
                return;
            }

            var texture = ResourceLoader.Load<Texture2D>(iconPath);
            if (texture is null)
            {
                MainFile.Logger.Info(
                    $"Extra-deck top-bar icon failed to load path={iconPath}; using white vanilla icon");
                return;
            }

            icon.Texture = texture;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info(
                $"Extra-deck top-bar icon load failed path={iconPath} error={ex}; using white vanilla icon");
        }
    }

    private static bool TryCloneDeckSlot(
        NTopBarDeckButton vanillaDeckButton,
        out Node mountParent,
        out Node mountNode,
        out NTopBarDeckButton clonedDeckButton,
        out int insertIndex,
        out bool usesRightSideHBox)
    {
        mountParent = null;
        mountNode = null;
        clonedDeckButton = null;
        insertIndex = -1;
        usesRightSideHBox = false;

        var immediateParent = vanillaDeckButton?.GetParent();
        if (immediateParent is null)
            return false;

        Node deckSlotAnchor = vanillaDeckButton;
        var ancestor = immediateParent;
        while (ancestor is not null
               && ancestor is not HBoxContainer
               && ancestor is not NTopBar)
        {
            deckSlotAnchor = ancestor;
            ancestor = ancestor.GetParent();
        }

        if (ancestor is HBoxContainer rightSideHBox)
        {
            var clonedSlot = deckSlotAnchor.Duplicate(DuplicateWithoutSignals);
            if (clonedSlot is null)
                return false;

            var deckPathInSlot = deckSlotAnchor.GetPathTo(vanillaDeckButton);
            var clonedButton = deckSlotAnchor == vanillaDeckButton
                ? clonedSlot as NTopBarDeckButton
                : clonedSlot.GetNodeOrNull<NTopBarDeckButton>(deckPathInSlot);
            if (clonedButton is null)
            {
                clonedSlot.Free();
                return false;
            }

            ClearUniqueNames(clonedSlot);
            mountParent = rightSideHBox;
            mountNode = clonedSlot;
            clonedDeckButton = clonedButton;
            insertIndex = deckSlotAnchor.GetIndex();
            usesRightSideHBox = true;
            return true;
        }

        if (vanillaDeckButton.Duplicate(DuplicateWithoutSignals) is not NTopBarDeckButton fallbackButton)
            return false;

        ClearUniqueNames(fallbackButton);
        mountParent = immediateParent;
        mountNode = fallbackButton;
        clonedDeckButton = fallbackButton;
        insertIndex = vanillaDeckButton.GetIndex();
        return true;
    }

    private static void RemoveOrphanedVirtualPileNodes(NTopBar topBar)
    {
        string[] nodeNames =
        [
            $"{ButtonNodeName}Slot",
            ButtonNodeName,
            $"{RewardPoolButtonNodeName}Slot",
            RewardPoolButtonNodeName
        ];
        foreach (var nodeName in nodeNames)
        {
            while (topBar.FindChild(nodeName, true, false) is Node orphan)
            {
                orphan.GetParent()?.RemoveChild(orphan);
                orphan.QueueFree();
            }
        }
    }

    private static void FreeDetachedNode(Node node)
    {
        if (!IsValid(node))
            return;

        node.GetParent()?.RemoveChild(node);
        node.Free();
    }

    private static void ClearUniqueNames(Node root)
    {
        root.UniqueNameInOwner = false;
        foreach (var child in root.GetChildren())
        {
            if (child is Node childNode)
                ClearUniqueNames(childNode);
        }
    }

    private static ThermalVortexCore TryGetCore(Player player)
    {
        try
        {
            return player?.GetRelic<ThermalVortexCore>();
        }
        catch
        {
            return null;
        }
    }

    private static bool HasValidManualRewardPoolDefinition(ThermalVortexCore core)
    {
        try
        {
            var definition = core?.CurrentRewardPoolDefinition;
            return definition?.IsManual == true && definition.Validate().IsValid;
        }
        catch
        {
            return false;
        }
    }

    private static NCapstoneContainer TryGetCapstoneContainer()
    {
        try
        {
            return NCapstoneContainer.Instance;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValid(GodotObject instance) =>
        instance is not null && GodotObject.IsInstanceValid(instance);

    private sealed class VirtualPileOpenContext(
        VirtualPileButtonState state,
        NTopBarDeckButton button,
        Player player,
        CardPile pile)
    {
        public VirtualPileButtonState State { get; } = state;
        public NTopBarDeckButton Button { get; } = button;
        public Player Player { get; } = player;
        public CardPile Pile { get; } = pile;
        public bool WasResolved { get; set; }
    }

    private sealed class VirtualPileTopBarState(
        NTopBar topBar,
        NTopBarDeckButton vanillaDeckButton,
        Player player)
    {
        private readonly NTopBar _topBar = topBar;
        private readonly NTopBarDeckButton _vanillaDeckButton = vanillaDeckButton;
        private readonly NodePath _originalDeckLeftNeighbor = vanillaDeckButton.FocusNeighborLeft;
        private readonly List<VirtualPileButtonState> _buttons = [];
        private Control _originalLeftControl;
        private NodePath _originalLeftControlRightNeighbor;
        private bool _navigationReady;
        private bool _disposed;

        public Player Player { get; } = player;
        public IReadOnlyList<VirtualPileButtonState> Buttons => _buttons;

        public void SetButtons(
            VirtualPileButtonState rewardPool,
            VirtualPileButtonState extraDeck)
        {
            _buttons.Clear();
            _buttons.Add(rewardPool);
            _buttons.Add(extraDeck);
        }

        public void RefreshAll(bool force)
        {
            var visibilityChanged = false;
            foreach (var button in _buttons)
                visibilityChanged |= button.Refresh(force);

            if (_navigationReady && (force || visibilityChanged))
                UpdateNavigation();
        }

        public void RefreshButton(VirtualPileButtonState state, bool force)
        {
            var visibilityChanged = state.Refresh(force);
            if (_navigationReady && (force || visibilityChanged))
                UpdateNavigation();
        }

        public void FinishLayoutAndNavigation()
        {
            if (_disposed
                || !IsValid(_topBar)
                || !IsValid(_vanillaDeckButton)
                || _buttons.Any(state => !IsValid(state.Button) || state.Button.GetParent() is null))
            {
                return;
            }

            if (_buttons.All(state => !state.UsesRightSideHBox))
            {
                var width = Mathf.Max(_vanillaDeckButton.Size.X, _vanillaDeckButton.CustomMinimumSize.X);
                if (width <= 0f)
                    width = 72f;

                for (var index = 0; index < _buttons.Count; index++)
                {
                    var slotsFromDeck = _buttons.Count - index;
                    _buttons[index].Button.Position = _vanillaDeckButton.Position
                        - new Vector2(slotsFromDeck * (width + ButtonSpacing), 0f);
                }
            }

            _originalLeftControl = ResolveFocusNeighbor(
                _vanillaDeckButton,
                _originalDeckLeftNeighbor);
            _originalLeftControlRightNeighbor = IsValid(_originalLeftControl)
                ? _originalLeftControl.FocusNeighborRight
                : default;
            _navigationReady = true;
            UpdateNavigation();
        }

        public void Dispose(bool removeButton)
        {
            if (_disposed)
                return;

            _disposed = true;
            RestoreNavigation();
            foreach (var button in _buttons)
                button.Dispose(removeButton);
        }

        private void UpdateNavigation()
        {
            if (!_navigationReady || !IsValid(_vanillaDeckButton))
                return;

            RestoreNavigation();
            var visibleButtons = _buttons
                .Where(state => state.IsAvailable && IsValid(state.Button) && state.Button.Visible)
                .ToList();
            if (visibleButtons.Count == 0)
                return;

            Control previous = _originalLeftControl;
            var previousPath = IsValid(previous)
                ? previous.GetPath()
                : _originalDeckLeftNeighbor;
            foreach (var state in visibleButtons)
            {
                state.Button.FocusNeighborLeft = previousPath;
                if (IsValid(previous))
                    previous.FocusNeighborRight = state.Button.GetPath();

                previous = state.Button;
                previousPath = state.Button.GetPath();
            }

            previous.FocusNeighborRight = _vanillaDeckButton.GetPath();
            _vanillaDeckButton.FocusNeighborLeft = previous.GetPath();
        }

        private void RestoreNavigation()
        {
            if (!IsValid(_vanillaDeckButton))
                return;

            if (PointsAtVirtualButton(_vanillaDeckButton.FocusNeighborLeft))
                _vanillaDeckButton.FocusNeighborLeft = _originalDeckLeftNeighbor;

            if (IsValid(_originalLeftControl)
                && PointsAtVirtualButton(_originalLeftControl.FocusNeighborRight))
            {
                _originalLeftControl.FocusNeighborRight = _originalLeftControlRightNeighbor;
            }

            foreach (var state in _buttons)
            {
                if (!IsValid(state.Button))
                    continue;

                state.Button.FocusNeighborLeft = default;
                state.Button.FocusNeighborRight = default;
            }
        }

        private bool PointsAtVirtualButton(NodePath path) =>
            _buttons.Any(state => IsValid(state.Button) && path == state.Button.GetPath());

        private static Control ResolveFocusNeighbor(Control source, NodePath neighborPath)
        {
            if (!IsValid(source) || neighborPath.IsEmpty)
                return null;

            try
            {
                return source.GetNodeOrNull<Control>(neighborPath);
            }
            catch
            {
                return null;
            }
        }
    }

    private sealed class VirtualPileButtonState(
        VirtualPileTopBarState topBarState,
        VirtualPileKind kind,
        NTopBarDeckButton button,
        Node mountParent,
        Node mountNode,
        int insertIndex,
        bool usesRightSideHBox,
        Player player)
    {
        private readonly Node _mountParent = mountParent;
        private readonly Node _mountNode = mountNode;
        private readonly int _insertIndex = insertIndex;
        private readonly MegaLabel _countLabel = button.GetNodeOrNull<MegaLabel>("DeckCardCount");
        private readonly bool _hasValidManualRewardPoolDefinition =
            kind != VirtualPileKind.RewardPool
            || HasValidManualRewardPoolDefinition(TryGetCore(player));
        private Tween _countTween;
        private string _lastSemanticCount;
        private bool _disposed;

        public VirtualPileTopBarState TopBarState { get; } = topBarState;
        public VirtualPileKind Kind { get; } = kind;
        public NTopBarDeckButton Button { get; } = button;
        public Player Player { get; } = player;
        public bool UsesRightSideHBox { get; } = usesRightSideHBox;
        public ThermalVortexCore Core => TryGetCore(Player);
        public bool IsAvailable => Kind switch
        {
            VirtualPileKind.RewardPool => Core is not null && _hasValidManualRewardPoolDefinition,
            _ => Core is not null
        };

        public void Mount()
        {
            if (_disposed || !IsValid(_mountParent) || !IsValid(_mountNode))
                return;

            _mountParent.AddChild(_mountNode);
            var targetIndex = Math.Clamp(_insertIndex, 0, _mountParent.GetChildCount() - 1);
            _mountParent.MoveChild(_mountNode, targetIndex);
        }

        public bool Refresh(bool force)
        {
            if (_disposed || !IsValid(Button))
                return false;

            var core = Core;
            var shouldShow = IsAvailable;
            var mountCanvasItem = _mountNode as CanvasItem;
            var visibilityChanged = Button.Visible != shouldShow
                || (mountCanvasItem is not null && mountCanvasItem.Visible != shouldShow);
            if (Button.Visible != shouldShow)
                Button.Visible = shouldShow;
            if (mountCanvasItem is not null && mountCanvasItem.Visible != shouldShow)
                mountCanvasItem.Visible = shouldShow;

            if (!shouldShow || _countLabel is null)
                return visibilityChanged;

            var count = FormatButtonCount(Kind, core);
            var semanticCountChanged = _lastSemanticCount is not null
                && !string.Equals(_lastSemanticCount, count, StringComparison.Ordinal);
            if (force || !string.Equals(_countLabel.Text, count, StringComparison.Ordinal))
                _countLabel.SetTextAutoSize(count);

            if (semanticCountChanged)
                AnimateCountChange();

            _lastSemanticCount = count;
            return visibilityChanged;
        }

        public void Dispose(bool removeButton)
        {
            if (_disposed)
                return;

            _disposed = true;
            _countTween?.Kill();
            _countTween = null;
            if (IsValid(Button))
                NHoverTipSet.Remove(Button);

            if (!removeButton || !IsValid(_mountNode))
                return;

            _mountNode.GetParent()?.RemoveChild(_mountNode);
            _mountNode.QueueFree();
        }

        private void AnimateCountChange()
        {
            _countTween?.Kill();
            _countLabel.PivotOffset = _countLabel.Size * 0.5f;
            _countLabel.Scale = Vector2.One * 1.35f;
            _countTween = Button.CreateTween();
            _countTween
                .TweenProperty(_countLabel, "scale", Vector2.One, 0.2d)
                .SetEase(Tween.EaseType.Out)
                .SetTrans(Tween.TransitionType.Back);
        }
    }
}
