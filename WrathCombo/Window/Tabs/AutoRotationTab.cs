using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.LanguageHelpers;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using System;
using System.Linq;
using WrathCombo.Combos.PvE;
using WrathCombo.Extensions;
using WrathCombo.Services;
using WrathCombo.Services.IPC;
using WrathCombo.Services.IPC_Subscriber;
using System.Collections.Generic;
using WrathCombo.AutoRotation;

namespace WrathCombo.Window.Tabs
{
    internal class AutoRotationTab : ConfigWindow
    {
        private static uint _selectedNpc = 0;

        private static readonly Dictionary<DPSRotationMode, string> DPSRotationModeTranslations = new()
        {
            { DPSRotationMode.Manual, "Manual" },
            { DPSRotationMode.Highest_Max, "Highest Max" },
            { DPSRotationMode.Lowest_Max, "Lowest Max" },
            { DPSRotationMode.Highest_Current, "Highest Current" },
            { DPSRotationMode.Lowest_Current, "Lowest Current" },
            { DPSRotationMode.Tank_Target, "Tank Target" },
            { DPSRotationMode.Nearest, "Nearest" },
            { DPSRotationMode.Furthest, "Furthest" },
            { DPSRotationMode.ManualNonPlayer, "Manual Non-Player" },
        };

        private static readonly Dictionary<HealerRotationMode, string> HealerRotationModeTranslations = new()
        {
            { HealerRotationMode.Manual, "Manual" },
            { HealerRotationMode.Highest_Current, "Highest Current" },
            { HealerRotationMode.Lowest_Current, "Lowest Current" },
        };

        private static bool ShowTranslatedCombo<TEnum>(string label, Dictionary<TEnum, string> translations, ref TEnum currentMode) where TEnum : Enum
        {
            // Get the (localized) display name of the current mode
            if (!translations.TryGetValue(currentMode, out var currentLabel))
                currentLabel = currentMode.ToString();

            // Show the dropdown
            if (ImGui.BeginCombo(label, currentLabel.Loc()))
            {
                foreach (var translation in translations)
                {
                    bool isSelected = currentMode.Equals(translation.Key);
                    if (ImGui.Selectable(translation.Value.Loc(), isSelected))
                    {
                        currentMode = translation.Key;
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
                return true;
            }
            return false;
        }

        internal static new void Draw()
        {
            ImGui.TextWrapped(("This is where you can configure the parameters in which Auto-Rotation will operate. " +
                "Features marked with an 'Auto-Mode' checkbox are able to be used with Auto-Rotation.").Loc());
            ImGui.Separator();

            var cfg = Service.Configuration.RotationConfig;
            bool changed = false;

            if (P.UIHelper.ShowIPCControlledIndicatorIfNeeded())
                changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                    "Enable Auto-Rotation".Loc(), ref cfg.Enabled);
            else
                changed |= ImGui.Checkbox("Enable Auto-Rotation".Loc(), ref cfg.Enabled);
            if (P.IPC.GetAutoRotationState())
            {
                var inCombatOnly = (bool)P.IPC.GetAutoRotationConfigState(
                    Enum.Parse<AutoRotationConfigOption>("InCombatOnly"))!;
                ImGuiExtensions.Prefix(!inCombatOnly);
                changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                    "Only in Combat".Loc(), ref cfg.InCombatOnly, "InCombatOnly");

                if (inCombatOnly)
                {
                    ImGuiExtensions.Prefix(false);
                    changed |= ImGui.Checkbox("Bypass When Combo Suggests Self-Use Action".Loc(), ref cfg.BypassBuffs);
                    ImGuiComponents.HelpMarker("Many jobs have an out of combat action that can be used, for example, ?? or ??. This will allow these to be used without being in combnat.".Loc(RPR.Soulsow.ActionName(), MNK.ForbiddenMeditation.ActionName()));

                    ImGuiExtensions.Prefix(false);
                    changed |= ImGui.Checkbox("Bypass Only in Combat for Quest Targets".Loc(), ref cfg.BypassQuest);
                    ImGuiComponents.HelpMarker("Disables Auto-Mode outside of combat unless you're within range of a quest target.".Loc());

                    ImGuiExtensions.Prefix(false);
                    changed |= ImGui.Checkbox("Bypass Only in Combat for FATE Targets".Loc(), ref cfg.BypassFATE);
                    ImGuiComponents.HelpMarker("Disables Auto-Mode outside of combat unless you're synced to a FATE.".Loc());

                    ImGuiExtensions.Prefix(true);
                    ImGuiEx.SetNextItemWidthScaled(100);
                    changed |= ImGui.InputInt("Delay to activate Auto-Rotation once combat starts (seconds)".Loc(), ref cfg.CombatDelay);

                    if (cfg.CombatDelay < 0)
                        cfg.CombatDelay = 0;
                }
            }

            changed |= ImGui.Checkbox("Enable Automatically in Instanced Content".Loc(), ref cfg.EnableInInstance);
            changed |= ImGui.Checkbox("Disable After Leaving Instanced Content".Loc(), ref cfg.DisableAfterInstance);

            if (ImGui.CollapsingHeader("Damage Settings".Loc()))
            {
                ImGuiEx.TextUnderlined("Targeting Mode".Loc());

                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("DPSRotationMode");
                changed |= ShowTranslatedCombo<DPSRotationMode>(
                    "###DPSTargetingMode",
                    DPSRotationModeTranslations,
                    ref cfg.DPSRotationMode);

                ImGuiComponents.HelpMarker(("Manual - Leaves all targeting decisions to you.\n" +
                    "Highest Max - Prioritises enemies with the highest max HP.\n" +
                    "Lowest Max - Prioritises enemies with the lowest max HP.\n" +
                    "Highest Current - Prioritises the enemy with the highest current HP.\n" +
                    "Lowest Current - Prioritises the enemy with the lowest current HP.\n" +
                    "Tank Target - Prioritises the same target as the first tank in your group.\n" +
                    "Nearest - Prioritises the closest target to you.\n" +
                    "Furthest - Prioritises the furthest target from you.\n" +
                    "Manual Non-Player - Your manually selected target, only if it is a non-player target within 25 yalms.").Loc());
                ImGui.Spacing();

                if (cfg.DPSRotationMode == AutoRotation.DPSRotationMode.Manual)
                {
                    changed |= ImGui.Checkbox("Enforce Best AoE Target Selection".Loc(), ref cfg.DPSSettings.AoEIgnoreManual);

                    ImGuiComponents.HelpMarker("For all other targeting modes, AoE will target based on highest number of targets hit. In manual mode, it will only do this if you tick this box.".Loc());
                }

                var input = ImGuiEx.InputInt(100f.Scale(), "Targets Required for AoE Damage Features".Loc(), ref cfg.DPSSettings.DPSAoETargets);
                if (input)
                {
                    changed |= input;
                    if (cfg.DPSSettings.DPSAoETargets < 0)
                        cfg.DPSSettings.DPSAoETargets = 0;
                }
                ImGuiComponents.HelpMarker("Disabling this will turn off AoE DPS features. Otherwise will require the amount of targets required to be in range of an AoE feature's attack to use. This applies to all 3 roles, and for any features that deal AoE damage.".Loc());

                ImGuiEx.SetNextItemWidthScaled(100);
                // SliderFloat 在拖曳過程中每一畫格都回傳 true，直接餵給 changed 會讓底下的
                // if (changed) Configuration.Save() 以幀率同步寫磁碟。改用
                // IsItemDeactivatedAfterEdit()：數值仍即時套用，但只在放開滑鼠時存檔一次。
                ImGui.SliderFloat("Max Target Distance".Loc(), ref cfg.DPSSettings.MaxDistance, 1, 30);
                changed |= ImGui.IsItemDeactivatedAfterEdit();
                cfg.DPSSettings.MaxDistance =
                    Math.Clamp(cfg.DPSSettings.MaxDistance, 1, 30);

                ImGuiComponents.HelpMarker("Max distance all targeting modes (except Manual) will look for a target. Values from 1 to 30 only.".Loc());

                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("FATEPriority");
                changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                    "Prioritise FATE Targets".Loc(), ref cfg.DPSSettings.FATEPriority, "FATEPriority");
                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("QuestPriority");
                changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                    "Prioritise Quest Targets".Loc(), ref cfg.DPSSettings.QuestPriority, "QuestPriority");
                changed |= ImGui.Checkbox("Prioritise Targets Not In Combat".Loc(), ref cfg.DPSSettings.PreferNonCombat);

                if (cfg.DPSSettings.PreferNonCombat && changed)
                    cfg.DPSSettings.OnlyAttackInCombat = false;

                changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                    "Only Attack Targets Already In Combat".Loc(), ref cfg.DPSSettings.OnlyAttackInCombat,
                    "OnlyAttackInCombat");

                if (cfg.DPSSettings.OnlyAttackInCombat && changed)
                    cfg.DPSSettings.PreferNonCombat = false;

                changed |= ImGui.Checkbox("Always Target Regardless of Action".Loc(), ref cfg.DPSSettings.AlwaysSelectTarget);

                ImGuiComponents.HelpMarker("Normally, Auto-rotation will only target an enemy if the next action it would fire needs a target. This will change the behaviour so it will always select the target regardless of what the action can target.".Loc());

                var npcs = Service.Configuration.IgnoredNPCs.ToList();
                var selected = npcs.FirstOrNull(x => x.Key == _selectedNpc);
                var prev = selected is null ? "" : $"{Svc.Data.Excel.GetSheet<BNpcName>().GetRow(selected.Value.Value).Singular} (ID: {selected.Value.Key})";
                ImGuiEx.TextUnderlined("Ignored NPCs".Loc());
                using (var combo = ImRaii.Combo("###Ignore", prev))
                {
                    if (combo)
                    {
                        if (ImGui.Selectable(""))
                        {
                            _selectedNpc = 0;
                        }

                        foreach (var npc in npcs)
                        {
                            var npcData = Svc.Data.Excel
                                .GetSheet<BNpcName>().GetRow(npc.Value);
                            if (ImGui.Selectable($"{npcData.Singular} (ID: {npc.Key})"))
                            {
                                _selectedNpc = npc.Key;
                            }
                        }
                    }
                }
                ImGuiComponents.HelpMarker(("These NPCs will be ignored by Auto-Rotation.\n" +
                                           "Every instance of this NPC will be excluded from automatic targeting (Manual will still work).\n" +
                                           "To remove an NPC from this list, select it and press the Delete button below.\n" +
                                           "To add an NPC to this list, target the NPC and use the command: /wrath ignore").Loc());

                if (_selectedNpc > 0)
                {
                    if (ImGui.Button("Delete From Ignored".Loc()))
                    {
                        Service.Configuration.IgnoredNPCs.Remove(_selectedNpc);
                        Service.Configuration.Save();

                        _selectedNpc = 0;
                    }
                }

            }
            ImGui.Spacing();
            if (ImGui.CollapsingHeader("Healing Settings".Loc()))
            {
                ImGuiEx.TextUnderlined("Healing Targeting Mode".Loc());
                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("HealerRotationMode");
                changed |= ShowTranslatedCombo<HealerRotationMode>(
                    "###HealerTargetingMode",
                    HealerRotationModeTranslations,
                    ref cfg.HealerRotationMode);
                ImGuiComponents.HelpMarker(("Manual - Will only heal a target if you select them manually. If the target does not meet the healing threshold settings criteria below it will skip healing in favour of DPSing (if also enabled).\n" +
                    "Highest Current - Prioritises the party member with the highest current HP%.\n" +
                    "Lowest Current - Prioritises the party member with the lowest current HP%.").Loc());

                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("SingleTargetHPP");
                changed |= P.UIHelper.ShowIPCControlledSliderIfNeeded(
                    "Single Target HP% Threshold".Loc(), ref cfg.HealerSettings.SingleTargetHPP, "SingleTargetHPP");

                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("SingleTargetRegenHPP");
                changed |= P.UIHelper.ShowIPCControlledSliderIfNeeded(
                    "Single Target HP% Threshold (target has Regen/Aspected Benefic)".Loc(), ref cfg.HealerSettings.SingleTargetRegenHPP, "SingleTargetRegenHPP");
                ImGuiComponents.HelpMarker("You typically want to set this lower than the above setting.".Loc());

                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("AoETargetHPP");
                changed |= P.UIHelper.ShowIPCControlledSliderIfNeeded(
                    "AoE HP% Threshold".Loc(), ref cfg.HealerSettings.AoETargetHPP, "AoETargetHPP");

                var input = ImGuiEx.InputInt(100f.Scale(), "Targets Required for AoE Healing Features".Loc(), ref cfg.HealerSettings.AoEHealTargetCount);
                if (input)
                {
                    changed |= input;
                    if (cfg.HealerSettings.AoEHealTargetCount < 0)
                        cfg.HealerSettings.AoEHealTargetCount = 0;
                }
                ImGuiComponents.HelpMarker("Disabling this will turn off AoE Healing features. Otherwise will require the amount of targets required to be in range of an AoE feature's heal to use.".Loc());
                ImGuiEx.SetNextItemWidthScaled(100);
                changed |= ImGui.InputInt("Delay to start healing once above conditions are met (seconds)".Loc(), ref cfg.HealerSettings.HealDelay);

                if (cfg.HealerSettings.HealDelay < 0)
                    cfg.HealerSettings.HealDelay = 0;
                ImGuiComponents.HelpMarker("Don't set this too high! 1-2 seconds is normally comfy enough to be considered a natural reaction.".Loc());

                ImGui.Spacing();

                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("AutoRez");
                changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                    "Auto-Resurrect".Loc(), ref cfg.HealerSettings.AutoRez, "AutoRez");
                ImGuiComponents.HelpMarker("Will attempt to resurrect dead party members. Applies to ??, ??, ??, ??, ?? and ?? ?? ??".Loc(WHM.ClassID.JobAbbreviation(), WHM.JobID.JobAbbreviation(), SCH.JobID.JobAbbreviation(), AST.JobID.JobAbbreviation(), SGE.JobID.JobAbbreviation(), OccultCrescent.ContentName, Svc.Data.GetExcelSheet<MKDSupportJob>().GetRow(10).Unknown0, OccultCrescent.Revive.ActionName()));
                var autoRez = (bool)P.IPC.GetAutoRotationConfigState(AutoRotationConfigOption.AutoRez)!;
                if (autoRez)
                {
                    ImGuiExtensions.Prefix(false);
                    changed |= ImGui.Checkbox("Apply to Out of Party Members".Loc(), ref cfg.HealerSettings.AutoRezOutOfParty);

                    ImGuiExtensions.Prefix(false);
                    changed |= ImGui.Checkbox("Require Swiftcast/Dualcast".Loc(), ref
                        cfg.HealerSettings.AutoRezRequireSwift);
                    ImGuiComponents.HelpMarker(
                        ("Requires ?? " +
                        "(or ??'s Dualcast) " +
                        "to be available to resurrect a party member, to avoid hard-casting.").Loc(
                            RoleActions.Magic.Swiftcast.ActionName(),
                            RDM.JobID.JobAbbreviation()));

                    ImGuiExtensions.Prefix(true);
                    P.UIHelper.ShowIPCControlledIndicatorIfNeeded("AutoRezDPSJobs");
                    changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                        "Apply to ?? & ??".Loc(SMN.JobID.JobAbbreviation(), RDM.JobID.JobAbbreviation()), ref cfg.HealerSettings.AutoRezDPSJobs, "AutoRezDPSJobs");
                    ImGuiComponents.HelpMarker("When playing as ?? or ??, also attempt to raise a dead party member. ?? will only resurrect with ?? or ?? active.".Loc(SMN.JobID.JobAbbreviation(), RDM.JobID.JobAbbreviation(), RDM.JobID.JobAbbreviation(), RoleActions.Magic.Buffs.Swiftcast.StatusName(), RDM.Buffs.Dualcast.StatusName()));
                }

                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("AutoCleanse");
                changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                    "Auto-??".Loc(RoleActions.Healer.Esuna.ActionName()), ref cfg.HealerSettings.AutoCleanse, "AutoCleanse");
                ImGuiComponents.HelpMarker("Will ?? any cleansable debuffs (Healing takes priority).".Loc(RoleActions.Healer.Esuna.ActionName()));

                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("ManageKardia");
                changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                    "[??] Automatically Manage Kardia".Loc(SGE.JobID.JobAbbreviation()), ref cfg.HealerSettings.ManageKardia, "ManageKardia");
                ImGuiComponents.HelpMarker("Switches ?? to party members currently being targeted by enemies, prioritising tanks if multiple people are being targeted.".Loc(SGE.Kardia.ActionName()));
                if (cfg.HealerSettings.ManageKardia)
                {
                    ImGuiExtensions.Prefix(cfg.HealerSettings.ManageKardia);
                    changed |= ImGui.Checkbox("Limit ?? swapping to tanks only".Loc(SGE.Kardia.ActionName()), ref cfg.HealerSettings.KardiaTanksOnly);
                }

                changed |= ImGui.Checkbox("[??/??] Pre-emptively apply heal over time on focus target".Loc(WHM.JobID.JobAbbreviation(), AST.JobID.JobAbbreviation()), ref cfg.HealerSettings.PreEmptiveHoT);
                ImGuiComponents.HelpMarker("Applies ??/?? to your focus target when out of combat and they are 30y or less away from an enemy. (Bypasses \"Only in Combat\" setting)".Loc(WHM.Regen.ActionName(), AST.AspectedBenefic.ActionName()));

                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("IncludeNPCs");
                changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded("Heal Friendly NPCs".Loc(), ref cfg.HealerSettings.IncludeNPCs);
                ImGuiComponents.HelpMarker("Useful for healer quests where NPCs are expected to be healed but aren't added directly to your party.".Loc());

            }

            ImGuiEx.TextUnderlined("Advanced".Loc());
            changed |= ImGui.InputInt("Throttle Delay (ms)".Loc(), ref cfg.Throttler);
            ImGuiComponents.HelpMarker("Auto-Rotation has a built in throttler to only run every so many milliseconds for performance reasons. If you experience issues with frame rate, try increasing this value. Do note this may have a side-effect of introducing clipping if set too high, so experiment with the value.".Loc());

            using (ImRaii.Disabled(!OrbwalkerIPC.IsEnabled))
            {
                changed |= ImGui.Checkbox("Enable Orbwalker Integration".Loc(), ref cfg.OrbwalkerIntegration);

                ImGuiComponents.HelpMarker("This will make Auto-Rotation use actions with cast times even whilst moving, as Orbwalker will lock movement during the cast.".Loc());
            }

            if (changed)
                Service.Configuration.Save();

        }
    }
}
