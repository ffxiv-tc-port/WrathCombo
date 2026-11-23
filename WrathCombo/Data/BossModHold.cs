#region

using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc.Exceptions;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Logging;
using WrathCombo.Data.Conflicts;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     BossMod(Reborn) 的停手機制期間（熱病、加速度炸彈、舉起的聖盾防禦等：出任何招都會出事），暫停自動循環。
/// </summary>
/// <remarks>
///     訊號＝BMR 的 <c>Hints.SpecialModeType</c>／<c>Hints.SpecialModeIn</c>（它的 <c>AIHints.ImminentSpecialMode</c>：每幀重算，只留生效時刻最早的一種）。
///     只有 Pyretic 要停（熱病、加速度炸彈、舉起的聖盾等，生效那刻不可出招、移動、詠唱）；不可移動、要保持移動、方向錯亂都不限制出招。
///     只看模式與剩餘秒數，不依副本或狀態 id 另判；生效時刻可能在十幾秒後（炸彈到期），也可能就是現在（舉盾期間）。
///     門檻同 BMR 自己擋技能與取消詠唱的預設（<c>PyreticThreshold</c> 1 秒）：剩不到 1 秒起停，生效後 BMR 還在回報就繼續停。
///     BMR 沒載入、端點不存在、型別不合或擲例外一律不暫停。只在框架執行緒呼叫，同一幀只問一次。
/// </remarks>
internal static class BossModHold
{
    // BMR AIHints.SpecialMode 的整數值（IPC 傳的是 (int)mode，0＝沒有）。
    internal const int ModeNormal = 0, ModePyretic = 1, ModeNoMovement = 2, ModeFreezing = 3, ModeMisdirection = 4;
    internal const float LeadSeconds = 1f;
    internal const long MaxHoldMs = 30_000;
    // 兩次判斷間隔超過這麼久（死亡、騎乘、關掉自動循環等期間不判斷）＝上限重新計時。
    internal const long GapMs = 1_000;
    private const long MissingBackoffMs = 5_000;
    private const long FailedBackoffMs = 60_000;
    private const long StartLogIntervalMs = 5_000;

    private static readonly HoldTracker Tracker = new(MaxHoldMs, GapMs);
    private static DateTime _frame;
    private static bool _primed;
    private static bool _paused;

    private static IExposedPlugin? _bmr;
    private static long _bmrLookupAt;
    private static long _queryBlockedUntil;
    private static string? _blockedWhy;
    private static long _missingLogNextAt;
    private static long _failedLogNextAt;
    private static long _startLogNextAt;
    private static int _startsSuppressed;
    private static bool _startLogged;

    /// <summary> BMR 這一刻要不要求停手：只看 Pyretic，剩不到 <see cref="LeadSeconds" /> 秒或已生效（負值）都算。 </summary>
    internal static bool Demands(int mode, float seconds) =>
        mode == ModePyretic && !float.IsNaN(seconds) && seconds < LeadSeconds;

    internal static string ModeName(int mode) => mode switch
    {
        ModeNormal => "沒有",
        ModePyretic => "停手機制（Pyretic：熱病、加速度炸彈等，不可出招、移動、詠唱）",
        ModeNoMovement => "不可移動（NoMovement，可出招）",
        ModeFreezing => "要保持移動（Freezing，可出招）",
        ModeMisdirection => "方向錯亂（Misdirection，可出招）",
        _ => $"未知模式 {mode}",
    };

    /// <returns> true＝這一輪自動循環整輪不跑。 </returns>
    internal static bool ShouldPause(bool enabled)
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread)
            return false;
        var frame = Svc.Framework.LastUpdate;
        if (_primed && frame == _frame)
            return _paused;
        _primed = true;
        _frame = frame;

        var now = Environment.TickCount64;
        int mode = ModeNormal;
        var seconds = float.MaxValue;
        string? why = null;
        if (!enabled)
            why = "已關閉「BossMod(Reborn) 要求停手時暫停自動循環」";
        else
            Query(now, out mode, out seconds, out why);

        var demanded = why is null && Demands(mode, seconds);
        _paused = Tracker.Update(demanded, now, out var ev);
        switch (ev)
        {
            case HoldEvent.Started:
                LogStarted(now, seconds);
                break;
            case HoldEvent.Ended:
                if (_startLogged)
                    PluginLog.Information(
                        $"[AutoRotation][BMR 停手] 恢復自動循環（暫停了 {Tracker.LastHeldMs / 1000f:f1} 秒）：" +
                        (why ?? EndReason(mode, seconds)));
                _startLogged = false;
                break;
            case HoldEvent.Capped:
                PluginLog.Information(
                    $"[AutoRotation][BMR 停手] BossMod(Reborn) 的停手要求已連續 {MaxHoldMs / 1000} 秒，視為異常、恢復自動循環；" +
                    "等它解除這次要求之後才會再暫停。");
                _startLogged = false;
                break;
        }
        return _paused;
    }

    private static string EndReason(int mode, float seconds) =>
        mode == ModePyretic
            ? $"BossMod(Reborn) 報的停手機制離生效還有 {seconds:f1} 秒"
            : mode == ModeNormal
                ? "BossMod(Reborn) 已解除停手要求"
                : $"BossMod(Reborn) 改報「{ModeName(mode)}」";

    // 短時間反覆暫停（例如 BMR 的要求一閃一閃）只記第一次，被略過的那幾次連同它們的恢復行都不記。
    private static void LogStarted(long now, float seconds)
    {
        if (now < _startLogNextAt)
        {
            _startsSuppressed++;
            _startLogged = false;
            return;
        }
        _startLogNextAt = now + StartLogIntervalMs;
        var when = seconds > 0f ? $"{seconds:f1} 秒後生效" : "生效中";
        var skipped = _startsSuppressed > 0 ? $"（前 {StartLogIntervalMs / 1000} 秒內另有 {_startsSuppressed} 次短暫停未記）" : "";
        _startsSuppressed = 0;
        _startLogged = true;
        PluginLog.Information(
            $"[AutoRotation][BMR 停手] 暫停自動循環：BossMod(Reborn) 回報「{ModeName(ModePyretic)}」{when}，" +
            $"期間不送任何自動技能（你自己按的技能不擋）{skipped}");
    }

    private static void Query(long now, out int mode, out float seconds, out string? why)
    {
        mode = ModeNormal;
        seconds = float.MaxValue;
        why = null;
        if (now < _queryBlockedUntil)
        {
            why = _blockedWhy;
            return;
        }
        if (!BmrLoaded(now))
        {
            why = "BossMod(Reborn) 沒載入";
            return;
        }

        var source = ConflictingPluginsChecks.BossModReborn;
        try
        {
            mode = source.SpecialModeTypeQuery();
            if (mode == ModePyretic)
                seconds = source.SpecialModeInQuery();
        }
        catch (IpcNotReadyError)
        {
            mode = ModeNormal;
            why = _blockedWhy = "BossMod(Reborn) 沒有 Hints.SpecialModeType／SpecialModeIn 端點（版本太舊或尚未就緒）";
            _queryBlockedUntil = now + MissingBackoffMs;
            if (now >= _missingLogNextAt)
            {
                _missingLogNextAt = now + 600_000;
                PluginLog.Information($"[AutoRotation][BMR 停手] {why}，遇到停手機制（熱病、加速度炸彈等）時自動循環不會自己暫停。");
            }
        }
        catch (Exception e)
        {
            // IpcTypeMismatchError 與對方擲出的例外都走這裡；SafeWrapper.None 不吞任何例外。
            mode = ModeNormal;
            why = _blockedWhy = "呼叫 BossMod(Reborn) 的停手提示失敗";
            _queryBlockedUntil = now + FailedBackoffMs;
            if (now >= _failedLogNextAt)
            {
                _failedLogNextAt = now + FailedBackoffMs;
                PluginLog.Information(
                    $"[AutoRotation][BMR 停手] {why}，{FailedBackoffMs / 1000} 秒內視同沒有要求、照常出招：" + e.ToStringFull());
            }
        }
    }

    // 別的插件在不在即時讀 IsLoaded；只有找哪一個插件的列舉每 5 秒做一次（重新安裝後最多晚 5 秒才認得新的那份）。
    private static bool BmrLoaded(long now)
    {
        if (now >= _bmrLookupAt)
        {
            _bmrLookupAt = now + 5_000;
            _bmr = null;
            foreach (var p in Svc.PluginInterface.InstalledPlugins)
            {
                if (p.InternalName is not ("BossModReborn" or "BossMod"))
                    continue;
                _bmr = p;
                if (p.IsLoaded)
                    break;
            }
        }
        return _bmr is { IsLoaded: true };
    }
}

internal enum HoldEvent : byte
{
    None = 0,
    Started,
    Ended,
    Capped,
}

/// <summary> 暫停的狀態機：開始、結束、連續超過上限就放掉，直到要求解除才重新武裝。不碰遊戲與 IPC。 </summary>
internal sealed class HoldTracker(long maxHoldMs, long gapMs)
{
    private bool _holding;
    private bool _capped;
    private long _episodeStart;
    private long _lastAt;

    internal bool Holding => _holding;

    /// <summary> 最近一次結束（或被上限放掉）時，那段暫停持續了多久。 </summary>
    internal long LastHeldMs { get; private set; }

    /// <returns> true＝這一刻要暫停。 </returns>
    internal bool Update(bool demanded, long now, out HoldEvent ev)
    {
        ev = HoldEvent.None;
        var gap = now - _lastAt > gapMs;
        _lastAt = now;
        if (!demanded)
        {
            _capped = false;
            if (_holding)
            {
                _holding = false;
                LastHeldMs = now - _episodeStart;
                ev = HoldEvent.Ended;
            }
            return false;
        }
        if (_capped)
            return false;
        if (!_holding)
        {
            _holding = true;
            _episodeStart = now;
            ev = HoldEvent.Started;
            return true;
        }
        if (gap)
            _episodeStart = now;
        if (now - _episodeStart > maxHoldMs)
        {
            _holding = false;
            _capped = true;
            LastHeldMs = now - _episodeStart;
            ev = HoldEvent.Capped;
            return false;
        }
        return true;
    }
}
