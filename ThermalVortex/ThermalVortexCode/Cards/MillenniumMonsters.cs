using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MillenniumShield : MonsterCard, IMillenniumCard, IMillenniumMonster
{
    public const int BaseMaxHp = 15;
    public const int UpgradedMaxHp = 20;
    public override string CustomPortraitPath => "millennium_shield.png".BigCardImagePath();
    public override string PortraitPath => "millennium_shield.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_shield.png".CardImagePath();
    public override int MonsterMaxHp => CurrentUpgradeLevel > 0 ? UpgradedMaxHp : BaseMaxHp;

    public MillenniumShield() : base(2, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithMonsterHpUpgrade(BaseMaxHp, UpgradedMaxHp);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class MillenniumTreasureGolem : MonsterCard, IMillenniumCard, IMillenniumMonster, ICyberCopyableEffectProvider, IMonsterFieldEnterResolvedListener
{
    public const int BaseMaxHp = 5;
    public int CurrentWeak => UpgradeVarValue("Weak");
    public override string CustomPortraitPath => "millennium_treasure_golem.png".BigCardImagePath();
    public override string PortraitPath => "millennium_treasure_golem.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_treasure_golem.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public MillenniumTreasureGolem() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithUpgradeVar("Weak", 1, 2);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-WEAK"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
    }

    public IEnumerable<ICyberCopyableEffect> CreateCyberCopyableEffects()
    {
        yield return new CyberUpkeepInheritedEffect(CyberUpkeepEffectKind.WeakAll, CurrentWeak);
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        await ThermalVortexCommandCompat.ApplyPower<MillenniumTreasureGolemPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class AwakenedMillenniumPrimitive : MonsterCard, IMillenniumCard, IMillenniumMonster, ICyberCopyableEffectProvider, IMonsterFieldEnterResolvedListener
{
    public const int BaseMaxHp = 5;
    public int CurrentVulnerable => UpgradeVarValue("Vulnerable");
    public override string CustomPortraitPath => "awakened_millennium_primitive.png".BigCardImagePath();
    public override string PortraitPath => "awakened_millennium_primitive.png".CardImagePath();
    public override string BetaPortraitPath => "awakened_millennium_primitive.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public AwakenedMillenniumPrimitive() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithUpgradeVar("Vulnerable", 1, 2);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            PowerExplanation<VulnerablePower>());
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
    }

    public IEnumerable<ICyberCopyableEffect> CreateCyberCopyableEffects()
    {
        yield return new CyberUpkeepInheritedEffect(CyberUpkeepEffectKind.VulnerableAll, CurrentVulnerable);
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        await ThermalVortexCommandCompat.ApplyPower<AwakenedMillenniumPrimitivePower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class MillenniumGravekeeper : MonsterCard, IMillenniumCard, IMillenniumMonster
{
    public const int BaseMaxHp = 4;
    public const int UpgradedMaxHp = 6;
    public override string CustomPortraitPath => "millennium_gravekeeper.png".BigCardImagePath();
    public override string PortraitPath => "millennium_gravekeeper.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_gravekeeper.png".CardImagePath();
    public override int MonsterMaxHp => CurrentUpgradeLevel > 0 ? UpgradedMaxHp : BaseMaxHp;

    public MillenniumGravekeeper() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
        WithBlock(4, 2);
        WithMonsterHpUpgrade(BaseMaxHp, UpgradedMaxHp);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
        await CommonActions.CardBlock(this, play);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class MillenniumCarrier : MonsterCard, IMillenniumCard, IMillenniumMonster
{
    public const int BaseMaxHp = 2;
    public const int UpgradedMaxHp = 3;
    public override string CustomPortraitPath => "millennium_carrier.png".BigCardImagePath();
    public override string PortraitPath => "millennium_carrier.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_carrier.png".CardImagePath();
    public override int MonsterMaxHp => CurrentUpgradeLevel > 0 ? UpgradedMaxHp : BaseMaxHp;

    public MillenniumCarrier() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
        WithDamage(6, 4);
        WithMonsterHpUpgrade(BaseMaxHp, UpgradedMaxHp);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
        await ThermalVortexCombatVfx.CardAttack(this, play, 1).Execute(ctx);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class MillenniumSleepingTablet : MonsterCard, IMillenniumCard, IMillenniumMonster, IChaosPhantomCopyableEffectProvider, IMonsterFieldEnterResolvedListener
{
    public const int BaseMaxHp = 1;
    public override string CustomPortraitPath => "millennium_sleeping_tablet.png".BigCardImagePath();
    public override string PortraitPath => "millennium_sleeping_tablet.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_sleeping_tablet.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public MillenniumSleepingTablet() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-SEALED_MILLENNIUM_PIECE"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ResolveMonsterSummon(play);
    }

    public IEnumerable<ICyberCopyableEffect> CreateChaosPhantomCopyableEffects()
    {
        yield return new ChaosPhantomSleepingTabletTransitionEffect(CurrentUpgradeLevel > 0);
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        if (!MonsterFieldService.IsOnField(enterEvent.Card))
            return;

        await ThermalVortexCommandCompat.ApplyPower<MillenniumSleepingTabletPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
        Owner.Creature.GetPower<MillenniumSleepingTabletPower>()?.Bind(enterEvent.Card, CurrentUpgradeLevel > 0);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class MillenniumPartner : MainDeckCard, IMillenniumCard
{
    public override string CustomPortraitPath => "millennium_partner.png".BigCardImagePath();
    public override string PortraitPath => "millennium_partner.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_partner.png".CardImagePath();

    public MillenniumPartner() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await ThermalVortexCommandCompat.ApplyPower<MillenniumPartnerPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }

}
