#region

using System;
using ECommons.DalamudServices;
using WrathCombo.Data.Conflicts;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     BossMod(Reborn) 的「下一次全體傷害還有幾秒」，帶單幀快取。目前只給赤魔的昏亂／抗死選項用。
/// </summary>
/// <remarks>
///     刻意不併進 <c>RaidWideCasting()</c>：治療職業與暗黑騎士靠它，改了會靜默改變他們的行為。
///     只在框架執行緒問 BMR，其他執行緒一律回「沒有預告」。
/// </remarks>
internal static class RaidwideForecast
{
    private static DateTime _cachedForFrame;
    private static bool _primed;
    private static float? _cached;

    /// <returns>秒數；<see langword="null" />＝BMR 不可用、沒有預告、呼叫失敗或不在框架執行緒。</returns>
    internal static float? NextRaidwideIn()
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread)
            return null;

        var frame = Svc.Framework.LastUpdate;
        if (_primed && frame == _cachedForFrame)
            return _cached;

        _primed = true;
        _cachedForFrame = frame;

        // BossModReborn 優先；兩個實例訂的是同一個標籤。
        var source =
            ConflictingPluginsChecks.BossModReborn.IpcAvailable
                ? ConflictingPluginsChecks.BossModReborn
                : ConflictingPluginsChecks.BossMod.IpcAvailable
                    ? ConflictingPluginsChecks.BossMod
                    : null;

        _cached = Sanitize(source?.NextRaidwideDamageIn());
        return _cached;
    }

    /// <summary>BMR 以 <see cref="float.MaxValue" /> 表示沒有預告；非有限值也當沒有。</summary>
    internal static float? Sanitize(float? raw) =>
        raw is { } s && float.IsFinite(s) && s < float.MaxValue ? s : null;

    /// <summary>
    ///     有預告：0 ≤ 剩餘秒數 ≤ <paramref name="leadSeconds" /> 才算將至（已過觸發點就來不及了）。
    ///     沒有預告回 <see langword="null" />，由呼叫端退回詠唱判斷。
    /// </summary>
    internal static bool? Verdict(float? forecast, float leadSeconds) =>
        forecast is { } s ? s >= 0f && s <= leadSeconds : null;
}
