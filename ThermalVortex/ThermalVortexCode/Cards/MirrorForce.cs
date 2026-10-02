using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MirrorForce : MainDeckCard
{
    public override string CustomPortraitPath => "mirror_force.png".BigCardImagePath();
    public override string PortraitPath => "mirror_force.png".CardImagePath();
    public override string BetaPortraitPath => "mirror_force.png".CardImagePath();

    public MirrorForce() : base(3, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithKeyword(CardKeyword.Retain, UpgradeType.None);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(KeywordExplanation("THERMALVORTEX-REFLECT"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<MirrorForcePower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }

}
