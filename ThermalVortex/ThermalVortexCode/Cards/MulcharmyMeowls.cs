using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MulcharmyMeowls : MonsterCard, ICyberCopyableEffectProvider, IMonsterFieldEnterResolvedListener
{
    public const int BaseMaxHp = 7;

    public override string CustomPortraitPath => "mulcharmy_meowls.png".BigCardImagePath();
    public override string PortraitPath => "mulcharmy_meowls.png".CardImagePath();
    public override string BetaPortraitPath => "mulcharmy_meowls.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public MulcharmyMeowls() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
    }

    public IEnumerable<ICyberCopyableEffect> CreateCyberCopyableEffects()
    {
        yield return new EnemyActionDrawInheritedEffect(EnemyActionDrawEffectKind.Summon);
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        await CommonActions.ApplySelf<MulcharmyMeowlsPower>(ctx, this, 1, false);
        Owner.Creature.GetPower<MulcharmyMeowlsPower>()?.BindSource(this);
    }
}
