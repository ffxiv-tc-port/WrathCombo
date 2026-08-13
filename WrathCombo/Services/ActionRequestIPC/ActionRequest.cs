using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Numerics;

namespace WrathCombo.Services.ActionRequestIPC;

/// <summary>
///     一筆由其他外掛透過 IPC 送進來的「請求使用動作」或「請求封鎖動作」。
/// </summary>
public readonly record struct ActionRequest
{
    /// <summary>
    ///     這筆請求對應的動作。
    /// </summary>
    public readonly ActionDescriptor Descriptor;

    /// <summary>
    ///     這筆請求的有效期限，單位與 <see cref="Environment.TickCount64" /> 相同。
    /// </summary>
    public readonly long Deadline;

    /// <summary>
    ///     指定的目標物件 ID；為 0 時代表「照原樣使用」。目前尚未使用。
    /// </summary>
    public readonly uint TargetEntityID;

    /// <summary>
    ///     未指定 <see cref="TargetEntityID" /> 時，地面指定類動作要用的座標。目前尚未使用。
    /// </summary>
    public readonly Vector3 TargetLocation;

    /// <summary>
    ///     要在哪個時機施放：<br />
    ///     <see langword="true" /> —— 只在 GCD 視窗施放。<br />
    ///     <see langword="false" /> —— 只在插入技（oGCD）視窗施放。<br />
    ///     <see langword="null" /> —— 任何時機、能放就放。
    /// </summary>
    public readonly bool? IsGCD;

    public ActionRequest(ActionType actionType, uint actionID, long deadline, bool? isGCD) : this()
    {
        Descriptor = new ActionDescriptor(actionType, actionID);
        Deadline = deadline;
        IsGCD = isGCD;
    }

    public ActionRequest(ActionDescriptor descriptor, long deadline, bool? isGCD) : this()
    {
        Descriptor = descriptor;
        Deadline = deadline;
        IsGCD = isGCD;
    }

    /// <summary>
    ///     這筆請求是否還在有效期限內。
    /// </summary>
    public bool IsActive => Environment.TickCount64 < Deadline;
}
