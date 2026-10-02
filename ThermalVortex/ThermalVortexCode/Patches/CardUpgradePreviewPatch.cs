using BaseLib.Extensions;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(CardModel), nameof(CardModel.UpdateDynamicVarPreview))]
internal static class CardUpgradePreviewPatch
{
    private static readonly SpireField<DynamicVar, decimal?> DynamicVarUpgrades =
        AccessTools.Field(typeof(DynamicVarExtensions), "DynamicVarUpgrades")
            ?.GetValue(null) as SpireField<DynamicVar, decimal?>;

    private static void Prefix(
        CardModel __instance,
        CardPreviewMode __0,
        Creature __1,
        DynamicVarSet __2)
    {
        if (__instance is not ThermalVortexCard
            || __instance.RunState is not null
            || __instance.CombatState is not null)
        {
            return;
        }

        var isUpgradePreview = __0 == CardPreviewMode.Upgrade && __instance.IsUpgradable;
        foreach (var dynamicVar in __2.Values.ToList())
        {
            // CardModel intentionally skips preview updates for canonical
            // library cards. Always restore their ordinary preview first so a
            // prior upgrade preview cannot leak green/red values into the base
            // card display.
            dynamicVar.ResetToBase();
            if (!isUpgradePreview)
                continue;

            var upgradeBy = DynamicVarUpgrades?.Get(dynamicVar);
            if (upgradeBy.HasValue)
                dynamicVar.PreviewValue = dynamicVar.BaseValue + upgradeBy.Value;

            if (dynamicVar is UpgradePreviewDynamicVar upgradePreviewVar)
                upgradePreviewVar.UpdateCardPreview(__instance, __0, __1, true);
        }
    }
}
