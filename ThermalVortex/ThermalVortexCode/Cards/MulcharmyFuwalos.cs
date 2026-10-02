using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MulcharmyFuwalos : MonsterCard, ICyberCopyableEffectProvider, IMonsterFieldEnterResolvedListener
{
    public const int BaseMaxHp = 4;

    public override string CustomPortraitPath => "mulcharmy_fuwalos.png".BigCardImagePath();
    public override string PortraitPath => "mulcharmy_fuwalos.png".CardImagePath();
    public override string BetaPortraitPath => "mulcharmy_fuwalos.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public MulcharmyFuwalos() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
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
        yield return new EnemyActionDrawInheritedEffect(EnemyActionDrawEffectKind.Attack);
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        await CommonActions.ApplySelf<MulcharmyFuwalosPower>(ctx, this, 1, false);
        Owner.Creature.GetPower<MulcharmyFuwalosPower>()?.BindSource(this);
    }
}
