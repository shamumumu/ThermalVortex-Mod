using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Commands;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class LimiterRemovalPower : ThermalVortexPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide combatSide, IEnumerable<Creature> creatures)
    {
        if (combatSide != Owner.Side)
            return;

        var player = Owner.Player;
        Flash();
        await PowerCmd.Remove(this);
        await MonsterFieldHealthService.EnforceCapacity(
            ctx,
            player,
            this,
            MonsterFieldLeaveReason.BattleDestroyed);
    }
}
