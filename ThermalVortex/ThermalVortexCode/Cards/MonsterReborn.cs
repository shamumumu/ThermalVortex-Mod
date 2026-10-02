using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MonsterReborn : MainDeckCard
{
    public override string CustomPortraitPath => "monster_reborn.png".BigCardImagePath();
    public override string PortraitPath => "monster_reborn.png".CardImagePath();
    public override string BetaPortraitPath => "monster_reborn.png".CardImagePath();

    public MonsterReborn() : base(3, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
        WithCostUpgradeBy(-1);
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(KeywordExplanation("THERMALVORTEX-MONSTER"));
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || RebornCandidates().Any());

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var owner = Owner;
        if (owner is null)
            return;

        var candidates = RebornCandidates();
        if (candidates.Count == 0)
            return;

        var selected = await CardSelectionHelper.ChooseOne(
            ctx,
            owner,
            candidates,
            "THERMALVORTEX-MONSTER_REBORN.selectionPrompt",
            false,
            requireManualConfirmation: false);
        if (!IsCurrentRebornCandidate(owner, selected))
            return;

        var combat = owner.Creature.CombatState;
        var combatWasActive = CombatManager.Instance?.IsInProgress == true;
        var playedNative = await YugiCardActionVfx.PlaySelectedCast(this, play);
        if (!ReferenceEquals(owner.Creature.CombatState, combat)
            || (combatWasActive && CombatManager.Instance?.IsInProgress != true))
            return;
        if (!playedNative)
        {
            await ThermalVortexCombatVfx.PlaySafeAsync(
                nameof(MonsterRebornVfx),
                () => MonsterRebornVfx.PlayAsync(owner.Creature));
        }

        if (!IsCurrentRebornCandidate(owner, selected))
            return;

        var result = await CardPileCmd.Add(selected, PileType.Hand, CardPilePosition.Top, this, false);
        if (result.success
            && ReferenceEquals(result.cardAdded, selected)
            && CardSelectionHelper.IsCurrentPileCard(owner, selected, PileType.Hand))
        {
            selected.SetToFreeThisTurn();
        }
    }

    private IReadOnlyList<CardModel> RebornCandidates() =>
        CardSelectionHelper.DiscardPileMonsters(Owner);

    private static bool IsCurrentRebornCandidate(Player owner, CardModel card) =>
        MonsterFieldService.IsFieldMonster(card)
        && !MonsterFieldService.IsOnField(card)
        && CardSelectionHelper.IsCurrentPileCard(owner, card, PileType.Discard);

}
