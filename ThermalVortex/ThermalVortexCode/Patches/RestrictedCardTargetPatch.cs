using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Patches;

// CardModel.IsValidTarget is non-virtual. Apply the same card-specific rule to
// the native pointer UI, queued plays, and the shared automatic target picker.
[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsValidTarget), [typeof(Creature)])]
internal static class RestrictedCardTargetPatch
{
    private static void Postfix(CardModel __instance, Creature __0, ref bool __result)
    {
        if (!__result)
            return;

        __result = __instance switch
        {
            Relinquished => Relinquished.IsMinion(__0),
            ChaosPhantom phantom when phantom.IsCompleteCopyOf<Relinquished>() => Relinquished.IsMinion(__0),
            DogmatikaPunishment punishment => punishment.HasLegalTargetAndCost(__0),
            _ => __result
        };
    }
}
