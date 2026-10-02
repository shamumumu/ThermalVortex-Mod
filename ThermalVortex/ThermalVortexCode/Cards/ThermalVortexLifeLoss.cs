using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Cards;

internal static class ThermalVortexLifeLoss
{
    internal static async Task<int> LosePlayerHp(Creature creature, decimal amount)
    {
        if (creature?.Player is null || amount <= 0)
            return 0;

        var beforeHp = Convert.ToDecimal(creature.CurrentHp);
        var before = Math.Max(0, (int)Math.Ceiling(beforeHp));
        var after = Math.Max(0, (int)Math.Ceiling(beforeHp - amount));
        await CreatureCmd.SetCurrentHp(creature, after);

        var currentHp = Convert.ToDecimal(creature.CurrentHp);
        var lost = Math.Max(0, before - Math.Max(0, (int)Math.Ceiling(currentHp)));
        if (lost > 0)
            TryGetCore(creature)?.RecordSelfDamage(lost);

        return lost;
    }

    private static ThermalVortexCore TryGetCore(Creature creature)
    {
        try
        {
            return creature?.Player?.GetRelic<ThermalVortexCore>();
        }
        catch
        {
            return null;
        }
    }
}
