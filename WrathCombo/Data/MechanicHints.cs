#region

using System;
using System.Collections.Generic;
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
///     🔴 這是<b>戰鬥時機</b>資料，快取上限就是<b>一幀</b> ——
///     不可以照 <see cref="ConflictingPluginsChecks" /> 那種 2 秒快取的形狀做。
///     同一幀內被問幾次都只會真的呼叫一次 IPC。
///     🔴 BMR 回的是<b>模組原始標記</b>，不含 InCombat／是否正在詠唱之類的策略過濾。
///     所以這份清單只能拿來當「優先名單」，跟 Wrath 既有的敵對／可選取／距離／
///     <c>IsCastInterruptible</c>／ICD 判斷<b>取交集</b>，不可以取代它們。
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

    private static bool _stunsReserved;
    private static bool _interruptsReserved;
    private static readonly Dictionary<(string Kind, uint Entity, uint Cast), long> _controlLoggedAt = [];

    /// <summary>使用者有沒有開「照 BossMod(Reborn) 的危險讀條標記眩暈／打斷」（與上面的目標排序是兩件事）。</summary>
    internal static bool ControlMarksEnabled =>
        Service.Configuration.BossModControlMarks;

    /// <summary>
    ///     BMR 標記這個目標「這一發該暈眩」：自動眩暈可以越過首領排除。
    /// </summary>
    /// <remarks>契約：只在標記存在的這一幀成立；沒有標記、BMR 不在、開關關閉一律 <see langword="false" />＝照舊排除首領。</remarks>
    internal static bool StunOverridesBossCheck(IGameObject? target)
    {
        if (!ControlMarksEnabled || target is null || !IsFlagged(Hints(HintKind.Stun), target))
            return false;
        LogControl("越過首領排除，眩暈 BMR 標記的讀條", target);
        return true;
    }

    /// <summary>
    ///     BMR 標記這個目標「這一發該暈眩」，且近戰自動掃腿開著：近戰可以對它用掃腿。
    /// </summary>
    /// <remarks>契約：沒有標記、BMR 不在、任一開關關閉一律 <see langword="false" />＝近戰照舊從不掃腿。</remarks>
    internal static bool StunMarkedForLegSweep(IGameObject? target)
    {
        if (!ControlMarksEnabled || !Service.Configuration.BossModMeleeLegSweep ||
            target is null || !IsFlagged(Hints(HintKind.Stun), target))
            return false;
        LogControl("近戰掃腿 BMR 標記的讀條", target);
        return true;
    }

    /// <summary>
    ///     BMR 的模組宣告本場眩暈保留給標記的讀條，而這個目標沒被標記：不該對它用眩暈。
    /// </summary>
    internal static bool StunHeldByReservation(IGameObject? target) =>
        HeldByReservation(HintKind.Stun, target);

    /// <summary>同 <see cref="StunHeldByReservation" />，對象是打斷。</summary>
    internal static bool InterruptHeldByReservation(IGameObject? target) =>
        HeldByReservation(HintKind.Interrupt, target);

    private static bool HeldByReservation(HintKind kind, IGameObject? target)
    {
        if (!ControlMarksEnabled || target is null)
            return false;
        var hints = Hints(kind);
        var reserved = kind is HintKind.Stun ? _stunsReserved : _interruptsReserved;
        if (!reserved || IsFlagged(hints, target))
            return false;
        LogControl(kind is HintKind.Stun ? "眩暈保留中，不暈沒標記的讀條" : "打斷保留中，不打沒標記的讀條", target);
        return true;
    }

    // 同一（判定, 目標, 讀條）10 秒內只記一次（約一次讀條一行）：判定在每幀路徑上、目標挑選會輪流問多個敵人。
    private static void LogControl(string what, IGameObject target)
    {
        var key = (what, target.EntityId, (target as IBattleChara)?.CastActionId ?? 0u);
        var now = Environment.TickCount64;
        if (_controlLoggedAt.TryGetValue(key, out var at) && now - at < 10_000)
            return;
        if (_controlLoggedAt.Count > 128)
            _controlLoggedAt.Clear();
        _controlLoggedAt[key] = now;
        PluginLog.Information($"[MechanicHints] {what}：目標 {target.BaseId}（讀條 {key.Item3}）");
    }

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
            _stunsReserved = false;
            _interruptsReserved = false;

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
        _stunsReserved = source.StunsReserved();
        _interruptsReserved = source.InterruptsReserved();
    }
}
