using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using ThermalVortex.ThermalVortexCode.CardInspection;
using ThermalVortex.ThermalVortexCode.RewardPools;
using SizeFlags = Godot.Control.SizeFlags;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal sealed partial class RewardPoolBuilderSession
{
    private RewardPoolConstructionMethod _constructionMethod;
    private GridContainer _constructionToolbar;
    private GridContainer _constructionActions;
    private GridContainer _draftChoiceGrid;
    private GridContainer _draftControls;
    private Label _constructionBudget;
    private ProgressBar _constructionBudgetProgress;
    private readonly List<ConstructionCardPreviewBinding> _constructionCardPreviews = [];
    private readonly List<ConstructionBundlePreviewBinding> _constructionBundlePreviews = [];
    private readonly List<ConstructionBannerBinding> _constructionBanners = [];
    private ConstructionPackageGridBinding _constructionPackageGrid;
    private SceneTree _constructionPackageTree;
    private Label _draftProgress;
    private bool _draftOfferCommitInProgress;
    private Button _packagesButton;
    private Button _freeActionButton;
    private Button _genesisActionButton;
    private Button _draftActionButton;

    private static string ConstructionMethodName(RewardPoolConstructionMethod method) => Localize(method switch
    {
        RewardPoolConstructionMethod.Genesis => "constructionGenesis",
        RewardPoolConstructionMethod.Draft => "constructionDraft",
        RewardPoolConstructionMethod.MultiplayerDraft => "constructionMultiplayerDraft",
        _ => "constructionFree"
    });

    private void BuildConstructionActions(VBoxContainer root)
    {
        _constructionActions = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _constructionActions.AddThemeConstantOverride("h_separation", 10);
        _constructionActions.AddThemeConstantOverride("v_separation", 6);
        root.AddChild(_constructionActions);
        var free = CreateConstructionButton(Localize("managerTileNew") + " · " + Localize("constructionFree"), 0f, 17);
        _freeActionButton = free;
        free.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        BindLibraryAction(free, CreatePreset);
        _constructionActions.AddChild(free);
        var genesis = CreateConstructionButton(Localize("constructionCreateGenesis"), 0f, 17);
        _genesisActionButton = genesis;
        genesis.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        BindLibraryAction(genesis, () =>
        {
            var now = DateTime.UtcNow;
            ShowEditorView(new RewardPoolPresetDraft(Guid.Empty,
                RewardPoolPresetService.GetDefaultName(), [], [], now, now,
                RewardPoolConstructionMethod.Genesis));
        });
        _constructionActions.AddChild(genesis);
        var state = RewardPoolDraftService.GetState();
        var draft = CreateConstructionButton(Localize(state is null || (state.Stage == RewardPoolDraftStage.Consumed && !state.IsLegacy)
            ? "constructionStartDraft" : "constructionContinueDraft"), 0f, 17);
        _draftActionButton = draft;
        draft.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (RewardPoolDraftService.IsSelected)
            draft.Text += " · " + Localize("managerActive");
        BindLibraryAction(draft, OpenDraft);
        _constructionActions.AddChild(draft);
    }

    private void BuildConstructionToolbar(VBoxContainer root)
    {
        _constructionToolbar = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _constructionToolbar.AddThemeConstantOverride("h_separation", 10);
        _constructionToolbar.AddThemeConstantOverride("v_separation", 6);
        root.AddChild(_constructionToolbar);
        var method = CreateOptionButton(170f);
        method.AddItem(Localize("constructionFree"), (int)RewardPoolConstructionMethod.Free);
        method.AddItem(Localize("constructionGenesis"), (int)RewardPoolConstructionMethod.Genesis);
        method.Select(_constructionMethod == RewardPoolConstructionMethod.Genesis ? 1 : 0);
        method.ItemSelected += _ =>
        {
            _constructionMethod = (RewardPoolConstructionMethod)method.GetSelectedId();
            RefreshEditor();
        };
        _constructionToolbar.AddChild(method);
        var budget = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        budget.AddThemeConstantOverride("separation", 4);
        _constructionToolbar.AddChild(budget);
        _constructionBudget = CreateConstructionLabel(string.Empty, 17, HorizontalAlignment.Left);
        _constructionBudget.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _constructionBudget.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        budget.AddChild(_constructionBudget);
        _constructionBudgetProgress = new ProgressBar
        {
            MaxValue = RewardPoolConstructionCatalog.GenesisBudget,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0f, 8f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _constructionBudgetProgress.AddThemeStyleboxOverride("background", CreateProgressStyle(new Color(0.10f, 0.12f, 0.13f)));
        _constructionBudgetProgress.AddThemeStyleboxOverride("fill", CreateProgressStyle(Gold));
        budget.AddChild(_constructionBudgetProgress);
        _packagesButton = CreateConstructionButton(Localize("constructionPackages"), 150f, 17);
        _packagesButton.Pressed += ShowPackageBrowser;
        _constructionToolbar.AddChild(_packagesButton);
    }

    private IEnumerable<string> ConstructionIds() => _selectedMainIds.Concat(_selectedExtraIds);

    private string ConstructionCardPoints(string id) =>
        RewardPoolConstructionCatalog.TryGetPoints(id, out var points)
            ? LocalizeCount("constructionCardPoints", ("Points", points))
            : Localize("constructionUnpriced");

    private void RefreshConstructionBudget()
    {
        if (!IsValid(_constructionBudget))
            return;
        if (_constructionMethod != RewardPoolConstructionMethod.Genesis)
        {
            if (IsValid(_constructionBudgetProgress))
                _constructionBudgetProgress.Visible = false;
            _constructionBudget.Text = Localize("constructionFreeHint");
            _constructionBudget.TooltipText = string.Empty;
            _constructionBudget.Modulate = MutedColor;
            return;
        }
        var ids = ConstructionIds().ToArray();
        var priced = ids.All(id => RewardPoolConstructionCatalog.TryGetPoints(id, out _));
        var total = RewardPoolConstructionCatalog.TotalPoints(ids.Where(id => RewardPoolConstructionCatalog.TryGetPoints(id, out _)));
        if (IsValid(_constructionBudgetProgress))
        {
            _constructionBudgetProgress.Visible = true;
            _constructionBudgetProgress.Value = Math.Min(total, RewardPoolConstructionCatalog.GenesisBudget);
            _constructionBudgetProgress.TooltipText = Localize("constructionBudgetHint");
            _constructionBudgetProgress.Modulate = priced && total <= RewardPoolConstructionCatalog.GenesisBudget
                ? Colors.White : BadColor;
        }
        _constructionBudget.Text = LocalizeCount("constructionBudget",
            ("Used", total), ("Budget", RewardPoolConstructionCatalog.GenesisBudget),
            ("Remaining", RewardPoolConstructionCatalog.GenesisBudget - total));
        if (!priced)
            _constructionBudget.Text += " · " + Localize("constructionUnpriced");
        _constructionBudget.Modulate = priced && total <= RewardPoolConstructionCatalog.GenesisBudget ? GoodColor : BadColor;
        _constructionBudget.TooltipText = Localize("constructionBudgetHint");
        if (!priced || total > RewardPoolConstructionCatalog.GenesisBudget)
            _saveAndUseButton.Disabled = true;
    }

    private bool CanAddConstructionCards(IEnumerable<string> addedIds, out string error)
    {
        error = string.Empty;
        if (_constructionMethod != RewardPoolConstructionMethod.Genesis)
            return true;
        var ids = ConstructionIds().Concat(addedIds).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Any(id => !RewardPoolConstructionCatalog.TryGetPoints(id, out _)))
        {
            error = Localize("constructionUnpriced");
            return false;
        }
        if (RewardPoolConstructionCatalog.TotalPoints(ids) > RewardPoolConstructionCatalog.GenesisBudget)
        {
            error = Localize("constructionOverBudget");
            return false;
        }
        return true;
    }

    private void ShowPackageBrowser()
    {
        var allPackages = RewardPoolConstructionCatalog.Packages.ToArray();
        var packages = allPackages;
        if (packages.Length == 0)
        {
            ShowConstructionNotice(Localize("constructionPackages"), Localize("constructionNoPackages"));
            return;
        }
        var content = BeginModal(Localize("constructionPackages"), Localize("constructionPackageHint"), out var actions);
        var packageModal = _modalRoot;
        var origins = RewardPoolCatalog.MainCardOriginOrder
            .Where(origin => allPackages.Any(package => package.Origin == origin)).ToArray();
        var originChooser = CreateOptionButton(0f);
        originChooser.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        originChooser.AddItem(Localize("builderOriginAll"));
        foreach (var origin in origins)
            originChooser.AddItem(Localize(OriginLocalizationKey(origin)));
        content.AddChild(originChooser);
        var chooser = CreateOptionButton(0f);
        chooser.FitToLongestItem = false;
        chooser.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        content.AddChild(chooser);
        var summary = CreateConstructionLabel(string.Empty, 16, HorizontalAlignment.Left);
        summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(summary);
        var members = CreateConstructionCardList(content);
        IReadOnlyList<CardEntry> entries = [];
        var add = CreateConstructionButton(Localize("constructionAddPackage"), 160f, 17);
        var inspect = CreateConstructionButton(Localize("constructionInspect"), 120f, 17);
        var close = CreateConstructionButton(Localize("managerClose"), 100f, 17);
        actions.AddChild(add);
        actions.AddChild(inspect);
        actions.AddChild(close);
        content.MoveChild(actions, content.GetChildCount() - 1);
        void RefreshPackage()
        {
            if (chooser.Selected < 0 || chooser.Selected >= packages.Length)
                return;
            var package = packages[chooser.Selected];
            var ids = package.CardIds.Distinct(StringComparer.Ordinal).ToArray();
            entries = ids.Where(_mainById.ContainsKey).Select(id => _mainById[id]).ToArray();
            FillConstructionCardList(members, entries);
            var added = ids.Except(_selectedMainIds, StringComparer.Ordinal).ToArray();
            var known = added.All(id => RewardPoolConstructionCatalog.TryGetPoints(id, out _));
            var points = known ? RewardPoolConstructionCatalog.TotalPoints(added) : 0;
            summary.Text = LocalizeCount("constructionPackageSummary", ("Count", ids.Length),
                ("Selected", ids.Length - added.Length), ("Added", added.Length), ("Points", points));
            var description = FormatPackageDescription(package);
            if (!string.IsNullOrWhiteSpace(description))
                summary.Text += "\n" + description;
            var capacity = _selectedMainIds.Count + added.Length <= MainSelectionCount;
            var budget = CanAddConstructionCards(added, out var error);
            add.Disabled = added.Length == 0 || entries.Count != ids.Length || !capacity || !budget;
            if (!capacity)
                summary.Text += "\n" + Localize("constructionPackageTooLarge");
            else if (!budget)
                summary.Text += "\n" + error;
            if (!known)
                summary.Text += "\n" + Localize("constructionUnpriced");
            inspect.Disabled = entries.Count == 0;
        }
        void RefreshOrigins()
        {
            packages = allPackages.Where(package => originChooser.Selected == 0
                || package.Origin == origins[originChooser.Selected - 1]).ToArray();
            chooser.Clear();
            foreach (var package in packages)
                chooser.AddItem(Localize(OriginLocalizationKey(package.Origin)) + " · " + RewardPoolConstructionCatalog.PackageName(package));
            chooser.Select(0);
            RefreshPackage();
        }
        originChooser.ItemSelected += _ => RefreshOrigins();
        chooser.ItemSelected += _ => RefreshPackage();
        inspect.Pressed += () => InspectConstructionSelection(members, entries);
        BindConstructionInspection(members, () => entries);
        close.Pressed += CloseModal;
        add.Pressed += () =>
        {
            if (_disposed || _closing || CardInspectionService.IsBusy
                || !ReferenceEquals(_modalRoot, packageModal) || !IsValid(packageModal)
                || !packageModal.IsInsideTree() || !packageModal.IsVisibleInTree()
                || chooser.Selected < 0 || chooser.Selected >= packages.Length)
                return;
            var added = packages[chooser.Selected].CardIds.Distinct(StringComparer.Ordinal)
                .Except(_selectedMainIds, StringComparer.Ordinal).ToArray();
            if (added.Any(id => !_mainById.ContainsKey(id))
                || _selectedMainIds.Count + added.Length > MainSelectionCount
                || !CanAddConstructionCards(added, out _))
            {
                RefreshPackage();
                return;
            }
            _selectedMainIds.AddRange(added);
            CloseModal();
            RefreshEditor();
            SetEditorToast(Localize("constructionPackageAdded"), null, StatusTone.Success);
        };
        RefreshOrigins();
        FinishModal(content, originChooser);
    }

    private static string FormatPackageDescription(RewardPoolPackage package)
    {
        var lines = new List<string>();
        var synergy = RewardPoolConstructionCatalog.PackageSynergy(package);
        if (!string.IsNullOrWhiteSpace(synergy))
            lines.Add(synergy);
        if (package.Axes.Count > 0)
        {
            var chinese = TranslationServer.GetLocale().StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            lines.Add(LocalizeValue("constructionPackageAxes", "Axes",
                string.Join(" / ", package.Axes.Select(axis => chinese ? axis.NameZh : axis.NameEn))));
        }
        if (package.BridgeCardIds.Count > 0)
            lines.Add(LocalizeCount("constructionPackageBridges", ("Count", package.BridgeCardIds.Count)));
        return string.Join("\n", lines);
    }

    private ItemList CreateConstructionCardList(VBoxContainer parent)
    {
        var list = CreatePortraitList(4);
        list.CustomMinimumSize = new Vector2(0f, 230f);
        list.SizeFlagsVertical = SizeFlags.Fill;
        list.Resized += () => UpdateEditorListColumns(list, list, 4);
        parent.AddChild(list);
        return list;
    }

    private void FillConstructionCardList(ItemList list, IReadOnlyList<CardEntry> entries)
    {
        list.Clear();
        foreach (var entry in entries)
            AddPortraitItem(list, entry, _selectedMainIds.Contains(entry.Id, StringComparer.Ordinal));
        if (entries.Count > 0)
            list.Select(0);
    }

    private void BindConstructionInspection(ItemList list, Func<IReadOnlyList<CardEntry>> entries)
    {
        list.ItemActivated += _ => InspectConstructionSelection(list, entries());
        list.GuiInput += input =>
        {
            if (input is InputEventMouseButton || input is InputEventKey { Echo: true })
                return;
            if (!input.IsActionPressed(MegaInput.accept, false, false))
                return;
            InspectConstructionSelection(list, entries());
            Root.GetViewport()?.SetInputAsHandled();
        };
        CardInspectionRegistry.Register(list, Root, point =>
        {
            var index = list.GetItemAtPosition(list.GetGlobalTransformWithCanvas().AffineInverse() * point, true);
            var current = entries();
            return index < 0 || index >= current.Count ? null : new CardInspectionRequest(
                Root, current.Select(entry => new CardInspectionEntry(entry.Model)).ToArray(), index, list);
        }, () => CanInspectConstructionList(list));
    }

    private bool CanInspectConstructionList(ItemList list) =>
        !_disposed && !_closing && !CardInspectionService.IsBusy
        && IsValid(list) && list.IsInsideTree() && list.IsVisibleInTree()
        && IsValid(_modalRoot) && _modalRoot.IsAncestorOf(list);

    private void InspectConstructionSelection(ItemList list, IReadOnlyList<CardEntry> entries)
    {
        var selected = list.GetSelectedItems();
        if (selected.Length == 0)
            return;
        InspectConstructionCardAt(list, entries, selected[0]);
    }

    private void InspectConstructionCardAt(ItemList list, IReadOnlyList<CardEntry> entries, int index)
    {
        if (!CanInspectConstructionList(list) || index < 0 || index >= entries.Count)
            return;
        if (CardInspectionService.TrySchedule(new CardInspectionRequest(Root,
            entries.Select(entry => new CardInspectionEntry(entry.Model)).ToArray(), index, list)))
            Root.GetViewport()?.SetInputAsHandled();
    }

    private void ShowConstructionCards(string title, IEnumerable<string> ids, string description = null,
        Action choose = null, string optionId = null)
    {
        var cardIds = ids.ToArray();
        var entries = cardIds.Select(id => _mainById.GetValueOrDefault(id) ?? _extraById.GetValueOrDefault(id))
            .Where(entry => entry is not null).ToArray();
        var previousFocus = Root.GetViewport()?.GuiGetFocusOwner();
        CloseModalCore(restoreFocus: false);
        _focusBeforeModal = IsFocusInside(_activeViewRoot, previousFocus) ? previousFocus : null;
        var modal = new Control
        {
            Name = "ThermalVortexConstructionPackagePreview",
            MouseFilter = Control.MouseFilterEnum.Stop, ZIndex = 500, ZAsRelative = false
        };
        modal.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _modalRoot = modal;
        Root.AddChild(modal);
        var shade = new ColorRect { Color = new Color(0.025f, 0.035f, 0.045f, 0.97f), MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        modal.AddChild(shade);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 126);
        modal.AddChild(margin);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 8);
        margin.AddChild(content);
        AddConstructionBanner(content, title);
        var summary = CreateConstructionLabel(LocalizeCount("constructionCardCount", ("Count", cardIds.Length)), 18, HorizontalAlignment.Center);
        summary.Name = "ThermalVortexConstructionPreviewCount";
        summary.SetMeta("cardCount", cardIds.Length);
        if (!string.IsNullOrWhiteSpace(description))
            summary.Text += "\n" + description;
        if (entries.Length != cardIds.Length)
            summary.Text += "\n" + string.Join(" / ", cardIds.Where(id => !_mainById.ContainsKey(id) && !_extraById.ContainsKey(id)));
        summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(summary);

        var back = ResourceLoader.Load<PackedScene>("res://scenes/ui/back_button.tscn")?.Instantiate<NBackButton>();
        if (back is not null)
        {
            back.Name = "ThermalVortexConstructionPreviewBack";
            back.OffsetTop = -110f;
            back.OffsetBottom = 0f;
            back.TooltipText = Localize("builderBack");
            back.Ready += () => back.Enable();
            back.Connect(NButton.SignalName.Released, Callable.From<NButton>(_ =>
            {
                if (ReferenceEquals(_modalRoot, modal) && !_disposed && !_closing && modal.IsInsideTree()
                    && !IsOwnedCardInspectionVisible())
                    CloseModal();
            }));
            modal.AddChild(back);
        }
        else
        {
            var close = CreateConstructionButton(Localize("builderBack"), 160f);
            close.Name = "ThermalVortexConstructionPreviewBack";
            close.Pressed += () => { if (ReferenceEquals(_modalRoot, modal)) CloseModal(); };
            close.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
            close.Position = new Vector2(20f, -100f);
            modal.AddChild(close);
        }

        if (choose is not null && entries.Length == cardIds.Length)
        {
            var confirm = ResourceLoader.Load<PackedScene>("res://scenes/ui/confirm_button.tscn")?.Instantiate<NConfirmButton>();
            if (confirm is not null)
            {
                confirm.Name = "ThermalVortexConstructionPreviewChoose";
                confirm.SetMeta("offerId", optionId ?? string.Empty);
                // Browsing/inspection accept must never submit the whole package.
                confirm.OverrideHotkeys([]);
                confirm.OffsetTop = -110f;
                confirm.OffsetBottom = 0f;
                confirm.TooltipText = Localize("constructionChoose") + " · " + title;
                confirm.Ready += () => confirm.Enable();
                confirm.GetNode<Control>("Image/Icon").Visible = false;
                var text = CreateConstructionLabel(Localize("constructionChoose"), 25, HorizontalAlignment.Center);
                text.VerticalAlignment = VerticalAlignment.Center;
                text.MouseFilter = Control.MouseFilterEnum.Ignore;
                text.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                confirm.AddChild(text);
                confirm.Connect(NButton.SignalName.Released, Callable.From<NButton>(_ =>
                {
                    if (!_disposed && !_closing && ReferenceEquals(_modalRoot, modal)
                        && modal.IsInsideTree() && modal.IsVisibleInTree() && !IsOwnedCardInspectionVisible())
                        choose();
                }));
                modal.AddChild(confirm);
            }
            else
            {
                var confirmButton = CreateConstructionButton(Localize("constructionChoose"), 160f);
                confirmButton.Name = "ThermalVortexConstructionPreviewChoose";
                confirmButton.SetMeta("offerId", optionId ?? string.Empty);
                confirmButton.Pressed += () =>
                {
                    if (!_disposed && !_closing && ReferenceEquals(_modalRoot, modal)
                        && modal.IsInsideTree() && !IsOwnedCardInspectionVisible()) choose();
                };
                confirmButton.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
                confirmButton.Position = new Vector2(-180f, -100f);
                modal.AddChild(confirmButton);
            }
        }
        if (!AddConstructionPackageGrid(content, modal, entries))
        {
            var list = CreateConstructionCardList(content);
            list.SizeFlagsVertical = SizeFlags.ExpandFill;
            FillConstructionCardList(list, entries);
            BindConstructionInspection(list, () => entries);
        }
        DeferFocus(back ?? FindFirstFocusable(modal));
    }

    private void ShowConstructionNotice(string title, string body)
    {
        var content = BeginModal(title, body, out var actions);
        var close = CreateConstructionButton(Localize("managerClose"), 130f);
        close.Pressed += CloseModal;
        actions.AddChild(close);
        FinishModal(content, close);
    }

    private void OpenDraft()
    {
        var state = RewardPoolDraftService.GetState();
        if (state is not null && (state.IsLegacy || state.Stage != RewardPoolDraftStage.Consumed))
        {
            ShowDraftView();
            return;
        }
        StartNewDraft();
    }

    private void StartNewDraft()
    {
        if (!RewardPoolDraftService.TryStartNew(out var error))
        {
            ShowConstructionNotice(Localize("constructionDraft"), error);
            return;
        }
        _selectionChanged = true;
        ShowDraftView();
    }

    private void ShowDraftView()
    {
        var state = RewardPoolDraftService.GetState();
        if (state is null || (state.Stage == RewardPoolDraftStage.Consumed && !state.IsLegacy))
        {
            ShowLibraryView();
            return;
        }
        _editingDraft = null;
        _editorBaseline = null;
        _selectedMainIds.Clear();
        _selectedMainIds.AddRange(state.MainCardIds);
        _selectedExtraIds.Clear();
        _selectedExtraIds.AddRange(state.ExtraCardIds);
        _constructionMethod = RewardPoolConstructionMethod.Draft;
        _showUpgradePreview = false;
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        root.AddChild(CreateConstructionLabel(Localize("constructionDraft"), 29, HorizontalAlignment.Left));
        var progress = CreateConstructionLabel(LocalizeCount("constructionDraftProgress", ("Packages", state.PackagePicks),
            ("PackageRounds", RewardPoolDraftService.PackageRoundCount),
            ("Generic", state.GenericRoundsCompleted), ("GenericRounds", state.GenericRoundCount),
            ("Main", state.MainCardIds.Count), ("Total", state.MainCardIds.Count + RewardPoolCatalog.RequiredMainCardCount),
            ("Minimum", RewardPoolSizePolicy.MinMainTotal), ("Maximum", RewardPoolSizePolicy.MaxMainTotal),
            ("Extra", state.ExtraCardIds.Count)), 18, HorizontalAlignment.Left);
        progress.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        progress.Name = "ThermalVortexDraftProgress";
        progress.SetMeta("genericRoundCount", state.GenericRoundCount);
        progress.SetMeta("genericRoundsCompleted", state.GenericRoundsCompleted);
        progress.SetMeta("genericCardsPerRound", state.GenericCardsPerRound);
        _draftProgress = progress;
        progress.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.AddChild(progress);
        var instructions = CreateConstructionLabel(state.IsLegacy ? Localize("constructionLegacyDraftHint")
            : state.Stage == RewardPoolDraftStage.Packages
            ? LocalizeCount("constructionDraftPackageHint", ("PackageRounds", RewardPoolDraftService.PackageRoundCount))
            : state.Stage == RewardPoolDraftStage.GenericPackages
            ? LocalizeCount("constructionDraftGenericHint", ("CardsPerRound", state.GenericCardsPerRound),
                ("GenericRounds", state.GenericRoundCount),
                ("Round", Math.Min(state.GenericRoundsCompleted + 1, state.GenericRoundCount)))
            : Localize(state.Stage == RewardPoolDraftStage.Complete ? "constructionDraftCompleteHint"
                : "constructionDraftPickHint"), 17, HorizontalAlignment.Left);
        instructions.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(instructions);
        var controls = new GridContainer { Columns = 3 };
        _draftControls = controls;
        controls.AddThemeConstantOverride("h_separation", 10);
        controls.AddThemeConstantOverride("v_separation", 6);
        root.AddChild(controls);
        var back = CreateConstructionButton(Localize("builderBack"), 165f, 17);
        back.Pressed += () => ShowLibraryView();
        controls.AddChild(back);
        var chosen = CreateConstructionButton(Localize("constructionViewChosen"), 160f, 17);
        chosen.Pressed += () => ShowConstructionCards(Localize("constructionViewChosen"), state.MainCardIds.Concat(state.ExtraCardIds));
        controls.AddChild(chosen);
        var restart = CreateConstructionButton(Localize("constructionRestartDraft"), 170f, 17);
        restart.Pressed += () => ShowChoiceModal(Localize("constructionRestartDraft"), Localize("constructionRestartPrompt"),
            Localize("constructionRestartDraft"), () => { CloseModal(); StartNewDraft(); },
            Localize("managerCancel"), CloseModal, primaryIsDestructive: true);
        controls.AddChild(restart);

        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        root.AddChild(scroll);
        _draftChoiceGrid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _draftChoiceGrid.AddThemeConstantOverride("h_separation", 14);
        _draftChoiceGrid.AddThemeConstantOverride("v_separation", 12);
        scroll.AddChild(_draftChoiceGrid);
        if (state.Stage == RewardPoolDraftStage.Complete && !state.IsLegacy)
        {
            var use = CreateConstructionButton(Localize("constructionUseDraft"), 200f);
            use.Pressed += () =>
            {
                if (!RewardPoolDraftService.TrySelect(out var error))
                {
                    ShowConstructionNotice(Localize("constructionDraft"), error);
                    return;
                }
                _selectionChanged = true;
                Complete();
            };
            _draftChoiceGrid.AddChild(use);
        }
        if (state.Stage == RewardPoolDraftStage.Complete || state.IsLegacy)
        {
            var save = CreateConstructionButton(Localize("constructionSaveDraft"), 220f);
            save.Pressed += () => ShowTextModal(Localize("constructionSaveDraft"), Localize("constructionSaveDraftHint"),
                RewardPoolPresetService.GetDefaultName(), Localize("builderSave"), name =>
                {
                    if (!RewardPoolPresetService.TryCreate(name, state.MainCardIds, state.ExtraCardIds,
                        out var saved, out var error, RewardPoolConstructionMethod.Free))
                    {
                        ShowOperationError(Localize("managerCreateFailed"), error);
                        return;
                    }
                    _libraryChanged = true;
                    CloseModal();
                    ShowEditorView(saved);
                });
            _draftChoiceGrid.AddChild(save);
        }
        else
        {
            foreach (var optionId in state.OfferIds)
                AddDraftChoice(optionId, state);
        }
        ReplaceView(root, ViewMode.Draft);
        DeferFocus(FindFirstFocusable(_draftChoiceGrid) ?? back);
    }

    private void AddDraftChoice(string optionId, RewardPoolDraftState state)
    {
        var panel = CreateSectionPanel();
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _draftChoiceGrid.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        CreateSectionMargin(panel).AddChild(body);
        var package = state.Stage == RewardPoolDraftStage.Packages
            ? RewardPoolConstructionCatalog.Packages.FirstOrDefault(candidate => candidate.Id == optionId) : null;
        var themeOffer = state.Stage == RewardPoolDraftStage.Packages
            ? state.PackageOffers.FirstOrDefault(offer => offer.Id == optionId) : null;
        var generic = state.Stage == RewardPoolDraftStage.GenericPackages
            ? state.GenericPackageOffers.FirstOrDefault(offer => offer.Id == optionId) : null;
        var packageIds = themeOffer?.CardIds ?? package?.CardIds ?? generic?.CardIds;
        var entry = packageIds is not null ? null : _mainById.GetValueOrDefault(optionId) ?? _extraById.GetValueOrDefault(optionId);
        var title = package is not null ? RewardPoolConstructionCatalog.PackageName(package)
            : generic is not null ? LocalizeCount("constructionGenericPackage", ("Number", state.GenericPackageOffers.ToList().FindIndex(offer => offer.Id == optionId) + 1))
            : entry?.Title ?? optionId;
        var name = CreateConstructionLabel(title, 23, HorizontalAlignment.Center);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        if (packageIds is not null || entry is null)
            body.AddChild(name);
        else
            name.Free();
        if (packageIds is not null)
        {
            if (package is not null)
            {
                body.AddChild(CreateConstructionLabel(Localize(OriginLocalizationKey(package.Origin)), 17, HorizontalAlignment.Center));
            }
            AddConstructionBundlePreview(body, packageIds, optionId, () => OpenPackagePreview());
            var description = CreateConstructionLabel(PackageDescription(),
                16, HorizontalAlignment.Left);
            description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            body.AddChild(description);
            body.AddChild(CreateConstructionLabel(LocalizeCount("constructionCardCount", ("Count", packageIds.Count)), 17, HorizontalAlignment.Center));
        }
        else if (entry is not null)
        {
            AddConstructionCardPreview(body, entry);
        }
        var inspect = CreateConstructionButton(Localize(packageIds is not null ? "constructionViewPackage" : "constructionInspect"), 0f, 17);
        inspect.Name = "ThermalVortexDraftInspectButton";
        inspect.SetMeta("offerId", optionId);
        inspect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        inspect.Disabled = packageIds is null && entry is null;
        inspect.Pressed += () =>
        {
            if (!CanUseDraftChoice(panel, state, optionId))
                return;
            if (packageIds is not null)
                OpenPackagePreview();
            else if (entry is not null)
                CardInspectionService.TrySchedule(new CardInspectionRequest(Root, [new CardInspectionEntry(entry.Model)], 0, inspect));
        };
        body.AddChild(inspect);
        var choose = CreateConstructionButton(Localize("constructionChoose"), 0f, 19);
        choose.Name = "ThermalVortexDraftChooseButton";
        choose.SetMeta("offerId", optionId);
        choose.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        choose.Disabled = packageIds is null && entry is null;
        choose.Pressed += () =>
        {
            CommitDraftChoice(panel, state, optionId);
        };
        body.AddChild(choose);

        void OpenPackagePreview()
        {
            if (packageIds is null || !CanUseDraftChoice(panel, state, optionId))
                return;
            ShowConstructionCards(title, packageIds, PackageDescription(),
                () => CommitDraftChoice(panel, state, optionId, allowModal: true), optionId);
        }

        string PackageDescription()
        {
            if (package is null)
                return LocalizeCount("constructionGenericPackageDescription", ("Count", packageIds.Count));
            var description = RewardPoolConstructionCatalog.PackageSynergy(package);
            return state.PackageVariantsEnabled
                ? description + "\n" + Localize("constructionPackageVariationHint") : description;
        }
    }

    private bool CanUseDraftChoice(Control panel, RewardPoolDraftState shown, string optionId, bool allowModal = false)
    {
        if (_disposed || _closing || _draftOfferCommitInProgress || _viewMode != ViewMode.Draft
            || !IsValid(panel) || !panel.IsInsideTree() || !panel.IsVisibleInTree()
            || !IsValid(_activeViewRoot) || !_activeViewRoot.IsAncestorOf(panel)
            || (!allowModal && IsValid(_modalRoot)) || IsOwnedCardInspectionVisible())
            return false;
        var current = RewardPoolDraftService.GetState();
        return current is not null && current.SessionId == shown.SessionId && current.Stage == shown.Stage
            && current.PackagePicks == shown.PackagePicks
            && current.MainCardIds.SequenceEqual(shown.MainCardIds)
            && current.ExtraCardIds.SequenceEqual(shown.ExtraCardIds)
            && current.OfferIds.SequenceEqual(shown.OfferIds)
            && current.PackageVariantsEnabled == shown.PackageVariantsEnabled
            && current.PackageVariantVersion == shown.PackageVariantVersion
            && (shown.Stage != RewardPoolDraftStage.Packages || SameDraftThemeOffer(current, shown, optionId))
            && current.OfferIds.Contains(optionId, StringComparer.Ordinal);
    }

    private static bool SameDraftThemeOffer(RewardPoolDraftState current, RewardPoolDraftState shown, string optionId)
    {
        var currentOffer = current.PackageOffers.FirstOrDefault(offer => offer.Id == optionId);
        var shownOffer = shown.PackageOffers.FirstOrDefault(offer => offer.Id == optionId);
        // Historical static sessions have no offers; both resolve through the
        // unchanged catalog. New sessions must match their displayed snapshot.
        return currentOffer is null && shownOffer is null
            || currentOffer is not null && shownOffer is not null
                && currentOffer.CardIds.SequenceEqual(shownOffer.CardIds);
    }

    private void CommitDraftChoice(Control panel, RewardPoolDraftState shown, string optionId, bool allowModal = false)
    {
        if (!CanUseDraftChoice(panel, shown, optionId, allowModal))
            return;
        _draftOfferCommitInProgress = true;
        try
        {
            if (!RewardPoolDraftService.TryChoose(optionId, out var error))
            {
                ShowConstructionNotice(Localize("constructionDraft"), error);
                return;
            }
            _selectionChanged = true;
            ShowDraftView();
        }
        finally { _draftOfferCommitInProgress = false; }
    }

    private void AddConstructionCardPreview(VBoxContainer body, CardEntry entry)
    {
        var host = new Control
        {
            Name = "ThermalVortexDraftCardPreview",
            CustomMinimumSize = new Vector2(0f, 370f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = Localize("builderDetailInspectHint"),
            ClipContents = true
        };
        body.AddChild(host);
        var fallback = new VBoxContainer { Visible = false };
        body.AddChild(fallback);
        NCard card = null;
        try
        {
            var model = CreateDetailPreviewModel(entry, out _);
            card = NCard.Create(model, ModelVisibility.Visible);
            if (!IsValid(card))
                throw new InvalidOperationException("The native card preview is unavailable.");
            card.MouseFilter = Control.MouseFilterEnum.Ignore;
            card.FocusMode = Control.FocusModeEnum.None;
            host.AddChild(card);
            var binding = new ConstructionCardPreviewBinding(card, host);
            _constructionCardPreviews.Add(binding);
            binding.ReadyHandler = () =>
            {
                if (!_constructionCardPreviews.Contains(binding) || !IsValid(card))
                    return;
                try
                {
                    // New cards do not have their native child bindings until _Ready.
                    card.SetPretendCardCanBePlayed(true);
                    card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                    binding.VisualsReady = true;
                    FitConstructionCardPreview(binding);
                    Callable.From(() => FitConstructionCardPreview(binding)).CallDeferred();
                }
                catch (Exception exception)
                {
                    MainFile.Logger.Info($"Draft native card preview failed card={entry.Id} reason={exception.Message}");
                    ReleaseConstructionCardPreview(binding);
                    host.Visible = false;
                    fallback.Visible = true;
                    BuildConstructionCardFallback(fallback, entry);
                }
            };
            binding.ResizeHandler = () => FitConstructionCardPreview(binding);
            binding.InputHandler = input =>
            {
                if (!CanInspectConstructionCard(binding) || !IsCardInspectionInput(input))
                    return;
                CardInspectionService.TrySchedule(new CardInspectionRequest(Root,
                    [new CardInspectionEntry(entry.Model)], 0, host));
                host.AcceptEvent();
            };
            host.Ready += binding.ReadyHandler;
            host.Resized += binding.ResizeHandler;
            host.GuiInput += binding.InputHandler;
            CardInspectionRegistry.Register(host, Root,
                _ => new CardInspectionRequest(Root, [new CardInspectionEntry(entry.Model)], 0, host),
                () => CanInspectConstructionCard(binding));
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info($"Draft native card preview unavailable card={entry.Id} reason={exception.Message}");
            if (IsValid(card))
            {
                var binding = _constructionCardPreviews.FirstOrDefault(candidate => ReferenceEquals(candidate.Card, card));
                if (binding is not null)
                    ReleaseConstructionCardPreview(binding);
                else
                {
                    card.GetParent()?.RemoveChild(card);
                    if (card.IsNodeReady())
                        NodePool.Free(card);
                    else
                        card.QueueFree();
                }
            }
            host.Visible = false;
            fallback.Visible = true;
            BuildConstructionCardFallback(fallback, entry);
        }
    }

    private bool CanInspectConstructionCard(ConstructionCardPreviewBinding binding) =>
        !_disposed && !_closing && _viewMode == ViewMode.Draft && !IsValid(_modalRoot)
        && binding.VisualsReady && _constructionCardPreviews.Contains(binding)
        && IsValid(binding.Host) && binding.Host.IsVisibleInTree();

    private void FitConstructionCardPreview(ConstructionCardPreviewBinding binding)
    {
        if (_disposed || !binding.VisualsReady || !_constructionCardPreviews.Contains(binding)
            || !IsValid(binding.Card) || !IsValid(binding.Host) || !binding.Host.IsInsideTree())
            return;
        var card = binding.Card;
        // GetCurrentSize includes Scale; measuring at the previous scale causes drift.
        card.Scale = Vector2.One;
        var cardSize = card.GetCurrentSize();
        var available = binding.Host.Size - new Vector2(20f, 16f);
        if (cardSize.X <= 1f || cardSize.Y <= 1f || available.X <= 1f || available.Y <= 1f)
            return;
        var scale = Math.Clamp(Math.Min(available.X / cardSize.X, available.Y / cardSize.Y), 0.10f, 0.86f);
        card.Scale = Vector2.One * scale;
        card.Position = binding.Host.Size * 0.5f;
    }

    private void ReleaseConstructionCardPreviews(Control view = null)
    {
        foreach (var binding in _constructionBundlePreviews.ToArray())
            if (view is null || (IsValid(binding.Host) && view.IsAncestorOf(binding.Host)))
                ReleaseConstructionBundlePreview(binding);
        foreach (var binding in _constructionBanners.ToArray())
            if (view is null || (IsValid(binding.Host) && view.IsAncestorOf(binding.Host)))
                ReleaseConstructionBanner(binding);
        if (_constructionPackageGrid is { } gridBinding
            && (view is null || ReferenceEquals(gridBinding.OwnerView, view)))
            ReleaseConstructionPackageGrid(gridBinding);
        foreach (var binding in _constructionCardPreviews.ToArray())
        {
            if (view is not null && IsValid(binding.Host) && !view.IsAncestorOf(binding.Host))
                continue;
            ReleaseConstructionCardPreview(binding);
        }
    }

    private void AddConstructionBundlePreview(VBoxContainer body, IReadOnlyList<string> ids, string optionId, Action inspect)
    {
        var entries = ids.Select(id => _mainById.GetValueOrDefault(id) ?? _extraById.GetValueOrDefault(id))
            .Where(entry => entry is not null).ToArray();
        var host = new Control
        {
            Name = "ThermalVortexDraftPackageCover", CustomMinimumSize = new Vector2(0f, 300f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        host.SetMeta("offerId", optionId);
        host.SetMeta("cardCount", ids.Count);
        body.AddChild(host);
        // The native fan is designed for a few cards. This is a cover only;
        // the service offer and the separate browser always retain every ID.
        var representatives = entries.Take(3).Select(entry => (CardModel)entry.Model.MutableClone()).ToArray();
        if (representatives.Length < 2)
        {
            var fallback = CreateConstructionLabel(string.Join("\n", entries.Select(entry => entry.Title)), 20, HorizontalAlignment.Center);
            fallback.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            fallback.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            host.AddChild(fallback);
            return;
        }
        NCardBundle bundle = null;
        try
        {
            bundle = NCardBundle.Create(representatives);
            if (!IsValid(bundle))
                throw new InvalidOperationException("The native package cover is unavailable.");
            bundle.Name = "ThermalVortexDraftNativeBundle";
            bundle.SetMeta("offerId", optionId);
            bundle.SetMeta("cardCount", ids.Count);
            var wrapper = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            host.AddChild(wrapper);
            var binding = new ConstructionBundlePreviewBinding(bundle, host, wrapper);
            binding.ClickedHandler = _ =>
            {
                if (!_disposed && !_closing && _constructionBundlePreviews.Contains(binding)
                    && host.IsInsideTree() && host.IsVisibleInTree() && !IsValid(_modalRoot)
                    && IsValid(_activeViewRoot) && _activeViewRoot.IsAncestorOf(host))
                    inspect();
            };
            binding.ReadyHandler = () => FitConstructionBundlePreview(binding);
            binding.ResizeHandler = () => FitConstructionBundlePreview(binding);
            binding.ExitHandler = () => ReleaseConstructionBundlePreview(binding);
            _constructionBundlePreviews.Add(binding);
            bundle.Clicked += binding.ClickedHandler;
            host.Ready += binding.ReadyHandler;
            host.Resized += binding.ResizeHandler;
            host.TreeExiting += binding.ExitHandler;
            wrapper.AddChild(bundle);
            if (host.IsNodeReady())
                FitConstructionBundlePreview(binding);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Draft native package cover unavailable reason=" + exception.Message);
            if (IsValid(bundle))
            {
                var binding = _constructionBundlePreviews.FirstOrDefault(item => ReferenceEquals(item.Bundle, bundle));
                if (binding is not null) ReleaseConstructionBundlePreview(binding);
                bundle.QueueFree();
            }
            var fallback = CreateConstructionLabel(string.Join("\n", entries.Take(3).Select(entry => entry.Title)), 20, HorizontalAlignment.Center);
            fallback.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            host.AddChild(fallback);
        }
    }

    private void FitConstructionBundlePreview(ConstructionBundlePreviewBinding binding)
    {
        if (_disposed || !_constructionBundlePreviews.Contains(binding) || !IsValid(binding.Host)
            || !IsValid(binding.Wrapper) || !binding.Host.IsInsideTree())
            return;
        // Keep the bundle's native 0.8/0.85 focus scale. Fit its outer wrapper,
        // including the native fan and hitbox, rather than overwriting hover.
        var available = binding.Host.Size - new Vector2(16f, 12f);
        var scale = Math.Clamp(Math.Min(available.X / (430f * 0.85f), available.Y / (590f * 0.85f)), 0.1f, 1f);
        binding.Wrapper.Scale = Vector2.One * scale;
        binding.Wrapper.Position = binding.Host.Size * 0.5f;
    }

    private void ReleaseConstructionBundlePreview(ConstructionBundlePreviewBinding binding)
    {
        if (!_constructionBundlePreviews.Remove(binding)) return;
        if (IsValid(binding.Bundle)) binding.Bundle.Clicked -= binding.ClickedHandler;
        if (!IsValid(binding.Host)) return;
        binding.Host.Ready -= binding.ReadyHandler;
        binding.Host.Resized -= binding.ResizeHandler;
        binding.Host.TreeExiting -= binding.ExitHandler;
        // NCardBundle owns its cover cards and releases them in _ExitTree.
    }

    private void AddConstructionBanner(VBoxContainer parent, string title)
    {
        var host = new Control { CustomMinimumSize = new Vector2(0f, 112f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(host);
        var banner = ResourceLoader.Load<PackedScene>("res://scenes/ui/common_banner.tscn")?.Instantiate<NCommonBanner>();
        if (banner is null)
        {
            var label = CreateConstructionLabel(title, 29, HorizontalAlignment.Center);
            label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            host.AddChild(label);
            return;
        }
        var binding = new ConstructionBannerBinding(host, banner);
        binding.ReadyHandler = () =>
        {
            banner.GetNode<MegaLabel>("%Label").SetTextAutoSize(title);
            FitConstructionBanner(binding);
        };
        binding.ResizeHandler = () => Callable.From(() => FitConstructionBanner(binding)).CallDeferred();
        binding.ExitHandler = () => ReleaseConstructionBanner(binding);
        _constructionBanners.Add(binding);
        banner.Ready += binding.ReadyHandler;
        host.Resized += binding.ResizeHandler;
        host.TreeExiting += binding.ExitHandler;
        host.AddChild(banner);
    }

    private void FitConstructionBanner(ConstructionBannerBinding binding)
    {
        if (_disposed || !_constructionBanners.Contains(binding) || !IsValid(binding.Host) || !IsValid(binding.Banner)) return;
        var scale = Math.Clamp(Math.Min(binding.Host.Size.X / 654f, binding.Host.Size.Y / 162f), 0.1f, 1f);
        binding.Banner.Scale = Vector2.One * scale;
        binding.Banner.Position = (binding.Host.Size - new Vector2(654f, 162f) * scale) * 0.5f;
    }

    private void ReleaseConstructionBanner(ConstructionBannerBinding binding)
    {
        if (!_constructionBanners.Remove(binding)) return;
        if (IsValid(binding.Banner)) binding.Banner.Ready -= binding.ReadyHandler;
        if (!IsValid(binding.Host)) return;
        binding.Host.Resized -= binding.ResizeHandler;
        binding.Host.TreeExiting -= binding.ExitHandler;
    }

    private bool AddConstructionPackageGrid(VBoxContainer parent, Control modal, IReadOnlyList<CardEntry> entries)
    {
        NCardGrid grid = null;
        try
        {
            grid = ResourceLoader.Load<PackedScene>("res://scenes/cards/card_grid.tscn")?.Instantiate<NCardGrid>();
            if (grid is null) return false;
            grid.Name = "ThermalVortexConstructionPackageGrid";
            grid.SetMeta("cardCount", entries.Count);
            grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            grid.SizeFlagsVertical = SizeFlags.ExpandFill;
            grid.CustomMinimumSize = new Vector2(330f, 240f);
            grid.ClipContents = true;
            grid.YOffset = 0;
            var scroll = grid.GetNode<Control>("%ScrollContainer");
            scroll.AnchorLeft = 0f;
            scroll.AnchorRight = 1f;
            scroll.OffsetLeft = 40f;
            scroll.OffsetRight = -44f;
            scroll.ClipChildren = CanvasItem.ClipChildrenMode.Disabled;
            scroll.ClipContents = false;
            var scrollbar = grid.GetNode<Control>("Scrollbar");
            scrollbar.OffsetLeft = -28f;
            scrollbar.OffsetRight = -8f;
            scrollbar.OffsetTop = 16f;
            scrollbar.OffsetBottom = -16f;
            var empty = CreateConstructionLabel(Localize("builderNoResults"), 18, HorizontalAlignment.Center);
            empty.Name = "EmptyState";
            empty.Visible = entries.Count == 0;
            empty.VerticalAlignment = VerticalAlignment.Center;
            empty.MouseFilter = Control.MouseFilterEnum.Ignore;
            empty.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            grid.AddChild(empty);
            var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            grid.AddChild(overlay);
            var layout = new NativeEditorGridBinding(grid, false, empty, overlay)
            {
                PendingCards = true, PendingFocus = entries.Count > 0
            };
            foreach (var entry in entries)
            {
                var model = (CardModel)entry.Model.MutableClone();
                layout.Models.Add(model);
                layout.Entries[model] = new NativeEditorGridEntry(entry, -1);
                if (_selectedMainIds.Contains(entry.Id, StringComparer.Ordinal)
                    || _selectedExtraIds.Contains(entry.Id, StringComparer.Ordinal))
                    layout.HighlightedModels.Add(model);
            }
            var binding = new ConstructionPackageGridBinding(grid, modal, _activeViewRoot, layout);
            binding.ReadyHandler = () =>
            {
                if (!ReferenceEquals(_constructionPackageGrid, binding) || !IsValid(grid)) return;
                layout.Ready = true;
                _constructionPackageTree = Root.GetTree();
                if (IsValid(_constructionPackageTree))
                    _constructionPackageTree.ProcessFrame += UpdateConstructionPackageGrid;
                UpdateConstructionPackageGrid();
            };
            binding.PressedHandler = holder => InspectConstructionGridHolder(binding, holder);
            binding.AltPressedHandler = holder => InspectConstructionGridHolder(binding, holder);
            binding.ExitHandler = () => ReleaseConstructionPackageGrid(binding);
            _constructionPackageGrid = binding;
            grid.Ready += binding.ReadyHandler;
            grid.HolderPressed += binding.PressedHandler;
            grid.HolderAltPressed += binding.AltPressedHandler;
            grid.TreeExiting += binding.ExitHandler;
            CardInspectionRegistry.Register(grid, Root, point =>
            {
                if (!CanUseConstructionPackageGrid(binding)) return null;
                var holder = grid.CurrentlyDisplayedCardHolders.FirstOrDefault(candidate =>
                    CanUseConstructionPackageGrid(binding, candidate)
                    && CardInspectionRegistry.GetCardFace(candidate.CardNode).GetGlobalRect().HasPoint(point));
                return CreateConstructionGridInspection(binding, holder);
            }, () => CanUseConstructionPackageGrid(binding));
            parent.AddChild(grid);
            return true;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Construction native package grid unavailable reason=" + exception.Message);
            if (_constructionPackageGrid is { } binding && ReferenceEquals(binding.Grid, grid))
                ReleaseConstructionPackageGrid(binding);
            if (IsValid(grid)) { grid.GetParent()?.RemoveChild(grid); grid.QueueFree(); }
            return false;
        }
    }

    private bool CanUseConstructionPackageGrid(ConstructionPackageGridBinding binding, NCardHolder holder = null) =>
        !_disposed && !_closing && !IsOwnedCardInspectionVisible()
        && ReferenceEquals(_constructionPackageGrid, binding) && ReferenceEquals(_modalRoot, binding.Modal)
        && IsValid(binding.Grid) && binding.Grid.IsInsideTree() && binding.Grid.IsVisibleInTree()
        && (holder is null || (IsValid(holder) && !holder.IsQueuedForDeletion()
            && holder.IsInsideTree() && holder.IsVisibleInTree() && binding.Grid.IsAncestorOf(holder)
            && holder.CardModel is { } model && binding.Layout.Entries.ContainsKey(model)));

    private CardInspectionRequest CreateConstructionGridInspection(ConstructionPackageGridBinding binding, NCardHolder holder)
    {
        if (holder is null || !CanUseConstructionPackageGrid(binding, holder)) return null;
        var index = binding.Layout.Models.FindIndex(model => ReferenceEquals(model, holder.CardModel));
        return index < 0 ? null : new CardInspectionRequest(Root,
            binding.Layout.Models.Select(model => new CardInspectionEntry(model)).ToArray(), index, holder);
    }

    private void InspectConstructionGridHolder(ConstructionPackageGridBinding binding, NCardHolder holder)
    {
        var request = CreateConstructionGridInspection(binding, holder);
        if (request is not null) CardInspectionService.TrySchedule(request);
    }

    private void UpdateConstructionPackageGrid()
    {
        var binding = _constructionPackageGrid;
        if (binding is null || _disposed || _closing || !binding.Layout.Ready
            || !IsValid(binding.Grid) || !binding.Grid.IsInsideTree() || !ReferenceEquals(_modalRoot, binding.Modal)) return;
        var layout = binding.Layout;
        // These existing layout helpers already preserve focus through native
        // reflow and restore each pooled card's temporary face scale on exit.
        FitNativeGridViewport(layout);
        if (layout.PendingCards && binding.Grid.GetNode<Control>("%ScrollContainer").Size.X >= 240f)
        {
            layout.PendingCards = false;
            binding.Grid.SetCards(layout.Models, PileType.None, [SortingOrders.Ascending], null);
            layout.PendingHighlightReplay = true;
        }
        var holders = binding.Grid.CurrentlyDisplayedCardHolders.Where(holder => IsValid(holder)
            && !holder.IsQueuedForDeletion() && layout.Entries.ContainsKey(holder.CardModel)).ToArray();
        foreach (var holder in holders) FitNativeGridCardFace(layout, holder.CardNode);
        if (layout.PendingHighlightReplay && !layout.PendingCards && !binding.Grid.IsAnimatingOut
            && (holders.Length > 0 || layout.Models.Count == 0))
        {
            foreach (var holder in holders)
                if (layout.HighlightedModels.Contains(holder.CardModel))
                {
                    binding.Grid.UnhighlightCard(holder.CardModel);
                    binding.Grid.HighlightCard(holder.CardModel);
                }
            layout.PendingHighlightReplay = false;
        }
        if (layout.PendingFocus && CanUseConstructionPackageGrid(binding))
        {
            var target = holders.FirstOrDefault(holder => ReferenceEquals(holder.CardModel, layout.PreferredFocusModel))
                ?? holders.FirstOrDefault(holder => CanUseConstructionPackageGrid(binding, holder));
            if (target is not null && (layout.ReflowFocusHolder is null || !ReferenceEquals(target, layout.ReflowFocusHolder)))
            {
                layout.PendingFocus = false;
                ClearNativeReflowFocusWait(layout);
                target.GrabFocus();
            }
            else if (layout.Models.Count == 0)
            {
                layout.PendingFocus = false;
                ClearNativeReflowFocusWait(layout);
            }
        }
        foreach (var banner in _constructionBanners.Where(item => IsValid(item.Host) && binding.Modal.IsAncestorOf(item.Host)))
            FitConstructionBanner(banner);
    }

    private void ReleaseConstructionPackageGrid(ConstructionPackageGridBinding binding)
    {
        if (binding.Released) return;
        binding.Released = true;
        if (ReferenceEquals(_constructionPackageGrid, binding)) _constructionPackageGrid = null;
        if (IsValid(_constructionPackageTree)) _constructionPackageTree.ProcessFrame -= UpdateConstructionPackageGrid;
        _constructionPackageTree = null;
        ClearNativeReflowFocusWait(binding.Layout);
        foreach (var card in binding.Layout.ScaledCards.Keys.ToArray()) RestoreNativeGridCardScale(binding.Layout, card);
        if (IsValid(binding.Grid))
        {
            CardInspectionRegistry.Unregister(binding.Grid);
            binding.Grid.Ready -= binding.ReadyHandler;
            binding.Grid.HolderPressed -= binding.PressedHandler;
            binding.Grid.HolderAltPressed -= binding.AltPressedHandler;
            binding.Grid.TreeExiting -= binding.ExitHandler;
        }
        binding.Layout.Ready = false;
        binding.Layout.PendingCards = false;
        binding.Layout.PendingFocus = false;
        binding.Layout.Entries.Clear();
        binding.Layout.Models.Clear();
        binding.Layout.HighlightedModels.Clear();
        binding.Layout.Overlay.QueueFree();
        // The native grid owns pooled holders/cards; its _ExitTree returns them.
    }

    private void ReleaseConstructionCardPreview(ConstructionCardPreviewBinding binding)
    {
        _constructionCardPreviews.Remove(binding);
        binding.VisualsReady = false;
        if (IsValid(binding.Host))
        {
            CardInspectionRegistry.Unregister(binding.Host);
            if (binding.ReadyHandler is not null)
                binding.Host.Ready -= binding.ReadyHandler;
            if (binding.ResizeHandler is not null)
                binding.Host.Resized -= binding.ResizeHandler;
            if (binding.InputHandler is not null)
                binding.Host.GuiInput -= binding.InputHandler;
        }
        if (!IsValid(binding.Card))
            return;
        try
        {
            binding.Card.GetParent()?.RemoveChild(binding.Card);
            if (binding.Card.IsNodeReady())
                NodePool.Free(binding.Card);
            else
                binding.Card.QueueFree();
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Draft native card preview return failed reason=" + exception.Message);
            if (IsValid(binding.Card) && !binding.Card.IsQueuedForDeletion())
                binding.Card.QueueFree();
        }
    }

    private void BuildConstructionCardFallback(VBoxContainer body, CardEntry entry)
    {
        body.AddChild(CreateConstructionLabel(entry.Title, 23, HorizontalAlignment.Center));
        var portrait = new TextureRect { Texture = LoadPortrait(entry), CustomMinimumSize = new Vector2(0f, 170f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
        body.AddChild(portrait);
        var description = CreateConstructionLabel(entry.Tooltip, 16, HorizontalAlignment.Left);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(description);
        var model = CreateDetailPreviewModel(entry, out _);
        body.AddChild(CreateConstructionLabel(LocalizeValue(entry.IsExtra ? "builderDetailMaterials" : "builderDetailCost",
            entry.IsExtra ? "Materials" : "Cost", GetDetailCostText(model)), 16, HorizontalAlignment.Left));
        var effect = new MegaRichTextLabel
        {
            BbcodeEnabled = true, FitContent = false, ScrollActive = true, AutoSizeEnabled = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0f, 150f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.All
        };
        effect.AddThemeFontOverride("normal_font", ThemeDB.FallbackFont);
        effect.AddThemeFontSizeOverride("normal_font_size", 18);
        body.AddChild(effect);
        effect.SetTextAutoSize(GetDetailDescription(entry, model, false));
    }

    private static Label CreateConstructionLabel(string text, int fontSize, HorizontalAlignment alignment)
    {
        var label = CreateLabel(text, fontSize, alignment);
        ApplyConstructionFont(label);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.965f, 0.886f));
        label.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.12f, 0.13f));
        label.AddThemeConstantOverride("outline_size", 3);
        return label;
    }

    private static Button CreateConstructionButton(string text, float width, int fontSize = 18)
    {
        var button = CreateButton(text, width, fontSize);
        ApplyConstructionFont(button);
        const string texturePath = "res://images/ui/reward_screen/reward_item_button.png";
        var texture = ResourceLoader.Exists(texturePath) ? ResourceLoader.Load<Texture2D>(texturePath) : null;
        if (texture is null)
            return button;
        foreach (var (state, tint) in new[]
        {
            ("normal", new Color(0.84f, 0.84f, 0.84f)), ("hover", Colors.White),
            ("pressed", new Color(0.70f, 0.70f, 0.70f)), ("disabled", new Color(0.48f, 0.48f, 0.48f, 0.8f))
        })
        {
            button.AddThemeStyleboxOverride(state, new StyleBoxTexture
            {
                Texture = texture, ModulateColor = tint,
                TextureMarginLeft = 24f, TextureMarginRight = 24f,
                TextureMarginTop = 14f, TextureMarginBottom = 14f,
                ContentMarginLeft = 18f, ContentMarginRight = 18f,
                ContentMarginTop = 6f, ContentMarginBottom = 6f
            });
        }
        button.AddThemeStyleboxOverride("focus", CreateFocusStyle());
        return button;
    }

    private static void ApplyConstructionFont(Control control)
    {
        const string fontPath = "res://themes/kreon_bold_glyph_space_one.tres";
        if (ResourceLoader.Exists(fontPath))
        {
            var font = ResourceLoader.Load<Font>(fontPath);
            if (font is not null)
                control.AddThemeFontOverride("font", font);
        }
        FontControlUtils.ApplyLocaleFontSubstitution(control, FontType.Bold, "font");
    }

    private sealed class ConstructionCardPreviewBinding(NCard card, Control host)
    {
        internal NCard Card { get; } = card;
        internal Control Host { get; } = host;
        internal Action ReadyHandler { get; set; }
        internal Action ResizeHandler { get; set; }
        internal Control.GuiInputEventHandler InputHandler { get; set; }
        internal bool VisualsReady { get; set; }
    }

    private sealed class ConstructionBundlePreviewBinding(NCardBundle bundle, Control host, Control wrapper)
    {
        public NCardBundle Bundle { get; } = bundle;
        public Control Host { get; } = host;
        public Control Wrapper { get; } = wrapper;
        public NCardBundle.ClickedEventHandler ClickedHandler { get; set; }
        public Action ReadyHandler { get; set; }
        public Action ResizeHandler { get; set; }
        public Action ExitHandler { get; set; }
    }

    private sealed class ConstructionBannerBinding(Control host, NCommonBanner banner)
    {
        public Control Host { get; } = host;
        public NCommonBanner Banner { get; } = banner;
        public Action ReadyHandler { get; set; }
        public Action ResizeHandler { get; set; }
        public Action ExitHandler { get; set; }
    }

    private sealed class ConstructionPackageGridBinding(NCardGrid grid, Control modal, Control ownerView,
        NativeEditorGridBinding layout)
    {
        public NCardGrid Grid { get; } = grid;
        public Control Modal { get; } = modal;
        public Control OwnerView { get; } = ownerView;
        public NativeEditorGridBinding Layout { get; } = layout;
        public Action ReadyHandler { get; set; }
        public NCardGrid.HolderPressedEventHandler PressedHandler { get; set; }
        public NCardGrid.HolderAltPressedEventHandler AltPressedHandler { get; set; }
        public Action ExitHandler { get; set; }
        public bool Released { get; set; }
    }

    private void UpdateConstructionLayout(float width)
    {
        if (_viewMode == ViewMode.Library && IsValid(_constructionActions))
            _constructionActions.Columns = width < 700f ? 1 : 3;
        if (_viewMode == ViewMode.Editor && IsValid(_constructionToolbar))
            _constructionToolbar.Columns = width < 760f ? 1 : 3;
        if (_viewMode == ViewMode.Draft && IsValid(_draftChoiceGrid))
            _draftChoiceGrid.Columns = width < 760f ? 1 : 3;
        if (_viewMode == ViewMode.Draft && IsValid(_draftControls))
            _draftControls.Columns = width < 650f ? 1 : 3;
    }

    private void ConfigureConstructionActionFocus()
    {
        if (!IsValid(_freeActionButton) || !IsValid(_genesisActionButton) || !IsValid(_draftActionButton))
            return;
        var firstTile = _presetTiles.Select(tile => tile.Button).FirstOrDefault(IsValid);
        var stacked = _constructionActions.Columns == 1;
        SetFocusNeighbors(_freeActionButton, _freeActionButton, _genesisActionButton,
            _managerCloseButton, stacked ? _genesisActionButton : (Control)firstTile ?? _librarySearch,
            _managerCloseButton, _genesisActionButton);
        SetFocusNeighbors(_genesisActionButton, _freeActionButton, _draftActionButton,
            stacked ? _freeActionButton : _managerCloseButton,
            stacked ? _draftActionButton : (Control)firstTile ?? _librarySearch, _freeActionButton, _draftActionButton);
        SetFocusNeighbors(_draftActionButton, _genesisActionButton, _draftActionButton,
            stacked ? _genesisActionButton : _managerCloseButton,
            (Control)firstTile ?? _librarySearch, _genesisActionButton, (Control)firstTile ?? _librarySearch);
        SetFocusNeighbor(_managerCloseButton, FocusDirection.Next, _freeActionButton);
        SetFocusNeighbor(_managerCloseButton, FocusDirection.Bottom, _freeActionButton);
    }
}
