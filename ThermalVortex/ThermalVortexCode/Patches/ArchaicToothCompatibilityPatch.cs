using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(ArchaicTooth), nameof(ArchaicTooth.SetupForPlayer))]
internal static class ArchaicToothSetupCompatibilityPatch
{
    private static bool Prefix(ArchaicTooth __instance, Player player, ref bool __result)
    {
        if (player?.Character is not ThermalVortexCharacter)
            return true;

        __result = ArchaicToothCompatibility.SetupForPlayer(__instance, player);
        return false;
    }
}

[HarmonyPatch(typeof(ArchaicTooth), nameof(ArchaicTooth.AfterObtained))]
internal static class ArchaicToothObtainCompatibilityPatch
{
    private static bool Prefix(ArchaicTooth __instance, ref Task __result)
    {
        if (__instance.Owner?.Character is not ThermalVortexCharacter)
            return true;

        __result = ArchaicToothCompatibility.AfterObtained(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(ArchaicTooth), "ExtraHoverTips", MethodType.Getter)]
internal static class ArchaicToothHoverCompatibilityPatch
{
    private static void Postfix(ArchaicTooth __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (ArchaicToothCompatibility.HasFusionPreview(__instance))
            __result = ArchaicToothCompatibility.GetFusionHoverTips(__instance);
    }
}

[HarmonyPatch(typeof(RelicModel), "EventDescription", MethodType.Getter)]
internal static class ArchaicToothEventDescriptionCompatibilityPatch
{
    private static void Postfix(RelicModel __instance, ref LocString __result)
    {
        if (__instance is ArchaicTooth tooth && ArchaicToothCompatibility.HasFusionPreview(tooth))
            __result = new LocString("relics", "THERMALVORTEX-ARCHAIC_TOOTH.eventDescription");
    }
}

[HarmonyPatch(typeof(RelicModel), "Description", MethodType.Getter)]
internal static class ArchaicToothDescriptionCompatibilityPatch
{
    private static void Postfix(RelicModel __instance, ref LocString __result)
    {
        if (__instance is ArchaicTooth tooth && ArchaicToothCompatibility.HasFusionPreview(tooth))
            __result = new LocString("relics", "THERMALVORTEX-ARCHAIC_TOOTH.description");
    }
}

internal static class ArchaicToothCompatibility
{
    private static readonly Action<ArchaicTooth, SerializableCard> SetStarterCard =
        AccessTools.PropertySetter(typeof(ArchaicTooth), nameof(ArchaicTooth.StarterCard))
            .CreateDelegate<Action<ArchaicTooth, SerializableCard>>();

    private static readonly Action<ArchaicTooth, SerializableCard> SetAncientCard =
        AccessTools.PropertySetter(typeof(ArchaicTooth), nameof(ArchaicTooth.AncientCard))
            .CreateDelegate<Action<ArchaicTooth, SerializableCard>>();

    internal static bool SetupForPlayer(ArchaicTooth tooth, Player player)
    {
        if (!TrySelectMaterials(player.Deck.Cards, out var electric, out var magnetic))
            return false;

        var result = CreatePreview<ElectromagneticCircle>(GetUpgradeLevel(electric, magnetic));
        SetPreview(tooth, electric, result);
        return true;
    }

    internal static bool TrySelectMaterials(
        IEnumerable<CardModel> deck,
        out InternalCombustion electric,
        out SectionPole magnetic)
    {
        electric = null;
        magnetic = null;
        var cards = deck.ToList();
        if (cards.Any(card => card is ElectromagneticCircle))
            return false;

        // OrderByDescending is stable, so equal upgrades retain permanent deck order.
        electric = cards.OfType<InternalCombustion>()
            .Where(card => card.IsRemovable)
            .OrderByDescending(card => card.CurrentUpgradeLevel)
            .FirstOrDefault();
        magnetic = cards.OfType<SectionPole>()
            .Where(card => card.IsRemovable)
            .OrderByDescending(card => card.CurrentUpgradeLevel)
            .FirstOrDefault();
        return electric is not null && magnetic is not null;
    }

    internal static int GetUpgradeLevel(CardModel electric, CardModel magnetic) =>
        (electric.IsUpgraded ? 1 : 0) + (magnetic.IsUpgraded ? 1 : 0);

    internal static async Task AfterObtained(ArchaicTooth tooth)
    {
        var player = tooth.Owner;
        if (player?.RunState is not RunState runState)
        {
            MainFile.Logger.Info("ArchaicToothCompatibility skipped reason=missing_run_state");
            return;
        }

        if (!TrySelectMaterials(player.Deck.Cards, out var electric, out var magnetic))
        {
            MainFile.Logger.Info("ArchaicToothCompatibility skipped reason=material_conditions_changed");
            return;
        }

        var upgradeLevel = GetUpgradeLevel(electric, magnetic);
        var result = runState.CreateCard<ElectromagneticCircle>(player);
        for (var level = 0; level < upgradeLevel; level++)
            CardCmd.Upgrade(result, CardPreviewStyle.None);
        if (result.CurrentUpgradeLevel != upgradeLevel)
            throw new InvalidOperationException("Archaic Tooth could not prepare the inherited card upgrades.");

        // Save the material-derived result before normal pickup hooks can upgrade it again.
        SetPreview(tooth, electric, result);
        await CardPileCmd.RemoveFromDeck(new CardModel[] { electric, magnetic });
        using var source = RewardGrantDiagnostics.EnterSource(
            RewardGrantPolicy.Ancient, "ThermalVortexArchaicToothFusion");
        var added = await CardPileCmd.Add(result, PileType.Deck, CardPilePosition.Bottom, null, false);
        CardCmd.PreviewCardPileAdd(added, 1.2f, CardPreviewStyle.HorizontalLayout);
        tooth.Flash();
    }

    private static void SetPreview(ArchaicTooth tooth, CardModel electric, CardModel result)
    {
        SetStarterCard(tooth, electric.ToSerializable());
        SetAncientCard(tooth, result.ToSerializable());
    }

    internal static bool HasFusionPreview(ArchaicTooth tooth) =>
        tooth.AncientCard?.Id?.Equals(ModelDb.Card<ElectromagneticCircle>().Id) == true;

    internal static IEnumerable<IHoverTip> GetFusionHoverTips(ArchaicTooth tooth)
    {
        // Owner is not assigned during some event, clone and saved-property restoration paths.
        var electric = tooth.StarterCard is { } starter
            ? CardModel.FromSerializable(starter)
            : CreatePreview<InternalCombustion>(0);
        var result = CardModel.FromSerializable(tooth.AncientCard);
        var magneticLevel = Math.Clamp(result.CurrentUpgradeLevel - (electric.IsUpgraded ? 1 : 0), 0, 1);
        // Only the magnetic card's type and upgrade contribution are reconstructed, not its enchantment.
        var magnetic = CreatePreview<SectionPole>(magneticLevel);
        var tips = new List<IHoverTip>();
        foreach (var card in new[] { electric, magnetic, result })
        {
            tips.AddRange(card.HoverTips);
            tips.Add(HoverTipFactory.FromCard(card, false));
        }
        return tips.DistinctBy(tip => tip.Id).ToArray();
    }

    private static CardModel CreatePreview<T>(int upgradeLevel) where T : CardModel
    {
        var card = ModelDb.Card<T>().ToMutable();
        return ThermalVortexGeneratedCards.ApplyUpgradeLevel(card, upgradeLevel);
    }
}
