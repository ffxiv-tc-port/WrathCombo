#region

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using WrathCombo.Attributes;
using WrathCombo.Combos;
using WrathCombo.Core;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Extensions;
using WrathCombo.Window.Tabs;
using TS = System.TimeSpan;

#endregion

namespace WrathCombo.Services.IPC;

public class Search(Leasing leasing)
{
    /// <summary>
    ///     A shortcut for <see cref="StringComparison.CurrentCultureIgnoreCase" />.
    /// </summary>
    private const StringComparison ToLower =
        StringComparison.CurrentCultureIgnoreCase;

    private readonly Leasing _leasing = leasing;

    /// <summary>
    ///     這個 Search 自己的節流器（自帶鎖、自帶字典）。
    /// </summary>
    /// <remarks>
    ///     🔴 <b>不要換回 <c>ECommons</c> 的 <c>EzThrottler</c></b>：
    ///     理由與注意事項見 <see cref="IpcThrottle" />。
    /// </remarks>
    private readonly IpcThrottle _ipcThrottle = new();

    #region Aggregations of Leasing Configurations

    /// <summary> 上次重建時的 <see cref="Leasing.StampsGeneration" />；表內依 LastUpdated 排序。 </summary>
    private long _autoRotationConfigsCacheGeneration;

    /// <summary>
    ///     Lists all auto-rotation configurations controlled under leases.
    /// </summary>
    [field: AllowNull, MaybeNull]
    internal Dictionary<AutoRotationConfigOption, Dictionary<string, int>>
        AllAutoRotationConfigsControlled
    {
        get
        {
            // 🔴 先把欄位抄進區域變數：判斷用哪一份、回傳就是哪一份。
            //    這幾個快取都是「在區域變數組好整份、最後才一次指派回欄位」，
            //    讀取端只要不重複讀欄位，就永遠看得到一份完整的快照。
            //    完整說明見 <see cref="PresetStates" />。
            var cached = field;

            // 🔴 世代只讀一次（承租外掛的執行緒會寫它）：讀兩次會用新的世代存下舊的快照，
            //    那份快取之後永遠不會再失效。
            var leasingStamp = _leasing.StampsGeneration;

            if (cached is not null &&
                leasingStamp == _autoRotationConfigsCacheGeneration)
                return cached;

            // 🔴 走訪租約與它身上的 ...Controlled 字典必須在 Leasing 的鎖內做
            //    （ProjectLeases 負責），不然 IPC 端點在別條執行緒上 Add 時，
            //    這裡的 LINQ 會擲 InvalidOperationException。
            //    選出來的是<b>值</b>（匿名型別的四個欄位），沒有把字典漏到鎖外。
            var rebuilt = _leasing.ProjectLeases(registration => registration
                    .AutoRotationConfigsControlled
                    .Select(pair => new
                    {
                        pair.Key,
                        registration.PluginName,
                        pair.Value,
                        registration.LastUpdated,
                    }))
                .GroupBy(x => x.Key)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.LastUpdated)
                        .ToDictionary(x => x.PluginName, x => x.Value)
                );
            field = rebuilt;

            _autoRotationConfigsCacheGeneration = leasingStamp;
            return rebuilt;
        }
    }

    /// <summary> 同 <see cref="_autoRotationConfigsCacheGeneration" />。 </summary>
    private long _allJobsControlledCacheGeneration;

    /// <summary>
    ///     Lists all jobs controlled under leases.
    /// </summary>
    [field: AllowNull, MaybeNull]
    internal Dictionary<Job, Dictionary<string, bool>> AllJobsControlled
    {
        get
        {
            var cached = field;

            // 🔴 跨執行緒的那一邊只讀一次，理由同
            //    AllAutoRotationConfigsControlled。
            var leasingStamp = _leasing.StampsGeneration;

            if (cached is not null &&
                leasingStamp == _allJobsControlledCacheGeneration)
                return cached;

            var rebuilt = _leasing.ProjectLeases(registration => registration.JobsControlled
                    .Select(pair => new
                    {
                        pair.Key,
                        registration.PluginName,
                        pair.Value,
                        registration.LastUpdated,
                    }))
                .GroupBy(x => x.Key)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.LastUpdated)
                        .ToDictionary(x => x.PluginName, x => x.Value)
                );
            field = rebuilt;

            _allJobsControlledCacheGeneration = leasingStamp;
            return rebuilt;
        }
    }

    /// <summary>
    ///     同 <see cref="_autoRotationConfigsCacheGeneration" />，但讀寫兩端都跨執行緒。
    /// </summary>
    /// <remarks>
    ///     讀寫兩端都跨執行緒、整段沒有鎖，所以用 <c>long</c>（<c>DateTime?</c> 是 16 bytes，<b>寫入不是原子的</b>）：
    ///     <see cref="AllPresetsControlled" /> 的唯一呼叫點 <c>UIHelper.PresetControlled</c> 是<b>承租外掛自己的執行緒到得了</b>的
    ///     （<c>[EzIPC]</c> 端點 → <see cref="PresetStates" /> → <c>CustomComboFunctions.IsEnabled</c> → 它），繪製執行緒每一格 preset 也會問一次。
    ///     不加鎖是刻意的：快取重建會走訪租約，鎖在 <c>Leasing.ProjectLeases</c> 裡面。
    /// </remarks>
    private long _allPresetsControlledCacheGeneration;

    /// <summary>
    ///     Lists all presets controlled under leases.<br />
    ///     Include both combos and options, but also jobs' options.
    /// </summary>
    [field: AllowNull, MaybeNull]
    internal Dictionary<CustomComboPreset,
            Dictionary<string, (bool enabled, bool autoMode)>>
        AllPresetsControlled
    {
        get
        {
            // 🔴 世代只讀一次，理由同 AllAutoRotationConfigsControlled。
            var generation = _leasing.StampsGeneration;

            var cached = field;
            var cachedGeneration =
                Volatile.Read(ref _allPresetsControlledCacheGeneration);

            if (cached is not null &&
                generation == cachedGeneration)
                return cached;

            var rebuilt = _leasing.ProjectLeases(registration => registration.CombosControlled
                    .Select(pair => new
                    {
                        pair.Key,
                        registration.PluginName,
                        pair.Value.enabled,
                        pair.Value.autoMode,
                        registration.LastUpdated,
                    }))
                .GroupBy(x => x.Key)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.LastUpdated)
                        .ToDictionary(x => x.PluginName,
                            x => (x.enabled, x.autoMode))
                )
                .Concat(
                    _leasing.ProjectLeases(registration => registration.OptionsControlled
                            .Select(pair => new
                            {
                                pair.Key,
                                registration.PluginName,
                                pair.Value,
                                registration.LastUpdated,
                            }))
                        .GroupBy(x => x.Key)
                        .ToDictionary(
                            g => g.Key,
                            g => g.OrderByDescending(x => x.LastUpdated)
                                .ToDictionary(x => x.PluginName,
                                    x => (x.Value, false))
                        )
                )
                .DistinctBy(x => x.Key)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            field = rebuilt;

            Volatile.Write(ref _allPresetsControlledCacheGeneration, generation);
            return rebuilt;
        }
    }

    #endregion

    #region Presets Information

    #region Cached Preset Info

    /// <summary>
    ///     The path to the configuration file for Wrath Combo.
    /// </summary>
    /// <remarks> 插件載入期間路徑不會變，算一次就留著（自動循環每一輪都會讀）；算的時候擲例外就不留。 </remarks>
    [field: AllowNull, MaybeNull]
    internal string ConfigFilePath => field ??= BuildConfigFilePath();

    private static string BuildConfigFilePath()
    {
        var pluginConfig = Svc.PluginInterface.GetPluginConfigDirectory();
        if (Path.EndsInDirectorySeparator(pluginConfig))
            pluginConfig = Path.TrimEndingDirectorySeparator(pluginConfig);
        pluginConfig =
            pluginConfig
                [..pluginConfig.LastIndexOf(Path.DirectorySeparatorChar)];
        pluginConfig = Path.Combine(pluginConfig, "WrathCombo.json");
        return pluginConfig;
    }

    /// <summary> <see cref="PresetStates" /> 的快取：內容、鍵（<see cref="Leasing.StampsGeneration" />）與建立時間一次指派。 </summary>
    private PresetStatesSnapshot? _presetStatesCache;

    private sealed record PresetStatesSnapshot(
        Dictionary<string, Dictionary<ComboStateKeys, bool>> Value,
        long Generation,
        DateTime BuiltAt);

    /// <summary>
    ///     Recursively finds the root parent of a given CustomComboPreset.
    /// </summary>
    /// <param name="preset">The CustomComboPreset to find the root parent for.</param>
    /// <returns>The root parent CustomComboPreset.</returns>
    public CustomComboPreset GetRootParent(CustomComboPreset preset)
    {
        if (!Attribute.IsDefined(
                typeof(CustomComboPreset).GetField(preset.ToString())!,
                typeof(ParentComboAttribute)))
        {
            return preset;
        }

        var parentAttribute = (ParentComboAttribute)Attribute.GetCustomAttribute(
            typeof(CustomComboPreset).GetField(preset.ToString())!,
            typeof(ParentComboAttribute)
        )!;

        return GetRootParent(parentAttribute.ParentPreset);
    }

    /// <summary>
    ///     Cached list of <see cref="CustomComboPreset">Presets</see>, and most of
    ///     their attribute-based information.
    /// </summary>
    [field: AllowNull, MaybeNull]
    // ReSharper disable once MemberCanBePrivate.Global
    internal Dictionary<string, (Job Job, CustomComboPreset ID,
        CustomComboInfoAttribute Info, bool HasParentCombo, bool IsVariant, string
        ParentComboName, ComboType ComboType)> Presets
    {
        get
        {
            // C# 的 a ??= b 等價於 a ?? (a = b)：欄位只讀一次，回傳的就是那一次讀到（或剛建好）的參考，本來就是快照式的。
            // 兩條執行緒同時初始化最多是白做一次工，讀取端拿到的一定是完整的一份。
            return field ??= PresetStorage.AllPresets!
                .Select(preset => new
                {
                    ID = preset,
                    JobId = (Job)preset.Attributes().CustomComboInfo.JobID,
                    InternalName = preset.ToString(),
                    Info = preset.Attributes().CustomComboInfo!,
                    HasParentCombo = preset.Attributes().Parent != null,
                    IsVariant = preset.Attributes().Variant != null,
                    ParentComboName = preset.Attributes().Parent != null
                        ? GetRootParent(preset).ToString()
                        : string.Empty,
                    preset.Attributes().ComboType,
                })
                .Where(combo =>
                    !combo.InternalName.EndsWith("any", ToLower))
                .ToDictionary(
                    combo => combo.InternalName,
                    combo => (combo.JobId, combo.ID, combo.Info,
                        combo.HasParentCombo, combo.IsVariant,
                        combo.ParentComboName, combo.ComboType)
                );
        }
    }

    /// <summary>
    ///     Cached list of <see cref="CustomComboPreset">Presets</see>, and the
    ///     state and Auto-Mode state of each.
    /// </summary>
    /// <remarks>
    ///     Rebuilt if the <see cref="ConfigFilePath">Config File</see> has been
    ///     updated since
    ///     <see cref="_presetStatesCache">last cached</see>, or a lease changed anything.
    /// </remarks>
    // ReSharper disable once MemberCanBePrivate.Global
    internal Dictionary<string, Dictionary<ComboStateKeys, bool>> PresetStates
    {
        get
        {
            // 🔴 世代只讀一次，理由同 AllAutoRotationConfigsControlled。
            var generation = _leasing.StampsGeneration;

            // 這一支是 [EzIPC] 端點（Provider.GetComboState／GetComboOptionState，承租外掛的執行緒）與繪製執行緒同時進得來的，快取的讀寫紀律：
            // 寫：在區域變數把整份新的組好，最後才一次指派回欄位（參考指派是原子的，讀取端看不到組到一半的字典）。
            // 讀：先把欄位抄進區域變數，判斷與回傳都用同一份，中間不會被別條執行緒的整份替換插進來。
            // 刻意不加鎖：快取有效性的判斷含 File.GetLastWriteTime，檔案 I/O 不可以放進鎖裡。
            var cached = Volatile.Read(ref _presetStatesCache);

            if (!Debug.DebugConfig)
            {
                if (cached != null &&
                    File.GetLastWriteTime(ConfigFilePath) <= cached.BuiltAt &&
                    cached.Generation == generation)
                    return cached.Value;
            }
            else
            {
                if (cached != null &&
                    !_ipcThrottle.Throttle("ipcPresetStateCheck", TS.FromSeconds(1)) &&
                    cached.Generation == generation)
                    return cached.Value;
            }

            var rebuilt = Presets
                .ToDictionary(
                    preset => preset.Key,
                    preset =>
                    {
                        var isEnabled =
                            CustomComboFunctions.IsEnabled(preset.Value.ID);
                        var ipcAutoMode = _leasing.CheckComboControlled(
                            preset.Value.ID.ToString())?.autoMode ?? false;
                        var isAutoMode =
                            Service.Configuration.AutoActions.TryGetValue(
                                preset.Value.ID, out var autoMode) &&
                            autoMode && preset.Value.ID.Attributes().AutoAction !=
                            null;
                        return new Dictionary<ComboStateKeys, bool>
                        {
                            { ComboStateKeys.Enabled, isEnabled },
                            { ComboStateKeys.AutoMode, isAutoMode || ipcAutoMode },
                        };
                    }
                );

            // 🔴 先發佈快取再刷新：UpdateActiveJobPresets() 經 GetJobAutorots → AutoActions
            //    遞迴進這一支，那一層要命中快取而不是無限重建。
            Volatile.Write(ref _presetStatesCache,
                new PresetStatesSnapshot(rebuilt, generation, DateTime.Now));
            UpdateActiveJobPresets();
            return rebuilt;
        }
    }

    internal void UpdateActiveJobPresets()
    {
        // GetJobAutorots 讀的是原生狀態（Player.JobId／Player.Job 走 Svc.Objects.LocalPlayer 的共用包裝，InPvP() 走 GameMain 的原生靜態），
        // 而這一支從 [EzIPC] 端點也到得了（Provider.GetComboState／GetComboOptionState → PresetStates 重建的尾端）＝承租外掛的執行緒，
        // 所以丟回 framework 執行緒上做。
        // 刻意不同步等：ActiveJobPresets 只是 DTR 提示用的計數器，沒有任何端點的回傳值依賴它，晚一幀更新不改變任何人看到的答案；
        // 同步等會把承租外掛的執行緒無謂地停在這裡。

        // 卸載期旁路：RunOnFrameworkThread 在 IsFrameworkUnloading 為真時是「就地在呼叫端執行緒執行」而不是排隊，
        // 關遊戲那一瞬間承租外掛打進 GetComboState／GetComboOptionState 的話，RefreshActiveJobPresets 會在承租外掛的執行緒上
        // 讀 Player.JobId 等原生狀態；失敗形式是 AccessViolationException，try/catch 攔不到。
        if (Svc.Framework.IsFrameworkUnloading &&
            !Svc.Framework.IsInFrameworkUpdateThread)
        {
            if (_ipcThrottle.Throttle("ipcFrameworkUnloadingPresetRefresh",
                    TS.FromSeconds(30)))
                Svc.Log.Information(
                    "The framework is unloading, so the active job preset " +
                    "count was left at its previous value instead of being " +
                    "refreshed from a non-framework thread.");
            return;
        }

        Task task;
        try
        {
            task = Svc.Framework.RunOnFrameworkThread(RefreshActiveJobPresets);
        }
        catch (Exception e)
        {
            Svc.Log.Error("Failed to refresh the active job presets: " + e);
            return;
        }

        if (task.IsCompleted)
        {
            if (task.IsFaulted)
                Svc.Log.Error(
                    "Failed to refresh the active job presets: " + task.Exception);
            return;
        }

        // 🔴 不要把這個 Task 丟著不管：它擲的例外會變成沒人觀察的
        //    UnobservedTaskException。
        task.ContinueWith(
            t => Svc.Log.Error(
                "Failed to refresh the active job presets: " + t.Exception),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    ///     <see cref="UpdateActiveJobPresets" /> 真正在 framework 執行緒上跑的那一段。
    /// </summary>
    private void RefreshActiveJobPresets()
    {
        // 從承租外掛的執行緒排進來的那一次可能在外掛卸載後才跑，那時 P 已是 null。
        if (UnloadToken.IsCancellationRequested)
            return;
        ActiveJobPresets = Window.Functions.Presets.GetJobAutorots.Count;
    }

    internal int ActiveJobPresets;

    #endregion

    #region Combo Information

    /// <summary>
    ///     The names of each combo.
    /// </summary>
    /// <value>
    ///     Job -> <c>list</c> of combo internal names.
    /// </value>
    internal Dictionary<Job, List<string>> ComboNamesByJob =>
        Presets
            .Where(preset =>
                preset.Value is { IsVariant: false, HasParentCombo: false } &&
                !preset.Key.Contains("pvp", ToLower))
            .GroupBy(preset => preset.Value.Job)
            .ToDictionary(
                g => g.Key,
                g => g.Select(preset => preset.Key).ToList()
            );

    /// <summary>
    ///     The states of each combo.
    /// </summary>
    /// <value>
    ///     Job -> Internal Name ->
    ///     <see cref="ComboStateKeys">State Key</see> -><br />
    ///     <c>bool</c> - Whether the state is enabled or not.
    /// </value>
    internal Dictionary<Job,
            Dictionary<string, Dictionary<ComboStateKeys, bool>>>
        ComboStatesByJob
    {
        get
        {
            // 🔴 PresetStates 是「整份替換式」的快取，而下面每一個 combo 都要查它
            //    一次。先抄進區域變數，整趟投影就綁在同一份完整快照上，不會查到
            //    一半換成新的那一份。
            // 整趟也只做一次快取有效性檢查（非除錯模式下含一次 File.GetLastWriteTime）。
            var presetStates = PresetStates;

            return ComboNamesByJob
                .ToDictionary(
                    job => job.Key,
                    job => job.Value
                        .ToDictionary(
                            combo => combo,
                            combo => presetStates[combo]
                        )
                );
        }
    }

    /// <summary>
    ///     The states of each combo, but heavily categorized.
    /// </summary>
    /// <value>
    ///     Job -> <see cref="ComboTargetTypeKeys">Target Key</see> ->
    ///     <see cref="ComboSimplicityLevelKeys">Simplicity Key</see> ->
    ///     Internal Name ->
    ///     <see cref="ComboStateKeys">State Key</see> -><br />
    ///     <c>bool</c> - Whether the state is enabled or not.
    /// </value>
    [field: AllowNull, MaybeNull]
    internal Dictionary<Job,
            Dictionary<ComboTargetTypeKeys,
                Dictionary<ComboSimplicityLevelKeys,
                    Dictionary<string, Dictionary<ComboStateKeys, bool>>>>>
        CurrentJobComboStatesCategorized
    {
        get
        {
            var job = (Job)CustomComboFunctions.JobIDs.ClassToJob(JobID!.Value);

            var cached = field;

            if (cached != null && cached.ContainsKey(job))
                return cached;

            // ComboStatesByJob 沒有快取、每次存取都整份重算（而它裡面每個 combo 又要查一次 PresetStates）。
            // 先抄進區域變數：整趟綁在同一份上，也只重算一次；在下面的投影裡逐個 combo 存取會重算 N 次，每次還可能看到不同的快照。
            var comboStatesByJob = ComboStatesByJob;

            var rebuilt = Presets
                .Where(preset =>
                    preset.Value is
                        { IsVariant: false, HasParentCombo: false } &&
                    preset.Value.Job == job &&
                    !preset.Key.Contains("pvp", ToLower))
                .SelectMany(preset => new[]
                {
                    new
                    {
                        Job = (Job)preset.Value.Info.JobID,
                        Combo = preset.Key,
                        preset.Value.Info,
                        preset.Value.ComboType,
                    },
                })
                .GroupBy(x => x.Job)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(x =>
                            x.ComboType switch
                            {
                                ComboType.Healing =>
                                    x.Info.InternalName.Contains("single target", ToLower)
                                        ? ComboTargetTypeKeys.HealST
                                        : ComboTargetTypeKeys.HealMT,
                                ComboType.Advanced or ComboType.Simple =>
                                    x.Info.InternalName.Contains("single target", ToLower)
                                        ? ComboTargetTypeKeys.SingleTarget
                                        : ComboTargetTypeKeys.MultiTarget,
                                _ => ComboTargetTypeKeys.Other,
                            }
                        )
                        .ToDictionary(
                            g2 => g2.Key,
                            g2 => g2.GroupBy(x =>
                                    x.ComboType switch
                                    {
                                        ComboType.Advanced =>
                                            ComboSimplicityLevelKeys.Advanced,
                                        ComboType.Simple =>
                                            ComboSimplicityLevelKeys.Simple,
                                        _ => ComboSimplicityLevelKeys.Other,
                                    }
                                )
                                .ToDictionary(
                                    g3 => g3.Key,
                                    g3 => g3.ToDictionary(
                                        x => x.Combo,
                                        x => comboStatesByJob[x.Job][x.Combo]
                                    )
                                )
                        )
                );

            field = rebuilt;

            Svc.Log.Verbose($"IPC Combo Built for {job}");

            return rebuilt;
        }
    }

    #endregion

    #region Options Information

    /// <summary>
    ///     The names of each option.
    /// </summary>
    /// <value>
    ///     Job -> Parent Combo Internal Name ->
    ///     <c>list</c> of option internal names.
    /// </value>
    internal Dictionary<Job,
            Dictionary<string,
                List<string>>>
        OptionNamesByJob =>
        Presets
            .Where(preset =>
                preset.Value is { IsVariant: false, HasParentCombo: true } &&
                !preset.Key.Contains("pvp", ToLower))
            .GroupBy(preset => preset.Value.Job)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(preset => preset.Value.ParentComboName)
                    .ToDictionary(
                        g2 => g2.Key,
                        g2 => g2.Select(preset => preset.Key).ToList()
                    )
            );

    /// <summary>
    ///     The states of each option.
    /// </summary>
    /// <value>
    ///     Job -> Parent Combo Internal Name -> Option Internal Name ->
    ///     State Key (really just <see cref="ComboStateKeys.Enabled" />) ->
    ///     <c>bool</c> - Whether the option is enabled or not.
    /// </value>
    internal Dictionary<Job,
            Dictionary<string,
                Dictionary<string,
                    Dictionary<ComboStateKeys, bool>>>>
        OptionStatesByJob
    {
        get
        {
            // 🔴 同 ComboStatesByJob：PresetStates 是整份替換式的快取，而下面每
            //    一個 option 都要查它一次。先抄進區域變數，整趟投影綁在同一份完整
            //    快照上，快取有效性檢查也只做一次。
            var presetStates = PresetStates;

            return OptionNamesByJob
                .ToDictionary(
                    job => job.Key,
                    job => job.Value
                        .ToDictionary(
                            parentCombo => parentCombo.Key,
                            parentCombo => parentCombo.Value
                                .ToDictionary(
                                    option => option,
                                    option => new Dictionary<ComboStateKeys, bool>
                                    {
                                        {
                                            ComboStateKeys.Enabled,
                                            presetStates[option][ComboStateKeys.Enabled]
                                        },
                                    }
                                )
                        )
                );
        }
    }

    #endregion

    /// <summary>
    ///     A wrapper for <see cref="Core.PluginConfiguration.AutoActions" /> with
    ///     IPC settings on top.
    /// </summary>
    /// <remarks>
    ///     結果只由 <see cref="PresetStates" /> 的內容決定，所以同一份快照只算一次。
    ///     🔴 回傳的字典跨呼叫、跨執行緒共用，呼叫端只能讀。
    /// </remarks>
    internal Dictionary<CustomComboPreset, bool> AutoActions
    {
        get
        {
            var presetStates = PresetStates;
            var cached = _autoActionsCache;
            if (cached is not null && ReferenceEquals(cached.Source, presetStates))
                return cached.Value;

            var rebuilt = presetStates
                .Where(x =>
                    Enum.Parse<CustomComboPreset>(x.Key).Attributes()
                        .AutoAction is not null)
                .ToDictionary(
                    preset => Enum.Parse<CustomComboPreset>(preset.Key),
                    preset => preset.Value[ComboStateKeys.AutoMode]
                );
            _autoActionsCache = new AutoActionsSnapshot(presetStates, rebuilt);
            return rebuilt;
        }
    }

    /// <summary>
    ///     <see cref="AutoActions" /> 的快取：來源快照與結果放在同一個物件、一次指派，
    ///     讀取端先抄進區域變數再判斷，與 <see cref="PresetStates" /> 同一套紀律。
    /// </summary>
    private AutoActionsSnapshot? _autoActionsCache;

    private sealed record AutoActionsSnapshot(
        Dictionary<string, Dictionary<ComboStateKeys, bool>> Source,
        Dictionary<CustomComboPreset, bool> Value);

    /// <summary>
    ///     A wrapper for <see cref="Core.PluginConfiguration.EnabledActions" /> with
    ///     IPC settings on top.
    /// </summary>
    internal HashSet<CustomComboPreset> EnabledActions =>
        PresetStates
            .Where(preset => preset.Value[ComboStateKeys.Enabled])
            .Select(preset => Enum.Parse<CustomComboPreset>(preset.Key))
            .ToHashSet();

    #endregion
}