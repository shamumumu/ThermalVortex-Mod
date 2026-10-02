using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace ThermalVortex.ThermalVortexCode.CardInspection;

internal static class CardInspectionService
{
    private enum Phase { Idle, Scheduled, Opening, Open, Closing }

    private static Phase _phase;
    private static NInspectCardScreen _screen;
    private static NInspectCardScreen _initializingScreen;
    private static CardInspectionRequest _pending;
    private static CardInspectionRequest _openingRequest;
    private static IReadOnlyList<CardInspectionEntry> _entries;
    private static Node _owner;
    private static Control _returnFocus;
    private static IScreenContext _sourceContext;
    private static bool _sourceIsModal;
    private static bool _contextSubscribed;
    private static bool _handlingContext;
    private static bool _closeAfterOpening;
    private static long _generation;
    private static bool _resetting;
    private static bool _layerOverridden;
    private static int _originalZIndex;
    private static bool _originalZAsRelative;

    internal static NInspectCardScreen Screen => IsValid(_screen)
        ? _screen
        : IsValid(NGame.Instance) && IsValid(NGame.Instance.InspectCardScreen)
            ? NGame.Instance.InspectCardScreen
            : null;

    internal static bool IsBusy => _phase != Phase.Idle || _pending is not null;
    internal static bool IsClosing => _phase == Phase.Closing;

    internal static bool IsCovering(Node owner) => IsBusy && Owns(owner);

    internal static bool TrySchedule(CardInspectionRequest request)
    {
        if (!IsValidRequest(request) || !IsValid(NGame.Instance)
            || _pending is not null || IsClosing || _resetting)
            return false;

        var replacing = _phase == Phase.Open && IsValid(_screen) && _screen.Visible;
        if (IsBusy && !(replacing && request.ReplaceOpenContent))
            return false;
        // A visible screen discovered after initialization belongs to its existing
        // native caller. Do not acquire it by running Open for a second time.
        if (!replacing && IsValid(Screen) && Screen.Visible)
            return false;

        if (!replacing)
        {
            SetOwner(request.Owner);
            _returnFocus = request.ReturnFocus ?? request.Owner.GetViewport()?.GuiGetFocusOwner();
            CaptureSourceContext(ActiveScreenContext.Instance.GetCurrentScreen());
            _phase = Phase.Scheduled;
            SubscribeContext();
        }

        _pending = request;
        var generation = ++_generation;
        Callable.From(() => ApplyScheduled(generation, replacing)).CallDeferred();
        return true;
    }

    internal static bool TryClose(Node owner)
    {
        if (!IsCovering(owner))
            return false;
        if (IsClosing)
            return true;

        if (_phase == Phase.Scheduled)
        {
            FinishSession(restoreFocus: false);
            return true;
        }

        _pending = null;
        ++_generation;
        if (_phase == Phase.Opening)
        {
            _closeAfterOpening = true;
            return true;
        }
        if (!IsValid(_screen) || !_screen.Visible)
        {
            FinishSession(restoreFocus: false);
            return true;
        }

        _screen.Close();
        return true;
    }

    internal static void Reset()
    {
        if (_resetting)
            return;
        _resetting = true;
        ++_generation;
        _pending = null;
        try
        {
            CardInspectionHoverTips.ForceClear();
            if (IsValid(_screen))
            {
                // Close owns removal of the original hotkeys, including callers
                // that opened the native screen without going through this service.
                if (_screen.Visible && _phase != Phase.Closing)
                    _screen.Close();
                CardInspectionScreenAccess.StopAnimations(_screen);
                _screen.Visible = false;
            }
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Card inspection reset: " + exception.Message);
        }
        finally
        {
            FinishSession(restoreFocus: false);
            DetachScreen();
            _initializingScreen = null;
            _resetting = false;
        }
    }

    internal static void BeginReady(NInspectCardScreen screen) => _initializingScreen = screen;

    internal static void EndReady(NInspectCardScreen screen)
    {
        if (ReferenceEquals(_initializingScreen, screen))
            _initializingScreen = null;
        ObserveScreen(screen);
    }

    internal static bool BeginNativeOpen(NInspectCardScreen screen, List<CardModel> cards)
    {
        if (_resetting || cards is null || cards.Count == 0 || cards.Any(card => card is null)
            || _phase is Phase.Opening or Phase.Open or Phase.Closing)
            return false;

        ObserveScreen(screen);
        if (_openingRequest is null)
        {
            // A native source won the input race before our deferred request.
            ++_generation;
            _pending = null;
            _entries = null;
            _returnFocus = screen.GetViewport()?.GuiGetFocusOwner();
            var sourceContext = ActiveScreenContext.Instance.GetCurrentScreen();
            CaptureSourceContext(ReferenceEquals(sourceContext, screen) ? null : sourceContext);
            SetOwner(_sourceContext as Node ?? _returnFocus ?? (Node)screen.GetParent());
        }
        else
        {
            _entries = _openingRequest.Entries;
        }

        _phase = Phase.Opening;
        _closeAfterOpening = false;
        SubscribeContext();
        CardInspectionHoverTips.ForceClear();
        RaiseAboveOwner(screen);
        return true;
    }

    internal static void EndNativeOpen(NInspectCardScreen screen)
    {
        if (ReferenceEquals(_screen, screen) && _phase == Phase.Opening)
        {
            _phase = Phase.Open;
            if (_closeAfterOpening)
                screen.Close();
            else
                OnContextUpdated();
        }
    }

    internal static bool BeginNativeClose(NInspectCardScreen screen)
    {
        // The lazy scene's _Ready calls Close before its first Open. It has not
        // registered hotkeys and must not cancel the request that created it.
        if (ReferenceEquals(screen, _initializingScreen))
            return true;
        if (!_resetting && ReferenceEquals(screen, _screen) && _phase == Phase.Opening)
        {
            _closeAfterOpening = true;
            return false;
        }
        if (ReferenceEquals(screen, _screen) && _phase == Phase.Closing)
            return false;
        if (!screen.Visible)
            return true;

        ObserveScreen(screen);
        _phase = Phase.Closing;
        _pending = null;
        ++_generation;
        CardInspectionHoverTips.ForceClear();
        CardInspectionScreenAccess.StopCardAnimation(screen);
        return true;
    }

    internal static void NativeOpenFailed(NInspectCardScreen screen)
    {
        if (!ReferenceEquals(screen, _screen))
            return;
        try
        {
            // The failed Open has already unwound, so cleanup need not wait for
            // the normal Open postfix that will not run on this path.
            if (_phase == Phase.Opening)
                _phase = Phase.Open;
            if (screen.Visible)
                screen.Close();
            else
                FinishSession(restoreFocus: false);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Card inspection open cleanup: " + exception.Message);
            FinishSession(restoreFocus: false);
        }
    }

    internal static CardInspectionDisplayContext GetDisplayContext()
    {
        if (_entries is null || !IsValid(_screen))
            return null;
        var index = CardInspectionScreenAccess.Index(_screen);
        var cards = CardInspectionScreenAccess.Cards(_screen);
        if (index < 0 || index >= _entries.Count || cards is null || index >= cards.Count)
            return null;
        var entry = _entries[index];
        return ReferenceEquals(entry.Model, cards[index]) ? entry.Display : null;
    }

    internal static void AdaptCurrentScreen(ref IScreenContext current)
    {
        // Native modals precede inspection in GetCurrentScreen. Only the same
        // modal that supplied this session yields to its own inspection; a new
        // modal or feedback screen retains the native priority.
        if (_sourceIsModal && (_phase is Phase.Opening or Phase.Open or Phase.Closing)
            && IsValid(_screen) && _screen.Visible && ReferenceEquals(current, _sourceContext))
            current = _screen;
    }

    private static void ApplyScheduled(long generation, bool replacing)
    {
        if (generation != _generation || _pending is not { } request)
            return;
        _pending = null;
        if (!IsValidRequest(request) || _resetting || IsClosing)
        {
            if (!replacing)
                FinishSession(restoreFocus: false);
            return;
        }
        var expectedContext = replacing ? (IScreenContext)_screen : _sourceContext;
        if (!ReferenceEquals(ActiveScreenContext.Instance.GetCurrentScreen(), expectedContext))
        {
            if (!replacing)
                FinishSession(restoreFocus: false);
            else
                OnContextUpdated();
            return;
        }

        try
        {
            if (replacing)
            {
                if (_phase != Phase.Open || !IsValid(_screen) || !_screen.Visible)
                    return;
                // Preserve the first source's owner, focus, layer and native
                // VisibilityChanged callbacks until the user finally closes.
                CardInspectionScreenAccess.FinishAnimations(_screen);
                var models = request.Entries.Select(entry => entry.Model).ToList();
                _entries = request.Entries;
                CardInspectionHoverTips.ForceClear();
                CardInspectionScreenAccess.ReplaceCards(_screen, models, request.Index, request.ViewUpgraded);
                return;
            }

            if (_phase != Phase.Scheduled || !IsValid(NGame.Instance))
                return;
            var screen = NGame.Instance.GetInspectCardScreen();
            if (!IsValid(screen))
            {
                FinishSession(restoreFocus: false);
                return;
            }

            _openingRequest = request;
            try
            {
                screen.Open(request.Entries.Select(entry => entry.Model).ToList(), request.Index, request.ViewUpgraded);
            }
            finally
            {
                _openingRequest = null;
            }
            if (_phase == Phase.Scheduled)
                FinishSession(restoreFocus: false);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Card inspection unavailable: " + exception.Message);
            if (!replacing && _phase == Phase.Scheduled)
                FinishSession(restoreFocus: false);
        }
    }

    private static bool IsValidRequest(CardInspectionRequest request) => request is not null
        && IsValid(request.Owner) && request.Owner.IsInsideTree()
        && request.Entries is { Count: > 0 }
        && request.Index >= 0 && request.Index < request.Entries.Count
        && request.Entries.All(entry => entry?.Model is not null);

    private static bool Owns(Node owner) => IsValid(owner) && IsValid(_owner)
        && (ReferenceEquals(owner, _owner) || owner.IsAncestorOf(_owner));

    private static void CaptureSourceContext(IScreenContext context)
    {
        _sourceContext = context;
        _sourceIsModal = context is not null && IsValid(NModalContainer.Instance)
            && ReferenceEquals(NModalContainer.Instance.OpenModal, context);
    }

    private static void SubscribeContext()
    {
        if (_contextSubscribed)
            return;
        ActiveScreenContext.Instance.Updated += OnContextUpdated;
        _contextSubscribed = true;
    }

    private static void OnContextUpdated()
    {
        if (_resetting || _handlingContext || _phase == Phase.Idle)
            return;
        _handlingContext = true;
        try
        {
            var current = ActiveScreenContext.Instance.GetCurrentScreen();
            if (_phase == Phase.Closing)
            {
                if (!ReferenceEquals(current, _screen) && !ReferenceEquals(current, _sourceContext))
                    _returnFocus = null;
                return;
            }
            if (_phase == Phase.Scheduled)
            {
                if (!ReferenceEquals(current, _sourceContext))
                    FinishSession(restoreFocus: false);
                return;
            }
            if (ReferenceEquals(current, _screen) || ReferenceEquals(current, _sourceContext))
                return;

            _returnFocus = null;
            _pending = null;
            ++_generation;
            if (_phase == Phase.Opening)
            {
                // Open updates screen context before it registers its hotkeys.
                // Wait for that call to return before running the paired Close.
                _closeAfterOpening = true;
            }
            else if (IsValid(_screen) && _screen.Visible)
                _screen.Close();
            else
                FinishSession(restoreFocus: false);
        }
        finally
        {
            _handlingContext = false;
        }
    }

    private static void SetOwner(Node owner)
    {
        if (ReferenceEquals(_owner, owner))
            return;
        if (IsValid(_owner))
            _owner.TreeExiting -= OnOwnerTreeExiting;
        _owner = owner;
        if (IsValid(_owner))
            _owner.TreeExiting += OnOwnerTreeExiting;
    }

    private static void OnOwnerTreeExiting()
    {
        // Opening inspection may legitimately hide the monster field or source
        // screen. Only leaving the tree ends its ownership, not visibility.
        if (_resetting || _phase == Phase.Closing)
            return;
        _pending = null;
        ++_generation;
        if (_phase == Phase.Opening)
        {
            _returnFocus = null;
            _closeAfterOpening = true;
            return;
        }
        if (IsValid(_screen) && _screen.Visible && _phase != Phase.Scheduled)
            _screen.Close();
        else
            FinishSession(restoreFocus: false);
    }

    private static void ObserveScreen(NInspectCardScreen screen)
    {
        if (ReferenceEquals(_screen, screen))
            return;
        DetachScreen();
        _screen = screen;
        screen.VisibilityChanged += OnVisibilityChanged;
        screen.TreeExiting += OnScreenTreeExiting;
    }

    private static void DetachScreen()
    {
        if (IsValid(_screen))
        {
            _screen.VisibilityChanged -= OnVisibilityChanged;
            _screen.TreeExiting -= OnScreenTreeExiting;
        }
        _screen = null;
    }

    private static void OnVisibilityChanged()
    {
        if (_resetting || !IsValid(_screen) || _screen.Visible
            || _phase is Phase.Idle or Phase.Scheduled)
            return;
        FinishSession(restoreFocus: true);
    }

    private static void OnScreenTreeExiting() => Reset();

    private static void FinishSession(bool restoreFocus)
    {
        var focus = _returnFocus;
        var owner = _owner;
        var sourceContext = _sourceContext;
        RestoreLayer();
        _phase = Phase.Idle;
        _pending = null;
        _openingRequest = null;
        _entries = null;
        SetOwner(null);
        _returnFocus = null;
        _sourceContext = null;
        _sourceIsModal = false;
        _closeAfterOpening = false;
        if (_contextSubscribed)
        {
            ActiveScreenContext.Instance.Updated -= OnContextUpdated;
            _contextSubscribed = false;
        }
        var generation = ++_generation;
        if (!restoreFocus || !IsValid(focus))
            return;

        // Let the source's native VisibilityChanged callback enable its controls
        // and let the screen-context update finish before restoring focus.
        Callable.From(() =>
        {
            if (generation == _generation && !IsBusy && IsValid(owner) && owner.IsInsideTree()
                && IsValid(focus) && focus.IsInsideTree() && focus.IsVisibleInTree()
                && (sourceContext is null || ReferenceEquals(ActiveScreenContext.Instance.GetCurrentScreen(), sourceContext))
                && focus.FocusMode != Control.FocusModeEnum.None)
                focus.GrabFocus();
        }).CallDeferred();
    }

    private static void RaiseAboveOwner(NInspectCardScreen screen)
    {
        if (_layerOverridden || !IsValid(_owner))
            return;
        // A builder's card may live in a modal above its owning root.
        // Keep native inspection above that actual source, then restore its layer on close.
        var sourceZ = Math.Max(EffectiveZ(_owner), EffectiveZ(_returnFocus));
        var inspectZ = EffectiveZ(screen);
        if (sourceZ < inspectZ)
            return;
        _originalZIndex = screen.ZIndex;
        _originalZAsRelative = screen.ZAsRelative;
        _layerOverridden = true;
        screen.ZAsRelative = false;
        screen.ZIndex = Math.Clamp(Math.Max(inspectZ, sourceZ + 1), -4096, 4096);
    }

    private static int EffectiveZ(Node node)
    {
        var value = 0;
        for (var current = node; IsValid(current); current = current.GetParent())
        {
            if (current is not CanvasItem item)
                continue;
            value += item.ZIndex;
            if (!item.ZAsRelative)
                break;
        }
        return value;
    }

    private static void RestoreLayer()
    {
        if (!_layerOverridden)
            return;
        if (IsValid(_screen))
        {
            _screen.ZIndex = _originalZIndex;
            _screen.ZAsRelative = _originalZAsRelative;
        }
        _layerOverridden = false;
    }

    private static bool IsValid(GodotObject value) => value is not null && GodotObject.IsInstanceValid(value);
}
