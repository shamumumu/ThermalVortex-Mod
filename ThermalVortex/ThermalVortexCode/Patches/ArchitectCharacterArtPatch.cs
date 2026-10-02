using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using ThermalVortex.ThermalVortexCode.Character;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal static class ArchitectCharacterArt
{
    private const string TextureFileName = "ending_visual_yami.png";

    // Measured character bounds in the existing 768 x 512 battle image.
    private static readonly Vector2 OriginalTextureSize = new(768f, 512f);
    private static readonly Vector2 OriginalFootAnchor = new(383.5f, 495f);
    private const float OriginalBodyHeight = 478f;

    // Measured in the 1536 x 1024 illustration, excluding its aura. As in the original,
    // the anchor uses the body's horizontal bounding-box center and its lowest sole.
    private const float EndingBodyTop = 10f / 1024f;
    private static readonly Vector2 EndingFootAnchor = new(783.5f / 1536f, 1005f / 1024f);

    private static readonly ConditionalWeakTable<Sprite2D, OriginalTransform> OriginalTransforms = new();
    private static bool loggedApplyFailure;

    internal static bool TryPrepare(Sprite2D sprite, Player player, out PreparedArt prepared)
    {
        prepared = null;
        if (!MillenniumPuzzleCharacterArt.IsThermalVortex(player)
            || !GodotObject.IsInstanceValid(sprite) || sprite.Texture is null)
            return false;

        try
        {
            // Preparation only loads the image and computes its transform; the caller controls
            // when to apply it, after its opaque blackout has entered the scene.
            var texture = MillenniumPuzzleCharacterArt.LoadTexture(player, TextureFileName, TextureFileName);
            if (texture is null)
                return false;

            var original = OriginalTransforms.GetValue(
                sprite,
                static target => new OriginalTransform(target.Position, target.Scale));
            var textureSize = texture.GetSize();
            var bodyHeight = (EndingFootAnchor.Y - EndingBodyTop) * textureSize.Y;
            if (bodyHeight <= 0f)
                return false;

            var scale = OriginalBodyHeight / bodyHeight;
            var originalAnchor = GetLocalAnchor(sprite, OriginalFootAnchor, OriginalTextureSize);
            var endingAnchor = GetLocalAnchor(sprite, EndingFootAnchor * textureSize, textureSize);

            prepared = new PreparedArt(
                sprite,
                texture,
                original.Position + (originalAnchor - endingAnchor * scale) * original.Scale,
                original.Scale * scale);
            return true;
        }
        catch (Exception exception)
        {
            LogFailure(exception);
            return false;
        }
    }

    internal static bool TryApply(PreparedArt prepared)
    {
        var sprite = prepared?.Sprite;
        if (!GodotObject.IsInstanceValid(sprite) || !sprite.IsInsideTree())
            return false;

        var previousTexture = sprite.Texture;
        var previousPosition = sprite.Position;
        var previousScale = sprite.Scale;
        try
        {
            // Absolute transforms remain idempotent even when a static fallback is requested.
            sprite.Texture = prepared.Texture;
            sprite.Scale = prepared.Scale;
            sprite.Position = prepared.Position;
            return true;
        }
        catch (Exception exception)
        {
            if (GodotObject.IsInstanceValid(sprite))
            {
                sprite.Texture = previousTexture;
                sprite.Position = previousPosition;
                sprite.Scale = previousScale;
            }

            LogFailure(exception);
            return false;
        }
    }

    private static Vector2 GetLocalAnchor(Sprite2D sprite, Vector2 pixelAnchor, Vector2 textureSize)
    {
        var anchor = pixelAnchor;
        if (sprite.Centered)
            anchor -= textureSize * 0.5f;
        if (sprite.FlipH)
            anchor.X = -anchor.X;
        if (sprite.FlipV)
            anchor.Y = -anchor.Y;
        // Offset translates the entire sprite; texture mirroring does not reverse that translation.
        return anchor + sprite.Offset;
    }

    private sealed record OriginalTransform(Vector2 Position, Vector2 Scale);

    internal sealed record PreparedArt(Sprite2D Sprite, Texture2D Texture, Vector2 Position, Vector2 Scale);

    private static void LogFailure(Exception exception)
    {
        if (loggedApplyFailure)
            return;
        loggedApplyFailure = true;
        MainFile.Logger.Info($"Could not apply ThermalVortex architect character art error={exception}");
    }
}
