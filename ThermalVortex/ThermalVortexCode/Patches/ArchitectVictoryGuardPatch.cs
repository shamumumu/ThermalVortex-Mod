using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(TheArchitect), "WinRun")]
internal static class ArchitectVictoryGuardPatch
{
    private static bool loggedFailure;

    private static void Prefix(TheArchitect __instance)
    {
        try
        {
            // Only the closing sequence prepares a response. The same native
            // attack helper is also used during some opening conversations.
            // Loading stays invisible until the actual Attack trigger below.
            ArchitectVictoryGuardVfx.Prepare(__instance);
        }
        catch (Exception exception)
        {
            LogFailure(exception);
        }
    }

    private static void Postfix(TheArchitect __instance, ref Task __result)
    {
        if (__result is null)
        {
            FinishEndingSafely(__instance, false);
            return;
        }
        __result = AwaitEnding(__instance, __result);
    }

    private static async Task AwaitEnding(TheArchitect architect, Task nativeEnding)
    {
        var succeeded = false;
        try
        {
            await nativeEnding;
            succeeded = true;
        }
        finally
        {
            // A skipped attack or failed ending cannot leave an armed response
            // behind. A completed guarded pose remains until the summary exits.
            FinishEndingSafely(architect, succeeded);
        }
    }

    private static void FinishEndingSafely(TheArchitect architect, bool succeeded)
    {
        try { ArchitectVictoryGuardVfx.FinishEnding(architect, succeeded); }
        catch (Exception exception) { LogFailure(exception); }
    }

    internal static void LogFailure(Exception exception)
    {
        if (loggedFailure)
            return;
        loggedFailure = true;
        MainFile.Logger.Info("Architect victory guard hook unavailable: " + exception.Message);
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.SetAnimationTrigger), [typeof(string)])]
internal static class ArchitectVictoryGuardAnimationPatch
{
    private static bool Prefix(NCreature __instance, string __0)
    {
        if (!string.Equals(__0, "Dead", StringComparison.Ordinal))
            return true;
        try
        {
            // Keep StartDeathAnim's focus, intent and task handling. Native
            // AnimDie(false) only disables UI for a player; it does not hide or
            // free the body. Suppress just the conflicting visual trigger.
            return !ArchitectVictoryGuardVfx.IsActive(__instance.Entity);
        }
        catch (Exception exception)
        {
            ArchitectVictoryGuardPatch.LogFailure(exception);
            return true;
        }
    }

    private static void Postfix(NCreature __instance, string __0, bool __runOriginal)
    {
        if (!__runOriginal || !string.Equals(__0, "Attack", StringComparison.Ordinal))
            return;
        try
        {
            if (__instance.HasSpineAnimation)
                ArchitectVictoryGuardVfx.OnArchitectAttack(__instance.Entity);
        }
        catch (Exception exception)
        {
            ArchitectVictoryGuardPatch.LogFailure(exception);
        }
    }
}

[HarmonyPatch(typeof(VfxCmd), nameof(VfxCmd.PlayOnCreature), [typeof(Creature), typeof(string)])]
internal static class ArchitectVictoryGuardLightningPatch
{
    private static bool Prefix(Creature __0, string __1)
    {
        if (!string.Equals(__1, "vfx/vfx_attack_lightning", StringComparison.Ordinal))
            return true;
        try
        {
            return !ArchitectVictoryGuardVfx.TryReplaceLightning(__0);
        }
        catch (Exception exception)
        {
            ArchitectVictoryGuardPatch.LogFailure(exception);
            return true;
        }
    }
}

[HarmonyPatch(typeof(NFireBurstVfx), nameof(NFireBurstVfx.Create), [typeof(Creature), typeof(float)])]
internal static class ArchitectVictoryGuardFirePatch
{
    private static bool Prefix(Creature __0, ref NFireBurstVfx __result)
    {
        try
        {
            if (!ArchitectVictoryGuardVfx.ShouldSuppressFire(__0))
                return true;
            // Native AddChildSafely immediately returns for a null child.
            // The guard owns the contact flash and the retained screen shake.
            __result = null;
            return false;
        }
        catch (Exception exception)
        {
            ArchitectVictoryGuardPatch.LogFailure(exception);
            return true;
        }
    }
}
