using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class FutureFusion : MainDeckCard
{
    public override string CustomPortraitPath => "future_fusion.png".BigCardImagePath();
    public override string PortraitPath => "future_fusion.png".CardImagePath();
    public override string BetaPortraitPath => "future_fusion.png".CardImagePath();

    public FutureFusion() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithUpgradeVar("MaterialLimit", 2, 3);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MATERIAL"),
            KeywordExplanation("THERMALVORTEX-XYZ"),
            ExtraDeckExplanation(),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            NativeKeywordExplanation(CardKeyword.Exhaust));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || TryGetCore()?.CanPrepareFutureFusion(MaxMaterials) == true);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var core = TryGetCore();
        if (core is not null)
            await core.PrepareFutureFusion(ctx, MaxMaterials);
    }

    private int MaxMaterials => UpgradeVarValue("MaterialLimit");

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

    protected override void OnUpgrade() => ConstructedUpgrade();
}
