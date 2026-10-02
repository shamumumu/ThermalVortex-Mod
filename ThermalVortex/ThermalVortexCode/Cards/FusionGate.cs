using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class FusionGate : MainDeckCard
{
    public override string CustomPortraitPath => "fusion_gate.png".BigCardImagePath();
    public override string PortraitPath => "fusion_gate.png".CardImagePath();
    public override string BetaPortraitPath => "fusion_gate.png".CardImagePath();

    public FusionGate() : base(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
        WithUpgradeVar("LifeLoss", 3, 2);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-FUSION_MATERIAL"),
            KeywordExplanation("THERMALVORTEX-XYZ"),
            NativeKeywordExplanation(CardKeyword.Exhaust));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CommonActions.ApplySelf<FusionGatePower>(ctx, this, 1, false);
        Owner.Creature.GetPower<FusionGatePower>()?.SetLifeLoss(UpgradeVarValue("LifeLoss"));
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
