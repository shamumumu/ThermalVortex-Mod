using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MacroCosmos : MainDeckCard
{
    public override string CustomPortraitPath => "macro_cosmos.png".BigCardImagePath();
    public override string PortraitPath => "macro_cosmos.png".CardImagePath();
    public override string BetaPortraitPath => "macro_cosmos.png".CardImagePath();

    public MacroCosmos() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(NativeKeywordExplanation(CardKeyword.Exhaust));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CommonActions.ApplySelf<MacroCosmosPower>(ctx, this, 1, false);
    }

}
