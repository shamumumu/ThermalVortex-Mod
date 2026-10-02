using System.Globalization;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Powers;

// Read the current instance when formatting a tooltip. The native clone path
// rebinds each variable's owner, so these delegates never capture another power.
internal static class PowerDescriptionValues
{
    internal static IEnumerable<DynamicVar> CreateVariables(ThermalVortexPower power) => power switch
    {
        FullArmorThunderLancePower =>
            [Number<FullArmorThunderLancePower>("UpkeepBlock", 6, p => p.UpkeepBlock)],
        GoldSarcophagusReturnPower =>
            [Text<GoldSarcophagusReturnPower>("SealedCardName", "", p => p.SealedCardName),
             Number<GoldSarcophagusReturnPower>("TurnsRemaining", 3, p => p.TurnsRemaining)],
        CyberBeaconPower =>
            [Text<CyberBeaconPower>("GeneratedCardName", "", p => CyberDragonName(p.GenerateUpgradedDragon))],
        CyberScrapYardPower =>
            [Text<CyberScrapYardPower>("GeneratedCardName", "", p => CyberDragonName(p.GenerateUpgradedDragon))],
        MillenniumTemplePower =>
            [Number<MillenniumTemplePower>("BlockPerCount", 2, p => p.BlockPerCount)],
        FusionGatePower =>
            [Number<FusionGatePower>("LifeLoss", 3, p => p.LifeLoss)],
        MillenniumContractBookPower =>
            [Number<MillenniumContractBookPower>("EnergyGain", 2, p => p.EnergyGain),
             Number<MillenniumContractBookPower>("DrawReduction", 1, p => p.DrawReduction)],
        MillenniumSleepingTabletPower =>
            [Number<MillenniumSleepingTabletPower>("RandomPieceCount", 1, p => p.RandomPieceCount),
             Number<MillenniumSleepingTabletPower>("ChosenPieceCount", 0, p => p.ChosenPieceCount)],
        FusionPreparationPower =>
            [Number<FusionPreparationPower>("FusionCount", 1, p => p.PendingCardCount),
             Number<FusionPreparationPower>("EnergyGain", 1, p => p.PendingEnergy)],
        OverloadedSummonSlotPower =>
            [Number<OverloadedSummonSlotPower>("SlotsToDestroy", OverloadedSummonSlotPower.SlotsToDestroyAtTurnEnd,
                p => OverloadedSummonSlotPower.SlotsToDestroyAtTurnEnd * p.PendingResolutionCount)],
        CyberWeldingPower =>
            [Number<CyberWeldingPower>("NextMultiplier", 1.5m, p => p.NextDamageMultiplier),
             Number<CyberWeldingPower>("AfterReduction", 50, p => p.AfterOutputReductionPercent),
             Number<CyberWeldingPower>("CurrentReduction", 0, p => p.CurrentReductionPercent)],
        _ => []
    };

    internal static string SelectDescriptionKey(ThermalVortexPower power, string key)
    {
        if (!power.IsMutable)
            return key;

        return power switch
        {
            MillenniumSleepingTabletPower { RandomPieceCount: > 0, ChosenPieceCount: > 0 } => key + ".mixed",
            MillenniumSleepingTabletPower { RandomPieceCount: 0, ChosenPieceCount: > 0 } => key + ".choose",
            RaTransitionPower { HasPhoenixTransition: true, HasRaTransition: false } => key + ".phoenix",
            RaTransitionPower { HasPhoenixTransition: false, HasRaTransition: true } => key + ".ra",
            CyberWeldingPower { HasPendingOutput: false } => key + ".resolved",
            _ => key
        };
    }

    private static string CyberDragonName(bool upgraded) =>
        new LocString("cards", "THERMALVORTEX-CYBER_DRAGON.title").GetFormattedText()
        + (upgraded ? "+" : "");

    private static DynamicVar Number<T>(string name, decimal fallback, Func<T, decimal> read)
        where T : ThermalVortexPower => new LiveNumber<T>(name, fallback, read);

    private static DynamicVar Text<T>(string name, string fallback, Func<T, string> read)
        where T : ThermalVortexPower => new LiveText<T>(name, fallback, read);

    private sealed class LiveNumber<T>(string name, decimal fallback, Func<T, decimal> read)
        : DynamicVar(name, fallback) where T : ThermalVortexPower
    {
        private T _power;

        public override void SetOwner(AbstractModel owner)
        {
            base.SetOwner(owner);
            _power = owner as T;
        }

        private decimal CurrentValue => _power?.IsMutable == true ? read(_power) : BaseValue;
        public override string ToString() => CurrentValue.ToString("0.##", CultureInfo.InvariantCulture);
        protected override decimal GetBaseValueForIConvertible() => CurrentValue;
    }

    private sealed class LiveText<T>(string name, string fallback, Func<T, string> read)
        : StringVar(name, fallback) where T : ThermalVortexPower
    {
        private T _power;

        public override void SetOwner(AbstractModel owner)
        {
            base.SetOwner(owner);
            _power = owner as T;
        }

        public override string ToString() =>
            _power?.IsMutable == true ? read(_power) : StringValue;
    }
}
