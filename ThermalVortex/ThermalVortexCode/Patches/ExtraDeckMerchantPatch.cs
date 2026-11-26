using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using ThermalVortex.ThermalVortexCode.Extensions;
using ThermalVortex.ThermalVortexCode.Relics;
using ThermalVortex.ThermalVortexCode.RewardPools;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch]
internal static class ExtraDeckMerchantPatch
{
    private static readonly ConditionalWeakTable<MerchantInventory, ExtraDeckPackMerchantEntry> Entries = new();
    private static readonly ConditionalWeakTable<NMerchantInventory, ShopSlots> Slots = new();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.Setup))]
    private static void RegisterPack(MerchantInventory __instance)
    {
        var player = __instance.Player;
        if (!__instance.IsBlitzShop
            && player.RunState.CurrentRoom is MerchantRoom room
            && player.GetRelic<ThermalVortexCore>() is { } core)
        {
            Entries.GetValue(__instance, _ => new ExtraDeckPackMerchantEntry(player, core, room));
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MerchantInventory), nameof(MerchantInventory.AllEntries), MethodType.Getter)]
    private static void IncludePackEntry(MerchantInventory __instance, List<MerchantEntry> __result)
    {
        // The native setup assigns this entry an ID and hooks it into inventory updates on every peer.
        if (Entries.TryGetValue(__instance, out var entry))
            __result.Add(entry);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Initialize))]
    private static void SplitRemovalArea(NMerchantInventory __instance, MerchantInventory __0)
    {
        if (Slots.TryGetValue(__instance, out _)
            || !Entries.TryGetValue(__0, out var entry)
            || __instance.GetNodeOrNull<NMerchantCardRemoval>("%MerchantCardRemoval") is not { } removal
            || removal.GetParent() is not Control rug
            || removal.Hitbox is not { } hitbox
            || removal.GetNodeOrNull<Control>("Cost") is not { } cost
            || removal.GetNodeOrNull<Sprite2D>("%Visual") is not { } art)
        {
            return;
        }

        const float nativeHoverScale = 0.8f;
        const float bottomMargin = 20f;
        var removalScale = MathF.Sqrt(0.5f);
        var controls = hitbox.GetRect().Merge(cost.GetRect());
        var origin = removal.Position + controls.Position * removal.Scale;
        var bounds = controls.Merge(art.Transform * art.GetRect());
        // Grow the service column downward to keep clear of the card above it.
        var rowHeight = MathF.Min(bounds.Size.Y * nativeHoverScale * removalScale,
            (rug.Size.Y - origin.Y - bottomMargin) / 2f);
        var rowSize = new Vector2(controls.Size.X * removal.Scale.X, rowHeight);
        if (rowSize.X <= 0 || rowSize.Y <= 0)
            return;

        var upper = new Control
        {
            Name = "ThermalVortexCardRemovalRow",
            Position = origin,
            Size = rowSize / removalScale,
            Scale = Vector2.One * removalScale,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        var pack = new ExtraDeckPackMerchantSlot();
        pack.Configure(entry, new Rect2(origin + new Vector2(0f, rowHeight), rowSize));

        rug.AddChild(upper);
        // Keep the native slot's own scale and animation signals intact. Native binding runs after this prefix.
        removal.Reparent(upper, keepGlobalTransform: false);
        RestoreRemovalUnfocus(removal, hitbox);
        removal.Position = upper.Size / 2f - bounds.GetCenter() * nativeHoverScale;
        rug.AddChild(pack);
        Slots.Add(__instance, new ShopSlots(removal, pack));
    }

    private static void RestoreRemovalUnfocus(NMerchantCardRemoval removal, NClickableControl hitbox)
    {
        // The native slot disconnects these on tree exit. Reparenting does not run _Ready again.
        var method = AccessTools.Method(typeof(NMerchantSlot), "OnUnfocus");
        var unfocus = Callable.From(method.CreateDelegate<Action>(removal));
        if (!hitbox.IsConnected(Control.SignalName.MouseExited, unfocus))
            hitbox.Connect(Control.SignalName.MouseExited, unfocus);
        if (!removal.IsConnected(Control.SignalName.FocusExited, unfocus))
            removal.Connect(Control.SignalName.FocusExited, unfocus);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Initialize))]
    private static void FinishPackSlot(NMerchantInventory __instance)
    {
        if (Slots.TryGetValue(__instance, out var slots))
            slots.Pack.Bind(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Close))]
    private static void ClearRemovalTipOnClose(NMerchantInventory __instance)
    {
        if (Slots.TryGetValue(__instance, out var slots))
            NHoverTipSet.Remove(slots.Removal);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.GetAllSlots))]
    private static void IncludePackSlot(NMerchantInventory __instance, ref IEnumerable<NMerchantSlot> __result)
    {
        if (Slots.TryGetValue(__instance, out var slots))
            __result = __result.Append(slots.Pack);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NMerchantInventory), "UpdateNavigation")]
    private static void RestoreNativeNeighbors(NMerchantInventory __instance)
    {
        if (!Slots.TryGetValue(__instance, out var slots))
            return;

        slots.RestoreNeighbor?.Invoke();
        slots.RestoreNeighbor = null;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NMerchantInventory), "UpdateNavigation")]
    private static void ConnectPackNavigation(NMerchantInventory __instance)
    {
        if (!Slots.TryGetValue(__instance, out var slots) || !slots.Pack.Entry.IsStocked)
            return;

        var pack = slots.Pack;
        var origin = pack.GetGlobalRect().GetCenter();
        var neighbor = __instance.GetAllSlots()
            .Where(slot => slot != pack && slot != slots.Removal && slot.Entry?.IsStocked == true)
            .MinBy(slot => slot.GetGlobalRect().GetCenter().DistanceSquaredTo(origin));
        pack.FocusNeighborLeft = neighbor is null ? new NodePath("") : pack.GetPathTo(neighbor);
        pack.FocusNeighborTop = new NodePath("");

        if (slots.Removal.Entry?.IsStocked == true)
        {
            pack.FocusNeighborTop = pack.GetPathTo(slots.Removal);
            var previousBottom = slots.Removal.FocusNeighborBottom;
            slots.Removal.FocusNeighborBottom = slots.Removal.GetPathTo(pack);
            slots.RestoreNeighbor = () => slots.Removal.FocusNeighborBottom = previousBottom;
        }
        else if (neighbor is not null)
        {
            pack.FocusNeighborTop = pack.GetPathTo(neighbor);
            var previousRight = neighbor.FocusNeighborRight;
            neighbor.FocusNeighborRight = neighbor.GetPathTo(pack);
            slots.RestoreNeighbor = () => neighbor.FocusNeighborRight = previousRight;
        }
    }

    private sealed record ShopSlots(NMerchantCardRemoval Removal, ExtraDeckPackMerchantSlot Pack)
    {
        internal Action RestoreNeighbor { get; set; }
    }
}

internal sealed class ExtraDeckPackMerchantEntry : MerchantEntry
{
    private readonly ThermalVortexCore _core;
    private readonly MerchantRoom _room;
    private IReadOnlyList<CardModel> _offers;
    private bool _sold;

    internal ExtraDeckPackMerchantEntry(Player player, ThermalVortexCore core, MerchantRoom room) : base(player)
    {
        _core = core;
        _room = room;
        CalcCost();
    }

    public override bool IsStocked => !_sold;

    public override void CalcCost()
    {
        var baseCost = AscensionHelper.GetValueIfAscension(AscensionLevel.Inflation, 100, 75);
        var increase = AscensionHelper.GetValueIfAscension(AscensionLevel.Inflation, 50, 25);
        var cost = baseCost + (long)increase * _core.ExtraDeckPackPurchasesSave;
        _cost = (int)Math.Min(int.MaxValue, cost);
    }

    protected override async Task<(bool, int)> OnTryPurchaseInternal(MerchantInventory inventory, bool ignoreCost)
    {
        var cost = ignoreCost ? 0 : Cost;
        _offers ??= ExtraDeckPacks.Create(Player, _room, ExtraDeckPackSource.Merchant);
        if (_offers.Count == 0)
            return (false, 0);

        var chosen = await ExtraDeckPacks.Choose(Player, _offers);
        if (chosen is null
            || _sold
            || !ReferenceEquals(Player.RunState.CurrentRoom, _room)
            || !ReferenceEquals(Player.GetRelic<ThermalVortexCore>(), _core)
            || (!ignoreCost && Player.Gold < cost)
            || !_core.TryGrantOwnedExtraDeckCard(chosen))
        {
            return (false, 0);
        }

        // Commit stock and price history before native GoldChanged listeners refresh the slot.
        _sold = true;
        if (_core.ExtraDeckPackPurchasesSave < int.MaxValue)
            _core.ExtraDeckPackPurchasesSave++;
        if (cost > 0)
            await PlayerCmd.LoseGold(cost, Player, GoldLossType.Spent);
        return (true, cost);
    }

    protected override void ClearAfterPurchase() => _sold = true;

    protected override void RestockAfterPurchase(MerchantInventory inventory) => ClearAfterPurchase();
}

internal sealed partial class ExtraDeckPackMerchantSlot : NMerchantSlot
{
    private static readonly Color Cream = new("fff6e2");
    private static readonly Color Unaffordable = new("ff6565");
    private static readonly Color SoldOut = new("8b8272");
    private const string Art = "charui/extra_deck_pack.png";
    private const string GoldArt = "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_gold.tres";
    private const string MerchantFont = "res://themes/kreon_bold_glyph_space_two.tres";
    private const float PriceHeight = 26f;
    private const float ContentInset = 8f;

    private ExtraDeckPackMerchantEntry _entry;
    private Control _visual;
    private TextureRect _packArt;
    private HBoxContainer _priceRow;
    private MegaLabel _price;
    private Label _soldOut;

    public override MerchantEntry Entry => _entry;
    protected override CanvasItem Visual => _visual;
    protected override Vector2 SmallScale => Vector2.One;
    protected override Vector2 HoverScale => Vector2.One * 1.035f;
    public override Vector2 MerchantHandOffset => new(Size.X / 2f - 38f, (Size.Y - PriceHeight) / 2f);

    internal void Configure(ExtraDeckPackMerchantEntry entry, Rect2 area)
    {
        _entry = entry;
        Name = "ThermalVortexExtraDeckPack";
        Position = area.Position;
        Size = area.Size;
        PivotOffset = Size / 2f;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        var font = ResourceLoader.Load<Font>(MerchantFont);
        var iconPath = Art.ImagePath();
        _visual = new Control { Name = "Visual", Size = Size, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_visual);
        var priceTop = Size.Y - PriceHeight - ContentInset;
        var iconSize = MathF.Max(0f, MathF.Min(Size.X - 2f * ContentInset, priceTop - ContentInset));
        _packArt = new TextureRect
        {
            Position = new Vector2((Size.X - iconSize) / 2f, (priceTop - iconSize) / 2f),
            Size = new Vector2(iconSize, iconSize),
            Texture = ResourceLoader.Exists(iconPath) ? ResourceLoader.Load<Texture2D>(iconPath) : null,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _visual.AddChild(_packArt);

        _priceRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = new Vector2(0f, priceTop),
            Size = new Vector2(Size.X, PriceHeight)
        };
        _priceRow.AddThemeConstantOverride("separation", 3);
        _visual.AddChild(_priceRow);
        _priceRow.AddChild(new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(GoldArt),
            CustomMinimumSize = new Vector2(22f, 22f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        });
        _price = new MegaLabel { Name = "CostLabel", AutoSizeEnabled = false };
        StyleLabel(_price, font, 21);
        _priceRow.AddChild(_price);
        _price.Owner = this;
        _price.UniqueNameInOwner = true;

        _soldOut = new Label { Text = Text("soldOut").GetFormattedText(), Position = _priceRow.Position, Size = _priceRow.Size };
        StyleLabel(_soldOut, font, 18);
        _soldOut.Modulate = SoldOut;
        _visual.AddChild(_soldOut);

        var hitbox = new NClickableControl
        {
            Name = "Hitbox",
            Position = new Vector2(5f, 4f),
            Size = Size - new Vector2(10f, 8f),
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(hitbox);
        hitbox.Owner = this;
        hitbox.UniqueNameInOwner = true;
        ConnectSignals();
        UpdateVisual();
    }

    internal void Bind(NMerchantInventory inventory)
    {
        if (IsInitialized)
            return;

        Initialize(inventory);
        _entry.EntryUpdated += UpdateVisual;
        _entry.PurchaseFailed += OnPurchaseFailed;
    }

    public override void _ExitTree()
    {
        if (IsInitialized)
        {
            _entry.EntryUpdated -= UpdateVisual;
            _entry.PurchaseFailed -= OnPurchaseFailed;
        }
        base._ExitTree();
    }

    protected override void UpdateVisual()
    {
        var stocked = Entry.IsStocked;
        _priceRow.Visible = stocked;
        _soldOut.Visible = !stocked;
        _packArt.Modulate = stocked ? Colors.White : SoldOut;
        FocusMode = stocked ? FocusModeEnum.All : FocusModeEnum.None;
        Hitbox.MouseFilter = stocked ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        if (stocked)
        {
            _price.Text = Entry.Cost.ToString();
            _price.Modulate = Entry.EnoughGold ? Colors.White : Unaffordable;
        }
        else
        {
            ClearHoverTip();
        }
    }

    protected override void CreateHoverTip()
    {
        if (!Entry.IsStocked)
            return;

        var tip = new HoverTip(Text("title"), Text("description"), null);
        NHoverTipSet.CreateAndShow(this, tip, HoverTipAlignment.None)
            ?.SetAlignment(GetHoveredHitboxGlobalRect(), HoverTipAlignment.Left);
    }

    private static LocString Text(string key) => new("static_hover_tips", $"THERMALVORTEX-EXTRA_DECK_PACK.{key}");

    private static void StyleLabel(Label label, Font font, int size)
    {
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        if (font is not null)
            label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Cream);
        label.AddThemeColorOverride("font_outline_color", new Color("251815"));
        label.AddThemeConstantOverride("outline_size", 5);
    }
}
