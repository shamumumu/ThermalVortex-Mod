using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class CyberDragonHitVfx
{
    private const string FrameRoot = MainFile.ResPath + "/images/vfx/cyber_dragon_hit/";
    private const string AnimationName = "hit";
    private const int FrameCount = 16;
    private const double DurationSeconds = 0.35d;
    private const double FadeOutSeconds = 0.10d;
    private const float DisplaySize = 280f;
    private static SpriteFrames _frames;
    private static bool _loadFailed;

    // AttackCommand adds this node at the same hit event as the original slash.
    // Its combat VFX container owns the node; the attack never waits for playback.
    internal static Node2D Create(Creature target)
    {
        if (_loadFailed)
            return Fallback(target);

        try
        {
            _frames ??= LoadFrames();
            var firstFrame = _frames.GetFrameTexture(AnimationName, 0);
            var scale = DisplaySize / Math.Max(firstFrame.GetWidth(), firstFrame.GetHeight());
            var sprite = new AnimatedSprite2D
            {
                Name = "CyberDragonHit",
                SpriteFrames = _frames,
                Animation = AnimationName,
                Centered = true,
                Scale = Vector2.One * scale,
                TextureFilter = CanvasItem.TextureFilterEnum.Linear
            };
            sprite.Ready += () => Start(sprite, target);
            sprite.AnimationFinished += () => Finish(sprite);
            return sprite;
        }
        catch (Exception exception)
        {
            _loadFailed = true;
            MainFile.Logger.Info("Cyber Dragon hit frames unavailable; using the original hit VFX: " + exception);
            return Fallback(target);
        }
    }

    private static void Start(AnimatedSprite2D sprite, Creature target)
    {
        try
        {
            var targetNode = target?.GetCreatureNode();
            if (!GodotObject.IsInstanceValid(targetNode) || !targetNode.IsInsideTree())
            {
                Finish(sprite);
                return;
            }

            // Match VfxCmd.PlayOnCreatureCenter after the native container adds us.
            sprite.GlobalPosition = targetNode.VfxSpawnPosition;
            sprite.Play(AnimationName);
            var fade = sprite.CreateTween();
            fade.TweenInterval(DurationSeconds - FadeOutSeconds);
            fade.TweenProperty(sprite, "modulate:a", 0f, FadeOutSeconds);
        }
        catch (Exception exception)
        {
            Finish(sprite);
            MainFile.Logger.Info("Cyber Dragon hit playback failed; using the original hit VFX: " + exception);
            ThermalVortexCombatVfx.PlayImpact(target);
        }
    }

    private static void Finish(AnimatedSprite2D sprite)
    {
        if (!GodotObject.IsInstanceValid(sprite) || sprite.IsQueuedForDeletion())
            return;
        sprite.Hide();
        sprite.QueueFree();
    }

    private static Node2D Fallback(Creature target)
    {
        ThermalVortexCombatVfx.PlayImpact(target);
        // The native AddChildSafely helper accepts a missing optional VFX node.
        return null;
    }

    private static SpriteFrames LoadFrames()
    {
        var frames = new SpriteFrames();
        try
        {
            frames.AddAnimation(AnimationName);
            frames.SetAnimationLoop(AnimationName, false);
            frames.SetAnimationSpeed(AnimationName, FrameCount / DurationSeconds);
            for (var index = 0; index < FrameCount; index++)
                frames.AddFrame(AnimationName, LoadFrame($"{FrameRoot}frame_{index:000}.png"));
            return frames;
        }
        catch
        {
            frames.Dispose();
            throw;
        }
    }

    private static Texture2D LoadFrame(string path)
    {
        // Installed PNG paths resolve through .png.import to packed .ctex data.
        if (ResourceLoader.Exists(path))
        {
            var texture = ResourceLoader.Load<Texture2D>(path);
            if (texture is not null)
                return texture;
        }

        // Keep source assets usable before Godot has generated import mappings.
        using var image = new Image();
        if (!Godot.FileAccess.FileExists(path)
            || image.LoadPngFromBuffer(Godot.FileAccess.GetFileAsBytes(path)) != Error.Ok)
            throw new InvalidOperationException("Missing or invalid Cyber Dragon hit frame: " + path);
        return ImageTexture.CreateFromImage(image)
            ?? throw new InvalidOperationException("Could not create Cyber Dragon hit texture: " + path);
    }
}
