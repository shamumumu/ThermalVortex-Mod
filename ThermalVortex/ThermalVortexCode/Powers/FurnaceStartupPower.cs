using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class FurnaceStartupPower : ThermalVortexPower, IMonsterFieldEnterResolvedListener
{
    private bool _triggeredThisTurn;
    private PlayerCombatState _turnCombatState;
    private int _turnNumber;

    internal bool TriggeredThisTurn => _triggeredThisTurn
        && ReferenceEquals(_turnCombatState, Owner?.Player?.PlayerCombatState)
        && _turnNumber == _turnCombatState?.TurnNumber;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent) =>
        await TryTriggerForSummon(ctx, enterEvent.Card);

    internal async Task TryTriggerForSummon(PlayerChoiceContext ctx, CardModel card)
    {
        var combatState = Owner?.Player?.PlayerCombatState;
        if (combatState is null)
            return;

        if (!ReferenceEquals(_turnCombatState, combatState)
            || _turnNumber != combatState.TurnNumber)
        {
            _turnCombatState = combatState;
            _turnNumber = combatState.TurnNumber;
            _triggeredThisTurn = false;
        }

        if (!_triggeredThisTurn
            && Amount > 0
            && ReferenceEquals(Owner.GetPower<FurnaceStartupPower>(), this)
            && card?.Owner?.Creature == Owner
            && MonsterFieldService.IsOnField(card))
        {
            _triggeredThisTurn = true;
            await CardPileCmd.Draw(ctx, Amount, card.Owner, false);
        }
    }

}
