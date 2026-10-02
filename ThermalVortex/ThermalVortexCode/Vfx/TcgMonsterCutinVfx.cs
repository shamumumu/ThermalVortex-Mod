using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Vfx;

// The previous Cyber*SummonVfx players and assets remain available as backups.
internal static class TcgMonsterCutinVfx
{
    private const string ResourceRoot = MainFile.ResPath + "/images/vfx/tcg_monster_cutin/";
    private const int FramesPerLoadTick = 3;
    private static readonly SemaphoreSlim PlaybackGate = new(1, 1);
    private static Sequence _cachedSequence;
    private static Task<Sequence> _loadTask;
    private static string _loadingKey;
    private static int _loadVersion;

    internal static bool HasSummonCutin(CardModel card) => GetKey(card) is not null;

    internal static Task PlaySummonAsync(CardModel card, CardPlay play) =>
        play.IsFirstInSeries && ReferenceEquals(play.Card, card)
            ? ThermalVortexCombatVfx.PlaySafeAsync(nameof(TcgMonsterCutinVfx), () => PlayAsync(card))
            : Task.CompletedTask;

    internal static void StartPreload(CardModel card)
    {
        var key = GetKey(card);
        if (key is null || PlaybackGate.CurrentCount == 0 || !TryGetTree(out var tree))
            return;

        try
        {
            _ = GetOrStartLoad(tree, key);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("TCG monster cut-in preload failed: " + ex);
        }
    }

    internal static async Task PlayAsync(CardModel card)
    {
        var key = GetKey(card);
        if (key is null || !TryGetTree(out var tree))
            return;

        var entered = false;
        CanvasLayer layer = null;
        VfxAudioPlayback audio = null;
        try
        {
            var combat = card.Owner?.Creature?.CombatState;
            bool CombatStillActive() => combat is not null
                && ReferenceEquals(card.Owner?.Creature?.CombatState, combat)
                && CombatManager.Instance.IsInProgress;
            if (!CombatStillActive())
                return;

            // Concurrent summons must not dispose each other's textures or stack black screens.
            entered = await PlaybackGate.WaitAsync(TimeSpan.FromSeconds(15));
            if (!entered || !IsTreeAlive(tree) || !CombatStillActive())
                return;

            var data = await GetOrStartLoad(tree, key);
            if (data is null || !IsTreeAlive(tree) || !CombatStillActive())
                return;

            var viewport = tree.Root.GetVisibleRect();
            if (viewport.Size.X <= 0f || viewport.Size.Y <= 0f)
                return;

            layer = new CanvasLayer
            {
                Name = "ThermalVortexTcgMonsterCutin",
                Layer = 135,
                // This sequence already advances by wall clock while paused.
                ProcessMode = Node.ProcessModeEnum.Always
            };
            var overlay = new ColorRect
            {
                Name = "TcgMonsterCutinDim",
                Position = viewport.Position,
                Size = viewport.Size,
                Color = new Color(0f, 0f, 0f, 0f),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            var sequence = new TextureRect
            {
                Name = "TcgMonsterCutinSequence",
                Texture = data.Frames[0],
                Position = viewport.Position,
                Size = viewport.Size,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = new Color(1f, 1f, 1f, 0f)
            };
            layer.AddChild(overlay);
            layer.AddChild(sequence);
            tree.Root.AddChild(layer);

            var exited = false;
            void OnExit() => exited = true;
            layer.TreeExiting += OnExit;
            try
            {
                var startedAt = Stopwatch.GetTimestamp();
                var audioPath = GetAudioPath(key);
                if (audioPath is not null)
                    audio = VfxAudioPlayback.TryStart(layer, audioPath);
                var lastFrame = -1;
                var fadeIn = Math.Min(0.16d, data.Duration * 0.12d);
                var fadeOut = Math.Min(0.22d, data.Duration * 0.15d);
                while (!exited && IsTreeAlive(tree) && CombatStillActive()
                    && GodotObject.IsInstanceValid(sequence))
                {
                    var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
                    if (elapsed >= data.Duration)
                        break;

                    var frame = Math.Min(data.Frames.Length - 1, (int)(elapsed * data.Fps));
                    if (frame != lastFrame)
                    {
                        sequence.Texture = data.Frames[frame];
                        lastFrame = frame;
                    }

                    var opacity = (float)Math.Min(
                        Math.Clamp(elapsed / fadeIn, 0d, 1d),
                        Math.Clamp((data.Duration - elapsed) / fadeOut, 0d, 1d));
                    overlay.Color = new Color(0f, 0f, 0f, opacity * 0.82f);
                    sequence.Modulate = new Color(1f, 1f, 1f, opacity);
                    if (!await WaitForNextFrameAsync(tree, layer))
                        break;
                }
            }
            finally
            {
                if (GodotObject.IsInstanceValid(layer))
                    layer.TreeExiting -= OnExit;
            }
        }
        catch (Exception ex)
        {
            // A visual/resource failure must never prevent a card's effect from resolving.
            MainFile.Logger.Info($"TCG monster cut-in failed key={key}: {ex}");
        }
        finally
        {
            audio?.Dispose();
            if (GodotObject.IsInstanceValid(layer))
            {
                // Hide immediately; QueueFree completes at the end of the current frame.
                layer.Visible = false;
                layer.QueueFree();
            }
            if (entered)
                PlaybackGate.Release();
        }
    }

    private static string GetKey(CardModel card) => card switch
    {
        CyberEndDragon => "cyber_end_dragon",
        CyberDragonInfinity => "cyber_dragon_infinity",
        DarkMagician => "dark_magician",
        BlueEyesWhiteDragon => "blue_eyes_white_dragon",
        PhantomSummoningGodExodia => "phantom_summoning_god_exodia",
        WingedDragonOfRa => "winged_dragon_of_ra",
        WingedDragonOfRaPhoenix => "winged_dragon_of_ra_phoenix",
        DivineArsenalFurnaceGod => "divine_arsenal_furnace_god",
        _ => null
    };

    private static string GetAudioPath(string key) => key switch
    {
        "cyber_end_dragon" or "cyber_dragon_infinity" or "blue_eyes_white_dragon"
            or "divine_arsenal_furnace_god" => MainFile.ResPath + "/audio/vfx/monster_cutin_light.ogg",
        "dark_magician" or "phantom_summoning_god_exodia" => MainFile.ResPath + "/audio/vfx/monster_cutin_dark.ogg",
        "winged_dragon_of_ra" or "winged_dragon_of_ra_phoenix" => MainFile.ResPath + "/audio/vfx/monster_cutin_divine.ogg",
        _ => null
    };

    private static Task<Sequence> GetOrStartLoad(SceneTree tree, string key)
    {
        if (_cachedSequence?.Key == key)
            return Task.FromResult(_cachedSequence);
        if (_loadingKey == key && _loadTask is { IsCompleted: false })
            return _loadTask;

        var version = ++_loadVersion;
        _cachedSequence?.Dispose();
        _cachedSequence = null;
        _loadingKey = key;
        _loadTask = LoadSequenceAsync(tree, key, version);
        return _loadTask;
    }

    private static async Task<Sequence> LoadSequenceAsync(SceneTree tree, string key, int version)
    {
        Sequence data = null;
        var cached = false;
        try
        {
            if (!await WaitForNextFrameAsync(tree, tree.Root))
                return null;

            var directory = ResourceRoot + key + "/";
            var metadataPath = directory + "sequence.json";
            if (!Godot.FileAccess.FileExists(metadataPath))
                throw new InvalidOperationException("Missing cut-in metadata: " + metadataPath);

            using var metadata = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(metadataPath));
            var root = metadata.RootElement;
            var count = root.GetProperty("frameCount").GetInt32();
            var fps = root.GetProperty("fps").GetDouble();
            var duration = root.GetProperty("duration").GetDouble();
            if (count < 1 || count > 1800 || !double.IsFinite(fps) || fps <= 0d
                || !double.IsFinite(duration) || duration <= 0d || duration > 60d)
                throw new InvalidOperationException("Invalid cut-in sequence metadata: " + metadataPath);

            data = new Sequence(key, new Texture2D[count], fps, duration);
            for (var i = 0; i < count; i++)
            {
                if (version != _loadVersion || !IsTreeAlive(tree))
                    return null;

                data.Frames[i] = LoadFrame($"{directory}frame_{i:000}.png");
                if ((i + 1) % FramesPerLoadTick == 0 && i + 1 < count
                    && !await WaitForNextFrameAsync(tree, tree.Root))
                    return null;
            }

            if (version != _loadVersion)
                return null;

            _cachedSequence = data;
            cached = true;
            MainFile.Logger.Info($"TCG monster cut-in loaded key={key} frames={count} duration={duration:0.###}s.");
            return data;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"TCG monster cut-in loading failed key={key}: {ex}");
            return null;
        }
        finally
        {
            if (!cached)
                data?.Dispose();
        }
    }

    private static Texture2D LoadFrame(string path)
    {
        // PckPacker imports PNGs as .ctex and retains a .png.import remap, not
        // the raw PNG. ResourceLoader follows that mapping in the installed PCK.
        // IgnoreDeep keeps these textures out of Godot's shared resource cache,
        // so this sequence can dispose them when another monster is loaded.
        if (ResourceLoader.Exists(path))
        {
            var importedTexture = ResourceLoader.Load<Texture2D>(
                path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
            if (importedTexture is not null)
                return importedTexture;
        }

        // Retain a raw-PNG fallback for unimported source assets.
        if (!Godot.FileAccess.FileExists(path))
            throw new InvalidOperationException("Missing cut-in frame: " + path);

        var bytes = Godot.FileAccess.GetFileAsBytes(path);
        using var image = new Image();
        var error = image.LoadPngFromBuffer(bytes);
        if (error != Error.Ok)
            throw new InvalidOperationException($"Could not decode cut-in frame {path}: {error}");

        return ImageTexture.CreateFromImage(image)
            ?? throw new InvalidOperationException("Could not create cut-in texture: " + path);
    }

    private static bool TryGetTree(out SceneTree tree)
    {
        tree = Engine.GetMainLoop() as SceneTree;
        return IsTreeAlive(tree);
    }

    private static bool IsTreeAlive(SceneTree tree) =>
        GodotObject.IsInstanceValid(tree)
        && GodotObject.IsInstanceValid(tree.Root)
        && tree.Root.IsInsideTree();

    private static async Task<bool> WaitForNextFrameAsync(SceneTree tree, Node owner)
    {
        if (!IsTreeAlive(tree) || !GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree())
            return false;

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void NextFrame() => done.TrySetResult(true);
        void Exiting() => done.TrySetResult(false);
        tree.ProcessFrame += NextFrame;
        owner.TreeExiting += Exiting;
        try
        {
            // Unlike awaiting a bare Godot signal, this also finishes when the
            // owner exits or frames stop arriving, releasing the caller's wait.
            return await done.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(tree))
                tree.ProcessFrame -= NextFrame;
            if (GodotObject.IsInstanceValid(owner))
                owner.TreeExiting -= Exiting;
        }
    }

    private sealed class Sequence(string key, Texture2D[] frames, double fps, double duration) : IDisposable
    {
        internal string Key { get; } = key;
        internal Texture2D[] Frames { get; } = frames;
        internal double Fps { get; } = fps;
        internal double Duration { get; } = duration;

        public void Dispose()
        {
            foreach (var frame in Frames)
                frame?.Dispose();
        }
    }
}
