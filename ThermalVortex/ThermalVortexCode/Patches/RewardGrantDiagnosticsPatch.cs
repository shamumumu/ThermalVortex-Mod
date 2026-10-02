using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.RewardPools;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(
    typeof(CardPileCmd),
    nameof(CardPileCmd.Add),
    [
        typeof(CardModel),
        typeof(CardPile),
        typeof(CardPilePosition),
        typeof(AbstractModel),
        typeof(bool)
    ])]
internal static class RewardGrantDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void ObserveGenericPermanentGrant(CardModel __0, CardPile __1)
    {
        // Observer only: no ref arguments and no return value by design.
        if (__1 is not null)
            RewardGrantDiagnostics.ObservePermanentDeckAdd(__0, __1.Type);
    }
}
