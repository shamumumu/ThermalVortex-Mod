using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.Patches;

namespace ThermalVortex.ThermalVortexCode.Powers;

[Flags]
public enum EnemyActionKind
{
    None = 0,
    Any = 1,
    Attack = 2,
    PowerChange = 4,
    Summon = 8
}

internal readonly struct EnemyActionSummary(
    EnemyActionKind kind,
    int anyCount,
    int attackCount,
    int powerChangeCount,
    int summonCount)
{
    public EnemyActionKind Kind { get; } = kind;
    public int AnyCount { get; } = anyCount;
    public int AttackCount { get; } = attackCount;
    public int PowerChangeCount { get; } = powerChangeCount;
    public int SummonCount { get; } = summonCount;
    public bool HasTriggers => Kind != EnemyActionKind.None && AnyCount > 0;
}

public abstract class EnemyActionDrawPower : ThermalVortexPower, IMonsterFieldLeaveListener
{
    private List<DrawSource> _sourceMonsters = [];

    protected abstract EnemyActionKind TriggerKinds { get; }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _sourceMonsters = [.. _sourceMonsters];
    }

    internal bool BindSource(CardModel sourceMonster)
    {
        if (sourceMonster is null || _sourceMonsters.Any(entry => ReferenceEquals(entry.Card, sourceMonster)))
            return false;

        _sourceMonsters.Add(new DrawSource(sourceMonster, 1));
        return true;
    }

    internal int SetSourceCount(CardModel sourceMonster, int count)
    {
        if (sourceMonster is null)
            return 0;

        count = Math.Max(0, count);
        var previous = _sourceMonsters.FirstOrDefault(entry => ReferenceEquals(entry.Card, sourceMonster));
        _sourceMonsters.RemoveAll(entry => ReferenceEquals(entry.Card, sourceMonster));
        if (count > 0)
            _sourceMonsters.Add(new DrawSource(sourceMonster, count));

        return count - (previous?.Count ?? 0);
    }

    internal async Task TryDrawForEnemyAction(PlayerChoiceContext ctx, EnemyActionSummary action)
    {
        var drawAmount = CaptureDrawAmount(action);
        var player = Owner?.Player;
        if (player is null || drawAmount <= 0)
            return;

        Flash();
        await CardPileCmd.Draw(ctx, drawAmount, player, false);
    }

    internal int CaptureDrawAmount(EnemyActionSummary action)
    {
        var triggerCount = CountTriggers(action);
        if (Amount <= 0 || triggerCount <= 0)
            return 0;

        var activeAmount = _sourceMonsters.Count > 0
            ? _sourceMonsters.Where(entry => MonsterFieldService.IsOnField(entry.Card)).Sum(entry => entry.Count)
            : Amount;
        return Owner?.Player is null ? 0 : activeAmount * triggerCount;
    }

    public async Task AfterMonsterLeftField(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        var removed = -SetSourceCount(leaveEvent.Card, 0);
        if (removed <= 0)
            return;

        if (Amount <= removed)
            await PowerCmd.Remove(this);
        else
            await ThermalVortexCommandCompat.ModifyPowerAmount(ctx, this, -removed, Applier, null, false);
    }

    public override Task AfterPowerAmountChanged(
        PlayerChoiceContext ctx,
        PowerModel power,
        decimal amount,
        Creature applier,
        CardModel card)
    {
        EnemyActionDrawResolution.CapturePowerChange(this, power, amount, applier);
        return Task.CompletedTask;
    }

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        EnemyActionDrawResolution.CaptureStatusCard(this, card, oldPileType, source);
        return Task.CompletedTask;
    }

    private int CountTriggers(EnemyActionSummary action)
    {
        if ((TriggerKinds & action.Kind) == EnemyActionKind.None)
            return 0;

        if ((TriggerKinds & EnemyActionKind.Any) != 0)
            return action.AnyCount;

        var count = 0;
        if ((TriggerKinds & EnemyActionKind.Attack) != 0)
            count += action.AttackCount;

        if ((TriggerKinds & EnemyActionKind.PowerChange) != 0)
            count += action.PowerChangeCount;

        if ((TriggerKinds & EnemyActionKind.Summon) != 0)
            count += action.SummonCount;

        return count;
    }

    private sealed record DrawSource(CardModel Card, int Count);
}
