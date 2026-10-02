using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using ThermalVortex.ThermalVortexCode.CardInspection;
using ThermalVortex.ThermalVortexCode.RewardPools;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal sealed partial class RewardPoolBuilderSession
{
    private readonly List<ManagerPresetCoverBinding> _managerPresetCovers = [];

    private bool CanUseLibraryAction(Button button) =>
        !_disposed && !_closing && _viewMode == ViewMode.Library
        && !IsValid(_modalRoot) && !CardInspectionService.IsBusy
        && IsValid(_activeViewRoot) && IsValid(button) && !button.IsQueuedForDeletion()
        && button.IsInsideTree() && button.IsVisibleInTree() && !button.Disabled
        && _activeViewRoot.IsAncestorOf(button);

    private void BindLibraryAction(Button button, Action action) =>
        button.Pressed += () => { if (CanUseLibraryAction(button)) action(); };

    private void BindManagerSelectionVisibility(ScrollContainer scroll, GridContainer grid)
    {
        void KeepSelectionVisible()
        {
            if (_disposed || _closing || _viewMode != ViewMode.Library
                || !ReferenceEquals(_presetScroll, scroll) || !ReferenceEquals(_presetGrid, grid)
                || !IsValid(_activeViewRoot) || !_activeViewRoot.IsAncestorOf(grid))
                return;
            var focused = _presetTiles.FirstOrDefault(tile => IsValid(tile.Button) && tile.Button.HasFocus())?.Button;
            EnsurePresetTileVisible(focused ?? GetSelectedPresetTileButton());
        }
        // Container sorting can finish after Root.Resized's deferred scroll.
        // Follow the final grid and viewport geometry, not the previous layout.
        scroll.Resized += KeepSelectionVisible;
        grid.Resized += KeepSelectionVisible;
        grid.TreeExiting += () =>
        {
            if (IsValid(scroll)) scroll.Resized -= KeepSelectionVisible;
            if (IsValid(grid)) grid.Resized -= KeepSelectionVisible;
        };
    }

    private VBoxContainer CreateManagerSelectedSummary()
    {
        var detail = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        detail.AddThemeConstantOverride("separation", 3);
        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 10);
        detail.AddChild(heading);
        _presetBadge = CreateBadgeLabel();
        heading.AddChild(_presetBadge);
        _presetName = CreateLabel(string.Empty, 21, HorizontalAlignment.Left);
        _presetName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _presetName.ClipText = true;
        _presetName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        heading.AddChild(_presetName);
        _presetCounts = CreateLabel(string.Empty, 15, HorizontalAlignment.Left);
        detail.AddChild(_presetCounts);
        var metadata = new HBoxContainer();
        metadata.AddThemeConstantOverride("separation", 12);
        detail.AddChild(metadata);
        _presetRarities = CreateLabel(string.Empty, 13, HorizontalAlignment.Left);
        _presetRarities.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _presetRarities.ClipText = true;
        _presetRarities.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        metadata.AddChild(_presetRarities);
        _presetUpdated = CreateLabel(string.Empty, 13, HorizontalAlignment.Right);
        _presetUpdated.Modulate = MutedColor;
        metadata.AddChild(_presetUpdated);
        _presetIssues = CreateLabel(string.Empty, 13, HorizontalAlignment.Left);
        _presetIssues.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _presetIssues.MaxLinesVisible = 2;
        _presetIssues.VerticalAlignment = VerticalAlignment.Top;
        detail.AddChild(_presetIssues);
        return detail;
    }

    private Control CreateManagerPresetCover(RewardPoolPresetDraft preset, StatusTone tone)
    {
        var entries = preset.MainCardIds.Concat(preset.ExtraCardIds).Distinct(StringComparer.Ordinal)
            .Select(id => _mainById.GetValueOrDefault(id) ?? _extraById.GetValueOrDefault(id))
            .Where(entry => entry is not null).Take(3).ToArray();
        if (entries.Length < 3)
            return CreateDeckBoxDisplay(tone, standard: false);

        var host = new Control
        {
            Name = "ThermalVortexManagerPresetCover",
            CustomMinimumSize = new Vector2(0f, 122f),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            FocusMode = Control.FocusModeEnum.None,
            ClipContents = true
        };
        host.SetMeta("presetId", preset.Id.ToString());
        host.SetMeta("representativeIds", string.Join(";", entries.Select(entry => entry.Id)));
        NCardBundle bundle = null;
        Control wrapper = null;
        ManagerPresetCoverBinding binding = null;
        try
        {
            bundle = NCardBundle.Create(entries.Select(entry => (CardModel)entry.Model.MutableClone()).ToArray());
            if (!IsValid(bundle))
                throw new InvalidOperationException("The native preset cover is unavailable.");
            bundle.Name = "ThermalVortexManagerPresetBundle";
            wrapper = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, FocusMode = Control.FocusModeEnum.None };
            host.AddChild(wrapper);
            binding = new ManagerPresetCoverBinding(bundle, host, wrapper);
            var captured = binding;
            binding.ReadyHandler = () =>
            {
                if (!_managerPresetCovers.Contains(captured) || !IsValid(bundle)) return;
                MakeManagerCoverPassive(bundle);
                FitManagerPresetCover(captured);
                Callable.From(() => FitManagerPresetCover(captured)).CallDeferred();
            };
            binding.ResizeHandler = () => FitManagerPresetCover(captured);
            binding.ExitHandler = () => ReleaseManagerPresetCover(captured);
            _managerPresetCovers.Add(binding);
            bundle.Ready += binding.ReadyHandler;
            host.Resized += binding.ResizeHandler;
            host.TreeExiting += binding.ExitHandler;
            wrapper.AddChild(bundle);
            return host;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Manager native preset cover unavailable reason=" + exception.Message);
            if (binding is not null) ReleaseManagerPresetCover(binding);
            if (IsValid(bundle) && bundle.GetParent() is null)
                bundle.QueueFree();
            if (IsValid(wrapper))
            {
                wrapper.GetParent()?.RemoveChild(wrapper);
                wrapper.QueueFree();
            }
            var fallback = CreateDeckBoxDisplay(tone, standard: false);
            fallback.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            host.AddChild(fallback);
            return host;
        }
    }

    private static void MakeManagerCoverPassive(Node node)
    {
        if (node is Control control)
        {
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
            control.FocusMode = Control.FocusModeEnum.None;
        }
        // These three cards identify a preset. They are not a selectable card list.
        if (node is NCard card) CardInspectionRegistry.Forget(card);
        foreach (var child in node.GetChildren()) MakeManagerCoverPassive(child);
    }

    private void FitManagerPresetCover(ManagerPresetCoverBinding binding)
    {
        if (_disposed || !_managerPresetCovers.Contains(binding) || !IsValid(binding.Host)
            || !IsValid(binding.Wrapper) || !binding.Host.IsInsideTree())
            return;
        var available = binding.Host.Size - new Vector2(12f, 8f);
        var scale = Math.Clamp(Math.Min(available.X / (430f * 0.85f), available.Y / (590f * 0.85f)), 0.1f, 1f);
        binding.Wrapper.Scale = Vector2.One * scale;
        binding.Wrapper.Position = binding.Host.Size * 0.5f;
    }

    private void ReleaseManagerPresetCovers(Control view = null)
    {
        foreach (var binding in _managerPresetCovers.ToArray())
            if (view is null || (IsValid(binding.Host) && view.IsAncestorOf(binding.Host)))
                ReleaseManagerPresetCover(binding);
    }

    private void ReleaseManagerPresetCover(ManagerPresetCoverBinding binding)
    {
        if (!_managerPresetCovers.Remove(binding)) return;
        if (IsValid(binding.Bundle)) binding.Bundle.Ready -= binding.ReadyHandler;
        if (!IsValid(binding.Host)) return;
        binding.Host.Resized -= binding.ResizeHandler;
        binding.Host.TreeExiting -= binding.ExitHandler;
        // NCardBundle owns and releases the cover cards when the tile leaves the tree.
    }

    private sealed class ManagerPresetCoverBinding(NCardBundle bundle, Control host, Control wrapper)
    {
        public NCardBundle Bundle { get; } = bundle;
        public Control Host { get; } = host;
        public Control Wrapper { get; } = wrapper;
        public Action ReadyHandler { get; set; }
        public Action ResizeHandler { get; set; }
        public Action ExitHandler { get; set; }
    }
}
