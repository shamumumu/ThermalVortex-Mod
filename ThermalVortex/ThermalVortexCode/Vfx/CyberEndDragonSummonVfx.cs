using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class CyberEndDragonSummonVfx
{
    private const int FrameCount = 48;
    private const int FramesPerLoadTick = 4;
    private const int EnergyCueFrame = 36;
    private const double FrameSeconds = 1d / 24d;
    private const double PlaybackSeconds = 2.00d;
    private const double FadeOutDurationSeconds = 0.22d;
    private const double OverlayFadeOutDurationSeconds = 0.28d;
    private const double HighlightFadeOutDurationSeconds = 0.32d;
    private const double FadeOutStartSeconds = PlaybackSeconds - FadeOutDurationSeconds;
    private const double CleanupSeconds = PlaybackSeconds + 0.05d;
    private const double HardTimeoutSeconds = PlaybackSeconds + 0.15d;
    private const float ReferenceViewportHeight = 1080f;
    private const string FramePathPrefix = MainFile.ResPath + "/images/vfx/cyber_end_dragon/frame_";
    private const string StartSfx = "event:/sfx/characters/defect/defect_dark_channel";
    private const string EnergySfx = "event:/sfx/characters/defect/defect_lightning_channel";
    private const string RevealSfx = "event:/sfx/characters/defect/defect_lightning_evoke";

    private const string SequenceShaderCode = @"shader_type canvas_item;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    vec2 edge_distance = min(UV, vec2(1.0) - UV);
    float edge = min(edge_distance.x, edge_distance.y);
    float feather = smoothstep(0.0, 0.12, edge);
    COLOR = vec4(tex.rgb, tex.a * feather) * COLOR;
}
";

    private const string HighlightShaderCode = @"shader_type canvas_item;
render_mode blend_add;

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    vec2 edge_distance = min(UV, vec2(1.0) - UV);
    float edge = min(edge_distance.x, edge_distance.y);
    float feather = smoothstep(0.0, 0.12, edge);
    float highlight = smoothstep(0.64, 0.98, max(tex.r, max(tex.g, tex.b)));
    COLOR = vec4(tex.rgb * highlight, tex.a * highlight * feather) * COLOR;
}
";

    private static Texture2D[] _cachedFrames;
    private static Task<Texture2D[]> _loadTask;
    private static Shader _sequenceShader;
    private static Shader _highlightShader;

    public static void StartPreload()
    {
        if (_cachedFrames is not null || _loadTask is { IsCompleted: false })
            return;

        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null)
            return;

        _loadTask = LoadFramesAsync(tree);
    }

    public static async Task PlayAsync()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null)
            return;

        var frames = await GetFramesForPlaybackAsync(tree);
        if (frames is null)
            return;

        var visibleRect = tree.Root.GetVisibleRect();
        if (visibleRect.Size == Vector2.Zero)
            return;

        var layer = new CanvasLayer
        {
            Name = "ThermalVortexCyberEndDragonSummonVfx",
            Layer = 135
        };

        var overlay = new ColorRect
        {
            Name = "CyberEndDragonSummonDim",
            Position = visibleRect.Position,
            Size = visibleRect.Size,
            Color = new Color(0.005f, 0.018f, 0.042f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 880
        };

        var contentRoot = new Control
        {
            Name = "CyberEndDragonSummonContent",
            Position = visibleRect.Position,
            Size = visibleRect.Size,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 890
        };

        var sequence = CreateSequenceRect(
            "CyberEndDragonSummonSequence",
            frames[0],
            CreateSequenceMaterial(),
            0f,
            0);
        var highlights = CreateSequenceRect(
            "CyberEndDragonSummonHighlights",
            frames[0],
            CreateHighlightMaterial(),
            0f,
            1);
        contentRoot.AddChild(sequence);
        contentRoot.AddChild(highlights);

        var effectScale = Mathf.Clamp(visibleRect.Size.Y / ReferenceViewportHeight, 0.65f, 1.45f);
        var effectsRoot = BuildEffects(visibleRect.Position + visibleRect.Size * 0.5f, effectScale);

        var flash = new ColorRect
        {
            Name = "CyberEndDragonSummonFlash",
            Position = visibleRect.Position,
            Size = visibleRect.Size,
            Color = new Color(0.86f, 0.96f, 1f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 930
        };

        try
        {
            layer.AddChild(overlay);
            layer.AddChild(contentRoot);
            layer.AddChild(effectsRoot);
            layer.AddChild(flash);
            tree.Root.AddChild(layer);
        }
        catch
        {
            if (GodotObject.IsInstanceValid(layer))
                layer.QueueFree();
            throw;
        }

        var landingCue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;

        void SignalLandingCue() => landingCue.TrySetResult();

        void Complete()
        {
            if (completed)
                return;

            completed = true;
            SignalLandingCue();
            if (GodotObject.IsInstanceValid(layer))
                layer.QueueFree();
        }

        void PlayCue(string path, float volume)
        {
            if (completed || !GodotObject.IsInstanceValid(layer))
                return;

            try
            {
                SfxCmd.Play(path, volume);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"Cyber End Dragon summon SFX failed path={path}: {ex}");
            }
        }

        layer.TreeExiting += SignalLandingCue;

        try
        {
            AnimateComposition(layer, overlay, contentRoot, sequence, highlights, effectsRoot, flash);
            PlayCue(StartSfx, 0.58f);

            var lifecycle = layer.CreateTween();
            lifecycle.TweenInterval(CleanupSeconds);
            lifecycle.Finished += Complete;

            _ = PlaySequenceAsync(
                layer,
                sequence,
                highlights,
                frames,
                () => PlayCue(EnergySfx, 0.66f),
                () => PlayCue(RevealSfx, 0.74f),
                Complete);

            var timeout = tree.CreateTimer(HardTimeoutSeconds);
            timeout.Timeout += Complete;
        }
        catch
        {
            Complete();
            throw;
        }

        await landingCue.Task;
    }

    private static TextureRect CreateSequenceRect(
        string name,
        Texture2D texture,
        Material material,
        float alpha,
        int zIndex)
    {
        var image = new TextureRect
        {
            Name = name,
            Texture = texture,
            Material = material,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, alpha),
            ZIndex = zIndex
        };
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return image;
    }

    private static ShaderMaterial CreateSequenceMaterial() =>
        new() { Shader = _sequenceShader ??= new Shader { Code = SequenceShaderCode } };

    private static ShaderMaterial CreateHighlightMaterial() =>
        new() { Shader = _highlightShader ??= new Shader { Code = HighlightShaderCode } };

    private static Node2D BuildEffects(Vector2 center, float scale)
    {
        var root = new Node2D
        {
            Name = "CyberEndDragonSummonAccents",
            Position = center,
            Scale = Vector2.One * scale,
            ZIndex = 910
        };

        root.AddChild(CreateRing(
            "CyberEndDragonRingOuter",
            660f,
            338f,
            0.06f,
            Mathf.Tau * 0.88f,
            7f,
            new Color(0.76f, 0.94f, 1f, 0.72f),
            0.70f));
        root.AddChild(CreateRing(
            "CyberEndDragonRingInner",
            515f,
            260f,
            Mathf.Pi * 0.90f,
            Mathf.Tau * 0.80f,
            4f,
            new Color(0.36f, 0.82f, 1f, 0.62f),
            0.79f));

        var shockwave = CreateRing(
            "CyberEndDragonShockwave",
            365f,
            195f,
            0f,
            Mathf.Tau,
            9f,
            new Color(0.92f, 0.98f, 1f, 0.94f),
            0.16f);
        root.AddChild(shockwave);

        for (var i = 0; i < 32; i++)
        {
            var angle = Mathf.Tau * i / 32f + Noise(i, 0.43f) * 0.08f;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var start = 210f + Noise(i, 0.89f) * 120f;
            var length = 34f + Noise(i, 1.57f) * 78f;
            root.AddChild(new Line2D
            {
                Name = $"CyberEndDragonParticle{i}",
                Points = new[] { direction * start, direction * (start + length) },
                Width = 2f + i % 3,
                DefaultColor = i % 4 == 0
                    ? new Color(0.92f, 0.98f, 1f, 0.78f)
                    : new Color(0.40f, 0.86f, 1f, 0.76f),
                Antialiased = true,
                Modulate = new Color(1f, 1f, 1f, 0f),
                Scale = Vector2.One * 0.36f,
                ZIndex = 912
            });
        }

        return root;
    }

    private static Line2D CreateRing(
        string name,
        float radiusX,
        float radiusY,
        float start,
        float sweep,
        float width,
        Color color,
        float initialScale)
    {
        return new Line2D
        {
            Name = name,
            Points = BuildEllipseArcPoints(radiusX, radiusY, start, sweep, 88),
            Width = width,
            DefaultColor = color,
            Antialiased = true,
            Modulate = new Color(1f, 1f, 1f, 0f),
            Scale = Vector2.One * initialScale,
            ZIndex = 911
        };
    }

    private static void AnimateComposition(
        CanvasLayer layer,
        ColorRect overlay,
        Control contentRoot,
        TextureRect sequence,
        TextureRect highlights,
        Node2D effectsRoot,
        ColorRect flash)
    {
        var tween = layer.CreateTween();
        tween.SetParallel(true);

        tween.TweenProperty(overlay, "color", new Color(0.005f, 0.018f, 0.042f, 0.66f), 0.14d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(
                overlay,
                "color",
                new Color(0.005f, 0.018f, 0.042f, 0f),
                OverlayFadeOutDurationSeconds)
            .SetDelay(PlaybackSeconds - OverlayFadeOutDurationSeconds)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        tween.TweenProperty(sequence, "modulate", new Color(1f, 1f, 1f, 0.90f), 0.10d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(
                sequence,
                "modulate",
                new Color(1f, 1f, 1f, 0f),
                FadeOutDurationSeconds)
            .SetDelay(FadeOutStartSeconds)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        tween.TweenProperty(highlights, "modulate", new Color(0.84f, 0.95f, 1f, 0.15f), 0.24d)
            .SetDelay(0.12d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(highlights, "modulate", new Color(0.94f, 0.98f, 1f, 0.31f), 0.20d)
            .SetDelay(1.40d)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(
                highlights,
                "modulate",
                new Color(0.88f, 0.96f, 1f, 0f),
                HighlightFadeOutDurationSeconds)
            .SetDelay(PlaybackSeconds - HighlightFadeOutDurationSeconds)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);

        AnimateEffects(tween, effectsRoot);
        AnimateFlashes(tween, flash);
        AnimateShake(tween, contentRoot);
    }

    private static void AnimateEffects(Tween tween, Node2D effectsRoot)
    {
        var particleIndex = 0;
        foreach (var child in effectsRoot.GetChildren())
        {
            if (child is not Node2D node)
                continue;

            var name = node.Name.ToString();
            if (name == "CyberEndDragonRingOuter")
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0.56f), 0.18d)
                    .SetDelay(0.05d);
                tween.TweenProperty(node, "rotation", node.Rotation + Mathf.Tau * 0.36f, 2.00d)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "scale", Vector2.One * 1.10f, 1.85d)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.30d)
                    .SetDelay(1.71d);
            }
            else if (name == "CyberEndDragonRingInner")
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0.48f), 0.20d)
                    .SetDelay(0.10d);
                tween.TweenProperty(node, "rotation", node.Rotation - Mathf.Tau * 0.44f, 2.00d)
                    .SetTrans(Tween.TransitionType.Sine)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "scale", Vector2.One * 1.18f, 1.85d)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.28d)
                    .SetDelay(1.73d);
            }
            else if (name == "CyberEndDragonShockwave")
            {
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0.88f), 0.06d)
                    .SetDelay(1.86d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "scale", Vector2.One * 1.44f, 0.27d)
                    .SetDelay(1.86d)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.20d)
                    .SetDelay(1.95d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
            }
            else if (name.StartsWith("CyberEndDragonParticle", StringComparison.Ordinal))
            {
                var delay = 0.70d + particleIndex % 10 * 0.055d;
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0.70f), 0.09d)
                    .SetDelay(delay)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "scale", Vector2.One * (1.06f + particleIndex % 4 * 0.04f), 0.62d)
                    .SetDelay(delay)
                    .SetTrans(Tween.TransitionType.Cubic)
                    .SetEase(Tween.EaseType.Out);
                tween.TweenProperty(node, "modulate", new Color(1f, 1f, 1f, 0f), 0.24d)
                    .SetDelay(delay + 0.48d)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
                particleIndex++;
            }
        }
    }

    private static void AnimateFlashes(Tween tween, ColorRect flash)
    {
        tween.TweenProperty(flash, "color", new Color(0.60f, 0.88f, 1f, 0.14f), 0.06d)
            .SetDelay(1.50d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(flash, "color", new Color(0.60f, 0.88f, 1f, 0f), 0.18d)
            .SetDelay(1.56d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
        tween.TweenProperty(flash, "color", new Color(0.90f, 0.98f, 1f, 0.25f), 0.055d)
            .SetDelay(1.89d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(flash, "color", new Color(0.90f, 0.98f, 1f, 0f), 0.18d)
            .SetDelay(1.945d)
            .SetTrans(Tween.TransitionType.Quad)
            .SetEase(Tween.EaseType.In);
    }

    private static void AnimateShake(Tween tween, Control contentRoot)
    {
        var origin = contentRoot.Position;
        var offsets = new[]
        {
            new Vector2(-4f, 2f),
            new Vector2(3f, -3f),
            new Vector2(-2f, -2f),
            new Vector2(3f, 2f),
            Vector2.Zero
        };

        for (var i = 0; i < offsets.Length; i++)
        {
            tween.TweenProperty(contentRoot, "position", origin + offsets[i], 0.035d)
                .SetDelay(1.49d + i * 0.035d)
                .SetTrans(Tween.TransitionType.Linear);
        }
    }

    private static async Task<Texture2D[]> GetFramesForPlaybackAsync(SceneTree tree)
    {
        if (_cachedFrames is not null)
            return _cachedFrames;

        StartPreload();
        var loadTask = _loadTask;
        if (loadTask is null)
            return null;

        var frames = await loadTask;
        if (frames is null && ReferenceEquals(_loadTask, loadTask))
            _loadTask = null;

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
                    return null;

                var path = $"{FramePathPrefix}{i:0000}.jpg";
                frames[i] = LoadFrame(path);
                if (frames[i] is null)
                    return null;

                if ((i + 1) % FramesPerLoadTick == 0 && i + 1 < frames.Length)
                    await WaitForNextTickAsync(tree);
            }

            _cachedFrames = frames;
            MainFile.Logger.Info($"Cyber End Dragon summon VFX loaded {FrameCount} frames.");
            return _cachedFrames;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Cyber End Dragon summon frame loading failed: " + ex);
            return null;
        }
    }

    private static Texture2D LoadFrame(string path)
    {
        if (ResourceLoader.Exists(path))
        {
            var importedTexture = ResourceLoader.Load<Texture2D>(path);
            if (importedTexture is not null)
                return importedTexture;
        }

        // JPEGs are packed as raw PCK entries by the lightweight resource
        // packer. Decode those bytes directly when no imported remap exists.
        if (!Godot.FileAccess.FileExists(path))
        {
            MainFile.Logger.Info("Could not find Cyber End Dragon summon frame: " + path);
            return null;
        }

        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        if (bytes is null || bytes.Length == 0)
        {
            MainFile.Logger.Info("Could not read Cyber End Dragon summon frame: " + path);
            return null;
        }

        using var image = new Image();
        var error = image.LoadJpgFromBuffer(bytes);
        if (error != Error.Ok)
        {
            MainFile.Logger.Info(
                $"Could not decode Cyber End Dragon summon frame path={path} error={error}");
            return null;
        }

        var texture = ImageTexture.CreateFromImage(image);
        if (texture is null)
            MainFile.Logger.Info("Could not create Cyber End Dragon summon texture: " + path);

        return texture;
    }

    private static async Task PlaySequenceAsync(
        CanvasLayer layer,
        TextureRect sequence,
        TextureRect highlights,
        Texture2D[] frames,
        Action playEnergyCue,
        Action playLandingCue,
        Action complete)
    {
        var reachedNaturalEnd = false;
        var energyCuePlayed = false;
        try
        {
            var tree = layer.GetTree();
            if (tree is null)
                return;

            var startedAt = Stopwatch.GetTimestamp();
            var displayedFrame = -1;
            while (true)
            {
                if (!GodotObject.IsInstanceValid(layer)
                    || !GodotObject.IsInstanceValid(sequence)
                    || !GodotObject.IsInstanceValid(highlights))
                {
                    return;
                }

                var elapsedSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
                if (elapsedSeconds >= PlaybackSeconds)
                {
                    reachedNaturalEnd = true;
                    playLandingCue();
                    complete();
                    return;
                }

                var frame = Math.Min(frames.Length - 1, (int)(elapsedSeconds / FrameSeconds));
                if (frame != displayedFrame)
                {
                    sequence.Texture = frames[frame];
                    highlights.Texture = frames[frame];
                    displayedFrame = frame;
                }

                if (!energyCuePlayed && frame >= EnergyCueFrame)
                {
                    energyCuePlayed = true;
                    playEnergyCue();
                }

                await layer.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Cyber End Dragon summon sequence failed: " + ex);
        }
        finally
        {
            if (!reachedNaturalEnd)
                complete();
        }
    }

    private static Vector2[] BuildEllipseArcPoints(
        float radiusX,
        float radiusY,
        float start,
        float sweep,
        int segments)
    {
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = start + sweep * i / segments;
            points[i] = new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
        }

        return points;
    }

    private static float Noise(int index, float seed) =>
        Mathf.PosMod(Mathf.Sin(index * 12.9898f + seed * 78.233f) * 43758.5453f, 1f);

    private static async Task WaitForNextTickAsync(SceneTree tree)
    {
        var root = tree?.Root;
        if (root is null || !GodotObject.IsInstanceValid(root))
            return;

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void SignalDone() => done.TrySetResult();

        root.TreeExiting += SignalDone;
        var timer = tree.CreateTimer(0.001d);
        timer.Timeout += SignalDone;
        await done.Task;

        if (GodotObject.IsInstanceValid(root))
            root.TreeExiting -= SignalDone;
    }
}
