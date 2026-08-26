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

        /// <summary>自動傷腿的觸發時機。0 ＝ 只對以我為目標的敵人，1 ＝ 無條件。</summary>
        /// <remarks>
        ///     ⚠️ 0 必須是有效值 —— 這是 <c>UserInt</c> 的預設，
        ///     選項編號從 0 起算才不會讓預設落在無效值上。
        /// </remarks>
        public static readonly UserInt ALL_Ranged_LegGraze_Trigger =
            new("ALL_Ranged_LegGraze_Trigger", 0);

        /// <summary>同一隻身上最多補幾次加重（遞減免疫保護）。</summary>
        public static readonly UserInt ALL_Ranged_LegGraze_MaxApplications =
            new("ALL_Ranged_LegGraze_MaxApplications", 3);

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

                case CustomComboPreset.ALL_Ranged_LegGraze:
                    // ⚠️ 第一顆畫出來的選項就是預設值（DrawRadioButton 在設定不存在時
                    // 會用自己的 outputValue 去初始化）——「只對追我的」必須排第一。
                    UserConfig.DrawRadioButton(ALL_Ranged_LegGraze_Trigger,
                        "只對正在追我的敵人",
                        "只有當敵人「目前的目標是你」時才補加重。\n手動風箏（例如深層迷宮拉怪）就是這個情境。",
                        0, descriptionAsTooltip: true);
                    UserConfig.DrawRadioButton(ALL_Ranged_LegGraze_Trigger,
                        "任何符合條件的目標",
                        "不管敵人在打誰，只要它身上沒有加重就補。\n會消耗較多次傷腿，也比較容易撞到遞減免疫。",
                        1, descriptionAsTooltip: true);
                    UserConfig.DrawSliderInt(1, 5, ALL_Ranged_LegGraze_MaxApplications,
                        "同一隻最多補幾次加重\n(遞減免疫保護，超過就停手)");
                    break;
            }
        }
    }
}
