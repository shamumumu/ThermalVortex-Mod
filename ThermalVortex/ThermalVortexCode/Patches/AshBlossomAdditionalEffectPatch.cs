using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Escape), [typeof(Creature), typeof(bool)])]
internal static class AshBlossomEscapePatch
{
    [HarmonyPrefix]
    private static bool NegateEscape(Creature creature, ref Task __result)
    {
        if (creature?.IsAlive != true || !AshBlossomActionNegation.TryNegateFrom())
            return true;
        __result = Task.CompletedTask;
        return false;
    }

}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.SetCurrentHp), [typeof(Creature), typeof(decimal)])]
internal static class AshBlossomHpLossPatch
{
    [HarmonyPrefix]
    private static bool NegateHpLoss(Creature creature, decimal amount, ref Task __result)
    {
        if (creature is null || amount >= creature.CurrentHp
            || !AshBlossomActionNegation.ShouldNegatePlayerEffect(creature))
            return true;
        __result = Task.CompletedTask;
        return false;
    }

}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.SetMaxHp), [typeof(Creature), typeof(decimal)])]
internal static class AshBlossomMaxHpLossPatch
{
    [HarmonyPrefix]
    private static bool NegateMaxHpLoss(Creature creature, decimal amount, ref Task<decimal> __result)
    {
        if (creature is null || amount >= creature.MaxHp
            || !AshBlossomActionNegation.ShouldNegatePlayerEffect(creature))
            return true;
        __result = Task.FromResult((decimal)creature.MaxHp);
        return false;
    }

}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.SetMaxAndCurrentHp), [typeof(Creature), typeof(decimal)])]
internal static class AshBlossomMaxAndCurrentHpLossPatch
{
    [HarmonyPrefix]
    private static bool NegateMaxAndCurrentHpLoss(Creature creature, decimal amount, ref Task __result)
    {
        if (creature is null || (amount >= creature.CurrentHp && amount >= creature.MaxHp)
            || !AshBlossomActionNegation.ShouldNegatePlayerEffect(creature))
            return true;
        __result = Task.CompletedTask;
        return false;
    }

}

[HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Remove), [typeof(PowerModel)])]
internal static class AshBlossomPowerRemovalPatch
{
    [HarmonyPrefix]
    private static bool NegatePowerRemoval(PowerModel __0, ref Task __result)
    {
        if (__0 is null or AshBlossomPower
            || AshBlossomActionNegation.IsCurrentPowerCleanup(__0)
            || !AshBlossomActionNegation.TryNegateFrom())
            return true;
        __result = Task.CompletedTask;
        return false;
    }

}

[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Afflict),
    [typeof(AfflictionModel), typeof(CardModel), typeof(decimal)])]
internal static class AshBlossomAfflictionPatch
{
    [HarmonyPrefix]
    private static bool NegateAffliction(CardModel __1, decimal __2, ref Task<AfflictionModel> __result)
    {
        if (__2 == 0 || __1?.Owner?.Creature?.IsPlayer != true
            || !AshBlossomActionNegation.TryNegateFrom())
            return true;
        // Native Afflict also returns null when its ShouldAfflict hook rejects it.
        __result = Task.FromResult<AfflictionModel>(null);
        return false;
    }

}

[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Discard), [typeof(PlayerChoiceContext), typeof(CardModel)])]
internal static class AshBlossomDiscardPatch
{
    [HarmonyPrefix]
    private static bool NegateDiscard(CardModel __1, ref Task __result)
    {
        if (__1?.Pile?.Type != PileType.Hand || !AshBlossomActionNegation.TryNegateFrom())
            return true;
        __result = Task.CompletedTask;
        return false;
    }

}

[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Exhaust),
    [typeof(PlayerChoiceContext), typeof(CardModel), typeof(bool), typeof(bool)])]
internal static class AshBlossomExhaustPatch
{
    [HarmonyPrefix]
    private static bool NegateExhaust(CardModel __1, ref Task __result)
    {
        if (__1?.Pile?.Type is not (PileType.Draw or PileType.Hand or PileType.Discard)
            || !AshBlossomActionNegation.TryNegateFrom())
            return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch]
internal static class AshBlossomNonAttackDamagePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(CreatureCmd)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(method => method.Name == nameof(CreatureCmd.Damage))
        .Where(method => method.GetParameters() is { Length: >= 5 } parameters
            && parameters[2].ParameterType == typeof(decimal)
            && parameters[3].ParameterType == typeof(ValueProp));

    [HarmonyPriority(Priority.First + 10)]
    private static bool Prefix(object[] __args, ref Task<IEnumerable<DamageResult>> __result)
    {
        var amount = (decimal)__args[2];
        var props = (ValueProp)__args[3];
        if (amount <= 0 || (props & ValueProp.Move) != 0)
            return true;

        IEnumerable<Creature> targets = __args[1] is Creature single
            ? [single]
            : __args[1] as IEnumerable<Creature>;
        var targetList = targets?.Where(target => target is not null).ToList();
        if (targetList?.Any(target => target.IsAlive) != true)
            return true;

        var source = __args.Skip(4).OfType<Creature>().FirstOrDefault()
            ?? __args.Skip(4).OfType<CardModel>().FirstOrDefault()?.Owner?.Creature;
        if (!AshBlossomActionNegation.TryNegateFrom(source))
            return true;

        // Match native's cancelled-damage result shape: one zero-damage result
        // per receiver, without damage hooks or HP/block changes.
        __result = Task.FromResult<IEnumerable<DamageResult>>(
            targetList.Select(target => new DamageResult(target, props)).ToList());
        return false;
    }
}

[HarmonyPatch]
internal static class AshBlossomPlayerResourcePatch
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(PlayerCmd)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(method => method.Name is nameof(PlayerCmd.LoseEnergy) or nameof(PlayerCmd.SetEnergy)
            or nameof(PlayerCmd.LoseStars) or nameof(PlayerCmd.SetStars)
            or nameof(PlayerCmd.LoseGold) or nameof(PlayerCmd.SetGold));

    private static bool Prefix(MethodBase __originalMethod, decimal __0, Player __1, ref Task __result)
    {
        var current = __originalMethod.Name switch
        {
            nameof(PlayerCmd.LoseEnergy) or nameof(PlayerCmd.SetEnergy) => __1?.PlayerCombatState?.Energy ?? 0,
            nameof(PlayerCmd.LoseStars) or nameof(PlayerCmd.SetStars) => __1?.PlayerCombatState?.Stars ?? 0,
            _ => __1?.Gold ?? 0
        };
        var changesValue = __originalMethod.Name.StartsWith("Set", StringComparison.Ordinal)
            ? __0 != current
            : __0 > 0 && current > 0;
        if (!changesValue || !AshBlossomActionNegation.TryNegateFrom())
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}
