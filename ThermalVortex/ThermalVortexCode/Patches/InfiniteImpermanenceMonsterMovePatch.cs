using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove))]
internal static class InfiniteImpermanenceMonsterMovePatch
{
    [HarmonyPrefix]
    private static bool NegateMove(
        MonsterModel __instance,
        ref Task __result,
        out EnemyPartialNegationState __state)
    {
        __state = null;

        var relinquished = __instance.Creature?.GetPower<RelinquishedControlPower>();
        if (relinquished?.IsControlActive == true)
        {
            __result = relinquished.SkipControlledMove(__instance);
            return false;
        }

        var chalice = __instance.Creature?.GetPower<ForbiddenChalicePower>();
        if (chalice is not null)
        {
            __result = ReplaceMoveAsync(__instance, chalice.ReplaceMoveWithStrengthGain);
            return false;
        }

        var judgment = __instance.Creature?.GetPower<SolemnJudgmentPower>();
        if (judgment is not null)
        {
            __result = NegateMoveAsync(__instance, judgment.TriggerNegation);
            return false;
        }

        var power = __instance.Creature?.GetPower<InfiniteImpermanencePower>();
        if (power is not null)
        {
            __result = NegateMoveAsync(__instance, power.TriggerNegation, power);
            return false;
        }

        var action = EnemyActionClassifier.Classify(__instance.NextMove);
        var warning = __instance.Creature?.GetPower<SolemnWarningPower>();
        if (warning is not null && (action.Kind & EnemyActionKind.Attack) != 0)
        {
            __result = NegateMoveAsync(__instance, warning.TriggerNegation, warning);
            return false;
        }

        var mirrorForce = TryGetMirrorForcePower(__instance);
        if (mirrorForce is not null && (action.Kind & EnemyActionKind.Attack) != 0)
        {
            __result = NegateAndReflectAttackAsync(__instance, mirrorForce);
            return false;
        }

        var strike = __instance.Creature?.GetPower<SolemnStrikePower>();
        if (strike is not null && (action.Kind & EnemyActionKind.Attack) == 0)
        {
            __result = NegateMoveAsync(__instance, strike.TriggerNegation, strike);
            return false;
        }

        // Record every acting enemy, including an unmarked one nested inside
        // another action. Only a real effect from the marked source can trigger.
        __state ??= new EnemyPartialNegationState();
        __state.AshScope = AshBlossomActionNegation.Enter(__instance.Creature);

        if (strike is not null)
        {
            __state ??= new EnemyPartialNegationState();
            __state.StrikeScope = SolemnStrikeActionNegation.Enter(__instance, strike);
        }

        return true;
    }

    [HarmonyPostfix]
    private static void FinishPartialNegation(ref Task __result, EnemyPartialNegationState __state)
    {
        if (__state is null)
            return;

        if (__state.StrikeScope is not null)
            SolemnStrikeActionNegation.DetachCaller(__state.StrikeScope);
        if (__state.AshScope is not null)
            AshBlossomActionNegation.DetachCaller(__state.AshScope);

        __result = CompletePartialNegationsAsync(__result, __state);
    }

    [HarmonyFinalizer]
    private static Exception FinishFailedAction(Exception __exception, EnemyPartialNegationState __state)
    {
        if (__exception is not null && __state?.AshScope is { } scope)
        {
            AshBlossomActionNegation.DetachCaller(scope);
            scope.Completed = true;
        }
        return __exception;
    }

    private static async Task NegateMoveAsync(MonsterModel monster, Action triggerNegation, PowerModel powerToRemove = null)
    {
        triggerNegation();

        if (monster.NextMove is not null)
            monster.MoveStateMachine?.OnMovePerformed(monster.NextMove);

        if (powerToRemove is not null)
            await PowerCmd.Remove(powerToRemove);
    }

    private static async Task ReplaceMoveAsync(MonsterModel monster, Func<Task> replacement)
    {
        if (monster.NextMove is not null)
            monster.MoveStateMachine?.OnMovePerformed(monster.NextMove);

        if (replacement is not null)
            await replacement();
    }

    private static async Task NegateAndReflectAttackAsync(MonsterModel monster, MirrorForcePower power)
    {
        var damage = GetIntentAttackDamage(monster);
        await NegateMoveAsync(monster, power.TriggerNegation);

        if (damage <= 0 || monster.Creature is null || power.Owner is null)
            return;

        await ThermalVortexCombatVfx.EffectDamageAsync(
            new BlockingPlayerChoiceContext(),
            monster.Creature,
            damage,
            ValueProp.Unpowered,
            power.Owner,
            null);
    }

    private static int GetIntentAttackDamage(MonsterModel monster)
    {
        var attacker = monster.Creature;
        var targets = attacker?.CombatState?.PlayerCreatures;
        if (attacker is null || targets is null)
            return 0;

        return monster.NextMove?.Intents
            .OfType<AttackIntent>()
            .Sum(intent => intent.GetTotalDamage(targets, attacker)) ?? 0;
    }

    private static MirrorForcePower TryGetMirrorForcePower(MonsterModel monster) =>
        monster.CombatState?.Players
            .Select(player => player.Creature?.GetPower<MirrorForcePower>())
            .FirstOrDefault(power => power is not null);

    private static async Task CompletePartialNegationsAsync(Task moveTask, EnemyPartialNegationState state)
    {
        try
        {
            await moveTask;
        }
        finally
        {
            if (state.StrikeScope is not null)
                await SolemnStrikeActionNegation.CompleteScopeAsync(state.StrikeScope);
            if (state.AshScope is not null)
                await AshBlossomActionNegation.CompleteScopeAsync(state.AshScope);
        }
    }
}

internal sealed class EnemyPartialNegationState
{
    internal SolemnStrikeActionNegation.Scope StrikeScope { get; set; }
    internal AshBlossomActionNegation.Scope AshScope { get; set; }
}

internal static class SolemnStrikeActionNegation
{
    private static readonly AsyncLocal<Scope> CurrentScope = new();

    internal sealed class Scope
    {
        internal Scope(MonsterModel monster, SolemnStrikePower power, Scope previous)
        {
            Monster = monster;
            Power = power;
            Previous = previous;
        }

        internal MonsterModel Monster { get; }
        internal SolemnStrikePower Power { get; }
        internal Scope Previous { get; }
    }

    internal static Scope Enter(MonsterModel monster, SolemnStrikePower power)
    {
        var scope = new Scope(monster, power, CurrentScope.Value);
        CurrentScope.Value = scope;
        power.TriggerNegation();
        return scope;
    }

    internal static void DetachCaller(Scope scope)
    {
        if (ReferenceEquals(CurrentScope.Value, scope))
            CurrentScope.Value = scope.Previous;
    }

    internal static bool IsActiveFor(MegaCrit.Sts2.Core.Entities.Creatures.Creature selectedEnemy) =>
        selectedEnemy is not null
        && ReferenceEquals(CurrentScope.Value?.Power.Owner, selectedEnemy);

    internal static bool IsSelectedEnemySource(
        MegaCrit.Sts2.Core.Entities.Creatures.Creature source,
        MegaCrit.Sts2.Core.Entities.Creatures.Creature selectedEnemy) =>
        source is not null
        && selectedEnemy is not null
        && ReferenceEquals(source, selectedEnemy)
        && IsActiveFor(selectedEnemy);

    internal static bool WouldNegateStatusCard(
        CardModel card,
        PileType? destinationPileType,
        AbstractModel source,
        AbstractModel observer = null,
        SolemnStrikePower expectedPower = null)
    {
        var power = CurrentScope.Value?.Power;
        return power is not null
            && (expectedPower is null || ReferenceEquals(power, expectedPower))
            && card?.Owner?.Creature?.IsPlayer == true
            && card.Type is CardType.Status or CardType.Curse
            && destinationPileType is PileType.Draw or PileType.Hand or PileType.Discard
            && IsSelectedEnemySource(
                AshBlossomActionNegation.ResolveObservedEffectSource(observer ?? expectedPower, source),
                power.Owner);
    }

    internal static bool ShouldNegateEnemyBenefit(MegaCrit.Sts2.Core.Entities.Creatures.Creature target) =>
        CurrentScope.Value is not null && target?.IsEnemy == true;

    internal static bool ShouldNegatePlayerEffect(MegaCrit.Sts2.Core.Entities.Creatures.Creature target) =>
        CurrentScope.Value is not null && target?.IsPlayer == true;

    internal static bool ShouldNegateEnemySummon(MegaCrit.Sts2.Core.Entities.Creatures.Creature creature) =>
        CurrentScope.Value is not null && creature?.IsEnemy == true;

    internal static async Task CompleteMoveAsync(Task moveTask, Scope scope)
    {
        try
        {
            await moveTask;
        }
        finally
        {
            await CompleteScopeAsync(scope);
        }
    }

    internal static async Task CompleteScopeAsync(Scope scope)
    {
        var owner = scope.Power.Owner;
        if (owner is not null && ReferenceEquals(owner.GetPower<SolemnStrikePower>(), scope.Power))
            await PowerCmd.Remove(scope.Power);
    }

    internal static void ResetForRunTransition()
    {
        CurrentScope.Value = null;
    }
}

internal static class AshBlossomActionNegation
{
    private static readonly AsyncLocal<Scope> CurrentScope = new();

    internal sealed class Scope(
        Creature source,
        AbstractModel model,
        Scope previous,
        bool isCallback = false,
        PowerModel nativeTemporaryCleanup = null) : IDisposable
    {
        internal Creature Source { get; } = source;
        internal AbstractModel Model { get; } = model;
        internal Scope Previous { get; } = previous;
        internal bool IsCallback { get; } = isCallback;
        internal PowerModel NativeTemporaryCleanup { get; } = nativeTemporaryCleanup;
        internal bool Completed { get; set; }

        public void Dispose()
        {
            DetachCaller(this);
            Completed = true;
        }
    }

    internal static Scope Enter(Creature source)
    {
        var scope = new Scope(source, source?.Monster, CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    internal static Scope EnterSource(AbstractModel model, System.Reflection.MethodBase callback = null)
    {
        // Even an unknown/null source is an explicit boundary: its effects
        // must not inherit an unrelated enemy from an outer attack.
        var scope = new Scope(ResolveSource(model), model, CurrentScope.Value,
            isCallback: true, nativeTemporaryCleanup: GetNativeTemporaryCleanup(model, callback));
        CurrentScope.Value = scope;
        return scope;
    }

    internal static void DetachCaller(Scope scope)
    {
        if (ReferenceEquals(CurrentScope.Value, scope))
            CurrentScope.Value = scope.Previous;
    }

    internal static Creature CurrentSource => CurrentScope.Value is { Completed: false } scope
        ? scope.Source
        : null;

    internal static bool IsCurrentPowerCleanup(PowerModel power) =>
        power is not null
        && CurrentScope.Value is { Completed: false } scope
        && ReferenceEquals(scope.Model, power);

    internal static bool IsCurrentLinkedPowerCleanup(PowerModel power, Creature target, decimal amount)
    {
        if (power is null || target is null
            || CurrentScope.Value is not { Completed: false, NativeTemporaryCleanup: { } temporary }
            || temporary is not ITemporaryPower linked
            || !ReferenceEquals(target, temporary.Owner)
            || target.Powers.Any(candidate => ReferenceEquals(candidate, temporary))
            || power.Id != linked.InternallyAppliedPower.Id)
        {
            return false;
        }

        // The native turn-end method removes its duration marker first, then
        // reverses exactly its own linked attribute. Both positive and negative
        // temporary attributes expire; adding or stacking them is not cleanup.
        var reversal = temporary.Type == PowerType.Buff ? -temporary.Amount : temporary.Amount;
        return amount == reversal;
    }

    private static PowerModel GetNativeTemporaryCleanup(
        AbstractModel model,
        System.Reflection.MethodBase callback)
    {
        if (callback?.Name != nameof(AbstractModel.AfterSideTurnEnd)
            || (callback.DeclaringType != typeof(TemporaryStrengthPower)
                && callback.DeclaringType != typeof(TemporaryDexterityPower)
                && callback.DeclaringType != typeof(TemporaryFocusPower)))
        {
            return null;
        }

        return model as PowerModel;
    }

    internal static Creature ResolveSource(AbstractModel source) => source switch
    {
        MonsterModel monster => monster.Creature,
        // A player-applied poison on this enemy remains the player's effect;
        // placing Ash on the enemy must not protect it from that poison.
        PowerModel power => power.Applier ?? power.Owner,
        CardModel card => card.Owner?.Creature,
        RelicModel relic => relic.Owner?.Creature,
        PotionModel potion => potion.Owner?.Creature,
        OrbModel orb => orb.Owner?.Creature,
        _ => null
    };

    internal static Creature ResolveObservedEffectSource(AbstractModel observer, AbstractModel source = null)
    {
        if (source is not null)
            return ResolveSource(source);

        var scope = CurrentScope.Value;
        // An observer did not cause the event it is handling. Nested base
        // callbacks can wrap the same model more than once; skip only those
        // wrappers, never an unrelated or explicitly unknown effect source.
        while (observer is not null && scope is { Completed: false, IsCallback: true }
            && ReferenceEquals(scope.Model, observer))
            scope = scope.Previous;
        return scope is { Completed: false } ? scope.Source : null;
    }

    private static Creature ResolveCommandSource(Creature source) =>
        CurrentScope.Value is { Completed: false, IsCallback: true } scope
            ? scope.Source
            : source ?? CurrentSource;

    internal static bool TryTriggerFor(AshBlossomPower power, Creature source) =>
        TryTriggerForResolvedSource(power, ResolveCommandSource(source));

    private static bool TryTriggerForResolvedSource(AshBlossomPower power, Creature source) =>
        power is not null
        && ReferenceEquals(source, power.Owner)
        && power.TryTriggerNegation();

    internal static bool TryNegateFrom(Creature source = null) =>
        TryNegateResolvedSource(ResolveCommandSource(source));

    private static bool TryNegateResolvedSource(Creature source) =>
        source?.IsEnemy == true
        && source.GetPower<AshBlossomPower>()?.TryTriggerNegation() == true;

    internal static bool ShouldNegateEnemyBenefit(Creature target) =>
        target is not null && TryNegateFrom();

    internal static bool ShouldNegatePlayerEffect(Creature target) =>
        target is not null && TryNegateFrom();

    internal static bool ShouldNegateEnemySummon(Creature creature) =>
        creature is not null && TryNegateFrom();

    internal static bool ShouldNegateStatusCard(
        CardModel card,
        PileType oldPileType,
        AbstractModel source = null,
        AshBlossomPower expectedPower = null)
    {
        var effectSource = GetStatusCardEffectSource(card, oldPileType, source, expectedPower);
        return effectSource is not null && (expectedPower is null
            ? TryNegateResolvedSource(effectSource)
            : TryTriggerForResolvedSource(expectedPower, effectSource));
    }

    internal static bool WouldNegateStatusCard(
        CardModel card,
        PileType oldPileType,
        AbstractModel source,
        AbstractModel observer)
    {
        var effectSource = GetStatusCardEffectSource(card, oldPileType, source, observer);
        return effectSource?.IsEnemy == true
            && effectSource.GetPower<AshBlossomPower>()?.CanTriggerNegation == true;
    }

    private static Creature GetStatusCardEffectSource(
        CardModel card,
        PileType oldPileType,
        AbstractModel source,
        AbstractModel observer)
    {
        if (card?.Owner?.Creature?.IsPlayer != true
            || card.Type is not (CardType.Status or CardType.Curse)
            || card.Pile?.Type is not (PileType.Draw or PileType.Hand or PileType.Discard)
            || oldPileType is PileType.Draw or PileType.Hand or PileType.Discard)
        {
            return null;
        }

        return ResolveObservedEffectSource(observer, source);
    }

    internal static Task CompleteScopeAsync(Scope scope)
    {
        scope.Dispose();
        return Task.CompletedTask;
    }

    internal static async Task CompleteSourceAsync(Task original, Scope scope)
    {
        try
        {
            if (original is not null)
                await original;
        }
        finally
        {
            scope.Dispose();
        }
    }

    internal static void ResetForRunTransition() => CurrentScope.Value = null;
}
