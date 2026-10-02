using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Pooling;
using ThermalVortex.ThermalVortexCode.Cards;

namespace ThermalVortex.ThermalVortexCode.Vfx;

/// <summary>Owns a native, fully labelled result card rendered off screen.</summary>
internal sealed class FusionResultCardCapture : IDisposable
{
    private static readonly Vector2I CaptureSize = new(680, 900);
    private SubViewport _viewport;
    private World2D _world;
    private NCard _card;

    // The caller borrows this texture until Dispose, after the presentation stops.
    internal Texture2D Texture { get; private set; }

    internal static async Task<FusionResultCardCapture> CreateAsync(
        SceneTree tree,
        Node owner,
        XyzMonsterCard summon,
        Func<bool> combatStillActive)
    {
        bool CanCapture() => GodotObject.IsInstanceValid(tree)
            && GodotObject.IsInstanceValid(owner) && owner.IsInsideTree()
            && !owner.IsQueuedForDeletion() && combatStillActive();

        if (summon is null || !CanCapture())
            return null;

        var capture = new FusionResultCardCapture();
        try
        {
            capture._world = new World2D();
            capture._viewport = new SubViewport
            {
                Name = "FusionNativeResultCard",
                Size = CaptureSize,
                TransparentBg = true,
                Disable3D = true,
                GuiDisableInput = true,
                World2D = capture._world,
                RenderTargetClearMode = SubViewport.ClearMode.Always,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            owner.AddChild(capture._viewport);

            // NCard owns its own model subscriptions. It does not replace a hand
            // or play-queue node; using the actual result preserves material-based
            // dynamic values, upgrades, costs and the normal localized card text.
            capture._card = NCard.Create(summon, ModelVisibility.Visible)
                ?? throw new InvalidOperationException("Native result card was unavailable.");
            var card = capture._card;
            card.MouseFilter = Control.MouseFilterEnum.Ignore;
            card.FocusMode = Control.FocusModeEnum.None;
            card.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
            capture._viewport.AddChild(card);
            card.SetPretendCardCanBePlayed(true);
            card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            card.CardHighlight?.AnimHideInstantly();
            card.KillRarityGlow();
            card.Body.Position = Vector2.Zero;
            card.Body.Rotation = 0f;
            card.Body.Scale = Vector2.One;
            card.Scale = Vector2.One;
            card.Rotation = 0f;
            card.Modulate = Colors.White;
            card.Visible = true;

            // The native body is centred on (0, 0). Leave room for the energy
            // badge above its frame, and include optional star/enchantment badges.
            var bounds = new Rect2(-170f, -235f, 340f, 470f);
            foreach (var path in new[] { "%StarIcon", "%Enchantment" })
            {
                var badge = card.GetNodeOrNull<Control>(path);
                if (GodotObject.IsInstanceValid(badge) && badge.Visible)
                    bounds = bounds.Merge(badge.GetRect());
            }
            var available = (Vector2)CaptureSize - new Vector2(16f, 16f);
            var scale = Math.Min(available.X / bounds.Size.X, available.Y / bounds.Size.Y);
            card.Scale = Vector2.One * scale;
            card.Position = (Vector2)CaptureSize * 0.5f - bounds.GetCenter() * scale;

            // Native text performs deferred sizing. Two completed render frames
            // include that layout without assuming a fixed display frame rate.
            for (var frame = 0; frame < 2; frame++)
            {
                if (!CanCapture()
                    || !await WaitForDrawAsync(tree, capture._viewport, CanCapture))
                {
                    capture.Dispose();
                    return null;
                }
            }
            if (!CanCapture())
            {
                capture.Dispose();
                return null;
            }

            capture.Texture = capture._viewport.GetTexture();
            capture._viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            return capture;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"Fusion native result card capture failed card={summon.GetType().Name}: {ex}");
            capture.Dispose();
            return null;
        }
    }

    private static async Task<bool> WaitForDrawAsync(
        SceneTree tree, Node owner, Func<bool> canCapture)
    {
        if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree() || !canCapture())
            return false;

        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Drawn() => completed.TrySetResult(true);
        void Exiting() => completed.TrySetResult(false);
        void CheckCombat()
        {
            if (!canCapture())
                completed.TrySetResult(false);
        }

        RenderingServer.FramePostDraw += Drawn;
        tree.ProcessFrame += CheckCombat;
        owner.TreeExiting += Exiting;
        try
        {
            return await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        catch (TimeoutException)
        {
            MainFile.Logger.Info("Fusion native result card capture skipped: render frame timed out.");
            return false;
        }
        finally
        {
            RenderingServer.FramePostDraw -= Drawn;
            if (GodotObject.IsInstanceValid(tree))
                tree.ProcessFrame -= CheckCombat;
            if (GodotObject.IsInstanceValid(owner))
                owner.TreeExiting -= Exiting;
        }
    }

    public void Dispose()
    {
        Texture = null;
        var viewport = _viewport;
        _viewport = null;
        if (GodotObject.IsInstanceValid(viewport))
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

        var card = _card;
        _card = null;
        if (GodotObject.IsInstanceValid(card) && !card.IsQueuedForDeletion())
        {
            try
            {
                // Removal invokes native _ExitTree to unsubscribe from the model.
                card.GetParent()?.RemoveChild(card);
                NodePool.Free(card);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info("Fusion native result card cleanup failed: " + ex.Message);
                if (GodotObject.IsInstanceValid(card) && !card.IsQueuedForDeletion())
                    card.QueueFree();
            }
        }
        if (GodotObject.IsInstanceValid(viewport) && !viewport.IsQueuedForDeletion())
        {
            if (viewport.IsInsideTree())
                viewport.QueueFree();
            else
                viewport.Free();
        }
        _world?.Dispose();
        _world = null;
    }
}
