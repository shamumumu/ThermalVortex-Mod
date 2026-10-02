using Godot;
using MegaCrit.Sts2.Core.Saves;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal sealed class VfxAudioPlayback : IDisposable
{
    private static readonly StringName MasterBus = new("Master");
    private readonly Node _owner;
    private readonly SceneTree _tree;
    private readonly string _resourcePath;
    private AudioStreamPlayer _player;
    private AudioStreamOggVorbis _stream;
    private bool _attached;
    private bool _disposed;
    private bool _cleanupErrorLogged;

    private VfxAudioPlayback(Node owner, string resourcePath)
    {
        _owner = owner;
        _tree = owner.GetTree();
        _resourcePath = resourcePath;
    }

    internal static VfxAudioPlayback TryStart(Node owner, string resourcePath, float playbackSpeed = 1f)
    {
        if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree()
            || string.IsNullOrWhiteSpace(resourcePath)
            || !float.IsFinite(playbackSpeed) || playbackSpeed <= 0f)
            return null;

        VfxAudioPlayback playback = null;
        try
        {
            playback = new VfxAudioPlayback(owner, resourcePath);
            playback.Start(playbackSpeed);
            return playback;
        }
        catch (Exception ex)
        {
            playback?.Dispose();
            MainFile.Logger.Info($"VFX audio unavailable path={resourcePath}: {ex.Message}");
            return null;
        }
    }

    private void Start(float playbackSpeed)
    {
        // Native resource packs retain the source bytes. Loading them directly
        // also works before Godot has generated an editor import remap.
        if (!Godot.FileAccess.FileExists(_resourcePath))
            throw new InvalidOperationException("Audio resource was not found.");
        var bytes = Godot.FileAccess.GetFileAsBytes(_resourcePath);
        _stream = AudioStreamOggVorbis.LoadFromBuffer(bytes)
            ?? throw new InvalidOperationException("Audio resource could not be decoded.");
        _stream.Loop = false;
        _player = new AudioStreamPlayer
        {
            Name = "ThermalVortexVfxAudio",
            Stream = _stream,
            Bus = MasterBus,
            PitchScale = playbackSpeed,
            ProcessMode = Node.ProcessModeEnum.Inherit
        };
        _owner.AddChild(_player);
        _attached = true;
        _owner.TreeExiting += Dispose;
        _player.Finished += Dispose;
        _tree.ProcessFrame += OnFrame;
        RefreshVolume();
        _player.Play();
    }

    private void OnFrame()
    {
        if (_disposed)
            return;
        try
        {
            if (!GodotObject.IsInstanceValid(_owner) || !_owner.IsInsideTree()
                || !GodotObject.IsInstanceValid(_player) || !_player.IsInsideTree())
            {
                Dispose();
                return;
            }
            RefreshVolume();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"VFX audio interrupted path={_resourcePath}: {ex.Message}");
            Dispose();
        }
    }

    private void RefreshVolume()
    {
        var saves = SaveManager.Instance;
        if (saves?.PrefsSave?.MuteInBackground == true && !_tree.Root.HasFocus())
        {
            _player.VolumeLinear = 0f;
            return;
        }

        var masterIndex = AudioServer.GetBusIndex(MasterBus);
        var masterGain = masterIndex >= 0 ? AudioServer.GetBusVolumeLinear(masterIndex) : 1f;
        if (!float.IsFinite(masterGain) || masterGain <= 0f
            || (masterIndex >= 0 && AudioServer.IsBusMute(masterIndex)))
        {
            _player.VolumeLinear = 0f;
            return;
        }

        var settings = saves?.SettingsSave;
        if (settings is null)
        {
            _player.VolumeLinear = 1f;
            return;
        }

        // Both native audio managers square the settings sliders. The debug
        // manager also sets Godot's Master bus, so compensate for its existing
        // gain instead of applying the master setting a second time.
        var volume = ClampVolume(settings.VolumeMaster) * ClampVolume(settings.VolumeSfx);
        var playerGain = volume * volume / masterGain;
        _player.VolumeLinear = float.IsFinite(playerGain) ? playerGain : 0f;
    }

    private static float ClampVolume(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Each cleanup step is independent: a node already being destroyed
        // must not leave the frame subscription or the owned stream alive.
        Cleanup(() => { if (GodotObject.IsInstanceValid(_tree)) _tree.ProcessFrame -= OnFrame; });
        Cleanup(() => { if (GodotObject.IsInstanceValid(_owner)) _owner.TreeExiting -= Dispose; });
        Cleanup(() => { if (GodotObject.IsInstanceValid(_player)) _player.Finished -= Dispose; });
        Cleanup(() => { if (GodotObject.IsInstanceValid(_player)) _player.Stop(); });
        Cleanup(() => { if (GodotObject.IsInstanceValid(_player)) _player.Stream = null; });
        Cleanup(() =>
        {
            if (!GodotObject.IsInstanceValid(_player))
                return;
            if (_attached)
                _player.QueueFree();
            else
                _player.Free();
        });
        Cleanup(() => _stream?.Dispose());
        _player = null;
        _stream = null;
    }

    private void Cleanup(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (_cleanupErrorLogged)
                return;
            _cleanupErrorLogged = true;
            MainFile.Logger.Info($"VFX audio cleanup failed path={_resourcePath}: {ex.Message}");
        }
    }
}
