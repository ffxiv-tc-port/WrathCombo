#region Dependencies
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using WrathCombo.AutoRotation;
using WrathCombo.CustomComboNS;
using WrathCombo.Data;
using WrathCombo.Extensions;
using static WrathCombo.CustomComboNS.Functions.CustomComboFunctions;
#endregion

namespace WrathCombo.Combos.PvE;

// 本檔假設與契約：
// - 接戰分支只在框架執行緒、自動循環正在跑這個預設時動作（按鈕替換照舊）；狀態只在框架執行緒讀寫。
// - 落點只有 BossMod(Reborn) 明確回「危險」才改瞬發；不知道／端點不在／查詢失敗照 DashSafety 的契約當允許。
// - 不存任何物件包裝或指標，只存目標的 GameObjectId。
internal partial class RDM
{
    /// <summary>接戰的純邏輯（不碰遊戲狀態，離線探針直接呼叫）。</summary>
    internal static class Engage
    {
        internal const int None = 0, New = 1, Continue = 2;

        // 接戰動作送不出去時最多等這麼久，之後放棄這個目標、回到原本的流程。
        internal const long PendingLimitMs = 5_000;

        // 短兵相接、顯貴衝擊、六分反擊、飛刺的射程（台服 Action 表）。
        internal const float CorpsRange = 25f;

        // 決定接戰前這麼久之內已送出的接戰動作也算這一次的（別的插件先放了、或同一招剛出手）。
        internal const long SentBeforeStartMs = 500;

        // 進戰鬥後這麼久之內，遊戲回報的開打秒數可能還是上一場的（開打時間在條件事件才更新）。
        internal const long StaleCombatStartMs = 500;

        /// <summary>
        ///     Target＝這次接戰的目標 id（0＝沒有）；Start＝第一次決定接戰的 TickCount64；CombatSeenAt＝等待中第一次看到戰鬥的 TickCount64。
        ///     LastOffer／LastOfferFrame＝最近一次交出的接戰動作與那一幀（0＝最近一次判斷沒有交出）。
        ///     Settled＝已判過「有沒有送出」；Offered（其中 OfferedIcon 來自快捷列圖示）／NotReady／LastStatus 只供沒送出時的診斷行。
        /// </summary>
        internal struct State
        {
            public ulong Target;
            public long Start;
            public bool Closed;
            public bool SawCombat;
            public long CombatSeenAt;
            public bool Settled;
            public uint LastOffer;
            public long LastOfferFrame;
            public int Offered, OfferedIcon, NotReady;
            public uint LastStatus;
        }

        /// <summary>每次進接戰判斷都先呼叫：看過戰鬥之後脫戰就清掉，下一次開打重新算。</summary>
        internal static void Observe(ref State s, bool inCombat, long now)
        {
            if (s.Target == 0)
                return;
            if (inCombat)
            {
                if (!s.SawCombat)
                    s.CombatSeenAt = now;
                s.SawCombat = true;
            }
            else if (s.SawCombat)
                s = default;
        }

        /// <summary>接戰時間窗用的開打秒數：等待中剛看到戰鬥的那一小段，遊戲回報值若比自己看到的長就用自己看到的。</summary>
        internal static double CombatSeconds(in State s, double reported, long now)
        {
            if (s.Target == 0 || s.CombatSeenAt == 0 || now - s.CombatSeenAt > StaleCombatStartMs)
                return reported;
            return Math.Min(reported, (now - s.CombatSeenAt) / 1000.0);
        }

        /// <summary>
        ///     「僅於戰鬥中」擋下時，這一發能不能放：照 FATE 目標繞過的條件（開著、人在 FATE 內、目標是 FATE 怪），
        ///     且 <paramref name="action" /> 就是這一幀剛交給進行中接戰目標的接戰動作。
        /// </summary>
        internal static bool PassesCombatGate(in State s, ulong targetId, uint action, long frame, bool bypassFate,
            bool inFate, uint targetFateId) =>
            bypassFate && inFate && targetFateId != 0 && action != 0 && targetId != 0 &&
            s.Target == targetId && !s.Closed && s.LastOffer == action && s.LastOfferFrame == frame;

        /// <summary>這一次判斷的結果：交出 <paramref name="action" />（0＝沒交出）時記下動作、幀與來源。</summary>
        internal static void NoteOffer(ref State s, uint action, long frame, bool fromAutoRotation)
        {
            s.LastOffer = action;
            s.LastOfferFrame = action == 0 ? 0 : frame;
            if (action == 0 || s.Target == 0)
                return;
            s.Offered++;
            if (!fromAutoRotation)
                s.OfferedIcon++;
        }

        /// <summary>
        ///     目標已通過接戰條件時呼叫。同一次接戰只觸發一次：送出任一接戰動作、或等超過 <see cref="PendingLimitMs" /> 就關閉，直到脫戰。
        ///     戰鬥外換目標重新開始；戰鬥中已經在接戰別的目標就不再開。
        /// </summary>
        /// <param name="mounted">騎乘中一律 None 且不動狀態：下坐騎後才開始（或接著等）這次接戰。</param>
        /// <param name="lastEngageSend">接戰動作最近一次送出的 TickCount64（沒有＝0）。</param>
        internal static int Arm(ref State s, ulong targetId, bool inCombat, bool mounted, long now, long lastEngageSend)
        {
            if (mounted)
                return None;
            if (s.Target != targetId)
            {
                if (inCombat && s.Target != 0)
                    return None;
                s = new State { Target = targetId, Start = now };
                return New;
            }

            if (s.Closed)
                return None;
            if (lastEngageSend >= s.Start - SentBeforeStartMs || now - s.Start > PendingLimitMs)
            {
                s.Closed = true;
                return None;
            }
            return Continue;
        }

        /// <summary>接戰結束（逾時／走進近戰距離／超過開打時間窗）時呼叫：決定了卻沒送出才回 true，同一次接戰只判一次。</summary>
        internal static bool TakeUnsentReport(ref State s, long lastEngageSend)
        {
            if (s.Target == 0 || s.Settled)
                return false;
            s.Settled = true;
            return lastEngageSend < s.Start - SentBeforeStartMs;
        }

        /// <summary>範圍 0＝只限 F.A.T.E. 的目標；1＝再加上野外（不在副本裡，或在新月島這類特殊區域）。</summary>
        internal static bool ScopeAllows(int scope, uint targetFateId, Func<bool> inFieldContent) =>
            targetFateId != 0 || scope == 1 && inFieldContent();

        /// <summary>這一幀要放的接戰技能；0＝照原本的流程。</summary>
        internal static uint Choose(int landing, bool canGrandImpact, bool hasInstant, bool accelerationReady,
            bool swiftcastReady)
        {
            if (landing != DashSafety.Dangerous)
                return Corpsacorps;
            if (canGrandImpact)
                return GrandImpact;
            // 已經有連續魔法／促進／即刻詠唱：原本的流程下一招就是瞬發，不必再多放一個。
            if (hasInstant)
                return 0;
            if (accelerationReady)
                return Acceleration;
            return swiftcastReady ? RoleActions.Magic.Swiftcast : 0;
        }
    }

    private static Engage.State _engageState;

    private static readonly uint[] EngageActions = [Corpsacorps, GrandImpact, Acceleration, RoleActions.Magic.Swiftcast];

    private static readonly Func<bool> InFieldContent = () => !InDuty() || ContentCheck.IsInFieldOperations;

    private static long LastEngageSend()
    {
        long last = 0;
        foreach (var id in EngageActions)
            if (ActionWatching.ActionTimestamps.TryGetValue(id, out var t) && t > last)
                last = t;
        return last;
    }

    /// <summary>
    ///     接戰分支：戰鬥外或剛開打設定秒數內，目標是射程內、不在近戰範圍的敵人 ⇒ 先用短兵相接衝過去；
    ///     BossMod(Reborn) 判落點危險 ⇒ 改用瞬發技開打。回 true 時 <paramref name="action" /> 是這一幀要放的技能。
    /// </summary>
    /// <param name="optionalTarget">自動循環指定的目標（<c>CustomCombo.OptionalTarget</c>）；null＝用目前目標。</param>
    private static bool TryEngage(IGameObject? optionalTarget, bool aoe, out uint action)
    {
        action = 0;
        if (!Svc.Framework.IsInFrameworkUpdateThread || !DashSafety.InAutoContext)
            return false;

        var fromAutoRotation = AutoRotationController.InRotation;
        var offered = TryEngageCore(optionalTarget, aoe, fromAutoRotation, out action);
        Engage.NoteOffer(ref _engageState, offered ? action : 0, Svc.Framework.LastUpdate.Ticks, fromAutoRotation);
        return offered;
    }

    /// <summary>自動循環這一幀的接戰狀態（只在框架執行緒讀；「僅於戰鬥中」的接戰例外用）。</summary>
    internal static ref readonly Engage.State EngageState => ref _engageState;

    private static unsafe bool TryEngageCore(IGameObject? optionalTarget, bool aoe, bool fromAutoRotation, out uint action)
    {
        action = 0;
        var now = Environment.TickCount64;
        var inCombat = InCombat();
        Engage.Observe(ref _engageState, inCombat, now);

        var enabled = aoe
            ? IsEnabled(CustomComboPreset.RDM_AoE_Corpsacorps) && Config.RDM_AoE_EngageCorpsacorps
            : IsEnabled(CustomComboPreset.RDM_ST_Corpsacorps) && Config.RDM_ST_EngageCorpsacorps;
        if (!enabled)
            return false;
        if (!ActionReady(Corpsacorps))
        {
            NoteCorpsNotReady();
            return false;
        }

        var combatSeconds = Engage.CombatSeconds(_engageState, CombatEngageDuration().TotalSeconds, now);
        if (inCombat && combatSeconds > Config.RDM_Engage_Window)
        {
            ReportUnsent("開打已超過接戰時間窗");
            return false;
        }

        var target = optionalTarget ?? CurrentTarget;
        if (target is not IBattleChara { IsDead: false, IsTargetable: true } || !target.IsHostile())
            return false;

        var distance = GetTargetDistance(target);
        var maxDistance = Math.Min(Engage.CorpsRange,
            aoe ? Config.RDM_AoE_Corpsacorps_Distance : Config.RDM_ST_Corpsacorps_Distance);
        if (InMeleeRange(target) || distance > maxDistance)
        {
            if (distance <= maxDistance && _engageState.Target == target.GameObjectId)
                ReportUnsent("已經走進近戰距離");
            return false;
        }

        if (!Engage.ScopeAllows(Config.RDM_Engage_Scope, target.Struct()->FateId, InFieldContent))
            return false;

        var targetId = target.GameObjectId;
        var arm = Engage.Arm(ref _engageState, targetId, inCombat, Player.Mounted, now, LastEngageSend());
        if (arm == Engage.None)
        {
            if (_engageState.Closed && !_engageState.Settled)
                ReportUnsent($"等了 {Engage.PendingLimitMs / 1000} 秒");
            return false;
        }

        var landing = DashSafety.Landing(Corpsacorps, targetId);
        action = Engage.Choose(landing, CanGrandImpact, CanInstantCast, ActionReady(Acceleration),
            Role.CanSwiftcast(false) && SimpleTarget.AnyDeadPartyMember is null);

        if (arm == Engage.New)
            PluginLog.Information(
                $"[WrathCombo][赤魔接戰] 落點判斷={LandingText(landing)} → " +
                (action == 0 ? "沒有可用的瞬發技，照原本的流程" : $"放「{action.ActionName()}」") +
                $"（{(aoe ? "範圍" : "單體")}，目標：{target.Name.TextValue} 0x{targetId:X}，距離 {distance:0.0} 米，" +
                (inCombat ? $"開打 {combatSeconds:0.0} 秒" : "戰鬥外") +
                (fromAutoRotation ? "；自動循環判斷）" : "；快捷列圖示判斷）"));

        return action != 0;
    }

    // 只在接戰等待中記：短兵相接不可用的次數與最後的狀態碼，給沒送出時的診斷行用。
    private static unsafe void NoteCorpsNotReady()
    {
        ref var s = ref _engageState;
        if (s.Target == 0 || s.Closed || s.Settled || Environment.TickCount64 - s.Start > Engage.PendingLimitMs)
            return;
        s.NotReady++;
        var am = ActionManager.Instance();
        if (am != null)
            s.LastStatus = am->GetActionStatus(ActionType.Action, Corpsacorps);
    }

    // 「交出 N 次」＞0 而沒送出＝卡在自動循環送出那一段；「不可用 N 次」＞0＝遊戲當下不讓放。
    // 只有快捷列圖示交出（自動循環 0 次）＝自動循環在交出之前就被擋下，看最後的自動循環原因。
    private static unsafe void ReportUnsent(string why)
    {
        if (_engageState.Target == 0 || _engageState.Settled ||
            !Engage.TakeUnsentReport(ref _engageState, LastEngageSend()))
            return;
        var s = _engageState;
        var am = ActionManager.Instance();
        PluginLog.Information(
            $"[WrathCombo][赤魔接戰] 決定接戰 {(Environment.TickCount64 - s.Start) / 1000.0:0.00} 秒後{why}，接戰動作沒有送出" +
            $"（目標 0x{s.Target:X}；期間交出接戰動作 {s.Offered} 次（自動循環 {s.Offered - s.OfferedIcon}、快捷列圖示 {s.OfferedIcon}）、" +
            $"短兵相接不可用 {s.NotReady} 次（最後狀態碼 {s.LastStatus}）；" +
            $"騎乘中={Player.Mounted}、遊戲佇列中的動作 {(am == null ? "?" : am->QueuedActionId.ToString())}、" +
            $"動畫鎖 {(am == null ? "?" : am->AnimationLock.ToString("0.00"))} 秒；" +
            $"自動循環最後的狀態：{AutoRotationController.EngageGateSummary(Environment.TickCount64)}）");
    }

    private static string LandingText(int landing) => landing switch
    {
        DashSafety.Safe => "安全",
        DashSafety.Dangerous => "危險",
        DashSafety.Missing => "不知道（BossMod 沒有落點查詢）",
        DashSafety.Failed => "不知道（落點查詢失敗）",
        _ => "不知道",
    };
}
