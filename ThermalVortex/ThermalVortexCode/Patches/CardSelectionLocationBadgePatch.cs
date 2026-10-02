using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using ThermalVortex.ThermalVortexCode.Cards;
using ThermalVortex.ThermalVortexCode.MonsterField;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NGridCardHolder), "SetCard")]
internal static class CardSelectionLocationBadgeSetCardPatch
{
    [HarmonyPostfix]
    private static void RefreshLocationBadge(NGridCardHolder __instance) =>
        CardSelectionLocationBadgeUi.RefreshDeferred(__instance);
}

[HarmonyPatch(typeof(NGridCardHolder), "OnCardReassigned")]
internal static class CardSelectionLocationBadgeReassignedPatch
{
    [HarmonyPostfix]
    private static void RefreshLocationBadge(NGridCardHolder __instance) =>
        CardSelectionLocationBadgeUi.RefreshDeferred(__instance);
}

[HarmonyPatch(typeof(NGridCardHolder), nameof(NGridCardHolder._Ready))]
internal static class CardSelectionLocationBadgeReadyPatch
{
    [HarmonyPostfix]
    private static void RefreshLocationBadge(NGridCardHolder __instance) =>
        CardSelectionLocationBadgeUi.RefreshDeferred(__instance);
}

[HarmonyPatch(typeof(NGridCardHolder), nameof(NGridCardHolder.SetIsPreviewingUpgrade))]
internal static class CardSelectionLocationBadgeUpgradePreviewPatch
{
    [HarmonyPostfix]
    private static void RefreshLocationBadge(NGridCardHolder __instance) =>
        CardSelectionLocationBadgeUi.RefreshDeferred(__instance);
}

internal static class CardSelectionLocationBadgeUi
{
    private const string BadgeNodeName = "ThermalVortexCardLocationBadge";
    private const string LabelNodeName = "LocationText";

    private static readonly Color HandColor = new(0.96f, 0.69f, 0.24f, 1f);
    private static readonly Color DrawColor = new(0.25f, 0.67f, 0.96f, 1f);
    private static readonly Color DiscardColor = new(0.72f, 0.52f, 0.34f, 1f);
    private static readonly Color ExhaustColor = new(0.67f, 0.42f, 0.94f, 1f);
    private static readonly Color ExtraDeckColor = new(0.72f, 0.9f, 1f, 1f);
    private static readonly Color PlayColor = new(0.96f, 0.31f, 0.23f, 1f);
    private static readonly Color DeckColor = new(0.48f, 0.74f, 0.79f, 1f);

    internal static void RefreshScreenDeferred(Node screen)
    {
        if (screen is null || !GodotObject.IsInstanceValid(screen))
            return;

        // _EnterTree precedes children entering, whereas sorting can reassign
        // already-ready pooled holders. Refresh against the settled hierarchy.
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(screen) && !screen.IsQueuedForDeletion())
                RefreshScreen(screen);
        }).CallDeferred();
    }

    private static void RefreshScreen(Node node)
    {
        if (node is NGridCardHolder holder)
        {
            Refresh(holder);
            return;
        }

        foreach (var child in node.GetChildren())
            RefreshScreen(child);
    }

    internal static void RefreshDeferred(NGridCardHolder holder)
    {
        if (holder is null || !GodotObject.IsInstanceValid(holder))
            return;

        // SetCard updates CardNode before NGridCardHolder synchronizes its
        // private base-card field. Reading the node avoids skipping refresh
        // when a pooled canonical holder is reassigned to a combat card.
        var card = holder.CardNode?.Model ?? holder.CardModel;
        if (card is null || card.IsCanonical)
        {
            // Reward-pool grids use canonical ModelDb cards and holder nodes are
            // pooled.  They never need a run-pile badge, but a recycled holder
            // may still contain one from an earlier mutable card.  Hide that
            // badge synchronously and avoid scheduling hundreds of no-op
            // deferred callbacks during the large builder grid's first frame.
            var staleBadge = FindBadge(holder);
            if (staleBadge is not null)
                staleBadge.Visible = false;
            return;
        }

        // Never render a recycled holder's previous location while its new
        // card assignment and parent screen settle for the deferred refresh.
        var pendingBadge = FindBadge(holder);
        if (pendingBadge is not null)
            pendingBadge.Visible = false;

        // A pooled holder is commonly assigned its card before being reparented
        // into the selection screen. Waiting one frame makes the screen check
        // reliable for both newly allocated and recycled grid rows.
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(holder))
                Refresh(holder);
        }).CallDeferred();
    }

    private static void Refresh(NGridCardHolder holder)
    {
        var badge = FindBadge(holder);
        if (!holder.IsInsideTree()
            || holder.IsQueuedForDeletion()
            || holder.CardNode?.Body is not { } body
            || !GodotObject.IsInstanceValid(body)
            || !body.IsInsideTree()
            || body.IsQueuedForDeletion()
            || !TryGetDescriptor(holder, out var descriptor))
        {
            if (badge is not null)
                badge.Visible = false;
            return;
        }

        badge ??= CreateBadge(holder);
        var label = badge.GetChildren().OfType<Label>().FirstOrDefault();
        if (label is null)
            return;

        label.Text = descriptor.Text;
        badge.AddThemeStyleboxOverride("panel", CreateBadgeStyle(descriptor.Accent));
        badge.Visible = true;
        LayoutBadge(holder, badge, label);
    }

    private static bool TryGetDescriptor(
        NGridCardHolder holder,
        out LocationDescriptor descriptor)
    {
        descriptor = default;
        var card = holder.CardModel;
        // CardModel remains the assigned base card when the native checkbox
        // swaps CardNode.Model to an upgrade-preview clone. Use that stable
        // source so previewing and sorting cannot change this entry's status.
        if (ExtraDeckTopBarUi.TryGetExtraDeckUsage(holder, card, out var used))
        {
            var usageKey = used ? "THERMALVORTEX-EXTRA_DECK_STATUS.used" : "THERMALVORTEX-EXTRA_DECK_STATUS.unused";
            var usageText = new LocString("cards", usageKey);
            descriptor = new LocationDescriptor(usageText.GetFormattedText(), used ? DiscardColor : ExtraDeckColor);
            return true;
        }

        if (!IsInsideSelectionScreen(holder))
            return false;

        if (ExtraDeckSelectionProxyScope.IsRemovalChoice(card))
        {
            var extraDeckText = new LocString("cards", "THERMALVORTEX-CARD_LOCATION.extraDeck");
            descriptor = new LocationDescriptor(extraDeckText.GetFormattedText(), ExtraDeckColor);
            return true;
        }

        // Reward-pool construction displays canonical ModelDb cards.  Canonical
        // models deliberately throw when their run-only Owner property is read;
        // they cannot have a hand/pile location badge in the first place.
        if (card is null
            || card.IsCanonical
            || !IsActiveCombatCard(card))
        {
            return false;
        }

        var pile = card.Pile;
        if (pile is null)
        {
            if (card is not XyzMonsterCard { ExtraDeckEntryIndex: >= 0 })
                return false;

            var extraDeckText = new LocString("cards", "THERMALVORTEX-CARD_LOCATION.extraDeck");
            descriptor = new LocationDescriptor(extraDeckText.GetFormattedText(), ExtraDeckColor);
            return true;
        }

        var ownerCombatPiles = card.Owner.PlayerCombatState?.AllPiles;
        if (ownerCombatPiles is null
            || !ownerCombatPiles.Any(current => ReferenceEquals(current, pile)))
        {
            // PileType.Deck belongs to the run deck rather than the current
            // combat. This keeps forge, replace, and upgrade grids unlabelled.
            return false;
        }

        var index = IndexOfReference(pile.Cards, card);
        if (index < 0)
            return false;

        var ordinal = index + 1;
        string key;
        Color accent;
        if (pile.Type == MonsterFieldPile.FieldPileType)
        {
            key = "THERMALVORTEX-CARD_LOCATION.field";
            accent = MonsterFieldUiState.GetSummonSlotColor(ordinal);
        }
        else
        {
            (key, accent) = pile.Type switch
            {
                PileType.Hand => ("THERMALVORTEX-CARD_LOCATION.hand", HandColor),
                PileType.Draw => ("THERMALVORTEX-CARD_LOCATION.draw", DrawColor),
                PileType.Discard => ("THERMALVORTEX-CARD_LOCATION.discard", DiscardColor),
                PileType.Exhaust => ("THERMALVORTEX-CARD_LOCATION.exhaust", ExhaustColor),
                PileType.Play => ("THERMALVORTEX-CARD_LOCATION.play", PlayColor),
                PileType.Deck => ("THERMALVORTEX-CARD_LOCATION.deck", DeckColor),
                _ => (null, default)
            };
        }

        if (key is null)
            return false;

        var text = new LocString("cards", key);
        if (pile.Type == MonsterFieldPile.FieldPileType || pile.Type == PileType.Hand)
            text.Add("Ordinal", ordinal);
        descriptor = new LocationDescriptor(text.GetFormattedText(), accent);
        return true;
    }

    private static bool IsActiveCombatCard(CardModel card)
    {
        try
        {
            var owner = card.Owner;
            var manager = CombatManager.Instance;
            return manager is not null
                && manager.IsInProgress
                && !manager.IsOverOrEnding
                && owner?.Creature?.CombatState is not null
                && owner.PlayerCombatState is not null
                && MonsterFieldUi.IsThermalVortexPlayer(owner);
        }
        catch
        {
            // Selection holders are heavily pooled across combat and
            // non-combat screens. A transient teardown state should simply
            // hide the badge instead of breaking the selection UI.
            return false;
        }
    }

    private static PanelContainer CreateBadge(NGridCardHolder holder)
    {
        var cardNode = holder.CardNode;
        var body = cardNode.Body;
        var badge = new PanelContainer
        {
            Name = BadgeNodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 0,
            ZAsRelative = true,
            ClipContents = false
        };

        var label = new Label
        {
            Name = LabelNodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Modulate = new Color(1f, 0.965f, 0.86f, 1f)
        };
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.94f));
        label.AddThemeConstantOverride("outline_size", 4);

        badge.AddChild(label);
        // The native inspection backstop and related-card tips cover the card's
        // canvas layer. A holder sibling at Z=200 escaped that occlusion and
        // also missed Body's visibility, modulation and hover transforms.
        body.AddChild(badge);
        var frame = cardNode.GetNodeOrNull<Control>("%Frame");
        (frame ?? body).Resized += () =>
        {
            if (GodotObject.IsInstanceValid(cardNode)
                && GodotObject.IsInstanceValid(badge)
                && GodotObject.IsInstanceValid(label)
                && cardNode.GetParent() is NGridCardHolder currentHolder)
            {
                LayoutBadge(currentHolder, badge, label);
            }
        };
        // NCard and its body survive pool reuse. Clear the selection-only
        // decoration when the card leaves, including moves to another holder.
        // Ordinary Hide/Show stays inherited so showing the body restores it.
        body.TreeExiting += () => badge.Visible = false;
        body.TreeEntered += () =>
        {
            if (GodotObject.IsInstanceValid(cardNode)
                && cardNode.GetParent() is NGridCardHolder currentHolder)
            {
                RefreshDeferred(currentHolder);
            }
        };
        return badge;
    }

    private static PanelContainer FindBadge(NGridCardHolder holder)
    {
        var body = holder.CardNode?.Body;
        if (body is null || !GodotObject.IsInstanceValid(body))
            return null;

        return body.GetChildren()
            .OfType<PanelContainer>()
            .FirstOrDefault(child => child.Name == BadgeNodeName);
    }

    private static void LayoutBadge(
        NGridCardHolder holder,
        PanelContainer badge,
        Label label)
    {
        var body = holder.CardNode.Body;
        var frame = holder.CardNode.GetNodeOrNull<Control>("%Frame");
        if (frame is null || frame.Size.X <= 0f || frame.Size.Y <= 0f)
            return;

        var bodyTransform = body.GetGlobalTransformWithCanvas();
        var holderTransform = holder.GetGlobalTransformWithCanvas();
        if (Mathf.IsZeroApprox(bodyTransform.Determinant())
            || Mathf.IsZeroApprox(holderTransform.Determinant()))
            return;

        // CardContainer is a zero-sized animation origin at the card's center,
        // not a top-left card rectangle. The native Frame occupies negative as
        // well as positive coordinates around it. Use its actual four corners
        // without requiring the frame to be visible during card initialization.
        var frameToBody = bodyTransform.AffineInverse() * frame.GetGlobalTransformWithCanvas();
        var cardRect = new Rect2(frameToBody * Vector2.Zero, Vector2.Zero)
            .Expand(frameToBody * new Vector2(frame.Size.X, 0f))
            .Expand(frameToBody * frame.Size)
            .Expand(frameToBody * new Vector2(0f, frame.Size.Y));

        var bodyToHolder = holderTransform.AffineInverse() * bodyTransform;
        var bodyScale = bodyToHolder.X.Length();
        if (Mathf.IsZeroApprox(bodyScale))
            return;
        var cardWidth = cardRect.Size.X * bodyScale;
        var toBodyScale = 1f / bodyScale;

        var badgeWidth = Math.Clamp(cardWidth * 0.84f, 150f, 276f) * toBodyScale;
        var badgeHeight = Math.Clamp(cardWidth * 0.12f, 31f, 42f) * toBodyScale;
        var badgeSize = new Vector2(badgeWidth, badgeHeight);

        // PanelContainer sizes its label inside the border. Giving the label
        // the whole panel's minimum size would enlarge the panel off-center.
        label.CustomMinimumSize = Vector2.Zero;
        label.AddThemeFontSizeOverride(
            "font_size",
            Math.Max(1, (int)Math.Round(Math.Clamp(cardWidth * 0.066f, 17f, 23f) * toBodyScale)));
        badge.CustomMinimumSize = badgeSize;
        badge.Size = badgeSize;
        badge.Position = new Vector2(
            cardRect.Position.X + (cardRect.Size.X - badge.Size.X) * 0.5f,
            cardRect.End.Y - (badge.Size.Y * 0.45f));
    }

    private static StyleBoxFlat CreateBadgeStyle(Color accent)
    {
        var background = new Color(
            0.018f + accent.R * 0.09f,
            0.015f + accent.G * 0.06f,
            0.025f + accent.B * 0.08f,
            0.97f);
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = accent,
            BorderWidthLeft = 3,
            BorderWidthTop = 2,
            BorderWidthRight = 3,
            BorderWidthBottom = 3,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
            ShadowColor = new Color(0f, 0f, 0f, 0.72f),
            ShadowSize = 5,
            ShadowOffset = new Vector2(0f, 3f)
        };
    }

    private static bool IsInsideSelectionScreen(Node node)
    {
        for (var current = node; current is not null; current = current.GetParent())
        {
            if (current is NCardGridSelectionScreen)
                return true;
        }

        return false;
    }

    private static int IndexOfReference(
        IReadOnlyList<CardModel> cards,
        CardModel target)
    {
        for (var index = 0; index < cards.Count; index++)
        {
            if (ReferenceEquals(cards[index], target))
                return index;
        }

        return -1;
    }

    private readonly record struct LocationDescriptor(string Text, Color Accent);
}
