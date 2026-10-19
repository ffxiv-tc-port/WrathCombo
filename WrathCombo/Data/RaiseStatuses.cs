#region

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     「已被復活（等待接受）」「生還」「無法復活」「限制復活」狀態的 id 集合，依 Status 表的圖示建（同一種狀態遊戲裡有不只一個 id）。
/// </summary>
/// <remarks>
///     集合在第一次用到時建一次。一定含各自的退回 id（148／2648／4263／4262）；查表失敗就只剩它們。
///     刻意不放進 StatusCache：那裡其他靜態初始式失敗會連帶讓自動復活整個擲例外。
/// </remarks>
internal static class RaiseStatuses
{
    internal const uint RaiseIcon = 210406, TranscendentIcon = 215273;
    internal const uint RaiseFallback = 148, TranscendentFallback = 2648;
    internal const uint UnraisableIcon = 229943, UnraisableFallback = 4263;
    internal const uint LimitedRaiseIcon = 218286, LimitedRaiseFallback = 4262;

    internal static FrozenSet<uint> Raised => Sets.Raised;
    internal static FrozenSet<uint> Transcendent => Sets.Transcendent;
    internal static FrozenSet<uint> Unraisable => Sets.Unraisable;
    internal static FrozenSet<uint> LimitedRaise => Sets.LimitedRaise;

    private static class Sets
    {
        internal static readonly FrozenSet<uint> Raised = BuildLogged(RaiseIcon, RaiseFallback);
        internal static readonly FrozenSet<uint> Transcendent = BuildLogged(TranscendentIcon, TranscendentFallback);
        internal static readonly FrozenSet<uint> Unraisable = BuildLogged(UnraisableIcon, UnraisableFallback);
        internal static readonly FrozenSet<uint> LimitedRaise = BuildLogged(LimitedRaiseIcon, LimitedRaiseFallback);
    }

    private static FrozenSet<uint> BuildLogged(uint icon, uint fallback)
    {
        var set = Build(icon, fallback, StatusRows, out var error);
        if (error is not null)
            PluginLog.Information(
                $"[RaiseStatuses] 讀 Status 表（圖示 {icon}）失敗，只用 {fallback} 判斷：{error.Message}");
        else
            PluginLog.Debug($"[RaiseStatuses] 圖示 {icon}：{string.Join(",", set.Order())}");
        return set;
    }

    private static IEnumerable<(uint Id, uint Icon)> StatusRows() =>
        Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Status>().Select(row => (row.RowId, row.Icon));

    /// <summary>圖示等於 <paramref name="icon" /> 的狀態 id，再加上 <paramref name="fallback" />；任何例外都只回 fallback。</summary>
    internal static FrozenSet<uint> Build(uint icon, uint fallback, Func<IEnumerable<(uint Id, uint Icon)>> rows,
        out Exception? error)
    {
        error = null;
        var ids = new HashSet<uint> { fallback };
        try
        {
            foreach (var (id, rowIcon) in rows())
                if (rowIcon == icon)
                    ids.Add(id);
        }
        catch (Exception e)
        {
            error = e;
            ids = [fallback];
        }

        return ids.ToFrozenSet();
    }

    /// <summary>身上有沒有任一個 <paramref name="ids" /> 裡的狀態（不論施放者）。</summary>
    /// <remarks>
    ///     讀的與 Dalamud <c>StatusList</c> 同一塊（<c>GetStatusManager()</c>、前 NumValidStatuses 格），但不建包裝、零配置。
    ///     只在取得 <paramref name="obj" /> 的同一幀呼叫。
    /// </remarks>
    internal static unsafe bool HasAny(IGameObject? obj, FrozenSet<uint> ids)
    {
        if (obj is not IBattleChara || ids.Count == 0)
            return false;
        var address = obj.Address;
        if (address == nint.Zero)
            return false;

        var manager = ((Character*)address)->GetStatusManager();
        if (manager == null)
            return false;

        var statuses = manager->Status;
        var count = Math.Min((int)manager->NumValidStatuses, statuses.Length);
        for (var i = 0; i < count; i++)
        {
            var id = statuses[i].StatusId;
            if (id != 0 && ids.Contains(id))
                return true;
        }

        return false;
    }
}
