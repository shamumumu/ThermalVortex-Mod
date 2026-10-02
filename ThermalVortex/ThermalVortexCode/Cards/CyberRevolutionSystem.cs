using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberRevolutionSystem : MainDeckCard
{
    public override string CustomPortraitPath => "cyber_revolution_system.png".BigCardImagePath();
    public override string PortraitPath => "cyber_revolution_system.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_revolution_system.png".CardImagePath();

    public CyberRevolutionSystem() : base(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(CardPreviewExplanation<CyberLarva>());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<CyberRevolutionSystemPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        await CyberSeries.GenerateCyberLarvaToDiscard(Owner, this);
    }
}
