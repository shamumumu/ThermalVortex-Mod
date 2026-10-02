using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using ThermalVortex.ThermalVortexCode.Character;

namespace ThermalVortex.ThermalVortexCode.Vfx;

// Bound to one Architect, one player and their retained Visuals. This node never
// holds up a gameplay Task, including while the final guarded pose is held.
internal partial class ArchitectVictoryGuardVfx : Node
{
    private const string NodeName = "ArchitectVictoryGuard";
    private const string Root = MainFile.ResPath + "/vfx/yugi_victory_guard/";
    private const string FrameRoot = MainFile.ResPath + "/images/charui/actions/victory_guard/";
    private const string NativeLightningPath = "res://scenes/vfx/vfx_attack_lightning.tscn";
    private static readonly int[] FrameNumbers = [0, 2, 5, 8, 12, 14, 16, 17];
    private static readonly MethodInfo ArchitectGetter = AccessTools.PropertyGetter(typeof(TheArchitect), "ArchitectCreature");
    private static readonly ConditionalWeakTable<Creature, ArchitectVictoryGuardVfx> ByArchitect = new();
    private static readonly ConditionalWeakTable<Creature, ArchitectVictoryGuardVfx> ByPlayer = new();

    private Creature _architect;
    private TheArchitect _endingEvent;
    private Creature _player;
    private Sprite2D _source;
    private Node2D _visual;
    private Node2D _lightning;
    private Node _bridge;
    private PackedScene _nativeScene;
    private GDScript _bridgeScript;
    private readonly Godot.Collections.Array<Texture2D> _frames = new();
    private Texture2D _previousTexture;
    private Transform2D _previousTransform;
    private Vector2 _previousOffset;
    private bool _previousCentered;
    private bool _previousVisible;
    private bool _active;
    private bool _closed;
    private bool _hit;
    private bool _rayReplaced;
    private bool _fireConsumed;
    private bool _ownsSprite;
    private double _windup;
    private double _elapsed;
    private double _impactAge;

    internal static void Prepare(TheArchitect architect)
    {
        if (!MillenniumPuzzleCharacterArt.IsThermalVortex(architect?.Owner)
            || !LocalContext.IsMe(architect.Owner)
            || architect.Node is not NCombatEventLayout layout || !layout.IsInsideTree())
            return;
        var attacker = ArchitectGetter?.Invoke(architect, null) as Creature;
        var player = architect.Owner.Creature;
        var source = layout.EmbeddedCombatRoom?.GetCreatureNode(player)?.Visuals?
            .FindChild("YugiSprite", true, false) as Sprite2D;
        if (attacker is null || !GodotObject.IsInstanceValid(source) || !source.IsInsideTree()
            || source.GetNodeOrNull<Node>(NodeName) is not null)
            return;

        var host = new ArchitectVictoryGuardVfx
        {
            Name = NodeName, _architect = attacker, _endingEvent = architect, _player = player, _source = source,
            ProcessPriority = 20
        };
        try
        {
            // Everything that can load or fail is prepared before the attack.
            // The live actor remains visible and its pose remains unchanged.
            source.AddChild(host);
            host.PrepareResources();
            ByArchitect.Remove(attacker);
            ByPlayer.Remove(player);
            ByArchitect.Add(attacker, host);
            ByPlayer.Add(player, host);
        }
        catch (Exception exception)
        {
            host.Close();
            if (GodotObject.IsInstanceValid(host)) host.QueueFree();
            LogFailure("preparation", exception);
        }
    }

    internal static void OnArchitectAttack(Creature architect)
    {
        if (architect is null || !ByArchitect.TryGetValue(architect, out var host)
            || !GodotObject.IsInstanceValid(host) || host._closed || host._active)
            return;
        try { host.Begin(); }
        catch (Exception exception)
        {
            host.Close();
            LogFailure("start", exception);
        }
    }

    internal static void FinishEnding(TheArchitect architect, bool succeeded)
    {
        var player = architect?.Owner?.Creature;
        if (player is null || !ByPlayer.TryGetValue(player, out var host)
            || !GodotObject.IsInstanceValid(host) || !ReferenceEquals(host._endingEvent, architect))
            return;
        if (!succeeded || !host._active)
            host.Close();
    }

    internal static bool IsActive(Creature player) => player is not null
        && ByPlayer.TryGetValue(player, out var host) && GodotObject.IsInstanceValid(host)
        && !host._closed && host._active;

    internal static bool TryReplaceLightning(Creature player)
    {
        if (!IsActive(player) || player.IsDead)
            return false;
        var host = ByPlayer.GetValue(player, _ => throw new InvalidOperationException());
        if (host._rayReplaced)
            return false;
        try
        {
            var target = player.GetCreatureNode();
            var container = player.GetVfxContainer();
            if (!GodotObject.IsInstanceValid(target) || !GodotObject.IsInstanceValid(container)
                || !container.IsInsideTree())
                throw new InvalidOperationException("Native lightning target is no longer in its scene.");
            host._lightning = host._nativeScene.Instantiate<Node2D>();
            host._bridge = new Node { Name = "GuardContact" };
            host._bridge.SetScript(host._bridgeScript);
            host._lightning.AddChild(host._bridge);
            host._bridge.Connect("impacted", Callable.From(host.OnImpact));
            container.AddChild(host._lightning);
            // Match native VfxCmd.PlayVfx: GlobalPosition replaces the authored
            // scene-root offset; the five internal positions remain untouched.
            host._lightning.GlobalPosition = target.GlobalPosition;
            if (!host._bridge.Call("setup", host._lightning, host._visual).AsBool())
                throw new InvalidOperationException("Native lightning contact track could not be prepared.");
            host._rayReplaced = true;
            return true;
        }
        catch (Exception exception)
        {
            host.Close();
            LogFailure("lightning", exception);
            return false;
        }
    }

    internal static bool ShouldSuppressFire(Creature player)
    {
        if (!IsActive(player))
            return false;
        var host = ByPlayer.GetValue(player, _ => throw new InvalidOperationException());
        if (!host._rayReplaced || host._fireConsumed)
            return false;
        host._fireConsumed = true;
        return true;
    }

    private void PrepareResources()
    {
        if (_source.Texture is null || _source.GetParent() is not Node2D parent)
            throw new InvalidOperationException("Victory actor has no usable visual parent.");
        _nativeScene = ResourceLoader.Load<PackedScene>(NativeLightningPath)
            ?? throw new InvalidOperationException("Native lightning scene is missing.");
        _bridgeScript = ResourceLoader.Load<GDScript>(Root + "lightning_bridge.gd")
            ?? throw new InvalidOperationException("Lightning contact script is missing.");
        var scene = ResourceLoader.Load<PackedScene>(Root + "yugi_victory_guard.tscn")
            ?? throw new InvalidOperationException("Victory guard scene is missing.");
        foreach (var number in FrameNumbers)
        {
            var path = $"{FrameRoot}frame_{number:000}.png";
            Texture2D texture = null;
            if (ResourceLoader.Exists(path))
                texture = ResourceLoader.Load<Texture2D>(path, cacheMode: ResourceLoader.CacheMode.IgnoreDeep);
            if (texture is null)
            {
                using var image = new Image();
                if (!Godot.FileAccess.FileExists(path)
                    || image.LoadPngFromBuffer(Godot.FileAccess.GetFileAsBytes(path)) != Error.Ok)
                    throw new InvalidOperationException("Missing victory frame: " + path);
                texture = ImageTexture.CreateFromImage(image);
            }
            _frames.Add(texture);
            if (texture.GetSize() != new Vector2(386, 534))
                throw new InvalidOperationException("Victory frame dimensions do not match: " + path);
        }

        _previousTexture = _source.Texture;
        _previousTransform = _source.Transform;
        _previousOffset = _source.Offset;
        _previousCentered = _source.Centered;
        _previousVisible = _source.Visible;
        var canvas = _source.Texture.GetSize();
        var ending = canvas == new Vector2(1536, 1024);
        var foot = ending ? new Vector2(783.5f, 1005f) : new Vector2(383.5f, 495f) * canvas / new Vector2(768, 512);
        var height = ending ? 995f : 478f * canvas.Y / 512f;
        var localFoot = foot - (_source.Centered ? canvas * 0.5f : Vector2.Zero);
        if (_source.FlipH) localFoot.X = -localFoot.X;
        if (_source.FlipV) localFoot.Y = -localFoot.Y;
        var config = new Godot.Collections.Dictionary
        {
            ["foot_origin"] = _source.Transform * (localFoot + _source.Offset),
            ["target_height"] = height * Math.Abs(_source.Scale.Y),
            ["facing_sign"] = Math.Sign(_source.Scale.X) * (_source.FlipH ? -1 : 1)
        };
        _visual = scene.Instantiate<Node2D>();
        _visual.Name = "YugiVictoryGuardVisual";
        _visual.Visible = false;
        _visual.ZIndex = _source.ZIndex;
        _visual.Modulate = _source.Modulate * _source.SelfModulate;
        parent.AddChild(_visual);
        if (!_visual.Call("setup", _frames, config).AsBool())
            throw new InvalidOperationException("Victory guard scene rejected its frame placement.");
        _visual.Call("seek", 0d, -1d);
        SetProcess(false);
    }

    private void Begin()
    {
        if (!IsInsideTree() || !GodotObject.IsInstanceValid(_visual) || !_visual.IsInsideTree())
            return;
        _windup = SaveManager.Instance?.PrefsSave?.FastMode switch
        {
            FastModeType.Fast => 0.25d,
            FastModeType.Instant => 0d,
            _ => 0.5d
        };
        if (!YugiNativeActionController.TakeVictoryGuard(_source))
            throw new InvalidOperationException("Victory actor is owned by another terminal presentation.");
        _ownsSprite = true;
        _active = true;
        _elapsed = 0;
        _source.Visible = false;
        _visual.Visible = true;
        _visual.Call("seek", 0d, -1d);
        SetProcess(true);
    }

    private void OnImpact()
    {
        if (_closed || !_active || _hit)
            return;
        _hit = true;
        _impactAge = 0;
        // The bridge has already snapped the shared body/shield scene before
        // emitting this signal on the native texture's exact contact update.
        try { NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Normal, -1f); }
        catch (Exception exception) { LogFailure("impact shake", exception); }
    }

    public override void _Process(double delta)
    {
        if (_closed || !_active)
            return;
        try
        {
            if (!GodotObject.IsInstanceValid(_source) || !GodotObject.IsInstanceValid(_visual)
                || !_source.IsInsideTree() || !_visual.IsInsideTree())
            {
                Close();
                return;
            }
            if (double.IsFinite(delta) && delta > 0)
                _elapsed += delta;
            if (_hit)
            {
                _impactAge += Math.Max(0, delta);
                _visual.Call("seek", 1d, _impactAge);
            }
            else
            {
                // Anticipated progress only chooses in-between poses. It is
                // capped below completion until the native contact callback.
                var progress = _windup <= 0 ? 0d : Math.Clamp(_elapsed / (_windup + 1d / 15d), 0d, 0.97d);
                if (_rayReplaced && GodotObject.IsInstanceValid(_bridge)
                    && _lightning?.GetNodeOrNull<AnimationPlayer>("AnimationPlayer") is { } animation)
                {
                    var contact = _bridge.Get("contact_time").AsDouble();
                    progress = _windup <= 0 ? 0d : Math.Clamp(
                        (_windup + animation.CurrentAnimationPosition) / (_windup + contact), 0d, 0.97d);
                }
                _visual.Call("seek", progress, -1d);
                if (_elapsed > _windup + 1d)
                    throw new InvalidOperationException("Native lightning ended without reaching its contact callback.");
            }
            if (GodotObject.IsInstanceValid(_bridge)) _bridge.Call("update_clip");
        }
        catch (Exception exception)
        {
            Close();
            LogFailure("playback", exception);
        }
    }

    public override void _ExitTree()
    {
        // Native game-over synchronously reparents this same Visuals subtree.
        // Keep the final pose and textures across that move, then release them
        // when the resulting summary (or the original room) actually exits.
        if (_active && YugiNativeActionController.MovingVisualsToGameOver)
            return;
        Close();
    }

    private void Close()
    {
        if (_closed)
            return;
        _closed = true;
        _active = false;
        SetProcess(false);
        if (_architect is not null && ByArchitect.TryGetValue(_architect, out var owner) && ReferenceEquals(owner, this))
            ByArchitect.Remove(_architect);
        if (_player is not null && ByPlayer.TryGetValue(_player, out owner) && ReferenceEquals(owner, this))
            ByPlayer.Remove(_player);
        try
        {
            if (GodotObject.IsInstanceValid(_lightning))
            {
                _lightning.Visible = false;
                _lightning.QueueFree();
            }
            if (GodotObject.IsInstanceValid(_visual))
            {
                _visual.Visible = false;
                _visual.Call("clear");
                _visual.QueueFree();
            }
        }
        finally
        {
            if (_ownsSprite && GodotObject.IsInstanceValid(_source))
            {
                YugiNativeActionController.ReleaseVictoryGuard(_source);
                _source.Texture = _previousTexture;
                _source.Transform = _previousTransform;
                _source.Offset = _previousOffset;
                _source.Centered = _previousCentered;
                _source.Visible = _previousVisible;
            }
            _ownsSprite = false;
            foreach (var frame in _frames) frame.Dispose();
            _frames.Clear();
            _bridge = null;
            _lightning = null;
            _visual = null;
        }
    }

    private static void LogFailure(string stage, Exception exception) =>
        MainFile.Logger.Info($"Architect victory guard {stage} unavailable; continuing native ending: {exception.Message}");
}
