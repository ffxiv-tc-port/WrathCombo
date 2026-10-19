using ECommons;
using ECommons.EzIpcManager;
using ECommons.GameHelpers;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using System;
using System.Collections.Generic;
using System.Linq;
#nullable disable

namespace WrathCombo.Services.IPC_Subscriber;

internal static class OrbwalkerIPC
{
    private static EzIPCDisposalToken[] _disposalTokens = EzIPC.Init(typeof(OrbwalkerIPC), "Orbwalker", SafeWrapper.IPCException);

    internal static bool IsEnabled => InstalledVersion >= _validVersion;

    private static bool _ipcAvailable;
    private static bool _ipcAvailablePrimed;
    private static long _ipcAvailableCheckedAt;

    // IsEnabled 的 2 秒快取版，給每次繪製／每次出招前都會問的路徑用（IsEnabled 每次都反射走訪已安裝插件清單）。
    // 刻意不訂閱插件清單變動：對方卸載後端點跟著退訂，CanOrbwalk 的其餘條件自己會回 false。
    // 會解參 IPC 回傳值的呼叫端要用 IsEnabled：快取窗內端點可能已經不在，包裝回的是 null。
    internal static bool IpcAvailable
    {
        get
        {
            var now = Environment.TickCount64;
            if (!_ipcAvailablePrimed || now - _ipcAvailableCheckedAt >= 2000)
            {
                _ipcAvailablePrimed = true;
                _ipcAvailableCheckedAt = now;
                _ipcAvailable = IsEnabled;
            }

            return _ipcAvailable;
        }
    }

    internal static Version InstalledVersion => DalamudReflector.TryGetDalamudPlugin("Orbwalker", out var dalamudPlugin, false, true) ? dalamudPlugin.GetType().Assembly.GetName().Version : new Version(0, 0, 0, 0);
    private static Version _validVersion = new(1,0,1,1);

#pragma warning disable CS0649, CS8618 // Complaints of the method
    [EzIPC] public static readonly Func<bool> PluginEnabled;
    [EzIPC] public static readonly Func<bool> MovementLocked;
    [EzIPC] public static readonly Func<bool> IsSlideWindowAuto; //True for Automatic, False for Manual
    [EzIPC] public static readonly Func<bool> OrbwalkingMode; //True for Slidecast, False for Slidelock
    [EzIPC] public static readonly Func<bool> BufferEnabled;
    [EzIPC] public static readonly Func<bool> ControllerModeEnabled;
    [EzIPC] public static readonly Func<bool> MouseButtonReleaseEnabled;
    [EzIPC] public static readonly Func<bool> PvPEnabled;
    [EzIPC] public static readonly Func<List<uint>> EnabledJobs;


    [EzIPC] public static Action<bool> SetPluginEnabled;
    [EzIPC] public static Action<bool> SetSlideAuto;
    [EzIPC] public static Action<bool> SetOrbwalkingMode;
    [EzIPC] public static Action<bool> SetBuffer;
    [EzIPC] public static Action<bool> SetControllerMode;
    [EzIPC] public static Action<bool> SetMouseButtonRelease;
    [EzIPC] public static Action<bool> SetPvP;
    [EzIPC] public static Action<uint, bool> SetEnabledJob;
#pragma warning restore CS8618, CS0649

    public static bool CanOrbwalk => IpcAvailable && PluginEnabled() && !MouseMoving && EnabledJobs().Any(x => x == (uint)Player.Job);

    private static unsafe InputManager.MouseButtonHoldState* _state => InputManager.GetMouseButtonHoldState();
    private static unsafe bool BothMousebuttonsHeld => _state->HasFlag(InputManager.MouseButtonHoldState.Left) && _state->HasFlag(InputManager.MouseButtonHoldState.Right);
        
    public static bool MouseMoving => MouseButtonReleaseEnabled() && BothMousebuttonsHeld;

    internal static void Dispose()
    {
        foreach (var token in _disposalTokens)
        {
            try
            {
                token.Dispose();
            }
            catch (Exception ex)
            {
                ex.Log();
            }
        }
    }
}