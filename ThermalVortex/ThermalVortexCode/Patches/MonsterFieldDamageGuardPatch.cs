using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using System.Threading;
using ThermalVortex.ThermalVortexCode;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(Creature), nameof(Creature.DamageBlockInternal), new Type[]
{
    typeof(decimal),
    typeof(ValueProp)
})]
internal static class RaCombatDamageWithoutMonsterGuardPatch
{
    private static void Prefix(Creature __instance, decimal __0, ValueProp __1)
    {
        MonsterFieldDamageGuard.RecordIncomingPlayerAttackDamage(__instance, __0);
    }
}

// Observe the native per-target result after block, field and HP-loss modifiers.
// Forwarding Damage overloads do not emit this hook again.
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageReceived))]
internal static class MonsterFieldPlayerHpImpactPatch
{
    private static void Prefix(DamageResult __4, ValueProp __5, Creature __6) =>
        MonsterFieldDamageGuard.PresentPlayerHpImpact(__4, __5, __6);
}

[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove))]
internal static class MonsterFieldAttackContextPatch
{
    private static void Prefix(MonsterModel __instance, ref IDisposable __state)
    {
        __state = MonsterFieldDamageGuard.EnterAttackContext(__instance);
    }

    private static void Postfix(IDisposable __state) => __state?.Dispose();

    private static Exception Finalizer(Exception __exception, IDisposable __state)
    {
        __state?.Dispose();
        return __exception;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(Creature),
    typeof(decimal),
    typeof(ValueProp),
    typeof(CardModel)
})]
internal static class MonsterFieldSingleCardSourceDamageGuardPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        Creature __1,
        decimal __2,
        ValueProp __3,
        CardModel __4,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        if (!MonsterFieldDamageGuard.CanGuard(__1, null))
            return true;

        __result = MonsterFieldDamageGuard.GuardSingleAsync(__0, __1, __2, __3, __4);
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(Creature),
    typeof(decimal),
    typeof(ValueProp),
    typeof(Creature)
})]
internal static class MonsterFieldSingleDamageGuardPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        Creature __1,
        decimal __2,
        ValueProp __3,
        Creature __4,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        if (!MonsterFieldDamageGuard.CanGuard(__1, __4))
            return true;

        __result = MonsterFieldDamageGuard.GuardSingleAsync(__0, __1, __2, __3, __4);
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(Creature),
    typeof(decimal),
    typeof(ValueProp),
    typeof(Creature),
    typeof(CardModel)
})]
internal static class MonsterFieldSingleCardDamageGuardPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        Creature __1,
        decimal __2,
        ValueProp __3,
        Creature __4,
        CardModel __5,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        if (!MonsterFieldDamageGuard.CanGuard(__1, __4))
            return true;

        __result = MonsterFieldDamageGuard.GuardSingleAsync(__0, __1, __2, __3, __4, __5);
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(IEnumerable<Creature>),
    typeof(decimal),
    typeof(ValueProp),
    typeof(Creature)
})]
internal static class MonsterFieldMultiDamageGuardPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        IEnumerable<Creature> __1,
        decimal __2,
        ValueProp __3,
        Creature __4,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        var targets = __1?.ToList() ?? [];
        if (!targets.Any(target => MonsterFieldDamageGuard.CanGuard(target, __4)))
            return true;

        __result = MonsterFieldDamageGuard.GuardMultipleAsync(__0, targets, __2, __3, __4);
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(IEnumerable<Creature>),
    typeof(decimal),
    typeof(ValueProp),
    typeof(Creature),
    typeof(CardModel)
})]
internal static class MonsterFieldMultiCardDamageGuardPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        IEnumerable<Creature> __1,
        decimal __2,
        ValueProp __3,
        Creature __4,
        CardModel __5,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        var targets = __1?.ToList() ?? [];
        if (!targets.Any(target => MonsterFieldDamageGuard.CanGuard(target, __4)))
            return true;

        __result = MonsterFieldDamageGuard.GuardMultipleAsync(__0, targets, __2, __3, __4, __5);
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(Creature),
    typeof(DamageVar),
    typeof(CardModel)
})]
internal static class MonsterFieldSingleDamageVarCardSourceGuardPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        Creature __1,
        DamageVar __2,
        CardModel __3,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        if (!MonsterFieldDamageGuard.CanGuard(__1, null))
            return true;

        __result = MonsterFieldDamageGuard.GuardSingleAsync(__0, __1, __2, __3);
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(Creature),
    typeof(DamageVar),
    typeof(Creature)
})]
internal static class MonsterFieldSingleDamageVarGuardPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        Creature __1,
        DamageVar __2,
        Creature __3,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        if (!MonsterFieldDamageGuard.CanGuard(__1, __3))
            return true;

        __result = MonsterFieldDamageGuard.GuardSingleAsync(__0, __1, __2, __3);
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(Creature),
    typeof(DamageVar),
    typeof(Creature),
    typeof(CardModel)
})]
internal static class MonsterFieldSingleDamageVarCardGuardPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        Creature __1,
        DamageVar __2,
        Creature __3,
        CardModel __4,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        if (!MonsterFieldDamageGuard.CanGuard(__1, __3))
            return true;

        __result = MonsterFieldDamageGuard.GuardSingleAsync(__0, __1, __2, __3, __4);
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(IEnumerable<Creature>),
    typeof(DamageVar),
    typeof(Creature)
})]
internal static class MonsterFieldMultiDamageVarGuardPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        IEnumerable<Creature> __1,
        DamageVar __2,
        Creature __3,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        var targets = __1?.ToList() ?? [];
        if (!targets.Any(target => MonsterFieldDamageGuard.CanGuard(target, __3)))
            return true;

        __result = MonsterFieldDamageGuard.GuardMultipleAsync(__0, targets, __2, __3);
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(IEnumerable<Creature>),
    typeof(DamageVar),
    typeof(Creature),
    typeof(CardModel)
})]
internal static class MonsterFieldMultiDamageVarCardGuardPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        IEnumerable<Creature> __1,
        DamageVar __2,
        Creature __3,
        CardModel __4,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        var targets = __1?.ToList() ?? [];
        if (!targets.Any(target => MonsterFieldDamageGuard.CanGuard(target, __3)))
            return true;

        __result = MonsterFieldDamageGuard.GuardMultipleAsync(__0, targets, __2, __3, __4);
        return false;
    }
}

internal static class MonsterFieldDamageGuard
{
    private static readonly AsyncLocal<MonsterModel> CurrentAttack = new();
    private static readonly AsyncLocal<NativeDamageGuardContext> CurrentNativeGuard = new();
    private static readonly AsyncLocal<int> NativeDamageCallDepth = new();
    private static readonly AsyncLocal<int> NativeBlockLossCallDepth = new();
    private static readonly AsyncLocal<AttackRevivalContext> CurrentAttackRevival = new();
    private static readonly AsyncLocal<int> AttackBlockLossDepth = new();
    private static readonly AsyncLocal<int> SuppressionDepth = new();

    internal static bool HasActiveAttackContext =>
        CurrentAttack.Value?.Creature?.IsEnemy == true;

    internal static bool IsConsumingAttackBlock => AttackBlockLossDepth.Value > 0;

    internal static void ResetForRunTransition()
    {
        CurrentAttack.Value = null;
        CurrentNativeGuard.Value = null;
        NativeDamageCallDepth.Value = 0;
        NativeBlockLossCallDepth.Value = 0;
        CurrentAttackRevival.Value?.Dispose();
        PhoenixRevivalSnapshot.ResetAll();
        CurrentAttackRevival.Value = null;
        AttackBlockLossDepth.Value = 0;
        SuppressionDepth.Value = 0;
    }

    internal static bool ShouldTrack(MonsterModel monster)
    {
        if (monster?.Creature?.IsEnemy != true)
            return false;

        var action = EnemyActionClassifier.Classify(monster.NextMove);
        return (action.Kind & EnemyActionKind.Attack) != 0;
    }

    internal static IDisposable EnterAttackContext(MonsterModel monster)
    {
        if (!ShouldTrack(monster))
            return null;

        var previousAttack = CurrentAttack.Value;
        var previousAttackRevival = CurrentAttackRevival.Value;
        CurrentAttack.Value = monster;
        CurrentAttackRevival.Value = new AttackRevivalContext();
        return new RestoreAttackContext(previousAttack, previousAttackRevival);
    }

    internal static IDisposable PushDiagnosticAttack(MonsterModel monster, bool useIntentDamage = false)
    {
        var previousAttack = CurrentAttack.Value;
        var previousAttackRevival = CurrentAttackRevival.Value;
        CurrentAttack.Value = monster;
        CurrentAttackRevival.Value = new AttackRevivalContext();
        return new RestoreAttackContext(previousAttack, previousAttackRevival);
    }

    internal static bool CanGuard(Creature target, Creature source)
    {
        if (SuppressionDepth.Value > 0 || target?.IsPlayer != true)
            return false;

        var currentAttack = CurrentAttack.Value;
        if (currentAttack?.Creature?.IsEnemy != true)
            return false;

        if (source is not null && !ReferenceEquals(source, currentAttack.Creature))
            return false;

        return MonsterFieldService.Count(target.Player) > 0
            || GetAttackRevival(target.Player) is not null;
    }

    internal static void PresentPlayerHpImpact(DamageResult result, ValueProp props, Creature source)
    {
        // Native LoseHpInternal records the actual before/after HP difference
        // in UnblockedDamage, after both field absorption and HP-loss modifiers.
        // A lethal hit belongs to the death presentation, not an idle recovery.
        var target = result?.Receiver;
        if (result is null || result.UnblockedDamage <= 0 || result.WasTargetKilled
            || target?.IsAlive != true)
            return;

        if (!ShouldPresentPlayerImpact(target, props, source))
            return;

        ThermalVortexPlayerImpactVfx.PlayCharacterImpact(target);
    }

    private static bool ShouldPresentPlayerImpact(Creature target, ValueProp props, Creature source)
    {
        // GuardNativeDamageAsync suppresses recursive routing, but its final
        // native HP-loss result still needs the same presentation as any hit.
        if (target?.IsPlayer != true
            || (props & ValueProp.SkipHurtAnim) == ValueProp.SkipHurtAnim)
        {
            return false;
        }

        if (source is not null)
            return source.IsEnemy;

        return CurrentAttack.Value?.Creature?.IsEnemy == true;
    }

    internal static void RecordDestroyedPhoenix(
        Player player, PhoenixRevivalSnapshot phoenix, int fullAttackDamage, object segmentToken)
    {
        if (phoenix is null)
            return;
        if (CurrentAttackRevival.Value?.Record(player, phoenix, fullAttackDamage, segmentToken) == true)
        {
            MainFile.Logger.Info(
                $"RaRevival phoenix recorded damage={fullAttackDamage} pile={phoenix.Card.Pile?.Type.ToString() ?? "null"} playerHp={player?.Creature?.CurrentHp ?? 0}");
        }
        else
            phoenix.Dispose();
    }

    internal static AttackRevivalEntry GetAttackRevival(Player player) =>
        CurrentAttackRevival.Value?.Find(player);

    internal static void RecordIncomingPlayerAttackDamage(Creature target, decimal damage, bool alreadyRouting = false)
    {
        if (SuppressionDepth.Value > 0 && !alreadyRouting
            || target?.IsPlayer != true
            || CurrentAttack.Value?.Creature?.IsEnemy != true)
        {
            return;
        }

        var amount = Math.Max(0, (int)Math.Ceiling(damage));
        if (amount <= 0)
            return;

        try
        {
            target.Player?.GetRelic<ThermalVortexCore>()?.RecordDamageReceivedThisCombat(
                CurrentAttack.Value?.Creature,
                amount);
        }
        catch
        {
            // Damage can be resolved while combat ownership is being torn down.
        }
    }

    internal static Task<IEnumerable<DamageResult>> GuardSingleAsync(
        PlayerChoiceContext ctx,
        Creature target,
        decimal damage,
        ValueProp props,
        CardModel card) =>
        GuardSingleCoreAsync(
            ctx,
            target,
            damage,
            props,
            (remainingDamage, residualProps) =>
                CreatureCmd.Damage(ctx, target, remainingDamage, residualProps, card));

    internal static Task<IEnumerable<DamageResult>> GuardSingleAsync(
        PlayerChoiceContext ctx,
        Creature target,
        decimal damage,
        ValueProp props,
        Creature source) =>
        GuardSingleCoreAsync(
            ctx,
            target,
            damage,
            props,
            (remainingDamage, residualProps) =>
                CreatureCmd.Damage(ctx, target, remainingDamage, residualProps, source));

    internal static Task<IEnumerable<DamageResult>> GuardSingleAsync(
        PlayerChoiceContext ctx,
        Creature target,
        decimal damage,
        ValueProp props,
        Creature source,
        CardModel card) =>
        GuardSingleCoreAsync(
            ctx,
            target,
            damage,
            props,
            (remainingDamage, residualProps) =>
                CreatureCmd.Damage(ctx, target, remainingDamage, residualProps, source, card));

    internal static Task<IEnumerable<DamageResult>> GuardSingleAsync(
        PlayerChoiceContext ctx,
        Creature target,
        DamageVar damage,
        CardModel card) =>
        GuardSingleCoreAsync(
            ctx,
            target,
            ResolveDamage(damage),
            ResolveProps(damage),
            (remainingDamage, residualProps) =>
                CreatureCmd.Damage(ctx, target, remainingDamage, residualProps, card));

    internal static Task<IEnumerable<DamageResult>> GuardSingleAsync(
        PlayerChoiceContext ctx,
        Creature target,
        DamageVar damage,
        Creature source) =>
        GuardSingleCoreAsync(
            ctx,
            target,
            ResolveDamage(damage),
            ResolveProps(damage),
            (remainingDamage, residualProps) =>
                CreatureCmd.Damage(ctx, target, remainingDamage, residualProps, source));

    internal static Task<IEnumerable<DamageResult>> GuardSingleAsync(
        PlayerChoiceContext ctx,
        Creature target,
        DamageVar damage,
        Creature source,
        CardModel card) =>
        GuardSingleCoreAsync(
            ctx,
            target,
            ResolveDamage(damage),
            ResolveProps(damage),
            (remainingDamage, residualProps) =>
                CreatureCmd.Damage(ctx, target, remainingDamage, residualProps, source, card));

    private static Task<IEnumerable<DamageResult>> GuardSingleCoreAsync(
        PlayerChoiceContext ctx,
        Creature target,
        decimal damage,
        ValueProp props,
        Func<decimal, ValueProp, Task<IEnumerable<DamageResult>>> applyNativeDamage,
        object attackSegmentToken = null) =>
        GuardNativeDamageAsync(ctx, [target], () => applyNativeDamage(damage, props), attackSegmentToken);

    private static async Task<IEnumerable<DamageResult>> GuardNativeDamageAsync(
        PlayerChoiceContext ctx,
        IReadOnlyList<Creature> targets,
        Func<Task<IEnumerable<DamageResult>>> applyNativeDamage,
        object attackSegmentToken = null)
    {
        var previousGuard = CurrentNativeGuard.Value;
        var previousDepth = NativeDamageCallDepth.Value;
        var token = attackSegmentToken ?? new object();
        var context = new NativeDamageGuardContext();
        var attackRevival = CurrentAttackRevival.Value;
        foreach (var target in targets.Where(target => CanGuard(target, CurrentAttack.Value?.Creature)))
        {
            context.States[target] = new NativeDamageGuardState(ctx, target, CurrentAttack.Value, token);
            attackRevival?.BeginSegment(target.Player, token);
        }
        CurrentNativeGuard.Value = context;
        NativeDamageCallDepth.Value = 0;
        using var revival = RaRevivalService.EnterAttackResolution(
            new AttackRevivalEntry(player => attackRevival?.Find(player, token)));
        using var _ = Suppress();
        try
        {
            // Native damage owns per-target modifiers and all damage events.
            // The narrowly scoped hook below inserts the field after native
            // BeforeDamageReceived, without changing the attack's properties.
            return await applyNativeDamage();
        }
        finally
        {
            foreach (var player in context.States.Keys.Select(target => target.Player).Distinct())
                attackRevival?.EndSegment(player, token);
            CurrentNativeGuard.Value = previousGuard;
            NativeDamageCallDepth.Value = previousDepth;
        }
    }

    internal static Task<IEnumerable<DamageResult>> GuardMultipleAsync(
        PlayerChoiceContext ctx,
        IReadOnlyList<Creature> targets,
        decimal damage,
        ValueProp props,
        Creature source) =>
        GuardNativeDamageAsync(ctx, targets, () => CreatureCmd.Damage(ctx, targets, damage, props, source));

    internal static Task<IEnumerable<DamageResult>> GuardMultipleAsync(
        PlayerChoiceContext ctx,
        IReadOnlyList<Creature> targets,
        decimal damage,
        ValueProp props,
        Creature source,
        CardModel card) =>
        GuardNativeDamageAsync(ctx, targets, () => CreatureCmd.Damage(ctx, targets, damage, props, source, card));

    internal static Task<IEnumerable<DamageResult>> GuardMultipleAsync(
        PlayerChoiceContext ctx,
        IReadOnlyList<Creature> targets,
        DamageVar damage,
        Creature source) =>
        GuardNativeDamageAsync(ctx, targets, () => CreatureCmd.Damage(ctx, targets, damage, source));

    internal static Task<IEnumerable<DamageResult>> GuardMultipleAsync(
        PlayerChoiceContext ctx,
        IReadOnlyList<Creature> targets,
        DamageVar damage,
        Creature source,
        CardModel card) =>
        GuardNativeDamageAsync(ctx, targets, () => CreatureCmd.Damage(ctx, targets, damage, source, card));

    private static async Task<MonsterFieldAttackDamageResult> GuardDamage(
        PlayerChoiceContext ctx,
        Creature target,
        decimal damage,
        ValueProp props,
        AbstractModel source,
        object attackSegmentToken)
    {
        // Capture draw obligations before this hit can destroy the source
        // monster and remove its power. Later hits re-check the live sources,
        // so only attack segments that occurred while the ability was active
        // are retained.
        EnemyActionDrawResolution.CaptureAttackSegment(target?.Player, attackSegmentToken);

        var remainingDamage = ToDamageInt(damage);
        if (remainingDamage <= 0)
            return default;

        // Prevent nested leave/block effects from being routed as this attack.
        using var nestedRoutingSuppression = Suppress();

        var canBlock = (props & ValueProp.Unblockable) != ValueProp.Unblockable;
        if (canBlock)
            remainingDamage = await AbsorbBlock(target, remainingDamage, "before_monsters");

        var result = await MonsterFieldHealthService.AbsorbAttackDamageDetailed(
            ctx,
            target.Player,
            remainingDamage,
            source);
        // Keep the complete hit, before block and field absorption, with this
        // segment's Phoenix. Residual player damage is not the revival amount.
        RecordDestroyedPhoenix(target.Player, result.DestroyedPhoenix, ToDamageInt(damage), attackSegmentToken);

        // Exhaust hooks resolve while battle-destroyed monsters leave the field.
        // Re-read block here so Circuit Talisman Beast's consume trigger can
        // protect against the same hit that destroyed it.
        if (!canBlock || result.RemainingDamage <= 0)
            return result;

        var remainingAfterTriggeredBlock = await AbsorbBlock(
            target,
            result.RemainingDamage,
            "after_monster_triggers");
        return new MonsterFieldAttackDamageResult(
            remainingAfterTriggeredBlock,
            result.DestroyedPhoenix);
    }

    private static async Task<int> AbsorbBlock(
        Creature target,
        int incomingDamage,
        string phase)
    {
        var remainingDamage = Math.Max(0, incomingDamage);
        var blockAbsorbed = Math.Min(Math.Max(0, target?.Block ?? 0), remainingDamage);
        if (blockAbsorbed <= 0)
            return remainingDamage;

        var guard = GetNativeGuard(target);
        if (guard is { Preparing: true } && ReferenceEquals(guard.Target, target))
        {
            guard.BlockAbsorbed += blockAbsorbed;
            guard.BlockWasBroken |= blockAbsorbed >= target.Block;
        }
        using (EnterAttackBlockLoss())
            await CreatureCmd.LoseBlock(target, blockAbsorbed);
        ThermalVortexPlayerImpactVfx.PlayBlockedImpact(target);
        remainingDamage -= blockAbsorbed;
        MainFile.Logger.Info(
            $"MonsterFieldGuard blockPhase={phase} blockAbsorbed={blockAbsorbed} remainingAfterBlock={remainingDamage}");
        return remainingDamage;
    }

    private static int ToDamageInt(decimal damage) =>
        Math.Max(0, (int)damage);

    private static decimal ResolveDamage(DamageVar damage) =>
        Math.Max(0, damage?.IntValue ?? 0);

    private static ValueProp ResolveProps(DamageVar damage) =>
        damage?.Props ?? ValueProp.Unpowered;

    private static IDisposable Suppress()
    {
        SuppressionDepth.Value++;
        return new SuppressionHandle();
    }

    private static IDisposable EnterAttackBlockLoss()
    {
        AttackBlockLossDepth.Value++;
        return new AttackBlockLossHandle();
    }

    private sealed class AttackBlockLossHandle : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            AttackBlockLossDepth.Value = Math.Max(0, AttackBlockLossDepth.Value - 1);
        }
    }

    private sealed class SuppressionHandle : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            SuppressionDepth.Value = Math.Max(0, SuppressionDepth.Value - 1);
        }
    }

    internal static NativeDamageGuardState BeginNativeBeforeDamage(
        PlayerChoiceContext ctx,
        Creature target,
        Creature dealer,
        decimal damage)
    {
        var state = GetNativeGuard(target);
        if (state is null && NativeDamageCallDepth.Value == 1
            && CurrentNativeGuard.Value is not null
            && target?.IsPlayer == true
            && ReferenceEquals(CurrentAttack.Value?.Creature, dealer))
        {
            // A group can also contain a player without field monsters. Native
            // damage still owns that target, but the outer routing scope must
            // not suppress their incoming-attack accounting.
            RecordIncomingPlayerAttackDamage(target, damage, alreadyRouting: true);
        }
        if (state is null || state.Claimed
            || !ReferenceEquals(state.Context, ctx)
            || !ReferenceEquals(state.Target, target)
            || dealer is not null && !ReferenceEquals(state.Source?.Creature, dealer))
        {
            return null;
        }

        // Claim before running native listeners, since thorns can cause nested
        // damage while BeforeDamageReceived is still awaiting completion.
        state.Claimed = true;
        return state;
    }

    internal static async Task CompleteNativeBeforeDamage(
        Task nativeBeforeDamage,
        NativeDamageGuardState state,
        decimal damage,
        ValueProp props)
    {
        await nativeBeforeDamage;
        if (state.Target?.IsAlive != true || state.Target.CombatState is null
            || CombatManager.Instance?.IsOverOrEnding == true)
            return;

        RecordIncomingPlayerAttackDamage(state.Target, damage, alreadyRouting: true);
        state.ModifiedDamage = damage;
        state.Props = props;
        state.Preparing = true;
        try
        {
            var result = await GuardDamage(
                state.Context, state.Target, damage, props, state.Source, state.AttackSegmentToken);
            state.Absorbed = ToDamageInt(damage) - result.RemainingDamage;
            state.BlockStepPending = true;
        }
        finally
        {
            state.Preparing = false;
        }
    }

    internal static bool TryConsumeNativeBlock(
        Creature target,
        decimal damage,
        ValueProp props,
        out decimal absorbed)
    {
        absorbed = 0;
        var state = GetNativeGuard(target);
        if (state?.BlockStepPending != true
            || !ReferenceEquals(state.Target, target)
            || damage != state.ModifiedDamage
            || props != state.Props)
        {
            return false;
        }

        state.BlockStepPending = false;
        state.BlockStepConsumed = true;
        absorbed = state.Absorbed;
        return true;
    }

    internal static NativeDamageGuardState GetCompletedNativeGuard(Creature target) =>
        GetNativeGuard(target) is { BlockStepConsumed: true } state ? state : null;

    private static NativeDamageGuardState GetNativeGuard(Creature target)
    {
        return NativeDamageCallDepth.Value == 1
            && target is not null
            && CurrentNativeGuard.Value is { } context
            && context.States.TryGetValue(target, out var state)
                ? state : null;
    }

    internal static bool SuppressNativeBlockBroken(Creature target)
    {
        var state = GetCompletedNativeGuard(target);
        if (NativeBlockLossCallDepth.Value > 0 || state?.BlockBreakNotificationPending != true)
            return false;

        state.BlockBreakNotificationPending = false;
        return true;
    }

    internal static int EnterNativeDamageCall()
    {
        var previous = NativeDamageCallDepth.Value;
        if (CurrentNativeGuard.Value is not null)
            NativeDamageCallDepth.Value = previous + 1;
        return previous;
    }

    internal static void RestoreNativeDamageCall(int previous) =>
        NativeDamageCallDepth.Value = previous;

    internal static int EnterNativeBlockLossCall()
    {
        var previous = NativeBlockLossCallDepth.Value;
        if (CurrentNativeGuard.Value is not null)
            NativeBlockLossCallDepth.Value = previous + 1;
        return previous;
    }

    internal static void RestoreNativeBlockLossCall(int previous) =>
        NativeBlockLossCallDepth.Value = previous;

    private sealed class NativeDamageGuardContext
    {
        internal Dictionary<Creature, NativeDamageGuardState> States { get; } =
            new(new ReferenceComparer<Creature>());
    }

    internal sealed class NativeDamageGuardState(
        PlayerChoiceContext context,
        Creature target,
        MonsterModel source,
        object attackSegmentToken)
    {
        internal PlayerChoiceContext Context { get; } = context;
        internal Creature Target { get; } = target;
        internal MonsterModel Source { get; } = source;
        internal object AttackSegmentToken { get; } = attackSegmentToken;
        internal bool Claimed { get; set; }
        internal bool Preparing { get; set; }
        internal bool BlockStepPending { get; set; }
        internal bool BlockStepConsumed { get; set; }
        internal decimal ModifiedDamage { get; set; }
        internal ValueProp Props { get; set; }
        internal int Absorbed { get; set; }
        internal int BlockAbsorbed { get; set; }
        internal bool BlockWasBroken { get; set; }
        internal bool BlockBreakNotificationPending { get; set; }
    }

    private sealed class AttackRevivalContext : IDisposable
    {
        private readonly Dictionary<Player, AttackRevivalEntry> _destroyedPhoenixByPlayer = [];
        private readonly object _sync = new();

        internal AttackRevivalEntry BeginSegment(Player player, object token)
        {
            if (player is null)
                return null;

            lock (_sync)
            {
                if (_destroyedPhoenixByPlayer.TryGetValue(player, out var previous))
                {
                    if (ReferenceEquals(previous.SegmentToken, token))
                        return previous;
                    previous.Dispose();
                }
                var entry = new AttackRevivalEntry(player, token);
                _destroyedPhoenixByPlayer[player] = entry;
                return entry;
            }
        }

        internal bool Record(Player player, PhoenixRevivalSnapshot phoenix, int fullAttackDamage, object token)
        {
            if (player is null || phoenix is null || fullAttackDamage <= 0)
                return false;

            lock (_sync)
            {
                return _destroyedPhoenixByPlayer.TryGetValue(player, out var entry)
                    && ReferenceEquals(entry.SegmentToken, token)
                    && entry.TryRecordPhoenix(phoenix, fullAttackDamage);
            }
        }

        internal AttackRevivalEntry Find(Player player, object token = null)
        {
            if (player is null)
                return null;

            lock (_sync)
            {
                return _destroyedPhoenixByPlayer.TryGetValue(player, out var entry)
                    && (token is null || ReferenceEquals(entry.SegmentToken, token))
                    && entry.CanConsume(player)
                    ? entry
                    : null;
            }
        }

        internal void EndSegment(Player player, object token)
        {
            if (player is null)
                return;
            lock (_sync)
            {
                if (_destroyedPhoenixByPlayer.TryGetValue(player, out var entry)
                    && ReferenceEquals(entry.SegmentToken, token))
                {
                    _destroyedPhoenixByPlayer.Remove(player);
                    entry.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                foreach (var entry in _destroyedPhoenixByPlayer.Values)
                    entry.Dispose();
                _destroyedPhoenixByPlayer.Clear();
            }
        }
    }

    internal sealed class AttackRevivalEntry : IDisposable
    {
        private int _consumed;
        private PhoenixRevivalSnapshot _phoenix;
        private readonly Func<Player, AttackRevivalEntry> _resolvePlayer;

        internal Player Player { get; }
        internal object SegmentToken { get; }
        internal int FullAttackDamage { get; private set; }

        internal AttackRevivalEntry(Player player, object segmentToken)
        {
            Player = player;
            SegmentToken = segmentToken;
        }

        internal AttackRevivalEntry(Func<Player, AttackRevivalEntry> resolvePlayer) =>
            _resolvePlayer = resolvePlayer;

        internal bool TryRecordPhoenix(PhoenixRevivalSnapshot phoenix, int fullAttackDamage)
        {
            if (_phoenix is not null || phoenix?.CanRevive != true || fullAttackDamage <= 0
                || !ReferenceEquals(phoenix.Player, Player) || Volatile.Read(ref _consumed) != 0)
                return false;
            _phoenix = phoenix;
            FullAttackDamage = fullAttackDamage;
            return true;
        }

        internal bool CanConsume(Player player) =>
            _resolvePlayer is not null
                ? _resolvePlayer(player)?.CanConsume(player) == true
                : player is not null
            && ReferenceEquals(Player, player)
            && _phoenix?.CanRevive == true
            && FullAttackDamage > 0
            && Volatile.Read(ref _consumed) == 0;

        internal bool TryConsume(Player player, out PhoenixRevivalSnapshot phoenix, out int fullAttackDamage)
        {
            phoenix = null;
            fullAttackDamage = 0;
            if (_resolvePlayer is not null)
                return _resolvePlayer(player)?.TryConsume(player, out phoenix, out fullAttackDamage) == true;

            if (!CanConsume(player)
                || Interlocked.CompareExchange(ref _consumed, 1, 0) != 0)
            {
                return false;
            }

            phoenix = Interlocked.Exchange(ref _phoenix, null);
            if (phoenix is null)
                return false;
            fullAttackDamage = FullAttackDamage;
            return true;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _consumed, 1);
            Interlocked.Exchange(ref _phoenix, null)?.Dispose();
        }
    }

    private sealed class RestoreAttackContext(
        MonsterModel previousAttack,
        AttackRevivalContext previousAttackRevival) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CurrentAttack.Value = previousAttack;
            CurrentAttackRevival.Value = previousAttackRevival;
        }
    }
}
