#region

using System;
using System.Collections.Generic;
using ECommons.DalamudServices;
using ECommons.Logging;
using WrathCombo.Data.Conflicts;
using WrathCombo.Extensions;
using WrathCombo.Services;
using static WrathCombo.CustomComboNS.Functions.CustomComboFunctions;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     依 BossMod(Reborn) 預告的下一筆全體傷害危險度，決定自動團減要不要放。
/// </summary>
/// <remarks>
///     契約：開關關閉、不在框架執行緒、BMR 沒有危險度端點（不在／舊版／呼叫失敗）或沒有全體傷害預告＝完全照舊（呼叫端的舊判斷，預設 <c>RaidWideCasting()</c>）。
///     有預告時只看預告：命中前 [0.8 秒＋讀條, min(持續−1, 提前量)＋讀條] 內才考慮；高全放、低不放、中與未知只放冷卻 ≤60 秒的 oGCD，
///     帶傷害屬性偏向的（昏亂）只在來源技能屬性對上時放；進階開關打開時中危也放大招與 GCD 盾。
///     持續秒數由呼叫端給各等級最短值：技能放下去之前讀不到狀態。
/// </remarks>
internal static class RaidwideMitigationPolicy
{
    internal enum DamageKind : byte
    {
        Unknown,
        Physical,
        Magic,
    }

    internal enum Verdict : byte
    {
        NotDue,
        Use,
        Skip,
    }

    internal const float MinLeadSeconds = 0.8f;
    internal const float CheapCooldownSeconds = 60f;

    #region 純函式

    /// <summary>危險度（0 未知、1 低、2 中、3 高；其他值當未知）是否允許這一招。</summary>
    internal static bool SeverityAllows(int severity, float cooldownSeconds, bool isGcd,
        DamageKind bias, DamageKind incoming, bool mediumUsesMajor)
    {
        if (severity == 3)
            return true;
        if (severity == 1)
            return false;
        if (mediumUsesMajor)
            return true;
        if (isGcd)
            return false;
        if (bias != DamageKind.Unknown)
            return incoming == bias;
        return cooldownSeconds <= CheapCooldownSeconds;
    }

    /// <summary>命中前剩餘秒數是否落在這一招的時間窗內；窗口上限小於下限時恆為假。</summary>
    internal static bool InWindow(float secondsToHit, float durationSeconds, float castSeconds, float leadSeconds)
    {
        var cast = Math.Max(0f, castSeconds);
        var tmin = MinLeadSeconds + cast;
        var tmax = Math.Min(durationSeconds - 1f, leadSeconds) + cast;
        return secondsToHit >= tmin && secondsToHit <= tmax;
    }

    /// <summary>
    ///     判斷本體。<paramref name="severity" /> 或 <paramref name="secondsToHit" /> 為 <see langword="null" />＝沒有危險度資訊，
    ///     只看 <paramref name="legacyDue" />（現行行為）。
    /// </summary>
    internal static Verdict Decide(int? severity, float? secondsToHit, bool legacyDue,
        float cooldownSeconds, float durationSeconds, float castSeconds, bool isGcd,
        DamageKind bias, DamageKind incoming, float leadSeconds, bool mediumUsesMajor)
    {
        if (severity is not { } sev || secondsToHit is not { } t)
            return legacyDue ? Verdict.Use : Verdict.NotDue;
        if (!InWindow(t, durationSeconds, castSeconds, leadSeconds))
            return Verdict.NotDue;
        return SeverityAllows(sev, cooldownSeconds, isGcd, bias, incoming, mediumUsesMajor)
            ? Verdict.Use
            : Verdict.Skip;
    }

    /// <summary>Action 表 AttackType：1～4 斬突打射＝物理、5＝魔法，其餘（0、吐息、音波…）不知道。</summary>
    internal static DamageKind KindOfAttackType(uint attackTypeRowId) => attackTypeRowId switch
    {
        1 or 2 or 3 or 4 => DamageKind.Physical,
        5 => DamageKind.Magic,
        _ => DamageKind.Unknown,
    };

    internal static string SeverityName(int severity) => severity switch
    {
        1 => "低",
        2 => "中",
        3 => "高",
        _ => "未知（照中）",
    };

    #endregion

    #region 每幀快照

    private readonly record struct Snapshot(bool Active, int Severity, float SecondsToHit, uint SourceAction, DamageKind Incoming);

    private static DateTime _cachedForFrame;
    private static bool _primed;
    private static Snapshot _cached;

    private static Snapshot Current()
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread)
            return default;

        var frame = Svc.Framework.LastUpdate;
        if (_primed && frame == _cachedForFrame)
            return _cached;

        _primed = true;
        _cachedForFrame = frame;
        _cached = Build();
        return _cached;
    }

    private static Snapshot Build()
    {
        if (!Service.Configuration.BossModRaidwideSeverity)
            return default;

        if (RaidwideForecast.NextRaidwideIn() is not { } seconds)
            return default;

        var source =
            ConflictingPluginsChecks.BossModReborn.IpcAvailable
                ? ConflictingPluginsChecks.BossModReborn
                : ConflictingPluginsChecks.BossMod.IpcAvailable
                    ? ConflictingPluginsChecks.BossMod
                    : null;

        if (source?.NextRaidwideSeverity() is not { } severity)
            return default;

        var sourceAction = source.NextRaidwideSourceAction() ?? 0;
        var incoming = sourceAction != 0 && ActionWatching.ActionSheet.TryGetValue(sourceAction, out var row)
            ? KindOfAttackType(row.AttackType.RowId)
            : DamageKind.Unknown;
        return new Snapshot(true, severity, seconds, sourceAction, incoming);
    }

    #endregion

    /// <summary>這一幀是否由危險度排程接管（否＝呼叫端照舊判斷）。</summary>
    internal static bool Active => Current().Active;

    /// <summary>全體傷害提前量：設定值夾在 1～15 秒（只有手改設定檔才會超出滑桿範圍）。</summary>
    internal static float LeadSeconds => Math.Clamp(Service.Configuration.BossModRaidwideLeadSeconds, 1f, 15f);

    /// <summary>
    ///     取代 <c>RaidWideCasting()</c> 的團減時機判斷；放在條件鏈最後（跳過時會寫 log）。
    /// </summary>
    /// <param name="action">實際要放的技能（冷卻與 GCD 與否從 Action 表讀）。</param>
    /// <param name="durationSeconds">效果持續秒數（各等級最短值）。</param>
    /// <param name="legacy">沒有危險度資訊時的舊判斷；<see langword="null" />＝<c>RaidWideCasting()</c>。</param>
    internal static bool Due(uint action, float durationSeconds, DamageKind bias = DamageKind.Unknown,
        float castSeconds = 0f, Func<bool>? legacy = null, float? leadSeconds = null)
    {
        var snap = Current();
        if (!snap.Active)
            return legacy?.Invoke() ?? RaidWideCasting();

        var verdict = Decide(snap.Severity, snap.SecondsToHit, false,
            RecastSeconds(action), durationSeconds, castSeconds, IsGcd(action),
            bias, snap.Incoming, leadSeconds ?? LeadSeconds,
            Service.Configuration.BossModRaidwideMediumUsesMajor);

        if (verdict != Verdict.NotDue)
            Log(action, verdict, snap);
        return verdict == Verdict.Use;
    }

    // 查不到的技能當成大招（冷卻無限長），中危時寧可不放。
    private static float RecastSeconds(uint action) =>
        ActionWatching.ActionSheet.TryGetValue(action, out var row) ? row.Recast100ms * 0.1f : float.MaxValue;

    private static bool IsGcd(uint action) =>
        ActionWatching.GetAttackType(action) != ActionWatching.ActionAttackType.Ability;

    // 連段每幀問好幾次；同一招同一種結論 15 秒內只記一行。
    private static readonly Dictionary<uint, (Verdict Verdict, long At)> LoggedAt = [];

    private static void Log(uint action, Verdict verdict, Snapshot snap)
    {
        var now = Environment.TickCount64;
        if (LoggedAt.TryGetValue(action, out var last) && last.Verdict == verdict && now - last.At < 15_000)
            return;
        LoggedAt[action] = (verdict, now);

        var kind = snap.Incoming switch
        {
            DamageKind.Physical => "物理",
            DamageKind.Magic => "魔法",
            _ => "屬性不明",
        };
        PluginLog.Information(
            $"[MitPolicy] 下一發全體傷害判成{SeverityName(snap.Severity)}（BMR 預告 {snap.SecondsToHit:0.0} 秒後，" +
            $"來源 {snap.SourceAction}，{kind}），{(verdict == Verdict.Use ? "開" : "不開")}{action.ActionName()}");
    }
}
