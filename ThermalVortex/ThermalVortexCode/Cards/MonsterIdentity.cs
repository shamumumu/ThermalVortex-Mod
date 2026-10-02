using MegaCrit.Sts2.Core.Models;

namespace ThermalVortex.ThermalVortexCode.Cards;

// Gameplay identity can differ from a card's physical class. Keep registration,
// deck origin, result-pile routing and the host's own cost on the physical card.
internal static class MonsterIdentity
{
    internal static CardModel GetEffectiveCard(CardModel card)
    {
        HashSet<CardModel> visited = null;
        while (card is ChaosPhantom { CopiedMonsterForIdentity: { } copied })
        {
            visited ??= new HashSet<CardModel>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            if (!visited.Add(card))
                break;
            card = copied;
        }

        return card;
    }

    internal static bool Matches<T>(CardModel card) => Matches(card, typeof(T));

    internal static bool Matches(CardModel card, Type type)
    {
        var effective = GetEffectiveCard(card);
        if (effective is null || type is null)
            return false;

        if (effective is PoleMonsterCard pole)
        {
            if (type == typeof(FirePoleCard))
                return pole.IsFirePole;
            if (type == typeof(ThunderPoleCard))
                return pole.IsThunderPole;
        }

        return type.IsInstanceOfType(effective);
    }

    internal static int GetUpgradeLevel(CardModel card) =>
        GetEffectiveCard(card)?.CurrentUpgradeLevel ?? 0;
}
