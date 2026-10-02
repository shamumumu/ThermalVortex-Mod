using System.Globalization;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.Cards;

internal static class CardEffectPreviewValues
{
    private static readonly string[] Names =
    [
        "Damage", "HitCount", "FirstDamage", "RemainingHitCount", "Block", "Weak",
        "Materials", "MonsterHp", "LifeLoss", "Heal", "Draw", "HandCount",
        "GeneratedPairs", "Count", "SelectedCount", "ReturnCount"
    ];

    internal static void AddArgs(CardModel card, LocString locString)
    {
        foreach (var name in Names)
        {
            locString.Add($"HasPreview{name}", false);
            locString.Add($"Preview{name}", string.Empty);
        }

        if (!CardEffectPreviewContext.TryGet(card, out var state))
            return;

        // A description may be queried while the selection UI is being removed.
        // Read the original card, and never project choices into its combat state.
        var values = new Dictionary<string, decimal>();
        try
        {
            var source = state.SourceCard;
            var owner = source?.Owner;
            if (owner?.Creature?.CombatState is null || owner.PlayerCombatState is null)
                return;

            AddNativeValues(source, state.Target, values);
            AddEffectValues(source, owner, state, values);
        }
        catch
        {
            // Preserve the short formula when its source/target is no longer valid.
            return;
        }

        foreach (var (name, value) in values)
        {
            locString.Add($"HasPreview{name}", true);
            var number = Math.Max(0, Math.Floor(value)).ToString("0", CultureInfo.InvariantCulture);
            locString.Add($"Preview{name}", $"[green]{number}[/green]");
        }
    }

    private static void AddNativeValues(
        CardModel card,
        Creature target,
        IDictionary<string, decimal> values)
    {
        if (card.DynamicVars.ContainsKey("Damage") && card.DynamicVars["Damage"] is DamageVar damage)
            values["Damage"] = PreviewDamage(card, target, damage.BaseValue, damage.Props);
        else if (card.DynamicVars.ContainsKey("FixedDamage"))
            values["Damage"] = PreviewDamage(card, target, card.DynamicVars["FixedDamage"].BaseValue);

        if (card.DynamicVars.ContainsKey("Block") && card.DynamicVars["Block"] is BlockVar block)
        {
            values["Block"] = PreviewBlock(card, block.BaseValue, block.Props);
        }

        if (card.DynamicVars.ContainsKey("Weak"))
            values["Weak"] = card.DynamicVars["Weak"].BaseValue;
    }

    private static void AddEffectValues(
        CardModel card,
        Player owner,
        CardEffectPreviewState state,
        IDictionary<string, decimal> values)
    {
        var target = state.Target;
        switch (card)
        {
            case GrenMajuDaEiza gren:
                values["Damage"] = PreviewDamage(card, target, FormulaUnit(card));
                values["HitCount"] = gren.GetExhaustedCardCount();
                break;

            case CyberNextDragon next:
            {
                var count = next.GetDamageMaterialCount(
                    MonsterFieldService.GetMonstersForPreview(owner).Count(monster => !CyberSeries.IsCyberMonster(monster)));
                var inherited = CyberDevourState.GetInheritedAttackContribution(card);
                values["Damage"] = PreviewDamage(card, target, FormulaUnit(card) + (count == 1 ? inherited : 0));
                values["HitCount"] = count;
                if (inherited > 0 && count > 1)
                {
                    values["FirstDamage"] = PreviewDamage(card, target, FormulaUnit(card) + inherited);
                    values["RemainingHitCount"] = count - 1;
                }
                break;
            }

            case OverheatedCoil overheated:
                values["Damage"] = PreviewDamage(card, target, overheated.DamageAmount);
                values["HitCount"] = MonsterFieldService.HasFieldOrPendingMonsterForPreview<ThunderPoleCard>(owner) ? 2 : 1;
                break;

            case StabilizedMagneticCoil stabilized:
                values["Block"] = PreviewBlock(card,
                    stabilized.BlockPerMonsterAmount
                        * MonsterFieldService.CountFieldOrPendingMonstersForPreview(owner, card),
                    ValueProp.Unpowered);
                break;

            case DormantMagneticFieldBeast beast:
                values.Remove("Weak");
                if (TryGetMaterialCount(beast, state, out var dormantMaterials))
                {
                    values["Materials"] = dormantMaterials;
                    values["Block"] = PreviewBlock(card, 8 + 4 * dormantMaterials, ValueProp.Unpowered);
                    values["Weak"] = card.CurrentUpgradeLevel > 0 ? dormantMaterials : 1;
                }
                break;

            case DivineArsenalFurnaceGod furnace:
                if (TryGetMaterialCount(furnace, state, out var furnaceMaterials))
                {
                    var handCount = furnace.GetDamageHandCount();
                    if (state.IsFusionSelection && state.SelectedCards is not null)
                    {
                        handCount -= state.SelectedCards.Count(material =>
                            material != card && material.Pile?.Type == PileType.Hand);
                    }
                    handCount = Math.Max(0, handCount);
                    values["Materials"] = furnaceMaterials;
                    values["HandCount"] = handCount;
                    values["Damage"] = PreviewDamage(card, target, FormulaUnit(card) * handCount * furnaceMaterials);
                }
                break;

            case ChimeratechOverdragon chimeratech:
                if (TryGetMaterialCount(chimeratech, state, out var chimeraMaterials))
                {
                    values["Materials"] = chimeraMaterials;
                    values["MonsterHp"] = ChimeratechOverdragon.MaxHpPerMaterial * chimeraMaterials
                        + CyberDevourState.GetMaxHpBonus(card);
                    values["Damage"] = PreviewDamage(card, target, FormulaUnit(card) * chimeraMaterials
                        + CyberDevourState.GetInheritedAttackContribution(card));
                }
                break;

            case EvilExodia evil:
                values.Remove("Damage");
                if (TryGetMaterialCount(evil, state, out var evilMaterials))
                {
                    var count = MillenniumResolution.GetCount(state.SourceCard);
                    values["Materials"] = evilMaterials;
                    values["Count"] = count;
                    values["Damage"] = PreviewDamage(card, target, (decimal)count * evilMaterials, ValueProp.Unpowered);
                }
                break;

            case ExodiaGuardian guardian:
                values.Remove("Block");
                if (TryGetMaterialCount(guardian, state, out var guardianMaterials))
                {
                    var count = MillenniumResolution.GetCount(state.SourceCard);
                    values["Materials"] = guardianMaterials;
                    values["Count"] = count;
                    values["Block"] = PreviewBlock(card, (decimal)count * guardianMaterials, ValueProp.Unpowered);
                }
                break;

            case CyberEndDragon end:
                if (TryGetMaterialCount(end, state, out var endMaterials))
                {
                    values["Materials"] = endMaterials;
                    values["MonsterHp"] = CyberEndDragon.HpPerMaterial * endMaterials
                        + CyberDevourState.GetMaxHpBonus(card);
                    values["LifeLoss"] = CyberWeldingPower.ScaleLifeLoss(card,
                        card.DynamicVars["HpLossPerMaterial"].IntValue * endMaterials);
                }
                break;

            case WingedDragonOfRaSphereMode sphere when card.CurrentUpgradeLevel > 0:
                if (state.IsFusionSelection && state.SelectedCards is not null)
                    values["MonsterHp"] = Math.Max(WingedDragonOfRaSphereMode.BaseMaxHp, MaterialHealth(state.SelectedCards));
                else if (HasCommittedMaterials(sphere))
                    values["MonsterHp"] = sphere.MonsterMaxHp;
                break;

            case ChineseWok when state.SelectedCards is not null:
                values["Heal"] = MaterialHealth(state.SelectedCards.Take(1));
                break;

            case CrushCardVirus when state.SelectedCards is not null:
                values["LifeLoss"] = CrushCardVirus.GetMaximumHealthTotal(state.SelectedCards);
                values["SelectedCount"] = state.SelectedCards.Count;
                break;

            case Relinquished when Relinquished.IsMinion(target):
                values["MonsterHp"] = Math.Max(1, target.CurrentHp);
                break;

            case HopeForEscape:
                values["Draw"] = HopeForEscape.CalculateDrawCount(owner.Creature.CurrentHp, owner.Creature.MaxHp);
                values["LifeLoss"] = HopeForEscape.CalculateLifeLoss(owner.Creature.MaxHp);
                break;

            case SolemnJudgment:
                values["LifeLoss"] = SolemnJudgment.CalculateLifePayment(owner.Creature.CurrentHp);
                break;

            case CardDestruction:
                values["HandCount"] = CardPile.GetCards(owner, PileType.Hand).Count(inHand => inHand != card);
                values["Draw"] = values["HandCount"];
                break;

            case CyberSymbiosis:
                values["GeneratedPairs"] = CyberSeries.GetCyberLarvaeGeneratedThisCombat(owner);
                break;

            case PhantomSummoningGodExodia god when god.MillenniumCrossCount is { } lockedCount:
                values["Count"] = lockedCount;
                values["HitCount"] = lockedCount;
                values["Damage"] = PreviewDamage(card, target, lockedCount, ValueProp.Unpowered);
                break;

            case WingedDragonOfRa ra when target?.IsEnemy == true:
                values["Damage"] = PreviewDamage(card, null, ra.GetRecordedDamage(target), ValueProp.Unpowered);
                break;

            case ForbiddenDroplet when state.SelectedCards is not null:
                values["SelectedCount"] = state.SelectedCards.Count;
                break;

            case CyberRepairPlant when card.CurrentUpgradeLevel > 0:
                values["ReturnCount"] = CardSelectionHelper.DiscardPileCyberMonsters(owner).Count;
                break;

            case PotOfAvarice when state.SelectedCards is not null:
                values["ReturnCount"] = state.SelectedCards.Count;
                break;
        }
    }

    private static int FormulaUnit(CardModel card) => card.DynamicVars.ExtraDamage.IntValue;

    private static decimal PreviewBlock(CardModel card, decimal baseBlock, ValueProp props)
    {
        var owner = card?.Owner?.Creature;
        return baseBlock > 0
            && (BrilliantRebootBlockLock.IsActive(owner)
                || owner?.HasPower<NoBlockPower>() == true)
            ? 0
            : Hook.ModifyBlock(
                owner.CombatState,
                owner,
                baseBlock,
                props,
                card,
                null,
                out _);
    }

    private static decimal PreviewDamage(
        CardModel card,
        Creature target,
        decimal baseDamage,
        ValueProp props = ValueProp.Move) =>
        Hook.ModifyDamage(
            card.Owner.RunState,
            card.Owner.Creature.CombatState,
            target,
            card.Owner.Creature,
            baseDamage,
            props,
            card,
            ModifyDamageHookType.All,
            target is null && card.TargetType == TargetType.AllEnemies
                ? CardPreviewMode.MultiCreatureTargeting
                : CardPreviewMode.Normal,
            out _);

    private static bool TryGetMaterialCount(XyzMonsterCard card, CardEffectPreviewState state, out int count)
    {
        if (state.IsFusionSelection)
        {
            count = state.SelectedCards?.Count ?? 0;
            return state.SelectedCards is not null;
        }

        count = card.Materials;
        if (HasCommittedMaterials(card))
            return true;

        // Fixed-count cards have a known result even before the particular
        // materials are chosen. Variable choices remain formulas until chosen.
        if (card is not DivineArsenalFurnaceGod && card.MinimumMaterials == card.MaximumMaterials)
        {
            count = card.MinimumMaterials;
            return true;
        }

        return false;
    }

    private static bool HasCommittedMaterials(XyzMonsterCard card) =>
        card.Materials > 0
        && (card.IsExtraDeckSummonPlayAuthorized
            || card.IsResolvingAuthorizedExtraDeckSummon
            || MonsterFieldService.IsOnField(card));

    private static int MaterialHealth(IEnumerable<CardModel> materials) =>
        materials.Sum(material => Math.Max(0, MonsterFieldHealthService.HasHealth(material)
            ? MonsterFieldHealthService.PeekHealth(material).CurrentHp
            : MonsterFieldHealthService.GetInitialMaxHp(material)));
}
