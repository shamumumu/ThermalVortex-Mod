using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Patches;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class FusionPreparationPower : ThermalVortexPower
{
    private List<int> _pendingEnergyAmounts = [];

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    internal int PendingCardCount => Math.Max(1, _pendingEnergyAmounts.Count);
    internal int PendingEnergy => _pendingEnergyAmounts.Count > 0
        ? _pendingEnergyAmounts.Sum()
        : Math.Max(0, Amount);

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _pendingEnergyAmounts = [.. _pendingEnergyAmounts];
    }

    internal void Bind(int energyAmount) =>
        _pendingEnergyAmounts.Add(Math.Max(0, energyAmount));

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        var pendingEnergyAmounts = _pendingEnergyAmounts.Count > 0
            ? _pendingEnergyAmounts.ToList()
            : [Math.Max(0, Amount)];
        foreach (var _ in pendingEnergyAmounts)
        {
            var command = player.Creature.CombatState.CreateCard<XyzSummon>(player);
            await ThermalVortexCommandCompat.AddGeneratedCardToCombat(command, PileType.Hand, player, CardPilePosition.Top);
        }

        await PlayerCmd.GainEnergy(pendingEnergyAmounts.Sum(), player);
        _pendingEnergyAmounts.Clear();
        await PowerCmd.Remove(this);
    }
}

public class MillenniumContractBookPower : ThermalVortexPower, IMillenniumPower, IMonsterFieldLeaveListener
{
    private List<ContractSource> _sources = [];

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _sources = [.. _sources];
    }

    internal bool BindSource(CardModel source, int drawReduction)
    {
        if (source is null || _sources.Any(entry => ReferenceEquals(entry.Source, source)))
            return false;

        _sources.Add(new ContractSource(source, 1, Math.Max(0, drawReduction)));
        return true;
    }

    internal int SetSourceContribution(CardModel source, int count, int totalDrawReduction)
    {
        if (source is null)
            return 0;

        count = Math.Max(0, count);
        var previous = _sources.FirstOrDefault(entry => ReferenceEquals(entry.Source, source));
        _sources.RemoveAll(entry => ReferenceEquals(entry.Source, source));
        if (count > 0)
            _sources.Add(new ContractSource(source, count, Math.Max(0, totalDrawReduction)));

        return count - (previous?.Count ?? 0);
    }

    internal static int GetNaturalDrawReduction(Player player)
    {
        var power = player?.Creature?.GetPower<MillenniumContractBookPower>();
        return power?.DrawReduction ?? 0;
    }

    internal int EnergyGain =>
        2 * _sources
            .Where(entry => MonsterFieldService.IsOnField(entry.Source))
            .Sum(entry => entry.Count);

    internal int DrawReduction =>
        _sources
            .Where(entry => MonsterFieldService.IsOnField(entry.Source))
            .Sum(entry => entry.DrawReduction);

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        var removed = RemoveInactiveSources();
        if (_sources.Count == 0)
        {
            await PowerCmd.Remove(this);
            return;
        }

        if (removed > 0)
            await ThermalVortexCommandCompat.ModifyPowerAmount(ctx, this, -removed, Applier, null, false);

        Flash();
        await PlayerCmd.GainEnergy(2 * _sources.Sum(entry => entry.Count), player);
    }

    public async Task AfterMonsterLeftField(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        var removed = -SetSourceContribution(leaveEvent.Card, 0, 0);
        if (removed <= 0)
            return;

        if (Amount <= removed)
            await PowerCmd.Remove(this);
        else
            await ThermalVortexCommandCompat.ModifyPowerAmount(ctx, this, -removed, Applier, null, false);
    }

    private int RemoveInactiveSources()
    {
        var removed = _sources
            .Where(entry => !MonsterFieldService.IsOnField(entry.Source))
            .Sum(entry => entry.Count);
        _sources.RemoveAll(entry => !MonsterFieldService.IsOnField(entry.Source));
        return removed;
    }

    private sealed record ContractSource(CardModel Source, int Count, int DrawReduction);
}

public class SinkingLandPower : ThermalVortexPower, IMonsterFieldCapacityModifier
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public int MonsterFieldCapacityBonus => -Math.Max(0, Amount);
}

public class DimensionalExpansionPower : ThermalVortexPower, IMonsterFieldCapacityModifier
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public int MonsterFieldCapacityBonus => Math.Max(0, Amount);
}

public class OverloadedSummonSlotPower : ThermalVortexPower, IMonsterFieldCapacityModifier
{
    internal const int SlotsToDestroyAtTurnEnd = 2;
    private List<int> _slotGrants = [];
    private int _resolvedGrants;
    private int _destroyedLegalSlots;
    private int _transferredSlotLoss;
    private bool _isResolving;
    private bool _isTransferringLoss;
    private int _transferBaseline;
    private int _transferLimit;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public int MonsterFieldCapacityBonus =>
        UntransferredCapacityBonus + ObservedTransfer;
    internal int PendingResolutionCount => Math.Max(0, _slotGrants.Count - _resolvedGrants);

    protected override IEnumerable<MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVar> CanonicalVars
    {
        get
        {
            foreach (var variable in base.CanonicalVars)
                yield return variable;

            yield return new MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVar("PendingResolutions", 0);
        }
    }

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _slotGrants = [.. _slotGrants];
        _transferredSlotLoss += ObservedTransfer;
        _isTransferringLoss = false;
        _isResolving = false;
    }

    internal void RegisterGrant(int slots)
    {
        if (slots <= 0)
            return;

        _slotGrants.Add(slots);
        RefreshPendingResolutions();
    }

    private int UntransferredCapacityBonus =>
        Math.Max(0, Amount) - _destroyedLegalSlots + _transferredSlotLoss;

    // While the persistent power is being applied, native callbacks can read
    // capacity. Offset only the loss already present there, so they never see
    // both this pending reduction and its persistent replacement at once.
    private int ObservedTransfer => _isTransferringLoss
        ? Math.Clamp(
            (Owner?.GetPower<SinkingLandPower>()?.Amount ?? 0) - _transferBaseline,
            0,
            _transferLimit)
        : 0;

    private bool IsStillApplied =>
        ReferenceEquals(Owner?.GetPower<OverloadedSummonSlotPower>(), this);

    private void RefreshPendingResolutions()
    {
        var pending = DynamicVars["PendingResolutions"];
        pending.BaseValue = PendingResolutionCount;
        pending.ResetToBase();
        InvokeDisplayAmountChanged();
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide combatSide, IEnumerable<Creature> creatures)
    {
        if (Owner?.Player is not { } player
            || combatSide != Owner.Side
            || _isResolving
            || !IsStillApplied)
            return;

        _isResolving = true;
        try
        {
            while (IsStillApplied)
            {
                while (_resolvedGrants < _slotGrants.Count)
                {
                    var capacity = MonsterFieldService.GetDisplayCapacity(player);
                    var monsters = MonsterFieldService.GetMonsters(player).ToList();
                    var visibleSlots = Math.Max(capacity, monsters.Count);
                    var firstDestroyedSlot = Math.Max(0, visibleSlots - SlotsToDestroyAtTurnEnd);
                    // Empty slots occupy the right edge too. Capture the cards
                    // before changing capacity or running any leave callbacks.
                    var targets = monsters.Skip(firstDestroyedSlot).Reverse().ToList();

                    _resolvedGrants++;
                    _destroyedLegalSlots += Math.Min(SlotsToDestroyAtTurnEnd, capacity);
                    RefreshPendingResolutions();
                    Flash();
                    await MonsterFieldHealthService.DestroyMonsters(ctx, targets, this);
                    if (!IsStillApplied)
                        return;
                }

                var lossToTransfer = Math.Max(0, -UntransferredCapacityBonus);
                if (lossToTransfer > 0)
                {
                    _transferBaseline = Owner.GetPower<SinkingLandPower>()?.Amount ?? 0;
                    _transferLimit = lossToTransfer;
                    _isTransferringLoss = true;
                    try
                    {
                        await ThermalVortexCommandCompat.ApplyPower<SinkingLandPower>(
                            ctx, Owner, lossToTransfer, Owner, null, false);
                    }
                    finally
                    {
                        _transferredSlotLoss += ObservedTransfer;
                        _isTransferringLoss = false;
                    }

                    if (!IsStillApplied)
                        return;

                    // A prevented or partial application must not restore
                    // destroyed slots when this temporary power is removed.
                    if (UntransferredCapacityBonus < 0)
                        return;
                }

                // Leave/power callbacks may have played another copy. Its
                // registration belongs to the same turn-end resolution.
                if (_resolvedGrants < _slotGrants.Count)
                    continue;

                await PowerCmd.Remove(this);
                return;
            }
        }
        finally
        {
            _isResolving = false;
        }
    }
}

public class SharedFatePower : ThermalVortexPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
}

public class DarkDuelPower : ThermalVortexPower
{
    private Dictionary<CardModel, int> _expirationTurns = new(new ReferenceComparer<CardModel>());
    private HashSet<CardModel> _expiringMonsters = new(new ReferenceComparer<CardModel>());

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _expirationTurns = new(_expirationTurns, new ReferenceComparer<CardModel>());
        _expiringMonsters = new(new ReferenceComparer<CardModel>());
    }

    internal bool UpdateZeroHealthProtection(CardModel monster, int previousHp, int currentHp)
    {
        if (monster is null)
            return false;

        if (currentHp > 0 || !MonsterFieldService.IsOnField(monster))
        {
            ForgetMonster(monster);
            return false;
        }

        // Resolving an expired death must not grant the same monster a new
        // grace period through shared-health or native revival callbacks.
        if (_expiringMonsters.Contains(monster))
            return false;

        if (previousHp > 0 && Owner?.Player?.PlayerCombatState is { } combatState)
            _expirationTurns.TryAdd(monster, combatState.TurnNumber + 1);

        return _expirationTurns.ContainsKey(monster);
    }

    internal void ForgetMonster(CardModel monster) => _expirationTurns.Remove(monster);

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide combatSide, IEnumerable<Creature> creatures)
    {
        if (Owner?.Player?.PlayerCombatState is not { } combatState || combatSide != Owner.Side)
            return;

        var expired = _expirationTurns
            .Where(entry => entry.Value <= combatState.TurnNumber)
            .Select(entry => entry.Key)
            .ToList();
        if (expired.Count == 0)
            return;

        Flash();
        _expiringMonsters.UnionWith(expired);
        foreach (var monster in expired)
            _expirationTurns.Remove(monster);
        try
        {
            await MonsterFieldHealthService.ExpireZeroHealthProtection(ctx, expired, this);
        }
        finally
        {
            _expiringMonsters.ExceptWith(expired);
        }
    }
}

internal enum RaTransitionTarget
{
    Phoenix,
    Ra
}

public class RaTransitionPower : ThermalVortexPower, IMonsterFieldLeaveListener
{
    private List<RaTransitionEntry> _entries = [];

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    internal bool HasPhoenixTransition => HasPendingTransition(RaTransitionTarget.Phoenix);
    internal bool HasRaTransition => HasPendingTransition(RaTransitionTarget.Ra);

    internal IReadOnlyList<int> SpherePreviewUpgradeLevels =>
        _entries
            .Where(entry => entry.Target == RaTransitionTarget.Phoenix
                && MonsterFieldService.IsOnField(entry.Source))
            .Select(entry => MonsterIdentity.GetEffectiveCard(entry.Source))
            .OfType<WingedDragonOfRaSphereMode>()
            .Select(sphere => sphere.CurrentUpgradeLevel)
            .Distinct()
            .OrderBy(level => level)
            .ToArray();

    private bool HasPendingTransition(RaTransitionTarget target) =>
        _entries.Any(entry => entry.Target == target && MonsterFieldService.IsOnField(entry.Source));

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _entries = [.. _entries];
    }

    internal void Bind(CardModel source, RaTransitionTarget target)
    {
        if (source is null || _entries.Any(entry => ReferenceEquals(entry.Source, source)))
            return;

        _entries.Add(new RaTransitionEntry(source, target));
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        foreach (var entry in _entries.ToList())
        {
            _entries.Remove(entry);
            if (!MonsterFieldService.IsOnField(entry.Source))
                continue;

            Flash();
            var transformed = await Transform(ctx, player, entry);
            if (!transformed && MonsterFieldService.IsOnField(entry.Source))
                _entries.Add(entry);
        }

        if (_entries.Count == 0)
            await PowerCmd.Remove(this);
    }

    public async Task AfterMonsterLeftField(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        _entries.RemoveAll(entry => ReferenceEquals(entry.Source, leaveEvent.Card));
        if (_entries.Count == 0)
            await PowerCmd.Remove(this);
    }

    private static async Task<bool> Transform(PlayerChoiceContext ctx, Player player, RaTransitionEntry entry)
    {
        var isOperationValid = MonsterFieldService.CaptureMaterialUseValidity(player);
        var previousIndex = MonsterFieldService.GetMonsters(player)
            .ToList()
            .FindIndex(card => ReferenceEquals(card, entry.Source));

        CardModel next = entry.Target switch
        {
            RaTransitionTarget.Phoenix => ThermalVortexGeneratedCards.CreateGeneratedCard<WingedDragonOfRaPhoenix>(player),
            RaTransitionTarget.Ra => ThermalVortexGeneratedCards.CreateGeneratedCard<WingedDragonOfRa>(player),
            _ => null
        };
        if (next is null)
            return false;

        using var replacementSlot = MonsterFieldService.PushTemporaryCapacityBonus(player, 1);
        var played = false;
        CyberDevourState.CombatGrowthTransfer growthTransfer = null;
        try
        {
            played = await EffectTargeting.PlayImmediately(
                ctx,
                player,
                next,
                prepare: async () =>
                {
                    growthTransfer = CyberDevourState.BeginCombatGrowthTransfer(entry.Source, next);
                    replacementSlot.Dispose();
                    using (MonsterFieldService.ReserveCapacitySlots(player))
                    {
                        await MonsterFieldService.SendToExhaust(
                            null,
                            [entry.Source],
                            entry.Source,
                            MonsterFieldLeaveReason.Exhaust);
                    }
                    if (!isOperationValid() || !growthTransfer.SourceDeparted)
                        return;
                    next.SetToFreeThisTurn();
                    await ThermalVortexCommandCompat.AddGeneratedCardToCombat(
                        next,
                        PileType.Hand,
                        player,
                        CardPilePosition.Top);
                },
                canPrepare: () => MonsterFieldService.IsOnField(entry.Source),
                excludeFromCardPlayCount: true,
                canExecute: () => growthTransfer?.SourceDeparted == true,
                isOperationValid: isOperationValid);
        }
        finally
        {
            growthTransfer?.Dispose();
            if (growthTransfer?.Committed != true && MonsterFieldService.IsOnField(entry.Source))
                await CyberDevourState.SynchronizePersistentEffects(ctx, entry.Source);
            if (!played && growthTransfer?.Committed != true)
                await EffectTargeting.RemoveUncommittedTransientCard(next);
        }

        if (growthTransfer?.Committed != true)
            return false;

        if (!MonsterFieldService.IsOnField(next))
            return true;

        if (previousIndex >= 0)
        {
            var monsters = MonsterFieldService.GetMonsters(player).ToList();
            var currentIndex = monsters.FindIndex(card => ReferenceEquals(card, next));
            if (currentIndex >= 0 && currentIndex != previousIndex)
                MonsterFieldService.MoveMonsterByCardEffect(player, currentIndex, previousIndex);
        }

        return true;
    }

    private sealed record RaTransitionEntry(CardModel Source, RaTransitionTarget Target);
}

internal static class RaRevivalService
{
    private static readonly AsyncLocal<MonsterFieldDamageGuard.AttackRevivalEntry> CurrentAttack = new();

    internal static void ResetForRunTransition() => CurrentAttack.Value = null;

    internal static bool CanRevive(Player player) =>
        CurrentAttack.Value?.CanConsume(player) == true;

    internal static IDisposable EnterAttackResolution(MonsterFieldDamageGuard.AttackRevivalEntry attackRevival)
    {
        var previous = CurrentAttack.Value;
        CurrentAttack.Value = attackRevival;
        return new RestoreAttack(previous);
    }

    internal static async Task Revive(Player player)
    {
        if (player?.Creature is null
            || CurrentAttack.Value?.TryConsume(player, out var snapshot, out var fullAttackDamage) != true)
        {
            return;
        }

        using var revivalSnapshot = snapshot;
        var phoenix = snapshot.Card;
        // These commands belong to the player's Phoenix, even though native
        // death resolution is still inside an enemy's PerformMove scope.
        using var effectSource = AshBlossomActionNegation.EnterSource(phoenix);
        var creature = player.Creature;
        var maximumBefore = creature.MaxHp;
        MainFile.Logger.Info(
            $"RaRevival begin damage={fullAttackDamage} maxHpBefore={maximumBefore} phoenixPile={phoenix.Pile?.Type.ToString() ?? "null"}");
        await CreatureCmd.SetMaxHp(creature, (decimal)maximumBefore + fullAttackDamage);
        await CreatureCmd.SetCurrentHp(creature, fullAttackDamage);
        try
        {
            if (!await snapshot.RestoreMonster(new BlockingPlayerChoiceContext()))
                MainFile.Logger.Info("RaRevival player restored but monster could not re-enter the field");
        }
        catch (Exception exception)
        {
            // Player revival is already committed. Do not retry the death or
            // consume this hit again if a monster-entry callback fails.
            MainFile.Logger.Info("RaRevival monster restoration failed: " + exception);
        }
        MainFile.Logger.Info(
            $"RaRevival complete hp={creature.CurrentHp}/{creature.MaxHp} damage={fullAttackDamage} phoenixPile={phoenix.Pile?.Type.ToString() ?? "null"}");
    }

    private sealed class RestoreAttack(MonsterFieldDamageGuard.AttackRevivalEntry previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CurrentAttack.Value = previous;
        }
    }
}
