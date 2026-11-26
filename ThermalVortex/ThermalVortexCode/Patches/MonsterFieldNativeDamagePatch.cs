using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace ThermalVortex.ThermalVortexCode.Patches;

// All damage overloads and native attacks reach this implementation. The depth
// follows its continuations and excludes damage started by its own listeners.
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
    [typeof(PlayerChoiceContext), typeof(IEnumerable<DamageTargetData>), typeof(decimal),
     typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay), typeof(ModifyDamageHookType)])]
internal static class MonsterFieldNativeDamageCallPatch
{
    private static bool Prefix(
        PlayerChoiceContext __0,
        ref IEnumerable<DamageTargetData> __1,
        decimal __2,
        ValueProp __3,
        Creature __4,
        CardModel __5,
        CardPlay __6,
        ModifyDamageHookType __7,
        out int? __state,
        ref Task<IEnumerable<DamageResult>> __result)
    {
        __state = null;
        if (__1 is not null && MonsterFieldDamageGuard.CanGuardAttack(__4))
        {
            var targets = __1 as IReadOnlyList<DamageTargetData> ?? __1.ToList();
            __1 = targets;
            if (targets.Any(data => MonsterFieldDamageGuard.HasFieldGuard(data.target)))
            {
                __result = MonsterFieldDamageGuard.GuardNativeDamageAsync(__0, targets, () =>
                    CreatureCmd.Damage(__0, targets, __2, __3, __4, __5, __6, __7));
                return false;
            }
        }

        __state = MonsterFieldDamageGuard.EnterNativeDamageCall();
        return true;
    }

    private static void Postfix(int? __state)
    {
        if (__state is { } previous)
            MonsterFieldDamageGuard.RestoreNativeDamageCall(previous);
    }

    private static Exception Finalizer(Exception __exception, int? __state)
    {
        if (__state is { } previous)
            MonsterFieldDamageGuard.RestoreNativeDamageCall(previous);
        return __exception;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeDamageReceived))]
internal static class MonsterFieldNativeBeforeDamagePatch
{
    private static void Prefix(
        PlayerChoiceContext __0,
        Creature __3,
        decimal __4,
        Creature __6,
        out MonsterFieldDamageGuard.NativeDamageGuardState __state) =>
        __state = MonsterFieldDamageGuard.BeginNativeBeforeDamage(__0, __3, __6, __4);

    private static void Postfix(
        decimal __4,
        ValueProp __5,
        MonsterFieldDamageGuard.NativeDamageGuardState __state,
        ref Task __result)
    {
        if (__state is not null)
            __result = MonsterFieldDamageGuard.CompleteNativeBeforeDamage(__result, __state, __4, __5);
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.DamageBlockInternal),
    [typeof(decimal), typeof(ValueProp)])]
internal static class MonsterFieldNativeBlockPatch
{
    private static bool Prefix(Creature __instance, decimal __0, ValueProp __1, ref decimal __result)
    {
        if (!MonsterFieldDamageGuard.TryConsumeNativeBlock(__instance, __0, __1, out var absorbed))
            return true;

        // Native subtracts this amount before its HP-loss and Osty hooks. The
        // actual block and monster health were already paid by the field guard.
        __result = absorbed;
        return false;
    }
}

[HarmonyPatch]
internal static class MonsterFieldNativeDamageResultPatch
{
    [HarmonyPatch(typeof(DamageResult), nameof(DamageResult.BlockedDamage), MethodType.Setter)]
    [HarmonyPrefix]
    private static void CorrectBlockedDamage(DamageResult __instance, ref int __0)
    {
        var state = MonsterFieldDamageGuard.GetCompletedNativeGuard(__instance.Receiver);
        if (state is not null)
            __0 = state.BlockAbsorbed;
    }

    [HarmonyPatch(typeof(DamageResult), nameof(DamageResult.WasBlockBroken), MethodType.Setter)]
    [HarmonyPrefix]
    private static void CorrectBlockBroken(DamageResult __instance, ref bool __0)
    {
        var state = MonsterFieldDamageGuard.GetCompletedNativeGuard(__instance.Receiver);
        if (state is not null)
        {
            __0 = state.BlockWasBroken;
            state.BlockBreakNotificationPending = __0;
        }
    }

    [HarmonyPatch(typeof(DamageResult), nameof(DamageResult.WasFullyBlocked), MethodType.Setter)]
    [HarmonyPrefix]
    private static void CorrectFullyBlocked(DamageResult __instance, ref bool __0)
    {
        var state = MonsterFieldDamageGuard.GetCompletedNativeGuard(__instance.Receiver);
        if (state is not null)
            __0 &= state.Absorbed == state.BlockAbsorbed;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterBlockBroken))]
internal static class MonsterFieldNativeBlockBrokenPatch
{
    private static bool Prefix(Creature __2, ref Task __result)
    {
        if (!MonsterFieldDamageGuard.SuppressNativeBlockBroken(__2))
            return true;

        // CreatureCmd.LoseBlock emitted real breaks while resolving the guard.
        __result = Task.CompletedTask;
        return false;
    }
}

// Explicit block-loss commands inside listeners remain independent events;
// only the native damage routine's duplicate notification is suppressed.
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.LoseBlock),
    [typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(Creature)])]
internal static class MonsterFieldNativeBlockLossCallPatch
{
    private static void Prefix(out int __state) =>
        __state = MonsterFieldDamageGuard.EnterNativeBlockLossCall();

    private static void Postfix(int __state) =>
        MonsterFieldDamageGuard.RestoreNativeBlockLossCall(__state);

    private static Exception Finalizer(Exception __exception, int __state)
    {
        MonsterFieldDamageGuard.RestoreNativeBlockLossCall(__state);
        return __exception;
    }
}
