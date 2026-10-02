using Godot;
using MegaCrit.Sts2.addons.mega_text;
using ThermalVortex.ThermalVortexCode.RewardPools;
using MouseFilterEnum = Godot.Control.MouseFilterEnum;
using FocusModeEnum = Godot.Control.FocusModeEnum;
using SizeFlags = Godot.Control.SizeFlags;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal sealed partial class RewardPoolBuilderSession
{
    private Control _managerStage;
    private Control _managerGridArea;
    private Label _managerTitle;
    private HBoxContainer _managerCountRow;
    private Button _managerGroupButton;
    private Button _managerPickupButton;
    private Button _managerDeleteButton;
    private Button _managerCancelButton;
    private Button _managerConfirmButton;
    private Button _managerOnlineButton;
    private readonly Dictionary<Guid, Label> _managerChecks = [];
    private readonly Dictionary<Button, Control> _managerPickups = [];
    private readonly HashSet<Guid> _managerMarkedPresets = [];
    private RewardPoolManagerGroups _managerGroups;
    private string _managerGroup = string.Empty;
    private string _managerTargetGroup = string.Empty;
    private ManagerBatchOperation _managerBatch;
    private bool _managerPickupShowing;
    private PanelContainer _builderChromePanel;
    private ColorRect _builderBackdrop;
    private ColorRect _builderGlow;
    private string _managerPendingCreationGroup;
    private HashSet<Guid> _managerExistingBeforeCreation;

    private enum ManagerBatchOperation { None, Delete, Move, Copy }

    private static string ManagerText(string chinese, string english) =>
        Localize("managerTitle").Any(character => character is >= '\u4e00' and <= '\u9fff')
            || System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? chinese : english;

    private void SetBuilderChrome(ViewMode mode)
    {
        if (!IsValid(_builderChromePanel) || !IsValid(_viewHost)) return;
        var manager = mode == ViewMode.Library;
        _builderChromePanel.AnchorLeft = manager ? 0f : 0.018f;
        _builderChromePanel.AnchorTop = manager ? 0f : 0.03f;
        _builderChromePanel.AnchorRight = manager ? 1f : 0.982f;
        _builderChromePanel.AnchorBottom = manager ? 1f : 0.97f;
        _builderChromePanel.AddThemeStyleboxOverride("panel", manager
            ? new StyleBoxFlat { BgColor = Colors.Transparent } : CreateOuterStyle());
        _viewHost.AddThemeConstantOverride("margin_left", manager ? 0 : 22);
        _viewHost.AddThemeConstantOverride("margin_top", manager ? 0 : 18);
        _viewHost.AddThemeConstantOverride("margin_right", manager ? 0 : 22);
        _viewHost.AddThemeConstantOverride("margin_bottom", manager ? 0 : 18);
        if (IsValid(_builderBackdrop)) _builderBackdrop.Visible = !manager;
        if (IsValid(_builderGlow)) _builderGlow.Visible = !manager;
    }

    private void BuildManagerLibraryView(Guid? preferredPresetId)
    {
        var read = RewardPoolPresetService.GetReadState();
        if (read.LoadFailure.HasValue) { ShowLibraryUnavailable(read); return; }
        TryCleanup("organize created preset", AssignManagerCreatedPresetGroup);
        _managerGroups ??= RewardPoolManagerGroups.LoadDefault();
        if (_managerGroup.Length > 0 && !_managerGroups.GroupNames.Contains(_managerGroup))
            _managerGroup = string.Empty;
        _editingDraft = null;
        _editorBaseline = null;
        _selectedMainIds.Clear();
        _selectedExtraIds.Clear();
        _managerBatch = ManagerBatchOperation.None;
        _managerMarkedPresets.Clear();
        _managerChecks.Clear();
        _managerPickups.Clear();
        _presetTiles.Clear();

        var root = new Control { Name = "ThermalVortexMdproManager", ClipContents = true };
        root.AddChild(CreateManagerBackground());
        _managerStage = new Control { Name = "ManagerDesignCanvas", MouseFilter = MouseFilterEnum.Ignore };
        root.AddChild(_managerStage);
        _managerStage.AddChild(CreateManagerFooterShape());
        var separator = new ColorRect
        {
            Name = "ManagerHeaderSeparator", Position = new Vector2(0f, 120f),
            Size = new Vector2(1920f, 1.5f), Color = new Color(0.56f, 0.60f, 0.59f, 0.8f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _managerStage.AddChild(separator);
        _managerCloseButton = CreateManagerBackButton();
        _managerCloseButton.TooltipText = Localize("builderBack");
        _managerCloseButton.Position = new Vector2(30f, 30f);
        _managerCloseButton.Size = new Vector2(60f, 60f);
        BindLibraryAction(_managerCloseButton, Complete);
        _managerStage.AddChild(_managerCloseButton);
        _managerTitle = CreateLabel(ManagerText("编辑卡组", "Edit Deck"), 36, HorizontalAlignment.Left);
        _managerTitle.Name = "ManagerTitle";
        _managerTitle.Position = new Vector2(120f, 30f);
        _managerTitle.Size = new Vector2(500f, 60f);
        _managerTitle.VerticalAlignment = VerticalAlignment.Center;
        _managerStage.AddChild(_managerTitle);
        _managerCountRow = new HBoxContainer { Name = "ManagerDeckCount" };
        _managerCountRow.AddThemeConstantOverride("separation", 16);
        var countIcon = CreateManagerDeckGlyph(32f);
        countIcon.CustomMinimumSize = new Vector2(32f, 60f);
        _managerCountRow.AddChild(countIcon);
        _managerPresetCount = CreateLabel("0", 48, HorizontalAlignment.Left);
        _managerPresetCount.VerticalAlignment = VerticalAlignment.Center;
        _managerCountRow.AddChild(_managerPresetCount);
        _managerStage.AddChild(_managerCountRow);
        _managerGroupButton = CreateManagerButton(string.Empty, new Vector2(280f, 60f));
        _managerGroupButton.Name = "ManagerGroup";
        BindLibraryAction(_managerGroupButton, ShowManagerGroups);
        _managerStage.AddChild(_managerGroupButton);
        _managerPickupButton = CreateManagerButton(string.Empty, new Vector2(164f, 60f));
        _managerPickupButton.Name = "ManagerPickup";
        _managerPickupButton.TooltipText = ManagerText("显示或隐藏代表卡", "Show or hide representative cards");
        var pickupIcon = CreateManagerDeckGlyph(48f);
        pickupIcon.Position = new Vector2(58f, 5f);
        _managerPickupButton.AddChild(pickupIcon);
        BindLibraryAction(_managerPickupButton, () =>
        {
            _managerPickupShowing = !_managerPickupShowing;
            foreach (var pair in _managerPickups)
                if (IsValid(pair.Value)) pair.Value.Visible = _managerPickupShowing;
        });
        _managerStage.AddChild(_managerPickupButton);
        _managerDeleteButton = CreateManagerButton(string.Empty, new Vector2(164f, 60f), cut: true);
        _managerDeleteButton.Name = "ManagerDelete";
        _managerDeleteButton.TooltipText = ManagerText("批量删除", "Delete decks");
        _managerDeleteButton.AddChild(CreateManagerDeleteGlyph());
        BindLibraryAction(_managerDeleteButton, () => BeginManagerBatch(ManagerBatchOperation.Delete));
        _managerStage.AddChild(_managerDeleteButton);
        _managerCancelButton = CreateManagerButton(Localize("managerCancel"), new Vector2(280f, 60f));
        _managerCancelButton.Name = "ManagerCancelBatch";
        BindLibraryAction(_managerCancelButton, CancelManagerBatch);
        _managerStage.AddChild(_managerCancelButton);
        _presetScroll = new ScrollContainer
        {
            Name = "ManagerDeckScroll", FollowFocus = true, FocusMode = FocusModeEnum.None,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        _managerStage.AddChild(_presetScroll);
        _managerGridArea = new Control { Name = "ManagerScrollContent", MouseFilter = MouseFilterEnum.Ignore };
        _presetScroll.AddChild(_managerGridArea);
        _presetGrid = new GridContainer { Name = "ManagerDeckGrid", Columns = 6 };
        _presetGrid.AddThemeConstantOverride("h_separation", 24);
        _presetGrid.AddThemeConstantOverride("v_separation", 24);
        _managerGridArea.AddChild(_presetGrid);
        BindManagerSelectionVisibility(_presetScroll, _presetGrid);
        _managerEmptyState = CreateLabel(string.Empty, 22, HorizontalAlignment.Center);
        _managerEmptyState.Name = "ManagerEmptySearch";
        _managerStage.AddChild(_managerEmptyState);
        _managerOnlineButton = CreateManagerButton(ManagerText("在线卡组", "Online Decks"), new Vector2(482f, 60f), cut: true);
        _managerOnlineButton.Name = "ManagerOnlineUnavailable";
        _managerOnlineButton.Disabled = true;
        _managerOnlineButton.TooltipText = ManagerText("模组尚未提供在线卡组服务。", "Online decks are not available in this mod.");
        _managerOnlineButton.AddThemeColorOverride("font_disabled_color", new Color(0.43f, 0.65f, 0.12f));
        _managerStage.AddChild(_managerOnlineButton);
        _managerConfirmButton = CreateManagerButton(string.Empty, new Vector2(482f, 60f), cut: true);
        _managerConfirmButton.Name = "ManagerConfirmBatch";
        BindLibraryAction(_managerConfirmButton, ConfirmManagerBatch);
        _managerStage.AddChild(_managerConfirmButton);
        _librarySearch = CreateLineEdit(ManagerText("搜索卡组", "Search decks"));
        _librarySearch.Name = "ManagerSearch";
        _librarySearch.CustomMinimumSize = new Vector2(482f, 60f);
        _librarySearch.AddThemeFontSizeOverride("font_size", 28);
        var searchStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.16f, 0.17f, 0.18f, 0.96f), BorderColor = ManagerVisualBorder,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 56f, ContentMarginRight = 14f, ContentMarginTop = 4f, ContentMarginBottom = 4f
        };
        _librarySearch.AddThemeStyleboxOverride("normal", searchStyle);
        _librarySearch.AddThemeStyleboxOverride("focus", ManagerVisualFocusStyle(false));
        _librarySearch.AddChild(CreateManagerSearchGlyph());
        _librarySearch.TextChanged += _ => ScheduleSearchRefresh(() => RefreshLibrary(_rememberedPresetId));
        _librarySearch.TextSubmitted += _ => RefreshLibrary(_rememberedPresetId);
        _managerStage.AddChild(_librarySearch);
        _libraryStatus = CreateLabel(string.Empty, 16, HorizontalAlignment.Left);
        _libraryStatus.Name = "ManagerStatus";
        _libraryStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _managerStage.AddChild(_libraryStatus);
        ReplaceView(root, ViewMode.Library);
        RefreshManagerLibrary(preferredPresetId);
        if (_managerGroups.LoadError.Length > 0) _libraryStatus.Text = _managerGroups.LoadError;
        DeferFocus(GetSelectedPresetTileButton() ?? _createPresetTile);
    }

    private void RefreshManagerLibrary(Guid? preferredPresetId)
    {
        if (_viewMode != ViewMode.Library || !IsValid(_presetGrid)) return;
        var read = RewardPoolPresetService.GetReadState();
        if (read.LoadFailure.HasValue) { ShowLibraryUnavailable(read); return; }
        var query = _librarySearch?.Text?.Trim() ?? string.Empty;
        var presets = read.Library.Presets
            .Where(preset => string.Equals(_managerGroups.GroupOf(preset.Id), _managerGroup, StringComparison.Ordinal))
            .Where(preset => query.Length == 0 || preset.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(preset => preset.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(preset => preset.Id).ToArray();
        ClearPresetTiles();
        _managerChecks.Clear();
        _managerPickups.Clear();
        AddManagerCreateTile();
        foreach (var preset in presets)
            AddManagerSavedTile(preset, RewardPoolPresetService.Validate(preset),
                read.Library.ActivePresetId == preset.Id && !RewardPoolDraftService.IsSelected);
        var draft = RewardPoolDraftService.GetState();
        var draftVisible = _managerGroup.Length == 0 && draft is not null
            && (draft.IsLegacy || draft.Stage != RewardPoolDraftStage.Consumed)
            && (query.Length == 0 || ConstructionMethodName(RewardPoolConstructionMethod.Draft)
                .Contains(query, StringComparison.OrdinalIgnoreCase));
        if (draftVisible) AddManagerDraftTile(draft);
        _managerPresetCount.Text = (presets.Length + (draftVisible ? 1 : 0)).ToString();
        _managerGroupButton.Text = _managerGroup.Length == 0 ? ManagerText("默认分组", "Default Group") : _managerGroup;
        _managerEmptyState.Visible = query.Length > 0 && presets.Length == 0 && !draftVisible;
        _managerEmptyState.Text = Localize("managerNoResults");
        if (preferredPresetId.HasValue && presets.Any(preset => preset.Id == preferredPresetId))
            _selectedPresetId = preferredPresetId;
        else if (_selectedPresetId.HasValue && !presets.Any(preset => preset.Id == _selectedPresetId))
            _selectedPresetId = null;
        _standardSelected = !_selectedPresetId.HasValue;
        _presetScroll.ScrollVertical = 0;
        UpdateManagerBatchControls();
        ApplyManagerTileStyles();
        LayoutManagerLibrary();
        ConfigureManagerFocus();
    }

    private void AddManagerCreateTile()
    {
        var button = CreateManagerTile(Localize("managerTileNewHint"));
        button.Name = "ManagerNewDeck";
        _createPresetTile = button;
        var glyph = CreateManagerAddGlyph();
        glyph.Position = new Vector2(80f, 66f);
        button.AddChild(glyph);
        BindLibraryAction(button, ShowManagerCreationModes);
        _presetGrid.AddChild(button);
        _presetTiles.Add(new PresetTileBinding(LibraryTileKind.Create, null, button, false));
    }

    private void AddManagerSavedTile(RewardPoolPresetDraft preset, RewardPoolPresetValidation validation, bool active)
    {
        var button = CreateManagerTile(BuildPresetTooltip(preset, validation));
        button.Name = "ManagerDeck_" + preset.Id.ToString("N");
        var box = CreateDeckBoxDisplay(validation.IsValid ? StatusTone.Success : StatusTone.Warning, false);
        box.Position = new Vector2(22f, 25f);
        box.Size = new Vector2(216f, 155f);
        box.CustomMinimumSize = Vector2.Zero;
        button.AddChild(box);
        var cover = CreateManagerPresetCover(preset, StatusTone.Normal);
        cover.Name = "ManagerRepresentativeCards";
        cover.CustomMinimumSize = Vector2.Zero;
        cover.Position = new Vector2(8f, 8f);
        cover.Size = new Vector2(244f, 170f);
        cover.Visible = _managerPickupShowing;
        button.AddChild(cover);
        _managerPickups[button] = cover;
        button.MouseEntered += () => { if (CanUseLibraryAction(button)) cover.Visible = true; };
        button.MouseExited += () => { if (IsValid(cover)) cover.Visible = _managerPickupShowing; };
        var name = CreateLabel(preset.Name, 25, HorizontalAlignment.Center);
        name.Position = new Vector2(10f, 188f);
        name.Size = new Vector2(240f, 30f);
        name.ClipText = true;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.MouseFilter = MouseFilterEnum.Ignore;
        button.AddChild(name);
        var check = CreateLabel("✓", 27, HorizontalAlignment.Center);
        check.Position = new Vector2(216f, 10f);
        check.Size = new Vector2(30f, 30f);
        check.Modulate = ManagerVisualGreen;
        check.MouseFilter = MouseFilterEnum.Ignore;
        check.Visible = _managerBatch != ManagerBatchOperation.None && _managerMarkedPresets.Contains(preset.Id);
        button.AddChild(check);
        _managerChecks[preset.Id] = check;
        BindLibraryAction(button, () =>
        {
            _selectedPresetId = preset.Id;
            _rememberedPresetId = preset.Id;
            _standardSelected = false;
            if (_managerBatch != ManagerBatchOperation.None)
            {
                if (!_managerMarkedPresets.Add(preset.Id)) _managerMarkedPresets.Remove(preset.Id);
                UpdateManagerBatchControls();
                ApplyManagerTileStyles();
            }
            else if (RewardPoolPresetService.TryGet(preset.Id, out var current)) ShowEditorView(current);
        });
        button.GuiInput += input =>
        {
            if (CanUseLibraryAction(button) && _managerBatch == ManagerBatchOperation.None
                && input is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
            {
                ShowManagerDeckActions(preset.Id);
                _viewport?.SetInputAsHandled();
            }
        };
        _presetGrid.AddChild(button);
        _presetTiles.Add(new PresetTileBinding(LibraryTileKind.Preset, preset.Id, button, active));
    }

    private void AddManagerDraftTile(RewardPoolDraftState state)
    {
        var button = CreateManagerTile(Localize("constructionContinueDraft"));
        button.Name = "ManagerDraftSession";
        var box = CreateDeckBoxDisplay(StatusTone.Normal, false);
        box.CustomMinimumSize = Vector2.Zero;
        box.Position = new Vector2(22f, 25f);
        box.Size = new Vector2(216f, 155f);
        button.AddChild(box);
        var label = CreateLabel(ConstructionMethodName(RewardPoolConstructionMethod.Draft), 25, HorizontalAlignment.Center);
        label.Position = new Vector2(10f, 188f);
        label.Size = new Vector2(240f, 30f);
        label.MouseFilter = MouseFilterEnum.Ignore;
        button.AddChild(label);
        button.SetMeta("sessionId", state.SessionId);
        BindLibraryAction(button, OpenDraft);
        _presetGrid.AddChild(button);
        _presetTiles.Add(new PresetTileBinding(LibraryTileKind.Draft, null, button, RewardPoolDraftService.IsSelected));
    }

    private Button CreateManagerTile(string tooltip)
    {
        var button = CreateManagerButton(string.Empty, new Vector2(260f, 232f), cut: true);
        button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        button.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        button.TooltipText = tooltip;
        button.AddThemeStyleboxOverride("normal", CreateManagerTileStyle(false, false));
        button.AddThemeStyleboxOverride("hover", CreateManagerTileStyle(true, false));
        button.AddThemeStyleboxOverride("pressed", CreateManagerTileStyle(true, true));
        button.AddThemeStyleboxOverride("focus", ManagerVisualFocusStyle(true));
        return button;
    }

    private void ApplyManagerTileStyles()
    {
        foreach (var tile in _presetTiles)
        {
            if (!IsValid(tile.Button)) continue;
            var marked = tile.PresetId.HasValue && _managerMarkedPresets.Contains(tile.PresetId.Value);
            tile.Button.AddThemeStyleboxOverride("normal", CreateManagerTileStyle(false,
                _managerBatch != ManagerBatchOperation.None ? marked : tile.IsActive));
            if (tile.PresetId.HasValue && _managerChecks.TryGetValue(tile.PresetId.Value, out var check) && IsValid(check))
                check.Visible = _managerBatch != ManagerBatchOperation.None && marked;
            tile.Button.Disabled = _managerBatch != ManagerBatchOperation.None && tile.Kind != LibraryTileKind.Preset;
        }
    }

    private void LayoutManagerLibrary()
    {
        if (_viewMode != ViewMode.Library || !IsValid(_managerStage) || !IsValid(_activeViewRoot)) return;
        var physical = _activeViewRoot.Size;
        if (physical.X <= 1f || physical.Y <= 1f) physical = Root.Size;
        if (physical.X <= 1f || physical.Y <= 1f) return;
        var scale = physical.Y / 1080f;
        var width = physical.X / scale;
        _managerStage.Scale = Vector2.One * scale;
        _managerStage.Size = new Vector2(width, 1080f);
        var line = _managerStage.GetNode<Control>("ManagerHeaderSeparator");
        line.Size = new Vector2(width, 1.5f);
        var right = width - 150f;
        _managerDeleteButton.Position = new Vector2(right - 164f, 30f);
        _managerDeleteButton.Size = new Vector2(164f, 60f);
        _managerPickupButton.Position = new Vector2(right - 368f, 30f);
        _managerPickupButton.Size = new Vector2(164f, 60f);
        _managerGroupButton.Position = new Vector2(right - 688f, 30f);
        _managerGroupButton.Size = new Vector2(280f, 60f);
        _managerCountRow.Position = new Vector2(right - 826f, 30f);
        _managerCountRow.Size = new Vector2(98f, 60f);
        _managerCancelButton.Position = new Vector2(right - 280f, 30f);
        _managerCancelButton.Size = new Vector2(280f, 60f);
        _managerTitle.Size = new Vector2(Math.Max(100f, right - 946f), 60f);
        _presetScroll.Position = new Vector2(120f, 120f);
        _presetScroll.Size = new Vector2(Math.Max(260f, width - 205f), 960f);
        var columns = Math.Max(1, (int)Math.Floor(_presetScroll.Size.X / 284f));
        var rows = Math.Max(1, (int)Math.Ceiling(_presetTiles.Count / (double)columns));
        var contentHeight = rows * 256f + 136f;
        _managerGridArea.CustomMinimumSize = new Vector2(_presetScroll.Size.X, contentHeight);
        _managerGridArea.Size = new Vector2(_presetScroll.Size.X, contentHeight);
        _presetGrid.Columns = columns;
        var gridWidth = columns * 284f - 24f;
        _presetGrid.Position = new Vector2((_presetScroll.Size.X - gridWidth) * 0.5f, 10f);
        _presetGrid.Size = new Vector2(gridWidth, rows * 256f - 24f);
        _managerOnlineButton.Position = new Vector2(width - 1081f, 980f);
        _managerOnlineButton.Size = new Vector2(482f, 60f);
        _managerConfirmButton.Position = _managerOnlineButton.Position;
        _managerConfirmButton.Size = _managerOnlineButton.Size;
        _librarySearch.Position = new Vector2(width - 567f, 980f);
        _librarySearch.Size = new Vector2(482f, 60f);
        _managerEmptyState.Position = new Vector2(120f, 380f);
        _managerEmptyState.Size = new Vector2(Math.Max(260f, width - 205f), 40f);
        _libraryStatus.Position = new Vector2(30f, 995f);
        _libraryStatus.Size = new Vector2(Math.Max(100f, width * 0.34f - 50f), 50f);
        ConfigureManagerFocus();
    }

    private void ConfigureManagerFocus()
    {
        if (_viewMode != ViewMode.Library || !IsValid(_presetGrid)) return;
        var tiles = _presetTiles.Where(tile => IsValid(tile.Button) && !tile.Button.Disabled).Select(tile => tile.Button).ToArray();
        var columns = Math.Max(1, _presetGrid.Columns);
        var top = _managerBatch == ManagerBatchOperation.None ? _managerGroupButton : _managerCancelButton;
        Control bottom = _managerBatch == ManagerBatchOperation.None ? _librarySearch : _managerConfirmButton;
        for (var index = 0; index < tiles.Length; index++)
            SetFocusNeighbors(tiles[index], index % columns > 0 ? tiles[index - 1] : tiles[index],
                index % columns < columns - 1 && index + 1 < tiles.Length ? tiles[index + 1] : tiles[index],
                index >= columns ? tiles[index - columns] : top,
                index + columns < tiles.Length ? tiles[index + columns] : bottom,
                index > 0 ? tiles[index - 1] : _managerCloseButton,
                index + 1 < tiles.Length ? tiles[index + 1] : bottom);
        if (tiles.Length > 0)
        {
            SetFocusNeighbors(_managerCloseButton, _managerCloseButton, top, _managerCloseButton, tiles[0], bottom, top);
            SetFocusNeighbors(top, _managerCloseButton, _managerPickupButton, top, tiles[0], _managerCloseButton, _managerPickupButton);
            SetFocusNeighbors(bottom, bottom, bottom, tiles[^1], bottom, tiles[^1], _managerCloseButton);
        }
    }

    private static Control CreateManagerDeckGlyph(float width)
    {
        var icon = new Control { Size = new Vector2(width, 50f), MouseFilter = MouseFilterEnum.Ignore };
        icon.AddChild(new ColorRect { Color = Colors.White, Position = new Vector2(0f, 7f), Size = new Vector2(width, 15f), MouseFilter = MouseFilterEnum.Ignore });
        icon.AddChild(new ColorRect { Color = Colors.White, Position = new Vector2(0f, 25f), Size = new Vector2(width, 23f), MouseFilter = MouseFilterEnum.Ignore });
        icon.AddChild(new ColorRect { Color = Colors.Black, Position = new Vector2(width * 0.5f - 2f, 16f), Size = new Vector2(4f, 4f), MouseFilter = MouseFilterEnum.Ignore });
        return icon;
    }

    private static Control CreateManagerDeleteGlyph()
    {
        var host = new Control { Position = new Vector2(48f, 6f), Size = new Vector2(68f, 48f), MouseFilter = MouseFilterEnum.Ignore };
        host.AddChild(new ColorRect { Color = Colors.White, Position = new Vector2(5f, 12f), Size = new Vector2(40f, 34f), MouseFilter = MouseFilterEnum.Ignore });
        host.AddChild(new ColorRect { Color = Colors.White, Position = new Vector2(0f, 6f), Size = new Vector2(50f, 5f), MouseFilter = MouseFilterEnum.Ignore });
        host.AddChild(new ColorRect { Color = Colors.White, Position = new Vector2(17f, 0f), Size = new Vector2(16f, 5f), MouseFilter = MouseFilterEnum.Ignore });
        for (var index = 0; index < 3; index++)
            host.AddChild(new ColorRect { Color = Colors.Black, Position = new Vector2(12f + index * 10f, 17f), Size = new Vector2(4f, 25f), MouseFilter = MouseFilterEnum.Ignore });
        var check = CreateLabel("✓", 22, HorizontalAlignment.Center);
        check.Position = new Vector2(38f, 22f);
        check.Size = new Vector2(30f, 28f);
        check.MouseFilter = MouseFilterEnum.Ignore;
        host.AddChild(check);
        return host;
    }

    private static Control CreateManagerSearchGlyph()
    {
        var host = new Control { Position = new Vector2(16f, 14f), Size = new Vector2(36f, 36f), MouseFilter = MouseFilterEnum.Ignore };
        var ring = new Vector2[33];
        for (var index = 0; index < ring.Length; index++)
            ring[index] = new Vector2(13f, 13f) + new Vector2(Mathf.Cos(Mathf.Tau * index / 32f), Mathf.Sin(Mathf.Tau * index / 32f)) * 12f;
        host.AddChild(ManagerVisualLine("SearchCircle", ring, new Color(0.75f, 0.76f, 0.76f), 3f));
        host.AddChild(ManagerVisualLine("SearchHandle", [new Vector2(22f, 22f), new Vector2(33f, 33f)], new Color(0.75f, 0.76f, 0.76f), 4f));
        return host;
    }
}
