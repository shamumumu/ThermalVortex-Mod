using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.Commands;
using ThermalVortex.ThermalVortexCode.Powers;
using ThermalVortex.ThermalVortexCode.Vfx;
using ThermalVortex.ThermalVortexCode.Patches;

namespace ThermalVortex.ThermalVortexCode.Cards;

public interface ICyberMonster;

public interface ICyberDevourStats
{
    int CyberAttackContribution { get; }
}

public interface ICyberCopyableEffectProvider
{
    IEnumerable<ICyberCopyableEffect> CreateCyberCopyableEffects();
}

public interface ICyberCopyableEffect
{
    ICyberCopyableEffect Clone();
    string GetDevourDisplayText();
    Task OnCyberSummoned(PlayerChoiceContext ctx, CardModel host, AbstractModel source);
    Task OnCyberUpkeep(PlayerChoiceContext ctx, CardModel host, AbstractModel source);
    Task OnCyberLeavingField(PlayerChoiceContext ctx, CardModel host, MonsterFieldLeaveEvent leaveEvent) =>
        Task.CompletedTask;
    Task OnCyberLeftFieldResolved(PlayerChoiceContext ctx, CardModel host, MonsterFieldLeaveEvent leaveEvent) =>
        Task.CompletedTask;
    bool PreventsEnemyAttackTarget(CardModel host) => false;
}

internal static class CyberSeries
{
    private static readonly Dictionary<Player, int> CyberLarvaeGeneratedThisCombat = new(new ReferenceComparer<Player>());

    internal static bool IsCyberMonster(CardModel card) =>
        MonsterIdentity.Matches<ICyberMonster>(card);

    internal static bool IsCyberExtraMonster(CardModel card) =>
        MonsterIdentity.Matches<XyzMonsterCard>(card) && IsCyberMonster(card);

    internal static bool IsGeneratedOnly(CardModel card) =>
        card is CyberDragon or CyberLarva;

    internal static int GetCyberLarvaeGeneratedThisCombat(Player player) =>
        player is not null && CyberLarvaeGeneratedThisCombat.TryGetValue(player, out var count)
            ? count
            : 0;

    internal static void ResetCombat(Player player)
    {
        if (player is null)
            return;

        CyberLarvaeGeneratedThisCombat.Remove(player);
        CyberDevourState.Reset(player);
    }

    internal static void ResetAll()
    {
        CyberLarvaeGeneratedThisCombat.Clear();
        CyberDevourState.ResetAll();
    }

    internal static async Task<CyberLarva> GenerateCyberLarvaToDiscard(
        Player owner,
        AbstractModel source,
        bool? upgraded = null)
    {
        // Copied effects keep the original effect's upgrade level, independently of the host.
        var larva = CreateGeneratedCard<CyberLarva>(owner, upgraded.HasValue ? null : source);
        if (larva is null)
            return null;

        if (upgraded == true)
            ThermalVortexGeneratedCards.ApplyUpgradeLevel(larva, 1);

        await ThermalVortexCommandCompat.AddGeneratedCardToCombat(larva, PileType.Discard, owner, CardPilePosition.Top);
        CountCyberLarva(owner);
        return larva;
    }

    internal static async Task<CyberDragon> GenerateCyberDragon(
        Player owner,
        PileType pileType,
        AbstractModel source,
        CardPilePosition position = CardPilePosition.Top,
        bool freeThisCombat = false,
        bool forceUpgraded = false)
    {
        var dragon = CreateGeneratedCard<CyberDragon>(owner, source);
        if (dragon is null)
            return null;

        if (forceUpgraded)
            ThermalVortexGeneratedCards.ApplyUpgradeLevel(dragon, 1);

        if (freeThisCombat)
            dragon.SetFreeForCombat();

        await ThermalVortexCommandCompat.AddGeneratedCardToCombat(dragon, pileType, owner, position);
        return dragon;
    }

    internal static T CreateGeneratedCard<T>(Player owner, AbstractModel source = null) where T : CardModel
    {
        try
        {
            var generated = owner?.Creature?.CombatState?.CreateCard<T>(owner);
            return ThermalVortexGeneratedCards.MatchUpgrade(generated, source);
        }
        catch
        {
            return null;
        }
    }

    private static void CountCyberLarva(Player owner)
    {
        if (owner is null)
            return;

        CyberLarvaeGeneratedThisCombat.TryGetValue(owner, out var count);
        CyberLarvaeGeneratedThisCombat[owner] = count + 1;
    }

}

internal static partial class CyberDevourState
{
    private static readonly Dictionary<CardModel, InheritedCyberState> States = new(new ReferenceComparer<CardModel>());

    internal static event Action<CardModel> Changed;

    internal static int GetMaxHpBonus(CardModel card) =>
        card is not null && States.TryGetValue(card, out var state)
            ? state.MaxHpBonus : GetPreviewSnapshot(card)?.MaxHpBonus ?? 0;

    internal static int GetStoredInheritedAttackContribution(CardModel card) =>
        card is not null && States.TryGetValue(card, out var state)
            ? state.AttackContribution : GetPreviewSnapshot(card)?.AttackContribution ?? 0;

    internal static int GetInheritedAttackContribution(CardModel card)
    {
        var host = ChaosPhantomCopyService.ResolveEffectHost(card);
        if (!ReferenceEquals(host, card))
            return GetStoredInheritedAttackContribution(card) + GetStoredInheritedAttackContribution(host);
        return GetStoredInheritedAttackContribution(card)
            + (card is ChaosPhantom phantom ? phantom.CompleteCopyInheritedAttack : 0);
    }

    internal static int GetDevouredMonsterCount(CardModel card) =>
        card is not null && States.TryGetValue(card, out var state)
            ? state.Records.Count : GetPreviewSnapshot(card)?.Records.Count ?? 0;

    internal static bool HasInheritedInfinityAbility(CardModel card) =>
        card is not null
        && States.TryGetValue(card, out var state)
        && state.Effects.Any(effect => effect is CyberInfinityDevourEffect);

    internal static bool PreventsEnemyAttackTarget(CardModel card) =>
        card is not null
        && States.TryGetValue(card, out var state)
        && state.Effects.Any(effect => effect.PreventsEnemyAttackTarget(card));

    internal static IReadOnlyList<CyberDevourRecord> GetRecords(CardModel card) =>
        card is not null && States.TryGetValue(card, out var state)
            ? state.Records.ToArray()
            : GetPreviewSnapshot(card)?.Records ?? Array.Empty<CyberDevourRecord>();

    internal static CardModel CreateInspectionSnapshot(CardModel source)
    {
        if (source is null)
            return null;

        var inspection = source.MutableClone() as CardModel;
        AttachInspectionDisplayState(source, inspection);
        return inspection;
    }

    internal static void AttachInspectionDisplayState(CardModel source, CardModel inspection)
    {
        if (source is null || inspection is not ThermalVortexCard inspectionCard
            || ReferenceEquals(source, inspection))
            return;

        if (source is ChaosPhantom sourcePhantom && inspection is ChaosPhantom inspectionPhantom)
            inspectionPhantom.AttachInspectionSource(sourcePhantom);

        if (States.TryGetValue(source, out var state))
        {
            AttachCombatPreviewSnapshot(inspection, CaptureCombatSnapshot(source));
        }
        else if (GetPreviewSnapshot(source) is { } combatPreview)
        {
            AttachCombatPreviewSnapshot(inspection, combatPreview);
        }
        else if (source is ThermalVortexCard sourceCard
                 && sourceCard.CyberDevourDisplaySnapshot is { } snapshot)
        {
            AttachCombatPreviewSnapshot(inspection, new CyberDevourCombatSnapshot(
                snapshot.MaxHpBonus, 0, snapshot.AttackContribution, [], snapshot.Records));
        }
    }

    // Borrowed growth belongs to the private form snapshot. Releasing a form
    // must never clear growth the physical Phantom acquired for itself.
    internal static void ReleaseCompleteCopySnapshot(CardModel snapshot)
    {
        ReleaseCombatState(snapshot);
    }

    internal static void SetCompleteCopyInheritedAttack(CardModel snapshot, int attack)
    {
        if (snapshot is not null)
            GetOrCreate(snapshot).AttackContribution = Math.Max(0, attack);
    }

    internal static void CopyDynamicStateForCompleteCopy(CardModel source, CardModel snapshot)
    {
        CopyForCombatClone(source, snapshot);
    }

    internal static IEnumerable<ICyberCopyableEffect> CreateEffectsForCompleteCopy(CardModel source) =>
        source is null
            ? Array.Empty<ICyberCopyableEffect>()
            : GetCopyableEffects(source).ToArray();

    internal static int GetDisplayInheritedAttackContribution(CardModel card)
    {
        if (card is not null && States.TryGetValue(card, out var state))
            return state.AttackContribution;

        return (card as ThermalVortexCard)?.CyberDevourDisplaySnapshot?.AttackContribution ?? 0;
    }

    internal static IEnumerable<IHoverTip> CreateHoverTips(CardModel card)
    {
        var hasDisplayState = TryGetDisplayState(card, out var maxHpBonus, out var attackContribution, out var records);
        var hasMaterialEffect = records.Any(record => record.HasMaterialEffect)
            || card is ChaosPhantom phantom
                && (phantom.HasCompleteCopyEffect<CyberMaterialDamageEffect>()
                    || phantom.HasCompleteCopyEffect<CyberFaraPersistenceEffect>());
        const string materialKey = "THERMALVORTEX-MATERIAL";
        if (hasMaterialEffect
            && (card is not ThermalVortexCard thermalCard
                || !thermalCard.ExplanationBindings.Any(binding =>
                    binding.Id == materialKey && binding.AppliesTo(card))))
        {
            yield return ThermalVortexCard.KeywordTip(materialKey);
        }

        if (!hasDisplayState || records.Count == 0)
            yield break;

        var grouped = records
            .GroupBy(record => record.Fingerprint)
            .Select(group => (Record: group.First(), Count: group.Count()))
            .ToList();
        var description = new LocString("static_hover_tips", "THERMALVORTEX_DEVOUR_STATUS.description");
        description.Add("Count", records.Count);
        description.Add("MaxHp", maxHpBonus);
        description.Add("Attack", attackContribution);
        description.Add("Records", string.Join("\n", grouped.Select(group => FormatRecord(group.Record, group.Count))));
        yield return new HoverTip(
            new LocString("static_hover_tips", "THERMALVORTEX_DEVOUR_STATUS.title"),
            description);
    }

    internal static int GetTotalAttackContribution(CardModel card)
    {
        if (card is null)
            return 0;

        var baseAttack = GetBaseAttackContribution(card);
        return baseAttack + GetStoredInheritedAttackContribution(card);
    }

    internal static void Absorb(CardModel devourer, CardModel victim)
    {
        if (devourer is null || victim is null || ReferenceEquals(devourer, victim))
            return;

        var contribution = CaptureDevourContribution(victim);
        var state = GetOrCreate(devourer);
        state.MaxHpBonus += contribution.MaxHp;
        state.CurrentHpBonus += contribution.CurrentHp;
        state.AttackContribution += contribution.Attack;
        state.Effects.AddRange(contribution.Effects);
        state.Records.AddRange(contribution.Records);

        RefreshFieldHealthAfterAbsorb(devourer, contribution.MaxHp, contribution.CurrentHp);

        NotifyChanged(devourer);
    }

    private static CyberDevourContribution CaptureDevourContribution(CardModel victim)
    {
        var maxHpContribution = GetMaxHpContribution(victim);
        var currentHpContribution = GetCurrentHpContribution(victim, maxHpContribution);
        var copiedEffects = GetCopyableEffects(victim).ToList();
        var victimState = States.TryGetValue(victim, out var existingVictimState)
            ? existingVictimState
            : null;
        var inheritedRecords = victimState?.Records.ToList() ?? [];
        var ownMaxHp = Math.Max(0, maxHpContribution - (victimState?.MaxHpBonus ?? 0));
        var ownAttack = GetBaseAttackContribution(victim);
        var attackContribution = ownAttack + (victimState?.AttackContribution ?? 0);
        var ownEffects = GetOwnCopyableEffects(victim).ToList();

        var records = new List<CyberDevourRecord> { CreateRecord(victim, ownMaxHp, ownAttack, ownEffects) };
        records.AddRange(inheritedRecords);
        // Devouring copies the contribution. The victim keeps its own growth
        // and inherited effects when recovered, including complete-copy state.
        return new CyberDevourContribution(
            Math.Max(0, maxHpContribution),
            Math.Max(0, currentHpContribution),
            Math.Max(0, attackContribution),
            copiedEffects,
            records);
    }

    internal static async Task TriggerSummonEffects(PlayerChoiceContext ctx, CardModel host, AbstractModel source)
    {
        if (host is null || !States.TryGetValue(host, out var state))
            return;

        using var effectSource = AshBlossomActionNegation.EnterSource(host);
        await SynchronizePersistentEffects(ctx, host);
        foreach (var effect in state.Effects.ToList())
            await effect.OnCyberSummoned(ctx, host, source);
    }

    internal static async Task TriggerUpkeepEffects(PlayerChoiceContext ctx, CardModel host, AbstractModel source)
    {
        if (host is null || !States.TryGetValue(host, out var state))
            return;

        using var effectSource = AshBlossomActionNegation.EnterSource(host);
        await SynchronizePersistentEffects(ctx, host);
        foreach (var effect in state.Effects.ToList())
            await effect.OnCyberUpkeep(ctx, host, source);
    }

    internal static Task SynchronizePersistentEffects(PlayerChoiceContext ctx, CardModel host)
    {
        if (host is null)
            return Task.CompletedTask;
        if (host is ChaosPhantom { HasActiveCompleteCopy: true } phantom)
            return phantom.SynchronizeCompleteCopyPersistentEffects(ctx);
        // A native persistent effect and acquired copies share one host key in
        // their power, so reconcile their combined count without replaying the
        // native summon/upkeep action. A departing Phantom lends no form here.
        var effects = host is ChaosPhantom
            ? GetOwnInheritedEffects(host)
            : GetOwnCopyableEffects(host).Concat(GetOwnInheritedEffects(host));
        return SynchronizePersistentEffects(ctx, host, effects);
    }

    internal static IEnumerable<ICyberCopyableEffect> GetOwnInheritedEffects(CardModel host) =>
        host is not null && States.TryGetValue(host, out var state)
            ? state.Effects.ToArray()
            : Array.Empty<ICyberCopyableEffect>();

    internal static async Task SynchronizePersistentEffects(
        PlayerChoiceContext ctx,
        CardModel host,
        IEnumerable<ICyberCopyableEffect> effects,
        Func<bool> isCurrent = null)
    {
        if (host?.Owner?.Creature is not { } owner || !MonsterFieldService.IsOnField(host)
            || isCurrent?.Invoke() == false)
            return;

        using var effectSource = AshBlossomActionNegation.EnterSource(host);

        // Reconcile the complete contribution of this host. Repeated upkeep is
        // idempotent, while several devoured copies still contribute separately.
        var snapshot = effects.ToList();
        var drawEffects = snapshot.OfType<EnemyActionDrawInheritedEffect>().ToList();
        await SynchronizeDrawSource<MaxxCPower>(ctx, host, drawEffects.Count(effect => effect.Kind == EnemyActionDrawEffectKind.Any), isCurrent);
        await SynchronizeDrawSource<MulcharmyFuwalosPower>(ctx, host, drawEffects.Count(effect => effect.Kind == EnemyActionDrawEffectKind.Attack), isCurrent);
        await SynchronizeDrawSource<MulcharmyPuruliaPower>(ctx, host, drawEffects.Count(effect => effect.Kind == EnemyActionDrawEffectKind.PowerChange), isCurrent);
        await SynchronizeDrawSource<MulcharmyMeowlsPower>(ctx, host, drawEffects.Count(effect => effect.Kind == EnemyActionDrawEffectKind.Summon), isCurrent);
        if (isCurrent?.Invoke() == false)
            return;

        var contracts = snapshot.OfType<CyberContractBookEffect>().ToList();
        var contractPower = owner.GetPower<MillenniumContractBookPower>();
        if (contractPower is null && contracts.Count > 0)
        {
            var applied = await ThermalVortexCommandCompat.ApplyPower<MillenniumContractBookPower>(ctx, owner, contracts.Count, owner, host, false);
            if (isCurrent?.Invoke() == false)
            {
                if (applied is not null && ReferenceEquals(owner.GetPower<MillenniumContractBookPower>(), applied))
                    await SynchronizePowerAmount(ctx, applied, -contracts.Count, host);
                return;
            }
            owner.GetPower<MillenniumContractBookPower>()?.SetSourceContribution(
                host, contracts.Count, contracts.Sum(effect => effect.DrawReduction));
        }
        else if (contractPower is not null)
        {
            var change = contractPower.SetSourceContribution(
                host, contracts.Count, contracts.Sum(effect => effect.DrawReduction));
            await SynchronizePowerAmount(ctx, contractPower, change, host);
        }

        if (isCurrent?.Invoke() == false)
            return;
        if (snapshot.Any(effect => effect is CyberInfinityDevourEffect))
            await CyberInfinityDevourEffect.EnsurePower(ctx, host);
    }

    private static async Task SynchronizeDrawSource<TPower>(
        PlayerChoiceContext ctx,
        CardModel host,
        int count,
        Func<bool> isCurrent) where TPower : EnemyActionDrawPower
    {
        if (isCurrent?.Invoke() == false)
            return;
        var owner = host.Owner.Creature;
        var power = owner.GetPower<TPower>();
        if (power is null)
        {
            if (count <= 0)
                return;

            var applied = await ThermalVortexCommandCompat.ApplyPower<TPower>(ctx, owner, count, owner, host, false);
            if (isCurrent?.Invoke() == false)
            {
                if (applied is not null && ReferenceEquals(owner.GetPower<TPower>(), applied))
                    await SynchronizePowerAmount(ctx, applied, -count, host);
                return;
            }
            owner.GetPower<TPower>()?.SetSourceCount(host, count);
            return;
        }

        var change = power.SetSourceCount(host, count);
        await SynchronizePowerAmount(ctx, power, change, host);
    }

    private static async Task SynchronizePowerAmount(
        PlayerChoiceContext ctx,
        PowerModel power,
        int change,
        CardModel host)
    {
        if (change == 0)
            return;

        if (power.Amount + change <= 0)
            await PowerCmd.Remove(power);
        else
            await ThermalVortexCommandCompat.ModifyPowerAmount(ctx, power, change, host.Owner.Creature, host, false);
    }

    internal static async Task TriggerLeavingEffects(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        if (leaveEvent.Card is null || !States.TryGetValue(leaveEvent.Card, out var state))
            return;

        using var effectSource = AshBlossomActionNegation.EnterSource(leaveEvent.Card);

        var isMaterialUseValid = leaveEvent.WasUsedAsMaterial
            ? MonsterFieldService.CaptureMaterialUseValidity(TryGetOwner(leaveEvent.Card))
            : null;
        foreach (var effect in state.Effects.ToList())
        {
            if (isMaterialUseValid?.Invoke() == false)
                return;
            await effect.OnCyberLeavingField(ctx, leaveEvent.Card, leaveEvent);
            if (isMaterialUseValid?.Invoke() == false)
                return;
        }
    }

    internal static async Task TriggerLeftResolvedEffects(PlayerChoiceContext ctx, MonsterFieldLeaveEvent leaveEvent)
    {
        if (leaveEvent.Card is null || !States.TryGetValue(leaveEvent.Card, out var state))
            return;

        using var effectSource = AshBlossomActionNegation.EnterSource(leaveEvent.Card);

        var isMaterialUseValid = leaveEvent.WasUsedAsMaterial
            ? MonsterFieldService.CaptureMaterialUseValidity(TryGetOwner(leaveEvent.Card))
            : null;
        foreach (var effect in state.Effects.ToList())
        {
            if (isMaterialUseValid?.Invoke() == false)
                return;
            await effect.OnCyberLeftFieldResolved(ctx, leaveEvent.Card, leaveEvent);
            if (isMaterialUseValid?.Invoke() == false)
                return;
        }
    }

    internal static void Transfer(CardModel from, CardModel to)
    {
        if (from is null || to is null || ReferenceEquals(from, to) || !States.Remove(from, out var state))
            return;

        States[to] = state;
    }

    internal static void Reset(Player player)
    {
        foreach (var card in States.Keys.ToList())
        {
            if (TryGetOwner(card) == player)
                States.Remove(card);
        }
        foreach (var card in PreviewSnapshots.Select(entry => entry.Key).ToList())
            if (TryGetOwner(card) == player)
                PreviewSnapshots.Remove(card);
    }

    internal static void ResetAll()
    {
        States.Clear();
        PreviewSnapshots.Clear();
    }

    private static void NotifyChanged(CardModel card)
    {
        var handlers = Changed;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().OfType<Action<CardModel>>())
        {
            try
            {
                handler(card);
            }
            catch (Exception exception)
            {
                // Presentation listeners must never interrupt a committed
                // devour between state transfer and the victim's pile move.
                MainFile.Logger.Info(
                    $"Cyber devour display listener failed card={card?.GetType().Name ?? "null"}: {exception}");
            }
        }
    }

    private static InheritedCyberState GetOrCreate(CardModel card)
    {
        if (!States.TryGetValue(card, out var state))
        {
            state = new InheritedCyberState();
            States[card] = state;
        }

        return state;
    }

    private static int GetMaxHpContribution(CardModel card)
    {
        var health = MonsterFieldHealthService.PeekHealth(card);
        if (health.IsValid)
            return health.MaxHp;

        return MonsterFieldHealthService.GetInitialMaxHp(card);
    }

    private static int GetCurrentHpContribution(CardModel card, int maxHpContribution)
    {
        var health = MonsterFieldHealthService.PeekHealth(card);
        return health.IsValid ? health.CurrentHp : maxHpContribution;
    }

    private static IEnumerable<ICyberCopyableEffect> GetCopyableEffects(CardModel card)
    {
        foreach (var effect in GetOwnCopyableEffects(card))
            yield return effect;

        if (!States.TryGetValue(card, out var state))
            yield break;

        foreach (var effect in state.Effects)
        {
            var clone = effect?.Clone();
            if (clone is not null)
                yield return clone;
        }
    }

    private static IEnumerable<ICyberCopyableEffect> GetOwnCopyableEffects(CardModel card)
    {
        if (card is ChaosPhantom phantom)
        {
            foreach (var effect in phantom.CreateCompleteCopyDevourEffects())
                yield return effect;
            yield break;
        }

        if (card is not ICyberCopyableEffectProvider provider)
            yield break;

        foreach (var effect in provider.CreateCyberCopyableEffects())
        {
            var clone = effect?.Clone();
            if (clone is not null)
                yield return clone;
        }
    }

    private static int GetBaseAttackContribution(CardModel card)
    {
        if (card is ChaosPhantom phantom)
            return phantom.CompleteCopyAttackContribution;

        if (card is ICyberDevourStats cyberStats)
            return Math.Max(0, cyberStats.CyberAttackContribution);

        try
        {
            if (card?.Type != CardType.Attack)
                return 0;

            if (card.DynamicVars.ContainsKey("CalculatedDamage"))
            {
                var target = card.Owner?.Creature?.CombatState?.HittableEnemies.FirstOrDefault();
                if (target is not null)
                    return Math.Max(0, (int)card.DynamicVars.CalculatedDamage.Calculate(target));
            }

            foreach (var key in new[] { "Damage", "FixedDamage", "ExtraDamage" })
            {
                if (card.DynamicVars.ContainsKey(key))
                    return Math.Max(0, card.DynamicVars[key].IntValue);
            }
        }
        catch
        {
            // A missing preview target must not prevent the actual devour.
        }

        return 0;
    }

    private static bool TryGetDisplayState(
        CardModel card,
        out int maxHpBonus,
        out int attackContribution,
        out IReadOnlyList<CyberDevourRecord> records)
    {
        if (card is not null && States.TryGetValue(card, out var state))
        {
            maxHpBonus = state.MaxHpBonus;
            attackContribution = state.AttackContribution;
            records = state.Records;
            return true;
        }

        var snapshot = (card as ThermalVortexCard)?.CyberDevourDisplaySnapshot;
        if (snapshot is not null)
        {
            maxHpBonus = snapshot.MaxHpBonus;
            attackContribution = snapshot.AttackContribution;
            records = snapshot.Records;
            return true;
        }

        maxHpBonus = 0;
        attackContribution = 0;
        records = Array.Empty<CyberDevourRecord>();
        return false;
    }

    private static CyberDevourRecord CreateRecord(
        CardModel victim,
        int maxHp,
        int attack,
        IReadOnlyList<ICyberCopyableEffect> effects)
    {
        var title = victim?.Title ?? victim?.GetType().Name ?? "?";
        var upgraded = victim?.CurrentUpgradeLevel > 0;
        var effectText = effects.Count == 0
            ? new LocString("static_hover_tips", "THERMALVORTEX_DEVOUR_STATUS.noEffect").GetFormattedText()
            : string.Join(" / ", effects.Select(effect => effect.GetDevourDisplayText()).Distinct());
        return new CyberDevourRecord(title, upgraded, maxHp, attack, effectText)
        {
            HasMaterialEffect = effects.Any(effect =>
                effect is CyberMaterialDamageEffect or CyberFaraPersistenceEffect)
        };
    }

    private static string FormatRecord(CyberDevourRecord record, int count)
    {
        var loc = new LocString("static_hover_tips", "THERMALVORTEX_DEVOUR_STATUS.record");
        loc.Add("Monster", record.Title + (record.Upgraded ? "+" : string.Empty));
        loc.Add("Copies", count > 1 ? $" ×{count}" : string.Empty);
        loc.Add("MaxHp", record.MaxHp * count);
        loc.Add("Attack", record.Attack * count);
        loc.Add("Effect", record.EffectText);
        return loc.GetFormattedText();
    }

    private static void RefreshFieldHealthAfterAbsorb(CardModel devourer, int maxHpContribution, int currentHpContribution)
    {
        if (!MonsterFieldService.IsOnField(devourer) || RelinquishedControlPower.HasSharedHealth(devourer))
            return;

        var current = MonsterFieldHealthService.PeekHealth(devourer);
        if (!current.IsValid)
            return;

        var updatedMaxHp = Math.Max(1, current.MaxHp + Math.Max(0, maxHpContribution));
        var updatedCurrentHp = Math.Clamp(current.CurrentHp + Math.Max(0, currentHpContribution), 0, updatedMaxHp);
        MonsterFieldHealthService.SetHealth(devourer, updatedCurrentHp, updatedMaxHp);
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

    private sealed class InheritedCyberState
    {
        internal int MaxHpBonus { get; set; }
        internal int CurrentHpBonus { get; set; }
        internal int AttackContribution { get; set; }
        internal List<ICyberCopyableEffect> Effects { get; } = [];
        internal List<CyberDevourRecord> Records { get; } = [];
    }
}

internal sealed record CyberDevourContribution(
    int MaxHp,
    int CurrentHp,
    int Attack,
    IReadOnlyList<ICyberCopyableEffect> Effects,
    IReadOnlyList<CyberDevourRecord> Records);

internal sealed class CyberDevourDisplaySnapshot
{
    internal CyberDevourDisplaySnapshot(
        int maxHpBonus,
        int attackContribution,
        IEnumerable<CyberDevourRecord> records)
    {
        MaxHpBonus = Math.Max(0, maxHpBonus);
        AttackContribution = Math.Max(0, attackContribution);
        Records = Array.AsReadOnly(records?.ToArray() ?? Array.Empty<CyberDevourRecord>());
    }

    internal int MaxHpBonus { get; }
    internal int AttackContribution { get; }
    internal IReadOnlyList<CyberDevourRecord> Records { get; }
}

internal sealed record CyberDevourRecord(
    string Title,
    bool Upgraded,
    int MaxHp,
    int Attack,
    string EffectText)
{
    internal bool HasMaterialEffect { get; init; }

    internal string Fingerprint => $"{Title}|{Upgraded}|{MaxHp}|{Attack}|{EffectText}";
}

internal sealed class CyberLarvaGenerationEffect(int amount = 1, bool upgraded = false) : ICyberCopyableEffect
{
    private readonly int _amount = Math.Max(1, amount);
    private readonly bool _upgraded = upgraded;

    public ICyberCopyableEffect Clone() => new CyberLarvaGenerationEffect(_amount, _upgraded);

    public string GetDevourDisplayText()
    {
        var loc = new LocString("static_hover_tips", "THERMALVORTEX_DEVOUR_STATUS.cyberLarvaEffect");
        loc.Add("Amount", _amount);
        loc.Add("Upgrade", _upgraded ? "+" : string.Empty);
        return loc.GetFormattedText();
    }

    public Task OnCyberSummoned(PlayerChoiceContext ctx, CardModel host, AbstractModel source) =>
        Generate(host, source);

    public Task OnCyberUpkeep(PlayerChoiceContext ctx, CardModel host, AbstractModel source) =>
        Task.CompletedTask;

    private async Task Generate(CardModel host, AbstractModel source)
    {
        var owner = host?.Owner;
        if (owner is null)
            return;

        for (var i = 0; i < _amount; i++)
            await CyberSeries.GenerateCyberLarvaToDiscard(
                owner,
                source as CardModel ?? host,
                upgraded: _upgraded);
    }
}

internal enum EnemyActionDrawEffectKind
{
    Any,
    Attack,
    PowerChange,
    Summon
}

internal sealed class EnemyActionDrawInheritedEffect(EnemyActionDrawEffectKind kind) : ICyberCopyableEffect
{
    private readonly EnemyActionDrawEffectKind _kind = kind;
    internal EnemyActionDrawEffectKind Kind => _kind;

    public ICyberCopyableEffect Clone() => new EnemyActionDrawInheritedEffect(_kind);

    public string GetDevourDisplayText() =>
        new LocString(
            "static_hover_tips",
            $"THERMALVORTEX_DEVOUR_STATUS.enemyDraw{_kind}").GetFormattedText();

    public Task OnCyberSummoned(PlayerChoiceContext ctx, CardModel host, AbstractModel source) =>
        Task.CompletedTask;

    public Task OnCyberUpkeep(PlayerChoiceContext ctx, CardModel host, AbstractModel source) =>
        Task.CompletedTask;
}

internal sealed class CyberInfinityDevourEffect : ICyberCopyableEffect
{
    public ICyberCopyableEffect Clone() => new CyberInfinityDevourEffect();

    public string GetDevourDisplayText() =>
        new LocString("static_hover_tips", "THERMALVORTEX_DEVOUR_STATUS.infinityEffect").GetFormattedText();

    public Task OnCyberSummoned(PlayerChoiceContext ctx, CardModel host, AbstractModel source) =>
        EnsurePower(ctx, host);

    internal static async Task EnsurePower(PlayerChoiceContext ctx, CardModel host)
    {
        var owner = host?.Owner?.Creature;
        if (owner is null || owner.HasPower<CyberDragonInfinityPower>())
            return;

        await ThermalVortexCommandCompat.ApplyPower<CyberDragonInfinityPower>(ctx, owner, 1, owner, host, false);
    }

    public Task OnCyberUpkeep(PlayerChoiceContext ctx, CardModel host, AbstractModel source) =>
        Task.CompletedTask;
}

internal enum CyberUpkeepEffectKind
{
    Block,
    WeakAll,
    VulnerableAll
}

internal sealed class CyberUpkeepInheritedEffect(CyberUpkeepEffectKind kind, int amount) : ICyberCopyableEffect
{
    private readonly CyberUpkeepEffectKind _kind = kind;
    private readonly int _amount = Math.Max(0, amount);

    public ICyberCopyableEffect Clone() => new CyberUpkeepInheritedEffect(_kind, _amount);

    public string GetDevourDisplayText()
    {
        var loc = new LocString("static_hover_tips", $"THERMALVORTEX_DEVOUR_STATUS.upkeep{_kind}");
        loc.Add("Amount", _amount);
        return loc.GetFormattedText();
    }

    public Task OnCyberSummoned(PlayerChoiceContext ctx, CardModel host, AbstractModel source) =>
        Task.CompletedTask;

    public async Task OnCyberUpkeep(PlayerChoiceContext ctx, CardModel host, AbstractModel source)
    {
        var owner = host?.Owner?.Creature;
        if (owner is null || _amount <= 0 || !MonsterFieldService.IsOnField(host))
            return;

        switch (_kind)
        {
            case CyberUpkeepEffectKind.Block:
                await ThermalVortexCommandCompat.GainBlock(
                    owner, _amount, ValueProp.Unpowered, null, false);
                break;
            case CyberUpkeepEffectKind.WeakAll:
                await ThermalVortexCommandCompat.ApplyPower<WeakPower>(ctx, owner.CombatState.HittableEnemies, _amount, owner, host, false);
                break;
            case CyberUpkeepEffectKind.VulnerableAll:
                await ThermalVortexCommandCompat.ApplyPower<VulnerablePower>(ctx, owner.CombatState.HittableEnemies, _amount, owner, host, false);
                break;
        }
    }
}

internal sealed class CyberMaterialDamageEffect(int amount) : ICyberCopyableEffect
{
    private readonly int _amount = Math.Max(0, amount);

    public ICyberCopyableEffect Clone() => new CyberMaterialDamageEffect(_amount);

    public string GetDevourDisplayText()
    {
        var loc = new LocString("static_hover_tips", "THERMALVORTEX_DEVOUR_STATUS.materialDamage");
        loc.Add("Amount", _amount);
        return loc.GetFormattedText();
    }

    public Task OnCyberSummoned(PlayerChoiceContext ctx, CardModel host, AbstractModel source) => Task.CompletedTask;
    public Task OnCyberUpkeep(PlayerChoiceContext ctx, CardModel host, AbstractModel source) => Task.CompletedTask;

    public async Task OnCyberLeavingField(PlayerChoiceContext ctx, CardModel host, MonsterFieldLeaveEvent leaveEvent)
    {
        if (!leaveEvent.WasUsedAsMaterial || _amount <= 0)
            return;

        var target = await EffectTargeting.ChooseEnemy(ctx, host.Owner);
        if (target is not null)
            await ThermalVortexCombatVfx.EffectDamageAsync(
                ctx,
                target,
                _amount,
                ValueProp.Unpowered,
                host.Owner.Creature,
                host);
    }
}

internal sealed class CyberFaraPersistenceEffect : ICyberCopyableEffect
{
    public ICyberCopyableEffect Clone() => new CyberFaraPersistenceEffect();

    public string GetDevourDisplayText() =>
        new LocString("static_hover_tips", "THERMALVORTEX_DEVOUR_STATUS.faraEffect").GetFormattedText();

    public Task OnCyberSummoned(PlayerChoiceContext ctx, CardModel host, AbstractModel source) => Task.CompletedTask;
    public Task OnCyberUpkeep(PlayerChoiceContext ctx, CardModel host, AbstractModel source) => Task.CompletedTask;
    public bool PreventsEnemyAttackTarget(CardModel host) => true;

    public async Task OnCyberLeftFieldResolved(PlayerChoiceContext ctx, CardModel host, MonsterFieldLeaveEvent leaveEvent)
    {
        if (leaveEvent.WasUsedAsMaterial)
            await MonsterFieldService.SpecialSummon(ctx, host, leaveEvent.Source ?? host);
    }
}

internal sealed class CyberContractBookEffect(int drawReduction) : ICyberCopyableEffect
{
    private readonly int _drawReduction = Math.Max(0, drawReduction);
    internal int DrawReduction => _drawReduction;

    public ICyberCopyableEffect Clone() => new CyberContractBookEffect(_drawReduction);

    public string GetDevourDisplayText()
    {
        var loc = new LocString("static_hover_tips", "THERMALVORTEX_DEVOUR_STATUS.contractBook");
        loc.Add("DrawReduction", _drawReduction);
        return loc.GetFormattedText();
    }

    public Task OnCyberSummoned(PlayerChoiceContext ctx, CardModel host, AbstractModel source) => Task.CompletedTask;
    public Task OnCyberUpkeep(PlayerChoiceContext ctx, CardModel host, AbstractModel source) => Task.CompletedTask;
}
