using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class CyberDragonInfinityPower : ThermalVortexPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        var devourers = GetFieldDevourers(player);
        if (devourers.Count == 0)
        {
            await PowerCmd.Remove(this);
            return;
        }

        foreach (var infinity in devourers)
        {
            if (!MonsterFieldService.IsOnField(infinity))
                continue;
            Flash();
            var damage = CyberDevourState.GetInheritedAttackContribution(infinity);
            if (damage <= 0)
                continue;

            var target = SelectRandomEnemy();
            if (target is not null)
                await ThermalVortexCombatVfx.EffectDamageAsync(
                    ctx,
                    target,
                    damage,
                    ValueProp.Unpowered,
                    Owner,
                    infinity);
        }
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide combatSide, IEnumerable<Creature> creatures)
    {
        if (combatSide != Owner.Side || Owner?.Player is null)
            return;

        foreach (var infinity in GetFieldDevourers(Owner.Player).ToList())
        {
            if (!MonsterFieldService.IsOnField(infinity))
                continue;

            Flash();
            var targets = MonsterFieldService.GetMonsters(Owner.Player)
                .Where(card => !ReferenceEquals(card, infinity))
                .ToList();
            if (targets.Count == 0)
            {
                var copiedInfinityUpgraded = infinity is ChaosPhantom copiedInfinity
                    ? copiedInfinity.CopiedMonsterWasUpgraded
                    : infinity.CurrentUpgradeLevel > 0;
                if (!copiedInfinityUpgraded)
                    await MonsterFieldService.SendToExhaust(ctx, [infinity], this, MonsterFieldLeaveReason.Exhaust);

                continue;
            }

            var victim = Owner.Player.RunState.Rng.CombatCardSelection.NextItem(targets);
            CyberDevourState.Absorb(infinity, victim);
            // Bind ongoing inherited abilities immediately, without executing
            // any summon or turn-start rewards during this turn-end devour.
            await CyberDevourState.SynchronizePersistentEffects(ctx, infinity);
            await MonsterFieldService.SendToExhaust(ctx, [victim], this, MonsterFieldLeaveReason.Exhaust);
        }

        if (GetFieldDevourers(Owner.Player).Count == 0)
            await PowerCmd.Remove(this);
    }

    private IReadOnlyList<CardModel> GetFieldDevourers(Player player) =>
        MonsterFieldService.GetMonsters(player)
            .Where(card => card is CyberDragonInfinity
                || CyberDevourState.HasInheritedInfinityAbility(card)
                || card is ChaosPhantom phantom
                    && phantom.HasCompleteCopyEffect<CyberInfinityDevourEffect>())
            .ToList();

    private Creature SelectRandomEnemy()
    {
        var enemies = Owner?.CombatState?.HittableEnemies.ToList();
        if (enemies is null || enemies.Count == 0)
            return null;

        return Owner.Player.RunState.Rng.CombatTargets.NextItem(enemies);
    }
}
