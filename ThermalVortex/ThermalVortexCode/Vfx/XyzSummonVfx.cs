using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class XyzSummonVfx
{
    private const double Duration = 0.78d;
    private const double TimeoutSeconds = 1.05d;

    public static async Task PlayAsync(XyzMonsterCard summon, Creature target, Func<bool> canContinue = null)
    {
        if (canContinue?.Invoke() == false)
            return;

        if (summon is null)
        {
            MainFile.Logger.Info("Xyz summon VFX skipped: summon is null.");
            return;
        }

        TcgMonsterCutinVfx.StartPreload(summon);

        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null)
        {
            MainFile.Logger.Info($"Xyz summon VFX skipped summon={summon.GetType().Name}: scene tree is unavailable.");
            return;
        }

        var viewportSize = tree.Root.GetVisibleRect().Size;
        if (viewportSize == Vector2.Zero)
        {
            MainFile.Logger.Info($"Xyz summon VFX skipped summon={summon.GetType().Name}: viewport size is zero.");
            return;
        }

        var layer = new CanvasLayer
        {
            Name = "ThermalVortexXyzSummonVfx",
            Layer = 128
        };

        var root = new Node2D
        {
            Name = "XyzSummonRoot",
            Position = viewportSize * 0.5f,
            ZIndex = 896
        };

        layer.AddChild(root);
        tree.Root.AddChild(layer);

        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;

        void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            finished.TrySetResult();

            if (GodotObject.IsInstanceValid(layer))
            {
                layer.Visible = false;
                layer.QueueFree();
            }
        }

        try
        {
            var targetOffset = ResolveTargetOffset(viewportSize, target);
            BuildRings(root);
            BuildParticles(root, targetOffset);

            var tween = layer.CreateTween();
            tween.SetParallel(true);
            tween.Finished += Complete;

            foreach (var child in root.GetChildren())
            {
                if (child is not Node2D node)
                    continue;

                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), Duration)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);

                if (node is Line2D line && line.Name.ToString().StartsWith("Ring", StringComparison.Ordinal))
                {
                    tween.TweenProperty(line, "rotation", line.Rotation + Mathf.Tau, Duration)
                        .SetTrans(Tween.TransitionType.Cubic)
                        .SetEase(Tween.EaseType.Out);
                    tween.TweenProperty(line, "scale", line.Scale * 1.28f, Duration)
                        .SetTrans(Tween.TransitionType.Cubic)
                        .SetEase(Tween.EaseType.Out);
                }
                else
                {
                    tween.TweenProperty(node, "position", targetOffset, Duration)
                        .SetTrans(Tween.TransitionType.Cubic)
                        .SetEase(Tween.EaseType.In);
                }
            }

            var timeout = layer.GetTree().CreateTimer(TimeoutSeconds);
            timeout.Timeout += Complete;
            var startedAt = Stopwatch.GetTimestamp();
            while (!finished.Task.IsCompleted)
            {
                if (canContinue?.Invoke() == false
                    || Stopwatch.GetElapsedTime(startedAt).TotalSeconds >= TimeoutSeconds + 0.25d
                    || !await FusionSummonVfx.WaitForNextFrameAsync(tree, layer))
                    break;
            }
        }
        finally
        {
            Complete();
        }
    }

    private static Vector2 ResolveTargetOffset(Vector2 viewportSize, Creature target)
    {
        var center = viewportSize * 0.5f;
        var screenTarget = target is not null && target.IsEnemy
            ? new Vector2(viewportSize.X * 0.72f, viewportSize.Y * 0.42f)
            : new Vector2(viewportSize.X * 0.30f, viewportSize.Y * 0.70f);

        return screenTarget - center;
    }

    private static void BuildRings(Node2D root)
    {
        var cyan = new Color(0.26f, 0.95f, 1f, 0.92f);
        var violet = new Color(0.74f, 0.46f, 1f, 0.72f);
        var white = new Color(1f, 1f, 1f, 0.84f);

        root.AddChild(CreateArc("RingOuter", 164f, 0f, Mathf.Tau * 0.88f, 9f, cyan));
        root.AddChild(CreateArc("RingInner", 104f, Mathf.Tau * 0.13f, Mathf.Tau * 0.83f, 6f, violet));
        root.AddChild(CreateArc("RingCore", 58f, Mathf.Tau * 0.07f, Mathf.Tau * 0.94f, 4f, white));

        for (var i = 0; i < 8; i++)
        {
            var angle = Mathf.Tau * i / 8f;
            var tick = new Line2D
            {
                Name = $"RingTick{i}",
                Points = new[]
                {
                    Unit(angle) * 118f,
                    Unit(angle) * 148f
                },
                Width = 5f,
                DefaultColor = i % 2 == 0 ? cyan : violet,
                Antialiased = true,
                ZIndex = 898
            };
            root.AddChild(tick);
        }
    }

    private static void BuildParticles(Node2D root, Vector2 targetOffset)
    {
        var colors = new[]
        {
            new Color(0.24f, 0.98f, 1f, 0.95f),
            new Color(0.95f, 0.72f, 1f, 0.88f),
            new Color(1f, 1f, 1f, 0.86f)
        };

        for (var i = 0; i < 18; i++)
        {
            var angle = Mathf.Tau * i / 18f;
            var start = Unit(angle) * (70f + (i % 4) * 18f);
            root.AddChild(CreateParticle(i, start, targetOffset, colors[i % colors.Length]));
        }
    }

    private static Line2D CreateArc(string name, float radius, float start, float sweep, float width, Color color)
    {
        return new Line2D
        {
            Name = name,
            Points = BuildArcPoints(radius, start, sweep, 64),
            Width = width,
            DefaultColor = color,
            Antialiased = true,
            ZIndex = 897
        };
    }

    private static Line2D CreateParticle(int index, Vector2 start, Vector2 targetOffset, Color color)
    {
        var direction = targetOffset - start;
        if (direction.LengthSquared() < 1f)
        {
            direction = Vector2.Right;
        }

        return new Line2D
        {
            Name = $"Particle{index}",
            Position = start,
            Points = new[]
            {
                Vector2.Zero,
                -direction.Normalized() * (16f + index % 3 * 4f)
            },
            Width = 3f + index % 3,
            DefaultColor = color,
            Antialiased = true,
            ZIndex = 899
        };
    }

    private static Vector2[] BuildArcPoints(float radius, float start, float sweep, int segments)
    {
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = start + sweep * i / segments;
            points[i] = Unit(angle) * radius;
        }

        return points;
    }

    private static Vector2 Unit(float angle)
    {
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }
}
