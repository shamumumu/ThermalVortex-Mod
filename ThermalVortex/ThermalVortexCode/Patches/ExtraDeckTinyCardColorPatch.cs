using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Character;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NTinyCard), nameof(NTinyCard.SetCard))]
internal static class ExtraDeckTinyCardColorPatch
{
    private static readonly System.Reflection.MethodInfo SetCardBackColorMethod =
        AccessTools.Method(typeof(NTinyCard), "SetCardBackColor", new[] { typeof(CardPoolModel) });

    private static readonly System.Reflection.FieldInfo CardBackField =
        AccessTools.Field(typeof(NTinyCard), "_cardBack");

    private static void Postfix(NTinyCard __instance, CardModel card)
    {
        if (card is not XyzMonsterCard)
            return;

        var extraDeckPool = ModelDb.CardPool<ExtraDeckCardPool>();
        SetCardBackColorMethod?.Invoke(__instance, new object[] { extraDeckPool });

        if (CardBackField?.GetValue(__instance) is CanvasItem cardBack)
            cardBack.Material = card.FrameMaterial;
    }
}
