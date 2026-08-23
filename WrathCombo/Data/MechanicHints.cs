#region

using System;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.Logging;
using WrathCombo.Data.Conflicts;
using WrathCombo.Services;
using EZ = ECommons.Throttlers.EzThrottler;
using TS = System.TimeSpan;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     BossMod(Reborn) 的「這一發該打斷／該暈眩」機制提示，帶單幀快取。
/// </summary>
/// <remarks>
///     <para>
///         🔴 這是<b>戰鬥時機</b>資料，快取上限就是<b>一幀</b> ——
///         不可以照 <see cref="ConflictingPluginsChecks" /> 那種 2 秒快取的形狀做。
///         同一幀內被問幾次都只會真的呼叫一次 IPC。
///     </para>
///     <para>
///         🔴 BMR 回的是<b>模組原始標記</b>，不含 InCombat／是否正在詠唱之類的策略過濾。
///         所以這份清單只能拿來當「優先名單」，跟 Wrath 既有的敵對／可選取／距離／
///         <c>IsCastInterruptible</c>／ICD 判斷<b>取交集</b>，不可以取代它們。
///     </para>
///     <para>
///         BMR 沒安裝、版本太舊、端點不存在、呼叫擲例外 —— 全部收斂成「空清單」，
///         呼叫端在非嚴格模式下因此<b>完全等同現行行為</b>。
///     </para>
/// </remarks>
internal static class MechanicHints
{
    internal enum HintKind
    {
        Interrupt,
        Stun,
    }

    private static ulong[] _interruptHints = [];
    private static ulong[] _stunHints = [];

    private static DateTime _cachedForFrame = DateTime.MinValue;
    private static bool _primed;

    /// <summary>使用者有沒有開「機制感知打斷／暈眩目標」。</summary>
    internal static bool Enabled =>
        Service.Configuration.MechanicAwareTargeting;

    /// <summary>
    ///     嚴格模式：只出手在 BMR 標記過的目標上。
    ///     BMR 缺席或沒有任何標記時＝完全不出手。
    /// </summary>
    internal static bool StrictOnly =>
        Enabled && Service.Configuration.MechanicAwareTargetingStrictOnly;

    internal static ulong[] Hints(HintKind kind)
    {
        Refresh();
        return kind is HintKind.Interrupt ? _interruptHints : _stunHints;
    }

    /// <summary>
    ///     這個物件有沒有被 BMR 標記。
    /// </summary>
    /// <remarks>
    ///     🔑 比對的是 <see cref="IGameObject.EntityId" />，<b>不是</b>
    ///     <c>GameObjectId</c> —— BMR 的 <c>Actor.InstanceID</c> 是從
    ///     <c>GameObject.EntityId</c> 建出來的（<c>WorldStateGameSync</c> 的
    ///     <c>OpCreate(obj-&gt;EntityId, …)</c>）。拿錯欄位的失敗形式是
    ///     <b>一致地零命中</b>，跟「BMR 真的沒標記」完全分不出來。
    /// </remarks>
    internal static bool IsFlagged(ulong[] hints, IGameObject? obj) =>
        hints.Length != 0 && obj is not null &&
        Array.IndexOf(hints, (ulong)obj.EntityId) >= 0;

    private static void Refresh()
    {
        var frame = Svc.Framework.LastUpdate;
        if (_primed && frame == _cachedForFrame)
            return;

        _primed = true;
        _cachedForFrame = frame;

        // BossModReborn 優先；沒裝才退回原版 BossMod。
        // 兩個實例訂的是同一組標籤（都寫全名 + applyPrefix: false）。
        var source =
            ConflictingPluginsChecks.BossModReborn.IpcAvailable
                ? ConflictingPluginsChecks.BossModReborn
                : ConflictingPluginsChecks.BossMod.IpcAvailable
                    ? ConflictingPluginsChecks.BossMod
                    : null;

        if (source is null)
        {
            _interruptHints = [];
            _stunHints = [];

            // 嚴格模式 ＋ 沒有 BMR ＝ 打斷／暈眩會完全不出手，
            // 而那看起來就像「功能壞了」。這條是使用者唯一查得到的線索。
            if (StrictOnly && EZ.Throttle("MechanicHintsNoBossMod", TS.FromMinutes(5)))
                PluginLog.Information(
                    "[MechanicHints] 已開啟「只打／只暈被標記的目標」，但偵測不到 " +
                    "BossMod(Reborn) 的機制提示 —— 打斷與暈眩的目標選擇因此不會出手。" +
                    "若非預期，請關掉該子選項，或確認 BossModReborn 已安裝且為新版。");

            return;
        }

        _interruptHints = source.InterruptHints();
        _stunHints = source.StunHints();
    }
}
