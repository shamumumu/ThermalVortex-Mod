using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Extensions;

namespace ThermalVortex.ThermalVortexCode.Cards;

public class MagicalHats : MainDeckCard
{
    public override string CustomPortraitPath => "magical_hats.png".BigCardImagePath();
    public override string PortraitPath => "magical_hats.png".CardImagePath();
    public override string BetaPortraitPath => "magical_hats.png".CardImagePath();

    public MagicalHats() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
        WithKeyword(CardKeyword.Exhaust, UpgradeType.Remove);
    }

    protected override bool IsPlayable =>
        base.IsPlayable && (Owner is null || HasAllCategories());

    protected override async Task OnPlay(PlayerChoiceContext ctx, CardPlay play)
    {
        var selected = new CardModel[3];
        var presentedSteps = new Stack<int>();
        var step = 0;
        while (step < selected.Length)
        {
            var choice = await ChooseFromDraw(ctx, step, presentedSteps.Count > 0);
            if (choice.WentBack)
            {
                if (presentedSteps.Count == 0)
                    return;

                var previousStep = presentedSteps.Pop();
                Array.Clear(selected, previousStep, selected.Length - previousStep);
                step = previousStep;
                await Task.Yield();
                continue;
            }

            var selectedCard = choice.FirstOrDefault;
            if (!choice.Confirmed || selectedCard is null)
                return;

            selected[step] = selectedCard;
            if (choice.WasPresented)
                presentedSteps.Push(step);

            step++;
        }

        for (var index = 0; index < selected.Length; index++)
        {
            if (!IsCurrentSelection(selected[index], index))
                return;
        }

        var rng = Owner.RunState?.Rng?.CombatCardSelection;
        if (rng is null)
            return;

        var chosen = rng.NextItem(selected);
        await CardPileCmd.Add(chosen, PileType.Hand, CardPilePosition.Top, this, false);
    }

    private Task<CardSelectionResult<CardModel>> ChooseFromDraw(
        PlayerChoiceContext ctx,
        int step,
        bool allowBack)
    {
        var candidates = CardSelectionHelper.DrawPileCards(Owner)
            .Where(card => IsSelectionForStep(card, step))
            .ToList();
        return CardSelectionHelper.ChooseOneWithBack(
            ctx,
            Owner,
            candidates,
            PromptForStep(step),
            allowBack,
            requireManualConfirmation: true);
    }

    private bool IsCurrentSelection(CardModel card, int step) =>
        CardSelectionHelper.IsCurrentPileCard(Owner, card, PileType.Draw)
        && IsSelectionForStep(card, step);

    private static bool IsSelectionForStep(CardModel card, int step) => step switch
    {
        0 => card.Type == CardType.Power,
        1 => card.Type == CardType.Skill,
        2 => card.Type == CardType.Attack,
        _ => false
    };

    private static string PromptForStep(int step) => step switch
    {
        0 => "THERMALVORTEX-MAGICAL_HATS.powerPrompt",
        1 => "THERMALVORTEX-MAGICAL_HATS.skillPrompt",
        2 => "THERMALVORTEX-MAGICAL_HATS.attackPrompt",
        _ => throw new ArgumentOutOfRangeException(nameof(step))
    };

    private bool HasAllCategories()
    {
        var draw = CardSelectionHelper.DrawPileCards(Owner);
        return Enumerable.Range(0, 3)
            .All(step => draw.Any(card => IsSelectionForStep(card, step)));
    }

    protected override void OnUpgrade() => ConstructedUpgrade();
}
