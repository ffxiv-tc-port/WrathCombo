#region

using ECommons.EzIpcManager;
using System.Diagnostics.CodeAnalysis;
using WrathCombo.CustomComboNS.Functions;

#endregion

namespace WrathCombo.Services.IPC;

/// <summary>
///     不需要租約（lease）就能呼叫的唯讀輔助端點。
/// </summary>
/// <remarks>
///     這些端點只是把 <see cref="CustomComboFunctions" /> 既有的判斷式原樣轉出去，
///     不改變 Wrath 自己的任何行為，因此不走 <see cref="Helper.CheckForBailConditionsAtSetTime" />。
/// </remarks>
public partial class Provider
{
    /// <summary>
    ///     檢查現在插入一個動作會不會卡到 GCD。
    /// </summary>
    /// <param name="estimatedWeaveTime">
    ///     要插入的動作預估會佔用的動作鎖時間（秒）。<br />
    ///     傳 <see langword="null" /> 時使用預設的動作鎖時間
    ///     （<see cref="CustomComboFunctions.BaseAnimationLock" />）。
    /// </param>
    /// <returns>現在能不能插入這個動作。</returns>
    /// <remarks>
    ///     會遵守使用者在 Wrath 裡設定的「每個插入視窗最多幾個插入技」，
    ///     所以就算時間還很充裕，只要使用者把上限調低就可能回 <see langword="false" />。
    /// </remarks>
    [EzIPC]
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public bool CanWeave(float? estimatedWeaveTime)
    {
        estimatedWeaveTime ??= CustomComboFunctions.BaseAnimationLock;
        return CustomComboFunctions.CanWeave(estimatedWeaveTime.Value);
    }

    /// <summary>
    ///     檢查現在是不是落在「延後插入」的視窗裡。
    /// </summary>
    /// <param name="weaveStart">
    ///     視窗開始時的 GCD 剩餘秒數；傳 <see langword="null" /> 時為 <c>1.25</c>。<br />
    ///     不能大於 GCD 的一半，超過會被夾到一半。
    /// </param>
    /// <param name="weaveEnd">
    ///     視窗結束時的 GCD 剩餘秒數；傳 <see langword="null" /> 時使用預設的動作鎖時間。
    /// </param>
    /// <returns>現在是否落在指定的延後插入視窗內。</returns>
    /// <remarks>
    ///     與 <see cref="CanWeave" /> 一樣會遵守使用者設定的每視窗插入技上限。
    /// </remarks>
    [EzIPC]
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public bool CanDelayedWeave(float? weaveStart, float? weaveEnd)
    {
        weaveStart ??= 1.25f;
        weaveEnd ??= CustomComboFunctions.BaseAnimationLock;
        return CustomComboFunctions.CanDelayedWeave(weaveStart.Value, weaveEnd.Value);
    }

    /// <summary>
    ///     檢查某個動作現在是否可用（等級、冷卻、解鎖狀態都納入）。
    /// </summary>
    /// <param name="actionId">動作 ID。</param>
    /// <param name="recastCheck">
    ///     是否連「重新使用中」也算不可用；傳 <see langword="null" /> 時為 <see langword="false" />。
    /// </param>
    /// <param name="castCheck">
    ///     是否連「詠唱中」也算不可用；傳 <see langword="null" /> 時為 <see langword="false" />。
    /// </param>
    /// <returns>這個動作現在是否可用。</returns>
    [EzIPC]
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public bool ActionReady(uint actionId, bool? recastCheck, bool? castCheck)
    {
        recastCheck ??= false;
        castCheck ??= false;
        return CustomComboFunctions.ActionReady(actionId, recastCheck.Value, castCheck.Value);
    }
}
