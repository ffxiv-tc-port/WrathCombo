#region

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Common.Math;
using ImGuiNET;
using WrathCombo.Core;
using WrathCombo.Services;
using Vector4 = System.Numerics.Vector4;

#endregion

namespace WrathCombo.Window;

internal class MajorChangesWindow : Dalamud.Interface.Windowing.Window
{
    /// <summary>
    ///     Create a major changes window, with some settings about it.
    /// </summary>
    public MajorChangesWindow() : base("Wrath Combo | New Changes")
    {
        PluginLog.Debug(
            "MajorChangesWindow: " +
            $"IsVersionProblematic: {DoesVersionHaveChange}, " +
            $"IsSuggestionHiddenForThisVersion: {IsPopupHiddenForThisVersion}, " +
            $"WasUsingOldMouseOverConfigs: {WasUsingOldMouseOverConfigs}"
        );
        if (DoesVersionHaveChange &&
            !IsPopupHiddenForThisVersion)
            IsOpen = true;

        BringToFront();

        Flags = ImGuiWindowFlags.AlwaysAutoResize;
    }

    /// <summary>
    ///     Draw the settings change suggestion window.
    /// </summary>
    public override void Draw()
    {
        PadOutMinimumWidthFor("Wrath Combo | New Changes");

        #region MouseOver Options moved

        ImGuiEx.TextUnderlined("治疗职业的鼠标悬停选项已迁移！");
        if (WasUsingOldMouseOverConfigs)
            ImGuiEx.Text(ImGuiColors.DalamudYellow,
                "你正在使用其中一个选项！请仔细阅读！");
        ImGuiEx.Text(
            "各治疗职业治疗连击检查鼠标悬停的选项已被移除，\n" +
            "现已替换为一个全局鼠标悬停选项（以及一些新选项）。\n\n" +
            "你可以在以下位置找到这个新设置：\n" +
            "设置 > 'Target Options' > 'Heal Stack Customization Options'"
        );
        ImGui.NewLine();
        if (ImGui.Button("> 打开设置页面##majorSettings1"))
            P.OnOpenConfigUi();
        if (ImGui.Button("> 帮我启用新的UI鼠标悬停选项"))
        {
            Service.Configuration.UseUIMouseoverOverridesInDefaultHealStack = true;
            Service.Configuration.Save();
        }
        if (Service.Configuration.UseUIMouseoverOverridesInDefaultHealStack)
        {
            ImGui.SameLine();
            FontAwesome.Print(ImGuiColors.HealerGreen, FontAwesomeIcon.Check);
            ImGui.SameLine();
            ImGuiEx.Text($"已启用");
        }

        #endregion

        ImGuiEx.Spacing(new System.Numerics.Vector2(0, 10));
        ImGui.Separator();
        ImGuiEx.Spacing(new System.Numerics.Vector2(0, 10));

        #region Retargeting

        ImGuiEx.TextUnderlined("新功能：动作重定向！");
        ImGuiEx.Text(
            "动作重定向让我们能够根据The Balance的建议和你的选项，\n" +
            "为你选择动作的目标，而无需你自行\n" +
            "设置转移或反应技能。");
        ImGuiComponents.HelpMarker(
            "此前有一些功能（如占星术士的大地星）\n" +
            "需要转移或反应技能才能工作，单体治疗连击\n" +
            "会（可选地）依次检查鼠标悬停 > 软目标 > 硬目标的HP，\n" +
            "这可能与你的实际指向不一致，从而使用了'错误'的治疗技能。\n\n" +
            "动作重定向解决了这个问题！"
        );
        ImGuiEx.Text(
            "此外，我们还新增了控制治疗连击用于检查HP并选择\n" +
            "不同治疗技能的目标'堆叠'的能力，\n" +
            "以及一个将所有单体治疗动作也重定向到同一堆叠的选项。\n" +
            "（强烈推荐启用此'重定向治疗动作'选项！）");
        ImGuiEx.Text(
            "你可以在以下位置找到这些新设置：\n" +
            "Settings > 'Target Options'（以及折叠的 'Heal Stack Customization Options'）"
        );
        ImGui.NewLine();
        if (ImGui.Button("> 打开设置页面##majorSettings2"))
            P.OnOpenConfigUi();
        if (ImGui.Button("> 帮我启用重定向治疗动作选项"))
        {
            Service.Configuration.RetargetHealingActionsToStack = true;
            Service.Configuration.Save();
        }
        if (Service.Configuration.RetargetHealingActionsToStack)
        {
            ImGui.SameLine();
            FontAwesome.Print(ImGuiColors.HealerGreen, FontAwesomeIcon.Check);
            ImGui.SameLine();
            ImGuiEx.Text($"已启用");
        }
        ImGui.NewLine();
        ImGuiEx.Text(
            "你会看到新的符号，用于指示某功能的动作是否被重定向："
        );
        ImGuiEx.Text("视设置而定，可能会被重定向：");
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
                ImGui.Text(FontAwesomeIcon.Random.ToIconString());
        }
        ImGui.SameLine();
        ImGuiEx.Text("总是会被重定向：");
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen))
                ImGui.Text(FontAwesomeIcon.Random.ToIconString());
        }
        ImGui.NewLine();
        ImGuiEx.Text(ImGuiColors.DalamudYellow,
            "如果你之前为现已被重定向的动作配置了转移/反应技能，\n" +
            "或者启用了反应技能/Bossmod的瞬发地面目标选项，\n" +
            "你可能会想要禁用这些选项。");
        ImGuiEx.Text(
            "这包括占星术士卡牌、舞者舞伴，以及（如果启用：）\n" +
            "单体治疗动作");
        ImGuiComponents.HelpMarker(
            "治疗动作是否启用取决于你的个人偏好\n" +
            "（强烈推荐启用），但舞伴和卡牌现在比简单的动作重定向\n" +
            "更加智能（会遵循The Balance的优先级，\n" +
            "检查损伤降低等状态）。");

        #endregion

        #region Close and Do not Show again

        ImGuiEx.Spacing(new System.Numerics.Vector2(0, 20));
        ImGui.Separator();
        ImGuiHelpers.CenterCursorFor(
            ImGuiHelpers.GetButtonSize("关闭并不再显示").X
            //+ ImGui.GetStyle().ItemSpacing.X * 2
        );
        if (ImGui.Button("关闭并不再显示"))
        {
            Service.Configuration.HideMajorChangesForVersion = Version;
            Service.Configuration.Save();
            IsOpen = false;
        }

        #endregion

        if (_centeredWindow < 5)
            CenterWindow();
    }

    #region Minimum Width

    private void PadOutMinimumWidthFor(string windowName)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(0)))
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.Text(FontAwesomeIcon.CaretDown.ToIconString());
            }

            ImGui.SameLine();
            ImGui.Text(windowName);
            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.Text(FontAwesomeIcon.Bars.ToIconString());
            }

            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.Text(FontAwesomeIcon.Times.ToIconString());
            }
        }
    }

    #endregion

    #region Version Checking

    /// <summary>
    ///     The current plugin version.
    /// </summary>
    private static readonly Version Version =
        Svc.PluginInterface.Manifest.AssemblyVersion;

    /// <summary>
    ///     The version where the problem was introduced.
    /// </summary>
    private static readonly Version VersionWhereChangeIntroduced =
        new(1, 0, 1, 6);

    /// <summary>
    ///     Whether the current version is problematic.
    /// </summary>
    /// <remarks>No need to update this value to re-use this window.</remarks>
    private static readonly bool DoesVersionHaveChange =
        Version >= VersionWhereChangeIntroduced;

    /// <summary>
    ///     Whether the suggestion should be hidden for this version.
    /// </summary>
    private static readonly bool IsPopupHiddenForThisVersion =
        Service.Configuration.HideMajorChangesForVersion >= VersionWhereChangeIntroduced;

    #endregion

    #region Specific Info to Display for Update

    private static bool _getConfigValue(string config) =>
        PluginConfiguration.GetCustomBoolValue(config);

    /// <summary>
    ///     If the user was using MouseOver options.
    /// </summary>
    private static bool WasUsingOldMouseOverConfigs =>
        _getConfigValue("AST_ST_SimpleHeals_UIMouseOver") ||
        _getConfigValue("SCH_ST_Heal_UIMouseOver") ||
        _getConfigValue("SCH_DeploymentTactics_UIMouseOver") ||
        _getConfigValue("SGE_ST_Heal_UIMouseOver") ||
        _getConfigValue("WHM_STHeals_UIMouseOver");

    #endregion

    #region Window Centering

    private static int _centeredWindow;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(HandleRef hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left; // x position of upper-left corner
        public int Top; // y position of upper-left corner
        public int Right; // x position of lower-right corner
        public int Bottom; // y position of lower-right corner
        public Vector2 Position => new Vector2(Left, Top);
        public Vector2 Size => new Vector2(Right - Left, Bottom - Top);
    }

    /// <summary>
    ///     Centers the GUI window to the game window.
    /// </summary>
    private void CenterWindow()
    {
        // Get the pointer to the window handle.
        var hWnd = IntPtr.Zero;
        foreach (var pList in Process.GetProcesses())
            if (pList.ProcessName is "ffxiv_dx11" or "ffxiv")
                hWnd = pList.MainWindowHandle;

        // If failing to get the handle then abort.
        if (hWnd == IntPtr.Zero)
            return;

        // Get the game window rectangle
        GetWindowRect(new HandleRef(null, hWnd), out var rGameWindow);

        // Get the size of the current window.
        var vThisSize = ImGui.GetWindowSize();

        // Set the position.
        var centeredPosition = rGameWindow.Position + new Vector2(
            rGameWindow.Size.X / 2 - vThisSize.X / 2,
            rGameWindow.Size.Y / 2 - vThisSize.Y / 2);
        ImGui.SetWindowPos(centeredPosition);
        Position = centeredPosition;
        PositionCondition = ImGuiCond.Once;

        _centeredWindow++;
    }

    #endregion
}
