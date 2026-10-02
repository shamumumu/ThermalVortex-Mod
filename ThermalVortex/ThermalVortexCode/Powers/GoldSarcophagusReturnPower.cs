using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class GoldSarcophagusReturnPower : ThermalVortexPower
{
    private CardModel _sealedCard;
    private int _turnsRemaining;

    internal CardModel SealedCard => _sealedCard;
    internal string SealedCardName => _sealedCard?.Title ?? string.Empty;
    internal int TurnsRemaining => Math.Max(0, _turnsRemaining);

    public override PowerType Type => PowerType.Buff;
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int DisplayAmount => TurnsRemaining;

    internal void Bind(CardModel card, int turns)
    {
        ArgumentNullException.ThrowIfNull(card);
        _sealedCard = card;
        _turnsRemaining = Math.Max(1, turns);
        InvokeDisplayAmountChanged();
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner || _sealedCard is null || _turnsRemaining <= 0)
            return;

        _turnsRemaining--;
        InvokeDisplayAmountChanged();
        if (_turnsRemaining > 0)
            return;

        var card = _sealedCard;
        if (card.Pile?.Type == PileType.Exhaust)
            await CardPileCmd.Add(card, PileType.Hand, CardPilePosition.Top, this, false);
        await PowerCmd.Remove(this);
    }
}
