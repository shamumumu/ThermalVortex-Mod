using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class SolemnStrike : SolemnCard
{
    public override string CustomPortraitPath => "solemn_strike.png".BigCardImagePath();
    public override string PortraitPath => "solemn_strike.png".CardImagePath();
    public override string BetaPortraitPath => "solemn_strike.png".CardImagePath();

    public SolemnStrike() : base(1, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithKeyword(CardKeyword.Retain, UpgradeType.Add);
        WithExplanations(KeywordExplanation("THERMALVORTEX-NEGATE"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (!await PayLife(GetLifePayment()))
            return;

        if (play.Target is not null)
            await ThermalVortexCommandCompat.ApplyPower<SolemnStrikePower>(ctx, play.Target, 1, Owner.Creature, this, false);
    }

    protected override decimal GetLifePayment() => 4;
}
