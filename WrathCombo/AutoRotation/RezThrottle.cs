#region

using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Logging;
using WrathCombo.Combos.PvE;
using WrathCombo.Combos.PvE.Content;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Data;
using WrathCombo.Extensions;

#endregion

namespace WrathCombo.AutoRotation;

/// <summary>
///     自動復活的同目標節流：某位隊友在 <see cref="WindowMs" /> 內已經真的送出過任一種復活（SendAction），就先跳過他。
/// </summary>
/// <remarks>
///     伺服器拒絕復活時沒有效果封包，對方也不會有「復活」狀態，沒有這道節流就每個週期重送。
///     計的是送出不是 UseAction 呼叫：還在 BossMod 佇列裡、還沒送出的那一發不算。
///     即刻詠唱刻意不計：它只在挑到「沒被節流的目標」之後才放，被節流的目標不會讓它白燒。
/// </remarks>
internal static class RezThrottle
{
    internal const long WindowMs = 2500;
    private const long LogAfterMs = 1000;
    private const long LogIntervalMs = 30_000;

    internal static readonly uint[] RaiseActions =
    [
        WHM.Raise, SCH.Resurrection, AST.Ascend, SGE.Egeiro, RDM.Verraise, OccultCrescent.Revive,
        BLU.AngelWhisper, Variant.VariantRaise, Variant.VariantRaise2, Bozja.LostArise,
    ];

    private static readonly Dictionary<ulong, long> LogNextAt = [];
    private static readonly Dictionary<ulong, long> LimitedLogNextAt = [];
    private static readonly object LogGate = new();

    /// <summary>自動復活挑目標時用：這位要不要先跳過。只在框架執行緒呼叫。</summary>
    internal static bool Skip(WrathPartyMember member)
    {
        var now = Environment.TickCount64;
        if (!SentWithin(ActionWatching.UsedOnDict, RaiseActions, member.GameObjectId, now, out var action, out var elapsed))
            return false;

        // 送出後 1 秒內對方還沒有「復活」狀態是常態（效果封包還在路上），那時不記。
        if (elapsed >= LogAfterMs && ShouldLog(member.GameObjectId, now))
            PluginLog.Information(
                $"[AutoRez] 對「{member.BattleChara?.Name.TextValue ?? "?"}」(0x{member.GameObjectId:X}) " +
                $"的「{action.ActionName()}」已在 {elapsed / 1000f:F1} 秒前送出，對方仍沒有「復活」狀態（可能被伺服器拒絕）；" +
                $"送出後 {WindowMs / 1000f:F1} 秒內不再對他送復活，先救別人或照常輸出。" +
                $"{LogIntervalMs / 1000} 秒內同一目標不再記。");
        return true;
    }

    /// <summary>
    ///     <paramref name="actions" /> 裡任一招在 <see cref="WindowMs" /> 內送給過 <paramref name="targetId" />；
    ///     <paramref name="action" />／<paramref name="elapsedMs" /> 是最近那一發。
    /// </summary>
    internal static bool SentWithin(Dictionary<(uint, ulong), long> usedOn, uint[] actions, ulong targetId, long now,
        out uint action, out long elapsedMs)
    {
        action = 0;
        elapsedMs = long.MaxValue;
        foreach (var id in actions)
            if (usedOn.TryGetValue((id, targetId), out var sentAt) && now - sentAt < elapsedMs)
            {
                action = id;
                elapsedMs = now - sentAt;
            }

        return elapsedMs < WindowMs;
    }

    /// <summary>
    ///     「自動復活跳過有『限制復活』的隊友」選項開著時：<paramref name="target" /> 身上有「限制復活」就跳過（呼叫端先判選項）。
    /// </summary>
    /// <remarks>只用在 WrathCombo 自己挑目標的地方；玩家自己指的目標不經過這裡。</remarks>
    internal static bool SkipLimitedRaise(IGameObject? target, string where)
    {
        if (!RaiseStatuses.HasAny(target, RaiseStatuses.LimitedRaise))
            return false;

        if (ShouldLog(LimitedLogNextAt, target!.GameObjectId, Environment.TickCount64))
            PluginLog.Information(
                $"[AutoRez] 「{target.Name.TextValue}」(0x{target.GameObjectId:X}) 身上有「限制復活」" +
                $"（部分復活技無效、每次被復活減一階、歸零變成無法復活）；依「自動復活跳過有『限制復活』的隊友」選項，" +
                $"{where}不自動選他，請手動復活。{LogIntervalMs / 1000} 秒內同一目標不再記。");
        return true;
    }

    internal static bool ShouldLog(ulong targetId, long now) => ShouldLog(LogNextAt, targetId, now);

    private static bool ShouldLog(Dictionary<ulong, long> map, ulong targetId, long now)
    {
        lock (LogGate)
        {
            if (map.TryGetValue(targetId, out var next) && now < next)
                return false;
            map[targetId] = now + LogIntervalMs;
            return true;
        }
    }
}
