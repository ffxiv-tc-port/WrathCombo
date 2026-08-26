using Dalamud.Interface.Colors;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Data;
using WrathCombo.Extensions;
using WrathCombo.Window.Functions;
using BossAvoidance = WrathCombo.Combos.PvE.All.Enums.BossAvoidance;
using PartyRequirement = WrathCombo.Combos.PvE.All.Enums.PartyRequirement;

namespace WrathCombo.Combos.PvE;

internal partial class WAR
{
    internal static class Config
    {
        public static UserInt
            WAR_Infuriate_Charges = new("WAR_Infuriate_Charges", 0),
            WAR_Infuriate_Range = new("WAR_Infuriate_Range", 0),
            WAR_SurgingRefreshRange = new("WAR_SurgingRefreshRange", 10),
            WAR_EyePath_Refresh = new("WAR_EyePath", 10),
            WAR_ST_Infuriate_Charges = new("WAR_ST_Infuriate_Charges", 0),
            WAR_ST_Infuriate_Gauge = new("WAR_ST_Infuriate_Gauge", 40),
            WAR_ST_FellCleave_Gauge = new("WAR_ST_FellCleave_Gauge", 90),
            WAR_ST_FellCleave_BurstPooling = new("WAR_ST_FellCleave_BurstPooling", 0),
            WAR_ST_Onslaught_Charges = new("WAR_ST_Onslaught_Charges", 0),
            WAR_ST_Onslaught_Movement = new("WAR_ST_Onslaught_Movement", 0),
            WAR_ST_PrimalRend_Movement = new("WAR_ST_PrimalRend_Movement", 0),
            WAR_ST_PrimalRend_EarlyLate = new("WAR_ST_PrimalRend_EarlyLate", 0),
            WAR_ST_Bloodwhetting_Health = new("WAR_ST_BloodwhettingOption", 90),
            WAR_ST_Bloodwhetting_SubOption = new("WAR_ST_Bloodwhetting_SubOpt", 0),
            WAR_ST_Equilibrium_Health = new("WAR_ST_EquilibriumOption", 50),
            WAR_ST_Equilibrium_SubOption = new("WAR_ST_Equilibrium_SubOpt", 0),
            WAR_ST_Rampart_Health = new("WAR_ST_Rampart_Health", 80),
            WAR_ST_Rampart_SubOption = new("WAR_ST_Rampart_SubOption", 0),
            WAR_ST_Thrill_Health = new("WAR_ST_Thrill_Health", 70),
            WAR_ST_Thrill_SubOption = new("WAR_ST_Thrill_SubOpt", 0),
            WAR_ST_Vengeance_Health = new("WAR_ST_Vengeance_Health", 60),
            WAR_ST_Vengeance_SubOption = new("WAR_ST_Vengeance_SubOpt", 0),
            WAR_ST_Holmgang_Health = new("WAR_ST_Holmgang_Health", 30),
            WAR_ST_Holmgang_SubOption = new("WAR_ST_Holmgang_SubOpt", 0),
            WAR_ST_Reprisal_Health = new("WAR_ST_Reprisal_Health", 80),
            WAR_ST_Reprisal_SubOption = new("WAR_ST_Reprisal_SubOpt", 0),
            WAR_ST_ArmsLength_Health = new("WAR_ST_ArmsLength_Health", 80),
            WAR_ST_MitsOptions = new("WAR_ST_MitsOptions", 0),
            WAR_ST_IRStop = new("WAR_ST_IRStop", 0),
            WAR_AoE_Infuriate_Charges = new("WAR_AoE_Infuriate_Charges", 0),
            WAR_AoE_Infuriate_Gauge = new("WAR_AoE_Infuriate_Gauge", 40),
            WAR_AoE_Decimate_Gauge = new("WAR_AoE_Decimate_Gauge", 90),
            WAR_AoE_Decimate_BurstPooling = new("WAR_AoE_Decimate_BurstPooling", 0),
            WAR_AoE_Onslaught_Charges = new("WAR_AoE_Onslaught_Charges", 0),
            WAR_AoE_Onslaught_Movement = new("WAR_AoE_Onslaught_Movement", 0),
            WAR_AoE_PrimalRend_Movement = new("WAR_AoE_PrimalRend_Movement", 0),
            WAR_AoE_PrimalRend_EarlyLate = new("WAR_AoE_PrimalRend_EarlyLate", 0),
            WAR_AoE_OrogenyUpheaval = new("WAR_AoE_OrogenyUpheaval", 0),
            WAR_AoE_Bloodwhetting_Health = new("WAR_AoE_BloodwhettingOption", 90),
            WAR_AoE_Bloodwhetting_SubOption = new("WAR_AoE_Bloodwhetting_SubOpt", 0),
            WAR_AoE_Equilibrium_Health = new("WAR_AoE_EquilibriumOption", 50),
            WAR_AoE_Equilibrium_SubOption = new("WAR_AoE_Equilibrium_SubOpt", 0),
            WAR_AoE_Rampart_Health = new("WAR_AoE_Rampart_Health", 80),
            WAR_AoE_Rampart_SubOption = new("WAR_AoE_Rampart_SubOpt", 0),
            WAR_AoE_Thrill_Health = new("WAR_AoE_Thrill_Health", 80),
            WAR_AoE_Thrill_SubOption = new("WAR_AoE_Thrill_SubOpt", 0),
            WAR_AoE_Vengeance_Health = new("WAR_AoE_Vengeance_Health", 60),
            WAR_AoE_Vengeance_SubOption = new("WAR_AoE_Vengeance_SubOpt", 0),
            WAR_AoE_Holmgang_Health = new("WAR_AoE_Holmgang_Health", 30),
            WAR_AoE_Holmgang_SubOption = new("WAR_AoE_Holmgang_SubOpt", 0),
            WAR_AoE_Reprisal_Health = new("WAR_AoE_Reprisal_Health", 80),
            WAR_AoE_Reprisal_SubOption = new("WAR_AoE_Reprisal_SubOpt", 0),
            WAR_AoE_ArmsLength_Health = new("WAR_AoE_ArmsLength_Health", 80),
            WAR_AoE_MitsOptions = new("WAR_AoE_MitsOptions", 0),
            WAR_AoE_IRStop = new("WAR_AoE_IRStop", 0),
            WAR_VariantCure = new("WAR_VariantCure"),
            WAR_BalanceOpener_Content = new("WAR_BalanceOpener_Content", 1),
            WAR_FC_IRStop = new("WAR_FC_IRStop", 0),
            WAR_FC_Infuriate_Charges = new("WAR_FC_Infuriate_Charges", 0),
            WAR_FC_Infuriate_Gauge = new("WAR_FC_Infuriate_Gauge", 40),
            WAR_FC_Onslaught_Charges = new("WAR_FC_Onslaught_Charges", 0),
            WAR_FC_Onslaught_Movement = new("WAR_FC_Onslaught_Movement", 0),
            WAR_FC_PrimalRend_Movement = new("WAR_FC_PrimalRend_Movement", 0),
            WAR_FC_PrimalRend_EarlyLate = new("WAR_FC_PrimalRend_EarlyLate", 0),
            WAR_Mit_Holmgang_Health = new("WAR_Mit_Holmgang_Health", 30),
            WAR_Mit_Bloodwhetting_Health = new("WAR_Mit_Bloodwhetting_Health", 70),
            WAR_Mit_Equilibrium_Health = new("WAR_Mit_Equilibrium_Health", 45),
            WAR_Mit_ThrillOfBattle_Health = new("WAR_Mit_ThrillOfBattle_Health", 60),
            WAR_Mit_Rampart_Health = new("WAR_Mit_Rampart_Health", 65),
            WAR_Mit_ShakeItOff_PartyRequirement = new("WAR_Mit_ShakeItOff_PartyRequirement", (int)PartyRequirement.Yes),
            WAR_Mit_ArmsLength_Boss = new("WAR_Mit_ArmsLength_Boss", (int)BossAvoidance.On),
            WAR_Mit_ArmsLength_EnemyCount = new("WAR_Mit_ArmsLength_EnemyCount", 0),
            WAR_Mit_Vengeance_Health = new("WAR_Mit_Vengeance_Health", 50),
            WAR_Bozja_LostCure_Health = new("WAR_Bozja_LostCure_Health", 50),
            WAR_Bozja_LostCure2_Health = new("WAR_Bozja_LostCure2_Health", 50),
            WAR_Bozja_LostCure3_Health = new("WAR_Bozja_LostCure3_Health", 50),
            WAR_Bozja_LostCure4_Health = new("WAR_Bozja_LostCure4_Health", 50),
            WAR_Bozja_LostAethershield_Health = new("WAR_Bozja_LostAethershield_Health", 70),
            WAR_Bozja_LostReraise_Health = new("WAR_Bozja_LostReraise_Health", 10);

        public static UserFloat
            WAR_ST_Onslaught_Distance = new("WAR_ST_Ons_Distance", 3.0f),
            WAR_ST_PrimalRend_Distance = new("WAR_ST_PR_Distance", 3.0f),
            WAR_AoE_Onslaught_Distance = new("WAR_AoE_Ons_Distance", 3.0f),
            WAR_AoE_PrimalRend_Distance = new("WAR_AoE_PR_Distance", 3.0f),
            WAR_FC_Onslaught_Distance = new("WAR_FC_Ons_Distance", 3.0f),
            WAR_FC_PrimalRend_Distance = new("WAR_FC_PR_Distance", 3.0f),
            WAR_ST_Onslaught_TimeStill = new("WAR_ST_Onslaught_TimeStill", 0),
            WAR_ST_PrimalRend_TimeStill = new("WAR_ST_PrimalRend_TimeStill", 0),
            WAR_AoE_Onslaught_TimeStill = new("WAR_AoE_Onslaught_TimeStill", 0),
            WAR_AoE_PrimalRend_TimeStill = new("WAR_AoE_PrimalRend_TimeStill", 0),
            WAR_FC_Onslaught_TimeStill = new("WAR_FC_Onslaught_TimeStill", 0),
            WAR_FC_PrimalRend_TimeStill = new("WAR_FC_PrimalRend_TimeStill", 0);

        public static UserIntArray
            WAR_Mit_Priorities = new("WAR_Mit_Priorities");

        public static UserBoolArray
            WAR_Mit_Holmgang_Difficulty = new("WAR_Mit_Holmgang_Difficulty", [true, false]);

        public static readonly ContentCheck.ListSet WAR_Mit_Holmgang_DifficultyListSet = ContentCheck.ListSet.Halved;

        private const int NumMitigationOptions = 8;

        internal static void Draw(CustomComboPreset preset)
        {
            switch (preset)
            {
                #region Single-Target
                case CustomComboPreset.WAR_ST_BalanceOpener:
                    UserConfig.DrawBossOnlyChoice(WAR_BalanceOpener_Content);
                    break;

                case CustomComboPreset.WAR_ST_StormsEye:
                    UserConfig.DrawSliderInt(0, 30, WAR_SurgingRefreshRange,
                        $" 重新整理{Buffs.SurgingTempest.StatusName()}增益前的剩餘秒數：");
                    break;

                case CustomComboPreset.WAR_ST_InnerRelease:
                    UserConfig.DrawSliderInt(0, 75, WAR_ST_IRStop,
                        "目標血量低於設定值時停止使用。\n如需禁用此功能，請設為0");
                    break;

                case CustomComboPreset.WAR_ST_Onslaught:
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Onslaught_Movement,
                            "僅在站立時", "僅在站立時使用猛攻", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Onslaught_Movement,
                            "任意移動", "無論移動狀態均可使用猛攻。\n注意：這可能導致你死亡", 1);
                    ImGui.Spacing();
                    if (WAR_ST_Onslaught_Movement == 0)
                    {
                        ImGui.SetCursorPosX(48);
                        UserConfig.DrawSliderFloat(0, 3, WAR_ST_Onslaught_TimeStill,
                            "站立檢測延遲（秒）：", decimals: 1);
                    }
                    ImGui.SetCursorPosX(48);
                    UserConfig.DrawSliderInt(0, 2, WAR_ST_Onslaught_Charges,
                        " How many charges to keep ready?\n (0 = Use All)");
                    ImGui.SetCursorPosX(48);
                    UserConfig.DrawSliderFloat(1, 20, WAR_ST_Onslaught_Distance,
                        "與目標距離小於等於此值時使用：", decimals: 1);
                    break;

                case CustomComboPreset.WAR_ST_Infuriate:
                    UserConfig.DrawSliderInt(0, 2, WAR_ST_Infuriate_Charges,
                        " How many charges to keep ready?\n (0 = Use All)");
                    UserConfig.DrawSliderInt(0, 50, WAR_ST_Infuriate_Gauge,
                        "獸魂低於等於此值時使用：");
                    break;

                case CustomComboPreset.WAR_ST_FellCleave:
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_FellCleave_BurstPooling,
                        "Burst Pooling", "允許在爆發期間額外使用裂石飛環\n注意：爆發期間會無視下方的獸魂量條", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_FellCleave_BurstPooling,
                        "無爆發池", "禁止在爆發期間額外使用裂石飛環\n注意：完全遵循下方獸魂量條設定", 1);
                    ImGui.Spacing();
                    UserConfig.DrawSliderInt(50, 100, WAR_ST_FellCleave_Gauge,
                        "消耗所需最低獸魂：");
                    break;

                case CustomComboPreset.WAR_ST_PrimalRend:
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_PrimalRend_EarlyLate,
                        "儘早", "儘快使用蠻荒崩裂", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_PrimalRend_EarlyLate,
                        "Late", "在消耗完所有原初的解放層數後再使用蠻荒崩裂", 1);
                    ImGui.NewLine();
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_PrimalRend_Movement,
                        "僅在站立時", "僅在站立時使用蠻荒崩裂", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_PrimalRend_Movement,
                        "任意移動", "無論移動狀態均可使用蠻荒崩裂。\n注意：這可能導致你死亡", 1);
                    ImGui.Spacing();
                    if (WAR_ST_PrimalRend_Movement == 0)
                    {
                        ImGui.SetCursorPosX(48);
                        UserConfig.DrawSliderFloat(0, 3, WAR_ST_PrimalRend_TimeStill,
                            "站立檢測延遲（秒）：", decimals: 1);
                    }
                    ImGui.SetCursorPosX(48);
                    UserConfig.DrawSliderFloat(1, 20, WAR_ST_PrimalRend_Distance,
                        "與目標距離小於等於此值時使用：", decimals: 1);
                    break;
                #endregion

                #region AoE
                case CustomComboPreset.WAR_AoE_Decimate:
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Decimate_BurstPooling,
                        "Burst Pooling", "允許在爆發期間額外使用地毀人亡\n注意：爆發期間會無視下方獸魂量條", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Decimate_BurstPooling,
                        "無爆發池", "禁止在爆發期間額外使用地毀人亡\n注意：完全遵循下方獸魂量條設定", 1);
                    ImGui.Spacing();
                    UserConfig.DrawSliderInt(50, 100, WAR_AoE_Decimate_Gauge,
                        "Minimum gauge required to spend:");
                    break;

                case CustomComboPreset.WAR_AoE_InnerRelease:
                    UserConfig.DrawSliderInt(0, 75, WAR_AoE_IRStop,
                        "目標血量低於設定值時停止使用。\n如需禁用此功能，請設為0");
                    break;


                case CustomComboPreset.WAR_AoE_Infuriate:
                    UserConfig.DrawSliderInt(0, 2, WAR_AoE_Infuriate_Charges,
                        " How many charges to keep ready?\n (0 = Use All)");
                    UserConfig.DrawSliderInt(0, 50, WAR_AoE_Infuriate_Gauge,
                        "Use when gauge is under or equal to");
                    break;

                case CustomComboPreset.WAR_AoE_Onslaught:
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Onslaught_Movement,
                            "僅在站立時", "僅在站立時使用猛攻", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Onslaught_Movement,
                            "任意移動", "無論移動狀態均可使用猛攻。\n注意：這可能導致你死亡", 1);
                    ImGui.Spacing();
                    if (WAR_AoE_Onslaught_Movement == 0) 
                    {
                        ImGui.SetCursorPosX(48);
                        UserConfig.DrawSliderFloat(0, 3, WAR_AoE_Onslaught_TimeStill,
                            "站立檢測延遲（秒）：", decimals: 1);
                    }
                    UserConfig.DrawSliderInt(0, 2, WAR_AoE_Onslaught_Charges,
                        " How many charges to keep ready?\n (0 = Use All)");
                    ImGui.SetCursorPosX(48);
                    UserConfig.DrawSliderFloat(1, 20, WAR_AoE_Onslaught_Distance,
                        "與目標距離小於等於此值時使用：", decimals: 1);
                    break;

                case CustomComboPreset.WAR_AoE_PrimalRend:
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_PrimalRend_EarlyLate,
                        "儘早", "儘快使用蠻荒崩裂", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_PrimalRend_EarlyLate,
                        "Late", "在消耗完所有蠻荒崩裂層數後再使用蠻荒崩裂", 1);
                    ImGui.NewLine();
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_PrimalRend_Movement,
                        "僅在站立時", "僅在站立時使用蠻荒崩裂", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_PrimalRend_Movement,
                        "任意移動", "無論移動狀態均可使用蠻荒崩裂。\n注意：這可能導致你死亡", 1);
                    ImGui.Spacing();
                    if (WAR_AoE_PrimalRend_Movement == 0)
                    {
                        ImGui.SetCursorPosX(48);
                        UserConfig.DrawSliderFloat(0, 3, WAR_AoE_PrimalRend_TimeStill,
                            "站立檢測延遲（秒）：", decimals: 1);
                    }
                    ImGui.SetCursorPosX(48);
                    UserConfig.DrawSliderFloat(1, 20, WAR_AoE_PrimalRend_Distance,
                        "與目標距離小於等於此值時使用：", decimals: 1);
                    break;

                case CustomComboPreset.WAR_AoE_Orogeny:
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_OrogenyUpheaval,
                        "包含動亂", "若山崩不可用則在AOE循環中啟用動亂", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_OrogenyUpheaval,
                        "不包含動亂", "在AOE循環中禁用動亂", 1);
                    break;
                #endregion

                #region Mitigations
                case CustomComboPreset.WAR_ST_Bloodwhetting:
                    UserConfig.DrawSliderInt(1, 100, WAR_ST_Bloodwhetting_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Bloodwhetting_SubOption,
                        "All Enemies", $"無論目標型別均使用{Bloodwhetting.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Bloodwhetting_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Bloodwhetting.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_AoE_Bloodwhetting:
                    UserConfig.DrawSliderInt(1, 100, WAR_AoE_Bloodwhetting_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Bloodwhetting_SubOption,
                        "All Enemies", $"無論目標型別均使用{Bloodwhetting.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Bloodwhetting_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Bloodwhetting.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_ST_Equilibrium:
                    UserConfig.DrawSliderInt(1, 100, WAR_ST_Equilibrium_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Equilibrium_SubOption,
                        "All Enemies", $"無論目標型別均使用{Equilibrium.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Equilibrium_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Equilibrium.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_AoE_Equilibrium:
                    UserConfig.DrawSliderInt(1, 100, WAR_AoE_Equilibrium_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Equilibrium_SubOption,
                        "All Enemies", $"無論目標型別均使用{Equilibrium.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Equilibrium_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Equilibrium.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_ST_Rampart:
                    UserConfig.DrawSliderInt(1, 100, WAR_ST_Rampart_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Rampart_SubOption,
                        "All Enemies", $"無論目標型別均使用{Role.Rampart.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Rampart_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Role.Rampart.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_AoE_Rampart:
                    UserConfig.DrawSliderInt(1, 100, WAR_AoE_Rampart_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Rampart_SubOption,
                        "All Enemies", $"無論目標型別均使用{Role.Rampart.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Rampart_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Role.Rampart.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_ST_Thrill:
                    UserConfig.DrawSliderInt(1, 100, WAR_ST_Thrill_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Thrill_SubOption,
                        "All Enemies", $"無論目標型別均使用{ThrillOfBattle.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Thrill_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{ThrillOfBattle.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_AoE_Thrill:
                    UserConfig.DrawSliderInt(1, 100, WAR_AoE_Thrill_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Thrill_SubOption,
                        "All Enemies", $"無論目標型別均使用{ThrillOfBattle.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Thrill_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{ThrillOfBattle.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_ST_Vengeance:
                    UserConfig.DrawSliderInt(1, 100, WAR_ST_Vengeance_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Vengeance_SubOption,
                        "All Enemies", $"無論目標型別均使用{Vengeance.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Vengeance_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Vengeance.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_AoE_Vengeance:
                    UserConfig.DrawSliderInt(1, 100, WAR_AoE_Vengeance_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Vengeance_SubOption,
                        "All Enemies", $"無論目標型別均使用{Vengeance.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Vengeance_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Vengeance.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_ST_Holmgang:
                    UserConfig.DrawSliderInt(1, 100, WAR_ST_Holmgang_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Holmgang_SubOption,
                        "All Enemies", $"無論目標型別均使用{Holmgang.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Holmgang_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Holmgang.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_AoE_Holmgang:
                    UserConfig.DrawSliderInt(1, 100, WAR_AoE_Holmgang_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Holmgang_SubOption,
                        "All Enemies", $"無論目標型別均使用{Holmgang.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Holmgang_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Holmgang.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_ST_Reprisal:
                    UserConfig.DrawSliderInt(1, 100, WAR_ST_Reprisal_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Reprisal_SubOption,
                        "All Enemies", $"無論目標型別均使用{Role.Reprisal.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_Reprisal_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Role.Reprisal.ActionName()}", 1);
                    break;

                case CustomComboPreset.WAR_AoE_Reprisal:
                    UserConfig.DrawSliderInt(1, 100, WAR_AoE_Reprisal_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Reprisal_SubOption,
                        "All Enemies", $"無論目標型別均使用{Role.Reprisal.ActionName()}", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_Reprisal_SubOption,
                        "Bosses Only", $"僅在目標為Boss時使用{Role.Reprisal.ActionName()}", 1);
                    break;

                #region One-Button Mitigation

                case CustomComboPreset.WAR_Mit_Holmgang_Max:
                    UserConfig.DrawDifficultyMultiChoice(WAR_Mit_Holmgang_Difficulty, WAR_Mit_Holmgang_DifficultyListSet,
                        "Select what difficulties Holmgang should be used in:");

                    UserConfig.DrawSliderInt(1, 100, WAR_Mit_Holmgang_Health,
                        "Player HP% to be \nless than or equal to:", 200, SliderIncrements.Fives);
                    break;

                case CustomComboPreset.WAR_Mit_Bloodwhetting:
                    UserConfig.DrawSliderInt(1, 100, WAR_Mit_Bloodwhetting_Health,
                        "HP% to use at or below", sliderIncrement: SliderIncrements.Ones);

                    UserConfig.DrawPriorityInput(WAR_Mit_Priorities, NumMitigationOptions, 0,
                        "Bloodwhetting Priority:");
                    break;

                case CustomComboPreset.WAR_Mit_Equilibrium:
                    UserConfig.DrawSliderInt(1, 100, WAR_Mit_Equilibrium_Health,
                        "HP% to use at or below", sliderIncrement: SliderIncrements.Ones);

                    UserConfig.DrawPriorityInput(WAR_Mit_Priorities, NumMitigationOptions, 1,
                        "Equilibrium Priority:");
                    break;

                case CustomComboPreset.WAR_Mit_Reprisal:
                    UserConfig.DrawPriorityInput(WAR_Mit_Priorities, NumMitigationOptions, 2,
                        "Reprisal Priority:");
                    break;

                case CustomComboPreset.WAR_Mit_ThrillOfBattle:
                    UserConfig.DrawSliderInt(1, 100, WAR_Mit_ThrillOfBattle_Health,
                        "HP% to use at or below (100 = Disable check)", sliderIncrement: SliderIncrements.Ones);

                    UserConfig.DrawPriorityInput(WAR_Mit_Priorities, NumMitigationOptions, 3,
                        "Thrill Of Battle Priority:");
                    break;

                case CustomComboPreset.WAR_Mit_Rampart:
                    UserConfig.DrawSliderInt(1, 100, WAR_Mit_Rampart_Health,
                        "HP% to use at or below (100 = Disable check)", sliderIncrement: SliderIncrements.Ones);

                    UserConfig.DrawPriorityInput(WAR_Mit_Priorities, NumMitigationOptions, 4,
                        "Rampart Priority:");
                    break;

                case CustomComboPreset.WAR_Mit_ShakeItOff:
                    ImGui.Indent();
                    UserConfig.DrawHorizontalRadioButton(WAR_Mit_ShakeItOff_PartyRequirement,
                        "Require party", "Will not use Shake It Off unless there are 2 or more party members.",
                        outputValue: (int)PartyRequirement.Yes);
                    UserConfig.DrawHorizontalRadioButton(WAR_Mit_ShakeItOff_PartyRequirement,
                        "Use Always", "Will not require a party for Shake It Off.",
                        outputValue: (int)PartyRequirement.No);
                    ImGui.Unindent();

                    ImGui.NewLine();
                    UserConfig.DrawPriorityInput(WAR_Mit_Priorities, NumMitigationOptions, 5,
                        "Shake It Off Priority:");
                    break;

                case CustomComboPreset.WAR_Mit_ArmsLength:
                    ImGui.Indent();
                    UserConfig.DrawHorizontalRadioButton(WAR_Mit_ArmsLength_Boss,
                        "All Enemies", "Will use Arm's Length regardless of the type of enemy.",
                        outputValue: (int)BossAvoidance.Off, itemWidth: 125f);
                    UserConfig.DrawHorizontalRadioButton(WAR_Mit_ArmsLength_Boss,
                        "Avoid Bosses", "Will try not to use Arm's Length when in a boss fight.",
                        outputValue: (int)BossAvoidance.On, itemWidth: 125f);
                    ImGui.Unindent();
                    ImGui.NewLine();
                    UserConfig.DrawSliderInt(0, 3, WAR_Mit_ArmsLength_EnemyCount,
                        "How many enemies should be nearby? (0 = No Requirement)");
                    UserConfig.DrawPriorityInput(WAR_Mit_Priorities, NumMitigationOptions, 6, "Arm's Length Priority:");
                    break;

                case CustomComboPreset.WAR_Mit_Vengeance:
                    UserConfig.DrawSliderInt(1, 100, WAR_Mit_Vengeance_Health,
                        "HP% to use at or below (100 = Disable check)",
                        sliderIncrement: SliderIncrements.Ones);
                    UserConfig.DrawPriorityInput(WAR_Mit_Priorities, NumMitigationOptions, 7, "Vengeance Priority:");
                    break;
                #endregion

                #endregion

                #region Other
                case CustomComboPreset.WAR_FC_InnerRelease:
                    UserConfig.DrawSliderInt(0, 75, WAR_FC_IRStop,
                        "目標血量低於設定值時停止使用。\n如需禁用此功能，請設為0");
                    break;

                case CustomComboPreset.WAR_FC_Onslaught:
                    UserConfig.DrawHorizontalRadioButton(WAR_FC_Onslaught_Movement,
                        "僅在站立時", "僅在站立時使用猛攻", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_FC_Onslaught_Movement,
                        "任意移動", "無論移動狀態均可使用猛攻。\n注意：這可能導致你死亡", 1);
                    ImGui.Spacing();
                    if (WAR_FC_Onslaught_Movement == 0)
                    {
                        ImGui.SetCursorPosX(48);
                        UserConfig.DrawSliderFloat(0, 3, WAR_FC_Onslaught_TimeStill,
                            "站立檢測延遲（秒）：", decimals: 1);
                    }
                    UserConfig.DrawSliderInt(0, 2, WAR_FC_Onslaught_Charges,
                        " How many charges to keep ready?\n (0 = Use All)");
                    ImGui.SetCursorPosX(48);
                    UserConfig.DrawSliderFloat(1, 20, WAR_FC_Onslaught_Distance,
                        "與目標距離小於等於此值時使用：", decimals: 1);
                    break;

                case CustomComboPreset.WAR_FC_Infuriate:
                    UserConfig.DrawSliderInt(0, 2, WAR_FC_Infuriate_Charges,
                        " How many charges to keep ready?\n (0 = Use All)");
                    UserConfig.DrawSliderInt(0, 50, WAR_FC_Infuriate_Gauge,
                        "獸魂低於等於此值時使用：");
                    break;

                case CustomComboPreset.WAR_FC_PrimalRend:
                    UserConfig.DrawHorizontalRadioButton(WAR_FC_PrimalRend_EarlyLate,
                        "儘早", "儘快使用原初的解放", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_FC_PrimalRend_EarlyLate,
                        "Late", "在消耗完所有原初的解放層數後再使用原初的解放", 1);
                    ImGui.NewLine();
                    UserConfig.DrawHorizontalRadioButton(WAR_FC_PrimalRend_Movement,
                        "僅在站立時", "僅在站立時使用原初的解放", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_FC_PrimalRend_Movement,
                        "任意移動", "無論移動狀態均可使用原初的解放。\n注意：這可能導致你死亡", 1);
                    ImGui.Spacing();
                    if (WAR_FC_PrimalRend_Movement == 0)
                    {
                        ImGui.SetCursorPosX(48);
                        UserConfig.DrawSliderFloat(0, 3, WAR_FC_PrimalRend_TimeStill,
                            "站立檢測延遲（秒）：", decimals: 1);
                    }
                    ImGui.SetCursorPosX(48);
                    UserConfig.DrawSliderFloat(1, 20, WAR_FC_PrimalRend_Distance,
                        "與目標距離小於等於此值時使用：", decimals: 1);
                    break;

                case CustomComboPreset.WAR_ST_Simple:
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_MitsOptions,
                        "包含減傷", "在簡易模式下啟用減傷技能。", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_ST_MitsOptions,
                        "不包含減傷", "在簡易模式下禁用減傷技能。", 1);
                    break;

                case CustomComboPreset.WAR_AoE_Simple:
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_MitsOptions,
                        "包含減傷", "在簡易模式下啟用減傷技能。", 0);
                    UserConfig.DrawHorizontalRadioButton(WAR_AoE_MitsOptions,
                        "不包含減傷", "在簡易模式下禁用減傷技能。", 1);
                    break;

                case CustomComboPreset.WAR_InfuriateFellCleave:
                    UserConfig.DrawSliderInt(0, 2, WAR_Infuriate_Charges,
                        " How many charges to keep ready?\n (0 = Use All)");
                    UserConfig.DrawSliderInt(0, 50, WAR_Infuriate_Range,
                        "獸魂低於等於此值時使用：");
                    break;

                case CustomComboPreset.WAR_EyePath:
                    UserConfig.DrawSliderInt(0, 30, WAR_EyePath_Refresh,
                        $" 重新整理{Buffs.SurgingTempest.StatusName()}增益前的剩餘秒數：");
                    break;

                case CustomComboPreset.WAR_Variant_Cure:
                    UserConfig.DrawSliderInt(1, 100, WAR_VariantCure,
                        " Player HP% to be less than or equal to:", 200);
                    break;

                case CustomComboPreset.WAR_RawIntuition_Targeting_TT:
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudGrey,
                        "注意：如果你是副T，並且希望將原初的血氣用於自己，建議透過一鍵減傷功能或你的循環中的減傷選項來實現。\n" +
                        "你也可以在隊伍中滑鼠懸停自己來使用原初的血氣或原初的直覺。\n" +
                        "如果你不這樣做，原初的勇猛會替換該連擊，並施放到主T身上。\n" +
                        "如果你不使用這些功能來進行個人減傷，建議不要啟用此選項。");
                    ImGui.Unindent();
                    break;
                    #endregion
            }
        }
    }
}
