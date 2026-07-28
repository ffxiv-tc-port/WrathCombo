using Dalamud.Interface.Colors;
using Dalamud.Bindings.ImGui;
using WrathCombo.CustomComboNS.Functions;
using static WrathCombo.Window.Functions.UserConfig;

namespace WrathCombo.Combos.PvE;

internal partial class BRD
{
    internal static class Config
    {
        public static UserInt
            BRD_RagingJawsRenewTime = new("ragingJawsRenewTime"),            
            BRD_STSecondWindThreshold = new("BRD_STSecondWindThreshold"),
            BRD_AoESecondWindThreshold = new("BRD_AoESecondWindThreshold"),
            BRD_VariantCure = new("BRD_VariantCure"),
            BRD_Adv_Opener_Selection = new("BRD_Adv_Opener_Selection", 0),
            BRD_Balance_Content = new("BRD_Balance_Content", 1),
            BRD_Adv_DoT_Threshold = new("BRD_Adv_DoT_Threshold", 1),
            BRD_Adv_DoT_SubOption = new("BRD_Adv_DoT_SubOption", 1),
            BRD_Adv_Buffs_Threshold = new ("BRD_Adv_Buffs_Threshold", 1),
            BRD_Adv_Buffs_SubOption = new ("BRD_Adv_Buffs_SubOption", 1),
            BRD_AoE_Adv_Buffs_Threshold = new("BRD_AoE_Adv_Buffs_Threshold", 1),
            BRD_AoE_Adv_Buffs_SubOption = new("BRD_AoE_Adv_Buffs_SubOption", 1);

        internal static void Draw(CustomComboPreset preset)
        {
            switch (preset)
            {
                case CustomComboPreset.BRD_ST_Adv_Balance_Standard:
                    DrawRadioButton(BRD_Adv_Opener_Selection, "Standard Opener", "", 0);
                    DrawRadioButton(BRD_Adv_Opener_Selection, "2.48 Adjusted Standard Opener", "", 1);
                    DrawRadioButton(BRD_Adv_Opener_Selection, "2.49 Standard Comfy", "", 2);

                    ImGui.Indent();
                    DrawBossOnlyChoice(BRD_Balance_Content);
                    ImGui.Unindent();
                    break;

                case CustomComboPreset.BRD_Adv_RagingJaws:
                    DrawSliderInt(3, 10, BRD_RagingJawsRenewTime,
                        "Remaining time (In seconds). Recommended 5, increase little by little if refresh is outside of radiant window");

                    break;

                case CustomComboPreset.BRD_Adv_DoT:

                    DrawSliderInt(0, 100, BRD_Adv_DoT_Threshold,
                        $"目標HP低於該百分比時停止使用持續傷害技能（0% = 總是使用，100% = 從不使用）。");

                    ImGui.Indent();

                    ImGui.TextColored(ImGuiColors.DalamudYellow, "選擇HP檢查可應用於哪種型別的敵人：");

                    DrawHorizontalRadioButton(BRD_Adv_DoT_SubOption,
                        "僅非Boss敵人", $"僅對非Boss敵人應用HP檢查", 0);

                    DrawHorizontalRadioButton(BRD_Adv_DoT_SubOption,
                        "All Content", $"對所有內容應用HP檢查", 1);

                    ImGui.Unindent();

                    break;

                case CustomComboPreset.BRD_Adv_Buffs:

                    DrawSliderInt(0, 100, BRD_Adv_Buffs_Threshold,
                       $"目標HP低於該百分比時停止使用增益（0% = 總是使用，100% = 從不使用）。");

                    ImGui.Indent();

                    ImGui.TextColored(ImGuiColors.DalamudYellow, "選擇HP檢查可應用於哪種型別的敵人：");

                    DrawHorizontalRadioButton(BRD_Adv_Buffs_SubOption,
                        "僅非Boss敵人", $"僅對非Boss敵人應用HP檢查", 0);

                    DrawHorizontalRadioButton(BRD_Adv_Buffs_SubOption,
                        "All Content", $"對所有內容應用HP檢查", 1);

                    ImGui.Unindent();

                    break;

                case CustomComboPreset.BRD_AoE_Adv_Buffs:

                    DrawSliderInt(0, 100, BRD_AoE_Adv_Buffs_Threshold,
                        $"目標HP低於該百分比時停止使用增益（0% = 總是使用，100% = 從不使用）。");

                    ImGui.Indent();

                    ImGui.TextColored(ImGuiColors.DalamudYellow, "選擇HP檢查可應用於哪種型別的敵人：");

                    DrawHorizontalRadioButton(BRD_AoE_Adv_Buffs_SubOption,
                        "僅非Boss敵人", $"僅對非Boss敵人應用HP檢查", 0);

                    DrawHorizontalRadioButton(BRD_AoE_Adv_Buffs_SubOption,
                        "All Content", $"對所有內容應用HP檢查", 1);

                    ImGui.Unindent();

                    break;

                case CustomComboPreset.BRD_ST_SecondWind:
                    DrawSliderInt(0, 100, BRD_STSecondWindThreshold,
                        "HP percent threshold to use Second Wind below.");

                    break;

                case CustomComboPreset.BRD_AoE_SecondWind:
                    DrawSliderInt(0, 100, BRD_AoESecondWindThreshold,
                        "HP percent threshold to use Second Wind below.");

                    break;

                case CustomComboPreset.BRD_Variant_Cure:
                    DrawSliderInt(1, 100, BRD_VariantCure, "HP% to be at or under", 200);

                    break;
            }
        }
    }
}
