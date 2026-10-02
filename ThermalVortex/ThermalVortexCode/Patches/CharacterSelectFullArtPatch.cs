using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
public static partial class CharacterSelectFullArtPatch
{
    private static readonly bool AnimatedBackgroundEnabled = false;
    private const string StaticBackgroundPath = MainFile.ResPath + "/images/charui/character_select_bg_yugi.png";
    private const string AnimatedBackgroundFramePathPrefix = MainFile.ResPath + "/images/charui/character_select_bg_yugi_loop/frame_";
    private const string ScreenAnimatedBackgroundNodeName = "ThermalVortexFullScreenAnimation";
    private const string LegacyStaticAnimatedBackgroundNodeName = "ThermalVortexFullArtAnimation";
    private const int AnimatedBackgroundSourceFrameCount = 122;
    private const int AnimatedBackgroundSkippedSourceFrameIndex = 52;
    private const int AnimatedBackgroundFrameCount = AnimatedBackgroundSourceFrameCount - 1;
    private const double AnimatedBackgroundFrameSeconds = 5d / AnimatedBackgroundFrameCount;
    private const int AnimatedBackgroundPreloadBatchSize = 3;
    private const double AnimatedBackgroundPreloadBatchDelaySeconds = 0.01d;
    private const double AnimatedBackgroundPreloadInitialDelaySeconds = 0.5d;
    private const string BaseDotTexturePath = "res://images/vfx/dot.png";
    private const string BaseLightTexturePath = "res://images/vfx/light.png";
    private const string BaseAdditiveMaterialPath = "res://themes/canvas_item_material_additive_shared.tres";

    private static Texture2D[] cachedAnimatedTextures;
    private static Texture2D cachedDotTexture;
    private static Texture2D cachedLightTexture;
    private static Material cachedAdditiveMaterial;
    private static NCharacterSelectScreen pendingAnimationScreen;
    private static int animatedFrameIndex;
    private static bool isPreloadingAnimatedFrames;
    private static bool animatedFramePreloadFailed;
    private static bool loggedApply;
    private static bool loggedAnimatedApply;
    private static bool loggedAnimatedFramesLoaded;
    private static bool loggedAnimatedPreloadStarted;
    private static bool loggedMissingAnimatedFrame;
    private static bool loggedStaticApply;
    private static bool loggedOverlayCreated;
    private static bool loggedEnergyOverlayCreated;
    private static bool loggedMissingStaticBg;

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(NCharacterSelectScreen __instance, NCharacterSelectButton __0, CharacterModel __1)
    {
        Apply(__instance, __0?.Character ?? __1);
    }

    public static void Apply(NCharacterSelectScreen screen, CharacterModel character = null)
    {
        if (screen == null)
        {
            return;
        }

        character ??= FindSelectedCharacter(screen);
        var shouldShow = IsThermalVortex(character);
        var staticBg = FindNode<Control>(screen, "StaticBg");
        if (staticBg == null)
        {
            if (shouldShow && !loggedMissingStaticBg)
            {
                MainFile.Logger.Info("Could not find StaticBg for ThermalVortex character select background.");
                loggedMissingStaticBg = true;
            }

            return;
        }

        var animatedBg = FindNode<Control>(screen, "AnimatedBg");
        var image = FindNode<TextureRect>(staticBg, "ThermalVortexFullArt");
        var animatedImage = FindNode<TextureRect>(screen, ScreenAnimatedBackgroundNodeName);
        var legacyAnimatedImage = FindNode<TextureRect>(staticBg, LegacyStaticAnimatedBackgroundNodeName);
        var overlay = FindNode<Control>(staticBg, "ThermalVortexFullArtOverlay");

        if (!shouldShow)
        {
            if (animatedImage != null)
            {
                StopBackgroundAnimation(animatedImage);
                animatedImage.Visible = false;
            }

            if (legacyAnimatedImage != null)
            {
                StopBackgroundAnimation(legacyAnimatedImage);
                legacyAnimatedImage.Visible = false;
            }

            if (image != null)
            {
                image.Visible = false;
            }

            if (overlay != null)
            {
                overlay.Visible = false;
            }

            if (animatedBg != null)
            {
                animatedBg.Visible = true;
            }

            return;
        }

        if (animatedBg != null)
        {
            animatedBg.Visible = false;
        }

        if (!AnimatedBackgroundEnabled)
        {
            if (animatedImage != null)
            {
                StopBackgroundAnimation(animatedImage);
                animatedImage.Visible = false;
            }

            if (legacyAnimatedImage != null)
            {
                StopBackgroundAnimation(legacyAnimatedImage);
                legacyAnimatedImage.Visible = false;
            }

            image ??= CreateStaticBackgroundImage(staticBg);
            image.Texture ??= LoadStaticBackgroundTexture();
            image.Visible = image.Texture != null;

            if (overlay != null)
            {
                overlay.Visible = false;
            }

            staticBg.Visible = image.Visible;

            if (image.Visible && !loggedStaticApply)
            {
                MainFile.Logger.Info("Applied ThermalVortex static character select background; animated background is disabled by default.");
                loggedStaticApply = true;
            }

            return;
        }

        var animatedFrames = cachedAnimatedTextures;
        if (animatedFrames == null)
        {
            pendingAnimationScreen = screen;
            BeginAnimatedBackgroundPreload(screen, false);

            if (animatedImage != null)
            {
                StopBackgroundAnimation(animatedImage);
                animatedImage.Visible = false;
            }

            if (legacyAnimatedImage != null)
            {
                StopBackgroundAnimation(legacyAnimatedImage);
                legacyAnimatedImage.Visible = false;
            }

            if (image != null)
            {
                image.Visible = false;
            }

            if (overlay != null)
            {
                overlay.Visible = false;
            }

            staticBg.Visible = false;
            return;
        }

        pendingAnimationScreen = null;
        if (image != null)
        {
            image.Visible = false;
        }

        if (legacyAnimatedImage != null)
        {
            StopBackgroundAnimation(legacyAnimatedImage);
            legacyAnimatedImage.Visible = false;
        }

        animatedImage ??= CreateScreenAnimatedBackgroundImage(screen, staticBg, animatedBg);
        PlaceScreenAnimatedBackground(screen, animatedImage, staticBg, animatedBg);
        animatedFrameIndex = 0;
        animatedImage.Texture = animatedFrames[0];
        animatedImage.Visible = true;
        StartBackgroundAnimation(animatedImage, animatedFrames);

        overlay ??= CreateBackgroundOverlay(staticBg);
        overlay.Visible = true;
        EnsureEnergyOverlay(overlay);
        staticBg.MoveChild(overlay, staticBg.GetChildCount() - 1);
        staticBg.Visible = true;

        if (!loggedApply)
        {
            MainFile.Logger.Info("Prepared ThermalVortex character select background overlay.");
            loggedApply = true;
        }

        if (animatedFrames != null && !loggedAnimatedApply)
        {
            var firstFrame = animatedFrames[0];
            MainFile.Logger.Info(
                $"Applied ThermalVortex animated character select background frames as screen-level layer with KeepAspectCentered. " +
                $"frame={firstFrame.GetWidth()}x{firstFrame.GetHeight()} screen={screen.Size} staticBgScale={staticBg.Scale}");
            loggedAnimatedApply = true;
        }
    }

    private static bool IsThermalVortex(CharacterModel character)
    {
        if (character == null)
        {
            return false;
        }

        return character is ThermalVortexCharacter
            || character.GetType().FullName == typeof(ThermalVortexCharacter).FullName
            || string.Equals(character.Id?.Entry, ThermalVortexCharacter.CharacterId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(character.Id?.ToString(), ThermalVortexCharacter.CharacterId, StringComparison.OrdinalIgnoreCase);
    }

    private static CharacterModel FindSelectedCharacter(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is NCharacterSelectButton { IsSelected: true } button)
            {
                return button.Character;
            }

            if (child is Node node)
            {
                var character = FindSelectedCharacter(node);
                if (character != null)
                {
                    return character;
                }
            }
        }

        return null;
    }

    private static TextureRect CreateScreenAnimatedBackgroundImage(Control screen, Node staticBg, Node animatedBg)
    {
        var image = new TextureRect
        {
            Name = ScreenAnimatedBackgroundNodeName,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        screen.AddChild(image);
        PlaceScreenAnimatedBackground(screen, image, staticBg, animatedBg);
        return image;
    }

    private static TextureRect CreateStaticBackgroundImage(Control staticBg)
    {
        var image = new TextureRect
        {
            Name = "ThermalVortexFullArt",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Texture = LoadStaticBackgroundTexture()
        };
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        staticBg.AddChild(image);
        staticBg.MoveChild(image, 0);
        return image;
    }

    private static Texture2D LoadStaticBackgroundTexture()
    {
        if (!ResourceLoader.Exists(StaticBackgroundPath))
        {
            if (!loggedMissingStaticBg)
            {
                MainFile.Logger.Info("Could not find ThermalVortex static character select background: " + StaticBackgroundPath);
                loggedMissingStaticBg = true;
            }

            return null;
        }

        return ResourceLoader.Load<Texture2D>(StaticBackgroundPath);
    }

    private static void PlaceScreenAnimatedBackground(Control screen, Node image, Node staticBg, Node animatedBg)
    {
        var backgroundIndex = -1;
        if (staticBg?.GetParent() == screen)
        {
            backgroundIndex = Math.Max(backgroundIndex, staticBg.GetIndex());
        }

        if (animatedBg?.GetParent() == screen)
        {
            backgroundIndex = Math.Max(backgroundIndex, animatedBg.GetIndex());
        }

        if (backgroundIndex < 0)
        {
            return;
        }

        var targetIndex = Math.Min(backgroundIndex + 1, screen.GetChildCount() - 1);
        screen.MoveChild(image, targetIndex);
    }

    public static void BeginAnimatedBackgroundPreload(Node owner, bool waitForMainMenu)
    {
        if (!AnimatedBackgroundEnabled)
        {
            return;
        }

        if (cachedAnimatedTextures != null || isPreloadingAnimatedFrames || animatedFramePreloadFailed)
        {
            return;
        }

        if (owner == null || !GodotObject.IsInstanceValid(owner))
        {
            return;
        }

        isPreloadingAnimatedFrames = true;
        _ = PreloadAnimatedFramesAsync(owner, waitForMainMenu);
    }

    private static async Task PreloadAnimatedFramesAsync(Node owner, bool waitForMainMenu)
    {
        try
        {
            if (!await WaitForAnimatedBackgroundPreloadReady(owner, waitForMainMenu))
            {
                return;
            }

            if (!loggedAnimatedPreloadStarted)
            {
                MainFile.Logger.Info(
                    $"Started ThermalVortex animated character select background preload in batches of {AnimatedBackgroundPreloadBatchSize}.");
                loggedAnimatedPreloadStarted = true;
            }

            var startedAt = DateTimeOffset.UtcNow;
            var frames = new Texture2D[AnimatedBackgroundFrameCount];
            var frameIndex = 0;
            for (var sourceFrameIndex = 0; sourceFrameIndex < AnimatedBackgroundSourceFrameCount; sourceFrameIndex++)
            {
                if (sourceFrameIndex == AnimatedBackgroundSkippedSourceFrameIndex)
                {
                    continue;
                }

                var texture = LoadAnimatedFrame(sourceFrameIndex);
                if (texture == null)
                {
                    animatedFramePreloadFailed = true;
                    return;
                }

                frames[frameIndex] = texture;
                frameIndex++;

                if (frameIndex % AnimatedBackgroundPreloadBatchSize == 0
                    && !await WaitForAnimatedBackgroundPreloadTick(owner))
                {
                    return;
                }
            }

            cachedAnimatedTextures = frames;
            if (!loggedAnimatedFramesLoaded)
            {
                var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
                MainFile.Logger.Info(
                    $"Preloaded {AnimatedBackgroundFrameCount} ThermalVortex animated character select background frames " +
                    $"from {AnimatedBackgroundSourceFrameCount} source frames, skipped frame_{AnimatedBackgroundSkippedSourceFrameIndex:000}, " +
                    $"elapsed_ms={elapsedMs:0}.");
                loggedAnimatedFramesLoaded = true;
            }

            if (pendingAnimationScreen != null
                && GodotObject.IsInstanceValid(pendingAnimationScreen)
                && pendingAnimationScreen.IsInsideTree())
            {
                Apply(pendingAnimationScreen);
            }
        }
        catch (Exception ex)
        {
            animatedFramePreloadFailed = true;
            MainFile.Logger.Info("ThermalVortex animated character select background preload failed: " + ex);
        }
        finally
        {
            isPreloadingAnimatedFrames = false;
        }
    }

    private static async Task<bool> WaitForAnimatedBackgroundPreloadReady(Node owner, bool waitForMainMenu)
    {
        if (!waitForMainMenu)
        {
            return await WaitForAnimatedBackgroundPreloadTick(owner);
        }

        if (owner is NGame game)
        {
            for (var i = 0; i < 120; i++)
            {
                if (!GodotObject.IsInstanceValid(game) || !game.IsInsideTree())
                {
                    return false;
                }

                if (game.MainMenu != null)
                {
                    return await WaitForAnimatedBackgroundPreloadTick(game, AnimatedBackgroundPreloadInitialDelaySeconds);
                }

                if (!await WaitForAnimatedBackgroundPreloadTick(game, 0.25d))
                {
                    return false;
                }
            }

            return false;
        }

        return await WaitForAnimatedBackgroundPreloadTick(owner, AnimatedBackgroundPreloadInitialDelaySeconds);
    }

    private static async Task<bool> WaitForAnimatedBackgroundPreloadTick(Node owner, double seconds = AnimatedBackgroundPreloadBatchDelaySeconds)
    {
        if (owner == null || !GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
        {
            return false;
        }

        var tree = owner.GetTree();
        if (tree == null)
        {
            return false;
        }

        await owner.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
        return GodotObject.IsInstanceValid(owner);
    }

    private static Texture2D LoadAnimatedFrame(int sourceFrameIndex)
    {
        var path = AnimatedBackgroundFramePathPrefix + $"{sourceFrameIndex:000}.png";
        if (!ResourceLoader.Exists(path))
        {
            if (!loggedMissingAnimatedFrame)
            {
                MainFile.Logger.Info("Could not find ThermalVortex animated character select background frame: " + path);
                loggedMissingAnimatedFrame = true;
            }

            return null;
        }

        var texture = ResourceLoader.Load<Texture2D>(path);
        if (texture != null)
        {
            return texture;
        }

        if (!loggedMissingAnimatedFrame)
        {
            MainFile.Logger.Info("Could not load ThermalVortex animated character select background frame: " + path);
            loggedMissingAnimatedFrame = true;
        }

        return null;
    }

    private static void StartBackgroundAnimation(TextureRect image, Texture2D[] frames)
    {
        var timer = FindNode<Godot.Timer>(image, "ThermalVortexFullArtFrameTimer");
        if (timer == null)
        {
            timer = CreateBackgroundAnimationTimer(image, frames);
        }

        if (timer.IsStopped())
        {
            timer.Start();
        }
    }

    private static Godot.Timer CreateBackgroundAnimationTimer(TextureRect image, Texture2D[] frames)
    {
        var timer = new Godot.Timer
        {
            Name = "ThermalVortexFullArtFrameTimer",
            WaitTime = AnimatedBackgroundFrameSeconds,
            OneShot = false,
            Autostart = false
        };

        timer.Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(image) || !image.Visible)
            {
                return;
            }

            animatedFrameIndex = (animatedFrameIndex + 1) % frames.Length;
            image.Texture = frames[animatedFrameIndex];
        };

        image.AddChild(timer);
        return timer;
    }

    private static void StopBackgroundAnimation(TextureRect image)
    {
        var timer = FindNode<Godot.Timer>(image, "ThermalVortexFullArtFrameTimer");
        timer?.Stop();
    }

    private static Control CreateBackgroundOverlay(Control staticBg)
    {
        var overlay = new Control
        {
            Name = "ThermalVortexFullArtOverlay",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        staticBg.AddChild(overlay);

        if (!loggedOverlayCreated)
        {
            MainFile.Logger.Info("Created ThermalVortex character select full art overlay.");
            loggedOverlayCreated = true;
        }

        return overlay;
    }

    private static void EnsureEnergyOverlay(Control overlay)
    {
        if (FindNode<Control>(overlay, "ThermalVortexEnergyOverlay") != null)
        {
            return;
        }

        var layer = new Control
        {
            Name = "ThermalVortexEnergyOverlay",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        layer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(layer);

        AddMoteParticles(layer);
        AddLightPulse(layer, "ThermalVortexGoldPulse", new Vector2(1438f, 420f), new Color(1f, 0.76f, 0.25f, 0.36f), 14f, 0.0d);
        AddLightPulse(layer, "ThermalVortexVioletPulse", new Vector2(1796f, 540f), new Color(0.62f, 0.24f, 1f, 0.30f), 11f, 0.7d);
        AddLightPulse(layer, "ThermalVortexCardPulse", new Vector2(1090f, 665f), new Color(1f, 0.68f, 0.28f, 0.24f), 9f, 1.4d);
        AddEnergyArc(layer, new EnergyArcDefinition(
            "ThermalVortexEnergyArcGold",
            new Vector2(990f, 286f),
            new Vector2(1560f, 170f),
            new Vector2(2180f, 470f),
            new Color(1f, 0.74f, 0.24f, 0.18f),
            2.2f,
            0.0d));
        AddEnergyArc(layer, new EnergyArcDefinition(
            "ThermalVortexEnergyArcViolet",
            new Vector2(1120f, 725f),
            new Vector2(1560f, 910f),
            new Vector2(2140f, 530f),
            new Color(0.54f, 0.18f, 1f, 0.15f),
            3.4f,
            1.1d));

        if (!loggedEnergyOverlayCreated)
        {
            MainFile.Logger.Info("Created ThermalVortex character select energy overlay with built-in particles.");
            loggedEnergyOverlayCreated = true;
        }
    }

    private static T FindNode<T>(Node root, string name) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T typed && typed.Name == name)
            {
                return typed;
            }

            if (child is Node node)
            {
                var found = FindNode<T>(node, name);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static void AddMoteParticles(Control layer)
    {
        var particles = new CpuParticles2D
        {
            Name = "ThermalVortexGoldVioletMotes",
            Material = GetAdditiveMaterial(),
            Position = new Vector2(1570f, 520f),
            Amount = 54,
            Texture = GetDotTexture(),
            Lifetime = 8.0d,
            Preprocess = 8.0d,
            Randomness = 0.62f,
            LocalCoords = true,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            EmissionRectExtents = new Vector2(760f, 360f),
            Direction = new Vector2(0f, -1f),
            Spread = 42f,
            Gravity = new Vector2(0f, -16f),
            InitialVelocityMin = 4f,
            InitialVelocityMax = 18f,
            OrbitVelocityMin = -0.012f,
            OrbitVelocityMax = 0.012f,
            RadialAccelMin = 0.0f,
            RadialAccelMax = 0.35f,
            DampingMin = 0.12f,
            DampingMax = 0.75f,
            AngleMax = 360f,
            ScaleAmountMin = 0.035f,
            ScaleAmountMax = 0.085f,
            Color = new Color(1f, 0.78f, 0.35f, 0.58f),
            ColorRamp = CreateParticleGradient(
                new Color(0.68f, 0.24f, 1f, 0f),
                new Color(1f, 0.84f, 0.32f, 0.90f),
                new Color(0.64f, 0.24f, 1f, 0f))
        };
        layer.AddChild(particles);
        particles.Restart();
        particles.Emitting = true;
    }

    private static void AddLightPulse(Control layer, string name, Vector2 position, Color color, float scale, double delay)
    {
        var pulse = new CpuParticles2D
        {
            Name = name,
            Material = GetAdditiveMaterial(),
            Position = position,
            Amount = 1,
            Texture = GetLightTexture(),
            Lifetime = 3.2d,
            Preprocess = delay,
            Explosiveness = 0.92f,
            Randomness = 0.12f,
            LocalCoords = true,
            Spread = 16f,
            Gravity = Vector2.Zero,
            ScaleAmountMin = scale,
            ScaleAmountMax = scale * 1.28f,
            Color = color,
            ColorRamp = CreateParticleGradient(
                new Color(color.R, color.G, color.B, 0f),
                color,
                new Color(color.R, color.G, color.B, 0f))
        };
        layer.AddChild(pulse);
        pulse.Restart();
        pulse.Emitting = true;
    }

    private static void AddEnergyArc(Control layer, EnergyArcDefinition arc)
    {
        var line = new Line2D
        {
            Name = arc.Name,
            Material = GetAdditiveMaterial(),
            Points = BuildArcPoints(arc.Start, arc.Control, arc.End),
            Width = arc.Width,
            DefaultColor = arc.Color,
            Antialiased = true,
            Modulate = new Color(1f, 1f, 1f, 0.55f)
        };
        layer.AddChild(line);

        var tween = line.CreateTween();
        tween.SetLoops();
        tween.TweenInterval(arc.Delay);
        tween.TweenProperty(line, "modulate", new Color(1f, 1f, 1f, 0.95f), 2.1d)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(line, "modulate", new Color(1f, 1f, 1f, 0.38f), 2.4d)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
    }

    private static Vector2[] BuildArcPoints(Vector2 start, Vector2 control, Vector2 end)
    {
        var points = new Vector2[32];
        for (var i = 0; i < points.Length; i++)
        {
            var amount = i / (points.Length - 1f);
            var inverse = 1f - amount;
            points[i] = inverse * inverse * start
                + 2f * inverse * amount * control
                + amount * amount * end;
        }

        return points;
    }

    private static Gradient CreateParticleGradient(Color start, Color middle, Color end)
    {
        return new Gradient
        {
            Offsets = [0f, 0.42f, 1f],
            Colors = [start, middle, end]
        };
    }

    private static Texture2D GetDotTexture()
    {
        cachedDotTexture ??= ResourceLoader.Load<Texture2D>(BaseDotTexturePath);
        return cachedDotTexture;
    }

    private static Texture2D GetLightTexture()
    {
        cachedLightTexture ??= ResourceLoader.Load<Texture2D>(BaseLightTexturePath);
        return cachedLightTexture;
    }

    private static Material GetAdditiveMaterial()
    {
        cachedAdditiveMaterial ??= ResourceLoader.Load<Material>(BaseAdditiveMaterialPath);
        return cachedAdditiveMaterial;
    }

    private readonly struct EnergyArcDefinition
    {
        public readonly string Name;
        public readonly Vector2 Start;
        public readonly Vector2 Control;
        public readonly Vector2 End;
        public readonly Color Color;
        public readonly float Width;
        public readonly double Delay;

        public EnergyArcDefinition(string name, Vector2 start, Vector2 control, Vector2 end, Color color, float width, double delay)
        {
            Name = name;
            Start = start;
            Control = control;
            End = end;
            Color = color;
            Width = width;
            Delay = delay;
        }
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
public static class CharacterSelectFullArtOnOpenPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(NCharacterSelectScreen __instance)
    {
        CharacterSelectFullArtPatch.Apply(__instance);
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
public static class CharacterSelectFullArtPreloadPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix(NGame __instance)
    {
        CharacterSelectFullArtPatch.BeginAnimatedBackgroundPreload(__instance, true);
    }
}
