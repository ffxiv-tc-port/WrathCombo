using Dalamud.Interface.Colors;
using Dalamud.Bindings.ImGui;
using ECommons.LanguageHelpers;
using WrathCombo.CustomComboNS.Functions;
using static WrathCombo.Extensions.UIntExtensions;
using static WrathCombo.Window.Functions.SliderIncrements;
using static WrathCombo.Window.Functions.UserConfig;

namespace WrathCombo.Combos.PvE;
internal partial class RDM
{
    internal static class Config
    {
        public static UserInt
            RDM_VariantCure = new("RDM_VariantCure"),
            RDM_ST_Lucid_Threshold = new("RDM_LucidDreaming_Threshold", 6500),
            RDM_AoE_Lucid_Threshold = new("RDM_AoE_Lucid_Threshold", 6500),
            RDM_BalanceOpener_Content = new("RDM_BalanceOpener_Content", 1),
            RDM_ST_Acceleration_Charges = new("RDM_ST_Acceleration_Charges", 0),
            RDM_AoE_Acceleration_Charges = new("RDM_AoE_Acceleration_Charges", 0),
            RDM_ST_Corpsacorps_Distance = new("RDM_ST_Corpsacorps_Distance", 25),
            RDM_ST_Corpsacorps_Time = new("RDM_ST_Corpsacorps_Time", 0),
            RDM_ST_GapCloseCorpsacorps_Time = new("RDM_ST_GapCloseCorpsacorps_Time", 0),
            RDM_AoE_Corpsacorps_Distance = new("RDM_AoE_Corpsacorps_Distance", 25),
            RDM_AoE_Corpsacorps_Time = new("RDM_AoE_Corpsacorps_Time", 0),
            RDM_AoE_GapCloseCorpsacorps_Time = new("RDM_AoE_GapCloseCorpsacorps_Time", 0),
            RDM_Opener_Selection = new("RDM_Opener_Selection", 0),
            RDM_ST_VerCureThreshold = new("RDM_ST_VerCureThreshold", 40),
            RDM_ST_VerCureHardcastThreshold = new("RDM_ST_VerCureHardcastThreshold", 25),
            RDM_AoE_VerCureThreshold = new("RDM_AoE_VerCureThreshold", 40),
            RDM_AoE_VerCureHardcastThreshold = new("RDM_AoE_VerCureHardcastThreshold", 25),
            RDM_RaidwideMit_LeadTime = new("RDM_RaidwideMit_LeadTime", 5),
            RDM_Engage_Scope = new("RDM_Engage_Scope", 0),
            RDM_Engage_Window = new("RDM_Engage_Window", 3);

        public static UserBool
            RDM_ST_EngageCorpsacorps = new("RDM_ST_EngageCorpsacorps", true),
            RDM_AoE_EngageCorpsacorps = new("RDM_AoE_EngageCorpsacorps", true);

        internal static void Draw(CustomComboPreset preset)
        {
            switch (preset)
            {
                case CustomComboPreset.RDM_Balance_Opener:
                    DrawHorizontalRadioButton(RDM_Opener_Selection, "Standard Opener", "Balance Standard Opener", 0);
                    DrawHorizontalRadioButton(RDM_Opener_Selection, "GapClosing Adjusted Standard Opener", "Shifts the melee a little bit to put a gapcloser in", 1);

                    ImGui.Indent();
                    DrawBossOnlyChoice(RDM_BalanceOpener_Content);
                    ImGui.Unindent();
                    break;
                
                case CustomComboPreset.RDM_Variant_Cure:
                    DrawSliderInt(1, 100, RDM_VariantCure, "HP% to be at or under", 200);
                    break;

                case CustomComboPreset.RDM_ST_MeleeCombo:
                    if (P.IPCSearch.AutoActions[CustomComboPreset.RDM_ST_DPS] == true && 
                        CustomComboFunctions.IsNotEnabled(CustomComboPreset.RDM_ST_MeleeCombo_IncludeRiposte))
                    {
                        ImGui.Indent();
                        ImGui.TextColored(ImGuiColors.DalamudRed, "WARNING: RIPOSTE IS NOT ENABLED.");
                        ImGui.TextColored(ImGuiColors.DalamudRed, "自動循環將不會自動啟動近戰連擊");
                        ImGui.Unindent();
                    }
                    break;
                
                case CustomComboPreset.RDM_ST_Corpsacorps:
                    DrawSliderInt(0, 25, RDM_ST_Corpsacorps_Distance,
                        " Use when Distance from target is less than or equal to:");
                    
                    DrawSliderInt(0, 5, RDM_ST_Corpsacorps_Time,
                        " How long you need to be stationary to use. Zero to disable");
                    DrawEngageOptions(RDM_ST_EngageCorpsacorps);
                    break;

                case CustomComboPreset.RDM_ST_MeleeCombo_GapCloser:
                    DrawSliderInt(0, 5, RDM_ST_GapCloseCorpsacorps_Time,
                        " How long you need to be stationary to use. Zero to disable");
                    break;

                case CustomComboPreset.RDM_AoE_Corpsacorps:
                    DrawSliderInt(0, 25, RDM_AoE_Corpsacorps_Distance,
                        " Use when Distance from target is less than or equal to:");
                    DrawSliderInt(0, 5, RDM_AoE_Corpsacorps_Time,
                        " How long you need to be stationary to use. Zero to disable");
                    DrawEngageOptions(RDM_AoE_EngageCorpsacorps);
                    break;

                case CustomComboPreset.RDM_AoE_MeleeCombo_GapCloser:
                    DrawSliderInt(0, 5, RDM_AoE_GapCloseCorpsacorps_Time,
                        " How long you need to be stationary to use. Zero to disable");
                    break;

                case CustomComboPreset.RDM_ST_Lucid:
                    DrawSliderInt(0, 10000, RDM_ST_Lucid_Threshold, $"Add {Role.LucidDreaming.ActionName()} when below this MP", sliderIncrement: Hundreds);
                    break;

                case CustomComboPreset.RDM_AoE_Lucid:
                    DrawSliderInt(0, 10000, RDM_AoE_Lucid_Threshold, $"Add {Role.LucidDreaming.ActionName()} when below this MP", sliderIncrement: Hundreds);
                    break;
                
                case CustomComboPreset.RDM_ST_Acceleration:
                    DrawSliderInt(0, 1, RDM_ST_Acceleration_Charges, "How many charges to keep ready\n (0 = Use All)");
                    break;

                case CustomComboPreset.RDM_AoE_Acceleration:
                    DrawSliderInt(0, 1, RDM_AoE_Acceleration_Charges, "How many charges to keep ready?\n (0 = Use All)");
                    break;

                case CustomComboPreset.RDM_ST_VerCure:
                    DrawVerCureSliders(RDM_ST_VerCureThreshold, RDM_ST_VerCureHardcastThreshold);
                    break;

                case CustomComboPreset.RDM_AoE_VerCure:
                    DrawVerCureSliders(RDM_AoE_VerCureThreshold, RDM_AoE_VerCureHardcastThreshold);
                    break;

                case CustomComboPreset.RDM_ST_Addle:
                case CustomComboPreset.RDM_ST_MagickBarrier:
                case CustomComboPreset.RDM_AoE_Addle:
                case CustomComboPreset.RDM_AoE_MagickBarrier:
                    DrawSliderInt(1, 15, RDM_RaidwideMit_LeadTime,
                        "Use it this many seconds or less before a raidwide predicted by BossMod (Reborn) (shared by all four options)".Loc(), 200);
                    break;
            }
        }

        private static void DrawVerCureSliders(UserInt instant, UserInt hardcast)
        {
            DrawSliderInt(1, 100, instant,
                "Cast it instantly (Dualcast or ??) at or under this HP%".Loc(Role.Swiftcast.ActionName()), 200);
            DrawSliderInt(0, 100, hardcast,
                "Hardcast only while standing still, at or under this HP% (0 disables)".Loc(), 200);
        }

        private static void DrawEngageOptions(UserBool engage)
        {
            DrawAdditionalBoolChoice(engage,
                "Open pulls with ?? (auto-rotation only)".Loc(Corpsacorps.ActionName()),
                ("Out of combat or right after combat starts, when the target is in range but outside melee range, dash in first. " +
                 "If BossMod (Reborn) says the landing spot is unsafe, open with an instant cast instead (??, ??, ??); when it cannot tell, dash as usual.")
                .Loc(GrandImpact.ActionName(), Acceleration.ActionName(), Role.Swiftcast.ActionName()));
            if (!engage)
                return;

            ImGui.Indent();
            DrawRadioButton(RDM_Engage_Scope, "F.A.T.E. targets only".Loc(), "", 0);
            DrawRadioButton(RDM_Engage_Scope, "All field pulls (incl. critical engagements)".Loc(), "", 1);
            DrawSliderInt(0, 10, RDM_Engage_Window,
                "Also within this many seconds after combat starts (0 = out of combat only; shared by single-target and AoE)".Loc(), 200);
            ImGui.Unindent();
        }
    }
}
