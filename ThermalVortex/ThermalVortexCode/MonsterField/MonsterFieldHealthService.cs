using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using System.Runtime.CompilerServices;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.Powers;

namespace ThermalVortex.ThermalVortexCode.MonsterField;

internal static class MonsterFieldHealthService
{
    private static readonly Dictionary<CardModel, MutableHealth> Health = new(new ReferenceComparer<CardModel>());
    // Copying a discarded monster can read its last field maximum without
    // changing the life with which the original monster normally returns.
    private static readonly Dictionary<CardModel, int> CopyMaxHpAdjustment = new(new ReferenceComparer<CardModel>());
    private static readonly Dictionary<CardModel, CopyMaxHpTransfer> PendingCopyMaxHpTransfers = new(new ReferenceComparer<CardModel>());
    private static readonly HashSet<CardModel> ZeroHealthProtected = new(new ReferenceComparer<CardModel>());
    private static readonly HashSet<CardPile> AttachedPiles = new(new ReferenceComparer<CardPile>());

    internal static event Action Changed;

    internal static void ResetAll()
    {
        PhoenixRevivalSnapshot.ResetAll();
        RelinquishedControlPower.ResetBindings();
        var changed = Health.Count > 0 || ZeroHealthProtected.Count > 0;
        foreach (var card in Health.Keys)
            TryGetOwner(card)?.Creature?.GetPower<DarkDuelPower>()?.ForgetMonster(card);
        Health.Clear();
        CopyMaxHpAdjustment.Clear();
        PendingCopyMaxHpTransfers.Clear();
        ZeroHealthProtected.Clear();
        AttachedPiles.Clear();
        if (changed)
            Changed?.Invoke();
    }

    internal static void Attach(Player player, CardPile pile)
    {
        if (player is null || pile is null || !AttachedPiles.Add(pile))
            return;

        pile.CardAdded += card =>
        {
            if (!MonsterFieldService.IsFieldMonster(card))
                return;

            ForgetCopyMaxHp(card);
            Initialize(card, true);
            MonsterFieldEventService.NotifyMonsterEnteredField(
                new MonsterFieldEnterEvent(card, PeekHealth(card)));
        };
        pile.CardRemoved += Clear;
        pile.ContentsChanged += () => Sync(player, pile.Cards.Where(MonsterFieldService.IsFieldMonster).ToList());

        Sync(player, pile.Cards.Where(MonsterFieldService.IsFieldMonster).ToList());
    }

    internal static void Sync(Player player, IReadOnlyList<CardModel> fieldCards)
    {
        if (player is null)
            return;

        var fieldSet = fieldCards.ToHashSet(new ReferenceComparer<CardModel>());
        foreach (var card in fieldCards)
            Initialize(card, false);

        foreach (var card in Health.Keys.ToList())
        {
            if (TryGetOwner(card) == player && (!fieldSet.Contains(card) || !MonsterFieldService.IsOnField(card)))
            {
                Health.Remove(card);
                ForgetZeroHealthProtection(card);
            }
        }
    }

    internal static MonsterFieldHealth GetHealth(CardModel card)
    {
        if (card is null)
            return default;

        Initialize(card, false);
        return Health.TryGetValue(card, out var health)
            ? new MonsterFieldHealth(health.CurrentHp, health.MaxHp)
            : default;
    }

    internal static MonsterFieldHealth PeekHealth(CardModel card)
    {
        if (card is null)
            return default;

        return Health.TryGetValue(card, out var health)
            ? new MonsterFieldHealth(health.CurrentHp, health.MaxHp)
            : GetHealth(card);
    }

    internal static bool HasHealth(CardModel card) => card is not null && Health.ContainsKey(card);

    internal static int GetEffectiveMaxHpForCopy(CardModel card)
    {
        if (card is null)
            return 0;
        if (MonsterFieldService.IsOnField(card) && Health.TryGetValue(card, out var health))
            return health.MaxHp;
        // Keep live card growth/upgrade values authoritative; only the field
        // portion (for example Sleeping Tablet health) needs a departure record.
        CopyMaxHpAdjustment.TryGetValue(card, out var adjustment);
        return Math.Max(1, GetInitialMaxHp(card) + adjustment);
    }

    internal static void RememberCopyMaxHp(CardModel card)
    {
        if (card is not null && MonsterFieldService.IsOnField(card))
        {
            var health = PeekHealth(card);
            var transferredGrowth = PendingCopyMaxHpTransfers.TryGetValue(card, out var transfer)
                && Health.TryGetValue(card, out var currentHealth)
                && ReferenceEquals(transfer.Health, currentHealth)
                    ? transfer.MaxHpBonus : 0;
            // The original entry still physically includes the growth while a
            // transformation has already moved its ledger to the successor.
            // Do not mistake that moved growth for an unrelated field bonus.
            CopyMaxHpAdjustment[card] = health.MaxHp - GetInitialMaxHp(card) - transferredGrowth;
        }
    }

    internal static IDisposable TrackTransferredGrowthForCopy(CardModel card, int maxHpBonus)
    {
        if (card is null || maxHpBonus <= 0 || RelinquishedControlPower.HasSharedHealth(card)
            || !Health.TryGetValue(card, out var health))
            return new CopyMaxHpTransfer(null, null, 0, null);

        PendingCopyMaxHpTransfers.TryGetValue(card, out var previous);
        var transfer = new CopyMaxHpTransfer(card, health, maxHpBonus, previous);
        PendingCopyMaxHpTransfers[card] = transfer;
        return transfer;
    }

    private sealed class CopyMaxHpTransfer(
        CardModel card, MutableHealth health, int maxHpBonus, CopyMaxHpTransfer previous) : IDisposable
    {
        internal MutableHealth Health { get; } = health;
        internal int MaxHpBonus { get; } = maxHpBonus;

        public void Dispose()
        {
            if (card is null || !PendingCopyMaxHpTransfers.TryGetValue(card, out var current)
                || !ReferenceEquals(current, this))
                return;
            if (previous is null)
                PendingCopyMaxHpTransfers.Remove(card);
            else
                PendingCopyMaxHpTransfers[card] = previous;
        }
    }

    internal static void ForgetCopyMaxHp(CardModel card)
    {
        if (card is not null)
            CopyMaxHpAdjustment.Remove(card);
    }

    internal static int GetInitialMaxHp(CardModel card)
    {
        var baseMaxHp = card switch
        {
            XyzMonsterCard xyzMonster => Math.Max(1, xyzMonster.MonsterMaxHp),
            MonsterCard monster => Math.Max(1, monster.MonsterMaxHp),
            _ => 0
        };

        return baseMaxHp <= 0 ? 0 : baseMaxHp + CyberDevourState.GetMaxHpBonus(card);
    }

    internal static void Clear(CardModel card)
    {
        if (card is null)
            return;

        RelinquishedControlPower.DetachForCopyReset(card);
        var changed = Health.Remove(card);
        changed |= ForgetZeroHealthProtection(card);
        if (changed)
            Changed?.Invoke();
    }

    internal static void Clear(IEnumerable<CardModel> cards)
    {
        var changed = false;
        foreach (var card in cards.ToList())
        {
            RelinquishedControlPower.DetachForCopyReset(card);
            changed |= Health.Remove(card);
            changed |= ForgetZeroHealthProtection(card);
        }

        if (changed)
            Changed?.Invoke();
    }

    private static bool ForgetZeroHealthProtection(CardModel card)
    {
        TryGetOwner(card)?.Creature?.GetPower<DarkDuelPower>()?.ForgetMonster(card);
        return ZeroHealthProtected.Remove(card);
    }

    private static void UpdateZeroHealthProtection(CardModel card, int previousHp, int currentHp)
    {
        var power = TryGetOwner(card)?.Creature?.GetPower<DarkDuelPower>();
        if (power?.UpdateZeroHealthProtection(card, previousHp, currentHp) == true)
            ZeroHealthProtected.Add(card);
        else
            ZeroHealthProtected.Remove(card);
    }

    internal static bool IsZeroHealthProtected(CardModel card) =>
        card is not null && ZeroHealthProtected.Contains(card);

    internal static async Task ExpireZeroHealthProtection(
        PlayerChoiceContext ctx,
        IEnumerable<CardModel> protectedCards,
        AbstractModel source)
    {
        var snapshot = protectedCards?
            .Where(card => card is not null)
            .Distinct(new ReferenceComparer<CardModel>())
            .ToList() ?? [];
        if (snapshot.Count == 0)
            return;

        var expired = CaptureFieldHealth(snapshot)
            .Where(target => ZeroHealthProtected.Contains(target.Card))
            .Where(target => target.Health.CurrentHp <= 0)
            .ToList();

        var changed = false;
        foreach (var card in snapshot)
            changed |= ZeroHealthProtected.Remove(card);

        foreach (var target in expired)
        {
            if (IsCurrentFieldHealth(target))
                await RelinquishedControlPower.ResolveSharedDeath(target.Card);
        }

        await SendDestroyedToDiscard(ctx, expired, source, MonsterFieldLeaveReason.BattleDestroyed);

        if (changed)
            Changed?.Invoke();
    }

    internal static void Heal(CardModel card, int amount)
    {
        if (card is null || amount <= 0 || !MonsterFieldService.IsOnField(card))
            return;

        Initialize(card, false);
        if (!Health.TryGetValue(card, out var health))
            return;

        SetHealth(card, Math.Clamp(health.CurrentHp + amount, 0, health.MaxHp), health.MaxHp);
    }

    internal static void IncreaseHealth(CardModel card, int amount)
    {
        if (card is null || amount <= 0 || !MonsterFieldService.IsOnField(card))
            return;

        Initialize(card, false);
        if (!Health.TryGetValue(card, out var health))
            return;

        var currentHp = health.CurrentHp + amount;
        var maxHp = health.MaxHp + amount;
        SetHealth(card, currentHp, maxHp);
    }

    internal static async Task SetCurrentHp(CardModel card, int currentHp)
    {
        if (card is null)
            return;

        Initialize(card, false);
        if (!Health.TryGetValue(card, out var health))
            return;

        SetHealth(card, Math.Clamp(currentHp, 0, health.MaxHp), health.MaxHp);
        await RelinquishedControlPower.ResolveSharedDeath(card);
    }

    internal static async Task DestroyRightmost(
        PlayerChoiceContext ctx,
        Player player,
        int count,
        AbstractModel source,
        MonsterFieldLeaveReason reason = MonsterFieldLeaveReason.BattleDestroyed)
    {
        if (player is null || count <= 0)
            return;

        var targets = MonsterFieldService.GetMonsters(player)
            .Reverse()
            .Take(count)
            .ToList();
        await DestroyMonsters(ctx, targets, source, reason);
    }

    internal static async Task DestroyMonsters(
        PlayerChoiceContext ctx,
        IEnumerable<CardModel> cards,
        AbstractModel source,
        MonsterFieldLeaveReason reason = MonsterFieldLeaveReason.BattleDestroyed)
    {
        var targets = CaptureFieldHealth(cards ?? []);
        if (targets.Count == 0)
            return;

        foreach (var target in targets)
            ForgetZeroHealthProtection(target.Card);

        // Forced destruction also belongs to a particular field incarnation.
        // A previous target's callbacks may recover and resummon a later card.
        var byCard = targets.ToDictionary(target => target.Card, new ReferenceComparer<CardModel>());
        await MonsterFieldService.SendToDiscardIf(ctx, byCard.Keys, source, reason,
            card => byCard.TryGetValue(card, out var target) && IsCurrentFieldHealth(target));
    }

    internal static async Task EnforceCapacity(
        PlayerChoiceContext ctx,
        Player player,
        AbstractModel source,
        MonsterFieldLeaveReason reason = MonsterFieldLeaveReason.BattleDestroyed)
    {
        if (player is null)
            return;

        var overflow = MonsterFieldService.Count(player) - MonsterFieldService.GetCapacity(player);
        if (overflow > 0)
            await DestroyRightmost(ctx, player, overflow, source, reason);
    }

    internal static async Task<int> AbsorbAttackDamage(
        PlayerChoiceContext ctx,
        Player player,
        decimal incomingDamage,
        AbstractModel source)
    {
        var result = await AbsorbAttackDamageDetailed(ctx, player, incomingDamage, source);
        result.DestroyedPhoenix?.Dispose();
        return result.RemainingDamage;
    }

    internal static async Task<MonsterFieldAttackDamageResult> AbsorbAttackDamageDetailed(
        PlayerChoiceContext ctx,
        Player player,
        decimal incomingDamage,
        AbstractModel source)
    {
        var remaining = Math.Max(0, (int)Math.Ceiling(incomingDamage));
        if (remaining <= 0 || player is null)
            return default;

        var originalDamage = remaining;
        var totalAbsorbed = 0;
        var destroyed = new List<FieldHealthTarget>();
        var monsters = CaptureFieldHealth(MonsterFieldService.GetMonsters(player)
            .Reverse()
            .Where(MonsterFieldService.CanBeAttackTarget));
        var preserveAtOne = player.Creature?.HasPower<SharedFatePower>() == true;
        for (var index = 0; index < monsters.Count; index++)
        {
            if (remaining <= 0)
                break;

            var target = monsters[index];
            var monster = target.Card;
            var health = target.Health;
            if (!IsCurrentFieldHealth(target) || health.CurrentHp <= 0)
                continue;

            var hasFallback = preserveAtOne && monsters
                .Skip(index + 1)
                .Any(candidate => IsCurrentFieldHealth(candidate) && candidate.Health.CurrentHp > 0);
            var availableHp = hasFallback ? Math.Max(0, health.CurrentHp - 1) : health.CurrentHp;
            if (availableHp <= 0)
                continue;

            var absorbed = Math.Min(remaining, availableHp);
            await ApplyAttackHealthLoss(monster, health, absorbed, source);
            remaining -= absorbed;
            totalAbsorbed += absorbed;

            if (IsDestroyedFieldHealth(target))
                destroyed.Add(target);
        }

        var revivalCandidates = new Dictionary<CardModel, PhoenixRevivalSnapshot>(new ReferenceComparer<CardModel>());
        try
        {
            // Leaving clears Chaos Phantom's copied form. Clone it before any
            // leave callback, but require the original pile's removal receipt.
            foreach (var target in destroyed.Where(IsDestroyedFieldHealth))
            {
                var snapshot = PhoenixRevivalSnapshot.Capture(target.Card);
                if (snapshot is not null)
                    revivalCandidates.Add(target.Card, snapshot);
            }
            await SendDestroyedToDiscard(ctx, destroyed, source, MonsterFieldLeaveReason.BattleDestroyed,
                card =>
                {
                    if (revivalCandidates.TryGetValue(card, out var snapshot))
                        snapshot.ConfirmDeparture();
                });

            if (totalAbsorbed > 0)
                MainFile.Logger.Info($"MonsterFieldGuard absorbed={totalAbsorbed} incoming={originalDamage} remaining={remaining} destroyed={destroyed.Count}");

            Changed?.Invoke();
            var phoenix = destroyed.Select(target => revivalCandidates.GetValueOrDefault(target.Card))
                .FirstOrDefault(snapshot => snapshot?.CanRevive == true);
            if (phoenix is not null)
                revivalCandidates.Remove(phoenix.Card); // Ownership passes to this damage segment.
            return new MonsterFieldAttackDamageResult(remaining, phoenix);
        }
        finally
        {
            foreach (var snapshot in revivalCandidates.Values)
                snapshot.Dispose();
        }
    }

    private static async Task ApplyAttackHealthLoss(
        CardModel card,
        MutableHealth health,
        int amount,
        AbstractModel source)
    {
        if (amount <= 0)
            return;

        var hpBefore = health.CurrentHp;
        health.CurrentHp = Math.Max(0, health.CurrentHp - amount);
        UpdateZeroHealthProtection(card, hpBefore, health.CurrentHp);
        var actualAmount = hpBefore - health.CurrentHp;
        if (actualAmount <= 0)
            return;

        MonsterFieldEventService.NotifyMonsterImpacted(
            new MonsterFieldImpactEvent(
                card,
                actualAmount,
                hpBefore,
                health.CurrentHp,
                health.CurrentHp <= 0 && !ZeroHealthProtected.Contains(card),
                source));

        RelinquishedControlPower.SynchronizeFromMonster(card, health.CurrentHp, health.MaxHp);
        await RelinquishedControlPower.ResolveSharedDeath(card);
    }

    internal static async Task DamageMonsters(
        PlayerChoiceContext ctx,
        IEnumerable<CardModel> cards,
        int damage,
        AbstractModel source,
        MonsterFieldLeaveReason reason)
    {
        if (damage <= 0 || cards is null)
            return;

        var destroyed = new List<FieldHealthTarget>();
        foreach (var target in CaptureFieldHealth(cards))
        {
            var card = target.Card;
            var health = target.Health;
            if (!IsCurrentFieldHealth(target) || health.CurrentHp <= 0)
                continue;

            var hpBefore = health.CurrentHp;
            health.CurrentHp = Math.Max(0, health.CurrentHp - damage);
            UpdateZeroHealthProtection(card, hpBefore, health.CurrentHp);
            var actualAmount = hpBefore - health.CurrentHp;
            var wasDestroyed = health.CurrentHp <= 0 && !ZeroHealthProtected.Contains(card);
            MonsterFieldEventService.NotifyMonsterImpacted(
                new MonsterFieldImpactEvent(
                    card,
                    actualAmount,
                    hpBefore,
                    health.CurrentHp,
                    wasDestroyed,
                    source));

            RelinquishedControlPower.SynchronizeFromMonster(card, health.CurrentHp, health.MaxHp);
            await RelinquishedControlPower.ResolveSharedDeath(card);

            if (IsDestroyedFieldHealth(target))
                destroyed.Add(target);
        }

        await SendDestroyedToDiscard(ctx, destroyed, source, reason);

        Changed?.Invoke();
    }

    private static List<FieldHealthTarget> CaptureFieldHealth(IEnumerable<CardModel> cards)
    {
        var result = new List<FieldHealthTarget>();
        foreach (var card in cards.Distinct(new ReferenceComparer<CardModel>()))
        {
            if (!MonsterFieldService.IsOnField(card))
                continue;
            Initialize(card, false);
            if (Health.TryGetValue(card, out var health))
                result.Add(new FieldHealthTarget(card, health));
        }
        return result;
    }

    private static bool IsCurrentFieldHealth(FieldHealthTarget target) =>
        MonsterFieldService.IsOnField(target.Card)
        && Health.TryGetValue(target.Card, out var current)
        && ReferenceEquals(current, target.Health);

    private static bool IsDestroyedFieldHealth(FieldHealthTarget target) =>
        IsCurrentFieldHealth(target)
        && target.Health.CurrentHp <= 0
        && !ZeroHealthProtected.Contains(target.Card);

    private static Task SendDestroyedToDiscard(
        PlayerChoiceContext ctx,
        IReadOnlyList<FieldHealthTarget> targets,
        AbstractModel source,
        MonsterFieldLeaveReason reason,
        Action<CardModel> onDeparted = null)
    {
        var byCard = targets.ToDictionary(target => target.Card, new ReferenceComparer<CardModel>());
        return MonsterFieldService.SendToDiscardIf(ctx, byCard.Keys, source, reason,
            card => byCard.TryGetValue(card, out var target) && IsDestroyedFieldHealth(target),
            onDeparted);
    }

    private sealed record FieldHealthTarget(CardModel Card, MutableHealth Health);

    internal static void SetHealth(CardModel card, int currentHp, int maxHp)
    {
        if (card is null)
            return;

        var maximum = Math.Max(RelinquishedControlPower.HasSharedHealth(card) ? 0 : 1, maxHp);
        CommitHealth(card, Math.Clamp(currentHp, 0, maximum), maximum);
        RelinquishedControlPower.SynchronizeFromMonster(card, Math.Clamp(currentHp, 0, maximum), maximum);
    }

    internal static async Task SetHealthAsync(CardModel card, int currentHp, int maxHp)
    {
        SetHealth(card, currentHp, maxHp);
        await RelinquishedControlPower.ResolveSharedDeath(card);
    }

    // Enemy HP events already represent the original change. Write only the
    // shared monster display here; never feed it back as another damage action.
    internal static void SetSharedHealth(CardModel card, int currentHp, int maxHp, AbstractModel source)
    {
        if (card is null)
            return;
        var maximum = Math.Max(0, maxHp);
        var current = Math.Clamp(currentHp, 0, maximum);
        var hpBefore = Health.TryGetValue(card, out var previous) ? previous.CurrentHp : current;
        CommitHealth(card, current, maximum);
        if (current < hpBefore)
            MonsterFieldEventService.NotifyMonsterImpacted(new MonsterFieldImpactEvent(
                card, hpBefore - current, hpBefore, current,
                current <= 0 && !ZeroHealthProtected.Contains(card), source));
    }

    private static void CommitHealth(CardModel card, int currentHp, int maxHp)
    {
        var previousHp = Health.TryGetValue(card, out var previous) ? previous.CurrentHp : currentHp;
        // Keep the instance stable across awaited death-prevention callbacks;
        // the damage loop must observe a native revival of the shared pool.
        if (Health.TryGetValue(card, out var health))
        {
            if (health.CurrentHp == currentHp && health.MaxHp == maxHp)
                return;
            health.CurrentHp = currentHp;
            health.MaxHp = maxHp;
        }
        else
            Health[card] = new MutableHealth(currentHp, maxHp);
        UpdateZeroHealthProtection(card, previousHp, currentHp);
        Changed?.Invoke();
    }

    private static void Initialize(CardModel card, bool reset)
    {
        if (!MonsterFieldService.IsFieldMonster(card))
            return;

        if (!reset && Health.ContainsKey(card))
            return;

        if (RelinquishedControlPower.SynchronizeBoundMonster(card))
            return;

        var maxHp = GetInitialMaxHp(card);
        if (maxHp <= 0)
            return;

        Health[card] = new MutableHealth(maxHp, maxHp);
        Changed?.Invoke();
    }

    private static Player TryGetOwner(CardModel card)
    {
        try
        {
            return card?.Owner;
        }
        catch
        {
            return null;
        }
    }

    private sealed class MutableHealth(int currentHp, int maxHp)
    {
        internal int CurrentHp { get; set; } = currentHp;
        internal int MaxHp { get; set; } = maxHp;
    }

}

internal readonly record struct MonsterFieldAttackDamageResult(
    int RemainingDamage,
    PhoenixRevivalSnapshot DestroyedPhoenix);

public readonly record struct MonsterFieldHealth(int CurrentHp, int MaxHp)
{
    public bool IsValid => MaxHp > 0;
}

internal sealed class ReferenceComparer<T> : IEqualityComparer<T>
    where T : class
{
    public bool Equals(T x, T y) => ReferenceEquals(x, y);

    public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
}
