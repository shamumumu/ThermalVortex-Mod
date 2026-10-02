using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class RelinquishedControlPower : ThermalVortexPower, IMonsterFieldLeaveListener
{
    private static readonly Dictionary<CardModel, RelinquishedControlPower> Bindings =
        new(new ReferenceComparer<CardModel>());
    private CardModel _relinquished;
    private Creature _subscribedOwner;
    private int _synchronizing;
    private bool _resolvingDeath;
    private bool _nativeDeathInProgress;
    private bool _deathConfirmed;

    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    internal bool IsControlActive => _relinquished is not null && !_deathConfirmed;

    private bool HasSharedZeroHealthProtection => IsControlActive
        && MonsterFieldService.IsOnField(_relinquished)
        && MonsterFieldHealthService.IsZeroHealthProtected(_relinquished);

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _relinquished = null;
        _subscribedOwner = null;
        _synchronizing = 0;
        _resolvingDeath = false;
        _nativeDeathInProgress = false;
        _deathConfirmed = false;
    }

    internal async Task BindAsync(CardModel relinquished)
    {
        if (relinquished is null || Owner is null)
            return;

        if (Bindings.TryGetValue(relinquished, out var previous) && !ReferenceEquals(previous, this))
            await ReleaseForCopyReset(relinquished);

        // A Single power can be reapplied to the same enemy by another monster.
        // The old body keeps its final health, but no longer owns this life pool.
        Detach();
        _relinquished = relinquished;
        _deathConfirmed = false;
        Bindings[relinquished] = this;
        _subscribedOwner = Owner;
        _subscribedOwner.CurrentHpChanged += OnEnemyHealthChanged;
        _subscribedOwner.MaxHpChanged += OnEnemyHealthChanged;
        _subscribedOwner.Died += OnEnemyDied;
        SynchronizeFromEnemy();
    }

    internal bool IsBoundTo(CardModel relinquished) =>
        ReferenceEquals(_relinquished, relinquished);

    internal static bool HasSharedHealth(CardModel card) =>
        card is not null && Bindings.TryGetValue(card, out var power) && power.IsControlActive;

    internal static void ResetBindings()
    {
        foreach (var power in Bindings.Values.Distinct().ToList())
            power.Detach();
        Bindings.Clear();
    }

    internal static void DetachForCopyReset(CardModel card)
    {
        if (card is not null && Bindings.TryGetValue(card, out var power))
            power.Detach();
    }

    internal static async Task ReleaseForCopyReset(CardModel card)
    {
        if (card is null || !Bindings.TryGetValue(card, out var power))
            return;

        // Release ownership before awaiting removal: even a removal-negating
        // effect must not let an old copy keep mutating the replacement body.
        power.Detach();
        await PowerCmd.Remove(power);
    }

    internal static bool SynchronizeBoundMonster(CardModel card)
    {
        if (card is null || !Bindings.TryGetValue(card, out var power) || !power.IsControlActive)
            return false;
        power.SynchronizeFromEnemy();
        return true;
    }

    internal static void SynchronizeFromMonster(CardModel card, int currentHp, int maxHp)
    {
        if (card is null || !Bindings.TryGetValue(card, out var power)
            || !power.IsControlActive || power._synchronizing > 0)
            return;

        power._synchronizing++;
        try
        {
            // This is the other display of one life pool, not another damage or
            // healing command. Native HP events refresh its UI without a second
            // block, damage modifier, or healing-trigger resolution.
            power.Owner.SetMaxHpInternal(Math.Max(0, maxHp));
            power.Owner.SetCurrentHpInternal(Math.Clamp(currentHp, 0, power.Owner.MaxHp));
        }
        finally
        {
            power._synchronizing--;
        }
        power.SynchronizeFromEnemy();
    }

    internal static Task ResolveSharedDeath(CardModel card) =>
        card is not null && Bindings.TryGetValue(card, out var power)
            ? power.ResolveSharedDeath()
            : Task.CompletedTask;

    private async Task ResolveSharedDeath()
    {
        if (!IsControlActive || Owner?.CurrentHp > 0 || Owner?.CombatState is null
            || HasSharedZeroHealthProtection || _resolvingDeath || _nativeDeathInProgress)
            return;

        _resolvingDeath = true;
        try
        {
            await CreatureCmd.Kill(Owner, false);
        }
        finally
        {
            _resolvingDeath = false;
            _nativeDeathInProgress = false;
        }
        if (IsControlActive)
            SynchronizeFromEnemy();
    }

    private void OnEnemyHealthChanged(int oldValue, int newValue)
    {
        if (_synchronizing > 0)
            return;
        // Native HP-loss callbacks precede BeforeDeath. A power removed by
        // another callback in that interval must not start a second Kill.
        _nativeDeathInProgress = Owner?.CurrentHp <= 0;
        SynchronizeFromEnemy();
    }

    private void OnEnemyDied(Creature creature)
    {
        if (ReferenceEquals(creature, Owner))
            _deathConfirmed = true;
    }

    private void SynchronizeFromEnemy()
    {
        if (!IsControlActive || Owner is null || _synchronizing > 0)
            return;

        _synchronizing++;
        try
        {
            MonsterFieldHealthService.SetSharedHealth(
                _relinquished, Math.Clamp(Owner.CurrentHp, 0, Owner.MaxHp), Owner.MaxHp, this);
        }
        finally
        {
            _synchronizing--;
        }
    }

    private void Detach()
    {
        if (_subscribedOwner is not null)
        {
            _subscribedOwner.CurrentHpChanged -= OnEnemyHealthChanged;
            _subscribedOwner.MaxHpChanged -= OnEnemyHealthChanged;
            _subscribedOwner.Died -= OnEnemyDied;
            _subscribedOwner = null;
        }
        if (_relinquished is not null && Bindings.TryGetValue(_relinquished, out var bound)
            && ReferenceEquals(bound, this))
            Bindings.Remove(_relinquished);
        _relinquished = null;
    }

    public override Task BeforeDeath(Creature creature)
    {
        if (ReferenceEquals(creature, Owner))
            _nativeDeathInProgress = true;
        return Task.CompletedTask;
    }

    public override bool ShouldDie(Creature creature) =>
        !ReferenceEquals(creature, Owner) || !HasSharedZeroHealthProtection;

    public override bool ShouldStopCombatFromEnding() =>
        HasSharedZeroHealthProtection && Owner?.CurrentHp <= 0;

    public override async Task AfterRemoved(Creature oldOwner)
    {
        var shouldSettleDeath = !_deathConfirmed && !_nativeDeathInProgress && !_resolvingDeath
            && oldOwner?.CurrentHp <= 0 && oldOwner.CombatState?.ContainsCreature(oldOwner) == true;
        Detach();
        if (shouldSettleDeath)
            await CreatureCmd.Kill(oldOwner, false);
    }

    internal Task SkipControlledMove(MonsterModel monster)
    {
        Flash();
        if (monster?.NextMove is not null)
            monster.MoveStateMachine?.OnMovePerformed(monster.NextMove);

        return Task.CompletedTask;
    }

    public async Task AfterMonsterLeftField(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        if (_relinquished is null || !ReferenceEquals(leaveEvent.Card, _relinquished))
            return;

        Flash();
        await PowerCmd.Remove(this);
    }

    public override async Task AfterDeath(
        PlayerChoiceContext ctx,
        Creature creature,
        bool wasRemovalPrevented,
        float deathAnimLength)
    {
        if (!ReferenceEquals(creature, Owner))
            return;

        if (wasRemovalPrevented)
        {
            _nativeDeathInProgress = false;
            SynchronizeFromEnemy();
            return;
        }

        _deathConfirmed = true;
        var relinquished = _relinquished;
        Detach();
        if (relinquished is null || !MonsterFieldService.IsOnField(relinquished))
            return;

        Flash();
        await MonsterFieldHealthService.DestroyMonsters(
            ctx,
            [relinquished],
            this,
            MonsterFieldLeaveReason.BattleDestroyed);
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide combatSide, IEnumerable<Creature> creatures)
    {
        if (!IsControlActive || !MonsterFieldService.IsOnField(_relinquished))
        {
            await PowerCmd.Remove(this);
            return;
        }

        if (Owner?.IsAlive != true && !HasSharedZeroHealthProtection)
        {
            // This turn hook is a completed-command boundary; a direct native
            // HP setter that did not call Kill must not leave a stale guard.
            _nativeDeathInProgress = false;
            await ResolveSharedDeath();
            if (!IsControlActive)
                return;
        }

        if (combatSide != Owner.Side)
            return;

        if (Amount <= 1)
        {
            var relinquished = _relinquished;
            if (relinquished is not null && MonsterFieldService.IsOnField(relinquished))
            {
                Flash();
                await MonsterFieldHealthService.DestroyMonsters(
                    ctx,
                    [relinquished],
                    this,
                    MonsterFieldLeaveReason.BattleDestroyed);
            }

            if (IsControlActive)
                await PowerCmd.Remove(this);
        }
        else
            await ThermalVortexCommandCompat.ModifyPowerAmount(ctx, this, -1, Applier, null, false);
    }
}
