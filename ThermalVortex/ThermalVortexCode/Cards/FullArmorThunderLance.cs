using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class FullArmorThunderLance : XyzMonsterCard, ICyberCopyableEffectProvider, IMonsterFieldEnterResolvedListener
{
    public const int RequiredMaterials = 3;
    public const int BaseMaxHp = 8;
    public const int SummonBlock = 8;
    public const int UpkeepBlock = 6;
    public const int UpgradedUpkeepBlock = 8;
    public override string CustomPortraitPath => "full_armor_thunder_lance.png".BigCardImagePath();
    public override string PortraitPath => "full_armor_thunder_lance.png".CardImagePath();
    public override string BetaPortraitPath => "full_armor_thunder_lance.png".CardImagePath();
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;
    public int CurrentUpkeepBlock => UpgradeVarValue("UpkeepBlock");
    internal int SummonedTurn { get; private set; } = int.MinValue;

    public FullArmorThunderLance() : base(CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
        WithBlock(SummonBlock, 4);
        WithUpgradeVar("UpkeepBlock", UpkeepBlock, UpgradedUpkeepBlock);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        await CommonActions.CardBlock(this, play);
    }

    public IEnumerable<ICyberCopyableEffect> CreateCyberCopyableEffects()
    {
        yield return new CyberUpkeepInheritedEffect(CyberUpkeepEffectKind.Block, CurrentUpkeepBlock);
    }

    public async Task AfterMonsterEnteredFieldResolved(
        PlayerChoiceContext ctx,
        MonsterFieldEnterEvent enterEvent)
    {
        SummonedTurn = Owner.PlayerCombatState?.TurnNumber ?? int.MinValue;
        await ThermalVortexCommandCompat.ApplyPower<FullArmorThunderLancePower>(ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
