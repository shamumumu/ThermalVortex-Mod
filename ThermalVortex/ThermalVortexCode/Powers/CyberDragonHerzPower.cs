using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class CyberDragonHerzPower : ThermalVortexPower
{
    private List<CardModel> _pendingHerz = [];
    private Dictionary<CardModel, bool> _copiedUpgradeStates = new(new ReferenceComparer<CardModel>());

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    internal IReadOnlyList<bool> GeneratedDragonUpgradeStates =>
        _pendingHerz
            .Where(MonsterFieldService.IsOnField)
            .Select(herz => _copiedUpgradeStates.TryGetValue(herz, out var copiedUpgraded)
                ? copiedUpgraded
                : herz.CurrentUpgradeLevel > 0)
            .Distinct()
            .OrderBy(upgraded => upgraded)
            .ToArray();

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _pendingHerz = [.. _pendingHerz];
        _copiedUpgradeStates = new Dictionary<CardModel, bool>(
            _copiedUpgradeStates,
            new ReferenceComparer<CardModel>());
    }

    internal void Bind(CardModel herz)
    {
        if (herz is not null && !_pendingHerz.Contains(herz))
            _pendingHerz.Add(herz);
    }

    internal void BindCompleteCopy(CardModel host, bool copiedUpgraded)
    {
        Bind(host);
        if (host is not null)
            _copiedUpgradeStates[host] = copiedUpgraded;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        foreach (var herz in _pendingHerz.ToList())
        {
            _pendingHerz.Remove(herz);
            var hasCopiedUpgrade = _copiedUpgradeStates.Remove(herz, out var copiedUpgraded);
            if (!MonsterFieldService.IsOnField(herz))
                continue;

            Flash();
            var transformed = await TransformIntoCyberDragon(
                ctx,
                player,
                herz,
                hasCopiedUpgrade ? copiedUpgraded : null);
            if (!transformed && MonsterFieldService.IsOnField(herz))
            {
                _pendingHerz.Add(herz);
                if (hasCopiedUpgrade)
                    _copiedUpgradeStates[herz] = copiedUpgraded;
            }
        }

        if (_pendingHerz.Count == 0)
            await PowerCmd.Remove(this);
    }

    private static async Task<bool> TransformIntoCyberDragon(
        PlayerChoiceContext ctx,
        Player player,
        CardModel herz,
        bool? copiedUpgraded)
    {
        var isOperationValid = MonsterFieldService.CaptureMaterialUseValidity(player);
        var previousIndex = MonsterFieldService.GetMonsters(player)
            .ToList()
            .FindIndex(card => ReferenceEquals(card, herz));

        var dragon = CyberSeries.CreateGeneratedCard<CyberDragon>(
            player,
            copiedUpgraded.HasValue ? null : herz);
        if (dragon is null)
            return false;

        if (copiedUpgraded == true)
            ThermalVortexGeneratedCards.ApplyUpgradeLevel(dragon, 1);

        using var replacementSlot = MonsterFieldService.PushTemporaryCapacityBonus(player, 1);
        var played = false;
        CyberDevourState.CombatGrowthTransfer growthTransfer = null;
        try
        {
            // Resolve the mandatory target before committing the transformation.
            // The replacement then uses a normal CardPlay, so summon damage and
            // every other OnPlay hook execute exactly once.
            played = await EffectTargeting.PlayImmediately(
                ctx,
                player,
                dragon,
                prepare: async () =>
                {
                    growthTransfer = CyberDevourState.BeginCombatGrowthTransfer(herz, dragon);
                    replacementSlot.Dispose();
                    using (MonsterFieldService.ReserveCapacitySlots(player))
                    {
                        await MonsterFieldService.SendToExhaust(
                            ctx,
                            [herz],
                            dragon,
                            MonsterFieldLeaveReason.Exhaust);
                    }
                    if (!isOperationValid() || !growthTransfer.SourceDeparted)
                        return;
                    dragon.SetToFreeThisTurn();
                    await ThermalVortexCommandCompat.AddGeneratedCardToCombat(
                        dragon,
                        PileType.Hand,
                        player,
                        CardPilePosition.Top);
                },
                canPrepare: () => MonsterFieldService.IsOnField(herz),
                excludeFromCardPlayCount: true,
                canExecute: () => growthTransfer?.SourceDeparted == true,
                isOperationValid: isOperationValid);
        }
        finally
        {
            growthTransfer?.Dispose();
            if (growthTransfer?.Committed != true && MonsterFieldService.IsOnField(herz))
                await CyberDevourState.SynchronizePersistentEffects(ctx, herz);
            if (!played && growthTransfer?.Committed != true)
            {
                await EffectTargeting.RemoveUncommittedTransientCard(dragon);
            }
        }

        if (growthTransfer?.Committed != true)
            return false;

        // Entering and then leaving during summon callbacks still completes
        // the transformation. Its growth now belongs to the successor.
        if (!MonsterFieldService.IsOnField(dragon))
            return true;

        if (previousIndex >= 0)
        {
            var monsters = MonsterFieldService.GetMonsters(player).ToList();
            var currentIndex = monsters.FindIndex(card => ReferenceEquals(card, dragon));
            if (currentIndex >= 0 && currentIndex != previousIndex)
                MonsterFieldService.MoveMonsterByCardEffect(player, currentIndex, previousIndex);
        }

        return true;
    }
}
