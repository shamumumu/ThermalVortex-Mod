using System.Globalization;
using System.Text;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using ThermalVortex.ThermalVortexCode.CardInspection;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.RewardPools;
using FocusModeEnum = Godot.Control.FocusModeEnum;
using GodotTimer = Godot.Timer;
using MouseFilterEnum = Godot.Control.MouseFilterEnum;
using SizeFlags = Godot.Control.SizeFlags;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal sealed record RewardPoolBuilderResult(
    bool LibraryChanged,
    bool SelectionChanged);

/// <summary>
/// Character-select-safe reward-pool preset manager and editor. This remains an
/// ordinary CLR object: only game-provided and built-in Godot nodes enter the tree,
/// avoiding a mod-defined Godot method bridge on the character-select screen.
/// </summary>
internal sealed partial class RewardPoolBuilderSession : IDisposable
{
    private const string LocalizationPrefix = "THERMALVORTEX-THERMAL_VORTEX.rewardPool.";
    private const int MainSelectionCount = RewardPoolSizePolicy.MaxSelectableMain;
    private const int ExtraSelectionCount = RewardPoolCatalog.SelectableExtraCardCount;
    private const float PresetTileMinimumWidth = 260f;
    private const float PresetTileHeight = 232f;
    private const int PresetGridMaximumColumns = 6;
    private const int SelectedGridMaximumColumns = 4;
    private const int PoolGridMaximumColumns = 6;
    private const int EditorCardColumnWidth = 118;
    private const int EditorCardColumnSeparation = 4;
    private const float EditorListChromeWidth = 24f;

    private static readonly Color Gold = new(0.95f, 0.72f, 0.24f, 1f);
    private static readonly Color PaleGold = new(1f, 0.90f, 0.58f, 1f);
    private static readonly Color Cyan = new(0.20f, 0.82f, 0.96f, 1f);
    private static readonly Color Silver = new(0.55f, 0.64f, 0.76f, 1f);
    private static readonly Color SelectedBackground = new(0.055f, 0.11f, 0.20f, 0.99f);
    private static readonly Color ActiveBackground = new(0.11f, 0.10f, 0.055f, 0.99f);
    private static readonly Color NormalBackground = new(0.018f, 0.035f, 0.070f, 0.90f);
    private static readonly Color GoodColor = new(0.62f, 1f, 0.70f, 1f);
    private static readonly Color WarningColor = new(1f, 0.80f, 0.38f, 1f);
    private static readonly Color BadColor = new(1f, 0.55f, 0.55f, 1f);
    private static readonly Color MutedColor = new(0.75f, 0.75f, 0.84f, 1f);

    private readonly TaskCompletionSource<RewardPoolBuilderResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<string> _portraitFailures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _descriptionFailures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _previewFailures = new(StringComparer.Ordinal);
    private readonly CardEntry[] _mainEntries;
    private readonly CardEntry[] _extraEntries;
    private readonly Dictionary<string, CardEntry> _mainById;
    private readonly Dictionary<string, CardEntry> _extraById;

    private NCharacterSelectScreen _host;
    private NHotkeyManager _hotkeyManager;
    private Viewport _viewport;
    private GodotTimer _searchRefreshTimer;
    private Action _pendingSearchRefresh;
    private MarginContainer _viewHost;
    private Control _activeViewRoot;
    private Control _modalRoot;
    private PanelContainer _modalPanel;
    private Label _modalMessage;
    private GridContainer _modalActions;
    private Control _focusBeforeModal;
    private bool _focusRedirectScheduled;

    // Library view state.
    private NMegaLineEdit _librarySearch;
    private ScrollContainer _presetScroll;
    private GridContainer _presetGrid;
    private readonly List<PresetTileBinding> _presetTiles = [];
    private Texture2D _deckBoxTexture;
    private Button _createPresetTile;
    private Button _standardPresetTile;
    private Label _managerPresetCount;
    private Label _managerEmptyState;
    private Label _libraryStatus;
    private Label _presetName;
    private Label _presetBadge;
    private Label _presetCounts;
    private Label _presetRarities;
    private Label _presetIssues;
    private Label _presetUpdated;
    private Button _managerCloseButton;
    private Button _unavailableStandardButton;
    private bool _standardSelected;
    private Guid? _selectedPresetId;
    private Guid? _rememberedPresetId;

    // Editor view state.
    private RewardPoolPresetDraft _editingDraft;
    private EditorSnapshot _editorBaseline;
    private readonly List<string> _selectedMainIds = [];
    private readonly List<string> _selectedExtraIds = [];
    private IReadOnlyList<CardEntry> _displayedPoolCards = [];
    private readonly List<SelectedCardEntry> _displayedSelectedCards = [];
    private NMegaLineEdit _presetNameEdit;
    private Button _mainTab;
    private Button _extraTab;
    private Label _selectedTitle;
    private Label _lockedHint;
    private Label _poolTitle;
    private Label _validationLabel;
    private Label _editorToast;
    private ItemList _selectedList;
    private ItemList _poolList;
    private NMegaLineEdit _search;
    private GridContainer _poolSearchRow;
    private GridContainer _mainFilterRow;
    private OptionButton _originFilter;
    private OptionButton _rarityFilter;
    private OptionButton _typeFilter;
    private OptionButton _sortOrder;
    private Button _sortDirection;
    private Button _saveButton;
    private Button _saveAndUseButton;
    private Label _detailCardTitle;
    private CenterContainer _nativeCardPreviewHost;
    private VBoxContainer _fallbackDetail;
    private NCard _nativeCardPreview;
    private Button _nativeCardInspectButton;
    private Button _upgradePreviewButton;
    private TextureRect _detailPortrait;
    private Label _detailCost;
    private Label _detailOrigin;
    private Label _detailType;
    private Label _detailRarity;
    private Label _detailEffectTitle;
    private MegaRichTextLabel _detailDescription;
    private ProgressBar _commonProgress;
    private ProgressBar _uncommonProgress;
    private ProgressBar _rareProgress;
    private Label _commonProgressLabel;
    private Label _uncommonProgressLabel;
    private Label _rareProgressLabel;
    private GridContainer _editorHeader;
    private GridContainer _editorBottom;
    private HBoxContainer _editorSplit;
    private PanelContainer _selectedPanel;
    private PanelContainer _poolPanel;
    private PanelContainer _detailPanel;
    private GridContainer _paneToolbar;
    private Button _showSelectedPaneButton;
    private Button _showPoolPaneButton;
    private Button _showDetailPaneButton;
    private BuilderPane _compactPane = BuilderPane.Pool;
    private BuilderTab _activeTab;
    private bool _sortDescending;
    private bool _suppressSearchRefresh;
    private bool _detailUpdateScheduled;
    private bool _editorColumnUpdateScheduled;
    private string _normalizedSearchQuery = string.Empty;
    private string _currentDetailCardId = string.Empty;
    private CardEntry _pendingDetailCard;
    private CardEntry _currentDetailCard;
    private bool _showUpgradePreview;

    private ViewMode _viewMode;
    private bool _closing;
    private bool _disposed;
    private bool _hotkeyBlocked;
    private bool _libraryChanged;
    private bool _selectionChanged;

    internal Control Root { get; }
    internal Task<RewardPoolBuilderResult> Completion => _completion.Task;

    private RewardPoolBuilderSession(
        NCharacterSelectScreen host,
        IReadOnlyList<CardModel> mainCandidates,
        IReadOnlyList<CardModel> extraCandidates)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(mainCandidates);
        ArgumentNullException.ThrowIfNull(extraCandidates);

        _host = host;
        _mainEntries = BuildEntries(mainCandidates, isExtra: false);
        _extraEntries = BuildEntries(extraCandidates, isExtra: true);
        _mainById = BuildEntryIndex(_mainEntries);
        _extraById = BuildEntryIndex(_extraEntries);

        Root = new Control
        {
            Name = "ThermalVortexRewardPoolBuilderRoot",
            MouseFilter = MouseFilterEnum.Stop,
            ZIndex = 200,
            ZAsRelative = false
        };
        Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    internal static RewardPoolBuilderSession Open(
        NCharacterSelectScreen host,
        IReadOnlyList<CardModel> mainCandidates,
        IReadOnlyList<CardModel> extraCandidates)
    {
        var session = new RewardPoolBuilderSession(host, mainCandidates, extraCandidates);
        try
        {
            session.BuildChrome();
            session.ShowLibraryView();
            session.Attach();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void Attach()
    {
        if (_disposed || !IsValid(_host))
            throw new InvalidOperationException("The character-select host is unavailable.");

        _host.AddChild(Root);
        Root.TreeExiting += OnRootTreeExiting;
        Root.Resized += UpdateResponsiveLayout;
        Root.VisibilityChanged += OnBuilderVisibilityChanged;
        TryCleanup("cover DPS HUD", UpdateDpsHudOcclusion);
        _viewport = Root.GetViewport();
        if (IsValid(_viewport))
            _viewport.GuiFocusChanged += OnGuiFocusChanged;
        _hotkeyManager = NHotkeyManager.Instance;
        if (!IsValid(_hotkeyManager))
            throw new InvalidOperationException("The hotkey manager is unavailable.");

        _hotkeyManager.AddBlockingScreen(Root);
        _hotkeyBlocked = true;
        Callable.From(UpdateResponsiveLayout).CallDeferred();
        DeferFocus(GetPreferredViewFocus());
    }

    /// <summary>
    /// Handles the shared character-select cancel input. Native card inspection
    /// closes first, then a modal, then the editor or library itself.
    /// </summary>
    internal bool RequestCancel()
    {
        if (_closing || _disposed)
            return false;

        if (CardInspectionService.TryClose(Root))
            return true;

        if (IsValid(_modalRoot))
        {
            CloseModal();
            return true;
        }

        if (_viewMode == ViewMode.Editor)
        {
            RequestLeaveEditor();
            return true;
        }

        if (_viewMode == ViewMode.Draft)
        {
            ShowLibraryView();
            return true;
        }

        if (_viewMode == ViewMode.Library && _managerBatch != ManagerBatchOperation.None)
        {
            CancelManagerBatch();
            return true;
        }

        Complete();
        return true;
    }

    public void Dispose() => DisposeCore(queueRoot: true);

    private void OnRootTreeExiting() => DisposeCore(queueRoot: false);

    private void Complete()
    {
        if (_closing || _disposed)
            return;

        TryCleanup("organize created preset", AssignManagerCreatedPresetGroup);
        _closing = true;
        _completion.TrySetResult(new RewardPoolBuilderResult(
            _libraryChanged,
            _selectionChanged));
    }

    private void DisposeCore(bool queueRoot)
    {
        if (_disposed)
            return;

        _disposed = true;
        _completion.TrySetCanceled();
        TryCleanup("restore DPS HUD layer", RestoreDpsHudLayer);
        TryCleanup("close card inspection", () => CardInspectionService.TryClose(Root));
        TryCleanup("unregister card inspection", () =>
        {
            if (IsValid(_poolList))
                CardInspectionRegistry.Unregister(_poolList);
            if (IsValid(_selectedList))
                CardInspectionRegistry.Unregister(_selectedList);
            if (IsValid(_detailPortrait))
                CardInspectionRegistry.Unregister(_detailPortrait);
        });
        TryCleanup("detach root handlers", () =>
        {
            Root.TreeExiting -= OnRootTreeExiting;
            Root.Resized -= UpdateResponsiveLayout;
            Root.VisibilityChanged -= OnBuilderVisibilityChanged;
            if (IsValid(_viewport))
                _viewport.GuiFocusChanged -= OnGuiFocusChanged;
        });
        TryCleanup("hide root", () =>
        {
            if (!IsValid(Root))
                return;
            Root.Visible = false;
            Root.MouseFilter = MouseFilterEnum.Ignore;
        });
        TryCleanup("remove hotkey blocker", () =>
        {
            if (_hotkeyBlocked && IsValid(_hotkeyManager))
                _hotkeyManager.RemoveBlockingScreen(Root);
        });
        _hotkeyBlocked = false;
        TryCleanup("stop search timer", () =>
        {
            if (!IsValid(_searchRefreshTimer))
                return;
            _searchRefreshTimer.Stop();
            _searchRefreshTimer.Timeout -= ApplyPendingSearchRefresh;
        });
        TryCleanup("release construction cards", () => ReleaseConstructionCardPreviews());
        TryCleanup("release manager covers", () => ReleaseManagerPresetCovers());
        TryCleanup("detach native editor grids", () => ReleaseNativeEditorGrids());
        TryCleanup("clear detail", () =>
        {
            ReleaseNativeCardPreview();
            if (IsValid(_detailPortrait))
                _detailPortrait.Texture = null;
            if (IsValid(_detailDescription))
                _detailDescription.SetTextAutoSize(string.Empty);
        });

        _pendingSearchRefresh = null;
        _pendingDetailCard = null;
        _currentDetailCard = null;
        _modalPanel = null;
        _modalMessage = null;
        _modalActions = null;
        _displayedPoolCards = [];
        _displayedSelectedCards.Clear();
        _presetTiles.Clear();
        _portraitFailures.Clear();
        _descriptionFailures.Clear();
        _previewFailures.Clear();
        _nativeCardInspectButton = null;
        _upgradePreviewButton = null;
        _deckBoxTexture = null;
        _createPresetTile = null;
        _standardPresetTile = null;
        _managerCloseButton = null;
        _hotkeyManager = null;
        _viewport = null;
        _host = null;

        if (queueRoot)
        {
            TryCleanup("queue root", () =>
            {
                if (IsValid(Root) && !Root.IsQueuedForDeletion())
                    Root.QueueFree();
            });
        }
    }

    private static void TryCleanup(string stage, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            try
            {
                MainFile.Logger.Info(
                    $"Reward-pool cleanup failed stage={stage} reason={exception.Message}");
            }
            catch
            {
                // Cleanup must continue even if the logger is shutting down.
            }
        }
    }

    private void BuildChrome()
    {
        _searchRefreshTimer = new GodotTimer
        {
            OneShot = true,
            WaitTime = 0.10d
        };
        _searchRefreshTimer.Timeout += ApplyPendingSearchRefresh;
        Root.AddChild(_searchRefreshTimer);

        var backdrop = new ColorRect
        {
            Color = new Color(0.004f, 0.012f, 0.030f, 0.90f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _builderBackdrop = backdrop;
        Root.AddChild(backdrop);

        var glow = new ColorRect
        {
            AnchorLeft = 0.08f,
            AnchorTop = 0.08f,
            AnchorRight = 0.92f,
            AnchorBottom = 0.92f,
            Color = new Color(0.20f, 0.08f, 0.42f, 0.13f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _builderGlow = glow;
        Root.AddChild(glow);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.018f,
            AnchorTop = 0.03f,
            AnchorRight = 0.982f,
            AnchorBottom = 0.97f,
            MouseFilter = MouseFilterEnum.Stop
        };
        _builderChromePanel = panel;
        panel.AddThemeStyleboxOverride("panel", CreateOuterStyle());
        Root.AddChild(panel);

        _viewHost = new MarginContainer();
        _viewHost.AddThemeConstantOverride("margin_left", 22);
        _viewHost.AddThemeConstantOverride("margin_top", 18);
        _viewHost.AddThemeConstantOverride("margin_right", 22);
        _viewHost.AddThemeConstantOverride("margin_bottom", 18);
        panel.AddChild(_viewHost);
    }

    private void ReplaceView(Control next, ViewMode mode)
    {
        CancelPendingSearchRefresh();
        CloseModalCore(restoreFocus: false);
        ReleaseNativeCardPreview();
        if (IsValid(_activeViewRoot))
            ReleaseConstructionCardPreviews(_activeViewRoot);
        if (IsValid(_activeViewRoot))
            ReleaseManagerPresetCovers(_activeViewRoot);
        if (IsValid(_activeViewRoot))
            ReleaseNativeEditorGrids(_activeViewRoot);
        if (IsValid(_activeViewRoot))
        {
            _activeViewRoot.Visible = false;
            _viewHost.RemoveChild(_activeViewRoot);
            _activeViewRoot.QueueFree();
        }

        _activeViewRoot = next;
        _viewMode = mode;
        SetBuilderChrome(mode);
        _viewHost.AddChild(next);
        Callable.From(UpdateResponsiveLayout).CallDeferred();
    }

    // ---------------------------------------------------------------------
    // Preset library
    // ---------------------------------------------------------------------

    private void ShowLibraryView(Guid? preferredPresetId = null)
    {
        BuildManagerLibraryView(preferredPresetId);
    }

    private void ShowLibraryUnavailable(RewardPoolPresetReadState readState)
    {
        _editingDraft = null;
        _editorBaseline = null;
        ClearPresetTiles();
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 24);
        root.AddChild(CreateLabel(Localize("managerTitle"), 31, HorizontalAlignment.Left));
        var message = CreateLabel(Localize("managerLibraryUnavailable"), 20, HorizontalAlignment.Left);
        message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        message.Modulate = WarningColor;
        root.AddChild(message);
        if (readState.StandardSelectedForProcess)
        {
            var selected = CreateLabel(Localize("managerStandardSessionSelected"), 18, HorizontalAlignment.Left);
            selected.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            root.AddChild(selected);
        }

        _unavailableStandardButton = CreateButton(Localize("managerUseStandardForSession"), 0f);
        _unavailableStandardButton.Pressed += () =>
        {
            if (!RewardPoolPresetService.TrySelect(null, out _, out var result))
            {
                ShowOperationError(Localize("managerSelectFailed"), result);
                return;
            }
            _selectionChanged = true;
            Complete();
        };
        root.AddChild(_unavailableStandardButton);
        _managerCloseButton = CreateButton(Localize("managerClose"), 0f);
        _managerCloseButton.Pressed += Complete;
        root.AddChild(_managerCloseButton);
        ReplaceView(root, ViewMode.LibraryUnavailable);
        SetFocusNeighbors(_unavailableStandardButton,
            left: _managerCloseButton, right: _managerCloseButton,
            top: _managerCloseButton, bottom: _managerCloseButton,
            previous: _managerCloseButton, next: _managerCloseButton);
        SetFocusNeighbors(_managerCloseButton,
            left: _unavailableStandardButton, right: _unavailableStandardButton,
            top: _unavailableStandardButton, bottom: _unavailableStandardButton,
            previous: _unavailableStandardButton, next: _unavailableStandardButton);
        DeferFocus(_unavailableStandardButton);
    }

    private void RefreshLibrary(Guid? preferredPresetId)
    {
        RefreshManagerLibrary(preferredPresetId);
    }

    private void ClearPresetTiles()
    {
        ReleaseManagerPresetCovers();
        _presetTiles.Clear();
        _createPresetTile = null;
        _standardPresetTile = null;
        if (!IsValid(_presetGrid))
            return;
        foreach (var child in _presetGrid.GetChildren())
        {
            if (child is not Node node)
                continue;
            _presetGrid.RemoveChild(node);
            node.QueueFree();
        }
    }

    private void AddCreatePresetTile()
    {
        AddManagerCreateTile();
    }

    private void AddStandardPresetTile(bool active)
    {
        var button = CreatePresetTileButton(Localize("summaryStandardCounts"));
        _standardPresetTile = button;
        var content = CreatePresetTileContent(button);
        content.AddChild(CreateTileBadgeRow(
            active,
            Localize("managerTileValid"),
            StatusTone.Success));
        content.AddChild(CreateDeckBoxDisplay(StatusTone.Success, standard: true));
        content.AddChild(CreateTileNameLabel(Localize("standardName")));
        content.AddChild(CreateTileSummaryLabel(Localize("managerStandardTileCounts")));
        BindLibraryAction(button, () => SelectStandardTile(
            ensureVisible: true,
            clearRememberedPreset: true));
        button.FocusEntered += () =>
        {
            if (CanUseLibraryAction(button))
                SelectStandardTile(ensureVisible: false, clearRememberedPreset: true);
        };
        _presetGrid.AddChild(button);
        _presetTiles.Add(new PresetTileBinding(
            LibraryTileKind.Standard,
            null,
            button,
            active));
    }

    private void AddSavedPresetTile(
        RewardPoolPresetDraft preset,
        RewardPoolPresetValidation validation,
        bool active)
    {
        AddManagerSavedTile(preset, validation, active);
    }

    private Button CreatePresetTileButton(string tooltip)
    {
        var button = new Button
        {
            Text = string.Empty,
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(0f, PresetTileHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.All,
            ClipContents = true
        };
        button.AddThemeStyleboxOverride("disabled", CreatePresetTileStyle(
            new Color(0.012f, 0.022f, 0.045f, 0.78f),
            new Color(0.18f, 0.22f, 0.30f, 0.8f),
            1));
        return button;
    }

    private static VBoxContainer CreatePresetTileContent(Button button)
    {
        var margin = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        button.AddChild(margin);
        var content = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        content.AddThemeConstantOverride("separation", 3);
        margin.AddChild(content);
        return content;
    }

    private static HBoxContainer CreateTileSpacerRow()
    {
        var row = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0f, 27f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        return row;
    }

    private HBoxContainer CreateTileBadgeRow(
        bool active,
        string stateText,
        StatusTone tone)
    {
        var row = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0f, 27f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        row.AddThemeConstantOverride("separation", 6);
        var activeBadge = CreateTileBadge(Localize("managerTileActive"), Gold);
        activeBadge.Visible = active;
        row.AddChild(activeBadge);
        var spacer = new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        row.AddChild(spacer);
        row.AddChild(CreateTileBadge(stateText, ToneColor(tone)));
        return row;
    }

    private static Label CreateTileBadge(string text, Color color)
    {
        var label = CreateLabel(text, 13, HorizontalAlignment.Center);
        label.CustomMinimumSize = new Vector2(68f, 25f);
        label.MouseFilter = MouseFilterEnum.Ignore;
        label.Modulate = color;
        label.AddThemeStyleboxOverride("normal", CreateTileBadgeStyle(color));
        return label;
    }

    private Control CreateDeckBoxDisplay(StatusTone tone, bool standard)
    {
        var host = new CenterContainer
        {
            CustomMinimumSize = new Vector2(0f, 108f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        if (LoadDeckBoxTexture() is { } texture)
        {
            var image = new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(152f, 104f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
                Modulate = standard
                    ? new Color(1f, 0.92f, 0.66f, 1f)
                    : tone == StatusTone.Error
                        ? new Color(0.68f, 0.72f, 0.80f, 0.92f)
                        : Colors.White
            };
            host.AddChild(image);
        }
        else
        {
            var fallback = CreateLabel("▰", 66, HorizontalAlignment.Center);
            fallback.Modulate = standard ? PaleGold : Cyan;
            fallback.MouseFilter = MouseFilterEnum.Ignore;
            host.AddChild(fallback);
        }
        return host;
    }

    private static Label CreateTileNameLabel(string text)
    {
        var label = CreateLabel(text, 19, HorizontalAlignment.Center);
        label.CustomMinimumSize = new Vector2(0f, 29f);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.MouseFilter = MouseFilterEnum.Ignore;
        return label;
    }

    private static Label CreateTileSummaryLabel(string text)
    {
        var label = CreateLabel(text, 13, HorizontalAlignment.Center);
        label.CustomMinimumSize = new Vector2(0f, 22f);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.ClipText = true;
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.Modulate = MutedColor;
        label.MouseFilter = MouseFilterEnum.Ignore;
        return label;
    }

    private Texture2D LoadDeckBoxTexture()
    {
        if (IsValid(_deckBoxTexture))
            return _deckBoxTexture;

        var customPath = "reward_pool_deck_box.png".CharacterUiPath();
        var fallbackPath = "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_deck.tres";
        try
        {
            var path = ResourceLoader.Exists(customPath) ? customPath : fallbackPath;
            if (ResourceLoader.Exists(path))
                _deckBoxTexture = ResourceLoader.Load<Texture2D>(path);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info(
                $"Reward-pool deck-box texture unavailable reason={exception.Message}");
        }
        return _deckBoxTexture;
    }

    private void SelectStandardTile(bool ensureVisible, bool clearRememberedPreset)
    {
        if (_viewMode != ViewMode.Library)
            return;
        _standardSelected = true;
        _selectedPresetId = null;
        if (clearRememberedPreset)
            _rememberedPresetId = null;
        RefreshStandardDetail();
        ApplyPresetTileStyles();
        if (ensureVisible)
            EnsurePresetTileVisible(_standardPresetTile);
    }

    private void SelectLibraryPreset(
        RewardPoolPresetDraft preset,
        bool ensureVisible,
        bool rememberSelection = true)
    {
        if (_viewMode != ViewMode.Library || preset is null)
            return;
        _standardSelected = false;
        _selectedPresetId = preset.Id;
        if (rememberSelection)
            _rememberedPresetId = preset.Id;
        RefreshPresetDetail(preset);
        ApplyPresetTileStyles();
        if (ensureVisible)
            EnsurePresetTileVisible(GetSelectedPresetTileButton());
    }

    private void ApplyPresetTileStyles()
    {
        ApplyManagerTileStyles();
    }

    private Button GetSelectedPresetTileButton() => _presetTiles
        .FirstOrDefault(tile => tile.Kind == LibraryTileKind.Standard && _standardSelected
            || tile.Kind == LibraryTileKind.Preset
                && !_standardSelected
                && tile.PresetId == _selectedPresetId)
        ?.Button;

    private void EnsurePresetTileVisible(Control tile)
    {
        var scroll = _presetScroll;
        var grid = _presetGrid;
        var view = _activeViewRoot;
        bool IsCurrent() => !_disposed && !_closing && _viewMode == ViewMode.Library
            && ReferenceEquals(_presetScroll, scroll) && ReferenceEquals(_presetGrid, grid)
            && ReferenceEquals(_activeViewRoot, view) && IsValid(scroll) && IsValid(grid)
            && IsValid(view) && IsValid(tile) && !tile.IsQueuedForDeletion()
            && scroll.IsInsideTree() && tile.IsInsideTree() && scroll.IsAncestorOf(tile);
        if (!IsCurrent())
            return;
        Callable.From(() =>
        {
            if (!IsCurrent())
                return;
            Callable.From(() =>
            {
                if (IsCurrent())
                    scroll.EnsureControlVisible(tile);
            }).CallDeferred();
        }).CallDeferred();
    }

    private void ConfigurePresetTileFocusNeighbors()
    {
        ConfigureManagerFocus();
    }

    private static Button FindFocusableActionSlot(
        IReadOnlyList<Button> slots,
        int startIndex,
        int step,
        Func<int, bool> staysInDirection)
    {
        for (var index = startIndex;
             index >= 0 && index < slots.Count && staysInDirection(index);
             index += step)
        {
            if (!slots[index].Disabled)
                return slots[index];
        }
        return null;
    }

    private static void SetFocusNeighbors(
        Control control,
        Control left,
        Control right,
        Control top,
        Control bottom,
        Control previous,
        Control next)
    {
        SetFocusNeighbor(control, FocusDirection.Left, left);
        SetFocusNeighbor(control, FocusDirection.Right, right);
        SetFocusNeighbor(control, FocusDirection.Top, top);
        SetFocusNeighbor(control, FocusDirection.Bottom, bottom);
        SetFocusNeighbor(control, FocusDirection.Previous, previous);
        SetFocusNeighbor(control, FocusDirection.Next, next);
    }

    private static void SetFocusNeighbor(
        Control from,
        FocusDirection direction,
        Control to)
    {
        if (!IsValid(from) || !IsValid(to))
            return;
        var path = from.GetPathTo(to);
        switch (direction)
        {
            case FocusDirection.Left:
                from.FocusNeighborLeft = path;
                break;
            case FocusDirection.Right:
                from.FocusNeighborRight = path;
                break;
            case FocusDirection.Top:
                from.FocusNeighborTop = path;
                break;
            case FocusDirection.Bottom:
                from.FocusNeighborBottom = path;
                break;
            case FocusDirection.Previous:
                from.FocusPrevious = path;
                break;
            case FocusDirection.Next:
                from.FocusNext = path;
                break;
        }
    }

    private void RefreshStandardDetail()
    {
        ApplyManagerTileStyles();
    }



    private void RefreshPresetDetail(RewardPoolPresetDraft preset)
    {
        ApplyManagerTileStyles();
    }

    private void CreatePreset()
    {
        var now = DateTime.UtcNow;
        var draft = new RewardPoolPresetDraft(
            Guid.Empty,
            RewardPoolPresetService.GetDefaultName(),
            [],
            [],
            now,
            now);
        ShowEditorView(draft);
    }

    private void UseStandard()
    {
        if (!RewardPoolPresetService.TrySelect(
                null,
                out _,
                out var persistence))
        {
            ShowOperationError(Localize("managerSelectFailed"), persistence);
            return;
        }

        _libraryChanged = true;
        _selectionChanged = true;
        SetLibraryStatus(Localize("managerStandardSelected"), null, StatusTone.Success);
        Complete();
    }

    private void UseSelectedPreset()
    {
        if (_selectedPresetId is not { } id)
            return;

        if (!RewardPoolPresetService.TrySelect(
                id,
                out var validation,
                out var persistence))
        {
            ShowOperationError(Localize("managerSelectFailed"), persistence);
            return;
        }

        _libraryChanged = true;
        _selectionChanged = true;
        SetLibraryStatus(
            validation.IsValid ? Localize("managerValid") : Localize("managerDraft"),
            null,
            validation.IsValid ? StatusTone.Success : StatusTone.Warning);
        Complete();
    }

    private void EditSelectedPreset()
    {
        if (_selectedPresetId is not { } id
            || !RewardPoolPresetService.TryGet(id, out var preset))
            return;
        ShowEditorView(preset);
    }

    private void DuplicateSelectedPreset()
    {
        if (_selectedPresetId is not { } id)
            return;

        if (!RewardPoolPresetService.TryDuplicate(id, out var copy, out var persistence))
        {
            ShowOperationError(Localize("managerSaveFailed"), persistence);
            return;
        }

        _libraryChanged = true;
        SetLibraryStatus(Localize("managerDuplicated"), null, StatusTone.Success);
        RefreshLibrary(copy.Id);
    }

    private void RenameSelectedPreset()
    {
        if (_selectedPresetId is not { } id
            || !RewardPoolPresetService.TryGet(id, out var preset))
            return;

        ShowTextModal(
            Localize("managerRenameTitle"),
            Localize("managerRenamePrompt"),
            preset.Name,
            Localize("managerRenameConfirm"),
            newName =>
            {
                if (!RewardPoolPresetService.TryRename(
                        preset.Id,
                        newName,
                        out var renamed,
                        out var persistence))
                {
                    CloseModal();
                    ShowOperationError(Localize("managerRenameFailed"), persistence);
                    return;
                }

                CloseModal();
                _libraryChanged = true;
                RefreshLibrary(renamed.Id);
            });
    }

    private void DeleteSelectedPreset()
    {
        if (_selectedPresetId is not { } id
            || !RewardPoolPresetService.TryGet(id, out var preset))
            return;

        var wasActive = RewardPoolPresetService.GetLibrarySnapshot().ActivePresetId == preset.Id;
        ShowChoiceModal(
            Localize("managerDeleteTitle"),
            LocalizeValue("managerDeletePrompt", "Name", preset.Name),
            Localize("managerDeleteConfirm"),
            () =>
            {
                CloseModal();
                if (!RewardPoolPresetService.TryDelete(preset.Id, out var persistence))
                {
                    ShowOperationError(Localize("managerDeleteFailed"), persistence);
                    return;
                }

                _libraryChanged = true;
                _selectionChanged |= wasActive;
                _selectedPresetId = null;
                _rememberedPresetId = null;
                RefreshLibrary(null);
            },
            Localize("managerCancel"),
            CloseModal,
            primaryIsDestructive: true);
    }

    private void SetLibraryStatus(
        string prefix,
        RewardPoolPresetOperationResult? result,
        StatusTone tone)
    {
        if (!IsValid(_libraryStatus))
            return;

        var detail = result.HasValue && !result.Value.IsValid
            ? FormatOperationError(result.Value)
            : string.Empty;
        _libraryStatus.Text = string.IsNullOrWhiteSpace(detail)
            || string.Equals(prefix, detail, StringComparison.Ordinal)
                ? prefix
                : $"{prefix}  {detail}";
        _libraryStatus.Modulate = ToneColor(tone);
    }

    private void ShowOperationError(
        string prefix,
        RewardPoolPresetOperationResult? result)
    {
        var detail = result.HasValue && !result.Value.IsValid
            ? FormatOperationError(result.Value)
            : string.Empty;
        var body = string.IsNullOrWhiteSpace(detail)
            || string.Equals(prefix, detail, StringComparison.Ordinal)
                ? prefix
                : $"{prefix}\n\n{detail}";
        var content = BeginModal(
            Localize("managerErrorTitle"),
            body,
            out var actions);
        var close = CreateButton(Localize("managerClose"), 170f);
        close.Pressed += CloseModal;
        actions.AddChild(close);
        FinishModal(content, close);
    }

    // ---------------------------------------------------------------------
    // Preset editor
    // ---------------------------------------------------------------------

    private void ShowEditorView(RewardPoolPresetDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _editingDraft = draft.Snapshot();
        _constructionMethod = draft.ConstructionMethod;
        _editorBaseline = EditorSnapshot.From(_editingDraft);
        _selectedMainIds.Clear();
        _selectedMainIds.AddRange(_editingDraft.MainCardIds);
        _selectedExtraIds.Clear();
        _selectedExtraIds.AddRange(_editingDraft.ExtraCardIds);
        _activeTab = BuilderTab.Main;
        _compactPane = BuilderPane.Pool;
        _sortDescending = false;
        _currentDetailCardId = string.Empty;
        _showUpgradePreview = false;

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 11);

        _editorHeader = new GridContainer
        {
            Columns = 3,
            CustomMinimumSize = new Vector2(0f, 58f)
        };
        _editorHeader.AddThemeConstantOverride("h_separation", 10);
        _editorHeader.AddThemeConstantOverride("v_separation", 8);
        root.AddChild(_editorHeader);
        var title = CreateLabel(Localize("builderPresetTitle"), 28, HorizontalAlignment.Left);
        title.Modulate = PaleGold;
        _editorHeader.AddChild(title);
        _presetNameEdit = CreateLineEdit(Localize("managerNamePlaceholder"));
        _presetNameEdit.Text = draft.Name;
        _presetNameEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _presetNameEdit.TextChanged += _ =>
        {
            UpdateEditorDirtyPresentation();
            RefreshEditorValidation();
        };
        _editorHeader.AddChild(_presetNameEdit);
        var back = CreateButton(Localize("builderBack"), 130f);
        back.Pressed += RequestLeaveEditor;
        _editorHeader.AddChild(back);

        BuildConstructionToolbar(root);

        var tabs = new HBoxContainer { CustomMinimumSize = new Vector2(0f, 52f) };
        tabs.AddThemeConstantOverride("separation", 10);
        root.AddChild(tabs);
        _mainTab = CreateButton(string.Empty, 0f);
        _mainTab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _mainTab.ToggleMode = true;
        _mainTab.Pressed += () => SelectTab(BuilderTab.Main);
        tabs.AddChild(_mainTab);
        _extraTab = CreateButton(string.Empty, 0f);
        _extraTab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _extraTab.ToggleMode = true;
        _extraTab.Pressed += () => SelectTab(BuilderTab.Extra);
        tabs.AddChild(_extraTab);

        _paneToolbar = new GridContainer
        {
            Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            Visible = false
        };
        _paneToolbar.AddThemeConstantOverride("h_separation", 8);
        _paneToolbar.AddThemeConstantOverride("v_separation", 8);
        root.AddChild(_paneToolbar);
        _showSelectedPaneButton = CreatePaneButton(
            Localize("builderShowSelected"),
            BuilderPane.Selected);
        _paneToolbar.AddChild(_showSelectedPaneButton);
        _showPoolPaneButton = CreatePaneButton(Localize("builderShowPool"), BuilderPane.Pool);
        _paneToolbar.AddChild(_showPoolPaneButton);
        _showDetailPaneButton = CreatePaneButton(Localize("builderShowDetail"), BuilderPane.Detail);
        _paneToolbar.AddChild(_showDetailPaneButton);

        _editorSplit = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _editorSplit.AddThemeConstantOverride("separation", 14);
        root.AddChild(_editorSplit);
        BuildSelectedPanel();
        BuildPoolPanel();
        BuildCardDetailPanel();

        _editorBottom = new GridContainer
        {
            Columns = 3,
            CustomMinimumSize = new Vector2(0f, 94f)
        };
        _editorBottom.AddThemeConstantOverride("h_separation", 14);
        _editorBottom.AddThemeConstantOverride("v_separation", 8);
        root.AddChild(_editorBottom);
        var bottomText = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bottomText.AddThemeConstantOverride("separation", 4);
        _editorBottom.AddChild(bottomText);
        var rarityGrid = new GridContainer { Columns = 2 };
        rarityGrid.AddThemeConstantOverride("h_separation", 10);
        rarityGrid.AddThemeConstantOverride("v_separation", 3);
        bottomText.AddChild(rarityGrid);
        (_commonProgressLabel, _commonProgress) = AddRarityProgress(
            rarityGrid,
            Localize("builderRarityCommon"));
        (_uncommonProgressLabel, _uncommonProgress) = AddRarityProgress(
            rarityGrid,
            Localize("builderRarityUncommon"));
        (_rareProgressLabel, _rareProgress) = AddRarityProgress(
            rarityGrid,
            Localize("builderRarityRare"));
        _validationLabel = CreateLabel(string.Empty, 17, HorizontalAlignment.Left);
        _validationLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        bottomText.AddChild(_validationLabel);
        _editorToast = CreateLabel(string.Empty, 15, HorizontalAlignment.Left);
        _editorToast.Modulate = MutedColor;
        bottomText.AddChild(_editorToast);

        _saveButton = CreateButton(Localize("builderSave"), 165f);
        _saveButton.Pressed += () => SaveEditor(useAfterSave: false);
        _editorBottom.AddChild(_saveButton);
        _saveAndUseButton = CreateButton(Localize("builderSaveAndUse"), 205f);
        _saveAndUseButton.Pressed += () => SaveEditor(useAfterSave: true);
        _editorBottom.AddChild(_saveAndUseButton);

        ReplaceView(root, ViewMode.Editor);
        SelectTab(BuilderTab.Main, clearSearch: false);
        UpdateEditorDirtyPresentation();
        DeferFocus(_poolList);
    }

    private void BuildSelectedPanel()
    {
        _selectedPanel = CreateSectionPanel();
        _selectedPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _selectedPanel.SizeFlagsStretchRatio = 0.31f;
        _editorSplit.AddChild(_selectedPanel);
        var margin = CreateSectionMargin(_selectedPanel);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);
        _selectedTitle = CreateLabel(string.Empty, 22, HorizontalAlignment.Left);
        _selectedTitle.Modulate = PaleGold;
        root.AddChild(_selectedTitle);
        _lockedHint = CreateLabel(string.Empty, 15, HorizontalAlignment.Left);
        _lockedHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _lockedHint.Modulate = WarningColor;
        root.AddChild(_lockedHint);
        _selectedList = CreatePortraitList(SelectedGridMaximumColumns);
        var selectedList = _selectedList;
        selectedList.ItemClicked += (index, position, button) =>
        {
            if (CanUseEditorList(selectedList))
                OnSelectedItemClicked(index, position, button);
        };
        selectedList.ItemSelected += index =>
        {
            if (CanUseEditorList(selectedList))
                OnSelectedItemSelected(index);
        };
        selectedList.GuiInput += input =>
        {
            if (CanUseEditorList(selectedList))
                OnSelectedListGuiInput(input);
        };
        root.AddChild(_selectedList);
        CardInspectionRegistry.Register(
            _selectedList,
            Root,
            point => CreateListInspectionRequest(_selectedList, fromPool: false, point),
            CanInspectEditorCards);
    }

    private void BuildPoolPanel()
    {
        _poolPanel = CreateSectionPanel();
        _poolPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _poolPanel.SizeFlagsStretchRatio = 0.45f;
        _editorSplit.AddChild(_poolPanel);
        var margin = CreateSectionMargin(_poolPanel);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);
        margin.AddChild(root);
        _poolTitle = CreateLabel(string.Empty, 22, HorizontalAlignment.Left);
        _poolTitle.Modulate = PaleGold;
        root.AddChild(_poolTitle);

        _poolSearchRow = new GridContainer
        {
            Columns = 3,
            CustomMinimumSize = new Vector2(0f, 44f)
        };
        _poolSearchRow.AddThemeConstantOverride("h_separation", 7);
        _poolSearchRow.AddThemeConstantOverride("v_separation", 7);
        root.AddChild(_poolSearchRow);
        _search = CreateLineEdit(Localize("builderSearchPlaceholder"));
        _search.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _search.TextChanged += _ => ScheduleSearchRefresh(RefreshPoolList);
        _search.TextSubmitted += _ => ApplyPendingSearchRefresh();
        _poolSearchRow.AddChild(_search);
        _sortOrder = CreateOptionButton(150f);
        _sortOrder.ItemSelected += _ => RefreshPoolAfterPresentationChange();
        _poolSearchRow.AddChild(_sortOrder);
        _sortDirection = CreateButton(string.Empty, 94f, 15);
        _sortDirection.Pressed += () =>
        {
            _sortDescending = !_sortDescending;
            UpdateSortDirectionText();
            RefreshPoolAfterPresentationChange();
        };
        _poolSearchRow.AddChild(_sortDirection);
        UpdateSortDirectionText();

        _mainFilterRow = new GridContainer
        {
            Columns = 3,
            CustomMinimumSize = new Vector2(0f, 44f)
        };
        _mainFilterRow.AddThemeConstantOverride("h_separation", 7);
        _mainFilterRow.AddThemeConstantOverride("v_separation", 7);
        root.AddChild(_mainFilterRow);
        _originFilter = CreateOptionButton(0f);
        _originFilter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _originFilter.AddItem(Localize("builderOriginAll"));
        foreach (var origin in RewardPoolCatalog.MainCardOriginOrder)
            _originFilter.AddItem(Localize(OriginLocalizationKey(origin)));
        _originFilter.ItemSelected += _ => RefreshPoolAfterPresentationChange();
        _mainFilterRow.AddChild(_originFilter);
        _rarityFilter = CreateOptionButton(0f);
        _rarityFilter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rarityFilter.AddItem(Localize("builderRarityAll"));
        _rarityFilter.AddItem(Localize("builderRarityCommon"));
        _rarityFilter.AddItem(Localize("builderRarityUncommon"));
        _rarityFilter.AddItem(Localize("builderRarityRare"));
        _rarityFilter.ItemSelected += _ => RefreshPoolAfterPresentationChange();
        _mainFilterRow.AddChild(_rarityFilter);
        _typeFilter = CreateOptionButton(0f);
        _typeFilter.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _typeFilter.AddItem(Localize("builderTypeAll"));
        _typeFilter.AddItem(Localize("builderTypeAttack"));
        _typeFilter.AddItem(Localize("builderTypeSkill"));
        _typeFilter.AddItem(Localize("builderTypePower"));
        _typeFilter.ItemSelected += _ => RefreshPoolAfterPresentationChange();
        _mainFilterRow.AddChild(_typeFilter);

        _poolList = CreatePortraitList(PoolGridMaximumColumns);
        var poolList = _poolList;
        poolList.ItemClicked += (index, position, button) =>
        {
            if (CanUseEditorList(poolList))
                OnPoolItemClicked(index, position, button);
        };
        poolList.ItemSelected += index =>
        {
            if (CanUseEditorList(poolList))
                OnPoolItemSelected(index);
        };
        poolList.GuiInput += input =>
        {
            if (CanUseEditorList(poolList))
                OnPoolListGuiInput(input);
        };
        root.AddChild(_poolList);
        CardInspectionRegistry.Register(
            _poolList,
            Root,
            point => CreateListInspectionRequest(_poolList, fromPool: true, point),
            CanInspectEditorCards);
    }

    private void BuildCardDetailPanel()
    {
        _detailPanel = CreateSectionPanel();
        _detailPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _detailPanel.SizeFlagsStretchRatio = 0.24f;
        _editorSplit.AddChild(_detailPanel);
        var margin = CreateSectionMargin(_detailPanel);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 7);
        margin.AddChild(root);
        var heading = CreateLabel(Localize("builderDetailTitle"), 22, HorizontalAlignment.Left);
        heading.Modulate = PaleGold;
        root.AddChild(heading);

        _nativeCardPreviewHost = new CenterContainer
        {
            CustomMinimumSize = new Vector2(0f, 500f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            Visible = false
        };
        _nativeCardPreviewHost.GuiInput += OnNativeCardPreviewHostGuiInput;
        root.AddChild(_nativeCardPreviewHost);

        _fallbackDetail = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _fallbackDetail.AddThemeConstantOverride("separation", 7);
        root.AddChild(_fallbackDetail);
        _detailCardTitle = CreateLabel(string.Empty, 25, HorizontalAlignment.Center);
        _detailCardTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailCardTitle.CustomMinimumSize = new Vector2(0f, 58f);
        _fallbackDetail.AddChild(_detailCardTitle);
        _detailPortrait = new TextureRect
        {
            CustomMinimumSize = new Vector2(0f, 225f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = Localize("builderDetailInspectHint")
        };
        _detailPortrait.GuiInput += OnFallbackDetailPortraitGuiInput;
        _fallbackDetail.AddChild(_detailPortrait);
        CardInspectionRegistry.Register(
            _detailPortrait,
            Root,
            _ => CreateCurrentCardInspectionRequest(_detailPortrait),
            CanInspectEditorCards);
        var metadata = new GridContainer { Columns = 2 };
        metadata.AddThemeConstantOverride("h_separation", 9);
        metadata.AddThemeConstantOverride("v_separation", 4);
        _fallbackDetail.AddChild(metadata);
        _detailCost = CreateDetailMetadataLabel();
        _detailOrigin = CreateDetailMetadataLabel();
        _detailType = CreateDetailMetadataLabel();
        _detailRarity = CreateDetailMetadataLabel();
        metadata.AddChild(_detailCost);
        metadata.AddChild(_detailOrigin);
        metadata.AddChild(_detailType);
        metadata.AddChild(_detailRarity);
        _detailEffectTitle = CreateLabel(Localize("builderDetailEffect"), 19, HorizontalAlignment.Left);
        _fallbackDetail.AddChild(_detailEffectTitle);
        _detailDescription = new MegaRichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = false,
            ScrollActive = true,
            ScrollFollowing = false,
            ScrollFollowingVisibleCharacters = false,
            AutoSizeEnabled = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0f, 140f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.All
        };
        _detailDescription.AddThemeFontOverride("normal_font", ThemeDB.FallbackFont);
        _detailDescription.AddThemeFontSizeOverride("normal_font_size", 21);
        _fallbackDetail.AddChild(_detailDescription);

        _upgradePreviewButton = CreateButton(Localize("builderDetailShowUpgrade"), 0f, 17);
        _upgradePreviewButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _upgradePreviewButton.ToggleMode = true;
        _upgradePreviewButton.Visible = false;
        _upgradePreviewButton.Disabled = true;
        _upgradePreviewButton.Pressed += OnUpgradePreviewPressed;
        root.AddChild(_upgradePreviewButton);
        ShowEmptyDetail();
    }

    private Button CreatePaneButton(string text, BuilderPane pane)
    {
        var button = CreateButton(text, 170f, 16);
        button.ToggleMode = true;
        button.Pressed += () =>
        {
            _compactPane = pane;
            UpdateResponsiveLayout();
            DeferFocus(pane switch
            {
                BuilderPane.Selected => _selectedList,
                BuilderPane.Detail => GetDetailFocusTarget(),
                _ => _poolList
            });
        };
        return button;
    }

    private void SelectTab(BuilderTab tab, bool clearSearch = true)
    {
        if (_viewMode != ViewMode.Editor)
            return;

        CancelPendingSearchRefresh();
        _activeTab = tab;
        _mainTab.ButtonPressed = tab == BuilderTab.Main;
        _extraTab.ButtonPressed = tab == BuilderTab.Extra;
        _mainFilterRow.Visible = tab == BuilderTab.Main;
        if (IsValid(_packagesButton))
            _packagesButton.Visible = tab == BuilderTab.Main;
        PopulateSortOptions(tab);
        if (clearSearch && !string.IsNullOrEmpty(_search.Text))
        {
            _suppressSearchRefresh = true;
            try
            {
                _search.Text = string.Empty;
            }
            finally
            {
                _suppressSearchRefresh = false;
            }
        }

        RefreshEditor();
    }

    private void RefreshEditor(CardEntry preferredDetail = null)
    {
        UpdateHeadings();
        RefreshSelectedList();
        RefreshPoolList(preferredDetail);
        RefreshEditorValidation();
        UpdateEditorDirtyPresentation();
    }

    private void UpdateHeadings()
    {
        _mainTab.Text = LocalizeCount(
            "builderTabMain",
            ("Selected", _selectedMainIds.Count),
            ("Minimum", RewardPoolSizePolicy.MinSelectableMain),
            ("Required", MainSelectionCount));
        _extraTab.Text = LocalizeCount(
            "builderTabExtra",
            ("Selected", _selectedExtraIds.Count),
            ("Locked", RewardPoolCatalog.RequiredExtraCardCount),
            ("Total", RewardPoolCatalog.ExtraDeckSize));

        if (_activeTab == BuilderTab.Main)
        {
            _selectedTitle.Text = LocalizeCount(
                "builderSelectedMain",
                ("Selected", _selectedMainIds.Count),
                ("Minimum", RewardPoolSizePolicy.MinSelectableMain),
                ("Required", MainSelectionCount));
            _lockedHint.Text = Localize("builderLockedMainHint");
            _poolTitle.Text = LocalizeCount("builderPoolMain", ("Count", _mainEntries.Length));
        }
        else
        {
            _selectedTitle.Text = LocalizeCount(
                "builderSelectedExtra",
                ("Selected", _selectedExtraIds.Count),
                ("Required", ExtraSelectionCount));
            _lockedHint.Text = Localize("builderLockedExtraHint");
            _poolTitle.Text = LocalizeCount("builderPoolExtra", ("Count", _extraEntries.Length));
        }
    }

    private void RefreshSelectedList(int preferredIndex = 0)
    {
        if (!IsValid(_selectedList))
            return;

        _selectedList.Clear();
        _displayedSelectedCards.Clear();
        var ids = ActiveSelectedIds();
        var index = ActiveEntryIndex();
        for (var sourceIndex = 0; sourceIndex < ids.Count; sourceIndex++)
        {
            var id = ids[sourceIndex] ?? string.Empty;
            index.TryGetValue(id, out var entry);
            _displayedSelectedCards.Add(new SelectedCardEntry(sourceIndex, id, entry));
        }

        if (_displayedSelectedCards.Count == 0)
        {
            AddEmptyItem(_selectedList, Localize("builderNoSelected"));
            return;
        }

        foreach (var selected in _displayedSelectedCards)
        {
            if (selected.Entry is { } entry)
            {
                AddPortraitItem(_selectedList, entry, selected: true);
            }
            else
            {
                var text = $"{Localize("builderInvalidCard")}\n{DisplayUnknownId(selected.Id)}";
                var item = _selectedList.AddItem(text, null, true);
                _selectedList.SetItemTooltip(item, selected.Id);
                _selectedList.SetItemCustomBgColor(item, new Color(0.30f, 0.06f, 0.10f, 0.94f));
                _selectedList.SetItemCustomFgColor(item, BadColor);
            }
        }

        _selectedList.Select(Math.Clamp(preferredIndex, 0, _displayedSelectedCards.Count - 1));
    }

    private void RefreshPoolList() => RefreshPoolList(null);

    private void RefreshPoolList(CardEntry preferredDetail)
    {
        if (preferredDetail is not null)
            CancelPendingSearchRefresh();
        if (_suppressSearchRefresh || _viewMode != ViewMode.Editor || !IsValid(_poolList))
            return;

        var restoreFocus = _poolList.HasFocus();
        _normalizedSearchQuery = NormalizeSearchText(_search?.Text);
        var originIndex = _activeTab == BuilderTab.Main ? _originFilter.Selected : 0;
        var rarity = _activeTab == BuilderTab.Main
            ? _rarityFilter.Selected switch
            {
                1 => CardRarity.Common,
                2 => CardRarity.Uncommon,
                3 => CardRarity.Rare,
                _ => (CardRarity?)null
            }
            : null;
        var type = _activeTab == BuilderTab.Main
            ? _typeFilter.Selected switch
            {
                1 => CardType.Attack,
                2 => CardType.Skill,
                3 => CardType.Power,
                _ => (CardType?)null
            }
            : null;
        var sort = (BuilderSort)_sortOrder.GetSelectedId();
        var filtered = ActiveCandidates()
            .Where(entry => MatchesFilters(
                entry,
                _normalizedSearchQuery,
                originIndex,
                rarity,
                type))
            .ToList();
        filtered.Sort((left, right) => CompareForDisplay(
            left,
            right,
            sort,
            _sortDescending));
        _displayedPoolCards = filtered;

        _poolList.Clear();
        var selectedIds = ActiveSelectedIds();
        foreach (var entry in filtered)
            AddPortraitItem(_poolList, entry, selectedIds.Contains(entry.Id, StringComparer.Ordinal));

        if (filtered.Count == 0)
        {
            AddEmptyItem(_poolList, Localize("builderNoResults"));
            ScheduleDetail(preferredDetail);
        }
        else
        {
            var preferredIndex = preferredDetail is null
                ? -1
                : filtered.FindIndex(entry => string.Equals(
                    entry.Id,
                    preferredDetail.Id,
                    StringComparison.Ordinal));
            _poolList.Select(preferredIndex >= 0 ? preferredIndex : 0);
            ScheduleDetail(preferredDetail ?? filtered[0]);
        }

        if (restoreFocus)
            DeferFocus(_poolList);
    }

    private void RefreshPoolAfterPresentationChange()
    {
        CancelPendingSearchRefresh();
        RefreshPoolList();
    }

    private void RefreshEditorValidation()
    {
        if (_viewMode != ViewMode.Editor || !IsValid(_validationLabel))
            return;

        var draft = CaptureEditorDraft();
        var validation = RewardPoolPresetService.Validate(draft);
        UpdateRarityProgress(validation);
        var nameOk = !string.IsNullOrWhiteSpace(draft.Name);
        var requiresInitialSave = _editingDraft?.Id == Guid.Empty;
        _saveButton.Disabled = !nameOk || (!requiresInitialSave && !IsEditorDirty());
        _saveAndUseButton.Disabled = !nameOk || !validation.IsValid;
        _validationLabel.Text = validation.IsValid
            ? "✓ " + Localize("builderReady")
            : "⚠ " + FormatValidationIssues(validation);
        _validationLabel.Modulate = validation.IsValid ? GoodColor : WarningColor;
        RefreshConstructionBudget();
    }

    private void UpdateRarityProgress(RewardPoolPresetValidation validation)
    {
        var minimum = RewardPoolCatalog.MinimumPerRewardRarity;
        UpdateRarityProgress(_commonProgressLabel, _commonProgress, validation.CommonCount, minimum);
        UpdateRarityProgress(_uncommonProgressLabel, _uncommonProgress, validation.UncommonCount, minimum);
        UpdateRarityProgress(_rareProgressLabel, _rareProgress, validation.RareCount, minimum);
    }

    private static void UpdateRarityProgress(
        Label label,
        ProgressBar progress,
        int count,
        int minimum)
    {
        if (!IsValid(label) || !IsValid(progress))
            return;
        label.Text = $"{count}/{minimum}";
        label.Modulate = count >= minimum ? GoodColor : WarningColor;
        progress.MaxValue = minimum;
        progress.Value = Math.Min(count, minimum);
    }

    private void UpdateEditorDirtyPresentation()
    {
        if (_viewMode != ViewMode.Editor || !IsValid(_editorToast))
            return;
        if (IsEditorDirty())
        {
            _editorToast.Text = "• " + Localize("managerDraft");
            _editorToast.Modulate = WarningColor;
        }
        else if (!IsEditorDirty() && _editorToast.Text.StartsWith("•", StringComparison.Ordinal))
            _editorToast.Text = string.Empty;
    }

    private RewardPoolPresetDraft CaptureEditorDraft() => new(
        _editingDraft?.Id ?? Guid.Empty,
        _presetNameEdit?.Text ?? _editingDraft?.Name ?? string.Empty,
        _selectedMainIds.ToArray(),
        _selectedExtraIds.ToArray(),
        _editingDraft?.CreatedUtc ?? DateTime.UtcNow,
        _editingDraft?.UpdatedUtc ?? DateTime.UtcNow,
        _constructionMethod);

    private bool IsEditorDirty() =>
        _editorBaseline is not null
        && !_editorBaseline.ContentEquals(EditorSnapshot.From(CaptureEditorDraft()));

    private bool SaveEditor(bool useAfterSave, Action afterSave = null)
    {
        if (_viewMode != ViewMode.Editor || _editingDraft is null)
            return false;

        var draft = CaptureEditorDraft();
        if (draft.Id != Guid.Empty && !IsEditorDirty())
        {
            if (!useAfterSave)
            {
                afterSave?.Invoke();
                return true;
            }

            if (!RewardPoolPresetService.TrySelect(
                    draft.Id,
                    out _,
                    out var selectionFailure))
            {
                ShowOperationError(Localize("managerSelectFailed"), selectionFailure);
                return false;
            }

            _selectionChanged = true;
            Complete();
            return true;
        }

        RewardPoolPresetDraft saved;
        RewardPoolPresetOperationResult persistence;
        var persisted = draft.Id == Guid.Empty
            ? RewardPoolPresetService.TryCreate(
                draft.Name,
                draft.MainCardIds,
                draft.ExtraCardIds,
                out saved,
                out persistence,
                draft.ConstructionMethod)
            : RewardPoolPresetService.TrySave(draft, out saved, out persistence);
        if (!persisted)
        {
            ShowOperationError(Localize("managerSaveFailed"), persistence);
            return false;
        }

        _editingDraft = saved;
        _presetNameEdit.Text = saved.Name;
        _selectedMainIds.Clear();
        _selectedMainIds.AddRange(saved.MainCardIds);
        _selectedExtraIds.Clear();
        _selectedExtraIds.AddRange(saved.ExtraCardIds);
        _editorBaseline = EditorSnapshot.From(saved);
        _libraryChanged = true;

        var validation = RewardPoolPresetService.Validate(saved);
        SetEditorToast(
            validation.IsValid ? Localize("builderSaved") : Localize("builderInvalidSaved"),
            null,
            validation.IsValid ? StatusTone.Success : StatusTone.Warning);
        RefreshEditorValidation();

        if (useAfterSave)
        {
            if (!RewardPoolPresetService.TrySelect(
                    saved.Id,
                    out _,
                    out persistence))
            {
                ShowOperationError(Localize("managerSelectFailed"), persistence);
                return false;
            }

            _selectionChanged = true;
            Complete();
            return true;
        }

        afterSave?.Invoke();
        return true;
    }

    private void SetEditorToast(
        string prefix,
        RewardPoolPresetOperationResult? result,
        StatusTone tone)
    {
        if (!IsValid(_editorToast))
            return;
        var detail = result.HasValue && !result.Value.IsValid
            ? FormatOperationError(result.Value)
            : string.Empty;
        _editorToast.Text = string.IsNullOrWhiteSpace(detail)
            || string.Equals(prefix, detail, StringComparison.Ordinal)
                ? prefix
                : $"{prefix}  {detail}";
        _editorToast.Modulate = ToneColor(tone);
    }

    private void RequestLeaveEditor()
    {
        if (_viewMode != ViewMode.Editor)
            return;
        if (!IsEditorDirty())
        {
            ShowLibraryView(_editingDraft?.Id == Guid.Empty ? null : _editingDraft?.Id);
            return;
        }

        ShowThreeChoiceModal(
            Localize("builderUnsavedTitle"),
            Localize("builderUnsavedPrompt"),
            Localize("builderSaveAndExit"),
            () =>
            {
                CloseModal();
                SaveEditor(
                    useAfterSave: false,
                    afterSave: () => ShowLibraryView(_editingDraft.Id));
            },
            Localize("builderDiscard"),
            () =>
            {
                CloseModal();
                ShowLibraryView(_editingDraft.Id == Guid.Empty ? null : _editingDraft.Id);
            },
            Localize("builderKeepEditing"),
            CloseModal);
    }

    private void OnPoolItemClicked(long index, Vector2 position, long mouseButton)
    {
        if (mouseButton == (long)MouseButton.Right)
            return;
        if (index < 0 || index >= _displayedPoolCards.Count)
            return;
        var entry = _displayedPoolCards[(int)index];
        ScheduleDetail(entry);
        if (mouseButton == (long)MouseButton.Left)
            ToggleCandidate(entry);
    }

    private void OnSelectedItemClicked(long index, Vector2 position, long mouseButton)
    {
        if (mouseButton == (long)MouseButton.Right)
            return;
        if (index < 0 || index >= _displayedSelectedCards.Count)
            return;
        var selected = _displayedSelectedCards[(int)index];
        ScheduleDetail(selected.Entry);
        if (mouseButton == (long)MouseButton.Left)
            RemoveSelectedOccurrence((int)index);
    }

    private void OnPoolItemSelected(long index)
    {
        if (index >= 0 && index < _displayedPoolCards.Count)
            ScheduleDetail(_displayedPoolCards[(int)index]);
    }

    private void OnSelectedItemSelected(long index)
    {
        if (index >= 0 && index < _displayedSelectedCards.Count)
            ScheduleDetail(_displayedSelectedCards[(int)index].Entry);
    }

    private void OnPoolListGuiInput(InputEvent inputEvent) =>
        HandleListInput(_poolList, fromPool: true, inputEvent);

    private void OnSelectedListGuiInput(InputEvent inputEvent) =>
        HandleListInput(_selectedList, fromPool: false, inputEvent);

    private void HandleListInput(ItemList list, bool fromPool, InputEvent inputEvent)
    {
        if (inputEvent is null or InputEventMouseButton
            || inputEvent is InputEventKey { Echo: true })
            return;
        var selected = list.GetSelectedItems();
        if (selected.Length == 0)
            return;
        var index = selected[0];
        if (inputEvent.IsActionPressed(MegaInput.select, false, false))
        {
            if (fromPool && index >= 0 && index < _displayedPoolCards.Count)
                ToggleCandidate(_displayedPoolCards[index]);
            else if (!fromPool && index >= 0 && index < _displayedSelectedCards.Count)
                RemoveSelectedOccurrence(index);
            Root.GetViewport()?.SetInputAsHandled();
        }
        else if (inputEvent.IsActionPressed(MegaInput.accept, false, false))
        {
            if (fromPool && index >= 0 && index < _displayedPoolCards.Count)
                ScheduleDetail(_displayedPoolCards[index]);
            else if (!fromPool && index >= 0 && index < _displayedSelectedCards.Count)
                ScheduleDetail(_displayedSelectedCards[index].Entry);
            Root.GetViewport()?.SetInputAsHandled();
        }
    }

    private void ToggleCandidate(CardEntry entry)
    {
        if (!CanInspectEditorCards() || CardInspectionService.IsBusy || entry is null)
            return;
        var ids = ActiveSelectedIds();
        var existing = ids.FindIndex(id => string.Equals(id, entry.Id, StringComparison.Ordinal));
        if (existing >= 0)
        {
            ids.RemoveAt(existing);
        }
        else
        {
            var limit = _activeTab == BuilderTab.Main ? MainSelectionCount : ExtraSelectionCount;
            if (ids.Count >= limit)
            {
                SetEditorToast(Localize("builderLimitReached"), null, StatusTone.Warning);
                return;
            }
            if (!CanAddConstructionCards([entry.Id], out var constructionError))
            {
                SetEditorToast(constructionError, null, StatusTone.Warning);
                return;
            }
            ids.Add(entry.Id);
        }

        _editorToast.Text = string.Empty;
        RefreshEditor(entry);
    }

    private void RemoveSelectedOccurrence(int displayedIndex)
    {
        if (!CanInspectEditorCards() || CardInspectionService.IsBusy
            || displayedIndex < 0 || displayedIndex >= _displayedSelectedCards.Count)
            return;
        var selected = _displayedSelectedCards[displayedIndex];
        var sourceIndex = selected.SourceIndex;
        var ids = ActiveSelectedIds();
        if (sourceIndex < 0 || sourceIndex >= ids.Count)
            return;
        ids.RemoveAt(sourceIndex);
        _editorToast.Text = string.Empty;
        UpdateHeadings();
        RefreshSelectedList(Math.Min(displayedIndex, Math.Max(0, ids.Count - 1)));
        RefreshPoolList(selected.Entry);
        RefreshEditorValidation();
        UpdateEditorDirtyPresentation();
    }

    private IReadOnlyList<CardEntry> ActiveCandidates() =>
        _activeTab == BuilderTab.Main ? _mainEntries : _extraEntries;

    private List<string> ActiveSelectedIds() =>
        _activeTab == BuilderTab.Main ? _selectedMainIds : _selectedExtraIds;

    private IReadOnlyDictionary<string, CardEntry> ActiveEntryIndex() =>
        _activeTab == BuilderTab.Main ? _mainById : _extraById;

    // ---------------------------------------------------------------------
    // Card detail, filtering, and sorting
    // ---------------------------------------------------------------------

    private void ScheduleDetail(CardEntry entry, bool force = false)
    {
        if (_disposed || _viewMode != ViewMode.Editor)
            return;
        var id = entry?.Id ?? string.Empty;
        if (!force
            && !_detailUpdateScheduled
            && string.Equals(_currentDetailCardId, id, StringComparison.Ordinal))
            return;
        _pendingDetailCard = entry;
        if (_detailUpdateScheduled)
            return;
        _detailUpdateScheduled = true;
        Callable.From(ApplyPendingDetail).CallDeferred();
    }

    private void ApplyPendingDetail()
    {
        _detailUpdateScheduled = false;
        if (_disposed || _viewMode != ViewMode.Editor)
            return;
        var entry = _pendingDetailCard;
        _pendingDetailCard = null;
        if (entry is null)
        {
            ShowEmptyDetail();
            return;
        }

        _currentDetailCardId = entry.Id;
        _currentDetailCard = entry;
        var previewModel = CreateDetailPreviewModel(entry, out var showingUpgrade);
        UpdateUpgradePreviewButton(entry, !ReferenceEquals(previewModel, entry.Model));
        if (TryShowNativeCardPreview(entry, previewModel, showingUpgrade))
            return;

        _nativeCardPreviewHost.Visible = false;
        _fallbackDetail.Visible = true;
        _detailPortrait.FocusMode = FocusModeEnum.All;
        _detailCardTitle.Text = SafeTitle(previewModel, entry.Id);
        _detailPortrait.Texture = LoadPortrait(entry);
        _detailCost.Text = LocalizeValue(
            entry.IsExtra ? "builderDetailMaterials" : "builderDetailCost",
            entry.IsExtra ? "Materials" : "Cost",
            GetDetailCostText(previewModel));
        _detailOrigin.Text = LocalizeValue("builderDetailOrigin", "Origin", entry.OriginText);
        _detailType.Text = LocalizeValue("builderDetailType", "Type", entry.TypeText);
        _detailRarity.Text = LocalizeValue("builderDetailRarity", "Rarity", entry.RarityText);
        SetDetailMetadataVisible(true);
        _detailDescription.SetTextAutoSize(GetDetailDescription(entry, previewModel, showingUpgrade));
        _detailDescription.ScrollToLine(0);
        Callable.From(() =>
        {
            if (!_disposed && IsValid(_detailDescription))
                _detailDescription.ScrollToLine(0);
        }).CallDeferred();
    }

    private void OnUpgradePreviewPressed()
    {
        if (_disposed || !IsValid(_upgradePreviewButton))
            return;

        _showUpgradePreview = _upgradePreviewButton.ButtonPressed;
        UpdateUpgradePreviewButton(_currentDetailCard, previewAvailable: true);
        if (_currentDetailCard is not null)
            ScheduleDetail(_currentDetailCard, force: true);
    }

    private CardModel CreateDetailPreviewModel(CardEntry entry, out bool showingUpgrade)
    {
        showingUpgrade = false;
        if (entry?.Model is not CardModel source)
            return null;

        try
        {
            if (source.MutableClone() is not CardModel preview)
                throw new InvalidOperationException("Card preview clone was unavailable.");

            if (_showUpgradePreview && CanPreviewUpgrade(source))
            {
                preview.UpgradePreviewType = CardUpgradePreviewType.Deck;
                if (!preview.IsUpgraded && preview.IsUpgradable)
                    preview.UpgradeInternal();
                showingUpgrade = preview.IsUpgraded;
            }

            return preview;
        }
        catch (Exception exception)
        {
            if (_previewFailures.Add(entry.Id))
            {
                MainFile.Logger.Info(
                    $"Reward-pool detail preview unavailable card={entry.Id} reason={exception.Message}");
            }
            return source;
        }
    }

    private void UpdateUpgradePreviewButton(CardEntry entry, bool previewAvailable)
    {
        if (!IsValid(_upgradePreviewButton))
            return;

        var canPreview = previewAvailable && CanPreviewUpgrade(entry?.Model);
        _upgradePreviewButton.Visible = canPreview;
        _upgradePreviewButton.Disabled = !canPreview;
        _upgradePreviewButton.ButtonPressed = canPreview && _showUpgradePreview;
        _upgradePreviewButton.Text = Localize(
            _showUpgradePreview ? "builderDetailShowBase" : "builderDetailShowUpgrade");
    }

    private static bool CanPreviewUpgrade(CardModel card)
    {
        try
        {
            return card?.MaxUpgradeLevel > 0;
        }
        catch
        {
            return false;
        }
    }

    private void ShowEmptyDetail()
    {
        if (!IsValid(_detailCardTitle))
            return;
        ReleaseNativeCardPreview();
        if (IsValid(_nativeCardPreviewHost))
            _nativeCardPreviewHost.Visible = false;
        if (IsValid(_fallbackDetail))
            _fallbackDetail.Visible = true;
        _detailCardTitle.Text = Localize("builderDetailEmpty");
        _currentDetailCardId = string.Empty;
        _currentDetailCard = null;
        _detailPortrait.Texture = null;
        _detailPortrait.FocusMode = FocusModeEnum.None;
        _detailCost.Text = string.Empty;
        _detailOrigin.Text = string.Empty;
        _detailType.Text = string.Empty;
        _detailRarity.Text = string.Empty;
        SetDetailMetadataVisible(false);
        _detailDescription.SetTextAutoSize(string.Empty);
        UpdateUpgradePreviewButton(null, previewAvailable: false);
    }

    private bool TryShowNativeCardPreview(
        CardEntry entry,
        CardModel previewModel,
        bool showingUpgrade)
    {
        ReleaseNativeCardPreview();
        if (!IsValid(_nativeCardPreviewHost) || previewModel is null)
            return false;

        NCard card = null;
        try
        {
            card = NCard.Create(previewModel, ModelVisibility.Visible);
            if (!IsValid(card))
                return false;

            _nativeCardPreview = card;
            card.MouseFilter = MouseFilterEnum.Ignore;
            card.FocusMode = FocusModeEnum.None;
            _nativeCardPreviewHost.AddChild(card);
            if (showingUpgrade)
                card.ShowUpgradePreview();
            else
                card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            AddNativeCardInspectButton(card);
            _fallbackDetail.Visible = false;
            _nativeCardPreviewHost.Visible = true;
            _nativeCardPreviewHost.FocusMode = FocusModeEnum.All;
            Callable.From(() => FitNativeCardPreview(card)).CallDeferred();
            return true;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info(
                $"Reward-pool native card preview unavailable card={entry.Id} reason={exception.Message}");
            if (IsValid(card))
            {
                _nativeCardPreview = card;
                ReleaseNativeCardPreview();
            }
            return false;
        }
    }

    private void AddNativeCardInspectButton(NCard card)
    {
        var button = new Button
        {
            Name = "ThermalVortexCardInspectButton",
            Text = string.Empty,
            Flat = true,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = Localize("builderDetailInspectHint"),
            ZIndex = 100,
            ZAsRelative = true
        };
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.Pressed += OnNativeCardInspectPressed;
        card.AddChild(button);
        _nativeCardInspectButton = button;
        CardInspectionRegistry.Register(
            button,
            Root,
            _ => CreateCurrentCardInspectionRequest(_nativeCardPreviewHost),
            CanInspectEditorCards);
    }

    private void OnNativeCardInspectPressed() =>
        ScheduleCurrentCardInspection(_nativeCardPreviewHost);

    private void FitNativeCardPreview(NCard card)
    {
        if (_disposed
            || !ReferenceEquals(card, _nativeCardPreview)
            || !IsValid(card)
            || !IsValid(_nativeCardPreviewHost))
            return;

        // GetCurrentSize includes Scale; fit against the original card dimensions.
        card.Scale = Vector2.One;
        var cardSize = card.GetCurrentSize();
        if (cardSize.X <= 1f || cardSize.Y <= 1f)
            cardSize = card.Size;
        if (cardSize.X <= 1f || cardSize.Y <= 1f)
            return;
        var available = _nativeCardPreviewHost.Size - new Vector2(12f, 12f);
        var scale = Math.Min(available.X / cardSize.X, available.Y / cardSize.Y);
        scale = Math.Clamp(scale, 0.42f, 0.86f);
        card.Scale = Vector2.One * scale;
    }

    private void ReleaseNativeCardPreview()
    {
        var inspectButton = _nativeCardInspectButton;
        _nativeCardInspectButton = null;
        if (IsValid(inspectButton))
        {
            CardInspectionRegistry.Unregister(inspectButton);
            inspectButton.Pressed -= OnNativeCardInspectPressed;
            inspectButton.GetParent()?.RemoveChild(inspectButton);
            inspectButton.Free();
        }

        var card = _nativeCardPreview;
        _nativeCardPreview = null;
        if (!IsValid(card))
            return;

        try
        {
            card.GetParent()?.RemoveChild(card);
            NodePool.Free(card);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info(
                $"Reward-pool native card preview return failed reason={exception.Message}");
            try
            {
                if (IsValid(card) && !card.IsQueuedForDeletion())
                    card.QueueFree();
            }
            catch
            {
                // There is nothing else safe to do during teardown.
            }
        }
    }

    private void OnNativeCardPreviewHostGuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton)
            return;
        HandleCardInspectionInput(_nativeCardPreviewHost, inputEvent);
    }

    private void OnFallbackDetailPortraitGuiInput(InputEvent inputEvent) =>
        HandleCardInspectionInput(_detailPortrait, inputEvent);

    private void HandleCardInspectionInput(Control returnFocus, InputEvent inputEvent)
    {
        if (!IsCardInspectionInput(inputEvent)
            || !ScheduleCurrentCardInspection(returnFocus))
        {
            return;
        }

        Root.GetViewport()?.SetInputAsHandled();
    }

    private static bool IsCardInspectionInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton mouseButton)
        {
            return !mouseButton.Pressed
                && mouseButton.ButtonIndex == MouseButton.Left;
        }

        if (inputEvent is null or InputEventKey { Echo: true })
            return false;

        return inputEvent.IsActionPressed(MegaInput.select, false, false)
            || inputEvent.IsActionPressed(MegaInput.accept, false, false);
    }

    private bool CanInspectEditorCards() =>
        !_disposed
        && !_closing
        && _viewMode == ViewMode.Editor
        && !IsValid(_modalRoot)
        && IsValid(Root)
        && Root.IsInsideTree()
        && Root.IsVisibleInTree();

    private CardInspectionRequest CreateListInspectionRequest(
        ItemList list,
        bool fromPool,
        Vector2 viewportPosition)
    {
        if (!CanUseEditorList(list) || CardInspectionService.IsBusy)
            return null;

        var localPosition = list.GetGlobalTransformWithCanvas().AffineInverse() * viewportPosition;
        var index = list.GetItemAtPosition(localPosition, true);
        var count = fromPool ? _displayedPoolCards.Count : _displayedSelectedCards.Count;
        if (index < 0 || index >= count)
            return null;

        var entries = new List<CardInspectionEntry>(count);
        var inspectionIndex = -1;
        for (var candidateIndex = 0; candidateIndex < count; candidateIndex++)
        {
            var model = fromPool
                ? _displayedPoolCards[candidateIndex]?.Model
                : _displayedSelectedCards[candidateIndex].Entry?.Model;
            if (model is null)
                continue;

            if (candidateIndex == index)
                inspectionIndex = entries.Count;
            entries.Add(new CardInspectionEntry(model));
        }
        if (inspectionIndex < 0)
            return null;

        return new CardInspectionRequest(
            Root,
            entries,
            inspectionIndex,
            list);
    }

    private CardInspectionRequest CreateCurrentCardInspectionRequest(Control returnFocus)
    {
        if (!CanInspectEditorCards() || _currentDetailCard?.Model is not CardModel model)
            return null;

        return new CardInspectionRequest(
            Root,
            [new CardInspectionEntry(model)],
            0,
            returnFocus ?? GetDetailFocusTarget(),
            ViewUpgraded: _showUpgradePreview && CanPreviewUpgrade(model));
    }

    private bool ScheduleCurrentCardInspection(Control returnFocus)
    {
        var request = CreateCurrentCardInspectionRequest(returnFocus);
        return request is not null && CardInspectionService.TrySchedule(request);
    }

    private bool IsOwnedCardInspectionVisible() =>
        CardInspectionService.IsCovering(Root);

    private Control GetDetailFocusTarget()
    {
        if (IsValid(_nativeCardPreviewHost)
            && _nativeCardPreviewHost.IsVisibleInTree())
        {
            return _nativeCardPreviewHost;
        }
        if (IsValid(_detailPortrait)
            && _detailPortrait.IsVisibleInTree()
            && _detailPortrait.Texture is not null)
        {
            return _detailPortrait;
        }
        return _detailDescription;
    }

    private void SetDetailMetadataVisible(bool visible)
    {
        _detailCost.Visible = visible;
        _detailOrigin.Visible = visible;
        _detailType.Visible = visible;
        _detailRarity.Visible = visible;
        _detailEffectTitle.Visible = visible;
        _detailDescription.Visible = visible;
    }

    private Texture2D LoadPortrait(CardEntry entry)
    {
        try
        {
            return entry.Model.Portrait;
        }
        catch (Exception exception)
        {
            if (_portraitFailures.Add(entry.Id))
            {
                MainFile.Logger.Info(
                    $"Reward-pool portrait unavailable card={entry.Id} reason={exception.Message}");
            }
            return null;
        }
    }

    private string GetDetailDescription(
        CardEntry entry,
        CardModel previewModel,
        bool showingUpgrade)
    {
        try
        {
            var description = showingUpgrade
                ? previewModel.GetDescriptionForUpgradePreview()
                : previewModel.GetDescriptionForPile(PileType.None, null);
            return string.IsNullOrWhiteSpace(description)
                ? Localize("builderDetailDescriptionUnavailable")
                : description;
        }
        catch (Exception exception)
        {
            if (_descriptionFailures.Add(entry.Id))
            {
                MainFile.Logger.Info(
                    $"Reward-pool description unavailable card={entry.Id} reason={exception.Message}");
            }
            return Localize("builderDetailDescriptionUnavailable");
        }
    }

    private static string GetDetailCostText(CardModel previewModel)
    {
        try
        {
            var cost = previewModel.EnergyCost;
            return cost.CostsX
                ? "X"
                : cost.GetWithModifiers(CostModifiers.All).ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "—";
        }
    }

    private static bool MatchesFilters(
        CardEntry entry,
        string normalizedSearchQuery,
        int originIndex,
        CardRarity? rarity,
        CardType? type)
    {
        if (normalizedSearchQuery.Length > 0
            && !entry.SearchText.Contains(normalizedSearchQuery, StringComparison.Ordinal))
            return false;
        if (originIndex > 0)
        {
            var expected = RewardPoolCatalog.MainCardOriginOrder[originIndex - 1];
            if (entry.Origin != expected)
                return false;
        }
        if (rarity is not null && entry.Rarity != rarity)
            return false;
        return type is null || entry.Type == type;
    }

    private static int CompareForDisplay(
        CardEntry left,
        CardEntry right,
        BuilderSort sort,
        bool descending)
    {
        var comparison = sort switch
        {
            BuilderSort.Name => CompareTitle(left, right),
            BuilderSort.Rarity => CompareRarity(left, right),
            BuilderSort.Type => CompareType(left, right),
            BuilderSort.Origin => CompareOrigin(left, right),
            _ => left.CatalogIndex.CompareTo(right.CatalogIndex)
        };
        if (comparison == 0)
            comparison = StringComparer.Ordinal.Compare(left.Id, right.Id);
        return descending ? -comparison : comparison;
    }

    private static int CompareTitle(CardEntry left, CardEntry right) =>
        StringComparer.CurrentCultureIgnoreCase.Compare(left.Title, right.Title);

    private static int CompareRarity(CardEntry left, CardEntry right)
    {
        var result = RarityIndex(left.Rarity).CompareTo(RarityIndex(right.Rarity));
        return result != 0 ? result : CompareTitle(left, right);
    }

    private static int CompareType(CardEntry left, CardEntry right)
    {
        var result = TypeIndex(left.Type).CompareTo(TypeIndex(right.Type));
        return result != 0 ? result : CompareTitle(left, right);
    }

    private static int CompareOrigin(CardEntry left, CardEntry right)
    {
        var result = left.OriginIndex.CompareTo(right.OriginIndex);
        return result != 0 ? result : CompareRarity(left, right);
    }

    private void PopulateSortOptions(BuilderTab tab)
    {
        _sortOrder.Clear();
        _sortOrder.AddItem(Localize("builderSortCatalog"), (int)BuilderSort.Catalog);
        _sortOrder.AddItem(Localize("builderSortName"), (int)BuilderSort.Name);
        if (tab == BuilderTab.Main)
        {
            _sortOrder.AddItem(Localize("builderSortOrigin"), (int)BuilderSort.Origin);
            _sortOrder.AddItem(Localize("builderSortRarity"), (int)BuilderSort.Rarity);
            _sortOrder.AddItem(Localize("builderSortType"), (int)BuilderSort.Type);
        }
        _sortOrder.Selected = 0;
    }

    private void UpdateSortDirectionText() =>
        _sortDirection.Text = Localize(
            _sortDescending ? "builderSortDescending" : "builderSortAscending");

    // ---------------------------------------------------------------------
    // Responsive layout and custom modal dialogs
    // ---------------------------------------------------------------------

    private void UpdateResponsiveLayout()
    {
        if (_disposed || !IsValid(Root))
            return;
        var width = Root.Size.X;
        if (width <= 1f)
            width = Root.GetViewport()?.GetVisibleRect().Size.X ?? 1920f;
        UpdateModalResponsiveLayout(width);
        UpdateConstructionLayout(width);

        if (_viewMode == ViewMode.Library)
        {
            LayoutManagerLibrary();
            return;
        }

        if (_viewMode != ViewMode.Editor
            || !IsValid(_selectedPanel)
            || !IsValid(_poolPanel)
            || !IsValid(_detailPanel))
            return;

        var compactChrome = width < 760f;
        if (IsValid(_editorHeader))
            _editorHeader.Columns = compactChrome ? 1 : 3;
        if (IsValid(_editorBottom))
            _editorBottom.Columns = width < 720f ? 1 : width < 880f ? 2 : 3;
        if (IsValid(_paneToolbar))
            _paneToolbar.Columns = width < 620f ? 1 : 3;
        if (IsValid(_presetNameEdit))
        {
            _presetNameEdit.CustomMinimumSize = new Vector2(
                width < 560f ? 0f : 250f,
                48f);
        }
        var poolToolColumns = width < 480f ? 1 : width < 700f ? 2 : 3;
        if (IsValid(_poolSearchRow))
            _poolSearchRow.Columns = poolToolColumns;
        if (IsValid(_mainFilterRow))
            _mainFilterRow.Columns = poolToolColumns;
        if (IsValid(_search))
        {
            _search.CustomMinimumSize = new Vector2(
                width < 700f ? 0f : 250f,
                48f);
        }

        var wide = width >= 1500f;
        var medium = width >= 980f && !wide;
        _paneToolbar.Visible = !wide;
        if (wide)
        {
            _selectedPanel.Visible = true;
            _poolPanel.Visible = true;
            _detailPanel.Visible = true;
        }
        else if (medium)
        {
            if (_compactPane == BuilderPane.Selected)
                _compactPane = BuilderPane.Pool;
            _selectedPanel.Visible = true;
            _poolPanel.Visible = _compactPane != BuilderPane.Detail;
            _detailPanel.Visible = _compactPane == BuilderPane.Detail;
            _showSelectedPaneButton.Visible = false;
            _showPoolPaneButton.Visible = true;
            _showDetailPaneButton.Visible = true;
        }
        else
        {
            _selectedPanel.Visible = _compactPane == BuilderPane.Selected;
            _poolPanel.Visible = _compactPane == BuilderPane.Pool;
            _detailPanel.Visible = _compactPane == BuilderPane.Detail;
            _showSelectedPaneButton.Visible = true;
            _showPoolPaneButton.Visible = true;
            _showDetailPaneButton.Visible = true;
        }

        _showSelectedPaneButton.ButtonPressed = _compactPane == BuilderPane.Selected;
        _showPoolPaneButton.ButtonPressed = _compactPane == BuilderPane.Pool;
        _showDetailPaneButton.ButtonPressed = _compactPane == BuilderPane.Detail;
        UpdateEditorListColumns();
        ScheduleEditorListColumnUpdate();
        if (IsValid(_nativeCardPreview)
            && IsValid(_nativeCardPreviewHost)
            && _nativeCardPreviewHost.IsVisibleInTree())
        {
            var card = _nativeCardPreview;
            Callable.From(() => FitNativeCardPreview(card)).CallDeferred();
        }
    }

    private void ScheduleEditorListColumnUpdate()
    {
        if (_editorColumnUpdateScheduled || _disposed)
            return;

        _editorColumnUpdateScheduled = true;
        Callable.From(() =>
        {
            _editorColumnUpdateScheduled = false;
            UpdateEditorListColumns();
        }).CallDeferred();
    }

    private void UpdateEditorListColumns()
    {
        if (_disposed || _viewMode != ViewMode.Editor)
            return;

        UpdateEditorListColumns(
            _selectedList,
            _selectedPanel,
            SelectedGridMaximumColumns);
        UpdateEditorListColumns(
            _poolList,
            _poolPanel,
            PoolGridMaximumColumns);
    }

    private static void UpdateEditorListColumns(
        ItemList list,
        Control panel,
        int maximumColumns)
    {
        if (!IsValid(list) || !IsValid(panel) || !list.IsVisibleInTree())
            return;

        var availableWidth = list.Size.X;
        if (availableWidth <= 1f)
            availableWidth = Math.Max(0f, panel.Size.X - 28f);
        if (availableWidth <= 1f)
            return;

        var usableWidth = Math.Max(
            EditorCardColumnWidth,
            availableWidth - EditorListChromeWidth);
        var columns = Math.Clamp(
            (int)Math.Floor(
                (usableWidth + EditorCardColumnSeparation)
                / (EditorCardColumnWidth + EditorCardColumnSeparation)),
            1,
            maximumColumns);
        if (list.MaxColumns != columns)
            list.MaxColumns = columns;
    }

    private void UpdateModalResponsiveLayout(float width)
    {
        if (!IsValid(_modalPanel)
            || !IsValid(_modalMessage)
            || !IsValid(_modalActions))
        {
            return;
        }

        if (width <= 1f)
            width = Root.GetViewport()?.GetVisibleRect().Size.X ?? 1920f;
        var availableWidth = Math.Max(280f, width - 40f);
        var modalWidth = Math.Min(660f, availableWidth);
        var contentWidth = Math.Max(220f, modalWidth - 70f);
        _modalPanel.CustomMinimumSize = new Vector2(modalWidth, 0f);
        _modalMessage.CustomMinimumSize = new Vector2(contentWidth, 64f);
        _modalActions.Columns = modalWidth >= 640f ? 3 : modalWidth >= 460f ? 2 : 1;
    }

    private void ShowChoiceModal(
        string title,
        string body,
        string primaryText,
        Action primaryAction,
        string secondaryText,
        Action secondaryAction,
        bool primaryIsDestructive = false)
    {
        var content = BeginModal(title, body, out var actions);
        var primary = CreateButton(primaryText, 190f);
        if (primaryIsDestructive)
            primary.Modulate = new Color(1f, 0.72f, 0.72f, 1f);
        primary.Pressed += primaryAction;
        actions.AddChild(primary);
        var secondary = CreateButton(secondaryText, 170f);
        secondary.Pressed += secondaryAction;
        actions.AddChild(secondary);
        FinishModal(content, primaryIsDestructive ? secondary : primary);
    }

    private void ShowThreeChoiceModal(
        string title,
        string body,
        string primaryText,
        Action primaryAction,
        string secondaryText,
        Action secondaryAction,
        string tertiaryText,
        Action tertiaryAction)
    {
        var content = BeginModal(title, body, out var actions);
        var primary = CreateButton(primaryText, 195f);
        primary.Pressed += primaryAction;
        actions.AddChild(primary);
        var secondary = CreateButton(secondaryText, 170f);
        secondary.Pressed += secondaryAction;
        actions.AddChild(secondary);
        var tertiary = CreateButton(tertiaryText, 180f);
        tertiary.Pressed += tertiaryAction;
        actions.AddChild(tertiary);
        FinishModal(content, primary);
    }

    private void ShowTextModal(
        string title,
        string body,
        string initialText,
        string primaryText,
        Action<string> primaryAction)
    {
        var content = BeginModal(title, body, out var actions);
        var edit = CreateLineEdit(Localize("managerNamePlaceholder"));
        edit.CustomMinimumSize = new Vector2(0f, 48f);
        edit.Text = initialText;
        edit.SelectAll();
        content.AddChild(edit);
        content.MoveChild(edit, Math.Max(0, content.GetChildCount() - 2));
        var primary = CreateButton(primaryText, 190f);
        primary.Disabled = string.IsNullOrWhiteSpace(edit.Text);
        primary.Pressed += () => primaryAction(edit.Text);
        edit.TextChanged += value => primary.Disabled = string.IsNullOrWhiteSpace(value);
        edit.TextSubmitted += value =>
        {
            if (!string.IsNullOrWhiteSpace(value))
                primaryAction(value);
        };
        actions.AddChild(primary);
        var cancel = CreateButton(Localize("managerCancel"), 170f);
        cancel.Pressed += CloseModal;
        actions.AddChild(cancel);
        FinishModal(content, edit);
    }

    private VBoxContainer BeginModal(
        string title,
        string body,
        out GridContainer actions)
    {
        var focusOwner = IsValid(_viewport) ? _viewport.GuiGetFocusOwner() : null;
        CloseModalCore(restoreFocus: false);
        _focusBeforeModal = IsFocusInside(_activeViewRoot, focusOwner) ? focusOwner : null;
        _modalRoot = new Control
        {
            MouseFilter = MouseFilterEnum.Stop,
            ZIndex = 500,
            ZAsRelative = false
        };
        _modalRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(_modalRoot);
        var shade = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.78f),
            MouseFilter = MouseFilterEnum.Stop
        };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _modalRoot.AddChild(shade);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _modalRoot.AddChild(center);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", CreateModalStyle());
        center.AddChild(panel);
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 28);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_right", 28);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 16);
        margin.AddChild(content);
        var heading = CreateLabel(title, 26, HorizontalAlignment.Left);
        heading.Modulate = PaleGold;
        content.AddChild(heading);
        var message = CreateLabel(body, 18, HorizontalAlignment.Left);
        message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        message.CustomMinimumSize = new Vector2(0f, 64f);
        content.AddChild(message);
        actions = new GridContainer
        {
            Columns = 1,
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd
        };
        actions.AddThemeConstantOverride("h_separation", 10);
        actions.AddThemeConstantOverride("v_separation", 10);
        content.AddChild(actions);
        _modalPanel = panel;
        _modalMessage = message;
        _modalActions = actions;
        UpdateModalResponsiveLayout(Root.Size.X);
        return content;
    }

    private void FinishModal(VBoxContainer content, Control defaultFocus) =>
        DeferFocus(defaultFocus);

    private void CloseModal() => CloseModalCore(restoreFocus: true);

    private void CloseModalCore(bool restoreFocus)
    {
        var previousFocus = _focusBeforeModal;
        _focusBeforeModal = null;
        if (!IsValid(_modalRoot))
        {
            _modalRoot = null;
            _modalPanel = null;
            _modalMessage = null;
            _modalActions = null;
            if (restoreFocus)
                DeferFocus(previousFocus ?? GetPreferredViewFocus());
            return;
        }
        var modal = _modalRoot;
        _modalRoot = null;
        _modalPanel = null;
        _modalMessage = null;
        _modalActions = null;
        modal.Visible = false;
        Root.RemoveChild(modal);
        modal.QueueFree();
        if (restoreFocus)
            DeferFocus(previousFocus ?? GetPreferredViewFocus());
    }

    private void OnGuiFocusChanged(Control focused)
    {
        if (_disposed
            || _closing
            || IsOwnedCardInspectionVisible()
            || !IsValid(Root)
            || !Root.IsInsideTree()
            || !Root.Visible)
        {
            return;
        }

        var scope = IsValid(_modalRoot) ? _modalRoot : _activeViewRoot;
        if (IsFocusInside(scope, focused))
            return;
        ScheduleFocusRedirect(scope);
    }

    private void ScheduleFocusRedirect(Control scope)
    {
        if (_focusRedirectScheduled)
            return;
        _focusRedirectScheduled = true;
        Callable.From(() =>
        {
            _focusRedirectScheduled = false;
            if (_disposed || _closing || IsOwnedCardInspectionVisible())
                return;

            var currentScope = IsValid(_modalRoot) ? _modalRoot : _activeViewRoot;
            if (!ReferenceEquals(scope, currentScope) || !IsValid(currentScope))
                return;
            var target = FindFirstFocusable(currentScope) ?? GetPreferredViewFocus();
            if (IsValid(target) && target.IsInsideTree() && target.IsVisibleInTree())
                target.GrabFocus();
        }).CallDeferred();
    }

    private Control GetPreferredViewFocus()
    {
        if (_viewMode == ViewMode.LibraryUnavailable)
            return IsValid(_unavailableStandardButton) ? _unavailableStandardButton : _managerCloseButton;

        if (_viewMode == ViewMode.Draft)
            return FindFirstFocusable(_activeViewRoot);

        if (_viewMode == ViewMode.Library)
        {
            var selectedTile = GetSelectedPresetTileButton();
            if (IsValid(selectedTile) && selectedTile.IsVisibleInTree())
                return selectedTile;
            if (IsValid(_createPresetTile) && _createPresetTile.IsVisibleInTree())
                return _createPresetTile;
            return _librarySearch;
        }

        var poolFocus = GetEditorPoolFocus();
        if (IsValid(poolFocus) && poolFocus.IsVisibleInTree())
            return poolFocus;
        var selectedFocus = GetEditorSelectedFocus();
        if (IsValid(selectedFocus) && selectedFocus.IsVisibleInTree())
            return selectedFocus;
        return _presetNameEdit;
    }

    private static Control FindFirstFocusable(Node node)
    {
        if (!IsValid(node))
            return null;
        foreach (var child in node.GetChildren())
        {
            if (child is not Control control || !control.IsVisibleInTree())
                continue;
            if (control.FocusMode != FocusModeEnum.None
                && (control is not BaseButton button || !button.Disabled))
            {
                return control;
            }

            var nested = FindFirstFocusable(control);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private static bool IsFocusInside(Control scope, Control focused) =>
        IsValid(scope)
        && IsValid(focused)
        && focused.IsVisibleInTree()
        && (ReferenceEquals(scope, focused) || scope.IsAncestorOf(focused));

    // ---------------------------------------------------------------------
    // UI factories, formatting, and card metadata
    // ---------------------------------------------------------------------

    private void ScheduleSearchRefresh(Action action)
    {
        if (_suppressSearchRefresh || _disposed)
            return;
        _pendingSearchRefresh = action;
        if (!IsValid(_searchRefreshTimer) || !_searchRefreshTimer.IsInsideTree())
        {
            ApplyPendingSearchRefresh();
            return;
        }
        _searchRefreshTimer.Stop();
        _searchRefreshTimer.Start();
    }

    private void ApplyPendingSearchRefresh()
    {
        if (_disposed)
            return;
        _searchRefreshTimer?.Stop();
        var action = _pendingSearchRefresh;
        _pendingSearchRefresh = null;
        action?.Invoke();
    }

    private void CancelPendingSearchRefresh()
    {
        if (IsValid(_searchRefreshTimer))
            _searchRefreshTimer.Stop();
        _pendingSearchRefresh = null;
    }

    private static CardEntry[] BuildEntries(IReadOnlyList<CardModel> cards, bool isExtra)
    {
        var fallbackOriginText = Localize("builderOriginThermalVortex");
        var originTexts = RewardPoolCatalog.MainCardOriginOrder.ToDictionary(
            origin => origin,
            origin => Localize(OriginLocalizationKey(origin)));
        var typeTexts = new Dictionary<CardType, string>
        {
            [CardType.Attack] = Localize("builderTypeAttack"),
            [CardType.Skill] = Localize("builderTypeSkill"),
            [CardType.Power] = Localize("builderTypePower")
        };
        var rarityTexts = new Dictionary<CardRarity, string>
        {
            [CardRarity.Common] = Localize("builderRarityCommon"),
            [CardRarity.Uncommon] = Localize("builderRarityUncommon"),
            [CardRarity.Rare] = Localize("builderRarityRare")
        };
        var result = new CardEntry[cards.Count];
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            var id = RewardPoolCatalog.GetId(card);
            var title = SafeTitle(card, id);
            var type = SafeType(card);
            var rarity = SafeRarity(card);
            RewardPoolCardOrigin? origin = null;
            try
            {
                if (RewardPoolCatalog.TryGetMainCardOrigin(card, out var resolvedOrigin))
                    origin = resolvedOrigin;
            }
            catch
            {
                // Unknown sources use the mod pool label.
            }
            var originText = origin is { } value
                ? originTexts.GetValueOrDefault(value, fallbackOriginText)
                : fallbackOriginText;
            var typeText = typeTexts.GetValueOrDefault(type, type.ToString());
            var rarityText = rarityTexts.GetValueOrDefault(rarity, rarity.ToString());
            result[index] = new CardEntry(
                card,
                id,
                title,
                NormalizeSearchText($"{title}\n{id}"),
                $"{title}\n{originText} · {typeText} · {rarityText}",
                originText,
                origin,
                GetOriginIndex(origin),
                type,
                typeText,
                rarity,
                rarityText,
                index,
                isExtra);
        }
        return result;
    }

    private static Dictionary<string, CardEntry> BuildEntryIndex(IEnumerable<CardEntry> entries)
    {
        var result = new Dictionary<string, CardEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
            result.TryAdd(entry.Id, entry);
        return result;
    }

    private int AddPortraitItem(ItemList list, CardEntry entry, bool selected)
    {
        var points = _viewMode == ViewMode.Editor && _constructionMethod == RewardPoolConstructionMethod.Genesis
            ? " · " + ConstructionCardPoints(entry.Id)
            : string.Empty;
        var item = list.AddItem(entry.Title + points, LoadPortrait(entry), true);
        list.SetItemTooltip(item, entry.Tooltip + (points.Length > 0 ? "\n" + points.TrimStart(' ', '·') : string.Empty));
        list.SetItemCustomBgColor(item, selected ? SelectedBackground : NormalBackground);
        list.SetItemIconModulate(item, selected ? PaleGold : Colors.White);
        return item;
    }

    private static ItemList CreatePortraitList(int maximumColumns)
    {
        var list = new ItemList
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Single,
            AllowReselect = true,
            AllowRmbSelect = true,
            AllowSearch = false,
            IconMode = ItemList.IconModeEnum.Top,
            FixedIconSize = new Vector2I(118, 84),
            IconScale = 1f,
            MaxColumns = maximumColumns,
            SameColumnWidth = true,
            FixedColumnWidth = EditorCardColumnWidth,
            MaxTextLines = 2,
            FocusMode = FocusModeEnum.All
        };
        list.AddThemeFontSizeOverride("font_size", 16);
        list.AddThemeConstantOverride("h_separation", EditorCardColumnSeparation);
        list.AddThemeStyleboxOverride("panel", CreateListStyle());
        list.AddThemeStyleboxOverride("focus", CreateFocusStyle());
        return list;
    }

    private static NMegaLineEdit CreateLineEdit(string placeholder)
    {
        var edit = new NMegaLineEdit
        {
            PlaceholderText = placeholder,
            CustomMinimumSize = new Vector2(250f, 48f),
            FocusMode = FocusModeEnum.All
        };
        edit.AddThemeFontSizeOverride("font_size", 18);
        edit.AddThemeStyleboxOverride("normal", CreateInputStyle(new Color(0.035f, 0.055f, 0.11f, 0.97f)));
        edit.AddThemeStyleboxOverride("focus", CreateInputStyle(new Color(0.055f, 0.085f, 0.16f, 0.99f), Cyan, 2));
        return edit;
    }

    private static PanelContainer CreateSectionPanel()
    {
        var panel = new PanelContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop
        };
        panel.AddThemeStyleboxOverride("panel", CreateSectionStyle());
        return panel;
    }

    private static MarginContainer CreateSectionMargin(PanelContainer panel)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        panel.AddChild(margin);
        return margin;
    }

    private static Label CreateLabel(string text, int fontSize, HorizontalAlignment alignment)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static Label CreateBadgeLabel()
    {
        var label = CreateLabel(string.Empty, 18, HorizontalAlignment.Left);
        label.CustomMinimumSize = new Vector2(0f, 38f);
        return label;
    }

    private static Label CreateDetailMetadataLabel()
    {
        var label = CreateLabel(string.Empty, 15, HorizontalAlignment.Left);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.CustomMinimumSize = new Vector2(0f, 34f);
        return label;
    }

    private static Button CreateButton(string text, float width, int fontSize = 18)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, 48f),
            FocusMode = FocusModeEnum.All,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", PaleGold);
        button.AddThemeColorOverride("font_focus_color", PaleGold);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", new Color(0.50f, 0.48f, 0.56f, 0.8f));
        button.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.12f, 0.075f, 0.19f, 0.96f), new Color(0.38f, 0.27f, 0.50f, 1f), 1));
        button.AddThemeStyleboxOverride("hover", CreateButtonStyle(new Color(0.20f, 0.10f, 0.31f, 0.98f), Gold, 2));
        button.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.055f, 0.095f, 0.18f, 0.99f), Cyan, 3));
        button.AddThemeStyleboxOverride("pressed", CreateButtonStyle(new Color(0.30f, 0.16f, 0.43f, 1f), PaleGold, 2));
        button.AddThemeStyleboxOverride("disabled", CreateButtonStyle(new Color(0.065f, 0.05f, 0.085f, 0.8f), new Color(0.20f, 0.18f, 0.24f, 0.8f), 1));
        return button;
    }

    private static OptionButton CreateOptionButton(float width)
    {
        var option = new OptionButton
        {
            CustomMinimumSize = new Vector2(width, 44f),
            FocusMode = FocusModeEnum.All
        };
        option.AddThemeFontSizeOverride("font_size", 15);
        option.AddThemeStyleboxOverride("normal", CreateButtonStyle(new Color(0.11f, 0.075f, 0.17f, 0.96f), new Color(0.34f, 0.25f, 0.44f, 1f), 1));
        option.AddThemeStyleboxOverride("hover", CreateButtonStyle(new Color(0.18f, 0.10f, 0.27f, 0.98f), Gold, 2));
        option.AddThemeStyleboxOverride("focus", CreateButtonStyle(new Color(0.055f, 0.095f, 0.18f, 0.99f), Cyan, 2));
        return option;
    }

    private static (Label Label, ProgressBar Progress) AddRarityProgress(
        GridContainer grid,
        string name)
    {
        var label = CreateLabel(name, 14, HorizontalAlignment.Left);
        grid.AddChild(label);
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        grid.AddChild(row);
        var progress = new ProgressBar
        {
            MinValue = 0,
            MaxValue = RewardPoolCatalog.MinimumPerRewardRarity,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(180f, 11f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        progress.AddThemeStyleboxOverride("background", CreateProgressStyle(new Color(0.07f, 0.05f, 0.10f, 1f)));
        progress.AddThemeStyleboxOverride("fill", CreateProgressStyle(new Color(0.60f, 0.34f, 0.88f, 1f)));
        row.AddChild(progress);
        var count = CreateLabel(string.Empty, 14, HorizontalAlignment.Right);
        count.CustomMinimumSize = new Vector2(50f, 22f);
        row.AddChild(count);
        return (count, progress);
    }

    private static StyleBoxFlat CreateOuterStyle() => new()
    {
        BgColor = new Color(0.010f, 0.023f, 0.052f, 0.985f),
        BorderColor = Gold,
        BorderWidthLeft = 3,
        BorderWidthTop = 3,
        BorderWidthRight = 3,
        BorderWidthBottom = 3,
        CornerRadiusTopLeft = 14,
        CornerRadiusTopRight = 14,
        CornerRadiusBottomLeft = 14,
        CornerRadiusBottomRight = 14,
        ShadowColor = new Color(0f, 0f, 0f, 0.78f),
        ShadowSize = 14
    };

    private static StyleBoxFlat CreateSectionStyle() => new()
    {
        BgColor = new Color(0.018f, 0.040f, 0.082f, 0.96f),
        BorderColor = new Color(0.30f, 0.28f, 0.52f, 0.96f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 10,
        CornerRadiusTopRight = 10,
        CornerRadiusBottomLeft = 10,
        CornerRadiusBottomRight = 10
    };

    private static StyleBoxFlat CreateModalStyle() => new()
    {
        BgColor = new Color(0.012f, 0.028f, 0.060f, 1f),
        BorderColor = Gold,
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 12,
        CornerRadiusTopRight = 12,
        CornerRadiusBottomLeft = 12,
        CornerRadiusBottomRight = 12,
        ShadowColor = new Color(0f, 0f, 0f, 0.85f),
        ShadowSize = 16
    };

    private static StyleBoxFlat CreateButtonStyle(Color background, Color border, int width) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = width,
        BorderWidthTop = width,
        BorderWidthRight = width,
        BorderWidthBottom = width,
        CornerRadiusTopLeft = 7,
        CornerRadiusTopRight = 7,
        CornerRadiusBottomLeft = 7,
        CornerRadiusBottomRight = 7
    };

    private static StyleBoxFlat CreatePresetTileStyle(
        Color background,
        Color border,
        int width,
        int shadowSize = 0) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = width,
        BorderWidthTop = width,
        BorderWidthRight = width,
        BorderWidthBottom = width,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 14,
        CornerRadiusBottomLeft = 14,
        CornerRadiusBottomRight = 4,
        CornerDetail = 1,
        ShadowColor = new Color(border.R, border.G, border.B, shadowSize > 0 ? 0.28f : 0f),
        ShadowSize = shadowSize
    };

    private static StyleBoxFlat CreateTileBadgeStyle(Color color) => new()
    {
        BgColor = new Color(color.R, color.G, color.B, 0.10f),
        BorderColor = new Color(color.R, color.G, color.B, 0.78f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8,
        CornerRadiusBottomRight = 3,
        CornerDetail = 1
    };

    private static StyleBoxFlat CreateInputStyle(Color background, Color? border = null, int width = 1) => new()
    {
        BgColor = background,
        BorderColor = border ?? new Color(0.36f, 0.26f, 0.47f, 1f),
        BorderWidthLeft = width,
        BorderWidthTop = width,
        BorderWidthRight = width,
        BorderWidthBottom = width,
        CornerRadiusTopLeft = 7,
        CornerRadiusTopRight = 7,
        CornerRadiusBottomLeft = 7,
        CornerRadiusBottomRight = 7
    };

    private static StyleBoxFlat CreateListStyle() => new()
    {
        BgColor = new Color(0.010f, 0.025f, 0.055f, 0.78f),
        BorderColor = new Color(0.20f, 0.22f, 0.38f, 0.92f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6
    };

    private static StyleBoxFlat CreateFocusStyle() => new()
    {
        BgColor = new Color(0.06f, 0.14f, 0.24f, 0.24f),
        BorderColor = Cyan,
        BorderWidthLeft = 2,
        BorderWidthTop = 2,
        BorderWidthRight = 2,
        BorderWidthBottom = 2,
        CornerRadiusTopLeft = 6,
        CornerRadiusTopRight = 6,
        CornerRadiusBottomLeft = 6,
        CornerRadiusBottomRight = 6
    };

    private static StyleBoxFlat CreateProgressStyle(Color color) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4
    };

    private static void AddEmptyItem(ItemList list, string text)
    {
        var item = list.AddItem(text, null, false);
        list.SetItemCustomFgColor(item, MutedColor);
    }

    private static string BuildPresetTooltip(
        RewardPoolPresetDraft preset,
        RewardPoolPresetValidation validation) =>
        $"{preset.Name}\n{preset.Id}\n{FormatRaritySummary(validation)}\n"
        + (validation.IsValid ? Localize("builderReady") : FormatValidationIssues(validation));

    private static string FormatRaritySummary(RewardPoolPresetValidation validation) =>
        LocalizeCount(
            "builderRaritySummary",
            ("Common", validation.CommonCount),
            ("Uncommon", validation.UncommonCount),
            ("Rare", validation.RareCount),
            ("Minimum", RewardPoolCatalog.MinimumPerRewardRarity));

    private static string FormatValidationIssues(RewardPoolPresetValidation validation)
    {
        if (validation.IsValid)
            return Localize("builderReady");
        if (validation.Issues is null || validation.Issues.Count == 0)
            return Localize("errorInvalid");

        var issueTypes = validation.Issues
            .Select(FormatValidationIssue)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        const int visibleIssueTypeCount = 4;
        var visible = string.Join(
            "\n",
            issueTypes.Take(visibleIssueTypeCount).Select(message => $"• {message}"));
        if (issueTypes.Length <= visibleIssueTypeCount)
            return visible;

        return visible + "\n• " + LocalizeCount(
            "moreIssues",
            ("Count", issueTypes.Length - visibleIssueTypeCount));
    }

    private static string FormatValidationIssue(RewardPoolPresetIssue issue) => issue.Code switch
    {
        RewardPoolPresetIssueCode.InvalidPresetName => Localize("errorPresetName"),
        RewardPoolPresetIssueCode.IncorrectMainCardCount => Localize("errorMainCount"),
        RewardPoolPresetIssueCode.IncorrectExtraCardCount => Localize("errorExtraCount"),
        RewardPoolPresetIssueCode.DuplicateMainCardId
            or RewardPoolPresetIssueCode.DuplicateExtraCardId => Localize("errorDuplicate"),
        RewardPoolPresetIssueCode.EmptyCardId
            or RewardPoolPresetIssueCode.UnknownMainCardId
            or RewardPoolPresetIssueCode.UnknownExtraCardId
            or RewardPoolPresetIssueCode.MainCardInWrongCatalog
            or RewardPoolPresetIssueCode.ExtraCardInWrongCatalog => Localize("errorUnknownCard"),
        RewardPoolPresetIssueCode.InsufficientCommonCards
            or RewardPoolPresetIssueCode.InsufficientUncommonCards
            or RewardPoolPresetIssueCode.InsufficientRareCards => Localize("errorRarity"),
        RewardPoolPresetIssueCode.CatalogUnavailable => Localize("errorCatalog"),
        RewardPoolPresetIssueCode.LibraryUnavailable => Localize("errorPresetLibrary"),
        RewardPoolPresetIssueCode.ConstructionBudgetExceeded => Localize("constructionOverBudget"),
        RewardPoolPresetIssueCode.InvalidConstruction => Localize("constructionInvalid"),
        _ => Localize("errorInvalid")
    };

    private static string FormatOperationError(RewardPoolPresetOperationResult result) =>
        result.Code switch
        {
            RewardPoolPresetOperationCode.InvalidPresetName => Localize("errorPresetName"),
            RewardPoolPresetOperationCode.PresetNotFound
                or RewardPoolPresetOperationCode.MissingActivePreset =>
                Localize("errorPresetMissing"),
            RewardPoolPresetOperationCode.UnsupportedLibraryVersion
                or RewardPoolPresetOperationCode.InvalidLibrary
                or RewardPoolPresetOperationCode.NoSavedLibrary
                or RewardPoolPresetOperationCode.InvalidPresetId
                or RewardPoolPresetOperationCode.DuplicatePresetId =>
                Localize("errorPresetLibrary"),
            RewardPoolPresetOperationCode.PersistenceError => Localize("errorPersistence"),
            _ => Localize("errorInvalid")
        };

    private static bool HasUnavailableCards(RewardPoolPresetValidation validation) =>
        validation?.Issues?.Any(issue => issue.Code is
            RewardPoolPresetIssueCode.EmptyCardId or
            RewardPoolPresetIssueCode.UnknownMainCardId or
            RewardPoolPresetIssueCode.UnknownExtraCardId or
            RewardPoolPresetIssueCode.MainCardInWrongCatalog or
            RewardPoolPresetIssueCode.ExtraCardInWrongCatalog) == true;

    private static string DisplayUnknownId(string id) =>
        string.IsNullOrWhiteSpace(id) ? "(∅)" : id;

    private static string SafeTitle(CardModel card, string fallbackId)
    {
        try
        {
            var title = card?.Title;
            return string.IsNullOrWhiteSpace(title) ? fallbackId : title;
        }
        catch
        {
            return fallbackId;
        }
    }

    private static CardType SafeType(CardModel card)
    {
        try
        {
            return card.Type;
        }
        catch
        {
            return (CardType)(-1);
        }
    }

    private static CardRarity SafeRarity(CardModel card)
    {
        try
        {
            return card.Rarity;
        }
        catch
        {
            return (CardRarity)(-1);
        }
    }

    private static string NormalizeSearchText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var normalized = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark)
                continue;
            if (char.IsWhiteSpace(character))
            {
                if (normalized.Length > 0 && normalized[^1] != ' ')
                    normalized.Append(' ');
                continue;
            }
            normalized.Append(char.ToUpperInvariant(character));
        }
        return normalized.ToString().TrimEnd();
    }

    private static int RarityIndex(CardRarity rarity) => rarity switch
    {
        CardRarity.Common => 0,
        CardRarity.Uncommon => 1,
        CardRarity.Rare => 2,
        _ => int.MaxValue
    };

    private static int TypeIndex(CardType type) => type switch
    {
        CardType.Attack => 0,
        CardType.Skill => 1,
        CardType.Power => 2,
        _ => int.MaxValue
    };

    private static int GetOriginIndex(RewardPoolCardOrigin? origin)
    {
        if (origin is null)
            return int.MaxValue;
        for (var index = 0; index < RewardPoolCatalog.MainCardOriginOrder.Count; index++)
        {
            if (RewardPoolCatalog.MainCardOriginOrder[index] == origin.Value)
                return index;
        }
        return int.MaxValue;
    }

    private void DeferFocus(Control control)
    {
        Callable.From(() =>
        {
            if (_disposed || IsOwnedCardInspectionVisible())
                return;

            if (IsValid(control) && control.IsInsideTree() && control.IsVisibleInTree())
            {
                control.GrabFocus();
                return;
            }

            var fallback = GetPreferredViewFocus();
            if (IsValid(fallback) && fallback.IsInsideTree() && fallback.IsVisibleInTree())
                fallback.GrabFocus();
        }).CallDeferred();
    }

    private static Color ToneColor(StatusTone tone) => tone switch
    {
        StatusTone.Success => GoodColor,
        StatusTone.Warning => WarningColor,
        StatusTone.Error => BadColor,
        _ => Colors.White
    };

    private static string OriginLocalizationKey(RewardPoolCardOrigin origin) => origin switch
    {
        RewardPoolCardOrigin.ThermalVortex => "builderOriginThermalVortex",
        RewardPoolCardOrigin.Ironclad => "builderOriginIronclad",
        RewardPoolCardOrigin.Silent => "builderOriginSilent",
        RewardPoolCardOrigin.Defect => "builderOriginDefect",
        RewardPoolCardOrigin.Necrobinder => "builderOriginNecrobinder",
        RewardPoolCardOrigin.Regent => "builderOriginRegent",
        _ => "builderOriginAll"
    };

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

    private static string LocalizeValue(string key, string name, string value)
    {
        var text = new LocString("characters", LocalizationPrefix + key);
        text.Add(name, value);
        return text.GetFormattedText();
    }

    private static bool IsValid(GodotObject value) =>
        value != null && GodotObject.IsInstanceValid(value);

    private sealed record CardEntry(
        CardModel Model,
        string Id,
        string Title,
        string SearchText,
        string Tooltip,
        string OriginText,
        RewardPoolCardOrigin? Origin,
        int OriginIndex,
        CardType Type,
        string TypeText,
        CardRarity Rarity,
        string RarityText,
        int CatalogIndex,
        bool IsExtra);

    private sealed record SelectedCardEntry(int SourceIndex, string Id, CardEntry Entry);

    private sealed record PresetTileBinding(
        LibraryTileKind Kind,
        Guid? PresetId,
        Button Button,
        bool IsActive);

    private sealed class EditorSnapshot(
        string name,
        IReadOnlyList<string> mainCardIds,
        IReadOnlyList<string> extraCardIds,
        RewardPoolConstructionMethod constructionMethod)
    {
        private string Name { get; } = name;
        private IReadOnlyList<string> MainCardIds { get; } = mainCardIds;
        private IReadOnlyList<string> ExtraCardIds { get; } = extraCardIds;
        private RewardPoolConstructionMethod ConstructionMethod { get; } = constructionMethod;

        internal static EditorSnapshot From(RewardPoolPresetDraft draft) => new(
            draft?.Name ?? string.Empty,
            draft?.MainCardIds?.ToArray() ?? [],
            draft?.ExtraCardIds?.ToArray() ?? [],
            draft?.ConstructionMethod ?? RewardPoolConstructionMethod.Free);

        internal bool ContentEquals(EditorSnapshot other) =>
            other is not null
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && ConstructionMethod == other.ConstructionMethod
            && MainCardIds.SequenceEqual(other.MainCardIds, StringComparer.Ordinal)
            && ExtraCardIds.SequenceEqual(other.ExtraCardIds, StringComparer.Ordinal);
    }

    private enum ViewMode
    {
        LibraryUnavailable,
        Library,
        Editor,
        Draft
    }

    private enum LibraryTileKind
    {
        Create,
        Standard,
        Preset,
        Draft
    }

    private enum FocusDirection
    {
        Left,
        Right,
        Top,
        Bottom,
        Previous,
        Next
    }

    private enum BuilderPane
    {
        Selected,
        Pool,
        Detail
    }

    private enum BuilderTab
    {
        Main,
        Extra
    }

    private enum BuilderSort
    {
        Catalog,
        Name,
        Rarity,
        Type,
        Origin
    }

    private enum StatusTone
    {
        Normal,
        Success,
        Warning,
        Error
    }
}
