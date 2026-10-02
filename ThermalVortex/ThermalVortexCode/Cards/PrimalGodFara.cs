using BaseLib.Utils;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class PrimalGodFara : MonsterCard, ICyberCopyableEffectProvider, IMonsterFieldAttackTargetRule, IMonsterFieldLeaveResolvedListener
{
    private const int BaseMaxHp = 1;

    public override string CustomPortraitPath => "primal_god_fara.png".BigCardImagePath();
    public override string PortraitPath => "primal_god_fara.png".CardImagePath();
    public override string BetaPortraitPath => "primal_god_fara.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;
    public bool CanBeAttackTarget => false;

    public PrimalGodFara() : base(3, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
        WithKeyword(CardKeyword.Innate, UpgradeType.Add);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-MATERIAL"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
    }

    public IEnumerable<ICyberCopyableEffect> CreateCyberCopyableEffects()
    {
        yield return new CyberFaraPersistenceEffect();
    }

    public async Task AfterMonsterLeftFieldResolved(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        if (!ReferenceEquals(leaveEvent.Card, this)
            || !leaveEvent.WasUsedAsMaterial)
        {
            return;
        }

        await MonsterFieldService.SpecialSummon(ctx, this, leaveEvent.Source ?? this);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
