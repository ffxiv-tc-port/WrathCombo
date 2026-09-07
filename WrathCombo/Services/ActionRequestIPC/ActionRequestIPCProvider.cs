using ECommons.EzIpcManager;
using ECommons.Logging;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using WrathCombo.CustomComboNS.Functions;

namespace WrathCombo.Services.ActionRequestIPC;

/// <summary>
///     供其他外掛「請求施放某個動作」與「暫時封鎖某個動作」的 IPC 供應端。
/// </summary>
/// <remarks>
///     刻意寫成 static，避免在冷卻查詢這種每幀都會走到的路徑上多一次實例解參考。<br />
///     ⚠️ 兩份清單在沒有任何外掛呼叫 IPC 時都是空的，所有消費端（
///     <see cref="Data.CooldownData" />、<see cref="CustomComboFunctions.ActionReady" />、
///     <see cref="CustomComboNS.CustomCombo.TryInvoke" />）都會在第一個判斷就退出，
///     因此**未被使用時對既有行為零影響**。
/// </remarks>
public static class ActionRequestIPCProvider
{
    /// <summary>
    ///     IPC 前綴。<br />
    ///     🔴 刻意寫死而不是取 <c>Svc.PluginInterface.InternalName</c>：端點名稱是與呼叫端的契約，
    ///     若哪天內部名稱被改掉，用執行期取值會讓端點名跟著變，而呼叫端只會收到靜默的 default 值。
    /// </summary>
    private const string IPCPrefix = "WrathCombo.ActionRequest";

    /// <summary>
    ///     保護 <see cref="ActionRequests" /> 與 <see cref="ActionBlacklist" /> 的鎖。
    /// </summary>
    /// <remarks>
    ///     🔴🔴 這兩份清單被<b>兩種執行緒</b>同時增刪：<br />
    ///     ① 承租外掛自己的執行緒 —— <c>[EzIPC]</c> 端點是拿
    ///     <c>GetIpcProvider().RegisterFunc()</c> 直接註冊委派的，中間沒有任何
    ///     framework 轉送，所以 <see cref="RequestActionUse" />／
    ///     <see cref="RequestBlacklist" />／各支 Reset 都跑在呼叫端的執行緒上。<br />
    ///     ② framework 執行緒 —— <c>CustomCombo.TryInvoke</c>（經
    ///     <see cref="TryGetRequestedAction" />）、<c>CustomComboFunctions.ActionReady</c>
    ///     與 <c>Data.CooldownData</c>（經 <see cref="GetArtificialCooldown" />）每一幀
    ///     都在讀，而且會就地 <c>RemoveAt</c> 掉過期的項目。<br />
    ///     裸 <see cref="List{T}" /> 零同步，失敗形式不是「讀到舊值」而是
    ///     <b>清單本身壞掉</b>（<c>Add</c> 觸發重新配置時讀取端可能拿到舊陣列或
    ///     越界索引）。<br />
    ///     🔴 這把鎖是葉節點：鎖內只做清單運算與 <c>Environment.TickCount64</c> 比較，
    ///     絕不呼叫 ImGui、不做檔案 I/O、不呼叫別的外掛，也不碰任何原生狀態
    ///     （<c>CanWeave()</c>／<c>ActionReady()</c> 都在鎖外做，見
    ///     <see cref="TryGetRequestedAction" />）。
    /// </remarks>
    private static readonly object Gate = new();

    /// <summary>
    ///     其他外掛請求施放的動作。
    /// </summary>
    /// <remarks>🔴 一律在 <see cref="Gate" /> 內存取。</remarks>
    private static readonly List<ActionRequest> ActionRequests = [];

    /// <summary>
    ///     其他外掛請求暫時封鎖（加上人工冷卻）的動作。
    /// </summary>
    /// <remarks>🔴 一律在 <see cref="Gate" /> 內存取。</remarks>
    private static readonly List<ActionRequest> ActionBlacklist = [];

    private static EzIPCDisposalToken[]? _disposalTokens;

    public static void Initialize()
    {
        _disposalTokens ??= EzIPC.Init(typeof(ActionRequestIPCProvider), IPCPrefix);
    }

    public static void Dispose()
    {
        if (_disposalTokens is null)
            return;

        foreach (var token in _disposalTokens)
            token.Dispose();

        _disposalTokens = null;
        lock (Gate)
        {
            ActionRequests.Clear();
            ActionBlacklist.Clear();
        }
    }

    /// <summary>
    ///     請求把某個動作封鎖一段時間。
    /// </summary>
    /// <param name="actionType">動作類型。</param>
    /// <param name="actionID">動作 ID。</param>
    /// <param name="timeMs">要封鎖多久（毫秒）。</param>
    [EzIPC]
    public static void RequestBlacklist(ActionType actionType, uint actionID, int timeMs)
    {
        ActionDescriptor descriptor = new(actionType, actionID);
        lock (Gate)
            ActionBlacklist.Add(
                new ActionRequest(descriptor, Environment.TickCount64 + timeMs, default));
    }

    /// <summary>
    ///     解除某個動作的封鎖。
    /// </summary>
    /// <param name="actionType">動作類型。</param>
    /// <param name="actionID">動作 ID。</param>
    [EzIPC]
    public static void ResetBlacklist(ActionType actionType, uint actionID)
    {
        var descriptor = new ActionDescriptor(actionType, actionID);
        lock (Gate)
            ActionBlacklist.RemoveAll(item => item.Descriptor == descriptor);
    }

    /// <summary>
    ///     清空整份封鎖清單。
    /// </summary>
    /// <remarks>
    ///     ⚠️ IPC 端點名稱是單數的 <c>ResetAllBlacklist</c>（不是 <c>ResetAllBlacklists</c>），
    ///     這是與 <c>WrathCombo.API</c> 套件逐字對齊的結果，不要「順手修正」。
    /// </remarks>
    [EzIPC]
    public static void ResetAllBlacklist()
    {
        lock (Gate)
            ActionBlacklist.Clear();
    }

    /// <summary>
    ///     取得某個動作目前的人工冷卻剩餘秒數；順便把已經過期的封鎖項目清掉。
    /// </summary>
    /// <param name="actionType">動作類型。</param>
    /// <param name="actionID">動作 ID。</param>
    /// <returns>剩餘秒數；沒有被封鎖時為 <c>0</c>。</returns>
    [EzIPC]
    public static float GetArtificialCooldown(ActionType actionType, uint actionID)
    {
        var d = new ActionDescriptor(actionType, actionID);
        var currentTick = Environment.TickCount64;
        var maxDeadline = 0L;

        // 🔴 這一支既是 [EzIPC] 端點（承租外掛的執行緒）又在每一幀的冷卻查詢路徑上
        //    （framework 執行緒），而它會就地 RemoveAt ⇒ 掃描與移除必須在鎖內是同
        //    一段。鎖內做的全部是清單索引、結構比較與整數比較，沒有任何 I/O，
        //    所以維持「拍快照 → 出鎖處理 → 回鎖移除」反而更糟：索引在放鎖的空檔
        //    會位移，移除得重新搜尋。
        lock (Gate)
        {
            if (ActionBlacklist.Count == 0)
                return 0f;

            for (var idx = ActionBlacklist.Count - 1; idx >= 0; idx--)
            {
                var currentRequest = ActionBlacklist[idx];
                if (currentRequest.Descriptor != d) continue;

                if (!currentRequest.IsActive)
                {
                    ActionBlacklist.RemoveAt(idx);
                }
                else
                {
                    var currentDeadline = currentRequest.Deadline - currentTick;
                    if (currentDeadline > maxDeadline) maxDeadline = currentDeadline;
                }
            }
        }

        return maxDeadline / 1000f;
    }

    /// <summary>
    ///     請求在一段時間內施放某個動作。
    /// </summary>
    /// <param name="actionType">動作類型。</param>
    /// <param name="actionID">動作 ID。</param>
    /// <param name="timeMs">這筆請求的有效時間（毫秒）。</param>
    /// <param name="isGcd">
    ///     施放時機：<see langword="true" /> 只在 GCD 視窗、<see langword="false" /> 只在插入技視窗、
    ///     <see langword="null" /> 不限。
    /// </param>
    [EzIPC]
    public static void RequestActionUse(ActionType actionType, uint actionID, int timeMs, bool? isGcd)
    {
        ActionDescriptor descriptor = new(actionType, actionID);
        lock (Gate)
            ActionRequests.Add(
                new ActionRequest(descriptor, Environment.TickCount64 + timeMs, isGcd));
    }

    /// <summary>
    ///     取消某個動作的施放請求。
    /// </summary>
    /// <param name="actionType">動作類型。</param>
    /// <param name="actionID">動作 ID。</param>
    [EzIPC]
    public static void ResetRequest(ActionType actionType, uint actionID)
    {
        var descriptor = new ActionDescriptor(actionType, actionID);
        lock (Gate)
            ActionRequests.RemoveAll(item => item.Descriptor == descriptor);
    }

    /// <summary>
    ///     清空所有施放請求。
    /// </summary>
    [EzIPC]
    public static void ResetAllRequests()
    {
        lock (Gate)
            ActionRequests.Clear();
    }

    /// <summary>
    ///     拍下目前仍然有效的施放請求，順便清掉過期的。
    /// </summary>
    /// <returns>
    ///     一份快照。順序與改動前的迭代器相同（由清單尾端往前，也就是最後送進來的
    ///     請求最先被看到）。
    /// </returns>
    /// <remarks>
    ///     🔴 改動前這一支是 <c>yield return</c> 迭代器，等於讓呼叫端一邊走訪活的
    ///     清單、一邊 <c>RemoveAt</c>，而清單同時被承租外掛的執行緒 <c>Add</c>。
    ///     改成鎖內一次拍完快照回傳，呼叫端在鎖外走訪一份不會再變的陣列。<br />
    ///     ⚠️ 唯一的差異是<b>清理時機</b>：改動前是惰性的，呼叫端提早 break 就不會
    ///     掃到後面的過期項；現在每次都整份掃完。過期項本來就永遠不會被選中
    ///     （<see cref="TryGetRequestedAction" /> 只挑 <c>IsActive</c> 的），
    ///     所以回傳值不受影響，只是清得早一點。
    /// </remarks>
    public static ActionRequest[] GetRequestedActions()
    {
        lock (Gate)
        {
            if (ActionRequests.Count == 0)
                return [];

            var active = new List<ActionRequest>(ActionRequests.Count);
            for (var idx = ActionRequests.Count - 1; idx >= 0; idx--)
            {
                var currentRequest = ActionRequests[idx];
                if (!currentRequest.IsActive)
                    ActionRequests.RemoveAt(idx);
                else
                    active.Add(currentRequest);
            }

            return active.ToArray();
        }
    }

    /// <summary>
    ///     取出一個「現在就可以放」的請求動作。
    /// </summary>
    /// <param name="actionId">找到的動作 ID。</param>
    /// <returns>是否有找到。</returns>
    public static bool TryGetRequestedAction(out uint actionId)
    {
        actionId = default;

        // 🔴 鎖內只拍快照；CanWeave()／ActionReady() 會摸原生狀態與遊戲設定，
        //    絕不可以在鎖內做。
        var requests = GetRequestedActions();
        if (requests.Length == 0)
            return false;

        // 📌 刻意<b>不</b>在選中之後把那筆請求移除：改動前也沒有移除，請求是靠
        //    自己的 Deadline 過期的。移除會讓每筆請求只生效一次，那是行為變更。
        foreach (var actionRequest in requests)
        {
            if (actionRequest.IsGCD != null)
            {
                if (CustomComboFunctions.CanWeave() == actionRequest.IsGCD.Value)
                    continue;
            }

            var action = actionRequest.Descriptor;
            if (action.ActionType == ActionType.Action)
            {
                if (CustomComboFunctions.ActionReady(action.ActionID))
                {
                    actionId = action.ActionID;
                    return true;
                }
            }
            else
            {
                if (EzThrottler.Throttle("InformUnsupportedIPC"))
                    PluginLog.Warning(
                        $"[Wrath IPC] 目前只支援請求 ActionType.Action 類型的動作（收到：{action}）");
            }
        }

        return false;
    }
}
