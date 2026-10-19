using System;
using Dalamud.Hooking;
using ECommons.DalamudServices;

namespace WrathCombo.Core;

/// <summary>
///     載入時建立遊戲函式 hook 的不擲版本：解不到就回 <see langword="null" />，
///     只停掉該項功能，插件照常載入。
/// </summary>
/// <remarks>
///     每個缺項只在載入時印一行 Warning，並記進 <see cref="Lost" /> 給設定視窗顯示。
///     可從任何執行緒呼叫（插件建構子與型別初始化都不在框架執行緒上）。
/// </remarks>
internal static class GameFunctionGuard
{
    private static readonly object Gate = new();
    private static volatile string[] _lost = [];

    /// <summary> 停用項目的說明，一項一則；沒有缺項時是空陣列。回傳的陣列不會再被改寫。 </summary>
    internal static string[] Lost => _lost;

    /// <param name="name"> 遊戲函式名，寫進 log。 </param>
    /// <param name="lost"> 少了它會停掉什麼，給使用者看。 </param>
    internal static Hook<T>? HookFromAddress<T>(nint address, T detour, string name, string lost)
        where T : Delegate
    {
        if (address == 0)
        {
            Report(name, "位址是 0", lost);
            return null;
        }

        try
        {
            return Svc.Hook.HookFromAddress(address, detour);
        }
        catch (Exception ex)
        {
            Report(name, ex.Message, lost);
            return null;
        }
    }

    /// <inheritdoc cref="HookFromAddress{T}" />
    internal static Hook<T>? HookFromSignature<T>(string signature, T detour, string name, string lost)
        where T : Delegate
    {
        try
        {
            return Svc.Hook.HookFromSignature(signature, detour);
        }
        catch (Exception ex)
        {
            Report(name, ex.Message, lost);
            return null;
        }
    }

    private static void Report(string name, string reason, string lost)
    {
        Svc.Log.Warning($"遊戲函式 {name} 在這個遊戲版本解不到（{reason}），已停用：{lost}");
        lock (Gate)
            _lost = [.. _lost, lost];
    }
}
