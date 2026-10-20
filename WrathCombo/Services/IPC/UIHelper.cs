#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using WrathCombo.AutoRotation;
using WrathCombo.Combos;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Extensions;
// ReSharper disable VariableHidesOuterVariable

#endregion

namespace WrathCombo.Services.IPC;

public class UIHelper(Leasing leasing)
{
    private readonly Leasing _leasing = leasing;
    private readonly Search _search = P.IPCSearch;

    #region Checks for the UI

    #region Auto-Rotation

    /// <summary>
    ///     <see cref="AutoRotationControlled" /> 這份快取上次重建時，<c>Leasing</c>
    ///     五個時間戳的值；<see langword="null" />＝還沒建過。
    /// </summary>
    /// <remarks>
    ///     沒有任何 <c>[EzIPC]</c> 端點走得到這一支：<c>Provider.GetAutoRotationState</c>
    ///     直接打 <c>Leasing.CheckAutoRotationControlled()</c>，那一支自己有鎖。<br />
    ///     另外四個時間戳：改其他設定只動 <c>LastUpdated</c>，而它決定多個控制者的排序與採用誰的開關值。
    /// </remarks>
    private (DateTime? State, DateTime? Configs, DateTime? Jobs, DateTime? Combos, DateTime? Options)?
        _autoRotationUpdated;

    /// <summary> <see langword="null" />＝沒人控制；這也要快取，否則被控制過一次之後每幀都重查。 </summary>
    private (string controllers, bool state)? AutoRotationControlled { get; set; }

    internal (string controllers, bool state)? AutoRotationStateControlled()
    {
        // 只讀一次時間戳：判快取有效與寫回快取各讀一次的話，中間承租外掛的執行緒改掉時，
        // 會用「新的時間戳」把「舊的快照」存起來，那份快取之後永遠不會再失效。同 PresetControlled 的處理。
        var leasingStamp = (_leasing.AutoRotationStateUpdated,
            _leasing.AutoRotationConfigsUpdated, _leasing.JobsUpdated,
            _leasing.CombosUpdated, _leasing.OptionsUpdated);

        // Return the cached value if it is valid, fastest
        if (_autoRotationUpdated == leasingStamp)
            return AutoRotationControlled;

        // Bail if the state is not controlled, fast
        var controlled = _leasing.CheckAutoRotationControlled();

        // Re-populate the cache with the current state, slowest
        //
        // 🔴 這裡跑在繪製執行緒上，而 IPC 端點在別條執行緒上改同一批字典
        //    ⇒ 走訪一定要在 Leasing 的鎖內做，拿回來的是值不是活字典。
        var snapshot = controlled is null
            ? null
            : _leasing.SnapshotAutoRotationControllers();

        AutoRotationControlled = snapshot is null
            ? null
            : (string.Join(", ", snapshot.Value.Controllers), snapshot.Value.State);
        _autoRotationUpdated = leasingStamp;

        return AutoRotationControlled;
    }

    #endregion

    #region Jobs

    /// <summary> 上次重建時的 <c>Leasing.StampsGeneration + 1</c>；<c>0</c>＝還沒建過。 </summary>
    /// <remarks>
    ///     形狀與理由見 <see cref="_presetsGeneration" />。
    /// </remarks>
    private long _jobsGeneration;

    private Dictionary<string, (string controllers, bool state)>
        JobsControlled { get; } = new();

    internal (string controllers, bool state)? JobControlled(uint job)
    {
        var jobName = CustomComboFunctions.JobIDs.JobIDToShorthand(job);

        // 🔴 世代只讀一次（承租外掛的執行緒會寫它）：讀兩次會用新的世代存下舊的快照、永不失效。
        //    鍵是全部時間戳的世代而不只 JobsUpdated：改別類設定也會讓多個控制者換排序。
        var stamp = _leasing.StampsGeneration + 1;
        var cachedStamp = Volatile.Read(ref _jobsGeneration);

        if (cachedStamp != stamp)
            JobsControlled.Clear();

        // Return the cached value if it is valid, fastest
        if (cachedStamp != 0 &&
            cachedStamp == stamp &&
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
            // 沒有玩家（讀圖畫面）時 CheckJobControlled 一律回 null，那不代表沒人控制：不寫進快取，
            // 否則玩家回來後要等到下一次租約變動才會再顯示被控制。
            if (string.IsNullOrEmpty(jobNotControlled.controllers) &&
                CustomComboFunctions.LocalPlayer is not null)
            {
                JobsControlled[jobName] = (string.Empty, false);
                Volatile.Write(ref _jobsGeneration, stamp);
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
        Volatile.Write(ref _jobsGeneration, stamp);

        return JobsControlled[jobName];
    }

    #endregion

    #region Presets

    /// <summary> 上次重建時的 <c>Leasing.StampsGeneration + 1</c>；<c>0</c>＝還沒建過。 </summary>
    /// <remarks>
    ///     用 <c>long</c> 而不是 <c>DateTime?</c>（<c>bool</c> ＋ <c>DateTime</c> 共 16 bytes，<b>寫入不是原子的</b>）：
    ///     <see cref="PresetControlled" /> 被兩種執行緒同時呼叫：繪製執行緒（設定視窗每一格 preset）與<b>承租外掛自己的執行緒</b>
    ///     （<c>[EzIPC]</c> 端點 → <c>Search.PresetStates</c> → <c>CustomComboFunctions.IsEnabled</c> → 這一支）。
    ///     撕裂讀會得到「有值旗標是新的、時間值還是舊的」之類的組合，讓快取有效性判斷得到錯的答案。
    /// </remarks>
    private long _presetsGeneration;

    private ConcurrentDictionary<string, (string controllers, bool enabled, bool autoMode)>
        PresetsControlled { get; } = new();

    internal (string controllers, bool enabled, bool autoMode)?
        PresetControlled(CustomComboPreset preset)
    {
        // 每個連段入口與每個選項判斷都會走到這裡（IsEnabled），所以不用會裝箱的 ToString()。
        var presetName = preset.EnumName();

        // 🔴 理由與鍵的範圍同 JobControlled；排第一的那把決定 IsEnabled 看到的開關。
        var stamp = _leasing.StampsGeneration + 1;
        var cachedStamp = Volatile.Read(ref _presetsGeneration);

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
            (_leasing.CheckComboControlled(preset) is null &&
             _leasing.CheckComboOptionControlled(preset) is null))
        {
            if (string.IsNullOrEmpty(presetNotControlled.controllers))
            {
                PresetsControlled[presetName] =
                    (string.Empty, false, false);
                Volatile.Write(ref _presetsGeneration, stamp);
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
        Volatile.Write(ref _presetsGeneration, stamp);

        return PresetsControlled[presetName];
    }

    #endregion

    #region Auto-Rotation Configs

    /// <summary> 上次重建時的 <c>Leasing.StampsGeneration + 1</c>；<c>0</c>＝還沒建過。 </summary>
    /// <remarks>
    ///     形狀與理由見 <see cref="_presetsGeneration" />。
    /// </remarks>
    private long _autoRotationConfigsGeneration;

    private Dictionary<string, (string controllers, int state)>
        AutoRotationConfigsControlled { get; } = new();

    internal (string controllers, int state)? AutoRotationConfigControlled(
        string configName)
    {
        var configOption = Enum.Parse<AutoRotationConfigOption>(configName);

        // 🔴 理由與鍵的範圍同 JobControlled；排第一的那把決定自動循環採用的設定值。
        var stamp = _leasing.StampsGeneration + 1;
        var cachedStamp = Volatile.Read(ref _autoRotationConfigsGeneration);

        if (cachedStamp != stamp)
            AutoRotationConfigsControlled.Clear();

        // Return the cached value if it is valid, fastest
        if (cachedStamp != 0 &&
            cachedStamp == stamp &&
            AutoRotationConfigsControlled.TryGetValue(configName,
                out var configControlled))
        {
            if (string.IsNullOrEmpty(configControlled.controllers))
                return null;
            return configControlled;
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
                Volatile.Write(ref _autoRotationConfigsGeneration, stamp);
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

        Volatile.Write(ref _autoRotationConfigsGeneration, stamp);

        // 上面 CheckAutoRotationConfigControlled() 說「有人控制」，但它回來之後 Leasing 的鎖就放開了：租約可能剛好在這個空檔被收走
        // （承租端自己 ReleaseControl()、或它的外掛被停用），重建出來的表就不含 configName，直接索引會擲 KeyNotFoundException；
        // 呼叫點裡 ShowIPCControlledIndicator 那一條沒有 try/catch，例外會冒進 Dalamud 的 Draw() 失敗處理。
        // 照本方法自己的慣例（controllers 為空就是「沒人控制」）回 null，並把這個結論也寫進快取。
        if (!AutoRotationConfigsControlled.TryGetValue(
                configName, out var rebuilt))
        {
            AutoRotationConfigsControlled[configName] = (string.Empty, 0);
            return null;
        }

        if (string.IsNullOrEmpty(rebuilt.controllers))
            return null;

        return rebuilt;
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

            // 職業列被停用時也要看得到；不帶旗標判不到＝停用中，只在這時把說明框推回全亮。
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                using var tooltipAlpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 1f, !ImGui.IsItemHovered());
                ImGui.SetTooltip(
                    IndicatorTooltip.Split("X")[0]
                    + "Job to revoke control inside.");
            }
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
