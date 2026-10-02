using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class MonsterPlayActionVfx
{
    private const int FrameCount = 22;
    private const double FrameSeconds = 1d / 24d;
    private const double ThrowCueSeconds = 8d / 24d;
    private const double TimeoutSeconds = 1.15d;
    private const string FramePathPrefix = MainFile.ResPath + "/images/charui/actions/monster_play/frame_";
    private static Texture2D[] _frames;
    private static readonly ConditionalWeakTable<Sprite2D, SpritePlaybackState> PlaybackStates = new();

    internal static int PlayCount { get; private set; }

    internal static void Interrupt(Sprite2D sprite)
    {
        if (sprite is not null && PlaybackStates.TryGetValue(sprite, out var state))
            state.Cancel?.Invoke();
    }

    public static async Task PlayAsync(Creature self)
    {
        if (self?.IsEnemy == true)
            return;

        var sprite = ResolveSprite(self);
        if (sprite is null)
        {
            MainFile.Logger.Info("Monster play action VFX skipped: YugiSprite is unavailable.");
            return;
        }
        if (YugiNativeActionController.IsDeathLocked(sprite))
            return;

        if (sprite.GetTree() is not SceneTree tree)
        {
            MainFile.Logger.Info("Monster play action VFX skipped: YugiSprite is not attached to a scene tree.");
            return;
        }

        var frames = LoadFrames();
        if (frames is null)
        {
            return;
        }

        Interrupt(sprite);
        if (YugiNativeActionController.IsDeathLocked(sprite))
            return;
        var nativeLease = YugiNativeActionController.SuspendForExternal(sprite);
        var playbackState = PlaybackStates.GetValue(sprite, static _ => new SpritePlaybackState());
        var idleTexture = playbackState.ActivePlaybackId == 0
            ? sprite.Texture
            : playbackState.RestoreTexture ?? sprite.Texture;
        var playbackId = ++playbackState.NextPlaybackId;
        playbackState.ActivePlaybackId = playbackId;
        playbackState.RestoreTexture = idleTexture;
        PlayCount++;
        var cue = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cueSent = false;
        var completed = false;

        void SignalCue()
        {
            if (cueSent)
            {
                return;
            }

            cueSent = true;
            cue.TrySetResult();
        }

        void Complete()
        {
            if (completed)
            {
                return;
            }

            completed = true;
            SignalCue();

            try
            {
                if (playbackState.ActivePlaybackId == playbackId)
                {
                    playbackState.ActivePlaybackId = 0;
                    playbackState.RestoreTexture = null;
                    playbackState.Cancel = null;
                    if (GodotObject.IsInstanceValid(sprite))
                    {
                        sprite.Texture = idleTexture;
                    }
                }
            }
            finally
            {
                nativeLease?.Dispose();
            }
        }

        playbackState.Cancel = Complete;
        _ = PlaySequenceAsync(sprite, frames, playbackState, playbackId, SignalCue, Complete);

        var cueTimer = tree.CreateTimer(ThrowCueSeconds);
        cueTimer.Timeout += SignalCue;
        var timeout = tree.CreateTimer(TimeoutSeconds);
        timeout.Timeout += Complete;

        await cue.Task;
    }

    private static Sprite2D ResolveSprite(Creature self)
    {
        var visuals = self?.GetCreatureNode()?.Visuals;
        return visuals?.FindChild("YugiSprite", true, false) as Sprite2D;
    }

    private static Texture2D[] LoadFrames()
    {
        if (_frames is not null)
        {
            return _frames;
        }

        var frames = new Texture2D[FrameCount];
        for (var i = 0; i < frames.Length; i++)
        {
            var path = $"{FramePathPrefix}{i:000}.png";
            if (!ResourceLoader.Exists(path))
            {
                MainFile.Logger.Info("Could not find monster play action frame: " + path);
                return null;
            }

            frames[i] = ResourceLoader.Load<Texture2D>(path);
            if (frames[i] is null)
            {
                MainFile.Logger.Info("Could not load monster play action frame: " + path);
                return null;
            }
        }

        _frames = frames;
        return _frames;
    }

    private static async Task PlaySequenceAsync(
        Sprite2D sprite,
        Texture2D[] frames,
        SpritePlaybackState playbackState,
        int playbackId,
        Action signalCue,
        Action complete)
    {
        try
        {
            for (var i = 0; i < frames.Length; i++)
            {
                if (playbackState.ActivePlaybackId != playbackId || !GodotObject.IsInstanceValid(sprite))
                {
                    return;
                }

                sprite.Texture = frames[i];
                if (i == 8)
                {
                    signalCue();
                }

                await DelayAsync(sprite, FrameSeconds);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("Monster play action sequence failed: " + ex);
        }
        finally
        {
            complete();
        }
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

    private sealed class SpritePlaybackState
    {
        public int NextPlaybackId;
        public int ActivePlaybackId;
        public Texture2D RestoreTexture;
        public Action Cancel;
    }
}
