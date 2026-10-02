using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Patches;

// Both single-target overloads delegate to this overload. Observe output only
// here so one command never spends its pending boost more than once.
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), new Type[]
{
    typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal),
    typeof(ValueProp), typeof(Creature), typeof(CardModel)
})]
internal static class CyberWeldingDamageOutputPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(
        IEnumerable<Creature> __1, decimal __2, ValueProp __3, Creature __4, CardModel __5,
        out IDisposable __state) =>
        __state = CyberWeldingPower.BeginDamageOutput(__5, __4, __2, __1, __3);

    private static void Postfix(IDisposable __state, ref Task<IEnumerable<DamageResult>> __result)
    {
        if (__state is not null)
            __result = CompleteOutput(__result, __state);
    }

    private static async Task<IEnumerable<DamageResult>> CompleteOutput(
        Task<IEnumerable<DamageResult>> original, IDisposable output)
    {
        using (output)
            return await original;
    }

    private static Exception Finalizer(Exception __exception, IDisposable __state)
    {
        if (__exception is not null)
            __state?.Dispose();
        return __exception;
    }
}

[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute))]
internal static class CyberWeldingAttackCleanupPatch
{
    private static void Postfix(AttackCommand __instance, ref Task<AttackCommand> __result) =>
        __result = CompleteAttack(__result, __instance);

    private static async Task<AttackCommand> CompleteAttack(Task<AttackCommand> original, AttackCommand attack)
    {
        try
        {
            return await original;
        }
        finally
        {
            // AfterAttack normally finishes the output. This also clears an
            // unfinished scope when native execution exits early or throws.
            CyberWeldingPower.FinishAttack(attack);
        }
    }
}
