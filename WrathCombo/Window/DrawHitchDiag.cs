using System;
using System.Diagnostics;
using System.Runtime;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using ECommons.Logging;
using WrathCombo.Services;

namespace WrathCombo.Window;

/// <summary>
///     介面繪製偶發卡頓的診斷：整個 WindowSystem 一幀超過 <see cref="ThresholdMs" /> 才寫一行 Information，
///     帶各段耗時、這一幀的 JIT 編譯與 GC 暫停。沒超過的幀只讀幾個計數器，不組字串、不配置。
/// </summary>
/// <remarks> 只在繪製執行緒呼叫；狀態是靜態欄位、不加鎖。 </remarks>
internal static class DrawHitchDiag
{
    internal const int ThresholdMs = 200;
    private const long LogIntervalMs = 60_000;

    internal enum Section
    {
        ConfigSidebar,
        ConfigBody,
        MajorChanges,
        TargetHelper,
        Count,
    }

    private static readonly long Threshold = Stopwatch.Frequency * ThresholdMs / 1000;
    private static readonly long[] SectionTicks = new long[(int)Section.Count];
    private static long _frameStart;
    private static TimeSpan _frameJit;
    private static TimeSpan _framePause;
    private static int _frameGen2;
    private static long _nextLogAt;
    private static int _skipped;

    internal static void BeginFrame()
    {
        Array.Clear(SectionTicks);
        _frameJit = JitInfo.GetCompilationTime(true);
        _framePause = GC.GetTotalPauseDuration();
        _frameGen2 = GC.CollectionCount(2);
        _frameStart = Stopwatch.GetTimestamp();
    }

    internal static long Begin() => Stopwatch.GetTimestamp();

    internal static void End(Section section, long start) =>
        SectionTicks[(int)section] += Stopwatch.GetTimestamp() - start;

    /// <returns>這一幀超過門檻；只有這時呼叫端才組上下文、呼叫 <see cref="Report" />。</returns>
    internal static bool EndFrame(out long elapsed)
    {
        elapsed = Stopwatch.GetTimestamp() - _frameStart;
        return elapsed >= Threshold;
    }

    internal static void Report(long elapsed, string windows)
    {
        var now = Environment.TickCount64;
        if (now < _nextLogAt)
        {
            _skipped++;
            return;
        }

        _nextLogAt = now + LogIntervalMs;
        var skipped = _skipped;
        _skipped = 0;

        // 診斷本身擲例外會讓 Dalamud 停掉整個插件的繪製，所以全包。
        try
        {
            WriteLine(elapsed, windows, skipped);
        }
        catch (Exception ex)
        {
            PluginLog.Information($"[DrawHitch] 介面一幀花了 {Ms(elapsed):0} 毫秒，細節取得失敗（{ex.GetType().Name}）");
        }
    }

    private static void WriteLine(long elapsed, string windows, int skipped)
    {
        var jit = (JitInfo.GetCompilationTime(true) - _frameJit).TotalMilliseconds;
        var pause = (GC.GetTotalPauseDuration() - _framePause).TotalMilliseconds;
        var gen2 = GC.CollectionCount(2) - _frameGen2;
        PluginLog.Information(
            $"[DrawHitch] 介面一幀花了 {Ms(elapsed):0} 毫秒（門檻 {ThresholdMs}）：" +
            $"設定視窗側欄 {Ms(SectionTicks[(int)Section.ConfigSidebar]):0}、" +
            $"設定視窗內容 {Ms(SectionTicks[(int)Section.ConfigBody]):0}、" +
            $"新版變更視窗 {Ms(SectionTicks[(int)Section.MajorChanges]):0}、" +
            $"隊伍卡片標示 {Ms(SectionTicks[(int)Section.TargetHelper]):0} 毫秒，其餘是視窗框架本身；" +
            $"這一幀 JIT 編譯 {jit:0} 毫秒、GC 暫停 {pause:0} 毫秒（第 2 代 {gen2} 次）；{windows}；" +
            $"職業 {(global::WrathCombo.WrathCombo.JobID?.ToString() ?? "?")}、戰鬥中 {(Svc.Condition[ConditionFlag.InCombat] ? "是" : "否")}、" +
            $"地區 {Svc.ClientState.TerritoryType}、開著的連擊 {Service.Configuration.EnabledActions.Count}" +
            (skipped > 0 ? $"（上一行之後另有 {skipped} 幀超過門檻未記錄）" : ""));
    }

    private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
