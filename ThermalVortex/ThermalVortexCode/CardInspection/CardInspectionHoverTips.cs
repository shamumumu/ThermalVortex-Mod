using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.PotionLab;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace ThermalVortex.ThermalVortexCode.CardInspection;

/// <summary>Keeps an existing related-card tip reachable while the pointer crosses the gap.</summary>
internal static class CardInspectionHoverTips
{
    private const double BridgeSeconds = 0.4;
    private static readonly ConditionalWeakTable<Control, TriggerRegistration> Triggers = new();
    private static readonly AccessTools.FieldRef<NHoverTipSet, bool> FollowOwner =
        AccessTools.FieldRefAccess<NHoverTipSet, bool>("_followOwner");
    private static readonly AccessTools.FieldRef<NHoverTipSet, NHoverTipCardContainer> CardContainer =
        AccessTools.FieldRefAccess<NHoverTipSet, NHoverTipCardContainer>("_cardHoverTipContainer");
    private static readonly AccessTools.FieldRef<NRelicCollectionEntry, RelicModel> CollectionRelic =
        AccessTools.FieldRefAccess<NRelicCollectionEntry, RelicModel>("relic");
    private static readonly AccessTools.FieldRef<NLabPotionHolder, PotionModel> LabPotion =
        AccessTools.FieldRefAccess<NLabPotionHolder, PotionModel>("_model");
    private static Session _active;
    [ThreadStatic] private static Control _softRemovalOwner;
    [ThreadStatic] private static int _hardClearDepth;

    internal static void RegisterTrigger(Control owner, Control trigger)
    {
        if (!IsLive(owner) || !IsLive(trigger))
            return;

        if (_active is { } active && ReferenceEquals(active.Owner, owner)
            && !ReferenceEquals(active.Trigger, trigger))
        {
            ClearSession(active, removeTip: true);
        }

        Triggers.Remove(owner);
        Triggers.Add(owner, new TriggerRegistration(trigger));
    }

    internal static void RequestSoftRemove(Control owner)
    {
        if (!TrySoftRemove(owner))
            NHoverTipSet.Remove(owner);
    }

    internal static void ForceClear()
    {
        if (_active is { } active)
            ClearSession(active, removeTip: true);
    }

    internal static bool IsPointerOverCard(Vector2 viewportPoint) =>
        _active is { } active && active.HasLiveSource() && active.ContainsCardPoint(viewportPoint);

    internal static Control BeginSoftRemoval(Control owner)
    {
        var previous = _softRemovalOwner;
        _softRemovalOwner = owner;
        return previous;
    }

    internal static void EndSoftRemoval(Control previous) => _softRemovalOwner = previous;

    internal static void BeginHardClear()
    {
        _hardClearDepth++;
        if (_active is { } active)
            ClearSession(active, removeTip: false);
    }

    internal static void EndHardClear() => _hardClearDepth--;

    internal static bool BeforeRemove(Control owner)
    {
        // Only the exact synchronous unfocus call chain may postpone removal.
        // Clear(), model rebinding, teardown and unrelated Remove() calls stay immediate.
        if (_hardClearDepth == 0 && ReferenceEquals(_softRemovalOwner, owner)
            && TrySoftRemove(owner))
        {
            return false;
        }

        if (_active is { } active && ReferenceEquals(active.Owner, owner))
            ClearSession(active, removeTip: false);
        return true;
    }

    internal static void BeforeCreate()
    {
        // A soft removal keeps the native owner-to-tip entry alive. Re-entering
        // the same source must release it before CreateAndShow registers again.
        if (_active is { } active)
            ClearSession(active, removeTip: true);
    }

    internal static void OnCreated(Control owner, NHoverTipSet tipSet)
    {
        if (!IsLive(owner) || !IsLive(tipSet))
            return;

        var trigger = ResolveTrigger(owner);
        if (!IsLive(trigger))
            return;

        if (_active is { } existing && ReferenceEquals(existing.TipSet, tipSet))
            return;

        var container = CardContainer(tipSet);
        if (!IsLive(container))
            return;

        // This is one known tip container at creation, never a scene-tree search.
        var cards = new List<NCard>();
        foreach (var child in container.GetChildren())
        {
            var card = child.GetNodeOrNull<NCard>("%Card");
            if (IsLive(card) && card.Model is not null)
                cards.Add(card);
        }
        if (cards.Count == 0)
            return;

        if (_active is { } previous)
            ClearSession(previous, removeTip: !ReferenceEquals(previous.Owner, owner));

        var session = new Session(owner, trigger, tipSet);
        _active = session;
        try
        {
            session.Attach(cards);
        }
        catch (Exception exception)
        {
            ClearSession(session, removeTip: false);
            MainFile.Logger.Info($"Related-card hover input unavailable: {exception.Message}");
        }
    }

    internal static void CheckLifetime(NHoverTipSet tipSet)
    {
        // The existing tip already processes following. Only constant-time lifetime
        // checks are added here; pointer geometry runs on enter/exit and one timer.
        if (_active is { } active && ReferenceEquals(active.TipSet, tipSet)
            && !active.HasLiveSource())
        {
            ClearSession(active, removeTip: true);
        }
    }

    internal static void KeepTransferStill(NHoverTipSet tipSet)
    {
        if (_active is { } active && ReferenceEquals(active.TipSet, tipSet))
            active.KeepFollowFrozen();
    }

    internal static void ClearForOwner(Control owner)
    {
        if (_active is { } active && ReferenceEquals(active.Owner, owner))
            ClearSession(active, removeTip: true);
    }

    internal static void ClearForSourceNode(Control source)
    {
        if (_active is { } active && (ReferenceEquals(active.Owner, source)
            || ReferenceEquals(active.Trigger, source)
            || ReferenceEquals(ResolveSourceNode(active.Owner), source)))
        {
            ClearSession(active, removeTip: true);
        }
    }

    internal static Control GetPowerTipOwner(NPower power) =>
        IsLive(power) && power.Model?.Owner is { } creature && IsLive(NCombatRoom.Instance)
            ? NCombatRoom.Instance.GetCreatureNode(creature)?.Hitbox
            : null;

    internal static void RegisterPowerTrigger(NPower power)
    {
        var owner = GetPowerTipOwner(power);
        if (IsLive(owner))
            RegisterTrigger(owner, power);
    }

    private static bool TrySoftRemove(Control owner)
    {
        var active = _active;
        if (active is null || !ReferenceEquals(active.Owner, owner) || !active.HasLiveSource())
            return false;
        active.BeginTransfer();
        return true;
    }

    private static Control ResolveTrigger(Control owner)
    {
        if (Triggers.TryGetValue(owner, out var registration) && IsLive(registration.Trigger))
            return registration.Trigger;
        return owner switch
        {
            NCardHolder holder => holder.Hitbox,
            _ => owner
        };
    }

    private static Control ResolveSourceNode(Control owner) => owner switch
    {
        NCardHolder holder => holder.CardNode,
        NRelicInventoryHolder holder => holder.Relic,
        NRelicBasicHolder holder => holder.Relic,
        NTreasureRoomRelicHolder holder => holder.Relic,
        NPotionHolder holder => holder.Potion,
        _ => owner
    };

    private static object SourceIdentity(Control owner, Control trigger)
    {
        if (trigger is NPower power)
            return power.Model;
        return owner switch
        {
            NCardHolder holder => holder.CardNode?.Model,
            NEventOptionButton option => option.Option,
            NRelicInventoryHolder holder => holder.Relic?.Model,
            NRelicBasicHolder holder => holder.Relic?.Model,
            NTreasureRoomRelicHolder holder => holder.Relic?.Model,
            NRelicCollectionEntry collection => CollectionRelic(collection),
            NLabPotionHolder lab => LabPotion(lab),
            NPotionHolder holder => holder.Potion?.Model,
            NRelic relic => relic.Model,
            NPotion potion => potion.Model,
            _ => trigger
        };
    }

    private static bool IsLive(Node node) =>
        node is not null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion();

    private static bool IsShowing(Control node) =>
        IsLive(node) && node.IsInsideTree() && node.IsVisibleInTree();

    internal static bool IsDraggingOrTargeting()
    {
        var combatRoom = NCombatRoom.Instance;
        var combatUi = IsLive(combatRoom) ? combatRoom.Ui : null;
        var hand = IsLive(combatUi) ? combatUi.Hand : null;
        if (IsLive(hand) && hand.InCardPlay)
            return true;

        // The native TargetManager singleton dereferences the current run.
        // Library and collection tooltips also use this check outside a run.
        var run = NRun.Instance;
        var globalUi = IsLive(run) ? run.GlobalUi : null;
        var targetManager = IsLive(globalUi) ? globalUi.TargetManager : null;
        return IsLive(targetManager) && targetManager.IsInSelection;
    }

    private static void ClearSession(Session session, bool removeTip)
    {
        if (!ReferenceEquals(_active, session))
            return;
        _active = null;
        session.Detach();
        if (removeTip && GodotObject.IsInstanceValid(session.Owner) && IsLive(session.TipSet))
            NHoverTipSet.Remove(session.Owner);
    }

    private sealed record TriggerRegistration(Control Trigger);

    private sealed class Session(Control owner, Control trigger, NHoverTipSet tipSet)
    {
        internal Control Owner { get; } = owner;
        internal Control Trigger { get; } = trigger;
        internal NHoverTipSet TipSet { get; } = tipSet;
        private readonly List<Control> _hitboxes = [];
        private readonly NCard _sourceCard = (owner as NCardHolder)?.CardNode;
        private readonly CardModel _sourceModel = (owner as NCardHolder)?.CardNode?.Model;
        private readonly CardModel _baseSourceModel = (owner as NCardHolder)?.CardModel;
        private readonly object _sourceOption = (owner as NEventOptionButton)?.Option;
        private readonly object _sourceIdentity = SourceIdentity(owner, trigger);
        private readonly bool _persistent = owner is NInspectCardScreen;
        private readonly IScreenContext _sourceScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        private SceneTreeTimer _timer;
        private bool _attached;
        private bool _transferring;
        private bool _originalFollowOwner;

        internal void Attach(IEnumerable<NCard> cards)
        {
            _attached = true;
            Owner.TreeExiting += OnSourceUnavailable;
            Owner.VisibilityChanged += OnVisibilityChanged;
            TipSet.TreeExiting += OnTipExiting;
            Trigger.MouseEntered += ResumeFromTrigger;
            if (!ReferenceEquals(Owner, Trigger))
            {
                Trigger.TreeExiting += OnSourceUnavailable;
                Trigger.VisibilityChanged += OnVisibilityChanged;
            }
            if (IsLive(_sourceCard))
                _sourceCard.ModelChanged += OnSourceModelChanged;
            ActiveScreenContext.Instance.Updated += OnScreenChanged;

            foreach (var card in cards)
            {
                var face = CardInspectionRegistry.GetCardFace(card);
                if (!IsLive(face))
                    continue;
                var hitbox = new Control
                {
                    Name = "CardInspectionHoverHitbox",
                    MouseFilter = Control.MouseFilterEnum.Stop,
                    MouseForcePassScrollEvents = false,
                    MouseBehaviorRecursive = Control.MouseBehaviorRecursiveEnum.Enabled,
                    FocusMode = Control.FocusModeEnum.None
                };
                _hitboxes.Add(hitbox);
                face.AddChild(hitbox);
                hitbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                hitbox.MouseEntered += OnTipEntered;
                hitbox.MouseExited += OnTipExited;
                // The global right-button router runs before GUI dispatch. Left
                // clicks and scrolling here must not operate the card behind it.
                hitbox.GuiInput += input =>
                {
                    if (input is InputEventMouse)
                        hitbox.AcceptEvent();
                };
            }
        }

        internal bool HasLiveSource() =>
            IsShowing(Owner) && IsShowing(Trigger) && IsShowing(TipSet)
            && !IsDraggingOrTargeting()
            && (!IsLive(_sourceCard)
                ? _sourceCard is null
                : ReferenceEquals(_sourceCard.Model, _sourceModel)
                  && ReferenceEquals((Owner as NCardHolder)?.CardNode, _sourceCard))
            && (Owner is not NCardHolder holder || ReferenceEquals(holder.CardModel, _baseSourceModel))
            && (Owner is not NEventOptionButton option || ReferenceEquals(option.Option, _sourceOption))
            && ReferenceEquals(SourceIdentity(Owner, Trigger), _sourceIdentity);

        internal void BeginTransfer()
        {
            // Related cards on the inspect screen have no source-hover lifetime.
            if (_persistent)
                return;
            if (!_transferring)
            {
                _transferring = true;
                _originalFollowOwner = FollowOwner(TipSet);
                FollowOwner(TipSet) = false;
            }
            if (PointerIsOnTip())
                CancelTimer();
            else if (_timer is null)
                StartTimer();
        }

        internal void ResumeFromTrigger()
        {
            if (!ReferenceEquals(_active, this) || !HasLiveSource())
                return;
            CancelTimer();
            if (!_transferring)
                return;
            _transferring = false;
            if (_originalFollowOwner)
                TipSet.SetFollowOwner();
        }

        internal void KeepFollowFrozen()
        {
            if (!_transferring)
                return;
            _originalFollowOwner = true;
            FollowOwner(TipSet) = false;
        }

        internal void Detach()
        {
            CancelTimer();
            if (!_attached)
                return;
            _attached = false;
            ActiveScreenContext.Instance.Updated -= OnScreenChanged;
            if (GodotObject.IsInstanceValid(Owner))
            {
                Owner.TreeExiting -= OnSourceUnavailable;
                Owner.VisibilityChanged -= OnVisibilityChanged;
            }
            if (GodotObject.IsInstanceValid(TipSet))
            {
                TipSet.TreeExiting -= OnTipExiting;
                if (_transferring)
                    FollowOwner(TipSet) = _originalFollowOwner;
            }
            if (GodotObject.IsInstanceValid(Trigger))
            {
                Trigger.MouseEntered -= ResumeFromTrigger;
                if (!ReferenceEquals(Owner, Trigger))
                {
                    Trigger.TreeExiting -= OnSourceUnavailable;
                    Trigger.VisibilityChanged -= OnVisibilityChanged;
                }
            }
            if (GodotObject.IsInstanceValid(_sourceCard))
                _sourceCard.ModelChanged -= OnSourceModelChanged;
            foreach (var hitbox in _hitboxes)
            {
                if (!IsLive(hitbox))
                    continue;
                hitbox.MouseEntered -= OnTipEntered;
                hitbox.MouseExited -= OnTipExited;
                hitbox.MouseFilter = Control.MouseFilterEnum.Ignore;
                hitbox.QueueFree();
            }
            _hitboxes.Clear();
        }

        private bool PointerIsOnTip()
        {
            if (!IsLive(TipSet) || !TipSet.IsInsideTree())
                return false;
            return ContainsCardPoint(TipSet.GetViewport().GetMousePosition());
        }

        internal bool ContainsCardPoint(Vector2 point)
        {
            foreach (var hitbox in _hitboxes)
            {
                if (CardInspectionRegistry.ContainsPoint(hitbox, point))
                    return true;
            }
            return false;
        }

        private void OnTipEntered()
        {
            if (ReferenceEquals(_active, this))
                BeginTransfer();
        }

        private void OnTipExited()
        {
            if (ReferenceEquals(_active, this) && _transferring && !PointerIsOnTip())
                StartTimer();
        }

        private void StartTimer()
        {
            CancelTimer();
            if (!IsLive(TipSet) || !TipSet.IsInsideTree())
                return;
            _timer = TipSet.GetTree().CreateTimer(BridgeSeconds);
            _timer.Timeout += OnBridgeExpired;
        }

        private void CancelTimer()
        {
            if (_timer is not null && GodotObject.IsInstanceValid(_timer))
                _timer.Timeout -= OnBridgeExpired;
            _timer = null;
        }

        private void OnBridgeExpired()
        {
            CancelTimer();
            if (!ReferenceEquals(_active, this))
                return;
            if (HasLiveSource() && PointerIsOnTip())
                return;
            ClearSession(this, removeTip: true);
        }

        private void OnSourceUnavailable() => ClearSession(this, removeTip: true);
        private void OnTipExiting() => ClearSession(this, removeTip: false);
        private void OnVisibilityChanged()
        {
            if (!HasLiveSource())
                ClearSession(this, removeTip: true);
        }
        private void OnSourceModelChanged(CardModel model)
        {
            if (!ReferenceEquals(model, _sourceModel))
                ClearSession(this, removeTip: true);
        }
        private void OnScreenChanged()
        {
            if (!ReferenceEquals(_sourceScreen, ActiveScreenContext.Instance.GetCurrentScreen()))
                ClearSession(this, removeTip: true);
        }
    }
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet.CreateAndShow),
    typeof(Control), typeof(IEnumerable<IHoverTip>), typeof(HoverTipAlignment))]
internal static class CardInspectionHoverCreatePatch
{
    private static void Prefix() => CardInspectionHoverTips.BeforeCreate();
    private static void Postfix(Control __0, NHoverTipSet __result) => CardInspectionHoverTips.OnCreated(__0, __result);
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet.Remove))]
internal static class CardInspectionHoverRemovePatch
{
    private static bool Prefix(Control __0) => CardInspectionHoverTips.BeforeRemove(__0);
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet.Clear))]
internal static class CardInspectionHoverClearPatch
{
    private static void Prefix() => CardInspectionHoverTips.BeginHardClear();
    private static void Finalizer() => CardInspectionHoverTips.EndHardClear();
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet._Process))]
internal static class CardInspectionHoverLifetimePatch
{
    private static void Postfix(NHoverTipSet __instance) => CardInspectionHoverTips.CheckLifetime(__instance);
}

[HarmonyPatch(typeof(NHoverTipSet), nameof(NHoverTipSet.SetFollowOwner))]
internal static class CardInspectionHoverFollowPatch
{
    private static void Postfix(NHoverTipSet __instance) => CardInspectionHoverTips.KeepTransferStill(__instance);
}

[HarmonyPatch]
internal static class CardInspectionHoverUnfocusPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type[] owners = [typeof(NCardHolder), typeof(NGridCardHolder), typeof(NHandCardHolder),
            typeof(NPreviewCardHolder), typeof(NSelectedHandCardHolder), typeof(NEventOptionButton),
            typeof(NRelicInventoryHolder), typeof(NRelicBasicHolder), typeof(NRelicCollectionEntry),
            typeof(NTreasureRoomRelicHolder), typeof(NPotionHolder), typeof(NLabPotionHolder)];
        return owners.Select(type => AccessTools.DeclaredMethod(type, "OnUnfocus")).Where(method => method is not null);
    }

    private static void Prefix(Control __instance, out Control __state) =>
        __state = CardInspectionHoverTips.BeginSoftRemoval(__instance);
    private static void Finalizer(Control __state) => CardInspectionHoverTips.EndSoftRemoval(__state);
}

[HarmonyPatch]
internal static class CardInspectionHoverRebindPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NCardHolder), nameof(NCardHolder.ReassignToCard));
        yield return AccessTools.Method(typeof(NCardHolder), nameof(NCardHolder.Clear));
        yield return AccessTools.Method(typeof(NCardHolder), "SetCard");
    }
    private static void Prefix(NCardHolder __instance) => CardInspectionHoverTips.ClearForOwner(__instance);
}

[HarmonyPatch]
internal static class CardInspectionHoverTargetingPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NPlayerHand), "StartCardPlay");
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(NTargetManager)))
        {
            if (method.Name == nameof(NTargetManager.StartTargeting))
                yield return method;
        }
    }
    private static void Prefix() => CardInspectionHoverTips.ForceClear();
}

[HarmonyPatch]
internal static class CardInspectionPowerHoverCreatePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NPower), "OnHovered");
        yield return AccessTools.Method(typeof(NPower), "ShowPowerHoverTips");
    }
    private static void Prefix(NPower __instance) => CardInspectionHoverTips.RegisterPowerTrigger(__instance);
}

[HarmonyPatch(typeof(NPower), "OnUnhovered")]
internal static class CardInspectionPowerHoverRemovePatch
{
    private static void Prefix(NPower __instance, out Control __state) =>
        __state = CardInspectionHoverTips.BeginSoftRemoval(CardInspectionHoverTips.GetPowerTipOwner(__instance));
    private static void Finalizer(Control __state) => CardInspectionHoverTips.EndSoftRemoval(__state);
}

[HarmonyPatch(typeof(NCreature), "OnUnfocus")]
internal static class CardInspectionCreatureHoverRemovePatch
{
    private static void Prefix(NCreature __instance, out Control __state) =>
        __state = CardInspectionHoverTips.BeginSoftRemoval(__instance.Hitbox);
    private static void Finalizer(Control __state) => CardInspectionHoverTips.EndSoftRemoval(__state);
}

[HarmonyPatch]
internal static class CardInspectionCreatureHoverCreatePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NCreature), "OnFocus");
        yield return AccessTools.Method(typeof(NCreature), "ShowCreatureHoverTips");
    }
    private static void Prefix(NCreature __instance) =>
        CardInspectionHoverTips.RegisterTrigger(__instance.Hitbox, __instance.Hitbox);
}

[HarmonyPatch]
internal static class CardInspectionHoverSourceModelPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.PropertySetter(typeof(NRelic), nameof(NRelic.Model));
        yield return AccessTools.PropertySetter(typeof(NPotion), nameof(NPotion.Model));
        yield return AccessTools.PropertySetter(typeof(NPower), nameof(NPower.Model));
        yield return AccessTools.PropertySetter(typeof(NPotionHolder), nameof(NPotionHolder.Potion));
    }
    private static void Prefix(Control __instance) => CardInspectionHoverTips.ClearForSourceNode(__instance);
}
