using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class DarkMagicianVfx
{
    private const double Duration = 1.18d;
    private const double BlockCueSeconds = 0.34d;
    private const double TimeoutSeconds = 1.45d;

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
            Name = "ThermalVortexDarkMagicianVfx",
            Layer = 131
        };

        var overlay = new ColorRect
        {
            Name = "DarkMagicianOverlay",
            Color = new Color(0.02f, 0.01f, 0.05f, 0f),
            Size = viewportSize,
            ZIndex = 890
        };

        var root = new Node2D
        {
            Name = "DarkMagicianRoot",
            Position = viewportSize * 0.5f + new Vector2(0f, -12f),
            ZIndex = 896
        };

        var shieldOffset = ResolveShieldScreenPosition(viewportSize, self) - root.Position;

        layer.AddChild(overlay);
        layer.AddChild(root);
        tree.Root.AddChild(layer);

        BuildMagicCircle(root);
        var mage = BuildMageSilhouette();
        root.AddChild(mage);
        BuildTransferRibbon(root, shieldOffset);
        var shield = BuildShield(shieldOffset);
        root.AddChild(shield);
        BuildOrbitParticles(root, shieldOffset);
        BuildShieldSparks(root, shieldOffset);

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

        tween.TweenProperty(overlay, "color", new Color(0.02f, 0.01f, 0.05f, 0.54f), 0.16d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(overlay, "color", new Color(0.02f, 0.01f, 0.05f, 0f), 0.28d)
            .SetDelay(0.88d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        AnimateChildren(tween, root, mage);

        var cueTimer = layer.GetTree().CreateTimer(BlockCueSeconds);
        cueTimer.Timeout += SignalBlockCue;

        var timeout = layer.GetTree().CreateTimer(TimeoutSeconds);
        timeout.Timeout += Complete;

        await blockCue.Task;
    }

    private static void AnimateChildren(Tween tween, Node2D root, Node2D mage)
    {
        tween.TweenProperty(mage, "modulate", new Color(1f, 1f, 1f, 1f), 0.22d)
            .SetDelay(0.18d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(mage, "scale", new Vector2(0.90f, 0.90f), 0.66d)
            .SetDelay(0.18d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(mage, "position", new Vector2(0f, -42f), 0.72d)
            .SetDelay(0.18d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(mage, "modulate", new Color(1f, 1f, 1f, 0f), 0.26d)
            .SetDelay(0.84d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        foreach (var child in root.GetChildren())
        {
            if (child is not Node2D node || node == mage)
            {
                continue;
            }

            var name = node.Name.ToString();
            if (name.StartsWith("Circle", StringComparison.Ordinal))
            {
                AnimateCircle(tween, node);
            }
            else if (name.StartsWith("TransferRibbon", StringComparison.Ordinal))
            {
                AnimateRibbon(tween, node);
            }
            else if (name.StartsWith("ShieldRoot", StringComparison.Ordinal))
            {
                AnimateShield(tween, node);
            }
            else if (name.StartsWith("OrbitParticle", StringComparison.Ordinal))
            {
                AnimateOrbitParticle(tween, node, ExtractIndex(name));
            }
            else if (name.StartsWith("ShieldSpark", StringComparison.Ordinal))
            {
                AnimateShieldSpark(tween, node, ExtractIndex(name));
            }
            else
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
                    .SetDelay(0.88d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
            }
        }
    }

    private static void AnimateCircle(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau * 0.78f, Duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "scale", node.Scale * 1.20f, Duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.34d)
            .SetDelay(0.82d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateRibbon(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.10d)
            .SetDelay(0.42d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.22d)
            .SetDelay(0.78d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateShield(Tween tween, Node2D node)
    {
        tween.TweenProperty(node, "scale", Vector2.One, 0.24d)
            .SetDelay(0.50d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.08d)
            .SetDelay(0.50d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "scale", new Vector2(1.08f, 1.08f), 0.34d)
            .SetDelay(0.74d)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
            .SetDelay(1.02d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateOrbitParticle(Tween tween, Node2D node, int index)
    {
        var delay = 0.18d + index % 23 * 0.018d;
        var start = node.Position;
        var end = start * (0.58f + index % 5 * 0.025f);

        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.06d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "position", end, 0.56d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau * 1.4f, 0.72d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.22d)
            .SetDelay(delay + 0.54d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateShieldSpark(Tween tween, Node2D node, int index)
    {
        var delay = 0.62d + index % 12 * 0.014d;

        tween.TweenProperty(node, "scale", Vector2.One, 0.24d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.05d)
            .SetDelay(delay)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.22d)
            .SetDelay(delay + 0.18d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static Vector2 ResolveShieldScreenPosition(Vector2 viewportSize, Creature self)
    {
        return self is not null && self.IsEnemy
            ? new Vector2(viewportSize.X * 0.72f, viewportSize.Y * 0.46f)
            : new Vector2(viewportSize.X * 0.25f, viewportSize.Y * 0.63f);
    }

    private static void BuildMagicCircle(Node2D root)
    {
        var violet = new Color(0.64f, 0.34f, 1f, 0.86f);
        var gold = new Color(0.95f, 0.78f, 0.36f, 0.82f);
        var pale = new Color(0.88f, 0.86f, 1f, 0.74f);

        root.AddChild(CreateArc("CircleOuter", 172f, 0f, Mathf.Tau * 0.90f, 8f, violet));
        root.AddChild(CreateArc("CircleMiddle", 124f, Mathf.Tau * 0.16f, Mathf.Tau * 0.72f, 6f, gold));
        root.AddChild(CreateArc("CircleInner", 76f, Mathf.Tau * 0.28f, Mathf.Tau * 0.86f, 4f, pale));

        var star = new Line2D
        {
            Name = "CircleStar",
            Points = BuildStarPoints(Vector2.Zero, 108f, 42f, 5, -Mathf.Pi / 2f),
            Width = 3f,
            DefaultColor = gold,
            Antialiased = true,
            ZIndex = 898
        };
        root.AddChild(star);

        for (var i = 0; i < 10; i++)
        {
            var angle = Mathf.Tau * i / 10f;
            root.AddChild(new Line2D
            {
                Name = $"CircleTick{i}",
                Points = new[]
                {
                    Unit(angle) * 138f,
                    Unit(angle) * 168f
                },
                Width = 4f,
                DefaultColor = i % 2 == 0 ? gold : pale,
                Antialiased = true,
                ZIndex = 898
            });
        }
    }

    private static Node2D BuildMageSilhouette()
    {
        var mage = new Node2D
        {
            Name = "MageSilhouette",
            Scale = new Vector2(0.84f, 0.84f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 900
        };

        var robe = new Color(0.50f, 0.28f, 0.82f, 0.70f);
        var deep = new Color(0.18f, 0.10f, 0.36f, 0.88f);
        var gold = new Color(0.95f, 0.78f, 0.36f, 0.82f);
        var pale = new Color(0.90f, 0.86f, 1f, 0.88f);

        mage.AddChild(new Polygon2D
        {
            Name = "MageRobe",
            Polygon = new[]
            {
                new Vector2(-70f, 18f),
                new Vector2(-28f, -78f),
                new Vector2(42f, -92f),
                new Vector2(86f, 12f),
                new Vector2(62f, 112f),
                new Vector2(-52f, 118f)
            },
            Color = robe,
            ZIndex = 900
        });

        mage.AddChild(new Polygon2D
        {
            Name = "MageHat",
            Polygon = new[]
            {
                new Vector2(-90f, -88f),
                new Vector2(-18f, -148f),
                new Vector2(76f, -132f),
                new Vector2(116f, -82f),
                new Vector2(36f, -98f)
            },
            Color = deep,
            ZIndex = 901
        });

        mage.AddChild(new Line2D
        {
            Name = "MageHatTrim",
            Points = new[]
            {
                new Vector2(-16f, -122f),
                new Vector2(56f, -112f),
                new Vector2(94f, -88f)
            },
            Width = 5f,
            DefaultColor = gold,
            Antialiased = true,
            ZIndex = 902
        });

        var faceGlow = CreateArc("MageFaceGlow", 31f, 0f, Mathf.Tau, 8f, pale);
        faceGlow.Position = new Vector2(12f, -60f);
        faceGlow.ZIndex = 902;
        mage.AddChild(faceGlow);

        mage.AddChild(new Line2D
        {
            Name = "MageStaff",
            Points = new[]
            {
                new Vector2(58f, -20f),
                new Vector2(128f, -100f)
            },
            Width = 7f,
            DefaultColor = gold,
            Antialiased = true,
            ZIndex = 903
        });

        var staffOrb = CreateArc("MageStaffOrb", 15f, 0f, Mathf.Tau, 12f, new Color(0.74f, 0.44f, 1f, 0.90f));
        staffOrb.Position = new Vector2(132f, -104f);
        staffOrb.ZIndex = 904;
        mage.AddChild(staffOrb);

        mage.AddChild(new Line2D
        {
            Name = "MageLeftArm",
            Points = new[]
            {
                new Vector2(-60f, 40f),
                new Vector2(-116f, 78f)
            },
            Width = 7f,
            DefaultColor = pale,
            Antialiased = true,
            ZIndex = 903
        });

        mage.AddChild(new Line2D
        {
            Name = "MageRightArm",
            Points = new[]
            {
                new Vector2(54f, 40f),
                new Vector2(104f, 70f)
            },
            Width = 7f,
            DefaultColor = pale,
            Antialiased = true,
            ZIndex = 903
        });

        return mage;
    }

    private static void BuildTransferRibbon(Node2D root, Vector2 shieldOffset)
    {
        var points = BuildBezierPoints(
            new Vector2(-18f, 20f),
            new Vector2(-86f, 76f),
            shieldOffset + new Vector2(52f, -62f),
            shieldOffset,
            42);

        root.AddChild(new Line2D
        {
            Name = "TransferRibbonGlow",
            Points = points,
            Width = 26f,
            DefaultColor = new Color(0.65f, 0.29f, 1f, 0.52f),
            Antialiased = true,
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 899
        });

        root.AddChild(new Line2D
        {
            Name = "TransferRibbonCore",
            Points = points,
            Width = 7f,
            DefaultColor = new Color(0.96f, 0.84f, 0.42f, 0.74f),
            Antialiased = true,
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 903
        });
    }

    private static Node2D BuildShield(Vector2 shieldOffset)
    {
        var shield = new Node2D
        {
            Name = "ShieldRoot",
            Position = shieldOffset,
            Scale = new Vector2(0.12f, 0.12f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 902
        };

        var outline = BuildShieldPoints(156f, 198f);
        var inner = BuildShieldPoints(112f, 146f);

        shield.AddChild(new Polygon2D
        {
            Name = "ShieldFill",
            Polygon = outline,
            Color = new Color(0.31f, 0.16f, 0.70f, 0.34f),
            ZIndex = 900
        });

        shield.AddChild(new Line2D
        {
            Name = "ShieldOutline",
            Points = ClosePoints(outline),
            Width = 6f,
            DefaultColor = new Color(0.96f, 0.82f, 0.36f, 0.92f),
            Antialiased = true,
            ZIndex = 902
        });

        shield.AddChild(new Line2D
        {
            Name = "ShieldInner",
            Points = ClosePoints(inner),
            Width = 3f,
            DefaultColor = new Color(0.88f, 0.84f, 1f, 0.70f),
            Antialiased = true,
            ZIndex = 903
        });

        var shieldAura = CreateArc("ShieldAura", 72f, 0f, Mathf.Tau, 3f, new Color(0.68f, 0.36f, 1f, 0.70f));
        shieldAura.Scale = new Vector2(1f, 1.24f);
        shieldAura.ZIndex = 901;
        shield.AddChild(shieldAura);

        shield.AddChild(new Line2D
        {
            Name = "ShieldStar",
            Points = BuildStarPoints(Vector2.Zero, 56f, 23f, 6, -Mathf.Pi / 2f),
            Width = 3f,
            DefaultColor = new Color(0.95f, 0.78f, 0.36f, 0.70f),
            Antialiased = true,
            ZIndex = 904
        });

        return shield;
    }

    private static void BuildOrbitParticles(Node2D root, Vector2 shieldOffset)
    {
        var colors = new[]
        {
            new Color(0.92f, 0.86f, 1f, 0.88f),
            new Color(0.72f, 0.42f, 1f, 0.82f),
            new Color(0.96f, 0.80f, 0.42f, 0.78f)
        };

        for (var i = 0; i < 58; i++)
        {
            var angle = Mathf.Tau * i / 58f + Noise(i, 0.29f) * 0.16f;
            var radius = 58f + Noise(i, 0.73f) * 98f;
            root.AddChild(new Polygon2D
            {
                Name = $"OrbitParticle{i}",
                Position = shieldOffset + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 1.18f),
                Rotation = angle,
                Polygon = BuildDiamondPoints(3.5f + i % 4),
                Color = colors[i % colors.Length],
                Modulate = new Color(1f, 1f, 1f, 0f),
                ZIndex = 905
            });
        }
    }

    private static void BuildShieldSparks(Node2D root, Vector2 shieldOffset)
    {
        for (var i = 0; i < 46; i++)
        {
            var angle = Mathf.Tau * i / 46f + Noise(i, 1.41f) * 0.18f;
            var direction = Unit(angle);
            var length = 18f + Noise(i, 2.17f) * 62f;

            root.AddChild(new Line2D
            {
                Name = $"ShieldSpark{i}",
                Position = shieldOffset,
                Points = new[]
                {
                    new Vector2(direction.X * 48f, direction.Y * 60f),
                    new Vector2(direction.X * (48f + length), direction.Y * (60f + length))
                },
                Width = 2f + i % 3,
                DefaultColor = i % 2 == 0
                    ? new Color(0.96f, 0.82f, 0.36f, 0.82f)
                    : new Color(0.82f, 0.62f, 1f, 0.78f),
                Antialiased = true,
                Modulate = new Color(1f, 1f, 1f, 0f),
                Scale = new Vector2(0.12f, 0.12f),
                ZIndex = 906
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

    private static Vector2[] BuildStarPoints(Vector2 center, float outer, float inner, int points, float rotation)
    {
        var result = new Vector2[points * 2 + 1];
        for (var i = 0; i < points * 2; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = rotation + Mathf.Pi * i / points;
            result[i] = center + Unit(angle) * radius;
        }

        result[^1] = result[0];
        return result;
    }

    private static Vector2[] BuildShieldPoints(float width, float height)
    {
        return new[]
        {
            new Vector2(0f, -height * 0.58f),
            new Vector2(width * 0.46f, -height * 0.30f),
            new Vector2(width * 0.38f, height * 0.28f),
            new Vector2(0f, height * 0.58f),
            new Vector2(-width * 0.38f, height * 0.28f),
            new Vector2(-width * 0.46f, -height * 0.30f)
        };
    }

    private static Vector2[] BuildDiamondPoints(float size)
    {
        return new[]
        {
            new Vector2(0f, -size * 2f),
            new Vector2(size, 0f),
            new Vector2(0f, size * 2f),
            new Vector2(-size, 0f)
        };
    }

    private static Vector2[] ClosePoints(Vector2[] points)
    {
        var result = new Vector2[points.Length + 1];
        Array.Copy(points, result, points.Length);
        result[^1] = points[0];
        return result;
    }

    private static Vector2[] BuildBezierPoints(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int segments)
    {
        var result = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var t = (float)i / segments;
            var oneMinusT = 1f - t;
            result[i] =
                oneMinusT * oneMinusT * oneMinusT * p0 +
                3f * oneMinusT * oneMinusT * t * p1 +
                3f * oneMinusT * t * t * p2 +
                t * t * t * p3;
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
