using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CombatState), nameof(CombatState.CloneCard))]
internal static class FusionDestinyCloneDiscountPatch
{
    [HarmonyPostfix]
    private static void CopyAppliedDiscount(CardModel __0, CardModel __result) =>
        __0?.Owner?.Creature?.GetPower<FusionDestinyPower>()?.CopyAppliedDiscount(__0, __result);
}
