using WrathCombo.CustomComboNS;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Extensions;
using WrathCombo.Window.Functions;

namespace WrathCombo.Combos.PvP
{
    internal static class WARPvP
    {
        #region IDS

        public const byte ClassID = 3;
        public const byte JobID = 21;

        internal class Role : PvPTank;

        internal const uint
            HeavySwing = 29074,
            Maim = 29075,
            StormsPath = 29076,
            PrimalRend = 29084,
            Onslaught = 29079,
            Orogeny = 29080,
            Blota = 29081,
            Bloodwhetting = 29082,
            PrimalScream = 29083,
            PrimalWrath = 41433;


        internal class Buffs
        {
            internal const ushort
                InnerRelease = 1303,
                NascentChaos = 1992,
                InnerChaosReady = 4284,
                PrimalRuinationReady = 4285,
                Wrathfull = 4286;
        }
        #endregion

        #region Config
        public static class Config
        {
            public static UserInt
                WARPVP_BlotaTiming = new("WARPVP_BlotaTiming"),
                WARPvP_RampartThreshold = new("WARPvP_RampartThreshold"),
                WARPvP_PrimalScreamLBThreshold = new("WARPvP_PrimalScreamLBThreshold", 100);


            internal static void Draw(CustomComboPreset preset)
            {
                switch (preset)
                {
                    case CustomComboPreset.WARPvP_Rampart:
                        UserConfig.DrawSliderInt(1, 100, WARPvP_RampartThreshold,
                            "Use Rampart below set threshold for self");
                        break;

                    case CustomComboPreset.WARPvP_BurstMode_PrimalScreamLB:
                        UserConfig.DrawSliderInt(1, 100, WARPvP_PrimalScreamLBThreshold,
                            "目標 HP% 在這個值以下才放（100＝不限）");
                        break;

                    case CustomComboPreset.WARPvP_BurstMode_Blota:
                        UserConfig.DrawHorizontalRadioButton(WARPVP_BlotaTiming, $"Before {PrimalRend.ActionName()}", "", 0);
                        UserConfig.DrawHorizontalRadioButton(WARPVP_BlotaTiming, $"After {PrimalRend.ActionName()}", "", 1);

                        break;
                }
            }
        }
        #endregion
       
        internal class WARPvP_BurstMode : CustomCombo
        {
            protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.WARPvP_BurstMode;

            // 獻身把目標拉到身邊，只拉得動玩家（NPC、場地物件拉不動）；已變成狂魂時不是拉人，照舊。
            private static bool BlotaCanPull => OriginalHook(Blota) != Blota || PvPCommon.TargetIsPlayer();

            protected override uint Invoke(uint actionID)
            {
                if (actionID is HeavySwing or Maim or StormsPath)
                {
                    if (IsEnabled(CustomComboPreset.WARPvP_Rampart) && PvPTank.CanRampart(Config.WARPvP_RampartThreshold))
                        return PvPTank.Rampart;

                    if (!PvPCommon.TargetImmuneToDamage())
                    {
                        var canWeave = CanWeave();

                        // Bloodwhetting condition (both WARPvP BurstMode and CanWeave)
                        if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_Bloodwhetting))
                        {
                            if (!GetCooldown(Bloodwhetting).IsCooldown || canWeave && IsOffCooldown(Bloodwhetting))
                                return OriginalHook(Bloodwhetting);
                        }

                        // 原初的怒號（極限爆發）是錐形 12 米，目標在範圍內才放；按鈕已變成原初的怒震時不放。
                        if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_PrimalScreamLB) && IsLB1Ready && HasBattleTarget() &&
                            OriginalHook(PrimalScream) == PrimalScream && GetTargetDistance() <= 12 &&
                            GetTargetHPPercent() <= Config.WARPvP_PrimalScreamLBThreshold && PvPCommon.LongCDAllowed())
                            return PrimalScream;

                        // 暴怒是自身周圍 10 米的範圍攻擊。
                        if (IsEnabled(CustomComboPreset.WARPvP_Rampage) && canWeave && PvPTank.CanRampage() && HasBattleTarget() &&
                            GetTargetDistance() <= 10 && PvPCommon.LongCDAllowed())
                            return PvPTank.Rampage;

                        // Primal Wrath if in melee range and Wrathfull effect is active
                        if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_PrimalScream) && InMeleeRange() && canWeave && HasStatusEffect(Buffs.Wrathfull))
                            return OriginalHook(PrimalScream);

                        // 位移距離規則生效時，獻身／蠻荒崩裂／猛攻改在這裡依距離放，下面近戰內外的同一招不再放。
                        if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_Blota) && PvPCommon.GapCloserRuleOn(Blota) && IsOffCooldown(Blota) &&
                            !HasStatusEffect(PvPCommon.Debuffs.Stun, CurrentTarget, true) && BlotaCanPull &&
                            (Config.WARPVP_BlotaTiming == 0 && IsOffCooldown(PrimalRend) || Config.WARPVP_BlotaTiming == 1 && IsOnCooldown(PrimalRend)) &&
                            PvPCommon.GapCloserInWindow(Blota))
                            return Blota;

                        if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_PrimalRend) && PvPCommon.GapCloserRuleOn(PrimalRend) && IsOffCooldown(PrimalRend) &&
                            PvPCommon.GapCloserInWindow(PrimalRend))
                            return PrimalRend;

                        if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_Onslaught) && PvPCommon.GapCloserRuleOn(Onslaught) && !GetCooldown(Onslaught).IsCooldown && canWeave &&
                            PvPCommon.GapCloserInWindow(Onslaught))
                            return Onslaught;

                        // Blota and PrimalRend conditions based on range and cooldowns
                        if (!InMeleeRange())
                        {
                            // Blota with specific conditions and burst mode enabled
                            if (IsOffCooldown(Blota) && !HasStatusEffect(PvPCommon.Debuffs.Stun, CurrentTarget, true) && IsEnabled(CustomComboPreset.WARPvP_BurstMode_Blota) && BlotaCanPull && !PvPCommon.GapCloserRuleOn(Blota))
                            {
                                if (Config.WARPVP_BlotaTiming == 0 && IsOffCooldown(PrimalRend))
                                    return OriginalHook(Blota);
                                if (Config.WARPVP_BlotaTiming == 1 && IsOnCooldown(PrimalRend))
                                    return OriginalHook(Blota);
                            }

                            // PrimalRend if ready or BurstMode enabled
                            if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_PrimalRend))
                            {
                                if ((IsOffCooldown(PrimalRend) || HasStatusEffect(Buffs.PrimalRuinationReady)) && !PvPCommon.GapCloserRuleOn(PrimalRend))
                                    return OriginalHook(PrimalRend);
                            }

                        }

                        // In melee range logic
                        if (InMeleeRange())
                        {
                            // Inner Chaos effect logic
                            if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_InnerChaos) && HasStatusEffect(Buffs.InnerChaosReady))
                                return OriginalHook(Blota);

                            // Onslaught and Orogeny conditions for melee
                            if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_Onslaught) && !GetCooldown(Onslaught).IsCooldown && canWeave && !PvPCommon.GapCloserRuleOn(Onslaught))
                                return OriginalHook(Onslaught);

                            // Nascent Chaos and Orogeny conditions
                            if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_Bloodwhetting) && HasStatusEffect(Buffs.NascentChaos))
                                return OriginalHook(Bloodwhetting);

                            if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_Orogeny) && !GetCooldown(Orogeny).IsCooldown && canWeave)
                                return OriginalHook(Orogeny);

                            // PrimalRend if ready or BurstMode enabled
                            if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_PrimalRend))
                            {
                                if ((IsOffCooldown(PrimalRend) || HasStatusEffect(Buffs.PrimalRuinationReady)) && !PvPCommon.GapCloserRuleOn(PrimalRend))
                                    return OriginalHook(PrimalRend);
                            }

                            // Blota with specific conditions and burst mode enabled in meleerange
                            if (IsOffCooldown(Blota) && !HasStatusEffect(PvPCommon.Debuffs.Stun, CurrentTarget, true) && IsEnabled(CustomComboPreset.WARPvP_BurstMode_Blota) && BlotaCanPull && !PvPCommon.GapCloserRuleOn(Blota))
                            {
                                if (Config.WARPVP_BlotaTiming == 0 && IsOffCooldown(PrimalRend))
                                    return OriginalHook(Blota);
                                if (Config.WARPVP_BlotaTiming == 1 && IsOnCooldown(PrimalRend))
                                    return OriginalHook(Blota);
                            }
                        }
                    }
                }
                return actionID;
            }
        }
    }
}
