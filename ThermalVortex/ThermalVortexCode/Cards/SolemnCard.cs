using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace ThermalVortex.ThermalVortexCode.Cards;

public abstract class SolemnCard(int cost, CardRarity rarity, TargetType target) :
    MainDeckCard(cost, CardType.Skill, rarity, target)
{
    protected override bool IsPlayable =>
        base.IsPlayable && !IsLifePaymentBlocked();

    protected override bool ShouldGlowRedInternal =>
        base.ShouldGlowRedInternal || IsLifePaymentBlocked();

    protected abstract decimal GetLifePayment();

    private bool IsLifePaymentBlocked() =>
        Owner is not null && base.IsPlayable && !CanPayLife(GetLifePayment());

    protected bool CanPayLife(decimal amount)
    {
        var creature = Owner?.Creature;
        return creature is not null && creature.CurrentHp - amount >= 1;
    }

    protected async Task<bool> PayLife(decimal amount)
    {
        var creature = Owner.Creature;
        if (!CanPayLife(amount))
            return false;

        if (amount > 0)
            await CreatureCmd.SetCurrentHp(creature, creature.CurrentHp - amount);
        return true;
    }
}
