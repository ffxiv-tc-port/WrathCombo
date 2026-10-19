using System;
using WrathCombo.Combos;
using WrathCombo.Window.Functions;

namespace WrathCombo.Extensions
{
    internal static class CustomComboPresetExtensions
    {
        public static Presets.PresetAttributes? Attributes(this CustomComboPreset preset)
        {
            if (Presets.Attributes.TryGetValue(preset, out var atts))
                return atts;
            return null;
        } 

        /// <summary> 與 <c>preset.ToString()</c> 同一個字串，但不配置：列舉的 ToString 繼承自 System.Enum，每次呼叫裝箱一次。 </summary>
        /// <remarks> 未定義的值（設定裡留著已刪掉的編號）沒有名稱，退回 ToString 的數字字串。 </remarks>
        public static string EnumName(this CustomComboPreset preset) =>
            Enum.GetName(preset) ?? preset.ToString();
    }
}
