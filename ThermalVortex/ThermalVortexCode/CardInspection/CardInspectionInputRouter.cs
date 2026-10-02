using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace ThermalVortex.ThermalVortexCode.CardInspection;

// A single input owner prevents the down/up pair from also reaching AltPressed or cancel.
internal sealed partial class CardInspectionInputRouter : Node
{
    private static CardInspectionInputRouter _instance;
    private Window _window;
    private SceneTree _tree;
    private bool _consumeRightRelease;
    private bool _consumeLeftRelease;
    private bool _moving;
    private bool _orderUpdateScheduled;
    private bool _reportedFailure;

    internal static void Install()
    {
        if (CardInspectionRegistry.Valid(_instance))
            return;
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is not { } root)
            return;
        _instance = new CardInspectionInputRouter
        {
            Name = "ThermalVortexCardInspectionInput",
            ProcessMode = ProcessModeEnum.Always
        };
        var router = _instance;
        Callable.From(() =>
        {
            if (CardInspectionRegistry.Valid(root) && CardInspectionRegistry.Valid(router)
                && !router.IsInsideTree())
                root.AddChild(router);
        }).CallDeferred();
    }

    public override void _Ready()
    {
        _tree = GetTree();
        _window = _tree.Root;
        _tree.NodeAdded += OnNodeAdded;
        _tree.NodeRemoved += OnNodeRemoved;
        _window.ChildOrderChanged += ScheduleInputOrder;
        _window.FocusExited += OnWindowFocusExited;
        RegisterExisting(_window);
        MaintainInputOrder();
        SetProcess(false);
        SetProcessInput(true);
    }

    public override void _Input(InputEvent input)
    {
        try
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Right } right)
            {
                if (!right.Pressed && _consumeRightRelease)
                {
                    _consumeRightRelease = false;
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (right.Pressed && _consumeRightRelease)
                {
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (!right.Pressed)
                    return;
                if (CardInspectionService.IsClosing)
                {
                    CaptureRightGesture();
                    return;
                }
                if (!CardInspectionService.IsBusy && IsCancellingGameplay())
                {
                    // Leave the press to the game's cancellation. Its release must not inspect the returned card.
                    _consumeRightRelease = true;
                    CardInspectionHoverTips.ForceClear();
                    return;
                }
                if (CardInspectionRegistry.TryResolve(GetViewport(), right.Position, out var request))
                {
                    if (CardInspectionService.TrySchedule(request) || CardInspectionService.IsBusy)
                    {
                        CaptureRightGesture();
                        return;
                    }
                }
                // With the native detail open, ordinary right-click remains a close gesture.
                var screen = NGame.Instance?.InspectCardScreen;
                if (CardInspectionRegistry.Valid(screen) && screen.Visible)
                {
                    CaptureRightGesture();
                    screen.Close();
                }
                else if (CardInspectionService.IsBusy)
                    CaptureRightGesture();
                return;
            }

            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } left)
            {
                if (!left.Pressed && _consumeLeftRelease)
                {
                    _consumeLeftRelease = false;
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (left.Pressed && CardInspectionHoverTips.IsPointerOverCard(left.Position))
                {
                    // Clearing the tooltip in _Input removes its GUI barrier;
                    // consume this gesture before the underlying shop/card can receive it.
                    _consumeLeftRelease = true;
                    GetViewport().SetInputAsHandled();
                    CardInspectionHoverTips.ForceClear();
                    return;
                }
            }

            // Native Close drops its hotkey blocker before its fade finishes.
            // Do not let that short visual transition send clicks/keys into the source screen.
            if (CardInspectionService.IsClosing
                && input is InputEventMouseButton or InputEventKey or InputEventJoypadButton)
            {
                GetViewport().SetInputAsHandled();
                return;
            }
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                CardInspectionHoverTips.ForceClear();
        }
        catch (Exception exception)
        {
            if (!_reportedFailure)
            {
                _reportedFailure = true;
                MainFile.Logger.Info($"Card inspection input unavailable: {exception}");
            }
        }
    }

    private static bool IsCancellingGameplay() =>
        CardInspectionHoverTips.IsDraggingOrTargeting();

    private void CaptureRightGesture()
    {
        _consumeRightRelease = true;
        GetViewport().SetInputAsHandled();
    }

    private static void RegisterExisting(Node node)
    {
        CardInspectionRegistry.Observe(node);
        foreach (var child in node.GetChildren())
            RegisterExisting(child);
    }

    private static void OnNodeAdded(Node node) => CardInspectionRegistry.Observe(node);
    private static void OnNodeRemoved(Node node) => CardInspectionRegistry.Forget(node);

    private void ScheduleInputOrder()
    {
        if (_moving || _orderUpdateScheduled || !IsInsideTree())
            return;
        _orderUpdateScheduled = true;
        Callable.From(() =>
        {
            _orderUpdateScheduled = false;
            if (CardInspectionRegistry.Valid(this) && IsInsideTree())
                MaintainInputOrder();
        }).CallDeferred();
    }

    private void MaintainInputOrder()
    {
        if (_moving || !IsInsideTree() || !CardInspectionRegistry.Valid(_window)
            || GetIndex() == _window.GetChildCount() - 1)
            return;
        _moving = true;
        try
        {
            // _Input uses reverse tree order. Reorder only when children actually change.
            _window.MoveChild(this, -1);
        }
        finally
        {
            _moving = false;
        }
    }

    private void OnWindowFocusExited()
    {
        _consumeRightRelease = false;
        _consumeLeftRelease = false;
        CardInspectionHoverTips.ForceClear();
    }

    public override void _ExitTree()
    {
        if (CardInspectionRegistry.Valid(_tree))
        {
            _tree.NodeAdded -= OnNodeAdded;
            _tree.NodeRemoved -= OnNodeRemoved;
        }
        if (CardInspectionRegistry.Valid(_window))
        {
            _window.ChildOrderChanged -= ScheduleInputOrder;
            _window.FocusExited -= OnWindowFocusExited;
        }
        CardInspectionService.Reset();
        CardInspectionHoverTips.ForceClear();
        CardInspectionRegistry.Clear();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
internal static class CardInspectionInstallPatch
{
    private static void Postfix() => CardInspectionInputRouter.Install();
}
