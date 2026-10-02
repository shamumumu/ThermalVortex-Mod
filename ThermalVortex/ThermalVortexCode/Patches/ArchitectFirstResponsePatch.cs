using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using ThermalVortex.ThermalVortexCode.Character;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(TheArchitect), "CreateOptionForCurrentLine")]
internal static class ArchitectFirstResponsePatch
{
    private static readonly MethodInfo CallbackGetter = typeof(EventOption)
        .GetProperty("OnChosen", BindingFlags.Instance | BindingFlags.NonPublic)?.GetGetMethod(true);

    private static void Postfix(TheArchitect __instance, ref EventOption __result)
    {
        if (__result is null || !ArchitectFirstResponse.IsFirstResponse(__instance))
            return;

        try
        {
            if (CallbackGetter?.Invoke(__result, null) is not Func<Task> advance)
                return;

            // The native dialogue option uses this constructor and this history flag.
            // Wrap OnChosen itself: BeforeChosen is a multicast async delegate and cannot
            // safely wait for an extra animation alongside the room's own callback.
            var original = __result;
            __result = new EventOption(
                __instance,
                () => ArchitectFirstResponse.AwaitRevealThenAdvance(__instance, advance),
                original.Title, original.Description, original.TextKey, original.HoverTips)
                .ThatWontSaveToChoiceHistory();
        }
        catch (Exception exception)
        {
            ArchitectFirstResponse.LogFailure(exception);
        }
    }
}

internal static class ArchitectFirstResponse
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string DialoguePrefix = "THE_ARCHITECT.talk.THERMALVORTEX-THERMAL_VORTEX.";
    private static readonly FieldInfo LineIndex = typeof(TheArchitect).GetField("_currentLineIndex", PrivateInstance);
    private static readonly FieldInfo Dialogue = typeof(TheArchitect).GetField("_dialogue", PrivateInstance);
    private static readonly FieldInfo SpeechBubble = typeof(TheArchitect).GetField("_speechBubble", PrivateInstance);
    private static readonly FieldInfo RoomEvent = typeof(NEventRoom).GetField("_event", PrivateInstance);
    private static readonly ConditionalWeakTable<TheArchitect, ResponseState> States = new();
    private static bool loggedFailure;

    internal static bool IsFirstResponse(TheArchitect architect)
    {
        if (!MillenniumPuzzleCharacterArt.IsThermalVortex(architect?.Owner)
            || !LocalContext.IsMe(architect.Owner))
            return false;

        return LineIndex?.GetValue(architect) is 0
            && Dialogue?.GetValue(architect) is AncientDialogue dialogue
            && dialogue.Lines.Count >= 2
            && dialogue.Lines[0].LineText.LocEntryKey.StartsWith(DialoguePrefix, StringComparison.Ordinal)
            && dialogue.Lines[0].LineText.LocEntryKey.EndsWith("-0r.ancient", StringComparison.Ordinal)
            && dialogue.Lines[1].LineText.LocEntryKey.EndsWith("-1r.char", StringComparison.Ordinal);
    }

    internal static Task AwaitRevealThenAdvance(TheArchitect architect, Func<Task> advance)
    {
        if (architect is not null && States.TryGetValue(architect, out var existing)
            && existing.Completion is not null)
            return existing.Completion.Task;
        if (!IsFirstResponse(architect))
            return advance();

        var state = States.GetValue(architect, static _ => new ResponseState());
        if (state.Completion is not null)
            return state.Completion.Task;

        // Publish the shared task before starting: duplicate callbacks must share both
        // the animation and the single original dialogue advance, not just the delay.
        state.Completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = RunFirstResponse(architect, advance, state.Completion);
        return state.Completion.Task;
    }

    private static async Task RunFirstResponse(
        TheArchitect architect, Func<Task> advance, TaskCompletionSource completion)
    {
        NEventRoom room = null;
        NCombatEventLayout scene = null;
        using var sceneExit = new CancellationTokenSource();
        void OnSceneExit() => sceneExit.Cancel();
        bool IsActive() => !sceneExit.IsCancellationRequested && IsCurrentRoom(architect, room, scene);

        try
        {
            room = NEventRoom.Instance;
            scene = architect.Node as NCombatEventLayout;
            if (!IsActive())
                return;

            // Remember the exit even if the same nodes are later reattached. Also release
            // an interrupted speech-bubble wait immediately, before any VFX exists.
            room.TreeExiting += OnSceneExit;
            scene.TreeExiting += OnSceneExit;

            try
            {
                if (SpeechBubble?.GetValue(architect) is NSpeechBubbleVfx bubble
                    && GodotObject.IsInstanceValid(bubble))
                {
                    try
                    {
                        // Keep a removed/interrupted bubble from holding the dialogue open.
                        await bubble.AnimOut().WaitAsync(TimeSpan.FromSeconds(0.5), sceneExit.Token);
                    }
                    finally
                    {
                        if (ReferenceEquals(SpeechBubble.GetValue(architect), bubble))
                            SpeechBubble.SetValue(architect, null);
                        if (GodotObject.IsInstanceValid(bubble))
                        {
                            bubble.Visible = false;
                            bubble.QueueFree();
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (sceneExit.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFailure(exception);
            }

            if (!IsActive() || !IsFirstResponse(architect))
                return;

            await ArchitectYamiRevealVfx.PlayAsync(architect);

            // A scene exit completes the VFX task too; it must not resume an old event.
            if (IsActive() && IsFirstResponse(architect))
                await advance();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(room))
                room.TreeExiting -= OnSceneExit;
            if (GodotObject.IsInstanceValid(scene))
                scene.TreeExiting -= OnSceneExit;
            completion.TrySetResult();
        }
    }

    private static bool IsCurrentRoom(TheArchitect architect, NEventRoom room, NCombatEventLayout scene)
    {
        return GodotObject.IsInstanceValid(room) && room.IsInsideTree()
            && GodotObject.IsInstanceValid(scene) && scene.IsInsideTree()
            && ReferenceEquals(NEventRoom.Instance, room)
            && ReferenceEquals(RoomEvent?.GetValue(room), architect)
            && ReferenceEquals(architect.Node, scene);
    }

    internal static void LogFailure(Exception exception)
    {
        if (loggedFailure)
            return;
        loggedFailure = true;
        MainFile.Logger.Info($"Could not prepare ThermalVortex architect first response error={exception}");
    }

    private sealed class ResponseState
    {
        internal TaskCompletionSource Completion;
    }
}
