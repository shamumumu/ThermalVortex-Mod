using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class DormantMagneticFieldPower : ThermalVortexPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
    {
        if (card.Owner?.Creature != Owner)
            return true;

        return card.Type != CardType.Attack;
    }

    public override bool ShouldClearBlock(Creature creature) =>
        creature != Owner || Amount <= 0;

    public override async Task AfterPreventingBlockClear(AbstractModel source, Creature creature)
    {
        if (creature != Owner)
            return;

        Flash();
        var blockToLose = Owner.Block - (Owner.Block / 2);
        if (blockToLose > 0)
            await CreatureCmd.LoseBlock(Owner, blockToLose);

        if (Amount <= 1)
            await PowerCmd.Remove(this);
        else
            await ThermalVortexCommandCompat.ModifyPowerAmount(null, this, -1, Owner, null, false);
    }
}
