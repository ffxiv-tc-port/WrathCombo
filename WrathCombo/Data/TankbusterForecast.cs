#region

using System;
using ECommons.DalamudServices;
using ECommons.Logging;
using WrathCombo.Data.Conflicts;
using WrathCombo.Services;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     BossMod(Reborn) 的「下一發打本機玩家的死刑還有幾秒」，帶單幀快取；給四個坦克的短減傷預判用。
/// </summary>
/// <remarks>
///     契約：沒有預告（BMR 不在、舊版沒有端點、呼叫失敗、不在框架執行緒）一律 <see cref="PreMitigationDue" /> 為假＝照舊只看血量。
///     只用 <c>Hints.NextTankbusterOnPlayerIn</c>：<c>Hints.NextTankbusterDamageIn</c> 不分打誰，會把打副坦的也當成打自己。
/// </remarks>
internal static class TankbusterForecast
{
    private static DateTime _cachedForFrame;
    private static bool _primed;
    private static float? _cached;
    private static long _loggedAt;
    private static uint _loggedAction;

    /// <returns>秒數；<see langword="null" />＝沒有打本機玩家的死刑預告。</returns>
    internal static float? NextTankbusterOnMeIn()
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread)
            return null;

        var frame = Svc.Framework.LastUpdate;
        if (_primed && frame == _cachedForFrame)
            return _cached;

        _primed = true;
        _cachedForFrame = frame;

        var source =
            ConflictingPluginsChecks.BossModReborn.IpcAvailable
                ? ConflictingPluginsChecks.BossModReborn
                : ConflictingPluginsChecks.BossMod.IpcAvailable
                    ? ConflictingPluginsChecks.BossMod
                    : null;

        _cached = RaidwideForecast.Sanitize(source?.NextTankbusterOnPlayerIn());
        return _cached;
    }

    /// <summary>提前量：設定值夾在 0.5～10 秒（只有手改設定檔才會超出滑桿範圍）。</summary>
    internal static float LeadSeconds =>
        Math.Clamp(Service.Configuration.BossModTankbusterLeadSeconds, 0.5f, 10f);

    /// <summary>有預告且 0 ≤ 剩餘秒數 ≤ 提前量。純函式，判斷本體。</summary>
    internal static bool Due(float? forecast, float leadSeconds) =>
        forecast is { } s && s >= 0f && s <= leadSeconds;

    /// <summary>開關打開、而且打本機玩家的死刑在提前量內：該開一個短減傷。</summary>
    internal static bool PreMitigationDue() =>
        Service.Configuration.BossModTankbusterMitigation && Due(NextTankbusterOnMeIn(), LeadSeconds);

    /// <summary>回傳 <paramref name="action" />，並記一行 Information（同一技能 5 秒內只記一次：連段每幀都會被問）。</summary>
    internal static uint Use(uint action)
    {
        var now = Environment.TickCount64;
        if (action != _loggedAction || now - _loggedAt >= 5_000)
        {
            _loggedAction = action;
            _loggedAt = now;
            PluginLog.Information(
                $"[WrathCombo][死刑預判減傷] BMR 預告死刑 {_cached ?? -1f:f1} 秒後打你，開短減傷 {action}");
        }
        return action;
    }
}
