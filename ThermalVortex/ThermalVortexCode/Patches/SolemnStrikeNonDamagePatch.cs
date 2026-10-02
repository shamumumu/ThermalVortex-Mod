using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;
using System.Threading;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock), new Type[]
{
    typeof(Creature), typeof(BlockVar), typeof(CardPlay), typeof(bool)
})]
internal static class SolemnStrikeBlockVarPatch
{
    [HarmonyPrefix]
    private static bool NegateBlock(Creature creature, ref Task<decimal> __result)
    {
        if (BrilliantRebootBlockLock.IsActive(creature))
        {
            __result = Task.FromResult(0m);
            return false;
        }

        // Ash waits for the numeric overload so zero block cannot spend a charge.
        if (!SolemnStrikeActionNegation.ShouldNegateEnemyBenefit(creature))
            return true;

        __result = Task.FromResult(0m);
        return false;
    }

}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock), new Type[]
{
    typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardPlay), typeof(bool)
})]
internal static class SolemnStrikeDecimalBlockPatch
{
    [HarmonyPrefix]
    private static bool NegateBlock(
        Creature creature,
        decimal amount,
        ref Task<decimal> __result,
        out bool __state)
    {
        __state = amount > 0;
        if (__state && BrilliantRebootBlockLock.IsActive(creature))
        {
            __state = false;
            __result = Task.FromResult(0m);
            return false;
        }

        if (!__state
            || (!SolemnStrikeActionNegation.ShouldNegateEnemyBenefit(creature)
                && !AshBlossomActionNegation.ShouldNegateEnemyBenefit(creature)))
            return true;

        __result = Task.FromResult(0m);
        return false;
    }

    [HarmonyPostfix]
    private static void CaptureBlock(
        Creature creature,
        bool __state,
        bool __runOriginal,
        ref Task<decimal> __result)
    {
        if (__state
            && __runOriginal
            && __result is not null
            && !EnemyActionDrawCommandCapture.IsNestedEnemyBenefit)
        {
            __result = EnemyActionDrawCommandCapture.AfterEnemyBenefitEffect(__result, creature);
        }
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.GainBlockInternal), [typeof(decimal)])]
internal static class BrilliantRebootInternalBlockPatch
{
    [HarmonyPrefix]
    private static bool PreventDirectBlockGain(Creature __instance, decimal amount) =>
        amount <= 0 || !BrilliantRebootBlockLock.IsActive(__instance);
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Heal), new Type[]
{
    typeof(Creature), typeof(decimal), typeof(bool)
})]
internal static class SolemnStrikeHealPatch
{
    [HarmonyPrefix]
    private static bool NegateHeal(
        Creature creature,
        decimal amount,
        ref Task __result,
        out bool __state)
    {
        __state = amount > 0 && creature.CurrentHp < creature.MaxHp;
        if (!__state
            || (!SolemnStrikeActionNegation.ShouldNegateEnemyBenefit(creature)
                && !AshBlossomActionNegation.ShouldNegateEnemyBenefit(creature)))
            return true;

        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPostfix]
    private static void CaptureHeal(
        Creature creature,
        bool __state,
        bool __runOriginal,
        ref Task __result)
    {
        if (__state
            && __runOriginal
            && __result is not null
            && !EnemyActionDrawCommandCapture.IsNestedEnemyBenefit)
        {
            __result = EnemyActionDrawCommandCapture.AfterEnemyBenefitEffect(__result, creature);
        }
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainMaxHp), new Type[]
{
    typeof(Creature), typeof(decimal)
})]
internal static class SolemnStrikeGainMaxHpPatch
{
    [HarmonyPrefix]
    private static bool NegateMaxHpGain(
        Creature creature,
        decimal amount,
        ref Task __result,
        out EnemyActionDrawCommandCapture.EnemyBenefitScope __state)
    {
        __state = null;
        if (creature is null || amount <= 0)
            return true;

        // Ash is bound to the acting enemy, irrespective of the recipient's
        // side. Preserve Solemn Strike's original enemy-benefit scope below.
        if (!creature.IsEnemy)
        {
            if (!AshBlossomActionNegation.ShouldNegateEnemyBenefit(creature))
                return true;
            __result = Task.CompletedTask;
            return false;
        }

        if (!SolemnStrikeActionNegation.ShouldNegateEnemyBenefit(creature)
            && !AshBlossomActionNegation.ShouldNegateEnemyBenefit(creature))
        {
            __state = EnemyActionDrawCommandCapture.EnterEnemyBenefit();
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPostfix]
    private static void CaptureMaxHpGain(
        Creature creature,
        bool __runOriginal,
        EnemyActionDrawCommandCapture.EnemyBenefitScope __state,
        ref Task __result)
    {
        if (__state is null)
            return;

        EnemyActionDrawCommandCapture.DetachCaller(__state);
        if (__runOriginal && __result is not null)
            __result = EnemyActionDrawCommandCapture.AfterEnemyBenefitEffect(__result, creature);
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.SetMaxHp), new Type[]
{
    typeof(Creature), typeof(decimal)
})]
internal static class SolemnStrikeSetMaxHpPatch
{
    [HarmonyPrefix]
    private static bool NegateMaxHpChange(
        Creature creature,
        decimal amount,
        ref Task<decimal> __result,
        out bool __state)
    {
        __state = amount > creature.MaxHp;
        if (!__state
            || (!SolemnStrikeActionNegation.ShouldNegateEnemyBenefit(creature)
                && !AshBlossomActionNegation.ShouldNegateEnemyBenefit(creature)))
            return true;

        __result = Task.FromResult((decimal)creature.MaxHp);
        return false;
    }

    [HarmonyPostfix]
    private static void CaptureMaxHpChange(
        Creature creature,
        bool __state,
        bool __runOriginal,
        ref Task<decimal> __result)
    {
        if (__state
            && __runOriginal
            && __result is not null
            && !EnemyActionDrawCommandCapture.IsNestedEnemyBenefit)
        {
            __result = EnemyActionDrawCommandCapture.AfterEnemyBenefitEffect(__result, creature);
        }
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.SetMaxAndCurrentHp), new Type[]
{
    typeof(Creature), typeof(decimal)
})]
internal static class SolemnStrikeSetMaxAndCurrentHpPatch
{
    [HarmonyPrefix]
    private static bool NegateHpChange(
        Creature creature,
        decimal amount,
        ref Task __result,
        out EnemyActionDrawCommandCapture.EnemyBenefitScope __state)
    {
        __state = null;
        if (creature is null
            || (amount <= creature.MaxHp && amount <= creature.CurrentHp))
        {
            return true;
        }

        if (!creature.IsEnemy)
        {
            if (!AshBlossomActionNegation.ShouldNegateEnemyBenefit(creature))
                return true;
            __result = Task.CompletedTask;
            return false;
        }

        if (!SolemnStrikeActionNegation.ShouldNegateEnemyBenefit(creature)
            && !AshBlossomActionNegation.ShouldNegateEnemyBenefit(creature))
        {
            __state = EnemyActionDrawCommandCapture.EnterEnemyBenefit();
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPostfix]
    private static void CaptureHpChange(
        Creature creature,
        bool __runOriginal,
        EnemyActionDrawCommandCapture.EnemyBenefitScope __state,
        ref Task __result)
    {
        if (__state is null)
            return;

        EnemyActionDrawCommandCapture.DetachCaller(__state);
        if (__runOriginal && __result is not null)
            __result = EnemyActionDrawCommandCapture.AfterEnemyBenefitEffect(__result, creature);
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.SetCurrentHp), new Type[]
{
    typeof(Creature), typeof(decimal)
})]
internal static class SolemnStrikeCurrentHpPatch
{
    [HarmonyPrefix]
    private static bool NegateHpIncrease(
        Creature creature,
        decimal amount,
        ref Task __result,
        out bool __state)
    {
        __state = amount > creature.CurrentHp;
        if (!__state
            || (!SolemnStrikeActionNegation.ShouldNegateEnemyBenefit(creature)
                && !AshBlossomActionNegation.ShouldNegateEnemyBenefit(creature)))
        {
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPostfix]
    private static void CaptureHpIncrease(
        Creature creature,
        bool __state,
        bool __runOriginal,
        ref Task __result)
    {
        if (__state
            && __runOriginal
            && __result is not null
            && !EnemyActionDrawCommandCapture.IsNestedEnemyBenefit)
        {
            __result = EnemyActionDrawCommandCapture.AfterEnemyBenefitEffect(__result, creature);
        }
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Add), new Type[] { typeof(Creature) })]
internal static class SolemnStrikeSummonPatch
{
    [HarmonyPrefix]
    private static bool NegateSummon(Creature creature, ref Task __result)
    {
        if (!SolemnStrikeActionNegation.ShouldNegateEnemySummon(creature)
            && !AshBlossomActionNegation.ShouldNegateEnemySummon(creature))
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseBlock), new Type[]
{
    typeof(Creature), typeof(decimal)
})]
internal static class SolemnStrikeLoseBlockPatch
{
    [HarmonyPrefix]
    private static bool NegateBlockLoss(
        Creature creature,
        decimal amount,
        ref Task __result,
        out bool __state)
    {
        // Block spent while resolving attack damage is part of that attack, not
        // a separate negative effect. In particular, multi-hit attacks must not
        // make Purulia draw once per hit or let Ash/Solemn preserve the block.
        __state = !MonsterFieldDamageGuard.IsConsumingAttackBlock
            && amount > 0
            && creature.Block > 0;
        if (!__state
            || (!SolemnStrikeActionNegation.ShouldNegatePlayerEffect(creature)
                && !AshBlossomActionNegation.ShouldNegatePlayerEffect(creature)))
            return true;

        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPostfix]
    private static void CaptureBlockLoss(
        Creature creature,
        bool __state,
        bool __runOriginal,
        ref Task __result)
    {
        if (__state && __runOriginal && __result is not null)
            __result = EnemyActionDrawCommandCapture.AfterPlayerNegativeEffect(__result, creature);
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseMaxHp), new Type[]
{
    typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(bool)
})]
internal static class SolemnStrikeLoseMaxHpPatch
{
    [HarmonyPrefix]
    private static bool NegateMaxHpLoss(
        Creature creature,
        decimal amount,
        ref Task __result,
        out bool __state)
    {
        __state = amount > 0 && creature.MaxHp > 1;
        if (!__state
            || (!SolemnStrikeActionNegation.ShouldNegatePlayerEffect(creature)
                && !AshBlossomActionNegation.ShouldNegatePlayerEffect(creature)))
            return true;

        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPostfix]
    private static void CaptureMaxHpLoss(
        Creature creature,
        bool __state,
        bool __runOriginal,
        ref Task __result)
    {
        if (__state && __runOriginal && __result is not null)
            __result = EnemyActionDrawCommandCapture.AfterPlayerNegativeEffect(__result, creature);
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Stun), new Type[]
{
    typeof(Creature), typeof(string)
})]
internal static class SolemnStrikeSimpleStunPatch
{
    [HarmonyPrefix]
    private static bool NegateStun(Creature creature, ref Task __result)
    {
        if (!SolemnStrikeActionNegation.ShouldNegatePlayerEffect(creature)
            && !AshBlossomActionNegation.ShouldNegatePlayerEffect(creature))
            return true;

        __result = Task.CompletedTask;
        return false;
    }

}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Stun), new Type[]
{
    typeof(Creature), typeof(Func<IReadOnlyList<Creature>, Task>), typeof(string)
})]
internal static class SolemnStrikeCallbackStunPatch
{
    [HarmonyPrefix]
    private static bool NegateStun(Creature creature, ref Task __result)
    {
        if (!SolemnStrikeActionNegation.ShouldNegatePlayerEffect(creature)
            && !AshBlossomActionNegation.ShouldNegatePlayerEffect(creature))
            return true;

        __result = Task.CompletedTask;
        return false;
    }

    [HarmonyPostfix]
    private static void CaptureStun(Creature creature, bool __runOriginal, ref Task __result)
    {
        if (__runOriginal && __result is not null)
            __result = EnemyActionDrawCommandCapture.AfterPlayerNegativeEffect(__result, creature);
    }
}

internal static class EnemyActionDrawCommandCapture
{
    private static readonly AsyncLocal<EnemyBenefitScope> CurrentEnemyBenefitScope = new();

    internal sealed class EnemyBenefitScope(EnemyBenefitScope previous)
    {
        internal EnemyBenefitScope Previous { get; } = previous;
    }

    internal static bool IsNestedEnemyBenefit => CurrentEnemyBenefitScope.Value is not null;

    internal static EnemyBenefitScope EnterEnemyBenefit()
    {
        var scope = new EnemyBenefitScope(CurrentEnemyBenefitScope.Value);
        CurrentEnemyBenefitScope.Value = scope;
        return scope;
    }

    internal static void DetachCaller(EnemyBenefitScope scope)
    {
        if (ReferenceEquals(CurrentEnemyBenefitScope.Value, scope))
            CurrentEnemyBenefitScope.Value = scope.Previous;
    }

    internal static async Task AfterEnemyBenefitEffect(Task original, Creature creature)
    {
        await original;
        EnemyActionDrawResolution.CaptureEnemyBenefitEffect(creature);
    }

    internal static async Task<decimal> AfterEnemyBenefitEffect(Task<decimal> original, Creature creature)
    {
        var result = await original;
        EnemyActionDrawResolution.CaptureEnemyBenefitEffect(creature);
        return result;
    }

    internal static async Task AfterPlayerNegativeEffect(Task original, Creature creature)
    {
        await original;
        EnemyActionDrawResolution.CapturePlayerNegativeEffect(creature);
    }
}
