using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class DetonationVfx
{
    private const int FrameCount = 36;
    private const int ImpactFrame = 30;
    private const int FramesPerLoadTick = 4;
    private const double FrameSeconds = 1d / 36d;
    private const double PlaybackDurationSeconds = FrameCount * FrameSeconds;
    private const double ImpactCueTimeoutSeconds = 1.20d;
    private const double LifecycleSeconds = 2.05d;
    private const double CleanupTimeoutSeconds = 2.25d;
    private const float ReferenceViewportHeight = 1080f;
    private const float MinimumScale = 0.75f;
    private const float MaximumScale = 1.25f;
    private const float CenterSafePadding = 48f;
    private const string FramePathPrefix = MainFile.ResPath + "/images/vfx/detonation/frame_";

    private static Texture2D[] _cachedFrames;
    private static Task<Texture2D[]> _loadTask;

    public static async Task PlayAsync(Creature target)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null)
        {
            return;
        }

        var visibleRect = tree.Root.GetVisibleRect();
        if (visibleRect.Size == Vector2.Zero)
        {
            return;
        }

        var frames = await GetFramesForPlaybackAsync(tree);
        if (frames is null)
        {
            return;
        }

        var scale = Mathf.Clamp(
            visibleRect.Size.Y / ReferenceViewportHeight,
            MinimumScale,
            MaximumScale);
        var targetPosition = ClampToVisibleRect(
            ResolveTargetPosition(target, visibleRect),
            visibleRect,
            scale);

        var layer = new CanvasLayer
        {
            Name = "ThermalVortexDetonationVfx",
            Layer = 134
        };

        var overlay = new ColorRect
        {
            Name = "DetonationOverlay",
            Position = visibleRect.Position,
            Size = visibleRect.Size,
            Color = new Color(0.035f, 0.012f, 0.008f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 890
        };

        var impactRoot = new Node2D
        {
            Name = "DetonationImpactRoot",
            Position = targetPosition,
            Scale = Vector2.One * scale,
            ZIndex = 0
        };

        var sprite = new Sprite2D
        {
            Name = "DetonationSequence",
            Texture = frames[0],
            Centered = true,
            Position = targetPosition,
            Scale = Vector2.One * scale,
            ZIndex = 900
        };

        var flash = new ColorRect
        {
            Name = "DetonationFlash",
            Position = visibleRect.Position,
            Size = visibleRect.Size,
            Color = new Color(1f, 0.79f, 0.38f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 910
        };

        ImpactVisuals visuals;
        try
        {
            visuals = BuildImpactVisuals(impactRoot);
            layer.AddChild(overlay);
            layer.AddChild(impactRoot);
            layer.AddChild(sprite);
            layer.AddChild(flash);
            tree.Root.AddChild(layer);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Detonation VFX setup failed: " + ex);
            if (GodotObject.IsInstanceValid(layer))
            {
                layer.QueueFree();
            }

            return;
        }

        var impactCue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cueSent = false;
        var impactStarted = false;
        var completed = false;

        void SignalImpactCue()
        {
            if (cueSent)
            {
                return;
            }

            cueSent = true;
            impactCue.TrySetResult();
        }

        void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            SignalImpactCue();

            if (GodotObject.IsInstanceValid(layer))
            {
                layer.QueueFree();
            }
        }

        void StartImpact()
        {
            if (impactStarted || completed)
            {
                return;
            }

            impactStarted = true;
            try
            {
                AnimateImpact(layer, overlay, flash, sprite, visuals);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info("Detonation VFX impact animation failed: " + ex);
                Complete();
            }
        }

        try
        {
            AnimateAnticipation(layer, overlay);

            var lifecycleTween = layer.CreateTween();
            lifecycleTween.TweenInterval(LifecycleSeconds);
            lifecycleTween.Finished += Complete;

            _ = PlaySequenceAsync(
                layer,
                sprite,
                frames,
                StartImpact,
                SignalImpactCue,
                Complete);

            var cueTimeout = tree.CreateTimer(ImpactCueTimeoutSeconds);
            cueTimeout.Timeout += SignalImpactCue;

            var cleanupTimeout = tree.CreateTimer(CleanupTimeoutSeconds);
            cleanupTimeout.Timeout += Complete;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Detonation VFX playback setup failed: " + ex);
            Complete();
        }

        await impactCue.Task;
    }

    private static async Task<Texture2D[]> GetFramesForPlaybackAsync(SceneTree tree)
    {
        if (_cachedFrames is not null)
        {
            return _cachedFrames;
        }

        _loadTask ??= LoadFramesAsync(tree);
        var loadTask = _loadTask;
        var frames = await loadTask;

        if (frames is null && ReferenceEquals(_loadTask, loadTask))
        {
            _loadTask = null;
        }

        return frames;
    }

    private static async Task<Texture2D[]> LoadFramesAsync(SceneTree tree)
    {
        try
        {
            await WaitForNextTickAsync(tree);

            var frames = new Texture2D[FrameCount];
            for (var i = 0; i < frames.Length; i++)
            {
                if (tree.Root is null || !GodotObject.IsInstanceValid(tree.Root))
                {
                    return null;
                }

                var path = $"{FramePathPrefix}{i:000}.png";
                if (!ResourceLoader.Exists(path))
                {
                    MainFile.Logger.Info("Could not find Detonation VFX frame: " + path);
                    return null;
                }

                frames[i] = ResourceLoader.Load<Texture2D>(path);
                if (frames[i] is null)
                {
                    MainFile.Logger.Info("Could not load Detonation VFX frame: " + path);
                    return null;
                }

                if ((i + 1) % FramesPerLoadTick == 0 && i + 1 < frames.Length)
                {
                    await WaitForNextTickAsync(tree);
                }
            }

            _cachedFrames = frames;
            MainFile.Logger.Info($"Detonation VFX loaded {FrameCount} frames on first use.");
            return _cachedFrames;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Detonation VFX frame loading failed: " + ex);
            return null;
        }
    }

    private static async Task PlaySequenceAsync(
        CanvasLayer layer,
        Sprite2D sprite,
        Texture2D[] frames,
        Action startImpact,
        Action signalImpactCue,
        Action complete)
    {
        var reachedNaturalEnd = false;
        try
        {
            var tree = layer.GetTree();
            if (tree is null)
            {
                return;
            }

            var startedAt = Stopwatch.GetTimestamp();
            var displayedFrame = -1;

            while (true)
            {
                if (!GodotObject.IsInstanceValid(layer) || !GodotObject.IsInstanceValid(sprite))
                {
                    return;
                }

                var elapsedSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
                var frame = Math.Min(
                    frames.Length - 1,
                    (int)(elapsedSeconds / FrameSeconds));

                // Preserve the authored impact frame even if a long render hitch crosses it.
                if (displayedFrame < ImpactFrame && frame > ImpactFrame)
                {
                    frame = ImpactFrame;
                }
                else if (elapsedSeconds >= PlaybackDurationSeconds)
                {
                    reachedNaturalEnd = true;
                    return;
                }

                if (frame != displayedFrame)
                {
                    sprite.Texture = frames[frame];
                    displayedFrame = frame;
                    if (frame == ImpactFrame)
                    {
                        startImpact();
                        signalImpactCue();
                    }
                }

                await layer.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Detonation VFX sequence failed: " + ex);
        }
        finally
        {
            if (!reachedNaturalEnd)
            {
                complete();
            }
        }
    }

    private static ImpactVisuals BuildImpactVisuals(Node2D root)
    {
        var burst = CreatePolygon(
            "DetonationBurst",
            new Color(1f, 0.72f, 0.22f, 0.96f),
            BuildStarPoints(18f, 7f, 8));
        burst.Scale = Vector2.One * 0.18f;
        burst.Modulate = new Color(1f, 1f, 1f, 0f);
        burst.ZIndex = 908;
        root.AddChild(burst);

        var rings = new[]
        {
            CreateBrokenRing(
                "DetonationGoldRing",
                230f,
                5f,
                new Color(1f, 0.67f, 0.18f, 0.95f),
                0.25f,
                new[] { 4f, 166f, 278f },
                new[] { 142f, 88f, 72f }),
            CreateBrokenRing(
                "DetonationPaleRing",
                238f,
                3.8f,
                new Color(1f, 0.90f, 0.57f, 0.84f),
                0.15f,
                new[] { 20f, 132f, 252f },
                new[] { 70f, 88f, 62f }),
            CreateBrokenRing(
                "DetonationCyanRing",
                210f,
                3.2f,
                new Color(0.36f, 0.84f, 1f, 0.74f),
                0.10f,
                new[] { 18f, 150f, 267f },
                new[] { 58f, 72f, 46f })
        };

        foreach (var ring in rings)
        {
            root.AddChild(ring);
        }

        var rayAngles = new[] { -8f, 34f, 79f, 126f, 171f, 218f, 264f, 315f };
        var rayLengths = new[] { 360f, 320f, 385f, 295f, 350f, 330f, 375f, 310f };
        var rays = new Line2D[rayAngles.Length];
        for (var i = 0; i < rays.Length; i++)
        {
            var angle = Degrees(rayAngles[i]);
            var bend = Degrees(i % 2 == 0 ? 2.8f : -2.4f);
            var direction = Unit(angle);
            var color = i < 6
                ? new Color(1f, 0.70f, 0.20f, 0.84f)
                : new Color(0.42f, 0.88f, 1f, 0.74f);
            var ray = CreateLine(
                $"DetonationRay{i}",
                color,
                i % 3 == 0 ? 5f : 3.5f,
                direction * 16f,
                Unit(angle + bend) * (rayLengths[i] * 0.44f),
                direction * rayLengths[i]);
            ray.Scale = Vector2.One * 0.08f;
            ray.Modulate = new Color(1f, 1f, 1f, 0f);
            ray.ZIndex = 905;
            rays[i] = ray;
            root.AddChild(ray);
        }

        var fragmentAngles = new[] { 12f, 66f, 118f, 187f, 242f, 307f };
        var fragmentStarts = new[] { 26f, 30f, 24f, 31f, 27f, 29f };
        var fragmentDistances = new[] { 290f, 230f, 275f, 210f, 300f, 250f };
        var fragments = new Polygon2D[fragmentAngles.Length];
        var fragmentTargets = new Vector2[fragmentAngles.Length];
        var fragmentRotations = new float[fragmentAngles.Length];
        for (var i = 0; i < fragments.Length; i++)
        {
            var angle = Degrees(fragmentAngles[i]);
            var direction = Unit(angle);
            var fragment = CreatePolygon(
                $"DetonationFragment{i}",
                i < 4
                    ? new Color(1f, 0.66f, 0.16f, 0.90f)
                    : new Color(0.36f, 0.84f, 1f, 0.80f),
                new Vector2(-8f, 0f),
                new Vector2(0f, -3.5f),
                new Vector2(14f, 0f),
                new Vector2(0f, 3.5f));
            fragment.Position = direction * fragmentStarts[i];
            fragment.Rotation = angle + (i % 2 == 0 ? 0.24f : -0.20f);
            fragment.Scale = Vector2.One * (0.78f + i % 3 * 0.12f);
            fragment.Modulate = new Color(1f, 1f, 1f, 0f);
            fragment.ZIndex = 906;
            fragments[i] = fragment;
            fragmentTargets[i] = direction * fragmentDistances[i];
            fragmentRotations[i] = fragment.Rotation + (i % 2 == 0 ? 2.1f : -2.3f);
            root.AddChild(fragment);
        }

        return new ImpactVisuals(
            burst,
            rings,
            rays,
            fragments,
            fragmentTargets,
            fragmentRotations);
    }

    private static void AnimateAnticipation(CanvasLayer layer, ColorRect overlay)
    {
        var tween = layer.CreateTween();
        tween.TweenProperty(
                overlay,
                "color",
                new Color(0.035f, 0.012f, 0.008f, 0.12f),
                4d * FrameSeconds)
            .SetDelay(26d * FrameSeconds)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
    }

    private static void AnimateImpact(
        CanvasLayer layer,
        ColorRect overlay,
        ColorRect flash,
        Sprite2D sprite,
        ImpactVisuals visuals)
    {
        var tween = layer.CreateTween();
        tween.SetParallel(true);

        tween.TweenProperty(flash, "color", new Color(1f, 0.79f, 0.38f, 0.13f), 0.035d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(flash, "color", new Color(1f, 0.79f, 0.38f, 0f), 0.16d)
            .SetDelay(0.035d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(overlay, "color", new Color(0.035f, 0.012f, 0.008f, 0f), 0.34d)
            .SetDelay(0.05d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(sprite, "modulate", new Color(1f, 1f, 1f, 0f), 10d * FrameSeconds)
            .SetDelay(2d * FrameSeconds)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        tween.TweenProperty(visuals.Burst, "modulate", new Color(1f, 1f, 1f, 1f), 0.02d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(visuals.Burst, "scale", Vector2.One, 0.10d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(visuals.Burst, "rotation", 0.42f, 0.22d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(visuals.Burst, "modulate", new Color(1f, 1f, 1f, 0f), 0.18d)
            .SetDelay(0.09d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        var ringStartDelays = new[] { 0d, 0.05d, 0.16d };
        var ringScaleDurations = new[] { 0.25d, 0.42d, 0.95d };
        var ringEndScales = new[] { 1.45f, 2.10f, 3.80f };
        var ringFadeStarts = new[] { 0.10d, 0.16d, 0.38d };
        var ringFadeDurations = new[] { 0.25d, 0.32d, 0.67d };
        for (var i = 0; i < visuals.Rings.Length; i++)
        {
            var ring = visuals.Rings[i];
            tween.TweenProperty(ring, "modulate", new Color(1f, 1f, 1f, 1f), 0.025d)
                .SetDelay(ringStartDelays[i])
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(ring, "scale", Vector2.One * ringEndScales[i], ringScaleDurations[i])
                .SetDelay(ringStartDelays[i])
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(ring, "modulate", new Color(1f, 1f, 1f, 0f), ringFadeDurations[i])
                .SetDelay(ringFadeStarts[i])
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.In);
        }

        for (var i = 0; i < visuals.Rays.Length; i++)
        {
            var ray = visuals.Rays[i];
            var revealDelay = i * 0.006d;
            tween.TweenProperty(ray, "modulate", new Color(1f, 1f, 1f, 1f), 0.025d)
                .SetDelay(revealDelay)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(ray, "scale", Vector2.One, 0.13d + i * 0.004d)
                .SetDelay(revealDelay)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(ray, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
                .SetDelay(0.10d + i * 0.009d)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.In);
        }

        for (var i = 0; i < visuals.Fragments.Length; i++)
        {
            var fragment = visuals.Fragments[i];
            var delay = i < 4 ? 0.04d + i * 0.015d : 0.16d + (i - 4) * 0.02d;
            var duration = 0.50d + i * 0.04d;
            tween.TweenProperty(fragment, "modulate", new Color(1f, 1f, 1f, 1f), 0.025d)
                .SetDelay(delay)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(fragment, "position", visuals.FragmentTargets[i], duration)
                .SetDelay(delay)
                .SetTrans(Tween.TransitionType.Cubic)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(fragment, "rotation", visuals.FragmentRotations[i], duration)
                .SetDelay(delay)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(fragment, "modulate", new Color(1f, 1f, 1f, 0f), duration - 0.22d)
                .SetDelay(delay + 0.22d)
                .SetTrans(Tween.TransitionType.Quad)
                .SetEase(Tween.EaseType.In);
        }
    }

    private static Node2D CreateBrokenRing(
        string name,
        float radius,
        float width,
        Color color,
        float initialScale,
        float[] starts,
        float[] sweeps)
    {
        var ring = new Node2D
        {
            Name = name,
            Scale = Vector2.One * initialScale,
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 904
        };

        for (var i = 0; i < starts.Length; i++)
        {
            var segments = Math.Max(10, (int)MathF.Ceiling(sweeps[i] / 6f));
            ring.AddChild(CreateArc(
                $"{name}Segment{i}",
                radius,
                Degrees(starts[i]),
                Degrees(sweeps[i]),
                width,
                color,
                segments));
        }

        return ring;
    }

    private static Line2D CreateArc(
        string name,
        float radius,
        float start,
        float sweep,
        float width,
        Color color,
        int segments)
    {
        return new Line2D
        {
            Name = name,
            Points = BuildArcPoints(radius, start, sweep, segments),
            Width = width,
            DefaultColor = color,
            Antialiased = true
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

    private static Vector2[] BuildStarPoints(float outerRadius, float innerRadius, int pointCount)
    {
        var points = new Vector2[pointCount * 2];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = -Mathf.Pi * 0.5f + Mathf.Pi * i / pointCount;
            points[i] = Unit(angle) * (i % 2 == 0 ? outerRadius : innerRadius);
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
            Antialiased = true
        };
    }

    private static Polygon2D CreatePolygon(string name, Color color, params Vector2[] points)
    {
        return new Polygon2D
        {
            Name = name,
            Polygon = points,
            Color = color
        };
    }

    private static Vector2 Unit(float angle)
    {
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    }

    private static float Degrees(float degrees)
    {
        return degrees * Mathf.Pi / 180f;
    }

    private static Vector2 ResolveTargetPosition(Creature target, Rect2 visibleRect)
    {
        try
        {
            var creatureNode = target?.GetCreatureNode();
            var hitbox = creatureNode?.Hitbox;
            if (hitbox is not null
                && GodotObject.IsInstanceValid(hitbox)
                && hitbox.IsInsideTree()
                && hitbox.Size.X > 0f
                && hitbox.Size.Y > 0f)
            {
                var center = hitbox.GetGlobalTransformWithCanvas() * (hitbox.Size * 0.5f);
                if (float.IsFinite(center.X) && float.IsFinite(center.Y))
                {
                    return center;
                }
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Detonation VFX target positioning fell back to enemy area: " + ex);
        }

        return visibleRect.Position + new Vector2(
            visibleRect.Size.X * 0.74f,
            visibleRect.Size.Y * 0.43f);
    }

    private static Vector2 ClampToVisibleRect(Vector2 position, Rect2 visibleRect, float scale)
    {
        var inset = CenterSafePadding * scale;

        return new Vector2(
            ClampAxis(position.X, visibleRect.Position.X, visibleRect.Size.X, inset),
            ClampAxis(position.Y, visibleRect.Position.Y, visibleRect.Size.Y, inset));
    }

    private static float ClampAxis(float value, float start, float length, float inset)
    {
        var minimum = start + inset;
        var maximum = start + length - inset;
        return minimum <= maximum
            ? Mathf.Clamp(value, minimum, maximum)
            : start + length * 0.5f;
    }

    private static async Task WaitForNextTickAsync(SceneTree tree)
    {
        var root = tree?.Root;
        if (root is null || !GodotObject.IsInstanceValid(root))
        {
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void SignalDone() => done.TrySetResult();

        root.TreeExiting += SignalDone;
        var timer = tree.CreateTimer(0.001d);
        timer.Timeout += SignalDone;
        await done.Task;

        if (GodotObject.IsInstanceValid(root))
        {
            root.TreeExiting -= SignalDone;
        }
    }

    private sealed class ImpactVisuals
    {
        public ImpactVisuals(
            Polygon2D burst,
            Node2D[] rings,
            Line2D[] rays,
            Polygon2D[] fragments,
            Vector2[] fragmentTargets,
            float[] fragmentRotations)
        {
            Burst = burst;
            Rings = rings;
            Rays = rays;
            Fragments = fragments;
            FragmentTargets = fragmentTargets;
            FragmentRotations = fragmentRotations;
        }

        public Polygon2D Burst { get; }

        public Node2D[] Rings { get; }

        public Line2D[] Rays { get; }

        public Polygon2D[] Fragments { get; }

        public Vector2[] FragmentTargets { get; }

        public float[] FragmentRotations { get; }
    }
}
