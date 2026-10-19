using Dalamud.Interface.Colors;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Window.Functions;
namespace WrathCombo.Combos.PvE;

internal partial class OccultCrescent
{
    internal static class Config
    {
        public static UserInt
            Phantom_Freelancer_Resuscitation_Health = new("Phantom_Freelancer_Resuscitation_Health", 50),
            Phantom_Geomancer_Sunbath_Health = new("Phantom_Geomancer_Sunbath_Health", 50),
            Phantom_Knight_PhantomGuard_Health = new("Phantom_Knight_PhantomGuard_Health", 50),
            Phantom_Knight_Pray_Health = new("Phantom_Knight_Pray_Health", 50),
            Phantom_Knight_OccultHeal_Health = new("Phantom_Knight_OccultHeal_Health", 50),
            Phantom_Knight_Pledge_Health = new("Phantom_Knight_Pledge_Health", 50),
            Phantom_Bard_MightyMarch_Health = new("Phantom_Bard_MightyMarch_Health", 50),
            Phantom_Monk_OccultChakra_Health = new("Phantom_Monk_OccultChakra_Health", 29),
            Phantom_Chemist_OccultPotion_Health = new("Phantom_Chemist_OccultPotion_Health", 50),
            Phantom_Chemist_OccultEther_MP = new("Phantom_Chemist_OccultEther_MP", 50),
            Phantom_Chemist_OccultElixir_HP = new("Phantom_Chemist_OccultElixir_HP", 25),
            Phantom_Oracle_Blessing_Health = new("Phantom_Oracle_Blessing_Health", 50),
            Phantom_Oracle_Starfall_Health = new("Phantom_Oracle_Starfall_Health", 100),
            Phantom_Ranger_OccultUnicorn_Health = new("Phantom_Ranger_OccultUnicorn_Health", 50),
            Phantom_Ranger_PhantomAim_Stop = new("Phantom_Ranger_PhantomAim_Stop", 30),
            Phantom_Thief_Steal_Health = new("Phantom_Thief_Steal_Health", 10);

        public static UserBool
            Phantom_Chemist_OccultElixir_RequireParty = new("Phantom_Chemist_OccultElixir_RequireParty", true),
            Phantom_TimeMage_Comet_RequireSpeed = new("Phantom_TimeMage_Comet_RequireSpeed", true),
            Phantom_TimeMage_Comet_UseSpeed = new("Phantom_TimeMage_Comet_UseSpeed", true);

        internal static void Draw(CustomComboPreset preset)
        {
            switch (preset)
            {
                case CustomComboPreset.Phantom_Freelancer_OccultResuscitation:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Freelancer_Resuscitation_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;

                case CustomComboPreset.Phantom_Geomancer_Sunbath:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Geomancer_Sunbath_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;

                case CustomComboPreset.Phantom_Knight_PhantomGuard:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Knight_PhantomGuard_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;
                case CustomComboPreset.Phantom_Knight_Pray:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Knight_Pray_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;
                case CustomComboPreset.Phantom_Knight_OccultHeal:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Knight_OccultHeal_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;
                case CustomComboPreset.Phantom_Knight_Pledge:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Knight_Pledge_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;
                case CustomComboPreset.Phantom_Bard_MightyMarch:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Bard_MightyMarch_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;

                case CustomComboPreset.Phantom_Monk_OccultChakra:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Monk_OccultChakra_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;

                case CustomComboPreset.Phantom_Oracle_Blessing:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Oracle_Blessing_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;

                case CustomComboPreset.Phantom_Oracle_Starfall:
                    UserConfig.DrawSliderInt(91, 100, Phantom_Oracle_Starfall_Health,
                        "玩家生命值百分比大於等於：", 200);
                    break;

                case CustomComboPreset.Phantom_Ranger_OccultUnicorn:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Ranger_OccultUnicorn_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;

                case CustomComboPreset.Phantom_Ranger_PhantomAim:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Ranger_PhantomAim_Stop,
                        "目標生命值百分比大於等於：", 200);
                    break;

                case CustomComboPreset.Phantom_Thief_Steal:
                    UserConfig.DrawSliderInt(1, 50, Phantom_Thief_Steal_Health,
                        "目標生命值百分比小於等於：", 200);
                    break;

                case CustomComboPreset.Phantom_Chemist_OccultPotion:
                    UserConfig.DrawSliderInt(1, 100, Phantom_Chemist_OccultPotion_Health,
                        "Player HP% to be \nless than or equal to:", 200);
                    break;

                case CustomComboPreset.Phantom_Chemist_OccultEther:
                    UserConfig.DrawSliderInt(1, 10000, Phantom_Chemist_OccultEther_MP,
                        "玩家魔力值小於等於：", sliderIncrement: SliderIncrements.Hundreds);
                    break;

                case CustomComboPreset.Phantom_Chemist_OccultElixir:
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudRed, "這是一個非常高消耗的功能！");
                    ImGui.Unindent();
                    UserConfig.DrawSliderInt(1, 100, Phantom_Chemist_OccultElixir_HP,
                        "隊伍平均生命值小於等於：", 200);
                    UserConfig.DrawAdditionalBoolChoice(Phantom_Chemist_OccultElixir_RequireParty,
                        "至少需要1名隊員", "");
                    ImGui.Indent();
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow, "大多數情況下不建議使用！");
                    ImGuiEx.TextWrapped(ImGuiColors.DalamudYellow, "如果要用，滑塊值應設定得較低！");
                    ImGui.Unindent();
                    break;

                case CustomComboPreset.Phantom_TimeMage_OccultComet:
                    UserConfig.DrawAdditionalBoolChoice(Phantom_TimeMage_Comet_RequireSpeed,
                        "需要即刻詠唱或魔神速才能使用魔彗星", "");
                    if (Phantom_TimeMage_Comet_RequireSpeed)
                    {
                        ImGui.Indent();
                        UserConfig.DrawAdditionalBoolChoice(
                            Phantom_TimeMage_Comet_UseSpeed,
                            "在使用魔彗星前新增即刻詠唱或魔神速", "");
                        ImGui.Unindent();
                    }
                    break;
            }
        }
    }
}
