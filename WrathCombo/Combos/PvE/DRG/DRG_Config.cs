using Dalamud.Bindings.ImGui;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Extensions;
using static WrathCombo.Window.Functions.UserConfig;
namespace WrathCombo.Combos.PvE;

internal partial class DRG
{
    internal static class Config
    {
        internal static void Draw(CustomComboPreset preset)
        {
            switch (preset)
            {
                case CustomComboPreset.DRG_ST_Opener:
                    DrawHorizontalRadioButton(DRG_SelectedOpener,
                        "Standard opener", "Uses Standard opener",
                        0);

                    DrawHorizontalRadioButton(DRG_SelectedOpener,
                        $"{PiercingTalon.ActionName()}起手", $"使用{PiercingTalon.ActionName()}起手",
                        1);

                    ImGui.NewLine();
                    DrawBossOnlyChoice(DRG_Balance_Content);
                    break;

                case CustomComboPreset.DRG_ST_Litany:
                    DrawHorizontalRadioButton(DRG_ST_Litany_SubOption,
                        "All content", $"Uses {BattleLitany.ActionName()} regardless of content.", 0);

                    DrawHorizontalRadioButton(DRG_ST_Litany_SubOption,
                        "Boss encounters Only", $"Only uses {BattleLitany.ActionName()} when in Boss encounters.", 1);
                    break;

                case CustomComboPreset.DRG_ST_Lance:

                    DrawHorizontalRadioButton(DRG_ST_Lance_SubOption,
                        "All content", $"Uses {LanceCharge.ActionName()} regardless of content.", 0);

                    DrawHorizontalRadioButton(DRG_ST_Lance_SubOption,
                        "Boss encounters Only", $"Only uses {LanceCharge.ActionName()} when in Boss encounters.", 1);
                    break;

                case CustomComboPreset.DRG_ST_HighJump:
                    DrawHorizontalMultiChoice(DRG_ST_Jump_Options,
                        "不移動時", $"僅在不移動時使用{Jump.ActionName()}", 2, 0);

                    DrawHorizontalMultiChoice(DRG_ST_Jump_Options,
                        "近戰範圍內", $"僅在近戰範圍內使用{Jump.ActionName()}", 2, 1);
                    break;

                case CustomComboPreset.DRG_ST_Mirage:
                    DrawAdditionalBoolChoice(DRG_ST_DoubleMirage,
                        "蒼天龍血期間爆發幻象衝", "在蒼天龍血效果下將幻象衝新增到循環中");
                    break;

                case CustomComboPreset.DRG_ST_DragonfireDive:
                    DrawHorizontalMultiChoice(DRG_ST_DragonfireDive_Options,
                        "不移動時", $"僅在不移動時使用{DragonfireDive.ActionName()}", 2, 0);

                    DrawHorizontalMultiChoice(DRG_ST_DragonfireDive_Options,
                        "近戰範圍內", $"僅在近戰範圍內使用{DragonfireDive.ActionName()}", 2, 1);
                    break;

                case CustomComboPreset.DRG_ST_Stardiver:
                    DrawHorizontalMultiChoice(DRG_ST_Stardiver_Options,
                        "不移動時", $"僅在不移動時使用{Stardiver.ActionName()}", 2, 0);

                    DrawHorizontalMultiChoice(DRG_ST_Stardiver_Options,
                        "近戰範圍內", $"僅在近戰範圍內使用{Stardiver.ActionName()}", 2, 1);
                    break;

                case CustomComboPreset.DRG_ST_ComboHeals:
                    DrawSliderInt(0, 100, DRG_ST_SecondWind_Threshold,
                        $"{Role.SecondWind.ActionName()} HP percentage threshold");

                    DrawSliderInt(0, 100, DRG_ST_Bloodbath_Threshold,
                        $"{Role.Bloodbath.ActionName()} HP percentage threshold");
                    break;

                case CustomComboPreset.DRG_AoE_Litany:
                    DrawSliderInt(0, 100, DRG_AoE_LitanyHP,
                        $"當目標血量百分比達到或低於此值時停止使用{BattleLitany.ActionName()}（設為0禁用此檢查）");
                    break;

                case CustomComboPreset.DRG_AoE_Lance:
                    DrawSliderInt(0, 100, DRG_AoE_LanceChargeHP,
                        $"當目標血量百分比達到或低於此值時停止使用{LanceCharge.ActionName()}（設為0禁用此檢查）");
                    break;

                case CustomComboPreset.DRG_AoE_HighJump:
                    DrawHorizontalMultiChoice(DRG_AoE_Jump_Options,
                        "不移動時", $"僅在不移動時使用{Jump.ActionName()}", 2, 0);

                    DrawHorizontalMultiChoice(DRG_AoE_Jump_Options,
                        "近戰範圍內", $"僅在近戰範圍內使用{Jump.ActionName()}", 2, 1);
                    break;

                case CustomComboPreset.DRG_AoE_DragonfireDive:
                    DrawHorizontalMultiChoice(DRG_AoE_DragonfireDive_Options,
                        "不移動時", $"僅在不移動時使用{DragonfireDive.ActionName()}", 2, 0);

                    DrawHorizontalMultiChoice(DRG_AoE_DragonfireDive_Options,
                        "近戰範圍內", $"僅在近戰範圍內使用{DragonfireDive.ActionName()}", 2, 1);
                    break;

                case CustomComboPreset.DRG_AoE_Stardiver:
                    DrawHorizontalMultiChoice(DRG_AoE_Stardiver_Options,
                        "不移動時", $"僅在不移動時使用{Stardiver.ActionName()}", 2, 0);

                    DrawHorizontalMultiChoice(DRG_AoE_Stardiver_Options,
                        "近戰範圍內", $"僅在近戰範圍內使用{Stardiver.ActionName()}", 2, 1);
                    break;

                case CustomComboPreset.DRG_AoE_ComboHeals:
                    DrawSliderInt(0, 100, DRG_AoE_SecondWind_Threshold,
                        $"{Role.SecondWind.ActionName()} HP percentage threshold");

                    DrawSliderInt(0, 100, DRG_AoE_Bloodbath_Threshold,
                        $"{Role.Bloodbath.ActionName()} HP percentage threshold");
                    break;

                case CustomComboPreset.DRG_Variant_Cure:
                    DrawSliderInt(1, 100, DRG_Variant_Cure,
                        "HP% to be at or under", 200);
                    break;
            }
        }

        #region Variables

        public static UserInt
            DRG_SelectedOpener = new("DRG_SelectedOpener", 0),
            DRG_Balance_Content = new("DRG_Balance_Content", 1),
            DRG_ST_Litany_SubOption = new("DRG_ST_Litany_SubOption", 1),
            DRG_ST_Lance_SubOption = new("DRG_ST_Lance_SubOption", 1),
            DRG_ST_SecondWind_Threshold = new("DRG_STSecondWindThreshold", 40),
            DRG_ST_Bloodbath_Threshold = new("DRG_STBloodbathThreshold", 30),
            DRG_AoE_LitanyHP = new("DRG_AoE_LitanyHP", 20),
            DRG_AoE_LanceChargeHP = new("DRG_AoE_LanceChargeHP", 20),
            DRG_AoE_SecondWind_Threshold = new("DRG_AoE_SecondWindThreshold", 40),
            DRG_AoE_Bloodbath_Threshold = new("DRG_AoE_BloodbathThreshold", 30),
            DRG_Variant_Cure = new("DRG_Variant_Cure", 50);

        public static UserBool
            DRG_ST_DoubleMirage = new("DRG_ST_DoubleMirage");

        public static UserBoolArray
            DRG_ST_Jump_Options = new("DRG_ST_Jump_Options"),
            DRG_ST_DragonfireDive_Options = new("DRG_ST_DragonfireDive_Options"),
            DRG_ST_Stardiver_Options = new("DRG_ST_Stardiver_Options"),
            DRG_AoE_Jump_Options = new("DRG_AoE_Jump_Options"),
            DRG_AoE_DragonfireDive_Options = new("DRG_AoE_DragonfireDive_Options"),
            DRG_AoE_Stardiver_Options = new("DRG_AoE_Stardiver_Options");

        #endregion
    }
}
