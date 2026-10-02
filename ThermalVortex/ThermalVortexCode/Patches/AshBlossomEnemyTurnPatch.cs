using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CombatManager), "EndEnemyTurnInternal")]
internal static class AshBlossomEnemyTurnPatch
{
    private static void Prefix(CombatManager __instance, out ICombatState __state) =>
        __state = __instance.DebugOnlyGetState();

    private static void Postfix(ref Task __result, ICombatState __state) =>
        __result = FinishEnemyTurn(__result, __state);

    private static async Task FinishEnemyTurn(Task original, ICombatState combat)
    {
        // This method encloses every turn-end hook. Its caller switches sides
        // only after this returned task completes, so the final hook is covered.
        await original;
        if (combat?.CurrentSide != CombatSide.Enemy)
            return;

        var powers = combat.Enemies
            .Select(enemy => enemy.GetPower<AshBlossomPower>())
            .Where(power => power is not null)
            .ToList();
        foreach (var power in powers)
            await power.FinishEnemyTurn();
    }
}

[HarmonyPatch]
internal static class AshBlossomEffectSourcePatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        new[] { typeof(AbstractModel).Assembly, typeof(AshBlossomPower).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(AbstractModel).IsAssignableFrom(type)
                && type != typeof(AbstractModel)
                && !type.ContainsGenericParameters)
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(IsEffectCallback)
            .Cast<MethodBase>()
            .Distinct();

    private static void Prefix(
        AbstractModel __instance,
        MethodBase __originalMethod,
        out AshBlossomActionNegation.Scope __state) =>
        __state = AshBlossomActionNegation.EnterSource(__instance, __originalMethod);

    private static bool IsEffectCallback(MethodInfo method)
    {
        if (method.ReturnType != typeof(Task) || !method.IsVirtual
            || method.IsAbstract || method.ContainsGenericParameters
            || method.GetMethodBody() is null)
        {
            return false;
        }

        // Abstract classes can contain the implementation inherited by every
        // concrete power (for example EnemyActionDrawPower's leave callbacks).
        // Native event overrides and card effects are separate source boundaries.
        var baseMethod = method.GetBaseDefinition();
        return (baseMethod.DeclaringType == typeof(AbstractModel)
                && (method.Name.StartsWith("Before", StringComparison.Ordinal)
                    || method.Name.StartsWith("After", StringComparison.Ordinal)))
            || (method.Name == "OnPlay"
                && typeof(CardModel).IsAssignableFrom(method.DeclaringType)
                && method.GetParameters() is { Length: 2 } parameters
                && parameters[0].ParameterType == typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext)
                && parameters[1].ParameterType == typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay));
    }

    private static void Postfix(ref Task __result, AshBlossomActionNegation.Scope __state)
    {
        AshBlossomActionNegation.DetachCaller(__state);
        __result = AshBlossomActionNegation.CompleteSourceAsync(__result, __state);
    }

    private static Exception Finalizer(Exception __exception, AshBlossomActionNegation.Scope __state)
    {
        if (__exception is not null && __state is not null)
        {
            __state.Dispose();
        }
        return __exception;
    }
}
