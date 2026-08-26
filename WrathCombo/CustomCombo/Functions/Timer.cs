using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WrathCombo.CustomComboNS.Functions
{
    internal abstract partial class CustomComboFunctions
    {
        private static DateTime combatStart = DateTime.Now;
        private static DateTime partyCombat = DateTime.Now;
        private static DateTime? castFinishedAt;
        private static uint castId;
        private static bool partyInCombat = false;

        public delegate void OnCastInterruptedDelegate(uint interruptedAction);
        public static event OnCastInterruptedDelegate? OnCastInterrupted;

        public static Dictionary<ulong, long> Deadtionary { get; set; } = new();

        /// <summary> Tells the elapsed time since the combat started. </summary>
        /// <returns> Combat time in seconds. </returns>
        public static TimeSpan CombatEngageDuration() => InCombat() ? DateTime.Now - combatStart : TimeSpan.Zero;

        public static TimeSpan PartyEngageDuration() => partyInCombat ? DateTime.Now - partyCombat : TimeSpan.Zero;

        public static TimeSpan TimeSpentDead(ulong partyMemberObjectId) => TimeSpentDead((uint)partyMemberObjectId);

        public static TimeSpan TimeSpentDead(uint partyMemberObjectId) => Deadtionary.ContainsKey(partyMemberObjectId) ? TimeSpan.FromMilliseconds((Environment.TickCount64 - Deadtionary[partyMemberObjectId])) : TimeSpan.Zero;

        public static void TimerSetup()
        {
            Svc.Condition.ConditionChange += OnCombat;
            Svc.Framework.Update += UpdatePartyTimer;
            Svc.Framework.Update += UpdateDeadtionary;
            Svc.Framework.Update += CheckInterruptedCasts;
        }

        private static void CheckInterruptedCasts(IFramework framework)
        {
            if (Player.Available && Player.Object.CurrentCastTime > 0)
            {
                if (castFinishedAt is null)
                {
                    castId = Player.Object.CastActionId;
                    float timeLeft = ((Player.Object.TotalCastTime - Player.Object.CurrentCastTime) * 1000f) - 500f;
                    castFinishedAt = DateTime.Now + TimeSpan.FromMilliseconds(timeLeft);
                }

            }
            else
            {
                if (castFinishedAt is not null)
                {
                    if (DateTime.Now < castFinishedAt)
                    {
                        OnCastInterrupted?.Invoke(castId);
                    }
                }

                castFinishedAt = null;
            }
        }

        private static void UpdateDeadtionary(IFramework framework)
        {
            if (!Player.Available) return;
            foreach (var member in DeadPeople.Where(x => x.BattleChara is not null && x.BattleChara.IsDead))
            {
                if (!Deadtionary.ContainsKey(member.BattleChara.GameObjectId))
                    Deadtionary[member.BattleChara.GameObjectId] = Environment.TickCount64;
            }

            var deadCopy = Deadtionary.ToList();
            foreach (var member in deadCopy)
            {
                if (!Svc.Objects.Any(x => x.GameObjectId == member.Key) || !Svc.Objects.First(x => x.GameObjectId == member.Key).IsDead)
                    Deadtionary.Remove(member.Key);
            }
        }

        private static unsafe void UpdatePartyTimer(IFramework framework)
        {
            if (!Player.Available) return;
            if (GetPartyMembers().Any(x => x.BattleChara is not null && x.BattleChara.Struct()->InCombat) && !partyInCombat)
            {
                partyInCombat = true;
                partyCombat = DateTime.Now;
            }
            else if (!GetPartyMembers().Any(x => x.BattleChara is not null && x.BattleChara.Struct()->InCombat))
            {
                partyInCombat = false;
            }
        }

        public static void TimerDispose()
        {
            Svc.Condition.ConditionChange -= OnCombat;
            Svc.Framework.Update -= UpdatePartyTimer;
            Svc.Framework.Update -= UpdateDeadtionary;
            Svc.Framework.Update -= CheckInterruptedCasts;
        }

        internal static void OnCombat(ConditionFlag flag, bool value)
        {
            if (flag == ConditionFlag.InCombat && value)
                combatStart = DateTime.Now;
        }

        /// <summary>
        ///     🔴 <c>AgentCountDownSettingDialog.Instance()</c> 由
        ///     <c>[Agent(AgentId.CountDownSettingDialog)]</c> 產生:內部鏈
        ///     AgentModule → UIModule → Framework,任一層回 null 整條就回 null(登入前、
        ///     切場景、登出後都是常態),而底層 <c>[StaticAddress]</c>／<c>[MemberFunction]</c>
        ///     特徵碼失配時改為擲 <c>InvalidOperationException</c>——兩種失效模式並存,
        ///     只擋一種等於假防護。裸解參考 null 原生指標是 AccessViolationException,
        ///     在 .NET Core 屬 corrupted-state exception,<c>try/catch</c> 攔不到 ⇒ 只能事前判空。
        ///     下面兩個屬性被連招邏輯每幀讀,所以判空後靜默回退成「沒有倒數」,不寫 log。
        /// </summary>
        private static unsafe AgentCountDownSettingDialog* CountDownAgentOrNull()
        {
            try
            {
                return AgentCountDownSettingDialog.Instance();
            }
            catch
            {
                return null;
            }
        }

        public static unsafe float CountdownRemaining
        {
            get
            {
                var agent = CountDownAgentOrNull();
                return agent == null ? 0f : MathF.Max(0, agent->TimeRemaining);
            }
        }

        public static unsafe bool CountdownActive
        {
            get
            {
                var agent = CountDownAgentOrNull();
                return agent != null && agent->Active;
            }
        }
       
    }
}
