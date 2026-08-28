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
using Dalamud.Interface.Colors;
using ECommons.GameHelpers;
using WrathCombo.AutoRotation;
using WCJobIDs = WrathCombo.CustomComboNS.Functions.CustomComboFunctions.JobIDs;

namespace WrathCombo.Window.Tabs
{
    internal class AutoRotationTab : ConfigWindow
    {
        private static uint _selectedNpc = 0;

        /// <summary>
        ///     IgnoredNPCs 是設定檔回讀的字典，裡面的 BNpcName id 可能跨版本殘留，
        ///     不保證存在於本地資料表。裸 GetRow 查無此列時 Lumina 會擲例外，而這個
        ///     分頁在 Draw 路徑上，一擲整個設定視窗就不見。查不到時顯示「未知 NPC」，
        ///     不要靜默把它從使用者的忽略清單裡移掉。
        /// </summary>
        private static string GetIgnoredNpcName(uint bNpcNameId)
        {
            var row = Svc.Data.Excel.GetSheet<BNpcName>().GetRowOrDefault(bNpcNameId);
            return row == null ? "Unknown NPC".Loc() : row.Value.Singular.ToString();
        }

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

        /// <summary>
        ///     設定視窗用的戰鬥職業清單(職業 id ＋ 顯示名)。名字取自 <c>ClassJob</c> 表，
        ///     台服會直接是繁體中文 —— 不要在這裡硬編英文職業名。
        ///     <para>
        ///     清單來源是 WrathCombo 自己的功能分組，所以只會列出真的有功能可設的戰鬥職業，
        ///     且順序與 PvE 分頁一致(坦克→治療→近戰→遠程)。只建一次；每幀重建會在
        ///     設定視窗開著時反覆配置。
        ///     </para>
        /// </summary>
        private static (uint JobId, string Name)[]? _combatJobs;

        private static (uint JobId, string Name)[] CombatJobs =>
            _combatJobs ??= groupedPresets.Values
                .Select(x => x.First().Info)
                .Where(x => x.JobID > 0 && WCJobIDs.JobIDToRole(x.JobID) != 0)
                .Select(x => (JobId: x.JobID, Name: WCJobIDs.JobIDToName(x.JobID)))
                .Distinct()
                .ToArray();

        /// <summary>
        ///     「AoE 傷害功能所需目標數」的逐職業覆寫區塊。
        ///     <para>
        ///     優先權：IPC 租約 &gt; 目前職業的覆寫 &gt; 全域值。解析只有一份，在
        ///     <see cref="DPSSettingsIPCWrapper.ResolveAoETargets" />；這裡只是顯示它。
        ///     </para>
        ///     <para>
        ///     ⚠️ 沒有覆寫的職業在列上直接寫「沿用全域」，不要畫成 0 ——
        ///     0 是合法的設定值(等於任何時候都不放 AoE)，拿它表示「沒設定」會誤導。
        ///     </para>
        /// </summary>
        private static void DrawPerJobAoETargets(DPSSettings dps, ref bool changed)
        {
            ImGui.Spacing();
            ImGuiEx.TextUnderlined("Per-Job Targets Required for AoE Damage Features".Loc());
            ImGuiComponents.HelpMarker(HelpPerJobAoETargets.Loc());

            var resolved = DPSSettingsIPCWrapper.ResolveAoETargets(dps);
            ImGuiEx.Text(ImGuiColors.DalamudGrey,
                "In effect right now: ?? (from ??)".Loc(
                    DescribeAoETargets(resolved.Value),
                    DescribeAoETargetsSource(resolved.Source)));

            var currentJob = Player.Available
                ? DPSSettingsIPCWrapper.NormalizeJobId(Player.JobId)
                : 0u;

            // 🔴 目前職業的快捷列與底下的完整清單會出現同一個職業，
            // 兩邊的 ImRaii.PushId(jobId) 在同一個視窗裡會撞 ID(點一邊影響另一邊)。
            // 各自多包一層 scope 隔開。
            using (ImRaii.PushId("WrathAoETargetsCurrentJob"))
            {
                if (currentJob == 0 || WCJobIDs.JobIDToRole(currentJob) == 0)
                    ImGuiEx.Text(ImGuiColors.DalamudGrey,
                        "Not on a combat job right now - the global value applies.".Loc());
                else
                    changed |= DrawAoETargetsJobRow(dps, currentJob,
                        WCJobIDs.JobIDToName(currentJob));
            }

            if (ImGui.TreeNode("All jobs".Loc() + "###WrathAoETargetsAllJobs"))
            {
                using (ImRaii.PushId("WrathAoETargetsAllJobsList"))
                {
                    foreach (var (jobId, name) in CombatJobs)
                        changed |= DrawAoETargetsJobRow(dps, jobId, name);
                }

                ImGui.TreePop();
            }
        }

        /// <summary>
        ///     一個職業的覆寫列：[有無覆寫] [值] 職業名 (狀態)。
        ///     沒有覆寫時仍然把全域值畫在同一個欄位裡(唯讀)，列與列才對得齊；
        ///     右邊的灰字才是「這是不是這個職業自己的值」的判準。
        /// </summary>
        private static bool DrawAoETargetsJobRow(DPSSettings dps, uint jobId, string name)
        {
            var changed = false;
            var hasOverride = dps.DPSAoETargetsPerJob.TryGetValue(jobId, out var value);

            using var jobIdScope = ImRaii.PushId((int)jobId);

            var overrideOn = hasOverride;
            if (ImGui.Checkbox("###WrathAoETargetsJobOverride", ref overrideOn))
            {
                if (overrideOn)
                    // 以目前的全域值起頭，不憑空塞一個數字給使用者。
                    dps.DPSAoETargetsPerJob[jobId] = dps.DPSAoETargets;
                else
                    dps.DPSAoETargetsPerJob.Remove(jobId);

                hasOverride = overrideOn;
                value = overrideOn ? dps.DPSAoETargets : null;
                changed = true;
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    "Give ?? its own requirement. Unticked means this job follows the global value above."
                        .Loc(name));

            ImGui.SameLine();
            using (ImRaii.Disabled(!hasOverride))
            {
                var shown = hasOverride ? value : dps.DPSAoETargets;
                if (ImGuiEx.InputInt(70f.Scale(), "###WrathAoETargetsJobValue", ref shown)
                    && hasOverride)
                {
                    if (shown < 0)
                        shown = 0;

                    dps.DPSAoETargetsPerJob[jobId] = shown;
                    value = shown;
                    changed = true;
                }
            }

            // 有覆寫的職業在列上要一眼看得出來 => 職業名用金色；沒覆寫的整列壓成灰字
            // 並明寫「沿用全域」(⚠️ 不要只靠欄位裡的數字，那個數字是全域值不是它自己的)。
            ImGui.SameLine();
            if (!hasOverride)
            {
                ImGuiEx.Text(ImGuiColors.DalamudGrey, name);
                ImGui.SameLine();
                ImGuiEx.Text(ImGuiColors.DalamudGrey, "(follows global)".Loc());
            }
            else if (value is null)
            {
                ImGuiEx.Text(ImGuiColors.ParsedGold, name);
                ImGui.SameLine();
                ImGuiEx.Text(ImGuiColors.DalamudOrange, "(AoE off for this job)".Loc());
            }
            else
            {
                ImGuiEx.Text(ImGuiColors.ParsedGold, name);
            }

            return changed;
        }

        private static string DescribeAoETargets(int? value) =>
            value is null ? "AoE disabled".Loc() : value.Value.ToString();

        private static string DescribeAoETargetsSource(AoETargetsSource source) =>
            source switch
            {
                AoETargetsSource.Lease => "another plugin's lease".Loc(),
                AoETargetsSource.Job => "this job's own setting".Loc(),
                _ => "the global setting".Loc(),
            };

        private const string HelpPerJobAoETargets =
            "Overrides the global requirement above on a per-job basis. When you switch jobs, that job's own value applies automatically.\n\nJobs without an override follow the global value - they are NOT set to 0.\n\nIf another plugin has taken this setting over with a lease, the lease wins over both the per-job value and the global value.";

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

                    // 這兩項現在可以被其他外掛用租約接管（BypassQuest / BypassFATE），
                    // 所以改用會顯示「被誰接管」的版本 —— 否則使用者會看到自己的勾選狀態、
                    // 實際生效的卻是別人設的值，而且沒有任何提示。
                    P.UIHelper.ShowIPCControlledIndicatorIfNeeded("BypassQuest");
                    ImGuiExtensions.Prefix(false);
                    changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                        "Bypass Only in Combat for Quest Targets".Loc(),
                        ref cfg.BypassQuest, "BypassQuest");
                    ImGuiComponents.HelpMarker("Disables Auto-Mode outside of combat unless you're within range of a quest target.".Loc());

                    P.UIHelper.ShowIPCControlledIndicatorIfNeeded("BypassFATE");
                    ImGuiExtensions.Prefix(false);
                    changed |= P.UIHelper.ShowIPCControlledCheckboxIfNeeded(
                        "Bypass Only in Combat for FATE Targets".Loc(),
                        ref cfg.BypassFATE, "BypassFATE");
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

                // DPSAoETargets 現在也能被租約接管。這個欄位是 int?，沒有對應的
                // ShowIPCControlled* 版本可用，所以只加「被誰接管」的提示列，
                // 輸入框本身維持原樣（讓使用者看得見接管狀態，不是把控制權藏起來）。
                P.UIHelper.ShowIPCControlledIndicatorIfNeeded("DPSAoETargets");
                var input = ImGuiEx.InputInt(100f.Scale(), "Targets Required for AoE Damage Features".Loc(), ref cfg.DPSSettings.DPSAoETargets);
                if (input)
                {
                    changed |= input;
                    if (cfg.DPSSettings.DPSAoETargets < 0)
                        cfg.DPSSettings.DPSAoETargets = 0;
                }
                ImGuiComponents.HelpMarker("Disabling this will turn off AoE DPS features. Otherwise will require the amount of targets required to be in range of an AoE feature's attack to use. This applies to all 3 roles, and for any features that deal AoE damage.".Loc());

                DrawPerJobAoETargets(cfg.DPSSettings, ref changed);

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
                var prev = selected is null ? "" : $"{GetIgnoredNpcName(selected.Value.Value)} (ID: {selected.Value.Key})";
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
                            if (ImGui.Selectable($"{GetIgnoredNpcName(npc.Value)} (ID: {npc.Key})"))
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
