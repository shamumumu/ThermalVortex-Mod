using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Rooms;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Generate))]
internal static class ExtraDeckPrayPatch
{
    [HarmonyPostfix]
    private static void AddPrayOption(Player __0, List<RestSiteOption> __result)
    {
        if (__0.Character is ThermalVortexCharacter && __0.GetRelic<ThermalVortexCore>() is not null)
            __result.Add(new ExtraDeckPrayOption(__0));
    }
}

[HarmonyPatch(typeof(NRestSiteButton), "Reload")]
internal static class ExtraDeckPrayIconPatch
{
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> ResolveOptionIcon(IEnumerable<CodeInstruction> instructions)
    {
        var nativeGetter = AccessTools.PropertyGetter(typeof(RestSiteOption), nameof(RestSiteOption.Icon));
        var resolver = AccessTools.Method(typeof(ExtraDeckPrayIconPatch), nameof(GetOptionIcon));

        // Resolve the texture at the button call site so the native getter can still be inlined.
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(nativeGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = resolver;
            }

            yield return instruction;
        }
    }

    private static Texture2D GetOptionIcon(RestSiteOption option) => option is ExtraDeckPrayOption
        ? PreloadManager.Cache.GetTexture2D(ExtraDeckPrayOption.TexturePath)
        : option.Icon;
}

internal sealed class ExtraDeckPrayOption(Player owner) : RestSiteOption(owner)
{
    internal static readonly string TexturePath = "charui/rest_site_pray.png".ImagePath();

    public override string OptionId => "THERMALVORTEX_PRAY";
    public override IEnumerable<string> AssetPaths => [TexturePath];

    public override bool IsEnabled => Owner.RunState.CurrentRoom is RestSiteRoom
        && Owner.GetRelic<ThermalVortexCore>() is not null;

    public override async Task<bool> OnSelect()
    {
        if (Owner.RunState.CurrentRoom is not RestSiteRoom room
            || Owner.GetRelic<ThermalVortexCore>() is not { } core)
        {
            return false;
        }

        var firstPack = ExtraDeckPacks.Create(Owner, room, ExtraDeckPackSource.Pray, packIndex: 0);
        var secondPack = ExtraDeckPacks.Create(Owner, room, ExtraDeckPackSource.Pray, packIndex: 1);
        if (firstPack.Count == 0 || secondPack.Count == 0)
            return false;

        var firstCard = await ExtraDeckPacks.Choose(Owner, firstPack);
        if (firstCard is not null)
            core.TryGrantOwnedExtraDeckCard(firstCard);

        var secondCard = await ExtraDeckPacks.Choose(Owner, secondPack);
        if (secondCard is not null)
            core.TryGrantOwnedExtraDeckCard(secondCard);

        return true;
    }
}
