using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Patches;

// Wait for the complete end-of-turn hook, including every other power's
// triggered effects, so power enumeration order cannot change Welding damage.
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterTurnEnd))]
internal static class CyberWeldingTurnEndPatch
{
    private static void Postfix(
        CombatSide __1, IEnumerable<Creature> __2, ref Task __result) =>
        __result = FinishTurn(__result, __1, __2.ToList());

    private static async Task FinishTurn(Task original, CombatSide side, IReadOnlyList<Creature> creatures)
    {
        await original;
        foreach (var creature in creatures)
        {
            var power = creature.GetPower<CyberWeldingPower>();
            if (power is not null)
                await power.ExpireAfterTurnEnd(side);
        }
    }
}
