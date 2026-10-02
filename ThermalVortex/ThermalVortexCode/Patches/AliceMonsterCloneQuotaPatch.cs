using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CardModel), nameof(CardModel.CreateClone))]
internal static class AliceMonsterCloneQuotaPatch
{
    [HarmonyPostfix]
    private static void ResetQuotaForNewMonsterEntity(CardModel __instance, CardModel __result)
    {
        if (__result is ThermalVortexCard monster)
        {
            monster.ResetAliceSpecialSummonQuota();
            CyberDevourState.CopyForCombatClone(__instance, monster);
        }
    }
}
