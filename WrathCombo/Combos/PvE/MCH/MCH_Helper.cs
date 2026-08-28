using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using WrathCombo.CustomComboNS;
using WrathCombo.CustomComboNS.Functions;
using static WrathCombo.Combos.PvE.MCH.Config;
using static WrathCombo.CustomComboNS.Functions.CustomComboFunctions;
using static WrathCombo.Data.ActionWatching;
using WrathCombo.Extensions;
using WrathCombo.Services;
using EZ = ECommons.Throttlers.EzThrottler;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
namespace WrathCombo.Combos.PvE;

internal partial class MCH
{
    internal static int BSUsed =>
        CombatActions.Count(x => x == BarrelStabilizer);

    internal static bool UseGaussRound =>
        GetRemainingCharges(OriginalHook(GaussRound)) >= GetRemainingCharges(OriginalHook(Ricochet));

    internal static bool UseRicochet =>
        GetRemainingCharges(OriginalHook(Ricochet)) > GetRemainingCharges(OriginalHook(GaussRound));

    internal static bool HasNotWeaved =>
        GetAttackType(LastAction) != ActionAttackType.Ability;

    #region Queen

    internal static bool UseQueen()
    {
        if (!HasStatusEffect(Buffs.Wildfire) &&
            !JustUsed(OriginalHook(Heatblast)) && ActionReady(RookAutoturret) &&
            !RobotActive && Battery >= 50)
        {
            if ((MCH_ST_Adv_Turret_SubOption == 0 || InBossEncounter() ||
                 IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) && InBossEncounter()) &&
                (GetCooldownRemainingTime(Wildfire) > GCD || !LevelChecked(Wildfire)))
            {
                if (LevelChecked(BarrelStabilizer))
                {
                    //1min
                    if (BSUsed == 1 && Battery >= 90)
                        return true;

                    //even mins
                    if (BSUsed >= 2 && Battery == 100)
                        return true;

                    //odd mins 1st queen
                    if (BSUsed >= 2 && Battery is 50 && LastSummonBattery is 100)
                        return true;

                    //odd mins 2nd queen
                    if ((BSUsed % 3 is 2 && Battery >= 60 ||
                         BSUsed % 3 is 0 && Battery >= 70 ||
                         BSUsed % 3 is 1 && Battery >= 80) && LastSummonBattery is 50)
                        return true;
                }

                if (!LevelChecked(BarrelStabilizer))
                    return true;
            }

            if (IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) && !InBossEncounter() && Battery is 100 ||
                MCH_ST_Adv_Turret_SubOption == 1 && !InBossEncounter() && Battery >= MCH_ST_TurretUsage)
                return true;
        }

        return false;
    }

    #endregion

    #region Reassembled

    internal static bool ReassembledExcavatorST =>
        IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && MCH_ST_Reassembled[0] && (HasStatusEffect(Buffs.Reassembled) || !HasStatusEffect(Buffs.Reassembled)) ||
        IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && !MCH_ST_Reassembled[0] && !HasStatusEffect(Buffs.Reassembled) ||
        !HasStatusEffect(Buffs.Reassembled) && GetRemainingCharges(Reassemble) <= MCH_ST_ReassemblePool ||
        !IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble);

    internal static bool ReassembledChainsawST =>
        IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && MCH_ST_Reassembled[1] && (HasStatusEffect(Buffs.Reassembled) || !HasStatusEffect(Buffs.Reassembled)) ||
        IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && !MCH_ST_Reassembled[1] && !HasStatusEffect(Buffs.Reassembled) ||
        !HasStatusEffect(Buffs.Reassembled) && GetRemainingCharges(Reassemble) <= MCH_ST_ReassemblePool ||
        !IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble);

    internal static bool ReassembledAnchorST =>
        IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && MCH_ST_Reassembled[2] && (HasStatusEffect(Buffs.Reassembled) || !HasStatusEffect(Buffs.Reassembled)) ||
        IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && !MCH_ST_Reassembled[2] && !HasStatusEffect(Buffs.Reassembled) ||
        !HasStatusEffect(Buffs.Reassembled) && GetRemainingCharges(Reassemble) <= MCH_ST_ReassemblePool ||
        !IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble);

    internal static bool ReassembledDrillST =>
        IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && MCH_ST_Reassembled[3] && (HasStatusEffect(Buffs.Reassembled) || !HasStatusEffect(Buffs.Reassembled)) ||
        IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && !MCH_ST_Reassembled[3] && !HasStatusEffect(Buffs.Reassembled) ||
        !HasStatusEffect(Buffs.Reassembled) && GetRemainingCharges(Reassemble) <= MCH_ST_ReassemblePool ||
        !IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble);

    internal static bool Reassembled()
    {
        if (!JustUsed(OriginalHook(Heatblast)) && !HasStatusEffect(Buffs.Reassembled) &&
            ActionReady(Reassemble) && !JustUsed(OriginalHook(Heatblast)))
        {
            if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) && !InBossEncounter() ||
                 IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && MCH_ST_Reassembled[0] &&
                 (MCH_ST_Adv_Excavator_SubOption == 1 && !InBossEncounter() ||
                  IsNotEnabled(CustomComboPreset.MCH_ST_Adv_TurretQueen))) &&
                LevelChecked(Excavator) && HasStatusEffect(Buffs.ExcavatorReady))
                return true;

            if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) && InBossEncounter() ||
                 IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && MCH_ST_Reassembled[0] &&
                 IsEnabled(CustomComboPreset.MCH_ST_Adv_TurretQueen) &&
                 (MCH_ST_Adv_Excavator_SubOption == 0 ||
                  MCH_ST_Adv_Excavator_SubOption == 1 && InBossEncounter())) &&
                LevelChecked(Excavator) && HasStatusEffect(Buffs.ExcavatorReady) &&
                (BSUsed is 1 ||
                 BSUsed % 3 is 2 && Battery <= 40 ||
                 BSUsed % 3 is 0 && Battery <= 50 ||
                 BSUsed % 3 is 1 && Battery <= 60 ||
                 GetStatusEffectRemainingTime(Buffs.ExcavatorReady) <= 6))
                return true;

            if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) ||
                 IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && MCH_ST_Reassembled[1]) &&
                !LevelChecked(Excavator) && !MaxBattery && LevelChecked(Chainsaw) &&
                GetCooldownRemainingTime(Chainsaw) <= GCD)
                return true;

            if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) ||
                 IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && MCH_ST_Reassembled[2]) &&
                !MaxBattery && LevelChecked(AirAnchor) &&
                GetCooldownRemainingTime(AirAnchor) <= GCD)
                return true;

            if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) ||
                 IsEnabled(CustomComboPreset.MCH_ST_Adv_Reassemble) && MCH_ST_Reassembled[3]) &&
                LevelChecked(Drill) &&
                (!LevelChecked(AirAnchor) && MCH_ST_Reassembled[2] || !MCH_ST_Reassembled[2]) &&
                ActionReady(Drill))
                return true;
        }

        return false;
    }

    #endregion

    #region Cooldowns

    internal static bool DrillCD =>
        !LevelChecked(Drill) ||
        !TraitLevelChecked(Traits.EnhancedMultiWeapon) && GetCooldownRemainingTime(Drill) >= 9 ||
        TraitLevelChecked(Traits.EnhancedMultiWeapon) && GetRemainingCharges(Drill) < GetMaxCharges(Drill) && GetCooldownRemainingTime(Drill) >= 9;

    internal static bool AnchorCD =>
        !LevelChecked(AirAnchor) ||
        LevelChecked(AirAnchor) && GetCooldownRemainingTime(AirAnchor) >= 9;

    internal static bool SawCD =>
        !LevelChecked(Chainsaw) ||
        LevelChecked(Chainsaw) && GetCooldownRemainingTime(Chainsaw) >= 9;

    internal static bool Tools(ref uint actionID)
    {
        if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) && !InBossEncounter() ||
             IsEnabled(CustomComboPreset.MCH_ST_Adv_Excavator) && ReassembledExcavatorST &&
             (MCH_ST_Adv_Excavator_SubOption == 1 && !InBossEncounter() ||
              IsNotEnabled(CustomComboPreset.MCH_ST_Adv_TurretQueen))) &&
            LevelChecked(Excavator) && HasStatusEffect(Buffs.ExcavatorReady))
        {
            actionID = Excavator;

            return true;
        }

        if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) && InBossEncounter() ||
             IsEnabled(CustomComboPreset.MCH_ST_Adv_Excavator) && ReassembledExcavatorST &&
             IsEnabled(CustomComboPreset.MCH_ST_Adv_TurretQueen) &&
             (MCH_ST_Adv_Excavator_SubOption == 0 || InBossEncounter())) &&
            LevelChecked(Excavator) && HasStatusEffect(Buffs.ExcavatorReady) &&
            (BSUsed is 1 ||
             BSUsed % 3 is 2 && Battery <= 40 ||
             BSUsed % 3 is 0 && Battery <= 50 ||
             BSUsed % 3 is 1 && Battery <= 60 ||
             GetStatusEffectRemainingTime(Buffs.ExcavatorReady) <= 6))
        {
            actionID = Excavator;

            return true;
        }

        if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) ||
             IsEnabled(CustomComboPreset.MCH_ST_Adv_Chainsaw) && ReassembledChainsawST) &&
            !MaxBattery && !HasStatusEffect(Buffs.ExcavatorReady) && LevelChecked(Chainsaw) &&
            GetCooldownRemainingTime(Chainsaw) <= GCD / 2)
        {
            actionID = Chainsaw;

            return true;
        }

        if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) ||
             IsEnabled(CustomComboPreset.MCH_ST_Adv_AirAnchor) && ReassembledAnchorST) &&
            !MaxBattery && LevelChecked(AirAnchor) &&
            GetCooldownRemainingTime(AirAnchor) <= GCD / 2)
        {
            actionID = AirAnchor;

            return true;
        }

        if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) ||
             IsEnabled(CustomComboPreset.MCH_ST_Adv_Drill) && ReassembledDrillST) &&
            !JustUsed(Drill) &&
            ActionReady(Drill) && GetCooldownRemainingTime(Wildfire) is >= 20 or <= 10)
        {
            actionID = Drill;

            return true;
        }

        if ((IsEnabled(CustomComboPreset.MCH_ST_SimpleMode) ||
             IsEnabled(CustomComboPreset.MCH_ST_Adv_AirAnchor)) &&
            LevelChecked(HotShot) && !LevelChecked(AirAnchor) && !MaxBattery &&
            GetCooldownRemainingTime(HotShot) <= GCD / 2)
        {
            actionID = HotShot;

            return true;
        }

        return false;
    }

    #endregion

    #region Combos

    internal static float GCD => GetCooldown(OriginalHook(SplitShot)).CooldownTotal;

    internal static unsafe bool IsComboExpiring(float times)
    {
        float gcd = GCD * times;

        return ActionManager.Instance()->Combo.Timer != 0 && ActionManager.Instance()->Combo.Timer < gcd;
    }

  #endregion

    #region Openers

    internal static WrathOpener Opener()
    {
        if (Lvl90EarlyTools.LevelChecked)
            return Lvl90EarlyTools;

        if (StandardOpener.LevelChecked)
            return StandardOpener;

        return WrathOpener.Dummy;
    }

    internal static MCHStandardOpener StandardOpener = new();
    internal static MCHLvl90EarlyToolsOpener Lvl90EarlyTools = new();

    internal class MCHStandardOpener : WrathOpener
    {
        public override int MinOpenerLevel => 100;

        public override int MaxOpenerLevel => 109;
        public override List<uint> OpenerActions { get; set; } =
        [
            Reassemble,
            AirAnchor,
            CheckMate,
            DoubleCheck,
            Drill,
            BarrelStabilizer,
            Chainsaw,
            Excavator,
            AutomatonQueen,
            Reassemble,
            Drill,
            CheckMate,
            Wildfire,
            FullMetalField,
            DoubleCheck,
            Hypercharge,
            BlazingShot,
            CheckMate,
            BlazingShot,
            DoubleCheck,
            BlazingShot,
            CheckMate,
            BlazingShot,
            DoubleCheck,
            BlazingShot,
            CheckMate,
            Drill,
            DoubleCheck,
            CheckMate,
            HeatedSplitShot,
            DoubleCheck,
            HeatedSlugShot,
            HeatedCleanShot
        ];

        internal override UserData ContentCheckConfig => MCH_Balance_Content;

        public override List<(int[] Steps, Func<int> HoldDelay)> PrepullDelays { get; set; } =
        [
            ([2], () => 4)
        ];

        public override bool HasCooldowns() =>
            GetRemainingCharges(Reassemble) is 2 &&
            GetRemainingCharges(OriginalHook(GaussRound)) is 3 &&
            GetRemainingCharges(OriginalHook(Ricochet)) is 3 &&
            IsOffCooldown(Chainsaw) &&
            IsOffCooldown(Wildfire) &&
            IsOffCooldown(BarrelStabilizer) &&
            IsOffCooldown(Excavator) &&
            IsOffCooldown(FullMetalField);
    }

    internal class MCHLvl90EarlyToolsOpener : WrathOpener
    {
        public override int MinOpenerLevel => 90;

        public override int MaxOpenerLevel => 99;
        public override List<uint> OpenerActions { get; set; } =
        [
            Reassemble,
            AirAnchor,
            GaussRound,
            Ricochet,
            Drill,
            BarrelStabilizer,
            Chainsaw,
            GaussRound,
            Ricochet,
            HeatedSplitShot,
            GaussRound,
            Ricochet,
            HeatedSlugShot,
            Wildfire,
            HeatedCleanShot,
            AutomatonQueen,
            Hypercharge,
            BlazingShot,
            Ricochet,
            BlazingShot,
            GaussRound,
            BlazingShot,
            Ricochet,
            BlazingShot,
            GaussRound,
            BlazingShot,
            Reassemble,
            Drill
        ];

        internal override UserData ContentCheckConfig => MCH_Balance_Content;

        public override List<(int[] Steps, Func<int> HoldDelay)> PrepullDelays { get; set; } =
        [
            ([2], () => 4)
        ];

        public override List<int> DelayedWeaveSteps { get; set; } =
        [
            14
        ];

        public override List<int> AllowUpgradeSteps { get; set; } =
        [
            3, 4,
            8, 9,
            11, 12,
            19, 21, 23, 25
        ];

        public override bool HasCooldowns() =>
            GetRemainingCharges(Reassemble) is 2 &&
            GetRemainingCharges(OriginalHook(GaussRound)) is 3 &&
            GetRemainingCharges(OriginalHook(Ricochet)) is 3 &&
            IsOffCooldown(Chainsaw) &&
            IsOffCooldown(Wildfire) &&
            IsOffCooldown(BarrelStabilizer);
    }

    #endregion

    #region Gauge

    internal static MCHGauge Gauge = GetJobGauge<MCHGauge>();

    internal static bool IsOverheated => Gauge.IsOverheated;

    internal static bool RobotActive => Gauge.IsRobotActive;

    internal static byte LastSummonBattery => Gauge.LastSummonBatteryPower;

    internal static byte Heat => Gauge.Heat;

    internal static byte Battery => Gauge.Battery;

    internal static bool MaxBattery => Battery >= 100;

    #endregion

    #region 濺射誤拉防護（跳彈射擊／將死／雙將）

    /// <summary>
    ///     跳彈射擊(2890)、將死(36980)、雙將(36979) 在 Action 表裡都是
    ///     <c>CastType == 2</c>、<c>EffectRange == 5</c>——以「目標」為圓心的濺射，
    ///     不是單體。圈內只要站著一隻還沒進戰鬥的敵人就會被一起打到、直接拉進來。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     半徑一律從 <see cref="ActionWatching.ActionSheet" /> 的 EffectRange 讀，不寫死；
    ///     90 級以下的虹吸彈(2874) 是 <c>CastType == 1 / EffectRange == 0</c>，
    ///     會在第一個判斷就回 false（＝安全），所以低等級時「擋跳彈、退回虹吸炮」仍然成立。
    ///     </para>
    ///     <para>
    ///     🔴 92 級之後 <b>虹吸彈也會被換成雙將(36979)，而雙將同樣是 5y 濺射</b>，
    ///     所以這個閘門必須同時套在 Gauss 與 Ricochet 兩條線上，只擋跳彈是擋不住的。
    ///     </para>
    ///     <para>
    ///     🔴 只在當幀走訪 <c>Svc.Objects</c>，不保存任何 IGameObject／IBattleChara；
    ///     最貴的 <c>IsHostile()</c>（原生 nameplate 呼叫）排在距離與交戰狀態之後。
    ///     </para>
    /// </remarks>
    /// <param name="actionId">已經過 OriginalHook 解析的實際技能 ID。</param>
    /// <returns>true = 這一發會掃到未交戰的敵人，不該放。</returns>
    internal static unsafe bool SplashWouldPullIdleEnemy(uint actionId)
    {
        if (!MCH_GaussRico_NoIdlePull)
            return false;

        if (!ActionSheet.TryGetValue(actionId, out var sheet))
            return false;

        // CastType 1＝純單體；CanTargetSelf＝以自己為圓心（本家族沒有），兩者都不會誤拉目標周圍的怪
        if (sheet.CastType != 2 || sheet.EffectRange <= 0 || sheet.CanTargetSelf)
            return false;

        if (CurrentTarget is not IBattleChara centre)
            return false;

        float radius = sheet.EffectRange;
        int idle = 0;

        foreach (var o in Svc.Objects)
        {
            if (o is not IBattleChara chara || o.ObjectKind != ObjectKind.BattleNpc)
                continue;

            // 距離用 hitbox 修正，與 CustomComboFunctions.NumberOfObjectsInRange<Circle> 同一套算法
            float reach = radius + o.HitboxRadius;
            if ((o.Position - centre.Position).LengthSquared() > reach * reach)
                continue;

            if (chara.IsDead || !o.IsTargetable)
                continue;

            // 已經在戰鬥中的怪不算「誤拉」
            if (chara.Struct()->InCombat)
                continue;

            // 使用者自己標記要忽略的 NPC 不列入考慮
            if (Service.Configuration.IgnoredNPCs.ContainsKey(o.BaseId))
                continue;

            if (!o.IsHostile())
                continue;

            idle++;
        }

        if (idle == 0)
            return false;

        // 使用者跑 LogLevel 2，診斷一律 Information；10 秒最多一次，避免洗版
        if (EZ.Throttle($"MCH_IdleSplashBlocked_{actionId}", 10000))
            Svc.Log.Information(
                $"[MCH] {actionId.ActionName()} 被擋：以目標為圓心半徑 {radius}y 內有 {idle} 隻未交戰的敵人（避免誤拉）。");

        return true;
    }

    /// <summary> 這一發虹吸彈／雙將不會掃到未交戰的敵人。 </summary>
    internal static bool GaussSplashSafe => !SplashWouldPullIdleEnemy(OriginalHook(GaussRound));

    /// <summary> 這一發跳彈射擊／將死不會掃到未交戰的敵人。 </summary>
    internal static bool RicochetSplashSafe => !SplashWouldPullIdleEnemy(OriginalHook(Ricochet));

    #endregion

    #region ID's

    public const byte JobID = 31;

    public const uint
        CleanShot = 2873,
        HeatedCleanShot = 7413,
        SplitShot = 2866,
        HeatedSplitShot = 7411,
        SlugShot = 2868,
        HeatedSlugShot = 7412,
        GaussRound = 2874,
        Ricochet = 2890,
        Reassemble = 2876,
        Drill = 16498,
        HotShot = 2872,
        AirAnchor = 16500,
        Hypercharge = 17209,
        Heatblast = 7410,
        SpreadShot = 2870,
        Scattergun = 25786,
        AutoCrossbow = 16497,
        RookAutoturret = 2864,
        RookOverdrive = 7415,
        AutomatonQueen = 16501,
        QueenOverdrive = 16502,
        Tactician = 16889,
        Chainsaw = 25788,
        BioBlaster = 16499,
        BarrelStabilizer = 7414,
        Wildfire = 2878,
        Dismantle = 2887,
        Flamethrower = 7418,
        BlazingShot = 36978,
        DoubleCheck = 36979,
        CheckMate = 36980,
        Excavator = 36981,
        FullMetalField = 36982;

    public static class Buffs
    {
        public const ushort
            Reassembled = 851,
            Tactician = 1951,
            Wildfire = 1946,
            Overheated = 2688,
            Flamethrower = 1205,
            Hypercharged = 3864,
            ExcavatorReady = 3865,
            FullMetalMachinist = 3866;
    }

    public static class Debuffs
    {
        public const ushort
            Dismantled = 860,
            Wildfire = 861,
            Bioblaster = 1866;
    }

    public static class Traits
    {
        public const ushort
            EnhancedMultiWeapon = 605;
    }

    #endregion
}
