using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CyberDragonInfinity : XyzMonsterCard, ICyberMonster, ICyberDevourStats, ICyberCopyableEffectProvider, IMonsterFieldEnterResolvedListener
{
    public const int RequiredMaterials = 3;
    public const int BaseMaxHp = 1;

    public override string CustomPortraitPath => "cyber_dragon_infinity.png".BigCardImagePath();
    public override string PortraitPath => "cyber_dragon_infinity.png".CardImagePath();
    public override string BetaPortraitPath => "cyber_dragon_infinity.png".CardImagePath();
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;
    public int CyberAttackContribution => 0;

    public CyberDragonInfinity() : base(CardType.Attack, CardRarity.Rare, TargetType.RandomEnemy)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-DEVOUR"),
            KeywordExplanation("THERMALVORTEX-SANITY", card => card.CurrentUpgradeLevel > 0),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            CardPreviewExplanation<CyberDragon>(matchSourceUpgrade: false));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (!play.IsFirstInSeries
            || !IsResolvingAuthorizedExtraDeckSummon
            || Owner?.Creature?.CombatState is null)
            return;

        await TcgMonsterCutinVfx.PlaySummonAsync(this, play);
    }

    public IEnumerable<ICyberCopyableEffect> CreateCyberCopyableEffects()
    {
        yield return new CyberInfinityDevourEffect();
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        await ThermalVortexCommandCompat.ApplyPower<CyberDragonInfinityPower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
