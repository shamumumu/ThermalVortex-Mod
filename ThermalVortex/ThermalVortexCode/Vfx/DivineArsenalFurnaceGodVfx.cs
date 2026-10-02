using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class DivineArsenalFurnaceGodVfx
{
    private const double Duration = 1.24d;
    private const double AttackCueSeconds = 0.72d;
    private const double TimeoutSeconds = 1.55d;

    public static async Task PlayAsync(Creature source, IReadOnlyList<Creature> targets, int exhaustedHandCount, int energySpent)
    {
        if (targets is null || targets.Count == 0 || energySpent <= 0)
        {
            return;
        }

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
            Name = "ThermalVortexDivineArsenalFurnaceGodVfx",
            Layer = 134
        };

        var overlay = new ColorRect
        {
            Name = "DivineArsenalOverlay",
            Color = new Color(0.01f, 0.02f, 0.03f, 0f),
            Size = viewportSize,
            ZIndex = 890
        };

        var flash = new ColorRect
        {
            Name = "DivineArsenalFlash",
            Color = new Color(1f, 0.82f, 0.42f, 0f),
            Size = viewportSize,
            ZIndex = 908
        };

        var root = new Node2D
        {
            Name = "DivineArsenalRoot",
            Position = viewportSize * 0.5f + new Vector2(0f, -32f),
            ZIndex = 896
        };

        layer.AddChild(overlay);
        layer.AddChild(root);
        layer.AddChild(flash);
        tree.Root.AddChild(layer);

        var targetOffsets = ResolveTargetOffsets(viewportSize, targets.Count, root.Position);
        BuildDeploymentRing(root);
        var mech = BuildMechSilhouette();
        root.AddChild(mech);
        BuildCardShards(root, viewportSize, exhaustedHandCount);
        BuildEnergyParticles(root, energySpent);
        BuildTargetLocks(root, targetOffsets);
        BuildJudgementBeams(root, targetOffsets);
        BuildShockwave(root);
        BuildImpacts(root, targetOffsets);

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

        tween.TweenProperty(overlay, "color", new Color(0.01f, 0.02f, 0.03f, 0.66f), 0.14d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(overlay, "color", new Color(0.01f, 0.02f, 0.03f, 0f), 0.28d)
            .SetDelay(0.92d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        tween.TweenProperty(flash, "color", new Color(1f, 0.82f, 0.42f, 0.22f), 0.05d)
            .SetDelay(0.70d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(flash, "color", new Color(1f, 0.82f, 0.42f, 0f), 0.12d)
            .SetDelay(0.76d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        AnimateChildren(tween, root, mech);

        var cueTimer = layer.GetTree().CreateTimer(AttackCueSeconds);
        cueTimer.Timeout += SignalAttackCue;

        var timeout = layer.GetTree().CreateTimer(TimeoutSeconds);
        timeout.Timeout += Complete;

        await attackCue.Task;
    }

    private static void AnimateChildren(Tween tween, Node2D root, Node2D mech)
    {
        tween.TweenProperty(mech, "modulate", new Color(1f, 1f, 1f, 1f), 0.20d)
            .SetDelay(0.18d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(mech, "scale", new Vector2(0.80f, 0.80f), 0.58d)
            .SetDelay(0.18d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(mech, "position", new Vector2(0f, -30f), 0.58d)
            .SetDelay(0.18d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(mech, "modulate", new Color(1f, 1f, 1f, 0f), 0.26d)
            .SetDelay(0.90d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        foreach (var child in root.GetChildren())
        {
            if (child is not Node2D node || node == mech)
            {
                continue;
            }

            var name = node.Name.ToString();
            if (name.StartsWith("Deploy", StringComparison.Ordinal))
            {
                AnimateDeployment(tween, node);
            }
            else if (name.StartsWith("CardShard", StringComparison.Ordinal))
            {
                AnimateCardShard(tween, node, ExtractIndex(name));
            }
            else if (name.StartsWith("EnergyParticle", StringComparison.Ordinal))
            {
                AnimateEnergyParticle(tween, node, ExtractIndex(name));
            }
            else if (name.StartsWith("TargetLock", StringComparison.Ordinal))
            {
                AnimateTargetLock(tween, node);
            }
            else if (name.StartsWith("JudgementBeam", StringComparison.Ordinal))
            {
                AnimateJudgementBeam(tween, node);
            }
            else if (name.StartsWith("Shockwave", StringComparison.Ordinal))
            {
                AnimateShockwave(tween, node);
            }
            else if (name.StartsWith("ImpactRing", StringComparison.Ordinal))
            {
                AnimateImpactRing(tween, node);
            }
            else if (name.StartsWith("ImpactSpark", StringComparison.Ordinal))
            {
                AnimateImpactSpark(tween, node, ExtractIndex(name));
            }
            else
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
                    .SetDelay(0.92d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
            }
        }
    }

    private static void AnimateDeployment(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau * 0.72f, Duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "scale", node.Scale * 1.20f, Duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.32d)
            .SetDelay(0.88d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateCardShard(Tween tween, Node2D node, int index)
    {
        var delay = 0.14d + index % 11 * 0.024d;
        var flight = 0.42d + index % 5 * 0.035d;

        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.06d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "position", Vector2.Zero, flight)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau * (0.8f + index % 4 * 0.25f), flight)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "scale", new Vector2(0.20f, 0.20f), flight)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.16d)
            .SetDelay(delay + flight * 0.78d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateEnergyParticle(Tween tween, Node2D node, int index)
    {
        var delay = 0.18d + index % 19 * 0.016d;

        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.05d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "position", Vector2.Zero, 0.50d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.18d)
            .SetDelay(delay + 0.38d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateTargetLock(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", Vector2.One, 0.18d)
            .SetDelay(0.44d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.08d)
            .SetDelay(0.44d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau * 0.55f, 0.58d)
            .SetDelay(0.44d)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.22d)
            .SetDelay(0.94d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateJudgementBeam(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", Vector2.One, 0.18d)
            .SetDelay(0.62d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.07d)
            .SetDelay(0.62d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(0.88d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateShockwave(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", new Vector2(1.18f, 1.18f), 0.28d)
            .SetDelay(0.74d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.06d)
            .SetDelay(0.74d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.22d)
            .SetDelay(0.98d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateImpactRing(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", new Vector2(1.28f, 1.28f), 0.24d)
            .SetDelay(0.72d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.05d)
            .SetDelay(0.72d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(0.94d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateImpactSpark(Tween tween, Node2D node, int index)
    {
        var delay = 0.72d + index % 12 * 0.012d;

        tween.TweenProperty(node, "scale", Vector2.One, 0.22d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.04d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.20d)
            .SetDelay(delay + 0.16d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static Vector2[] ResolveTargetOffsets(Vector2 viewportSize, int targetCount, Vector2 origin)
    {
        var count = Mathf.Max(1, Mathf.Min(targetCount, 5));
        var offsets = new Vector2[count];
        var left = viewportSize.X * 0.64f;
        var right = viewportSize.X * 0.86f;
        var top = viewportSize.Y * 0.34f;
        var bottom = viewportSize.Y * 0.58f;

        for (var i = 0; i < count; i++)
        {
            var x = count == 1 ? viewportSize.X * 0.76f : Mathf.Lerp(left, right, (float)i / (count - 1));
            var y = count == 1 ? viewportSize.Y * 0.43f : Mathf.Lerp(top, bottom, i % 2 == 0 ? 0.2f : 0.9f);
            offsets[i] = new Vector2(x, y) - origin;
        }

        return offsets;
    }

    private static void BuildDeploymentRing(Node2D root)
    {
        var cyan = new Color(0.36f, 0.90f, 1f, 0.86f);
        var amber = new Color(1f, 0.74f, 0.25f, 0.82f);
        var white = new Color(0.92f, 0.96f, 1f, 0.86f);

        root.AddChild(CreateArc("DeployOuter", 210f, 0f, Mathf.Tau * 0.88f, 9f, cyan));
        root.AddChild(CreateArc("DeployMiddle", 154f, Mathf.Tau * 0.13f, Mathf.Tau * 0.76f, 7f, amber));
        root.AddChild(CreateArc("DeployInner", 96f, Mathf.Tau * 0.28f, Mathf.Tau * 0.92f, 4f, white));

        var hex = new Line2D
        {
            Name = "DeployHex",
            Points = ClosePoints(BuildRegularPoints(Vector2.Zero, 126f, 6, -Mathf.Pi / 2f)),
            Width = 3f,
            DefaultColor = amber,
            Antialiased = true,
            ZIndex = 898
        };
        root.AddChild(hex);

        for (var i = 0; i < 12; i++)
        {
            var angle = Mathf.Tau * i / 12f;
            root.AddChild(new Line2D
            {
                Name = $"DeployTick{i}",
                Points = new[]
                {
                    Unit(angle) * 166f,
                    Unit(angle) * 202f
                },
                Width = 4f,
                DefaultColor = i % 2 == 0 ? white : cyan,
                Antialiased = true,
                ZIndex = 899
            });
        }
    }

    private static Node2D BuildMechSilhouette()
    {
        var mech = new Node2D
        {
            Name = "MechSilhouette",
            Scale = new Vector2(0.68f, 0.68f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 900
        };

        var steel = new Color(0.82f, 0.87f, 0.89f, 0.92f);
        var shadow = new Color(0.21f, 0.25f, 0.30f, 0.92f);
        var dark = new Color(0.24f, 0.32f, 0.42f, 0.68f);
        var amber = new Color(1f, 0.68f, 0.18f, 0.84f);
        var cyan = new Color(0.36f, 0.90f, 1f, 0.76f);

        mech.AddChild(CreatePolygon("BackFinLeft", dark, new Vector2(-150f, -42f), new Vector2(-248f, -142f), new Vector2(-112f, -116f), new Vector2(-48f, -40f)));
        mech.AddChild(CreatePolygon("BackFinRight", dark, new Vector2(150f, -42f), new Vector2(248f, -142f), new Vector2(112f, -116f), new Vector2(48f, -40f)));
        mech.AddChild(CreatePolygon("ShoulderLeft", steel, new Vector2(-142f, -46f), new Vector2(-78f, -96f), new Vector2(-20f, -58f), new Vector2(-58f, 16f), new Vector2(-136f, 4f)));
        mech.AddChild(CreatePolygon("ShoulderRight", steel, new Vector2(142f, -46f), new Vector2(78f, -96f), new Vector2(20f, -58f), new Vector2(58f, 16f), new Vector2(136f, 4f)));
        mech.AddChild(CreatePolygon("TorsoDark", shadow, new Vector2(-58f, -70f), new Vector2(58f, -70f), new Vector2(82f, 10f), new Vector2(34f, 118f), new Vector2(-34f, 118f), new Vector2(-82f, 10f)));
        mech.AddChild(CreatePolygon("TorsoPlate", steel, new Vector2(-36f, -44f), new Vector2(36f, -44f), new Vector2(48f, 6f), new Vector2(16f, 74f), new Vector2(-16f, 74f), new Vector2(-48f, 6f)));
        mech.AddChild(CreatePolygon("Head", steel, new Vector2(-32f, -110f), new Vector2(0f, -146f), new Vector2(34f, -110f), new Vector2(20f, -76f), new Vector2(-20f, -76f)));

        mech.AddChild(CreateLine("HornLeft", amber, 5f, new Vector2(-22f, -120f), new Vector2(-86f, -172f)));
        mech.AddChild(CreateLine("HornRight", amber, 5f, new Vector2(22f, -120f), new Vector2(86f, -172f)));
        mech.AddChild(CreateLine("ArmLeft", steel, 16f, new Vector2(-116f, -10f), new Vector2(-186f, 70f), new Vector2(-236f, 100f)));
        mech.AddChild(CreateLine("ArmRight", steel, 16f, new Vector2(116f, -10f), new Vector2(186f, 70f), new Vector2(236f, 100f)));
        mech.AddChild(CreateLine("RailLeft", cyan, 7f, new Vector2(-218f, 84f), new Vector2(-278f, 122f)));
        mech.AddChild(CreateLine("RailRight", cyan, 7f, new Vector2(218f, 84f), new Vector2(278f, 122f)));
        mech.AddChild(CreateArc("CoreGlow", 48f, 0f, Mathf.Tau, 8f, amber));
        mech.AddChild(CreateLine("LegTrim", cyan, 5f, new Vector2(-66f, 88f), new Vector2(-28f, 138f), new Vector2(0f, 92f), new Vector2(28f, 138f), new Vector2(66f, 88f)));

        return mech;
    }

    private static void BuildCardShards(Node2D root, Vector2 viewportSize, int exhaustedHandCount)
    {
        var count = Mathf.Clamp(exhaustedHandCount * 3, 8, 24);
        var source = new Vector2(viewportSize.X * 0.18f, viewportSize.Y * 0.82f) - root.Position;

        for (var i = 0; i < count; i++)
        {
            var start = source + new Vector2(Noise(i, 0.17f) * 94f - 34f, Noise(i, 0.53f) * 56f - 24f);
            var shard = new Node2D
            {
                Name = $"CardShard{i}",
                Position = start,
                Rotation = Noise(i, 0.91f) * Mathf.Tau,
                Scale = new Vector2(1f, 1f),
                Modulate = new Color(1f, 1f, 1f, 0f),
                ZIndex = 904
            };

            shard.AddChild(CreatePolygon("CardFill", new Color(0.28f, 0.36f, 0.44f, 0.78f), new Vector2(-12f, -18f), new Vector2(12f, -18f), new Vector2(12f, 18f), new Vector2(-12f, 18f)));
            shard.AddChild(CreateLine("CardEdge", new Color(1f, 0.74f, 0.25f, 0.88f), 2f, new Vector2(-12f, -18f), new Vector2(12f, -18f), new Vector2(12f, 18f), new Vector2(-12f, 18f), new Vector2(-12f, -18f)));
            shard.AddChild(CreateLine("CardTrace", new Color(0.36f, 0.90f, 1f, 0.70f), 2f, new Vector2(-7f, 0f), new Vector2(7f, 0f)));
            root.AddChild(shard);
        }
    }

    private static void BuildEnergyParticles(Node2D root, int energySpent)
    {
        var count = Mathf.Clamp(energySpent * 18, 36, 96);

        for (var i = 0; i < count; i++)
        {
            var angle = Mathf.Tau * i / count + Noise(i, 1.03f) * 0.18f;
            var radius = 90f + Noise(i, 1.79f) * 250f;
            var particle = CreateArc($"EnergyParticle{i}", 3f + i % 3, 0f, Mathf.Tau, 2f, i % 2 == 0
                ? new Color(0.36f, 0.90f, 1f, 0.88f)
                : new Color(1f, 0.62f, 0.22f, 0.78f));
            particle.Position = Unit(angle) * radius;
            particle.Modulate = new Color(1f, 1f, 1f, 0f);
            particle.ZIndex = 903;
            root.AddChild(particle);
        }
    }

    private static void BuildTargetLocks(Node2D root, Vector2[] targetOffsets)
    {
        for (var i = 0; i < targetOffsets.Length; i++)
        {
            var lockRoot = new Node2D
            {
                Name = $"TargetLock{i}",
                Position = targetOffsets[i],
                Scale = new Vector2(0.24f, 0.24f),
                Modulate = new Color(1f, 1f, 1f, 0f),
                ZIndex = 905
            };

            lockRoot.AddChild(CreateArc("LockCircle", 42f, 0f, Mathf.Tau, 3f, new Color(1f, 0.74f, 0.25f, 0.90f)));
            lockRoot.AddChild(CreateLine("LockHex", new Color(0.36f, 0.90f, 1f, 0.78f), 2f, ClosePoints(BuildRegularPoints(Vector2.Zero, 28f, 6, -Mathf.Pi / 2f))));
            lockRoot.AddChild(CreateLine("LockLeft", new Color(1f, 0.74f, 0.25f, 0.90f), 3f, new Vector2(-58f, 0f), new Vector2(-30f, 0f)));
            lockRoot.AddChild(CreateLine("LockRight", new Color(1f, 0.74f, 0.25f, 0.90f), 3f, new Vector2(30f, 0f), new Vector2(58f, 0f)));
            lockRoot.AddChild(CreateLine("LockTop", new Color(1f, 0.74f, 0.25f, 0.90f), 3f, new Vector2(0f, -58f), new Vector2(0f, -30f)));
            lockRoot.AddChild(CreateLine("LockBottom", new Color(1f, 0.74f, 0.25f, 0.90f), 3f, new Vector2(0f, 30f), new Vector2(0f, 58f)));
            root.AddChild(lockRoot);
        }
    }

    private static void BuildJudgementBeams(Node2D root, Vector2[] targetOffsets)
    {
        for (var i = 0; i < targetOffsets.Length; i++)
        {
            var start = new Vector2((i - (targetOffsets.Length - 1) * 0.5f) * 30f, 34f);
            var end = targetOffsets[i];
            root.AddChild(CreateBeamLine($"JudgementBeamGlow{i}", start, end, 34f, new Color(1f, 0.44f, 0.14f, 0.55f), 4101));
            root.AddChild(CreateBeamLine($"JudgementBeamCore{i}", start, end, 9f, new Color(0.94f, 0.98f, 1f, 0.95f), 4106));

            var direction = (end - start).Normalized();
            var perpendicular = new Vector2(-direction.Y, direction.X);
            root.AddChild(CreateBeamLine($"JudgementBeamAmberEdge{i}", start + perpendicular * 18f, end + perpendicular * 22f, 4f, new Color(1f, 0.74f, 0.25f, 0.72f), 4105));
            root.AddChild(CreateBeamLine($"JudgementBeamCyanEdge{i}", start - perpendicular * 18f, end - perpendicular * 22f, 4f, new Color(0.36f, 0.90f, 1f, 0.72f), 4105));
        }
    }

    private static Line2D CreateBeamLine(string name, Vector2 start, Vector2 end, float width, Color color, int zIndex)
    {
        return new Line2D
        {
            Name = name,
            Points = new[] { start, end },
            Width = width,
            DefaultColor = color,
            Antialiased = true,
            Scale = new Vector2(0.08f, 0.08f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = zIndex
        };
    }

    private static void BuildShockwave(Node2D root)
    {
        root.AddChild(new Line2D
        {
            Name = "Shockwave",
            Points = new[]
            {
                new Vector2(-420f, 112f),
                new Vector2(420f, 84f)
            },
            Width = 7f,
            DefaultColor = new Color(0.94f, 0.98f, 1f, 0.66f),
            Antialiased = true,
            Scale = new Vector2(0.18f, 0.18f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 907
        });
    }

    private static void BuildImpacts(Node2D root, Vector2[] targetOffsets)
    {
        var sparkIndex = 0;
        for (var i = 0; i < targetOffsets.Length; i++)
        {
            var ring = CreateArc($"ImpactRing{i}", 68f, 0f, Mathf.Tau, 4f, new Color(0.94f, 0.98f, 1f, 0.90f));
            ring.Position = targetOffsets[i];
            ring.Scale = new Vector2(0.16f, 0.16f);
            ring.Modulate = new Color(1f, 1f, 1f, 0f);
            ring.ZIndex = 906;
            root.AddChild(ring);

            for (var j = 0; j < 28; j++)
            {
                var angle = Mathf.Tau * j / 28f + Noise(sparkIndex, 2.47f) * 0.24f;
                var direction = Unit(angle);
                var length = 26f + Noise(sparkIndex, 3.01f) * 100f;
                root.AddChild(new Line2D
                {
                    Name = $"ImpactSpark{sparkIndex}",
                    Position = targetOffsets[i],
                    Points = new[]
                    {
                        direction * 12f,
                        direction * length
                    },
                    Width = 2f + sparkIndex % 3,
                    DefaultColor = sparkIndex % 2 == 0
                        ? new Color(1f, 0.74f, 0.25f, 0.82f)
                        : new Color(0.36f, 0.90f, 1f, 0.82f),
                    Antialiased = true,
                    Scale = new Vector2(0.12f, 0.12f),
                    Modulate = new Color(1f, 1f, 1f, 0f),
                    ZIndex = 908
                });
                sparkIndex++;
            }
        }
    }

    private static Polygon2D CreatePolygon(string name, Color color, params Vector2[] points)
    {
        return new Polygon2D
        {
            Name = name,
            Polygon = points,
            Color = color,
            ZIndex = 900
        };
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
            ZIndex = 901
        };
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

    private static Vector2[] BuildRegularPoints(Vector2 center, float radius, int count, float rotation)
    {
        var result = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = center + Unit(rotation + Mathf.Tau * i / count) * radius;
        }

        return result;
    }

    private static Vector2[] ClosePoints(Vector2[] points)
    {
        var result = new Vector2[points.Length + 1];
        Array.Copy(points, result, points.Length);
        result[^1] = points[0];
        return result;
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
