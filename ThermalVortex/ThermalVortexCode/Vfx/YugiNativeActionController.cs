using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace ThermalVortex.ThermalVortexCode.Vfx;

// One controller owns the live sprite. Legacy monster throws borrow it through
// a lease, so their timers cannot race the native idle/cast/hurt frame updates.
internal partial class YugiNativeActionController : Node
{
    private const string ControllerName = "YugiNativeActions";
    private const string ResourceRoot = MainFile.ResPath + "/images/charui/actions/ludo_native/";
    private Sprite2D _sprite;
    private Creature _creature;
    private Texture2D _staticTexture;
    private Vector2 _staticOffset;
    private Vector2 _staticScale;
    private Vector2 _staticFoot;
    private float _staticHeight;
    private bool _staticCentered;
    private bool _captured;
    private bool _ownsSprite;
    private bool _disabled;
    private bool _victoryGuardActive;
    private int _externalLeases;
    private Sequence _idle;
    private Sequence _hurt;
    private object _hurtCombat;
    private Sequence _cast;
    private object _castCombat;
    private Sequence _current;
    private double _elapsed;
    private int _lastFrame = -1;
    private TaskCompletionSource<bool> _completion;
    private VfxAudioPlayback _audio;
    private Shader _hurtRecoveryShader;
    private ShaderMaterial _hurtRecoveryMaterial;
    private Material _materialBeforeHurtRecovery;
    private bool _hurtRecoveryActive;

    private const string HurtRecoveryShaderCode = """
        shader_type canvas_item;
        uniform sampler2D idle_texture : filter_linear, repeat_disable;
        uniform vec2 idle_uv_scale;
        uniform vec2 idle_uv_offset;
        uniform float recovery_weight = 0.0;
        varying vec4 sprite_color;
        void vertex() {
            sprite_color = COLOR;
        }
        void fragment() {
            vec4 hurt = texture(TEXTURE, UV);
            vec2 idle_uv = UV * idle_uv_scale + idle_uv_offset;
            vec4 idle = texture(idle_texture, idle_uv);
            if (any(lessThan(idle_uv, vec2(0.0))) || any(greaterThan(idle_uv, vec2(1.0)))) {
                idle = vec4(0.0);
            }
            float alpha = mix(hurt.a, idle.a, recovery_weight);
            vec3 premultiplied = mix(hurt.rgb * hurt.a, idle.rgb * idle.a, recovery_weight);
            COLOR = vec4(premultiplied / max(alpha, 0.00001), alpha) * sprite_color;
        }
        """;

    internal static void Attach(Sprite2D sprite, Creature creature)
    {
        if (!GodotObject.IsInstanceValid(sprite)
            || sprite.GetNodeOrNull<Node>(ControllerName) is not null)
            return;
        sprite.AddChild(new YugiNativeActionController
        {
            Name = ControllerName,
            _sprite = sprite,
            _creature = creature,
            ProcessMode = ProcessModeEnum.Inherit
        });
    }

    internal static Task<bool> PlayAsync(CardModel card, CardPlay play, string action)
    {
        // Copied effects and repeated resolutions retain their gameplay but do
        // not replay the actor's full gesture for a different physical card.
        if (card is null || !play.IsFirstInSeries || !ReferenceEquals(play.Card, card))
            return Task.FromResult(false);
        try
        {
            var sprite = card.Owner?.Creature?.GetCreatureNode()?.Visuals?
                .FindChild("YugiSprite", true, false) as Sprite2D;
            var controller = sprite?.GetNodeOrNull<YugiNativeActionController>(ControllerName);
            return controller?.BeginCast(action) ?? Task.FromResult(false);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Yugi native cast skipped: " + exception.Message);
            return Task.FromResult(false);
        }
    }

    internal static IDisposable SuspendForExternal(Sprite2D sprite) =>
        sprite?.GetNodeOrNull<YugiNativeActionController>(ControllerName)?.BorrowSprite();

    internal static bool TakeVictoryGuard(Sprite2D sprite)
    {
        var controller = sprite?.GetNodeOrNull<YugiNativeActionController>(ControllerName);
        if (controller is null || controller.DeathLocked || controller._victoryGuardActive)
            return false;
        // Claim ownership before interrupting callbacks that would restore idle.
        controller._victoryGuardActive = true;
        try
        {
            MonsterPlayActionVfx.Interrupt(sprite);
            controller.StopCast(true, resumeIdle: false);
            controller.StopHurt(resumeIdle: false);
            controller.ClearHurtRecovery();
            controller._externalLeases = 0;
            controller._current = null;
            controller._ownsSprite = false;
            return true;
        }
        catch
        {
            controller._victoryGuardActive = false;
            throw;
        }
    }

    internal static void ReleaseVictoryGuard(Sprite2D sprite)
    {
        var controller = sprite?.GetNodeOrNull<YugiNativeActionController>(ControllerName);
        if (controller is not null)
            controller._victoryGuardActive = false;
    }

    internal static bool TryPlayHurt(Creature creature)
    {
        try
        {
            var sprite = creature?.GetCreatureNode()?.Visuals?
                .FindChild("YugiSprite", true, false) as Sprite2D;
            return sprite?.GetNodeOrNull<YugiNativeActionController>(ControllerName)?.BeginHurt() == true;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Yugi native hurt skipped: " + exception.Message);
            return false;
        }
    }

    public override void _Ready()
    {
        try
        {
            CaptureStatic();
            _idle = LoadSequence("idle");
            _hurt = LoadSequence("hurt");
        }
        catch (Exception exception)
        {
            DisableAfterFailure(exception);
        }
    }

    public override void _Process(double delta)
    {
        // This victory presentation owns the actor even after WinRun marks it dead.
        if (_victoryGuardActive)
            return;
        // Death owns its clock even after the combat manager declares a loss.
        // It must run before the live-action and external-lease early exits.
        if (UpdateDeath(delta))
            return;
        if (_disabled || !_captured || _externalLeases > 0)
            return;
        try
        {
            if (!CanAnimate())
            {
                StopCast(false);
                StopHurt(resumeIdle: false);
                RestoreStatic();
                _current = null;
                return;
            }
            if (_cast is not null && !ReferenceEquals(_creature.CombatState, _castCombat))
            {
                StopCast(false, resumeIdle: false);
                return;
            }
            if (_hurtCombat is not null && !ReferenceEquals(_creature.CombatState, _hurtCombat))
            {
                StopHurt(resumeIdle: false);
                return;
            }
            if (_current is null)
                StartIdle();
            if (_current is null)
                return;
            if (double.IsFinite(delta) && delta > 0)
                _elapsed += delta;
            if (_current == _idle)
                _elapsed %= _current.Duration;
            else if (_elapsed >= _current.Duration)
            {
                if (_current == _hurt)
                    StopHurt();
                else
                    StopCast(true);
                return;
            }
            var frame = Math.Min(_current.LastMotionFrame, _current.StartFrame + (int)(_elapsed * _current.Fps));
            ShowFrame(frame);
            UpdateHurtRecovery();
            if (_cast is not null && frame >= _cast.ReleaseFrame)
                _completion?.TrySetResult(true);
        }
        catch (Exception exception)
        {
            DisableAfterFailure(exception);
        }
    }

    public override void _ExitTree()
    {
        // NGameOverScreen moves the same Visuals to its foreground during one
        // synchronous call. That move must not finish the pending recall.
        if ((DeathLocked || _victoryGuardActive) && MovingVisualsToGameOver)
            return;
        FinishDeath();
        StopCast(false, resumeIdle: false);
        StopHurt(resumeIdle: false);
        RestoreStatic();
        _current = null;
        _idle?.Dispose();
        _idle = null;
        _hurt?.Dispose();
        _hurt = null;
        _hurtRecoveryMaterial?.Dispose();
        _hurtRecoveryMaterial = null;
        _hurtRecoveryShader?.Dispose();
        _hurtRecoveryShader = null;
    }

    private bool CanAnimate() => GodotObject.IsInstanceValid(_sprite)
        && _sprite.IsInsideTree() && _creature?.CombatState is not null
        && !DeathLocked && !_victoryGuardActive && _creature.IsDead == false && CombatManager.Instance?.IsInProgress == true;

    private bool BeginHurt()
    {
        if (_disabled || !_captured || _hurt is null || !CanAnimate())
            return false;
        try
        {
            // Cancel the old throw before it can restore its saved texture.
            MonsterPlayActionVfx.Interrupt(_sprite);
            if (_externalLeases > 0)
                return false;
            // A hurt reaction interrupts the visual, not the card's gameplay.
            // Report the already-started cast as presented to avoid replaying
            // a fallback cast on top of this reaction.
            StopCast(true, resumeIdle: false);
            StopHurt(resumeIdle: false);
            _hurtCombat = _creature.CombatState;
            // Native AnyState Hit restarts hurt even when hurt is already active.
            // The authored start_frame is an immediate hurt pose; never queue
            // reactions or wait for the visual before resolving more damage.
            SetSequence(_hurt);
            return true;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Yugi native hurt unavailable: " + exception.Message);
            StopHurt();
            return false;
        }
    }

    private void StopHurt(bool resumeIdle = true)
    {
        var wasActive = _hurtCombat is not null;
        _hurtCombat = null;
        if (!wasActive)
            return;
        if (resumeIdle && _externalLeases == 0 && CanAnimate())
            StartIdle();
        else
        {
            RestoreStatic();
            _current = null;
        }
    }

    private Task<bool> BeginCast(string action)
    {
        if (_disabled || !_captured || _externalLeases > 0
            || (_completion is not null && !_completion.Task.IsCompleted)
            || !CanAnimate() || (action != "single" && action != "grand"))
            return Task.FromResult(false);
        try
        {
            var sequence = LoadSequence(action);
            if (sequence is null)
                return Task.FromResult(false);
            // A resolved card may be followed by another cast during recovery.
            // End the old owner synchronously before assigning the next one.
            StopHurt(resumeIdle: false);
            StopCast(false, resumeIdle: false);
            _cast = sequence;
            _castCombat = _creature.CombatState;
            _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completion = _completion.Task;
            SetSequence(sequence);
            _audio = VfxAudioPlayback.TryStart(this, MainFile.ResPath + "/audio/ludo_cast/" + action + "_aligned.ogg");
            return completion;
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Yugi native cast unavailable: " + exception.Message);
            StopCast(false);
            return Task.FromResult(false);
        }
    }

    private IDisposable BorrowSprite()
    {
        if (DeathLocked || _victoryGuardActive)
            return null;
        if (_externalLeases++ == 0)
        {
            StopCast(false, resumeIdle: false);
            StopHurt(resumeIdle: false);
            RestoreStatic();
            _current = null;
        }
        return new SpriteLease(this);
    }

    private void ReturnSprite()
    {
        if (_externalLeases <= 0)
            return;
        _externalLeases--;
        if (_externalLeases == 0 && !_disabled && IsInsideTree() && CanAnimate())
            StartIdle();
    }

    private void CaptureStatic()
    {
        if (!GodotObject.IsInstanceValid(_sprite) || _sprite.Texture is null)
            throw new InvalidOperationException("YugiSprite has no static fallback texture.");
        _staticTexture = _sprite.Texture;
        _staticOffset = _sprite.Offset;
        _staticScale = _sprite.Scale;
        _staticCentered = _sprite.Centered;
        using var image = _staticTexture.GetImage();
        var rect = image?.GetUsedRect() ?? new Rect2I(Vector2I.Zero, (Vector2I)_staticTexture.GetSize());
        if (rect.Size.Y <= 0)
            throw new InvalidOperationException("Yugi static texture has no visible character.");
        _staticHeight = rect.Size.Y;
        var footX = rect.Position.X + rect.Size.X * 0.5f;
        if (image is not null)
        {
            // Coat tails can bias the whole-body bbox. Match the authored
            // anchor to the two boots using only the bottom 15% of the body.
            var bottomStart = rect.Position.Y + (int)(rect.Size.Y * 0.85f);
            using var feet = image.GetRegion(new Rect2I(0, bottomStart, image.GetWidth(), rect.End.Y - bottomStart));
            var feetRect = feet.GetUsedRect();
            if (feetRect.Size.X > 0)
                footX = feetRect.Position.X + feetRect.Size.X * 0.5f;
        }
        var origin = _staticCentered ? _staticTexture.GetSize() * 0.5f : Vector2.Zero;
        _staticFoot = _staticOffset + new Vector2(footX, rect.End.Y) - origin;
        _captured = true;
    }

    private void SetSequence(Sequence sequence)
    {
        if (DeathLocked || _victoryGuardActive)
            return;
        ClearHurtRecovery();
        _current = sequence;
        _elapsed = 0;
        _lastFrame = -1;
        var ratio = _staticHeight / sequence.CharacterHeight;
        _sprite.Scale = _staticScale * ratio;
        _sprite.Centered = true;
        _sprite.Offset = _staticFoot / ratio - sequence.FootAnchor + sequence.Canvas * 0.5f;
        _ownsSprite = true;
        ShowFrame(sequence.StartFrame);
    }

    private void StartIdle()
    {
        if (DeathLocked)
            return;
        if (_idle is not null && _externalLeases == 0 && !_disabled)
            SetSequence(_idle);
        else
        {
            RestoreStatic();
            _current = null;
        }
    }

    private void ShowFrame(int frame)
    {
        if (frame == _lastFrame)
            return;
        _sprite.Texture = _current.Frames[frame];
        _lastFrame = frame;
    }

    private void UpdateHurtRecovery()
    {
        if (_current != _hurt || _idle is null || _hurt.RecoveryBlendSeconds <= 0)
            return;
        var start = _hurt.Duration - _hurt.RecoveryBlendSeconds;
        if (_elapsed < start)
            return;
        if (!_hurtRecoveryActive)
        {
            if (_hurtRecoveryMaterial is null)
            {
                _hurtRecoveryShader = new Shader { Code = HurtRecoveryShaderCode };
                _hurtRecoveryMaterial = new ShaderMaterial { Shader = _hurtRecoveryShader };
                var ratio = _idle.CharacterHeight / _hurt.CharacterHeight;
                // Sample the idle pose in the current hurt canvas using the
                // same body height and boot anchor as SetSequence. Switching
                // canvases at weight 1 therefore cannot move the character.
                _hurtRecoveryMaterial.SetShaderParameter("idle_texture", _idle.Frames[_idle.StartFrame]);
                _hurtRecoveryMaterial.SetShaderParameter("idle_uv_scale", _hurt.Canvas * ratio / _idle.Canvas);
                _hurtRecoveryMaterial.SetShaderParameter("idle_uv_offset", (_idle.FootAnchor - _hurt.FootAnchor * ratio) / _idle.Canvas);
            }
            _materialBeforeHurtRecovery = _sprite.Material;
            _sprite.Material = _hurtRecoveryMaterial;
            _hurtRecoveryActive = true;
        }
        var weight = (float)Math.Clamp((_elapsed - start) / _hurt.RecoveryBlendSeconds, 0, 1);
        weight = weight * weight * (3f - 2f * weight);
        _hurtRecoveryMaterial.SetShaderParameter("recovery_weight", weight);
    }

    private void ClearHurtRecovery()
    {
        if (!_hurtRecoveryActive)
            return;
        if (GodotObject.IsInstanceValid(_sprite))
            _sprite.Material = _materialBeforeHurtRecovery;
        _materialBeforeHurtRecovery = null;
        _hurtRecoveryActive = false;
    }

    private void StopCast(bool completed, bool resumeIdle = true)
    {
        _audio?.Dispose();
        _audio = null;
        var completion = _completion;
        _completion = null;
        var previous = _cast;
        _cast = null;
        _castCombat = null;
        try
        {
            if (previous is not null)
            {
                if (resumeIdle && _externalLeases == 0 && CanAnimate())
                    StartIdle();
                else
                {
                    RestoreStatic();
                    _current = null;
                }
            }
        }
        catch (Exception exception)
        {
            completed = false;
            MainFile.Logger.Info("Yugi native animation restoration failed: " + exception.Message);
        }
        finally
        {
            try
            {
                previous?.Dispose();
            }
            finally
            {
                // Even a disappearing node or failed cleanup must release the
                // card's awaited visual task so gameplay cannot get stuck.
                completion?.TrySetResult(completed);
            }
        }
    }

    private void RestoreStatic()
    {
        ClearHurtRecovery();
        if (DeathLocked || _victoryGuardActive || !_ownsSprite || !GodotObject.IsInstanceValid(_sprite))
            return;
        _sprite.Texture = _staticTexture;
        _sprite.Offset = _staticOffset;
        _sprite.Scale = _staticScale;
        _sprite.Centered = _staticCentered;
        _ownsSprite = false;
    }

    private void DisableAfterFailure(Exception exception)
    {
        if (DeathLocked)
        {
            FinishDeath();
            MainFile.Logger.Info("Yugi death presentation stopped: " + exception.Message);
            return;
        }
        _disabled = true;
        StopCast(false, resumeIdle: false);
        StopHurt(resumeIdle: false);
        RestoreStatic();
        _current = null;
        MainFile.Logger.Info("Yugi native animation disabled; keeping static art: " + exception.Message);
    }

    private static Sequence LoadSequence(string action)
    {
        var folder = ResourceRoot + action + "/";
        var frames = new List<Texture2D>();
        try
        {
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(folder + "sequence.json"));
            var root = document.RootElement;
            var fps = root.GetProperty("fps").GetDouble();
            var count = root.GetProperty("frame_count").GetInt32();
            var height = root.GetProperty("character_height").GetSingle();
            var releaseFrame = root.TryGetProperty("release_frame", out var release)
                && release.ValueKind == JsonValueKind.Number ? release.GetInt32() : 0;
            var startFrame = root.TryGetProperty("start_frame", out var start)
                ? start.GetInt32() : 0;
            var lastMotionFrame = root.TryGetProperty("last_motion_frame", out var lastMotion)
                ? lastMotion.GetInt32() : count - 1;
            var recoveryBlendSeconds = root.TryGetProperty("recovery_blend_seconds", out var recovery)
                ? recovery.GetDouble() : 0;
            var foot = ReadVector(root.GetProperty("foot_anchor"));
            var canvas = ReadVector(root.GetProperty("canvas"), dimensions: true);
            if (!double.IsFinite(fps) || fps <= 0 || fps > 60 || count != 25
                || !float.IsFinite(height) || height <= 0 || canvas.X <= 0 || canvas.Y <= 0
                || !foot.IsFinite() || releaseFrame < 0 || releaseFrame >= count
                || startFrame < 0 || startFrame >= count
                || lastMotionFrame < startFrame || lastMotionFrame >= count
                || !double.IsFinite(recoveryBlendSeconds) || recoveryBlendSeconds < 0
                || recoveryBlendSeconds > (count - startFrame) / fps)
                throw new InvalidOperationException("Invalid native animation metadata: " + action);
            for (var index = 0; index < count; index++)
            {
                var texture = LoadFrame($"{folder}frame_{index:000}.png");
                frames.Add(texture);
                if (texture.GetSize() != canvas)
                    throw new InvalidOperationException("Native animation canvas mismatch: " + action);
            }
            return new Sequence(frames.ToArray(), fps, canvas, foot, height, releaseFrame, startFrame, lastMotionFrame, recoveryBlendSeconds);
        }
        catch (Exception exception)
        {
            foreach (var texture in frames)
                texture.Dispose();
            MainFile.Logger.Info("Could not load native character sequence " + action + ": " + exception.Message);
            return null;
        }
    }

    private static Vector2 ReadVector(JsonElement value, bool dimensions = false) => value.ValueKind == JsonValueKind.Array
        ? new Vector2(value[0].GetSingle(), value[1].GetSingle())
        : new Vector2(value.GetProperty(dimensions ? "width" : "x").GetSingle(), value.GetProperty(dimensions ? "height" : "y").GetSingle());

    private static Texture2D LoadFrame(string path)
    {
        if (ResourceLoader.Exists(path))
        {
            var imported = ResourceLoader.Load<Texture2D>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
            if (imported is not null)
                return imported;
        }
        using var image = new Image();
        if (!Godot.FileAccess.FileExists(path) || image.LoadPngFromBuffer(Godot.FileAccess.GetFileAsBytes(path)) != Error.Ok)
            throw new InvalidOperationException("Missing or invalid native animation frame: " + path);
        return ImageTexture.CreateFromImage(image)
            ?? throw new InvalidOperationException("Could not create native animation texture: " + path);
    }

    private sealed class Sequence(Texture2D[] frames, double fps, Vector2 canvas, Vector2 footAnchor, float characterHeight, int releaseFrame, int startFrame, int lastMotionFrame, double recoveryBlendSeconds) : IDisposable
    {
        internal Texture2D[] Frames { get; } = frames;
        internal double Fps { get; } = fps;
        internal double Duration => (Frames.Length - StartFrame) / Fps;
        internal Vector2 Canvas { get; } = canvas;
        internal Vector2 FootAnchor { get; } = footAnchor;
        internal float CharacterHeight { get; } = characterHeight;
        internal int ReleaseFrame { get; } = releaseFrame;
        internal int StartFrame { get; } = startFrame;
        // Retain the authored duration while holding the last stable pose in
        // place of a bad generated tail frame. Source PNGs remain untouched.
        internal int LastMotionFrame { get; } = lastMotionFrame;
        internal double RecoveryBlendSeconds { get; } = recoveryBlendSeconds;
        public void Dispose()
        {
            foreach (var frame in Frames)
                frame.Dispose();
        }
    }

    private sealed class SpriteLease(YugiNativeActionController owner) : IDisposable
    {
        private YugiNativeActionController _owner = owner;
        public void Dispose()
        {
            var current = _owner;
            _owner = null;
            if (GodotObject.IsInstanceValid(current))
                current.ReturnSprite();
        }
    }
}
