using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class CrushCardVirus : MainDeckCard
{
    public override string CustomPortraitPath => "crush_card_virus.png".BigCardImagePath();
    public override string PortraitPath => "crush_card_virus.png".CardImagePath();
    public override string BetaPortraitPath => "crush_card_virus.png".CardImagePath();

    public CrushCardVirus() : base(1, CardType.Skill, CardRarity.Rare, TargetType.AllEnemies)
    {
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || MonsterFieldService.GetMonsters(Owner).Any());

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var monsters = MonsterFieldService.GetMonsters(Owner).ToList();
        var selectedMonsters = new List<CardModel>();
        if (CurrentUpgradeLevel > 0)
        {
            selectedMonsters.AddRange(await CardSelectionHelper.ChooseMany(
                ctx,
                Owner,
                monsters,
                "THERMALVORTEX-CRUSH_CARD_VIRUS.selectionPrompt",
                1,
                monsters.Count,
                false,
                sourceCard: this));
        }
        else
        {
            var selected = await CardSelectionHelper.ChooseOne(
                ctx,
                Owner,
                monsters,
                "THERMALVORTEX-CRUSH_CARD_VIRUS.selectionPrompt",
                false,
                sourceCard: this);
            if (selected is not null)
                selectedMonsters.Add(selected);
        }

        selectedMonsters = selectedMonsters.Where(MonsterFieldService.IsOnField)
            .Distinct(new ReferenceComparer<CardModel>()).ToList();
        if (selectedMonsters.Count == 0)
            return;

        // Snapshot the shared amount before any loss or leave effect can
        // change these monsters' maximum health or the enemy lineup.
        var hpLoss = GetMaximumHealthTotal(selectedMonsters);
        if (hpLoss <= 0)
            return;

        var enemies = Owner.Creature.CombatState.HittableEnemies.ToList();
        await MonsterFieldHealthService.DamageMonsters(
            ctx, selectedMonsters, hpLoss, this, MonsterFieldLeaveReason.FieldClear);

        foreach (var enemy in enemies)
        {
            if (enemy.IsDead)
                continue;
            ThermalVortexCombatVfx.PlayImpact(enemy);
            await CreatureCmd.SetCurrentHp(enemy, Math.Max(0, enemy.CurrentHp - hpLoss));
        }
    }

    internal static int GetMaximumHealthTotal(IEnumerable<CardModel> monsters) =>
        monsters.Sum(card =>
        {
            var health = MonsterFieldHealthService.PeekHealth(card);
            return Math.Max(0, health.IsValid ? health.MaxHp : MonsterFieldHealthService.GetInitialMaxHp(card));
        });

    protected override void OnUpgrade() => ConstructedUpgrade();
}
