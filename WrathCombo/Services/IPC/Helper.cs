#region

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Ipc.Exceptions;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using ECommons.Logging;
using WrathCombo.Attributes;
using WrathCombo.Combos;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Extensions;
using TS = System.TimeSpan;

#endregion

namespace WrathCombo.Services.IPC;

public partial class Helper(ref Leasing leasing)
{
    private readonly Leasing _leasing = leasing;

    /// <summary>
    ///     Checks for typical bail conditions at the time of a set.
    /// </summary>
    /// <param name="result">
    ///     The result to set if the method should bail.
    /// </param>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease(string,string)" />
    /// </param>
    /// <returns>If the method should bail.</returns>
    internal bool CheckForBailConditionsAtSetTime
        (out SetResult result, Guid? lease = null)
    {
        // 刻意不查上游的遠端 IPC 開關：開關檔放在第三方網域，IPC 一律視為啟用。

        // Bail if the lease is not valid
        if (lease is not null &&
            !_leasing.CheckLeaseExists(lease.Value))
        {
            Logging.Warn(BailMessages.InvalidLease);
            result = SetResult.InvalidLease;
            return true;
        }

        // Bail if the lease is blacklisted
        if (lease is not null &&
            _leasing.CheckBlacklist(lease.Value))
        {
            Logging.Warn(BailMessages.BlacklistedLease);
            result = SetResult.BlacklistedLease;
            return true;
        }

        result = SetResult.IGNORED;
        return false;
    }

    /// <summary>
    ///     Gets the "opposite" preset, as in Advanced if given Simple, and vice
    ///     versa.
    /// </summary>
    /// <param name="preset">The preset to search for the opposite of.</param>
    /// <returns>The Opposite-mode preset.</returns>
    internal static CustomComboPreset? GetOppositeModeCombo(CustomComboPreset preset)
    {
        const StringComparison lower = StringComparison.CurrentCultureIgnoreCase;
        var attr = preset.Attributes();

        // Bail if it is not one of the main combos
        if (attr.ComboType is not (ComboType.Advanced or ComboType.Simple))
            return null;

        // Detect the target type
        var targetType =
            attr.CustomComboInfo.InternalName.Contains("single target", lower)
                ? ComboTargetTypeKeys.SingleTarget
                : (attr.CustomComboInfo.InternalName.Contains("- aoe", lower))
                    ? ComboTargetTypeKeys.MultiTarget
                    : ComboTargetTypeKeys.Other;

        // Bail if it is not a Single-Target or Multi-Target primary preset
        if (targetType == ComboTargetTypeKeys.Other)
            return null;

        // Detect the simplicity level
        var simplicityLevel =
            attr.ComboType is ComboType.Simple
                ? ComboSimplicityLevelKeys.Simple
                : ComboSimplicityLevelKeys.Advanced;
        // Flip the simplicity level
        var simplicityLevelToSearchFor =
            simplicityLevel == ComboSimplicityLevelKeys.Simple
                ? ComboSimplicityLevelKeys.Advanced
                : ComboSimplicityLevelKeys.Simple;

        try
        {
            // Get the opposite mode
            var categorizedPreset =
                P.IPCSearch.CurrentJobComboStatesCategorized
                        [(Job)attr.CustomComboInfo.JobID]
                    [targetType][simplicityLevelToSearchFor];

            // Return the opposite mode, as a proper preset
            var oppositeMode = categorizedPreset.FirstOrDefault().Key;
            var oppositeModePreset = (CustomComboPreset)
                Enum.Parse(typeof(CustomComboPreset), oppositeMode, true);
            return oppositeModePreset;
        }
        catch (Exception ex)
        {
            ex.LogWarning(
                "No opposite combo found, this is probably correct if this is a healer.");
            return null;
        }
    }

    #region 從 IPC 執行緒讀原生狀態

    /// <summary>
    ///     這個 helper 靜態路徑自己的節流器（自帶鎖、自帶字典）。
    /// </summary>
    /// <remarks>
    ///     🔴 不要換回 ECommons 的 EzThrottler，理由見 <see cref="IpcThrottle" />。
    /// </remarks>
    private static readonly IpcThrottle StaticThrottle = new();

    /// <summary>
    ///     等 framework 執行緒回話的硬上限。
    /// </summary>
    /// <remarks>
    ///     正常情況下一幀之內就回來了。設這個上限是為了讓「framework 執行緒因為
    ///     別的原因卡住」不會連帶把承租外掛的執行緒無限期卡在這裡。
    /// </remarks>
    private static readonly TS FrameworkReadTimeout =
        TS.FromMilliseconds(500);

    /// <summary>
    ///     在 framework 執行緒上讀一次本地玩家目前的職業／職業（class）列 ID。
    /// </summary>
    /// <returns>
    ///     <c>LocalPlayer.ClassJob.RowId</c>；<see langword="null" /> 表示當下沒有
    ///     本地玩家，或是 framework 執行緒在逾時內沒有回話。
    /// </returns>
    /// <remarks>
    ///     🔴🔴 <b>原生狀態只能在 framework 執行緒讀。</b>
    ///     而帶 <c>[EzIPC]</c> 的端點是跑在<b>承租外掛的執行緒</b>上的。<br />
    ///     🔴 不要改用 <c>Svc.Framework.Run(...)</c>：那一支<b>一律</b> StartNew，
    ///     在 framework 執行緒上同步等它會死結。<br />
    ///     🔴 不要用 <c>task.Wait()</c>：它把例外包成 <c>AggregateException</c>，
    ///     而且不帶逾時就是無限期等。
    /// </remarks>
    internal static uint? CurrentClassJobIdFromFramework()
    {
        // 卸載期旁路：Dalamud 的 RunOnFrameworkThread 在 IsFrameworkUnloading 為真時是「就地在呼叫端執行緒執行」而不是排隊，
        // 承租外掛在關遊戲／停用外掛那一瞬間打進來的話，委派會直接在承租外掛的執行緒上讀 ObjectTable 的共用包裝。
        // 所以卸載期直接回 null：那正是這一支既有的失敗慣例值（「沒有本地玩家」），兩個呼叫端本來就處理得了。
        if (Svc.Framework.IsFrameworkUnloading &&
            !Svc.Framework.IsInFrameworkUpdateThread)
        {
            if (StaticThrottle.Throttle("ipcFrameworkUnloadingJobRead",
                    TS.FromSeconds(30)))
                Logging.Information(
                    "The framework is unloading, so the current job was not " +
                    "read from a non-framework thread; treating it as " +
                    "'no player available'.");
            return null;
        }

        Task<uint?> task;
        try
        {
            task = Svc.Framework.RunOnFrameworkThread(ReadClassJobIdOnFramework);
        }
        catch (Exception e)
        {
            // 已在 framework 執行緒上時是就地執行，委派自己擲的例外會直接冒到這裡。
            Logging.Error("Failed to read the current job: " + e);
            return null;
        }

        if (!task.IsCompleted &&
            Task.WaitAny([task], FrameworkReadTimeout) < 0)
        {
            if (StaticThrottle.Throttle("ipcFrameworkJobReadTimeout",
                    TS.FromSeconds(30)))
                Logging.Information(
                    "Timed out waiting for the framework thread to report the " +
                    "current job; treating it as 'no player available'.");
            return null;
        }

        if (task.IsCompletedSuccessfully)
            return task.Result;

        if (task.IsFaulted)
            Logging.Error("Failed to read the current job: " + task.Exception);

        return null;
    }

    /// <summary>
    ///     <see cref="CurrentClassJobIdFromFramework" /> 真正在 framework 執行緒上
    ///     跑的那一段：兩次原生存取合併成一次，中間不會被「玩家消失」插進來。
    /// </summary>
    private static uint? ReadClassJobIdOnFramework() =>
        CustomComboFunctions.LocalPlayer is { } player
            ? player.ClassJob.RowId
            : null;

    /// <summary>
    ///     框架執行緒每幀發佈的目前職業（class）列 ID；<c>-1</c>＝沒有本地玩家。
    ///     任何執行緒都可以讀，不碰原生；只用來判快取鍵，答案仍以 <see cref="CurrentClassJobIdFromFramework" /> 為準。
    /// </summary>
    private static long _classJobSnapshot = -1;

    internal static long ClassJobSnapshot => Volatile.Read(ref _classJobSnapshot);

    /// <summary> 只在框架執行緒呼叫，每幀一次。 </summary>
    internal static void PublishClassJobSnapshot(IPlayerCharacter? player) =>
        Volatile.Write(ref _classJobSnapshot,
            player is null ? -1 : player.ClassJob.RowId);

    #endregion


    /// <summary>
    ///     「這個端點只在 framework 執行緒上作答」的閘門。
    /// </summary>
    /// <param name="endpointName">端點名稱，只用於診斷訊息與節流的 key。</param>
    /// <returns>
    ///     <see langword="true" /> ＝ 現在就在 framework 執行緒上，可以照常讀遊戲狀態；<br />
    ///     <see langword="false" /> ＝ 呼叫端應該直接回 fail-safe 值，不要碰任何遊戲狀態。
    /// </returns>
    /// <remarks>
    ///     🔴🔴 給<b>那些沒辦法丟回 framework 執行緒的端點</b>用。
    ///     🔑 fail-safe 一律是 <see langword="false" />（不能插入／動作不可用），
    ///     呼叫端照著做只會少放一個技能，不會做出錯的動作。
    /// </remarks>
    internal static bool AnswerOnlyOnFrameworkThread(string endpointName)
    {
        if (Svc.Framework.IsInFrameworkUpdateThread)
            return true;

        if (StaticThrottle.Throttle("ipcOffFrameworkThread:" + endpointName,
                TS.FromMinutes(1)))
            Logging.Information(
                $"'{endpointName}' was called from a non-framework thread, so it " +
                "returned false without reading any game state. " +
                $"Caller: {DescribeIpcCaller()}. " +
                "Please call this from your framework/tick handler instead.");

        return false;
    }

    /// <summary>
    ///     盡量描述「是誰在呼叫」：呼叫端執行緒上第一個不屬於 Wrath／Dalamud／BCL
    ///     的堆疊框，加上執行緒識別。
    /// </summary>
    /// <remarks>
    ///     ⚠️ 只在節流放行的那一次才會走到 —— 建 <see cref="System.Diagnostics.StackTrace" />
    ///     不便宜，不要搬到常走的路徑上。
    /// </remarks>
    private static string DescribeIpcCaller()
    {
        var thread = Thread.CurrentThread;
        var who = "unknown";

        try
        {
            foreach (var frame in new StackTrace(false).GetFrames())
            {
                var method = frame.GetMethod();
                var type = method?.DeclaringType;
                var assembly = type?.Assembly.GetName().Name;

                if (assembly is null or "WrathCombo" or "Dalamud" or "ECommons" ||
                    assembly.StartsWith("System"))
                    continue;

                who = $"{assembly}!{type!.FullName}.{method!.Name}";
                break;
            }
        }
        catch (Exception e)
        {
            who = $"unknown ({e.GetType().Name})";
        }

        var name = string.IsNullOrEmpty(thread.Name) ? "" : $" '{thread.Name}'";
        return $"{who} [thread {thread.ManagedThreadId}{name}]";
    }

    #region Auto-Rotation Ready

    /// <summary>
    ///     Checks the current job to see whatever specified mode is enabled
    ///     (enabled and enabled in Auto-Mode).
    /// </summary>
    /// <param name="mode">
    ///     The <see cref="ComboTargetTypeKeys">Target Type</see> to check.
    /// </param>
    /// <param name="enabledStateToCheck">
    ///     The <see cref="ComboStateKeys">State</see> to check.
    /// </param>
    /// <param name="previousMatch">
    ///     The <see cref="ComboSimplicityLevelKeys">Simplicity Level</see> that
    ///     was used in the last set of calls of this method, to make sure that it
    ///     uses the same level for both checking if enabled and enabled in
    ///     Auto-Mode.<br />
    ///     Or <see langword="null" /> if it is the first call, so the level can be
    ///     set.
    /// </param>
    /// <returns>
    ///     Whether the current job has simple or advanced combo enabled
    ///     (however specified) for the target type specified.
    /// </returns>
    /// <seealso cref="Provider.IsCurrentJobConfiguredOn" />
    /// <seealso cref="Provider.IsCurrentJobAutoModeOn" />
    internal ComboSimplicityLevelKeys? CheckCurrentJobModeIsEnabled
    (ComboTargetTypeKeys mode,
        ComboStateKeys enabledStateToCheck,
        ComboSimplicityLevelKeys? previousMatch = null)
    {
        // 🔴 這一支從 [EzIPC] 端點到得了（Provider.IsCurrentJobConfiguredOn／
        //    IsCurrentJobAutoModeOn），也就是承租
        //    外掛的執行緒。原生狀態只能在 framework 執行緒讀，見
        //    CurrentClassJobIdFromFramework()。
        var classJobId = CurrentClassJobIdFromFramework();
        if (classJobId is null)
            return null;

        return CheckJobModeIsEnabled(
            classJobId.Value, mode, enabledStateToCheck, previousMatch);
    }

    /// <summary>
    ///     同 <see cref="CheckCurrentJobModeIsEnabled" />，但職業（class）列 ID 由呼叫端給，
    ///     必須是已在框架執行緒讀好的值。
    /// </summary>
    internal ComboSimplicityLevelKeys? CheckJobModeIsEnabled
    (uint classJobId,
        ComboTargetTypeKeys mode,
        ComboStateKeys enabledStateToCheck,
        ComboSimplicityLevelKeys? previousMatch = null)
    {
        // Convert current job/class to a job, if it is a class
        var job = (Job)CustomComboFunctions.JobIDs.ClassToJob(classJobId);

        // Get the user's settings for this job
        P.IPCSearch.CurrentJobComboStatesCategorized.TryGetValue(job,
            out var comboStates);

        // Bail if there are no combos found for this job
        if (comboStates is null || comboStates.Count == 0)
            return null;

        // Try to get the Simple Mode settings
        comboStates[mode]
            .TryGetValue(ComboSimplicityLevelKeys.Simple, out var simpleResults);
        var simpleHigher = simpleResults?.FirstOrDefault();

        #region Override the Values with any IPC-control

        // 只在區域變數算出「這一趟要看的那個狀態」，完全不碰共用字典。
        var autoActions = P.IPCSearch.AutoActions;
        var enabledActions = P.IPCSearch.EnabledActions;

        // P.IPCSearch 的這兩份已經把租約的值疊在使用者設定上。
        bool StateOf(CustomComboPreset preset) =>
            enabledStateToCheck switch
            {
                ComboStateKeys.AutoMode => autoActions[preset],
                ComboStateKeys.Enabled => enabledActions.Contains(preset),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(enabledStateToCheck), enabledStateToCheck, null),
            };

        CustomComboPreset? simpleComboPreset = simpleHigher is null
            ? null
            : (CustomComboPreset)
            Enum.Parse(typeof(CustomComboPreset), simpleHigher.Value.Key, true);
        bool? simpleState = simpleComboPreset is null
            ? null
            : StateOf(simpleComboPreset.Value);

        #endregion

        // Get the Advanced Mode settings
        var advancedKey =
            comboStates[mode][ComboSimplicityLevelKeys.Advanced].First().Key;

        #region Override the Values with any IPC-control

        var advancedComboPreset = (CustomComboPreset)
            Enum.Parse(typeof(CustomComboPreset), advancedKey, true);
        var advancedState = StateOf(advancedComboPreset);

        #endregion

        // If the simplicity level is set, check that specifically instead of either
        if (previousMatch is not null)
        {
            if (previousMatch == ComboSimplicityLevelKeys.Simple &&
                simpleState == true)
                return ComboSimplicityLevelKeys.Simple;
            return advancedState
                ? ComboSimplicityLevelKeys.Advanced
                : null;
        }

        // Check for either Simple or Advanced being ready
        return simpleState == true ?
            ComboSimplicityLevelKeys.Simple :
            advancedState ? ComboSimplicityLevelKeys.Advanced :
                null;
    }

    /// <summary>
    ///     Cache of the combos to set the current job to be Auto-Rotation ready.
    /// </summary>
    private static readonly Dictionary<Job, List<string>>
        CombosForARCache = new();

    /// <summary>
    ///     Gets the combos to set the current job to be Auto-Rotation ready.
    /// </summary>
    /// <param name="job">The job to get the combos for.</param>
    /// <param name="includeOptions">
    ///     Whether to include the options for the combos.
    /// </param>
    /// <returns>
    ///     A list of combo names to set the current job to be Auto-Rotation ready.
    /// </returns>
    /// <seealso cref="Provider.SetCurrentJobAutoRotationReady" />
    internal static List<string>? GetCombosToSetJobAutoRotationReady
        (Job job, bool includeOptions = true)
    {
        #region Getting Combo data

        if (CombosForARCache.TryGetValue(job, out var value))
            return value;

        P.IPCSearch.CurrentJobComboStatesCategorized.TryGetValue(job,
            out var comboStates);

        if (comboStates is null)
            return null;

        #endregion

        List<string> combos = [];

        #region Single Target

        comboStates[ComboTargetTypeKeys.SingleTarget]
            .TryGetValue(ComboSimplicityLevelKeys.Simple, out var stSimpleResults);
        var stSimple =
            stSimpleResults?.FirstOrDefault();

        if (stSimple is not null)
            combos.Add(comboStates[ComboTargetTypeKeys.SingleTarget]
                [ComboSimplicityLevelKeys.Simple].First().Key);
        else
        {
            var stAdvanced = comboStates[ComboTargetTypeKeys.SingleTarget]
                [ComboSimplicityLevelKeys.Advanced].First().Key;
            combos.Add(stAdvanced);
            if (includeOptions)
                combos.AddRange(P.IPCSearch.OptionNamesByJob[job][stAdvanced]);
        }

        #endregion

        #region Multi Target

        comboStates[ComboTargetTypeKeys.MultiTarget]
            .TryGetValue(ComboSimplicityLevelKeys.Simple, out var mtSimpleResults);
        var mtSimple =
            mtSimpleResults?.FirstOrDefault();

        if (mtSimple is not null)
            combos.Add(comboStates[ComboTargetTypeKeys.MultiTarget]
                [ComboSimplicityLevelKeys.Simple].First().Key);
        else
        {
            var mtAdvanced = comboStates[ComboTargetTypeKeys.MultiTarget]
                [ComboSimplicityLevelKeys.Advanced].First().Key;
            combos.Add(mtAdvanced);
            if (includeOptions)
                combos.AddRange(P.IPCSearch.OptionNamesByJob[job][mtAdvanced]);
        }

        #endregion

        #region Heals

        if (comboStates.TryGetValue(ComboTargetTypeKeys.HealST, out var healResults))
            combos.Add(healResults
                [ComboSimplicityLevelKeys.Other].First().Key);
        var healST = healResults?.FirstOrDefault().Key;
        if (healST is not null)
        {
            var healSTPreset = comboStates[ComboTargetTypeKeys.HealST]
                [ComboSimplicityLevelKeys.Other].First().Key;
            if (includeOptions)
                combos.AddRange(P.IPCSearch.OptionNamesByJob[job][healSTPreset]);
        }

        if (comboStates.TryGetValue(ComboTargetTypeKeys.HealMT, out healResults))
            combos.Add(healResults
                [ComboSimplicityLevelKeys.Other].First().Key);
        var healMT = healResults?.FirstOrDefault().Key;
        if (healMT is not null)
        {
            var healMTPreset = comboStates[ComboTargetTypeKeys.HealMT]
                [ComboSimplicityLevelKeys.Other].First().Key;
            if (includeOptions)
                combos.AddRange(P.IPCSearch.OptionNamesByJob[job][healMTPreset]);
        }

        #endregion

        if (includeOptions)
            CombosForARCache[job] = combos;
        return combos;
    }

    #endregion

    #region IPC Callback

    /// <summary>
    ///     承租外掛註冊「租約被取消」回呼時用的 IPC 方法名。
    ///     完整的 IPC tag 是 <c>{承租外掛自己的前綴}.WrathComboCallback</c>。
    /// </summary>
    private const string LeaseeCallbackName = "WrathComboCallback";

    /// <summary>
    ///     Calls a leasee's lease-cancellation callback over IPC.
    /// </summary>
    /// <param name="prefix">The leasee's IPC prefix.</param>
    /// <param name="reason">The reason the lease is being cancelled.</param>
    /// <param name="additionalInfo">
    ///     Any additional information to pass to the leasee.
    /// </param>
    /// <remarks>
    ///     🔴 call gate <b>一定要每次呼叫都照 prefix 重新取得</b>，不可以存成靜態欄位。
    /// </remarks>
    internal static void CallIPCCallback(string prefix, CancellationReason reason,
        string additionalInfo = "")
    {
        var ipcName = prefix + "." + LeaseeCallbackName;

        try
        {
            Svc.PluginInterface
                .GetIpcSubscriber<int, string, object>(ipcName)
                .InvokeAction((int)reason, additionalInfo);
        }
        catch (IpcNotReadyError)
        {
            // 承租外掛沒註冊這個回呼，或是已經卸載了。這是預期得到的狀況，不是錯誤。
            // 要使用者回報的診斷。
            Logging.Information(
                "Leasee has no lease-cancellation callback registered " +
                "(IPC method '" + ipcName + "' is not registered); " +
                "the cancellation could not be delivered to it.");
        }
        catch (Exception e)
        {
            // 走到這裡幾乎都是「承租外掛的回呼自己擲例外」——它是在我們的堆疊上同步執行的。
            // 例外要一起印：「call gate 不存在」與「對方的回呼炸了」處置完全不同，只印 prefix 分不出來。
            Logging.Error(
                "Failed to call IPC callback with IPC prefix: " + prefix + "\n" + e);
        }
    }

    #endregion
}

/// <summary>
///     Simple Wrapper for logging IPC events, to help keep things consistent.
/// </summary>
internal static class Logging
{
    private const string Prefix = "[Wrath IPC] ";

    private static StackTrace StackTrace => new();

    private static string PrefixMethod
    {
        get
        {
            for (var i = 3; i >= 0; i--)
            {
                try
                {
                    var frame = StackTrace.GetFrame(i);
                    var method = frame.GetMethod();
                    var className = method.DeclaringType.Name;
                    var methodName = method.Name;
                    return $"[{className}.{methodName}] ";
                }
                catch
                {
                    // Continue to the next index
                }
            }

            return "[Unknown Method] ";
        }
    }

    public static void Verbose(string message) =>
        PluginLog.Verbose(Prefix + PrefixMethod + message);

    public static void Log(string message) =>
        PluginLog.Debug(Prefix + PrefixMethod + message);

    /// <summary>
    ///     要請使用者回報的診斷訊息用這個。
    /// </summary>
    public static void Information(string message) =>
        PluginLog.Information(Prefix + PrefixMethod + message);

    public static void Warn(string message) =>
        PluginLog.Warning(Prefix + PrefixMethod + message
#if DEBUG
                          + "\n" + (StackTrace)
#endif
        );

    public static void Error(string message) =>
        PluginLog.Error(Prefix + PrefixMethod + message + "\n" + (StackTrace));
}
