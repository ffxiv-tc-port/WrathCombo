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

        ImGuiEx.TextUnderlined("治療職業的滑鼠懸停選項已遷移！");
        if (WasUsingOldMouseOverConfigs)
            ImGuiEx.Text(ImGuiColors.DalamudYellow,
                "你正在使用其中一個選項！請仔細閱讀！");
        ImGuiEx.Text(
            "各治療職業治療連擊檢查滑鼠懸停的選項已被移除，\n" +
            "現已替換為一個全域滑鼠懸停選項（以及一些新選項）。\n\n" +
            "你可以在以下位置找到這個新設定：\n" +
            "設定 > 'Target Options' > 'Heal Stack Customization Options'"
        );
        ImGui.NewLine();
        if (ImGui.Button("> 開啟設定頁面##majorSettings1"))
            P.OnOpenConfigUi();
        if (ImGui.Button("> 幫我啟用新的UI滑鼠懸停選項"))
        {
            Service.Configuration.UseUIMouseoverOverridesInDefaultHealStack = true;
            Service.Configuration.Save();
        }
        if (Service.Configuration.UseUIMouseoverOverridesInDefaultHealStack)
        {
            ImGui.SameLine();
            FontAwesome.Print(ImGuiColors.HealerGreen, FontAwesomeIcon.Check);
            ImGui.SameLine();
            ImGuiEx.Text($"已啟用");
        }

        #endregion

        ImGuiEx.Spacing(new System.Numerics.Vector2(0, 10));
        ImGui.Separator();
        ImGuiEx.Spacing(new System.Numerics.Vector2(0, 10));

        #region Retargeting

        ImGuiEx.TextUnderlined("新功能：動作重定向！");
        ImGuiEx.Text(
            "動作重定向讓我們能夠根據The Balance的建議和你的選項，\n" +
            "為你選擇動作的目標，而無需你自行\n" +
            "設定轉移或反應技能。");
        ImGuiComponents.HelpMarker(
            "此前有一些功能（如占星術士的大地星）\n" +
            "需要轉移或反應技能才能工作，單體治療連擊\n" +
            "會（可選地）依次檢查滑鼠懸停 > 軟目標 > 硬目標的HP，\n" +
            "這可能與你的實際指向不一致，從而使用了'錯誤'的治療技能。\n\n" +
            "動作重定向解決了這個問題！"
        );
        ImGuiEx.Text(
            "此外，我們還新增了控制治療連擊用於檢查HP並選擇\n" +
            "不同治療技能的目標'堆疊'的能力，\n" +
            "以及一個將所有單體治療動作也重定向到同一堆疊的選項。\n" +
            "（強烈推薦啟用此'重定向治療動作'選項！）");
        ImGuiEx.Text(
            "你可以在以下位置找到這些新設定：\n" +
            "Settings > 'Target Options'（以及摺疊的 'Heal Stack Customization Options'）"
        );
        ImGui.NewLine();
        if (ImGui.Button("> 開啟設定頁面##majorSettings2"))
            P.OnOpenConfigUi();
        if (ImGui.Button("> 幫我啟用重定向治療動作選項"))
        {
            Service.Configuration.RetargetHealingActionsToStack = true;
            Service.Configuration.Save();
        }
        if (Service.Configuration.RetargetHealingActionsToStack)
        {
            ImGui.SameLine();
            FontAwesome.Print(ImGuiColors.HealerGreen, FontAwesomeIcon.Check);
            ImGui.SameLine();
            ImGuiEx.Text($"已啟用");
        }
        ImGui.NewLine();
        ImGuiEx.Text(
            "你會看到新的符號，用於指示某功能的動作是否被重定向："
        );
        ImGuiEx.Text("視設定而定，可能會被重定向：");
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudYellow))
                ImGui.Text(FontAwesomeIcon.Random.ToIconString());
        }
        ImGui.SameLine();
        ImGuiEx.Text("總是會被重定向：");
        ImGui.SameLine();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen))
                ImGui.Text(FontAwesomeIcon.Random.ToIconString());
        }
        ImGui.NewLine();
        ImGuiEx.Text(ImGuiColors.DalamudYellow,
            "如果你之前為現已被重定向的動作配置了轉移/反應技能，\n" +
            "或者啟用了反應技能/Bossmod的瞬發地面目標選項，\n" +
            "你可能會想要禁用這些選項。");
        ImGuiEx.Text(
            "這包括占星術士卡牌、舞者舞伴，以及（如果啟用：）\n" +
            "單體治療動作");
        ImGuiComponents.HelpMarker(
            "治療動作是否啟用取決於你的個人偏好\n" +
            "（強烈推薦啟用），但舞伴和卡牌現在比簡單的動作重定向\n" +
            "更加智慧（會遵循The Balance的優先順序，\n" +
            "檢查損傷降低等狀態）。");

        #endregion

        #region Close and Do not Show again

        ImGuiEx.Spacing(new System.Numerics.Vector2(0, 20));
        ImGui.Separator();
        ImGuiHelpers.CenterCursorFor(
            ImGuiHelpers.GetButtonSize("關閉並不再顯示").X
            //+ ImGui.GetStyle().ItemSpacing.X * 2
        );
        if (ImGui.Button("關閉並不再顯示"))
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
