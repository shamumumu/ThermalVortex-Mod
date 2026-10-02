using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using System.Runtime.CompilerServices;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Cards;

// Combat-only value snapshot. Permanent deck entries and run saves must never
// retain it. Each restore owns independent effect instances and collections.
internal sealed class CyberDevourCombatSnapshot
{
    internal int MaxHpBonus { get; }
    internal int CurrentHpBonus { get; }
    internal int AttackContribution { get; }
    internal IReadOnlyList<ICyberCopyableEffect> Effects { get; }
    internal IReadOnlyList<CyberDevourRecord> Records { get; }

    internal CyberDevourCombatSnapshot(
        int maxHpBonus,
        int currentHpBonus,
        int attackContribution,
        IEnumerable<ICyberCopyableEffect> effects,
        IEnumerable<CyberDevourRecord> records)
    {
        MaxHpBonus = Math.Max(0, maxHpBonus);
        CurrentHpBonus = Math.Max(0, currentHpBonus);
        AttackContribution = Math.Max(0, attackContribution);
        Effects = Array.AsReadOnly((effects ?? [])
            .Select(effect => effect?.Clone()).Where(effect => effect is not null).ToArray());
        Records = Array.AsReadOnly((records ?? []).ToArray());
    }

    internal CyberDevourCombatSnapshot DeepClone() =>
        new(MaxHpBonus, CurrentHpBonus, AttackContribution, Effects, Records);
}

internal static partial class CyberDevourState
{
    // Playability checks create short-lived previews repeatedly. Their growth
    // must not keep the discarded card models alive for the entire combat.
    private static readonly ConditionalWeakTable<CardModel, CyberDevourCombatSnapshot> PreviewSnapshots = new();

    internal static CyberDevourCombatSnapshot CaptureCombatSnapshot(CardModel card) =>
        card is not null && States.TryGetValue(card, out var state)
            ? new(state.MaxHpBonus, state.CurrentHpBonus, state.AttackContribution, state.Effects, state.Records)
            : null;

    internal static void RestoreCombatSnapshot(CardModel card, CyberDevourCombatSnapshot snapshot)
    {
        if (card is null)
            return;
        States.Remove(card);
        PreviewSnapshots.Remove(card);
        if (snapshot is null)
            return;

        var state = new InheritedCyberState
        {
            MaxHpBonus = snapshot.MaxHpBonus,
            CurrentHpBonus = snapshot.CurrentHpBonus,
            AttackContribution = snapshot.AttackContribution
        };
        state.Effects.AddRange(snapshot.Effects.Select(effect => effect.Clone()));
        state.Records.AddRange(snapshot.Records);
        States[card] = state;
    }

    internal static void CopyForCombatClone(CardModel source, CardModel clone)
    {
        if (source is null || clone is null || ReferenceEquals(source, clone))
            return;
        RestoreCombatSnapshot(clone, CaptureCombatSnapshot(source));
    }

    internal static void ReleaseCombatState(CardModel card)
    {
        if (card is null)
            return;
        States.Remove(card);
        PreviewSnapshots.Remove(card);
    }

    internal static void AttachCombatPreviewSnapshot(CardModel card, CyberDevourCombatSnapshot snapshot)
    {
        if (card is null)
            return;
        PreviewSnapshots.Remove(card);
        if (snapshot is not null)
            PreviewSnapshots.Add(card, snapshot.DeepClone());
        if (card is ThermalVortexCard thermalCard)
            thermalCard.AttachCyberDevourDisplaySnapshot(snapshot is null ? null :
                new CyberDevourDisplaySnapshot(snapshot.MaxHpBonus, snapshot.AttackContribution, snapshot.Records));
    }

    private static CyberDevourCombatSnapshot GetPreviewSnapshot(CardModel card) =>
        card is not null && PreviewSnapshots.TryGetValue(card, out var snapshot) ? snapshot : null;

    internal static CyberDevourCombatSnapshot CaptureDisplaySnapshot(CardModel card) =>
        CaptureCombatSnapshot(card) ?? GetPreviewSnapshot(card)?.DeepClone();

    // A source Phantom contributes its own growth to somebody else's borrowed
    // form, while retaining that growth on its actual body. Ordinary cloning
    // uses CopyForCombatClone instead and never flattens the two layers.
    internal static void AddSnapshotGrowth(CardModel card, CyberDevourCombatSnapshot snapshot)
    {
        if (card is null || snapshot is null)
            return;
        var state = GetOrCreate(card);
        state.MaxHpBonus += snapshot.MaxHpBonus;
        state.CurrentHpBonus += snapshot.CurrentHpBonus;
        state.AttackContribution += snapshot.AttackContribution;
        state.Effects.AddRange(snapshot.Effects.Select(effect => effect.Clone()));
        state.Records.AddRange(snapshot.Records);
    }

    internal static CombatGrowthTransfer BeginCombatGrowthTransfer(CardModel from, CardModel to) =>
        new(from, to);

    internal sealed class CombatGrowthTransfer : IDisposable
    {
        private readonly CardModel _from;
        private readonly CardModel _to;
        private readonly InheritedCyberState _moved;
        private readonly int _maxHp;
        private readonly int _currentHp;
        private readonly int _attack;
        private readonly ICyberCopyableEffect[] _effects = [];
        private readonly CyberDevourRecord[] _records = [];
        private readonly CardPile _sourcePile;
        private readonly IDisposable _copyMaxHpTransfer;
        private bool _sourceReentered;
        private bool _disposed;

        internal bool Committed { get; private set; }
        internal bool SourceDeparted { get; private set; }

        internal CombatGrowthTransfer(CardModel from, CardModel to)
        {
            _from = from;
            _to = to;
            if (from is null || to is null || ReferenceEquals(from, to))
                throw new ArgumentException("A transformation requires two different monster entities.");
            if (States.ContainsKey(to))
                throw new InvalidOperationException("The replacement already owns combat growth.");

            _sourcePile = from.Pile;
            if (_sourcePile is not null)
                _sourcePile.CardRemoved += OnSourceRemoved;

            if (States.Remove(from, out var moved))
            {
                _moved = moved;
                _maxHp = moved.MaxHpBonus;
                _currentHp = moved.CurrentHpBonus;
                _attack = moved.AttackContribution;
                _effects = moved.Effects.ToArray();
                _records = moved.Records.ToArray();
                States[to] = moved;
                _copyMaxHpTransfer = MonsterFieldHealthService.TrackTransferredGrowthForCopy(from, _maxHp);
            }
            MonsterFieldEventService.MonsterEnteredVisual += OnMonsterEntered;
        }

        private void OnMonsterEntered(MonsterFieldEnterEvent entry)
        {
            if (ReferenceEquals(entry.Card, _to))
                Committed = true;
            if (ReferenceEquals(entry.Card, _from))
                _sourceReentered = true;
        }

        private void OnSourceRemoved(CardModel card)
        {
            if (ReferenceEquals(card, _from))
                SourceDeparted = true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            MonsterFieldEventService.MonsterEnteredVisual -= OnMonsterEntered;
            if (_sourcePile is not null)
                _sourcePile.CardRemoved -= OnSourceRemoved;
            _copyMaxHpTransfer?.Dispose();
            if (Committed || _moved is null
                || !States.TryGetValue(_to, out var current) || !ReferenceEquals(current, _moved))
                return;

            // The successor can gain unrelated growth while its play awaits.
            // Return exactly the original transfer, keeping both entities'
            // later contributions in their respective ledgers.
            current.MaxHpBonus = Math.Max(0, current.MaxHpBonus - _maxHp);
            current.CurrentHpBonus = Math.Max(0, current.CurrentHpBonus - _currentHp);
            current.AttackContribution = Math.Max(0, current.AttackContribution - _attack);
            foreach (var effect in _effects)
            {
                var index = current.Effects.FindIndex(candidate => ReferenceEquals(candidate, effect));
                if (index >= 0)
                    current.Effects.RemoveAt(index);
            }
            foreach (var record in _records)
            {
                var index = current.Records.FindIndex(candidate => ReferenceEquals(candidate, record));
                if (index >= 0)
                    current.Records.RemoveAt(index);
            }
            if (current.MaxHpBonus == 0 && current.CurrentHpBonus == 0 && current.AttackContribution == 0
                && current.Effects.Count == 0 && current.Records.Count == 0)
                States.Remove(_to);

            var restored = GetOrCreate(_from);
            restored.MaxHpBonus += _maxHp;
            restored.CurrentHpBonus += _currentHp;
            restored.AttackContribution += _attack;
            restored.Effects.AddRange(_effects);
            restored.Records.AddRange(_records);
            if (_sourceReentered)
                RefreshFieldHealthAfterAbsorb(_from, _maxHp, _currentHp);
            NotifyChanged(_from);
            NotifyChanged(_to);
        }
    }
}
