#region

using System;
using ECommons;
using ECommons.EzIpcManager;
using ECommons.Logging;
using ECommons.Reflection;
using EZ = ECommons.Throttlers.EzThrottler;
using TS = System.TimeSpan;

// ReSharper disable InlineTemporaryVariable

#endregion

namespace WrathCombo.Services.IPC_Subscriber;

/// <summary>
///     訂閱 BossMod / BossModReborn 的 IPC，外加少量反射查詢。
/// </summary>
/// <remarks>
///     注意：<b>IPC 標籤前綴與「外掛安裝偵測用名」是兩件事，不可混用。</b>
///     所以本類別的每個訂閱都寫「完整標籤名 + <c>applyPrefix: false</c>」，
///     把「安裝偵測用名」和「IPC 前綴」徹底脫鉤。
/// </remarks>
internal sealed class BossModIPC(
    string pluginName,
    Version validVersion)
    : ReusableIPC(pluginName, validVersion)
{
    public bool HasAutomaticActionsQueued()
    {
        if (!IsEnabled)
        {
            PluginLog.Verbose($"[ConflictingPlugins] [{PluginName}] " +
                            $"IPC is not enabled.");
            return false;
        }

        if (!_hasEntries.TryInvoke(out var hasEntries))
        {
            PluginLog.Verbose($"[ConflictingPlugins] [{PluginName}] " +
                            $"`ActionQueue.HasEntries` IPC not ready yet.");
            return false;
        }

        PluginLog.Verbose(
            $"[ConflictingPlugins] [{PluginName}] `ActionQueue.HasEntries`: " +
            hasEntries);
        return hasEntries;
    }

    /// <summary>
    ///     BossMod(Reborn) 的「手動動作佇列」接管是否啟用。
    /// </summary>
    /// <returns>
    ///     對方有註冊 <c>BossMod.ActionQueue.UseManualQueueEnabled</c> 且回報啟用時為
    ///     <c>true</c>；沒安裝、版本太舊（還沒有這個端點）或 IPC 尚未就緒時一律回
    ///     <c>false</c> —— 也就是<b>優雅退回「照 <c>UseAction</c> 回傳值辦事」的現行行為</b>。
    /// </returns>
    /// <remarks>
    ///     用途見 <c>AutoRotation/AutoRotationController.cs</c>：BossMod(Reborn) 攔下
    ///     <c>ActionManager::UseAction</c> 並把技能收進自己的佇列時，detour 會回傳
    ///     <c>false</c> —— 我們收到的是「失敗」，但技能其實已排隊、稍後會由對方送出。
    /// </remarks>
    public bool IsManualQueueTakeoverEnabled()
    {
        if (!IsEnabled)
            return false;

        if (!_useManualQueueEnabled.TryInvoke(out var enabled))
        {
            PluginLog.Verbose($"[ConflictingPlugins] [{PluginName}] " +
                              "`ActionQueue.UseManualQueueEnabled` IPC not " +
                              "available (older BossMod build?).");
            return false;
        }

        PluginLog.Verbose(
            $"[ConflictingPlugins] [{PluginName}] " +
            $"`ActionQueue.UseManualQueueEnabled`: {enabled}");
        return enabled;
    }

    /// <summary>
    ///     對方的 AI 是不是正在自己挑目標（＝會和 Wrath 的自動選目標打架）。
    /// </summary>
    /// <remarks>
    ///     判準（對照 <c>AIBehaviour.Execute</c>）：AI 要真的在跑（<c>Beh != null</c>）、
    ///     沒有 <c>ForbidActions</c>（那會讓 <c>forbidTargeting</c> 成立、完全不挑目標）、
    ///     也沒有 <c>ManualTarget</c>（那是「沿用玩家自己的硬目標」，不算它在挑）。
    ///     已知缺口：原版 BossMod（非 Reborn）的 AI 設定不在 <c>Plugin._ai</c> 底下（要走 <c>TickService</c> → <c>_rotation</c> → <c>_aiConfig</c>），
    ///     本方法對它會在第一個判空就 return false；刻意不修（無法離線驗證）。
    /// </remarks>
    public bool IsAutoTargetingEnabled()
    {
        if (!PluginIsLoaded)
        {
            PluginLog.Verbose($"[ConflictingPlugins] [{PluginName}] " +
                            $"Plugin is not loaded.");
            return false;
        }

        var ai = Plugin.GetFoP("_ai");
        if (ai == null)
        {
            PluginLog.Verbose(
                $"[ConflictingPlugins] [{PluginName}] Could not access _ai field");
            return false;
        }

        if (ai.GetFoP("Beh") == null)
        {
            PluginLog.Verbose(
                $"[ConflictingPlugins] [{PluginName}] AI is idle (`Beh` is null)");
            return false;
        }

        var aiConfig = ai.GetFoP("_config");
        if (aiConfig == null)
        {
            PluginLog.Verbose(
                $"[ConflictingPlugins] [{PluginName}] Could not access AI._config field");
            return false;
        }

        // 用非泛型 GetFoP + 模式比對：泛型版是 (T)GetFoP(...)，欄位不存在時會在
        // 拆箱 null 時擲 NullReferenceException，對上游改名毫無抵抗力。
        if (aiConfig.GetFoP("ForbidActions") is not bool aiForbidActions ||
            aiConfig.GetFoP("ManualTarget") is not bool aiManualTarget)
        {
            PluginLog.Verbose(
                $"[ConflictingPlugins] [{PluginName}] Could not read " +
                $"`AIConfig.ForbidActions` / `AIConfig.ManualTarget`");
            return false;
        }

        PluginLog.Verbose(
            $"[ConflictingPlugins] [{PluginName}] `AI.Running`: true, " +
            $"`AI.ForbidActions`: {aiForbidActions}, " +
            $"`AI.ManualTarget`: {aiManualTarget}");

        return !aiForbidActions && !aiManualTarget;
    }

    /// <summary>
    ///     BossMod(Reborn) 目前標記為「這一發該打斷」的敵人 InstanceID 清單。
    /// </summary>
    /// <returns>
    ///     空陣列 ＝ 沒有任何標記、對方沒安裝、端點不存在、或呼叫擲了例外。<br />
    ///     呼叫端一律把空陣列當成「沒有機制資訊」，<b>退回現行行為</b>。
    /// </returns>
    /// <remarks>
    ///     🔴 對方回的是<b>模組的原始標記</b>，刻意不含 InCombat／是否正在詠唱之類的
    ///     策略過濾（BMR 自己的消費端是在旗標之外另外加的）。所以這份清單只能當
    ///     「優先名單」跟 Wrath 既有的過濾條件<b>取交集</b>，不可以拿來取代既有過濾。
    /// </remarks>
    public ulong[] ShouldInterruptTargets() =>
        SafeHintList(_shouldInterruptTargets, "Hints.ShouldInterruptTargets");

    /// <summary>
    ///     BossMod(Reborn) 目前標記為「這一發該暈眩」的敵人 InstanceID 清單。
    /// </summary>
    /// <inheritdoc cref="ShouldInterruptTargets" />
    public ulong[] ShouldStunTargets() =>
        SafeHintList(_shouldStunTargets, "Hints.ShouldStunTargets");

    /// <summary>
    ///     取一份提示清單，任何失敗都降級成「沒有機制資訊」（空陣列）。
    /// </summary>
    /// <remarks>
    ///     若執行期型別對不上，Dalamud 會擲出的<b>不是</b>
    ///     <c>IpcNotReadyError</c> —— 而 ECommons 的 <c>TryInvoke</c>
    ///     <b>只攔 <c>IpcNotReadyError</c></b>，其餘例外會一路穿到連段裡。
    ///     所以這裡另外包一層 <see langword="try" />：任何例外都吃掉並回空陣列，
    ///     ＝功能降級成「BMR 不在」，不是壞掉。
    /// </remarks>
    private ulong[] SafeHintList(Func<ulong[]> ipc, string tag)
    {
        // 刻意不判 IsEnabled（每次都反射走訪已安裝插件清單，而這裡每幀被問兩次）：
        // 可用性由呼叫端的 BossModCheck.IpcAvailable（2 秒快取）把關；對方不在時 TryInvoke 回 false。
        try
        {
            if (!ipc.TryInvoke(out var hints))
            {
                PluginLog.Verbose($"[MechanicHints] [{PluginName}] " +
                                  $"`{tag}` IPC not available.");
                return [];
            }

            return hints ?? [];
        }
        catch (Exception e)
        {
            // 要使用者回報的診斷：這條是「功能悄悄不動」時唯一查得到的線索。
            if (EZ.Throttle($"MechanicHintsFailure_{tag}", TS.FromMinutes(1)))
                PluginLog.Information(
                    $"[MechanicHints] [{PluginName}] `{tag}` 呼叫失敗，" +
                    $"本次視同「沒有機制資訊」並退回現行的打斷／暈眩目標選擇：" +
                    e.ToStringFull());
            return [];
        }
    }

    /// <summary>
    ///     <c>BossMod.Hints.IsActionDashSafe</c>：(技能 id, 目標 GameObjectId) → 0 不知道／1 安全／2 危險。
    /// </summary>
    /// <remarks>
    ///     刻意不包 try、也不判 <see cref="ReusableIPC.IsEnabled" />（每次都反射掃外掛清單）：
    ///     呼叫端 <c>DashSafety</c> 用 2 秒快取的可用性判斷，並自己攔所有例外與退避。
    /// </remarks>
    internal Func<uint, ulong, int> IsActionDashSafe => _isActionDashSafe;

    private long _raidwideFailureLoggedAt;

    /// <summary>
    ///     <c>BossMod.Hints.NextRaidwideDamageIn</c>：下一次全體傷害還有幾秒（對方沒有預告時回 <see cref="float.MaxValue" />）。
    /// </summary>
    /// <returns>
    ///     <see langword="null" />＝端點沒就緒、型別對不上或擲了任何例外，呼叫端一律當「沒有預告」。
    /// </returns>
    /// <remarks>
    ///     只能在框架執行緒呼叫：對方把這個端點包在主執行緒閘門裡，別的執行緒呼叫會阻塞最多 5 秒。
    /// </remarks>
    public float? NextRaidwideDamageIn()
    {
        try
        {
            return _nextRaidwideDamageIn.TryInvoke(out var seconds) ? seconds : null;
        }
        catch (Exception e)
        {
            // TryInvoke 只攔 IpcNotReadyError；IpcTypeMismatchError 與對方擲出的例外在這裡吃掉。
            var now = Environment.TickCount64;
            if (now - _raidwideFailureLoggedAt >= 60_000)
            {
                _raidwideFailureLoggedAt = now;
                PluginLog.Information(
                    $"[WrathCombo][自動減傷] [{PluginName}] `Hints.NextRaidwideDamageIn` 呼叫失敗，" +
                    "本次視同沒有預告、退回敵方詠唱判斷：" + e.ToStringFull());
            }
            return null;
        }
    }

    public DateTime LastModified()
    {
        if (!IsEnabled) return DateTime.MinValue;

        try
        {
            return _lastModified();
        }
        catch (Exception e)
        {
            PluginLog.Warning($"[ConflictingPlugins] [{PluginName}] " +
                              $"`Configuration.LastModified` failed: " +
                              e.ToStringFull());
            return DateTime.MinValue;
        }
    }

    // 下面每個標籤都寫全名並關掉前綴（applyPrefix: false）—— 原因見類別註解。
#pragma warning disable CS0649, CS8618 // Complaints of the method
    [EzIPC("BossMod.Rotation.ActionQueue.HasEntries", false)]
    private readonly Func<bool> _hasEntries = null!;

    [EzIPC("BossMod.Configuration.LastModified", false)]
    private readonly Func<DateTime> _lastModified = null!;

    [EzIPC("BossMod.ActionQueue.UseManualQueueEnabled", false)]
    private readonly Func<bool> _useManualQueueEnabled = null!;

    [EzIPC("BossMod.Hints.ShouldInterruptTargets", false)]
    private readonly Func<ulong[]> _shouldInterruptTargets = null!;

    [EzIPC("BossMod.Hints.ShouldStunTargets", false)]
    private readonly Func<ulong[]> _shouldStunTargets = null!;

    [EzIPC("BossMod.Hints.IsActionDashSafe", false)]
    private readonly Func<uint, ulong, int> _isActionDashSafe = null!;

    [EzIPC("BossMod.Hints.NextRaidwideDamageIn", false)]
    private readonly Func<float> _nextRaidwideDamageIn = null!;
#pragma warning restore CS8618, CS0649
}
