using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ForbiddenChalice : MainDeckCard
{
    public override string CustomPortraitPath => "forbidden_chalice.png".BigCardImagePath();
    public override string PortraitPath => "forbidden_chalice.png".CardImagePath();
    public override string BetaPortraitPath => "forbidden_chalice.png".CardImagePath();

    public ForbiddenChalice() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.Remove);
        WithExplanations(PowerExplanation<StrengthPower>());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (play.Target is not null && !play.Target.IsDead)
        {
            await ThermalVortexCommandCompat.ApplyPower<ForbiddenChalicePower>(ctx, play.Target, 1, Owner.Creature, this, false);
        }
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
