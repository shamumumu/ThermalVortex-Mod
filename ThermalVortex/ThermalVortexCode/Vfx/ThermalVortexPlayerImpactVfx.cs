using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class ThermalVortexPlayerImpactVfx
{
    private const double ShakeStepSeconds = 0.045d;
    private const double FlashInSeconds = 0.04d;
    private const double FlashOutSeconds = 0.14d;
    private static readonly ConditionalWeakTable<Sprite2D, ImpactState> ImpactStates = new();

    internal static void PlayCharacterImpact(Creature target)
    {
        try
        {
            if (YugiNativeActionController.TryPlayHurt(target))
                return;

            var sprite = ResolveYugiSprite(target);
            if (sprite is null)
                return;

            var state = ImpactStates.GetValue(
                sprite,
                static key => new ImpactState(key.Position, key.Modulate));
            var generation = ++state.Generation;

            KillTween(state.ShakeTween);
            KillTween(state.FlashTween);
            sprite.Position = state.RestPosition;
            sprite.Modulate = state.RestModulate;

            var shakeTween = sprite.CreateTween();
            state.ShakeTween = shakeTween;
            shakeTween.TweenProperty(
                    sprite,
                    "position",
                    state.RestPosition + new Vector2(-18f, 1f),
                    ShakeStepSeconds)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            shakeTween.TweenProperty(
                    sprite,
                    "position",
                    state.RestPosition + new Vector2(14f, -1f),
                    ShakeStepSeconds)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.InOut);
            shakeTween.TweenProperty(
                    sprite,
                    "position",
                    state.RestPosition + new Vector2(-7f, 0f),
                    ShakeStepSeconds)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.InOut);
            shakeTween.TweenProperty(sprite, "position", state.RestPosition, ShakeStepSeconds)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            shakeTween.Finished += () => CompleteShake(sprite, state, generation);

            var flashTween = sprite.CreateTween();
            state.FlashTween = flashTween;
            flashTween.TweenProperty(
                    sprite,
                    "modulate",
                    new Color(1f, 0.42f, 0.28f, state.RestModulate.A),
                    FlashInSeconds)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
            flashTween.TweenProperty(sprite, "modulate", state.RestModulate, FlashOutSeconds)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.In);
            flashTween.Finished += () => CompleteFlash(sprite, state, generation);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"ThermalVortex player impact VFX failed: {ex}");
        }
    }

    internal static void PlayBlockedImpact(Creature target)
    {
        try
        {
            if (target?.IsPlayer != true || CombatManager.Instance?.IsInProgress != true)
                return;

            SfxCmd.Play("event:/sfx/block_hit", 1f);

            var creatureNode = target.GetCreatureNode();
            var container = target.GetVfxContainer();
            if (!IsLiveNode(creatureNode) || !IsLiveNode(container))
                return;

            container.AddChildSafely(NBlockSparkVfx.Create(target));
            container.AddChildSafely(NDamageBlockedVfx.Create(target));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"ThermalVortex block impact VFX failed: {ex}");
        }
    }

    private static Sprite2D ResolveYugiSprite(Creature target)
    {
        var creatureNode = target?.GetCreatureNode();
        if (!IsLiveNode(creatureNode))
            return null;

        var sprite = creatureNode.Visuals?.FindChild("YugiSprite", true, false) as Sprite2D;
        return IsLiveNode(sprite) ? sprite : null;
    }

    private static bool IsLiveNode(Node node) =>
        node is not null
        && GodotObject.IsInstanceValid(node)
        && node.IsInsideTree();

    private static void KillTween(Tween tween)
    {
        if (tween is not null && GodotObject.IsInstanceValid(tween))
            tween.Kill();
    }

    private static void CompleteShake(Sprite2D sprite, ImpactState state, int generation)
    {
        if (state.Generation != generation)
            return;

        state.ShakeTween = null;
        if (IsLiveNode(sprite))
            sprite.Position = state.RestPosition;
    }

    private static void CompleteFlash(Sprite2D sprite, ImpactState state, int generation)
    {
        if (state.Generation != generation)
            return;

        state.FlashTween = null;
        if (IsLiveNode(sprite))
            sprite.Modulate = state.RestModulate;
    }

    private sealed class ImpactState(Vector2 restPosition, Color restModulate)
    {
        internal Vector2 RestPosition { get; } = restPosition;
        internal Color RestModulate { get; } = restModulate;
        internal Tween ShakeTween { get; set; }
        internal Tween FlashTween { get; set; }
        internal int Generation { get; set; }
    }
}
