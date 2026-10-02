using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NPlayerHand), "StartCardPlay")]
internal static class CardEffectPreviewHandStartPatch
{
    private static readonly FieldInfo PreviewTargetField = AccessTools.Field(typeof(NCard), "_previewTarget");

    private static void Prefix(NHandCardHolder __0) =>
        CardEffectPreviewContext.BeginHandPlay(__0?.CardModel);

    private static void Postfix(NPlayerHand __instance, NHandCardHolder __0)
    {
        var card = GodotObject.IsInstanceValid(__0) ? __0.CardNode : null;
        if (!GodotObject.IsInstanceValid(card))
        {
            CardEffectPreviewContext.EndHandPlay();
            return;
        }
        var model = card.Model;
        if (model is not ThermalVortexCard)
            return;
        if (card.IsQueuedForDeletion() || !card.IsInsideTree() || !card.IsNodeReady())
        {
            CardEffectPreviewContext.EndHandPlay(model);
            return;
        }
        try
        {
            if (!__instance.InCardPlay)
            {
                CardEffectPreviewContext.EndHandPlay(card.Model);
                card.UpdateVisuals(card.DisplayingPile, CardPreviewMode.Normal);
                return;
            }
            if (!CardEffectPreviewContext.TryGet(card.Model, out _))
                CardEffectPreviewContext.BeginHandPlay(card.Model);
            CardEffectPreviewContext.SetTarget(card.Model, PreviewTargetField?.GetValue(card) as Creature);
            card.UpdateVisuals(card.DisplayingPile, CardPreviewMode.Normal);
        }
        catch (Exception ex)
        {
            CardEffectPreviewContext.EndHandPlay(model);
            MainFile.Logger.Info($"Card effect preview unavailable phase=hand_play error={ex}");
        }
    }

    private static Exception Finalizer(Exception __exception, NHandCardHolder __0)
    {
        if (__exception is not null)
            CardEffectPreviewContext.EndHandPlay(__0?.CardModel);
        return __exception;
    }
}

[HarmonyPatch(typeof(NCardPlay), "Cleanup")]
internal static class CardEffectPreviewHandEndPatch
{
    private static void Prefix(NCardPlay __instance) =>
        CardEffectPreviewContext.EndHandPlay(__instance.Holder?.CardModel);
}

[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand._ExitTree))]
internal static class CardEffectPreviewHandExitPatch
{
    private static void Prefix() => CardEffectPreviewContext.EndHandPlay();
}

[HarmonyPatch(typeof(NPlayerHand), "OnCombatEnded")]
internal static class CardEffectPreviewCombatEndPatch
{
    private static void Prefix() => CardEffectPreviewContext.EndHandPlay();
}

[HarmonyPatch(typeof(NCard), nameof(NCard.SetPreviewTarget))]
internal static class CardEffectPreviewTargetPatch
{
    private static void Prefix(NCard __instance, Creature __0) =>
        CardEffectPreviewContext.SetTarget(__instance.Model, __0);
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.GetDescriptionForUpgradePreview))]
internal static class CardEffectPreviewUpgradePatch
{
    private static void Prefix(out IDisposable __state) =>
        __state = CardEffectPreviewContext.Suppress();

    private static Exception Finalizer(Exception __exception, IDisposable __state)
    {
        __state?.Dispose();
        return __exception;
    }
}

[HarmonyPatch]
internal static class CardEffectPreviewSelectionCreatePatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        typeof(NSimpleCardSelectScreen).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == nameof(NSimpleCardSelectScreen.Create));

    private static void Postfix(NSimpleCardSelectScreen __result) =>
        CardEffectPreviewUi.AttachPendingSelection(__result);
}

[HarmonyPatch(typeof(NSimpleCardSelectScreen), "OnCardClicked")]
internal static class CardEffectPreviewSelectionChangedPatch
{
    private static void Postfix(NSimpleCardSelectScreen __instance) =>
        CardEffectPreviewUi.SelectionChanged(__instance);
}

internal static class CardEffectPreviewUi
{
    private static readonly AsyncLocal<SelectionRequest> PendingSelection = new();
    private static readonly ConditionalWeakTable<NSimpleCardSelectScreen, CardEffectPreviewPanel> Panels = new();
    private static readonly FieldInfo SelectedCardsField =
        AccessTools.Field(typeof(NSimpleCardSelectScreen), "_selectedCards");

    internal static IDisposable ForSelection(CardModel sourceCard, CardEffectSelectionPreview preview)
    {
        var previous = PendingSelection.Value;
        PendingSelection.Value = sourceCard is ThermalVortexCard
            ? new SelectionRequest(sourceCard, preview ?? new CardEffectSelectionPreview())
            : null;
        return new SelectionRequestScope(previous);
    }

    internal static void AttachPendingSelection(NSimpleCardSelectScreen screen)
    {
        var request = PendingSelection.Value;
        if (!GodotObject.IsInstanceValid(screen) || screen.IsQueuedForDeletion()
            || request is null || Panels.TryGetValue(screen, out _))
            return;

        // The screen is still outside the scene tree. Reserve a column before
        // the native grid computes its rows and columns for the first time.
        var panel = CardEffectPreviewPanel.Create(request.SourceCard, request.Preview);
        if (panel is null)
            return;
        Control grid = null;
        float? originalGridRight = null;
        void RestoreSelectionLayout()
        {
            Panels.Remove(screen);
            if (GodotObject.IsInstanceValid(grid) && !grid.IsQueuedForDeletion()
                && originalGridRight is { } right)
                grid.OffsetRight = right;
        }
        try
        {
            grid = screen.GetNodeOrNull<Control>("%CardGrid");
            originalGridRight = grid?.OffsetRight;
            if (grid is not null)
                grid.OffsetRight -= CardEffectPreviewPanel.ReservedWidth;
            // Ready/refresh may disable the panel after attachment succeeded.
            // Give the native selection grid its column back when that happens.
            panel.TreeExiting += RestoreSelectionLayout;
            Panels.Add(screen, panel);
            screen.AddChild(panel);
        }
        catch (Exception ex)
        {
            RestoreSelectionLayout();
            panel.DisableAfterFailure("attach_selection", ex);
        }
    }

    internal static void SelectionChanged(NSimpleCardSelectScreen screen)
    {
        if (!Panels.TryGetValue(screen, out var panel)
            || !GodotObject.IsInstanceValid(panel)
            || SelectedCardsField?.GetValue(screen) is not IEnumerable<CardModel> selected)
            return;
        panel.SetSelectedCards(selected.ToList());
    }

    internal static CardEffectPreviewPanel ShowTargeting(Node parent, CardModel sourceCard)
    {
        if (parent is null || sourceCard is not ThermalVortexCard)
            return null;
        var panel = CardEffectPreviewPanel.Create(sourceCard, new CardEffectSelectionPreview());
        if (panel is null)
            return null;
        panel.IsTargetingPanel = true;
        try
        {
            parent.AddChild(panel);
            return panel;
        }
        catch (Exception ex)
        {
            panel.DisableAfterFailure("attach_targeting", ex);
            return null;
        }
    }

    private sealed record SelectionRequest(CardModel SourceCard, CardEffectSelectionPreview Preview);

    private sealed class SelectionRequestScope(SelectionRequest previous) : IDisposable
    {
        public void Dispose() => PendingSelection.Value = previous;
    }
}

internal partial class CardEffectPreviewPanel : Control
{
    internal const float ReservedWidth = 340f;
    private NCard _cardNode;
    private CardEffectPreviewContext.Binding _binding;
    private IReadOnlyList<CardModel> _fixedCards;
    private Vector2 _baseCardSize;
    private Vector2 _lastViewportSize;
    private string _lastDescription;
    private Creature _lastTarget;
    private double _refreshElapsed;
    private bool _released;
    internal bool IsTargetingPanel { get; set; }

    internal static CardEffectPreviewPanel Create(CardModel source, CardEffectSelectionPreview preview)
    {
        CardEffectPreviewPanel panel = null;
        try
        {
            if (source?.MutableClone() is not CardModel clone)
                return null;

            var fixedCards = preview.FixedCards?.ToArray() ?? [];
            panel = new CardEffectPreviewPanel
            {
                Name = "ThermalVortexEffectPreview",
                MouseFilter = MouseFilterEnum.Ignore,
                FocusMode = FocusModeEnum.None,
                ZIndex = 150,
                _fixedCards = fixedCards
            };
            panel._binding = CardEffectPreviewContext.Bind(clone, source, null,
                fixedCards.Length > 0 ? fixedCards : null, preview.IsFusionSelection);
            panel._cardNode = NCard.Create(clone, ModelVisibility.Visible);
            panel.AddChild(panel._cardNode);
            panel._cardNode.MouseFilter = MouseFilterEnum.Ignore;
            panel._cardNode.FocusMode = FocusModeEnum.None;
            return panel;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Card effect preview unavailable phase=create card={source?.GetType().Name} error={ex}");
            panel?.Release();
            return null;
        }
    }

    public override void _Ready()
    {
        if (_released)
            return;
        try
        {
            // NCard initializes its labels in its own _Ready, before this
            // parent becomes ready. Create can run outside the scene tree.
            _cardNode.SetPretendCardCanBePlayed(true);
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            _cardNode.Scale = Vector2.One;
            _baseCardSize = _cardNode.GetCurrentSize();
            if (_baseCardSize.X <= 0 || _baseCardSize.Y <= 0)
                _baseCardSize = new Vector2(300f, 450f);
            Refresh();
        }
        catch (Exception ex)
        {
            DisableAfterFailure("ready", ex);
        }
    }

    internal void SetSelectedCards(IReadOnlyList<CardModel> selected)
    {
        if (_released)
            return;
        _binding.State = _binding.State with
        {
            SelectedCards = _fixedCards.Concat(selected)
                .Distinct<CardModel>(ReferenceEqualityComparer.Instance).ToList()
        };
        Refresh();
    }

    internal void SetTarget(Creature target)
    {
        if (_released)
            return;
        _binding.State = _binding.State with { Target = target };
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_released)
            return;
        if (CombatManager.Instance?.IsOverOrEnding == true)
        {
            Release();
            return;
        }
        _refreshElapsed += delta;
        if (_refreshElapsed < 0.15d || !IsVisibleInTree())
            return;
        _refreshElapsed = 0;
        Refresh();
    }

    private void Refresh()
    {
        if (_released || !IsNodeReady() || !GodotObject.IsInstanceValid(_cardNode)
            || !_cardNode.IsNodeReady())
            return;
        try
        {
            RefreshReadyCard();
        }
        catch (Exception ex)
        {
            DisableAfterFailure("refresh", ex);
        }
    }

    private void RefreshReadyCard()
    {
        var model = _cardNode.Model;
        var target = _binding.State.Target;
        if (!ReferenceEquals(_lastTarget, target))
        {
            _cardNode.SetPreviewTarget(target);
            _lastTarget = target;
        }
        model.DynamicVars.ClearPreview();
        model.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, model.DynamicVars);
        var description = model.GetDescriptionForPile(PileType.Hand, target);
        if (!string.Equals(_lastDescription, description, StringComparison.Ordinal))
        {
            _lastDescription = description;
            _cardNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        }
        var viewport = GetViewportRect().Size;
        if (_lastViewportSize == viewport)
            return;
        _lastViewportSize = viewport;
        var scale = Math.Min(300f / _baseCardSize.X, viewport.Y * 0.56f / _baseCardSize.Y);
        _cardNode.Scale = Vector2.One * scale;
        var center = IsTargetingPanel
            ? new Vector2(ReservedWidth * 0.52f, viewport.Y * 0.62f)
            : new Vector2(viewport.X - ReservedWidth * 0.52f, viewport.Y * 0.48f);
        // NCard's body is centered on the card's local origin in native holders.
        _cardNode.Position = center;
    }

    internal void DisableAfterFailure(string phase, Exception ex)
    {
        if (_released)
            return;
        MainFile.Logger.Info($"Card effect preview unavailable phase={phase} error={ex}");
        Release();
    }

    internal void Release()
    {
        if (_released)
            return;
        _released = true;
        _binding?.Dispose();
        Visible = false;
        if (GodotObject.IsInstanceValid(_cardNode) && _cardNode.GetParent() is null)
            _cardNode.QueueFree();
        if (GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion())
            QueueFree();
    }

    public override void _ExitTree()
    {
        _released = true;
        _binding?.Dispose();
    }
}
