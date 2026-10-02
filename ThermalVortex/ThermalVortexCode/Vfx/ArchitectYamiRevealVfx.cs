using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Events;
using ThermalVortex.ThermalVortexCode.Character;
using ThermalVortex.ThermalVortexCode.Patches;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class ArchitectYamiRevealVfx
{
    private const string EyeTextureFileName = "ending_visual_yami_eyes.png";
    private const string SpriteNodeName = "YugiSprite";
    private const double BlackHoldSeconds = 0.25d;
    private const double EyeFadeInSeconds = 0.25d;
    private const double EyeHoldSeconds = 0.30d;
    private const double RevealSeconds = 0.50d;
    private const double RevealStart = BlackHoldSeconds + EyeFadeInSeconds + EyeHoldSeconds;
    private const double Duration = RevealStart + RevealSeconds;
    private static bool loggedFailure;

    internal static Task PlayAsync(TheArchitect architect)
    {
        try
        {
            var player = architect?.Owner;
            if (!MillenniumPuzzleCharacterArt.IsThermalVortex(player) || !LocalContext.IsMe(player))
                return Task.CompletedTask;

            var scene = architect.Node as NCombatEventLayout;
            if (!GodotObject.IsInstanceValid(scene) || !scene.IsInsideTree())
                return Task.CompletedTask;

            var creature = scene.EmbeddedCombatRoom?.GetCreatureNode(player.Creature);
            var source = creature?.Visuals?.FindChild(SpriteNodeName, true, false) as Sprite2D;
            if (!GodotObject.IsInstanceValid(source) || !source.IsInsideTree() || !scene.IsAncestorOf(source))
                return Task.CompletedTask;

            // Load both textures before touching the live sprite or adding a blackout.
            if (!ArchitectCharacterArt.TryPrepare(source, player, out var prepared))
                return Task.CompletedTask;
            var eyeTexture = MillenniumPuzzleCharacterArt.LoadTexture(player, EyeTextureFileName, EyeTextureFileName);
            if (eyeTexture is null || eyeTexture.GetSize() != prepared.Texture.GetSize())
            {
                ArchitectCharacterArt.TryApply(prepared);
                return Task.CompletedTask;
            }

            return PlayPreparedAsync(scene, prepared, eyeTexture);
        }
        catch (Exception exception)
        {
            LogFailure(exception);
            return Task.CompletedTask;
        }
    }

    private static Task PlayPreparedAsync(
        NCombatEventLayout scene,
        ArchitectCharacterArt.PreparedArt prepared,
        Texture2D eyeTexture)
    {
        var source = prepared.Sprite;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CanvasLayer layer = null;
        ColorRect blackout = null;
        Sprite2D eyes = null;
        Tween tween = null;
        SceneTreeTimer timeout = null;
        var completed = false;

        void Complete()
        {
            if (completed)
                return;
            completed = true;

            try
            {
                // Release the screen before completing the awaited first-response task.
                if (GodotObject.IsInstanceValid(blackout))
                    blackout.MouseFilter = Control.MouseFilterEnum.Ignore;
                if (GodotObject.IsInstanceValid(layer))
                {
                    layer.Visible = false;
                    layer.TreeExiting -= Complete;
                    if (layer.IsInsideTree())
                        layer.QueueFree();
                    else
                        layer.Free();
                }
                if (GodotObject.IsInstanceValid(tween))
                {
                    tween.Finished -= Complete;
                    tween.Kill();
                }
                if (GodotObject.IsInstanceValid(timeout))
                    timeout.Timeout -= Complete;
                if (GodotObject.IsInstanceValid(scene))
                    scene.TreeExiting -= Complete;
                if (GodotObject.IsInstanceValid(source))
                    source.TreeExiting -= Complete;
            }
            catch (Exception exception)
            {
                LogFailure(exception);
            }
            finally
            {
                completion.TrySetResult();
            }
        }

        void Animate(double elapsed)
        {
            if (completed)
                return;
            if (!GodotObject.IsInstanceValid(scene) || !scene.IsInsideTree()
                || !GodotObject.IsInstanceValid(source) || !source.IsInsideTree()
                || !GodotObject.IsInstanceValid(layer) || !layer.IsInsideTree())
            {
                Complete();
                return;
            }

            try
            {
                var visibleRect = scene.GetViewport().GetVisibleRect();
                blackout.Position = visibleRect.Position;
                blackout.Size = visibleRect.Size;

                // Both textures have the same canvas coordinates. Recompute the full transform
                // each frame to follow camera motion, aspect-ratio changes and the character's pose.
                eyes.Transform = layer.GetFinalTransform().AffineInverse() * source.GetGlobalTransformWithCanvas();
                eyes.Centered = source.Centered;
                eyes.Offset = source.Offset;
                eyes.FlipH = source.FlipH;
                eyes.FlipV = source.FlipV;

                var reveal = SmoothStep((elapsed - RevealStart) / RevealSeconds);
                blackout.Color = new Color(0f, 0f, 0f, 1f - reveal);

                float eyeAlpha;
                if (elapsed < BlackHoldSeconds)
                    eyeAlpha = 0f;
                else if (elapsed < BlackHoldSeconds + EyeFadeInSeconds)
                    eyeAlpha = SmoothStep((elapsed - BlackHoldSeconds) / EyeFadeInSeconds);
                else if (elapsed < RevealStart)
                {
                    var pulse = (float)((elapsed - BlackHoldSeconds - EyeFadeInSeconds) / EyeHoldSeconds);
                    eyeAlpha = 1f - 0.14f * Mathf.Sin(Mathf.Pi * pulse);
                }
                else
                    eyeAlpha = 1f - reveal;

                eyes.Modulate = new Color(1f, 1f, 1f, eyeAlpha);
            }
            catch (Exception exception)
            {
                Complete();
                LogFailure(exception);
            }
        }

        try
        {
            layer = new CanvasLayer
            {
                Name = "ThermalVortexArchitectYamiReveal",
                Layer = 200,
                ProcessMode = Node.ProcessModeEnum.Always
            };
            blackout = new ColorRect
            {
                Name = "Blackout",
                Color = Colors.Black,
                Position = scene.GetViewport().GetVisibleRect().Position,
                Size = scene.GetViewport().GetVisibleRect().Size,
                MouseFilter = Control.MouseFilterEnum.Stop,
                MouseForcePassScrollEvents = false
            };
            eyes = new Sprite2D
            {
                Name = "YamiEyeEnergy",
                Texture = eyeTexture,
                Modulate = new Color(1f, 1f, 1f, 0f),
                TextureFilter = source.TextureFilter
            };

            layer.AddChild(blackout);
            layer.AddChild(eyes);
            scene.TreeExiting += Complete;
            source.TreeExiting += Complete;
            layer.TreeExiting += Complete;
            scene.AddChild(layer);

            // The full-screen opaque node is already attached before the visible artwork changes.
            if (!layer.IsInsideTree() || !ArchitectCharacterArt.TryApply(prepared))
            {
                Complete();
                return completion.Task;
            }
            Animate(0d);
            if (completed)
                return completion.Task;

            tween = layer.CreateTween();
            tween.SetPauseMode(Tween.TweenPauseMode.Process);
            tween.SetIgnoreTimeScale(true);
            tween.TweenMethod(Callable.From<double>(Animate), 0d, Duration, Duration);
            tween.Finished += Complete;

            // A separate bounded timeout also clears input blocking if a tween is interrupted.
            timeout = scene.GetTree().CreateTimer(Duration + 0.5d, true, false, true);
            timeout.Timeout += Complete;
        }
        catch (Exception exception)
        {
            Complete();
            LogFailure(exception);
        }

        return completion.Task;
    }

    private static float SmoothStep(double value)
    {
        var amount = (float)Math.Clamp(value, 0d, 1d);
        return amount * amount * (3f - 2f * amount);
    }

    private static void LogFailure(Exception exception)
    {
        if (loggedFailure)
            return;
        loggedFailure = true;
        MainFile.Logger.Info($"Could not play ThermalVortex architect reveal error={exception}");
    }

}
