using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberBeacon : MainDeckCard
{
    public override string CustomPortraitPath => "cyber_beacon.png".BigCardImagePath();
    public override string PortraitPath => "cyber_beacon.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_beacon.png".CardImagePath();

    public CyberBeacon() : base(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(
            CardPreviewExplanation<CyberDragon>(),
            CardPreviewExplanation<CyberLarva>());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<CyberBeaconPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        Owner.Creature.GetPower<CyberBeaconPower>()?.BindUpgrade(CurrentUpgradeLevel > 0);
        await CyberSeries.GenerateCyberLarvaToDiscard(Owner, this);
    }
}
