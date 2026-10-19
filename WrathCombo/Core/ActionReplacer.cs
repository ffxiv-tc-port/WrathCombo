#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using ECommons;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using WrathCombo.Combos.PvE;
using WrathCombo.CustomComboNS;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Extensions;
using WrathCombo.Services;

#endregion

namespace WrathCombo.Core;

/// <summary> This class facilitates action+icon replacement. </summary>
internal sealed class ActionReplacer : IDisposable
{
    public delegate uint GetActionDelegate(IntPtr actionManager, uint actionID);

    public readonly List<CustomCombo> CustomCombos;
    // 兩個 hook 都可能是 null：遊戲改版後解不到時只停掉該項（見 GameFunctionGuard），插件照常載入。
    public readonly Hook<GetActionDelegate>? getActionHook;

    private readonly Hook<IsActionReplaceableDelegate>? isActionReplaceableHook;

    public readonly Dictionary<uint, uint> LastActionInvokeFor = [];

    private readonly ActionThrottle _actionThrottle = new();

    /// <summary>
    ///     Critical for the hook, do not remove or modify.
    /// </summary>
    // ReSharper disable once FieldCanBeMadeReadOnly.Local
    private IntPtr _actionManager = IntPtr.Zero;

    /// <summary> Initializes a new instance of the <see cref="ActionReplacer" /> class. </summary>
    public ActionReplacer()
    {
        CustomCombos = Assembly.GetAssembly(typeof(CustomCombo))!.GetTypes()
            .Where(t => !t.IsAbstract && t.BaseType == typeof(CustomCombo))
            .Select(Activator.CreateInstance)
            .Cast<CustomCombo>()
            .OrderByDescending(x => x.Preset)
            .ToList();

        // ReSharper disable once RedundantCast
        // Must keep the nint cast
        getActionHook = GameFunctionGuard.HookFromAddress<GetActionDelegate>((nint) ActionManager.Addresses.GetAdjustedActionId.Value, GetAdjustedActionDetour,
            "ActionManager.GetAdjustedActionId", "快捷列的技能替換；循環判斷也會失準（拿不到技能目前變成哪一個）");
        isActionReplaceableHook = GameFunctionGuard.HookFromAddress<IsActionReplaceableDelegate>(Service.Address.IsActionIdReplaceable, IsActionReplaceableDetour,
            "IsActionIdReplaceable", "部分技能的快捷列圖示不再跟著組合變化");

        getActionHook?.Enable();
        isActionReplaceableHook?.Enable();
    }

    public void Dispose()
    {
        getActionHook?.Disable();
        getActionHook?.Dispose();
        isActionReplaceableHook?.Disable();
        isActionReplaceableHook?.Dispose();
    }

    private ulong IsActionReplaceableDetour(uint actionID) => 1;

    /// <summary> Calls the original hook. </summary>
    /// <param name="actionID"> Action ID. </param>
    /// <returns> The result from the hook. hook 沒解到時原樣回 <paramref name="actionID" />（不呼叫任何原生函式）。 </returns>
    internal uint OriginalHook(uint actionID) =>
        getActionHook is { } hook
            ? hook.OriginalDisposeSafe(_actionManager, actionID)
            : actionID;

#pragma warning disable CS1573
    /// <summary>
    ///     Throttles access to <see cref="GetAdjustedAction(uint)" />.
    /// </summary>
    /// <param name="actionID">The action a combo replaces.</param>
    /// <returns>The action a combo returns.</returns>
    /// <remarks>
    ///     The <see langword="IntPtr" /> parameter is necessary for the hook
    ///     delegate, but is not used in the method.<br />
    ///     Do not remove or modify the <see langword="IntPtr" /> parameter.
    /// </remarks>
    // 刻意維持原樣，不要「修好」它：整個本體在 try 內，catch 一定 return 一個值，受管理例外不會穿進原生框架。
    // Original 是透過本體裡的 OriginalHook() 呼叫的（getActionHook.OriginalDisposeSafe），只是字面上沒有 ".Original("。
    // catch 回 actionID 而不是 OriginalHook(actionID) 是刻意的：會進到 catch 最可能的原因就是 hook 本身不可用，
    // 此時再呼叫一次 Original 只會在 catch 裡二次擲例外，而那正是會逸出到原生框架的那一種。
    private uint GetAdjustedActionDetour(IntPtr _, uint actionID)
    {
        try
        {
            if (!FrameworkThreadGate.Check("GetAdjustedActionId"))
                return OriginalHook(actionID);

            if (FilteredCombos is null)
                UpdateFilteredCombos();

            // Bail if not wanting to replace actions in this manner
            if (Service.Configuration.PerformanceMode)
                return OriginalHook(actionID);
            if (Svc.Objects.LocalPlayer == null)
                return OriginalHook(actionID);

            // Only refresh every so often
            if (!_actionThrottle.Throttle(actionID,
                    Service.Configuration.Throttle))
                return LastActionInvokeFor[actionID];

            // Actually get the action
            LastActionInvokeFor[actionID] = GetAdjustedAction(actionID);
            return LastActionInvokeFor[actionID];
        }
        catch (Exception e)
        {
            e.Log();
            return actionID;
        }
    }
#pragma warning restore CS1573

    /// <summary>
    ///     Replaces an action with the result from a combo.
    /// </summary>
    /// <param name="actionID">The action a combo replaces.</param>
    /// <returns>The action a combo returns.</returns>
    private unsafe uint GetAdjustedAction(uint actionID)
    {
        try
        {
            if (ClassLocked() ||
                (DisabledJobsPVE.Any(x => x == Svc.Objects.LocalPlayer.ClassJob.RowId) && !Svc.ClientState.IsPvP) ||
                (DisabledJobsPVP.Any(x => x == Svc.Objects.LocalPlayer.ClassJob.RowId) && Svc.ClientState.IsPvP))
                return OriginalHook(actionID);

            // 直接走訪清單、每個連段當下才篩：與走訪 FilteredCombos 同一個順序、同一個條件，但不必每一輪新建迭代器。
            foreach (CustomCombo combo in CustomCombos)
            {
                if (!IsForCurrentJob(combo))
                    continue;

                if (combo.TryInvoke(actionID, out uint newActionID))
                {
                    if (Service.Configuration.BlockSpellOnMove &&
                        ActionManager.GetAdjustedCastTime(ActionType.Action, newActionID) > 0 &&
                        CustomComboFunctions.TimeMoving.Ticks > 0)
                    {
                        return All.SavageBlade;
                    }

                    return newActionID;
                }
            }

            return OriginalHook(actionID);
        }

        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Preset error");
            return OriginalHook(actionID);
        }
    }

    /// <summary>
    ///     Checks if the player could be on a job instead of a class.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the user could be on a job instead.
    /// </returns>
    public static unsafe bool ClassLocked()
    {
        if (Svc.Objects.LocalPlayer is null) return false;

        if (Svc.Objects.LocalPlayer.Level <= 35) return false;

        if (Svc.Objects.LocalPlayer.ClassJob.RowId is
            (>= 8 and <= 25) or 27 or 28 or >= 30)
            return false;

        if (!UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(66049))
            return false;

        if ((Svc.Objects.LocalPlayer.ClassJob.RowId is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 26 or 29) &&
            Svc.Condition[ConditionFlag.BoundByDuty56] && // in an instance duty
            Svc.Objects.LocalPlayer.Level > 35) return true;

        return false;
    }

    private delegate ulong IsActionReplaceableDelegate(uint actionID);

    #region Restrict combos to current job

    public static IEnumerable<CustomCombo>? FilteredCombos;

    /// <summary> 這個連段屬於目前的職業（或職能）與目前的 PvP 狀態。 </summary>
    /// <remarks> 每次呼叫都讀當下的職業與 PvP 狀態：進出 PvP 區不會重建 <see cref="FilteredCombos" />，結果不可快取。 </remarks>
    private static bool IsForCurrentJob(CustomCombo x) =>
        x.Preset.Attributes() is not null && x.Preset.Attributes().IsPvP == CustomComboFunctions.InPvP() &&
        ((x.Preset.Attributes().RoleAttribute is not null && x.Preset.Attributes().RoleAttribute.PlayerIsRole()) ||
         x.Preset.Attributes().CustomComboInfo.JobID == Player.JobId ||
         x.Preset.Attributes().CustomComboInfo.JobID == CustomComboFunctions.JobIDs.ClassToJob(Player.JobId));

    public void UpdateFilteredCombos()
    {
        FilteredCombos = CustomCombos.Where(IsForCurrentJob);
        var filteredCombos = FilteredCombos as CustomCombo[] ?? FilteredCombos.ToArray();
        var names = filteredCombos.Select(x => x.Preset.Attributes().CustomComboInfo.Name).ToArray();
        // 每次切職業都會印；列上只放數量與前幾個，完整清單（每個一行）記 Verbose。
        Svc.Log.Debug(
            $"Now running {names.Length} combos: {string.Join(", ", names.Take(5))}{(names.Length > 5 ? ", ..." : "")}");
        Svc.Log.Verbose($"Now running {names.Length} combos (full list)\n{string.Join("\n", names)}");
    }

    #endregion
}