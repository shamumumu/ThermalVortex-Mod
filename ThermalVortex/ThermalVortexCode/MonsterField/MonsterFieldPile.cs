using BaseLib.Abstracts;
using BaseLib.Patches.Content;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace ThermalVortex.ThermalVortexCode.MonsterField;

public class MonsterFieldPile : CustomPile
{
    public static readonly PileType FieldPileType = (PileType)1001;

    public MonsterFieldPile() : base(FieldPileType)
    {
    }

    public static void Register() =>
        CustomPiles.RegisterCustomPile(FieldPileType, () => new MonsterFieldPile());

    public override bool CardShouldBeVisible(CardModel card) => false;

    public override Vector2 GetTargetPosition(CardModel card, Vector2 defaultPosition)
    {
        var index = 0;
        for (; index < Cards.Count; index++)
        {
            if (ReferenceEquals(Cards[index], card))
                break;
        }

        return new Vector2(345f + index * 92f, 650f);
    }
}
