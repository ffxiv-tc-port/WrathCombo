using ImGuiNET;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Extensions;
using WrathCombo.Window.Functions;
using static WrathCombo.Window.Functions.UserConfig;
namespace WrathCombo.Combos.PvE;

internal partial class SAM
{
    internal static class Config
    {
        internal static void Draw(CustomComboPreset preset)
        {
            switch (preset)
            {
                case CustomComboPreset.SAM_ST_Opener:
                    DrawBossOnlyChoice(SAM_Balance_Content);
                    ImGui.NewLine();
                    DrawSliderInt(0, 13, SAM_Opener_PrePullDelay,
                        $"從首次{MeikyoShisui.ActionName()}到下一步的延遲（秒）\n此延遲透過將你的按鈕替換為狂怒劍來強制執行。");
                    break;

                case CustomComboPreset.SAM_ST_CDs_Iaijutsu:
                    DrawHorizontalMultiChoice(SAM_ST_CDs_IaijutsuOption, $"Add {Higanbana.ActionName()}", "根據子選項決定是否使用彼岸花。", 4, 0);
                    DrawHorizontalMultiChoice(SAM_ST_CDs_IaijutsuOption, $"Add {TenkaGoken.ActionName()}", "同步等級低於50級時會使用天下五劍。", 4, 1);
                    DrawHorizontalMultiChoice(SAM_ST_CDs_IaijutsuOption, $"Use {MidareSetsugekka.ActionName()}", "會使用紛亂雪月花與天道雪月花。", 4, 2);
                    DrawHorizontalMultiChoice(SAM_ST_CDs_IaijutsuOption, $"Use {TsubameGaeshi.ActionName()}", "Will use Tsubame-gaeshi and Tendo Kaeshi Setsugekka.", 4, 3);

                    if (SAM_ST_CDs_IaijutsuOption[0])
                    {
                        ImGui.Indent();
                        DrawHorizontalRadioButton(SAM_ST_Higanbana_Suboption,
                            "All Enemies", $"Uses {Higanbana.ActionName()} regardless of targeted enemy type.", 0);

                        DrawHorizontalRadioButton(SAM_ST_Higanbana_Suboption,
                            "Bosses Only", $"Only uses {Higanbana.ActionName()} when the targeted enemy is a boss.", 1);
                        ImGui.Unindent();

                        DrawSliderInt(0, 10, SAM_ST_Higanbana_HP_Threshold,
                            $"目標血量低於該百分比時停止使用{Higanbana.ActionName()}（0% = 總是使用）。");

                        DrawSliderInt(0, 15, SAM_ST_Higanbana_Refresh,
                            $"重新應用{Higanbana.ActionName()}前的剩餘秒數。設定為0禁用此檢查。");
                    }
                    break;

                case CustomComboPreset.SAM_ST_CDs_MeikyoShisui:
                    DrawHorizontalRadioButton(SAM_ST_Meikyo_Suboption,
                        "在所有內容中使用The Balance邏輯", $"無論內容型別都使用{MeikyoShisui.ActionName()}邏輯。", 0);

                    DrawHorizontalRadioButton(SAM_ST_Meikyo_Suboption,
                        "僅在Boss戰中使用The Balance邏輯", $"僅在Boss戰中使用{MeikyoShisui.ActionName()}邏輯。" +
                                                                         $"\n在非Boss戰中，無論閃數量如何，每分鐘都會使用明鏡止水。", 1);
                    break;

                case CustomComboPreset.SAM_ST_ComboHeals:
                    DrawSliderInt(0, 100, SAM_STSecondWindThreshold,
                        $"{Role.SecondWind.ActionName()} HP percentage threshold");

                    DrawSliderInt(0, 100, SAM_STBloodbathThreshold,
                        $"{Role.Bloodbath.ActionName()} HP percentage threshold");
                    break;

                case CustomComboPreset.SAM_AoE_ComboHeals:
                    DrawSliderInt(0, 100, SAM_AoESecondWindThreshold,
                        $"{Role.SecondWind.ActionName()} HP percentage threshold");

                    DrawSliderInt(0, 100, SAM_AoEBloodbathThreshold,
                        $"{Role.Bloodbath.ActionName()} HP percentage threshold");
                    break;

                case CustomComboPreset.SAM_ST_CDs_Senei:
                    DrawAdditionalBoolChoice(SAM_ST_CDs_Guren,
                        "Guren Option", "Adds Guren to the rotation if Senei is not unlocked.");
                    break;

                case CustomComboPreset.SAM_ST_CDs_OgiNamikiri:
                    DrawAdditionalBoolChoice(SAM_ST_CDs_OgiNamikiri_Movement,
                        "Movement Option", "Adds Ogi Namikiri and Kaeshi: Namikiri when you're not moving.");
                    break;

                case CustomComboPreset.SAM_ST_Shinten:
                    DrawSliderInt(25, 85, SAM_ST_KenkiOvercapAmount,
                        "Set the Kenki overcap amount for ST combos.");

                    DrawSliderInt(0, 100, SAM_ST_ExecuteThreshold,
                        "HP percent threshold to not save Kenki");
                    break;

                case CustomComboPreset.SAM_AoE_Kyuten:
                    DrawSliderInt(25, 85, SAM_AoE_KenkiOvercapAmount,
                        "Set the Kenki overcap amount for AOE combos.");
                    break;

                case CustomComboPreset.SAM_ST_GekkoCombo:
                    DrawAdditionalBoolChoice(SAM_Gekko_KenkiOvercap,
                        "Kenki Overcap Protection", "Spends Kenki when at the set value or above.");

                    if (SAM_Gekko_KenkiOvercap)
                        DrawSliderInt(25, 100, SAM_Gekko_KenkiOvercapAmount,
                            "Kenki Amount", sliderIncrement: SliderIncrements.Fives);
                    break;

                case CustomComboPreset.SAM_ST_KashaCombo:
                    DrawAdditionalBoolChoice(SAM_Kasha_KenkiOvercap,
                        "Kenki Overcap Protection", "Spends Kenki when at the set value or above.");

                    if (SAM_Kasha_KenkiOvercap)
                        DrawSliderInt(25, 100, SAM_Kasha_KenkiOvercapAmount,
                            "Kenki Amount", sliderIncrement: SliderIncrements.Fives);
                    break;

                case CustomComboPreset.SAM_ST_YukikazeCombo:
                    DrawAdditionalBoolChoice(SAM_Yukaze_KenkiOvercap,
                        "Kenki Overcap Protection", "Spends Kenki when at the set value or above.");

                    if (SAM_Yukaze_KenkiOvercap)
                        DrawSliderInt(25, 100, SAM_Yukaze_KenkiOvercapAmount,
                            "Kenki Amount", sliderIncrement: SliderIncrements.Fives);
                    break;

                case CustomComboPreset.SAM_AoE_OkaCombo:
                    DrawAdditionalBoolChoice(SAM_Oka_KenkiOvercap,
                        "Kenki Overcap Protection", "Spends Kenki when at the set value or above.");

                    if (SAM_Oka_KenkiOvercap)
                        DrawSliderInt(25, 100, SAM_Oka_KenkiOvercapAmount,
                            "Kenki Amount", sliderIncrement: SliderIncrements.Fives);
                    break;

                case CustomComboPreset.SAM_AoE_MangetsuCombo:
                    DrawAdditionalBoolChoice(SAM_Mangetsu_KenkiOvercap,
                        "Kenki Overcap Protection", "Spends Kenki when at the set value or above.");

                    if (SAM_Mangetsu_KenkiOvercap)
                        DrawSliderInt(25, 100, SAM_Mangetsu_KenkiOvercapAmount,
                            "Kenki Amount", sliderIncrement: SliderIncrements.Fives);
                    break;

                case CustomComboPreset.SAM_Variant_Cure:
                    DrawSliderInt(1, 100, SAM_VariantCure,
                        "HP% to be at or under", 200);
                    break;
            }
        }
        #region Variables

        public static UserInt
            SAM_Balance_Content = new("SAM_Balance_Content", 1),
            SAM_Opener_PrePullDelay = new("SAM_Opener_PrePullDelay", 13),
            SAM_ST_KenkiOvercapAmount = new("SAM_ST_KenkiOvercapAmount", 65),
            SAM_ST_Higanbana_Suboption = new("SAM_ST_Higanbana_Suboption", 1),
            SAM_ST_Meikyo_Suboption = new("SAM_ST_Meikyo_Suboption", 1),
            SAM_ST_Higanbana_HP_Threshold = new("SAM_ST_Higanbana_HP_Threshold", 0),
            SAM_ST_Higanbana_Refresh = new("SAM_ST_Higanbana_Refresh", 15),
            SAM_ST_ExecuteThreshold = new("SAM_ST_ExecuteThreshold", 1),
            SAM_STSecondWindThreshold = new("SAM_STSecondWindThreshold", 40),
            SAM_STBloodbathThreshold = new("SAM_STBloodbathThreshold", 30),
            SAM_AoE_KenkiOvercapAmount = new("SAM_AoE_KenkiOvercapAmount", 50),
            SAM_AoESecondWindThreshold = new("SAM_AoESecondWindThreshold", 40),
            SAM_AoEBloodbathThreshold = new("SAM_AoEBloodbathThreshold", 30),
            SAM_Gekko_KenkiOvercapAmount = new("SAM_Gekko_KenkiOvercapAmount", 65),
            SAM_Kasha_KenkiOvercapAmount = new("SAM_Kasha_KenkiOvercapAmount", 65),
            SAM_Yukaze_KenkiOvercapAmount = new("SAM_Yukaze_KenkiOvercapAmount", 65),
            SAM_Oka_KenkiOvercapAmount = new("SAM_Oka_KenkiOvercapAmount", 50),
            SAM_Mangetsu_KenkiOvercapAmount = new("SAM_Mangetsu_KenkiOvercapAmount", 50),
            SAM_VariantCure = new("SAM_VariantCure", 50);

        public static UserBool
            SAM_Gekko_KenkiOvercap = new("SAM_Gekko_KenkiOvercap"),
            SAM_Kasha_KenkiOvercap = new("SAM_Kasha_KenkiOvercap"),
            SAM_Yukaze_KenkiOvercap = new("SAM_Yukaze_KenkiOvercap"),
            SAM_ST_CDs_Guren = new("SAM_ST_CDs_Guren"),
            SAM_ST_CDs_OgiNamikiri_Movement = new("SAM_ST_CDs_OgiNamikiri_Movement"),
            SAM_Oka_KenkiOvercap = new("SAM_Oka_KenkiOvercap"),
            SAM_Mangetsu_KenkiOvercap = new("SAM_Mangetsu_KenkiOvercap");

        public static UserBoolArray
            SAM_ST_CDs_IaijutsuOption = new("SAM_ST_CDs_IaijutsuOption");

        #endregion
    }
}
