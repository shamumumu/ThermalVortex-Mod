using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using ThermalVortex.ThermalVortexCode.CardInspection;
using ThermalVortex.ThermalVortexCode.RewardPools;
using SizeFlags = Godot.Control.SizeFlags;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal sealed partial class RewardPoolBuilderSession
{
    private const int SingleCardDraftColumns = 5;
    private const int SingleCardDraftRows = RewardPoolDraftService.GenericCardOfferCount / SingleCardDraftColumns;
    // Match NCardGrid's spacing and leave room for its full-size hover animation.
    private const float NativeCardGridSpacing = 40f;
    private const int NativeCardGridTopInset = 80;
    private const int SingleCardDraftVerticalInset = 52;
    private static readonly Vector2 SingleCardDraftGridSize =
        NCard.defaultSize * NCardHolder.smallScale * new Vector2(SingleCardDraftColumns, SingleCardDraftRows)
        + new Vector2(SingleCardDraftColumns - 1, SingleCardDraftRows - 1) * NativeCardGridSpacing
        + new Vector2(44f, SingleCardDraftVerticalInset) * 2f;

    private static string TransitionPackName(string id) => id switch
    {
        "transition.start" => ManagerText("启动", "Setup"),
        "transition.defense" => ManagerText("解场防御", "Defense"),
        "transition.sustain" => ManagerText("续航", "Sustain"),
        _ => ManagerText("过渡包", "Transition pack")
    };

    private static string TransitionPackDescription(string id) => id switch
    {
        "transition.start" => ManagerText("帮助主轴启动。", "Helps get your main strategy going."),
        "transition.defense" => ManagerText("补充解场和防御。", "Adds answers to enemy pressure."),
        "transition.sustain" => ManagerText("补充资源和续航。", "Adds tools for longer fights."),
        _ => string.Empty
    };

    private void ShowSingleCardDraftRound(VBoxContainer view, RewardPoolDraftState shown, Button back)
    {
        var entries = shown.OfferIds.Select(id => _mainById.GetValueOrDefault(id)).ToArray();
        var grid = entries.Length == RewardPoolDraftService.GenericCardOfferCount && entries.All(entry => entry is not null)
            ? ResourceLoader.Load<PackedScene>("res://scenes/cards/card_grid.tscn")?.Instantiate<NCardGrid>()
            : null;
        if (grid is null)
        {
            view.AddChild(CreateConstructionLabel(
                ManagerText("当前候选无法显示。", "The current card offer could not be displayed."),
                20, HorizontalAlignment.Center));
            ReplaceView(view, ViewMode.Draft);
            DeferFocus(back);
            return;
        }

        var page = new Control();
        view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        page.AddChild(view);

        var models = entries.Select(entry => (CardModel)entry.Model.MutableClone()).ToArray();
        var selected = new HashSet<int>();
        var count = CreateDraftPrompt(string.Empty);
        count.Name = "ThermalVortexSingleCardDraftCount";
        count.HorizontalAlignment = HorizontalAlignment.Right;
        view.AddChild(count);

        var host = new Control
        {
            Name = "ThermalVortexSingleCardDraftGridHost",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 280f)
        };
        view.AddChild(host);
        grid.Name = "ThermalVortexSingleCardDraftGrid";
        grid.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        grid.Size = SingleCardDraftGridSize;
        grid.YOffset = SingleCardDraftVerticalInset - NativeCardGridTopInset;
        var nativeScroll = grid.GetNode<Control>("%ScrollContainer");
        nativeScroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        nativeScroll.ClipContents = false;
        host.AddChild(grid);
        var confirmFootprint = Vector2.Zero;
        var viewSpacing = view.GetThemeConstant("separation");
        host.Resized += FitGrid;

        var nativeConfirm = ResourceLoader.Load<PackedScene>("res://scenes/ui/confirm_button.tscn")?.Instantiate<NConfirmButton>();
        var fallbackConfirm = nativeConfirm is null ? CreateConstructionButton("✓", 70f, 30) : null;
        var confirm = (Control)nativeConfirm ?? fallbackConfirm;
        confirm.Name = "ThermalVortexSingleCardDraftConfirm";
        confirm.TooltipText = ManagerText("提交选牌", "Submit picks");
        var firstOfferId = shown.OfferIds[0];
        SceneTree navigationTree = null;
        NGridCardHolder[] holders = [];
        NGridCardHolder lastBottomFocus = null;
        if (nativeConfirm is not null)
        {
            nativeConfirm.OverrideHotkeys([]);
            confirmFootprint = PlaceSingleCardDraftConfirm(nativeConfirm);
            nativeConfirm.Ready += () =>
            {
                nativeConfirm.Enable();
                UpdateConfirmation();
            };
            nativeConfirm.Connect(NButton.SignalName.Released, Callable.From<NButton>(_ => Submit()));
            view.AddChild(new Control { CustomMinimumSize = new Vector2(0f, confirmFootprint.Y), MouseFilter = Control.MouseFilterEnum.Ignore });
            page.AddChild(nativeConfirm);
        }
        else
        {
            var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            fallbackConfirm.Pressed += Submit;
            actions.AddChild(fallbackConfirm);
            view.AddChild(actions);
        }

        grid.Ready += () =>
        {
            grid.SetCanScroll(false);
            FitGrid();
            grid.SetCards(models, PileType.None, [SortingOrders.Ascending], null);
            navigationTree = grid.GetTree();
            navigationTree.ProcessFrame += ConnectNavigation;
        };
        grid.TreeExiting += () =>
        {
            if (IsValid(navigationTree)) navigationTree.ProcessFrame -= ConnectNavigation;
            navigationTree = null;
            CardInspectionRegistry.Unregister(grid);
        };
        grid.HolderPressed += holder =>
        {
            if (!CanAct()) return;
            var index = Array.IndexOf(models, holder.CardModel);
            if (index < 0) return;
            if (selected.Remove(index))
                grid.UnhighlightCard(models[index]);
            else if (selected.Count < RewardPoolDraftService.GenericCardPickCount)
            {
                selected.Add(index);
                grid.HighlightCard(models[index]);
            }
            UpdateConfirmation();
        };
        grid.HolderAltPressed += holder =>
        {
            var request = Inspect(holder);
            if (request is not null)
                CardInspectionService.TrySchedule(request);
        };
        CardInspectionRegistry.Register(grid, Root, point =>
        {
            var holder = grid.CurrentlyDisplayedCardHolders.FirstOrDefault(candidate =>
                IsValid(candidate) && candidate.IsVisibleInTree() && IsValid(candidate.CardNode)
                && CardInspectionRegistry.GetCardFace(candidate.CardNode).GetGlobalRect().HasPoint(point));
            return Inspect(holder);
        }, CanAct);

        ReplaceView(page, ViewMode.Draft);
        UpdateConfirmation();
        DeferFocus(back);

        bool CanAct() => CanUseDraftChoice(grid, shown, firstOfferId);

        void FitGrid()
        {
            var availableSize = host.Size;
            if (availableSize.X <= 0f || availableSize.Y <= 0f)
                return;
            // On wide screens the centered grid can use the footer height
            // while the native checkmark stays clear of the last column.
            if (confirmFootprint.Y > 0f && (availableSize.X - SingleCardDraftGridSize.X) * 0.5f >= confirmFootprint.X)
                availableSize.Y += confirmFootprint.Y + viewSpacing;
            var scale = MathF.Min(1f, MathF.Min(
                availableSize.X / SingleCardDraftGridSize.X, availableSize.Y / SingleCardDraftGridSize.Y));
            grid.Scale = Vector2.One * scale;
            grid.Position = (availableSize - SingleCardDraftGridSize * scale) * 0.5f;
        }

        void ConnectNavigation()
        {
            if (!IsValid(grid) || !grid.IsInsideTree() || grid.IsAnimatingOut)
                return;
            var current = models.Select(grid.GetCardHolder).ToArray();
            if (current.Any(holder => !IsValid(holder) || holder.IsQueuedForDeletion()))
                return;
            navigationTree.ProcessFrame -= ConnectNavigation;
            navigationTree = null;
            holders = current;
            foreach (var holder in holders.Skip(SingleCardDraftColumns))
                holder.FocusEntered += () =>
                {
                    lastBottomFocus = holder;
                    SetFocusNeighbor(confirm, FocusDirection.Top, holder);
                };
            UpdateNavigation();
            if (CanAct())
                DeferFocus(holders[0]);
        }

        void UpdateNavigation()
        {
            if (holders.Length == 0)
                return;
            var showConfirm = selected.Count == RewardPoolDraftService.GenericCardPickCount;
            for (var index = 0; index < holders.Length; index++)
            {
                var holder = holders[index];
                var column = index % SingleCardDraftColumns;
                var topRow = index < SingleCardDraftColumns;
                SetFocusNeighbors(holder,
                    column > 0 ? holders[index - 1] : holder,
                    column < SingleCardDraftColumns - 1 ? holders[index + 1] : holder,
                    topRow ? back : holders[index - SingleCardDraftColumns],
                    topRow ? holders[index + SingleCardDraftColumns] : showConfirm ? confirm : holder,
                    index > 0 ? holders[index - 1] : back,
                    index + 1 < holders.Length ? holders[index + 1] : showConfirm ? confirm : back);
            }
            SetFocusNeighbor(back, FocusDirection.Bottom, holders[0]);
            SetFocusNeighbor(confirm, FocusDirection.Top, lastBottomFocus ?? holders[^1]);
            SetFocusNeighbor(confirm, FocusDirection.Previous, holders[^1]);
            SetFocusNeighbor(confirm, FocusDirection.Next, back);
        }

        void UpdateConfirmation()
        {
            var ready = selected.Count == RewardPoolDraftService.GenericCardPickCount;
            count.SetTextAutoSize($"{selected.Count}/[blue]{RewardPoolDraftService.GenericCardPickCount}[/blue]");
            if (nativeConfirm is not null)
            {
                // Leave the tick visible while incomplete. Hotkeys are disabled;
                // only a focused or clicked, enabled-for-input tick can submit.
                nativeConfirm.Modulate = ready ? Colors.White : new Color(1f, 1f, 1f, 0.4f);
                nativeConfirm.MouseFilter = ready ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
                nativeConfirm.FocusMode = ready ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
            }
            else
            {
                fallbackConfirm.Disabled = !ready;
            }
            UpdateNavigation();
        }

        CardInspectionRequest Inspect(NCardHolder holder)
        {
            if (!CanAct() || !IsValid(holder))
                return null;
            var index = Array.IndexOf(models, holder.CardModel);
            return index < 0 ? null : new CardInspectionRequest(Root,
                entries.Select(entry => new CardInspectionEntry(entry.Model)).ToArray(), index, holder);
        }

        void Submit()
        {
            if (!CanAct() || selected.Count != RewardPoolDraftService.GenericCardPickCount)
                return;
            var ids = selected.Order().Select(index => entries[index].Id).ToArray();
            _draftOfferCommitInProgress = true;
            try
            {
                if (!RewardPoolDraftService.TryChooseGenericCards(shown.SessionId, shown.GenericRoundsCompleted, ids, out var error))
                {
                    ShowConstructionNotice(Localize("constructionDraft"), error);
                    return;
                }
                _selectionChanged = true;
                ShowDraftView();
            }
            finally { _draftOfferCommitInProgress = false; }
        }
    }

    private static Vector2 PlaceSingleCardDraftConfirm(NConfirmButton button)
    {
        var width = button.OffsetRight - button.OffsetLeft;
        var height = button.OffsetBottom - button.OffsetTop;
        // The native shadow extends 26px right and 39px down. Leave room for
        // the focus animation, too, and keep the visible art inside the page.
        const float rightOverflow = 34f;
        const float bottomOverflow = 52f;
        var rightInset = DraftHorizontalMargin + width + rightOverflow;
        var bottomInset = DraftBottomMargin + height + bottomOverflow;

        // NConfirmButton._Ready reads these offsets before using NGame.Size
        // to set a local Position. Include the draft page's origin so its
        // native animation and resize callback land at the same visible spot.
        button.OffsetRight = DraftHorizontalMargin + rightInset - 120f;
        button.OffsetLeft = button.OffsetRight - width;
        button.OffsetBottom = 110f - DraftTopMargin - bottomInset;
        button.OffsetTop = button.OffsetBottom - height;
        return new Vector2(width + rightOverflow, height + bottomOverflow + 10f);
    }
}
