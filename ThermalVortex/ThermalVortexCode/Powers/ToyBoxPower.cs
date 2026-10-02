using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class ToyBoxPower : ThermalVortexPower, IMonsterFieldLeaveListener
{
    private List<bool> _sourceUpgradeOrder = [];
    private List<bool> _availableUpgradeOrder = [];

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    // The bound sources recur every turn, including versions whose current
    // turn's opportunity has already been spent. Return a detached snapshot.
    internal IReadOnlyList<bool> GeneratedTokenUpgradeStates =>
        _sourceUpgradeOrder.Distinct().OrderBy(upgraded => upgraded).ToArray();

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _sourceUpgradeOrder = [.. _sourceUpgradeOrder];
        _availableUpgradeOrder = [.. _availableUpgradeOrder];
    }

    internal void BindUpgradedToken(bool upgraded)
    {
        _sourceUpgradeOrder.Add(upgraded);
        _availableUpgradeOrder.Add(upgraded);
    }

    public override Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature == Owner)
            _availableUpgradeOrder = [.. _sourceUpgradeOrder];

        return Task.CompletedTask;
    }

    public async Task AfterMonsterLeftField(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        if (_availableUpgradeOrder.Count == 0
            || !leaveEvent.WasDestroyedByBattle
            || leaveEvent.Card?.Owner?.Creature != Owner
            || Owner?.Player is null)
        {
            return;
        }

        var generateUpgradedToken = _availableUpgradeOrder[0];
        Flash();
        using var capacity = MonsterFieldService.PushTemporaryCapacityBonus(Owner.Player, 1);
        var token = Owner.CombatState.CreateCard<ToyBoxToken>(Owner.Player);
        if (generateUpgradedToken)
            ThermalVortexGeneratedCards.ApplyUpgradeLevel(token, 1);
        await ThermalVortexCommandCompat.AddGeneratedCardToCombat(token, PileType.Hand, Owner.Player, CardPilePosition.Top);
        var played = await EffectTargeting.PlayImmediately(ctx, Owner.Player, token);
        if (played)
            _availableUpgradeOrder.RemoveAt(0);
        else
            await EffectTargeting.RemoveUncommittedTransientCard(token);
    }
}
