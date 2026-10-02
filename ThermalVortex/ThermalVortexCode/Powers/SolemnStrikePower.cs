using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Patches;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class SolemnStrikePower : ThermalVortexPower
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    public void TriggerNegation() => Flash();

    public override bool TryModifyPowerAmountReceived(
        PowerModel power,
        Creature target,
        decimal amount,
        Creature source,
        out decimal newAmount)
    {
        newAmount = amount;

        if (amount <= 0 || !ShouldCancelPower(power, target, source))
            return false;

        newAmount = 0;
        return true;
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        if (!SolemnStrikeActionNegation.WouldNegateStatusCard(
                card, card?.Pile?.Type, source, expectedPower: this))
            return;

        await CardPileCmd.RemoveFromCombat(card, true);
    }

    private bool ShouldCancelPower(PowerModel power, Creature target, Creature source)
    {
        if (!SolemnStrikeActionNegation.IsActiveFor(Owner)
            || !SolemnStrikeActionNegation.IsSelectedEnemySource(source, Owner))
            return false;

        if (power.Type == PowerType.Debuff && target?.IsPlayer == true)
            return true;

        return power.Type == PowerType.Buff && target?.IsEnemy == true;
    }
}
