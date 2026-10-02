using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class SummonRulesPower : ThermalVortexPower, IMonsterFieldEnterListener
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    protected override IEnumerable<DynamicVar> CanonicalVars
    {
        get
        {
            foreach (var dynamicVar in base.CanonicalVars)
                yield return dynamicVar;

            yield return new MonsterFieldCapacityVar();
        }
    }

    public override LocString Description
    {
        get
        {
            var description = base.Description;
            description.Add("MonsterFieldCapacity", MonsterFieldService.GetCapacity(Owner));
            return description;
        }
    }

    public bool CanSummon(CardModel card) => MonsterFieldService.CanPlaceOnField(card);

    public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal cost, out decimal modifiedCost)
    {
        modifiedCost = cost;
        if (card is not CyberDragon dragon
            || card.Owner?.Creature != Owner
            || !dragon.HasIntrinsicFreeCost)
            return false;

        modifiedCost = 0;
        return cost != 0;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        RefreshCyberDragonCosts(player);
        if (player?.Creature != Owner)
            return;

        foreach (var monster in MonsterFieldService.GetMonsters(player).ToList())
        {
            if (!MonsterFieldService.IsOnField(monster))
                continue;
            await CyberDevourState.TriggerUpkeepEffects(ctx, monster, this);
            if (MonsterFieldService.IsOnField(monster) && monster is ChaosPhantom phantom)
                await phantom.TriggerCopiedUpkeep(ctx);
        }
    }

    public override Task AfterCardDrawn(PlayerChoiceContext ctx, CardModel card, bool fromHandDraw)
    {
        RefreshCyberDragonCosts(card?.Owner);
        return Task.CompletedTask;
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel source)
    {
        MonsterFieldUpgradeLockService.CompleteAwaitingPileChange(card, oldPileType);
        RefreshCyberDragonCosts(card?.Owner);
        var destinationPileType = card?.Pile?.Type;
        if (destinationPileType is not null)
            ExodiaTorso.QueueVictoryCheck(card, destinationPileType.Value);

        if (destinationPileType != MonsterFieldPile.FieldPileType
            || card?.Owner?.Creature != Owner
            || !MonsterFieldService.IsOnField(card))
        {
            return;
        }

        var enterEvent = new MonsterFieldEnterEvent(
            card,
            MonsterFieldHealthService.PeekHealth(card));
        await MillenniumResolution.ResolveFieldEntry(enterEvent, source, oldPileType);
    }

    public override Task AfterCardEnteredCombat(CardModel card)
    {
        RefreshCyberDragonCosts(card?.Owner);
        QueueAutoPlayTorsoForCurrentPile(card);
        return Task.CompletedTask;
    }

    public override Task AfterCardGeneratedForCombat(CardModel card, Player creator)
    {
        if (card?.Owner?.Creature == Owner)
        {
            RefreshCyberDragonCosts(card.Owner);
            QueueAutoPlayTorsoForCurrentPile(card);
        }

        return Task.CompletedTask;
    }

    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        if (cardPlay?.ResultPile == MonsterFieldPile.FieldPileType)
            MonsterFieldUpgradeLockService.BeginAwaitingFieldEntry(cardPlay.Card);

        if (cardPlay?.Card is CyberDragon cyberDragon)
            cyberDragon.RefreshSpecialSummonCost();

        return YugiCardActionVfx.BeforeCardPlayed(Owner, cardPlay);
    }

    public override Task AfterCardPlayed(PlayerChoiceContext ctx, CardPlay cardPlay)
    {
        RefreshCyberDragonCosts(cardPlay?.Card?.Owner);
        return Task.CompletedTask;
    }

    public void AfterMonsterEnteredField(MonsterFieldEnterEvent enterEvent)
    {
        ExodiaTorso.QueueVictoryCheck(
            enterEvent.Card,
            MonsterFieldPile.FieldPileType);
    }

    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
    {
        if (card is MillenniumCross cross)
            return cross.CanSummonExodia();

        if (!MonsterFieldService.IsFieldMonster(card))
            return true;

        // Auto-play bypasses CardModel.CanPlay, so preserve the same Extra Deck
        // authorization gate that protects manual play.
        if (card is XyzMonsterCard { IsExtraDeckSummonPlayAuthorized: false })
            return false;

        if (card is CyberNextDragon cyberNextDragon)
            return cyberNextDragon.CanPlaceOnFieldAfterUsingNonCyberMaterials();

        return card is MonsterCard && autoPlayType == AutoPlayType.None
            ? MonsterFieldService.CanNormalSummonOnField(card)
            : MonsterFieldService.CanPlaceOnField(card);
    }

    private void RefreshCyberDragonCosts(Player player)
    {
        if (player?.Creature == Owner)
            CyberDragon.RefreshSpecialSummonCosts(player);
    }

    private static void QueueAutoPlayTorsoForCurrentPile(CardModel card)
    {
        var pileType = card?.Pile?.Type;
        if (pileType is not null)
            ExodiaTorso.QueueVictoryCheck(card, pileType.Value);
    }

    private sealed class MonsterFieldCapacityVar() :
        DynamicVar("MonsterFieldCapacity", MonsterFieldService.BaseCapacity)
    {
        private SummonRulesPower _rulesOwner;

        public override void SetOwner(AbstractModel owner)
        {
            base.SetOwner(owner);
            _rulesOwner = owner as SummonRulesPower;
        }

        public override string ToString() =>
            MonsterFieldService.GetCapacity(_rulesOwner?.Owner).ToString();

        protected override decimal GetBaseValueForIConvertible() =>
            MonsterFieldService.GetCapacity(_rulesOwner?.Owner);
    }
}
