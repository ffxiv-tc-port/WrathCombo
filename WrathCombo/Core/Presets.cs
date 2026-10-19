using Dalamud.Utility;
using ECommons;
using ECommons.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using WrathCombo.Attributes;
using WrathCombo.Combos;
using WrathCombo.Extensions;
using WrathCombo.Services;
using WrathCombo.Window.Functions;
using EZ = ECommons.Throttlers.EzThrottler;
using TS = System.TimeSpan;

namespace WrathCombo.Core
{
    internal static class PresetStorage
    {
        private static HashSet<CustomComboPreset>? PvPCombos;
        private static HashSet<CustomComboPreset>? VariantCombos;
        private static HashSet<CustomComboPreset>? BozjaCombos;
        private static HashSet<CustomComboPreset>? OccultCrescentCombos;
        private static HashSet<CustomComboPreset>? EurekaCombos;
        private static Dictionary<CustomComboPreset, CustomComboPreset[]>? ConflictingCombos;
        private static Dictionary<CustomComboPreset, CustomComboPreset?>? ParentCombos;  // child: parent
        
        public static HashSet<CustomComboPreset>? AllPresets;
        
        public static HashSet<uint> AllRetargetedActions {
            get {
                if (!EZ.Throttle("allRetargetedActions", TS.FromSeconds(3)))
                    return field;
                var result = Enum.GetValues<CustomComboPreset>()
                    .SelectMany(preset => preset.Attributes()?.RetargetedActions ?? [])
                    .ToHashSet();
                PluginLog.Verbose($"Retrieved {result.Count} retargeted actions");
                field = result;
                return result;
            }
        } = null!;

        public static void Init()
        {
            // 每個 preset 的屬性只讀一次再分給各張表：每讀一次都會重新建構該欄位上的全部屬性（含翻譯說明文字）。
            var presets = Enum.GetValues<CustomComboPreset>();
            var attributes = new object[presets.Length][];
            for (var i = 0; i < presets.Length; i++)
                attributes[i] = ReadAttributes(presets[i]);

            PvPCombos = PresetsWith<PvPCustomComboAttribute>(presets, attributes);
            VariantCombos = PresetsWith<VariantAttribute>(presets, attributes);
            BozjaCombos = PresetsWith<BozjaAttribute>(presets, attributes);
            OccultCrescentCombos = PresetsWith<OccultCrescentAttribute>(presets, attributes);
            EurekaCombos = PresetsWith<EurekaAttribute>(presets, attributes);

            ConflictingCombos = new Dictionary<CustomComboPreset, CustomComboPreset[]>(presets.Length);
            for (var i = 0; i < presets.Length; i++)
                ConflictingCombos.Add(presets[i], Pick<ConflictingCombosAttribute>(attributes[i])?.ConflictingPresets ?? []);

            ParentCombos = new Dictionary<CustomComboPreset, CustomComboPreset?>(presets.Length);
            for (var i = 0; i < presets.Length; i++)
                ParentCombos.Add(presets[i], Pick<ParentComboAttribute>(attributes[i])?.ParentPreset);

            AllPresets = presets.ToHashSet();

            for (var i = 0; i < presets.Length; i++)
            {
                Presets.Attributes.Add(presets[i], new Presets.PresetAttributes(presets[i], attributes[i]));
            }
            PluginLog.Information($"Cached {Presets.Attributes.Count} preset attributes."); 
        }

        /// <summary> 與 Dalamud <c>EnumExtensions.GetAttribute</c> 同一條讀法；每次呼叫都重新建構該欄位上的全部屬性。 </summary>
        internal static object[] ReadAttributes(CustomComboPreset preset)
        {
            var name = Enum.GetName(preset);
            if (string.IsNullOrEmpty(name))
                return [];
            return typeof(CustomComboPreset).GetField(name)?.GetCustomAttributes(false) ?? [];
        }

        /// <summary> 等同 <c>GetAttribute&lt;T&gt;()</c>：同型別出現兩個照樣擲。 </summary>
        internal static T? Pick<T>(object[] attributes) where T : Attribute =>
            attributes.OfType<T>().SingleOrDefault();

        private static HashSet<CustomComboPreset> PresetsWith<T>(CustomComboPreset[] presets, object[][] attributes)
            where T : Attribute
        {
            var result = new HashSet<CustomComboPreset>();
            for (var i = 0; i < presets.Length; i++)
                if (Pick<T>(attributes[i]) != null)
                    result.Add(presets[i]);
            return result;
        }


        /// <summary> Gets a value indicating whether a preset is enabled. </summary>
        /// <param name="preset"> Preset to check. </param>
        /// <returns> The boolean representation. </returns>
        public static bool IsEnabled(CustomComboPreset preset) => Service.Configuration.EnabledActions.Contains(preset) && !ShouldBeHidden(preset);

        /// <summary>
        /// Gets a value indicating whether a preset is marked as hidden.
        /// </summary>
        /// <param name="preset"></param>
        /// <returns></returns>
        public static bool ShouldBeHidden(CustomComboPreset preset) =>
            preset.Attributes().Hidden != null &&
            !Service.Configuration.ShowHiddenFeatures;

        /// <summary> Gets a value indicating whether a preset is secret. </summary>
        /// <param name="preset"> Preset to check. </param>
        /// <returns> The boolean representation. </returns>
        public static bool IsPvP(CustomComboPreset preset) => PvPCombos.Contains(preset);

        /// <summary> Gets a value indicating whether a preset is secret. </summary>
        /// <param name="preset"> Preset to check. </param>
        /// <returns> The boolean representation. </returns>
        public static bool IsVariant(CustomComboPreset preset) => VariantCombos.Contains(preset);

        /// <summary>
        ///     Gets a value indicating whether a preset can be retargeted under some
        ///     settings, with <see cref="ActionRetargeting" />.
        /// </summary>
        /// <param name="preset"> Preset to check. </param>
        /// <returns> The boolean representation. </returns>
        public static bool IsPossiblyRetargeted(CustomComboPreset preset) =>
            preset.GetAttribute<RetargetedAttribute>() != null;

        /// <summary>
        ///     Gets a value indicating whether a preset is possibly retargeted with
        ///     <see cref="ActionRetargeting" />.
        /// </summary>
        /// <param name="preset"> Preset to check. </param>
        /// <returns> The boolean representation. </returns>
        public static bool IsRetargeted(CustomComboPreset preset) =>
            preset.GetAttribute<RetargetedAttribute>() != null;

        /// <summary> Gets a value indicating whether a preset is secret. </summary>
        /// <param name="preset"> Preset to check. </param>
        /// <returns> The boolean representation. </returns>
        public static bool IsBozja(CustomComboPreset preset) => BozjaCombos.Contains(preset);

        /// <summary> Gets a value indicating whether a preset is secret. </summary>
        /// <param name="preset"> Preset to check. </param>
        /// <returns> The boolean representation. </returns>
        public static bool IsOccultCrescent(CustomComboPreset preset) => OccultCrescentCombos.Contains(preset);

        /// <summary> Gets a value indicating whether a preset is secret. </summary>
        /// <param name="preset"> Preset to check. </param>
        /// <returns> The boolean representation. </returns>
        public static bool IsEureka(CustomComboPreset preset) => EurekaCombos.Contains(preset);

        /// <summary> Gets the parent combo preset if it exists, or null. </summary>
        /// <param name="preset"> Preset to check. </param>
        /// <returns> The parent preset. </returns>
        public static CustomComboPreset? GetParent(CustomComboPreset preset) => ParentCombos[preset];

        /// <summary> Gets an array of conflicting combo presets. </summary>
        /// <param name="preset"> Preset to check. </param>
        /// <returns> The conflicting presets. </returns>
        public static CustomComboPreset[] GetConflicts(CustomComboPreset preset) => ConflictingCombos[preset];

        /// <summary> Gets the full list of conflicted combos. </summary>
        public static List<CustomComboPreset> GetAllConflicts() => ConflictingCombos.Keys.ToList();

        /// <summary> 等同 <c>GetAllConflicts().Contains(preset)</c>，但不複製整份清單（設定視窗每幀每個 preset 都問）。 </summary>
        public static bool HasConflictEntry(CustomComboPreset preset) => ConflictingCombos.ContainsKey(preset);

        /// <summary> Get all the info from conflicted combos. </summary>
        public static List<CustomComboPreset[]> GetAllConflictOriginals() => ConflictingCombos.Values.ToList();

        public static CustomComboPreset? GetPresetByString(string value)
        {
            if (Enum.GetValues<CustomComboPreset>().TryGetFirst(x => x.ToString().ToLower() == value.ToLower(), out var pre))
            {
                return pre;
            }
            return null;
        }

        public static CustomComboPreset? GetPresetByInt(int value)
        {
            if (Enum.GetValues<CustomComboPreset>().TryGetFirst(x => (int)x == value, out var pre))
            {
                return pre;
            }
            return null;
        }

        /// <summary> Iterates up a preset's parent tree, enabling each of them. </summary>
        /// <param name="preset"> Combo preset to enabled. </param>
        public static void EnableParentPresets(CustomComboPreset preset)
        {
            var parentMaybe = GetParent(preset);

            while (parentMaybe != null)
            {
                EnablePreset(parentMaybe.Value);
                parentMaybe = GetParent(parentMaybe.Value);
            }
        }

        public static void DisableAllConflicts(CustomComboPreset preset)
        {
            var conflicts = GetConflicts(preset);
            foreach (var conflict in conflicts)
            {
                Service.Configuration.DisableAction(conflict);
            }
        }

        public static bool EnablePreset(string preset, bool outputLog = false)
        {
            var pre = GetPresetByString(preset);
            if (pre != null)
            {
                return EnablePreset(pre.Value, outputLog);
            }
            return false;
        }

        public static bool EnablePreset(int preset, bool outputLog = false)
        {
            var pre = GetPresetByInt(preset);
            if (pre != null)
            {
                return EnablePreset(pre.Value, outputLog);
            }
            return false;
        }

        public static bool EnablePreset(CustomComboPreset preset, bool outputLog = false)
        {
            var ctrlText = GetControlledText(preset);
            EnableParentPresets(preset);
            var ret = Service.Configuration.EnableAction(preset);
            DisableAllConflicts(preset);

            if (outputLog)
                DuoLog.Information($"{(int)preset} - {preset} SET{ctrlText}");

            return ret;
        }

        public static bool DisablePreset(string preset, bool outputLog = false)
        {
            var pre = GetPresetByString(preset);
            if (pre != null)
            {
                return DisablePreset(pre.Value, outputLog);
            }
            return false;
        }

        public static bool DisablePreset(int preset, bool outputLog = false)
        {
            var pre = GetPresetByInt(preset);
            if (pre != null)
            {
                return DisablePreset(pre.Value, outputLog);
            }
            return false;
        }

        public static bool DisablePreset(CustomComboPreset preset, bool outputLog = false)
        {
            if (Service.Configuration.DisableAction(preset))
            {
                var ctrlText = GetControlledText(preset);

                if (outputLog)
                    DuoLog.Information($"{(int)preset} - {preset} UNSET{ctrlText}");

                return true;
            }

            return false;
        }

        private static object GetControlledText(CustomComboPreset preset)
        {
            var controlled = P.UIHelper.PresetControlled(preset) is not null;
            var ctrlText = controlled ? " " + OptionControlledByIPC : "";

            return ctrlText;
        }

        public static bool TogglePreset(CustomComboPreset preset, bool outputLog = false)
        {
            var ctrlText = GetControlledText(preset);
            if (!Service.Configuration.DisableAction(preset))
            {
                var ret = EnablePreset(preset);
                if (outputLog)
                    DuoLog.Information($"{(int)preset} - {preset} SET{ctrlText}");
            }
            else if (outputLog)
            {
                DuoLog.Information($"{(int)preset} - {preset} UNSET{ctrlText}");
            }
            return false;
        }

        public static bool TogglePreset(string preset, bool outputLog = false)
        {
            var pre = GetPresetByString(preset);
            if (pre != null)
            {
                return TogglePreset(pre.Value, outputLog);
            }
            return false;
        }

        public static bool TogglePreset(int preset, bool outputLog = false)
        {
            var pre = GetPresetByInt(preset);
            if (pre != null)
            {
                return TogglePreset(pre.Value, outputLog);
            }
            return false;
        }
        
        internal static ComboType GetComboType(CustomComboPreset preset) =>
            GetComboType(ReadAttributes(preset));

        /// <param name="attributes"> <see cref="ReadAttributes" /> 對同一個 preset 讀出來的屬性。 </param>
        internal static ComboType GetComboType(object[] attributes)
        {
            var simple = Pick<SimpleCombo>(attributes);
            var advanced = Pick<AdvancedCombo>(attributes);
            var basic = Pick<BasicCombo>(attributes);
            var healing = Pick<HealingCombo>(attributes);
            var mitigation = Pick<MitigationCombo>(attributes);
            var parent = (object?)Pick<ParentComboAttribute>(attributes) ??
                         (object?)Pick<BozjaParentAttribute>(attributes) ??
                         (object?)Pick<EurekaParentAttribute>(attributes) ??
                         Pick<VariantParentAttribute>(attributes);
            
            if (simple != null)
                return ComboType.Simple;
            if (advanced != null)
                return ComboType.Advanced;
            if (basic != null)
                return ComboType.Basic;
            
            if (healing != null)
                return ComboType.Healing;
            if (mitigation != null)
                return ComboType.Mitigation;
            
            if (parent == null)
                return ComboType.Feature;
            
            return ComboType.Option;
        }
    }
}
