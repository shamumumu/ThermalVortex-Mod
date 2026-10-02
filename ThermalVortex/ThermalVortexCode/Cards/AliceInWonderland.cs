using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class AliceInWonderland : MainDeckCard
{
    public override string CustomPortraitPath => "alice_in_wonderland.png".BigCardImagePath();
    public override string PortraitPath => "alice_in_wonderland.png".CardImagePath();
    public override string BetaPortraitPath => "alice_in_wonderland.png".CardImagePath();

    public AliceInWonderland() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithKeyword(CardKeyword.Ethereal, UpgradeType.None);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CommonActions.ApplySelf<AliceInWonderlandPower>(ctx, this, 1, false);
    }

}
