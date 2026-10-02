using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberDragonHerz : MonsterCard,
    ICyberMonster,
    ICyberCopyableEffectProvider,
    IChaosPhantomCopyableEffectProvider,
    IMonsterFieldEnterResolvedListener
{
    public const int BaseMaxHp = 1;

    public override string CustomPortraitPath => "cyber_dragon_herz.png".BigCardImagePath();
    public override string PortraitPath => "cyber_dragon_herz.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_dragon_herz.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public CyberDragonHerz() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            CardPreviewExplanation<CyberDragon>(),
            CardPreviewExplanation<CyberLarva>());
    }

    public IEnumerable<ICyberCopyableEffect> CreateCyberCopyableEffects()
    {
        yield return new CyberLarvaGenerationEffect(upgraded: CurrentUpgradeLevel > 0);
    }

    public IEnumerable<ICyberCopyableEffect> CreateChaosPhantomCopyableEffects()
    {
        yield return new ChaosPhantomHerzTransitionEffect(CurrentUpgradeLevel > 0);
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        await CyberSeries.GenerateCyberLarvaToDiscard(Owner, this);
        await ThermalVortexCommandCompat.ApplyPower<CyberDragonHerzPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        Owner.Creature.GetPower<CyberDragonHerzPower>()?.Bind(this);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
