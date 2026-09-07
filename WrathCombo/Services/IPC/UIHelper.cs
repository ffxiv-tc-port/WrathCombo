#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using WrathCombo.AutoRotation;
using WrathCombo.Combos;
using WrathCombo.CustomComboNS.Functions;
// ReSharper disable VariableHidesOuterVariable

#endregion

namespace WrathCombo.Services.IPC;

public class UIHelper(Leasing leasing)
{
    private readonly Leasing _leasing = leasing;
    private readonly Search _search = P.IPCSearch;

    #region Checks for the UI

    #region Auto-Rotation

    private DateTime? _autoRotationUpdated;

    private (string controllers, bool state)
        AutoRotationControlled { get; set; } = (string.Empty, false);

    internal (string controllers, bool state)? AutoRotationStateControlled()
    {
        // Return the cached value if it is valid, fastest
        if (string.IsNullOrEmpty(AutoRotationControlled.controllers) &&
            _autoRotationUpdated is not null &&
            _autoRotationUpdated == _leasing.AutoRotationStateUpdated)
            return AutoRotationControlled;

        // Bail if the state is not controlled, fast
        var controlled = _leasing.CheckAutoRotationControlled();
        if (controlled is null)
            return null;

        // Re-populate the cache with the current state, slowest
        //
        // 🔴 這裡跑在繪製執行緒上，而 IPC 端點在別條執行緒上改同一批字典
        //    ⇒ 走訪一定要在 Leasing 的鎖內做，拿回來的是值不是活字典。
        // ⚠️ 順手修掉一個既有的 .First()：上面的 CheckAutoRotationControlled()
        //    與這一行之間鎖是放開的，租約可能剛好在這個空檔被收走，
        //    原本的 .First() 會擲 InvalidOperationException（"Sequence contains
        //    no elements"）。快照回 null 就照「沒人控制」處理。
        var snapshot = _leasing.SnapshotAutoRotationControllers();
        if (snapshot is null)
            return null;

        AutoRotationControlled =
            (string.Join(", ", snapshot.Value.Controllers), snapshot.Value.State);
        _autoRotationUpdated = _leasing.AutoRotationStateUpdated;

        return AutoRotationControlled;
    }

    #endregion

    #region Jobs

    private DateTime? _jobsUpdated;

    private Dictionary<string, (string controllers, bool state)>
        JobsControlled { get; } = new();

    internal (string controllers, bool state)? JobControlled(uint job)
    {
        var jobName = CustomComboFunctions.JobIDs.JobIDToShorthand(job);

        if (_jobsUpdated != _leasing.JobsUpdated)
            JobsControlled.Clear();

        // Return the cached value if it is valid, fastest
        if (_jobsUpdated is not null &&
            _jobsUpdated == _leasing.JobsUpdated &&
            JobsControlled.TryGetValue(jobName, out var jobControlled))
        {
            if (string.IsNullOrEmpty(jobControlled.controllers))
                return null;
            return jobControlled;
        }

        // Bail if the job is not controlled, fast
        if ((JobsControlled.TryGetValue(jobName, out var jobNotControlled) &&
             string.IsNullOrEmpty(jobNotControlled.controllers)) ||
            _leasing.CheckJobControlled((int)job) is null)
        {
            if (string.IsNullOrEmpty(jobNotControlled.controllers))
            {
                JobsControlled[jobName] = (string.Empty, false);
                _jobsUpdated = _leasing.JobsUpdated;
            }

            return null;
        }

        // Re-populate the cache with the current set of controlled jobs, slowest
        JobsControlled.Clear();
        foreach (var jobListing in Enum.GetValues(typeof(Job)))
            JobsControlled[jobListing.ToString()!] = (string.Empty, false);
        foreach (var controlledJob in _search.AllJobsControlled)
            JobsControlled[controlledJob.Key.ToString()] =
                (string.Join(", ", controlledJob.Value.Keys), true);
        _jobsUpdated = _leasing.JobsUpdated;

        return JobsControlled[jobName];
    }

    #endregion

    #region Presets

    /// <summary>
    ///     <see cref="PresetsControlled" /> 這份快取上次重建時的時間戳，
    ///     以 <c>DateTime.Ticks + 1</c> 存；<c>0</c> 代表「還沒設定過」
    ///     （等同改動前的 <see langword="null" />）。
    /// </summary>
    /// <remarks>
    ///     🔴🔴 改動前這是 <c>DateTime?</c>，也就是 <c>bool</c> ＋ <c>DateTime</c>
    ///     共 16 bytes 的結構 —— <b>寫入不是原子的</b>。而
    ///     <see cref="PresetControlled" /> 被兩種執行緒同時呼叫：繪製執行緒（設定
    ///     視窗每一格 preset）與<b>承租外掛自己的執行緒</b>（<c>[EzIPC]</c> 端點 →
    ///     <c>Search.PresetStates</c> → <c>CustomComboFunctions.IsEnabled</c> →
    ///     這一支）。撕裂讀的結果是「有值旗標是新的、時間值還是舊的」之類的組合，
    ///     會讓快取有效性判斷得到錯的答案。<br />
    ///     🔑 換成 <see cref="long" />：x64 上對齊的 64 位元讀寫本來就是原子的，
    ///     配 <see cref="Volatile" /> 補上順序保證就夠了。<b>不用
    ///     <c>Interlocked</c></b> —— 這裡沒有「讀出來算一算再寫回去」的複合操作，
    ///     只有整份覆寫。<br />
    ///     🔴 <b>刻意存 <c>Ticks + 1</c> 而不是 <c>Ticks</c></b>：
    ///     <c>DateTime.MinValue.Ticks</c> 就是 <c>0</c>，而下面算出來的
    ///     <c>presetsUpdated</c> 在還沒有任何租約時真的會是
    ///     <see cref="DateTime.MinValue" /> ⇒ 直接存 Ticks 會和「還沒設定過」
    ///     的哨兵值撞在一起，把「設過了、值是 MinValue」誤判成「沒設過」。<br />
    ///     📌 同檔的 <c>_jobsUpdated</c> 與 <c>_autoRotationConfigsUpdated</c>
    ///     沒有一起改：它們只在 <c>JobControlled</c>／
    ///     <c>AutoRotationConfigControlled</c> 用到，而那兩支只有繪製執行緒到得了
    ///     （呼叫點是 UIHelper 自己的 UI 方法與 Window/Tabs/PvEFeatures.cs，
    ///     沒有任何 IPC 端點的路徑走到），連帶它們守的兩份裸
    ///     <see cref="Dictionary{TKey,TValue}" /> 也是單一執行緒的。
    /// </remarks>
    private long _presetsUpdatedTicks;

    private ConcurrentDictionary<string, (string controllers, bool enabled, bool autoMode)>
        PresetsControlled { get; } = new();

    internal (string controllers, bool enabled, bool autoMode)?
        PresetControlled(CustomComboPreset preset)
    {
        var presetName = preset.ToString();

        var presetsUpdated = (DateTime)
            (_leasing.CombosUpdated > _leasing
                .OptionsUpdated
                ? _leasing.CombosUpdated
                : _leasing.OptionsUpdated ?? DateTime.MinValue);

        // 🔴 只讀一次欄位：改動前這裡讀了兩次（判「不等」一次、判「不是 null」
        //    一次），中間別條執行緒改掉的話兩個判斷會落在不同的值上。
        var stamp = presetsUpdated.Ticks + 1;
        var cachedStamp = Volatile.Read(ref _presetsUpdatedTicks);

        if (cachedStamp != stamp && cachedStamp != 0)
            PresetsControlled.Clear();

        // Return the cached value if it is valid, fastest
        if (cachedStamp != 0 &&
            cachedStamp == stamp &&
            PresetsControlled.TryGetValue(presetName, out var presetControlled))
        {
            if (string.IsNullOrEmpty(presetControlled.controllers))
                return null;
            return presetControlled;
        }

        // Bail if the preset is not controlled, fast-ish
        if ((PresetsControlled.TryGetValue(presetName,
                 out var presetNotControlled) &&
             string.IsNullOrEmpty(presetNotControlled.controllers)) ||
            (_leasing.CheckComboControlled(presetName) is null &&
             _leasing.CheckComboOptionControlled(presetName) is null))
        {
            if (string.IsNullOrEmpty(presetNotControlled.controllers))
            {
                PresetsControlled[presetName] =
                    (string.Empty, false, false);
                Volatile.Write(ref _presetsUpdatedTicks, stamp);
            }

            return null;
        }

        // Re-populate the cache with the current set of controlled presets, slowest
        PresetsControlled.Clear();
        foreach (var controlledPreset in _search.AllPresetsControlled)
            PresetsControlled[controlledPreset.Key.ToString()] =
                (string.Join(", ", controlledPreset.Value.Keys),
                    controlledPreset.Value.Values.First().enabled,
                    controlledPreset.Value.Values.First().autoMode);

        if (!PresetsControlled.ContainsKey(presetName))
            PresetsControlled[presetName] =
                (string.Empty, false, false);
        Volatile.Write(ref _presetsUpdatedTicks, stamp);

        return PresetsControlled[presetName];
    }

    #endregion

    #region Auto-Rotation Configs

    private DateTime? _autoRotationConfigsUpdated;

    private Dictionary<string, (string controllers, int state)>
        AutoRotationConfigsControlled { get; } = new();

    internal (string controllers, int state)? AutoRotationConfigControlled(
        string configName)
    {
        var configOption = Enum.Parse<AutoRotationConfigOption>(configName);

        if (_autoRotationConfigsUpdated != _leasing.AutoRotationConfigsUpdated)
            AutoRotationConfigsControlled.Clear();

        // Return the cached value if it is valid, fastest
        if (_autoRotationConfigsUpdated is not null &&
            _autoRotationConfigsUpdated == _leasing.AutoRotationConfigsUpdated &&
            AutoRotationConfigsControlled.TryGetValue(configName,
                out var configControlled))
        {
            if (string.IsNullOrEmpty(configControlled.controllers))
                return null;
            return AutoRotationConfigsControlled[configName];
        }

        // Bail if the config is not controlled, fast-ish
        if ((AutoRotationConfigsControlled.TryGetValue(configName,
                 out var configNotControlled) &&
             string.IsNullOrEmpty(configNotControlled.controllers)) ||
            _leasing.CheckAutoRotationConfigControlled(configOption) is null)
        {
            if (string.IsNullOrEmpty(configNotControlled.controllers))
            {
                AutoRotationConfigsControlled[configName] = (string.Empty, 0);
                _autoRotationConfigsUpdated = _leasing.AutoRotationConfigsUpdated;
            }

            return null;
        }

        // Re-populate the cache with the current set of controlled configs, slowest
        AutoRotationConfigsControlled.Clear();
        foreach (var controlledConfig in _search.AllAutoRotationConfigsControlled)
        {
            var controllers = string.Join(", ", controlledConfig.Value.Keys);
            var state = controlledConfig.Value.Values.First();
            AutoRotationConfigsControlled[controlledConfig.Key.ToString()] =
                (controllers, state);
        }

        _autoRotationConfigsUpdated = _search.LastCacheUpdateForAutoRotationConfigs;

        return AutoRotationConfigsControlled[configName];
    }

    #endregion

    #endregion

    #region Helper methods for the UI

    #region Style Variables

    private readonly Vector4 _backgroundColor = new(0.68f, 0.77f, 0.80f, 1);
    private readonly Vector4 _hoverColor = new(0.84f, 0.92f, 0.96f, 1);
    private readonly Vector4 _textColor = new(0.05f, 0.05f, 0.05f, 1);
    private readonly Vector2 _padding = new(14f.Scale(), 2f);
    private readonly Vector2 _spacing = new(0, 0);
    private readonly float _rounding = 8f;

    #endregion

    #region Tooltips

    private const string IndicatorTooltip =
        "This option is controlled by another plugin.\n" +
        "Click the X to revoke control.";

    private const string OptionTooltip =
        "This option is controlled by another plugin.\n" +
        "There is a 'Controlled by:' label above this option,\n" +
        "which lists the plugins controlling this option,\n" +
        "and a 'X' button to revoke control.";

    #endregion

    internal int ShowNumberOfLeasees() => _leasing.RegistrationCount;

    internal (string pluginName, int configurationsCount)[] ShowLeasees() =>
        _leasing.SnapshotLeaseeSummaries();

    // Method to display the controlled indicator, which lists the plugins
    private bool ShowIPCControlledIndicator
    (bool? forAutoRotation = null,
        uint? forJob = null,
        CustomComboPreset? forPreset = null,
        string? forAutoRotationConfig = null,
        bool showX = true,
        bool shortDisplay = false)
    {
        (string controllers, object state)? controlled = null;
        var revokeID = "RevokeControl";

        #region Bail if not needed

        if (forAutoRotation is not null)
        {
            if ((controlled = AutoRotationStateControlled()) is null)
                return false;
            revokeID += "ar" + forAutoRotation;
        }

        if (forJob is not null)
        {
            if ((controlled = JobControlled((uint)forJob)) is null)
                return false;
            revokeID += "jb" + forJob;
        }

        if (forPreset is not null)
        {
            var check = PresetControlled((CustomComboPreset)forPreset);
            if (check is null)
                return false;
            controlled = (check.Value.controllers, check.Value.enabled);
            revokeID += "pr" + forPreset;
        }

        if (forAutoRotationConfig is not null)
        {
            if ((controlled =
                    AutoRotationConfigControlled(forAutoRotationConfig)) is null)
                return false;
            revokeID += "ac" + forAutoRotationConfig;
        }

        if (controlled is null)
            return false;

        #endregion

        revokeID += controlled.Value.controllers + controlled.Value.state;

        ImGui.BeginGroup();
        ImGui.PushStyleColor(ImGuiCol.Button, _backgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Text, _textColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, _padding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, _rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, _spacing);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);

        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _backgroundColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, _backgroundColor);
        var buttonText = shortDisplay
            ? "Controlled"
            : "Controlled by: " + controlled.Value.controllers;
        ImGui.SmallButton(buttonText);
        ImGui.PopStyleColor(2);

        if (showX)
        {
            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, _hoverColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, _hoverColor);
            ImGui.PushStyleColor(ImGuiCol.Text, _textColor);
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 12f.Scale());
            if (ImGui.SmallButton("X###" + revokeID))
                RevokeControl(controlled.Value.controllers);
            ImGui.PopStyleColor(3);

            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(2);
            ImGui.EndGroup();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(IndicatorTooltip);
        }
        else
        {
            ImGui.PopStyleVar(4);
            ImGui.PopStyleColor(2);
            ImGui.EndGroup();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    IndicatorTooltip.Split("X")[0]
                    + "Job to revoke control inside.");
        }

        return true;
    }

    // Method to display a differently-styled and disabled checkbox if controlled
    // ReSharper disable ConstantConditionalAccessQualifier
    private bool ShowIPCControlledCheckbox
    (string label, ref bool backupVar,
        bool? forAutoRotation = null,
        uint? forJob = null,
        CustomComboPreset? forPreset = null,
        bool presetShowState = true,
        string? forAutoRotationConfig = null)
    {
        bool DefaultUI(string label, ref bool backupVar)
        {
            return ImGui.Checkbox(label, ref backupVar);
        }

        (string controllers, bool state)? controlled = null;

        #region Bail if not needed

        try
        {
            if (forAutoRotation is not null)
                controlled = AutoRotationStateControlled();
            if (forJob is not null)
                controlled = JobControlled((uint)forJob);
            if (forPreset is not null)
            {
                var check =
                    PresetControlled((CustomComboPreset)forPreset);
                if (check is null)
                    controlled = null;
                else
                {
                    controlled = presetShowState
                        ? (check?.controllers ?? "", check?.enabled ?? false)
                        : (check?.controllers ?? "", check?.autoMode ?? false);
                }
            }

            if (forAutoRotationConfig is not null)
            {
                var check = AutoRotationConfigControlled(forAutoRotationConfig);
                if (check is null)
                    controlled = null;
                else
                    controlled = (check.Value.controllers, check.Value.state == 1);
            }

            if (controlled is null)
            {
                return DefaultUI(label, ref backupVar);
            }
        }
        catch (Exception e)
        {
            Logging.Error("Error in UIHelper.\n" + e);
            return DefaultUI(label, ref backupVar);
        }

        #endregion

        ImGui.BeginGroup();
        ImGui.PushStyleColor(ImGuiCol.FrameBg, _backgroundColor);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, _textColor);

        ImGui.BeginDisabled();
        var hold = false;
        var _ = controlled.Value.state;
        if (forPreset is null)
        {
            ImGui.Checkbox("", ref _);
        }
        else if (label.StartsWith('#'))
            hold = ImGui.Checkbox(label, ref _);
        else
            hold = ImGui.Checkbox("", ref _);

        ImGui.EndDisabled();
        ImGui.SameLine();

        if (forPreset is null)
            ImGui.TextColoredWrapped(ImGuiColors.DalamudGrey, label);
        else
            ImGui.TextColoredWrapped(ImGuiColors.DalamudGrey,
                label.Contains("Auto") ? "" : label.Split('#')[0]);

        ImGui.PopStyleColor(2);
        ImGui.EndGroup();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(OptionTooltip);

        return hold;
    }

    private bool ShowIPCControlledSlider
        (string label, ref int backupVar, string? forAutoRotationConfig = null)
    {
        bool DefaultUI(string label, ref int backupVar)
        {
            ImGuiEx.SetNextItemWidthScaled(200);
            return ImGuiEx.SliderInt(
                label, ref backupVar, 1, 99, "%d%%");
        }

        (string controllers, int state)? controlled = null;

        #region Bail if not needed

        try
        {
            if (forAutoRotationConfig is not null)
                controlled = AutoRotationConfigControlled(forAutoRotationConfig);

            if (controlled is null)
            {
                ImGuiEx.SetNextItemWidthScaled(200);
                return DefaultUI(label, ref backupVar);
            }
        }
        catch (Exception e)
        {
            Logging.Error("Error in UIHelper.\n" + e.Message);
            return DefaultUI(label, ref backupVar);
        }

        #endregion

        ImGui.BeginGroup();
        ImGui.PushStyleColor(ImGuiCol.FrameBg, _backgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Text, _textColor);
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, _backgroundColor);

        var _ = controlled.Value.state;
        ImGui.BeginDisabled();
        ImGuiEx.SetNextItemWidthScaled(200);
        ImGuiEx.SliderInt("", ref _, 1, 99, "%d%%");
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.TextColoredWrapped(ImGuiColors.DalamudGrey, label);

        ImGui.PopStyleColor(3);
        ImGui.EndGroup();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(OptionTooltip);

        return false;
    }

    private bool ShowIPCControlledCombo
    (string label, bool useDPSVar,
        ref DPSRotationMode dpsVar, ref HealerRotationMode healVar,
        string? forAutoRotationConfig = null)
    {
        bool DefaultUI
        (string label, bool useDPSVar,
            ref DPSRotationMode dpsVar,
            ref HealerRotationMode healVar)
        {
            return useDPSVar
                ? ImGuiEx.EnumCombo(label, ref dpsVar)
                : ImGuiEx.EnumCombo(label, ref healVar);
        }

        (string controller, int state)? controlled = null;

        #region Bail if not needed

        try
        {
            if (forAutoRotationConfig is not null)
                controlled = AutoRotationConfigControlled(forAutoRotationConfig);

            if (controlled is null)
            {
                return DefaultUI(label, useDPSVar, ref dpsVar, ref healVar);
            }
        }
        catch (Exception e)
        {
            Logging.Error("Error in UIHelper.\n" + e.Message);
            return DefaultUI(label, useDPSVar, ref dpsVar, ref healVar);
        }

        #endregion

        ImGui.PushStyleColor(ImGuiCol.FrameBg, _backgroundColor);
        ImGui.PushStyleColor(ImGuiCol.Text, _textColor);

        var option = Enum.Parse<AutoRotationConfigOption>(forAutoRotationConfig!);
        var valueType = option.GetType()
            .GetField(option.ToString())
            .GetCustomAttributes(typeof(ConfigValueTypeAttribute), false)
            .Cast<ConfigValueTypeAttribute>()
            .First().ValueType;
        var value =
            Enum.Parse(valueType, controlled.Value.state.ToString());
        string valueString = value.ToString()!.Replace("_", " ");
        string[] values = [valueString];

        var _ = 0;
        ImGui.BeginDisabled();
        ImGui.Combo("", ref _, values, values.Length);
        ImGui.EndDisabled();

        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(OptionTooltip);

        return false;
    }

    /// <summary>
    ///     Button click method for Indicator to cancel plugin control.
    /// </summary>
    /// <param name="controllers">The displayed list of plugins to revoke.</param>
    private void RevokeControl(string controllers)
    {
        var controllerNames = controllers.Split(", ");

        // 🔴 先在鎖內把 ID 抄出來，再在鎖外一把一把收：RemoveRegistration() 會
        //    回呼承租外掛的碼，握著鎖跑它會死結。
        // ⚠️ 抄出來到真的收之間，租約可能已經不在了（承租端自己 ReleaseControl()、
        //    或它的外掛被停用）⇒ RemoveRegistration() 內的索引會擲
        //    KeyNotFoundException。這裡跑在繪製執行緒上，讓它冒出去會被 Dalamud
        //    當成 Draw() 失敗（10 秒內兩次就把視窗永久關掉），所以逐把攔。
        var leases = _leasing.FindLeasesByPluginNames(controllerNames);
        foreach (var lease in leases)
            try
            {
                _leasing.RemoveRegistration(
                    lease, CancellationReason.WrathUserManuallyCancelled);
            }
            catch (Exception e)
            {
                Logging.Error(
                    "Failed to revoke a lease the user asked to revoke; " +
                    "it may already have been released.\n" + e);
            }
    }

    #region Actual UI Method overloads

    #region Indicator

    public bool ShowIPCControlledIndicatorIfNeeded() =>
        ShowIPCControlledIndicator(forAutoRotation: true);

    public bool ShowIPCControlledIndicatorIfNeeded
        (uint job, bool showX = true, bool shortDisplay = false) =>
        ShowIPCControlledIndicator
            (forJob: job, showX: showX, shortDisplay: shortDisplay);

    public bool ShowIPCControlledIndicatorIfNeeded(CustomComboPreset preset) =>
        ShowIPCControlledIndicator(forPreset: preset);

    public bool ShowIPCControlledIndicatorIfNeeded(string configName) =>
        ShowIPCControlledIndicator(forAutoRotationConfig: configName);

    #endregion

    #region Disabled Inputs

    public bool ShowIPCControlledCheckboxIfNeeded
        (string label, ref bool backupVar) =>
        ShowIPCControlledCheckbox(label, ref backupVar, forAutoRotation: true);

    public bool ShowIPCControlledCheckboxIfNeeded
        (string label, ref bool backupVar, uint job) =>
        ShowIPCControlledCheckbox(label, ref backupVar, forJob: job);

    public bool ShowIPCControlledCheckboxIfNeeded
    (string label, ref bool backupVar, CustomComboPreset preset,
        bool showState) =>
        ShowIPCControlledCheckbox
            (label, ref backupVar, forPreset: preset, presetShowState: showState);

    public bool ShowIPCControlledCheckboxIfNeeded
        (string label, ref bool backupVar, string configName) =>
        ShowIPCControlledCheckbox(label, ref backupVar,
            forAutoRotationConfig: configName);

    public bool ShowIPCControlledSliderIfNeeded
        (string label, ref int backupVar, string configName) =>
        ShowIPCControlledSlider(
            label, ref backupVar, forAutoRotationConfig: configName);

    public bool ShowIPCControlledComboIfNeeded
    (string label, bool useDPSVar,
        ref DPSRotationMode dpsVar, ref HealerRotationMode healVar,
        string? configName = null) =>
        ShowIPCControlledCombo(
            label, useDPSVar, ref dpsVar, ref healVar,
            forAutoRotationConfig: configName);

    #endregion

    #endregion

    #endregion
}
