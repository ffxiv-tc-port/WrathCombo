using System;
using System.Collections.Generic;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using Serilog.Events;
using WrathCombo.Combos;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Data.Conflicts;
using WrathCombo.Services;

namespace WrathCombo.AutoRotation;

// 本檔假設與契約：
// - 只寫 log、不改行為；只在框架執行緒使用，所以不加鎖、不用 EzThrottler（全插件共用），節流自帶字典＋時間。
// - Information 關掉時（Dalamud 根層 LogLevelSwitch）只讀一次開關就返回，不記錄、不組字串。
// - 「手動」＝經過 UseAction hook、mode 不是 Queue、不在自動循環本體內、不是本插件自己送的；
//   其他插件直接呼叫 UseAction 也算手動，行尾的「BMR 佇列接管」等欄位讓讀 log 的人判斷。

/// <summary> 「手動按的技能被吃掉／插不進去」的 Information 診斷（每行以 <see cref="Tag" /> 開頭）。 </summary>
internal static class ManualPressDiag
{
    internal const string Tag = "[ManualAct]";

    private const long ThrottleMs = 30_000;
    private const long SuppressWindowMs = 300;
    private const long AutoRecentMs = 2_000;
    private const long ManualRecentMs = 3_000;

    private enum PathKind : byte { Replace, PerfReplace, Suppress, AutoFill, Direct, Clear, Hold }

    internal static bool Active =>
        Dalamud.EntryPoint.LogLevelSwitch.MinimumLevel <= LogEventLevel.Information;

    /// <summary> 本插件在自動循環本體以外自己送技能時設為 true（呼叫端負責還原）。 </summary>
    internal static bool OwnSendOutsideRotation;

    /// <summary> 自動循環裡不經 CanQueue 直接送技能的路徑名；其他時候為 null（<c>Run</c> 結束時清掉）。 </summary>
    internal static string? DirectPath;

    private static uint _lastAutoId;
    private static long _lastAutoAt;
    private static uint _lastManualId;
    private static long _lastManualAt;

    private static readonly Dictionary<uint, CustomComboPreset> ReplacedBy = [];
    private static readonly Dictionary<uint, (long At, uint Queued, uint Would, CustomComboPreset Preset)> Suppressed = [];
    private static readonly Dictionary<(PathKind, uint), long> LastLogged = [];

    internal readonly struct Press
    {
        internal readonly bool Valid;
        internal readonly bool Own;
        internal readonly ActionManager.UseActionMode Mode;
        internal readonly uint RequestedId;
        internal readonly uint SentId;
        internal readonly uint QueuedBefore;
        internal readonly CustomComboPreset? PerfPreset;

        internal Press(bool own, ActionManager.UseActionMode mode, uint requested, uint sent, uint queuedBefore,
            CustomComboPreset? perfPreset)
        {
            Valid = true;
            Own = own;
            Mode = mode;
            RequestedId = requested;
            SentId = sent;
            QueuedBefore = queuedBefore;
            PerfPreset = perfPreset;
        }
    }

    /// <summary> 快捷列替換成功時記下是哪個功能換的（給事後的「手動按的被換掉」那一行用）。 </summary>
    internal static void NoteReplaced(uint actionId, CustomComboPreset preset)
    {
        if (Active)
            ReplacedBy[actionId] = preset;
    }

    /// <summary> SuppressQueuedActions 讓某個功能放棄替換的那一刻。 </summary>
    internal static void NoteSuppressed(uint actionId, uint queued, uint would, CustomComboPreset preset)
    {
        if (Active)
            Suppressed[actionId] = (Environment.TickCount64, queued, would, preset);
    }

    internal static unsafe Press Begin(ActionManager* am, ActionManager.UseActionMode mode, uint requested, uint sent,
        CustomComboPreset? perfPreset)
    {
        try
        {
            var own = AutoRotationController.InRotation || OwnSendOutsideRotation;
            var queued = am->QueuedActionType == ActionType.Action ? am->QueuedActionId : 0;
            return new Press(own, mode, requested, sent, queued, perfPreset);
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
            return default;
        }
    }

    internal static unsafe void End(in Press p, ActionManager* am, bool ret)
    {
        try
        {
            var now = Environment.TickCount64;
            var queuedAfter = am->QueuedActionType == ActionType.Action ? am->QueuedActionId : 0;

            if (p.Own)
            {
                if (!AutoRotationController.InRotation)
                    return;

                if (p.QueuedBefore != 0 && p.QueuedBefore == _lastManualId && now - _lastManualAt <= ManualRecentMs &&
                    p.SentId != p.QueuedBefore)
                    Log(PathKind.Direct, p.QueuedBefore, now,
                        $"自動循環的「{DirectPath ?? "主循環"}」在佇列裡還有你 {now - _lastManualAt} ms 前手動按的 " +
                        $"{Name(p.QueuedBefore)} 時送出 {Name(p.SentId)}；送出後佇列＝{Name(queuedAfter)}，遊戲回 {ret}");

                _lastAutoId = p.SentId;
                _lastAutoAt = now;
                return;
            }

            if (p.Mode == ActionManager.UseActionMode.Queue)
                return;

            _lastManualId = p.RequestedId;
            _lastManualAt = now;
            var autoOn = AutoRotationController.cfg?.Enabled == true;

            if (autoOn && p.PerfPreset is { } perf && p.SentId != p.RequestedId)
                Log(PathKind.PerfReplace, p.RequestedId, now,
                    $"效能模式下按下的 {Name(p.RequestedId)} 被功能 {perf} 換成 {Name(p.SentId)}；遊戲回 {ret}");
            else if (autoOn && Service.Configuration.ActionChanging && !Service.Configuration.PerformanceMode &&
                     Service.ActionReplacer.LastActionInvokeFor.TryGetValue(p.RequestedId, out var adjusted) &&
                     adjusted != p.RequestedId && ReplacedBy.TryGetValue(p.RequestedId, out var by))
                Log(PathKind.Replace, p.RequestedId, now,
                    $"按下的 {Name(p.RequestedId)} 被功能 {by} 換成 {Name(adjusted)}（快捷列替換）；遊戲回 {ret}");

            if (Suppressed.TryGetValue(p.RequestedId, out var s) && now - s.At <= SuppressWindowMs)
                Log(PathKind.Suppress, p.RequestedId, now,
                    $"佇列裡已有 {Name(s.Queued)}，「佇列技能抑制」讓功能 {s.Preset} 放棄把 {Name(p.RequestedId)} " +
                    $"換成 {Name(s.Would)}，這次送出原技能；遊戲回 {ret}");

            if (autoOn && _lastAutoAt != 0 && now - _lastAutoAt <= AutoRecentMs &&
                ((p.QueuedBefore != 0 && p.QueuedBefore == _lastAutoId) || !ret))
                Log(PathKind.AutoFill, p.RequestedId, now,
                    $"手動按 {Name(p.RequestedId)} 時自動循環 {now - _lastAutoAt} ms 前剛送了 {Name(_lastAutoId)}；" +
                    $"按前佇列＝{Name(p.QueuedBefore)}，遊戲回 {ret}，按後佇列＝{Name(queuedAfter)}");
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    private static bool _failed;

    // 診斷本身出錯不可以影響出招；只記一次。
    private static void ReportFailure(Exception ex)
    {
        if (_failed)
            return;
        _failed = true;
        PluginLog.Warning($"{Tag} 診斷失敗，本次載入期間不再回報：{ex.GetType().Name}: {ex.Message}");
    }

    /// <summary> 復活／康復在重送同一招前清掉佇列的那一刻；佇列裡的是使用者剛手動按的才記。 </summary>
    internal static void NoteQueueCleared(string path, uint actionId)
    {
        if (!Active || actionId != _lastManualId)
            return;
        try
        {
            var now = Environment.TickCount64;
            if (now - _lastManualAt > ManualRecentMs)
                return;
            Log(PathKind.Clear, actionId, now,
                $"自動循環的「{path}」清掉了佇列裡你 {now - _lastManualAt} ms 前手動按的 {Name(actionId)}（它要自己重送同一招）");
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    /// <summary> 自動循環因為 BMR 佇列裡有玩家手動排的同群組技能而先不送的那一刻。 </summary>
    internal static void NoteManualHold(uint manualAction, uint heldAction, long ageMs)
    {
        if (!Active)
            return;
        try
        {
            Log(PathKind.Hold, manualAction, Environment.TickCount64,
                $"BMR 佇列裡有你 {ageMs} ms 前手動排的 {Name(manualAction)}，自動循環先不送同一冷卻群組的 {Name(heldAction)}（放出或過期後恢復）");
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    private static void Log(PathKind path, uint actionId, long now, string text)
    {
        var key = (path, actionId);
        if (LastLogged.TryGetValue(key, out var last) && now - last < ThrottleMs)
            return;
        LastLogged[key] = now;
        PluginLog.Information($"{Tag}[{path}] {text}｜{State()}");
    }

    private static string State()
    {
        var auto = AutoRotationController.cfg?.Enabled == true ? "開" : "關";
        var lease = P?.UIHelper?.AutoRotationStateControlled();
        var leaseText = lease is { } l ? $"{l.controllers}（{(l.state ? "開" : "關")}）" : "無";
        var bmr = ConflictingPluginsChecks.BossModReborn.ManualQueueTakeover ? "是" : "否";
        return $"自動循環＝{auto}，自動循環租約＝{leaseText}，BMR 佇列接管＝{bmr}，" +
               $"效能模式＝{(Service.Configuration.PerformanceMode ? "是" : "否")}，" +
               $"佇列技能抑制＝{(Service.Configuration.SuppressQueuedActions ? "開" : "關")}";
    }

    private static string Name(uint id) =>
        id == 0 ? "（空）" : $"{CustomComboFunctions.GetActionName(id)}#{id}";
}
