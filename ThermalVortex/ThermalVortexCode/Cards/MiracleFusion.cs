using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MiracleFusion : MainDeckCard
{
    public override string CustomPortraitPath => "miracle_fusion.png".BigCardImagePath();
    public override string PortraitPath => "miracle_fusion.png".CardImagePath();
    public override string BetaPortraitPath => "miracle_fusion.png".CardImagePath();

    public MiracleFusion() : base(3, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(
            ExtraDeckExplanation(),
            KeywordExplanation("THERMALVORTEX-FUSION_MATERIAL"),
            KeywordExplanation("THERMALVORTEX-XYZ"),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            NativeKeywordExplanation(CardKeyword.Exhaust));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && TryGetCore()?.CanMiracleFusionSummon == true;

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var core = TryGetCore();
        if (core is not null)
            await core.MiracleFusionSummon(ctx);
    }

    private ThermalVortexCore TryGetCore()
    {
        try
        {
            return Owner?.GetRelic<ThermalVortexCore>();
        }
        catch
        {
            return null;
        }
    }
}
