using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class FutureFusionPower : ThermalVortexPower
{
    private List<ExtraDeckReservation> _reservedSummons = [];

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _reservedSummons = _reservedSummons.Select(reservation => reservation.DeepClone()).ToList();
    }

    internal bool Bind(ExtraDeckReservation reservation)
    {
        if (reservation is null
            || string.IsNullOrWhiteSpace(reservation.SerializedEntry)
            || !ReferenceEquals(Owner?.CombatState, reservation.Combat))
            return false;

        _reservedSummons.Add(reservation.DeepClone());
        return true;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner || _reservedSummons.Count == 0)
            return;

        var core = TryGetCore(player);
        var pending = _reservedSummons.ToList();
        _reservedSummons.Clear();
        var next = 0;
        try
        {
            if (core is not null)
            {
                Flash();
                while (next < pending.Count)
                {
                    var reserved = pending[next];
                    if (!core.IsCurrentReservation(reserved))
                        break;
                    // This item now belongs to the core's summon/rollback path.
                    // Do not return its old snapshot again after an exception.
                    next++;
                    await core.SummonReservedExtraDeck(ctx, reserved);
                }
            }
        }
        finally
        {
            if (core is not null)
                for (; next < pending.Count; next++)
                    core.RestoreReservedExtraDeck(pending[next]);
            // A summon hook can bind a new reservation on this same power.
            // Leave those new entries for the following turn.
            if (_reservedSummons.Count == 0)
                await PowerCmd.Remove(this);
        }
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        _reservedSummons.Clear();
        return Task.CompletedTask;
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
