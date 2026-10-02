using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class ExodiaTorsoVfx
{
    private const string VideoPath = MainFile.ResPath + "/images/vfx/exodia_special_win/summon_special_win.ogv";
    private const string AudioPath = MainFile.ResPath + "/audio/vfx/exodia_special_win.ogg";
    private const double TimeoutSeconds = 15d;
    private static readonly Vector2 VideoSize = new(1280f, 720f);

    public static async Task PlayAsync(Creature self)
    {
        var combatState = self?.CombatState;
        if (combatState is null || Engine.GetMainLoop() is not SceneTree tree
            || !GodotObject.IsInstanceValid(tree.Root) || !tree.Root.IsInsideTree())
            return;

        var source = self.GetCreatureNode();
        if (!GodotObject.IsInstanceValid(source) || !source.IsInsideTree())
            return;

        bool CombatStillActive() => ReferenceEquals(self.CombatState, combatState)
            && CombatManager.Instance is { IsInProgress: true, IsOverOrEnding: false };
        if (!CombatStillActive())
            return;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CanvasLayer layer = null;
        ColorRect blackout = null;
        VideoStreamPlayer video = null;
        VideoStreamTheora stream = null;
        VfxAudioPlayback audio = null;

        void Complete()
        {
            audio?.Dispose();
            completion.TrySetResult();
        }

        void UpdateBounds()
        {
            var viewport = tree.Root.GetVisibleRect();
            blackout.Position = viewport.Position;
            blackout.Size = viewport.Size;
            var scale = Math.Min(viewport.Size.X / VideoSize.X, viewport.Size.Y / VideoSize.Y);
            video.Size = VideoSize * scale;
            video.Position = viewport.Position + (viewport.Size - video.Size) * 0.5f;
        }

        void OnFrame()
        {
            if (completion.Task.IsCompleted)
                return;
            try
            {
                if (!CombatStillActive() || !GodotObject.IsInstanceValid(source) || !source.IsInsideTree()
                    || !GodotObject.IsInstanceValid(layer) || !layer.IsInsideTree()
                    || !GodotObject.IsInstanceValid(video) || !video.IsPlaying())
                {
                    Complete();
                    return;
                }
                UpdateBounds();
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info("Exodia special victory playback interrupted: " + ex);
                Complete();
            }
        }

        try
        {
            // Prepare the native stream before covering the battle with a black screen.
            stream = ResourceLoader.Load<VideoStreamTheora>(VideoPath, cacheMode: ResourceLoader.CacheMode.Ignore);
            if (stream is null)
                throw new InvalidOperationException("Missing Exodia special victory video: " + VideoPath);

            layer = new CanvasLayer
            {
                Name = "ThermalVortexExodiaTorsoVfx",
                Layer = 134,
                ProcessMode = Node.ProcessModeEnum.Always
            };
            blackout = new ColorRect
            {
                Name = "ExodiaVictoryBlackout",
                Color = Colors.Black,
                MouseFilter = Control.MouseFilterEnum.Stop,
                MouseForcePassScrollEvents = false
            };
            video = new VideoStreamPlayer
            {
                Name = "ExodiaSpecialVictoryVideo",
                Stream = stream,
                Expand = true,
                Loop = false,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            layer.AddChild(blackout);
            layer.AddChild(video);
            UpdateBounds();
            video.Finished += Complete;
            layer.TreeExiting += Complete;
            source.TreeExiting += Complete;
            tree.ProcessFrame += OnFrame;
            tree.Root.AddChild(layer);
            video.Play();
            if (!video.IsPlaying())
                throw new InvalidOperationException("Exodia special victory video could not start.");
            audio = VfxAudioPlayback.TryStart(layer, AudioPath);

            // Victory follows the complete movie, with a wall-clock escape for decoder/scene failures.
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(TimeoutSeconds));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Exodia special victory video failed: " + ex);
        }
        finally
        {
            audio?.Dispose();
            if (GodotObject.IsInstanceValid(layer))
            {
                layer.Visible = false;
                layer.TreeExiting -= Complete;
            }
            try
            {
                if (GodotObject.IsInstanceValid(tree))
                    tree.ProcessFrame -= OnFrame;
                if (GodotObject.IsInstanceValid(source))
                    source.TreeExiting -= Complete;
                if (GodotObject.IsInstanceValid(video))
                {
                    video.Finished -= Complete;
                    video.Stop();
                    video.Stream = null;
                }
            }
            finally
            {
                if (GodotObject.IsInstanceValid(layer))
                {
                    if (layer.IsInsideTree())
                        layer.QueueFree();
                    else
                        layer.Free();
                }
                stream?.Dispose();
            }
        }
    }
}
