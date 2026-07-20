#region

using Dalamud.Interface.Colors;
using ECommons.ImGuiMethods;
using ImGuiNET;
using System.Linq;
using System.Numerics;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Data;
using WrathCombo.Extensions;
using WrathCombo.Services;
using WrathCombo.Window.Functions;
using Preset = WrathCombo.Combos.CustomComboPreset;

// ReSharper disable SwitchStatementMissingSomeEnumCasesNoDefault
// ReSharper disable InconsistentNaming
// ReSharper disable CheckNamespace
// ReSharper disable ClassNeverInstantiated.Global

#endregion

namespace WrathCombo.Combos.PvE;

internal partial class DNC
{
    internal static class Config
    {
        /// <summary>
        ///     Draw the Anti-Drift options for the Single-Target Standard Step
        ///     option.
        /// </summary>
        private static void DrawAntiDriftOptions()
        {
            ImGuiEx.Spacing(new Vector2(40, 12));
            ImGui.Text("防偏移选项：     （悬停以查看更多信息）");

            #region Show a colored display of the user's current detected GCD

            var color = GCDValue switch
            {
                GCDRange.Perfect => ImGuiColors.HealerGreen,
                GCDRange.NotGood => ImGuiColors.DalamudYellow,
                _ => ImGuiColors.DalamudRed,
            };
            ImGui.SameLine();
            ImGui.Text("GCD: " );
            ImGui.SameLine();
            ImGui.TextColored(color, $"{GCD:0.00}");
            ImGui.NewLine();
            #endregion

            var t = ImGui.GetCursorPos();
            const string texTrip = "强制三重穿插";
            UserConfig.DrawRadioButton(
                DNC_ST_ADV_AntiDrift, texTrip,
                "在非开幕爆发窗口期间强制三重穿插使用华丽/扇舞·急+扇舞·骤。" +
                "\n修复在SS/FM剩0.5秒冷却时使用GCD导致的偏移问题。" +
                "\n推荐的防偏移选项。",
                outputValue: (int) AntiDrift.TripleWeave, descriptionAsTooltip: true);
            var h = ImGui.GetCursorPos();
            const string texHold = "标准步前预留等待";
            UserConfig.DrawRadioButton(
                DNC_ST_ADV_AntiDrift, texHold,
                "如果标准步将在你下一个GCD前冷却完成，会预留GCD等待。" +
                "\n这会造成一定的空转时间。" +
                "\n仅在拥有额外技速时推荐，但可作为防偏移选项使用。",
                outputValue: (int) AntiDrift.Hold, descriptionAsTooltip: true);
            UserConfig.DrawRadioButton(
                DNC_ST_ADV_AntiDrift, "两者皆用",
                "将同时使用以上两个选项。" +
                "\n这会造成一定的空转时间。" +
                "\n不推荐，但若以上选项都不适合你可以作为解决方案。",
                outputValue: (int) AntiDrift.Both, descriptionAsTooltip: true);
            UserConfig.DrawRadioButton(
                DNC_ST_ADV_AntiDrift, "皆不使用",
                "不使用任何防偏移选项。" +
                "\n这会导致偏移。不推荐。",
                outputValue: (int) AntiDrift.None, descriptionAsTooltip: true);

            #region Show recommended setting, based on GCD

            // Save the current cursor position
            var pos = ImGui.GetCursorPos();

            // Determine which recommendation text to show
            const string rec = "（推荐）";
            var recTriple = GCDValue is GCDRange.Perfect ? rec : "";
            var recHold = GCDValue is not GCDRange.Perfect ? rec : "";

            // Set the position of (any) Triple-Weave recommendation text
            var texSize = ImGui.CalcTextSize(texHold);
            ImGui.SetCursorPos(
                t with { X = t.X + texSize.X + 110f.Scale(), Y = t.Y - texSize.Y - 2f.Scale() });
            ImGui.TextColored(ImGuiColors.DalamudGrey, recTriple);

            // Set the position of (any) Hold recommendation text
            ImGui.SetCursorPos(
                h with { X = h.X + texSize.X + 110f.Scale(), Y = h.Y - 2f.Scale() });
            ImGui.TextColored(ImGuiColors.DalamudGrey, recHold);

            // Reset to where the cursor was
            ImGui.SetCursorPos(pos);

            #endregion
        }

        private static void DrawPartnerInfo()
        {
            ImGuiEx.TextWrapped(ImGuiColors.DalamudGrey,
                "这将检查你的队伍成员，并根据The Balance的优先级列表以及复生虚弱、损伤降低等状态，选择最理想的舞伴。");
        }

        internal static void Draw(Preset preset)
        {
            switch (preset)
            {
                case Preset.DNC_CustomDanceSteps:
                    ImGui.Indent(35f.Scale());

                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                    ImGui.TextWrapped(
                        "此功能的设置不提供支持！");
                    ImGui.PopStyleColor();

                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                    ImGui.TextWrapped(
                        "\n你可以通过为每个舞步输入技能ID来更改相应的动作。" +
                        "\n默认值为瀑泻、百花争艳、扇舞·序和扇舞·破。" +
                        "\n如果设置为0，它们将重置为这些技能。" +
                        "\n（你可以通过Garland Tools搜索技能并点击齿轮图标获取技能ID。）");
                    ImGui.PopStyleColor();

                    int[] actions = Service.Configuration.DancerDanceCompatActionIDs
                        .Select(x => (int) x).ToArray();

                    bool inputChanged = false;
                    ImGuiEx.SetNextItemWidthScaled(50);
                    inputChanged |= ImGui.InputInt(
                        "（红色）蔷薇曲脚步 替换技能ID",
                        ref actions[0], 0);
                    ImGuiEx.SetNextItemWidthScaled(50);
                    inputChanged |= ImGui.InputInt(
                        "（蓝色）小鸟交叠跳 替换技能ID",
                        ref actions[1], 0);
                    ImGuiEx.SetNextItemWidthScaled(50);
                    inputChanged |= ImGui.InputInt(
                        "（绿色）绿叶小踢腿 替换技能ID",
                        ref actions[2], 0);
                    ImGuiEx.SetNextItemWidthScaled(50);
                    inputChanged |= ImGui.InputInt(
                        "（黄色）金冠趾尖转 替换技能ID",
                        ref actions[3], 0);

                    ImGuiEx.Spacing(new Vector2(0, 12));

                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                    ImGui.TextWrapped(
                        "这可能会导致冲突！");
                    ImGui.PopStyleColor();
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                    ImGui.TextWrapped("请仔细检查你设置的技能是否与你使用的其他连击冲突，或启用以下功能！");
                    ImGui.PopStyleColor();

                    if (inputChanged)
                    {
                        Service.Configuration.DancerDanceCompatActionIDs = actions
                            .Select(x => (uint) x).ToArray();
                        Service.Configuration.Save();
                    }

                    ImGui.Unindent(35f.Scale());
                    ImGui.Spacing();

                    break;

                #region Advanced Single Target UI

                case Preset.DNC_ST_BalanceOpener:
                    ImGui.Indent();
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                    ImGui.TextWrapped(
                        "起手式变体：     （悬停以查看更多信息）");
                    ImGui.PopStyleColor();
                    ImGui.Unindent();

                    ImGui.NewLine();
                    UserConfig.DrawRadioButton(DNC_ST_OpenerSelection,
                        "标准：15秒倒数",
                        "需要至少15秒冷却\n且在15秒时开始标准步。",
                        (int)Openers.FifteenSecond, descriptionAsTooltip: true);
                    UserConfig.DrawRadioButton(DNC_ST_OpenerSelection,
                        "标准：7秒倒数",
                        "需要至少7秒冷却\n且在7秒时开始标准步。\n表现比15秒差。",
                        (int)Openers.SevenSecond, descriptionAsTooltip: true);
                    UserConfig.DrawRadioButton(DNC_ST_OpenerSelection,
                        "技巧：30秒倒数",
                        "需要30秒冷却\n且在30秒时开始标准步。\n通常不推荐。\nBuff对齐效果比标准15秒差。",
                        (int)Openers.ThirtySecondTech, descriptionAsTooltip: true);
                    UserConfig.DrawRadioButton(DNC_ST_OpenerSelection,
                        "技巧：7+秒倒数",
                        "需要至少7秒冷却\n且需事先完成标准步。\n不包含飞燕回风。\n不会为你自动使用标准步。\n通常不推荐。\nBuff对齐效果比标准7秒差。",
                        (int)Openers.SevenPlusSecondTech, descriptionAsTooltip: true);
                    UserConfig.DrawRadioButton(DNC_ST_OpenerSelection,
                        "技巧：7秒倒数",
                        "需要至少7秒冷却\n且在7秒时开始技巧步。\n不推荐。",
                        (int)Openers.SevenSecondTech, descriptionAsTooltip: true);

                    ImGui.Indent();
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudGrey);
                    ImGui.TextWrapped(
                        "起手式选项：");
                    ImGui.PopStyleColor();

                    UserConfig.DrawAdditionalBoolChoice(DNC_ST_OpenerOption_Peloton,
                        $"包含 {Peloton.ActionName()}", "");

                    UserConfig.DrawBossOnlyChoice(DNC_ST_OpenerDifficulty, "选择在哪类内容中使用此起手式：");
                    ImGui.Unindent();

                    break;

                case Preset.DNC_ST_Adv_PartnerAuto:
                    UserConfig.DrawAdditionalBoolChoice(DNC_Partner_FocusOverride,
                        "优先聚焦目标##DPFocusOver0",
                        "如果你有一个在范围内的聚焦目标，将优先于The Balance建议的舞伴。",
                        indentDescription: true);

                    break;

                case Preset.DNC_ST_Adv_AutoPartner:
                    ImGui.Indent(29f.Scale());
                    DrawPartnerInfo();
                    ImGui.Unindent(29f.Scale());

                    UserConfig.DrawAdditionalBoolChoice(DNC_Partner_FocusOverride,
                        "优先聚焦目标##DPFocusOver1",
                        "如果你有一个在范围内、存活、且没有复生虚弱或损伤降低的聚焦目标，将优先于The Balance建议的舞伴。",
                        indentDescription: true);

                    break;

                case Preset.DNC_ST_EspritOvercap:
                    UserConfig.DrawSliderInt(50, 100, DNCEspritThreshold_ST,
                        "气魄",
                        itemWidth: 150f, sliderIncrement: SliderIncrements.Fives);

                    break;

                case Preset.DNC_ST_Adv_SS:
                    UserConfig.DrawSliderInt(0, 15, DNC_ST_Adv_SSBurstPercent,
                        "目标HP%低于此值时停止使用标准步",
                        itemWidth: 75f, sliderIncrement: SliderIncrements.Fives);

                    ImGuiEx.Spacing(new Vector2(30, 0));
                    UserConfig.DrawHorizontalRadioButton(
                        DNC_ST_ADV_SS_IncludeSS,
                        "包含标准步",
                        "将把标准步本身、" +
                        "\n舞步、以及终曲都包含进循环中。",
                        outputValue: (int) IncludeStep.Yes,
                        itemWidth: 125f);
                    UserConfig.DrawHorizontalRadioButton(
                        DNC_ST_ADV_SS_IncludeSS,
                        "排除标准步",
                        "仅包含舞步和终曲；" +
                        "\n你需要手动按下标准步。",
                        outputValue: (int) IncludeStep.No,
                        itemWidth: 125f);

                    DrawAntiDriftOptions();

                    break;

                case Preset.DNC_ST_Adv_TS:
                    UserConfig.DrawSliderInt(0, 15, DNC_ST_Adv_TSBurstPercent,
                        "目标HP%低于此值时停止使用技巧步",
                        itemWidth: 75f, sliderIncrement: SliderIncrements.Fives);

                    ImGuiEx.Spacing(new Vector2(30, 0));
                    UserConfig.DrawHorizontalRadioButton(
                        DNC_ST_ADV_TS_IncludeTS,
                        "包含技巧步",
                        "将把技巧步本身、" +
                        "\n舞步、以及终曲都包含进循环中。",
                        outputValue: (int) IncludeStep.Yes,
                        itemWidth: 125f);
                    UserConfig.DrawHorizontalRadioButton(
                        DNC_ST_ADV_TS_IncludeTS,
                        "排除技巧步",
                        "仅包含舞步和终曲；" +
                        "\n你需要手动按下技巧步。",
                        outputValue: (int) IncludeStep.No,
                        itemWidth: 125f);

                    DrawAntiDriftOptions();

                    break;

                case Preset.DNC_ST_Adv_Feathers:
                    UserConfig.DrawSliderInt(0, 5, DNC_ST_Adv_FeatherBurstPercent,
                        "目标HP%低于此值时倾泻所有储蓄的羽毛",
                        itemWidth: 75f);

                    break;

                case Preset.DNC_ST_Adv_Tillana:
                    ImGui.Indent();
                    UserConfig.DrawHorizontalRadioButton(
                        DNC_ST_ADV_TillanaUse,
                        "正常使用蒂拉纳",
                        "将按照The Balance建议使用蒂拉纳" +
                        "\n可能导致蒂拉纳偏移出爆发窗口。",
                        outputValue: (int) TillanaDriftProtection.None,
                        itemWidth: 125f);
                    UserConfig.DrawHorizontalRadioButton(
                        DNC_ST_ADV_TillanaUse,
                        "优先蒂拉纳而非气魄",
                        "即使气魄高于50，也会优先使用蒂拉纳而非军刀或黎明之舞。" +
                        "\n可防止蒂拉纳偏移出爆发窗口。" +
                        "\n应与军刀舞的气魄滑块设为>50搭配使用。" +
                        "\n不推荐。",
                        outputValue: (int) TillanaDriftProtection.Favor,
                        itemWidth: 125f);
                    ImGui.Unindent();

                    break;

                case Preset.DNC_ST_Adv_SaberDance:
                    UserConfig.DrawSliderInt(50, 100,
                        DNC_ST_Adv_SaberThreshold,
                        "气魄",
                        itemWidth: 150f, sliderIncrement: SliderIncrements.Fives);

                    break;

                case Preset.DNC_ST_Adv_PanicHeals:
                    UserConfig.DrawSliderInt(0, 80,
                        DNC_ST_Adv_PanicHealWaltzPercent,
                        "愈疗之华HP%",
                        itemWidth: 200f, sliderIncrement: SliderIncrements.Fives);

                    UserConfig.DrawSliderInt(0, 80, DNC_ST_Adv_PanicHealWindPercent,
                        "续气HP%",
                        itemWidth: 200f, sliderIncrement: SliderIncrements.Fives);

                    break;

                #endregion

                #region Advanced AoE UI

                case Preset.DNC_AoE_EspritOvercap:
                    UserConfig.DrawSliderInt(50, 100, DNCEspritThreshold_AoE,
                        "气魄",
                        itemWidth: 150f, sliderIncrement: SliderIncrements.Fives);

                    break;

                case Preset.DNC_AoE_Adv_SS:
                    UserConfig.DrawSliderInt(0, 60, DNC_AoE_Adv_SSBurstPercent,
                        "目标HP%低于此值时停止使用标准步",
                        itemWidth: 75f, sliderIncrement: SliderIncrements.Fives);

                    ImGuiEx.Spacing(new Vector2(30, 0));
                    UserConfig.DrawHorizontalRadioButton(
                        DNC_AoE_Adv_SS_IncludeSS,
                        "包含标准步",
                        "将把标准步本身、" +
                        "\n舞步、以及终曲都包含进循环中。",
                        outputValue: (int) IncludeStep.Yes,
                        itemWidth: 125f);
                    UserConfig.DrawHorizontalRadioButton(
                        DNC_AoE_Adv_SS_IncludeSS,
                        "排除标准步",
                        "仅包含舞步和终曲；" +
                        "\n你需要手动按下标准步。",
                        outputValue: (int) IncludeStep.No,
                        itemWidth: 125f);

                    break;

                case Preset.DNC_AoE_Adv_TS:
                    UserConfig.DrawSliderInt(0, 60, DNC_AoE_Adv_TSBurstPercent,
                        "目标HP%低于此值时停止使用技巧步",
                        itemWidth: 75f, sliderIncrement: SliderIncrements.Fives);

                    ImGuiEx.Spacing(new Vector2(30, 0));
                    UserConfig.DrawHorizontalRadioButton(
                        DNC_AoE_Adv_TS_IncludeTS,
                        "包含技巧步",
                        "将把技巧步本身、" +
                        "\n舞步、以及终曲都包含进循环中。",
                        outputValue: (int) IncludeStep.Yes,
                        itemWidth: 125f);
                    UserConfig.DrawHorizontalRadioButton(
                        DNC_AoE_Adv_TS_IncludeTS,
                        "排除技巧步",
                        "仅包含舞步和终曲；" +
                        "\n你需要手动按下技巧步。",
                        outputValue: (int) IncludeStep.No,
                        itemWidth: 125f);

                    break;

                case Preset.DNC_AoE_Adv_SaberDance:
                    UserConfig.DrawSliderInt(50, 100, DNC_AoE_Adv_SaberThreshold,
                        "气魄",
                        itemWidth: 150f, sliderIncrement: SliderIncrements.Fives);

                    break;

                case Preset.DNC_AoE_Adv_PanicHeals:
                    UserConfig.DrawSliderInt(0, 80,
                        DNC_AoE_Adv_PanicHealWaltzPercent,
                        "愈疗之华HP%",
                        itemWidth: 200f, sliderIncrement: SliderIncrements.Fives);

                    UserConfig.DrawSliderInt(0, 80,
                        DNC_AoE_Adv_PanicHealWindPercent,
                        "续气HP%",
                        itemWidth: 200f, sliderIncrement: SliderIncrements.Fives);

                    break;

                #endregion

                case Preset.DNC_DesirablePartner:
                    ImGui.Indent(35f.Scale());
                    DrawPartnerInfo();
                    ImGui.Unindent(35f.Scale());
                    ImGuiEx.Spacing(new Vector2(0, 12));

                    UserConfig.DrawAdditionalBoolChoice(DNC_Partner_FocusOverride,
                        "优先聚焦目标##DPFocusOver2",
                        "如果你有一个在范围内、存活、且没有复生虚弱或损伤降低的聚焦目标，将优先于The Balance建议的舞伴。",
                        indentDescription: true);

                    ImGuiEx.Spacing(new Vector2(29, 12));
                    ImGui.Text("舞伴最佳时显示的动作：     (悬停查看详情)");
                    ImGui.NewLine();
                    UserConfig.DrawRadioButton(
                        DNC_Partner_ActionToShow, "由游戏决定",
                        "不会更改FFXIV在快捷栏中放置的动作。\n" +
                        "当你有舞伴时，会照常显示终舞。\n\n" +
                        "这是默认行为。",
                        outputValue: (int)PartnerShowAction.Default,
                        descriptionAsTooltip: true);
                    UserConfig.DrawRadioButton(
                        DNC_Partner_ActionToShow, "华丽舞姿",
                        "当前舞伴最佳时会显示华丽舞姿。\n" +
                        "这会阻止你使用华丽舞姿或终舞\n(除非你硬指向舞伴以外的友方)。\n\n" +
                        "比蛮神之剑选项干扰更小。",
                        outputValue: (int)PartnerShowAction.ClosedPosition,
                        descriptionAsTooltip: true);
                    UserConfig.DrawRadioButton(
                        DNC_Partner_ActionToShow, "蛮神之剑",
                        "当前舞伴最佳时会显示蛮神之剑。\n" +
                        "蛮神之剑是一个已移除的动作，我们用它来阻挡输入。\n" +
                        "这会阻止你使用华丽舞姿或终舞。\n\n" +
                        "这是推荐选项，可避免你误切换舞伴。",
                        outputValue: (int)PartnerShowAction.SavageBlade,
                        descriptionAsTooltip: true);

                    break;

                case Preset.DNC_Variant_Cure:
                    UserConfig.DrawSliderInt(1, 80, DNCVariantCurePercent,
                        "HP%等于或低于此值",
                        itemWidth: 200f, sliderIncrement: SliderIncrements.Fives);

                    break;

            }
        }

        #region Constants

        public enum Openers
        {
            FifteenSecond,
            SevenSecond,
            ThirtySecondTech,
            SevenPlusSecondTech,
            SevenSecondTech,
        }

        public enum IncludeStep
        {
            No,
            Yes,
        }

        public enum TillanaDriftProtection
        {
            None,
            Favor,
        }

        public enum AntiDrift
        {
            None,
            TripleWeave,
            Hold,
            Both,
        }

        #endregion

        #region Options

        #region Advanced Single Target

        /// <summary>
        ///     Difficulty of Opener for Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: <see cref="ContentCheck.IsInBossOnlyContent" /> <br />
        ///     <b>Options</b>: All Content or
        ///     <see cref="ContentCheck.IsInBossOnlyContent" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_BalanceOpener" />
        public static readonly UserBoolArray DNC_ST_OpenerDifficulty =
            new("DNC_ST_OpenerDifficulty", [false, true]);

        /// <summary>
        ///     Opener selection for Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: <see cref="Openers.FifteenSecond" /> <br />
        ///     <b>Options</b>: <see cref="Openers">Openers Enum</see>
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_BalanceOpener" />
        public static readonly UserInt DNC_ST_OpenerSelection =
            new("DNC_ST_OpenerSelection", (int) Openers.FifteenSecond);

        /// <summary>
        ///     Whether to include Peloton in the opener.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: <see langword="true"/><br />
        ///     <b>Options</b>: <see langword="true"/> or <see langword="false"/>
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_BalanceOpener" />
        public static readonly UserBool DNC_ST_OpenerOption_Peloton =
            new("DNC_ST_OpenerOption_Peloton", true);

        /// <summary>
        ///     Esprit threshold for Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 50 <br />
        ///     <b>Range</b>: 50 - 100 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_EspritOvercap" />
        public static readonly UserInt DNCEspritThreshold_ST =
            new("DNCEspritThreshold_ST", 50);

        /// <summary>
        ///     Target HP% to use Standard Step above for Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 0 <br />
        ///     <b>Range</b>: 0 - 15 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_Adv_SS" />
        public static readonly UserInt DNC_ST_Adv_SSBurstPercent =
            new("DNC_ST_Adv_SSBurstPercent", 0);

        /// <summary>
        ///     Include Standard Step in rotation for Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: <see cref="IncludeStep.Yes" /> <br />
        ///     <b>Options</b>: <see cref="IncludeStep">IncludeStep Enum</see>
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_Adv_SS" />
        public static readonly UserInt DNC_ST_ADV_SS_IncludeSS =
            new("DNC_ST_ADV_SS_IncludeSS", (int) IncludeStep.Yes);

        /// <summary>
        ///     Anti-Drift choice for Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: <see cref="AntiDrift.TripleWeave" /> <br />
        ///     <b>Options</b>: <see cref="AntiDrift">AntiDrift Enum</see>
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_Adv_SS" />
        public static readonly UserInt DNC_ST_ADV_AntiDrift =
            new("DNC_ST_ADV_AntiDrift", (int) AntiDrift.TripleWeave);

        /// <summary>
        ///     Include Technical Step in rotation for Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: <see cref="IncludeStep.Yes" /> <br />
        ///     <b>Options</b>: <see cref="IncludeStep">IncludeStep Enum</see>
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_Adv_TS" />
        public static readonly UserInt DNC_ST_ADV_TS_IncludeTS =
            new("DNC_ST_ADV_TS_IncludeTS", (int) IncludeStep.Yes);

        /// <summary>
        ///     Target HP% to use Technical Step above for Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 0 <br />
        ///     <b>Range</b>: 0 - 15 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_Adv_TS" />
        public static readonly UserInt DNC_ST_Adv_TSBurstPercent =
            new("DNC_ST_Adv_TSBurstPercent", 0);

        /// <summary>
        ///     Target HP% to dump all pooled feathers below for Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 0 <br />
        ///     <b>Range</b>: 0 - 5 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Ones" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_Adv_Feathers" />
        public static readonly UserInt DNC_ST_Adv_FeatherBurstPercent =
            new("DNC_ST_Adv_FeatherBurstPercent", 0);

        /// <summary>
        ///     Tillana drift protection for Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: <see cref="TillanaDriftProtection.None" /> <br />
        ///     <b>Options</b>: <see cref="TillanaDriftProtection" /> Enum.
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_Adv_Tillana" />
        public static readonly UserInt DNC_ST_ADV_TillanaUse =
            new("DNC_ST_ADV_TillanaUse", (int) TillanaDriftProtection.None);

        /// <summary>
        ///     Esprit threshold for Saber Dance in Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 50 <br />
        ///     <b>Range</b>: 50 - 100 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_Adv_SaberDance" />
        public static readonly UserInt DNC_ST_Adv_SaberThreshold =
            new("DNC_ST_Adv_SaberThreshold", 50);

        /// <summary>
        ///     Player HP% threshold for Curing Waltz in Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 30 <br />
        ///     <b>Range</b>: 0 - 80 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_Adv_PanicHeals" />
        public static readonly UserInt DNC_ST_Adv_PanicHealWaltzPercent =
            new("DNC_ST_Adv_PanicHealWaltzPercent", 30);

        /// <summary>
        ///     Player HP% threshold for Second Wind in Single Target.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 20 <br />
        ///     <b>Range</b>: 0 - 80 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_ST_Adv_PanicHeals" />
        public static readonly UserInt DNC_ST_Adv_PanicHealWindPercent =
            new("DNC_ST_Adv_PanicHealWindPercent", 20);

        #endregion

        #region Advanced AoE

        /// <summary>
        ///     Esprit threshold for AoE.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 50 <br />
        ///     <b>Range</b>: 50 - 100 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_AoE_EspritOvercap" />
        public static readonly UserInt DNCEspritThreshold_AoE =
            new("DNCEspritThreshold_AoE", 50);

        /// <summary>
        ///     Target HP% to use Standard Step above for AoE.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 40 <br />
        ///     <b>Range</b>: 0 - 60 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_AoE_Adv_SS" />
        public static readonly UserInt DNC_AoE_Adv_SSBurstPercent =
            new("DNC_AoE_Adv_SSBurstPercent", 40);

        /// <summary>
        ///     Include Standard Step in rotation for AoE.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: <see cref="IncludeStep.Yes" /> <br />
        ///     <b>Options</b>: <see cref="IncludeStep">IncludeStep Enum</see>
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_AoE_Adv_SS" />
        public static readonly UserInt DNC_AoE_Adv_SS_IncludeSS =
            new("DNC_AoE_Adv_SS_IncludeSS", (int) IncludeStep.Yes);

        /// <summary>
        ///     Target HP% to use Technical Step above for AoE.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 40 <br />
        ///     <b>Range</b>: 0 - 60 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_AoE_Adv_TS" />
        public static readonly UserInt DNC_AoE_Adv_TSBurstPercent =
            new("DNC_AoE_Adv_TSBurstPercent", 40);

        /// <summary>
        ///     Include Technical Step in rotation for AoE.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: <see cref="IncludeStep.Yes" /> <br />
        ///     <b>Options</b>: <see cref="IncludeStep">IncludeStep Enum</see>
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_AoE_Adv_TS" />
        public static readonly UserInt DNC_AoE_Adv_TS_IncludeTS =
            new("DNC_AoE_Adv_TS_IncludeTS", (int) IncludeStep.Yes);

        /// <summary>
        ///     Esprit threshold for Saber Dance in AoE.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 50 <br />
        ///     <b>Range</b>: 50 - 100 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_AoE_Adv_SaberDance" />
        public static readonly UserInt DNC_AoE_Adv_SaberThreshold =
            new("DNC_AoE_Adv_SaberThreshold", 50);

        /// <summary>
        ///     Player HP% threshold for Curing Waltz in AoE.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 30 <br />
        ///     <b>Range</b>: 0 - 80 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_AoE_Adv_PanicHeals" />
        public static readonly UserInt DNC_AoE_Adv_PanicHealWaltzPercent =
            new("DNC_AoE_Adv_PanicHealWaltzPercent", 30);

        /// <summary>
        ///     Player HP% threshold for Second Wind in AoE.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 20 <br />
        ///     <b>Range</b>: 0 - 80 <br />
        ///     <b>Step</b>: <see cref="SliderIncrements.Fives" />
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_AoE_Adv_PanicHeals" />
        public static readonly UserInt DNC_AoE_Adv_PanicHealWindPercent =
            new("DNC_AoE_Adv_PanicHealWindPercent", 20);

        #endregion

        #region Smaller Features

        /// <summary>
        ///     Whether the Focus Target should override the desired partner, while
        ///     still valid.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: false
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_DesirablePartner" />
        public static readonly UserBool DNC_Partner_FocusOverride =
            new("DNC_Partner_FocusOverride", false);

        public enum PartnerShowAction
        {
            Default,
            ClosedPosition,
            SavageBlade,
        }

        /// <summary>
        ///     What action should be shown on the hotbar when the current dance
        ///     partner is considered optimal.
        /// </summary>
        /// <value>
        ///     Default: 0 <br />
        ///     Options: <see cref="PartnerShowAction" /> Enum
        /// </value>
        public static readonly UserInt DNC_Partner_ActionToShow =
            new("DNC_Partner_ActionToShow", (int)PartnerShowAction.Default);

        #endregion

        /// <summary>
        ///     HP% threshold for Variant Cure.
        /// </summary>
        /// <value>
        ///     <b>Default</b>: 1 <br />
        ///     <b>Range</b>: 1 - 80
        /// </value>
        /// <seealso cref="CustomComboPreset.DNC_Variant_Cure" />
        public static readonly UserInt DNCVariantCurePercent =
            new("DNCVariantCurePercent", 20);

        #endregion
    }
}
