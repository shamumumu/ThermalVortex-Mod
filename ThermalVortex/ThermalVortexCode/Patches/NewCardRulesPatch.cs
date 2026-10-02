using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw), new Type[]
{
    typeof(PlayerChoiceContext),
    typeof(decimal),
    typeof(Player),
    typeof(bool)
})]
internal static class MillenniumContractBookNaturalDrawPatch
{
    private static void Prefix(PlayerChoiceContext __0, ref decimal __1, Player __2, bool __3)
    {
        if (!__3 || __2 is null || __1 <= 0)
            return;

        var reduction = MillenniumContractBookPower.GetNaturalDrawReduction(__2);
        if (reduction <= 0)
            return;

        __1 = Math.Max(0, __1 - reduction);
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.HandlePlayerDeath))]
internal static class RaPhoenixPlayerDeathPatch
{
    private static bool Prefix(Player __0, ref Task __result)
    {
        if (!RaRevivalService.CanRevive(__0))
            return true;

        __result = RaRevivalService.Revive(__0);
        return false;
    }
}

[HarmonyPatch(typeof(CreatureCmd), "KillWithoutCheckingWinCondition", new Type[]
{
    typeof(Creature), typeof(bool), typeof(int)
})]
internal static class RaPhoenixLethalDamagePatch
{
    private static bool Prefix(Creature __0, ref Task __result)
    {
        if (__0?.IsPlayer != true)
            return true;

        var player = __0.Player;
        if (!RaRevivalService.CanRevive(player))
            return true;

        MainFile.Logger.Info(
            $"RaRevival intercepted lethal damage hp={__0.CurrentHp} route=KillWithoutCheckingWinCondition");
        __result = RaRevivalService.Revive(player);
        return false;
    }
}
