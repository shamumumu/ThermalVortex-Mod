using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class DormantMagneticFieldBeastVfx
{
    private const double Duration = 1.18d;
    private const double BlockCueSeconds = 0.34d;
    private const double TimeoutSeconds = 1.48d;

    public static async Task PlayAsync(Creature self, IReadOnlyList<Creature> targets, int materials)
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

        var targetCount = targets?.Count ?? 0;
        var layer = new CanvasLayer
        {
            Name = "ThermalVortexDormantMagneticFieldBeastVfx",
            Layer = 133
        };

        var overlay = new ColorRect
        {
            Name = "DormantMagneticFieldOverlay",
            Color = new Color(0.01f, 0.03f, 0.04f, 0f),
            Size = viewportSize,
            ZIndex = 890
        };

        var root = new Node2D
        {
            Name = "DormantMagneticFieldRoot",
            Position = viewportSize * 0.5f + new Vector2(0f, -18f),
            ZIndex = 896
        };

        var selfOffset = ResolveSelfOffset(viewportSize, self, root.Position);
        var targetOffsets = ResolveTargetOffsets(viewportSize, targetCount, root.Position);

        layer.AddChild(overlay);
        layer.AddChild(root);
        tree.Root.AddChild(layer);

        BuildMaterialOrbs(root, selfOffset, materials);
        BuildGroundField(root, selfOffset);
        var beast = BuildSleepingBeast(selfOffset);
        root.AddChild(beast);
        BuildShield(root, selfOffset);
        BuildTargetBranches(root, selfOffset, targetOffsets);
        BuildFieldResidue(root, selfOffset);

        var blockCue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cueSent = false;
        var completed = false;

        void SignalBlockCue()
        {
            if (cueSent)
            {
                return;
            }

            cueSent = true;
            blockCue.TrySetResult();
        }

        void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            SignalBlockCue();

            if (GodotObject.IsInstanceValid(layer))
            {
                layer.QueueFree();
            }
        }

        var tween = layer.CreateTween();
        tween.SetParallel(true);
        tween.Finished += Complete;

        tween.TweenProperty(overlay, "color", new Color(0.01f, 0.03f, 0.04f, 0.42f), 0.16d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(overlay, "color", new Color(0.01f, 0.03f, 0.04f, 0f), 0.30d)
            .SetDelay(0.92d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        AnimateChildren(tween, root, beast, targetOffsets.Length);

        var cueTimer = layer.GetTree().CreateTimer(BlockCueSeconds);
        cueTimer.Timeout += SignalBlockCue;

        var timeout = layer.GetTree().CreateTimer(TimeoutSeconds);
        timeout.Timeout += Complete;

        await blockCue.Task;
    }

    private static void AnimateChildren(Tween tween, Node2D root, Node2D beast, int targetCount)
    {
        tween.TweenProperty(beast, "modulate", new Color(1f, 1f, 1f, 1f), 0.20d)
            .SetDelay(0.22d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(beast, "scale", new Vector2(0.84f, 0.84f), 0.34d)
            .SetDelay(0.22d)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(beast, "scale", new Vector2(0.80f, 0.80f), 0.34d)
            .SetDelay(0.56d)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(beast, "modulate", new Color(1f, 1f, 1f, 0f), 0.26d)
            .SetDelay(0.94d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        foreach (var child in root.GetChildren())
        {
            if (child is not Node2D node || node == beast)
            {
                continue;
            }

            var name = node.Name.ToString();
            if (name.StartsWith("MaterialOrb", StringComparison.Ordinal))
            {
                AnimateMaterialOrb(tween, node, ExtractIndex(name));
            }
            else if (name.StartsWith("GroundField", StringComparison.Ordinal))
            {
                AnimateGroundField(tween, node);
            }
            else if (name.StartsWith("Shield", StringComparison.Ordinal))
            {
                AnimateShield(tween, node);
            }
            else if (name.StartsWith("Branch", StringComparison.Ordinal))
            {
                AnimateBranch(tween, node, ExtractIndex(name), targetCount);
            }
            else if (name.StartsWith("TargetLock", StringComparison.Ordinal))
            {
                AnimateTargetLock(tween, node, ExtractIndex(name));
            }
            else if (name.StartsWith("WeakMark", StringComparison.Ordinal))
            {
                AnimateWeakMark(tween, node, ExtractIndex(name));
            }
            else if (name.StartsWith("Residue", StringComparison.Ordinal))
            {
                AnimateResidue(tween, node);
            }
            else
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
                    .SetDelay(0.94d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
            }
        }
    }

    private static void AnimateMaterialOrb(Tween tween, Node2D node, int index)
    {
        var delay = 0.04d + index * 0.055d;

        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.08d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "position", node.GetMeta("target").AsVector2(), 0.32d)
            .SetDelay(delay + 0.05d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(node, "scale", new Vector2(0.34f, 0.34f), 0.32d)
            .SetDelay(delay + 0.05d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.14d)
            .SetDelay(delay + 0.30d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateGroundField(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", Vector2.One, 0.26d)
            .SetDelay(0.20d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau * 0.20f, Duration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.12d)
            .SetDelay(0.20d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.26d)
            .SetDelay(0.98d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateShield(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", Vector2.One, 0.22d)
            .SetDelay(0.38d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.08d)
            .SetDelay(0.38d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau * 0.18f, 0.68d)
            .SetDelay(0.38d)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "scale", new Vector2(1.08f, 1.08f), 0.34d)
            .SetDelay(0.66d)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(1.02d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateBranch(Tween tween, Node2D node, int index, int targetCount)
    {
        var delay = 0.52d + index * 0.035d;
        var fadeDelay = targetCount > 4 ? 0.82d : 0.86d;

        tween.TweenProperty(node, "scale", Vector2.One, 0.17d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.06d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(fadeDelay + index * 0.012d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateTargetLock(Tween tween, Node2D node, int index)
    {
        var delay = 0.58d + index * 0.035d;

        tween.TweenProperty(node, "scale", Vector2.One, 0.17d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.06d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau * 0.42f, 0.44d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.22d)
            .SetDelay(0.94d + index * 0.012d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateWeakMark(Tween tween, Node2D node, int index)
    {
        var delay = 0.62d + index * 0.035d;

        tween.TweenProperty(node, "scale", Vector2.One, 0.16d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.06d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(0.98d + index * 0.012d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateResidue(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", new Vector2(1.12f, 1.12f), 0.36d)
            .SetDelay(0.68d)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.12d)
            .SetDelay(0.68d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(1.08d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static Vector2 ResolveSelfOffset(Vector2 viewportSize, Creature self, Vector2 origin)
    {
        var position = self is not null && self.IsEnemy
            ? new Vector2(viewportSize.X * 0.74f, viewportSize.Y * 0.52f)
            : new Vector2(viewportSize.X * 0.25f, viewportSize.Y * 0.64f);

        return position - origin;
    }

    private static Vector2[] ResolveTargetOffsets(Vector2 viewportSize, int targetCount, Vector2 origin)
    {
        if (targetCount <= 0)
        {
            return Array.Empty<Vector2>();
        }

        var offsets = new Vector2[targetCount];
        if (targetCount == 1)
        {
            offsets[0] = new Vector2(viewportSize.X * 0.76f, viewportSize.Y * 0.48f) - origin;
            return offsets;
        }

        var startX = viewportSize.X * 0.62f;
        var endX = viewportSize.X * 0.88f;
        var topY = viewportSize.Y * 0.39f;
        var bottomY = viewportSize.Y * 0.58f;

        for (var i = 0; i < targetCount; i++)
        {
            var x = Mathf.Lerp(startX, endX, (float)i / (targetCount - 1));
            var wave = i % 2 == 0 ? 0.78f : 0.22f;
            var y = Mathf.Lerp(topY, bottomY, wave) + (Noise(i, 0.61f) - 0.5f) * viewportSize.Y * 0.035f;
            offsets[i] = new Vector2(x, y) - origin;
        }

        return offsets;
    }

    private static void BuildMaterialOrbs(Node2D root, Vector2 selfOffset, int materials)
    {
        var count = Math.Clamp(materials, 1, 3);
        for (var i = 0; i < count; i++)
        {
            var start = selfOffset + new Vector2((i - (count - 1) * 0.5f) * 46f, -156f - i * 12f);
            var target = selfOffset + new Vector2((i - (count - 1) * 0.5f) * 14f, -36f);
            var orb = new Node2D
            {
                Name = $"MaterialOrb{i}",
                Position = start,
                Scale = Vector2.One,
                Modulate = new Color(1f, 1f, 1f, 0f),
                ZIndex = 908
            };
            orb.SetMeta("target", Variant.From(target));
            orb.AddChild(CreateArc("OrbGlow", 18f, 0f, Mathf.Tau, 10f, new Color(0.38f, 1f, 0.90f, 0.42f)));
            orb.AddChild(CreateArc("OrbCore", 8f, 0f, Mathf.Tau, 7f, new Color(0.95f, 1f, 0.92f, 0.92f)));
            orb.AddChild(CreateArc("OrbGold", 4f, 0f, Mathf.Tau, 5f, new Color(1f, 0.78f, 0.30f, 0.86f)));
            root.AddChild(orb);
        }
    }

    private static void BuildGroundField(Node2D root, Vector2 selfOffset)
    {
        var field = new Node2D
        {
            Name = "GroundFieldRoot",
            Position = selfOffset + new Vector2(0f, 14f),
            Scale = new Vector2(0.12f, 0.12f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 898
        };

        for (var i = 0; i < 3; i++)
        {
            var ring = CreateArc($"GroundFieldRing{i}", 56f + i * 24f, 0f, Mathf.Tau, 3f - i * 0.4f, new Color(0.34f, 0.96f, 0.86f, 0.70f - i * 0.14f));
            ring.Scale = new Vector2(1.42f, 0.34f);
            field.AddChild(ring);
        }

        for (var i = 0; i < 18; i++)
        {
            var angle = Mathf.Tau * i / 18f;
            field.AddChild(CreateLine(
                $"GroundFieldTick{i}",
                i % 2 == 0 ? new Color(1f, 0.78f, 0.30f, 0.72f) : new Color(0.42f, 1f, 0.88f, 0.62f),
                2f,
                new Vector2(Mathf.Cos(angle) * 72f, Mathf.Sin(angle) * 17f),
                new Vector2(Mathf.Cos(angle) * 94f, Mathf.Sin(angle) * 23f)));
        }

        root.AddChild(field);
    }

    private static Node2D BuildSleepingBeast(Vector2 selfOffset)
    {
        var beast = new Node2D
        {
            Name = "SleepingBeast",
            Position = selfOffset + new Vector2(8f, -86f),
            Scale = new Vector2(0.78f, 0.78f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 902
        };

        var body = new Color(0.04f, 0.16f, 0.22f, 0.78f);
        var cyan = new Color(0.34f, 0.96f, 0.86f, 0.72f);
        var gold = new Color(1f, 0.78f, 0.30f, 0.72f);

        beast.AddChild(CreatePolygon("BeastBody", body,
            new Vector2(-150f, 28f),
            new Vector2(-116f, -30f),
            new Vector2(-54f, -54f),
            new Vector2(48f, -50f),
            new Vector2(128f, -12f),
            new Vector2(154f, 24f),
            new Vector2(94f, 50f),
            new Vector2(4f, 38f),
            new Vector2(-80f, 54f)));
        beast.AddChild(CreateLine("BeastBodyTrace", cyan, 4f,
            new Vector2(-150f, 28f),
            new Vector2(-116f, -30f),
            new Vector2(-54f, -54f),
            new Vector2(48f, -50f),
            new Vector2(128f, -12f),
            new Vector2(154f, 24f),
            new Vector2(94f, 50f),
            new Vector2(4f, 38f),
            new Vector2(-80f, 54f),
            new Vector2(-150f, 28f)));
        beast.AddChild(CreatePolygon("BeastHead", new Color(0.04f, 0.18f, 0.25f, 0.84f),
            new Vector2(74f, -22f),
            new Vector2(146f, -48f),
            new Vector2(184f, -12f),
            new Vector2(136f, 20f)));
        beast.AddChild(CreateLine("BeastHeadTrace", cyan, 4f,
            new Vector2(74f, -22f),
            new Vector2(146f, -48f),
            new Vector2(184f, -12f),
            new Vector2(136f, 20f),
            new Vector2(74f, -22f)));
        beast.AddChild(CreateLine("BeastHornLeft", gold, 4f, new Vector2(128f, -48f), new Vector2(94f, -94f), new Vector2(154f, -66f)));
        beast.AddChild(CreateLine("BeastHornRight", gold, 4f, new Vector2(152f, -48f), new Vector2(202f, -92f), new Vector2(184f, -32f)));
        beast.AddChild(CreateLine("BeastClosedEye", new Color(1f, 0.88f, 0.42f, 0.90f), 4f, new Vector2(128f, -12f), new Vector2(166f, -12f)));

        for (var i = 0; i < 5; i++)
        {
            var rib = CreateArc($"BeastRib{i}", 18f, Mathf.Pi * 1.08f, Mathf.Pi * 0.84f, 2f, new Color(0.44f, 1f, 0.88f, 0.32f));
            rib.Position = new Vector2(-96f + i * 44f, -2f);
            rib.Scale = new Vector2(1f, 1.45f);
            beast.AddChild(rib);
        }

        BuildSleepGlyphs(beast);
        return beast;
    }

    private static void BuildSleepGlyphs(Node2D beast)
    {
        for (var i = 0; i < 3; i++)
        {
            var z = new Node2D
            {
                Name = $"SleepZ{i}",
                Position = new Vector2(150f + i * 26f, -92f - i * 26f),
                Rotation = -0.18f + i * 0.10f,
                Scale = new Vector2(0.62f + i * 0.15f, 0.62f + i * 0.15f),
                ZIndex = 905
            };

            var color = i == 1
                ? new Color(1f, 0.78f, 0.30f, 0.72f)
                : new Color(0.42f, 1f, 0.88f, 0.70f);
            z.AddChild(CreateLine("ZGlyph", color, 5f, new Vector2(-18f, -12f), new Vector2(18f, -12f), new Vector2(-18f, 12f), new Vector2(18f, 12f)));
            z.AddChild(CreateLine("ZTickA", new Color(0.42f, 1f, 0.88f, 0.34f), 2f, new Vector2(-26f, -12f), new Vector2(-20f, -12f)));
            z.AddChild(CreateLine("ZTickB", new Color(1f, 0.78f, 0.30f, 0.32f), 2f, new Vector2(20f, 12f), new Vector2(28f, 12f)));
            beast.AddChild(z);
        }

        for (var i = 0; i < 6; i++)
        {
            var mote = CreateArc($"SleepMote{i}", 2.4f + i % 2, 0f, Mathf.Tau, 2f, new Color(0.42f, 1f, 0.88f, 0.34f));
            mote.Position = new Vector2(122f + i * 17f, -76f - i * 9f);
            mote.ZIndex = 904;
            beast.AddChild(mote);
        }
    }

    private static void BuildShield(Node2D root, Vector2 selfOffset)
    {
        var shield = new Node2D
        {
            Name = "ShieldRoot",
            Position = selfOffset + new Vector2(0f, -36f),
            Scale = new Vector2(0.16f, 0.16f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 904
        };

        var outer = BuildRegularPoints(Vector2.Zero, 92f, 6, -Mathf.Pi / 2f);
        var inner = BuildRegularPoints(Vector2.Zero, 66f, 6, -Mathf.Pi / 2f);
        shield.AddChild(new Polygon2D
        {
            Name = "ShieldFill",
            Polygon = outer,
            Color = new Color(0.16f, 0.58f, 0.54f, 0.24f),
            ZIndex = 901
        });
        shield.AddChild(CreateLine("ShieldOuter", new Color(0.42f, 1f, 0.88f, 0.92f), 5f, ClosePoints(outer)));
        shield.AddChild(CreateLine("ShieldGold", new Color(1f, 0.78f, 0.30f, 0.70f), 3f, ClosePoints(BuildRegularPoints(Vector2.Zero, 118f, 6, -Mathf.Pi / 2f))));
        shield.AddChild(CreateLine("ShieldInner", new Color(0.85f, 1f, 0.95f, 0.58f), 2f, ClosePoints(inner)));

        for (var i = 0; i < 6; i++)
        {
            var angle = Mathf.Tau * i / 6f - Mathf.Pi / 2f;
            var point = Unit(angle) * 92f;
            shield.AddChild(CreatePolygon($"ShieldFacet{i}", new Color(0.40f, 1f, 0.88f, 0.45f),
                point + Unit(angle + Mathf.Pi / 2f) * 13f,
                point + Unit(angle) * 24f + Unit(angle + Mathf.Pi / 2f) * 8f,
                point + Unit(angle) * 24f - Unit(angle + Mathf.Pi / 2f) * 8f,
                point - Unit(angle + Mathf.Pi / 2f) * 13f));
        }

        root.AddChild(shield);
    }

    private static void BuildTargetBranches(Node2D root, Vector2 selfOffset, Vector2[] targetOffsets)
    {
        if (targetOffsets.Length == 0)
        {
            return;
        }

        var source = selfOffset + new Vector2(72f, -40f);
        var manyTargets = targetOffsets.Length > 4;
        var branchWidth = manyTargets ? 3.0f : 4.6f;
        var branchAlpha = manyTargets ? 0.48f : 0.68f;

        for (var i = 0; i < targetOffsets.Length; i++)
        {
            var target = targetOffsets[i];
            var control = (source + target) * 0.5f + new Vector2(0f, -28f - i * 2f);
            var points = BuildQuadraticPoints(source, control, target, 28);

            var branch = new Node2D
            {
                Name = $"Branch{i}",
                Scale = new Vector2(0.05f, 0.05f),
                Modulate = new Color(1f, 1f, 1f, 0f),
                ZIndex = 906
            };
            branch.AddChild(new Line2D
            {
                Name = "BranchGlow",
                Points = points,
                Width = branchWidth * 3.4f,
                DefaultColor = new Color(0.32f, 1f, 0.88f, branchAlpha * 0.25f),
                Antialiased = true,
                ZIndex = 905
            });
            branch.AddChild(new Line2D
            {
                Name = "BranchCore",
                Points = points,
                Width = branchWidth,
                DefaultColor = new Color(0.58f, 1f, 0.92f, branchAlpha),
                Antialiased = true,
                ZIndex = 907
            });
            branch.AddChild(CreateArc("BranchHead", manyTargets ? 7f : 9f, 0f, Mathf.Tau, manyTargets ? 3f : 4f, new Color(0.96f, 1f, 0.90f, 0.86f)));
            branch.GetChild<Node2D>(2).Position = target;
            root.AddChild(branch);

            BuildTargetLock(root, target, i, manyTargets);
            BuildWeakMark(root, target, i, manyTargets);
        }
    }

    private static void BuildTargetLock(Node2D root, Vector2 target, int index, bool manyTargets)
    {
        var lockRoot = new Node2D
        {
            Name = $"TargetLock{index}",
            Position = target,
            Scale = new Vector2(0.18f, 0.18f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 907
        };

        var radius = manyTargets ? 32f : 42f;
        lockRoot.AddChild(CreateArc("LockCyan", radius, 0.12f, Mathf.Pi * 1.28f, 3f, new Color(0.54f, 1f, 0.92f, 0.78f)));
        lockRoot.AddChild(CreateArc("LockGold", radius * 0.72f, Mathf.Pi * 1.16f, Mathf.Pi * 0.82f, 2f, new Color(1f, 0.78f, 0.30f, 0.62f)));
        lockRoot.AddChild(CreateLine("LockSlash", new Color(0.92f, 0.95f, 0.88f, 0.48f), 2f, new Vector2(-radius * 0.58f, -radius * 0.58f), new Vector2(radius * 0.58f, radius * 0.58f)));
        root.AddChild(lockRoot);
    }

    private static void BuildWeakMark(Node2D root, Vector2 target, int index, bool manyTargets)
    {
        var mark = new Node2D
        {
            Name = $"WeakMark{index}",
            Position = target + new Vector2(0f, 24f),
            Scale = new Vector2(0.16f, 0.16f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 908
        };

        var height = manyTargets ? 42f : 56f;
        mark.AddChild(CreateLine("WeakDropA", new Color(0.72f, 0.76f, 0.72f, 0.55f), 3f, new Vector2(-18f, -height), new Vector2(18f, height * 0.2f)));
        mark.AddChild(CreateLine("WeakDropB", new Color(0.42f, 1f, 0.88f, 0.42f), 2f, new Vector2(12f, -height * 0.82f), new Vector2(-12f, height * 0.12f)));
        root.AddChild(mark);
    }

    private static void BuildFieldResidue(Node2D root, Vector2 selfOffset)
    {
        var residue = new Node2D
        {
            Name = "ResidueRoot",
            Position = selfOffset + new Vector2(0f, -36f),
            Scale = new Vector2(0.72f, 0.72f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 903
        };

        for (var i = 0; i < 4; i++)
        {
            var ring = CreateArc($"ResidueRing{i}", 40f + i * 18f, 0f, Mathf.Tau, 2f, new Color(0.34f, 0.96f, 0.86f, 0.42f - i * 0.06f));
            residue.AddChild(ring);
        }

        residue.AddChild(CreateArc("ResidueCore", 8f, 0f, Mathf.Tau, 6f, new Color(1f, 0.80f, 0.36f, 0.70f)));
        root.AddChild(residue);
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
            Points = BuildArcPoints(radius, start, sweep, 48),
            Width = width,
            DefaultColor = color,
            Antialiased = true,
            ZIndex = 901
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

    private static Vector2[] BuildQuadraticPoints(Vector2 p0, Vector2 p1, Vector2 p2, int segments)
    {
        var result = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var t = (float)i / segments;
            var oneMinusT = 1f - t;
            result[i] = oneMinusT * oneMinusT * p0 + 2f * oneMinusT * t * p1 + t * t * p2;
        }

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
