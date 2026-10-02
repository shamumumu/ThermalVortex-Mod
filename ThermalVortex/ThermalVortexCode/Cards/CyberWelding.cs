using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberWelding : MainDeckCard
{
    public override string CustomPortraitPath => "cyber_welding.png".BigCardImagePath();
    public override string PortraitPath => "cyber_welding.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_welding.png".CardImagePath();

    public CyberWelding() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.Remove);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<CyberWeldingPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        Owner.Creature.GetPower<CyberWeldingPower>()?.ArmNextOutput();
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
