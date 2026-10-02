using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Patches;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class TurnCardPlayTrackerPower : ThermalVortexPower
{
    public int CardsPlayedThisTurn { get; private set; }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override bool IsVisibleInternal => false;
    public override bool ShouldPlayVfx => false;

    public override Task AfterCardPlayed(PlayerChoiceContext ctx, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner?.Creature == Owner
            && !CardPlayCountExemption.IsExempt(cardPlay))
        {
            CardsPlayedThisTurn++;
        }

        return Task.CompletedTask;
    }

    public override Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player.Creature == Owner)
            CardsPlayedThisTurn = 0;

        return Task.CompletedTask;
    }
}
