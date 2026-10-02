using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal static class EnemyActionClassifier
{
    internal static EnemyActionSummary Classify(MoveState move)
    {
        var intents = move?.Intents;
        if (intents is null || intents.Count == 0)
            return new EnemyActionSummary(EnemyActionKind.None, 0, 0, 0, 0);

        var kind = EnemyActionKind.None;
        var attackCount = 0;
        var powerChangeCount = 0;
        var summonCount = 0;
        var otherCount = 0;
        foreach (var intent in intents)
        {
            kind |= EnemyActionKind.Any;
            switch (intent.IntentType)
            {
                case IntentType.Attack:
                case IntentType.DeathBlow:
                    kind |= EnemyActionKind.Attack;
                    attackCount += AttackRepeatCount(intent);
                    break;
                case IntentType.Buff:
                case IntentType.Debuff:
                case IntentType.DebuffStrong:
                case IntentType.StatusCard:
                case IntentType.CardDebuff:
                    kind |= EnemyActionKind.PowerChange;
                    powerChangeCount++;
                    break;
                case IntentType.Summon:
                    kind |= EnemyActionKind.Summon;
                    // This is only the advertised action count.  Actual
                    // summoned units are captured from CreatureCmd.Add so a
                    // three-unit summon produces three independent triggers.
                    summonCount++;
                    break;
                default:
                    otherCount++;
                    break;
            }
        }

        var anyCount = attackCount + powerChangeCount + summonCount + otherCount;
        return new EnemyActionSummary(kind, anyCount, attackCount, powerChangeCount, summonCount);
    }

    private static int AttackRepeatCount(AbstractIntent intent) =>
        intent is AttackIntent attack ? Math.Max(1, attack.Repeats) : 1;
}
