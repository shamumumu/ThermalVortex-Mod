using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using ThermalVortex.ThermalVortexCode.Character;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NCreature), nameof(NCreature.StartDeathAnim), [typeof(bool)])]
internal static class YugiDeathAnimationPatch
{
    private static void Postfix(NCreature __instance, ref float __result)
    {
        var creature = __instance?.Entity;
        if (!MillenniumPuzzleCharacterArt.IsThermalVortex(creature?.Player))
            return;

        try
        {
            // Keep native focus/UI cleanup and death prevention. A party loss
            // reserves recall for the game-over screen's red transition;
            // its pending completion must not block opening that screen.
            if (!YugiNativeActionController.TryBeginDeath(creature, out var completion, out var remainingSeconds))
                return;

            var nativeCompletion = __instance.DeathAnimationTask;
            __instance.DeathAnimationTask = nativeCompletion is null
                ? completion
                : Task.WhenAll(nativeCompletion, completion);
            __result = (float)remainingSeconds;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Yugi native death presentation unavailable: " + exception.Message);
        }
    }
}

[HarmonyPatch(typeof(CreatureCmd), "KillWithoutCheckingWinCondition", [typeof(Creature), typeof(bool), typeof(int)])]
internal static class YugiDeathAnimationWaitPatch
{
    private static void Postfix(Creature __0, ref Task __result)
    {
        if (__result is null || !MillenniumPuzzleCharacterArt.IsThermalVortex(__0?.Player))
            return;

        __result = AwaitPresentation(__result, __0);
    }

    private static async Task AwaitPresentation(Task nativeDeath, Creature creature)
    {
        // Non-game-over deaths still finish their recall here. A party loss
        // returns immediately while recall waits for the native red backstop.
        // A prevented death (including Phoenix revival) has no presentation.
        await nativeDeath;
        await YugiNativeActionController.GetDeathCompletion(creature);
    }
}
