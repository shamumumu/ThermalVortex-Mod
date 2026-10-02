using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class FurnaceStartup : MainDeckCard
{
    public override string CustomPortraitPath => "furnace_startup.png".BigCardImagePath();
    public override string PortraitPath => "furnace_startup.png".CardImagePath();
    public override string BetaPortraitPath => "furnace_startup.png".CardImagePath();

    public FurnaceStartup() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CommonActions.ApplySelf<FurnaceStartupPower>(ctx, this, 1, false);
    }

}
