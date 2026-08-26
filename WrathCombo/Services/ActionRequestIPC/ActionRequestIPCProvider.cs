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
    ///     其他外掛請求施放的動作。
    /// </summary>
    public static List<ActionRequest> ActionRequests = [];

    /// <summary>
    ///     其他外掛請求暫時封鎖（加上人工冷卻）的動作。
    /// </summary>
    public static List<ActionRequest> ActionBlacklist = [];

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
        ActionRequests.Clear();
        ActionBlacklist.Clear();
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
        ActionBlacklist.Add(new ActionRequest(descriptor, Environment.TickCount64 + timeMs, default));
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
        if (ActionBlacklist.Count == 0)
            return 0f;

        var d = new ActionDescriptor(actionType, actionID);
        var currentTick = Environment.TickCount64;
        var maxDeadline = 0L;

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
        ActionRequests.Add(new ActionRequest(descriptor, Environment.TickCount64 + timeMs, isGcd));
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
        ActionRequests.RemoveAll(item => item.Descriptor == descriptor);
    }

    /// <summary>
    ///     清空所有施放請求。
    /// </summary>
    [EzIPC]
    public static void ResetAllRequests()
    {
        ActionRequests.Clear();
    }

    /// <summary>
    ///     列舉目前仍然有效的施放請求，順便清掉過期的。
    /// </summary>
    public static IEnumerable<ActionRequest> GetRequestedActions()
    {
        if (ActionRequests.Count == 0)
            yield break;

        for (var idx = ActionRequests.Count - 1; idx >= 0; idx--)
        {
            var currentRequest = ActionRequests[idx];
            if (!currentRequest.IsActive)
            {
                ActionRequests.RemoveAt(idx);
            }
            else
            {
                yield return currentRequest;
            }
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

        if (ActionRequests.Count == 0)
            return false;

        foreach (var actionRequest in GetRequestedActions())
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
