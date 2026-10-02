using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class LimiterRemoval : MainDeckCard
{
    public override string CustomPortraitPath => "limiter_removal.png".BigCardImagePath();
    public override string PortraitPath => "limiter_removal.png".CardImagePath();
    public override string BetaPortraitPath => "limiter_removal.png".CardImagePath();

    public LimiterRemoval() : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<LimiterRemovalPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }

}
