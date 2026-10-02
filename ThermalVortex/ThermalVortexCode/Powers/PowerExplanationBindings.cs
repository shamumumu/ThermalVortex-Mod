using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Powers;

// Power hover tips do not inherit the source card's explanations. Bind by
// meaning and current state; localized text and card-name substrings are not IDs.
internal static class PowerExplanationBindings
{
    internal static IEnumerable<IHoverTip> Create(ThermalVortexPower power)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var suffix in KeywordKeys(power))
        {
            var key = $"THERMALVORTEX-{suffix}";
            if (!visited.Add(key))
                continue;

            yield return ThermalVortexCard.KeywordTip(key, power.IsMutable ? power.Owner?.Player : null);
            foreach (var tip in ThermalVortexCard.CreateDependencyTips(key, visited))
                yield return tip;
        }

        switch (power)
        {
            case MacroCosmosPower or FusionGatePower or AliceInWonderlandPower:
                yield return HoverTipFactory.FromKeyword(CardKeyword.Exhaust);
                break;
            case FullArmorThunderLancePower or DormantMagneticFieldPower or MillenniumTemplePower:
                yield return HoverTipFactory.Static(StaticHoverTip.Block, []);
                break;
            case ForbiddenChalicePower:
                yield return HoverTipFactory.FromPower<StrengthPower>(null);
                break;
            case AwakenedMillenniumPrimitivePower:
                yield return HoverTipFactory.FromPower<VulnerablePower>(null);
                break;
            case FusionPreparationPower or MillenniumContractBookPower:
                yield return HoverTipFactory.ForEnergy(power);
                break;
        }

        foreach (var tip in CardPreviews(power))
            yield return tip;
    }

    private static IEnumerable<string> KeywordKeys(ThermalVortexPower power) => power switch
    {
        SummonRulesPower or FurnaceStartupPower or AliceInWonderlandPower or ToyBoxPower
            or MillenniumPartnerPower or SharedFatePower or DarkDuelPower
            => ["MONSTER"],
        AshBlossomPower or InfiniteImpermanencePower or SolemnJudgmentPower
            or SolemnWarningPower or SolemnStrikePower or MirrorForcePower
            => ["NEGATE"],
        FusionDestinyPower => ["FUSION_CARD"],
        FusionGatePower => ["MONSTER", "XYZ"],
        FutureFusionPower => ["XYZ", "MONSTER"],
        RelinquishedControlPower => ["CONTROL"],
        DarkCyberWorldPower => ["CYBER_MONSTER", "DEVOUR"],
        CyberDragonInfinityPower => ["DEVOUR", "MONSTER"],
        CyberWeldingPower welding when !power.IsMutable || welding.HasPendingOutput => ["MONSTER"],
        MillenniumTemplePower => ["MILLENNIUM_COUNT"],
        MillenniumSleepingTabletPower => ["SEALED_MILLENNIUM_PIECE"],
        MillenniumTreasureGolemPower => ["WEAK"],
        ExodiaSummonerPower when LocManager.Instance?.Language == "zhs" => ["MILLENNIUM_MONSTER"],
        _ => []
    };

    private static IEnumerable<IHoverTip> CardPreviews(ThermalVortexPower power)
    {
        switch (power)
        {
            case GoldSarcophagusReturnPower { SealedCard: { } card }:
                // Preserve this cabinet's actual card state, but isolate the
                // dynamic-variable preview writes made by the native card UI.
                yield return new CardHoverTip((CardModel)card.MutableClone());
                break;
            case CyberBeaconPower beacon:
                yield return Preview<CyberDragon>(power.IsMutable && beacon.GenerateUpgradedDragon ? 1 : 0);
                break;
            case CyberScrapYardPower scrapYard:
                yield return Preview<CyberDragon>(power.IsMutable && scrapYard.GenerateUpgradedDragon ? 1 : 0);
                break;
            case CyberDragonHerzPower herz:
                foreach (var upgraded in PreviewStates(power, herz.GeneratedDragonUpgradeStates))
                    yield return Preview<CyberDragon>(upgraded ? 1 : 0);
                break;
            case ToyBoxPower toyBox:
                foreach (var upgraded in PreviewStates(power, toyBox.GeneratedTokenUpgradeStates))
                    yield return Preview<ToyBoxToken>(upgraded ? 1 : 0);
                break;
            case CyberRevolutionSystemPower:
                // This rule changes all Cyber Larvae; it does not generate a
                // card or restrict the affected cards to a particular upgrade.
                yield return Preview<CyberLarva>();
                break;
            case FusionPreparationPower:
                yield return Preview<XyzSummon>();
                break;
            case RaTransitionPower transition:
                var hasPhoenix = power.IsMutable && transition.HasPhoenixTransition;
                var hasRa = power.IsMutable && transition.HasRaTransition;
                // Match SelectDescriptionKey: canonical and unbound instances
                // use the default description containing both transitions.
                var showBoth = !hasPhoenix && !hasRa;
                if (hasPhoenix || showBoth)
                {
                    var levels = power.IsMutable ? transition.SpherePreviewUpgradeLevels : [];
                    foreach (var level in levels.DefaultIfEmpty(0).Distinct())
                        yield return Preview<WingedDragonOfRaSphereMode>(level);
                }
                yield return Preview<WingedDragonOfRaPhoenix>();
                if (hasRa || showBoth)
                    yield return Preview<WingedDragonOfRa>();
                break;
        }
    }

    private static IEnumerable<bool> PreviewStates(ThermalVortexPower power, IReadOnlyList<bool> states) =>
        power.IsMutable ? states.DefaultIfEmpty(false).Distinct() : [false];

    private static IHoverTip Preview<T>(int upgradeLevel = 0) where T : CardModel
    {
        // Upgrade a detached display card; never register it in combat or
        // invoke a generation/summon hook just to show an explanation.
        var preview = ThermalVortexGeneratedCards.ApplyUpgradeLevel(
            (T)ModelDb.Card<T>().ToMutable(), upgradeLevel);
        return new CardHoverTip(preview);
    }
}
