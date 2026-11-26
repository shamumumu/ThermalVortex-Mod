using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch]
internal static class OpeningFusionDrawPatch
{
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        var draw = AccessTools.Method(typeof(CardPileCmd), "DrawInternal",
            [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)]);
        var stateMachine = draw?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        return AccessTools.Method(stateMachine, nameof(IAsyncStateMachine.MoveNext))
            ?? throw new InvalidOperationException("Cannot locate the native card draw loop.");
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> UseOpeningDrawCandidates(
        IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var stateMachine = __originalMethod.DeclaringType;
        var fromHandDraw = AccessTools.Field(stateMachine, "fromHandDraw");
        var player = AccessTools.Field(stateMachine, "player");
        var nativeShuffle = AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.ShuffleIfNecessary));
        var nativeSelect = ((Func<IEnumerable<CardModel>, CardModel>)Enumerable.FirstOrDefault).Method;
        var shuffle = AccessTools.Method(typeof(OpeningFusionDrawPatch), nameof(ShuffleForDraw));
        var select = AccessTools.Method(typeof(OpeningFusionDrawPatch), nameof(SelectDrawCard));
        var code = instructions.ToList();
        if (fromHandDraw is null || player is null || nativeShuffle is null
            || code.Count(instruction => instruction.Calls(nativeShuffle)) != 1
            || code.Count(instruction => instruction.Calls(nativeSelect)) != 1)
        {
            throw new InvalidOperationException("The native draw loop no longer has the expected shuffle and selection calls.");
        }

        foreach (var instruction in code)
        {
            var isShuffle = instruction.Calls(nativeShuffle);
            var isSelect = instruction.Calls(nativeSelect);
            if (isShuffle || isSelect)
            {
                // Leave the native draw count, notifications and movement in the original loop.
                var loadState = new CodeInstruction(OpCodes.Ldarg_0);
                loadState.labels.AddRange(instruction.labels);
                loadState.blocks.AddRange(instruction.blocks);
                instruction.labels.Clear();
                instruction.blocks.Clear();
                yield return loadState;
                yield return new CodeInstruction(OpCodes.Ldfld, fromHandDraw);

                if (isSelect)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, player);
                }

                instruction.operand = isShuffle ? shuffle : select;
            }

            yield return instruction;
        }
    }

    private static bool SkipFusion(Player player, bool fromHandDraw) =>
        fromHandDraw && player?.GetRelic<ThermalVortexCore>()?.HandlesFusionInOpeningDraw == true;

    private static CardModel SelectDrawCard(IEnumerable<CardModel> cards, bool fromHandDraw, Player player) =>
        SkipFusion(player, fromHandDraw)
            ? cards.FirstOrDefault(card => card is not XyzSummon)
            : cards.FirstOrDefault();

    private static Task ShuffleForDraw(PlayerChoiceContext context, Player player, bool fromHandDraw)
    {
        if (!SkipFusion(player, fromHandDraw))
            return CardPileCmd.ShuffleIfNecessary(context, player);

        var draw = CardPile.Get(PileType.Draw, player).Cards;
        if (draw.Any(card => card is not XyzSummon)
            || !CardPile.GetCards(player, PileType.Discard).Any(card => card is not XyzSummon))
        {
            return Task.CompletedTask;
        }

        // A draw pile containing only Fusion has no card available for this draw.
        return draw.Count == 0
            ? CardPileCmd.ShuffleIfNecessary(context, player)
            : CardPileCmd.Shuffle(context, player);
    }
}
