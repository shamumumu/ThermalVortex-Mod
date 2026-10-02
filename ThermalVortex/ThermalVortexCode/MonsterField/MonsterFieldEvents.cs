using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Patches;

namespace ThermalVortex.ThermalVortexCode.MonsterField;

public enum MonsterFieldLeaveReason
{
    FusionMaterial = 0,
    Material = 1,
    BattleDestroyed = 2,
    Exhaust = 3,
    FieldClear = 4,
    Transformation = 5
}

public readonly record struct MonsterFieldLeaveEvent(
    CardModel Card,
    MonsterFieldHealth Health,
    MonsterFieldLeaveReason Reason,
    AbstractModel Source)
{
    public bool WasDestroyedByBattle => Reason == MonsterFieldLeaveReason.BattleDestroyed;
    public bool WasUsedAsMaterial => Reason is MonsterFieldLeaveReason.FusionMaterial or MonsterFieldLeaveReason.Material;
    public bool WasExhausted => Reason == MonsterFieldLeaveReason.Exhaust;
}

public readonly record struct MonsterFieldEnterEvent(
    CardModel Card,
    MonsterFieldHealth Health);

internal readonly record struct MonsterFieldImpactEvent(
    CardModel Card,
    int Amount,
    int HpBefore,
    int HpAfter,
    bool Destroyed,
    AbstractModel Source);

public interface IMonsterFieldEnterListener
{
    void AfterMonsterEnteredField(MonsterFieldEnterEvent enterEvent);
}

public interface IMonsterFieldEnterResolvedListener
{
    Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent);
}

public interface IMonsterFieldLeaveListener
{
    Task AfterMonsterLeftField(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent);
}

public interface IMonsterFieldLeaveResolvedListener
{
    Task AfterMonsterLeftFieldResolved(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent);
}

public interface IMonsterFieldAttackTargetRule
{
    bool CanBeAttackTarget { get; }
}

internal static class MonsterFieldEventService
{
    internal static event Action<MonsterFieldEnterEvent> MonsterEnteredVisual;
    internal static event Action<MonsterFieldImpactEvent> MonsterImpactedVisual;

    internal static void NotifyMonsterEnteredField(MonsterFieldEnterEvent enterEvent)
    {
        NotifyVisual(MonsterEnteredVisual, enterEvent, "entry");

        var owner = TryGetOwner(enterEvent.Card);
        var combat = owner?.Creature?.CombatState;
        if (combat is null)
            return;

        var listeners = combat.Creatures
            .SelectMany(creature => creature.Powers)
            .OfType<IMonsterFieldEnterListener>()
            .Cast<IMonsterFieldEnterListener>()
            .ToList();

        if (enterEvent.Card is IMonsterFieldEnterListener cardListener)
            listeners.Add(cardListener);

        foreach (var listener in listeners)
        {
            using var effectSource = EnterListenerSource(listener);
            listener.AfterMonsterEnteredField(enterEvent);
        }
    }

    internal static void NotifyMonsterImpacted(MonsterFieldImpactEvent impactEvent) =>
        NotifyVisual(MonsterImpactedVisual, impactEvent, "impact");

    internal static async Task NotifyMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        var owner = TryGetOwner(enterEvent.Card);
        var combat = owner?.Creature?.CombatState;
        if (combat is null)
            return;

        var listeners = combat.Creatures
            .SelectMany(creature => creature.Powers)
            .OfType<IMonsterFieldEnterResolvedListener>()
            .ToList();

        if (enterEvent.Card is IMonsterFieldEnterResolvedListener cardListener)
            listeners.Add(cardListener);

        foreach (var listener in listeners)
        {
            using var effectSource = EnterListenerSource(listener);
            await listener.AfterMonsterEnteredFieldResolved(ctx, enterEvent);
        }
    }

    internal static async Task NotifyMonsterLeavingField(
        PlayerChoiceContext ctx,
        MonsterFieldLeaveEvent leaveEvent,
        Func<bool> canLeave = null)
    {
        var owner = TryGetOwner(leaveEvent.Card);
        var combat = owner?.Creature?.CombatState;
        if (combat is null)
            return;

        var isMaterialUseValid = leaveEvent.WasUsedAsMaterial
            ? MonsterFieldService.CaptureMaterialUseValidity(owner)
            : null;

        var listeners = combat.Creatures
            .SelectMany(creature => creature.Powers)
            .OfType<IMonsterFieldLeaveListener>()
            .Cast<IMonsterFieldLeaveListener>()
            .ToList();

        if (leaveEvent.Card is IMonsterFieldLeaveListener cardListener)
            listeners.Add(cardListener);

        foreach (var listener in listeners)
        {
            if (isMaterialUseValid?.Invoke() == false || canLeave?.Invoke() == false)
                return;

            using var effectSource = EnterListenerSource(listener);
            await listener.AfterMonsterLeftField(ctx, leaveEvent);
        }

        if (isMaterialUseValid?.Invoke() == false || canLeave?.Invoke() == false)
            return;

        using (AshBlossomActionNegation.EnterSource(leaveEvent.Card))
            await CyberDevourState.TriggerLeavingEffects(ctx, leaveEvent);
    }

    /// <summary>
    /// A monster used as material from hand, discard, exhaust, or draw
    /// was never on the field, so field-wide listeners must not observe a fake
    /// departure. Its own material effects and inherited copy effects still
    /// resolve as part of the material payment.
    /// </summary>
    internal static async Task NotifyOffFieldMaterialUsed(
        PlayerChoiceContext ctx,
        MonsterFieldLeaveEvent leaveEvent)
    {
        var isMaterialUseValid = MonsterFieldService.CaptureMaterialUseValidity(TryGetOwner(leaveEvent.Card));
        if (!leaveEvent.WasUsedAsMaterial || !isMaterialUseValid())
            return;

        if (leaveEvent.Card is IMonsterFieldLeaveListener cardListener)
        {
            using var effectSource = EnterListenerSource(cardListener);
            await cardListener.AfterMonsterLeftField(ctx, leaveEvent);
        }

        if (!isMaterialUseValid())
            return;

        using (AshBlossomActionNegation.EnterSource(leaveEvent.Card))
            await CyberDevourState.TriggerLeavingEffects(ctx, leaveEvent);
    }

    internal static async Task NotifyMonsterLeftFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldLeaveEvent leaveEvent)
    {
        var owner = TryGetOwner(leaveEvent.Card);
        var combat = owner?.Creature?.CombatState;
        if (combat is null)
            return;

        var isMaterialUseValid = leaveEvent.WasUsedAsMaterial
            ? MonsterFieldService.CaptureMaterialUseValidity(owner)
            : null;

        var listeners = combat.Creatures
            .SelectMany(creature => creature.Powers)
            .OfType<IMonsterFieldLeaveResolvedListener>()
            .Cast<IMonsterFieldLeaveResolvedListener>()
            .ToList();

        if (leaveEvent.Card is IMonsterFieldLeaveResolvedListener cardListener)
            listeners.Add(cardListener);

        foreach (var listener in listeners)
        {
            if (isMaterialUseValid?.Invoke() == false)
                return;

            using var effectSource = EnterListenerSource(listener);
            await listener.AfterMonsterLeftFieldResolved(ctx, leaveEvent);
        }

        if (isMaterialUseValid?.Invoke() == false)
            return;

        using (AshBlossomActionNegation.EnterSource(leaveEvent.Card))
            await CyberDevourState.TriggerLeftResolvedEffects(ctx, leaveEvent);
    }

    internal static async Task NotifyOffFieldMaterialUseResolved(
        PlayerChoiceContext ctx,
        MonsterFieldLeaveEvent leaveEvent)
    {
        var isMaterialUseValid = MonsterFieldService.CaptureMaterialUseValidity(TryGetOwner(leaveEvent.Card));
        if (!leaveEvent.WasUsedAsMaterial || !isMaterialUseValid())
            return;

        if (leaveEvent.Card is IMonsterFieldLeaveResolvedListener cardListener)
        {
            using var effectSource = EnterListenerSource(cardListener);
            await cardListener.AfterMonsterLeftFieldResolved(ctx, leaveEvent);
        }

        if (!isMaterialUseValid())
            return;

        using (AshBlossomActionNegation.EnterSource(leaveEvent.Card))
            await CyberDevourState.TriggerLeftResolvedEffects(ctx, leaveEvent);
    }

    private static IDisposable EnterListenerSource(object listener) =>
        AshBlossomActionNegation.EnterSource(listener as AbstractModel);

    private static MegaCrit.Sts2.Core.Entities.Players.Player TryGetOwner(CardModel card)
    {
        try
        {
            return card?.Owner;
        }
        catch
        {
            return null;
        }
    }

    private static void NotifyVisual<T>(Action<T> listeners, T payload, string eventName)
    {
        if (listeners is null)
            return;

        foreach (var listener in listeners.GetInvocationList().Cast<Action<T>>())
        {
            try
            {
                listener(payload);
            }
            catch (Exception exception)
            {
                MainFile.Logger.Info($"Monster field {eventName} visual listener failed: {exception}");
            }
        }
    }
}
