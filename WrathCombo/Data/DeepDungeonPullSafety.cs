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
using WrathCombo.AutoRotation;
using WrathCombo.Data.Conflicts;
using static WrathCombo.CustomComboNS.Functions.CustomComboFunctions;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     深層迷宮裡，自動循環要對「還沒在戰鬥中」的敵人出手前，先問 BossMod(Reborn) 會不會一次拉太多隻；回 false 就不開打。
/// </summary>
/// <remarks>
///     只給自動循環用（呼叫點都在 <c>AutoRotationController</c>），玩家自己按的連段替換不經過這裡。
///     只在框架執行緒呼叫；不在框架執行緒、設定關閉、不在深層迷宮、目標已在戰鬥中時一律放行且不碰 IPC。
///     對方沒載入、端點沒註冊、型別不合或擲任何例外 ⇒ 一律放行（照舊開打），並退避一段時間不再呼叫。
/// </remarks>
internal static class DeepDungeonPullSafety
{
    // Evaluate 的結果分類（只給診斷與量測台用）。
    internal const int Safe = 1, Unsafe = 2, Cached = 3, BackedOff = 4, Missing = -1, Failed = -2;

    internal const long AnswerTtlMs = 500;
    internal const long MissingBackoffMs = 5_000;
    internal const long FailedBackoffMs = 60_000;
    internal const long BlockedLogIntervalMs = 10_000;
    private const long MissingLogIntervalMs = 600_000;
    private const long FailedLogIntervalMs = 60_000;

    // 同一隻在 AnswerTtlMs 內只問一次：一輪自動循環會有好幾個預設問同一個目標。
    private const int CacheSize = 8;
    private static readonly ulong[] CacheTarget = new ulong[CacheSize];
    private static readonly bool[] CacheSafe = new bool[CacheSize];
    private static readonly long[] CacheAt = new long[CacheSize];
    private static readonly bool[] CacheValid = new bool[CacheSize];
    private static int _cacheNext;

    private static long _queryBlockedUntil;
    private static long _missingLogNextAt;
    private static long _failedLogNextAt;
    private static readonly Dictionary<ulong, long> BlockedLogNextAt = [];

    private static bool Active =>
        AutoRotationController.cfg?.DeepDungeonPullSafety == true &&
        Svc.Framework.IsInFrameworkUpdateThread &&
        Svc.Condition[ConditionFlag.InDeepDungeon];

    /// <summary> 自動循環可不可以對 <paramref name="target" /> 出手（只看目標本身）。 </summary>
    internal static bool AllowsTarget(IGameObject? target) =>
        target is null || !Active || Ask(target);

    /// <summary>
    ///     這一招送出去會不會開打不安全的敵人：<paramref name="hitsTarget" /> 為真時先看目標，
    ///     再看範圍技打得到、還沒在戰鬥中的其他敵人（形狀判定同 <c>NumberOfEnemiesInRange</c>）。
    /// </summary>
    internal static bool AllowsAction(uint actionId, IGameObject? target, bool hitsTarget)
    {
        if (!Active)
            return true;
        if (hitsTarget && target is not null && !Ask(target))
            return false;
        if (!ActionWatching.ActionSheet.TryGetValue(actionId, out var row) ||
            row.AttackType.RowId == 0 || row.CastType is not (2 or 3 or 4) ||
            LocalPlayer is not { } player)
            return true;

        foreach (var o in Svc.Objects)
        {
            // 目標本身若在範圍內會再問一次，走快取。
            if (o is not IBattleNpc npc || !npc.IsTargetable || npc.IsDead || !npc.IsHostile() ||
                IsEngaged(npc) || !InEffectArea(row, npc, target, player))
                continue;
            if (!Ask(npc))
                return false;
        }

        return true;
    }

    private static unsafe bool IsEngaged(IGameObject o) => o is IBattleChara c && c.Struct()->InCombat;

    private static bool InEffectArea(in Lumina.Excel.Sheets.Action row, IGameObject o, IGameObject? target, IGameObject player)
    {
        switch (row.CastType)
        {
            case 2 when row.CanTargetSelf:
                return PointInCircle(o.Position - player.Position, row.EffectRange + o.HitboxRadius);
            case 2:
                return target is not null && PointInCircle(o.Position - target.Position, row.EffectRange + o.HitboxRadius);
            case 3:
                return target is not null && GetTargetDistance(o) <= row.Range &&
                       PointInCone(o.Position - player.Position, PositionalMath.GetDirection(player.Position, target.Position), 45f);
            case 4:
                return target is not null && GetTargetDistance(o) <= row.Range &&
                       HitboxInRect(o, PositionalMath.GetRotation(player.Position, target.Position), row.Range * 0.5f, row.XAxisModifier * 0.5f);
            default:
                return false;
        }
    }

    // 已在戰鬥中、不是敵對 NPC ⇒ 不問（BMR 對這些也回 true）。
    private static bool Ask(IGameObject target)
    {
        if (target is not IBattleNpc || IsEngaged(target) || !target.IsHostile())
            return true;

        var now = Environment.TickCount64;
        var query = ResolveQuery();
        if (query is null)
        {
            if (now >= _missingLogNextAt)
            {
                _missingLogNextAt = now + MissingLogIntervalMs;
                PluginLog.Information("[AutoRotation][深宮] 沒有可用的 BossMod(Reborn)，開打前無法詢問安全與否，自動循環照舊開打。");
            }
            return true;
        }

        var safe = Evaluate(target.GameObjectId, query, now, out var outcome, out var error);
        if (!safe)
        {
            if (ShouldLogBlocked(target.GameObjectId, now))
                PluginLog.Information(
                    $"[AutoRotation][深宮] BossMod 回報開打「{target.Name.TextValue}」(0x{target.GameObjectId:X}) 會一次拉太多隻，" +
                    $"自動循環先不開打它；同一目標 {BlockedLogIntervalMs / 1000} 秒內不再記。");
            return false;
        }

        if (outcome == Missing && now >= _missingLogNextAt)
        {
            _missingLogNextAt = now + MissingLogIntervalMs;
            PluginLog.Information("[AutoRotation][深宮] BossMod(Reborn) 沒有 BossMod.DeepDungeon.IsPullSafe（沒載入或版本較舊），自動循環照舊開打。");
        }
        else if (outcome == Failed && now >= _failedLogNextAt)
        {
            _failedLogNextAt = now + FailedLogIntervalMs;
            PluginLog.Information(
                $"[AutoRotation][深宮] 呼叫 BossMod.DeepDungeon.IsPullSafe 失敗，{FailedBackoffMs / 1000} 秒內照舊開打：{error?.ToStringFull()}");
        }

        return true;
    }

    // BossModReborn 優先、沒裝才退回原版 BossMod（同 DashSafety）。
    private static Func<ulong, bool>? ResolveQuery()
    {
        var reborn = ConflictingPluginsChecks.BossModReborn;
        if (reborn.IpcAvailable)
            return reborn.PullSafeQuery;
        var original = ConflictingPluginsChecks.BossMod;
        return original.IpcAvailable ? original.PullSafeQuery : null;
    }

    /// <summary> 帶短期快取與退避的查詢本體；回傳 false 只有一種情形：端點真的回了 false（或快取裡是它回的 false）。 </summary>
    /// <remarks> 只在框架執行緒呼叫。退避期間回 true 且不呼叫 <paramref name="query" />。 </remarks>
    internal static bool Evaluate(ulong targetId, Func<ulong, bool> query, long now, out int outcome, out Exception? error)
    {
        error = null;
        for (var i = 0; i < CacheSize; i++)
            if (CacheValid[i] && CacheTarget[i] == targetId && now - CacheAt[i] < AnswerTtlMs)
            {
                outcome = Cached;
                return CacheSafe[i];
            }

        if (now < _queryBlockedUntil)
        {
            outcome = BackedOff;
            return true;
        }

        bool safe;
        try
        {
            safe = query(targetId);
        }
        catch (IpcNotReadyError e)
        {
            outcome = Missing;
            error = e;
            _queryBlockedUntil = now + MissingBackoffMs;
            return true;
        }
        catch (Exception e)
        {
            // IpcTypeMismatchError、Newtonsoft 例外、對方擲出的例外都走這裡；ECommons 的 TryInvoke 攔不到它們。
            outcome = Failed;
            error = e;
            _queryBlockedUntil = now + FailedBackoffMs;
            return true;
        }

        outcome = safe ? Safe : Unsafe;
        var slot = _cacheNext;
        _cacheNext = (slot + 1) % CacheSize;
        CacheTarget[slot] = targetId;
        CacheSafe[slot] = safe;
        CacheAt[slot] = now;
        CacheValid[slot] = true;
        return safe;
    }

    internal static bool ShouldLogBlocked(ulong targetId, long now)
    {
        if (BlockedLogNextAt.TryGetValue(targetId, out var next) && now < next)
            return false;
        if (BlockedLogNextAt.Count >= 256)
            BlockedLogNextAt.Clear();
        BlockedLogNextAt[targetId] = now + BlockedLogIntervalMs;
        return true;
    }
}
