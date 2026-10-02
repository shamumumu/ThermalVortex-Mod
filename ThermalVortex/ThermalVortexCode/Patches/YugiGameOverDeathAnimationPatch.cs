using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NGameOverScreen), "MoveCreaturesToDifferentLayerAndDisableUi")]
internal static class YugiGameOverVisualReparentPatch
{
    private static void Prefix(out bool __state)
    {
        __state = YugiNativeActionController.MovingVisualsToGameOver;
        YugiNativeActionController.MovingVisualsToGameOver = true;
    }

    private static void Finalizer(bool __state)
    {
        YugiNativeActionController.MovingVisualsToGameOver = __state;
    }
}

[HarmonyPatch]
internal static class YugiGameOverDeathAnimationPatch
{
    private static MethodBase TargetMethod()
    {
        var animateIn = AccessTools.Method(typeof(NGameOverScreen), "AnimateIn");
        var stateMachine = animateIn.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? throw new InvalidOperationException("Native game-over intro is not an async state machine.");
        return AccessTools.Method(stateMachine, "MoveNext");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var result = instructions.ToList();
        var awaitTween = AccessTools.Method(typeof(TweenHelper), nameof(TweenHelper.AwaitFinished),
            [typeof(Tween), typeof(Node)]);
        // The first awaited tween is the native red backstop (also including
        // the event-room fade). Keep the later UI tween and every other native
        // instruction intact; Banner and Continue follow this existing await.
        var transitionAwait = result.FindIndex(instruction => instruction.Calls(awaitTween));
        if (transitionAwait < 0)
            throw new InvalidOperationException("Native game-over transition await was not found.");
        result[transitionAwait].opcode = OpCodes.Call;
        result[transitionAwait].operand = AccessTools.Method(typeof(YugiGameOverDeathAnimationPatch),
            nameof(AwaitTransitionAndRecall));
        return result;
    }

    private static async Task<bool> AwaitTransitionAndRecall(Tween tween, Node owner)
    {
        if (!await TweenHelper.AwaitFinished(tween, owner))
            return false;
        await YugiNativeActionController.PlayPendingGameOverDeaths(owner);
        return GodotObject.IsInstanceValid(owner) && owner.IsInsideTree();
    }
}
