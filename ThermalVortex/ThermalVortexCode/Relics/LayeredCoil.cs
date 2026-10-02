using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Relics;

public class LayeredCoil : ThermalVortexRelic
{
    private bool _triggeredThisTurn;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override async Task AfterCardPlayed(PlayerChoiceContext ctx, CardPlay cardPlay)
    {
        if (!_triggeredThisTurn
            && cardPlay.IsAutoPlay
            && cardPlay.Card is MonsterCard
            && cardPlay.Card.Owner == Owner)
        {
            _triggeredThisTurn = true;
            Flash();
            await CardPileCmd.Draw(ctx, 1, Owner, false);
        }
    }

    public override Task AfterPlayerTurnStart(PlayerChoiceContext ctx, MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (player == Owner)
            _triggeredThisTurn = false;

        return Task.CompletedTask;
    }
}
