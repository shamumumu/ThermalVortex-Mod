using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class ForbiddenChalicePower : ThermalVortexPower
{
    public const int StrengthGain = 3;

    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    internal async Task ReplaceMoveWithStrengthGain()
    {
        Flash();
        await ThermalVortexCommandCompat.ApplyPower<StrengthPower>(null, Owner, StrengthGain, Applier, null, false);
        await PowerCmd.Remove(this);
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide combatSide, IEnumerable<Creature> creatures)
    {
        if (combatSide == Owner.Side)
            await PowerCmd.Remove(this);
    }
}
