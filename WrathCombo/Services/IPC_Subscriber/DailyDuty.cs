#region

using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Logging;

#endregion

namespace WrathCombo.Services.IPC_Subscriber;

/// <summary>
///     讀 DailyDuty 通緝令「自動開打」此刻要求開打的目標（<c>DailyDuty.HuntAssist.EngageTargetId</c>，提供端 <c>Func&lt;ulong&gt;</c>）。
/// </summary>
/// <remarks>
///     只在框架執行緒呼叫，同一幀最多問一次。DailyDuty 沒載入、端點不存在、型別不合或擲例外一律當 0（沒有要求），並退避一段時間不再問。
///     刻意不走 EzIPC：<c>SafeWrapper.IPCException</c> 會把端點不存在吞成 0，分不出「沒有要求」與「舊版沒有這個端點」。
/// </remarks>
internal static class DailyDutyIPC
{
    internal const string EngageTargetIdTag = "DailyDuty.HuntAssist.EngageTargetId";
    private const long MissingBackoffMs = 5_000;
    private const long FailedBackoffMs = 60_000;

    private static ICallGateSubscriber<ulong>? _engageTargetId;
    private static DateTime _frame;
    private static bool _primed;
    private static ulong _value;

    private static IExposedPlugin? _plugin;
    private static long _lookupAt;
    private static long _blockedUntil;
    private static long _missingLogNextAt;
    private static long _failedLogNextAt;

    /// <summary> DailyDuty 此刻要求開打的目標 GameObjectId；沒有要求或問不到回 0。 </summary>
    internal static ulong HuntEngageTargetId()
    {
        if (!Svc.Framework.IsInFrameworkUpdateThread)
            return 0;
        var frame = Svc.Framework.LastUpdate;
        if (_primed && frame == _frame)
            return _value;
        _primed = true;
        _frame = frame;
        _value = Query(Environment.TickCount64);
        return _value;
    }

    private static ulong Query(long now)
    {
        if (now < _blockedUntil || !Loaded(now))
            return 0;

        try
        {
            _engageTargetId ??= Svc.PluginInterface.GetIpcSubscriber<ulong>(EngageTargetIdTag);
            return _engageTargetId.InvokeFunc();
        }
        catch (IpcNotReadyError)
        {
            _blockedUntil = now + MissingBackoffMs;
            if (now >= _missingLogNextAt)
            {
                _missingLogNextAt = now + 600_000;
                PluginLog.Information($"[AutoRotation][通緝目標] DailyDuty 沒有 {EngageTargetIdTag} 端點（版本太舊或尚未就緒），DailyDuty 通緝目標繞過暫時不會作用。");
            }
        }
        catch (Exception e)
        {
            // IpcTypeMismatchError 與對方擲出的例外都走這裡。
            _blockedUntil = now + FailedBackoffMs;
            if (now >= _failedLogNextAt)
            {
                _failedLogNextAt = now + FailedBackoffMs;
                PluginLog.Information($"[AutoRotation][通緝目標] 呼叫 DailyDuty 的 {EngageTargetIdTag} 失敗，{FailedBackoffMs / 1000} 秒內視同沒有要求：" + e.ToStringFull());
            }
        }

        return 0;
    }

    // 別的插件在不在即時讀 IsLoaded；只有找哪一個插件的列舉每 5 秒做一次。
    private static bool Loaded(long now)
    {
        if (now >= _lookupAt)
        {
            _lookupAt = now + 5_000;
            _plugin = null;
            foreach (var p in Svc.PluginInterface.InstalledPlugins)
            {
                if (!string.Equals(p.InternalName, "DailyDuty", StringComparison.OrdinalIgnoreCase))
                    continue;
                _plugin = p;
                if (p.IsLoaded)
                    break;
            }
        }
        return _plugin is { IsLoaded: true };
    }
}
