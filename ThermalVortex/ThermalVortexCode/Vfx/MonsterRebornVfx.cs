using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class MonsterRebornVfx
{
    private const double Duration = 1.12d;
    private const double RebornCueSeconds = 0.56d;
    private const double TimeoutSeconds = 1.42d;

    public static async Task PlayAsync(Creature self)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null)
        {
            return;
        }

        var viewportSize = tree.Root.GetVisibleRect().Size;
        if (viewportSize == Vector2.Zero)
        {
            return;
        }

        var layer = new CanvasLayer
        {
            Name = "ThermalVortexMonsterRebornVfx",
            Layer = 133
        };

        var overlay = new ColorRect
        {
            Name = "MonsterRebornOverlay",
            Color = new Color(0.01f, 0.03f, 0.035f, 0f),
            Size = viewportSize,
            ZIndex = 890
        };

        var flash = new ColorRect
        {
            Name = "MonsterRebornFlash",
            Color = new Color(0.58f, 1f, 0.74f, 0f),
            Size = viewportSize,
            ZIndex = 914
        };

        var root = new Node2D
        {
            Name = "MonsterRebornRoot",
            Position = viewportSize * 0.5f + new Vector2(0f, -16f),
            ZIndex = 896
        };

        layer.AddChild(overlay);
        layer.AddChild(root);
        layer.AddChild(flash);
        tree.Root.AddChild(layer);

        BuildGroundCircle(root);
        BuildSpirit(root);
        BuildRebornCross(root);
        BuildParticles(root);
        BuildReleaseWaves(root, viewportSize);

        var rebornCue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cueSent = false;
        var completed = false;

        void SignalRebornCue()
        {
            if (cueSent)
            {
                return;
            }

            cueSent = true;
            rebornCue.TrySetResult();
        }

        void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            SignalRebornCue();

            if (GodotObject.IsInstanceValid(layer))
            {
                layer.QueueFree();
            }
        }

        var tween = layer.CreateTween();
        tween.SetParallel(true);
        tween.Finished += Complete;

        tween.TweenProperty(overlay, "color", new Color(0.01f, 0.03f, 0.035f, 0.36f), 0.16d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(overlay, "color", new Color(0.01f, 0.03f, 0.035f, 0f), 0.24d)
            .SetDelay(0.92d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        tween.TweenProperty(flash, "color", new Color(0.58f, 1f, 0.74f, 0.26f), 0.07d)
            .SetDelay(0.52d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(flash, "color", new Color(0.58f, 1f, 0.74f, 0f), 0.18d)
            .SetDelay(0.60d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        AnimateChildren(tween, root);

        var cueTimer = layer.GetTree().CreateTimer(RebornCueSeconds);
        cueTimer.Timeout += SignalRebornCue;

        var timeout = layer.GetTree().CreateTimer(TimeoutSeconds);
        timeout.Timeout += Complete;

        await rebornCue.Task;
    }

    private static void AnimateChildren(Tween tween, Node2D root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is not Node2D node)
            {
                continue;
            }

            var name = node.Name.ToString();
            if (name.StartsWith("GroundCircle", StringComparison.Ordinal))
            {
                AnimateGroundCircle(tween, node, ExtractIndex(name));
            }
            else if (name.StartsWith("RebornCross", StringComparison.Ordinal))
            {
                AnimateCross(tween, node);
            }
            else if (name.StartsWith("CrossHalo", StringComparison.Ordinal))
            {
                AnimateCrossHalo(tween, node, ExtractIndex(name));
            }
            else if (name.StartsWith("Spirit", StringComparison.Ordinal))
            {
                AnimateSpirit(tween, node);
            }
            else if (name.StartsWith("Particle", StringComparison.Ordinal))
            {
                AnimateParticle(tween, node, ExtractIndex(name));
            }
            else if (name.StartsWith("ReleaseWave", StringComparison.Ordinal))
            {
                AnimateReleaseWave(tween, node, ExtractIndex(name));
            }
            else
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.22d)
                    .SetDelay(0.92d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
            }
        }
    }

    private static void AnimateGroundCircle(Tween tween, Node2D node, int index)
    {
        var delay = 0.10d + index * 0.04d;

        tween.TweenProperty(node, "scale", Vector2.One, 0.22d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0.90f - index * 0.12f), 0.10d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "scale", Vector2.One * (1.20f + index * 0.08f), 0.54d)
            .SetDelay(delay + 0.20d)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.26d)
            .SetDelay(0.90d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateCross(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", new Vector2(0.86f, 0.86f), 0.24d)
            .SetDelay(0.12d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.12d)
            .SetDelay(0.12d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "scale", new Vector2(0.96f, 0.96f), 0.28d)
            .SetDelay(0.52d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(0.94d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateCrossHalo(Tween tween, Node2D node, int index)
    {
        tween.TweenProperty(node, "scale", Vector2.One * (1.04f + index * 0.04f), 0.42d)
            .SetDelay(0.20d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0.72f - index * 0.12f), 0.12d)
            .SetDelay(0.20d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau * (0.16f + index * 0.05f), Duration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(0.94d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateSpirit(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", Vector2.One, 0.20d)
            .SetDelay(0.32d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0.82f), 0.12d)
            .SetDelay(0.32d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "position", node.Position + new Vector2(0f, -118f), 0.46d)
            .SetDelay(0.34d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.22d)
            .SetDelay(0.78d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateParticle(Tween tween, Node2D node, int index)
    {
        var delay = 0.16d + index * 0.004d;

        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0.82f), 0.08d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "position", node.GetMeta("target").AsVector2(), 0.70d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(0.88d + index * 0.001d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateReleaseWave(Tween tween, Node2D node, int index)
    {
        var delay = 0.54d + index * 0.04d;

        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0.78f - index * 0.15f), 0.04d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "scale", Vector2.One * (3.3f + index * 0.82f), 0.38d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.32d)
            .SetDelay(delay + 0.10d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void BuildGroundCircle(Node2D root)
    {
        var position = new Vector2(0f, 112f);
        for (var i = 0; i < 3; i++)
        {
            var ring = new Node2D
            {
                Name = $"GroundCircle{i}",
                Position = position,
                Scale = new Vector2(0.18f, 0.18f),
                Modulate = new Color(1f, 1f, 1f, 0f),
                ZIndex = 898
            };

            var radius = 70f + i * 34f;
            var color = i == 1
                ? new Color(1f, 0.82f, 0.30f, 0.62f)
                : new Color(0.32f, 1f, 0.72f, 0.78f - i * 0.12f);
            var circle = CreateArc("GroundCircleRing", radius, 0f, Mathf.Tau, 3.5f - i * 0.5f, color);
            circle.Scale = new Vector2(1f, 0.28f);
            ring.AddChild(circle);

            root.AddChild(ring);
        }
    }

    private static void BuildRebornCross(Node2D root)
    {
        var cross = new Node2D
        {
            Name = "RebornCross",
            Position = new Vector2(0f, -8f),
            Scale = new Vector2(0.64f, 0.64f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 906
        };

        cross.AddChild(CreateArc("CrossLoopGlow", 55f, 0f, Mathf.Tau, 10f, new Color(0.32f, 1f, 0.72f, 0.26f))
            .WithScale(new Vector2(0.86f, 1.10f), new Vector2(0f, -112f)));
        cross.AddChild(CreateArc("CrossLoop", 48f, 0f, Mathf.Tau, 6f, new Color(0.38f, 1f, 0.72f, 0.92f))
            .WithScale(new Vector2(0.88f, 1.12f), new Vector2(0f, -112f)));
        cross.AddChild(CreateLine("CrossStemGlow", new Color(0.86f, 1f, 0.80f, 0.28f), 18f, new Vector2(0f, -60f), new Vector2(0f, 116f)));
        cross.AddChild(CreateLine("CrossStem", new Color(0.92f, 1f, 0.82f, 0.96f), 8f, new Vector2(0f, -60f), new Vector2(0f, 116f)));
        cross.AddChild(CreateLine("CrossArmGlow", new Color(1f, 0.78f, 0.28f, 0.30f), 16f, new Vector2(-86f, -8f), new Vector2(86f, -8f)));
        cross.AddChild(CreateLine("CrossArm", new Color(1f, 0.82f, 0.34f, 0.92f), 7f, new Vector2(-86f, -8f), new Vector2(86f, -8f)));
        cross.AddChild(CreateLine("CrossSmallArm", new Color(0.38f, 1f, 0.72f, 0.72f), 4f, new Vector2(-50f, 44f), new Vector2(50f, 44f)));
        cross.AddChild(CreatePolygon("CrossBase", new Color(0.08f, 0.22f, 0.16f, 0.48f),
            new Vector2(-36f, 116f),
            new Vector2(36f, 116f),
            new Vector2(23f, 143f),
            new Vector2(-23f, 143f)));
        cross.AddChild(CreateLine("CrossBaseTrace", new Color(1f, 0.82f, 0.34f, 0.76f), 4f,
            new Vector2(-36f, 116f),
            new Vector2(36f, 116f),
            new Vector2(23f, 143f),
            new Vector2(-23f, 143f),
            new Vector2(-36f, 116f)));

        root.AddChild(cross);

        for (var i = 0; i < 3; i++)
        {
            var halo = CreateArc($"CrossHalo{i}", 110f + i * 48f, 0f, Mathf.Tau, 2f, i == 1
                ? new Color(1f, 0.82f, 0.34f, 0.44f)
                : new Color(0.38f, 1f, 0.72f, 0.48f - i * 0.06f));
            halo.Position = new Vector2(0f, -10f);
            halo.Scale = new Vector2(0.64f, 0.36f);
            halo.Modulate = new Color(1f, 1f, 1f, 0f);
            halo.ZIndex = 900;
            root.AddChild(halo);
        }
    }

    private static void BuildSpirit(Node2D root)
    {
        var spirit = new Node2D
        {
            Name = "Spirit",
            Position = new Vector2(0f, 68f),
            Scale = new Vector2(0.58f, 0.58f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 904
        };

        spirit.AddChild(CreateArc("SpiritHeadGlow", 32f, 0f, Mathf.Tau, 9f, new Color(0.62f, 1f, 0.78f, 0.24f))
            .WithScale(Vector2.One, new Vector2(0f, -34f)));
        spirit.AddChild(CreateArc("SpiritHead", 22f, 0f, Mathf.Tau, 3f, new Color(0.92f, 1f, 0.82f, 0.62f))
            .WithScale(new Vector2(0.88f, 1.12f), new Vector2(0f, -34f)));
        spirit.AddChild(CreatePolygon("SpiritBody", new Color(0.38f, 1f, 0.72f, 0.22f),
            new Vector2(-32f, -4f),
            new Vector2(32f, -4f),
            new Vector2(14f, 62f),
            new Vector2(-14f, 62f)));
        spirit.AddChild(CreateLine("SpiritTrace", new Color(0.92f, 1f, 0.82f, 0.48f), 3f, new Vector2(0f, -8f), new Vector2(0f, 58f)));

        root.AddChild(spirit);
    }

    private static void BuildParticles(Node2D root)
    {
        for (var i = 0; i < 72; i++)
        {
            var angle = Noise(i, 0.29f) * Mathf.Tau;
            var radius = 20f + Noise(i, 0.91f) * 238f;
            var start = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.58f);
            var rise = 36f + Noise(i, 1.57f) * 118f;
            var target = start * (0.74f + Noise(i, 2.13f) * 0.20f)
                + new Vector2((Noise(i, 2.91f) - 0.5f) * 44f, -rise);

            var particle = CreateArc($"Particle{i}", 1.8f + Noise(i, 3.41f) * 2.2f, 0f, Mathf.Tau, 2f, i % 3 == 0
                ? new Color(1f, 0.84f, 0.30f, 0.70f)
                : new Color(0.42f, 1f, 0.72f, 0.74f));
            particle.Position = start;
            particle.Modulate = new Color(1f, 1f, 1f, 0f);
            particle.ZIndex = 908;
            particle.SetMeta("target", Variant.From(target));
            root.AddChild(particle);
        }
    }

    private static void BuildReleaseWaves(Node2D root, Vector2 viewportSize)
    {
        var baseRadius = Mathf.Max(92f, Mathf.Min(viewportSize.X, viewportSize.Y) * 0.18f);
        for (var i = 0; i < 2; i++)
        {
            var wave = CreateArc($"ReleaseWave{i}", baseRadius + i * 34f, 0f, Mathf.Tau, 4f - i * 0.5f, i == 0
                ? new Color(0.92f, 1f, 0.82f, 0.82f)
                : new Color(0.38f, 1f, 0.72f, 0.58f));
            wave.Position = new Vector2(0f, 58f);
            wave.Scale = new Vector2(0.16f, 0.09f);
            wave.Modulate = new Color(1f, 1f, 1f, 0f);
            wave.ZIndex = 912;
            root.AddChild(wave);
        }
    }

    private static Line2D CreateArc(string name, float radius, float start, float sweep, float width, Color color)
    {
        return new Line2D
        {
            Name = name,
            Points = BuildArcPoints(radius, start, sweep, 72),
            Width = width,
            DefaultColor = color,
            Antialiased = true,
            ZIndex = 897
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

    private static Line2D CreateLine(string name, Color color, float width, params Vector2[] points)
    {
        return new Line2D
        {
            Name = name,
            Points = points,
            Width = width,
            DefaultColor = color,
            Antialiased = true,
            ZIndex = 898
        };
    }

    private static Polygon2D CreatePolygon(string name, Color color, params Vector2[] points)
    {
        return new Polygon2D
        {
            Name = name,
            Polygon = points,
            Color = color,
            ZIndex = 897
        };
    }

    private static Vector2 Unit(float angle)
    {
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private static int ExtractIndex(string name)
    {
        var index = 0;
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsDigit(name[i]))
            {
                index = index * 10 + (name[i] - '0');
            }
        }

        return index;
    }

    private static float Noise(int index, float seed)
    {
        return Mathf.PosMod(Mathf.Sin(index * 12.9898f + seed * 78.233f) * 43758.5453f, 1f);
    }

    private static T WithScale<T>(this T node, Vector2 scale, Vector2 position)
        where T : Node2D
    {
        node.Scale = scale;
        node.Position = position;
        return node;
    }
}
