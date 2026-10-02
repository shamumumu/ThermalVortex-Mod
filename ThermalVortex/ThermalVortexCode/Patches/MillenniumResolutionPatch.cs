using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]
internal static class MillenniumResolutionPatch
{
    [HarmonyPrefix]
    private static void Capture(
        CardModel __instance,
        PlayerChoiceContext __0,
        ref MillenniumResolution.ResolutionHandle __state) =>
        __state = MillenniumResolution.EnterPlay(__instance, __0);

    [HarmonyPostfix]
    private static void Complete(ref Task __result, MillenniumResolution.ResolutionHandle __state)
    {
        if (__state is null)
            return;
        __state.DetachCaller();
        if (__result is null)
        {
            __state.Dispose();
            return;
        }
        __result = MillenniumResolution.CompletePlay(__result, __state);
    }

    [HarmonyFinalizer]
    private static void OnSynchronousFailure(Exception __exception, MillenniumResolution.ResolutionHandle __state)
    {
        if (__exception is not null)
            __state?.Dispose();
    }
}
