using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class FullArmorThunderLancePower : ThermalVortexPower
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override int DisplayAmount => UpkeepBlock;

    // Display the field's next upkeep contribution. Amount records applications;
    // neither it nor this preview replaces the existing reward calculation.
    internal int UpkeepBlock => Owner?.Player is { } player
        ? MonsterFieldService.GetMonsters(player)
            .OfType<FullArmorThunderLance>()
            .Sum(source => source.CurrentUpkeepBlock)
        : 0;

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        if (card is FullArmorThunderLance
            && card.Owner?.Creature == Owner
            && (oldPileType == MonsterFieldPile.FieldPileType
                || card.Pile?.Type == MonsterFieldPile.FieldPileType))
        {
            InvokeDisplayAmountChanged();
        }

        return Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player.Creature != Owner)
            return;

        var sources = MonsterFieldService.GetMonsters(player)
            .OfType<FullArmorThunderLance>()
            .ToList();
        if (sources.Count == 0)
        {
            await PowerCmd.Remove(this);
            return;
        }

        var currentTurn = player.PlayerCombatState?.TurnNumber ?? int.MinValue;
        var block = sources
            .Where(source => source.SummonedTurn < currentTurn)
            .Sum(source => source.CurrentUpkeepBlock);
        if (block <= 0)
            return;

        Flash();
        await ThermalVortexCommandCompat.GainBlock(
            Owner, block, ValueProp.Unpowered, null, false);
    }
}
