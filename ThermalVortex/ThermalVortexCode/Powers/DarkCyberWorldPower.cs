using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class DarkCyberWorldPower : ThermalVortexPower
{
    private bool _hasSanity;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    internal void EnableSanity() => _hasSanity = true;

    public override async Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide combatSide, IEnumerable<Creature> creatures)
    {
        if (combatSide != Owner.Side || Owner?.Player is null)
            return;

        var cyberMonsters = CardSelectionHelper.DiscardPileCyberMonsters(Owner.Player);
        if (cyberMonsters.Count == 0)
            return;

        Flash();
        if (cyberMonsters.Count == 1)
        {
            if (_hasSanity)
                return;

            var victim = cyberMonsters[0];
            if (CardSelectionHelper.IsCurrentPileCard(Owner.Player, victim, PileType.Discard))
                await CardCmd.Exhaust(ctx ?? new BlockingPlayerChoiceContext(), victim, false, false);
            return;
        }

        var devourer = Owner.Player.RunState.Rng.CombatCardSelection.NextItem(cyberMonsters);
        var targets = cyberMonsters.Where(card => !ReferenceEquals(card, devourer)).ToList();
        var target = Owner.Player.RunState.Rng.CombatCardSelection.NextItem(targets);
        if (!CardSelectionHelper.IsCurrentPileCard(Owner.Player, devourer, PileType.Discard)
            || !CardSelectionHelper.IsCurrentPileCard(Owner.Player, target, PileType.Discard))
        {
            return;
        }

        CyberDevourState.Absorb(devourer, target);
        await CardCmd.Exhaust(ctx ?? new BlockingPlayerChoiceContext(), target, false, false);
    }
}
