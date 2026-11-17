using System;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using ECommons.LanguageHelpers;
using Dalamud.Bindings.ImGui;
using System.Linq;
using System.Numerics;
using ECommons.Logging;
using WrathCombo.Combos;
using WrathCombo.Core;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Services;
using WrathCombo.Window.Functions;

namespace WrathCombo.Window.Tabs
{
    internal class PvPFeatures : ConfigWindow
    {
        internal static string OpenJob = string.Empty;
        internal static int ColCount = 1;

        internal static new void Draw()
        {
            using (var scrolling = ImRaii.Child("scrolling", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y), true))
            {
                var indentwidth = 12f.Scale();
                var indentwidth2 = indentwidth + 42f.Scale();

                if (OpenJob == string.Empty)
                {
                    ImGuiEx.LineCentered("pvpDesc", () =>
                    {
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.TextWrapped($"{FontAwesomeIcon.SkullCrossbones.ToIconString()}");
                        ImGui.PopFont();
                        ImGui.SameLine();
                        ImGui.TextWrapped("These are PvP features. They will only work in PvP-enabled zones.".Loc());
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        ImGui.TextWrapped($"{FontAwesomeIcon.SkullCrossbones.ToIconString()}");
                        ImGui.PopFont();
                    });
                    ImGuiEx.LineCentered($"pvpDesc2", () =>
                    {
                        ImGuiEx.TextUnderlined("Select a job from below to enable and configure features for it.".Loc());
                    });
                    ImGui.Spacing();

                    ColCount = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / 200f.Scale()));

                    using (var tab = ImRaii.Table("PvPTable", ColCount))
                    {
                        ImGui.TableNextColumn();

                        if (!tab)
                            return;

                        foreach (string? jobName in groupedPresets.Where(x =>
                                     x.Value.Any(y => PresetStorage.IsPvP(y.Preset) &&
                                                      !PresetStorage.ShouldBeHidden(y.Preset)))
                                     .Select(x => x.Key))
                        {
                            string abbreviation = groupedPresets[jobName].First().Info.JobShorthand;
                            string header = string.IsNullOrEmpty(abbreviation) ? jobName : $"{jobName} - {abbreviation}";
                            var id = groupedPresets[jobName].First().Info.JobID;
                            IDalamudTextureWrap? icon = Icons.GetJobIcon(id);
                            using (var disabled = ImRaii.Disabled(DisabledJobsPVP.Any(x => x == id)))
                            {
                                if (ImGui.Selectable($"###{header}", OpenJob == jobName, ImGuiSelectableFlags.None,
                                        icon == null ? new Vector2(0, 32).Scale() : new Vector2(0, icon.Size.Y / 2f).Scale()))
                                {
                                    OpenJob = jobName;
                                }
                                ImGui.SameLine(indentwidth);
                                if (icon != null)
                                {
                                    ImGui.Image(icon.Handle, new Vector2(icon.Size.X, icon.Size.Y).Scale() / 2f);
                                    ImGui.SameLine(indentwidth2);
                                }
                                ImGui.Text($"{header} {(disabled ? "(Disabled due to update)".Loc() : "")}");
                            }

                            ImGui.TableNextColumn();
                        }
                    }
                }
                else
                {
                    var id = groupedPresets[OpenJob].First().Info.JobID;
                    IDalamudTextureWrap? icon = Icons.GetJobIcon(id);

                    using (var headingTab = ImRaii.Child("PvPHeadingTab", new Vector2(ImGui.GetContentRegionAvail().X, icon is null ? 24f.Scale() : (icon.Size.Y / 2f).Scale() + 4f)))
                    {
                        if (ImGui.Button("Back".Loc(), new Vector2(0, 24f.Scale())))
                        {
                            OpenJob = "";
                            return;
                        }
                        ImGui.SameLine();
                        ImGuiEx.LineCentered(() =>
                        {
                            if (icon != null)
                            {
                                ImGui.Image(icon.Handle, new Vector2(icon.Size.X, icon.Size.Y).Scale() / 2f);
                                ImGui.SameLine();
                            }
                            ImGuiEx.Text($"{OpenJob}");
                        });

                    }

                    if (id != Combos.PvE.ADV.JobID)
                        DrawGlobalTogglesNotice(OpenJob);

                    using (var contents = ImRaii.Child("Contents", new Vector2(0)))
                    {
                        currentPreset = 1;
                        try
                        {
                            if (ImGui.BeginTabBar($"subTab{OpenJob}", ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.AutoSelectNewTabs))
                            {
                                if (ImGui.BeginTabItem("Normal".Loc()))
                                {
                                    DrawHeadingContents(OpenJob);
                                    ImGui.EndTabItem();
                                }

                                ImGui.EndTabBar();
                            }
                        }
                        catch { }

                    }
                }

            }
        }

        private static readonly (CustomComboPreset Preset, string Name)[] GlobalPvPToggles =
        [
            (CustomComboPreset.PvP_CCOnlyOnPlayers, "目標不是玩家時不放控場技能"),
            (CustomComboPreset.PvP_LongCDOnlyOnPlayers, "目標不是玩家時不放長冷卻技能"),
            (CustomComboPreset.PvP_GapCloserMinDistance, "目標太近時不放位移技能"),
        ];

        // 這三個開關掛在「通用/多職業」，各職業頁只顯示狀態與捷徑，不在這裡改值（勾選仍走原本的 preset 介面）。
        private static void DrawGlobalTogglesNotice(string jobName)
        {
            string advName = CustomComboFunctions.JobIDs.JobIDToName(Combos.PvE.ADV.JobID);
            ImGui.TextDisabled($"全職業 PvP 開關（在「{advName}」裡設定）：");
            if (groupedPresets.ContainsKey(advName))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"前往{advName}###pvpGlobalToggles"))
                    OpenJob = advName;
            }

            foreach (var (preset, name) in GlobalPvPToggles)
            {
                bool on = PresetStorage.IsEnabled(preset);
                string note = on && preset == CustomComboPreset.PvP_GapCloserMinDistance &&
                              Combos.PvP.PvPCommon.GapCloserActions.Any(a => a.Job.Contains(jobName))
                    ? "（本頁的位移技能改依距離放，下面「近戰內／外」相關的子選項不適用）"
                    : "";
                ImGui.TextColored(on ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey, $"　・{name}：{(on ? "已開啟" : "未開啟")}{note}");
            }
        }

        private static void DrawHeadingContents(string jobName)
        {
            foreach (var (preset, info) in groupedPresets[jobName].Where(x => PresetStorage.IsPvP(x.Preset)))
            {
                InfoBox presetBox = new() { Color = Colors.Grey, BorderThickness = 1f.Scale(), ContentsOffset = 8f.Scale(), ContentsAction = () => { Presets.DrawPreset(preset, info); } };

                if (Service.Configuration.HideConflictedCombos)
                {
                    var conflictOriginals = PresetStorage.GetConflicts(preset); // Presets that are contained within a ConflictedAttribute

                    if (!PresetStorage.HasConflictEntry(preset) || conflictOriginals.Length == 0)
                    {
                        presetBox.Draw();
                        ImGuiEx.Spacing(new Vector2(0, 12));
                        continue;
                    }

                    if (conflictOriginals.Any(PresetStorage.IsEnabled))
                    {
                        if (DateTime.UtcNow - LastPresetDeconflictTime > TimeSpan.FromSeconds(3))
                        {
                            if (Service.Configuration.DisableAction(preset))
                            {
                                PluginLog.Debug($"Removed {preset} due to conflict");
                                Service.Configuration.Save();
                            }
                            LastPresetDeconflictTime = DateTime.UtcNow;
                        }

                        // Keep removed items in the counter
                        var parent = PresetStorage.GetParent(preset) ?? preset;
                        currentPreset += 1 + Presets.AllChildren(presetChildren[parent]);
                    }

                    else
                    {
                        presetBox.Draw();
                        continue;
                    }
                }

                else
                {
                    presetBox.Draw();
                    ImGuiEx.Spacing(new Vector2(0, 12));
                }
            }
        }
    }
}
