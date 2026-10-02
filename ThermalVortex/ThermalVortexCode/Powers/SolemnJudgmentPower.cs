using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class SolemnJudgmentPower : ThermalVortexPower
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    public void TriggerNegation() => Flash();

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide combatSide, IEnumerable<Creature> creatures)
    {
        if (combatSide == Owner.Side)
            await PowerCmd.Remove(this);
    }
}
