#region

using System;
using System.Collections.Generic;
using ECommons;
using ECommons.EzIpcManager;
using ECommons.Logging;

#endregion

namespace WrathCombo.Services.IPC_Subscriber;

/// <summary>
///     訂閱 MonsterDex 的「這隻怪吃不吃控場」查詢。
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>刻意不用 <c>SafeWrapper.IPCException</c></b>：那個包裝會把例外吞掉並
///         回傳 <c>default</c>，對本端點就是 <c>0</c> —— 而 <c>0</c> 在契約裡是
///         「所有控場都免疫」這個<b>有意義的值</b>。沒安裝 MonsterDex 會因此被讀成
///         「全部免疫」，把暈眩／加重整個靜默關掉。所以這裡走預設的
///         <see cref="SafeWrapper.None" /> ＋ <c>TryInvoke</c>，才分得出
///         「沒有資料」與「明確說免疫」。
///     </para>
///     <para>
///         契約（逐字）：<br />
///         <c>MonsterDex.GetMobVulnerabilities</c> → <c>Func&lt;uint,int&gt;</c>，
///         參數 ＝ <c>IBattleChara.NameId</c>（<c>BNpcName</c> 的 row id）。<br />
///         回傳 <c>-1</c> ＝ 無資料（🔴 <b>視同「可用」</b>，絕不當成免疫）；<br />
///         否則為位元旗標：bit0 可暈眩、bit1 可加重、bit2 可睡眠、bit3 可束縛、bit4 可減速。
///     </para>
/// </remarks>
internal static class MonsterDexIPC
{
    /// <summary>對方明確回報的「查無此怪」，也是本類別所有失敗路徑的回傳值。</summary>
    internal const int NoData = -1;

    /// <summary>端點打不通之後，隔多久才再試一次（避免每幀擲例外）。</summary>
    private const long UnavailableBackoffMs = 30000;

    /// <summary>「查無資料」的快取有效期；對方可能只是資料表還沒載完。</summary>
    private const long NoDataTtlMs = 60000;

    /// <summary>快取上限，避免異常輸入讓字典無限成長。</summary>
    private const int MaxCachedNames = 4096;

    /// <summary>
    ///     訂閱是<b>延後到第一次真的要查的時候</b>才建立的。
    ///     這樣「使用者沒開任何相關功能」時，本外掛完全不會去碰 MonsterDex 的 IPC，
    ///     連 <see cref="Dispose" /> 也不會反過來把訂閱建起來再拆掉。
    /// </summary>
    private static EzIPCDisposalToken[]? _disposalTokens;

    /// <summary>NameId → (旗標, 到期時間)。正資料不過期，NoData 走短 TTL。</summary>
    private static readonly Dictionary<uint, (int Value, long ExpiresAt)> Cache = [];

    private static long _unavailableUntil;

    /// <summary>
    ///     上一次呼叫時端點是否真的答得出來。純診斷用，不參與任何判斷。
    /// </summary>
    internal static bool EndpointResponding { get; private set; }

#pragma warning disable CS0649, CS8618 // Complaints of the field
    // 刻意寫全名 ＋ applyPrefix: false，把「安裝偵測用名」和「IPC 前綴」脫鉤
    // ——理由與 BossModIPC 相同，見該類別的註解。
    [EzIPC("MonsterDex.GetMobVulnerabilities", false)]
    private static Func<uint, int> GetMobVulnerabilities;
#pragma warning restore CS8618, CS0649

    /// <summary>
    ///     查一隻怪的控場易感性位元旗標。
    /// </summary>
    /// <param name="nameId">
    ///     <c>IBattleChara.NameId</c>（<c>BNpcName</c> row id）。
    /// </param>
    /// <returns>
    ///     位元旗標；<see cref="NoData" />（<c>-1</c>）代表<b>沒有資料</b> ——
    ///     沒安裝、端點不存在、呼叫失敗、對方說查不到，全部走這條。<br />
    ///     🔴 呼叫端必須把它當成「可用」，不可以當成免疫。
    /// </returns>
    internal static int Vulnerabilities(uint nameId)
    {
        if (nameId == 0)
            return NoData;

        var now = Environment.TickCount64;

        if (Cache.TryGetValue(nameId, out var cached))
        {
            if (now < cached.ExpiresAt)
                return cached.Value;
            Cache.Remove(nameId);
        }

        // 端點打不通時退避，不要每幀都去擲一次例外。
        if (now < _unavailableUntil)
            return NoData;

        int result;
        try
        {
            EnsureSubscribed();

            if (!GetMobVulnerabilities.TryInvoke(nameId, out result))
            {
                MarkUnavailable(now, "端點尚未註冊（MonsterDex 沒安裝或版本較舊）");
                return NoData;
            }
        }
        catch (Exception e)
        {
            // TryInvoke 只攔 IpcNotReadyError；型別不符之類的例外走這裡。
            MarkUnavailable(now, e.ToStringFull());
            return NoData;
        }

        EndpointResponding = true;

        if (Cache.Count >= MaxCachedNames)
            Cache.Clear();

        Cache[nameId] = result < 0
            ? (NoData, now + NoDataTtlMs) // 對方說查不到：短期快取，之後再問一次
            : (result, long.MaxValue);    // 真資料是靜態的，不必過期

        return result < 0 ? NoData : result;
    }

    private static void MarkUnavailable(long now, string reason)
    {
        _unavailableUntil = now + UnavailableBackoffMs;

        // 只在「本來答得出來（或還沒印過）→ 現在答不出來」這個轉折印一次，
        // 不要每次退避到期重試都洗一行。
        var worthLogging = EndpointResponding || !_loggedUnavailable;
        EndpointResponding = false;
        if (!worthLogging)
            return;

        _loggedUnavailable = true;

        // 一律 Information：使用者的記錄等級會濾掉 Debug/Verbose。
        PluginLog.Information(
            $"[MonsterDex] `GetMobVulnerabilities` 目前不可用，" +
            $"接下來 {UnavailableBackoffMs / 1000} 秒內一律視同「無資料」（＝放行，" +
            $"不會擋掉任何暈眩／加重）：{reason}");
    }

    private static bool _loggedUnavailable;

    /// <summary>第一次真的要查的時候才建立訂閱。重複呼叫是安全的。</summary>
    private static void EnsureSubscribed() =>
        _disposalTokens ??= EzIPC.Init(typeof(MonsterDexIPC), "MonsterDex");

    internal static void Dispose()
    {
        Cache.Clear();

        // 從沒訂閱過就什麼都不做 —— 不要在 Dispose 裡反過來把訂閱建起來。
        var tokens = _disposalTokens;
        _disposalTokens = null;
        if (tokens is null)
            return;

        foreach (var token in tokens)
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
