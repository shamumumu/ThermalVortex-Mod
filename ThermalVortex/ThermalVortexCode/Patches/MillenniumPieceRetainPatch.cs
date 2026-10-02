using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CardModel), "get_ShouldRetainThisTurn")]
internal static class MillenniumPieceRetainPatch
{
    [HarmonyPostfix]
    private static void ApplyHeldTorsoRetain(CardModel __instance, ref bool __result)
    {
        if (__result
            || __instance?.Pile?.Type != PileType.Hand
            || !MillenniumSeries.IsSealedPiece(__instance)
            || __instance.Owner?.PlayerCombatState is null)
        {
            return;
        }

        __result = CardPile.GetCards(__instance.Owner, PileType.Hand)
            .Any(card => MillenniumSeries.IsCardType(card, typeof(ExodiaTorso)));
    }
}
