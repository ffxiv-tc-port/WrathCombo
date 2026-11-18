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
                WARPvP_PrimalScreamLBThreshold = new("WARPvP_PrimalScreamLBThreshold", 100),
                // 80＝掉了兩成 HP（相當於原初的血煙一個防護罩的量）才放；滿血時放，吸血全部溢補。
                WARPvP_BloodwhettingHPThreshold = new("WARPvP_BloodwhettingHPThreshold", 80);

            // 「也放」型開關：缺鍵讀到 false＝只對玩家放，舊設定檔不必遷移就生效。
            public static UserBool
                WARPvP_BloodwhettingNonPlayers = new("WARPvP_BloodwhettingNonPlayers"),
                WARPvP_OnslaughtNonPlayers = new("WARPvP_OnslaughtNonPlayers"),
                WARPvP_PrimalRendNonPlayers = new("WARPvP_PrimalRendNonPlayers");

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

                    case CustomComboPreset.WARPvP_BurstMode_Bloodwhetting:
                        UserConfig.DrawSliderInt(1, 100, WARPvP_BloodwhettingHPThreshold,
                            "自身 HP% 在這個值以下（受過傷）才放原初的血氣（100＝不限）");
                        UserConfig.DrawAdditionalBoolChoice(WARPvP_BloodwhettingNonPlayers, "目標不是玩家時也放",
                            "沒勾＝目標是 NPC、物件或沒有目標時不放（原初的血氣本身沒有傷害）。之後的混沌旋風不受影響。");
                        break;

                    case CustomComboPreset.WARPvP_BurstMode_Onslaught:
                        UserConfig.DrawAdditionalBoolChoice(WARPvP_OnslaughtNonPlayers, "目標不是玩家時也放",
                            "沒勾＝目標是 NPC、物件或沒有目標時不放猛攻（不會移動的目標不必衝過去追）。");
                        break;

                    case CustomComboPreset.WARPvP_BurstMode_PrimalRend:
                        UserConfig.DrawAdditionalBoolChoice(WARPvP_PrimalRendNonPlayers, "目標不是玩家時也放",
                            "沒勾＝目標是 NPC、物件或沒有目標時不放蠻荒崩裂（躍向目標＋眩暈，留給追擊玩家）。之後的盡毀不受影響。");
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

            // 原初的血氣沒有傷害（吸血＋防護罩），只對玩家、且受過傷才放；之後的混沌旋風是傷害技，不經這裡。
            private static bool BloodwhettingAllowed =>
                (Config.WARPvP_BloodwhettingNonPlayers || PvPCommon.TargetIsPlayer()) &&
                PlayerHealthPercentageHp() <= Config.WARPvP_BloodwhettingHPThreshold;

            // 猛攻、蠻荒崩裂是追擊用的位移（後者附眩暈）；NPC／物件不會移動，預設不對它們放。
            private static bool OnslaughtAllowed => Config.WARPvP_OnslaughtNonPlayers || PvPCommon.TargetIsPlayer();
            private static bool PrimalRendAllowed => Config.WARPvP_PrimalRendNonPlayers || PvPCommon.TargetIsPlayer();

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
                            if ((!GetCooldown(Bloodwhetting).IsCooldown || canWeave && IsOffCooldown(Bloodwhetting)) && BloodwhettingAllowed)
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
                            PrimalRendAllowed && PvPCommon.GapCloserInWindow(PrimalRend))
                            return PrimalRend;

                        if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_Onslaught) && PvPCommon.GapCloserRuleOn(Onslaught) && !GetCooldown(Onslaught).IsCooldown && canWeave &&
                            OnslaughtAllowed && PvPCommon.GapCloserInWindow(Onslaught))
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
                                if ((IsOffCooldown(PrimalRend) && PrimalRendAllowed || HasStatusEffect(Buffs.PrimalRuinationReady)) && !PvPCommon.GapCloserRuleOn(PrimalRend))
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
                            if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_Onslaught) && !GetCooldown(Onslaught).IsCooldown && canWeave && OnslaughtAllowed && !PvPCommon.GapCloserRuleOn(Onslaught))
                                return OriginalHook(Onslaught);

                            // Nascent Chaos and Orogeny conditions
                            if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_Bloodwhetting) && HasStatusEffect(Buffs.NascentChaos))
                                return OriginalHook(Bloodwhetting);

                            if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_Orogeny) && !GetCooldown(Orogeny).IsCooldown && canWeave)
                                return OriginalHook(Orogeny);

                            // PrimalRend if ready or BurstMode enabled
                            if (IsEnabled(CustomComboPreset.WARPvP_BurstMode_PrimalRend))
                            {
                                if ((IsOffCooldown(PrimalRend) && PrimalRendAllowed || HasStatusEffect(Buffs.PrimalRuinationReady)) && !PvPCommon.GapCloserRuleOn(PrimalRend))
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
