using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

public enum MillenniumPieceSummonEffectKind
{
    Control,
    Draw,
    Energy,
    Fusion,
    Heal
}

/// <summary>
/// Native and inherited summon rewards share one executor. Only the committed
/// entry dispatcher calls native rewards; OnPlay merely requests a monster body.
/// </summary>
public static class MillenniumPieceEffects
{
    public const int SummonControlCount = 15;
    public const int SummonDrawCount = 6;
    public const int SummonEnergy = 3;
    public const int SummonHealing = 10;
    public const int HeldControlCount = 5;
    public const int HeldDrawCount = 2;
    public const int HeldEnergy = 1;

    public static Task ResolveNativeSummonReward(PlayerChoiceContext ctx, CardModel card) =>
        card is SealedMillenniumPieceCard piece
            ? ResolveSummonReward(ctx, card, piece.SummonEffectKind)
            : Task.CompletedTask;

    public static bool HasFusionSummonAbility(CardModel card) =>
        card is ExodiaRightLeg
        || card is ChaosPhantom phantom && phantom.HasCompleteCopyEffect<MillenniumPieceInheritedEffect>(
            effect => effect.Kind == MillenniumPieceSummonEffectKind.Fusion)
        || CyberDevourState.GetOwnInheritedEffects(card)
            .OfType<MillenniumPieceInheritedEffect>()
            .Any(effect => effect.Kind == MillenniumPieceSummonEffectKind.Fusion);

    internal static async Task ResolveSummonReward(
        PlayerChoiceContext ctx,
        CardModel host,
        MillenniumPieceSummonEffectKind kind)
    {
        var owner = host?.Owner;
        if (!MonsterFieldService.CaptureMaterialUseValidity(owner)()
            || ExodiaTorso.IsVictoryResolving(owner))
            return;

        switch (kind)
        {
            case MillenniumPieceSummonEffectKind.Control:
                await ControlTopCards(ctx, owner, SummonControlCount, host);
                break;
            case MillenniumPieceSummonEffectKind.Draw:
                await CardPileCmd.Draw(ctx, SummonDrawCount, owner, false);
                break;
            case MillenniumPieceSummonEffectKind.Energy:
                await PlayerCmd.GainEnergy(SummonEnergy, owner);
                break;
            case MillenniumPieceSummonEffectKind.Heal:
                await CreatureCmd.Heal(owner.Creature, SummonHealing, false);
                break;
            // Right-leg permissions are consumed by the shared summon-event
            // dispatcher, which merges hand and field sources for that event.
            case MillenniumPieceSummonEffectKind.Fusion:
                break;
        }
    }

    public static async Task ControlTopCards(
        PlayerChoiceContext ctx,
        Player owner,
        int count,
        AbstractModel source)
    {
        var combatIsValid = MonsterFieldService.CaptureMaterialUseValidity(owner);
        bool IsValid() => combatIsValid() && !ExodiaTorso.IsVictoryResolving(owner);
        Func<bool> isValid = IsValid;
        if (count <= 0 || !isValid())
            return;

        await CardSelectionHelper.RefillDrawPileFromDiscardIfNeeded(
            ctx,
            owner,
            count,
            source,
            isValid);

        if (!isValid())
            return;

        var choices = CardSelectionHelper.DrawPileCards(owner).Take(count).ToList();
        if (choices.Count == 0)
            return;

        var chosen = await CardSelectionHelper.ChooseMany(
            ctx, owner, choices,
            "THERMALVORTEX-MILLENNIUM_CONTROL.selectionPrompt",
            0, choices.Count,
            cancelable: true,
            sourceCard: source as CardModel);
        foreach (var card in chosen)
        {
            if (!isValid())
                return;
            if (!CardSelectionHelper.IsCurrentPileCard(owner, card, PileType.Draw))
                continue;

            await CardPileCmd.Add(card, PileType.Discard, CardPilePosition.Top, source, false);
        }
    }

}

internal sealed class MillenniumPieceInheritedEffect(MillenniumPieceSummonEffectKind kind) : ICyberCopyableEffect
{
    internal MillenniumPieceSummonEffectKind Kind { get; } = kind;

    public ICyberCopyableEffect Clone() => new MillenniumPieceInheritedEffect(Kind);

    public string GetDevourDisplayText()
    {
        var suffix = Kind switch
        {
            MillenniumPieceSummonEffectKind.Control => "pieceControl",
            MillenniumPieceSummonEffectKind.Draw => "pieceDraw",
            MillenniumPieceSummonEffectKind.Energy => "pieceEnergy",
            MillenniumPieceSummonEffectKind.Fusion => "pieceFusion",
            _ => "pieceHeal"
        };
        var amount = Kind switch
        {
            MillenniumPieceSummonEffectKind.Control => MillenniumPieceEffects.SummonControlCount,
            MillenniumPieceSummonEffectKind.Draw => MillenniumPieceEffects.SummonDrawCount,
            MillenniumPieceSummonEffectKind.Energy => MillenniumPieceEffects.SummonEnergy,
            MillenniumPieceSummonEffectKind.Heal => MillenniumPieceEffects.SummonHealing,
            _ => 1
        };
        var description = new LocString("static_hover_tips", $"THERMALVORTEX_DEVOUR_STATUS.{suffix}");
        description.Add("Amount", amount);
        return description.GetFormattedText();
    }

    public Task OnCyberSummoned(PlayerChoiceContext ctx, CardModel host, AbstractModel source) =>
        MillenniumPieceEffects.ResolveSummonReward(ctx, host, Kind);

    public Task OnCyberUpkeep(PlayerChoiceContext ctx, CardModel host, AbstractModel source) =>
        Task.CompletedTask;
}
