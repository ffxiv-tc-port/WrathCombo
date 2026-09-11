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

        // 🔴🔴 這一支是從 Task.Run 進來的（WrathCombo.cs 的
        //    ClientState_TerritoryChanged），也就是執行緒池的執行緒，而原本的
        //    等待迴圈就在那條執行緒上每 400 毫秒呼叫一次
        //    GenericHelpers.IsScreenReady() —— 那支會走 RaptureAtkUnitManager
        //    找 addon 再解參 addon->IsVisible，是不折不扣的原生讀取，而且這是
        //    每次區域轉換的常態路徑，不是卸載期才會走到。失敗形式是
        //    AccessViolationException，try/catch 攔不到。
        //    🔑 改成把等待本身排到框架執行緒上：每一跳都是一次
        //    RunOnTick，原生讀取因此永遠發生在框架執行緒。
        //    ⚠️ 時序刻意不變：一樣是最多 52 次就緒檢查、間隔 400 毫秒（合計
        //    20.4 秒），畫面就緒後一樣再等 4 秒才檢查 IPC —— 那 4 秒是留給
        //    AutoDuty 之類的外掛接手用的。
        //    ⚠️ 卸載期不必另外加閘門：本 pin 的 Dalamud
        //    （Dalamud/Game/Framework.cs:227-236）在 IsFrameworkUnloading 為真
        //    且帶延遲時回的是 Task.FromCanceled，委派一次都不會跑 ⇒ 整條鏈
        //    自然停住。沒有延遲的那一個才會退化成就地執行。
        Svc.Framework.RunOnTick(() => WaitForScreenThenCheckIPC(0),
            delayTicks: 1);
    };

    /// <summary>
    ///     One hop of the wait for the screen to be ready, always run on the
    ///     Framework thread.<br />
    ///     Re-schedules itself every 400ms until the screen is ready, giving
    ///     up after the same number of checks the old blocking wait loop did.
    /// </summary>
    /// <param name="count">
    ///     How many times this has already re-scheduled itself.
    /// </param>
    private static void WaitForScreenThenCheckIPC(byte count)
    {
        if (!GenericHelpers.IsScreenReady())
        {
            if (count > 50) return;
            Svc.Framework.RunOnTick(
                () => WaitForScreenThenCheckIPC((byte)(count + 1)),
                TimeSpan.FromMilliseconds(400));
            return;
        }

        // Wait for any IPC to seize control, e.g. AutoDuty has a delay after
        // entering an instance for the first time
        PluginLog.Verbose("OnIPCInstanceChange: Waiting for any IPC ...");
        Svc.Framework.RunOnTick(CheckIPCControl, TimeSpan.FromSeconds(4));
    }

    /// <summary>
    ///     Checks whether an IPC is in control of Auto-Rotation, and if so
    ///     kicks off <see cref="CheckStancePartner" />.<br />
    ///     Always run on the Framework thread.
    /// </summary>
    private static void CheckIPCControl()
    {
        // If IPC-Controlled: Run Check() on the next tick
        if (P.UIHelper.AutoRotationStateControlled() is not null)
        {
            PluginLog.Verbose("OnIPCInstanceChange: Is IPC-Controlled");

            // 🔴🔴 這一段以前是從 Task.Run 進來的（執行緒池），現在整條等待鏈
            //    已經排在框架執行緒上（見 CheckForIPCControl）。閘門仍然留著：
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
    }

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
