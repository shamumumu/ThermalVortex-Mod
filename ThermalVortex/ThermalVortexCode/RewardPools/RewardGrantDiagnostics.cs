using System.Diagnostics;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.RewardPools;

internal sealed record RewardGrantSourceRegistration(
    string TypePrefix,
    RewardGrantPolicy Policy);

/// <summary>
/// The shared audited source registry used by runtime grant policy resolution
/// and the logging-only guard for permanent direct grants. The observer never
/// changes a card, pile, result, or exception.
/// </summary>
internal static class RewardGrantDiagnostics
{
    private static readonly AsyncLocal<SourceContext> CurrentSource = new();

    internal static IReadOnlyList<RewardGrantSourceRegistration> RegisteredDirectGrantSources { get; } =
    [
        new("MegaCrit.Sts2.Core.Models.Events.BrainLeech+<ShareKnowledge>", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Events.EndlessConveyor+<FriedEel>", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Events.InfestedAutomaton+<Study>", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Events.InfestedAutomaton+<TouchCore>", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Events.RoomFullOfCheese+<Gorge>", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Relics.GlassEye+", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Relics.Kaleidoscope+", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Relics.LeadPaperweight+", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Relics.LostCoffer+", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Relics.MassiveScroll+", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Relics.Orrery+", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Relics.SeaGlass+", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Relics.ScrollBoxes+", RewardGrantPolicy.GenericRandomReward),
        new("ThermalVortex.ThermalVortexCode.Patches.ArcaneScrollCompatibility", RewardGrantPolicy.GenericRandomReward),

        // Authored fixed content. TrashHeap is the finite event pool found by
        // the assembly audit; the remaining entries grant named fixed cards.
        new("MegaCrit.Sts2.Core.Models.Events.TrashHeap+<Grab>", RewardGrantPolicy.FixedEventPool),
        new("MegaCrit.Sts2.Core.Models.Events.Bugslayer+<AddAndPreview>", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Events.ByrdonisNest+<Take>", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Events.EndlessConveyor+<SeapunkSalad>", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Events.SpiritGrafter+<LetItIn>", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Events.TheLegendsWereTrue+<NabTheMap>", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Events.TinkerTime+<RiderChosen>", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Events.ZenWeaver+<BreathingTechniques>", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Relics.DistinguishedCape+", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Relics.JewelryBox+", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Relics.LargeCapsule+", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Relics.NeowsTorment+", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Relics.PaelsHorn+", RewardGrantPolicy.FixedSingle),
        // Sere Talon first rolls curses, then adds authored Wish cards.  The
        // per-card branch is refined in ResolveMixedSourcePolicy below.
        new("MegaCrit.Sts2.Core.Models.Relics.SereTalon+", RewardGrantPolicy.CurseStatus),
        new("MegaCrit.Sts2.Core.Models.Relics.Storybook+", RewardGrantPolicy.FixedSingle),
        new("MegaCrit.Sts2.Core.Models.Relics.TanxsWhistle+", RewardGrantPolicy.FixedSingle),

        new("MegaCrit.Sts2.Core.Models.Events.Amalgamator+<Combine", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Events.AromaOfChaos+<LetGo>", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Events.EndlessConveyor+<JellyLiver>", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Events.MorphicGrove+<Group>", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Events.Symbiote+<KillWithFire>", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Events.Trial+<NondescriptInnocent>", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Events.WhisperingHollow+<Hug>", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Events.WoodCarvings+<Bird>", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Events.WoodCarvings+<Torus>", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Relics.ArchaicTooth+", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Relics.Astrolabe+", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Relics.Byrdpip+", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Relics.Claws+", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Relics.LeafyPoultice+", RewardGrantPolicy.Transform),
        new("MegaCrit.Sts2.Core.Models.Relics.PandorasBox+", RewardGrantPolicy.Transform),

        // Cards created for combat or another transient pile. These entries
        // document their semantic boundary even though the runtime observer
        // intentionally ignores every pile except the permanent deck.
        new("MegaCrit.Sts2.Core.Models.Relics.BigHat+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.BiiigHug+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.BlessedAntler+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.BurningSticks+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.ChoicesParadox+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.Crossbow+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.FuneraryMask+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.JeweledMask+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.MusicBox+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.PowerCell+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.RadiantPearl+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.Toolbox+", RewardGrantPolicy.Generated),
        new("MegaCrit.Sts2.Core.Models.Relics.VexingPuzzlebox+", RewardGrantPolicy.Generated),

        new("MegaCrit.Sts2.Core.Models.Events.FieldOfManSizedHoles+<Resist>", RewardGrantPolicy.CurseStatus),
        new("MegaCrit.Sts2.Core.Models.Events.LostWisp+<Claim>", RewardGrantPolicy.CurseStatus),
        new("MegaCrit.Sts2.Core.Models.Events.Trial+<NondescriptGuilty>", RewardGrantPolicy.CurseStatus),
        new("MegaCrit.Sts2.Core.Models.Events.UnrestSite+<Rest>", RewardGrantPolicy.CurseStatus),
        new("MegaCrit.Sts2.Core.Models.Events.Wellspring+<AddGuilty>", RewardGrantPolicy.CurseStatus),
        // Hefty Tablet offers generic random cards and also adds fixed Injury.
        // The per-card branch is refined below.
        new("MegaCrit.Sts2.Core.Models.Relics.HeftyTablet+", RewardGrantPolicy.GenericRandomReward),
        new("MegaCrit.Sts2.Core.Models.Relics.NeowsBones+", RewardGrantPolicy.CurseStatus),

        new("MegaCrit.Sts2.Core.Models.Events.Reflections+<Shatter>", RewardGrantPolicy.CopyReturn),
        new("MegaCrit.Sts2.Core.Models.Relics.BingBong+", RewardGrantPolicy.CopyReturn),
        new("MegaCrit.Sts2.Core.Models.Relics.DollysMirror+", RewardGrantPolicy.CopyReturn),
        new("MegaCrit.Sts2.Core.Models.Relics.PaelsTooth+", RewardGrantPolicy.CopyReturn),
        new("MegaCrit.Sts2.Core.Models.Relics.DustyTome+", RewardGrantPolicy.Ancient)
    ];

    internal static IDisposable EnterGenericRandomSource(string sourceName) =>
        EnterSource(RewardGrantPolicy.GenericRandomReward, sourceName);

    internal static IDisposable EnterSource(
        RewardGrantPolicy policy,
        string sourceName)
    {
        var previous = CurrentSource.Value;
        CurrentSource.Value = new SourceContext(
            policy,
            string.IsNullOrWhiteSpace(sourceName) ? "unknown" : sourceName.Trim());
        return new Scope(previous);
    }

    internal static bool TryGetRegisteredPolicy(
        string declaringTypeName,
        out RewardGrantPolicy policy)
    {
        if (!string.IsNullOrWhiteSpace(declaringTypeName))
        {
            var registration = RegisteredDirectGrantSources.FirstOrDefault(candidate =>
                declaringTypeName.StartsWith(candidate.TypePrefix, StringComparison.Ordinal));
            if (registration is not null)
            {
                policy = registration.Policy;
                return true;
            }
        }

        policy = default;
        return false;
    }

    /// <summary>
    /// Resolves the current runtime call through the same source registry used
    /// by permanent-grant diagnostics. An unknown caller deliberately falls
    /// back to the generic-random policy at the call site.
    /// </summary>
    internal static bool TryResolveRuntimePolicy(out RewardGrantPolicy policy)
    {
        if (TryResolveSource(
                out var resolvedPolicy,
                out _,
                out var isRegistered)
            && isRegistered
            && resolvedPolicy.HasValue)
        {
            policy = resolvedPolicy.Value;
            return true;
        }

        policy = default;
        return false;
    }

    internal static bool TryResolveSource(
        out RewardGrantPolicy? policy,
        out string sourceName,
        out bool isRegistered)
    {
        var scoped = CurrentSource.Value;
        if (scoped is not null)
        {
            policy = scoped.Policy;
            sourceName = scoped.SourceName;
            isRegistered = true;
            return true;
        }

        string unregisteredEventOrRelic = null;
        try
        {
            foreach (var frame in new StackTrace(false).GetFrames() ?? [])
            {
                var typeName = frame.GetMethod()?.DeclaringType?.FullName;
                if (typeName is null)
                    continue;

                if (TryGetRegisteredPolicy(typeName, out var registeredPolicy))
                {
                    policy = registeredPolicy;
                    sourceName = typeName;
                    isRegistered = true;
                    return true;
                }

                if (unregisteredEventOrRelic is null && IsEventOrRelicType(typeName))
                    unregisteredEventOrRelic = typeName;
            }
        }
        catch
        {
            // Stack classification is best effort. Unknown callers use the
            // generic policy at runtime and remain visible to diagnostics.
        }

        policy = null;
        sourceName = unregisteredEventOrRelic;
        isRegistered = false;
        return unregisteredEventOrRelic is not null;
    }

    internal static void ObservePermanentDeckAdd(CardModel card, PileType pileType)
    {
        if (pileType != PileType.Deck
            || card?.Owner?.GetRelic<ThermalVortexCore>() is not { } core
            || !RewardGrantPolicies.IsManualMode(card.Owner)
            || !TryResolveSource(out var policy, out var sourceName, out var isRegistered)
            || IsAllowedByDefinition(card, core))
        {
            return;
        }

        policy = ResolveMixedSourcePolicy(sourceName, card, policy);

        if (policy.HasValue && RewardGrantPolicies.IsPassthrough(policy.Value))
            return;

        var reason = policy switch
        {
            RewardGrantPolicy.GenericRandomReward => "unexpected_generic_direct_grant",
            RewardGrantPolicy.Transform => "unexpected_transform_direct_grant",
            _ when isRegistered => "unexpected_registered_direct_grant",
            _ => "unclassified_permanent_direct_grant"
        };
        try
        {
            MainFile.Logger.Info(
                $"RewardGrantDiagnostic {reason} source={sourceName ?? "unknown"} card={card.Id} policy={policy?.ToString() ?? "unregistered"} action=observed_only");
        }
        catch
        {
            // Logging is best effort and must not change the grant.
        }
    }

    internal static bool IsAllowedByDefinition(CardModel card, ThermalVortexCore core)
    {
        if (card is null || core?.IsConstructedRewardPoolEnabled != true)
            return true;

        // Native ordinary colorless rewards remain candidate-level additions
        // to a constructed pool; generated visual-colorless tokens do not use
        // ColorlessCardPool and therefore do not receive this exemption.
        if (RewardPoolCandidateResolver.IsOrdinaryColorlessRewardCandidate(card))
            return true;

        return RewardPoolCatalog.TryGetExtraRewardCandidate(RewardPoolCatalog.GetId(card), out _)
            ? RewardPoolCandidateResolver.IsSelectedExtraRewardCandidate(card, core)
            : RewardPoolCatalog.TryGetMainRewardCandidate(RewardPoolCatalog.GetId(card),
                  core.CurrentRewardPoolDefinition?.IsMultiplayerConstruction == true, out _)
              && core.IsMainDeckRewardAllowed(card);
    }

    internal static void ObserveNoCandidateTransform(CardModel source)
    {
        try
        {
            MainFile.Logger.Info(
                $"RewardPoolTransform no_selected_candidate source={source?.Id?.ToString() ?? "unknown"} action=no_op");
        }
        catch
        {
            // Logging is best effort and must not change the transform.
        }
    }

    internal static RewardGrantPolicy? ResolveMixedSourcePolicy(
        string sourceName,
        CardModel card,
        RewardGrantPolicy? registeredPolicy)
    {
        var cardTypeName = card?.GetType().FullName ?? string.Empty;
        if (sourceName?.StartsWith(
                "MegaCrit.Sts2.Core.Models.Relics.SereTalon+",
                StringComparison.Ordinal) == true)
        {
            return cardTypeName.EndsWith(".Wish", StringComparison.Ordinal)
                ? RewardGrantPolicy.FixedSingle
                : RewardGrantPolicy.CurseStatus;
        }

        if (sourceName?.StartsWith(
                "MegaCrit.Sts2.Core.Models.Relics.HeftyTablet+",
                StringComparison.Ordinal) == true)
        {
            return cardTypeName.EndsWith(".Injury", StringComparison.Ordinal)
                ? RewardGrantPolicy.CurseStatus
                : RewardGrantPolicy.GenericRandomReward;
        }

        return registeredPolicy;
    }

    internal static void ResetScopesForRunTransition() =>
        CurrentSource.Value = null;

    private static bool IsEventOrRelicType(string typeName) =>
        typeName.StartsWith("MegaCrit.Sts2.Core.Models.Events.", StringComparison.Ordinal)
        || typeName.StartsWith("MegaCrit.Sts2.Core.Models.Relics.", StringComparison.Ordinal);

    private sealed record SourceContext(
        RewardGrantPolicy Policy,
        string SourceName);

    private sealed class Scope(SourceContext previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CurrentSource.Value = previous;
        }
    }
}
