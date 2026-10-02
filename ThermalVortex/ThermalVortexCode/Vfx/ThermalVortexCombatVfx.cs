using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class ThermalVortexCombatVfx
{
    internal const string DefaultHitVfx = "vfx/vfx_attack_slash";

    internal static AttackCommand CardAttack(CardModel card, CardPlay play, int hitCount) =>
        ConfigureAttack(CommonActions.CardAttack(card, play, hitCount, DefaultHitVfx, null, null), card);

    // Explicit creature targets must stay single-targeted. BaseLib's Creature
    // overloads instead dispatch on card.TargetType and can silently repeat AoE.
    internal static AttackCommand CardAttack(
        CardModel card,
        Creature target,
        decimal damage,
        int hitCount) =>
        ConfigureExplicitAttack(DamageCmd.Attack(damage), card, hitCount).Targeting(target);

    internal static AttackCommand CardAttack(
        CardModel card,
        Creature target,
        CalculatedDamageVar damage,
        int hitCount) =>
        ConfigureExplicitAttack(CreateCalculatedAttack(damage), card, hitCount).Targeting(target);

    // One command owns all targets and all hits; never call this per enemy.
    internal static AttackCommand CardAttackAllEnemies(CardModel card, decimal damage, int hitCount) =>
        ConfigureExplicitAttack(DamageCmd.Attack(damage), card, hitCount)
            .TargetingAllOpponents(card.Owner.Creature.CombatState)
            .SpawningHitVfxOnEachCreature();

    internal static AttackCommand CardAttackAllEnemies(CardModel card, CalculatedDamageVar damage, int hitCount) =>
        ConfigureExplicitAttack(CreateCalculatedAttack(damage), card, hitCount)
            .TargetingAllOpponents(card.Owner.Creature.CombatState)
            .SpawningHitVfxOnEachCreature();

    private static AttackCommand CreateCalculatedAttack(CalculatedDamageVar damage)
    {
        // The native constructor keeps the formula, but does not copy its props.
        var attack = DamageCmd.Attack(damage);
        return (damage.Props & ValueProp.Unpowered) != 0 ? attack.Unpowered() : attack;
    }

    private static AttackCommand ConfigureExplicitAttack(AttackCommand attack, CardModel card, int hitCount) =>
        attack.WithHitCount(hitCount).FromCard(card).WithHitFx(DefaultHitVfx, null, null);

    internal static Task<IEnumerable<DamageResult>> EffectDamageAsync(
        PlayerChoiceContext ctx,
        Creature target,
        decimal damage,
        ValueProp props,
        Creature source,
        CardModel card)
    {
        PlayImpact(target);
        return CreatureCmd.Damage(ctx, target, damage, props, source, card);
    }

    internal static void PlayImpact(Creature target)
    {
        if (target is null || target.IsDead)
            return;

        if (!CanPlay(DefaultHitVfx))
            return;

        try
        {
            VfxCmd.PlayOnCreatureCenter(target, DefaultHitVfx);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"{DefaultHitVfx} VFX failed target={target} error={ex}");
        }
    }

    internal static async Task PlaySafeAsync(string name, Func<Task> play)
    {
        if (play is null || !CanPlay(name))
            return;

        try
        {
            await play();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"{name} VFX failed: {ex}");
        }
    }

    internal static void PlaySafe(string name, Action play)
    {
        if (play is null || !CanPlay(name))
            return;

        try
        {
            play();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"{name} VFX failed: {ex}");
        }
    }

    private static bool CanPlay(string name)
    {
        try
        {
            if (Engine.GetMainLoop() is SceneTree tree && tree.Root is not null)
            {
                if (tree.Root.GetVisibleRect().Size != Vector2.Zero)
                    return true;

                MainFile.Logger.Info($"{name} VFX skipped: viewport size is zero.");
                return false;
            }

            MainFile.Logger.Info($"{name} VFX skipped: scene tree is unavailable.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"{name} VFX availability check failed: {ex}");
        }

        return false;
    }

    private static AttackCommand ConfigureAttack(AttackCommand attack, CardModel card) =>
        card?.TargetType == TargetType.AllEnemies
            ? attack.SpawningHitVfxOnEachCreature()
            : attack;
}
