using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NCardLibraryGrid), "InitGrid")]
internal static class CardLibraryInitialPortraitPatch
{
    [HarmonyPrefix]
    private static void UseSmallPortraitWhileBuildingGrid() =>
        CardPortraitResolutionContext.EnterSmallCardImageScope();

    [HarmonyFinalizer]
    private static Exception RestorePortraitResolutionContext(Exception __exception)
    {
        CardPortraitResolutionContext.ExitSmallCardImageScope();
        return __exception;
    }
}

[HarmonyPatch(typeof(NCard), "Reload")]
internal static class CardLibraryPortraitPatch
{
    [HarmonyPrefix]
    private static void UseSmallPortraitInsideCardLibrary(NCard __instance, out bool __state)
    {
        __state = IsInsideCardLibraryGrid(__instance);
        if (__state)
            CardPortraitResolutionContext.EnterSmallCardImageScope();
    }

    [HarmonyFinalizer]
    private static Exception RestorePortraitResolutionContext(bool __state, Exception __exception)
    {
        if (__state)
            CardPortraitResolutionContext.ExitSmallCardImageScope();

        return __exception;
    }

    private static bool IsInsideCardLibraryGrid(NCard card)
    {
        for (var node = card?.GetParent(); node is not null; node = node.GetParent())
        {
            if (node is NCardLibraryGrid)
                return true;
        }

        return false;
    }
}
