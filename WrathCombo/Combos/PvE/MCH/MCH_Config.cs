using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using ECommons.LanguageHelpers;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Extensions;
using static WrathCombo.Window.Functions.UserConfig;
namespace WrathCombo.Combos.PvE;

internal partial class MCH
{
    internal static class Config
    {
        internal static void Draw(CustomComboPreset preset)
        {
            switch (preset)
            {
                case CustomComboPreset.MCH_ST_Adv_Opener:
                    ImGui.Indent();
                    DrawBossOnlyChoice(MCH_Balance_Content);
                    break;

                case CustomComboPreset.MCH_ST_Adv_WildFire:
                    DrawHorizontalRadioButton(MCH_ST_Adv_Wildfire_SubOption,
                        "All content", $"Uses {Wildfire.ActionName()} regardless of content.", 0);

                    DrawHorizontalRadioButton(MCH_ST_Adv_Wildfire_SubOption,
                        "Bosses Only", $"Only uses {Wildfire.ActionName()} when the targeted enemy is a boss.", 1);

                    DrawStHpCutoff(MCH_ST_WildfireHPOption, MCH_ST_WildfireHPBossOption);
                    break;

                case CustomComboPreset.MCH_ST_Adv_Stabilizer:
                    ImGui.Indent();
                    DrawHorizontalRadioButton(MCH_ST_Adv_BarrelStabiliser_SubOption,
                        "All content", $"Uses {BarrelStabilizer.ActionName()} regardless of content.", 0);

                    DrawHorizontalRadioButton(MCH_ST_Adv_BarrelStabiliser_SubOption,
                        "Boss encounters Only", $"Only uses {BarrelStabilizer.ActionName()} when in Boss encounters.", 1);
                    ImGui.Unindent();

                    DrawStHpCutoff(MCH_ST_BarrelStabilizerHPOption, MCH_ST_BarrelStabilizerHPBossOption);
                    break;

                case CustomComboPreset.MCH_ST_Adv_Hypercharge:
                    DrawStHpCutoff(MCH_ST_HyperchargeHPOption, MCH_ST_HyperchargeHPBossOption);
                    break;

                case CustomComboPreset.MCH_ST_Adv_Stabilizer_FullMetalField:
                    ImGui.Indent();
                    DrawHorizontalRadioButton(MCH_ST_Adv_FullMetalMachinist_SubOption,
                        "All content", $"Uses {FullMetalField.ActionName()} regardless of content.", 0);

                    DrawHorizontalRadioButton(MCH_ST_Adv_FullMetalMachinist_SubOption,
                        "Boss encounters Only", $"Only uses {FullMetalField.ActionName()} when in Boss encounters.", 1);
                    ImGui.Unindent();
                    break;

                case CustomComboPreset.MCH_ST_Adv_TurretQueen:
                    DrawHorizontalRadioButton(MCH_ST_Adv_Turret_SubOption,
                        "Use The Balance Logic in all content", "Uses ?? logic regardless of content.".Loc(AutomatonQueen.ActionName()), 0);

                    DrawHorizontalRadioButton(MCH_ST_Adv_Turret_SubOption,
                        "Use The Balance logic only in Boss encounters", "Only uses ?? logic when in Boss encounters.".Loc(AutomatonQueen.ActionName()), 1);

                    if (MCH_ST_Adv_Turret_SubOption == 1)
                    {
                        DrawSliderInt(50, 100, MCH_ST_TurretUsage,
                            $"Uses {AutomatonQueen.ActionName()} at this battery threshold outside of Boss encounter.");
                    }

                    DrawStHpCutoff(MCH_ST_QueenHPOption, MCH_ST_QueenHPBossOption);
                    break;

                case CustomComboPreset.MCH_ST_Adv_Excavator:
                    DrawHorizontalRadioButton(MCH_ST_Adv_Excavator_SubOption,
                        "All content", $"Uses {Excavator.ActionName()} logic regardless of content.", 0);

                    DrawHorizontalRadioButton(MCH_ST_Adv_Excavator_SubOption,
                        "Boss encounters Only", $"Only uses {Excavator.ActionName()} logic when in Boss encounters.", 1);

                    DrawStToolsHpCutoff();
                    break;

                case CustomComboPreset.MCH_ST_Adv_Drill:
                case CustomComboPreset.MCH_ST_Adv_AirAnchor:
                case CustomComboPreset.MCH_ST_Adv_Chainsaw:
                    DrawStToolsHpCutoff();
                    break;

                case CustomComboPreset.MCH_ST_Adv_GaussRicochet:
                    DrawSliderInt(0, 2, MCH_ST_GaussRicoPool,
                        "Number of Charges of to Save for Manual Use");

                    DrawAdditionalBoolChoice(MCH_GaussRico_NoIdlePull,
                        "不打到未交戰的敵人",
                        "跳彈射擊／將死（92 級後連虹吸彈／雙將也是）是以目標為圓心 5 米的濺射，圈內只要有一隻還沒進戰鬥的怪就會被拉進來。勾選後，這種情況下不放這一發。");
                    break;

                case CustomComboPreset.MCH_ST_Adv_Reassemble:
                    DrawStHpCutoff(MCH_ST_ReassembleHPOption, MCH_ST_ReassembleHPBossOption);

                    DrawSliderInt(0, 1, MCH_ST_ReassemblePool,
                        "Number of Charges to Save for Manual Use");

                    DrawHorizontalMultiChoice(MCH_ST_Reassembled, $"Use on {Excavator.ActionName()}", "", 5, 0);
                    DrawHorizontalMultiChoice(MCH_ST_Reassembled, $"Use on {Chainsaw.ActionName()}", "", 5, 1);
                    DrawHorizontalMultiChoice(MCH_ST_Reassembled, $"Use on {AirAnchor.ActionName()}", "", 5, 2);
                    DrawHorizontalMultiChoice(MCH_ST_Reassembled, $"Use on {Drill.ActionName()}", "", 5, 3);
                    DrawHorizontalMultiChoice(MCH_ST_Reassembled, $"Use on {CleanShot.ActionName()}", "", 5, 4);
                    break;

                case CustomComboPreset.MCH_ST_Adv_QueenOverdrive:
                    DrawSliderInt(0, 100, MCH_ST_QueenOverDrive,
                        "HP% for the target to be at or under");
                    break;

                case CustomComboPreset.MCH_ST_Adv_SecondWind:
                    DrawSliderInt(0, 100, MCH_ST_SecondWindThreshold,
                        $"{Role.SecondWind.ActionName()} HP percentage threshold");
                    break;

                //AoE
                case CustomComboPreset.MCH_AoE_Adv_GaussRicochet:
                    DrawHorizontalMultiChoice(MCH_AoE_GaussOptions,
                        "Use outside of ??".Loc(Hypercharge.ActionName()),
                        "Weaves Gauss Round / Double Check and Ricochet / Checkmate into the gaps of the normal combo while ?? is not active. If neither timing is checked, this advanced option never fires.".Loc(Hypercharge.ActionName()), 2, 0);

                    DrawHorizontalMultiChoice(MCH_AoE_GaussOptions,
                        "Use during ??".Loc(Hypercharge.ActionName()),
                        "Weaves Gauss Round / Double Check and Ricochet / Checkmate into the gaps between ?? / ?? shots. If neither timing is checked, this advanced option never fires.".Loc(Hypercharge.ActionName(), AutoCrossbow.ActionName()), 2, 1);

                    DrawAdditionalBoolChoice(MCH_GaussRico_NoIdlePull,
                        "不打到未交戰的敵人",
                        "跳彈射擊／將死（92 級後連虹吸彈／雙將也是）是以目標為圓心 5 米的濺射，圈內只要有一隻還沒進戰鬥的怪就會被拉進來。勾選後，這種情況下不放這一發。");
                    break;

                case CustomComboPreset.MCH_AoE_Adv_Reassemble:
                    DrawSliderInt(0, 100, MCH_AoE_ReassembleHPThreshold,
                        AoEHpCutoffLabel.Loc(Reassemble.ActionName()));

                    DrawSliderInt(0, 2, MCH_AoE_ReassemblePool,
                        "Number of Charges to Save for Manual Use");

                    DrawHorizontalMultiChoice(MCH_AoE_Reassembled, $"Use on {SpreadShot.ActionName()}/{Scattergun.ActionName()}", "", 4, 0);
                    DrawHorizontalMultiChoice(MCH_AoE_Reassembled, $"Use on {AirAnchor.ActionName()}", "", 4, 1);
                    DrawHorizontalMultiChoice(MCH_AoE_Reassembled, $"Use on {Chainsaw.ActionName()}", "", 4, 2);
                    DrawHorizontalMultiChoice(MCH_AoE_Reassembled, $"Use on {Excavator.ActionName()}", "", 4, 3);
                    break;

                case CustomComboPreset.MCH_AoE_Adv_QueenOverdrive:
                    DrawSliderInt(0, 100, MCH_AoE_QueenOverDrive,
                        "HP% for the target to be at or under");
                    break;

                case CustomComboPreset.MCH_AoE_Adv_SecondWind:
                    DrawSliderInt(0, 100, MCH_AoE_SecondWindThreshold,
                        $"{Role.SecondWind.ActionName()} HP percentage threshold");
                    break;

                case CustomComboPreset.MCH_AoE_Adv_Queen:
                    DrawSliderInt(50, 100, MCH_AoE_TurretUsage,
                        "Battery threshold", sliderIncrement: 5);
                    break;

                case CustomComboPreset.MCH_AoE_Adv_Hypercharge:
                    DrawSliderInt(0, 100, MCH_AoE_HyperchargeHPThreshold,
                        AoEHpCutoffLabel.Loc(Hypercharge.ActionName()));
                    break;

                case CustomComboPreset.MCH_AoE_Adv_FlameThrower:
                    DrawSliderInt(0, 50, MCH_AoE_FlamethrowerHPOption,
                        AoEHpCutoffLabel.Loc(Flamethrower.ActionName()));
                    break;

                //Variant
                case CustomComboPreset.MCH_Variant_Cure:
                    DrawSliderInt(1, 100, MCH_VariantCure,
                        "HP% to be at or under", 200);
                    break;
            }
        }

        private const string AoEHpCutoffLabel =
            "Stop Using ?? When Target HP% is at or Below (Set to 0 to Disable This Check)";

        private static void DrawStHpCutoff(UserInt hpCutoff, UserInt bossScope,
            string label = "Stop using at Enemy HP %. Set to Zero to disable this check.")
        {
            DrawSliderInt(0, 50, hpCutoff, label);

            ImGui.Indent();
            ImGui.TextColored(ImGuiColors.DalamudYellow,
                "Select what kind of enemies the HP check should be applied to:".Loc());

            DrawHorizontalRadioButton(bossScope,
                "Non-Bosses".Loc(), "Only applies the HP check above to non-bosses.".Loc(), 0);

            DrawHorizontalRadioButton(bossScope,
                "All Enemies".Loc(), "Applies the HP check above to all enemies.".Loc(), 1);
            ImGui.Unindent();
        }

        // 四個工具選項共用同一組門檻（上游是一個「工具」選項，我方拆成四個）。
        private static void DrawStToolsHpCutoff() =>
            DrawStHpCutoff(MCH_ST_ToolsHPOption, MCH_ST_ToolsHPBossOption,
                "Stop using ??, ??, ?? and ?? at Enemy HP %. Set to Zero to disable this check. (shared by these four options)"
                    .Loc(Drill.ActionName(), AirAnchor.ActionName(), Chainsaw.ActionName(), Excavator.ActionName()));

        #region Variables

        public static UserInt
            MCH_Balance_Content = new("MCH_Balance_Content", 1),
            MCH_ST_QueenOverDrive = new("MCH_ST_QueenOverDrive", 1),
            MCH_ST_Adv_Excavator_SubOption = new("MCH_ST_Adv_Excavator_SubOption", 1),
            MCH_ST_Adv_Turret_SubOption = new("MCH_ST_Adv_Turret_SubOption", 1),
            MCH_ST_Adv_BarrelStabiliser_SubOption = new("MCH_ST_Adv_BarrelStabiliser_SubOption", 1),
            MCH_ST_Adv_Wildfire_SubOption = new("MCH_ST_Adv_Wildfire_SubOption", 1),
            MCH_ST_Adv_FullMetalMachinist_SubOption = new("MCH_ST_Adv_FullMetalMachinist_SubOption", 1),
            MCH_ST_TurretUsage = new("MCH_ST_TurretUsage", 100),
            MCH_ST_ReassemblePool = new("MCH_ST_ReassemblePool", 0),
            MCH_ST_GaussRicoPool = new("MCH_ST_GaussRicoPool", 0),
            MCH_AoE_QueenOverDrive = new("MCH_AoE_QueenOverDrive", 20),
            MCH_ST_SecondWindThreshold = new("MCH_ST_SecondWindThreshold", 40),
            MCH_AoE_ReassemblePool = new("MCH_AoE_ReassemblePool", 0),
            MCH_AoE_TurretUsage = new("MCH_AoE_TurretUsage", 100),
            MCH_AoE_SecondWindThreshold = new("MCH_AoE_SecondWindThreshold", 40),
            MCH_VariantCure = new("MCH_VariantCure", 50);

        // 目標血量門檻（鍵名同上游）：預設 0＝不檢查，維持加入前的行為；上游預設 25。
        // *HPBossOption：0＝只對非頭目、1＝對全部目標。
        public static UserInt
            MCH_ST_WildfireHPOption = new("MCH_ST_WildfireHPOption", 0),
            MCH_ST_WildfireHPBossOption = new("MCH_ST_WildfireHPBossOption", 0),
            MCH_ST_BarrelStabilizerHPOption = new("MCH_ST_BarrelStabilizerHPOption", 0),
            MCH_ST_BarrelStabilizerHPBossOption = new("MCH_ST_BarrelStabilizerHPBossOption", 0),
            MCH_ST_HyperchargeHPOption = new("MCH_ST_HyperchargeHPOption", 0),
            MCH_ST_HyperchargeHPBossOption = new("MCH_ST_HyperchargeHPBossOption", 0),
            MCH_ST_QueenHPOption = new("MCH_ST_QueenHPOption", 0),
            MCH_ST_QueenHPBossOption = new("MCH_ST_QueenHPBossOption", 0),
            MCH_ST_ReassembleHPOption = new("MCH_ST_ReassembleHPOption", 0),
            MCH_ST_ReassembleHPBossOption = new("MCH_ST_ReassembleHPBossOption", 0),
            MCH_ST_ToolsHPOption = new("MCH_ST_ToolsHPOption", 0),
            MCH_ST_ToolsHPBossOption = new("MCH_ST_ToolsHPBossOption", 0),
            MCH_AoE_HyperchargeHPThreshold = new("MCH_AoE_HyperchargeHPThreshold", 0),
            MCH_AoE_ReassembleHPThreshold = new("MCH_AoE_ReassembleHPThreshold", 0),
            MCH_AoE_FlamethrowerHPOption = new("MCH_AoE_FlamethrowerHPOption", 0);

        // 預設 true：UserBool 的建構式在鍵不存在時會把預設值寫進設定字典，
        // 所以「既有使用者」與「全新安裝」都會拿到 true（與 EzConfig 的行為不同）。
        public static UserBool
            MCH_GaussRico_NoIdlePull = new("MCH_GaussRico_NoIdlePull", true);

        public static UserBoolArray
            MCH_ST_Reassembled = new("MCH_ST_Reassembled"),
            MCH_AoE_Reassembled = new("MCH_AoE_Reassembled"),
            MCH_AoE_GaussOptions = new("MCH_AoE_GaussOptions", [false, true]);

        #endregion
    }
}
