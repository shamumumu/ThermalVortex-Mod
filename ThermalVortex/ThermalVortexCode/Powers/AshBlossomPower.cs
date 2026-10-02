using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Patches;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class AshBlossomPower : ThermalVortexPower
{
    private int? _triggeredRound;

    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int DisplayAmount => Math.Max(0, Amount - (_triggeredRound.HasValue ? 1 : 0));

    internal bool CanTriggerNegation => Amount > 0
        && Owner?.CombatState?.CurrentSide == CombatSide.Enemy
        && ReferenceEquals(Owner.GetPower<AshBlossomPower>(), this);

    internal bool TryTriggerNegation()
    {
        if (!CanTriggerNegation)
            return false;

        var combat = Owner.CombatState;
        if (_triggeredRound == combat.RoundNumber)
            return true;

        // Reserve the last charge instead of removing the hook listener midway
        // through the turn. The enemy-turn wrapper spends it after all hooks.
        _triggeredRound = combat.RoundNumber;
        Flash();
        InvokeDisplayAmountChanged();
        return true;
    }

    internal async Task FinishEnemyTurn()
    {
        if (!_triggeredRound.HasValue)
            return;

        _triggeredRound = null;
        InvokeDisplayAmountChanged();
        if (!ReferenceEquals(Owner?.GetPower<AshBlossomPower>(), this))
            return;

        if (Amount <= 1)
            await PowerCmd.Remove(this);
        else
            await ThermalVortexCommandCompat.ModifyPowerAmount(
                new BlockingPlayerChoiceContext(), this, -1, Applier, null, false);
    }

    public override bool TryModifyPowerAmountReceived(
        PowerModel power,
        Creature target,
        decimal amount,
        Creature source,
        out decimal newAmount)
    {
        newAmount = amount;

        if (amount == 0 || ReferenceEquals(power, this)
            || (amount < 0 && AshBlossomActionNegation.IsCurrentPowerCleanup(power))
            || AshBlossomActionNegation.IsCurrentLinkedPowerCleanup(power, target, amount)
            || !ShouldCancelPower(power, target, source))
            return false;

        newAmount = 0;
        return true;
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        if (!AshBlossomActionNegation.ShouldNegateStatusCard(card, oldPileType, source, this))
            return;

        await CardPileCmd.RemoveFromCombat(card, true);
    }

    private bool ShouldCancelPower(PowerModel power, Creature target, Creature source)
    {
        return target is not null
            && AshBlossomActionNegation.TryTriggerFor(this, source);
    }
}
