using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class BlueEyesWhiteDragonVfx
{
    private const double Duration = 1.18d;
    private const double AttackCueSeconds = 0.72d;
    private const double TimeoutSeconds = 1.45d;

    public static async Task PlayAsync(Creature target)
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
            Name = "ThermalVortexBlueEyesWhiteDragonVfx",
            Layer = 132
        };

        var overlay = new ColorRect
        {
            Name = "BlueEyesOverlay",
            Color = new Color(0.01f, 0.03f, 0.06f, 0f),
            Size = viewportSize,
            ZIndex = 790
        };

        var flash = new ColorRect
        {
            Name = "BlueEyesFlash",
            Color = new Color(0.72f, 0.94f, 1f, 0f),
            Size = viewportSize,
            ZIndex = 805
        };

        var root = new Node2D
        {
            Name = "BlueEyesRoot",
            Position = viewportSize * 0.5f + new Vector2(0f, -24f),
            ZIndex = 796
        };

        var targetOffset = ResolveTargetOffset(viewportSize, target, root.Position);

        layer.AddChild(overlay);
        layer.AddChild(root);
        layer.AddChild(flash);
        tree.Root.AddChild(layer);

        BuildSummonGate(root);
        var dragon = BuildDragonSilhouette();
        root.AddChild(dragon);
        BuildBlast(root, targetOffset);
        BuildTravelParticles(root, targetOffset);
        BuildImpact(root, targetOffset);

        var attackCue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cueSent = false;
        var completed = false;

        void SignalAttackCue()
        {
            if (cueSent)
            {
                return;
            }

            cueSent = true;
            attackCue.TrySetResult();
        }

        void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            SignalAttackCue();

            if (GodotObject.IsInstanceValid(layer))
            {
                layer.QueueFree();
            }
        }

        var tween = layer.CreateTween();
        tween.SetParallel(true);
        tween.Finished += Complete;

        tween.TweenProperty(overlay, "color", new Color(0.01f, 0.03f, 0.06f, 0.58f), 0.16d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(overlay, "color", new Color(0.01f, 0.03f, 0.06f, 0f), 0.32d)
            .SetDelay(0.86d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        tween.TweenProperty(flash, "color", new Color(0.72f, 0.94f, 1f, 0.20f), 0.08d)
            .SetDelay(0.72d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(flash, "color", new Color(0.72f, 0.94f, 1f, 0f), 0.24d)
            .SetDelay(0.80d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        AnimateChildren(tween, root, dragon, targetOffset);

        var cueTimer = layer.GetTree().CreateTimer(AttackCueSeconds);
        cueTimer.Timeout += SignalAttackCue;

        var timeout = layer.GetTree().CreateTimer(TimeoutSeconds);
        timeout.Timeout += Complete;

        await attackCue.Task;
    }

    private static void AnimateChildren(Tween tween, Node2D root, Node2D dragon, Vector2 targetOffset)
    {
        tween.TweenProperty(dragon, "modulate", new Color(1f, 1f, 1f, 1f), 0.22d)
            .SetDelay(0.20d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(dragon, "scale", new Vector2(0.82f, 0.82f), 0.70d)
            .SetDelay(0.18d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(dragon, "position", new Vector2(12f, -48f), 0.78d)
            .SetDelay(0.18d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(dragon, "modulate", new Color(1f, 1f, 1f, 0f), 0.28d)
            .SetDelay(0.84d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        foreach (var child in root.GetChildren())
        {
            if (child is not Node2D node || node == dragon)
            {
                continue;
            }

            var name = node.Name.ToString();
            if (name.StartsWith("Gate", StringComparison.Ordinal))
            {
                AnimateGate(tween, node);
            }
            else if (name.StartsWith("Blast", StringComparison.Ordinal))
            {
                AnimateBlast(tween, node);
            }
            else if (name.StartsWith("TravelParticle", StringComparison.Ordinal))
            {
                AnimateTravelParticle(tween, node, targetOffset, ExtractIndex(name));
            }
            else if (name.StartsWith("ImpactRing", StringComparison.Ordinal))
            {
                AnimateImpactRing(tween, node);
            }
            else if (name.StartsWith("ImpactParticle", StringComparison.Ordinal))
            {
                AnimateImpactParticle(tween, node, ExtractIndex(name));
            }
            else
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.26d)
                    .SetDelay(0.88d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
            }
        }
    }

    private static void AnimateGate(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau, Duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "scale", node.Scale * 1.22f, Duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.36d)
            .SetDelay(0.78d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateBlast(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", Vector2.One, 0.18d)
            .SetDelay(0.58d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.08d)
            .SetDelay(0.58d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.34d)
            .SetDelay(0.86d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateTravelParticle(Tween tween, Node2D node, Vector2 targetOffset, int index)
    {
        var delay = 0.22d + index % 17 * 0.018d;
        var flight = 0.46d + index % 5 * 0.035d;

        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.07d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "position", targetOffset, flight)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.20d)
            .SetDelay(delay + flight * 0.74d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateImpactRing(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", new Vector2(1.32f, 1.32f), 0.30d)
            .SetDelay(0.72d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.06d)
            .SetDelay(0.72d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(0.92d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateImpactParticle(Tween tween, Node2D node, int index)
    {
        var delay = 0.74d + index % 8 * 0.012d;

        tween.TweenProperty(node, "scale", Vector2.One, 0.24d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.05d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.22d)
            .SetDelay(delay + 0.16d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static Vector2 ResolveTargetOffset(Vector2 viewportSize, Creature target, Vector2 origin)
    {
        var screenTarget = target is not null && target.IsEnemy
            ? new Vector2(viewportSize.X * 0.74f, viewportSize.Y * 0.42f)
            : new Vector2(viewportSize.X * 0.30f, viewportSize.Y * 0.70f);

        return screenTarget - origin;
    }

    private static void BuildSummonGate(Node2D root)
    {
        var blue = new Color(0.28f, 0.82f, 1f, 0.88f);
        var white = new Color(0.92f, 1f, 1f, 0.92f);
        var pale = new Color(0.56f, 0.92f, 1f, 0.62f);

        root.AddChild(CreateArc("GateOuter", 186f, 0f, Mathf.Tau * 0.92f, 10f, blue));
        root.AddChild(CreateArc("GateMiddle", 130f, Mathf.Tau * 0.08f, Mathf.Tau * 0.84f, 7f, pale));
        root.AddChild(CreateArc("GateInner", 78f, Mathf.Tau * 0.18f, Mathf.Tau * 0.78f, 5f, white));

        for (var i = 0; i < 12; i++)
        {
            var angle = Mathf.Tau * i / 12f;
            var tick = new Line2D
            {
                Name = $"GateTick{i}",
                Points = new[]
                {
                    Unit(angle) * 144f,
                    Unit(angle) * 176f
                },
                Width = 4f,
                DefaultColor = i % 2 == 0 ? white : blue,
                Antialiased = true,
                ZIndex = 798
            };
            root.AddChild(tick);
        }
    }

    private static Node2D BuildDragonSilhouette()
    {
        var dragon = new Node2D
        {
            Name = "DragonSilhouette",
            Position = new Vector2(-22f, -10f),
            Scale = new Vector2(0.64f, 0.64f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 800
        };

        var glow = new Color(0.45f, 0.88f, 1f, 0.42f);
        var core = new Color(0.93f, 1f, 1f, 0.94f);

        dragon.AddChild(new Polygon2D
        {
            Name = "LeftWing",
            Polygon = new[]
            {
                new Vector2(-128f, -20f),
                new Vector2(-238f, -158f),
                new Vector2(-48f, -90f),
                new Vector2(20f, -128f),
                new Vector2(-30f, -26f)
            },
            Color = glow,
            ZIndex = 798
        });

        dragon.AddChild(new Polygon2D
        {
            Name = "RightWing",
            Polygon = new[]
            {
                new Vector2(18f, -22f),
                new Vector2(116f, -148f),
                new Vector2(160f, -42f),
                new Vector2(248f, -90f),
                new Vector2(104f, -8f)
            },
            Color = new Color(0.56f, 0.94f, 1f, 0.34f),
            ZIndex = 798
        });

        dragon.AddChild(new Line2D
        {
            Name = "DragonBody",
            Points = new[]
            {
                new Vector2(-230f, 64f),
                new Vector2(-172f, 20f),
                new Vector2(-96f, -18f),
                new Vector2(-16f, -16f),
                new Vector2(52f, -46f),
                new Vector2(122f, -32f),
                new Vector2(184f, 4f),
                new Vector2(236f, 8f)
            },
            Width = 18f,
            DefaultColor = core,
            Antialiased = true,
            ZIndex = 801
        });

        dragon.AddChild(new Polygon2D
        {
            Name = "DragonHead",
            Polygon = new[]
            {
                new Vector2(166f, -28f),
                new Vector2(232f, -64f),
                new Vector2(218f, -20f),
                new Vector2(268f, 4f),
                new Vector2(210f, 24f),
                new Vector2(160f, 8f)
            },
            Color = core,
            ZIndex = 802
        });

        dragon.AddChild(new Line2D
        {
            Name = "DragonHorns",
            Points = new[]
            {
                new Vector2(194f, -40f),
                new Vector2(222f, -104f),
                new Vector2(208f, -36f),
                new Vector2(260f, -78f)
            },
            Width = 7f,
            DefaultColor = new Color(0.78f, 0.96f, 1f, 0.88f),
            Antialiased = true,
            ZIndex = 803
        });

        dragon.AddChild(new Line2D
        {
            Name = "DragonSpine",
            Points = new[]
            {
                new Vector2(-94f, -32f),
                new Vector2(-60f, -70f),
                new Vector2(-20f, -28f),
                new Vector2(24f, -76f),
                new Vector2(58f, -40f),
                new Vector2(98f, -74f),
                new Vector2(130f, -30f)
            },
            Width = 6f,
            DefaultColor = glow,
            Antialiased = true,
            ZIndex = 803
        });

        return dragon;
    }

    private static void BuildBlast(Node2D root, Vector2 targetOffset)
    {
        var direction = targetOffset == Vector2.Zero ? Vector2.Right : targetOffset.Normalized();
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var start = -direction * 24f;

        root.AddChild(CreateBlastLine("BlastGlow", start, targetOffset, 34f, new Color(0.26f, 0.82f, 1f, 0.52f), 802));
        root.AddChild(CreateBlastLine("BlastCore", start, targetOffset, 13f, new Color(0.94f, 1f, 1f, 0.98f), 804));
        root.AddChild(CreateBlastLine("BlastEdgeA", start + perpendicular * 20f, targetOffset + perpendicular * 28f, 5f, new Color(0.22f, 0.82f, 1f, 0.76f), 803));
        root.AddChild(CreateBlastLine("BlastEdgeB", start - perpendicular * 20f, targetOffset - perpendicular * 28f, 5f, new Color(0.22f, 0.82f, 1f, 0.76f), 803));
    }

    private static Line2D CreateBlastLine(string name, Vector2 start, Vector2 end, float width, Color color, int zIndex)
    {
        return new Line2D
        {
            Name = name,
            Points = new[] { start, end },
            Width = width,
            DefaultColor = color,
            Antialiased = true,
            Modulate = new Color(1f, 1f, 1f, 0f),
            Scale = new Vector2(0.10f, 0.10f),
            ZIndex = zIndex
        };
    }

    private static void BuildTravelParticles(Node2D root, Vector2 targetOffset)
    {
        var colors = new[]
        {
            new Color(0.28f, 0.82f, 1f, 0.95f),
            new Color(0.92f, 1f, 1f, 0.90f),
            new Color(0.62f, 0.92f, 1f, 0.84f)
        };

        for (var i = 0; i < 52; i++)
        {
            var angle = Mathf.Tau * i / 52f + Noise(i, 0.13f) * 0.12f;
            var start = Unit(angle) * (78f + Noise(i, 0.71f) * 128f);
            var direction = targetOffset - start;
            if (direction.LengthSquared() < 1f)
            {
                direction = Vector2.Right;
            }

            root.AddChild(new Line2D
            {
                Name = $"TravelParticle{i}",
                Position = start,
                Points = new[]
                {
                    Vector2.Zero,
                    -direction.Normalized() * (18f + i % 4 * 5f)
                },
                Width = 3f + i % 3,
                DefaultColor = colors[i % colors.Length],
                Antialiased = true,
                Modulate = new Color(1f, 1f, 1f, 0f),
                ZIndex = 802
            });
        }
    }

    private static void BuildImpact(Node2D root, Vector2 targetOffset)
    {
        var ring = CreateArc("ImpactRing", 68f, 0f, Mathf.Tau, 5f, new Color(0.92f, 1f, 1f, 0.92f));
        ring.Position = targetOffset;
        ring.Scale = new Vector2(0.16f, 0.16f);
        ring.Modulate = new Color(1f, 1f, 1f, 0f);
        ring.ZIndex = 804;
        root.AddChild(ring);

        for (var i = 0; i < 34; i++)
        {
            var angle = Mathf.Tau * i / 34f + Noise(i, 1.19f) * 0.16f;
            var direction = Unit(angle);
            var length = 22f + Noise(i, 2.31f) * 78f;
            root.AddChild(new Line2D
            {
                Name = $"ImpactParticle{i}",
                Position = targetOffset,
                Points = new[]
                {
                    direction * 12f,
                    direction * length
                },
                Width = 2f + i % 3,
                DefaultColor = i % 2 == 0
                    ? new Color(0.80f, 0.98f, 1f, 0.86f)
                    : new Color(0.34f, 0.84f, 1f, 0.74f),
                Antialiased = true,
                Modulate = new Color(1f, 1f, 1f, 0f),
                Scale = new Vector2(0.12f, 0.12f),
                ZIndex = 805
            });
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
            ZIndex = 797
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

    private static int ExtractIndex(string name)
    {
        var index = 0;
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsDigit(name[i]))
            {
                index = index * 10 + name[i] - '0';
            }
        }

        return index;
    }

    private static float Noise(int index, float seed)
    {
        return Mathf.PosMod(Mathf.Sin(index * 12.9898f + seed * 78.233f) * 43758.5453f, 1f);
    }

    private static Vector2 Unit(float angle)
    {
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }
}
