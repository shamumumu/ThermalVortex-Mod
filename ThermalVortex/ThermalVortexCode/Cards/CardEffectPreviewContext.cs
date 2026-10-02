using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace ThermalVortex.ThermalVortexCode.Cards;

internal sealed record CardEffectPreviewState(
    CardModel SourceCard,
    Creature Target,
    IReadOnlyList<CardModel> SelectedCards,
    bool IsFusionSelection);

internal sealed record CardEffectSelectionPreview(
    IReadOnlyList<CardModel> FixedCards = null,
    bool IsFusionSelection = false);

/// <summary>Display-only state. Neither cards nor combat state receive projected values.</summary>
internal static class CardEffectPreviewContext
{
    private static readonly ConditionalWeakTable<CardModel, Binding> Bindings = new();
    private static readonly AsyncLocal<int> SuppressionDepth = new();
    private static Binding _handBinding;

    internal static bool TryGet(CardModel card, out CardEffectPreviewState state)
    {
        state = null;
        if (card is null || SuppressionDepth.Value > 0
            || !Bindings.TryGetValue(card, out var binding) || binding.IsDisposed)
            return false;

        state = binding.State;
        return true;
    }

    internal static void BeginHandPlay(CardModel card)
    {
        EndHandPlay();
        if (card is ThermalVortexCard)
            _handBinding = Bind(card, card, null, null, false);
    }

    internal static void EndHandPlay(CardModel card = null)
    {
        if (_handBinding is null
            || (card is not null && !ReferenceEquals(_handBinding.State.SourceCard, card)))
            return;
        _handBinding.Dispose();
        _handBinding = null;
    }

    internal static void SetTarget(CardModel card, Creature target)
    {
        if (card is not null && Bindings.TryGetValue(card, out var binding))
            binding.State = binding.State with { Target = target };
    }

    internal static Binding Bind(
        CardModel displayCard,
        CardModel sourceCard,
        Creature target,
        IReadOnlyList<CardModel> selectedCards,
        bool isFusionSelection)
    {
        var previous = Bindings.TryGetValue(displayCard, out var prior) ? prior : null;
        var binding = new Binding(displayCard,
            new CardEffectPreviewState(sourceCard, target, selectedCards, isFusionSelection), previous);
        Bindings.Remove(displayCard);
        Bindings.Add(displayCard, binding);
        return binding;
    }

    internal static IDisposable Suppress()
    {
        SuppressionDepth.Value++;
        return new Suppression();
    }

    internal sealed class Binding(
        CardModel displayCard,
        CardEffectPreviewState state,
        Binding previous) : IDisposable
    {
        internal CardEffectPreviewState State { get; set; } = state;
        internal bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            if (!Bindings.TryGetValue(displayCard, out var current) || !ReferenceEquals(current, this))
                return;
            Bindings.Remove(displayCard);
            if (previous is { IsDisposed: false })
                Bindings.Add(displayCard, previous);
        }
    }

    private sealed class Suppression : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            SuppressionDepth.Value = Math.Max(0, SuppressionDepth.Value - 1);
        }
    }
}
