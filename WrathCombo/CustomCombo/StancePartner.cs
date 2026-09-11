#region

using System;
using System.Threading.Tasks;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using WrathCombo.Combos.PvE;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Extensions;

#endregion

namespace WrathCombo.CustomComboNS;

public static class StancePartner
{
    /// <summary>
    ///     The number of times that <see cref="CheckStancePartner" /> has been
    ///     called.
    /// </summary>
    private static int _stancePartnerRunTries;

    /// <summary>
    ///     The Action to check if an IPC is in control, after a territory change.
    /// </summary>
    /// <seealso cref="WrathCombo.ClientState_TerritoryChanged"/>
    public static readonly Action CheckForIPCControl = () =>
    {
        // Reset run count
        _stancePartnerRunTries = 0;

        // Wait (a limited amount of time) for the screen to be ready
        PluginLog.Verbose("OnIPCInstanceChange: Waiting for screen ...");
        byte count = 0;
        while (!GenericHelpers.IsScreenReady())
        {
            if (count > 50) return;
            count++;
            Task.Delay(400).Wait();
        }

        // Wait for any IPC to seize control, e.g. AutoDuty has a delay after
        // entering an instance for the first time
        PluginLog.Verbose("OnIPCInstanceChange: Waiting for any IPC ...");
        Task.Delay(4000).Wait();

        // If IPC-Controlled: Run Check() on the next tick
        if (P.UIHelper.AutoRotationStateControlled() is not null)
        {
            PluginLog.Verbose("OnIPCInstanceChange: Is IPC-Controlled");

            // 🔴🔴 這一整支是從 Task.Run 進來的（WrathCombo.cs 的
            //    ClientState_TerritoryChanged），也就是執行緒池的執行緒，而且
            //    上面剛剛才卡了最多 24 秒 —— 那段期間使用者很可能已經在關遊戲。
            //    無延遲的 RunOnTick 在 IsFrameworkUnloading 為真時會退化成
            //    「就地在呼叫端執行緒執行」（本 pin Dalamud/Game/Framework.cs：
            //    RunOnTick 沒有 delay 時直接轉呼叫 RunOnFrameworkThread），
            //    而 CheckStancePartner 會讀 LocalPlayer 與 ActionManager 等原生
            //    狀態。失敗形式是 AccessViolationException，try/catch 攔不到。
            //    🔑 卸載期就不要再放技能了：跳過等同於「這次的區域轉換沒做」，
            //    那是上面兩個 return 本來就會產生的結果。
            //    ⚠️ 帶延遲的那一個（本檔下面的重試排程）不需要這道閘門：
            //    RunOnTick 有 delay 時在卸載期回的是已取消的 Task，委派不會跑。
            if (Svc.Framework.IsFrameworkUnloading)
            {
                PluginLog.Information(
                    "OnIPCInstanceChange: The framework is unloading, so the " +
                    "Tank Stance / Dance Partner check was skipped.");
                return;
            }

            Svc.Framework.RunOnTick(CheckStancePartner!);
        }
        else
            PluginLog.Verbose("OnIPCInstanceChange: Not IPC-Controlled");
    };

    /// <summary>
    ///     The action to try casting the abilities for Tank Stance / Dance Partner.
    /// </summary>
    private static readonly Action CheckStancePartner = () =>
    {
        PluginLog.Verbose("OnIPCInstanceChange: Trying to run StancePartner ..");

        // Whether we'll loop again, passed to Cast below
        var callAgainToConfirm = false;

        #region Tank Stance

        Cast(PLD.JobID, PLD.IronWill, PLD.Buffs.IronWill,
            null, ref callAgainToConfirm);

        Cast(WAR.JobID, WAR.Defiance, WAR.Buffs.Defiance,
            null, ref callAgainToConfirm);

        Cast(DRK.JobID, DRK.Grit, DRK.Buffs.Grit,
            null, ref callAgainToConfirm);

        Cast(GNB.JobID, GNB.RoyalGuard, GNB.Buffs.RoyalGuard,
            null, ref callAgainToConfirm);

        #endregion

        #region Dance Partner

        Cast(DNC.JobID, DNC.ClosedPosition, DNC.Buffs.ClosedPosition,
            DNC.DesiredDancePartner, ref callAgainToConfirm);

        #endregion

        // Give up trying after 10 calls
        if (_stancePartnerRunTries > 10)
            return;

        // Loop again to re-check
        if (!callAgainToConfirm) return;
        _stancePartnerRunTries++;
        Svc.Framework.RunOnTick(CheckStancePartner!,
            TimeSpan.FromSeconds(1));
    };

    /// <summary>
    ///     Method to try to use the requested ability.<br />
    ///     Will check if on the correct job, and if the ability is ready.<br />
    ///     Will also check if the ability is already active.<br />
    ///     If the cast failed or the buff from the ability still isn't active,
    ///     it will try again.
    /// </summary>
    /// <param name="job">
    ///     Job ID, from the class.
    ///     E.G. <see cref="PLD.JobID">PLD.JobID</see>
    /// </param>
    /// <param name="action">
    ///     The ability we want to cast.
    ///     E.G. <see cref="PLD.IronWill">PLD.IronWill</see>
    /// </param>
    /// <param name="buff">
    ///     The buff we want to check for, that should come from the
    ///     <paramref name="action" />.
    ///     E.G. <see cref="PLD.Buffs.IronWill">PLD.Buffs.IronWill</see>
    /// </param>
    /// <param name="target">
    ///     The target to cast the ability on.
    ///     Default is null, which will cast on self.
    /// </param>
    /// <param name="callAgain">
    ///     A reference to whether <see cref="CheckStancePartner" /> should be called again.
    /// </param>
    private static unsafe void Cast
    (byte job, uint action, ushort buff, ulong? target, ref bool
        callAgain)
    {
        if (JobID != job || CustomComboFunctions.HasStatusEffect(buff))
            return;
        PluginLog.Verbose(
            $"OnIPCInstanceChange: Trying to cast {action.ActionName()}");

        callAgain = true;

        if (CustomComboFunctions.JustUsed(action, 0.5f))
            return;
        if (!CustomComboFunctions.ActionReady(action))
            return;

        PluginLog.Verbose(
            $"OnIPCInstanceChange: Casting {action.ActionName()} {target}");

        if (target is null)
            ActionManager.Instance()->UseAction(ActionType.Action, action);
        else
            ActionManager.Instance()->UseAction(ActionType.Action, action,
                (ulong)target);
    }
}
