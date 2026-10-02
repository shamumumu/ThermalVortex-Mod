using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MillenniumTemple : MainDeckCard, IMillenniumCard
{
    public const int BaseBlockPerCount = 2;
    public const int UpgradedBlockPerCount = 3;
    public int CurrentBlockPerCount => UpgradeVarValue("BlockPerCount");
    public override string CustomPortraitPath => "millennium_temple.png".BigCardImagePath();
    public override string PortraitPath => "millennium_temple.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_temple.png".CardImagePath();

    public MillenniumTemple() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
        WithUpgradeVar("BlockPerCount", BaseBlockPerCount, UpgradedBlockPerCount);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_CARD"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_POWER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_COUNT"),
            BlockExplanation());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<MillenniumTemplePower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        var power = Owner.Creature.GetPower<MillenniumTemplePower>();
        if (power is not null)
            power.BlockPerCount = Math.Max(power.BlockPerCount, CurrentBlockPerCount);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
