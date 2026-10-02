using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal partial class YugiNativeActionController
{
    private const double DeathDuration = 2.1;
    private const string DeathResourceRoot = ResourceRoot + "death/";
    private const string DeathScenePath = MainFile.ResPath + "/vfx/yugi_death_recall/yugi_death_recall.tscn";
    private enum DeathVisualState { None, WaitingForGameOver, Dying, DeadHidden }
    private DeathVisualState _deathState;
    private Node2D _deathVisual;
    private RecallResources _deathResources;
    private TaskCompletionSource _deathCompletion;
    private object _deathCombat;
    private double _deathElapsed;
    private bool _visibleBeforeDeath;
    private ProcessModeEnum _processModeBeforeDeath;
    private bool DeathLocked => _deathState != DeathVisualState.None;
    internal static bool MovingVisualsToGameOver { get; set; }

    internal static bool IsDeathLocked(Sprite2D sprite) => GodotObject.IsInstanceValid(sprite)
        && sprite.GetNodeOrNull<YugiNativeActionController>(ControllerName) is { } controller
        && (controller.DeathLocked || controller._victoryGuardActive);

    // A party defeat must reach the native game-over transition before the
    // recall starts. Only deaths outside that screen hold the command here.
    internal static Task GetDeathCompletion(Creature creature)
    {
        var controller = FindController(creature);
        return controller?._deathState == DeathVisualState.Dying
            ? controller._deathCompletion?.Task ?? Task.CompletedTask
            : Task.CompletedTask;
    }

    internal static bool TryBeginDeath(Creature creature, out Task completion, out double remainingSeconds)
    {
        completion = Task.CompletedTask;
        remainingSeconds = 0;
        var controller = FindController(creature);
        if (controller is null || controller._victoryGuardActive || !controller.BeginDeath())
            return false;
        completion = controller._deathState == DeathVisualState.WaitingForGameOver
            ? Task.CompletedTask
            : controller._deathCompletion?.Task ?? Task.CompletedTask;
        remainingSeconds = controller._deathState == DeathVisualState.Dying
            ? Math.Max(0, DeathDuration - controller._deathElapsed) : 0;
        return true;
    }

    internal static Task PlayPendingGameOverDeaths(Node gameOverScreen)
    {
        var completions = new List<Task>();
        // The native screen reparents the original Visuals here; the original
        // NCreature is no longer their ancestor. Find the retained controllers.
        foreach (var controller in gameOverScreen.FindChildren(ControllerName, "", true, false)
                     .OfType<YugiNativeActionController>())
        {
            if (controller._deathState != DeathVisualState.WaitingForGameOver)
                continue;
            if (!GodotObject.IsInstanceValid(controller._deathVisual)
                || !controller._deathVisual.IsInsideTree())
            {
                controller.FinishDeath();
                continue;
            }
            controller._deathElapsed = 0;
            controller._deathState = DeathVisualState.Dying;
            completions.Add(controller._deathCompletion?.Task ?? Task.CompletedTask);
        }
        return Task.WhenAll(completions);
    }

    private static YugiNativeActionController FindController(Creature creature)
    {
        try
        {
            var visuals = creature?.GetCreatureNode()?.Visuals;
            var sprite = GodotObject.IsInstanceValid(visuals)
                ? visuals.FindChild("YugiSprite", true, false) as Sprite2D : null;
            return GodotObject.IsInstanceValid(sprite)
                ? sprite.GetNodeOrNull<YugiNativeActionController>(ControllerName) : null;
        }
        catch (ObjectDisposedException) { return null; }
    }

    private bool BeginDeath()
    {
        if (DeathLocked)
            return true;
        if (!_captured || !GodotObject.IsInstanceValid(_sprite) || !_sprite.IsInsideTree()
            || _sprite.GetParent() is not Node2D parent)
            return false;

        _deathCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runState = _creature?.Player?.RunState;
        var waitForGameOver = runState?.IsGameOver == true && runState.CurrentRoom?.IsVictoryRoom != true;
        _deathState = waitForGameOver ? DeathVisualState.WaitingForGameOver : DeathVisualState.Dying;
        _deathElapsed = 0;
        _deathCombat = _creature?.CombatState;
        _visibleBeforeDeath = _sprite.Visible;
        _processModeBeforeDeath = ProcessMode;
        ProcessMode = ProcessModeEnum.Always;
        try
        {
            // Freeze the actual current texture before a cast disposal or the
            // legacy throw's synchronous restoration can release/change it.
            var initialTransform = GetPixelTransform(_sprite);
            var foot = _staticFoot;
            if (_sprite.FlipH)
                foot.X = 2 * _staticOffset.X - foot.X;
            if (_sprite.FlipV)
                foot.Y = 2 * _staticOffset.Y - foot.Y;
            // Cancel only the native sequence's scale; retain the current
            // sprite position, rotation, skew and the shared parent's transform.
            var footOrigin = _sprite.Transform * (foot * _staticScale / _sprite.Scale);
            var targetHeight = _staticHeight * Math.Abs(_staticScale.Y);
            var facingSign = Math.Sign(_staticScale.X) * (_sprite.FlipH ? -1 : 1);
            _deathResources = new RecallResources();
            if (_sprite.Texture is not null)
            {
                using var initialImage = _sprite.Texture.GetImage();
                _deathResources.InitialTexture = ImageTexture.CreateFromImage(initialImage);
            }

            // Set the lock first: returning a borrowed sprite may otherwise
            // restart idle. Interrupt completes its old restoration synchronously.
            CancelLiveActionsForDeath();

            var config = _deathResources.Load();
            config["target_height"] = targetHeight;
            config["foot_origin"] = footOrigin;
            config["facing_sign"] = facingSign;
            var scene = ResourceLoader.Load<PackedScene>(DeathScenePath)
                ?? throw new InvalidOperationException("Death recall scene is missing.");
            _deathVisual = scene.Instantiate<Node2D>();
            _deathVisual.Name = "YugiDeathRecall";
            _deathVisual.ZIndex = _sprite.ZIndex;
            _deathVisual.Modulate = _sprite.Modulate * _sprite.SelfModulate;
            parent.AddChild(_deathVisual);
            if (!_deathVisual.Call("setup", _deathResources.Body, _deathResources.Rift, config,
                    _deathResources.InitialTexture, initialTransform).AsBool())
                throw new InvalidOperationException("Death recall setup rejected its resources.");
            _sprite.Visible = false;
            _deathVisual.Call("seek", 0.0);
        }
        catch (Exception exception)
        {
            // Missing art is a presentation failure, never a pending game-over.
            MainFile.Logger.Info("Yugi death recall unavailable; leaving actor hidden: " + exception.Message);
            try { CancelLiveActionsForDeath(); }
            finally { FinishDeath(); }
        }
        return true;
    }

    private void CancelLiveActionsForDeath()
    {
        // Each cleanup runs even if an earlier resource/texture was disposed.
        try { MonsterPlayActionVfx.Interrupt(_sprite); }
        finally
        {
            try { StopCast(true, resumeIdle: false); }
            finally
            {
                try { StopHurt(resumeIdle: false); }
                finally
                {
                    ClearHurtRecovery();
                    _externalLeases = 0;
                    _current = null;
                    _ownsSprite = true;
                }
            }
        }
    }

    private static Transform2D GetPixelTransform(Sprite2D sprite)
    {
        var size = sprite.Texture?.GetSize() ?? Vector2.Zero;
        var pixelOrigin = sprite.Offset - (sprite.Centered ? size * 0.5f : Vector2.Zero);
        var flip = new Transform2D(new Vector2(sprite.FlipH ? -1 : 1, 0),
            new Vector2(0, sprite.FlipV ? -1 : 1),
            new Vector2(sprite.FlipH ? size.X : 0, sprite.FlipV ? size.Y : 0));
        return sprite.Transform * new Transform2D(0, pixelOrigin) * flip;
    }

    private bool UpdateDeath(double delta)
    {
        if (!DeathLocked)
            return false;
        try
        {
            if (!GodotObject.IsInstanceValid(_sprite) || !_sprite.IsInsideTree())
            {
                FinishDeath();
                return true;
            }
            // The normal phoenix interception never starts death. This branch
            // also supports a genuine later resurrection of this same creature.
            if (_creature is { IsDead: false } && _creature.CurrentHp > 0)
            {
                RestoreAfterRevival();
                return false;
            }
            _sprite.Visible = false;
            if (_deathState == DeathVisualState.DeadHidden)
                return true;
            var currentCombat = _creature?.CombatState;
            if ((currentCombat is not null && !ReferenceEquals(_deathCombat, currentCombat))
                || !GodotObject.IsInstanceValid(_deathVisual) || !_deathVisual.IsInsideTree())
            {
                FinishDeath();
                return true;
            }
            // Keep the captured actor at seek(0) while the original red
            // backstop transitions in. Its own completed tween starts recall.
            if (_deathState == DeathVisualState.WaitingForGameOver)
            {
                if (_creature?.Player?.RunState.IsGameOver == true)
                    return true;
                // A teammate's death-prevention hook can cancel a provisional
                // party loss. The still-dead actor then uses normal recall.
                _deathState = DeathVisualState.Dying;
            }
            if (double.IsFinite(delta) && delta > 0)
                _deathElapsed += delta;
            var complete = _deathVisual.Call("seek", Math.Min(_deathElapsed, DeathDuration)).AsBool();
            if (complete || _deathElapsed >= DeathDuration)
                FinishDeath();
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Yugi death recall interrupted: " + exception.Message);
            FinishDeath();
        }
        return true;
    }

    private void FinishDeath()
    {
        if (!DeathLocked)
            return;
        _deathState = DeathVisualState.DeadHidden;
        if (GodotObject.IsInstanceValid(_sprite))
            _sprite.Visible = false;
        try
        {
            var visual = _deathVisual;
            _deathVisual = null;
            if (GodotObject.IsInstanceValid(visual))
            {
                try { visual.Call("clear"); }
                finally { visual.QueueFree(); }
            }
        }
        catch (Exception exception)
        {
            MainFile.Logger.Info("Yugi death recall cleanup: " + exception.Message);
        }
        finally
        {
            try
            {
                var resources = _deathResources;
                _deathResources = null;
                resources?.Dispose();
            }
            catch (Exception exception) { MainFile.Logger.Info("Yugi death resource cleanup: " + exception.Message); }
            finally { _deathCompletion?.TrySetResult(); }
        }
    }

    private void RestoreAfterRevival()
    {
        FinishDeath();
        _deathState = DeathVisualState.None;
        _deathCombat = null;
        _deathCompletion = null;
        ProcessMode = _processModeBeforeDeath;
        RestoreStatic();
        _sprite.Visible = _visibleBeforeDeath;
        _current = null;
        if (CanAnimate())
            StartIdle();
    }

    private sealed class RecallResources : IDisposable
    {
        internal readonly Godot.Collections.Array<Texture2D> Body = new();
        internal readonly Godot.Collections.Array<Texture2D> Rift = new();
        internal ImageTexture InitialTexture;

        internal GDictionary Load()
        {
            using var metadata = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(DeathResourceRoot + "metadata.json"));
            var root = metadata.RootElement;
            var anchors = root.GetProperty("body_foot_anchors");
            var multipliers = root.GetProperty("body_scale_multipliers");
            var height = root.GetProperty("body_reference_height").GetSingle();
            if (anchors.GetArrayLength() != 36 || multipliers.GetArrayLength() != 36
                || !float.IsFinite(height) || height <= 0)
                throw new InvalidOperationException("Invalid death recall placement metadata.");
            var footAnchors = new GArray();
            var scales = new GArray();
            for (var i = 0; i < 36; i++)
            {
                var foot = ReadVector(anchors[i]);
                var multiplier = multipliers[i].GetSingle();
                if (!foot.IsFinite() || !float.IsFinite(multiplier) || multiplier <= 0)
                    throw new InvalidOperationException("Invalid death recall frame placement.");
                footAnchors.Add(foot);
                scales.Add(multiplier);
                Body.Add(LoadCheckedFrame($"body/frame_{i:000}.png", new Vector2(806, 615)));
            }
            for (var i = 4; i <= 28; i++)
                Rift.Add(LoadCheckedFrame($"rift/frame_{i:000}.png", new Vector2(1120, 1120)));
            return new GDictionary
            {
                ["body_reference_height"] = height,
                ["body_foot_anchors"] = footAnchors,
                ["body_scale_multipliers"] = scales
            };
        }

        private static Texture2D LoadCheckedFrame(string relativePath, Vector2 canvas)
        {
            var frame = LoadFrame(DeathResourceRoot + relativePath);
            if (frame.GetSize() == canvas)
                return frame;
            frame.Dispose();
            throw new InvalidOperationException("Death recall canvas mismatch: " + relativePath);
        }

        public void Dispose()
        {
            foreach (var frame in Body) frame.Dispose();
            foreach (var frame in Rift) frame.Dispose();
            Body.Clear();
            Rift.Clear();
            InitialTexture?.Dispose();
            InitialTexture = null;
        }
    }
}
