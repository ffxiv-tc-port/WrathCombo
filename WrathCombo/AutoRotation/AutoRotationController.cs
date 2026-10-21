using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.Logging;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using WrathCombo.Combos;
using WrathCombo.Combos.PvE;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Data;
using WrathCombo.Data.Conflicts;
using WrathCombo.Extensions;
using WrathCombo.Services;
using WrathCombo.Services.IPC_Subscriber;
using WrathCombo.Window.Functions;
using static WrathCombo.CustomComboNS.Functions.CustomComboFunctions;
using static WrathCombo.Data.ActionWatching;
using Action = Lumina.Excel.Sheets.Action;
using ActionType = FFXIVClientStructs.FFXIV.Client.Game.ActionType;

#pragma warning disable CS0414 // Field is assigned but its value is never used

namespace WrathCombo.AutoRotation
{
    internal unsafe static class AutoRotationController
    {
        public static AutoRotationConfigIPCWrapper? cfg;

        static long LastHealAt = 0;
        static long LastRezAt = 0;

        static bool _lockedST = false;
        static bool _lockedAoE = false;

        static DateTime? TimeToHeal;

        const float QueryRange = 30f;

        static Func<WrathPartyMember, bool> RezQuery => x =>
            x.BattleChara is not null &&
            x.BattleChara.IsDead &&
            x.BattleChara.IsTargetable &&
            (cfg.HealerSettings.AutoRezOutOfParty || GetPartyMembers().Any(y => y.GameObjectId == x.BattleChara.GameObjectId)) &&
            GetTargetDistance(x.BattleChara) <= QueryRange &&
            !RaiseStatuses.HasAny(x.BattleChara, RaiseStatuses.Transcendent) && // Transcendent Effect
            !RaiseStatuses.HasAny(x.BattleChara, RaiseStatuses.Raised) && // Raise Effect
            !RaiseStatuses.HasAny(x.BattleChara, RaiseStatuses.Unraisable) && // 無法復活
            TimeSpentDead(x.BattleChara.GameObjectId).TotalSeconds > 2 &&
            !(Service.Configuration.RotationConfig.HealerSettings.AutoRezSkipLimitedRaise &&
              RezThrottle.SkipLimitedRaise(x.BattleChara, "自動復活")) &&
            !RezThrottle.Skip(x);

        public static bool LockedST
        {
            get => _lockedST;
            set
            {
                //if (_lockedST != value)
                //    Svc.Log.Debug($"Locked ST updated to {value}");

                _lockedST = value;
            }
        }
        public static bool LockedAoE
        {
            get => _lockedAoE;
            set
            {
                //if (_lockedAoE != value)
                //    Svc.Log.Debug($"Locked AoE updated to {value}");

                _lockedAoE = value;
            }
        }

        private static bool _ninjaLockedAoE;

        /// <summary>
        ///     BossMod(Reborn) 的手動動作佇列是不是正在接管我們送出的技能。
        /// </summary>
        /// <remarks>
        ///     BMR 的 <c>ActionManagerEx.UseActionDetour</c> 在
        ///     <c>mode == UseActionMode.None</c> 且動作型別是 <c>Spell</c>／<c>Item</c>
        ///     （<c>BossMod.ActionType.Spell == 1 == CS 的 ActionType.Action</c>，
        ///     也就是本檔所有 <c>UseAction</c> 呼叫）時，會把技能推進它自己的手動佇列，
        ///     然後<b>回傳 <c>false</c></b>、不讓遊戲看到這次呼叫。
        /// </remarks>
        private static bool BossModRebornQueueTakeover =>
            ConflictingPluginsChecks.BossModReborn.ManualQueueTakeover;

        /// <summary>
        ///     這次 <c>ActionManager.UseAction</c> 是否「實際上被接受了」。
        /// </summary>
        /// <param name="ret">
        ///     <c>UseAction</c> 的原始回傳值。
        /// </param>
        /// <returns>
        ///     <c>ret</c> 為真，或 BossMod(Reborn) 的手動佇列正在接管（此時
        ///     <c>ret == false</c> 代表<b>已排隊、稍後由對方送出</b>，不是失敗）。
        /// </returns>
        /// <remarks>
        ///     注意：這裡<b>只改記帳，絕不重送</b>。技能已經在對方佇列裡，重送＝雙重施放。
        ///     兩害相權取其輕：照字面當失敗＝<b>每個節流週期都重打同一發治療</b>，
        ///     而重複 Push 同一個動作還會讓 BMR 進入 emergency 模式（清空佇列裡其他項目、
        ///     強制插隊）；視同成功最壞只是偶爾晚約 1.2 秒（＋詠唱時間）。取後者。
        /// </remarks>
        private static bool ActionAccepted(bool ret) =>
            ret || BossModRebornQueueTakeover;

        // 同 BaseSelection.Any(IsCombatBypassTarget)、判斷順序相同；非戰鬥時每一輪都問，不建 Where 列舉器。
        // 兩個繞過都不可能成立時直接回 false：不掃物件表（每隻都要做視線檢查）。
        static bool CombatBypass
        {
            get
            {
                if (!cfg.BypassQuest && !(cfg.BypassFATE && InFATE()))
                    return false;
                var priority = DPSTargeting.AnyPriority();
                foreach (var x in Svc.Objects)
                    if (DPSTargeting.InBaseSelection(x, priority) && IsCombatBypassTarget(x))
                        return true;
                return false;
            }
        }

        private static bool IsCombatBypassTarget(IGameObject x) =>
            IsCombatBypassCandidate(cfg.BypassQuest, IsQuestMob(x), cfg.BypassFATE, x.Struct()->FateId, InFATE());

        /// <summary> 可選目標（BaseSelection）裡的這一隻，能不能讓「僅於戰鬥中」被繞過。 </summary>
        /// <remarks> FATE 繞過要「人在 FATE 內」且「這隻本身是 FATE 怪」兩者都成立；只在 FATE 範圍內不夠。 </remarks>
        internal static bool IsCombatBypassCandidate(bool bypassQuest, bool isQuestMob, bool bypassFate,
            uint fateId, bool inFate) =>
            (bypassQuest && isQuestMob) || (bypassFate && fateId != 0 && inFate);

        /// <summary> 會對敵人出招時用：同 <see cref="IsCombatBypassCandidate" />，但 FATE 繞過另要「這一招的目標本身是 FATE 怪」。 </summary>
        /// <remarks> 任務目標繞過不看目標。<paramref name="targetFateId" /> 為 0＝目標不是 FATE 怪或沒有目標。 </remarks>
        internal static bool IsCombatBypassCandidateFor(bool bypassQuest, bool isQuestMob, bool bypassFate,
            uint fateId, bool inFate, uint targetFateId) =>
            IsCombatBypassCandidate(bypassQuest, isQuestMob, bypassFate && targetFateId != 0, fateId, inFate);

        // 對敵人出招的閘門用這支；總閘門仍用 CombatBypass（後面還有補血、復活等不對敵人的動作）。
        private static bool CombatBypassFor(IGameObject? target)
        {
            var targetFateId = TargetFateId(target);
            var inFate = InFATE();
            if (!cfg.BypassQuest && !(cfg.BypassFATE && inFate && targetFateId != 0))
                return false;
            foreach (var x in DPSTargeting.BaseSelection)
                if (IsCombatBypassCandidateFor(cfg.BypassQuest, IsQuestMob(x), cfg.BypassFATE, x.Struct()->FateId, inFate, targetFateId))
                    return true;
            return false;
        }

        // 只在取得目標的同一幀呼叫。
        private static uint TargetFateId(IGameObject? target) => target is null ? 0u : target.Struct()->FateId;

        static bool NotInCombat => !GetPartyMembers().Any(x => x.BattleChara is not null && x.BattleChara.Struct()->InCombat) || PartyEngageDuration().TotalSeconds < cfg.CombatDelay;

        private static bool ShouldSkipAutorotation()
        {
            var stage = PreGateStage();
            if (stage != NoActStage.Rotation)
            {
                DiagReportPreGate(stage);
                return true;
            }

            return !EzThrottler.Throttle("Autorot", cfg.Throttler);
        }

        // 順序有意義：Player.Available 要先於 Player.Object 的讀取，前面成立就不看後面。
        private static NoActStage PreGateStage()
        {
            if (!cfg.Enabled) return NoActStage.Disabled;
            if (!Player.Available) return NoActStage.Unavailable;
            if (Player.Object.IsDead) return NoActStage.Dead;
            if (GenericHelpers.IsOccupied()) return NoActStage.Occupied;
            if (Player.Mounted) return NoActStage.Mounted;
            return NoActStage.Rotation;
        }

        #region 不出招診斷（只在框架執行緒讀寫；判定與節流本體在 NoActDiagnostics）

        private static bool _diagFired;
        private static bool _diagFiredMain;
        private static bool _diagMainPass;
        private static NoActStOutcome _diagStOutcome;
        private static int _diagDpsSeen;
        private static int _diagDpsPvpBlocked;
        private static bool _diagTranscendent;
        private static NoActLogGate _diagGate;
        private static long _diagPreGateNextAt;
        private static long _diagRelevanceNextAt;
        private static bool _diagHostileTarget;
        private static bool _diagPlayerInCombat;
        private static long _diagSourceNextAt;
        private static int _diagSourceSkipped;
        private static ulong _diagNotFateTargetId;
        private static uint _diagMovingAct;
        private static CustomComboPreset _diagMovingPreset;
        private static NoActReason _diagLastRaw;
        private static long _diagLastRawAt;

        private static void DiagBeginTick()
        {
            _diagFired = false;
            _diagFiredMain = false;
            _diagMainPass = false;
            DiagResetPass();
        }

        // 「連擊建議自用技能時繞過」前置輪的單體結果不算數，主輪重新累計。
        private static void DiagEnterMainPass()
        {
            _diagMainPass = true;
            DiagResetPass();
        }

        private static void DiagResetPass()
        {
            _diagStOutcome = NoActStOutcome.None;
            _diagNotFateTargetId = 0;
            _diagMovingAct = 0;
            _diagDpsSeen = 0;
            _diagDpsPvpBlocked = 0;
            _diagTranscendent = false;
        }

        private static void DiagNoteFired()
        {
            _diagFired = true;
            if (_diagMainPass)
                _diagFiredMain = true;
        }

        /// <returns> 恆為 <c>false</c>，讓呼叫端可以直接 <c>return DiagSt(...)</c>。 </returns>
        private static bool DiagSt(bool record, NoActStOutcome outcome)
        {
            if (record && outcome > _diagStOutcome)
                _diagStOutcome = outcome;
            return false;
        }

        // 同一輪記第一個（優先序最高的）被移動擋下的招；只存數值，字串等真的要寫 log 才組。
        private static bool DiagMovingCaster(bool record, uint outAct, CustomComboPreset preset)
        {
            if (record && NoActStOutcome.MovingCaster > _diagStOutcome)
            {
                _diagMovingAct = outAct;
                _diagMovingPreset = preset;
            }
            return DiagSt(record, NoActStOutcome.MovingCaster);
        }

        // 對敵人出招的閘門擋下時用：拿掉「目標是 FATE 怪」條件就會放行的，才算目標不是 FATE 怪。
        private static bool DiagCombatGate(bool record, IGameObject? target)
        {
            if (!record) return false;
            if (!cfg.BypassFATE || !InFATE() || TargetFateId(target) != 0 || !CombatBypass)
                return DiagSt(true, NoActStOutcome.StGate);
            if (target is null)
                return DiagSt(true, NoActStOutcome.NoTarget);
            if (NoActStOutcome.NotFateTarget > _diagStOutcome)
                _diagNotFateTargetId = target.GameObjectId;
            return DiagSt(true, NoActStOutcome.NotFateTarget);
        }

        // 前置判定每幀都會走到；節流器要 3 秒穩定才寫，每 250 毫秒餵一次就夠。
        private static void DiagReportPreGate(NoActStage stage)
        {
            if (stage == NoActStage.Unavailable) return;
            var now = Environment.TickCount64;
            if (now < _diagPreGateNextAt) return;
            _diagPreGateNextAt = now + 250;
            DiagOffer(now, NoActDiagnostics.ClassifyRaw(stage, false, NoActStOutcome.None, 0, 0, false));
        }

        private static void DiagEndTick(NoActStage stage)
        {
            var now = Environment.TickCount64;
            if (_diagFiredMain && !InCombat())
                DiagReportGateSource(now);

            DiagOffer(now, NoActDiagnostics.ClassifyRaw(stage, _diagFired, _diagStOutcome,
                _diagDpsSeen, _diagDpsPvpBlocked, _diagTranscendent));
        }

        private static void DiagOffer(long now, NoActReason raw)
        {
            _diagLastRaw = raw;
            _diagLastRawAt = now;
            var reason = raw;
            if (NoActDiagnostics.IsComplaint(raw))
            {
                if (now >= _diagRelevanceNextAt)
                {
                    _diagRelevanceNextAt = now + 250;
                    _diagHostileTarget = Svc.Targets.Target is IBattleChara { IsDead: false } t && t.IsHostile();
                    _diagPlayerInCombat = InCombat();
                }

                reason = NoActDiagnostics.ApplyRelevance(raw, _diagHostileTarget, _diagPlayerInCombat);
            }

            if (!_diagGate.Offer(now, reason, out var emitted, out var skipped))
                return;

            var text = NoActDiagnostics.Describe(emitted);
            var detail = DiagDetail(emitted);
            if (detail.Length > 0)
                text += "：" + detail;
            if (skipped > 0)
                text += $"（上一行之後另有 {skipped} 種狀態因頻率上限未記錄）";
            PluginLog.Information($"[AutoRotation][不出招] {text}");
        }

        // 細節只是輔助判讀，取不到不可以讓自動循環本身擲例外。
        private static string DiagDetail(NoActReason reason)
        {
            try
            {
                return reason switch
                {
                    NoActReason.NotInCombat => DiagNotInCombatDetail(),
                    NoActReason.NoTarget => $"目標選擇模式 {cfg.DPSRotationMode}",
                    NoActReason.NotFateTarget => DiagNotFateTargetDetail(),
                    NoActReason.MovingCaster => _diagMovingAct == 0
                        ? ""
                        : $"{_diagMovingAct.ActionName()}（{_diagMovingAct}，{_diagMovingPreset}）",
                    _ => "",
                };
            }
            catch (Exception ex)
            {
                return $"細節取得失敗（{ex.GetType().Name}）";
            }
        }

        // 與記下 ID 的那一輪同一幀呼叫，用 ID 重查物件。
        private static string DiagNotFateTargetDetail()
        {
            var sb = new StringBuilder();
            sb.Append("目標選擇模式 ").Append(cfg.DPSRotationMode);
            if (_diagNotFateTargetId != 0 && Svc.Objects.SearchById(_diagNotFateTargetId) is { } t)
                sb.Append("，被擋下的目標「").Append(t.Name.TextValue).Append("」");
            sb.Append("，優先選擇 FATE 目標=").Append(cfg.DPSSettings.FATEPriority ? "開" : "關");
            return sb.ToString();
        }

        private static string DiagNotInCombatDetail()
        {
            var sb = new StringBuilder();
            sb.Append("FATE 目標繞過=").Append(cfg.BypassFATE ? "開" : "關")
                .Append("，人在 FATE 內（等級已同步）=").Append(InFATE() ? "是" : "否")
                .Append("，可選目標裡有 FATE 怪=")
                .Append(DPSTargeting.BaseSelection.Any(x => x.Struct()->FateId != 0) ? "是" : "否")
                .Append("，任務目標繞過=").Append(cfg.BypassQuest ? "開" : "關");
            if (cfg.CombatDelay > 0)
                sb.Append("，開戰延遲 ").Append(cfg.CombatDelay).Append(" 秒");
            if (Svc.Targets.Target is IBattleNpc npc)
                sb.Append("，目標「").Append(npc.Name.TextValue).Append("」")
                    .Append(DiagPlateText(npc.GetNameplateKind()))
                    .Append(npc.Struct()->FateId != 0 ? "、是 FATE 怪" : "、不是 FATE 怪");
            return sb.ToString();
        }

        private static string DiagPlateText(NameplateKind kind) => kind switch
        {
            NameplateKind.HostileNotEngaged => "未交戰",
            NameplateKind.HostileEngagedSelfDamaged or NameplateKind.HostileEngagedSelfUndamaged => "與你的隊伍交戰中",
            NameplateKind.HostileEngagedOther => "與其他人交戰中",
            NameplateKind.Dead => "已死亡",
            _ => $"名牌類型 {(int)kind}",
        };

        // 只在主輪出了招、自己卻還不在戰鬥中時才進來（每次開打最多幾次）。
        private static void DiagReportGateSource(long now)
        {
            var source = DiagGateSource();
            if (!NoActDiagnostics.IsSuspiciousGateSource(source)) return;
            if (now < _diagSourceNextAt)
            {
                _diagSourceSkipped++;
                return;
            }

            _diagSourceNextAt = now + 60_000;
            var skipped = _diagSourceSkipped;
            _diagSourceSkipped = 0;
            var tail = skipped > 0 ? $"（上一行之後同類另有 {skipped} 次未記錄）" : "";
            PluginLog.Information($"[AutoRotation][開打依據] 自己不在戰鬥中，自動循環仍出招：{NoActDiagnostics.DescribeGateSource(source)}{tail}");
        }

        private static NoActGateSource DiagGateSource()
        {
            Span<ulong> slots = stackalloc ulong[7];
            var slotCount = 0;
            for (var i = 2; i <= 8; i++)
                if (CustomComboNS.SimpleTarget.GetPartyMemberInSlotSlot(i) is { } slotMember)
                    slots[slotCount++] = slotMember.GameObjectId;

            var selfId = Player.Object.GameObjectId;
            bool self = false, slot = false, nonSlot = false, npc = false;
            foreach (var member in GetPartyMembers())
            {
                var chara = member.BattleChara;
                if (chara is null || !chara.Struct()->InCombat) continue;
                if (chara.GameObjectId == selfId) self = true;
                else if (member.IsOutOfPartyNPC) npc = true;
                else if (slots[..slotCount].Contains(chara.GameObjectId)) slot = true;
                else nonSlot = true;
            }

            var engageDelayBlocking = PartyEngageDuration().TotalSeconds < cfg.CombatDelay;
            var source = NoActDiagnostics.ClassifyGateSource(cfg.InCombatOnly, engageDelayBlocking,
                self, InCombat(), slot, nonSlot, npc, false, false);
            if (source != NoActGateSource.Unknown)
                return source;

            // 繞過依據要掃 BaseSelection，只在其他依據都不成立時才算；判定與 CombatBypass 共用同一支。
            var selection = DPSTargeting.BaseSelection;
            var fate = selection.Any(x => IsCombatBypassCandidate(false, false, cfg.BypassFATE, x.Struct()->FateId, InFATE()));
            var quest = selection.Any(x => IsCombatBypassCandidate(cfg.BypassQuest, IsQuestMob(x), false, 0, false));
            return NoActDiagnostics.ClassifyGateSource(cfg.InCombatOnly, engageDelayBlocking,
                self, InCombat(), slot, nonSlot, npc, fate, quest);
        }

        #endregion

        #region 赤魔接戰：「僅於戰鬥中」的例外（只在框架執行緒）

        private enum EngageGateNote : byte
        {
            None,
            NoBypass,
            NoTarget,
            TargetNotFate,
            Busy,
            NotOffered,
            Unusable,
            Rejected,
            Sent,
        }

        private static EngageGateNote _engageNote;
        private static long _engageNoteAt;
        private static uint _engageNoteStatus;
        private static ulong _engageLoggedTarget;

        // AoE 在前，與 ProcessAutoActions 的順序相同。
        private static readonly CustomComboPreset[] EngagePresets = [CustomComboPreset.RDM_AoE_DPS, CustomComboPreset.RDM_ST_DPS];

        private static void NoteEngage(EngageGateNote note, uint status = 0)
        {
            _engageNote = note;
            _engageNoteAt = Environment.TickCount64;
            _engageNoteStatus = status;
        }

        /// <summary>
        ///     「僅於戰鬥中」擋下整輪時唯一的例外：赤魔接戰這一幀交給目標的接戰動作，目標照 FATE 目標繞過的條件
        ///     （開著、人在 FATE 內、目標是 FATE 怪）就只送那一發，其餘一律照舊不出招。
        /// </summary>
        /// <remarks> 可選目標清單受「最大距離」「只打已在戰鬥中的目標」限制，射程內但較遠的接戰目標進不去，總閘門不會因它放行。 </remarks>
        private static bool TryEngagePastCombatGate(Dictionary<CustomComboPreset, bool> autoActions)
        {
            if (Player.Job != Job.RDM)
                return false;
            var inFate = InFATE();
            if (!cfg.BypassFATE || !inFate)
            {
                NoteEngage(EngageGateNote.NoBypass);
                return false;
            }

            var target = AutoRotationHelper.GetSingleTarget(cfg.DPSRotationMode);
            if (target is null)
            {
                NoteEngage(EngageGateNote.NoTarget);
                return false;
            }

            var fateId = TargetFateId(target);
            if (fateId == 0)
            {
                NoteEngage(EngageGateNote.TargetNotFate);
                return false;
            }

            if (Player.Object.CurrentCastTime > 0 || ActionManager.Instance()->AnimationLock > 0 || HasStatusEffect(418))
            {
                NoteEngage(EngageGateNote.Busy);
                return false;
            }

            var frame = Svc.Framework.LastUpdate.Ticks;
            var targetId = target.GameObjectId;
            foreach (var preset in EngagePresets)
            {
                if (!autoActions.TryGetValue(preset, out var on) || !on)
                    continue;
                var attributes = preset.Attributes();
                if (attributes is not { AutoAction: { IsHeal: false } auto, ReplaceSkill: not null })
                    continue;

                uint gameAct = attributes.ReplaceSkill.ActionIDs.First();
                if (auto.IsAoE)
                {
                    var aoeTargets = cfg.DPSSettings.DPSAoETargets;
                    if (aoeTargets == null || NumberOfEnemiesInRange(OriginalHook(gameAct), target, true) < aoeTargets)
                        continue;
                }

                var combo = AutoRotationHelper.InvokeCombo(preset, attributes, ref gameAct, target);
                if (!RDM.Engage.PassesCombatGate(RDM.EngageState, targetId, combo, frame, cfg.BypassFATE, inFate, fateId))
                {
                    NoteEngage(EngageGateNote.NotOffered);
                    continue;
                }

                var outAct = OriginalHook(combo);
                var status = ActionManager.Instance()->GetActionStatus(ActionType.Action, outAct);
                var hostile = ActionSheet[outAct].CanTargetHostile;
                if (!CanQueue(outAct) || status != 0 || hostile && !IsInLineOfSight(target))
                {
                    NoteEngage(EngageGateNote.Unusable, status);
                    return false;
                }

                Svc.Targets.Target = target;
                var accepted = ActionAccepted(ActionManager.Instance()->UseAction(ActionType.Action,
                    Service.ActionReplacer.getActionHook?.IsEnabled == true ? gameAct : outAct,
                    hostile ? targetId : Player.Object.GameObjectId));
                NoteEngage(accepted ? EngageGateNote.Sent : EngageGateNote.Rejected);
                if (accepted && _engageLoggedTarget != targetId)
                {
                    _engageLoggedTarget = targetId;
                    PluginLog.Information($"[AutoRotation][赤魔接戰] 自己不在戰鬥中、「僅於戰鬥中」開著：照 FATE 目標繞過，" +
                        $"只送接戰動作「{outAct.ActionName()}」（目標：{target.Name.TextValue} 0x{targetId:X}，F.A.T.E. {fateId}）");
                }
                return accepted;
            }

            return false;
        }

        /// <summary> 給接戰沒送出的診斷行：自動循環最後一次的不出招原因，以及接戰例外最後的結果（不受診斷去抖影響）。 </summary>
        internal static string EngageGateSummary(long now)
        {
            var raw = _diagLastRawAt == 0
                ? "沒有紀錄"
                : $"{NoActDiagnostics.Describe(_diagLastRaw)}（{(now - _diagLastRawAt) / 1000.0:0.0} 秒前）";
            var engage = _engageNote switch
            {
                EngageGateNote.None => "沒有走到",
                EngageGateNote.NoBypass => "FATE 目標繞過關著或人不在 FATE 內",
                EngageGateNote.NoTarget => "沒有目標",
                EngageGateNote.TargetNotFate => "目標不是 F.A.T.E. 怪",
                EngageGateNote.Busy => "詠唱中、動畫鎖或無敵中",
                EngageGateNote.NotOffered => "這一幀沒有交出接戰動作",
                EngageGateNote.Unusable => $"遊戲不讓放或看不到目標（狀態碼 {_engageNoteStatus}）",
                EngageGateNote.Rejected => "送出被拒",
                _ => "已送出",
            };
            if (_engageNote != EngageGateNote.None)
                engage += $"（{(now - _engageNoteAt) / 1000.0:0.0} 秒前）";
            return $"{raw}；「僅於戰鬥中」的接戰例外：{engage}";
        }

        #endregion

        /// <summary> 自動循環本體正在跑（只在框架執行緒讀寫）；讓連段分得出呼叫來自自動循環還是快捷列圖示。 </summary>
        internal static bool InRotation;

        internal static void Run()
        {
            cfg ??= new AutoRotationConfigIPCWrapper(Service.Configuration.RotationConfig);
          
            // Early exit for all conditions that should prevent autorotation
            if (ShouldSkipAutorotation())
                return;

            InRotation = true;
            try
            {
                RunRotation();
            }
            finally
            {
                InRotation = false;
            }
        }

        // 每一輪都會跑：不要用 lambda 捕獲區域變數（下面的 _），閉包類別會在方法入口就配置。
        private static void RunRotation()
        {
            uint _ = 0;
            var autoActions = Presets.GetJobAutorots;
            Data.DashSafety.AutoPresets = autoActions;
            DiagBeginTick();

            // Pre-emptive HoT for healers
            if (cfg.HealerSettings.PreEmptiveHoT && Player.Job is Job.CNJ or Job.WHM or Job.AST)
                PreEmptiveHot();

            // Bypass buffs logic
            if (cfg.BypassBuffs && NotInCombat)
            {
                if (ProcessAutoActions(autoActions, ref _, false, true))
                {
                    DiagEndTick(NoActStage.Processed);
                    return;
                }
            }

            DiagEnterMainPass();

            // Only run in combat if required
            if (cfg.InCombatOnly && NotInCombat && !CombatBypass)
            {
                TryEngagePastCombatGate(autoActions);
                DiagEndTick(NoActStage.NotInCombat);
                return;
            }

            // Healer logic
            bool isHealer = Player.Object.GetRole() is CombatRole.Healer;
            var healTarget = isHealer ? AutoRotationHelper.GetSingleTarget(cfg.HealerRotationMode) : null;

            bool aoeheal = isHealer
                && HealerTargeting.CanAoEHeal()
                && HasAutoHeal(autoActions, true);

            bool needsHeal = ((healTarget != null
                && HasAutoHeal(autoActions, false))
                || aoeheal)
                && isHealer;

            if (needsHeal && TimeToHeal is null)
                TimeToHeal = DateTime.Now;
            else if (!needsHeal)
                TimeToHeal = null;

            // Check if any healing action is ready
            bool actCheck = false;
            foreach (var pair in autoActions)
            {
                var attr = pair.Key.Attributes();
                if (attr?.AutoAction?.IsHeal == true && ActionReady(AutoRotationHelper.InvokeCombo(pair.Key, attr, ref _)))
                {
                    actCheck = true;
                    break;
                }
            }

            bool canHeal = TimeToHeal is not null
                && (DateTime.Now - TimeToHeal.Value).TotalSeconds >= cfg.HealerSettings.HealDelay
                && actCheck;

            // Don't act if currently casting
            if (Player.Object?.CurrentCastTime > 0)
            {
                DiagEndTick(NoActStage.Busy);
                return;
            }

            // Healer cleanse/rez logic
            if (isHealer || (Player.Job is Job.SMN or Job.RDM && cfg.HealerSettings.AutoRezDPSJobs) || OccultCrescent.ReviveEnabledAndUsable)
            {
                // 有人要補時只清致命狀態（死亡宣告這類），其餘照舊等補完再清。
                if (cfg.HealerSettings.AutoCleanse && isHealer)
                    CleanseParty(lethalOnly: needsHeal);

                if (!needsHeal && cfg.HealerSettings.AutoRez)
                    RezParty();
            }

            // SGE Kardia logic
            if (Player.Job is Job.SGE && cfg.HealerSettings.ManageKardia)
                UpdateKardiaTarget();

            // Don't act if animation locked (beyond the queue window)
            if (ActionManager.Instance()->AnimationLock > cfg.QueueWindow)
            {
                DiagEndTick(NoActStage.Busy);
                return;
            }

            // Reset locks if no action for 3 seconds
            if (TimeSinceLastAction.TotalSeconds >= 3)
            {
                LockedAoE = false;
                LockedST = false;
                _ninjaLockedAoE = false;
            }

            ProcessAutoActions(autoActions, ref _, canHeal, false);
            DiagEndTick(canHeal ? NoActStage.Healing : NoActStage.Processed);
        }

        // 有沒有開著自動模式的治療預設；aoe＝true 找範圍治療，false 找非範圍治療。
        private static bool HasAutoHeal(Dictionary<CustomComboPreset, bool> autoActions, bool aoe)
        {
            foreach (var pair in autoActions)
            {
                var auto = pair.Key.Attributes()?.AutoAction;
                if (auto?.IsHeal == true && (auto.IsAoE == true) == aoe)
                    return true;
            }

            return false;
        }

        private static bool ProcessAutoActions(Dictionary<CustomComboPreset, bool> autoActions, ref uint _, bool canHeal, bool stOnly)
        {
            // 兩輪走訪＝原本的穩定排序（先 AoE、再單體，各自照字典順序），不配置；本體不改 autoActions。
            for (var aoePass = 1; aoePass >= 0; aoePass--)
            foreach (var pair in autoActions)
            {
                var preset = pair.Key;
                var attributes = preset.Attributes();
                if (attributes is not { AutoAction: { } action, ReplaceSkill: not null } ||
                    action.IsHeal != canHeal ||
                    (stOnly && action.IsAoE) ||
                    action.IsAoE != (aoePass == 1))
                    continue;

                if (!action.IsHeal)
                    _diagDpsSeen++;

                // Skip if locked
                if ((action.IsAoE && LockedST) || (!action.IsAoE && LockedAoE))
                    continue;

                // Skip if rez invuln is up
                if (!action.IsHeal && HasStatusEffect(418))
                {
                    _diagTranscendent = true;
                    continue;
                }

                uint gameAct = attributes.ReplaceSkill!.ActionIDs.First();

                // Skip if action is unavailable
                if (ActionManager.Instance()->GetActionStatus(ActionType.Action, gameAct) == 639)
                {
                    if (!action.IsHeal)
                        _diagDpsPvpBlocked++;
                    continue;
                }

                var outAct = OriginalHook(AutoRotationHelper.InvokeCombo(preset, attributes, ref _));
                if (!CanQueue(outAct) && outAct is not All.SavageBlade)
                    continue;

                if (action.IsHeal)
                {
                    AutomateHealing(preset, attributes, gameAct);
                    continue;
                }

                // Tank logic
                if (Player.Object?.GetRole() is CombatRole.Tank)
                {
                    AutomateTanking(preset, attributes, gameAct);
                    continue;
                }

                // DPS logic
                if (!action.IsHeal && AutomateDPS(preset, attributes, gameAct))
                    return false;
            }

            return false;
        }

        private static void PreEmptiveHot()
        {
            if (PartyInCombat() || Svc.Targets.FocusTarget is null || (InDuty() && !Svc.DutyState.IsDutyStarted))
                return;

            ushort regenBuff = Player.Job switch
            {
                Job.AST => AST.Buffs.AspectedBenefic,
                Job.CNJ or Job.WHM => WHM.Buffs.Regen,
                _ => 0
            };

            uint regenSpell = Player.Job switch
            {
                Job.AST => AST.AspectedBenefic,
                Job.CNJ or Job.WHM => WHM.Regen,
                _ => 0
            };

            if (regenSpell != 0 && !JustUsed(regenSpell, 4) && Svc.Targets.FocusTarget != null && (!HasStatusEffect(regenBuff, out var regen, Svc.Targets.FocusTarget) || regen?.RemainingTime <= 5f))
            {
                var query = Svc.Objects.Where(x => !x.IsDead && x.IsTargetable && x.IsHostile());
                if (!query.Any())
                    return;

                if (query.Min(x => GetTargetDistance(x, Svc.Targets.FocusTarget)) <= QueryRange)
                {
                    var spell = ActionManager.Instance()->GetAdjustedActionId(regenSpell);

                    if (Svc.Targets.FocusTarget.IsDead)
                        return;

                    if (!ActionReady(spell))
                        return;

                    if (ActionManager.CanUseActionOnTarget(spell, Svc.Targets.FocusTarget.Struct()) && !OutOfRange(spell, Player.Object, Svc.Targets.FocusTarget) && ActionManager.Instance()->GetActionStatus(ActionType.Action, spell) == 0)
                    {
                        ActionManager.Instance()->UseAction(ActionType.Action, regenSpell, Svc.Targets.FocusTarget.GameObjectId);
                        return;
                    }
                }
            }
        }

        private static void RezParty()
        {
            if (HasStatusEffect(418)) return;
            uint resSpell = 
                OccultCrescent.ReviveEnabledAndUsable
                ? OccultCrescent.Revive
                : Player.Job switch
            {
                Job.CNJ or Job.WHM => WHM.Raise,
                Job.SCH or Job.SMN => SCH.Resurrection,
                Job.AST => AST.Ascend,
                Job.SGE => SGE.Egeiro,
                Job.RDM => RDM.Verraise,
                _ => 0,
            };

            if (resSpell == 0)
                return;

            if (ActionManager.Instance()->QueuedActionId == resSpell)
                ActionManager.Instance()->QueuedActionId = 0;

            if (Player.Object.CurrentMp >= GetResourceCost(resSpell) && ActionReady(resSpell))
            {
                var timeSinceLastRez = TimeSinceLastSuccessfulCast(resSpell);
                if ((timeSinceLastRez != -1f && timeSinceLastRez < 4f) || Player.Object.IsCasting())
                    return;

                if (DeadPeople.Where(RezQuery).FindFirst(x => x is not null, out var member))
                {
                    if (resSpell == OccultCrescent.Revive)
                    {
                        ActionManager.Instance()->UseAction(ActionType.Action, resSpell, member.BattleChara.GameObjectId);
                        return;
                    }

                    if (Player.Job is Job.RDM)
                    {
                        if (ActionReady(RoleActions.Magic.Swiftcast) && !HasStatusEffect(RDM.Buffs.Dualcast))
                        {
                            ActionManager.Instance()->UseAction(ActionType.Action, RoleActions.Magic.Swiftcast);
                            return;
                        }

                        if (ActionManager.GetAdjustedCastTime(ActionType.Action, resSpell) == 0)
                        {
                            ActionManager.Instance()->UseAction(ActionType.Action, resSpell, member.BattleChara.GameObjectId);
                        }

                    }
                    else
                    {
                        if (ActionReady(RoleActions.Magic.Swiftcast))
                        {
                            if (ActionManager.Instance()->GetActionStatus(ActionType.Action, RoleActions.Magic.Swiftcast) == 0)
                            {
                                ActionManager.Instance()->UseAction(ActionType.Action, RoleActions.Magic.Swiftcast);
                                return;
                            }
                        }

                        if (!IsMoving() || HasStatusEffect(RoleActions.Magic.Buffs.Swiftcast))
                        {

                            if ((cfg is not null) && ((cfg.HealerSettings.AutoRezRequireSwift && ActionManager.GetAdjustedCastTime(ActionType.Action, resSpell) == 0) || !cfg.HealerSettings.AutoRezRequireSwift))
                                ActionManager.Instance()->UseAction(ActionType.Action, resSpell, member.BattleChara.GameObjectId);
                        }
                    }
                }
            }
        }

        private static void CleanseParty(bool lethalOnly)
        {
            if (HasStatusEffect(418)) return;

            // 致命狀態優先；lethalOnly 時沒有就整個不動（含不清掉已排入的復原）。
            WrathPartyMember? member = null;
            foreach (var x in GetPartyMembers())
            {
                if (!HasCleansableDebuff(x.BattleChara) || x.GameObject?.IsFriendly() != true)
                    continue;
                if (HasLethalCleansableDebuff(x.BattleChara))
                {
                    member = x;
                    break;
                }
                if (!lethalOnly && member is null)
                    member = x;
            }

            if (lethalOnly && member is null)
                return;

            if (ActionManager.Instance()->QueuedActionId == RoleActions.Healer.Esuna)
                ActionManager.Instance()->QueuedActionId = 0;

            if (member is not null)
            {
                if (InActionRange(RoleActions.Healer.Esuna, member.BattleChara) && IsInLineOfSight(member.BattleChara))
                    ActionManager.Instance()->UseAction(ActionType.Action, RoleActions.Healer.Esuna, member.BattleChara.GameObjectId);
            }
        }

        private static void UpdateKardiaTarget()
        {
            if (HasStatusEffect(418)) return;
            if (!LevelChecked(SGE.Kardia)) return;
            if (CombatEngageDuration().TotalSeconds < 3) return;

            foreach (var member in GetPartyMembers().Where(x => !x.BattleChara.IsDead).OrderByDescending(x => x.BattleChara?.GetRole() is CombatRole.Tank))
            {
                if (cfg.HealerSettings.KardiaTanksOnly && member.BattleChara?.GetRole() is not CombatRole.Tank &&
                    !HasStatusEffect(3615, member.BattleChara, true)) continue;

                var enemiesTargeting = Svc.Objects.Count(x => x.IsTargetable && x.IsHostile() && x.TargetObjectId == member.BattleChara.GameObjectId);
                if (enemiesTargeting > 0 && !HasStatusEffect(SGE.Buffs.Kardion, member.BattleChara))
                {
                    ActionManager.Instance()->UseAction(ActionType.Action, SGE.Kardia, member.BattleChara.GameObjectId);
                    return;
                }
            }

        }

        private static bool AutomateDPS(CustomComboPreset preset, Presets.PresetAttributes attributes, uint gameAct)
        {
            var mode = cfg.DPSRotationMode;
            if (attributes.AutoAction!.IsAoE)
            {
                return AutoRotationHelper.ExecuteAoE(mode, preset, attributes, gameAct);
            }
            else
            {
                return AutoRotationHelper.ExecuteST(mode, preset, attributes, gameAct);
            }
        }

        private static bool AutomateTanking(CustomComboPreset preset, Presets.PresetAttributes attributes, uint gameAct)
        {
            var mode = cfg.DPSRotationMode;
            if (attributes.AutoAction!.IsAoE)
            {
                return AutoRotationHelper.ExecuteAoE(mode, preset, attributes, gameAct);
            }
            else
            {
                return AutoRotationHelper.ExecuteST(mode, preset, attributes, gameAct);
            }
        }

        private static bool AutomateHealing(CustomComboPreset preset, Presets.PresetAttributes attributes, uint gameAct)
        {
            var mode = cfg.HealerRotationMode;
            if (Player.Object.IsCasting()) return false;
            if (Environment.TickCount64 < LastHealAt + 1200) return false;

            if (attributes.AutoAction!.IsAoE)
            {
                var ret = AutoRotationHelper.ExecuteAoE(mode, preset, attributes, gameAct);
                return ret;
            }
            else
            {
                var ret = AutoRotationHelper.ExecuteST(mode, preset, attributes, gameAct);
                return ret;
            }
        }

        public static class AutoRotationHelper
        {
            private static bool InQueueWindow(uint outAct) =>
                QueueTiming.CanSend(outAct.ActionAttackType() is ActionAttackType.Ability, RemainingGCD,
                    ActionManager.Instance()->AnimationLock, cfg.QueueWindow);

            // 排隊窗內 GCD 的重鑄還在跑；只對重鑄放寬，其他不能放的原因（MP、狀態）照舊擋。
            private static bool StatusAllowsSend(uint outAct) =>
                ActionManager.Instance()->GetActionStatus(ActionType.Action, outAct,
                    checkRecastActive: cfg.QueueWindow <= 0) == 0;

            public static IGameObject? GetSingleTarget(Enum rotationMode)
            {
                if (rotationMode is DPSRotationMode dpsmode)
                {
                    if (Player.Object.GetRole() is CombatRole.Tank)
                    {
                        IGameObject? target = dpsmode switch
                        {
                            DPSRotationMode.Manual => Svc.Targets.Target,
                            DPSRotationMode.Highest_Max => TankTargeting.GetHighestMaxTarget(),
                            DPSRotationMode.Lowest_Max => TankTargeting.GetLowestMaxTarget(),
                            DPSRotationMode.Highest_Current => TankTargeting.GetHighestCurrentTarget(),
                            DPSRotationMode.Lowest_Current => TankTargeting.GetLowestCurrentTarget(),
                            DPSRotationMode.Tank_Target => Svc.Targets.Target,
                            DPSRotationMode.Nearest => DPSTargeting.GetNearestTarget(),
                            DPSRotationMode.Furthest => DPSTargeting.GetFurthestTarget(),
                            DPSRotationMode.ManualNonPlayer => CustomTargeting.GetManualNonPlayerTarget(),
                            _ => Svc.Targets.Target,
                        };
                        return target;
                    }
                    else
                    {
                        IGameObject? target = dpsmode switch
                        {
                            DPSRotationMode.Manual => Svc.Targets.Target,
                            DPSRotationMode.Highest_Max => DPSTargeting.GetHighestMaxTarget(),
                            DPSRotationMode.Lowest_Max => DPSTargeting.GetLowestMaxTarget(),
                            DPSRotationMode.Highest_Current => DPSTargeting.GetHighestCurrentTarget(),
                            DPSRotationMode.Lowest_Current => DPSTargeting.GetLowestCurrentTarget(),
                            DPSRotationMode.Tank_Target => DPSTargeting.GetTankTarget(),
                            DPSRotationMode.Nearest => DPSTargeting.GetNearestTarget(),
                            DPSRotationMode.Furthest => DPSTargeting.GetFurthestTarget(),
                            DPSRotationMode.ManualNonPlayer => CustomTargeting.GetManualNonPlayerTarget(),
                            _ => Svc.Targets.Target,
                        };
                        return target;
                    }
                }
                if (rotationMode is HealerRotationMode healermode)
                {
                    if (Player.Object.GetRole() != CombatRole.Healer) return null;
                    IGameObject? target = healermode switch
                    {
                        HealerRotationMode.Manual => HealerTargeting.ManualTarget(),
                        HealerRotationMode.Highest_Current => HealerTargeting.GetHighestCurrent(),
                        HealerRotationMode.Lowest_Current => HealerTargeting.GetLowestCurrent(),
                        _ => HealerTargeting.ManualTarget(),
                    };
                    return target;
                }

                return null;
            }

            public static bool ExecuteAoE(Enum mode, CustomComboPreset preset, Presets.PresetAttributes attributes, uint gameAct)
            {
                if (attributes.AutoAction!.IsHeal)
                {
                    LockedAoE = false;
                    LockedST = false;

                    uint outAct = OriginalHook(InvokeCombo(preset, attributes, ref gameAct, Player.Object));
                    if (!StatusAllowsSend(outAct) || !InQueueWindow(outAct)) return false;
                    if (!ActionReady(outAct))
                        return false;

                    if (HealerTargeting.CanAoEHeal(outAct))
                    {
                        var castTime = ActionManager.GetAdjustedCastTime(ActionType.Action, outAct);
                        bool orbwalking = cfg.OrbwalkerIntegration && OrbwalkerIPC.CanOrbwalk;
                        if (TimeMoving.TotalMilliseconds > 0 && castTime > 0 && !orbwalking)
                            return false;

                        var ret = ActionManager.Instance()->UseAction(ActionType.Action, Service.ActionReplacer.getActionHook?.IsEnabled == true ? gameAct : outAct);
                        var accepted = ActionAccepted(ret);

                        if (accepted)
                        {
                            LastHealAt = Environment.TickCount64 + castTime;
                            DiagNoteFired();
                        }

                        return accepted;
                    }
                }
                else
                {

                    var target = !cfg.DPSSettings.AoEIgnoreManual && cfg.DPSRotationMode == DPSRotationMode.Manual ? Svc.Targets.Target : DPSTargeting.BaseSelection.MaxBy(x => NumberOfEnemiesInRange(OriginalHook(gameAct), x, true));

                    // 總閘門的 CombatBypass 不看目標；FATE 繞過要這一招的目標本身是 FATE 怪。
                    if (cfg.InCombatOnly && NotInCombat && !CombatBypassFor(target))
                        return DiagCombatGate(true, target);

                    var numEnemies = NumberOfEnemiesInRange(OriginalHook(gameAct), target, true);
                    if (!_ninjaLockedAoE)
                    {
                        // 生效值可能來自 IPC 租約／目前職業的覆寫／全域設定
                        // (解析在 DPSSettingsIPCWrapper.ResolveAoETargets)。
                        // 取一次存起來，同一格內不重複解析。
                        var aoeTargets = cfg.DPSSettings.DPSAoETargets;
                        if (aoeTargets == null || numEnemies < aoeTargets)
                        {
                            LockedAoE = false;
                            return false;
                        }
                        else
                        {
                            LockedAoE = true;
                            LockedST = false;
                        }
                    }

                    uint outAct = OriginalHook(InvokeCombo(preset, attributes, ref gameAct));
                    if (outAct is All.SavageBlade) return true;
                    if (!CanQueue(outAct)) return false;
                    if (!ActionReady(outAct, true, true))
                        return false;

                    if (!StatusAllowsSend(outAct) || !InQueueWindow(outAct))
                        return false;

                    var sheet = ActionSheet[outAct];
                    var mustTarget = sheet.CanTargetHostile;

                    bool switched = SwitchOnDChole(attributes, outAct, ref target);
                    var castTime = ActionManager.GetAdjustedCastTime(ActionType.Action, outAct);
                    bool orbwalking = cfg.OrbwalkerIntegration && OrbwalkerIPC.CanOrbwalk;
                    if (TimeMoving.TotalMilliseconds > 0 && castTime > 0 && !orbwalking)
                        return DiagMovingCaster(mode is DPSRotationMode, outAct, preset);

                    // 深層迷宮會開打不安全的敵人 ⇒ 這一招不放、解除 AoE 鎖，讓單體預設（會改挑已在戰鬥中的目標）接手。
                    if (mode is DPSRotationMode && !DeepDungeonPullSafety.AllowsAction(outAct, target, mustTarget))
                    {
                        LockedAoE = false;
                        return false;
                    }

                    if (mustTarget || cfg.DPSSettings.AlwaysSelectTarget)
                        Svc.Targets.Target = target;

                    if (mustTarget && target is not null)
                    {
                        Svc.GameConfig.TryGet(Dalamud.Game.Config.UiControlOption.AutoFaceTargetOnAction, out uint original);
                        Svc.GameConfig.Set(Dalamud.Game.Config.UiControlOption.AutoFaceTargetOnAction, 1);
                        Vector3 pos = new(Player.Object.Position.X, Player.Object.Position.Y, Player.Object.Position.Z);
                        ActionManager.Instance()->AutoFaceTargetPosition(&pos, target.GameObjectId);
                        Svc.GameConfig.Set(Dalamud.Game.Config.UiControlOption.AutoFaceTargetOnAction, original);
                    }

                    //Chance target of target.GameObjectID can be null
                    var ret = ActionManager.Instance()->UseAction(
                        ActionType.Action,
                        Service.ActionReplacer.getActionHook?.IsEnabled == true ? gameAct : outAct,
                        (mustTarget && target != null) || switched ? target.GameObjectId : Player.Object.GameObjectId);

                    if (outAct is NIN.Ten or NIN.Chi or NIN.Jin or NIN.TenCombo or NIN.ChiCombo or NIN.JinCombo && ActionAccepted(ret))
                        _ninjaLockedAoE = true;
                    else
                        _ninjaLockedAoE = false;

                    if (ActionAccepted(ret))
                        DiagNoteFired();

                    return true;

                }
                return false;
            }

            public static bool ExecuteST(Enum mode, CustomComboPreset preset, Presets.PresetAttributes attributes, uint gameAct)
            {
                var diag = mode is DPSRotationMode;
                if (_ninjaLockedAoE) return DiagSt(diag, NoActStOutcome.Transient);
                var target = GetSingleTarget(mode);
                var pullBlocked = false;
                if (mode is DPSRotationMode dpsMode && !DeepDungeonPullSafety.AllowsTarget(target))
                {
                    // 有替代目標就改打它；沒有就保留原目標，但下面不對它出手、也不選它。
                    var fallback = PullFallback(dpsMode);
                    if (fallback is null)
                        pullBlocked = true;
                    else
                        target = fallback;
                }

                var outAct = OriginalHook(InvokeCombo(preset, attributes, ref gameAct, target));
                if (!CanQueue(outAct))
                {
                    return DiagSt(diag, NoActStOutcome.Transient);
                }

                // 「目標是不是 FATE 怪」看選出來的目標，不看賢者把治療轉給隊友之後的目標。
                var selectedTarget = target;
                bool switched = SwitchOnDChole(attributes, outAct, ref target);

                var canUseSelf = NIN.MudraSigns.Contains(outAct)
                    ? target is not null && target.IsHostile()
                    : ActionManager.CanUseActionOnTarget(outAct, Player.GameObject);

                var blockedSelfBuffs = GetCooldown(outAct).CooldownTotal >= 5;

                // 治療模式的目標是隊友，FATE 繞過不看目標。
                if (cfg.InCombatOnly && NotInCombat && !(mode is DPSRotationMode ? CombatBypassFor(selectedTarget) : CombatBypass) && !(canUseSelf && cfg.BypassBuffs && !blockedSelfBuffs))
                    return DiagCombatGate(diag, selectedTarget);

                if (target is null && !canUseSelf)
                    return DiagSt(diag, NoActStOutcome.NoTarget);

                var areaTargeted = ActionSheet[outAct].TargetArea;
                var canUseTarget = target is not null && ActionManager.CanUseActionOnTarget(outAct, target.Struct());

                var inRange = target is null
                    ? canUseSelf
                    : IsInLineOfSight(target) && (NIN.MudraSigns.Contains(outAct)
                        ? GetTargetDistance(target) <= 20f
                        : InActionRange(outAct, target));

                var canUse = (canUseSelf || canUseTarget || areaTargeted) && InQueueWindow(outAct);

                // IPC 只在真的要送出時問（目標本身上面已問過、走快取）；自用技不打目標，照常放。
                var hitsTarget = canUseTarget || areaTargeted;
                if (mode is DPSRotationMode && (pullBlocked && hitsTarget ||
                    canUse && (inRange || areaTargeted) && !DeepDungeonPullSafety.AllowsAction(outAct, target, hitsTarget)))
                    return DiagSt(diag, NoActStOutcome.Transient);

                if ((canUse || cfg.DPSSettings.AlwaysSelectTarget) && !pullBlocked)
                    Svc.Targets.Target = target;

                var castTime = ActionManager.GetAdjustedCastTime(ActionType.Action, outAct);
                bool orbwalking = cfg.OrbwalkerIntegration && OrbwalkerIPC.CanOrbwalk;
                if (TimeMoving.TotalMilliseconds > 0 && castTime > 0 && !orbwalking)
                    return DiagMovingCaster(diag, outAct, preset);

                if (canUse && (inRange || areaTargeted))
                {
                    var ret = ActionManager.Instance()->UseAction(ActionType.Action, Service.ActionReplacer.getActionHook?.IsEnabled == true ? gameAct : outAct, canUseTarget || areaTargeted ? target.GameObjectId : Player.Object.GameObjectId);
                    var accepted = ActionAccepted(ret);
                    if (mode is HealerRotationMode && accepted)
                        LastHealAt = Environment.TickCount64 + castTime;

                    if (accepted)
                        DiagNoteFired();
                    else
                        DiagSt(diag, NoActStOutcome.Transient);

                    return accepted;
                }

                if (canUse)
                    return DiagSt(diag, NoActStOutcome.OutOfRange);

                return DiagSt(diag, canUseSelf || canUseTarget || areaTargeted
                    ? NoActStOutcome.Transient
                    : NoActStOutcome.TargetNotAttackable);
            }

            /// <summary> 深層迷宮裡 BMR 擋下選到的目標時的替代：可選目標裡已在戰鬥中、最近的一隻。 </summary>
            /// <returns> null＝沒有替代，或目標是玩家自己選的（手動類模式不替玩家換目標）。 </returns>
            private static IGameObject? PullFallback(DPSRotationMode mode)
            {
                if (mode is DPSRotationMode.Manual or DPSRotationMode.ManualNonPlayer ||
                    mode is DPSRotationMode.Tank_Target && Player.Object.GetRole() is CombatRole.Tank)
                    return null;

                var priority = DPSTargeting.AnyPriority();
                IGameObject? best = null;
                var bestDistance = float.MaxValue;
                foreach (var x in Svc.Objects)
                {
                    if (x is not IBattleChara c || !c.Struct()->InCombat || !DPSTargeting.InBaseSelection(x, priority))
                        continue;
                    var distance = GetTargetDistance(x);
                    if (distance < bestDistance)
                    {
                        best = x;
                        bestDistance = distance;
                    }
                }

                return best;
            }

            private static bool SwitchOnDChole(Presets.PresetAttributes attributes, uint outAct, ref IGameObject? newtarget)
            {
                if (outAct is SGE.Druochole && !attributes.AutoAction!.IsHeal)
                {
                    if (GetPartyMembers()
                        .Where(x => !x.BattleChara.IsDead &&
                            x.BattleChara.IsTargetable &&
                            GetTargetDistance(x.BattleChara) <= QueryRange &&
                            IsInLineOfSight(x.BattleChara))
                        .OrderBy(x => GetTargetHPPercent(x.BattleChara))
                        .Select(x => x.BattleChara)
                        .TryGetFirst(out newtarget))
                    {
                        return true;
                    }
                }

                return false;
            }

            public static uint InvokeCombo(CustomComboPreset preset, Presets.PresetAttributes attributes, ref uint originalAct, IGameObject? optionalTarget = null)
            {
                if (attributes.ReplaceSkill is null) return originalAct;
                var outAct = attributes.ReplaceSkill.ActionIDs.FirstOrDefault();
                foreach (var actToCheck in attributes.ReplaceSkill.ActionIDs)
                {
                    var customCombo = FindCombo(preset);
                    if (customCombo != null)
                    {
                        if (customCombo.TryInvoke(actToCheck, out var changedAct, optionalTarget))
                        {
                            originalAct = actToCheck;
                            outAct = changedAct;
                            break;
                        }
                    }
                }

                return outAct;
            }

            // 同 CustomCombos.FirstOrDefault(x => x.Preset == preset)，不配置閉包與委派。
            private static CustomComboNS.CustomCombo? FindCombo(CustomComboPreset preset)
            {
                foreach (var combo in Service.ActionReplacer.CustomCombos)
                    if (combo.Preset == preset)
                        return combo;
                return null;
            }
        }

        public class DPSTargeting
        {
            private static bool Query(IGameObject x) =>
                x is IBattleChara chara &&
                !chara.IsDead &&
                chara.IsTargetable &&
                chara.IsHostile() &&
                IsInRange(chara, cfg.DPSSettings.MaxDistance) &&
                GetTargetHeightDifference(chara) <= cfg.DPSSettings.MaxDistance &&
                !TargetIsInvincible(chara) &&
                !Service.Configuration.IgnoredNPCs.ContainsKey(chara.BaseId) &&
                ((cfg.DPSSettings.OnlyAttackInCombat && chara.Struct()->InCombat) || !cfg.DPSSettings.OnlyAttackInCombat) &&
                IsInLineOfSight(chara);

            public static IEnumerable<IGameObject> BaseSelection => Svc.Objects.Any(x => Query(x) && IsPriority(x))
                                                                        ? Svc.Objects.Where(x => Query(x) && IsPriority(x))
                                                                        : Svc.Objects.Where(x => Query(x));

            /// <summary> <see cref="BaseSelection" /> 的前半：有沒有優先目標（同樣的判斷順序，不配置）。 </summary>
            internal static bool AnyPriority()
            {
                foreach (var x in Svc.Objects)
                    if (Query(x) && IsPriority(x))
                        return true;
                return false;
            }

            /// <summary> <see cref="BaseSelection" /> 的篩選條件；<paramref name="anyPriority" /> 取同一幀 <see cref="AnyPriority" /> 的結果。 </summary>
            internal static bool InBaseSelection(IGameObject x, bool anyPriority) =>
                anyPriority ? Query(x) && IsPriority(x) : Query(x);

            private static bool IsPriority(IGameObject x)
            {
                if (x is IBattleChara chara)
                {
                    bool isFate = cfg.DPSSettings.FATEPriority && x.Struct()->FateId != 0 && InFATE();
                    bool isQuest = cfg.DPSSettings.QuestPriority && IsQuestMob(x);

                    return isFate || isQuest;
                }
                return false;
            }

            public static bool IsCombatPriority(IGameObject x)
            {
                if (x is IBattleChara chara)
                {
                    if (!cfg.DPSSettings.PreferNonCombat) return true;
                    bool inCombat = cfg.DPSSettings.PreferNonCombat && !chara.Struct()->InCombat;
                    return inCombat;
                }
                return false;
            }

            public static IGameObject? GetTankTarget()
            {
                var tank = GetPartyMembers().FirstOrDefault(x => x.BattleChara?.GetRole() == CombatRole.Tank || HasStatusEffect(3615, x.BattleChara, true));
                if (tank == null)
                    return null;

                return tank.BattleChara.TargetObject;
            }

            public static IGameObject? GetNearestTarget()
            {
                return BaseSelection
                    .OrderByDescending(x => IsCombatPriority(x))
                    .ThenBy(x => GetTargetDistance(x))
                    .FirstOrDefault();
            }

            public static IGameObject? GetFurthestTarget()
            {
                return BaseSelection
                    .OrderByDescending(x => IsCombatPriority(x))
                    .ThenByDescending(x => GetTargetDistance(x))
                    .FirstOrDefault();
            }

            public static IGameObject? GetLowestCurrentTarget()
            {
                return BaseSelection
                    .OrderByDescending(x => IsCombatPriority(x))
                    .ThenBy(x => GetTargetCurrentHP(x))
                    .FirstOrDefault();
            }

            public static IGameObject? GetHighestCurrentTarget()
            {
                return BaseSelection
                    .OrderByDescending(x => IsCombatPriority(x))
                    .ThenByDescending(x => GetTargetCurrentHP(x))
                    .FirstOrDefault();
            }

            public static IGameObject? GetLowestMaxTarget()
            {

                return BaseSelection
                    .OrderByDescending(x => IsCombatPriority(x))
                    .ThenBy(x => GetTargetMaxHP(x))
                    .ThenBy(x => GetTargetHPPercent(x))
                    .ThenBy(x => GetTargetDistance(x))
                    .FirstOrDefault();
            }

            public static IGameObject? GetHighestMaxTarget()
            {
                return BaseSelection
                    .OrderByDescending(x => IsCombatPriority(x))
                    .ThenByDescending(x => GetTargetMaxHP(x))
                    .ThenBy(x => GetTargetHPPercent(x))
                    .FirstOrDefault();
            }
        }

        public static class HealerTargeting
        {
            internal static IGameObject? ManualTarget()
            {
                if (Svc.Targets.Target == null) return null;
                var t = Svc.Targets.Target;
                bool goodToHeal = GetTargetHPPercent(t) <= (TargetHasRegen(t) ? cfg.HealerSettings.SingleTargetRegenHPP : cfg.HealerSettings.SingleTargetHPP);
                if (goodToHeal && !t.IsHostile())
                {
                    return t;
                }
                return null;
            }
            internal static IGameObject? GetHighestCurrent()
            {
                if (GetPartyMembers().Count == 0) return Player.Object;
                var target = GetPartyMembers()
                    .Where(x => !x.BattleChara.IsDead &&
                        x.BattleChara.IsTargetable &&
                        GetTargetDistance(x.BattleChara) <= QueryRange &&
                        GetTargetHPPercent(x.BattleChara) <= (TargetHasRegen(x.BattleChara) ? cfg.HealerSettings.SingleTargetRegenHPP : cfg.HealerSettings.SingleTargetHPP) &&
                        IsInLineOfSight(x.BattleChara))
                    .OrderByDescending(x => GetTargetHPPercent(x.BattleChara)).FirstOrDefault();
                return target?.BattleChara;
            }

            internal static IGameObject? GetLowestCurrent()
            {
                if (GetPartyMembers().Count == 0) return Player.Object;
                var target = GetPartyMembers()
                    .Where(x => !x.BattleChara.IsDead &&
                        x.BattleChara.IsTargetable &&
                        GetTargetDistance(x.BattleChara) <= QueryRange &&
                        GetTargetHPPercent(x.BattleChara) <= (TargetHasRegen(x.BattleChara) ? cfg.HealerSettings.SingleTargetRegenHPP : cfg.HealerSettings.SingleTargetHPP) &&
                        IsInLineOfSight(x.BattleChara))
                    .OrderBy(x => GetTargetHPPercent(x.BattleChara)).FirstOrDefault();
                return target?.BattleChara;
            }

            internal static bool CanAoEHeal(uint outAct = 0)
            {
                int memberCount;
                try
                {
                    var members = GetPartyMembers()
                        .Where(x => x.BattleChara is not null &&
                            !x.BattleChara.IsDead &&
                            x.BattleChara.IsTargetable &&
                            (outAct == 0
                                ? GetTargetDistance(x.BattleChara) <= 15f
                                : InActionRange(outAct, x.BattleChara)) &&
                            GetTargetHPPercent(x.BattleChara) <= cfg.HealerSettings.AoETargetHPP);
                    memberCount = members.Count();
                }
                catch { memberCount = 0; }

                if (memberCount < cfg.HealerSettings.AoEHealTargetCount)
                    return false;

                return true;
            }

            private static bool TargetHasRegen(IGameObject? target)
            {
                if (target is null) return false;
                return JobID switch
                {
                    AST.JobID => HasStatusEffect(AST.Buffs.AspectedBenefic, target, true),
                    WHM.JobID => HasStatusEffect(WHM.Buffs.Regen, target, true),
                    _ => false,
                };
            }
        }

        public static class TankTargeting
        {
            public static IGameObject? GetLowestCurrentTarget()
            {
                return DPSTargeting.BaseSelection
                    .OrderByDescending(x => DPSTargeting.IsCombatPriority(x))
                    .ThenByDescending(x => x.TargetObject?.GameObjectId != Player.Object?.GameObjectId)
                    .ThenBy(x => GetTargetCurrentHP(x))
                    .ThenBy(x => GetTargetHPPercent(x)).FirstOrDefault();
            }

            public static IGameObject? GetHighestCurrentTarget()
            {
                return DPSTargeting.BaseSelection
                    .OrderByDescending(x => DPSTargeting.IsCombatPriority(x))
                    .ThenByDescending(x => x.TargetObject?.GameObjectId != Player.Object?.GameObjectId)
                    .ThenByDescending(x => GetTargetCurrentHP(x))
                    .ThenBy(x => GetTargetHPPercent(x)).FirstOrDefault();
            }

            public static IGameObject? GetLowestMaxTarget()
            {
                var t = DPSTargeting.BaseSelection
                    .OrderByDescending(x => DPSTargeting.IsCombatPriority(x))
                    .ThenByDescending(x => x.TargetObject?.GameObjectId != Player.Object?.GameObjectId)
                    .ThenBy(x => GetTargetMaxHP(x))
                    .ThenBy(x => GetTargetHPPercent(x)).FirstOrDefault();

                return t;
            }

            public static IGameObject? GetHighestMaxTarget()
            {
                return DPSTargeting.BaseSelection
                    .OrderByDescending(x => DPSTargeting.IsCombatPriority(x))
                    .ThenByDescending(x => x.TargetObject?.GameObjectId != Player.Object?.GameObjectId)
                    .ThenByDescending(x => GetTargetMaxHP(x))
                    .ThenBy(x => GetTargetHPPercent(x)).FirstOrDefault();
            }
        }

        public static class CustomTargeting
        {
            /// <summary>
            /// 获取手动选择的非玩家目标。
            /// </summary>
            public static IGameObject? GetManualNonPlayerTarget()
            {
                IGameObject? target = Svc.Targets.Target;

                if (IsValidNonPlayerTarget(target))
                {
                    return target;
                }

                return null;
            }

            /// <summary>
            /// 检查目标是否为有效的非玩家目标。
            /// </summary>
            private static bool IsValidNonPlayerTarget(IGameObject? target)
            {
                if (target == null)
                    return false;

                if (target.ObjectKind == ObjectKind.Player)
                    return false;

                if (target is not IBattleChara chara || chara.IsDead || !chara.IsTargetable)
                    return false;

                if (GetTargetDistance(target) > 25)
                    return false;

                if (cfg.DPSSettings.OnlyAttackInCombat && !chara.Struct()->InCombat)
                    return false;

                return true;
            }

            /// <summary>
            /// 获取目标与玩家的距离。
            /// </summary>
            private static float GetTargetDistance(IGameObject target)
            {
                var targetStruct = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;
                float centerDistance = Vector3.Distance(Player.Object.Position, target.Position);
                return Math.Max(0, centerDistance - targetStruct->HitboxRadius);
            }
        }

    }
}