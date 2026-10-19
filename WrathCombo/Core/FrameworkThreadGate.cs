#region

using System;
using System.Threading;
using ECommons.DalamudServices;

#endregion

namespace WrathCombo.Core;

/// <summary>
///     連段求值入口的執行緒閘門。
/// </summary>
/// <remarks>
///     契約：連段、重定向與它們一路用到的靜態狀態（<c>EzThrottler</c>、各種快取字典、物件表）只在框架執行緒上碰。
///     被掛鉤的遊戲函式別的插件也呼叫得到，由哪條執行緒打進來不是這裡能決定的，所以在入口擋。
/// </remarks>
internal static class FrameworkThreadGate
{
    private static int _reported;

    /// <param name="entry"> 入口名稱，只用在記錄檔。 </param>
    /// <returns> <see langword="false" />＝不在框架執行緒，呼叫端應原樣交給遊戲原本的函式。 </returns>
    /// <remarks> 不擲例外：呼叫端在原生掛鉤裡，有一處在 try 之外。 </remarks>
    internal static bool Check(string entry)
    {
        try
        {
            if (Svc.Framework.IsInFrameworkUpdateThread)
                return true;

            // 一次載入只記一則：堆疊用來認出是誰從別條執行緒打進來。
            if (Interlocked.Exchange(ref _reported, 1) == 0)
                Svc.Log.Warning(
                    $"[WrathCombo] {entry} 被框架執行緒以外的執行緒呼叫：這類呼叫不跑連段與重定向，" +
                    "原樣交給遊戲原本的函式（這次載入只記這一則）。\n" + Environment.StackTrace);

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
