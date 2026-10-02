using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class AshBlossomVfx
{
    private const int FrameCount = 96;
    private const int PreloadFramesPerTick = 1;
    private const double FrameSeconds = 1d / 24d;
    private const double TimeoutSeconds = 4.65d;
    private const string FramePathPrefix = MainFile.ResPath + "/images/vfx/ash_blossom/frame_";
    private const string PowerIconPath = MainFile.ResPath + "/images/powers/ash_blossom.png";
    private const string BigPowerIconPath = MainFile.ResPath + "/images/powers/big/ash_blossom.png";
    private const float SpriteScale = 0.15f;
    private const float SequenceAlpha = 0.68f;
    private const float WardOpacity = 0.78f;
    private const int WardZ = 120;
    private const int SpriteZ = 180;
    private const float FallbackShieldRadiusX = 112f;
    private const float FallbackShieldRadiusY = 180f;
    private const float ShieldTopY = 0f;
    private const float BoundsHorizontalPadding = 20f;
    private const float BoundsTopPadding = 18f;
    private const float BoundsBottomPadding = 18f;
    private const float TextureCenterX = 360f;
    private const float TextureCenterY = 640f;
    private const float HolyWaterEndFrameX = 366f;
    private const float HolyWaterEndFrameY = 1256f;
    private static readonly Vector2 EffectScreenOffset = new(24f, 22f);
    private static readonly Vector2 WaterEndOffset = new(
        (HolyWaterEndFrameX - TextureCenterX) * SpriteScale,
        (HolyWaterEndFrameY - TextureCenterY) * SpriteScale);
    private static Texture2D[] _cachedFrames;
    private static Texture2D _cachedPowerIcon;
    private static Texture2D _cachedBigPowerIcon;
    private static Task _preloadTask;
    private static int _preloadedFrameCount;
    private static bool _loggedPreloadComplete;

    private readonly struct VfxLayout
    {
        public VfxLayout(Vector2 ashPosition, Vector2 wardTopPosition, float shieldRadiusX, float shieldRadiusY, string source)
        {
            AshPosition = ashPosition;
            WardTopPosition = wardTopPosition;
            ShieldRadiusX = shieldRadiusX;
            ShieldRadiusY = shieldRadiusY;
            Source = source;
        }

        public Vector2 AshPosition { get; }
        public Vector2 WardTopPosition { get; }
        public float ShieldRadiusX { get; }
        public float ShieldRadiusY { get; }
        public string Source { get; }
    }

    public static void StartPreload()
    {
        if (_cachedFrames is not null || _preloadTask is { IsCompleted: false })
        {
            return;
        }

        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null)
        {
            return;
        }

        _preloadTask = PreloadFramesAsync(tree);
    }

    public static void Play(Creature self)
    {
        StartPreload();
        _ = PlaySafeAsync(self);
    }

    private static async Task PlaySafeAsync(Creature self)
    {
        try
        {
            await PlayAsync(self);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Ash Blossom VFX failed: " + ex);
        }
    }

    private static async Task PlayAsync(Creature self)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null)
        {
            return;
        }

        var textures = await GetFramesForPlaybackAsync(tree);
        if (textures is null)
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
            Name = "ThermalVortexAshBlossomVfx",
            Layer = 130
        };

        var layout = ResolveLayout(tree, viewportSize, self);
        MainFile.Logger.Info(
            $"[ThermalVortex] AshBlossomVfx layout source={layout.Source} ash={Format(layout.AshPosition)} wardTop={Format(layout.WardTopPosition)} shield=({layout.ShieldRadiusX:0.0},{layout.ShieldRadiusY:0.0})");

        var root = new Node2D
        {
            Name = "AshBlossomRoot",
            Position = layout.AshPosition,
            ZIndex = SpriteZ
        };

        var sprite = new Sprite2D
        {
            Name = "AshBlossomSequence",
            Texture = textures[0],
            Centered = true,
            Scale = Vector2.One * SpriteScale,
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = SpriteZ + 1
        };

        var ward = BuildWard(layout.WardTopPosition, layout.ShieldRadiusX, layout.ShieldRadiusY);

        root.AddChild(sprite);
        layer.AddChild(ward);
        layer.AddChild(root);
        tree.Root.AddChild(layer);

        AnimateWard(ward);
        var completed = false;

        void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;

            if (GodotObject.IsInstanceValid(layer))
            {
                layer.QueueFree();
            }
        }

        _ = PlaySequenceAsync(layer, sprite, textures, Complete);

        var timeout = layer.GetTree().CreateTimer(TimeoutSeconds);
        timeout.Timeout += Complete;
    }

    private static async Task<Texture2D[]> GetFramesForPlaybackAsync(SceneTree tree)
    {
        if (_cachedFrames is not null)
        {
            return _cachedFrames;
        }

        StartPreload();
        var preloadTask = _preloadTask;
        if (preloadTask is not null)
        {
            await preloadTask;
        }

        return _cachedFrames;
    }

    private static async Task PreloadFramesAsync(SceneTree tree)
    {
        try
        {
            await WaitForNextTickAsync(tree);
            PreloadPowerIcons();

            var frames = new Texture2D[FrameCount];
            for (var i = 0; i < frames.Length; i++)
            {
                frames[i] = LoadFrame(i);
                if (frames[i] is null)
                {
                    return;
                }

                _preloadedFrameCount = i + 1;
                if ((i + 1) % PreloadFramesPerTick == 0)
                {
                    await WaitForNextTickAsync(tree);
                }
            }

            _cachedFrames = frames;
            if (!_loggedPreloadComplete)
            {
                MainFile.Logger.Info($"Ash Blossom VFX preloaded {FrameCount} frames.");
                _loggedPreloadComplete = true;
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Ash Blossom VFX preload failed after {_preloadedFrameCount}/{FrameCount} frames: {ex}");
        }
    }

    private static Texture2D LoadFrame(int index)
    {
        var path = $"{FramePathPrefix}{index:000}.png";
        if (!ResourceLoader.Exists(path))
        {
            MainFile.Logger.Info("Could not find Ash Blossom VFX frame: " + path);
            return null;
        }

        var texture = ResourceLoader.Load<Texture2D>(path);
        if (texture is null)
        {
            MainFile.Logger.Info("Could not load Ash Blossom VFX frame: " + path);
        }

        return texture;
    }

    private static void PreloadPowerIcons()
    {
        _cachedPowerIcon ??= LoadOptionalTexture(PowerIconPath);
        _cachedBigPowerIcon ??= LoadOptionalTexture(BigPowerIconPath);
    }

    private static Texture2D LoadOptionalTexture(string path)
    {
        if (!ResourceLoader.Exists(path))
        {
            return null;
        }

        return ResourceLoader.Load<Texture2D>(path);
    }

    private static VfxLayout ResolveLayout(SceneTree tree, Vector2 viewportSize, Creature self)
    {
        if (TryResolvePlayerBoundsLayout(self, out var layout))
        {
            return layout;
        }

        var ashPosition = ResolveFallbackHeadPosition(viewportSize, self);
        return new VfxLayout(
            ashPosition,
            ashPosition + WaterEndOffset,
            FallbackShieldRadiusX,
            FallbackShieldRadiusY,
            "fallback");
    }

    private static bool TryResolvePlayerBoundsLayout(Creature self, out VfxLayout layout)
    {
        layout = default;

        var visuals = self?.GetCreatureNode()?.Visuals;
        if (visuals is null || !GodotObject.IsInstanceValid(visuals))
            return false;

        var bounds = visuals?.FindChild("Bounds", true, false) as Control;
        if (bounds is null)
        {
            return false;
        }

        var rect = bounds.GetGlobalRect();
        if (rect.Size == Vector2.Zero)
        {
            return false;
        }

        var top = rect.Position.Y - BoundsTopPadding;
        var bottom = rect.Position.Y + rect.Size.Y + BoundsBottomPadding;
        var centerX = rect.Position.X + rect.Size.X * 0.5f;
        var wardTopPosition = new Vector2(centerX, top);
        var shieldRadiusX = Mathf.Max(FallbackShieldRadiusX, rect.Size.X * 0.5f + BoundsHorizontalPadding);
        var shieldRadiusY = Mathf.Max(FallbackShieldRadiusY, (bottom - top) * 0.5f);

        layout = new VfxLayout(
            wardTopPosition - WaterEndOffset,
            wardTopPosition,
            shieldRadiusX,
            shieldRadiusY,
            $"bounds rect=({rect.Position.X:0.0},{rect.Position.Y:0.0},{rect.Size.X:0.0},{rect.Size.Y:0.0})");
        return true;
    }

    private static Vector2 ResolveFallbackHeadPosition(Vector2 viewportSize, Creature self)
    {
        var basePosition = self is not null && self.IsEnemy
            ? new Vector2(viewportSize.X * 0.74f, viewportSize.Y * 0.20f)
            : new Vector2(viewportSize.X * 0.25f, viewportSize.Y * 0.18f);

        return basePosition + EffectScreenOffset;
    }

    private static Node2D BuildWard(Vector2 position, float shieldRadiusX, float shieldRadiusY)
    {
        var ward = new Node2D
        {
            Name = "AshBlossomWard",
            Position = position,
            Modulate = new Color(1f, 1f, 1f, 1f),
            ZIndex = WardZ
        };

        var streamStart = new Vector2(18f, -42f);
        var streamControlA = new Vector2(-18f, -30f);
        var streamControlB = new Vector2(48f, -18f);
        var streamEnd = new Vector2(0f, ShieldTopY);

        for (var i = 0; i < 6; i++)
        {
            var start = i / 6f;
            var end = (i + 1.5f) / 6f;
            ward.AddChild(new Line2D
            {
                Name = $"WardStreamSegment{i}",
                Points = BuildBezierSegmentPoints(streamStart, streamControlA, streamControlB, streamEnd, start, Mathf.Min(end, 1f), 10),
                Width = 3.0f - i * 0.10f,
                DefaultColor = new Color(0.58f, 0.88f, 1f, 0.50f),
                Antialiased = true,
                Modulate = new Color(1f, 1f, 1f, 0f),
                ZIndex = WardZ + 3
            });
        }

        ward.AddChild(new Line2D
        {
            Name = "WardWaterPool",
            Points = BuildEllipsePoints(24f, 8f, 36),
            Position = streamEnd,
            Width = 2.8f,
            DefaultColor = new Color(0.72f, 0.94f, 1f, 0.48f),
            Antialiased = true,
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = WardZ + 4
        });

        for (var i = 0; i < 7; i++)
        {
            var t = i / 6f;
            var point = Bezier(streamStart, streamControlA, streamControlB, streamEnd, t);

            ward.AddChild(new Polygon2D
            {
                Name = $"WardDrop{i}",
                Polygon = BuildDropletPoints(2.7f + i % 3),
                Position = point,
                Rotation = -0.25f + t * 0.50f,
                Color = new Color(0.70f, 0.94f, 1f, 0.54f),
                Modulate = new Color(1f, 1f, 1f, 0f),
                ZIndex = WardZ + 4
            });
        }

        var shield = new Node2D
        {
            Name = "WardShield",
            Position = new Vector2(0f, shieldRadiusY),
            Scale = new Vector2(0.88f, 0.86f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = WardZ + 2
        };

        shield.AddChild(new Polygon2D
        {
            Name = "ShieldFill",
            Polygon = BuildEllipsePoints(shieldRadiusX, shieldRadiusY, 96),
            Color = new Color(0.56f, 0.86f, 1f, 0.044f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = WardZ + 2
        });

        shield.AddChild(new Line2D
        {
            Name = "ShieldFlowArcRightTop",
            Points = BuildEllipseArcPoints(shieldRadiusX, shieldRadiusY, -Mathf.Pi / 2f, Mathf.Pi * 0.58f, 34),
            Width = 4.2f,
            DefaultColor = new Color(0.58f, 0.90f, 1f, 0.44f),
            Antialiased = true,
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = WardZ + 4
        });

        shield.AddChild(new Line2D
        {
            Name = "ShieldFlowArcLeftTop",
            Points = BuildEllipseArcPoints(shieldRadiusX, shieldRadiusY, -Mathf.Pi / 2f, -Mathf.Pi * 0.58f, 34),
            Width = 4.2f,
            DefaultColor = new Color(0.58f, 0.90f, 1f, 0.44f),
            Antialiased = true,
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = WardZ + 4
        });

        shield.AddChild(new Line2D
        {
            Name = "ShieldFlowArcRightBottom",
            Points = BuildEllipseArcPoints(shieldRadiusX, shieldRadiusY, Mathf.Pi * 0.08f, Mathf.Pi * 0.42f, 30),
            Width = 3.8f,
            DefaultColor = new Color(0.66f, 0.94f, 1f, 0.36f),
            Antialiased = true,
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = WardZ + 3
        });

        shield.AddChild(new Line2D
        {
            Name = "ShieldFlowArcLeftBottom",
            Points = BuildEllipseArcPoints(shieldRadiusX, shieldRadiusY, Mathf.Pi * 0.92f, -Mathf.Pi * 0.42f, 30),
            Width = 3.8f,
            DefaultColor = new Color(0.66f, 0.94f, 1f, 0.36f),
            Antialiased = true,
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = WardZ + 3
        });

        ward.AddChild(shield);

        return ward;
    }

    private static void AnimateWard(Node2D ward)
    {
        var tween = ward.CreateTween();
        tween.SetParallel(true);

        var dropIndex = 0;
        var streamIndex = 0;
        foreach (var child in ward.GetChildren())
        {
            if (child is not Node2D node)
            {
                continue;
            }

            var name = node.Name.ToString();
            if (name.StartsWith("WardStreamSegment", StringComparison.Ordinal))
            {
                var delay = 1.24d + streamIndex * 0.085d;
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, WardOpacity), 0.09d)
                    .SetDelay(delay)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.36d)
                    .SetDelay(delay + 0.34d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
                streamIndex++;
            }
            else if (name == "WardWaterPool")
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, WardOpacity), 0.14d)
                    .SetDelay(1.64d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "scale", new Vector2(1.55f, 1.25f), 0.30d)
                    .SetDelay(1.66d)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.34d)
                    .SetDelay(2.18d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
            }
            else if (name.StartsWith("WardDrop", StringComparison.Ordinal))
            {
                var delay = 1.30d + dropIndex * 0.055d;
                var drift = new Vector2(-node.Position.X * 0.12f, 18f + dropIndex % 4 * 2f);
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, WardOpacity), 0.12d)
                    .SetDelay(delay)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "position", node.Position + drift, 0.72d)
                    .SetDelay(delay)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.20d)
                    .SetDelay(delay + 0.62d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
                dropIndex++;
            }
            else if (name == "WardShield")
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, WardOpacity), 0.28d)
                    .SetDelay(1.70d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "scale", Vector2.One, 0.36d)
                    .SetDelay(1.70d)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "scale", new Vector2(1.06f, 1.03f), 1.10d)
                    .SetDelay(2.24d)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.InOut);
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.72d)
                    .SetDelay(3.46d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);

                AnimateShieldChildren(tween, node);
            }
        }
    }

    private static void AnimateShieldChildren(Tween tween, Node2D shield)
    {
        var arcIndex = 0;
        foreach (var child in shield.GetChildren())
        {
            if (child is not Node2D node)
            {
                continue;
            }

            var name = node.Name.ToString();
            if (name.StartsWith("ShieldFlowArc", StringComparison.Ordinal))
            {
                var delay = 1.76d + arcIndex * 0.14d;
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.18d)
                    .SetDelay(delay)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "position", node.Position + new Vector2(0f, arcIndex < 2 ? 2f : -2f), 0.42d)
                    .SetDelay(delay)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.InOut);
                arcIndex++;
            }
            else if (name == "ShieldFill")
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 1f), 0.42d)
                    .SetDelay(2.22d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
            }
        }
    }

    private static async Task PlaySequenceAsync(CanvasLayer layer, Sprite2D sprite, Texture2D[] textures, Action complete)
    {
        try
        {
            Fade(sprite, SequenceAlpha, 0.12d);
            await DelayAsync(layer, 0.12d);

            for (var i = 0; i < textures.Length; i++)
            {
                if (!GodotObject.IsInstanceValid(layer) || !GodotObject.IsInstanceValid(sprite))
                {
                    return;
                }

                sprite.Texture = textures[i];
                await DelayAsync(layer, FrameSeconds);
            }

            if (GodotObject.IsInstanceValid(sprite))
            {
                Fade(sprite, 0f, 0.20d);
                await DelayAsync(layer, 0.22d);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Ash Blossom VFX sequence failed: " + ex);
        }
        finally
        {
            complete();
        }
    }

    private static void Fade(CanvasItem item, float alpha, double duration)
    {
        if (!GodotObject.IsInstanceValid(item))
        {
            return;
        }

        var target = new Color(item.Modulate.R, item.Modulate.G, item.Modulate.B, alpha);
        item.CreateTween()
            .TweenProperty(item, "modulate", target, duration)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(alpha > item.Modulate.A ? Tween.EaseType.Out : Tween.EaseType.In);
    }

    private static async Task DelayAsync(Node node, double seconds)
    {
        if (!GodotObject.IsInstanceValid(node))
        {
            return;
        }

        var tree = node.GetTree();
        if (tree is null)
        {
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = tree.CreateTimer(seconds);
        timer.Timeout += () => done.TrySetResult();
        await done.Task;
    }

    private static async Task WaitForNextTickAsync(SceneTree tree)
    {
        if (tree?.Root is null)
        {
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = tree.CreateTimer(0.001d);
        timer.Timeout += () => done.TrySetResult();
        await done.Task;
    }

    private static string Format(Vector2 value)
    {
        return $"({value.X:0.0},{value.Y:0.0})";
    }

    private static Vector2[] BuildBezierPoints(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int segments)
    {
        var result = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            result[i] = Bezier(p0, p1, p2, p3, (float)i / segments);
        }

        return result;
    }

    private static Vector2[] BuildBezierSegmentPoints(
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        Vector2 p3,
        float start,
        float end,
        int segments)
    {
        var result = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var t = Mathf.Lerp(start, end, (float)i / segments);
            result[i] = Bezier(p0, p1, p2, p3, t);
        }

        return result;
    }

    private static Vector2 Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var oneMinusT = 1f - t;
        return
            oneMinusT * oneMinusT * oneMinusT * p0 +
            3f * oneMinusT * oneMinusT * t * p1 +
            3f * oneMinusT * t * t * p2 +
            t * t * t * p3;
    }

    private static Vector2[] BuildEllipsePoints(float radiusX, float radiusY, int segments)
    {
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = Mathf.Tau * i / segments;
            points[i] = new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
        }

        return points;
    }

    private static Vector2[] BuildEllipseArcPoints(float radiusX, float radiusY, float start, float sweep, int segments)
    {
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = start + sweep * i / segments;
            points[i] = new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
        }

        return points;
    }

    private static Vector2[] BuildDropletPoints(float size)
    {
        return new[]
        {
            new Vector2(0f, -size * 1.35f),
            new Vector2(size * 0.64f, -size * 0.25f),
            new Vector2(size * 0.42f, size * 0.58f),
            new Vector2(0f, size),
            new Vector2(-size * 0.42f, size * 0.58f),
            new Vector2(-size * 0.64f, -size * 0.25f)
        };
    }
}
