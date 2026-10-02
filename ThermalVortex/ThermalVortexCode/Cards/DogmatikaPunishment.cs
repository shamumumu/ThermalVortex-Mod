using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class DogmatikaPunishment : MainDeckCard
{
    public override string CustomPortraitPath => "dogmatika_punishment.png".BigCardImagePath();
    public override string PortraitPath => "dogmatika_punishment.png".CardImagePath();
    public override string BetaPortraitPath => "dogmatika_punishment.png".CardImagePath();

    public DogmatikaPunishment() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
        WithCostUpgradeBy(-1);
        WithExplanations(
            ExtraDeckExplanation(),
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            NativeKeywordExplanation(CardKeyword.Exhaust));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || HasValidTargetAndCost());

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var core = TryGetCore();
        if (core is null)
            return;

        var target = play.Target ?? await EffectTargeting.ResolveForAutoPlay(ctx, Owner, this);
        if (!IsValidTarget(target))
            return;

        var targetHp = Math.Max(0, target.CurrentHp);
        var combat = Owner.Creature.CombatState;
        var hp = await core.ChooseAndConsumeExtraDeckMonsterAboveHp(
            ctx,
            targetHp,
            selectedHp => target.IsAlive && target.IsHittable
                && ReferenceEquals(target.CombatState, combat)
                && ReferenceEquals(Owner.Creature.CombatState, combat)
                && selectedHp > Math.Max(0, target.CurrentHp));
        if (hp > 0 && target.IsAlive && ReferenceEquals(target.CombatState, combat))
        {
            ThermalVortexCombatVfx.PlayImpact(target);
            await CreatureCmd.Kill(target, false);
        }
    }

    internal bool HasLegalTargetAndCost(Creature target) =>
        target?.IsAlive == true
        && target.IsEnemy
        && target.IsHittable
        && ReferenceEquals(target.CombatState, Owner?.Creature?.CombatState)
        && TryGetCore()?.HasExtraDeckMonsterAboveHp(Math.Max(0, target.CurrentHp)) == true;

    private bool HasValidTargetAndCost()
    {
        var core = TryGetCore();
        var enemies = Owner?.Creature?.CombatState?.HittableEnemies;
        return core is not null
            && enemies is not null
            && enemies.Any(HasLegalTargetAndCost);
    }

    private ThermalVortexCore TryGetCore()
    {
        try
        {
            return Owner?.GetRelic<ThermalVortexCore>();
        }
        catch
        {
            return null;
        }
    }

}
