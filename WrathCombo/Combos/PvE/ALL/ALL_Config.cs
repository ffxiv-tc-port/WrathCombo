using Dalamud.Interface.Colors;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Window.Functions;

namespace WrathCombo.Combos.PvE;

internal partial class All
{
    internal static class Config
    {
        public static readonly UserInt ALL_Tank_Reprisal_Threshold =
            new("ALL_Tank_Reprisal_Threshold");
        
        public static readonly UserBoolArray ALL_Healer_RescueRetargetingOptions = new("ALL_Healer_RescueRetargetingOptions");
            

        internal static void Draw(CustomComboPreset preset)
        {
            switch (preset)
            {
                case CustomComboPreset.ALL_Tank_Reprisal:
                    UserConfig.DrawSliderInt(0, 9, ALL_Tank_Reprisal_Threshold,
                        "允許其他人雪仇剩餘時間\n(0=目標上不能有雪仇效果)");
                    break;
                
                case CustomComboPreset.ALL_Healer_RescueRetargeting:
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow,"UI滑鼠懸停 > 場景滑鼠懸停 > 焦點目標 > 軟目標 > 硬目標");
                    ImGui.Unindent();
                    UserConfig.DrawHorizontalMultiChoice(ALL_Healer_RescueRetargetingOptions,"場景滑鼠懸停", "將場景滑鼠懸停新增到優先順序集合", 3, 0);
                    UserConfig.DrawHorizontalMultiChoice(ALL_Healer_RescueRetargetingOptions,"焦點目標", "將焦點目標新增到優先順序集合", 3, 1);
                    UserConfig.DrawHorizontalMultiChoice(ALL_Healer_RescueRetargetingOptions,"軟目標", "將軟目標新增到優先順序集合", 3, 2);
                    break;
            }
        }
    }
}
