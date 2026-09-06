#region

using System;
using System.Collections.Generic;

#endregion

namespace WrathCombo.Services.IPC;

/// <summary>
///     給 IPC 端點後端用的極簡節流器：自帶字典、自帶鎖。
/// </summary>
/// <remarks>
///     🔴🔴 <b>為什麼不用 <c>ECommons</c> 的 <c>EzThrottler</c></b>：它是整個外掛
///     共用的<b>靜態裸 <c>Dictionary</c></b>，零同步（<c>EzThrottler{T}.Throttle</c>
///     直接 <c>ContainsKey</c> ＋ 索引指派）。IPC 端點的實作跑在<b>承租外掛的
///     執行緒</b>上，而這個外掛自己的戰鬥碼每一幀都在 framework 執行緒上打同一個
///     <c>EzThrottler</c> ⇒ 失敗形式不是「節流失準」，而是<b>那個字典本身壞掉</b>，
///     還會連帶弄壞這個外掛<b>所有</b>模組的節流。<br />
///     🔴 每個持有者各自 <c>new</c> 一個：key 的命名空間跟著實例走，不會再和別的
///     模組互撞 —— 那是 <c>EzThrottler</c> 的另一個既有問題（它的 key 是全域的，
///     而且一旦建立就永久持久）。<br />
///     🔴 <b>這把鎖是葉節點</b>：鎖內只做字典運算就出來，絕不呼叫 ImGui、
///     絕不做檔案或網路 I/O、絕不呼叫別的外掛 ⇒ 它不可能參與死結。
/// </remarks>
internal sealed class IpcThrottle
{
    /// <summary>
    ///     保護 <see cref="_expiry" /> 的鎖。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     每個 key 的節流到期時刻（<c>Environment.TickCount64</c> 的絕對值）。
    /// </summary>
    /// <remarks>
    ///     ⚠️ 存的是<b>到期時刻</b>而不是「上次放行的時刻」，這是為了和
    ///     <c>EzThrottler</c> 逐字同語意，見 <see cref="Throttle(string,long)" />。
    /// </remarks>
    private readonly Dictionary<string, long> _expiry = [];

    /// <summary>
    ///     同一個 <paramref name="key" /> 每 <paramref name="windowMs" /> 毫秒只
    ///     放行一次；第一次必定放行。
    /// </summary>
    /// <remarks>
    ///     語意<b>逐字對齊</b> <c>ECommons</c> 的
    ///     <c>EzThrottler{T}.Throttle(name, ms, rethrottle: false)</c>：<br />
    ///     · key 還沒見過 → 記下到期時刻，回 <see langword="true" />。<br />
    ///     · <c>TickCount64</c> <b>嚴格大於</b>到期時刻 → 重新計時，回
    ///     <see langword="true" />。<br />
    ///     · 其餘 → 完全不動狀態，回 <see langword="false" />。<br />
    ///     🔴 也就是說這是「檢查<b>並消費</b>」：回 <see langword="true" /> 的那一次
    ///     會把計時器重新上緊。只想「看看還剩多久」而不消費的話，不可以用這一支。<br />
    ///     ⚠️ 「還沒見過這個 key」是用「字典裡有沒有這個 key」表示的。不要改成拿
    ///     哨兵值（例如 <c>long.MinValue</c>）當初始值：那配上減法比較會溢位成負數，
    ///     判斷式就永遠不成立。<br />
    ///     ⚠️ <c>TickCount64</c> 是自開機以來的毫秒數（<c>long</c>），不會溢位；
    ///     不要改用 32 位元的 <c>Environment.TickCount</c>。
    /// </remarks>
    /// <param name="key">節流的識別鍵。</param>
    /// <param name="windowMs">節流窗口長度，單位毫秒。</param>
    /// <returns>這一次要不要放行。</returns>
    internal bool Throttle(string key, long windowMs)
    {
        lock (_gate)
        {
            var now = Environment.TickCount64;

            if (_expiry.TryGetValue(key, out var expiresAt) && now <= expiresAt)
                return false;

            _expiry[key] = now + windowMs;
            return true;
        }
    }

    /// <inheritdoc cref="Throttle(string,long)" />
    /// <param name="key">節流的識別鍵。</param>
    /// <param name="window">節流窗口長度。</param>
    /// <returns>這一次要不要放行。</returns>
    internal bool Throttle(string key, TimeSpan window) =>
        Throttle(key, (long)window.TotalMilliseconds);
}
