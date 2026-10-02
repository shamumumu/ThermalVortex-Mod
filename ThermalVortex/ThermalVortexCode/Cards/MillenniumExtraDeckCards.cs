using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MillenniumGrandThief : XyzMonsterCard, IMillenniumCard, IMillenniumMonster, IMonsterFieldEnterResolvedListener
{
    public const int RequiredMaterials = 2;
    public const int BaseMaxHp = 5;
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;
    public override string CustomPortraitPath => "millennium_grand_thief.png".BigCardImagePath();
    public override string PortraitPath => "millennium_grand_thief.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_grand_thief.png".CardImagePath();
    internal bool IsGoldOffer { get; private set; }
    internal int GoldOfferPrice { get; private set; }
    internal int GoldOfferPieces { get; private set; }

    public override string Title => IsGoldOffer
        ? OfferText(GoldOfferPieces == 0 ? "skipTitle" : "offerTitle").GetFormattedText()
        : base.Title;

    public MillenniumGrandThief() : base(CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_UNIQUE_HAND"),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_MONSTER"),
            KeywordExplanation("THERMALVORTEX-SEALED_MILLENNIUM_PIECE"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_GOLD_EXCHANGE", _ => LocManager.Instance?.Language != "zhs"));
    }

    protected override Task OnPlay(PlayerChoiceContext ctx, CardPlay play) => Task.CompletedTask;

    public async Task AfterMonsterEnteredFieldResolved(PlayerChoiceContext ctx, MonsterFieldEnterEvent enterEvent)
    {
        var isValid = MonsterFieldService.CaptureMaterialUseValidity(Owner);
        if (!isValid())
            return;

        var offers = GetAffordableOffers(Owner.Gold, CurrentUpgradeLevel > 0);
        if (offers.Count == 0)
            return;

        // Each card is an inert display copy, never a combat card or owned extra-deck entry.
        // Put the explicit zero-payment choice first so an automated choice never spends gold.
        var choices = new List<CardModel> { CreateOffer(0, 0) };
        choices.AddRange(offers.Select(offer => CreateOffer(offer.Price, offer.Pieces)));
        var chosen = await CardSelectionHelper.ChooseOne(
            ctx, Owner, choices,
            "THERMALVORTEX-MILLENNIUM_GRAND_THIEF.selectionPrompt",
            cancelable: true,
            sourceCard: this) as MillenniumGrandThief;
        if (!isValid() || chosen is null || chosen.GoldOfferPieces <= 0
            || !choices.Any(card => ReferenceEquals(card, chosen))
            || Owner.Gold < chosen.GoldOfferPrice)
            return;

        var goldBefore = Owner.Gold;
        await PlayerCmd.LoseGold(chosen.GoldOfferPrice, Owner, GoldLossType.Spent);
        if (!isValid() || goldBefore - Owner.Gold < chosen.GoldOfferPrice)
            return;

        for (var i = 0; i < chosen.GoldOfferPieces && isValid(); i++)
            await MillenniumSeries.AddChosenSealedPieceToHand(ctx, Owner, this);
    }

    internal static IReadOnlyList<(int Price, int Pieces)> GetAffordableOffers(decimal gold, bool upgraded)
    {
        var result = new List<(int Price, int Pieces)>();
        long price = upgraded ? 7 : 10;
        for (var pieces = 1; price <= int.MaxValue && price <= gold; pieces++, price *= 10)
            result.Add(((int)price, pieces));
        return result;
    }

    private MillenniumGrandThief CreateOffer(int price, int pieces)
    {
        var offer = (MillenniumGrandThief)ModelDb.Card<MillenniumGrandThief>().ToMutable();
        offer.IsGoldOffer = true;
        offer.GoldOfferPrice = price;
        offer.GoldOfferPieces = pieces;
        return offer;
    }

    private LocString OfferText(string entry)
    {
        var text = new LocString("cards", $"THERMALVORTEX-MILLENNIUM_GRAND_THIEF.{entry}");
        text.Add("Gold", GoldOfferPrice);
        text.Add("Pieces", GoldOfferPieces);
        return text;
    }

    protected override void AddExtraArgsToDescription(LocString locString)
    {
        base.AddExtraArgsToDescription(locString);
        locString.Add("IsGoldOffer", IsGoldOffer);
        locString.Add("GoldOfferDescription", OfferText(GoldOfferPieces == 0 ? "skipDescription" : "offerDescription"));
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class ExodiaSummoner : XyzMonsterCard, IMillenniumCard, IMillenniumMonster, IMonsterFieldEnterResolvedListener
{
    public const int RequiredMaterials = 2;
    public const int BaseMaxHp = 5;
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;
    public override string CustomPortraitPath => "exodia_summoner.png".BigCardImagePath();
    public override string PortraitPath => "exodia_summoner.png".CardImagePath();
    public override string BetaPortraitPath => "exodia_summoner.png".CardImagePath();

    public ExodiaSummoner() : base(CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_UNIQUE_HAND"),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_REWARD_MONSTER", _ => LocManager.Instance?.Language != "zhs"));
    }

    protected override Task OnPlay(PlayerChoiceContext ctx, CardPlay play) => Task.CompletedTask;

    public async Task AfterMonsterEnteredFieldResolved(PlayerChoiceContext ctx, MonsterFieldEnterEvent enterEvent)
    {
        await ThermalVortexCommandCompat.ApplyPower<ExodiaSummonerPower>(
            ctx, Owner.Creature, 1, Owner.Creature, this, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class MillenniumMasterKey : XyzMonsterCard, IMillenniumCard, IMillenniumMonster, IMonsterFieldEnterResolvedListener
{
    public const int RequiredMaterials = 2;
    public const int BaseMaxHp = 5;
    public override int MinimumMaterials => RequiredMaterials;
    public override int MaximumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;
    public override string CustomPortraitPath => "millennium_master_key.png".BigCardImagePath();
    public override string PortraitPath => "millennium_master_key.png".CardImagePath();
    public override string BetaPortraitPath => "millennium_master_key.png".CardImagePath();

    public MillenniumMasterKey() : base(CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_UNIQUE_HAND"),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_REWARD_MONSTER", _ => LocManager.Instance?.Language != "zhs"));
    }

    protected override Task OnPlay(PlayerChoiceContext ctx, CardPlay play) => Task.CompletedTask;

    public async Task AfterMonsterEnteredFieldResolved(PlayerChoiceContext ctx, MonsterFieldEnterEvent enterEvent)
    {
        var isValid = MonsterFieldService.CaptureMaterialUseValidity(Owner);
        foreach (var model in MillenniumExtraDeckGeneration.GetMainRewardMillenniumMonsters())
        {
            if (!isValid())
                return;

            var card = Owner.Creature.CombatState.CreateCard(model, Owner);
            if (CurrentUpgradeLevel > 0)
                CardCmd.Upgrade(card);
            var result = await ThermalVortexCommandCompat.AddGeneratedCardToCombat(
                card, PileType.Draw, Owner, CardPilePosition.Random);
            // Native generation from no pile directly into Draw has no card or
            // fly-to-pile animation to finish. Its contents update immediately,
            // but the draw-pile counter only listens for this completion event.
            if (result.success && result.cardAdded?.Pile is { Type: PileType.Draw } drawPile)
                drawPile.InvokeCardAddFinished();
        }
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class EvilExodia : XyzMonsterCard, IMillenniumCard, IMillenniumMonster,
    IMonsterFieldEnterResolvedListener
{
    public const int RequiredMaterials = 2;
    public const int BaseMaxHp = 5;
    private Creature _summonTarget;
    public override int MinimumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;
    public override string CustomPortraitPath => "evil_exodia.png".BigCardImagePath();
    public override string PortraitPath => "evil_exodia.png".CardImagePath();
    public override string BetaPortraitPath => "evil_exodia.png".CardImagePath();

    public EvilExodia() : base(CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithFormulaDamage(1, 1,
            (card, _) => ((EvilExodia)card).GetDamageMultiplier(),
            ValueProp.Unpowered);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_UNIQUE_HAND"),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_COUNT"),
            ExtraDeckExplanation());
    }

    protected override Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        if (play.IsFirstInSeries)
            _summonTarget = play.Target;
        return Task.CompletedTask;
    }

    public async Task AfterMonsterEnteredFieldResolved(PlayerChoiceContext ctx, MonsterFieldEnterEvent enterEvent)
    {
        var damage = FormulaDamageUnit * MillenniumResolution.GetCount(enterEvent.Card)
            * (decimal)Math.Max(MinimumMaterials, Materials);
        var target = _summonTarget;
        _summonTarget = null;
        if (target?.IsAlive != true)
            target = await EffectTargeting.ResolveForAutoPlay(ctx, Owner, this);
        if (target?.IsAlive != true || !MonsterFieldService.CaptureMaterialUseValidity(Owner)())
            return;

        await ThermalVortexCombatVfx.CardAttack(this, target, damage, 1).Unpowered().Execute(ctx);
    }

    private decimal GetDamageMultiplier() =>
        MillenniumResolution.GetCount(this) * (decimal)Math.Max(MinimumMaterials, Materials);

    protected override void OnUpgrade() => ConstructedUpgrade();
}

public class ExodiaGuardian : XyzMonsterCard, IMillenniumCard, IMillenniumMonster,
    IMonsterFieldEnterResolvedListener
{
    public const int RequiredMaterials = 2;
    public const int BaseMaxHp = 5;
    public override int MinimumMaterials => RequiredMaterials;
    public override int MonsterMaxHp => BaseMaxHp;
    public override string CustomPortraitPath => "exodia_guardian.png".BigCardImagePath();
    public override string PortraitPath => "exodia_guardian.png".CardImagePath();
    public override string BetaPortraitPath => "exodia_guardian.png".CardImagePath();

    public ExodiaGuardian() : base(CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.None);
        WithExplanations(
            KeywordExplanation("THERMALVORTEX-FUSION_MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_UNIQUE_HAND"),
            KeywordExplanation("THERMALVORTEX-MONSTER"),
            KeywordExplanation("THERMALVORTEX-MILLENNIUM_COUNT"),
            ExtraDeckExplanation(),
            BlockExplanation());
    }

    protected override Task OnPlay(PlayerChoiceContext ctx, CardPlay play) => Task.CompletedTask;

    public async Task AfterMonsterEnteredFieldResolved(PlayerChoiceContext ctx, MonsterFieldEnterEvent enterEvent)
    {
        if (!MonsterFieldService.CaptureMaterialUseValidity(Owner)())
            return;

        var block = MillenniumResolution.GetCount(enterEvent.Card) * (decimal)Math.Max(MinimumMaterials, Materials);
        await ThermalVortexCommandCompat.GainBlockFromCardEffect(
            Owner.Creature, block, ValueProp.Unpowered, null, false);
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}

internal static class MillenniumExtraDeckGeneration
{
    // Canonical series markers follow Chinese names, regardless of the display language.
    // Intersect with the reward catalog before generating to exclude tokens and extra-deck monsters.
    internal static IReadOnlyList<CardModel> GetMainRewardMillenniumMonsters() =>
        RewardPoolCatalog.GetMainRewardCandidates(RewardPoolCardOrigin.ThermalVortex)
            .Where(card => card is MonsterCard
                && card is not ExtraDeckCard
                && MillenniumSeries.IsMillenniumMonster(card)
                && !MillenniumSeries.IsSealedPiece(card))
            .DistinctBy(card => card.Id)
            .ToArray();

    internal static CardModel CreateRandomMainRewardMillenniumMonster(Player player)
    {
        var combat = player?.Creature?.CombatState;
        var rng = player?.RunState?.Rng?.CombatCardGeneration;
        if (combat is null || rng is null)
            return null;

        var candidates = GetMainRewardMillenniumMonsters();
        return candidates.Count == 0 ? null : combat.CreateCard(rng.NextItem(candidates), player);
    }
}
