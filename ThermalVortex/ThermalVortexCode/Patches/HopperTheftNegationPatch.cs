using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch]
internal static class HopperTheftNegationPatch
{
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        var move = typeof(ThievingHopper).GetMethod("ThieveryMove",
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [typeof(IReadOnlyList<Creature>)], null);
        return move?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod(nameof(IAsyncStateMachine.MoveNext),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "Cannot locate ThievingHopper.ThieveryMove's async state machine for theft negation.");
    }

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> FilterTheftCandidates(IEnumerable<CodeInstruction> instructions)
    {
        var result = instructions.ToList();
        var getCards = typeof(CardPile).GetMethod(nameof(CardPile.GetCards),
            [typeof(Player), typeof(PileType[])]);
        var replacement = typeof(HopperTheftNegationPatch).GetMethod(nameof(GetTheftCandidates),
            BindingFlags.Static | BindingFlags.NonPublic);
        var reads = result.Where(instruction => instruction.opcode == OpCodes.Call
            && Equals(instruction.operand, getCards)).ToList();
        if (getCards is null || replacement is null || reads.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one CardPile.GetCards call in ThievingHopper.ThieveryMove; found {reads.Count}.");
        }

        // Keep the native selection, RNG, movement and attack. Blocking SwipePower
        // later is too late: Steal has already removed the permanent deck card.
        reads[0].operand = replacement;
        return result;
    }

    private static IEnumerable<CardModel> GetTheftCandidates(Player player, PileType[] pileTypes)
    {
        var cards = CardPile.GetCards(player, pileTypes);
        var source = AshBlossomActionNegation.CurrentSource;
        if (source?.Monster is not ThievingHopper
            || !cards.Any(card => card.DeckVersion is not null))
        {
            return cards;
        }

        // Only a real, eligible theft consumes Ash; an empty candidate pile does
        // not. Strike takes precedence so it does not also spend an Ash charge.
        return SolemnStrikeActionNegation.IsActiveFor(source)
            || AshBlossomActionNegation.TryNegateFrom(source)
            ? Enumerable.Empty<CardModel>()
            : cards;
    }
}
