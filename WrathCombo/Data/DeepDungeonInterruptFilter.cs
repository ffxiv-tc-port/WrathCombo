#region

using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc.Exceptions;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using WrathCombo.Combos.PvE;
using WrathCombo.Core;
using WrathCombo.Data.Conflicts;
using WrathCombo.Services;
using static WrathCombo.CustomComboNS.Functions.CustomComboFunctions;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     深層迷宮非首領層：打斷技只打 BossMod(Reborn) 標記為該打斷的詠唱；遠程打斷可改打被標記的施放者。
/// </summary>
/// <remarks>
///     只在框架執行緒、設定開啟、身在深層迷宮且樓層號不是 10 的倍數時生效；其餘一律照舊、不碰 IPC。
///     BMR 不在、端點沒註冊、型別不合或擲任何例外 ⇒ 照舊全打，並退避一段時間不再呼叫。
///     BMR 有回答時以它為準：回空清單＝現在沒有該打斷的詠唱，打斷技保留。
///     改打只換這一招送出的目標（技能重定向或自動循環的 UseAction 參數），不改玩家選中的目標。
/// </remarks>
internal static class DeepDungeonInterruptFilter
{
    internal const long MissingBackoffMs = 5_000;
    internal const long FailedBackoffMs = 60_000;
    private const long PerTargetLogIntervalMs = 10_000;
    private const long MissingLogIntervalMs = 600_000;
    private const long FailedLogIntervalMs = 60_000;

    private static DateTime _frame = DateTime.MinValue;
    private static bool _primed;
    private static ulong[]? _flagged; // null＝本幀不過濾（照舊）
    private static bool _casterResolved;
    private static uint _casterEntityId; // 0＝本幀沒有可改打的施放者；跨幀只留 id，不留物件

    private static long _queryBlockedUntil;
    private static long _missingLogNextAt;
    private static long _failedLogNextAt;
    private static readonly Dictionary<ulong, long> SkipLogNextAt = [];
    private static readonly Dictionary<ulong, long> RetargetLogNextAt = [];

    private static bool RetargetEnabled =>
        Service.Configuration.DeepDungeonSelectiveInterrupt &&
        Service.Configuration.DeepDungeonInterruptRetarget;

    /// <summary> 打斷技可不可以打 <paramref name="target" />；只在它已通過可打斷與詠唱進度判斷之後問。 </summary>
    internal static bool Allows(IGameObject target)
    {
        var flagged = Flagged();
        if (flagged is null || MechanicHints.IsFlagged(flagged, target))
            return true;

        if (ShouldLog(SkipLogNextAt, target.GameObjectId))
        {
            var cast = target is IBattleChara c ? c.CastActionId : 0;
            PluginLog.Information(
                $"[深宮打斷] BossMod 沒有把「{target.Name.TextValue}」(0x{target.GameObjectId:X}) 的詠唱 {cast} 標記為該打斷，" +
                $"打斷技保留；同一目標 {PerTargetLogIntervalMs / 1000} 秒內不再記。");
        }
        return false;
    }

    /// <summary>
    ///     遠程打斷可改打的施放者：被 BMR 標記、可打斷、射程與視線內、不是目前目標；沒有就回 null。
    /// </summary>
    /// <remarks> 回傳的是共用包裝，只可在同一幀內當場使用。 </remarks>
    internal static IBattleChara? RetargetCaster(uint actionId)
    {
        if (actionId != RoleActions.PhysRanged.HeadGraze || !RetargetEnabled)
            return null;
        var flagged = Flagged();
        if (flagged is null || flagged.Length == 0)
            return null;

        if (!_casterResolved)
        {
            _casterResolved = true;
            _casterEntityId = FindCaster(flagged, actionId);
        }

        return _casterEntityId == 0 ? null : Svc.Objects.SearchByEntityId(_casterEntityId) as IBattleChara;
    }

    /// <summary> 自動循環送 <paramref name="actionId" /> 時要改打的施放者；目前目標本身就該打時回 null。 </summary>
    internal static IBattleChara? ActionTargetOverride(uint actionId)
    {
        if (actionId != RoleActions.PhysRanged.HeadGraze || !RetargetEnabled || CanInterruptEnemy() ||
            RetargetCaster(actionId) is not { } caster)
            return null;
        LogRetarget(caster);
        return caster;
    }

    /// <summary> 連段回傳打斷技、而該打的是別隻時，登記技能重定向（玩家按鍵走這條；自動循環另外直接帶目標）。 </summary>
    internal static void OnComboResult(uint replacedAction, uint result)
    {
        if (result != RoleActions.PhysRanged.HeadGraze || !RetargetEnabled || CanInterruptEnemy() ||
            RetargetCaster(result) is not { } caster)
            return;
        result.Retarget(replacedAction, new CasterResolver(caster.EntityId).Resolve);
    }

    private sealed class CasterResolver(uint entityId)
    {
        [ActionRetargeting.TargetResolver]
        public IGameObject? Resolve()
        {
            var caster = Svc.Objects.SearchByEntityId(entityId);
            if (caster is IBattleChara c)
                LogRetarget(c);
            return caster;
        }
    }

    private static ulong[]? Flagged()
    {
        if (!Service.Configuration.DeepDungeonSelectiveInterrupt || !Svc.Framework.IsInFrameworkUpdateThread)
            return null;

        var frame = Svc.Framework.LastUpdate;
        if (_primed && frame == _frame)
            return _flagged;
        _primed = true;
        _frame = frame;
        _casterResolved = false;
        _casterEntityId = 0;
        _flagged = Svc.Condition[ConditionFlag.InDeepDungeon] && OnNormalFloor()
            ? Query(Environment.TickCount64)
            : null;
        return _flagged;
    }

    // 樓層讀不到（換層中、全域槽是空的）＝當成不在一般層，照舊全打。
    // Instance() 特徵碼解不出時擲受管例外而不是回 null，所以要 catch。
    private static unsafe bool OnNormalFloor()
    {
        try
        {
            var ef = EventFramework.Instance();
            var dd = ef != null ? ef->GetInstanceContentDeepDungeon() : null;
            return dd != null && dd->Floor != 0 && dd->Floor % 10 != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ulong[]? Query(long now)
    {
        var query = ResolveQuery();
        if (query is null)
        {
            LogMissing(now);
            return null;
        }

        var flagged = Evaluate(query, now, out var outcome, out var error);
        if (outcome == Missing)
            LogMissing(now);
        else if (outcome == Failed && now >= _failedLogNextAt)
        {
            _failedLogNextAt = now + FailedLogIntervalMs;
            PluginLog.Information(
                $"[深宮打斷] 呼叫 BossMod.Hints.ShouldInterruptTargets 失敗，{FailedBackoffMs / 1000} 秒內照舊全打：{error?.ToStringFull()}");
        }
        return flagged;
    }

    // Evaluate 的結果分類（只給診斷與量測台用）。
    internal const int Answered = 1, BackedOff = 4, Missing = -1, Failed = -2;

    /// <summary> 帶退避的查詢本體：回 null＝這次不過濾（照舊全打），非 null＝BMR 的回答（可為空）。 </summary>
    /// <remarks> 只在框架執行緒呼叫。退避期間回 null 且不呼叫 <paramref name="query" />。 </remarks>
    internal static ulong[]? Evaluate(Func<ulong[]> query, long now, out int outcome, out Exception? error)
    {
        error = null;
        if (now < _queryBlockedUntil)
        {
            outcome = BackedOff;
            return null;
        }

        try
        {
            var flagged = query() ?? [];
            outcome = Answered;
            return flagged;
        }
        catch (IpcNotReadyError e)
        {
            outcome = Missing;
            error = e;
            _queryBlockedUntil = now + MissingBackoffMs;
            return null;
        }
        catch (Exception e)
        {
            // IpcTypeMismatchError、Newtonsoft 例外、對方擲出的例外都走這裡；ECommons 的 TryInvoke 只攔 IpcNotReadyError。
            outcome = Failed;
            error = e;
            _queryBlockedUntil = now + FailedBackoffMs;
            return null;
        }
    }

    // BossModReborn 優先、沒裝才退回原版 BossMod（同 DeepDungeonPullSafety）。
    private static Func<ulong[]>? ResolveQuery()
    {
        var reborn = ConflictingPluginsChecks.BossModReborn;
        if (reborn.IpcAvailable)
            return reborn.InterruptQuery;
        var original = ConflictingPluginsChecks.BossMod;
        return original.IpcAvailable ? original.InterruptQuery : null;
    }

    private static unsafe uint FindCaster(ulong[] flagged, uint actionId)
    {
        var current = CurrentTarget?.GameObjectId;
        uint best = 0;
        var bestLeft = float.MaxValue;
        foreach (var o in Svc.Objects)
        {
            if (o is not IBattleChara c || c.GameObjectId == current || !c.IsTargetable || !c.IsHostile() ||
                !MechanicHints.IsFlagged(flagged, c) || !CanInterruptEnemy(null, c) ||
                !InActionRange(actionId, c) || !IsInLineOfSight(c) ||
                !ActionManager.CanUseActionOnTarget(actionId, (GameObjectStruct*)c.Address))
                continue;
            var left = c.TotalCastTime - c.CurrentCastTime;
            if (left < bestLeft)
            {
                best = c.EntityId;
                bestLeft = left;
            }
        }
        return best;
    }

    private static void LogMissing(long now)
    {
        if (now < _missingLogNextAt)
            return;
        _missingLogNextAt = now + MissingLogIntervalMs;
        PluginLog.Information("[深宮打斷] 沒有可用的 BossMod(Reborn) 打斷標記（沒載入或尚未就緒），打斷技照舊全打。");
    }

    private static void LogRetarget(IBattleChara caster)
    {
        if (ShouldLog(RetargetLogNextAt, caster.GameObjectId))
            PluginLog.Information(
                $"[深宮打斷] 目前目標不需打斷，傷頭改打被 BossMod 標記的「{caster.Name.TextValue}」(0x{caster.GameObjectId:X}) 的詠唱 {caster.CastActionId}；" +
                $"玩家選中的目標不變。同一目標 {PerTargetLogIntervalMs / 1000} 秒內不再記。");
    }

    private static bool ShouldLog(Dictionary<ulong, long> nextAt, ulong targetId)
    {
        var now = Environment.TickCount64;
        if (nextAt.TryGetValue(targetId, out var next) && now < next)
            return false;
        if (nextAt.Count >= 256)
            nextAt.Clear();
        nextAt[targetId] = now + PerTargetLogIntervalMs;
        return true;
    }
}
