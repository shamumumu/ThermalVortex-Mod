using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class ElderEntityNtss : MonsterCard, ICyberCopyableEffectProvider, IMonsterFieldLeaveListener
{
    public const int BaseMaxHp = 4;
    private const int BaseTriggerDamage = 10;
    private const int UpgradedTriggerDamage = 15;
    public override string CustomPortraitPath => "elder_entity_ntss.png".BigCardImagePath();
    public override string PortraitPath => "elder_entity_ntss.png".CardImagePath();
    public override string BetaPortraitPath => "elder_entity_ntss.png".CardImagePath();
    public override int MonsterMaxHp => BaseMaxHp;

    public ElderEntityNtss() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithUpgradeVar("Damage", BaseTriggerDamage, UpgradedTriggerDamage);
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
        yield return new CyberMaterialDamageEffect(UpgradeVarValue("Damage"));
    }

    public async Task AfterMonsterLeftField(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        if (!ReferenceEquals(leaveEvent.Card, this) || !leaveEvent.WasUsedAsMaterial)
            return;

        var target = await EffectTargeting.ChooseEnemy(ctx, Owner);
        if (target is null)
            return;

        var damage = UpgradeVarValue("Damage");
        await ThermalVortexCombatVfx.EffectDamageAsync(
            ctx,
            target,
            damage,
            ValueProp.Unpowered,
            Owner.Creature,
            this);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
