using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Replace))]
internal static class ThermalVortexCoreReplacementStatePatch
{
    [HarmonyPrefix]
    private static void PreserveCoreState(RelicModel __0, RelicModel __1)
    {
        if (__0 is ThermalVortexCore source
            && source is not AncientThermalVortexCore
            && __1 is AncientThermalVortexCore replacement)
        {
            replacement.CopyStateFrom(source);
        }
    }
}

[HarmonyPatch(typeof(DustyTome), nameof(DustyTome.SetupForPlayer))]
internal static class ThermalVortexDustyTomePatch
{
    private static bool Prefix(DustyTome __instance, Player player)
    {
        if (player?.Character is not ThermalVortexCharacter)
            return true;

        __instance.AncientCard = ModelDb.Card<PrimalGodFara>().Id;
        return false;
    }
}
