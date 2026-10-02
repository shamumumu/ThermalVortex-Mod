using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class MillenniumTemplePower : ThermalVortexPower, IMillenniumPower
{
    public int BlockPerCount { get; set; } = MillenniumTemple.BaseBlockPerCount;
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide combatSide, IEnumerable<Creature> creatures)
    {
        if (combatSide != Owner.Side || Owner?.Player is null)
            return;

        var count = MillenniumSeries.CountTotal(Owner.Player);
        var block = count * Math.Max(0, BlockPerCount);
        if (block <= 0)
            return;

        Flash();
        await ThermalVortexCommandCompat.GainBlock(
            Owner, block, ValueProp.Unpowered, null, false);
    }
}

public class MillenniumPartnerPower : ThermalVortexPower, IMillenniumPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        var card = MillenniumSeries.CreateRandomMainDeckMonster(player);
        if (card is null)
            return;

        Flash();
        await ThermalVortexCommandCompat.AddGeneratedCardToCombat(card, PileType.Hand, player, CardPilePosition.Top);
    }
}

public class MillenniumTreasureGolemPower : ThermalVortexPower, IMillenniumPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        var golems = MonsterFieldService.GetMonsters(player).OfType<MillenniumTreasureGolem>().ToList();
        if (golems.Count == 0)
        {
            await PowerCmd.Remove(this);
            return;
        }

        var layers = golems.Sum(golem => golem.CurrentWeak);
        var enemies = Owner.CombatState?.HittableEnemies.ToList() ?? [];
        if (layers <= 0 || enemies.Count == 0)
            return;

        Flash();
        await ThermalVortexCommandCompat.ApplyPower<WeakPower>(ctx, enemies, layers, Owner, null, false);
    }
}

public class AwakenedMillenniumPrimitivePower : ThermalVortexPower, IMillenniumPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        var primitives = MonsterFieldService.GetMonsters(player).OfType<AwakenedMillenniumPrimitive>().ToList();
        if (primitives.Count == 0)
        {
            await PowerCmd.Remove(this);
            return;
        }

        var layers = primitives.Sum(primitive => primitive.CurrentVulnerable);
        var enemies = Owner.CombatState?.HittableEnemies.ToList() ?? [];
        if (layers <= 0 || enemies.Count == 0)
            return;

        Flash();
        await ThermalVortexCommandCompat.ApplyPower<VulnerablePower>(ctx, enemies, layers, Owner, null, false);
    }
}

public class MillenniumSleepingTabletPower : ThermalVortexPower, IMillenniumPower, IMonsterFieldLeaveResolvedListener
{
    private List<PendingTransformation> _pending = [];
    private bool _resolving;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    internal int RandomPieceCount => _pending.Count(entry => !entry.ChoosePiece && IsCurrentSource(entry));
    internal int ChosenPieceCount => _pending.Count(entry => entry.ChoosePiece && IsCurrentSource(entry));

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _pending = [.. _pending];
        _resolving = false;
    }

    internal void Bind(CardModel source, bool choosePiece)
    {
        if (source?.Owner?.PlayerCombatState is not { } combatState
            || !ReferenceEquals(source.Owner.Creature, Owner)
            || !MonsterFieldService.IsOnField(source))
            return;

        // A fresh entry is a new delayed effect, including a replay of the same
        // physical card. An entry during turn-start waits for the next turn.
        _pending.RemoveAll(entry => ReferenceEquals(entry.Source, source));
        _pending.Add(new PendingTransformation(source, choosePiece, combatState.TurnNumber + 1));
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner || _resolving)
            return;

        _pending.RemoveAll(entry => !IsCurrentSource(entry));
        var due = _pending
            .Where(entry => entry.DueTurn <= player.PlayerCombatState.TurnNumber)
            .ToList();
        var isOperationValid = MonsterFieldService.CaptureMaterialUseValidity(player);
        _resolving = true;
        try
        {
            foreach (var entry in due)
            {
                if (!isOperationValid())
                    return;
                if (!_pending.Remove(entry) || !IsCurrentSource(entry))
                    continue;

                Flash();
                await MonsterFieldService.SendToDiscard(
                    ctx,
                    [entry.Source],
                    this,
                    MonsterFieldLeaveReason.Transformation);
                if (!isOperationValid())
                    return;
                if (MonsterFieldService.IsOnField(entry.Source))
                {
                    // A prevented departure did not complete the conversion.
                    if (!_pending.Any(pending => ReferenceEquals(pending.Source, entry.Source)))
                        _pending.Add(entry);
                    continue;
                }

                if (entry.ChoosePiece)
                    await MillenniumSeries.AddChosenSealedPieceToHand(ctx, player, this);
                else
                    await MillenniumSeries.AddRandomSealedPieceToHand(player, this);
            }
        }
        finally
        {
            _resolving = false;
        }

        if (_pending.Count == 0 && ReferenceEquals(Owner.GetPower<MillenniumSleepingTabletPower>(), this))
            await PowerCmd.Remove(this);
    }

    public async Task AfterMonsterLeftFieldResolved(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        if (!MonsterFieldService.IsOnField(leaveEvent.Card))
            _pending.RemoveAll(entry => ReferenceEquals(entry.Source, leaveEvent.Card));

        if (!_resolving && _pending.Count == 0
            && ReferenceEquals(Owner.GetPower<MillenniumSleepingTabletPower>(), this))
            await PowerCmd.Remove(this);
    }

    private static bool IsCurrentSource(PendingTransformation entry) =>
        MonsterFieldService.IsOnField(entry.Source)
        && MonsterIdentity.Matches<MillenniumSleepingTablet>(entry.Source);

    private sealed record PendingTransformation(CardModel Source, bool ChoosePiece, int DueTurn);
}
