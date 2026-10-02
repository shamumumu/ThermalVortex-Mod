using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using System.Runtime.CompilerServices;
using ThermalVortex.ThermalVortexCode;
using ThermalVortexCharacter = ThermalVortex.ThermalVortexCode.Character.ThermalVortex;

namespace ThermalVortex.ThermalVortexCode.Patches;

[HarmonyPatch(typeof(NRunHistory), nameof(NRunHistory.GetDeathQuote))]
internal static class ThermalVortexScopedRunSummaryDeathQuotePatch
{
    [HarmonyPostfix]
    private static void ReplaceThermalVortexRunSummaryDeathQuote(
        RunHistory __0,
        ModelId __1,
        GameOverType __2,
        ref string __result)
    {
        if (ThermalVortexRunSummaryText.TryGetScopedRunSummaryQuote(__1, __2, out var quote))
            __result = quote;
    }
}

[HarmonyPatch(
    typeof(NGameOverScreen),
    nameof(NGameOverScreen.Create),
    [typeof(RunState), typeof(SerializableRun)])]
internal static class ThermalVortexGameOverScreenCreatePatch
{
    [HarmonyPostfix]
    private static void StoreThermalVortexGameOverQuote(SerializableRun __1, NGameOverScreen __result)
    {
        if (!ThermalVortexRunSummaryText.TryCreateGameOverContext(__1, out var context))
            return;

        ThermalVortexRunSummaryText.StoreGameOverContext(__result, context);
    }
}

[HarmonyPatch(typeof(NGameOverScreen), "InitializeBannerAndQuote")]
internal static class ThermalVortexGameOverScreenInitializeBannerAndQuotePatch
{
    [HarmonyPostfix]
    private static void ApplyThermalVortexInitialGameOverQuote(NGameOverScreen __instance)
    {
        ThermalVortexRunSummaryText.ApplyInitialGameOverQuote(__instance);
    }
}

[HarmonyPatch(typeof(NGameOverScreen), "OpenSummaryScreen")]
internal static class ThermalVortexGameOverScreenOpenSummaryPatch
{
    [HarmonyPrefix]
    private static void PrepareThermalVortexRunSummaryQuote(NGameOverScreen __instance)
    {
        ThermalVortexRunSummaryText.PrepareRunSummaryQuote(__instance);
    }

    [HarmonyPostfix]
    private static void ApplyThermalVortexVictoryRunSummaryQuoteImmediately(NGameOverScreen __instance)
    {
        ThermalVortexRunSummaryText.ApplyVictoryRunSummaryQuoteFallback(__instance);
    }
}

[HarmonyPatch(typeof(NGameOverScreen), "AnimateRunSummary")]
internal static class ThermalVortexGameOverScreenAnimateRunSummaryPatch
{
    [HarmonyPostfix]
    private static void ApplyThermalVortexVictoryRunSummaryQuoteAfterAnimation(NGameOverScreen __instance, ref Task __result)
    {
        __result = ThermalVortexRunSummaryText.ApplyVictoryRunSummaryQuoteAfterTask(__instance, __result);
    }
}

[HarmonyPatch(typeof(NAbandonRunConfirmPopup), "OnYesButtonPressed")]
internal static class ThermalVortexAbandonRunConfirmPatch
{
    [HarmonyPrefix]
    private static void TrackAbandonRequest()
    {
        ThermalVortexRunSummaryText.MarkAbandonRequested();
        MainFile.Logger.Info("RunSummary abandon request tracked.");
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded), [typeof(bool)])]
internal static class ThermalVortexRunEndedPatch
{
    [HarmonyPostfix]
    private static void TrackRunEnded(bool __0, SerializableRun __result)
    {
        ThermalVortexRunSummaryText.MarkRunEnded(__0, __result);
    }
}

internal static class ThermalVortexRunSummaryText
{
    private const string FullCharacterModelId = "CHARACTER.THERMALVORTEX-THERMAL_VORTEX";
    private const string FullCharacterId = "THERMALVORTEX-THERMAL_VORTEX";
    private const string FullCharacterEntry = "THERMAL_VORTEX";
    private const string FullCharacterCategory = "THERMALVORTEX";
    private const string DeathQuoteLabelName = "DeathQuoteLabel";
    private const float RunSummaryDeathQuoteY = 156f;
    private const int RunSummaryQuoteBaseLines = 2;
    private const int RunSummaryQuoteEstimatedCharsPerLine = 22;
    private const float RunSummaryQuoteLineOffsetY = 32f;
    private const float RunSummaryQuoteMaxOffsetY = 128f;
    private static readonly System.Reflection.FieldInfo EncounterQuoteField =
        AccessTools.Field(typeof(NGameOverScreen), "_encounterQuote");
    private static readonly System.Reflection.FieldInfo SummaryContainerField =
        AccessTools.Field(typeof(NGameOverScreen), "_summaryContainer");

    private static readonly string[] AbandonQuotes =
    [
        "你不难过，是因为我替你难过了。\n真残忍，不是么?",
        "你扮小丑扮得太久了，演得太入戏，都忘记自己了。",
        "我一直在等，等到绝望。",
        "你已手握刀剑，那么就准备战斗。"
    ];

    private static readonly string[] DefeatQuotes =
    [
        "请再多坚持一秒，等那个人一骑绝尘如狂风闪电般出现在你面前，\n你将跨上他的马背，即便他是被所罗门囚禁了一千年的魔鬼。",
        "明知道什么事情都不可能，还非要揣着希望，\n明明想把命都赌上，可是连下注的理由都没有。",
        "浮生梦，三生渺渺，因缘无踪，\n虽堪恋，何必重逢。\n息壤生生，谁当逝水，东流无终。",
        "命运这种东西，生来就是要被踏于足下的，\n如果你还未有力量反抗它，只需怀着勇气等待。"
    ];

    private static readonly string[] VictoryQuotes =
    [
        "汝必以眼　偿还僭越\n汝必以痛　偿还狂妄\n汝必以血　偿还背叛",
        "那一千年完了，\n撒旦必从监牢里被释放，出来要迷惑地上四方的列国，\n就是歌革和玛各，叫他们聚集争战。他们的人数多如海沙。",
        "在这混沌的世界里，我是那永不熄灭的火焰",
        "所谓弃族的命运，就是要穿越荒原，再次竖起战旗，返回家乡。",
        "或许是不知梦的缘故，流离之人追逐幻影。"
    ];

    private static readonly ConditionalWeakTable<NGameOverScreen, GameOverQuoteContext> GameOverContexts = new();
    private static readonly TimeSpan PendingAbandonWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PendingRunEndWindow = TimeSpan.FromMinutes(5);
    private static DateTime LastAbandonRequestedUtc = DateTime.MinValue;
    private static PendingRunEndConclusion PendingRunEnd;
    private static NGameOverScreen ActiveRunSummaryScreen;
    private static GameOverQuoteContext ActiveRunSummaryContext;

    internal static void ResetForRunTransition()
    {
        ClearPendingAbandon();
        ClearPendingRunEnd();
        ActiveRunSummaryScreen = null;
        ActiveRunSummaryContext = null;
    }

    internal static bool TryCreateGameOverContext(SerializableRun run, out GameOverQuoteContext context)
    {
        context = null;

        // Each game-over screen belongs to the local player, even in a mixed party.
        if (run is null || !IsThermalVortex(LocalContext.GetMe(run)?.CharacterId))
        {
            ClearPendingAbandon();
            ClearPendingRunEnd();
            return false;
        }

        var conclusion = GetConclusion(run, out var conclusionSource);
        context = new GameOverQuoteContext(PickQuote(conclusion), conclusion);
        MainFile.Logger.Info(
            $"RunSummary game_over quote start_time={run.StartTime} conclusion={conclusion} source={conclusionSource} win_time={run.WinTime}");
        return true;
    }

    internal static void MarkAbandonRequested()
    {
        LastAbandonRequestedUtc = DateTime.UtcNow;
    }

    internal static void MarkRunEnded(bool isVictory, SerializableRun run)
    {
        if (run is null || !IsThermalVortex(LocalContext.GetMe(run)?.CharacterId))
        {
            PendingRunEnd = null;
            return;
        }

        var conclusion = isVictory
            ? ThermalVortexRunConclusion.Victory
            : HasRecentPendingAbandon()
                ? ThermalVortexRunConclusion.Abandon
                : ThermalVortexRunConclusion.Defeat;

        if (PendingRunEnd is not null
            && PendingRunEnd.StartTime == run.StartTime
            && GetConclusionPriority(PendingRunEnd.Conclusion) > GetConclusionPriority(conclusion))
        {
            MainFile.Logger.Info(
                $"RunSummary run ended ignored start_time={run.StartTime} existing={PendingRunEnd.Conclusion} incoming={conclusion} is_victory={isVictory}");
            return;
        }

        PendingRunEnd = new PendingRunEndConclusion(run.StartTime, conclusion, DateTime.UtcNow);
        MainFile.Logger.Info(
            $"RunSummary run ended start_time={run.StartTime} is_victory={isVictory} conclusion={conclusion}");
    }

    internal static void StoreGameOverContext(NGameOverScreen screen, GameOverQuoteContext context)
    {
        if (screen == null || context == null)
            return;

        GameOverContexts.Remove(screen);
        GameOverContexts.Add(screen, context);
    }

    internal static void ApplyInitialGameOverQuote(NGameOverScreen screen)
    {
        if (screen == null || !GameOverContexts.TryGetValue(screen, out var context))
            return;

        CaptureOriginalEncounterQuote(screen, context);
        if (context.Conclusion == ThermalVortexRunConclusion.Victory)
        {
            MainFile.Logger.Info("GameOver initial quote left vanilla for victory.");
            return;
        }

        ApplyInitialDeathQuote(screen, context);
    }

    internal static void PrepareRunSummaryQuote(NGameOverScreen screen)
    {
        if (screen == null || !GameOverContexts.TryGetValue(screen, out var context))
            return;

        if (context.Conclusion != ThermalVortexRunConclusion.Victory)
        {
            RestoreOriginalEncounterQuote(screen, context);
            MainFile.Logger.Info($"RunSummary quote left vanilla conclusion={context.Conclusion}");
            return;
        }

        ActiveRunSummaryScreen = screen;
        ActiveRunSummaryContext = context;
        ApplyRunSummaryEncounterQuote(screen, context);
        MainFile.Logger.Info($"RunSummary quote scope begin conclusion={context.Conclusion}");
    }

    internal static bool TryGetScopedRunSummaryQuote(ModelId characterId, GameOverType gameOverType, out string quote)
    {
        quote = null;

        if (ActiveRunSummaryContext == null || !IsThermalVortex(characterId))
            return false;

        if (ActiveRunSummaryContext.Conclusion != ThermalVortexRunConclusion.Victory)
            return false;

        quote = ActiveRunSummaryContext.Quote;
        MainFile.Logger.Info(
            $"RunSummary scoped death quote applied conclusion={ActiveRunSummaryContext.Conclusion} game_over_type={gameOverType}");
        return true;
    }

    internal static async Task ApplyVictoryRunSummaryQuoteAfterTask(NGameOverScreen screen, Task originalTask)
    {
        try
        {
            if (originalTask != null)
                await originalTask;
        }
        finally
        {
            EndRunSummaryQuoteScope(screen);
        }
    }

    private static void ApplyRunSummaryEncounterQuote(NGameOverScreen screen, GameOverQuoteContext context)
    {
        ApplyEncounterQuote(screen, context.Quote, $"RunSummary encounter quote cached conclusion={context.Conclusion}");
    }

    private static void ApplyInitialDeathQuote(NGameOverScreen screen, GameOverQuoteContext context)
    {
        var replacementCount = 0;
        foreach (var label in FindNodes<RichTextLabel>(screen, DeathQuoteLabelName))
        {
            label.Text = context.Quote;
            label.Visible = true;
            replacementCount++;
        }

        foreach (var label in FindNodes<Label>(screen, DeathQuoteLabelName))
        {
            label.Text = context.Quote;
            label.Visible = true;
            replacementCount++;
        }

        MainFile.Logger.Info(
            $"GameOver initial quote applied conclusion={context.Conclusion} labels={replacementCount}");
    }

    private static void RestoreOriginalEncounterQuote(NGameOverScreen screen, GameOverQuoteContext context)
    {
        CaptureOriginalEncounterQuote(screen, context);
        if (!context.HasOriginalEncounterQuote)
            return;

        ApplyEncounterQuote(
            screen,
            context.OriginalEncounterQuote,
            $"RunSummary encounter quote restored conclusion={context.Conclusion}");
    }

    private static void CaptureOriginalEncounterQuote(NGameOverScreen screen, GameOverQuoteContext context)
    {
        if (EncounterQuoteField == null)
        {
            MainFile.Logger.Info("RunSummary encounter quote field missing.");
            return;
        }

        if (context.HasOriginalEncounterQuote)
            return;

        try
        {
            context.OriginalEncounterQuote = EncounterQuoteField.GetValue(screen) as string;
            context.HasOriginalEncounterQuote = true;
            MainFile.Logger.Info($"GameOver original encounter quote captured conclusion={context.Conclusion}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("GameOver original encounter quote capture failed: " + ex);
        }
    }

    private static void ApplyEncounterQuote(NGameOverScreen screen, string quote, string logMessage)
    {
        if (EncounterQuoteField == null)
        {
            MainFile.Logger.Info("GameOver encounter quote field missing.");
            return;
        }

        try
        {
            EncounterQuoteField.SetValue(screen, quote);
            MainFile.Logger.Info(logMessage);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("GameOver encounter quote set failed: " + ex);
        }
    }

    internal static void ApplyVictoryRunSummaryQuoteFallback(NGameOverScreen screen)
    {
        if (screen == null || !GameOverContexts.TryGetValue(screen, out var context))
            return;

        if (context.Conclusion != ThermalVortexRunConclusion.Victory)
            return;

        ApplyVictoryRunSummaryLayout(screen, context);

        var replacementCount = 0;
        foreach (var label in FindNodes<RichTextLabel>(screen, DeathQuoteLabelName))
        {
            ApplyVisibleRunSummaryQuote(label, context.Quote);
            replacementCount++;
        }

        foreach (var label in FindNodes<Label>(screen, DeathQuoteLabelName))
        {
            ApplyVisibleRunSummaryQuote(label, context.Quote);
            replacementCount++;
        }

        MainFile.Logger.Info(
            $"RunSummary fallback quote applied conclusion={context.Conclusion} labels={replacementCount}");
    }

    private static void ApplyVictoryRunSummaryLayout(NGameOverScreen screen, GameOverQuoteContext context)
    {
        var summaryContainer = GetSummaryContainer(screen);
        if (summaryContainer == null)
            return;

        CaptureOriginalSummaryPosition(summaryContainer, context);

        var offsetY = CalculateVictoryRunSummaryOffset(context.Quote);
        var target = context.OriginalSummaryPosition + new Vector2(0f, offsetY);
        summaryContainer.Position = target;
        MainFile.Logger.Info(
            $"RunSummary layout applied conclusion={context.Conclusion} lines={EstimateQuoteLineCount(context.Quote)} offset_y={offsetY} original_y={context.OriginalSummaryPosition.Y} target_y={target.Y}");
    }

    private static void ApplyVisibleRunSummaryQuote(RichTextLabel label, string quote)
    {
        label.Text = quote;
        label.VisibleRatio = 1f;
        ApplyRunSummaryDeathQuoteVisibility(label);
    }

    private static void ApplyVisibleRunSummaryQuote(Label label, string quote)
    {
        label.Text = quote;
        label.VisibleRatio = 1f;
        ApplyRunSummaryDeathQuoteVisibility(label);
    }

    private static void ApplyRunSummaryDeathQuoteVisibility(Control label)
    {
        label.Visible = true;
        label.Position = new Vector2(label.Position.X, RunSummaryDeathQuoteY);
        label.Modulate = new Color(label.Modulate.R, label.Modulate.G, label.Modulate.B, 1f);
    }

    private static Control GetSummaryContainer(NGameOverScreen screen)
    {
        if (SummaryContainerField == null)
        {
            MainFile.Logger.Info("RunSummary summary container field missing.");
            return null;
        }

        try
        {
            return SummaryContainerField.GetValue(screen) as Control;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info("RunSummary summary container get failed: " + ex);
            return null;
        }
    }

    private static void CaptureOriginalSummaryPosition(Control summaryContainer, GameOverQuoteContext context)
    {
        if (context.HasOriginalSummaryPosition)
            return;

        context.OriginalSummaryPosition = summaryContainer.Position;
        context.HasOriginalSummaryPosition = true;
    }

    private static float CalculateVictoryRunSummaryOffset(string quote)
    {
        var extraLines = Math.Max(0, EstimateQuoteLineCount(quote) - RunSummaryQuoteBaseLines);
        return Math.Min(RunSummaryQuoteMaxOffsetY, extraLines * RunSummaryQuoteLineOffsetY);
    }

    private static int EstimateQuoteLineCount(string quote)
    {
        if (string.IsNullOrWhiteSpace(quote))
            return 1;

        var lineCount = 0;
        foreach (var line in quote.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var length = Math.Max(1, line.Length);
            lineCount += Math.Max(1, (length + RunSummaryQuoteEstimatedCharsPerLine - 1) / RunSummaryQuoteEstimatedCharsPerLine);
        }

        return lineCount;
    }

    internal static void EndRunSummaryQuoteScope(NGameOverScreen screen)
    {
        if (ActiveRunSummaryScreen != screen)
            return;

        MainFile.Logger.Info($"RunSummary quote scope end conclusion={ActiveRunSummaryContext?.Conclusion}");
        ActiveRunSummaryScreen = null;
        ActiveRunSummaryContext = null;
    }

    private static bool IsThermalVortex(ModelId characterId)
    {
        if (characterId == null)
            return false;

        return IsThermalVortexId(characterId.Entry)
            || IsThermalVortexId(characterId.ToString())
            || (string.Equals(characterId.Category, FullCharacterCategory, StringComparison.OrdinalIgnoreCase)
                && string.Equals(characterId.Entry, FullCharacterEntry, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsThermalVortexId(string value)
    {
        return string.Equals(value, ThermalVortexCharacter.CharacterId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, FullCharacterModelId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, FullCharacterId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, FullCharacterEntry, StringComparison.OrdinalIgnoreCase);
    }

    private static ThermalVortexRunConclusion GetConclusion(SerializableRun run, out string source)
    {
        if (TryGetCurrentHistoryConclusion(run, out var historyConclusion))
        {
            ClearPendingRunEnd();
            ClearPendingAbandon();
            source = "history";
            return historyConclusion;
        }

        if (TryConsumePendingRunEnd(run, out var pendingConclusion))
        {
            source = "run_end";
            return pendingConclusion;
        }

        if (run.WinTime > 0)
        {
            ClearPendingAbandon();
            source = "win_time";
            return ThermalVortexRunConclusion.Victory;
        }

        if (ConsumePendingAbandon())
        {
            source = "abandon_request";
            return ThermalVortexRunConclusion.Abandon;
        }

        if (run.Players?.Any(player => player.CurrentHp <= 0) == true)
        {
            source = "current_hp";
            return ThermalVortexRunConclusion.Defeat;
        }

        source = "fallback";
        return ThermalVortexRunConclusion.Abandon;
    }

    private static bool TryGetCurrentHistoryConclusion(
        SerializableRun run,
        out ThermalVortexRunConclusion conclusion)
    {
        conclusion = default;

        var history = RunManager.Instance?.History;
        if (run == null || history == null || history.StartTime != run.StartTime)
            return false;

        if (history.WasAbandoned)
        {
            conclusion = ThermalVortexRunConclusion.Abandon;
            return true;
        }

        if (history.Win)
        {
            conclusion = ThermalVortexRunConclusion.Victory;
            return true;
        }

        conclusion = ThermalVortexRunConclusion.Defeat;
        return true;
    }

    private static string PickQuote(ThermalVortexRunConclusion conclusion)
    {
        var quotes = conclusion switch
        {
            ThermalVortexRunConclusion.Abandon => AbandonQuotes,
            ThermalVortexRunConclusion.Defeat => DefeatQuotes,
            ThermalVortexRunConclusion.Victory => VictoryQuotes,
            _ => DefeatQuotes
        };

        return quotes[Random.Shared.Next(quotes.Length)];
    }

    private static int GetConclusionPriority(ThermalVortexRunConclusion conclusion)
    {
        return conclusion switch
        {
            ThermalVortexRunConclusion.Victory => 3,
            ThermalVortexRunConclusion.Abandon => 2,
            ThermalVortexRunConclusion.Defeat => 1,
            _ => 0
        };
    }

    private static void ClearPendingAbandon()
    {
        LastAbandonRequestedUtc = DateTime.MinValue;
    }

    private static void ClearPendingRunEnd()
    {
        PendingRunEnd = null;
    }

    private static bool TryConsumePendingRunEnd(
        SerializableRun run,
        out ThermalVortexRunConclusion conclusion)
    {
        conclusion = default;

        if (PendingRunEnd is null)
            return false;

        var isRecent = DateTime.UtcNow - PendingRunEnd.CreatedUtc <= PendingRunEndWindow;
        var isSameRun = run != null && PendingRunEnd.StartTime == run.StartTime;
        if (!isRecent || !isSameRun)
        {
            ClearPendingRunEnd();
            return false;
        }

        conclusion = PendingRunEnd.Conclusion;
        ClearPendingRunEnd();
        ClearPendingAbandon();
        return true;
    }

    private static bool ConsumePendingAbandon()
    {
        if (LastAbandonRequestedUtc == DateTime.MinValue)
            return false;

        var isRecent = DateTime.UtcNow - LastAbandonRequestedUtc <= PendingAbandonWindow;
        ClearPendingAbandon();
        return isRecent;
    }

    private static bool HasRecentPendingAbandon()
    {
        return LastAbandonRequestedUtc != DateTime.MinValue
            && DateTime.UtcNow - LastAbandonRequestedUtc <= PendingAbandonWindow;
    }

    private static IEnumerable<T> FindNodes<T>(Node root, string name)
        where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T node && string.Equals(node.Name, name, StringComparison.Ordinal))
                yield return node;

            if (child is Node childNode)
            {
                foreach (var descendant in FindNodes<T>(childNode, name))
                    yield return descendant;
            }
        }
    }

    internal sealed class GameOverQuoteContext(string quote, ThermalVortexRunConclusion conclusion)
    {
        public string Quote { get; } = quote;
        public ThermalVortexRunConclusion Conclusion { get; } = conclusion;
        public string OriginalEncounterQuote { get; set; }
        public bool HasOriginalEncounterQuote { get; set; }
        public Vector2 OriginalSummaryPosition { get; set; }
        public bool HasOriginalSummaryPosition { get; set; }
    }

    private sealed class PendingRunEndConclusion(
        long startTime,
        ThermalVortexRunConclusion conclusion,
        DateTime createdUtc)
    {
        public long StartTime { get; } = startTime;
        public ThermalVortexRunConclusion Conclusion { get; } = conclusion;
        public DateTime CreatedUtc { get; } = createdUtc;
    }
}

internal enum ThermalVortexRunConclusion
{
    Abandon,
    Defeat,
    Victory
}
