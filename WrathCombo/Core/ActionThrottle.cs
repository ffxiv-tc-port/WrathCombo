#region

using System;
using System.Collections.Generic;

#endregion

namespace WrathCombo.Core;

/// <summary>
///     以 actionID 為鍵的節流器，語意與 <c>EzThrottler.Throttle(name, ms)</c> 相同：
///     第一次必定放行，回 <see langword="true" /> 的那一次把到期時刻重新上緊。
/// </summary>
/// <remarks>
///     快捷列 hook 每幀對每一格呼叫；用字串鍵的 <c>EzThrottler</c> 每次都要組一個新字串。
///     唯一的呼叫端（快捷列 hook）已在入口用 <see cref="FrameworkThreadGate" /> 擋掉框架執行緒以外的呼叫；
///     鎖留著當保險，鎖內只做字典運算。
/// </remarks>
internal sealed class ActionThrottle
{
    private readonly Dictionary<uint, long> _expiry = [];
    private readonly object _gate = new();

    internal bool Throttle(uint actionID, int milliseconds) =>
        Throttle(actionID, milliseconds, Environment.TickCount64);

    internal bool Throttle(uint actionID, int milliseconds, long now)
    {
        lock (_gate)
        {
            if (_expiry.TryGetValue(actionID, out var expiresAt) && now <= expiresAt)
                return false;

            _expiry[actionID] = now + milliseconds;
            return true;
        }
    }
}
