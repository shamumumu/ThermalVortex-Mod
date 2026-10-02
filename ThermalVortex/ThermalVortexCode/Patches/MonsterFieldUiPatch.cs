using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using ThermalVortex.ThermalVortexCode.CardInspection;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
internal static class MonsterFieldUiActivatePatch
{
    private static void Postfix(NCombatUi __instance, CombatState __0)
    {
        MonsterFieldUi.Attach(__instance, __0);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Deactivate))]
internal static class MonsterFieldUiDeactivatePatch
{
    private static void Prefix(NCombatUi __instance)
    {
        MonsterFieldUi.Detach(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), "_ExitTree")]
internal static class MonsterFieldUiExitTreePatch
{
    private static void Prefix(NCombatUi __instance)
    {
        MonsterFieldUi.Detach(__instance);
    }
}

[HarmonyPatch(typeof(CombatManager), "EndCombatInternal")]
internal static class MonsterFieldUiCombatEndPatch
{
    private static void Prefix()
    {
        MonsterFieldUi.DetachAll();
    }
}

[HarmonyPatch(typeof(CombatManager), "Reset", typeof(bool))]
internal static class MonsterFieldUiCombatResetPatch
{
    private static void Prefix()
    {
        MonsterFieldUi.DetachAll();
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
internal static class MonsterFieldUiMapOpenPatch
{
    private static void Postfix()
    {
        RelicHoverCardPileFade.EndRelicHover("map_open");
        MonsterFieldUi.RefreshVisibilityForAll("map_open");
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Close))]
internal static class MonsterFieldUiMapClosePatch
{
    private static void Postfix()
    {
        MonsterFieldUi.RefreshVisibilityForAll("map_close");
    }
}

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen.OnSubmenuOpened))]
internal static class MonsterFieldUiSettingsOpenedPatch
{
    private static void Postfix()
    {
        RelicHoverCardPileFade.EndRelicHover("settings_open");
        MonsterFieldUi.RefreshVisibilityForAll("settings_open");
    }
}

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen.OnSubmenuClosed))]
internal static class MonsterFieldUiSettingsClosedPatch
{
    private static void Postfix()
    {
        MonsterFieldUi.RefreshVisibilityForAll("settings_close");
    }
}

[HarmonyPatch(typeof(NSettingsScreen), "OnSubmenuShown")]
internal static class MonsterFieldUiSettingsShownPatch
{
    private static void Postfix()
    {
        RelicHoverCardPileFade.EndRelicHover("settings_shown");
        MonsterFieldUi.RefreshVisibilityForAll("settings_shown");
    }
}

[HarmonyPatch(typeof(NSettingsScreen), "OnSubmenuHidden")]
internal static class MonsterFieldUiSettingsHiddenPatch
{
    private static void Postfix()
    {
        MonsterFieldUi.RefreshVisibilityForAll("settings_hidden");
    }
}

[HarmonyPatch(typeof(NPauseMenu), nameof(NPauseMenu.OnSubmenuOpened))]
internal static class MonsterFieldUiPauseOpenedPatch
{
    private static void Postfix()
    {
        RelicHoverCardPileFade.EndRelicHover("pause_open");
        MonsterFieldUi.RefreshVisibilityForAll("pause_open");
    }
}

[HarmonyPatch(typeof(NPauseMenu), nameof(NPauseMenu.OnSubmenuClosed))]
internal static class MonsterFieldUiPauseClosedPatch
{
    private static void Postfix()
    {
        MonsterFieldUi.RefreshVisibilityForAll("pause_close");
    }
}

[HarmonyPatch(typeof(NPeekButton), nameof(NPeekButton.SetPeeking), typeof(bool))]
internal static class MonsterFieldUiPeekButtonPatch
{
    private static void Postfix(NPeekButton __instance)
    {
        MonsterFieldUi.OnPeekStateChanged(__instance);
    }
}

internal static class MonsterFieldUi
{
    internal const string NodeName = "ThermalVortexMonsterFieldUi";

    private static readonly Dictionary<NCombatUi, MonsterFieldUiState> States = [];
    private static readonly HashSet<NPeekButton> ActivePeekButtons = [];

    internal static void Attach(NCombatUi ui, CombatState state)
    {
        Detach(ui);

        var player = state is null ? null : LocalContext.GetMe(state);
        if (ui is null || player is null || player.Creature is null || !IsThermalVortexPlayer(player))
        {
            return;
        }

        var fieldUi = new MonsterFieldUiState(ui, player);
        States[ui] = fieldUi;
    }

    internal static void Detach(NCombatUi ui)
    {
        if (ui is null)
        {
            return;
        }

        if (!States.Remove(ui, out var state))
        {
            return;
        }

        state.Dispose();
    }

    internal static void DetachAll()
    {
        foreach (var state in States.Values.ToList())
        {
            state.Dispose();
        }

        States.Clear();
        ActivePeekButtons.Clear();
    }

    internal static void DetachIfCurrent(NCombatUi ui, MonsterFieldUiState state)
    {
        if (ui is null || state is null)
        {
            return;
        }

        if (!States.TryGetValue(ui, out var current) || !ReferenceEquals(current, state))
        {
            return;
        }

        States.Remove(ui);
        current.Dispose();
    }

    internal static void RefreshVisibilityForAll(string reason)
    {
        foreach (var state in States.Values.ToList())
        {
            state.RefreshVisibility(reason);
        }
    }

    internal static void OnPeekStateChanged(NPeekButton button)
    {
        var isPeeking = button is not null
                        && GodotObject.IsInstanceValid(button)
                        && button.IsPeeking;
        if (!isPeeking)
            ActivePeekButtons.Remove(button);
        else
            ActivePeekButtons.Add(button);

        RefreshVisibilityForAll(isPeeking ? "peek_open" : "peek_close");
    }

    internal static bool IsPeekActiveFor(Node currentScreen)
    {
        if (currentScreen is null || !GodotObject.IsInstanceValid(currentScreen))
            return false;

        foreach (var button in ActivePeekButtons.ToList())
        {
            if (button is null
                || !GodotObject.IsInstanceValid(button)
                || !button.IsPeeking)
            {
                ActivePeekButtons.Remove(button);
                continue;
            }

            // A covered overlay is disabled/hidden without resetting IsPeeking.
            // Keep it registered so Peek resumes when that overlay becomes topmost,
            // but never let a hidden/lower overlay reveal the field by itself.
            if (!button.IsInsideTree() || !button.IsVisibleInTree())
                continue;

            for (Node current = button; current is not null; current = current.GetParent())
            {
                if (ReferenceEquals(current, currentScreen))
                    return true;
            }
        }

        return false;
    }

    internal static bool IsThermalVortexPlayer(Player player) =>
        IsThermalVortexCharacter(player?.Character);

    private static bool IsThermalVortexCharacter(CharacterModel character)
    {
        if (character is null)
        {
            return false;
        }

        return character is ThermalVortexCharacter
            || character.GetType().FullName == typeof(ThermalVortexCharacter).FullName
            || string.Equals(character.Id?.Entry, ThermalVortexCharacter.CharacterId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(character.Id?.ToString(), ThermalVortexCharacter.CharacterId, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class MonsterFieldUiState : IDisposable
{
    private const int MaxColumns = 3;
    private const float BaseGap = 8f;
    private const float SafeLeft = 24f;
    private const float SafeTop = 150f;
    private const float SafeBottomInset = 330f;
    private const float PlayerGap = 28f;
    private const float MinimumScale = 0.68f;
    private const float FieldAnchorLeft = 80f;
    private const float FieldAnchorTop = 225f;
    private const float HoverBelowGap = 16f;
    private const int DefaultSceneLayerZIndex = -10;
    private const int DefaultCombatVfxLayerZIndex = -9;
    private static readonly Vector2 BaseSlotSize = new(72f, 100f);
    // Every summon slot follows one continuous rose-to-wine curve. Slot 1 is
    // #FFC8D2 (light pink-red), and slot 10 reaches #3B0D18 (deep wine red).
    private static readonly Color SummonSlotStartColor = new(1f, 0.784314f, 0.823529f, 1f);
    private static readonly Color SummonSlotEndColor = new(0.231373f, 0.050980f, 0.094118f, 1f);
    private readonly NCombatUi _ui;
    private readonly NCombatRoom _combatRoom;
    private readonly NPlayerHand _hand;
    private readonly ActiveScreenContext _screenContext;
    private readonly Player _player;
    private readonly CardPile _pile;
    private readonly Control _root;
    private readonly Control _effectOverlay;
    private readonly GridContainer _grid;
    private readonly Godot.Timer _refreshTimer;
    private readonly List<PanelContainer> _slots = [];
    private readonly List<Control> _contents = [];
    private readonly List<TextureRect> _icons = [];
    private readonly List<Label> _placeholders = [];
    private readonly List<Label> _healthLabels = [];
    private readonly List<Label> _devourBadges = [];
    private readonly List<ColorRect> _healthBarBackgrounds = [];
    private readonly List<ColorRect> _healthBarFills = [];
    private readonly List<CardModel> _displayedCards = [];
    private readonly HashSet<CardModel> _pendingDevourPulses = new(new ReferenceComparer<CardModel>());

    private NHoverTipSet _hoverTips;
    private CardModel _pressedCard;
    private int _pressedIndex = -1;
    private CardModel _dragCard;
    private int _dragIndex = -1;
    private int _lastCapacity = -1;
    private int _lastSlotCount = -1;
    private Vector2 _lastViewportSize = Vector2.Zero;
    private Vector2 _currentRootSize = Vector2.Zero;
    private Vector2 _currentSlotSize = Vector2.Zero;
    private float _currentGap = BaseGap;
    private bool _disposed;
    private bool _refreshPending;
    private bool _forceLayoutPending;
    private bool _refreshing;
    internal MonsterFieldUiState(NCombatUi ui, Player player)
    {
        _ui = ui;
        _combatRoom = FindCombatRoom(ui);
        _hand = ui.Hand;
        _screenContext = ActiveScreenContext.Instance;
        _player = player;
        _pile = MonsterFieldService.GetPile(player);
        // The native battle scene and combat VFX use negative absolute Z values.
        // Reuse those layers so combat/global screen UI at Z=0 or higher always stays above the field.
        var sceneLayerZIndex = ResolveSceneLayerZIndex(_combatRoom);
        var combatVfxLayerZIndex = ResolveCombatVfxLayerZIndex(_combatRoom);

        _root = new Control
        {
            Name = MonsterFieldUi.NodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = sceneLayerZIndex,
            ZAsRelative = false
        };

        // Keep transient field effects outside the slot tree. RebuildSlots queues
        // every slot for deletion, while this sibling remains valid long enough
        // to finish impact and summon animations from captured screen positions.
        _effectOverlay = new Control
        {
            Name = "ThermalVortexMonsterFieldEffects",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = combatVfxLayerZIndex,
            ZAsRelative = false,
            ClipContents = false
        };
        _effectOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _grid = new GridContainer
        {
            Name = "Slots",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _root.AddChild(_grid);

        _refreshTimer = new Godot.Timer
        {
            Name = "RefreshTimer",
            WaitTime = 0.25d,
            Autostart = true,
            OneShot = false
        };
        _refreshTimer.Timeout += RefreshIfChanged;
        _root.AddChild(_refreshTimer);

        _ui.AddChild(_root);
        _ui.AddChild(_effectOverlay);
        _pile.ContentsChanged += Refresh;
        MonsterFieldHealthService.Changed += Refresh;
        MonsterFieldService.VisualMutationCompleted += OnVisualMutationCompleted;
        MonsterFieldEventService.MonsterEnteredVisual += OnMonsterEnteredVisual;
        MonsterFieldEventService.MonsterImpactedVisual += OnMonsterImpactedVisual;
        CyberDevourState.Changed += OnDevourStateChanged;
        if (_screenContext is not null)
            _screenContext.Updated += OnActiveScreenContextUpdated;
        if (_hand is not null)
            _hand.ModeChanged += OnHandModeChanged;
        UpdateRootVisibility("attach");
        RequestRefresh(true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        HideHover();
        CardInspectionService.TryClose(_root);
        foreach (var slot in _slots)
            CardInspectionRegistry.Unregister(slot);
        if (_pile is not null)
        {
            _pile.ContentsChanged -= Refresh;
        }

        MonsterFieldHealthService.Changed -= Refresh;
        MonsterFieldService.VisualMutationCompleted -= OnVisualMutationCompleted;
        MonsterFieldEventService.MonsterEnteredVisual -= OnMonsterEnteredVisual;
        MonsterFieldEventService.MonsterImpactedVisual -= OnMonsterImpactedVisual;
        CyberDevourState.Changed -= OnDevourStateChanged;
        if (_screenContext is not null)
            _screenContext.Updated -= OnActiveScreenContextUpdated;
        if (_hand is not null && GodotObject.IsInstanceValid(_hand))
            _hand.ModeChanged -= OnHandModeChanged;

        if (_refreshTimer is not null)
        {
            _refreshTimer.Timeout -= RefreshIfChanged;
        }

        if (GodotObject.IsInstanceValid(_root))
        {
            _root.Visible = false;
            _root.GetParent()?.RemoveChild(_root);
            _root.QueueFree();
        }

        if (GodotObject.IsInstanceValid(_effectOverlay))
        {
            _effectOverlay.Visible = false;
            _effectOverlay.GetParent()?.RemoveChild(_effectOverlay);
            _effectOverlay.QueueFree();
        }
    }

    private void AddSlot(int index, Vector2 slotSize)
    {
        var slot = new PanelContainer
        {
            Name = $"Slot{index + 1}",
            // Peek permits inspection; changes to the field still require the
            // ordinary combat interaction state.
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = slotSize,
            Size = slotSize
        };
        slot.MouseEntered += () => ShowHover(slot, index);
        slot.MouseExited += () => CardInspectionHoverTips.RequestSoftRemove(_root);
        slot.GuiInput += input => HandleSlotInput(index, input);

        var content = new Control
        {
            Name = "Content",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = slotSize,
            Size = slotSize
        };

        var icon = new TextureRect
        {
            Name = "CardArt",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = slotSize,
            Size = slotSize
        };

        var placeholder = new Label
        {
            Name = "EmptyMarker",
            Text = "+",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(0.68f, 0.95f, 1f, 0.72f),
            CustomMinimumSize = slotSize,
            Size = slotSize
        };
        placeholder.AddThemeFontSizeOverride("font_size", Math.Max(22, (int)Math.Round(slotSize.X * 0.42f)));

        var healthBarBackground = new ColorRect
        {
            Name = "HealthBarBackground",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = new Color(0.02f, 0.01f, 0.01f, 0.88f)
        };

        var healthBarFill = new ColorRect
        {
            Name = "HealthBarFill",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Color = new Color(0.12f, 0.92f, 0.48f, 0.95f)
        };
        healthBarBackground.AddChild(healthBarFill);

        var healthLabel = new Label
        {
            Name = "HealthText",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = Colors.White
        };
        healthLabel.AddThemeFontSizeOverride("font_size", Math.Max(10, (int)Math.Round(slotSize.X * 0.19f)));
        healthLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        healthLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        healthLabel.AddThemeConstantOverride("shadow_offset_y", 1);

        var devourBadge = new Label
        {
            Name = "DevourBadge",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(0.88f, 0.72f, 1f, 1f),
            Visible = false,
            ZIndex = 3
        };
        devourBadge.AddThemeFontSizeOverride("font_size", Math.Max(9, (int)Math.Round(slotSize.X * 0.15f)));
        devourBadge.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.01f, 0.18f, 0.95f));
        devourBadge.AddThemeConstantOverride("outline_size", 3);

        content.AddChild(icon);
        content.AddChild(placeholder);
        content.AddChild(healthBarBackground);
        content.AddChild(healthLabel);
        content.AddChild(devourBadge);
        slot.AddChild(content);

        _grid.AddChild(slot);
        _slots.Add(slot);
        CardInspectionRegistry.Register(
            slot,
            _root,
            _ => CreateInspectionRequest(index, slot),
            ShouldAllowSlotInspection);
        _contents.Add(content);
        _icons.Add(icon);
        _placeholders.Add(placeholder);
        _healthLabels.Add(healthLabel);
        _devourBadges.Add(devourBadge);
        _healthBarBackgrounds.Add(healthBarBackground);
        _healthBarFills.Add(healthBarFill);
    }

    private void Refresh() => RequestRefresh(false);

    private void OnActiveScreenContextUpdated() =>
        RefreshVisibility("active_screen_context");

    private void OnHandModeChanged() =>
        RefreshVisibility("hand_mode");

    private void OnVisualMutationCompleted(Player player)
    {
        if (ReferenceEquals(player, _player))
            RequestRefresh(false);
    }

    private void OnDevourStateChanged(CardModel card)
    {
        if (card?.Owner != _player)
            return;

        if (MonsterFieldService.IsOnField(card))
            _pendingDevourPulses.Add(card);
        RequestRefresh(false);
    }

    private void OnMonsterEnteredVisual(MonsterFieldEnterEvent enterEvent)
    {
        if (!IsOwnedByPlayer(enterEvent.Card))
            return;

        try
        {
            RequestRefresh(false);
            if (!_root.Visible || !GodotObject.IsInstanceValid(_effectOverlay))
                return;

            PlayMonsterEntryPulse(CaptureEffectSnapshot(enterEvent.Card));
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Monster field entry VFX failed: " + exception);
        }
    }

    private void OnMonsterImpactedVisual(MonsterFieldImpactEvent impactEvent)
    {
        if (impactEvent.Amount <= 0 || !IsOwnedByPlayer(impactEvent.Card))
            return;

        try
        {
            if (_root.Visible && GodotObject.IsInstanceValid(_effectOverlay))
                PlayMonsterImpact(CaptureEffectSnapshot(impactEvent.Card), impactEvent);

            RequestRefresh(false);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Monster field impact VFX failed: " + exception);
        }
    }

    private bool IsOwnedByPlayer(CardModel card)
    {
        try
        {
            return card is not null && ReferenceEquals(card.Owner, _player);
        }
        catch
        {
            return false;
        }
    }

    private void RequestRefresh(bool forceLayout)
    {
        if (_disposed || !GodotObject.IsInstanceValid(_root))
            return;

        _refreshPending = true;
        _forceLayoutPending |= forceLayout;
        DrainPendingRefresh();
    }

    private void DrainPendingRefresh()
    {
        if (_refreshing || _disposed || !GodotObject.IsInstanceValid(_root))
            return;

        if (!ShouldRemainAttached())
        {
            MonsterFieldUi.DetachIfCurrent(_ui, this);
            return;
        }

        if (UpdateRootVisibility("refresh"))
            _forceLayoutPending = true;

        if (!_root.Visible || MonsterFieldService.IsVisualMutationInProgress(_player))
        {
            HideHover();
            return;
        }

        _refreshing = true;
        try
        {
            while (_refreshPending)
            {
                if (_disposed || !GodotObject.IsInstanceValid(_root))
                    return;

                if (!ShouldRemainAttached())
                {
                    MonsterFieldUi.DetachIfCurrent(_ui, this);
                    return;
                }

                if (UpdateRootVisibility("refresh_pending"))
                    _forceLayoutPending = true;

                if (!_root.Visible || MonsterFieldService.IsVisualMutationInProgress(_player))
                {
                    HideHover();
                    return;
                }

                var forcePendingLayout = _forceLayoutPending;
                _refreshPending = false;
                _forceLayoutPending = false;
                try
                {
                    RefreshCore(forcePendingLayout);
                }
                catch
                {
                    _refreshPending = true;
                    _forceLayoutPending |= forcePendingLayout;
                    throw;
                }
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshIfChanged()
    {
        if (_disposed || !GodotObject.IsInstanceValid(_root))
        {
            return;
        }

        if (!ShouldRemainAttached())
        {
            MonsterFieldUi.DetachIfCurrent(_ui, this);
            return;
        }

        var becameVisible = UpdateRootVisibility("refresh_timer");
        if (!_root.Visible)
        {
            HideHover();
            return;
        }

        if (becameVisible)
        {
            RequestRefresh(true);
            return;
        }

        if (MonsterFieldService.IsVisualMutationInProgress(_player))
        {
            _refreshPending = true;
            return;
        }

        if (_refreshPending)
        {
            DrainPendingRefresh();
            return;
        }

        var capacity = MonsterFieldService.GetDisplayCapacity(_player);
        var cards = MonsterFieldService.GetDisplayMonsters(_player).ToList();
        var slotCount = Math.Max(capacity, cards.Count);
        var viewportSize = GetViewportSize();
        if (HasViewportChanged(viewportSize))
        {
            RequestRefresh(true);
            return;
        }

        if (capacity != _lastCapacity
            || slotCount != _lastSlotCount
            || CardsChanged(cards))
        {
            RequestRefresh(false);
            return;
        }

        UpdatePosition(viewportSize, _currentRootSize);
    }

    private void RefreshCore(bool forceLayout)
    {
        var capacity = MonsterFieldService.GetDisplayCapacity(_player);
        var cards = MonsterFieldService.GetDisplayMonsters(_player).ToList();
        var slotCount = Math.Max(capacity, cards.Count);
        var viewportSize = GetViewportSize();
        var playerRect = GetPlayerAnchorRect();
        var layout = CalculateLayout(slotCount, viewportSize, playerRect);
        var needsLayout = forceLayout
                          || slotCount != _lastSlotCount
                          || !ApproximatelyEqual(layout.SlotSize, _currentSlotSize);
        var contentChanged = CardsChanged(cards);
        var styleChanged = capacity != _lastCapacity;
        var animateAddedSlotFrom = needsLayout
                                   && _lastSlotCount >= 0
                                   && slotCount > _lastSlotCount
                                   && MonsterFieldService.IsCapacityLimitIgnored(_player)
            ? Math.Max(_lastSlotCount, capacity)
            : int.MaxValue;

        if (needsLayout || contentChanged || styleChanged)
        {
            HideHover();
        }

        if (needsLayout)
        {
            RebuildSlots(slotCount, layout);
        }

        _displayedCards.Clear();
        _displayedCards.AddRange(cards);
        var priorityTarget = _displayedCards
            .LastOrDefault(MonsterFieldService.CanBeAttackTarget);

        for (var i = 0; i < _slots.Count; i++)
        {
            var slotOrdinal = i + 1;
            var occupied = i < _displayedCards.Count;
            var isPriorityTarget = false;

            if (occupied)
            {
                var card = _displayedCards[i];
                isPriorityTarget = ReferenceEquals(card, priorityTarget);
                _icons[i].Texture = TryGetCardTexture(card);
                _icons[i].Modulate = Colors.White;
                _placeholders[i].Visible = false;
                ApplyHealth(_healthLabels[i], _healthBarBackgrounds[i], _healthBarFills[i], card);
                ApplyDevourBadge(_devourBadges[i], card);
                if (_pendingDevourPulses.Remove(card) && _devourBadges[i].Visible)
                    AnimateDevourBadge(_devourBadges[i]);
            }
            else
            {
                _icons[i].Texture = null;
                _icons[i].Modulate = new Color(1f, 1f, 1f, 0f);
                _placeholders[i].Visible = true;
                _healthLabels[i].Visible = false;
                _healthBarBackgrounds[i].Visible = false;
                _devourBadges[i].Visible = false;
            }

            ApplySlotStyle(_slots[i], occupied, isPriorityTarget, slotOrdinal);
            ApplyPlaceholderStyle(_placeholders[i], slotOrdinal);
            if (i >= animateAddedSlotFrom)
                AnimateAddedSlot(_slots[i], layout.SlotSize, slotOrdinal);
        }

        UpdatePosition(viewportSize, layout.RootSize);
        _lastCapacity = capacity;
        _lastSlotCount = slotCount;
        _lastViewportSize = viewportSize;
        _currentRootSize = layout.RootSize;
        _currentSlotSize = layout.SlotSize;
        _currentGap = layout.Gap;
        UpdateSlotInputMode(ShouldAllowSlotInteraction() || ShouldAllowSlotInspection());
    }

    private bool ShouldRemainAttached()
    {
        // CombatSetUp activates the UI while CombatManager is still starting.
        // Treating that transient phase as a permanent detach condition creates
        // a race: a timer/context update can dispose this state before combat
        // reaches IsInProgress, leaving no attachment path for the whole room.
        return GodotObject.IsInstanceValid(_ui)
               && _player is not null
               && MonsterFieldUi.IsThermalVortexPlayer(_player);
    }

    private bool IsCombatReadyForDisplay(CombatManager manager)
    {
        return ShouldRemainAttached()
               && _ui.IsInsideTree()
               && manager is not null
               && manager.IsInProgress
               && !manager.IsOverOrEnding
               && _player.Creature is not null
               && _player.PlayerCombatState is not null;
    }

    internal void RefreshVisibility(string reason)
    {
        if (_disposed || !GodotObject.IsInstanceValid(_root))
        {
            return;
        }

        if (!ShouldRemainAttached())
        {
            MonsterFieldUi.DetachIfCurrent(_ui, this);
            return;
        }

        var becameVisible = UpdateRootVisibility(reason);
        if (becameVisible)
            RequestRefresh(true);
        else if (_root.Visible && _refreshPending)
            DrainPendingRefresh();
    }

    private bool UpdateRootVisibility(string reason)
    {
        var visible = ShouldShowInCombatUi();
        var allowInteraction = visible && ShouldAllowSlotInteraction();
        var allowInspection = visible && ShouldAllowSlotInspection();
        UpdateSlotInputMode(allowInteraction || allowInspection);
        if (!allowInspection)
            HideHover();
        if (!allowInteraction)
        {
            CancelSlotInteraction();
        }

        if (_root.Visible == visible)
        {
            if (GodotObject.IsInstanceValid(_effectOverlay))
                _effectOverlay.Visible = visible;
            return false;
        }

        _root.Visible = visible;
        if (GodotObject.IsInstanceValid(_effectOverlay))
            _effectOverlay.Visible = visible;
        MainFile.Logger.Info($"MonsterFieldUi visibility visible={visible} reason={reason}");
        if (!visible)
        {
            RelicHoverCardPileFade.EndRelicHover($"monster_field_hidden_{reason}");
            ClearTransientEffects();
        }

        return visible;
    }

    private bool ShouldShowInCombatUi()
    {
        var manager = CombatManager.Instance;
        if (!IsCombatReadyForDisplay(manager))
        {
            return false;
        }

        if (!_ui.IsVisibleInTree() || manager.IsPaused)
        {
            return false;
        }

        if (_screenContext is null
            || _combatRoom is null
            || !ReferenceEquals(_combatRoom.Ui, _ui))
        {
            return false;
        }

        var currentScreen = _screenContext.GetCurrentScreen();
        if (MonsterFieldUi.IsPeekActiveFor(currentScreen as Node))
            return true;

        if (_hand is not null
            && GodotObject.IsInstanceValid(_hand)
            && _hand.IsInCardSelection)
        {
            return false;
        }

        return ReferenceEquals(currentScreen, _combatRoom);
    }

    private bool ShouldAllowSlotInteraction()
    {
        var manager = CombatManager.Instance;
        if (!IsCombatReadyForDisplay(manager)
            || !_ui.IsVisibleInTree()
            || manager.IsPaused
            || _screenContext is null
            || _combatRoom is null
            || !ReferenceEquals(_combatRoom.Ui, _ui))
        {
            return false;
        }

        var currentScreen = _screenContext.GetCurrentScreen();
        if (!ReferenceEquals(currentScreen, _combatRoom)
            || MonsterFieldUi.IsPeekActiveFor(currentScreen as Node))
        {
            return false;
        }

        return _hand is null
               || !GodotObject.IsInstanceValid(_hand)
               || !_hand.IsInCardSelection;
    }

    private bool ShouldAllowSlotInspection() =>
        !_disposed
        && GodotObject.IsInstanceValid(_root)
        && _root.IsVisibleInTree()
        && ShouldShowInCombatUi()
        && !CardInspectionService.IsBusy;

    private void UpdateSlotInputMode(bool allowInteraction)
    {
        var mouseFilter = allowInteraction
            ? Control.MouseFilterEnum.Stop
            : Control.MouseFilterEnum.Ignore;
        foreach (var slot in _slots)
        {
            if (GodotObject.IsInstanceValid(slot))
                slot.MouseFilter = mouseFilter;
        }
    }

    private static NCombatRoom FindCombatRoom(Node node)
    {
        for (var current = node?.GetParent(); current is not null; current = current.GetParent())
        {
            if (current is NCombatRoom combatRoom)
                return combatRoom;
        }

        return null;
    }

    private static int ResolveSceneLayerZIndex(NCombatRoom combatRoom)
    {
        if (combatRoom?.SceneContainer is { } sceneContainer
            && GodotObject.IsInstanceValid(sceneContainer))
            return Math.Min(DefaultSceneLayerZIndex, sceneContainer.ZIndex);

        return DefaultSceneLayerZIndex;
    }

    private static int ResolveCombatVfxLayerZIndex(NCombatRoom combatRoom)
    {
        if (combatRoom?.CombatVfxContainer is { } combatVfx
            && GodotObject.IsInstanceValid(combatVfx))
            return Math.Min(DefaultCombatVfxLayerZIndex, combatVfx.ZIndex);

        return DefaultCombatVfxLayerZIndex;
    }

    private void RebuildSlots(int slotCount, FieldLayout layout)
    {
        HideHover();

        foreach (var child in _grid.GetChildren())
        {
            if (child is Control control)
                CardInspectionRegistry.Unregister(control);
            _grid.RemoveChild(child);
            child.QueueFree();
        }

        _slots.Clear();
        _contents.Clear();
        _icons.Clear();
        _placeholders.Clear();
        _healthLabels.Clear();
        _devourBadges.Clear();
        _healthBarBackgrounds.Clear();
        _healthBarFills.Clear();

        _grid.Columns = layout.Columns;
        _grid.AddThemeConstantOverride("h_separation", (int)Math.Round(layout.Gap));
        _grid.AddThemeConstantOverride("v_separation", (int)Math.Round(layout.Gap));

        for (var i = 0; i < slotCount; i++)
        {
            AddSlot(i, layout.SlotSize);
        }

        _root.CustomMinimumSize = layout.RootSize;
        _root.Size = layout.RootSize;
        _grid.CustomMinimumSize = layout.RootSize;
        _grid.Size = layout.RootSize;
        ResizeSlotContents(layout.SlotSize);
    }

    private void ResizeSlotContents(Vector2 slotSize)
    {
        var inset = Math.Max(4f, slotSize.X * 0.075f);
        for (var i = 0; i < _slots.Count; i++)
        {
            _slots[i].CustomMinimumSize = slotSize;
            _slots[i].Size = slotSize;
            _contents[i].CustomMinimumSize = slotSize;
            _contents[i].Size = slotSize;

            _icons[i].Position = new Vector2(inset, inset);
            _icons[i].CustomMinimumSize = slotSize - new Vector2(inset * 2f, inset * 2f);
            _icons[i].Size = slotSize - new Vector2(inset * 2f, inset * 2f);

            _placeholders[i].Position = Vector2.Zero;
            _placeholders[i].CustomMinimumSize = slotSize;
            _placeholders[i].Size = slotSize;
            _placeholders[i].AddThemeFontSizeOverride("font_size", Math.Max(22, (int)Math.Round(slotSize.X * 0.42f)));

            var barHeight = Math.Max(7f, slotSize.Y * 0.075f);
            var barInset = Math.Max(5f, slotSize.X * 0.08f);
            var barWidth = Math.Max(12f, slotSize.X - barInset * 2f);
            var barY = slotSize.Y - barHeight - Math.Max(5f, slotSize.Y * 0.055f);
            _healthBarBackgrounds[i].Position = new Vector2(barInset, barY);
            _healthBarBackgrounds[i].CustomMinimumSize = new Vector2(barWidth, barHeight);
            _healthBarBackgrounds[i].Size = new Vector2(barWidth, barHeight);
            _healthBarFills[i].Position = Vector2.Zero;
            _healthBarFills[i].Size = new Vector2(barWidth, barHeight);

            var labelHeight = Math.Max(14f, slotSize.Y * 0.16f);
            _healthLabels[i].Position = new Vector2(0f, barY - labelHeight + 2f);
            _healthLabels[i].CustomMinimumSize = new Vector2(slotSize.X, labelHeight);
            _healthLabels[i].Size = new Vector2(slotSize.X, labelHeight);
            _healthLabels[i].AddThemeFontSizeOverride("font_size", Math.Max(10, (int)Math.Round(slotSize.X * 0.19f)));

            var badgeHeight = Math.Max(14f, slotSize.Y * 0.17f);
            _devourBadges[i].Position = new Vector2(slotSize.X * 0.18f, 1f);
            _devourBadges[i].CustomMinimumSize = new Vector2(slotSize.X * 0.78f, badgeHeight);
            _devourBadges[i].Size = new Vector2(slotSize.X * 0.78f, badgeHeight);
            _devourBadges[i].PivotOffset = new Vector2(_devourBadges[i].Size.X, badgeHeight / 2f);
            _devourBadges[i].AddThemeFontSizeOverride("font_size", Math.Max(9, (int)Math.Round(slotSize.X * 0.15f)));
        }
    }

    private void UpdatePosition(Vector2 viewportSize, Vector2 rootSize)
    {
        if (rootSize.X <= 0f || rootSize.Y <= 0f)
        {
            _root.Position = new Vector2(SafeLeft, SafeTop);
            return;
        }

        var safeBottom = Math.Max(SafeTop + rootSize.Y, viewportSize.Y - SafeBottomInset);
        var position = new Vector2(
            FieldAnchorLeft,
            Math.Clamp(FieldAnchorTop, SafeTop, Math.Max(SafeTop, safeBottom - rootSize.Y)));

        _root.Position = position;
        _root.Size = rootSize;
        _grid.Size = rootSize;
    }

    private FieldLayout CalculateLayout(int capacity, Vector2 viewportSize, Rect2 playerRect)
    {
        if (capacity <= 0)
            return new FieldLayout(Vector2.Zero, Vector2.Zero, 1, BaseGap);

        var columns = Math.Min(MaxColumns, Math.Max(1, capacity));
        var rows = (int)Math.Ceiling(capacity / (float)columns);
        var baseRootSize = CalculateRootSize(BaseSlotSize, BaseGap, columns, rows);
        var availableHeight = Math.Max(BaseSlotSize.Y, viewportSize.Y - SafeBottomInset - SafeTop);
        var verticalScale = baseRootSize.Y > availableHeight
            ? availableHeight / baseRootSize.Y
            : 1f;

        var horizontalScale = 1f;
        if (playerRect.Size.X > 0f)
        {
            var uiRect = _ui.GetGlobalRect();
            var localPlayerLeft = playerRect.Position.X - uiRect.Position.X;
            var availableWidth = Math.Max(BaseSlotSize.X, localPlayerLeft - SafeLeft - PlayerGap);
            if (baseRootSize.X > availableWidth)
                horizontalScale = availableWidth / baseRootSize.X;
        }

        var scale = Math.Clamp(Math.Min(verticalScale, horizontalScale), MinimumScale, 1f);
        var slotSize = new Vector2(
            (float)Math.Round(BaseSlotSize.X * scale),
            (float)Math.Round(BaseSlotSize.Y * scale));
        var gap = Math.Max(4f, BaseGap * scale);
        var rootSize = CalculateRootSize(slotSize, gap, columns, rows);
        return new FieldLayout(slotSize, rootSize, columns, gap);
    }

    private static Vector2 CalculateRootSize(Vector2 slotSize, float gap, int columns, int rows)
    {
        if (columns <= 0 || rows <= 0)
            return Vector2.Zero;

        return new Vector2(
            columns * slotSize.X + (columns - 1) * gap,
            rows * slotSize.Y + (rows - 1) * gap);
    }

    private Vector2 GetViewportSize()
    {
        var viewportSize = _ui.Size;
        if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
        {
            viewportSize = _ui.GetViewportRect().Size;
        }

        return viewportSize;
    }

    private Rect2 GetPlayerAnchorRect()
    {
        var playerNode = FindPlayerCreatureNode(_ui.GetTree()?.Root, _player);
        if (playerNode is null)
            return default;

        try
        {
            var hitbox = playerNode.Hitbox;
            if (hitbox is not null && GodotObject.IsInstanceValid(hitbox))
            {
                var hitboxRect = hitbox.GetGlobalRect();
                if (hitboxRect.Size.X > 0f && hitboxRect.Size.Y > 0f)
                    return hitboxRect;
            }
        }
        catch
        {
            // Fall back to the creature node rect below.
        }

        return playerNode.GetGlobalRect();
    }

    private static NCreature FindPlayerCreatureNode(Node root, Player player)
    {
        if (root is null)
        {
            return null;
        }

        if (root is NCreature creature && ReferenceEquals(creature.Entity, player.Creature))
        {
            return creature;
        }

        foreach (var child in root.GetChildren())
        {
            var match = FindPlayerCreatureNode(child, player);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static Texture2D TryGetCardTexture(CardModel card)
    {
        try
        {
            return card.Portrait;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ApplySlotStyle(
        PanelContainer slot,
        bool occupied,
        bool priorityTarget,
        int slotOrdinal)
    {
        var slotColor = GetSummonSlotColor(slotOrdinal);
        var backgroundColor = new Color(
            0.01f + slotColor.R * 0.16f,
            0.02f + slotColor.G * 0.12f,
            0.04f + slotColor.B * 0.14f,
            occupied ? 0.92f : 0.82f);
        var borderWidth = priorityTarget ? 4 : occupied ? 3 : 2;
        var style = new StyleBoxFlat
        {
            BgColor = backgroundColor,
            BorderColor = slotColor,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4
        };
        slot.AddThemeStyleboxOverride("panel", style);
    }

    private static void ApplyPlaceholderStyle(Label placeholder, int slotOrdinal)
    {
        var color = GetSummonSlotColor(slotOrdinal);
        placeholder.Modulate = new Color(color.R, color.G, color.B, 0.82f);
    }

    private MonsterFieldEffectSnapshot CaptureEffectSnapshot(CardModel card)
    {
        var slotIndex = FindDisplayedCardIndex(card);
        if (slotIndex < 0)
        {
            var fieldCards = MonsterFieldService.GetDisplayMonsters(_player);
            for (var i = 0; i < fieldCards.Count; i++)
            {
                if (!ReferenceEquals(fieldCards[i], card))
                    continue;

                slotIndex = i;
                break;
            }
        }

        if (slotIndex >= 0 && slotIndex < _slots.Count)
        {
            var slot = _slots[slotIndex];
            if (slot is not null && GodotObject.IsInstanceValid(slot) && slot.IsInsideTree())
            {
                var slotRect = slot.GetGlobalRect();
                if (slotRect.Size.X > 0f && slotRect.Size.Y > 0f)
                    return new MonsterFieldEffectSnapshot(ToOverlayRect(slotRect), slotIndex + 1);
            }
        }

        return new MonsterFieldEffectSnapshot(
            GetPlayerCenterFallbackRect(),
            Math.Max(1, slotIndex + 1));
    }

    private int FindDisplayedCardIndex(CardModel card)
    {
        for (var i = 0; i < _displayedCards.Count; i++)
        {
            if (ReferenceEquals(_displayedCards[i], card))
                return i;
        }

        return -1;
    }

    private Rect2 GetPlayerCenterFallbackRect()
    {
        var slotSize = _currentSlotSize.X > 0f && _currentSlotSize.Y > 0f
            ? _currentSlotSize
            : BaseSlotSize;
        var playerRect = GetPlayerAnchorRect();
        var globalCenter = playerRect.Size.X > 0f && playerRect.Size.Y > 0f
            ? playerRect.Position + playerRect.Size * 0.5f
            : _ui.GetGlobalRect().Position + GetViewportSize() * 0.5f;
        var overlayOrigin = _effectOverlay.GetGlobalRect().Position;
        return new Rect2(globalCenter - overlayOrigin - slotSize * 0.5f, slotSize);
    }

    private Rect2 ToOverlayRect(Rect2 globalRect) =>
        new(globalRect.Position - _effectOverlay.GetGlobalRect().Position, globalRect.Size);

    private void PlayMonsterEntryPulse(MonsterFieldEffectSnapshot snapshot)
    {
        var color = GetSummonSlotColor(snapshot.SlotOrdinal);
        var pulse = new Panel
        {
            Name = "MonsterEntryPulse",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = snapshot.Rect.Position,
            Size = snapshot.Rect.Size,
            PivotOffset = snapshot.Rect.Size * 0.5f,
            Scale = new Vector2(0.72f, 0.72f),
            ZIndex = 1
        };
        pulse.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(color.R, color.G, color.B, 0.24f),
            BorderColor = new Color(color.R, color.G, color.B, 0.98f),
            BorderWidthLeft = 4,
            BorderWidthTop = 4,
            BorderWidthRight = 4,
            BorderWidthBottom = 4,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        });
        _effectOverlay.AddChild(pulse);

        var tween = pulse.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(pulse, "scale", new Vector2(1.34f, 1.34f), 0.42d)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(pulse, "modulate", new Color(1f, 1f, 1f, 0f), 0.42d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
        tween.Finished += () => QueueFreeIfValid(pulse);
    }

    private void PlayMonsterImpact(
        MonsterFieldEffectSnapshot snapshot,
        MonsterFieldImpactEvent impactEvent)
    {
        var impactRoot = new Control
        {
            Name = impactEvent.Destroyed ? "MonsterDeathImpact" : "MonsterDamageImpact",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = snapshot.Rect.Position,
            Size = snapshot.Rect.Size,
            ClipContents = false,
            ZIndex = 2
        };
        _effectOverlay.AddChild(impactRoot);

        var flash = new ColorRect
        {
            Name = "WhiteFlash",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = snapshot.Rect.Size,
            Color = new Color(1f, 0.96f, 0.96f, impactEvent.Destroyed ? 0.92f : 0.74f)
        };
        impactRoot.AddChild(flash);

        var flashTween = flash.CreateTween();
        flashTween.TweenProperty(flash, "modulate", new Color(1f, 1f, 1f, 0f), 0.20d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        var origin = impactRoot.Position;
        var shakeDistance = Math.Clamp(snapshot.Rect.Size.X * 0.075f, 4f, 9f);
        var shakeTween = impactRoot.CreateTween();
        shakeTween.TweenProperty(impactRoot, "position", origin + new Vector2(-shakeDistance, 1f), 0.035d);
        shakeTween.TweenProperty(impactRoot, "position", origin + new Vector2(shakeDistance, -1f), 0.045d);
        shakeTween.TweenProperty(impactRoot, "position", origin + new Vector2(-shakeDistance * 0.55f, 0f), 0.045d);
        shakeTween.TweenProperty(impactRoot, "position", origin, 0.055d);

        var damageLabel = new Label
        {
            Name = "DamageNumber",
            Text = $"-{impactEvent.Amount}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(-20f, -34f),
            Size = new Vector2(snapshot.Rect.Size.X + 40f, 38f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = impactEvent.Destroyed
                ? new Color(1f, 0.84f, 0.30f, 1f)
                : new Color(1f, 0.24f, 0.20f, 1f),
            ZIndex = 4
        };
        damageLabel.AddThemeFontSizeOverride(
            "font_size",
            Math.Max(20, (int)Math.Round(snapshot.Rect.Size.X * 0.34f)));
        damageLabel.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0f, 0f, 0.95f));
        damageLabel.AddThemeConstantOverride("outline_size", 5);
        impactRoot.AddChild(damageLabel);

        var numberTween = damageLabel.CreateTween();
        numberTween.SetParallel(true);
        numberTween.TweenProperty(damageLabel, "position", damageLabel.Position + new Vector2(0f, -34f), 0.58d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        numberTween.TweenProperty(damageLabel, "modulate", new Color(1f, 1f, 1f, 0f), 0.46d)
            .SetDelay(0.12d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        if (impactEvent.Destroyed)
            AddMonsterDeathBurst(impactRoot, snapshot.Rect.Size, GetSummonSlotColor(snapshot.SlotOrdinal));

        var cleanupTween = impactRoot.CreateTween();
        cleanupTween.TweenInterval(impactEvent.Destroyed ? 0.78d : 0.66d);
        cleanupTween.Finished += () => QueueFreeIfValid(impactRoot);
    }

    private static void AddMonsterDeathBurst(Control root, Vector2 slotSize, Color slotColor)
    {
        var center = slotSize * 0.5f;
        for (var i = 0; i < 12; i++)
        {
            var particleSize = 5f + i % 3 * 2f;
            var particle = new ColorRect
            {
                Name = $"DeathParticle{i}",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Position = center - Vector2.One * (particleSize * 0.5f),
                Size = Vector2.One * particleSize,
                PivotOffset = Vector2.One * (particleSize * 0.5f),
                Color = i % 3 == 0 ? Colors.White : slotColor,
                ZIndex = 3
            };
            root.AddChild(particle);

            var angle = Mathf.Tau * i / 12f;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var distance = Math.Max(slotSize.X, slotSize.Y) * (0.55f + i % 4 * 0.08f);
            var particleTween = particle.CreateTween();
            particleTween.SetParallel(true);
            particleTween.TweenProperty(particle, "position", particle.Position + direction * distance, 0.56d)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            particleTween.TweenProperty(particle, "rotation", angle + Mathf.Pi, 0.56d);
            particleTween.TweenProperty(particle, "modulate", new Color(1f, 1f, 1f, 0f), 0.48d)
                .SetDelay(0.08d)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.In);
        }
    }

    private static void QueueFreeIfValid(Node node)
    {
        if (node is not null && GodotObject.IsInstanceValid(node))
            node.QueueFree();
    }

    private void ClearTransientEffects()
    {
        if (!GodotObject.IsInstanceValid(_effectOverlay))
            return;

        foreach (var child in _effectOverlay.GetChildren())
        {
            _effectOverlay.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void AnimateAddedSlot(PanelContainer slot, Vector2 slotSize, int slotOrdinal)
    {
        if (slot is null || !GodotObject.IsInstanceValid(slot))
            return;

        var color = GetSummonSlotColor(slotOrdinal);
        slot.PivotOffset = slotSize * 0.5f;
        slot.Scale = new Vector2(0.68f, 0.68f);
        slot.Rotation = slotOrdinal % 2 == 0 ? 0.045f : -0.045f;
        slot.Modulate = new Color(color.R, color.G, color.B, 0.18f);

        var tween = slot.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(slot, "scale", Vector2.One, 0.34d)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(slot, "rotation", 0f, 0.28d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(slot, "modulate", Colors.White, 0.24d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
    }

    internal static Color GetSummonSlotColor(int slotOrdinal)
    {
        if (slotOrdinal <= 1)
            return SummonSlotStartColor;
        if (slotOrdinal >= 10)
            return SummonSlotEndColor;

        // A sub-linear gamma makes the early slots change faster while keeping
        // all ten colors on the same continuous pink-red-wine trajectory.
        var normalized = (slotOrdinal - 1) / 9f;
        var progress = MathF.Pow(normalized, 0.72f);
        return Blend(SummonSlotStartColor, SummonSlotEndColor, progress);
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        var clamped = Math.Clamp(amount, 0f, 1f);
        return new Color(
            from.R + (to.R - from.R) * clamped,
            from.G + (to.G - from.G) * clamped,
            from.B + (to.B - from.B) * clamped,
            from.A + (to.A - from.A) * clamped);
    }

    private void ApplyHealth(Label label, ColorRect background, ColorRect fill, CardModel card)
    {
        var health = MonsterFieldHealthService.GetHealth(card);
        if (!health.IsValid)
        {
            label.Visible = false;
            background.Visible = false;
            return;
        }

        label.Visible = true;
        background.Visible = true;
        label.Text = $"{health.CurrentHp}/{health.MaxHp}";

        var fillRatio = Math.Clamp(health.CurrentHp / (float)Math.Max(1, health.MaxHp), 0f, 1f);
        fill.Size = new Vector2(background.Size.X * fillRatio, background.Size.Y);
        fill.Color = fillRatio > 0.5f
            ? new Color(0.12f, 0.92f, 0.48f, 0.95f)
            : fillRatio > 0.25f
                ? new Color(1f, 0.76f, 0.18f, 0.95f)
                : new Color(1f, 0.24f, 0.18f, 0.95f);
    }

    private static void ApplyDevourBadge(Label badge, CardModel card)
    {
        var count = CyberDevourState.GetDevouredMonsterCount(card);
        badge.Visible = count > 0;
        if (!badge.Visible)
            return;

        var loc = new LocString("static_hover_tips", "THERMALVORTEX_DEVOUR_STATUS.badge");
        loc.Add("Count", count > 99 ? "99+" : count.ToString());
        badge.Text = loc.GetFormattedText();
    }

    private static void AnimateDevourBadge(Label badge)
    {
        badge.Scale = Vector2.One;
        var tween = badge.CreateTween();
        tween.TweenProperty(badge, "scale", new Vector2(1.32f, 1.32f), 0.12d)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(badge, "scale", Vector2.One, 0.22d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
    }

    private bool CardsChanged(IReadOnlyList<CardModel> cards)
    {
        if (cards.Count != _displayedCards.Count)
            return true;

        for (var i = 0; i < cards.Count; i++)
        {
            if (!ReferenceEquals(cards[i], _displayedCards[i]))
                return true;
        }

        return false;
    }

    private static bool ApproximatelyEqual(Vector2 left, Vector2 right) =>
        Math.Abs(left.X - right.X) < 0.5f
        && Math.Abs(left.Y - right.Y) < 0.5f;

    private bool HasViewportChanged(Vector2 viewportSize) =>
        Math.Abs(viewportSize.X - _lastViewportSize.X) > 1f
        || Math.Abs(viewportSize.Y - _lastViewportSize.Y) > 1f;

    private void ShowHover(Control owner, int index)
    {
        HideHover();
        if (!ShouldAllowSlotInspection()
            || index < 0
            || index >= _displayedCards.Count)
        {
            return;
        }

        CardInspectionHoverTips.RegisterTrigger(_root, owner);
        _hoverTips = NHoverTipSet.CreateAndShow(_root, new CardHoverTip(_displayedCards[index]), HoverTipAlignment.Center);
        _hoverTips.SetExtraFollowOffset(new Vector2(0f, _currentRootSize.Y + HoverBelowGap));
    }

    private void HideHover()
    {
        if (_hoverTips is null)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(_hoverTips))
        {
            NHoverTipSet.Remove(_root);
        }

        _hoverTips = null;
    }

    private void HandleSlotInput(int index, InputEvent input)
    {
        if (!ShouldAllowSlotInteraction())
        {
            CancelSlotInteraction();
            return;
        }

        if (input is not InputEventMouseButton button || button.ButtonIndex != MouseButton.Left)
            return;

        if (button.Pressed)
        {
            StartSlotPress(index);
            return;
        }

        FinishSlotPress(button.GlobalPosition);
    }

    private void StartSlotPress(int index)
    {
        _pressedCard = null;
        _pressedIndex = -1;
        _dragCard = null;
        _dragIndex = -1;

        if (index < 0 || index >= _displayedCards.Count)
            return;

        var card = _displayedCards[index];
        if (card is null)
            return;

        _pressedCard = card;
        _pressedIndex = index;
        if (!MonsterFieldService.IsManualRepositionUnlocked(_player))
            return;

        _dragIndex = index;
        _dragCard = card;
        HideHover();
        if (index >= 0 && index < _slots.Count)
            _slots[index].Modulate = new Color(1f, 1f, 1f, 0.62f);
    }

    private void FinishSlotPress(Vector2 globalPosition)
    {
        var pressedCard = _pressedCard;
        var pressedIndex = _pressedIndex;
        var wasDragEligible = ReferenceEquals(_dragCard, pressedCard);
        var targetIndex = FindSlotIndexAt(globalPosition);
        ResetDragVisual();
        _dragCard = null;
        _dragIndex = -1;
        _pressedCard = null;
        _pressedIndex = -1;

        if (pressedCard is null
            || pressedIndex < 0
            || pressedIndex >= _displayedCards.Count
            || !ReferenceEquals(_displayedCards[pressedIndex], pressedCard))
        {
            RequestRefresh(false);
            return;
        }

        if (targetIndex == pressedIndex)
        {
            OpenInspection(pressedCard, pressedIndex);
            return;
        }

        if (!wasDragEligible || !MonsterFieldService.IsManualRepositionUnlocked(_player))
        {
            RequestRefresh(false);
            return;
        }

        if (targetIndex < 0 || targetIndex >= _displayedCards.Count)
        {
            RequestRefresh(false);
            return;
        }

        if (MonsterFieldService.SwapMonsters(_player, pressedIndex, targetIndex))
            RequestRefresh(false);
    }

    private CardInspectionRequest CreateInspectionRequest(int selectedIndex, Control returnFocus)
    {
        if (!ShouldAllowSlotInspection()
            || selectedIndex < 0
            || selectedIndex >= _displayedCards.Count
            || _displayedCards[selectedIndex] is null)
        {
            return null;
        }

        return new CardInspectionRequest(
            _root,
            _displayedCards.Select(card => new CardInspectionEntry(card)).ToArray(),
            selectedIndex,
            returnFocus);
    }

    private void OpenInspection(CardModel selectedCard, int selectedIndex)
    {
        if (selectedCard is null
            || selectedIndex < 0
            || selectedIndex >= _displayedCards.Count
            || !ReferenceEquals(_displayedCards[selectedIndex], selectedCard))
        {
            return;
        }

        var request = CreateInspectionRequest(
            selectedIndex,
            selectedIndex < _slots.Count ? _slots[selectedIndex] : null);
        if (request is not null && CardInspectionService.TrySchedule(request))
        {
            HideHover();
            MonsterFieldUi.RefreshVisibilityForAll("card_inspect_open");
        }
    }

    private int FindSlotIndexAt(Vector2 globalPosition)
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].GetGlobalRect().HasPoint(globalPosition))
                return i;
        }

        return -1;
    }

    private void ResetDragVisual()
    {
        if (_dragIndex >= 0 && _dragIndex < _slots.Count)
            _slots[_dragIndex].Modulate = Colors.White;
    }

    private void CancelSlotInteraction()
    {
        ResetDragVisual();
        _pressedCard = null;
        _pressedIndex = -1;
        _dragCard = null;
        _dragIndex = -1;
    }

    private readonly record struct MonsterFieldEffectSnapshot(Rect2 Rect, int SlotOrdinal);

    private readonly record struct FieldLayout(Vector2 SlotSize, Vector2 RootSize, int Columns, float Gap);
}
