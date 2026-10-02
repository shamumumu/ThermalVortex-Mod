using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberLarva : MonsterCard, ICyberMonster
{
    public const int BaseMaxHp = 1;

    public override string CustomPortraitPath => "cyber_larva.png".BigCardImagePath();
    public override string PortraitPath => "cyber_larva.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_larva.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public CyberLarva() : base(1, CardType.Status, CardRarity.Status, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.Remove);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play) =>
        await ResolveMonsterSummon(play);

    protected override void OnUpgrade() => ConstructedUpgrade();
}
