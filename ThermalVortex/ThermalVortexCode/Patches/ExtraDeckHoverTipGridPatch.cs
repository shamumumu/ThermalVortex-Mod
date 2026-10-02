using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using ThermalVortex.ThermalVortexCode.Relics;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NHoverTipCardContainer), nameof(NHoverTipCardContainer.LayoutResizeAndReposition))]
internal static class ExtraDeckHoverTipGridPatch
{
    private const int MinimumGridCardCount = 4;
    private const int MaxColumns = 3;
    private const float Gap = 16f;
    private const float SafeMargin = 32f;
    private const float TopMargin = 120f;
    private const float ReservedCenterLeftRatio = 0.34f;
    private static readonly Vector2 FallbackCardSize = new(220f, 308f);
    private static readonly Func<NHoverTipCardContainer, IEnumerable<Control>> TipsGetter = CreateTipsGetter();

    [HarmonyPostfix]
    private static void LayoutExtraDeckCardsAsGrid(NHoverTipCardContainer __instance)
    {
        var tips = GetTipControls(__instance).ToList();
        if (tips.Count < MinimumGridCardCount)
            return;

        var cards = tips.Select(FindCard).ToList();
        if (cards.Any(card => card?.Model is null || !ThermalVortexCore.IsXyzMonsterDisplayCard(card.Model)))
            return;

        var cardSize = ResolveCellSize(tips);
        var viewport = __instance.GetViewportRect().Size;
        var columns = ResolveColumnCount(tips.Count, cardSize, viewport);
        var rows = (int)Math.Ceiling(tips.Count / (float)columns);
        var gridSize = new Vector2(
            (columns * cardSize.X) + ((columns - 1) * Gap),
            (rows * cardSize.Y) + ((rows - 1) * Gap));

        for (var index = 0; index < tips.Count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            tips[index].Position = new Vector2(column * (cardSize.X + Gap), row * (cardSize.Y + Gap));
        }

        __instance.CustomMinimumSize = gridSize;
        __instance.Size = gridSize;
        PlaceInSafeArea(__instance, gridSize, viewport);
    }

    private static Func<NHoverTipCardContainer, IEnumerable<Control>> CreateTipsGetter()
    {
        var getter = AccessTools.PropertyGetter(typeof(NHoverTipCardContainer), "Tips");
        if (getter is null)
            return null;

        return (Func<NHoverTipCardContainer, IEnumerable<Control>>)Delegate.CreateDelegate(
            typeof(Func<NHoverTipCardContainer, IEnumerable<Control>>),
            getter);
    }

    private static IEnumerable<Control> GetTipControls(NHoverTipCardContainer container)
    {
        if (TipsGetter is not null)
            return TipsGetter(container);

        return container.GetChildren().OfType<Control>();
    }

    private static NCard FindCard(Node node)
    {
        if (node is NCard card)
            return card;

        foreach (var child in node.GetChildren())
        {
            var childCard = FindCard(child);
            if (childCard is not null)
                return childCard;
        }

        return null;
    }

    private static Vector2 ResolveCellSize(IEnumerable<Control> tips)
    {
        var size = Vector2.Zero;
        foreach (var tip in tips)
        {
            var tipSize = tip.Size;
            if (tipSize.X <= 0 || tipSize.Y <= 0)
                tipSize = tip.GetCombinedMinimumSize();

            size.X = Math.Max(size.X, tipSize.X);
            size.Y = Math.Max(size.Y, tipSize.Y);
        }

        if (size.X <= 0 || size.Y <= 0)
            return FallbackCardSize;

        return size;
    }

    private static int ResolveColumnCount(int cardCount, Vector2 cardSize, Vector2 viewport)
    {
        var maxColumns = Math.Min(MaxColumns, cardCount);
        if (cardSize.X <= 0 || viewport.X <= 0)
            return maxColumns;

        var availableWidth = Math.Max(cardSize.X, ReservedCenterLeft(viewport) - (SafeMargin * 2f));
        var fittingColumns = (int)Math.Floor((availableWidth + Gap) / (cardSize.X + Gap));
        return Math.Clamp(fittingColumns, 1, maxColumns);
    }

    private static void PlaceInSafeArea(Control control, Vector2 size, Vector2 viewport)
    {
        var position = control.GlobalPosition;
        var reservedCenterLeft = ReservedCenterLeft(viewport);
        var maxSafeX = Math.Max(SafeMargin, reservedCenterLeft - size.X - SafeMargin);

        position.X = Clamp(position.X, SafeMargin, maxSafeX);
        position.Y = Clamp(position.Y, TopMargin, Math.Max(TopMargin, viewport.Y - size.Y - SafeMargin));
        control.GlobalPosition = position;
    }

    private static float ReservedCenterLeft(Vector2 viewport) =>
        viewport.X * ReservedCenterLeftRatio;

    private static float Clamp(float value, float min, float max) =>
        max <= min ? min : Math.Clamp(value, min, max);
}
