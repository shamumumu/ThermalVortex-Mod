using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class FusionGatePower : ThermalVortexPower
{
    private int _lifeLoss = 3;
    internal int LifeLoss => _lifeLoss;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    internal void SetLifeLoss(int amount)
    {
        _lifeLoss = Math.Max(0, amount);
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        Flash();
        await LoseHp(_lifeLoss);
        if (Owner.CurrentHp <= 0)
            return;

        var summoned = false;
        var core = TryGetCore(player);
        if (core?.CanFusionGateSummon == true)
            summoned = await core.FusionGateSummon(ctx);

        if (!summoned && Owner.CurrentHp > 0)
            await LoseHp(_lifeLoss);
    }

    private async Task LoseHp(int amount)
    {
        await CreatureCmd.SetCurrentHp(Owner, Math.Max(0, Owner.CurrentHp - amount));
    }

    private static ThermalVortexCore TryGetCore(Player player)
    {
        try
        {
            return player?.GetRelic<ThermalVortexCore>();
        }
        catch
        {
            return null;
        }
    }
}
