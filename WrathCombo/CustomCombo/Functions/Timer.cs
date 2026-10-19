using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Collections.Generic;
using System.Linq;
using WrathCombo.Extensions;

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

        /// <summary> <see cref="UpdateDeadtionary" /> 這一幀要移除的鍵；只在框架執行緒用。 </summary>
        private static readonly List<ulong> DeadtionaryRemovals = [];

        private static void UpdateDeadtionary(IFramework framework)
        {
            if (!Player.Available) return;
            foreach (var member in DeadPeople)
            {
                if (member.BattleChara is not { IsDead: true } chara) continue;
                var id = chara.GameObjectId;
                if (!Deadtionary.ContainsKey(id))
                    Deadtionary[id] = Environment.TickCount64;
            }

            if (Deadtionary.Count == 0) return;

            // 不在物件表裡、或已經不是死亡狀態的才移除；先收集再移除，不邊走訪邊改。
            DeadtionaryRemovals.Clear();
            foreach (var entry in Deadtionary)
                if (GameObjectExtensions.FindObjectById(entry.Key) is not { IsDead: true })
                    DeadtionaryRemovals.Add(entry.Key);

            foreach (var key in DeadtionaryRemovals)
                Deadtionary.Remove(key);
        }

        private static unsafe void UpdatePartyTimer(IFramework framework)
        {
            if (!Player.Available) return;
            if (AnyPartyMemberInCombat(GetPartyMembers()) && !partyInCombat)
            {
                partyInCombat = true;
                partyCombat = DateTime.Now;
            }
            else if (!AnyPartyMemberInCombat(GetPartyMembers()))
            {
                partyInCombat = false;
            }
        }

        private static unsafe bool AnyPartyMemberInCombat(List<WrathPartyMember> party)
        {
            foreach (var member in party)
                if (member.BattleChara is { } chara && chara.Struct()->InCombat)
                    return true;

            return false;
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
        ///     <c>AgentCountDownSettingDialog.Instance()</c> 兩種失效並存:內部鏈 AgentModule → UIModule → Framework 任一層是 null 就回 null
        ///     (登入前、切場景、登出後都是常態),底層特徵碼失配時改為擲 <c>InvalidOperationException</c>;只擋一種等於假防護。
        ///     裸解參考 null 原生指標是 <c>try/catch</c> 攔不到的 AccessViolationException,所以事前判空。
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
