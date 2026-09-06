#region

// ReSharper disable RedundantUsingDirective

using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using WrathCombo.Combos;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Extensions;
// 🔴🔴 這個檔<b>刻意不引用</b> ECommons.Throttlers.EzThrottler。
//    它是整個外掛共用的靜態裸 Dictionary、零同步，而這裡每一支都是 IPC 端點的
//    後端，跑在承租外掛的執行緒上 ⇒ 打它的失敗形式不是「節流失準」，而是
//    那個字典本身壞掉，還會連帶弄壞這個外掛所有模組的節流。
//    要節流請用本檔的 Leasing.ThrottleIpcLog()（自帶鎖、自帶字典）。
using CancellationReasonEnum = WrathCombo.Services.IPC.CancellationReason;

// ReSharper disable UseSymbolAlias
// ReSharper disable UnusedMember.Global

#endregion

namespace WrathCombo.Services.IPC;

public class Lease(
    string internalPluginName,
    string pluginName,
    Action<int, string>? callback,
    string? ipcPrefixForCallback = null)
{
    /// <summary>
    ///     The identifier for this lease.<br />
    ///     Given to the plugin when registering for a lease to identify themselves.
    /// </summary>
    public Guid ID { get; } = Guid.NewGuid();
    /// <summary>
    ///     The internal name of the registering plugin.<br />
    ///     Used in <see cref="Leasing.CheckIfLeaseePluginsUnloaded"/> to check if the
    ///     plugin has been unloaded.
    /// </summary>
    public string InternalPluginName { get; } = internalPluginName;
    /// <summary>
    ///     The name to display for the registering plugin.
    /// </summary>
    public string PluginName { get; } = pluginName;
    /// <summary>
    ///     The callback to call when the lease is cancelled.<br />
    ///     Only from: <see cref="Provider.RegisterForLease(string,string,Action{int,string})" />
    /// </summary>
    public Action<int, string>? Callback { get; } = callback;
    /// <summary>
    ///     The IPC prefix to use for the callback.<br />
    ///     Only from: <see cref="Provider.RegisterForLeaseWithCallback" />
    /// </summary>
    public string? IPCPrefixForCallback { get; } = ipcPrefixForCallback;

    /// <summary>
    ///     The date and time this lease was created.
    /// </summary>
    // ReSharper disable once UnusedMember.Local
    internal DateTime Created { get; } = DateTime.Now;
    /// <summary>
    ///     The date and time this lease was last updated.
    /// </summary>
    internal DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    ///     A simple checksum of the configurations controlled by this registration.
    /// </summary>
    internal byte[] ConfigurationsHash
    {
        get
        {
            var allKeys = AutoRotationControlled.Keys
                .Select(k => k.ToString())
                .Concat(JobsControlled.Keys.Select(k => k.ToString()))
                .Concat(CombosControlled.Keys.Select(k => k.ToString()))
                .Concat(OptionsControlled.Keys.Select(k => k.ToString()))
                .ToArray();

            var concatenatedKeys = string.Join(",", allKeys);
            return SHA256.HashData(Encoding.UTF8.GetBytes(concatenatedKeys));
        }
    }

    /// <summary>
    ///     The number of sets leased by this registration currently.
    /// </summary>
    public int SetsLeased =>
        AutoRotationControlled.Count +
        JobsControlled.Count +
        CombosControlled.Count +
        OptionsControlled.Count;

    #region Configurations controlled by this lease

    internal Dictionary<byte, bool> AutoRotationControlled { get; set; } = new();

    internal Dictionary<AutoRotationConfigOption, int> AutoRotationConfigsControlled
    {
        get;
        set;
    } = new();

    internal Dictionary<Job, bool> JobsControlled { get; set; } = new();

    internal Dictionary<CustomComboPreset, (bool enabled, bool autoMode)>
        CombosControlled
    { get; set; } = new();

    internal Dictionary<CustomComboPreset, bool> OptionsControlled { get; set; } =
        new();

    #endregion

    /// <summary>
    ///     Cancels the lease.<br/>
    ///     Will invoke the callback if one was provided either as an
    ///     <see cref="Callback">Action</see> or a via
    ///     <see cref="IPCPrefixForCallback">IPC</see>.
    /// </summary>
    /// <param name="cancellationReason">
    ///     The <see cref="CancellationReasonEnum" /> for cancelling the lease.
    /// </param>
    /// <param name="additionalInfo">
    ///     Any additional information to provide with the cancellation.
    /// </param>
    /// <remarks>
    ///     Usually called by
    ///     <see cref="Leasing.RemoveRegistration">RemoveRegistration()</see>,
    ///     which is often called by <see cref="Provider.ReleaseControl" /> or
    ///     by the user via <see cref="UIHelper.RevokeControl"/>.
    /// </remarks>
    public void Cancel
        (CancellationReasonEnum cancellationReason, string additionalInfo = "")
    {
        Logging.Log(
            "Cancelling Lease for: "
            + PluginName
            + " (" + cancellationReason + ")" +
            (additionalInfo != ""
                ? "\n" + additionalInfo
                : "")
        );

        // 🔴 取消回呼是「別的外掛的碼」，在我們的堆疊上同步執行。
        //    IPC 那一條路徑（Helper.CallIPCCallback）本來就包了 try/catch，
        //    Action 這一條卻是裸呼叫：承租外掛的回呼一擲例外，例外就會往上冒到
        //    RemoveRegistration()，讓它後面的 UI 快取失效與
        //    UpdateActiveJobPresets() 全部跳過——UI 會停在過期的狀態，而且會從
        //    Provider.Dispose()（外掛停用）這種不該擲例外的路徑上冒出來。
        //    比照 IPC 路徑包起來。
        //
        //    ⚠️ 回呼內部可能再入呼叫 Leasing 的註冊／移除方法，所以 catch 之後
        //    不要在這裡碰任何集合狀態。租約本身已經由 RemoveRegistration() 在
        //    呼叫本方法**之前**就從 Registrations 移除了（那個順序正是用來擋
        //    「承租端在回呼裡 ReleaseControl() 同一把租約」的無限遞迴），
        //    所以這裡不需要、也不應該再去動 Registrations。
        try
        {
            if (Callback is not null)
                Callback.Invoke((int)cancellationReason, additionalInfo);
            else if (IPCPrefixForCallback is not null)
                Helper.CallIPCCallback(IPCPrefixForCallback, cancellationReason, additionalInfo);
        }
        catch (Exception e)
        {
            // 不吞成完全靜默：使用者跑 LogLevel 1，Error 收得到。
            LogCallbackFailure(PluginName, cancellationReason, e);
        }
    }

    #region 取消回呼失敗的節流記錄

    /// <summary>
    ///     每個承租外掛最近一次「取消回呼擲例外」被記進 log 的時間，以及那之後
    ///     被抑制掉的次數。
    /// </summary>
    /// <value>
    ///     <b>Key:</b> 承租外掛的顯示名稱（<see cref="PluginName" />）。<br />
    ///     <b>Item1:</b> 上次真的印出來的時間（<c>Environment.TickCount64</c>）。<br />
    ///     <b>Item2:</b> 那之後被抑制掉的次數。
    /// </value>
    /// <remarks>
    ///     ⚠️ 用外掛名當 key，不是用租約 <c>Guid</c>：租約每次重新註冊都是新的
    ///     Guid，拿 Guid 當 key 等於完全沒節流。會反覆炸的是「那個外掛的回呼」。
    /// </remarks>
    private static readonly Dictionary<string, (long LoggedAt, int Suppressed)>
        _callbackFailures = new();

    /// <summary>
    ///     守住 <see cref="_callbackFailures" /> 的鎖。
    /// </summary>
    /// <remarks>
    ///     🔴 取消通知是在 <c>Leasing</c> 放掉自己的鎖<b>之後</b>才送的，送的人可能是
    ///     IPC 呼叫端的執行緒、framework 執行緒，或 <c>Provider.BuildCaches</c> 那條
    ///     thread-pool 執行緒 ⇒ 這張表真的會被並行寫。裸 <c>Dictionary</c> 在這裡的
    ///     失敗形式是<b>字典本身壞掉</b>，不是「少印一行 log」。
    /// </remarks>
    private static readonly object _callbackFailuresGate = new();

    /// <summary>
    ///     同一個承租外掛的回呼失敗，最多每這麼多毫秒印一次完整例外。
    /// </summary>
    private const long CallbackFailureLogWindowMs = 60_000;

    /// <summary>
    ///     記錄承租外掛的取消回呼擲出的例外，同一個外掛節流到每分鐘一次。
    /// </summary>
    /// <remarks>
    ///     用 <see cref="Logging.Error" />（≥ Information，使用者的 LogLevel 1 收得到）。
    ///     訊息一定帶承租外掛名——不然使用者回報「我的租約被莫名取消」時，
    ///     log 裡看不出是誰的回呼炸的。<br />
    ///     ⚠️ 這裡刻意不用 <c>EzThrottler</c>：它的 key 是全域持久的，
    ///     和別處共用一個命名空間；而且它只能回答「放不放行」，沒辦法順便把
    ///     被抑制掉的次數帶出來。同檔 <c>ConflictingPluginsChecks</c> 也是同樣理由
    ///     自己用 <c>TickCount64</c> 做。
    /// </remarks>
    private static void LogCallbackFailure(
        string pluginName, CancellationReasonEnum cancellationReason, Exception e)
    {
        // 這裡是例外處理路徑，本身再擲例外就會把「取消」整條打斷 —— 而擋掉
        // 那件事正是這整段程式碼存在的理由。所以全部包起來。
        try
        {
            var key = string.IsNullOrEmpty(pluginName) ? "<unknown leasee>" : pluginName;
            var now = Environment.TickCount64;
            int suppressed;

            // 鎖內只做字典運算就出來：Logging 會建一個 StackTrace，不便宜，
            // 而且沒有理由讓別條執行緒等它。
            lock (_callbackFailuresGate)
            {
                var seen = _callbackFailures.TryGetValue(key, out var state);

                // ⚠️ 不要拿哨兵值（如 long.MinValue）當「還沒印過」：
                // now - long.MinValue 會溢位成負數，比較就永遠不成立。
                // 改用「字典裡有沒有這個 key」來表示，第一次必定印。
                if (seen && now - state.LoggedAt < CallbackFailureLogWindowMs)
                {
                    _callbackFailures[key] = (state.LoggedAt, state.Suppressed + 1);
                    return;
                }

                suppressed = seen ? state.Suppressed : 0;
                _callbackFailures[key] = (now, 0);
            }

            Logging.Error(
                "Leasee '" + key +
                "' threw from its lease-cancellation callback (" +
                cancellationReason + "). " +
                "The lease was still cancelled and removed; " +
                "only the leasee's own notification failed." +
                (suppressed > 0
                    ? " (" + suppressed +
                      " further failure(s) from this leasee were suppressed " +
                      "in the last " + CallbackFailureLogWindowMs / 1000 + "s.)"
                    : "") +
                "\n" + e);
        }
        catch
        {
            // 連記 log 都失敗就放棄記錄。絕對不能讓它往上冒。
        }
    }

    #endregion
}

public partial class Leasing
{
    /// <summary>
    ///     Active leases.
    /// </summary>
    /// <remarks>
    ///     🔴 <b>刻意是 private 的。</b>不要改回 <c>internal</c>，也不要把它（或是
    ///     <see cref="Lease" /> 身上那幾個 <c>...Controlled</c> 字典）交到這個類別外面。
    ///     所有存取一律經過 <see cref="_gate" />；外面要資料就走
    ///     <see cref="ProjectLeases{TResult}" /> 或 <see cref="SnapshotLeases" />
    ///     這類「在鎖內拍完快照才回傳」的存取器。
    /// </remarks>
    private readonly Dictionary<Guid, Lease> _registrations = new();

    #region 執行緒安全

    /// <summary>
    ///     守住 <see cref="_registrations" />、每一把 <see cref="Lease" /> 身上那幾個
    ///     <c>...Controlled</c> 字典，以及 <see cref="_userRevokedTemporaryBlacklist" />
    ///     的鎖。
    /// </summary>
    /// <remarks>
    ///     🔴🔴 <b>為什麼一定要有。</b>這些字典被<b>三種執行緒</b>碰：<br />
    ///     ① <b>承租外掛自己的執行緒</b> —— <c>[EzIPC]</c> 端點是拿
    ///     <c>IDalamudPluginInterface.GetIpcProvider&lt;…&gt;().RegisterFunc()</c>
    ///     直接註冊委派的（<c>ECommons/EzIpcManager/EzIPC.cs</c>），
    ///     <b>中間沒有任何 framework 執行緒轉送</b>
    ///     ⇒ <c>SetComboState</c> 這類端點就跑在<b>呼叫端</b>的執行緒上。<br />
    ///     ② <b>framework／繪製執行緒</b> —— <see cref="CheckIfLeaseePluginsUnloaded" />
    ///     每 500 幀跑一次，<c>UIHelper</c> 與設定視窗每幀讀。<br />
    ///     ③ <b>執行緒池</b> —— <c>Provider.BuildCaches</c> 裡的
    ///     <c>Task.Run(() =&gt; P.IPC.Helper.IPCEnabled)</c>，遠端把 IPC 關掉時會直接
    ///     呼叫 <see cref="SuspendLeases" /> ⇒ 在 thread-pool 執行緒上改字典。<br />
    ///     <br />
    ///     裸 <c>Dictionary</c> 在這種情況下的失敗形式<b>不是「讀到舊值」，是字典本身
    ///     壞掉</b>（一邊擴充雜湊桶、一邊有人在走訪），而讀取端的 <c>foreach</c>／LINQ
    ///     會擲 <c>InvalidOperationException</c>。<br />
    ///     <br />
    ///     🔑 <b>鎖內三不</b>：不呼叫 ImGui、不做檔案／網路 I/O、不呼叫承租外掛的碼。
    ///     取值／拍快照就出來。
    /// </remarks>
    private readonly object _gate = new();

    /// <summary>
    ///     還沒送出去的「租約已取消」通知。
    /// </summary>
    /// <remarks>
    ///     🔴🔴 <see cref="Lease.Cancel" /> 會<b>同步執行承租外掛的碼</b>
    ///     （<c>Callback.Invoke</c>，或 <c>Helper.CallIPCCallback</c> 的 call gate）。
    ///     那段碼絕對不可以在握著 <see cref="_gate" /> 的時候跑：<br />
    ///     對方的回呼裡只要出現 <c>RunOnFrameworkThread(…).Wait()</c> 這種再普通不過的
    ///     寫法，它就會停在那裡等 framework 執行緒；而 framework／繪製執行緒此刻正卡在
    ///     <see cref="_gate" /> 上等我們 ⇒ <b>兩邊互等，整個遊戲凍住</b>。<br />
    ///     ⚠️ <c>Monitor</c> 對<b>同一條</b>執行緒是可重入的，所以「承租端在回呼裡回頭
    ///     呼叫我們」本身不會卡死；真正會鎖死的是回呼跑去等<b>另一條</b>執行緒。<br />
    ///     <br />
    ///     🔑 所以移除在鎖內做完，通知排進這張表，等鎖真的放掉之後才送。
    /// </remarks>
    private readonly List<(Lease Lease, CancellationReasonEnum Reason, string Info)>
        _pendingCancellations = [];

    /// <summary>
    ///     送出 <see cref="_pendingCancellations" /> 裡累積的取消通知。<b>一定要在鎖外
    ///     呼叫</b>（放在 <c>finally</c> 裡，讓鎖內擲例外時也送得出去）。
    /// </summary>
    /// <remarks>
    ///     🔑 <c>Monitor.IsEntered</c> 問的是「<b>目前這條執行緒</b>還握著這把鎖嗎」。
    ///     還握著就表示我們是巢狀呼叫，直接回去，由最外層那一格在真的放掉鎖之後統一送
    ///     —— 通知永遠不會在鎖內送出。
    /// </remarks>
    private void FlushPendingCancellations()
    {
        if (Monitor.IsEntered(_gate))
            return;

        while (true)
        {
            (Lease Lease, CancellationReasonEnum Reason, string Info) pending;
            lock (_gate)
            {
                if (_pendingCancellations.Count == 0)
                    return;
                pending = _pendingCancellations[0];
                _pendingCancellations.RemoveAt(0);
            }

            // 🔴 一把通知不出去，不能連累其他把。Lease.Cancel() 內部已經擋掉承租端
            //    回呼擲的例外，這裡再包一層是為了擋它自己的記錄路徑。
            try
            {
                pending.Lease.Cancel(pending.Reason, pending.Info);
            }
            catch (Exception e)
            {
                Logging.Error(
                    "Failed to notify '" + pending.Lease.PluginName +
                    "' that its lease was cancelled (" + pending.Reason +
                    "); the lease was still removed.\n" + e);
            }

            // 原本在 RemoveRegistration() 尾端。移到鎖外：它會整份重算職業 preset
            // （最終走到設定與 Excel 表），不該握著鎖跑。
            try
            {
                P.IPCSearch.UpdateActiveJobPresets();
            }
            catch (Exception e)
            {
                Logging.Error(
                    "Failed to refresh the active job presets after a lease was " +
                    "cancelled; the lease was still removed.\n" + e);
            }
        }
    }

    /// <summary>
    ///     這個 IPC 記錄鍵上次真的印出來的時間（<c>Environment.TickCount64</c>）。
    /// </summary>
    /// <remarks>
    ///     🔴🔴 <b>為什麼不用 <c>ECommons</c> 的 <c>EzThrottler</c></b>：它是整個外掛
    ///     共用的<b>靜態裸 <c>Dictionary</c></b>，零同步。用到它的這幾支是 IPC 端點的
    ///     後端，跑在<b>承租外掛的執行緒</b>上，而 Wrath 自己的戰鬥碼每一幀都在
    ///     framework 執行緒上打同一個 <c>EzThrottler</c>
    ///     ⇒ 失敗形式不是「節流失準」，而是<b>那個字典本身壞掉</b>，
    ///     還會連帶弄壞這個外掛<b>所有</b>模組的節流。<br />
    ///     📌 同檔的 <c>Lease.LogCallbackFailure</c> 與
    ///     <c>ConflictingPluginsChecks</c> 也是同樣理由自己用 <c>TickCount64</c> 做。
    /// </remarks>
    private readonly Dictionary<string, long> _ipcLogThrottle = [];

    /// <summary>
    ///     同一個 <paramref name="key" /> 每 <paramref name="windowMs" /> 毫秒只放行
    ///     一次；第一次必定放行（與 <c>EzThrottler</c> 的行為一致）。
    /// </summary>
    private bool ThrottleIpcLog(string key, long windowMs)
    {
        lock (_gate)
        {
            var now = Environment.TickCount64;
            if (_ipcLogThrottle.TryGetValue(key, out var last) &&
                now - last < windowMs)
                return false;

            _ipcLogThrottle[key] = now;
            return true;
        }
    }

    #endregion

    #region 給類別外部的唯讀存取器

    /// <summary>
    ///     目前有幾把租約。
    /// </summary>
    internal int RegistrationCount
    {
        get
        {
            lock (_gate)
                return _registrations.Count;
        }
    }

    /// <summary>
    ///     在鎖內對每一把租約套用 <paramref name="selector" />，把結果<b>整份實體化</b>
    ///     之後回傳。
    /// </summary>
    /// <remarks>
    ///     🔴 <paramref name="selector" /> 是<b>在鎖內</b>執行的 ⇒ 只能做純運算：
    ///     不可以呼叫 ImGui、不可以做 I/O、不可以呼叫別的外掛。<br />
    ///     🔴 而且它只能吐<b>值</b>或全新的集合。把 <see cref="Lease" /> 身上那幾個
    ///     <c>...Controlled</c> 字典原封不動漏到鎖外，等於什麼都沒防到。
    /// </remarks>
    internal List<TResult> ProjectLeases<TResult>
        (Func<Lease, IEnumerable<TResult>> selector)
    {
        lock (_gate)
            return _registrations.Values.SelectMany(selector).ToList();
    }

    /// <summary>
    ///     目前每一把租約的顯示用快照。給 UI 用：<b>在鎖內拍完，鎖外才畫</b>。
    /// </summary>
    internal (Guid ID, string PluginName, int SetsLeased, string Jobs, int Combos,
        bool AutoRotation, DateTime Created)[] SnapshotLeases()
    {
        lock (_gate)
            return _registrations.Values
                .Select(l => (
                    l.ID,
                    l.PluginName,
                    l.SetsLeased,
                    l.JobsControlled.Count > 0
                        ? string.Join(",", l.JobsControlled.Keys)
                        : "0",
                    l.CombosControlled.Count,
                    l.AutoRotationControlled.Count > 0,
                    l.Created))
                .ToArray();
    }

    /// <summary>
    ///     每一把租約的「外掛名 + 押了幾組設定」。
    /// </summary>
    internal (string pluginName, int configurationsCount)[] SnapshotLeaseeSummaries()
    {
        lock (_gate)
            return _registrations.Values
                .Select(l => (l.PluginName, l.SetsLeased))
                .ToArray();
    }

    /// <summary>
    ///     某一把租約押了幾組設定；租約不存在時回 <c>null</c>。
    /// </summary>
    internal int? TryGetSetsLeased(Guid lease)
    {
        lock (_gate)
            return _registrations.TryGetValue(lease, out var registration)
                ? registration.SetsLeased
                : null;
    }

    /// <summary>
    ///     目前正在控制自動循環<b>開關</b>的外掛名（最近更新的排前面），以及它被控制到
    ///     的狀態。沒有人在控制時回 <c>null</c>。
    /// </summary>
    internal (string[] Controllers, bool State)? SnapshotAutoRotationControllers()
    {
        lock (_gate)
        {
            var controllers = _registrations.Values
                .Where(l => l.AutoRotationControlled.Count != 0)
                .OrderByDescending(l => l.LastUpdated)
                .ToList();

            if (controllers.Count == 0)
                return null;

            return (controllers.Select(l => l.PluginName).ToArray(),
                controllers[0].AutoRotationControlled[0]);
        }
    }

    /// <summary>
    ///     找出這些外掛名目前握著的租約 ID。
    /// </summary>
    /// <remarks>
    ///     ⚠️ 回傳之後鎖就放掉了 ⇒ 拿去 <see cref="RemoveRegistration" /> 之前那把租約
    ///     可能已經不在了。呼叫端要自己擋 <c>KeyNotFoundException</c>。
    /// </remarks>
    internal Guid[] FindLeasesByPluginNames(ICollection<string> pluginNames)
    {
        lock (_gate)
            return _registrations.Values
                .Where(l => pluginNames.Contains(l.PluginName))
                .Select(l => l.ID)
                .ToArray();
    }

    #endregion

    #region Cache Bust dates

    /// <summary>
    ///     When the Auto-Rotation state was last updated.<br />
    ///     Used to bust the UI cache.<br />
    ///     <c>null</c> if never updated.
    /// </summary>
    internal DateTime? AutoRotationStateUpdated;

    /// <summary>
    ///     When the Auto-Rotation configurations were last updated.<br />
    ///     Used to bust the UI cache.<br />
    ///     <c>null</c> if never updated.
    /// </summary>
    internal DateTime? AutoRotationConfigsUpdated;

    /// <summary>
    ///     When Jobs-controlled were last updated.<br />
    ///     Used to bust the UI cache.<br />
    ///     <c>null</c> if never updated.
    /// </summary>
    internal DateTime? JobsUpdated;

    /// <summary>
    ///     When Combos-controlled were last updated.<br />
    ///     Used to bust the UI cache.<br />
    ///     <c>null</c> if never updated.
    /// </summary>
    internal DateTime? CombosUpdated;

    /// <summary>
    ///     When Options-controlled were last updated.<br />
    ///     Used to bust the UI cache.<br />
    ///     <c>null</c> if never updated.
    /// </summary>
    internal DateTime? OptionsUpdated;

    #endregion

    #region Normal IPC Flow

    /// <summary>
    ///     Creates a new <see cref="Lease" /> and saves it to
    ///     <see cref="Registrations" />, ensuring the lease ID is unique.
    /// </summary>
    /// <param name="internalPluginName">
    ///     The internal name of the registering plugin.
    /// </param>
    /// <param name="pluginName">The name of the registering plugin.</param>
    /// <param name="callback">
    ///     The cancellation callback for that plugin.<br />
    ///     Note: only from
    ///     <see cref="Provider.RegisterForLease(string,string,Action{int,string})" />
    /// </param>
    /// <param name="ipcPrefixForCallback">
    ///     The cancellation callback for that plugin.<br />
    ///     Note: only from
    ///     <see cref="Provider.RegisterForLeaseWithCallback" />
    /// </param>
    /// <returns>
    ///     The lease ID to be used by the plugin in subsequent calls.<br />
    ///     Or <c>null</c> if the plugin is blacklisted.
    /// </returns>
    /// <seealso cref="Provider.RegisterForLease(string,string)" />
    /// <seealso cref="Provider.RegisterForLeaseWithCallback" />
    /// <seealso cref="Provider.RegisterForLease(string,string,Action{int,string})" />
    internal Guid? CreateRegistration
    (string internalPluginName, string pluginName,
        Action<int, string>? callback = null, string? ipcPrefixForCallback = null)
    {
        // Bail if the plugin is temporarily blacklisted
        if (CheckBlacklist(internalPluginName))
        {
            Logging.Warn(
                $"{pluginName}: Plugin is temporarily blacklisted, cannot register for a lease");
            return null;
        }

        // Make sure the lease ID is unique
        // (unnecessary, but could save a big headache)
        //
        // 🔴 產生與寫入必須在<b>同一次</b>持鎖內完成：分成「先查再寫」兩次持鎖的話，
        //    兩條執行緒可能同時查到「這個 ID 沒人用」然後雙雙 Add ⇒ 後者擲
        //    ArgumentException。Guid 撞號的機率當然可以忽略，但這裡免費。
        Lease lease;
        lock (_gate)
        {
            do
            {
                // Create a new lease
                lease = new Lease
                (internalPluginName, pluginName,
                    callback, ipcPrefixForCallback);
            } while (_registrations.ContainsKey(lease.ID) ||
                     _userRevokedTemporaryBlacklist.ContainsKey(lease.ID));

            // Save the lease
            _registrations.Add(lease.ID, lease);

            // 又有租約了：下一次真的把它暫停掉時要重新印得出來。
            // 純粹是記錄用的旗標，見 SuspendLeases。
            _allLeasesSuspended = false;
        }

        Logging.Log($"{pluginName}: Created Lease");

        // Provide the lease ID to the plugin
        return lease.ID;
    }

    /// <summary>
    ///     When <see cref="CheckAutoRotationControlled"/> was last cached.
    /// </summary>
    private DateTime? _lastAutoRotationStateCheck;

    /// <summary>
    ///     Cached value of <see cref="CheckAutoRotationControlled"/>
    /// </summary>
    private bool? _autoRotationStateUpdated;

    /// <summary>
    ///     Checks if Auto-Rotation's state is controlled by a lease.
    /// </summary>
    /// <returns>
    ///     The state Auto-Rotation is controlled to, or <c>null</c> if it is not.
    /// </returns>
    /// <seealso cref="Provider.GetAutoRotationState" />
    internal bool? CheckAutoRotationControlled()
    {
        lock (_gate)
        {
            if (AutoRotationStateUpdated is null ||
                _registrations.Count == 0)
                return null;

            if (_lastAutoRotationStateCheck >= AutoRotationStateUpdated)
                return _autoRotationStateUpdated;

            var lease = _registrations.Values
                .Where(l => l.AutoRotationControlled.Count != 0)
                .OrderByDescending(l => l.LastUpdated)
                .FirstOrDefault();

            _lastAutoRotationStateCheck = DateTime.Now;
            _autoRotationStateUpdated = lease?.AutoRotationControlled[0];

            return _autoRotationStateUpdated;
        }
    }

    /// <summary>
    ///     Adds a registration for Auto-Rotation control to a lease.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease(string,string)" />
    /// </param>
    /// <param name="newState">Whether to enabled Auto-Rotation.</param>
    /// <seealso cref="Provider.SetAutoRotationState" />
    internal SetResult AddRegistrationForAutoRotation(Guid lease, bool newState)
    {
        // 鎖內只做判斷與字典寫入；Logging 會建一個 StackTrace，留到鎖外印。
        //
        // ⚠️ 這裡的判斷式維持原樣（AutoRotationConfigsControlled.Count > 0 而不是
        //    AutoRotationControlled.Count > 0）。看起來像筆誤，但改它會改變行為，
        //    不在這次的範圍內 —— 已另外回報。
        string pluginName;
        bool duplicate;
        lock (_gate)
        {
            var registration = _registrations[lease];
            pluginName = registration.PluginName;

            duplicate = registration.AutoRotationConfigsControlled.Count > 0 &&
                        registration.AutoRotationControlled[0] == newState;

            if (!duplicate)
            {
                // Always [0], not an actual add
                registration.AutoRotationControlled[0] = newState;

                registration.LastUpdated = DateTime.Now;
                AutoRotationStateUpdated = DateTime.Now;
            }
        }

        if (duplicate)
        {
            if (ThrottleIpcLog("ipcAutoRotSetLog", 15_000))
                Logging.Log(
                    $"{pluginName}: You are already controlling Auto-Rotation");

            return SetResult.Duplicate;
        }

        Logging.Log($"{pluginName}: Auto-Rotation state updated");
        return SetResult.Okay;
    }

    /// <summary>
    ///     Checks if a lease controls the current job.
    /// </summary>
    /// <returns>
    ///     The state the current job is controlled to, or <c>null</c> if it is not.
    /// </returns>
    /// <seealso cref="Provider.IsCurrentJobAutoRotationReady" />
    /// <seealso cref="Provider.IsCurrentJobConfiguredOn" />
    /// <seealso cref="Provider.IsCurrentJobAutoModeOn" />
    internal bool? CheckJobControlled(int? job = null)
    {
        if (CustomComboFunctions.LocalPlayer is null)
            return null;

        var currentJob = (Job)CustomComboFunctions.LocalPlayer.ClassJob.RowId;
        var resolvedJob = job is null ? currentJob : (Job)job;

        lock (_gate)
        {
            var lease = _registrations.Values
                .Where(l => l.JobsControlled.ContainsKey(resolvedJob))
                .OrderByDescending(l => l.LastUpdated)
                .FirstOrDefault();

            return lease?.JobsControlled[resolvedJob];
        }
    }

    /// <summary>
    ///     Adds a registration for the current Job to a lease.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease(string,string)" />
    /// </param>
    /// <param name="jobOverride">A manual override, only used in testing</param>
    /// <seealso cref="Provider.SetCurrentJobAutoRotationReady" />
    internal SetResult AddRegistrationForCurrentJob(Guid lease, Job? jobOverride = null)
    {
        // 🔑 這一支<b>刻意不把整個方法包進鎖裡</b>：它中途會呼叫
        //    P.IPC.IsCurrentJobAutoRotationReady()、P.IPCSearch.EnabledActions、
        //    CustomComboFunctions.IsEnabled()（讀使用者設定）這些外部子系統。
        //    把那些拉進鎖內會大幅拉長持鎖時間，也把不相干的子系統綁進同一把鎖的
        //    鎖序裡 ⇒ 反而製造死結。照艦隊慣例：<b>只鎖住字典，取值就出來</b>。
        //    代價是這一整支不是原子的——但它本來也不是，而我們要保證的是
        //    「字典不會壞、讀取端不會擲例外」，不是「整個操作可序列化」。
        //    ⚠️ Lease 物件<b>只查一次</b>就沿用同一個參考（下面每次持鎖都用它）。
        //    每次重查 _registrations[lease] 的話，租約若在放鎖的空檔被收走就會擲
        //    KeyNotFoundException —— 那是改動前不存在的新失敗點。沿用參考等同於
        //    改動前的語意：那個 Lease 物件還活著，只是不在表裡了。
        Lease registration;
        lock (_gate)
            registration = _registrations[lease];

        var pluginName = registration.PluginName;

        if (CustomComboFunctions.LocalPlayer is null)
        {
            Logging.Error(
                "Failed to register current job: player object does not exist!");
            return SetResult.PlayerNotAvailable;
        }

        var job = (Job)CustomComboFunctions.JobIDs.ClassToJob((uint)Player.Job);
        if (jobOverride is not null)
            job = jobOverride.Value;

        bool added;
        lock (_gate)
            added = registration.JobsControlled.TryAdd(job, true);

        if (!added)
        {
            if (ThrottleIpcLog("ipcJobSetLog", 15_000))
                Logging.Log(
                    $"{pluginName}: You are already controlling the current job ({job})");

            return SetResult.Duplicate;
        }

        Logging.Log(
            $"{pluginName}: Registering Current Job ({job}) ...");


        bool locking;
        var combos = Helper.GetCombosToSetJobAutoRotationReady(job, false)!;
        var options = Helper.GetCombosToSetJobAutoRotationReady(job)!;
        string[] stringKeys;

        // Lock the job if it's already ready
        if (P.IPC.IsCurrentJobAutoRotationReady())
        {
            locking = true;
            stringKeys = [];
            combos = P.IPCSearch.EnabledActions
                .Where(a => a.Attributes().CustomComboInfo.JobID
                           == (uint)job)
                .Where(a => a.Attributes().Parent is null)
                .Select(a => a.ToString())
                .ToList();
            options = P.IPCSearch.EnabledActions
                .Where(a => a.Attributes().CustomComboInfo.JobID
                           == (uint)job)
                .Where(a => a.Attributes().Parent is not null)
                .Select(a => a.ToString())
                .ToList();
        }
        // Get the list of combos and options to enable
        else
        {
            locking = false;
            string[] alreadyControlled;
            lock (_gate)
                alreadyControlled = registration.CombosControlled.Keys
                    .Select(k => k.ToString()).ToArray();

            stringKeys = combos.Select(k => k.ToString())
                .Concat(alreadyControlled)
                .ToArray();
        }

        // Register all combos
        foreach (var combo in combos)
            AddRegistrationForCombo(lease, combo, true, true);

        // Register all options
        foreach (var option in options)
        {
            if (stringKeys.Contains(option)) continue;

            // Enable the option, or lock the option to its current state
            var state = true;
            if (locking)
            {
                var ccpOption = (CustomComboPreset)
                    Enum.Parse(typeof(CustomComboPreset), option);
                state = CustomComboFunctions.IsEnabled(ccpOption);
            }

            AddRegistrationForOption(lease, option, state);
        }

        var logText =
            $"{pluginName}: Registered Current Job ({job})";
        if (locking)
            logText += " (was already ready: locked it)";

        Logging.Log(logText);

        lock (_gate)
        {
            registration.LastUpdated = DateTime.Now;
            JobsUpdated = DateTime.Now;
            CombosUpdated = DateTime.Now;
            OptionsUpdated = DateTime.Now;
        }

        P.IPCSearch.UpdateActiveJobPresets();

        return SetResult.OkayWorking;
    }

    /// <summary>
    ///     Removes a registration from the IPC service, cancelling the lease.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease(string,string)" />
    /// </param>
    /// <param name="cancellationReason">
    ///     The <see cref="CancellationReasonEnum" /> for cancelling the lease.
    /// </param>
    /// <param name="additionalInfo">
    ///     Any additional information to log and provide with the cancellation.
    /// </param>
    /// <remarks>
    ///     Will call the <see cref="Lease.Callback" /> method if one was
    ///     provided.
    /// </remarks>
    internal void RemoveRegistration
    (Guid lease, CancellationReasonEnum cancellationReason,
        string additionalInfo = "")
    {
        try
        {
            lock (_gate)
            {
                // 鍵不存在時照舊擲 KeyNotFoundException（呼叫端本來就先
                // CheckLeaseExists／ContainsKey 過一輪）。只是先把 Lease 物件抄下來，
                // 因為下面會先移除再取消。
                var registration = _registrations[lease];

                if (cancellationReason ==
                    CancellationReasonEnum.WrathUserManuallyCancelled)
                    _userRevokedTemporaryBlacklist.Add(
                        lease,
                        (registration.InternalPluginName,
                            registration.ConfigurationsHash,
                            DateTime.Now)
                    );

        // 🔴 一定要「先移除，再取消」，順序反過來會無限遞迴。
        //    Lease.Cancel() 是同步回呼承租外掛的碼（Callback.Invoke，或
        //    Helper.CallIPCCallback() 的 call gate）。對方在那個回呼裡對**同一把**
        //    租約呼叫 ReleaseControl() 是完全合理的寫法，而先 Cancel 的話此刻
        //    Registrations 裡那把租約還在 —— Provider.ReleaseControl() 的
        //    CheckLeaseExists() 會通過，於是再進 RemoveRegistration()、再 Cancel()、
        //    再回呼……一路遞迴到 StackOverflowException（🔴 那是攔不下來的，
        //    整個遊戲直接掛掉）。
        //
        //    先 Remove 之後，承租端在回呼裡查詢會看到租約已經不存在，
        //    ReleaseControl() 就會走「租約無效」那條正常的分支而不是再入。
        //    ⚠️ 這是知情核可的行為變更：回呼執行期間，這把租約對承租端而言
        //    已經消失了。
                //
                // 🔴🔴 而「取消」這件事現在是<b>排進佇列、等鎖放掉才送</b>的
                //    （見 _pendingCancellations）：承租外掛的回呼是別人的碼，握著鎖
                //    去跑它，只要對方在回呼裡等 framework 執行緒就會整個遊戲凍住。
                //    先移除仍然成立，而且比原本更強——回呼送出去的時候，租約早就
                //    不在表裡了。
                _registrations.Remove(lease);
                _pendingCancellations.Add(
                    (registration, cancellationReason, additionalInfo));

                // Bust the UI cache
                AutoRotationStateUpdated = DateTime.Now;
                AutoRotationConfigsUpdated = DateTime.Now;
                JobsUpdated = DateTime.Now;
                CombosUpdated = DateTime.Now;
                OptionsUpdated = DateTime.Now;
            }
        }
        finally
        {
            // 🔴 放 finally：上面 _userRevokedTemporaryBlacklist.Add() 對同一個鍵會擲
            //    ArgumentException，那時候佇列裡可能還積著<b>別人</b>排進來的通知。
            //    不送出去的話，那些承租外掛會一直以為自己還握著控制權。
            FlushPendingCancellations();
        }
    }

    #endregion

    #region Fine-Grained Combo Methods

    /// <summary>
    ///     Checks if a combo is controlled by a lease.
    /// </summary>
    /// <param name="combo">The combo internal name to check.</param>
    /// <returns>
    ///     The <see cref="ComboStateKeys">states</see> the combo is controlled to,
    ///     or <c>null</c> if it is not.
    /// </returns>
    /// <seealso cref="Provider.GetComboState" />
    internal (bool enabled, bool autoMode)? CheckComboControlled(string combo)
    {
        CustomComboPreset customComboPreset;
        try
        {
            customComboPreset = (CustomComboPreset)
                Enum.Parse(typeof(CustomComboPreset), combo, true);
        }
        catch
        {
            return null;
        }

        lock (_gate)
        {
            var lease = _registrations.Values
                .Where(l => l.CombosControlled.ContainsKey(customComboPreset))
                .OrderByDescending(l => l.LastUpdated)
                .FirstOrDefault();

            return lease?.CombosControlled[customComboPreset];
        }
    }

    /// <summary>
    ///     Adds a registration for a combo to a lease.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease(string,string)" />
    /// </param>
    /// <param name="combo">The combo internal name to register control of.</param>
    /// <param name="newState">The state to set the preset to.</param>
    /// <param name="newAutoState">The state to set the Auto-Mode to.</param>
    /// <seealso cref="Provider.SetComboState" />
    internal SetResult AddRegistrationForCombo
        (Guid lease, string combo, bool newState, bool newAutoState)
    {
        var preset = (CustomComboPreset)
            Enum.Parse(typeof(CustomComboPreset), combo, true);

        // Disable the combo of the opposite type mode, if one exists
        var oppositeModeCombo = Helper.GetOppositeModeCombo(preset);
        var oppositeText = oppositeModeCombo is not null
            ? $" (Disabled opposite combo: {oppositeModeCombo})"
            : "";

        Lease registration;
        byte[] configurationsHash;
        int setsLeased;
        lock (_gate)
        {
            registration = _registrations[lease];

            if (oppositeModeCombo is not null)
                registration.CombosControlled[(CustomComboPreset)oppositeModeCombo] =
                    (false, false);

            registration.CombosControlled[preset] = (newState, newAutoState);

            // ConfigurationsHash 與 SetsLeased 都會走訪內層字典 ⇒ 必須在鎖內算完，
            // 不能留到鎖外才取。
            configurationsHash = registration.ConfigurationsHash;
            setsLeased = registration.SetsLeased;
        }

        // 🔴 RemoveRegistration() 會排出承租端的回呼，一定要在鎖外呼叫。
        //    ⚠️ 兩個判斷式的順序照原樣：CheckBlacklist() 有副作用
        //    （CleanOutdatedBlacklistEntries），對調會改變黑名單的過期時機。
        if (CheckBlacklist(configurationsHash) && setsLeased > 4)
        {
            RemoveRegistration(lease,
                CancellationReasonEnum.WrathUserManuallyCancelled,
                "Matched currently-blacklisted configuration");
            return SetResult.BlacklistedLease;
        }

        lock (_gate)
        {
            registration.LastUpdated = DateTime.Now;
            CombosUpdated = DateTime.Now;
        }

        Logging.Log(
            $"{registration.PluginName}: Registered Combo ({combo}){oppositeText}");
        return SetResult.Okay;
    }

    /// <summary>
    ///     Checks if a combo option is controlled by a lease.
    /// </summary>
    /// <param name="option">The combo option internal name to check.</param>
    /// <returns>
    ///     The state the combo option is controlled to, or <c>null</c> if it is not.
    /// </returns>
    /// <seealso cref="Provider.GetComboOptionState" />
    internal bool? CheckComboOptionControlled(string option)
    {
        CustomComboPreset customComboPreset;
        try
        {
            customComboPreset = (CustomComboPreset)
                Enum.Parse(typeof(CustomComboPreset), option, true);
        }
        catch
        {
            return null;
        }

        lock (_gate)
        {
            var lease = _registrations.Values
                .Where(l => l.OptionsControlled.ContainsKey(customComboPreset))
                .OrderByDescending(l => l.LastUpdated)
                .FirstOrDefault();

            return lease?.OptionsControlled[customComboPreset];
        }
    }

    /// <summary>
    ///     Adds a registration for a combo option to a lease.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease(string,string)" />
    /// </param>
    /// <param name="option">The option internal name to register control of.</param>
    /// <param name="newState">The state to set the preset to.</param>
    /// <seealso cref="Provider.SetComboOptionState" />
    internal SetResult AddRegistrationForOption
        (Guid lease, string option, bool newState)
    {
        var preset = (CustomComboPreset)
            Enum.Parse(typeof(CustomComboPreset), option, true);

        Lease registration;
        byte[] configurationsHash;
        int setsLeased;
        lock (_gate)
        {
            registration = _registrations[lease];

            registration.OptionsControlled[preset] = newState;

            configurationsHash = registration.ConfigurationsHash;
            setsLeased = registration.SetsLeased;
        }

        // 🔴 與 AddRegistrationForCombo 同理：RemoveRegistration() 要在鎖外呼叫，
        //    兩個判斷式的順序照原樣。
        if (CheckBlacklist(configurationsHash) && setsLeased > 4)
        {
            RemoveRegistration(lease,
                CancellationReasonEnum.WrathUserManuallyCancelled,
                "Matched currently-blacklisted configuration");
            return SetResult.BlacklistedLease;
        }

        lock (_gate)
        {
            registration.LastUpdated = DateTime.Now;
            OptionsUpdated = DateTime.Now;
        }

        Logging.Log($"{registration.PluginName}: Registered Option ({option})");
        return SetResult.Okay;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    ///     Checks if a lease exists.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease(string,string)" />
    /// </param>
    /// <returns>Whether the lease exists.</returns>
    internal bool CheckLeaseExists(Guid lease)
    {
        lock (_gate)
            return _registrations.ContainsKey(lease);
    }

    /// <summary>
    ///     是否已經處於「全部租約都被暫停」的狀態。
    ///     只用來決定要不要印那一行 Warning，對暫停行為本身沒有作用。
    ///     由 <see cref="CreateRegistration" /> 在有新租約時清掉。
    /// </summary>
    private bool _allLeasesSuspended;

    /// <summary>
    ///     Suspend all leases. Called when IPC is disabled remotely.
    /// </summary>
    /// <param name="reason">
    ///     The <see cref="CancellationReasonEnum">reason</see> for suspending leases.
    /// </param>
    /// <seealso cref="Helper.IPCEnabled" />
    /// <seealso cref="RemoveRegistration" />
    internal void SuspendLeases(CancellationReasonEnum? reason = null)
    {
        var reasonToUse = reason ?? CancellationReasonEnum.AllServicesSuspended;

        // 🔴 這一行原本每次呼叫都印。實機上 WrathCombo.UpdateCaches 每次換職業／
        //    首次載入都會呼叫一次（WrathCombo.cs 的 CancellationReason.JobChanged），
        //    使用者的 log 累積出 800 多筆 WRN，而其中絕大多數根本沒有任何租約可暫停。
        //    只在「本來沒暫停 -> 現在暫停」那一次印，而且沒有租約時完全不印。
        //
        // 🔑 快照與旗標一起在鎖內拍，鎖外才印、才收：印 log 會建 StackTrace，
        //    收租約會呼叫承租外掛的碼，兩件都不該在鎖內做。
        List<Guid> leaseIds;
        int leaseCount;
        bool wasSuspended;
        lock (_gate)
        {
            leaseIds = [.. _registrations.Keys];
            leaseCount = leaseIds.Count;
            wasSuspended = _allLeasesSuspended;
            _allLeasesSuspended = true;
        }

        if (leaseCount > 0 && !wasSuspended)
            Logging.Warn(
                $"Suspending all leases ({leaseCount}), reason: {reasonToUse}.");
        else
            Logging.Verbose(
                $"Suspend requested (reason: {reasonToUse}), " +
                $"leases={leaseCount}, alreadySuspended={wasSuspended}.");

        // dispose every lease in _registrations
        //
        // 🔴 一定要對快照迭代，不能直接跑 _registrations.Values：
        //    RemoveRegistration() 會就地 _registrations.Remove()，而承租外掛在取消
        //    回呼裡直接 RegisterForLease() 重新註冊是完全合理的寫法，那會走到
        //    _registrations.Add()，列舉器立刻失效並擲出 InvalidOperationException
        //    ——而且是從 Provider.Dispose()（外掛停用）這種不該擲例外的路徑冒出來。
        //    （net9 實測：單純 Remove 不會使列舉器失效，Add 才會。所以這是只有
        //      承租端搶著重新註冊時才會爆的潛伏 bug，不是每次都爆。）
        //
        // 🔴🔴 而且<b>迴圈本身不可以握著 _gate</b>：RemoveRegistration() 排出去的
        //    取消通知要在鎖外送，握著鎖跑整個迴圈會把「等 framework 執行緒的承租端
        //    回呼」變成死結。每一次 RemoveRegistration() 自己進出鎖就好。
        //
        //    TryGetValue 這道閘門同樣必要：承租外掛可能在回呼裡自己 ReleaseControl()
        //    掉「另一把」租約，快照裡那個鍵就變成死鍵，RemoveRegistration() 內的
        //    _registrations[lease] 會擲 KeyNotFoundException。
        //    Provider.ReleaseControl() 本身也是先 CheckLeaseExists() 再呼叫，
        //    這裡沿用同一個慣例。
        //
        // 🔴 一把租約收不掉，不能連累其他租約收不掉。
        //    Lease.Cancel() 內部已經把「承租端回呼擲例外」擋住了，但
        //    RemoveRegistration() 裡還有別的東西會擲：WrathUserManuallyCancelled
        //    那條路徑上的 _userRevokedTemporaryBlacklist.Add()（同鍵會擲
        //    ArgumentException）。任何一個擲出來，這個 foreach 就地中斷，
        //    剩下的租約既沒被通知也沒被移除 —— 而這條路徑正是
        //    Provider.Dispose()（外掛停用）走的，剩下的承租外掛會一直以為
        //    自己還握著 Wrath 的控制權。
        //    每一把各自 try，收不掉的那把記一行就繼續。
        foreach (var leaseId in leaseIds)
        {
            // 名字要在移除前抄下來，之後那把租約就查不到了。
            string leaseeName;
            lock (_gate)
            {
                if (!_registrations.TryGetValue(leaseId, out var registration))
                    continue;
                leaseeName = registration.PluginName;
            }

            try
            {
                RemoveRegistration(leaseId, reasonToUse);
            }
            catch (Exception e)
            {
                Logging.Error(
                    "Failed to suspend the lease held by '" + leaseeName +
                    "' (" + reasonToUse + "); continuing with the remaining " +
                    "leases.\n" + e);
            }
        }
    }

    #region Checking for plugin being unloaded

    private int _framesSinceLastCheck;

    private bool _checkingLeaseePluginsUnloaded;

    /// <summary>
    ///     Initializes the Leasing service, and registers leasee unloading checks.
    /// </summary>
    public Leasing()
    {
        Svc.Framework.Update += CheckIfLeaseePluginsUnloaded;
    }

    /// <summary>
    ///     Checks currently loaded plugins against leases.<br />
    ///     Will run every 500 frames and check if the leasees plugin is still
    ///     loaded.<br />
    ///     This method is registered to trigger off those events in the
    ///     <see cref="Leasing()">ctor</see>.
    /// </summary>
    private void CheckIfLeaseePluginsUnloaded(IFramework _)
    {
        if (_framesSinceLastCheck < 500 || _checkingLeaseePluginsUnloaded)
        {
            _framesSinceLastCheck++;
            return;
        }

        _checkingLeaseePluginsUnloaded = true;

        // 🔴 這個方法掛在 Framework.Update 上，而 _checkingLeaseePluginsUnloaded
        //    是「正在跑」的閘門。原本收尾那兩行只要被任何例外跳過，旗標就永遠
        //    停在 true，之後每一幀都在開頭 return —— 「承租外掛被停用時自動收回
        //    租約」這個功能會**靜默永久失效**，而且沒有任何徵兆。
        //    改成 try/finally，保證旗標一定會被放掉。
        try
        {
            var plugins = Svc.PluginInterface
                .InstalledPlugins
                .Where(p => p.IsLoaded)
                .Select(p => p.InternalName).ToList();

            // 快照要在鎖內拍：外面若正好有 IPC 端點在 Add，複製建構子走訪到一半
            // 會擲 InvalidOperationException。名字也一併抄走，之後不再碰 Lease。
            List<(Guid Lease, string InternalPluginName, string PluginName)>
                leasesCopy;
            lock (_gate)
                leasesCopy = _registrations.Values
                    .Select(l => (l.ID, l.InternalPluginName, l.PluginName))
                    .ToList();

            // 這裡本來就是對快照迭代（Add 型再入不會炸），但仍要確認鍵還在：
            // RemoveRegistration() 觸發的取消回呼可能讓承租端順手 ReleaseControl()
            // 掉快照裡的另一把租約，那之後 _registrations[lease] 會擲
            // KeyNotFoundException。與 SuspendLeases() 同一個閘門。
            foreach (var (lease, internalPluginName, pluginName) in leasesCopy)
                if (!plugins.Contains(internalPluginName) &&
                    CheckLeaseExists(lease))
                    // 與 SuspendLeases() 同理：一把收不掉不能連累其他把，
                    // 更不能讓例外冒進 Framework.Update。
                    try
                    {
                        RemoveRegistration(
                            lease, CancellationReasonEnum.LeaseePluginDisabled
                        );
                    }
                    catch (Exception e)
                    {
                        Logging.Error(
                            "Failed to remove the lease held by unloaded plugin '" +
                            pluginName +
                            "'; continuing with the remaining leases.\n" + e);
                    }
        }
        catch (Exception e)
        {
            Logging.Error(
                "The leasee-unloaded check failed.\n" + e);
        }
        finally
        {
            _checkingLeaseePluginsUnloaded = false;
            _framesSinceLastCheck = 0;
        }
    }

    #endregion

    #endregion

    #region Blacklist functionality

    /// <summary>
    ///     List of plugin names that have been revoked by the user.<br />
    ///     Trys to prevent a plugin from immediately re-registering after being
    ///     revoked by the user.
    /// </summary>
    /// <value>
    ///     <b>Key:</b> The former lease ID of the plugin.<br />
    ///     <b>Values:</b><br />
    ///     <b>Item1:</b> The internal plugin name.<br />
    ///     <b>Item2:</b> The <see cref="Lease.ConfigurationsHash" /> of the
    ///     previous lease.<br />
    ///     <b>Item3:</b> The time the lease was revoked.
    /// </value>
    /// <remarks>
    ///     The blacklisting is cleared after 2 minutes.
    /// </remarks>
    private readonly Dictionary<Guid, (string, byte[], DateTime)>
        _userRevokedTemporaryBlacklist = new();

    /// <summary>
    ///     Removes entries from the blacklist that are older than 2 minutes.
    /// </summary>
    private void CleanOutdatedBlacklistEntries()
    {
        var now = DateTime.Now;

        // ReSharper disable once NotAccessedVariable
        // ReSharper disable once RedundantAssignment
        var durationForBan = TimeSpan.FromMinutes(2);
#if DEBUG
        durationForBan = TimeSpan.FromSeconds(15);
#endif

        lock (_gate)
        {
            Dictionary<Guid, (string, byte[], DateTime)> blacklistCopy =
                new(_userRevokedTemporaryBlacklist);
            foreach (var (lease, (_, _, time)) in blacklistCopy)
                if (now - time > durationForBan)
                    _userRevokedTemporaryBlacklist.Remove(lease);
        }
    }

    /// <summary>
    ///     Checks if a lease was revoked by the user and is still blacklisted.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease(string,string)" />.
    /// </param>
    /// <returns>If the lease is blacklisted.</returns>
    internal bool CheckBlacklist(Guid lease)
    {
        CleanOutdatedBlacklistEntries();

        lock (_gate)
            return _userRevokedTemporaryBlacklist.ContainsKey(lease);
    }

    /// <summary>
    ///     Checks if a plugin name was revoked by the user and is still blacklisted.
    /// </summary>
    /// <param name="internalPluginName">
    ///     The internal name of the plugin that was revoked.
    /// </param>
    /// <returns>If the plugin's name is blacklisted.</returns>
    internal bool CheckBlacklist(string internalPluginName)
    {
        CleanOutdatedBlacklistEntries();

        lock (_gate)
            return _userRevokedTemporaryBlacklist.Values
                .Any(entry => entry.Item1 == internalPluginName);
    }

    /// <summary>
    ///     Checks if a configuration hash revoked by the user and is still
    ///     blacklisted.<br />
    ///     The only blacklist check that can trigger after establishing a new lease.
    /// </summary>
    /// <param name="hash">
    ///     The configuration hash of the plugin that was revoked.
    /// </param>
    /// <returns>If the hash is blacklisted.</returns>
    internal bool CheckBlacklist(byte[] hash)
    {
        CleanOutdatedBlacklistEntries();

        lock (_gate)
            return _userRevokedTemporaryBlacklist.Values
                .Any(entry => entry.Item2.SequenceEqual(hash));
    }

    #endregion
}
