using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using ThermalVortex.ThermalVortexCode.CardInspection;
using ThermalVortex.ThermalVortexCode.RewardPools;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal sealed partial class RewardPoolBuilderSession
{
    private NCardGrid _poolCardGrid;
    private NCardGrid _selectedCardGrid;
    private readonly List<NativeEditorGridBinding> _nativeGridBindings = [];
    private readonly List<int> _selectedCompatibilityIndices = [];
    private SceneTree _nativeGridTree;

    private NCardGrid CreateNativeEditorGrid(VBoxContainer parent, bool fromPool)
    {
        NCardGrid grid = null;
        try
        {
            const string path = "res://scenes/cards/card_grid.tscn";
            grid = ResourceLoader.Load<PackedScene>(path)?.Instantiate<NCardGrid>();
            if (grid is null)
                return null;

            grid.Name = fromPool ? "RewardPoolNativeCandidates" : "RewardPoolNativeSelected";
            grid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            grid.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            grid.CustomMinimumSize = new Vector2(330f, 240f);
            grid.ClipContents = true;
            // The game scene normally fills a whole screen. Keep its card sizing,
            // navigation and scrollbar, while fitting its gutters to this pane.
            grid.YOffset = 0;
            var scroll = grid.GetNode<Control>("%ScrollContainer");
            scroll.AnchorLeft = 0f;
            scroll.AnchorRight = 1f;
            scroll.OffsetLeft = 40f;
            scroll.OffsetRight = -44f;
            // Focus grows the native holder from 0.8 to 1.0. The grid clips at
            // the pane boundary; the scroll content must allow that extra 30px.
            scroll.ClipChildren = CanvasItem.ClipChildrenMode.Disabled;
            scroll.ClipContents = false;
            var scrollbar = grid.GetNode<Control>("Scrollbar");
            scrollbar.OffsetLeft = -28f;
            scrollbar.OffsetRight = -8f;
            scrollbar.OffsetTop = 16f;
            scrollbar.OffsetBottom = -16f;

            var empty = CreateConstructionLabel(
                Localize(fromPool ? "builderNoResults" : "builderNoSelected"),
                17,
                HorizontalAlignment.Center);
            empty.Name = "EmptyState";
            empty.MouseFilter = Control.MouseFilterEnum.Ignore;
            empty.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            empty.VerticalAlignment = VerticalAlignment.Center;
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            grid.AddChild(empty);
            var overlay = new Control
            {
                Name = "ConstructionPointBadges",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ClipContents = true
            };
            overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            grid.AddChild(overlay);

            var binding = new NativeEditorGridBinding(grid, fromPool, empty, overlay);
            binding.PressedHandler = holder => OnNativeGridHolderPressed(binding, holder);
            binding.AltPressedHandler = holder => OnNativeGridHolderAltPressed(binding, holder);
            binding.ReadyHandler = () =>
            {
                if (!_nativeGridBindings.Contains(binding) || !IsValid(grid))
                    return;
                binding.Ready = true;
                EnsureNativeGridProcess();
                ApplyNativeGridCards(binding);
            };
            grid.HolderPressed += binding.PressedHandler;
            grid.HolderAltPressed += binding.AltPressedHandler;
            grid.Ready += binding.ReadyHandler;
            _nativeGridBindings.Add(binding);
            CardInspectionRegistry.Register(grid, Root,
                point => CreateNativeGridInspectionRequest(binding, point),
                () => CanUseNativeGrid(binding));
            parent.AddChild(grid);
            return grid;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info($"Reward-pool native grid unavailable pool={fromPool} reason={exception.Message}");
            if (IsValid(grid))
            {
                var binding = _nativeGridBindings.FirstOrDefault(item => ReferenceEquals(item.Grid, grid));
                if (binding is not null)
                    ReleaseNativeEditorGrid(binding);
                grid.GetParent()?.RemoveChild(grid);
                grid.QueueFree();
            }
            return null;
        }
    }

    private NativeEditorGridBinding NativeGridBinding(NCardGrid grid) =>
        _nativeGridBindings.FirstOrDefault(binding => ReferenceEquals(binding.Grid, grid));

    private void RefreshNativePoolGrid(bool restoreFocus, CardEntry preferredDetail)
    {
        var binding = NativeGridBinding(_poolCardGrid);
        if (binding is null)
            return;
        var models = new List<CardModel>(_displayedPoolCards.Count);
        binding.Entries.Clear();
        foreach (var entry in _displayedPoolCards)
        {
            if (!binding.PreviewModels.TryGetValue(entry.Id, out var preview))
            {
                preview = (CardModel)entry.Model.MutableClone();
                binding.PreviewModels[entry.Id] = preview;
            }
            models.Add(preview);
            binding.Entries[preview] = new NativeEditorGridEntry(entry, -1);
        }
        SetNativeGridCards(binding, models, restoreFocus, preferredDetail?.Id);
    }

    private void RefreshNativeSelectedGrid(int preferredIndex)
    {
        var binding = NativeGridBinding(_selectedCardGrid);
        if (binding is null)
            return;
        var models = new List<CardModel>(_displayedSelectedCards.Count);
        binding.Entries.Clear();
        for (var index = 0; index < _displayedSelectedCards.Count; index++)
        {
            var selected = _displayedSelectedCards[index];
            if (selected.Entry is not { } entry)
                continue;
            // Each occurrence has its own preview identity. A corrupt draft may
            // contain duplicates, and removing one must preserve the others.
            var preview = (CardModel)entry.Model.MutableClone();
            models.Add(preview);
            binding.Entries[preview] = new NativeEditorGridEntry(entry, index);
        }
        var restoreFocus = IsFocusInside(_selectedCardGrid, Root.GetViewport()?.GuiGetFocusOwner());
        SetNativeGridCards(binding, models, restoreFocus,
            preferredIndex >= 0 && preferredIndex < _displayedSelectedCards.Count
                ? _displayedSelectedCards[preferredIndex].Id : null);
        _selectedCardGrid.Visible = models.Count > 0 || _selectedCompatibilityIndices.Count == 0;
        _selectedList.Visible = _selectedCompatibilityIndices.Count > 0;
        _selectedList.CustomMinimumSize = new Vector2(0f, models.Count > 0 ? 110f : 240f);
        _selectedList.SizeFlagsVertical = models.Count > 0
            ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill;
    }

    private void SetNativeGridCards(NativeEditorGridBinding binding, List<CardModel> models,
        bool restoreFocus, string preferredId)
    {
        foreach (var previous in binding.HighlightedModels)
            if (binding.Ready)
                binding.Grid.UnhighlightCard(previous);
        binding.HighlightedModels.Clear();
        var changed = !binding.Models.SequenceEqual(models);
        binding.Models = models;
        binding.Empty.Visible = models.Count == 0;
        binding.PendingFocus |= restoreFocus;
        binding.PreferredFocusId = preferredId;
        if (binding.PreferredFocusModel is not null && !binding.Entries.ContainsKey(binding.PreferredFocusModel))
            binding.PreferredFocusModel = null;
        if (binding.Ready && changed)
            ApplyNativeGridCards(binding);
        foreach (var model in models)
        {
            var entry = binding.Entries[model].Entry;
            if (!binding.FromPool || ActiveSelectedIds().Contains(entry.Id, StringComparer.Ordinal))
            {
                binding.HighlightedModels.Add(model);
                if (binding.Ready)
                    binding.Grid.HighlightCard(model);
            }
        }
        binding.PendingHighlightReplay = true;
        UpdateNativeEditorGrids();
    }

    private void ApplyNativeGridCards(NativeEditorGridBinding binding)
    {
        binding.PendingCards = true;
        TryApplyNativeGridCards(binding);
    }

    private void TryApplyNativeGridCards(NativeEditorGridBinding binding)
    {
        if (!binding.PendingCards)
            return;
        if (!binding.Ready || !IsValid(binding.Grid) || !binding.Grid.IsInsideTree())
            return;
        FitNativeGridViewport(binding);
        // Native Columns becomes zero below one 300px card at smallScale 0.8.
        // Ready can precede the parent container's first completed layout.
        if (binding.Grid.GetNode<Control>("%ScrollContainer").Size.X < 240f)
            return;
        binding.PendingCards = false;
        binding.Grid.SetCards(binding.Models, PileType.None, [SortingOrders.Ascending], null);
        binding.PendingHighlightReplay = true;
    }

    private void FitNativeGridViewport(NativeEditorGridBinding binding)
    {
        var scroll = binding.Grid.GetNode<Control>("%ScrollContainer");
        var expectedWidth = binding.Grid.Size.X - 84f;
        // Native row zero is centered at 80 + (422 * 0.8) / 2 = 248.8.
        // Keep its focused face centered in shorter panes without changing the
        // holder's native hover/focus scale or the model's gameplay properties.
        var expectedYOffset = (int)Math.Floor(Math.Min(0f, binding.Grid.Size.Y * 0.5f - 248.8f));
        var faceScale = Math.Clamp((binding.Grid.Size.Y - 24f) / 422f, 0.1f, 1f);
        binding.CardFaceScale = new Vector2(faceScale, faceScale);
        if (Math.Abs(scroll.Size.X - expectedWidth) < 0.5f
            && scroll.AnchorLeft == 0f && scroll.AnchorRight == 1f
            && Math.Abs(scroll.OffsetLeft - 40f) < 0.5f
            && Math.Abs(scroll.OffsetRight + 44f) < 0.5f
            && binding.Grid.YOffset == expectedYOffset)
            return;
        if (binding.Ready)
        {
            var focus = Root.GetViewport()?.GuiGetFocusOwner();
            var focused = binding.Grid.CurrentlyDisplayedCardHolders.FirstOrDefault(holder =>
                IsValid(holder) && !holder.IsQueuedForDeletion() && IsFocusInside(holder, focus)
                && holder.CardModel is { } model && binding.Entries.ContainsKey(model));
            if (focused is not null)
            {
                binding.PreferredFocusModel = focused.CardModel;
                binding.PreferredFocusId = binding.Entries[focused.CardModel].Entry.Id;
                ClearNativeReflowFocusWait(binding);
                binding.ReflowFocusHolder = focused;
                binding.ReflowFocusExitHandler = () =>
                {
                    if (ReferenceEquals(binding.ReflowFocusHolder, focused))
                        ClearNativeReflowFocusWait(binding);
                };
                focused.TreeExiting += binding.ReflowFocusExitHandler;
                binding.PendingFocus = true;
            }
        }
        // Native InitGrid defers Size=(its current width, content height). A
        // parent resize before that deferred assignment can restore an old X.
        // Restore only horizontal anchors/gutters; preserve native scroll Y.
        scroll.AnchorLeft = 0f;
        scroll.AnchorRight = 1f;
        scroll.OffsetLeft = 40f;
        scroll.OffsetRight = -44f;
        binding.Grid.YOffset = expectedYOffset;
        if (binding.Ready)
        {
            // Use the native resize path to reflow its rows without SetCards'
            // fade/rebuild request. Once the pane is stable, its deferred X is
            // already the expected width and this branch stops running.
            binding.Grid.Notification((int)Control.NotificationResized);
            binding.PendingHighlightReplay = true;
        }
    }

    private void ClearNativeReflowFocusWait(NativeEditorGridBinding binding)
    {
        var holder = binding.ReflowFocusHolder;
        var handler = binding.ReflowFocusExitHandler;
        binding.ReflowFocusHolder = null;
        binding.ReflowFocusExitHandler = null;
        if (IsValid(holder) && handler is not null)
            holder.TreeExiting -= handler;
    }

    private void FitNativeGridCardFace(NativeEditorGridBinding binding, NCard card)
    {
        if (!IsValid(card))
            return;
        if (!binding.ScaledCards.TryGetValue(card, out var scaleBinding))
        {
            if (binding.CardFaceScale == Vector2.One)
                return;
            Action onExit = () => RestoreNativeGridCardScale(binding, card);
            scaleBinding = new NativeGridCardScaleBinding(card.Scale, onExit);
            binding.ScaledCards[card] = scaleBinding;
            card.TreeExiting += onExit;
        }
        var scale = scaleBinding.OriginalScale * binding.CardFaceScale;
        if (card.Scale != scale)
            card.Scale = scale;
    }

    private void RestoreNativeGridCardScale(NativeEditorGridBinding binding, NCard card)
    {
        if (!binding.ScaledCards.Remove(card, out var scaleBinding) || !IsValid(card))
            return;
        card.TreeExiting -= scaleBinding.ExitHandler;
        card.Scale = scaleBinding.OriginalScale;
    }

    private bool CanUseNativeGrid(NativeEditorGridBinding binding, NCardHolder holder = null) =>
        CanInspectEditorCards()
        && !IsOwnedCardInspectionVisible()
        && _nativeGridBindings.Contains(binding)
        && IsValid(binding.Grid)
        && IsValid(_activeViewRoot)
        && _activeViewRoot.IsAncestorOf(binding.Grid)
        && binding.Grid.IsInsideTree()
        && binding.Grid.IsVisibleInTree()
        && (holder is null || (IsValid(holder) && !holder.IsQueuedForDeletion()
            && holder.IsInsideTree() && holder.IsVisibleInTree()
            && binding.Grid.IsAncestorOf(holder)
            && holder.CardModel is { } model && binding.Entries.ContainsKey(model)));

    private bool CanUseEditorList(ItemList list) => CanInspectEditorCards()
        && !IsOwnedCardInspectionVisible() && IsValid(list)
        && list.IsInsideTree() && list.IsVisibleInTree()
        && IsValid(_activeViewRoot) && _activeViewRoot.IsAncestorOf(list);

    private void OnNativeGridHolderPressed(NativeEditorGridBinding binding, NCardHolder holder)
    {
        if (!CanUseNativeGrid(binding, holder))
            return;
        var selected = binding.Entries[holder.CardModel];
        ScheduleDetail(selected.Entry);
        if (binding.FromPool)
            ToggleCandidate(selected.Entry);
        else
            RemoveSelectedOccurrence(selected.DisplayedIndex);
    }

    private void OnNativeGridHolderAltPressed(NativeEditorGridBinding binding, NCardHolder holder)
    {
        // Native accept is AltPressed. Preserve the editor's existing detail
        // action; right-click inspection remains owned by CardInspectionService.
        if (CanUseNativeGrid(binding, holder))
            ScheduleDetail(binding.Entries[holder.CardModel].Entry);
    }

    private CardInspectionRequest CreateNativeGridInspectionRequest(
        NativeEditorGridBinding binding, Vector2 point)
    {
        if (!CanUseNativeGrid(binding))
            return null;
        var holder = binding.Grid.CurrentlyDisplayedCardHolders.FirstOrDefault(candidate =>
            CanUseNativeGrid(binding, candidate)
            && CardInspectionRegistry.GetCardFace(candidate.CardNode).GetGlobalRect().HasPoint(point));
        return CreateNativeGridInspectionRequest(binding, holder);
    }

    private CardInspectionRequest CreateNativeGridInspectionRequest(
        NativeEditorGridBinding binding, NCardHolder holder)
    {
        if (!CanUseNativeGrid(binding, holder) || holder is null || CardInspectionService.IsBusy)
            return null;
        var index = binding.Models.FindIndex(model => ReferenceEquals(model, holder.CardModel));
        if (index < 0)
            return null;
        return new CardInspectionRequest(Root,
            binding.Models.Select(model => new CardInspectionEntry(model)).ToArray(),
            index, holder);
    }

    private void EnsureNativeGridProcess()
    {
        if (IsValid(_nativeGridTree))
            return;
        _nativeGridTree = Root.GetTree();
        if (IsValid(_nativeGridTree))
            _nativeGridTree.ProcessFrame += UpdateNativeEditorGrids;
    }

    private void UpdateNativeEditorGrids()
    {
        if (_disposed || _closing || _viewMode != ViewMode.Editor)
            return;
        var focus = Root.GetViewport()?.GuiGetFocusOwner();
        foreach (var binding in _nativeGridBindings)
        {
            if (!binding.Ready || !IsValid(binding.Grid) || !binding.Grid.IsInsideTree())
                continue;
            FitNativeGridViewport(binding);
            TryApplyNativeGridCards(binding);
            var holders = binding.Grid.CurrentlyDisplayedCardHolders
                .Where(holder => IsValid(holder) && !holder.IsQueuedForDeletion()).ToArray();
            RestoreNativeGridInspectionFocus(binding, holders);
            if (binding.PendingHighlightReplay && !binding.PendingCards && !binding.Grid.IsAnimatingOut
                && (holders.Length > 0 || binding.Models.Count == 0))
            {
                // HighlightCard before asynchronous SetCards layout records the
                // selection but cannot animate a card that has not been created.
                foreach (var holder in holders)
                    if (holder.CardModel is { } model && binding.HighlightedModels.Contains(model))
                    {
                        binding.Grid.UnhighlightCard(model);
                        binding.Grid.HighlightCard(model);
                    }
                binding.PendingHighlightReplay = false;
            }
            var displayed = holders.ToHashSet();
            foreach (var old in binding.PriceBadges.Keys.Where(holder => !displayed.Contains(holder)).ToArray())
            {
                var badge = binding.PriceBadges[old];
                if (IsValid(badge))
                    badge.QueueFree();
                binding.PriceBadges.Remove(old);
            }
            foreach (var holder in holders)
            {
                if (!binding.Entries.TryGetValue(holder.CardModel, out var entry))
                {
                    if (binding.PriceBadges.TryGetValue(holder, out var staleBadge))
                        staleBadge.Visible = false;
                    continue;
                }
                FitNativeGridCardFace(binding, holder.CardNode);
                var priced = _constructionMethod == RewardPoolConstructionMethod.Genesis;
                if (priced && !binding.PriceBadges.ContainsKey(holder))
                {
                    var label = CreateConstructionLabel(string.Empty, 16, HorizontalAlignment.Center);
                    label.Name = "CardPoints";
                    label.MouseFilter = Control.MouseFilterEnum.Ignore;
                    label.AddThemeStyleboxOverride("normal", CreateProgressStyle(new Color(0.07f, 0.10f, 0.12f, 0.96f)));
                    binding.Overlay.AddChild(label);
                    binding.PriceBadges[holder] = label;
                }
                if (binding.PriceBadges.TryGetValue(holder, out var badge))
                {
                    badge.Visible = priced && holder.IsVisibleInTree();
                    if (badge.Visible)
                    {
                        badge.Text = ConstructionCardPoints(entry.Entry.Id);
                        var face = CardInspectionRegistry.GetCardFace(holder.CardNode);
                        var rect = face.GetGlobalRect();
                        var transform = binding.Overlay.GetGlobalTransformWithCanvas().AffineInverse();
                        var topRight = transform * (rect.Position + new Vector2(rect.Size.X, 0f));
                        badge.Size = new Vector2(92f, 27f);
                        badge.Position = topRight - new Vector2(92f, 2f);
                    }
                }
                if (CanUseNativeGrid(binding, holder) && IsFocusInside(holder, focus)
                    && !ReferenceEquals(binding.LastFocusedModel, holder.CardModel))
                {
                    binding.LastFocusedModel = holder.CardModel;
                    ScheduleDetail(entry.Entry);
                }
            }
            if (binding.PendingFocus && CanUseNativeGrid(binding))
            {
                var preferred = holders.FirstOrDefault(holder => binding.Entries.TryGetValue(holder.CardModel, out var entry)
                    && (ReferenceEquals(holder.CardModel, binding.PreferredFocusModel)
                        || (binding.PreferredFocusModel is null
                            && string.Equals(entry.Entry.Id, binding.PreferredFocusId, StringComparison.Ordinal))));
                var target = (Control)preferred
                    ?? holders.FirstOrDefault(holder => CanUseNativeGrid(binding, holder));
                if (binding.ReflowFocusHolder is not null
                    && ReferenceEquals(target, binding.ReflowFocusHolder))
                    continue;
                if (IsValid(target) && !target.IsQueuedForDeletion() && target.IsInsideTree() && target.IsVisibleInTree())
                {
                    binding.PendingFocus = false;
                    ClearNativeReflowFocusWait(binding);
                    target.GrabFocus();
                }
                else if (binding.Models.Count == 0)
                {
                    binding.PendingFocus = false;
                    ClearNativeReflowFocusWait(binding);
                    DeferFocus(_search);
                }
            }
        }
    }

    private void RestoreNativeGridInspectionFocus(NativeEditorGridBinding binding, NGridCardHolder[] holders)
    {
        if (binding.PendingFocusAfterInspectionId is not { } id || CardInspectionService.IsBusy)
            return;
        if (!CanUseNativeGrid(binding)
            || !binding.Entries.Values.Any(entry => string.Equals(entry.Entry.Id, id, StringComparison.Ordinal)))
        {
            binding.PendingFocusAfterInspectionId = null;
            binding.PendingFocusAfterInspectionModel = null;
            return;
        }
        if (binding.PendingCards || binding.Grid.IsAnimatingOut)
            return;
        var target = holders.FirstOrDefault(holder => CanUseNativeGrid(binding, holder)
            && ReferenceEquals(holder.CardModel, binding.PendingFocusAfterInspectionModel))
            ?? holders.FirstOrDefault(holder => CanUseNativeGrid(binding, holder)
                && string.Equals(binding.Entries[holder.CardModel].Entry.Id, id, StringComparison.Ordinal));
        if (target is null)
            return;
        // Native inspection may outlive the original holder after grid reflow.
        // Restore only its source grid, using a current holder rather than the old instance.
        ClearNativeReflowFocusWait(binding);
        binding.PreferredFocusModel = target.CardModel;
        binding.PreferredFocusId = id;
        binding.PendingFocus = true;
        binding.PendingFocusAfterInspectionId = null;
        binding.PendingFocusAfterInspectionModel = null;
    }

    private Control NativeGridFocusTarget(NCardGrid grid)
    {
        var binding = NativeGridBinding(grid);
        if (binding is null || !binding.Ready || !IsValid(grid) || !grid.IsInsideTree() || !grid.IsVisibleInTree())
            return null;
        // Native DefaultFocusedControl indexes its first row before SetCards'
        // asynchronous layout can create it, and may retain a freed last holder.
        return grid.CurrentlyDisplayedCardHolders.FirstOrDefault(holder =>
            IsValid(holder) && !holder.IsQueuedForDeletion() && holder.IsInsideTree()
            && holder.IsVisibleInTree() && binding.Entries.ContainsKey(holder.CardModel));
    }

    private Control GetEditorPoolFocus() => IsValid(_poolCardGrid) && _poolCardGrid.IsVisibleInTree()
        ? NativeGridFocusTarget(_poolCardGrid) ?? _search : _poolList;

    private Control GetEditorSelectedFocus() => IsValid(_selectedCardGrid) && _selectedCardGrid.IsVisibleInTree()
        ? NativeGridFocusTarget(_selectedCardGrid)
            ?? (IsValid(_selectedList) && _selectedList.IsVisibleInTree() ? _selectedList : _presetNameEdit)
        : _selectedList;

    private void RequestNativeGridFocus(NCardGrid grid)
    {
        var binding = NativeGridBinding(grid);
        if (binding is not null)
            binding.PendingFocus = true;
    }

    private int SelectedCompatibilityIndex(int index) => IsValid(_selectedCardGrid)
        ? index >= 0 && index < _selectedCompatibilityIndices.Count ? _selectedCompatibilityIndices[index] : -1
        : index;

    private void ReleaseNativeEditorGrids(Control view = null)
    {
        foreach (var binding in _nativeGridBindings.ToArray())
            if (view is null || (IsValid(binding.Grid) && view.IsAncestorOf(binding.Grid)))
                ReleaseNativeEditorGrid(binding);
    }

    private void ReleaseNativeEditorGrid(NativeEditorGridBinding binding)
    {
        _nativeGridBindings.Remove(binding);
        ClearNativeReflowFocusWait(binding);
        foreach (var card in binding.ScaledCards.Keys.ToArray())
            RestoreNativeGridCardScale(binding, card);
        if (IsValid(binding.Grid))
        {
            CardInspectionRegistry.Unregister(binding.Grid);
            binding.Grid.Ready -= binding.ReadyHandler;
            binding.Grid.HolderPressed -= binding.PressedHandler;
            binding.Grid.HolderAltPressed -= binding.AltPressedHandler;
        }
        foreach (var badge in binding.PriceBadges.Values)
            if (IsValid(badge))
            {
                badge.GetParent()?.RemoveChild(badge);
                badge.QueueFree();
            }
        binding.PriceBadges.Clear();
        binding.Entries.Clear();
        binding.PreviewModels.Clear();
        binding.HighlightedModels.Clear();
        binding.Models.Clear();
        binding.Ready = false;
        binding.PendingCards = false;
        binding.PendingHighlightReplay = false;
        binding.PreferredFocusModel = null;
        binding.PendingFocusAfterInspectionId = null;
        binding.PendingFocusAfterInspectionModel = null;
        binding.ReflowFocusHolder = null;
        if (ReferenceEquals(_poolCardGrid, binding.Grid))
            _poolCardGrid = null;
        if (ReferenceEquals(_selectedCardGrid, binding.Grid))
            _selectedCardGrid = null;
        if (_nativeGridBindings.Count == 0 && IsValid(_nativeGridTree))
        {
            _nativeGridTree.ProcessFrame -= UpdateNativeEditorGrids;
            _nativeGridTree = null;
        }
        // NCardGrid._ExitTree owns cancellation and all native holder/card cleanup.
    }

    private sealed record NativeEditorGridEntry(CardEntry Entry, int DisplayedIndex);

    private sealed record NativeGridCardScaleBinding(Vector2 OriginalScale, Action ExitHandler);

    private sealed class NativeEditorGridBinding(NCardGrid grid, bool fromPool, Label empty, Control overlay)
    {
        public NCardGrid Grid { get; } = grid;
        public bool FromPool { get; } = fromPool;
        public Label Empty { get; } = empty;
        public Control Overlay { get; } = overlay;
        public List<CardModel> Models { get; set; } = [];
        public Dictionary<CardModel, NativeEditorGridEntry> Entries { get; } = [];
        public Dictionary<string, CardModel> PreviewModels { get; } = new(StringComparer.Ordinal);
        public HashSet<CardModel> HighlightedModels { get; } = [];
        public Dictionary<NGridCardHolder, Label> PriceBadges { get; } = [];
        public Dictionary<NCard, NativeGridCardScaleBinding> ScaledCards { get; } = [];
        public Vector2 CardFaceScale { get; set; } = Vector2.One;
        public CardModel LastFocusedModel { get; set; }
        public CardModel PreferredFocusModel { get; set; }
        public NGridCardHolder ReflowFocusHolder { get; set; }
        public Action ReflowFocusExitHandler { get; set; }
        public bool Ready { get; set; }
        public bool PendingCards { get; set; }
        public bool PendingHighlightReplay { get; set; }
        public bool PendingFocus { get; set; }
        public string PreferredFocusId { get; set; }
        public string PendingFocusAfterInspectionId { get; set; }
        public CardModel PendingFocusAfterInspectionModel { get; set; }
        public Action ReadyHandler { get; set; }
        public NCardGrid.HolderPressedEventHandler PressedHandler { get; set; }
        public NCardGrid.HolderAltPressedEventHandler AltPressedHandler { get; set; }
    }
}
