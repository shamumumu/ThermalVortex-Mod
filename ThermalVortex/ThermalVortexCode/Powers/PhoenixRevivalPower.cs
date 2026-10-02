using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Powers;

// Display the existing attack-scoped revival rule without owning its resolution.
public class PhoenixRevivalPower : ThermalVortexPower, IMonsterFieldLeaveResolvedListener
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override string CustomPackedIconPath => "winged_dragon_of_ra_phoenix.png".PowerImagePath();
    public override string CustomBigIconPath => "winged_dragon_of_ra_phoenix.png".BigPowerImagePath();

    internal static async Task Synchronize(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature?.CombatState is null)
            return;

        var power = player.Creature.GetPower<PhoenixRevivalPower>();
        var phoenix = MonsterFieldService.GetMonsters(player).FirstOrDefault(card =>
            card is WingedDragonOfRaPhoenix
            || card is ChaosPhantom phantom && phantom.IsCompleteCopyOf<WingedDragonOfRaPhoenix>());
        if (phoenix is not null && power is null)
        {
            await ThermalVortexCommandCompat.ApplyPower<PhoenixRevivalPower>(
                ctx, player.Creature, 1, player.Creature, phoenix, false);
        }
        else if (phoenix is null && power is not null)
        {
            await PowerCmd.Remove(power);
        }
    }

    public override Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source) =>
        card?.Owner?.Creature == Owner
            ? Synchronize(null, Owner.Player)
            : Task.CompletedTask;

    public Task AfterMonsterLeftFieldResolved(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent) =>
        leaveEvent.Card?.Owner?.Creature == Owner
            ? Synchronize(ctx, Owner.Player)
            : Task.CompletedTask;
}
