using System.Diagnostics;
using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Vfx;

internal static class FusionSummonVfx
{
    private const string ScenePath = MainFile.ResPath + "/vfx/fusion/fusion_summon.tscn";
    // Keep the recovered timeline intact and play it at 80% of its original speed.
    private const double NormalPlaybackSpeed = 0.8d;
    private const double QueueTimeoutSeconds = 15d;
    private static readonly SemaphoreSlim PlaybackGate = new(1, 1);
    private static readonly StringName FinishedSignal = new("finished");

    internal static async Task PlayAsync(
        XyzMonsterCard summon,
        FusionMaterialVisualSnapshot materialVisuals,
        Creature fallbackTarget)
    {
        if (summon is null)
            return;

        TcgMonsterCutinVfx.StartPreload(summon);
        var speed = GetPlaybackSpeed();
        if (speed <= 0d || Engine.GetMainLoop() is not SceneTree tree || !IsTreeAlive(tree))
            return;

        var combat = summon.Owner?.Creature?.CombatState;
        bool CombatStillActive() => combat is not null
            && ReferenceEquals(summon.Owner?.Creature?.CombatState, combat)
            && summon.Owner?.Creature is { IsAlive: true }
            && !summon.HasBeenRemovedFromState
            && CombatManager.Instance?.IsInProgress == true
            && CombatManager.Instance?.IsOverOrEnding != true;

        if (!CombatStillActive() || !await EnterPlaybackGateAsync(tree, CombatStillActive))
            return;

        try
        {
            if (!CombatStillActive() || !IsTreeAlive(tree))
                return;

            var viewport = tree.Root.GetVisibleRect();
            if (viewport.Size.X <= 0f || viewport.Size.Y <= 0f)
                return;

            var fallback = false;
            var playbackStarted = false;
            CanvasLayer layer = null;
            Node sceneInstance = null;
            Control presentation = null;
            FusionResultCardCapture resultCapture = null;
            VfxAudioPlayback audio = null;
            Callable finishedHandler = default;
            var connected = false;
            try
            {
                if (!ResourceLoader.Exists(ScenePath))
                    throw new InvalidOperationException("Missing fusion scene: " + ScenePath);
                var packed = ResourceLoader.Load<PackedScene>(ScenePath)
                    ?? throw new InvalidOperationException("Could not load fusion scene: " + ScenePath);
                sceneInstance = packed.Instantiate();
                presentation = sceneInstance as Control
                    ?? throw new InvalidOperationException("Fusion scene root must be a Control.");
                if (!presentation.HasMethod("configure") || !presentation.HasMethod("get_duration")
                    || !presentation.HasMethod("play") || !presentation.HasMethod("stop")
                    || !presentation.HasSignal(FinishedSignal))
                {
                    throw new InvalidOperationException("Fusion scene presentation contract is incomplete.");
                }

                layer = new CanvasLayer
                {
                    Name = "ThermalVortexFusionSummonVfx",
                    Layer = 134,
                    Visible = false
                };
                presentation.MouseFilter = Control.MouseFilterEnum.Ignore;
                layer.AddChild(presentation);
                tree.Root.AddChild(layer);
                // The scene already uses FullRect anchors. Setting Size before
                // attachment becomes extra offsets and doubles the final rect.
                presentation.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

                var materials = await LoadMaterialsAsync(
                    tree, presentation,
                    materialVisuals ?? new FusionMaterialVisualSnapshot(summon.Materials),
                    CombatStillActive);
                if (materials is null || !CombatStillActive() || !IsTreeAlive(tree))
                    return;

                var showResult = !TcgMonsterCutinVfx.HasSummonCutin(summon);
                if (showResult)
                {
                    // Result cards use the native renderer, preserving their
                    // current name, energy, rules text and dynamic values.
                    resultCapture = await FusionResultCardCapture.CreateAsync(
                        tree, presentation, summon, CombatStillActive);
                    if (!CombatStillActive() || !IsTreeAlive(tree))
                        return;
                }
                var resultVisual = showResult ? FusionMaterialVisualSnapshot.CaptureCard(summon) : null;
                var resultCard = resultVisual?.ToSceneData(LoadPortrait(resultVisual.PortraitPath));
                var configuration = new Godot.Collections.Dictionary
                {
                    ["materials"] = materials,
                    ["result_full_texture"] = resultCapture is null ? default : Variant.From(resultCapture.Texture),
                    ["result_card"] = resultCard is null ? default : Variant.From(resultCard),
                    ["show_result"] = showResult
                };
                if (!presentation.Call("configure", configuration).AsBool())
                    throw new InvalidOperationException("Fusion scene rejected its presentation data.");

                var duration = presentation.Call("get_duration").AsDouble();
                if (!double.IsFinite(duration) || duration <= 0d || duration > 60d)
                    throw new InvalidOperationException("Fusion scene returned an invalid duration.");

                var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                finishedHandler = Callable.From(() => { finished.TrySetResult(true); });
                if (presentation.Connect(FinishedSignal, finishedHandler) != Error.Ok)
                    throw new InvalidOperationException("Could not connect fusion completion signal.");
                connected = true;

                if (!CombatStillActive() || !IsTreeAlive(tree))
                    return;
                layer.Visible = true;
                presentation.Call("play", speed);
                audio = VfxAudioPlayback.TryStart(layer, GetAudioPath(materials.Count, speed));
                playbackStarted = true;
                await WaitForPlaybackAsync(
                    tree, presentation, finished.Task, duration / speed + 1.5d, CombatStillActive);
            }
            catch (Exception ex)
            {
                // Only an unavailable/uninitializable scene falls back. An ended
                // combat or an interrupted running presentation must not start another effect.
                fallback = !playbackStarted;
                MainFile.Logger.Info($"Fusion summon VFX failed card={summon.GetType().Name}: {ex}");
            }
            finally
            {
                audio?.Dispose();
                // Native targeting begins after this call. Hide synchronously;
                // QueueFree alone could leave a mask visible for one more frame.
                if (GodotObject.IsInstanceValid(layer))
                    layer.Visible = false;
                if (GodotObject.IsInstanceValid(presentation))
                {
                    presentation.Visible = false;
                    try
                    {
                        if (connected && presentation.IsConnected(FinishedSignal, finishedHandler))
                            presentation.Disconnect(FinishedSignal, finishedHandler);
                        if (presentation.HasMethod("stop"))
                            presentation.Call("stop");
                    }
                    catch (Exception ex)
                    {
                        MainFile.Logger.Info("Fusion presentation cleanup failed: " + ex.Message);
                    }
                }

                resultCapture?.Dispose();
                if (GodotObject.IsInstanceValid(layer))
                {
                    if (layer.IsInsideTree())
                        layer.QueueFree();
                    else
                        layer.Free();
                }
                else if (GodotObject.IsInstanceValid(sceneInstance))
                {
                    sceneInstance.Free();
                }
            }

            if (fallback && CombatStillActive() && IsTreeAlive(tree))
            {
                await ThermalVortexCombatVfx.PlaySafeAsync(
                    nameof(XyzSummonVfx),
                    () => XyzSummonVfx.PlayAsync(summon, fallbackTarget, CombatStillActive));
            }
        }
        finally
        {
            PlaybackGate.Release();
        }
    }

    private static double GetPlaybackSpeed()
    {
        try
        {
            return SaveManager.Instance?.PrefsSave?.FastMode switch
            {
                FastModeType.Instant => 0d,
                FastModeType.Fast => NormalPlaybackSpeed * 2d,
                _ => NormalPlaybackSpeed
            };
        }
        catch
        {
            return NormalPlaybackSpeed;
        }
    }

    private static string GetAudioPath(int materialCount, double speed)
    {
        var variant = materialCount == 0 ? "zero" : materialCount > 5 ? "many" : "standard";
        // Both current speeds are baked with pitch-preserving time stretching.
        var mode = speed > NormalPlaybackSpeed ? "fast" : "normal";
        return MainFile.ResPath + $"/audio/vfx/fusion_{variant}_{mode}.ogg";
    }

    private static async Task<bool> EnterPlaybackGateAsync(SceneTree tree, Func<bool> combatStillActive)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (IsTreeAlive(tree) && combatStillActive())
        {
            if (PlaybackGate.Wait(0))
                return true;
            if (Stopwatch.GetElapsedTime(startedAt).TotalSeconds >= QueueTimeoutSeconds
                || !await WaitForNextFrameAsync(tree, tree.Root))
            {
                MainFile.Logger.Info("Fusion summon VFX skipped: presentation queue did not become available.");
                return false;
            }
        }
        return false;
    }

    private static async Task<Godot.Collections.Array> LoadMaterialsAsync(
        SceneTree tree,
        Node owner,
        FusionMaterialVisualSnapshot snapshot,
        Func<bool> combatStillActive)
    {
        var materials = new Godot.Collections.Array();
        for (var i = 0; i < snapshot.MaterialCount; i++)
        {
            if (!combatStillActive() || !IsTreeAlive(tree))
                return null;
            var visual = snapshot.GetCard(i);
            materials.Add(visual is null
                ? default
                : Variant.From(visual.ToSceneData(LoadPortrait(visual.PortraitPath))));
            if ((i + 1) % 4 == 0 && i + 1 < snapshot.MaterialCount
                && !await WaitForNextFrameAsync(tree, owner))
                return null;
        }
        return materials;
    }

    private static Texture2D LoadPortrait(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            // Keep the ordinary shared cache: these are borrowed card portraits,
            // including the .png.import mappings in an installed PCK.
            return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Fusion portrait load failed path={path}: {ex.Message}");
            return null;
        }
    }

    private static async Task WaitForPlaybackAsync(
        SceneTree tree,
        Node owner,
        Task<bool> finished,
        double timeoutSeconds,
        Func<bool> combatStillActive)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (!finished.IsCompleted && IsTreeAlive(tree) && combatStillActive()
            && GodotObject.IsInstanceValid(owner) && owner.IsInsideTree())
        {
            if (Stopwatch.GetElapsedTime(startedAt).TotalSeconds >= timeoutSeconds)
            {
                MainFile.Logger.Info("Fusion summon VFX stopped: completion signal timed out.");
                return;
            }
            if (!await WaitForNextFrameAsync(tree, owner))
                return;
        }
    }

    private static bool IsTreeAlive(SceneTree tree) =>
        GodotObject.IsInstanceValid(tree)
        && GodotObject.IsInstanceValid(tree.Root)
        && tree.Root.IsInsideTree();

    internal static async Task<bool> WaitForNextFrameAsync(SceneTree tree, Node owner)
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
}
