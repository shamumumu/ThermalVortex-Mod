using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberloadFusion : MainDeckCard
{
    public override string CustomPortraitPath => "cyberload_fusion.png".BigCardImagePath();
    public override string PortraitPath => "cyberload_fusion.png".CardImagePath();
    public override string BetaPortraitPath => "cyberload_fusion.png".CardImagePath();

    public CyberloadFusion() : base(3, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            ExtraDeckExplanation(),
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-FUSION_MATERIAL"),
            KeywordExplanation("THERMALVORTEX-XYZ"),
            KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && TryGetCore()?.CanLoadFusionSummon == true;

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var core = TryGetCore();
        if (core is not null)
            await core.LoadFusionSummon(ctx);
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
