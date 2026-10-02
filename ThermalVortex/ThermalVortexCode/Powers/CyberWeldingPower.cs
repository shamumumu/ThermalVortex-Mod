using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Powers;

public class CyberWeldingPower : ThermalVortexPower
{
    internal const decimal DamageMultiplier = 1.5m;
    internal const decimal FollowupDamageReduction = 0.5m;
    private static readonly AsyncLocal<MainAttackOutputScope> CurrentMainAttackOutput = new();
    private int _applicationsThisTurn;
    private int _resolvedApplications;
    private bool _pending;
    private bool _expired;
    private List<OutputResolution> _resolutions = [];

    internal bool HasPendingOutput => _pending && !_expired;
    internal decimal NextDamageMultiplier => BoostMultiplier(_applicationsThisTurn);
    internal decimal AfterOutputReductionPercent => Math.Min(100m, _applicationsThisTurn * 50m);
    internal decimal CurrentReductionPercent => (1m - FollowupMultiplier) * 100m;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _resolutions = [];
    }

    internal void ArmNextOutput()
    {
        _applicationsThisTurn++;
        _pending = true;
        _expired = false;
    }

    public override Task BeforeAttack(AttackCommand attack)
    {
        if (!_expired && OwnsDamage(attack.Attacker))
        {
            var card = attack.ModelSource as CardModel;
            var group = FindMainAttackOutput(card, attack);
            _resolutions.Add(new OutputResolution(this, card, attack,
                true, group?.GetState(this) ?? new OutputState(FollowupMultiplier), group is not null));
        }

        return Task.CompletedTask;
    }

    internal static IDisposable BeginMainAttackOutput(CardModel card, params AttackCommand[] attacks)
    {
        var scope = new MainAttackOutputScope(card, attacks, CurrentMainAttackOutput.Value);
        CurrentMainAttackOutput.Value = scope;
        return scope;
    }

    private static MainAttackOutputScope FindMainAttackOutput(CardModel card, AttackCommand attack)
    {
        for (var scope = CurrentMainAttackOutput.Value; scope is not null; scope = scope.Previous)
        {
            if (scope.Contains(card, attack))
                return scope;
        }

        return null;
    }

    public override Task AfterAttack(PlayerChoiceContext ctx, AttackCommand attack)
    {
        FinishAttack(attack);
        return Task.CompletedTask;
    }

    internal static void FinishAttack(AttackCommand attack)
    {
        var power = FindPower(attack.ModelSource as CardModel, attack.Attacker);
        power?._resolutions.LastOrDefault(resolution => ReferenceEquals(resolution.Token, attack))?.Dispose();
    }

    public override decimal ModifyDamageMultiplicative(
        Creature target, decimal amount, ValueProp props, Creature dealer, CardModel card)
    {
        if (_expired || !OwnsDamage(dealer) || (target is not null && !target.IsEnemy))
            return 1m;

        return GetMultiplier(card, props);
    }

    // Damage prefixes only identify real output. The native damage hook above
    // applies the multiplier once and remains safe to call for previews.
    internal static IDisposable BeginDamageOutput(
        CardModel card, Creature source, decimal amount, IEnumerable<Creature> targets, ValueProp props)
    {
        var power = FindPower(card, source);
        if (power is null || power._expired || !power.OwnsDamage(source)
            || amount <= 0 || !targets.Any(target => target?.IsEnemy == true && !target.IsDead))
            return null;

        // Only native attack damage belongs to an AttackCommand. An inherited
        // material effect from the same card is a separate output resolution.
        var current = props.IsCardOrMonsterMove() ? power.FindResolution(card, true) : null;
        if (current is not null)
        {
            power.StartOutput(current);
            return null;
        }

        if (!power.IsEligible(card))
            return null;

        var resolution = new OutputResolution(power, card, new object(),
            props.IsCardOrMonsterMove(), new OutputState(power.FollowupMultiplier));
        power._resolutions.Add(resolution);
        power.StartOutput(resolution);
        return resolution;
    }

    internal static IDisposable BeginLifeLossOutput(
        CardModel card, decimal amount, IEnumerable<Creature> targets) =>
        BeginDamageOutput(card, card.Owner.Creature, amount, targets, ValueProp.Unpowered);

    internal static int ScaleLifeLoss(CardModel card, decimal amount) =>
        (int)Math.Min(int.MaxValue, Math.Max(0m, Math.Floor(amount *
            (FindPower(card, card?.Owner?.Creature)?.GetMultiplier(card, ValueProp.Unpowered) ?? 1m))));

    internal async Task ExpireAfterTurnEnd(CombatSide side)
    {
        if (side != Owner.Side)
            return;

        _expired = true;
        _pending = false;
        _applicationsThisTurn = 0;
        _resolvedApplications = 0;
        _resolutions.Clear();
        await PowerCmd.Remove(this);
    }

    private static CyberWeldingPower FindPower(CardModel card, Creature source) =>
        source?.GetPower<CyberWeldingPower>()
        ?? source?.PetOwner?.Creature?.GetPower<CyberWeldingPower>()
        ?? card?.Owner?.Creature?.GetPower<CyberWeldingPower>();

    private bool OwnsDamage(Creature source) =>
        source == Owner || (source is not null && Owner.Pets.Contains(source));

    private bool IsEligible(CardModel card) =>
        card?.Owner?.Creature == Owner && MonsterFieldService.IsFieldMonster(card);

    private decimal FollowupMultiplier =>
        Math.Max(0m, 1m - _resolvedApplications * FollowupDamageReduction);

    private OutputResolution FindResolution(CardModel card, bool attack) =>
        _resolutions.LastOrDefault(resolution => ReferenceEquals(resolution.Card, card)
            && resolution.IsAttackDamage == attack);

    private decimal GetMultiplier(CardModel card, ValueProp props)
    {
        if (_expired)
            return 1m;

        var eligible = IsEligible(card);
        if (!eligible && !props.IsCardOrMonsterMove())
            return 1m;

        var resolution = FindResolution(card, props.IsCardOrMonsterMove());
        if (resolution is not null)
            return !resolution.State.Started && eligible && _pending
                ? BoostMultiplier(_applicationsThisTurn)
                : resolution.State.Multiplier;

        // Outside a real resolution this is a read-only preview of the next output.
        return eligible && _pending ? BoostMultiplier(_applicationsThisTurn) : FollowupMultiplier;
    }

    private void StartOutput(OutputResolution resolution)
    {
        var state = resolution.State;
        if (state.Started)
            return;

        state.Started = true;
        if (!IsEligible(resolution.Card) || !_pending)
            return;

        state.ActiveApplications = _applicationsThisTurn;
        state.Multiplier = BoostMultiplier(state.ActiveApplications);
        _pending = false;
        Flash();
    }

    private void CompleteOutput(OutputResolution resolution)
    {
        if (!_resolutions.Remove(resolution) || resolution.IsGrouped)
            return;

        CompleteOutputState(resolution.State);
    }

    private void CompleteOutputState(OutputState state)
    {
        if (_expired || !state.Started)
            return;

        // A newer output can complete inside an older output's callbacks.
        // Finishing the older output must not undo that newer penalty or re-arm.
        _resolvedApplications = Math.Max(_resolvedApplications, state.ActiveApplications);
    }

    private static decimal BoostMultiplier(int applications)
    {
        var multiplier = 1m;
        for (var i = 0; i < applications; i++)
            multiplier *= DamageMultiplier;
        return multiplier;
    }

    private sealed class OutputResolution(
        CyberWeldingPower power, CardModel card, object token, bool isAttackDamage,
        OutputState state, bool isGrouped = false) : IDisposable
    {
        internal CardModel Card { get; } = card;
        internal object Token { get; } = token;
        internal bool IsAttackDamage { get; } = isAttackDamage;
        internal OutputState State { get; } = state;
        internal bool IsGrouped { get; } = isGrouped;
        public void Dispose() => power.CompleteOutput(this);
    }

    private sealed class OutputState(decimal multiplier)
    {
        internal bool Started { get; set; }
        internal int ActiveApplications { get; set; }
        internal decimal Multiplier { get; set; } = multiplier;
    }

    private sealed class MainAttackOutputScope(
        CardModel card, IEnumerable<AttackCommand> attacks, MainAttackOutputScope previous) : IDisposable
    {
        private readonly HashSet<AttackCommand> _attacks = new(
            attacks.Where(attack => attack is not null), ReferenceEqualityComparer.Instance);
        private readonly Dictionary<CyberWeldingPower, OutputState> _states =
            new(ReferenceEqualityComparer.Instance);
        private bool _disposed;

        internal MainAttackOutputScope Previous { get; } = previous;

        internal bool Contains(CardModel source, AttackCommand attack) =>
            !_disposed && ReferenceEquals(source, card) && _attacks.Contains(attack);

        internal OutputState GetState(CyberWeldingPower power)
        {
            if (!_states.TryGetValue(power, out var state))
            {
                state = new OutputState(power.FollowupMultiplier);
                _states.Add(power, state);
            }

            return state;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (ReferenceEquals(CurrentMainAttackOutput.Value, this))
                CurrentMainAttackOutput.Value = Previous;

            // Match only the registered native commands. Nested inherited
            // outputs retain their own states and may settle a newer penalty.
            foreach (var (power, state) in _states)
            {
                power._resolutions.RemoveAll(resolution => ReferenceEquals(resolution.State, state));
                power.CompleteOutputState(state);
            }

            _states.Clear();
        }
    }
}
