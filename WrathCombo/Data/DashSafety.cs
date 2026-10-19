#region

using System;
using System.Collections.Generic;
using Dalamud.Plugin.Ipc.Exceptions;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using WrathCombo.AutoRotation;
using WrathCombo.Combos;
using WrathCombo.Data.Conflicts;
using WrathCombo.Extensions;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     自動循環放位移技之前先問 BossMod(Reborn) 落點安不安全；判危險就不放，讓連段改選下一招。
/// </summary>
/// <remarks>
///     只問「自動循環正在跑的預設」。按鈕替換路徑對同一個預設也要問：掛鉤開著時自動循環送的是原始技能，
///     實際放出的那一招是 <c>GetAdjustedActionId</c> 掛鉤當下算出來的。
///     只在框架執行緒問（對方在別的執行緒會走 5 秒閘門）；同一幀同一組（技能，目標）只問一次。
///     不知道、端點不在、任何例外 ⇒ 照現行放。
/// </remarks>
internal static class DashSafety
{
    // 對方契約，數值不可改。
    internal const int Unknown = 0, Safe = 1, Dangerous = 2;

    // 本端自用：端點不在／呼叫失敗（都照現行放）。
    internal const int Missing = -1, Failed = -2;

    private const long MissingBackoffMs = 5_000;
    private const long FailedBackoffMs = 60_000;
    private const long BlockedLogIntervalMs = 5_000;
    private const long MissingLogIntervalMs = 600_000;
    private const long FailedLogIntervalMs = 60_000;

    /// <summary>正在跑的連段預設；只在 <c>CustomCombo.TryInvoke</c> 執行 <c>Invoke</c> 的期間有值。</summary>
    [ThreadStatic]
    internal static CustomComboPreset? InvokingPreset;

    /// <summary>自動循環指定的目標（<c>TryInvoke</c> 的 targetOverride）；null＝沒指定、用目前目標。</summary>
    [ThreadStatic]
    internal static ulong? InvokingTargetId;

    /// <summary>自動循環最近一輪的預設（唯讀共用；只在框架執行緒寫入與讀取）。</summary>
    internal static Dictionary<CustomComboPreset, bool>? AutoPresets;

    private const int CacheSize = 8;
    private static readonly uint[] CacheAction = new uint[CacheSize];
    private static readonly ulong[] CacheTarget = new ulong[CacheSize];
    private static readonly int[] CacheAnswer = new int[CacheSize];
    private static DateTime _cacheFrame = DateTime.MinValue;
    private static int _cacheCount;

    private static long _queryBlockedUntil;

    // 鍵：技能 id（判危險）或負數（Missing／Failed）。
    private static readonly Dictionary<long, long> LogNextAt = [];
    private static readonly object LogGate = new();

    /// <summary>
    ///     這一發位移技現在能不能放（目標＝自動循環指定的目標，沒指定就是目前目標）。只有自動循環中、BossMod(Reborn) 回「危險」時為 false。
    /// </summary>
    /// <param name="actionId">實際會送出的技能 id（替換後的）。</param>
    internal static unsafe bool Allowed(uint actionId)
    {
        if (!IsAutoContext(AutoRotationController.cfg?.Enabled == true, InvokingPreset, AutoPresets) ||
            !IsDash(actionId) ||
            !Svc.Framework.IsInFrameworkUpdateThread)
            return true;

        var query = ResolveQuery();
        if (query is null)
            return true;

        // 與 Svc.Targets.Target?.GameObjectId 同值，但不建包裝（同一幀會被問好幾次）。
        var hard = InvokingTargetId is null ? TargetSystem.Instance()->GetHardTarget() : null;
        var targetId = InvokingTargetId ?? (hard == null ? 0UL : (ulong)hard->GetGameObjectId());
        var now = Environment.TickCount64;
        var answer = Evaluate(actionId, targetId, query, Svc.Framework.LastUpdate, now, out var error);

        if (answer == Dangerous)
        {
            if (ShouldLog(actionId, BlockedLogIntervalMs, now))
                PluginLog.Information(
                    $"[DashSafety] 自動循環：BossMod 判「{actionId.ActionName()}」({actionId}) 的落點危險" +
                    $"（目標：{Svc.Objects.SearchById(targetId)?.Name.TextValue ?? "無"} 0x{targetId:X}），這一發不放、改選下一招。" +
                    $"{BlockedLogIntervalMs / 1000} 秒內同招不再記。");
            return false;
        }

        LogUnavailable(answer, error, now);
        return true;
    }

    /// <summary>
    ///     三態查詢：回 <see cref="Unknown" />／<see cref="Safe" />／<see cref="Dangerous" />／<see cref="Missing" />／<see cref="Failed" />，
    ///     與 <see cref="Allowed" /> 共用當幀快取、退避與診斷節流。不看自動循環情境，由呼叫端判。
    /// </summary>
    /// <remarks>不在框架執行緒時回 <see cref="Unknown" />，不解析端點、不呼叫 IPC。</remarks>
    internal static int Landing(uint actionId, ulong targetId)
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread)
            return Unknown;

        var query = ResolveQuery();
        if (query is null)
            return Unknown;

        var now = Environment.TickCount64;
        var answer = Evaluate(actionId, targetId, query, Svc.Framework.LastUpdate, now, out var error);
        LogUnavailable(answer, error, now);
        return answer;
    }

    /// <summary>自動循環開著、而且正在跑的連段預設在自動循環清單裡。只在框架執行緒讀。</summary>
    internal static bool InAutoContext =>
        IsAutoContext(AutoRotationController.cfg?.Enabled == true, InvokingPreset, AutoPresets);

    private static void LogUnavailable(int answer, Exception? error, long now)
    {
        if (answer == Missing && ShouldLog(Missing, MissingLogIntervalMs, now))
            PluginLog.Information(
                "[DashSafety] BossMod(Reborn) 沒有 BossMod.Hints.IsActionDashSafe（版本較舊？），" +
                "自動循環的位移技照原本的條件放。");
        else if (answer == Failed && ShouldLog(Failed, FailedLogIntervalMs, now))
            PluginLog.Information(
                $"[DashSafety] 呼叫 BossMod.Hints.IsActionDashSafe 失敗，{FailedBackoffMs / 1000} 秒內視同" +
                $"「不知道」、位移技照原本的條件放：{error?.ToStringFull()}");
    }

    internal static bool IsAutoContext(bool autoRotationEnabled, CustomComboPreset? invoking,
        Dictionary<CustomComboPreset, bool>? autoPresets) =>
        autoRotationEnabled && invoking is { } preset && autoPresets is not null &&
        autoPresets.TryGetValue(preset, out var on) && on;

    // 以遊戲資料的 AffectsPosition 判；對方對沒登記的技能回 0，多問只是多一次呼叫。
    private static bool IsDash(uint actionId) =>
        ActionWatching.ActionSheet.TryGetValue(actionId, out var row) && row.AffectsPosition;

    // BossModReborn 優先、沒裝才退回原版 BossMod（同 MechanicHints）。
    private static Func<uint, ulong, int>? ResolveQuery()
    {
        var reborn = ConflictingPluginsChecks.BossModReborn;
        if (reborn.IpcAvailable)
            return reborn.DashSafeQuery;
        var original = ConflictingPluginsChecks.BossMod;
        return original.IpcAvailable ? original.DashSafeQuery : null;
    }

    /// <summary>
    ///     帶當幀快取與退避的查詢本體；回 <see cref="Unknown" />／<see cref="Safe" />／<see cref="Dangerous" />／
    ///     <see cref="Missing" />／<see cref="Failed" />。契約外的回傳值一律當 <see cref="Unknown" />。
    /// </summary>
    /// <remarks>只在框架執行緒呼叫。退避期間回 <see cref="Unknown" /> 且不呼叫 <paramref name="query" />。</remarks>
    internal static int Evaluate(uint actionId, ulong targetId, Func<uint, ulong, int> query,
        DateTime frame, long now, out Exception? error)
    {
        error = null;
        if (frame != _cacheFrame)
        {
            _cacheFrame = frame;
            _cacheCount = 0;
        }

        for (var i = 0; i < _cacheCount; i++)
            if (CacheAction[i] == actionId && CacheTarget[i] == targetId)
                return CacheAnswer[i];

        int answer;
        if (now < _queryBlockedUntil)
            answer = Unknown;
        else
            try
            {
                answer = query(actionId, targetId);
                if (answer is not (Unknown or Safe or Dangerous))
                    answer = Unknown;
            }
            catch (IpcNotReadyError e)
            {
                answer = Missing;
                error = e;
                _queryBlockedUntil = now + MissingBackoffMs;
            }
            catch (Exception e)
            {
                // 型別不合（IpcTypeMismatchError 等）不是 IpcNotReadyError，ECommons 的 TryInvoke 攔不到。
                answer = Failed;
                error = e;
                _queryBlockedUntil = now + FailedBackoffMs;
            }

        if (_cacheCount < CacheSize)
        {
            CacheAction[_cacheCount] = actionId;
            CacheTarget[_cacheCount] = targetId;
            CacheAnswer[_cacheCount] = answer;
            _cacheCount++;
        }

        return answer;
    }

    internal static bool ShouldLog(long key, long intervalMs, long now)
    {
        lock (LogGate)
        {
            if (LogNextAt.TryGetValue(key, out var next) && now < next)
                return false;
            LogNextAt[key] = now + intervalMs;
            return true;
        }
    }
}
