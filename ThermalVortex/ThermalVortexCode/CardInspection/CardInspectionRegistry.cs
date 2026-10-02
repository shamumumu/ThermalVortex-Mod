using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using ThermalVortex.ThermalVortexCode.Patches;

namespace ThermalVortex.ThermalVortexCode.CardInspection;

// Entries describe views, not card models: pooled views and transform previews rebind.
internal static class CardInspectionRegistry
{
    private static readonly HashSet<NCard> Cards = [];
    private static readonly HashSet<NDeckHistoryEntry> HistoryEntries = [];
    private static readonly Dictionary<Control, Registration> Custom = [];
    private static readonly Dictionary<NCard, CardPreviewMode> PreviewModes = [];
    private static readonly FieldInfo FrameField = AccessTools.Field(typeof(NCard), "_frame");
    private static readonly FieldInfo TargetField = AccessTools.Field(typeof(NCard), "_previewTarget");
    private static readonly FieldInfo UnpoweredField = AccessTools.Field(typeof(NCard), "_forceUnpoweredPreview");
    private static readonly FieldInfo PlayableField = AccessTools.Field(typeof(NCard), "_pretendCardCanBePlayed");
    private static readonly FieldInfo GridCardsField = AccessTools.Field(typeof(NCardGrid), "_cards");
    private static readonly FieldInfo TipOwnerField = AccessTools.Field(typeof(NHoverTipSet), "_owner");
    private static readonly Func<NCardGrid, CardModel, ModelVisibility> GridVisibility =
        AccessTools.Method(typeof(NCardGrid), "GetCardVisibility")
            ?.CreateDelegate<Func<NCardGrid, CardModel, ModelVisibility>>();

    internal static void Register(Control hitRegion, Node owner,
        Func<Vector2, CardInspectionRequest> requestFactory, Func<bool> canInspect = null)
    {
        if (!Valid(hitRegion) || !Valid(owner) || requestFactory is null)
            return;
        if (Custom.TryGetValue(hitRegion, out var existing))
        {
            existing.Owner = owner;
            existing.Create = requestFactory;
            existing.CanInspect = canInspect;
            return;
        }
        var entry = new Registration(hitRegion, owner, requestFactory, canInspect);
        entry.OnExit = () => Unregister(hitRegion);
        Custom.Add(hitRegion, entry);
        hitRegion.TreeExiting += entry.OnExit;
    }

    internal static void Unregister(Control hitRegion)
    {
        if (hitRegion is not null && Custom.Remove(hitRegion, out var entry) && Valid(hitRegion))
            hitRegion.TreeExiting -= entry.OnExit;
    }

    internal static void Observe(Node node)
    {
        if (node is NCard card)
            Cards.Add(card);
        else if (node is NDeckHistoryEntry history)
            HistoryEntries.Add(history);
    }

    internal static void Forget(Node node)
    {
        if (node is NCard card)
        {
            Cards.Remove(card);
            PreviewModes.Remove(card);
        }
        if (node is NDeckHistoryEntry history)
            HistoryEntries.Remove(history);
        if (node is Control control)
            Unregister(control);
    }

    internal static void RememberPreview(NCard card, CardPreviewMode mode)
    {
        if (Valid(card))
            PreviewModes[card] = mode;
    }

    internal static void Clear()
    {
        foreach (var control in Custom.Keys.ToArray())
            Unregister(control);
        Cards.Clear();
        HistoryEntries.Clear();
        PreviewModes.Clear();
    }

    internal static bool TryResolve(Viewport viewport, Vector2 point, out CardInspectionRequest request)
    {
        request = null;
        var active = ActiveScreenContext.Instance?.GetCurrentScreen() as Node;
        var inspect = NGame.Instance?.InspectCardScreen;
        var inspecting = Valid(inspect) && inspect.Visible;
        var hovered = viewport.GuiGetHoveredControl();
        var hits = new List<Hit>();

        if (!inspecting)
        {
            foreach (var entry in Custom.Values)
            {
                if (Visible(entry.Region) && entry.Region.GetViewport() == viewport
                    && Valid(entry.Owner) && entry.Owner.IsInsideTree()
                    && (entry.CanInspect?.Invoke() ?? true) && ContainsPoint(entry.Region, point))
                    hits.Add(new Hit(entry.Region, entry.Region, null, entry));
            }
            foreach (var history in HistoryEntries)
            {
                if (Visible(history) && history.Card is not null && history.GetViewport() == viewport
                    && InActiveScope(history, active) && ContainsPoint(history, point))
                    hits.Add(new Hit(history, history, null, null, history));
            }
        }

        foreach (var card in Cards)
        {
            if (!Visible(card) || card.Model is null || card.Visibility != ModelVisibility.Visible
                || card.GetViewport() != viewport)
                continue;
            var tip = Ancestor<NHoverTipSet>(card);
            // The main inspected card keeps the native close interaction. Only its related cards replace content.
            if (inspecting ? !IsInspectTip(tip, inspect) : !InActiveScope(card, active))
                continue;
            var face = GetCardFace(card);
            if (!ContainsPoint(face, point))
                continue;
            var holder = Ancestor<NCardHolder>(card);
            Control interaction = holder ?? (Control)Ancestor<NMerchantCard>(card)
                ?? (Control)Ancestor<NCardBundle>(card) ?? card;
            hits.Add(new Hit(face, interaction, card));
        }

        hits.Sort((a, b) => CompareDrawOrder(b.Face, a.Face));
        foreach (var hit in hits)
        {
            if (BlockedByHoveredControl(hit, hovered, point))
                continue;
            if (hit.Custom is not null)
                request = hit.Custom.Create(point);
            else if (hit.History is not null)
                request = BuildHistoryRequest(hit.History, active);
            else
                request = BuildCardRequest(hit.Card, active, inspecting);
            if (request is not null && request.Entries.Count > 0)
                return true;
        }
        return false;
    }

    internal static Control GetCardFace(NCard card)
    {
        if (!Valid(card))
            return null;
        if (FrameField?.GetValue(card) is Control frame && Visible(frame)
            && frame.Size.X > 0 && frame.Size.Y > 0)
            return frame;
        if (Valid(card.Body) && card.Body.Size.X > 0 && card.Body.Size.Y > 0)
            return card.Body;
        return card;
    }

    internal static bool ContainsPoint(Control control, Vector2 viewportPoint)
    {
        if (!Visible(control) || control.Size.X <= 0 || control.Size.Y <= 0)
            return false;
        var transform = control.GetGlobalTransformWithCanvas();
        if (Mathf.IsZeroApprox(transform.Determinant()))
            return false;
        if (!new Rect2(Vector2.Zero, control.Size).HasPoint(transform.AffineInverse() * viewportPoint))
            return false;
        for (var node = control.GetParent(); node is not null; node = node.GetParent())
        {
            if (node is Control { ClipContents: true } clip)
            {
                var clipTransform = clip.GetGlobalTransformWithCanvas();
                if (Mathf.IsZeroApprox(clipTransform.Determinant())
                    || !new Rect2(Vector2.Zero, clip.Size).HasPoint(clipTransform.AffineInverse() * viewportPoint))
                    return false;
            }
        }
        return true;
    }

    internal static CardInspectionDisplayContext CaptureDisplay(NCard card)
    {
        var mode = PreviewModes.TryGetValue(card, out var remembered) ? remembered : CardPreviewMode.Normal;
        return new CardInspectionDisplayContext(card.DisplayingPile, mode,
            TargetField?.GetValue(card) as Creature ?? card.Model?.CurrentTarget,
            UnpoweredField?.GetValue(card) is true, PlayableField?.GetValue(card) is true);
    }

    private static CardInspectionRequest BuildCardRequest(NCard card, Node active, bool replace)
    {
        var selected = new CardInspectionEntry(card.Model, CaptureDisplay(card));
        var entries = new List<CardInspectionEntry>();
        var index = 0;
        var holder = Ancestor<NCardHolder>(card);
        var tip = Ancestor<NHoverTipSet>(card);
        var owner = ResolveOwner(card, active);
        var viewUpgraded = selected.Display.Mode == CardPreviewMode.Upgrade;
        if (!replace && Ancestor<NCardGrid>(card) is { } grid
            && GridCardsField?.GetValue(grid) is IReadOnlyList<CardModel> models)
        {
            var baseCard = holder?.CardModel ?? card.Model;
            var found = false;
            foreach (var model in models)
            {
                if (model is null || (GridVisibility is not null && GridVisibility(grid, model) != ModelVisibility.Visible))
                    continue;
                if (!found && ReferenceEquals(model, baseCard))
                {
                    index = entries.Count;
                    entries.Add(selected);
                    found = true;
                }
                else
                    entries.Add(new CardInspectionEntry(model, selected.Display with { PreviewTarget = null }));
            }
            if (!found)
            {
                entries.Clear();
                entries.Add(selected);
                index = 0;
            }
            viewUpgraded |= grid.IsShowingUpgrades;
        }
        else if (!replace && holder is NHandCardHolder && Valid(NPlayerHand.Instance))
        {
            AddViews(NPlayerHand.Instance.ActiveHolders.Select(h => h.CardNode));
        }
        else if (!replace && Ancestor<NCardBundle>(card) is { } bundle)
        {
            AddViews(bundle.CardNodes);
        }
        else if (Valid(tip))
        {
            AddViews(Cards.Where(c => Valid(c) && ReferenceEquals(Ancestor<NHoverTipSet>(c), tip))
                .OrderBy(c => c, NodeOrderComparer.Instance));
        }
        else
            entries.Add(selected);
        if (entries.Count == 0)
            entries.Add(selected);
        return new CardInspectionRequest(owner, entries, index,
            holder?.Hitbox ?? card.GetViewport().GuiGetFocusOwner(), viewUpgraded, replace);

        void AddViews(IEnumerable<NCard> views)
        {
            foreach (var view in views)
            {
                if (!Visible(view) || view.Model is null || view.Visibility != ModelVisibility.Visible)
                    continue;
                if (ReferenceEquals(view, card))
                    index = entries.Count;
                entries.Add(ReferenceEquals(view, card) ? selected : new CardInspectionEntry(view.Model, CaptureDisplay(view)));
            }
        }
    }

    private static CardInspectionRequest BuildHistoryRequest(NDeckHistoryEntry selected, Node active)
    {
        var parent = selected.GetParent();
        var entries = new List<CardInspectionEntry>();
        var index = 0;
        foreach (var entry in HistoryEntries.Where(h => Visible(h) && ReferenceEquals(h.GetParent(), parent))
                     .OrderBy(h => h.GetIndex()))
        {
            if (entry.Card is null)
                continue;
            if (ReferenceEquals(entry, selected))
                index = entries.Count;
            entries.Add(new CardInspectionEntry(entry.Card));
        }
        return new CardInspectionRequest(ResolveOwner(selected, active), entries, index, selected);
    }

    private static bool InActiveScope(Node source, Node active)
    {
        if (!Valid(active))
            return true;
        var tip = Ancestor<NHoverTipSet>(source);
        if (Valid(tip) && TipOwnerField?.GetValue(tip) is Node tipOwner)
            source = tipOwner;
        if (Within(active, source))
            return true;
        if (MonsterFieldUi.IsPeekActiveFor(active)
            && (Ancestor<NCombatUi>(source) is not null || IsTransientCard(source)))
            return true;
        // UI-independent fly-card effects are hosted outside the room's screen subtree.
        return IsTransientCard(source) && active.GetType().Namespace?.Contains(".Nodes.Rooms", StringComparison.Ordinal) == true;
    }

    private static Node ResolveOwner(Node source, Node active)
    {
        if (Ancestor<NHoverTipSet>(source) is { } tip && TipOwnerField?.GetValue(tip) is Node tipOwner)
            source = tipOwner;
        if (Valid(active))
            return active;
        for (var node = source; node is not null; node = node.GetParent())
            if (node is IScreenContext)
                return node;
        return NGame.Instance;
    }

    private static bool IsTransientCard(Node source)
    {
        for (var node = source; node is not null; node = node.GetParent())
            if (node.GetType().Namespace?.Contains(".Nodes.Vfx", StringComparison.Ordinal) == true)
                return true;
        return false;
    }

    private static bool IsInspectTip(NHoverTipSet tip, NInspectCardScreen inspect) =>
        Valid(tip) && TipOwnerField?.GetValue(tip) is Node owner && Within(inspect, owner);

    private static bool BlockedByHoveredControl(Hit hit, Control hovered, Vector2 point)
    {
        if (!Visible(hovered) || !ContainsPoint(hovered, point)
            || Within(hit.Interaction, hovered) || Within(hovered, hit.Interaction))
            return false;
        // Another card's hitbox can sit behind a visually raised/rotated card.
        // Compare actual drawing before treating that hitbox as an occluder.
        var otherCard = Ancestor<NCard>(hovered) ?? Ancestor<NCardHolder>(hovered)?.CardNode;
        if (Valid(otherCard))
            return CompareDrawOrder(GetCardFace(otherCard), hit.Face) > 0;
        return CompareDrawOrder(hovered, hit.Face) >= 0;
    }

    internal static int CompareDrawOrder(CanvasItem first, CanvasItem second)
    {
        if (ReferenceEquals(first, second))
            return 0;
        var layer = CanvasLayerOf(first).CompareTo(CanvasLayerOf(second));
        if (layer != 0)
            return layer;
        var z = EffectiveZ(first).CompareTo(EffectiveZ(second));
        return z != 0 ? z : CompareTreeOrder(first, second);
    }

    private static int CanvasLayerOf(Node node)
    {
        for (; node is not null; node = node.GetParent())
            if (node is CanvasLayer layer)
                return layer.Layer;
        return 0;
    }

    private static int EffectiveZ(CanvasItem item)
    {
        var z = item.ZIndex;
        for (var node = item.GetParent(); item.ZAsRelative && node is not null; node = node.GetParent())
        {
            if (node is CanvasLayer)
                break;
            if (node is CanvasItem parent)
            {
                z += parent.ZIndex;
                item = parent;
            }
        }
        return Math.Clamp(z, -4096, 4096);
    }

    private static int CompareTreeOrder(Node first, Node second)
    {
        var a = PathToRoot(first);
        var b = PathToRoot(second);
        var i = 0;
        while (i < Math.Min(a.Count, b.Count) && ReferenceEquals(a[i], b[i]))
            i++;
        if (i == a.Count || i == b.Count)
            return a.Count.CompareTo(b.Count);
        return a[i].GetIndex().CompareTo(b[i].GetIndex());
    }

    private static List<Node> PathToRoot(Node node)
    {
        var path = new List<Node>();
        for (; node is not null; node = node.GetParent())
            path.Add(node);
        path.Reverse();
        return path;
    }

    internal static bool Valid(GodotObject value) => value is not null && GodotObject.IsInstanceValid(value);
    private static bool Visible(Control value) => Valid(value) && value.IsInsideTree()
        && !value.IsQueuedForDeletion() && value.IsVisibleInTree() && value.SelfModulate.A > 0.001f
        && AncestorAlphaVisible(value);

    private static bool AncestorAlphaVisible(Node node)
    {
        for (; node is not null; node = node.GetParent())
            if (node is CanvasItem item && item.Modulate.A <= 0.001f)
                return false;
        return true;
    }

    internal static bool Within(Node parent, Node child) => Valid(parent) && Valid(child)
        && (ReferenceEquals(parent, child) || parent.IsAncestorOf(child));

    internal static T Ancestor<T>(Node source) where T : Node
    {
        for (var node = source; node is not null; node = node.GetParent())
            if (node is T match)
                return match;
        return null;
    }

    private sealed class Registration(Control region, Node owner,
        Func<Vector2, CardInspectionRequest> create, Func<bool> canInspect)
    {
        internal readonly Control Region = region;
        internal Node Owner = owner;
        internal Func<Vector2, CardInspectionRequest> Create = create;
        internal Func<bool> CanInspect = canInspect;
        internal Action OnExit;
    }

    private sealed record Hit(Control Face, Control Interaction, NCard Card = null,
        Registration Custom = null, NDeckHistoryEntry History = null);

    private sealed class NodeOrderComparer : IComparer<NCard>
    {
        internal static readonly NodeOrderComparer Instance = new();
        public int Compare(NCard x, NCard y) => CompareTreeOrder(x, y);
    }
}

[HarmonyPatch(typeof(NCard), nameof(NCard.UpdateVisuals))]
internal static class CardInspectionRememberPreviewPatch
{
    private static void Postfix(NCard __instance, CardPreviewMode __1) =>
        CardInspectionRegistry.RememberPreview(__instance, __1);
}

[HarmonyPatch(typeof(NCard), nameof(NCard.OnFreedToPool))]
internal static class CardInspectionPoolReleasePatch
{
    private static void Prefix(NCard __instance) => CardInspectionRegistry.Forget(__instance);
}

[HarmonyPatch(typeof(NCard), nameof(NCard.OnReturnedFromPool))]
internal static class CardInspectionPoolAcquirePatch
{
    private static void Postfix(NCard __instance) => CardInspectionRegistry.Observe(__instance);
}
