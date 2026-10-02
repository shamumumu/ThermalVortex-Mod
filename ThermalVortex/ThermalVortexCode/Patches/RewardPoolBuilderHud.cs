using System.Reflection;
using Godot;

namespace ThermalVortex.ThermalVortexCode.Patches;

internal sealed partial class RewardPoolBuilderSession
{
    private const int CoveredDpsHudLayer = -1;
    private CanvasLayer _coveredDpsHud;
    private int _dpsHudOriginalLayer;

    private void OnBuilderVisibilityChanged() =>
        TryCleanup("update DPS HUD overlap", UpdateDpsHudOcclusion);

    private void UpdateDpsHudOcclusion()
    {
        if (_disposed || !IsValid(Root) || !Root.IsInsideTree() || !Root.IsVisibleInTree())
        {
            RestoreDpsHudLayer();
            return;
        }
        if (IsValid(_coveredDpsHud)) return;

        // The optional DPS mod owns a separate canvas above native menus.
        // Only borrow its exact existing instance; never load or initialize it.
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => candidate.GetName().Name == "sts2dps");
        var entry = assembly?.GetType("Sts2Dps.ModEntry", throwOnError: false);
        var ui = entry?.GetField("_ui", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        if (ui?.GetType().FullName != "Sts2Dps.ModUI") return;
        var canvas = ui.GetType().GetField("_canvas", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ui) as CanvasLayer;
        if (!IsValid(canvas) || !canvas.IsInsideTree()
            || !ReferenceEquals(canvas.GetParent(), Root.GetTree().Root)
            || !ReferenceEquals(canvas.GetViewport(), Root.GetViewport())
            || canvas.Layer <= CoveredDpsHudLayer)
            return;

        _coveredDpsHud = canvas;
        _dpsHudOriginalLayer = canvas.Layer;
        // Keep visibility, expanded state, mouse filters and saved settings intact.
        // A temporary layer beneath the native canvas also preserves card inspection.
        canvas.Layer = CoveredDpsHudLayer;
    }

    private void RestoreDpsHudLayer()
    {
        var canvas = _coveredDpsHud;
        var originalLayer = _dpsHudOriginalLayer;
        _coveredDpsHud = null;
        // A replacement node or another owner's later layer change is not ours.
        if (IsValid(canvas) && canvas.Layer == CoveredDpsHudLayer)
            canvas.Layer = originalLayer;
    }
}
