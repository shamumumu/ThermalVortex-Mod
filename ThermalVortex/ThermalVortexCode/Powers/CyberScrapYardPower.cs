using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class CyberScrapYardPower : ThermalVortexPower
{
    private bool _generateUpgradedDragon;
    internal bool GenerateUpgradedDragon => _generateUpgradedDragon;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    internal void BindUpgrade(bool upgraded) =>
        _generateUpgradedDragon |= upgraded;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player?.Creature != Owner)
            return;

        Flash();
        await CyberSeries.GenerateCyberDragon(
            player,
            PileType.Discard,
            this,
            CardPilePosition.Top,
            forceUpgraded: _generateUpgradedDragon);
    }
}
