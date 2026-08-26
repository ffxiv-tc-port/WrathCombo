using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using WrathCombo.Core;
using WrathCombo.CustomComboNS;
using WrathCombo.Data;
using WrathCombo.Extensions;

namespace WrathCombo.Combos.PvE;

internal partial class All
{
    public const byte JobID = 0;

    /// Used to block user input.
    public const uint SavageBlade = 11;

    public const uint
        Sprint = 3;

    private const uint
        IsleSprint = 31314;

    public static class Buffs
    {
        public const ushort
            Raised = 148,
            Transcendent = 2648;
    }

    public static class Enums
    {
        /// <summary>
        ///     Whether abilities should be restricted to Bosses or not.
        /// </summary>
        internal enum BossAvoidance
        {
            Off = 1,
            On = 2,
        }

        /// <summary>
        ///     Whether abilities should be restricted to while in a party or not.
        /// </summary>
        internal enum PartyRequirement
        {
            No,
            Yes,
        }
    }

    public static class Debuffs
    {
        public const ushort
            Stun = 2,
            // 台服 Status.csv #14「加重」——移動速度降低，傷腿(7554) 施加的就是這個。
            // ⚠️ 不要跟 #9「減速」(自動攻擊/詠唱變慢) 搞混，那是另一件事。
            Heavy = 14,
            Weakness = 43,
            BrinkOfDeath = 44;
    }

    internal class ALL_IslandSanctuary_Sprint : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_IslandSanctuary_Sprint;

        protected override uint Invoke(uint actionID) =>
            actionID is Sprint && Svc.ClientState.TerritoryType is 1055
                ? IsleSprint
                : actionID;
    }

    //Tank Features
    internal class ALL_Tank_Interrupt : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Tank_Interrupt;

        protected override uint Invoke(uint actionID)
        {
            var tar = IsEnabled(CustomComboPreset.ALL_Tank_Interrupt_Retarget) ? SimpleTarget.InterruptableEnemy : CurrentTarget;
            switch (actionID)
            {
                case RoleActions.Tank.LowBlow or PLD.ShieldBash when CanInterruptEnemy(null, tar) && ActionReady(RoleActions.Tank.Interject):
                    return RoleActions.Tank.Interject.Retarget(actionID, tar);

                case RoleActions.Tank.LowBlow or PLD.ShieldBash when TargetIsCasting() && ActionReady(RoleActions.Tank.LowBlow) && !TargetIsBoss():
                    return RoleActions.Tank.LowBlow.Retarget(actionID, tar);

                case PLD.ShieldBash when IsOnCooldown(RoleActions.Tank.LowBlow):
                default:
                    return actionID;
            }
        }
    }

    internal class ALL_Tank_Reprisal : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Tank_Reprisal;

        protected override uint Invoke(uint actionID) =>
            actionID is RoleActions.Tank.Reprisal && GetStatusEffectRemainingTime(RoleActions.Tank.Debuffs.Reprisal, CurrentTarget, true) > Config.ALL_Tank_Reprisal_Threshold && IsOffCooldown(RoleActions.Tank.Reprisal)
                ? SavageBlade
                : actionID;
    }

    internal class ALL_Tank_Shirk : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Tank_ShirkRetargeting;

        protected override uint Invoke(uint actionID)
        {
            if (actionID is not RoleActions.Tank.Shirk)
                return actionID;

            var target =
                IsNotEnabled(CustomComboPreset.ALL_Tank_ShirkRetargeting_Healer)
                    ? SimpleTarget.AnyLivingTank
                    : SimpleTarget.AnyLivingHealer;

            if (IsEnabled(CustomComboPreset.ALL_Tank_ShirkRetargeting_Fallback))
                target ??= SimpleTarget.AnyLivingSupport;

            RoleActions.Tank.Shirk.Retarget(target, dontCull: true);

            return actionID;
        }
    }

    //Healer Features
    internal class ALL_Healer_Raise : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Healer_Raise;

        protected override uint Invoke(uint actionID)
        {
            List<uint> replacedActions =
                [WHM.Raise, AST.Ascend, SGE.Egeiro, SCH.Resurrection];
            if (!replacedActions.Contains(actionID))
                return actionID;
            if (actionID is SCH.Resurrection &&
                LocalPlayer.ClassJob.RowId is not SCH.JobID)
                return actionID;

            if (ActionReady(RoleActions.Magic.Swiftcast))
                return RoleActions.Magic.Swiftcast;

            if (actionID == WHM.Raise &&
                IsEnabled(CustomComboPreset.WHM_ThinAirRaise) &&
                ActionReady(WHM.ThinAir) &&
                !HasStatusEffect(WHM.Buffs.ThinAir))
                return WHM.ThinAir;

            if (IsEnabled(CustomComboPreset.ALL_Healer_Raise_Retarget))
                return actionID.Retarget(replacedActions.ToArray(),
                    SimpleTarget.Stack.AllyToRaise, dontCull: true);

            return actionID;
        }
    }

    internal class ALL_Healer_EsunaRetargeting : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Healer_EsunaRetargeting;

        protected override uint Invoke(uint actionID)
        {
            if (actionID is not RoleActions.Healer.Esuna)
                return actionID;

            RoleActions.Healer.Esuna.Retarget(SimpleTarget.Stack.AllyToEsuna, dontCull: true);

            return actionID;
        }
    }
    
    internal class ALL_Healer_RescueRetargeting : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Healer_RescueRetargeting;

        protected override uint Invoke(uint actionID)
        {
            if (actionID is not RoleActions.Healer.Rescue)
                return actionID;

            var target =
                SimpleTarget.UIMouseOverTarget.IfNotThePlayer().IfInParty() ??
                
                //Field Mouseover
                (Config.ALL_Healer_RescueRetargetingOptions[0]
                    ? SimpleTarget.ModelMouseOverTarget.IfNotThePlayer().IfInParty()
                    : null) ??

                //Focus target retarget
                (Config.ALL_Healer_RescueRetargetingOptions[1]
                    ? SimpleTarget.FocusTarget.IfNotThePlayer().IfInParty()
                    : null) ??
                
                //Focus target retarget
                (Config.ALL_Healer_RescueRetargetingOptions[2]
                    ? SimpleTarget.SoftTarget.IfNotThePlayer().IfInParty()
                    : null) ??
                
                SimpleTarget.HardTarget.IfNotThePlayer().IfInParty();

            return actionID.Retarget(target);
        }
    }

    //Caster Features
    internal class ALL_Caster_Addle : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Caster_Addle;

        protected override uint Invoke(uint actionID) =>
            actionID is RoleActions.Caster.Addle && HasStatusEffect(RoleActions.Caster.Debuffs.Addle, CurrentTarget, true) && IsOffCooldown(RoleActions.Caster.Addle)
                ? SavageBlade
                : actionID;
    }

    internal class ALL_Caster_Raise : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Caster_Raise;

        protected override uint Invoke(uint actionID)
        {
            List<uint> replacedActions =
                [BLU.AngelWhisper, RDM.Verraise, SMN.Resurrection];
            if (!replacedActions.Contains(actionID))
                return actionID;
            if (actionID is SMN.Resurrection &&
                LocalPlayer.ClassJob.RowId is not SMN.JobID)
                return actionID;

            if (HasStatusEffect(RoleActions.Magic.Buffs.Swiftcast) ||
                HasStatusEffect(RDM.Buffs.Dualcast))
                if (IsEnabled(CustomComboPreset.ALL_Caster_Raise_Retarget))
                    return actionID.Retarget(replacedActions.ToArray(),
                        SimpleTarget.Stack.AllyToRaise, dontCull: true);
                else
                    return actionID;

            if (IsOffCooldown(RoleActions.Magic.Swiftcast))
                return RoleActions.Magic.Swiftcast;

            if (LocalPlayer.ClassJob.RowId is RDM.JobID &&
                ActionReady(RDM.Vercure))
                return RDM.Vercure;

            if (IsEnabled(CustomComboPreset.ALL_Caster_Raise_Retarget))
                return actionID.Retarget(replacedActions.ToArray(),
                    SimpleTarget.Stack.AllyToRaise, dontCull: true);

            return actionID;
        }
    }

    //Melee DPS Features
    internal class ALL_Melee_Feint : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Melee_Feint;

        protected override uint Invoke(uint actionID) =>
            actionID is RoleActions.Melee.Feint && HasStatusEffect(RoleActions.Melee.Debuffs.Feint, CurrentTarget, true) && IsOffCooldown(RoleActions.Melee.Feint)
                ? SavageBlade
                : actionID;
    }

    internal class ALL_Melee_TrueNorth : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Melee_TrueNorth;

        protected override uint Invoke(uint actionID) =>
            actionID is RoleActions.Melee.TrueNorth && HasStatusEffect(RoleActions.Melee.Buffs.TrueNorth)
                ? SavageBlade
                : actionID;
    }

    //Ranged Physical Features
    internal class ALL_Ranged_Mitigation : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Ranged_Mitigation;

        protected override uint Invoke(uint actionID) =>
            actionID is BRD.Troubadour or MCH.Tactician or DNC.ShieldSamba &&
            (HasStatusEffect(BRD.Buffs.Troubadour, anyOwner: true) || HasStatusEffect(MCH.Buffs.Tactician, anyOwner: true) ||
             HasStatusEffect(DNC.Buffs.ShieldSamba, anyOwner: true)) &&
            IsOffCooldown(actionID)
                ? SavageBlade
                : actionID;
    }

    internal class ALL_Ranged_Interrupt : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Ranged_Interrupt;

        protected override uint Invoke(uint actionID) =>
            actionID is RoleActions.PhysRanged.FootGraze && CanInterruptEnemy() && ActionReady(RoleActions.PhysRanged.HeadGraze)
                ? RoleActions.PhysRanged.HeadGraze
                : actionID;
    }

    /// <summary>
    ///     自動傷腿：目標身上沒有「加重」時，把傷足換成傷腿補上移動減速。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         接線方式沿用同檔的 <see cref="ALL_Ranged_Interrupt" />（傷足→傷頭）：
    ///         都是掛在「傷足」這顆工具鍵上的物理遠程 role 功能。
    ///     </para>
    ///     <para>
    ///         ⚠️ 兩個功能共用同一顆按鍵。<c>ActionReplacer</c> 是
    ///         <c>OrderByDescending(x =&gt; x.Preset)</c>，本功能的列舉值比打斷那條大，
    ///         所以會<b>先</b>被評估到 —— 因此這裡主動讓路：可打斷時原樣回傳
    ///         <c>actionID</c>，<c>TryInvoke</c> 就會回 <see langword="false" />，
    ///         打斷那條才接得到。不要改成靠列舉值大小去隱含排序。
    ///     </para>
    /// </remarks>
    internal class ALL_Ranged_LegGraze : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.ALL_Ranged_LegGraze;

        protected override uint Invoke(uint actionID)
        {
            if (actionID is not RoleActions.PhysRanged.FootGraze)
                return actionID;

            // 打斷永遠比補加重重要 —— 讓路給 ALL_Ranged_Interrupt。
            if (IsEnabled(CustomComboPreset.ALL_Ranged_Interrupt) &&
                CanInterruptEnemy() &&
                ActionReady(RoleActions.PhysRanged.HeadGraze))
                return actionID;

            if (!RoleActions.PhysRanged.CanLegGraze())
                return actionID;

            if (CurrentTarget is not IBattleChara target)
                return actionID;

            if (!target.IsHostile() || !target.IsTargetable || target.IsBoss())
                return actionID;

            // 觸發時機（預設＝只對正在以我為目標的敵人，也就是手動風箏的情境）。
            if (Config.ALL_Ranged_LegGraze_Trigger == 0 &&
                (LocalPlayer is null ||
                 target.TargetObjectId != LocalPlayer.GameObjectId))
                return actionID;

            // 已經有加重就不要再補。
            if (HasStatusEffect(Debuffs.Heavy, target, true))
                return actionID;

            // 遞減免疫：照暈眩的樣式，同一隻身上連續施加超過上限就停手。
            if (!ICDTracker.StatusIsExpired(Debuffs.Heavy, target.GameObjectId) &&
                ICDTracker.NumberOfTimesApplied(Debuffs.Heavy, target.GameObjectId) >=
                Config.ALL_Ranged_LegGraze_MaxApplications)
                return actionID;

            // MonsterDex 明確說「不吃加重」才跳過；沒安裝／查無資料一律照放。
            if (!MonsterVulnerability.CanBeHeavied(target))
                return actionID;

            return RoleActions.PhysRanged.LegGraze;
        }
    }
}
