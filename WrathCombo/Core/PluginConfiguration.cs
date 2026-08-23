using Dalamud.Configuration;
using ECommons.DalamudServices;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ECommons.Logging;
using WrathCombo.AutoRotation;
using WrathCombo.Combos;
using WrathCombo.Extensions;
using WrathCombo.Services;
using WrathCombo.Window;
using Debug = WrathCombo.Window.Tabs.Debug;

namespace WrathCombo.Core
{
    /// <summary> Plugin configuration. </summary>
    [Serializable]
    public class PluginConfiguration : IPluginConfiguration
    {
        #region Version

        /// <summary> Gets or sets the configuration version. </summary>
        public int Version { get; set; } = 5;

        #endregion

        #region EnabledActions

        /// <summary> Gets or sets the collection of enabled combos. </summary>
        [JsonProperty("EnabledActionsV6")]
        public HashSet<CustomComboPreset> EnabledActions { get; set; } = [];

        #endregion

        #region Settings Options

        /// <summary> Gets or sets a value indicating whether to output combat log to the chatbox. </summary>
        public bool EnabledOutputLog { get; set; } = false;

        /// <summary> Gets or sets a value indicating whether to hide combos which conflict with enabled presets. </summary>
        public bool HideConflictedCombos { get; set; } = false;

        /// <summary> Gets or sets a value indicating whether to hide the children of a feature if it is disabled. </summary>
        public bool HideChildren { get; set; } = false;

        /// <summary> Gets or sets the offset of the melee range check. Default is 0. </summary>
        public double MeleeOffset { get; set; } = 0;

        public bool BlockSpellOnMove = false;

        public Vector4 TargetHighlightColor { get; set; } = new() { W = 1, X = 0.5f, Y = 0.5f, Z = 0.5f };

        public bool OutputOpenerLogs;

        public float MovementLeeway = 0f;

        public float OpenerTimeout = 4f;

        public bool PerformanceMode = false;

        public int Throttle = 50;

        public double InterruptDelay  = 0.0f;
        
        public int MaximumWeavesPerWindow = 2;

        public bool OpenToPvE = false;

        public bool OpenToPvP = false;

        public bool OpenToCurrentJob = false;

        public bool OpenToCurrentJobOnSwitch = false;

        #region Target Settings

        public bool RetargetHealingActionsToStack = false;

        public bool AddOutOfPartyNPCsToRetargeting = false;

        public bool UseUIMouseoverOverridesInDefaultHealStack = false;

        public bool UseFieldMouseoverOverridesInDefaultHealStack = false;

        public bool UseFocusTargetOverrideInDefaultHealStack = false;

        public bool UseLowestHPOverrideInDefaultHealStack = false;

        public bool UseCustomHealStack = false;

        #region Mechanic-Aware Enemy Targeting

        /// <summary>
        ///     打斷／暈眩的目標選擇要不要參考 BossMod(Reborn) 的機制標記。
        /// </summary>
        /// <remarks>
        ///     預設 <see langword="false" /> ＝ 完全維持現行行為。<br />
        ///     開啟後只是把 BMR 標記過的敵人<b>排到前面</b>，仍然要通過既有的
        ///     敵對／可選取／距離／可打斷／ICD 判斷。BMR 缺席或沒標記任何東西時，
        ///     排序沒有東西可排，結果與關閉時相同。
        /// </remarks>
        public bool MechanicAwareTargeting = false;

        /// <summary>
        ///     嚴格模式：只把 BMR 標記過的敵人列入候選。
        /// </summary>
        /// <remarks>
        ///     🔴 這一條會<b>減少</b>出手：BMR 缺席或清單為空時，打斷／暈眩的目標
        ///     選擇一律回 <see langword="null" />（＝不出手）。
        ///     只在 <see cref="MechanicAwareTargeting" /> 開啟時有效。
        /// </remarks>
        public bool MechanicAwareTargetingStrictOnly = false;

        /// <summary>
        ///     暈眩目標選擇要不要跳過 MonsterDex 明確標示為「不吃暈眩」的敵人。
        /// </summary>
        /// <remarks>
        ///     🔴 只有在 MonsterDex <b>明確回報 bit0 ＝ 0</b> 時才排除。
        ///     沒安裝、端點不存在、查無資料（回 <c>-1</c>）一律<b>放行</b>，
        ///     絕不把「不知道」當成「免疫」。
        /// </remarks>
        public bool MonsterDexStunGate = false;

        #endregion

        // Just has value so the UI element for it is more obvious from the get-go
        public string[] CustomHealStack = [
            "FocusTarget",
            "HardTarget",
            "Self",
        ];

        public string[] RaiseStack = [
            "AnyHealer",
            "AnyTank",
            "AnyRaiser",
            "AnyDeadPartyMember",
        ];

        #endregion

        public bool ActionChanging = true;

        internal void SetActionChanging(bool? newValue = null)
        {
            if (newValue is not null && newValue != ActionChanging)
            {
                ActionChanging = newValue.Value;
                Save();
            }

            // Checks if action replacing is not in line with the setting
            if (ActionChanging && !Service.ActionReplacer.getActionHook.IsEnabled)
                Service.ActionReplacer.getActionHook.Enable();
            if (!ActionChanging && Service.ActionReplacer.getActionHook.IsEnabled)
                Service.ActionReplacer.getActionHook.Disable();
        }

        public bool ShowHiddenFeatures = false;

        public bool SuppressQueuedActions = true;
        
        public bool UILeftColumnCollapsed = false;

        #endregion

        #region AutoAction Settings
        public Dictionary<CustomComboPreset, bool> AutoActions { get; set; } = [];

        public AutoRotationConfig RotationConfig { get; set; } = new();

        public Dictionary<uint, uint> IgnoredNPCs { get; set; } = new();

        #endregion

        #region Custom Float Values

        [JsonProperty("CustomFloatValuesV6")]
        internal static Dictionary<string, float> CustomFloatValues { get; set; } = [];

        /// <summary> Gets a custom float value. </summary>
        public static float GetCustomFloatValue(string config, float defaultMinValue = 0)
        {
            if (!CustomFloatValues.TryGetValue(config, out float configValue))
            {
                SetCustomFloatValue(config, defaultMinValue);
                return defaultMinValue;
            }

            return configValue;
        }

        /// <summary> Sets a custom float value. </summary>
        public static void SetCustomFloatValue(string config, float value) => CustomFloatValues[config] = value;

        #endregion

        #region Custom Int Values

        [JsonProperty("CustomIntValuesV6")]
        internal static Dictionary<string, int> CustomIntValues { get; set; } = [];

        /// <summary> Gets a custom integer value. </summary>
        public static int GetCustomIntValue(string config, int defaultMinVal = 0)
        {
            if (!CustomIntValues.TryGetValue(config, out int configValue))
            {
                SetCustomIntValue(config, defaultMinVal);
                return defaultMinVal;
            }

            return configValue;
        }

        /// <summary> Sets a custom integer value. </summary>
        public static void SetCustomIntValue(string config, int value) => CustomIntValues[config] = value;

        #endregion

        #region Custom Int Array Values
        [JsonProperty("CustomIntArrayValuesV6")]
        internal static Dictionary<string, int[]> CustomIntArrayValues { get; set; } = [];

        /// <summary> Gets a custom integer array value. </summary>
        public static int[] GetCustomIntArrayValue(string config)
        {
            if (!CustomIntArrayValues.TryGetValue(config, out int[]? configValue))
            {
                SetCustomIntArrayValue(config, []);
                return [];
            }

            return configValue;
        }

        /// <summary> Sets a custom integer array value. </summary>
        public static void SetCustomIntArrayValue(string config, int[] value) => CustomIntArrayValues[config] = value;

        #endregion

        #region Custom Bool Values

        [JsonProperty("CustomBoolValuesV6")]
        internal static Dictionary<string, bool> CustomBoolValues { get; set; } = [];

        /// <summary> Gets a custom boolean value. </summary>
        public static bool GetCustomBoolValue(string config)
        {
            if (!CustomBoolValues.TryGetValue(config, out bool configValue))
            {
                SetCustomBoolValue(config, false);
                return false;
            }

            return configValue;
        }

        /// <summary> Sets a custom boolean value. </summary>
        public static void SetCustomBoolValue(string config, bool value) => CustomBoolValues[config] = value;

        #endregion

        #region Custom Bool Array Values

        [JsonProperty("CustomBoolArrayValuesV6")]
        internal static Dictionary<string, bool[]> CustomBoolArrayValues { get; set; } = [];

        /// <summary> Gets a custom boolean array value. </summary>
        public static bool[] GetCustomBoolArrayValue(string config)
        {
            if (!CustomBoolArrayValues.TryGetValue(config, out bool[]? configValue))
            {
                SetCustomBoolArrayValue(config, Array.Empty<bool>());
                return Array.Empty<bool>();
            }

            return configValue;
        }

        /// <summary> Sets a custom boolean array value. </summary>
        public static void SetCustomBoolArrayValue(string config, bool[] value) => CustomBoolArrayValues[config] = value;

        #endregion

        #region Job-specific

        /// <summary> Gets active Blue Mage (BLU) spells. </summary>
        public List<uint> ActiveBLUSpells { get; set; } = [];

        /// <summary> Gets or sets an array of 4 ability IDs to interact with the <see cref="CustomComboPreset.DNC_CustomDanceSteps"/> combo. </summary>
        public uint[] DancerDanceCompatActionIDs { get; set; } = [ 0, 0, 0, 0, ];

        #endregion

        #region Preset Resetting

        [JsonProperty]
        private static Dictionary<string, bool> ResetFeatureCatalog { get; set; } = [];

        private static bool GetResetValues(string config)
        {
            if (ResetFeatureCatalog.TryGetValue(config, out var value)) return value;

            return false;
        }

        private static void SetResetValues(string config, bool value)
        {
            ResetFeatureCatalog[config] = value;
        }

        public void ResetFeatures(string config, int[] values)
        {
            Svc.Log.Debug($"{config} {GetResetValues(config)}");
            if (!GetResetValues(config))
            {
                bool needToResetMessagePrinted = false;

                var presets = Enum.GetValues<CustomComboPreset>().Cast<int>();

                foreach (int value in values)
                {
                    Svc.Log.Debug(value.ToString());
                    if (presets.Contains(value))
                    {
                        var preset = Enum.GetValues<CustomComboPreset>()
                            .Where(preset => (int)preset == value)
                            .First();

                        if (!PresetStorage.IsEnabled(preset)) continue;

                        if (!needToResetMessagePrinted)
                        {
                            DuoLog.Error($"由於內部配置更新，某些功能已被停用:");
                            needToResetMessagePrinted = !needToResetMessagePrinted;
                        }

                        var info = preset.GetComboAttribute();
                        DuoLog.Error($"- {info.JobName}: {info.Name}");
                        EnabledActions.Remove(preset);
                    }
                }

                if (needToResetMessagePrinted)
                    DuoLog.Error($"請重新啟用這些功能以繼續使用。我們為造成的不便深表歉意");
            }
            SetResetValues(config, true);
            Save();
        }

        #endregion

        #region Other (SpecialEvent, MotD)

        /// <summary> Hides the message of the day. </summary>
        public bool HideMessageOfTheDay { get; set; } = false;

        /// <summary>
        ///     Whether the Major Changes window was hidden for a
        ///     specific version.
        /// </summary>
        /// <seealso cref="MajorChangesWindow"/>
        public Version HideMajorChangesForVersion { get; set; } =
            System.Version.Parse("0.0.0");

        /// <summary>
        ///     If the DTR Bar text should be shortened.
        /// </summary>
        public bool ShortDTRText { get; set; } = false;

        #endregion

        #region Saving

        /// <summary>
        ///     The queue of items to be saved.
        /// </summary>
        /// <remarks>
        ///     第二個欄位是「誰要求存檔」，只在存檔失敗的錯誤訊息裡用到。
        ///     以前這裡放的是 <see cref="StackTrace"/>，等於每次 <see cref="Save"/>
        ///     都在主執行緒走訪一次完整呼叫堆疊；改用編譯期就決定好的呼叫者資訊，
        ///     診斷價值一樣但執行期成本是零。
        /// </remarks>
        internal static readonly Queue<(PluginConfiguration, string)> SaveQueue = [];

        /// <summary>
        ///     Whether an item is currently being saved.
        /// </summary>
        private static bool _isSaving;

        /// <summary>
        ///     Process the <see cref="SaveQueue"/>, trying to save each item.
        /// </summary>
        /// <seealso cref="Save"/>
        internal static void ProcessSaveQueue()
        {
            if (_isSaving || SaveQueue.Count == 0) return;

            _isSaving = true;
            (PluginConfiguration config, string trace) dequeued;
            lock (SaveQueue)
                dequeued = SaveQueue.Dequeue();
            var (config, trace) = dequeued;

            try
            {
                Svc.PluginInterface.SavePluginConfig(config);
                _isSaving = false;
            }
            catch (Exception)
            {
                Svc.Framework.Run(() => RetrySave(config, trace));
            }
        }

        internal static void RetrySave
            (PluginConfiguration config, string trace)
        {
            var success = false;
            var retryCount = 0;

            while (!success)
            {
                try
                {
                    Svc.PluginInterface.SavePluginConfig(config);
                    success = true;
                }
                catch (Exception e)
                {
                    retryCount++;
                    if (retryCount < 3)
                    {
                        Task.Delay(20).Wait();
                        continue;
                    }

                    PluginLog.Error(
                        "Failed to save configuration after 3 retries.\n" +
                        e.Message + "\n" + trace);
                    _isSaving = false;
                    return;
                }
            }

            _isSaving = false;
        }

        /// <summary> Set the configuration to be saved to disk. </summary>
        /// <remarks>
        ///     <para>
        ///     Configurations set to be saved will be processed in the order they
        ///     were added, each frame.
        ///     </para>
        ///     <para>
        ///     同一份設定在佇列裡最多只會排一筆：真正的序列化是在出列時才做，
        ///     所以已經排隊的那一筆本來就會寫入「當下最新」的內容，重複排隊只會
        ///     讓同一份檔案被重寫好幾次。每個 tick 只處理一筆，重複排隊還會讓
        ///     佇列越積越長，放開滑桿之後仍持續寫檔。
        ///     </para>
        /// </remarks>
        /// <seealso cref="SaveQueue"/>
        public void Save
            ([CallerMemberName] string caller = "",
             [CallerFilePath] string callerFile = "",
             [CallerLineNumber] int callerLine = 0)
        {
            if (Debug.DebugConfig)
                return;

            lock (SaveQueue)
            {
                foreach (var (queued, _) in SaveQueue)
                    if (ReferenceEquals(queued, this))
                        return;

                SaveQueue.Enqueue(
                    (this, $"{caller} ({Path.GetFileName(callerFile)}:{callerLine})"));
            }
        }

        #endregion
    }
}
