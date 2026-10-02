using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Commands;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class Relinquished : XyzMonsterCard
{
    public const int RequiredMaterials = 1;
    private int? _absorbedMaxHp;

    public override string CustomPortraitPath => "relinquished.png".BigCardImagePath();
    public override string PortraitPath => "relinquished.png".CardImagePath();
    public override string BetaPortraitPath => "relinquished.png".CardImagePath();
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => _absorbedMaxHp ?? 1;

    public Relinquished() : base(CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
    {
        WithUpgradeVar("ControlTurns", 3, 5);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-CONTROL"),
            KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || FindMinionTarget() is not null);

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var target = play.Target ?? await EffectTargeting.ResolveForAutoPlay(ctx, Owner, this);
        if (!IsValidTarget(target))
            return;

        var targetHp = Math.Max(1, target.CurrentHp);
        var targetMaxHp = Math.Max(targetHp, target.MaxHp);
        _absorbedMaxHp = targetMaxHp;
        var effectHost = ChaosPhantomCopyService.ResolveEffectHost(this);
        await RelinquishedControlPower.ReleaseForCopyReset(effectHost);
        if (effectHost is ChaosPhantom phantom)
            phantom.SetCompleteCopyMaxHp(targetMaxHp);
        MonsterFieldHealthService.SetHealth(effectHost, targetHp, targetMaxHp);

        await ThermalVortexCommandCompat.ApplyPower<RelinquishedControlPower>(
            ctx,
            target,
            UpgradeVarValue("ControlTurns"),
            Owner.Creature,
            effectHost,
            false);
        if (target.GetPower<RelinquishedControlPower>() is { } control)
            await control.BindAsync(effectHost);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();

    private Creature FindMinionTarget() =>
        Owner?.Creature?.CombatState?.HittableEnemies?.FirstOrDefault(IsValidMinionTarget);

    private static bool IsValidMinionTarget(Creature creature) =>
        IsMinion(creature);

    public static bool IsMinion(Creature creature) =>
        creature?.IsAlive == true
        && creature.IsEnemy
        && creature.HasPower<MinionPower>();
}
