using WrathCombo.Core;
using WrathCombo.CustomComboNS;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Window.Functions;

namespace WrathCombo.Combos.PvP
{
    internal static class SCHPvP
    {
        #region IDS

        public const byte ClassID = 26;
        public const byte JobID = 28;       

        internal class Role : PvPHealer;

        public const uint
            Broil = 29231,
            Adloquilum = 29232,
            Biolysis = 29233,
            DeploymentTactics = 29234,
            Expedient = 29236,
            ChainStratagem = 29716;


        internal class Buffs
        {
            internal const ushort
                Catalyze = 3088,
                Recitation = 3094;
        }
        internal class Debuffs
        {
            internal const ushort
                Biolysis = 3089,
                Biolytic = 3090;
        }
        #endregion

        #region Config
        public static class Config
        {
            public static UserBool
               SCHPvP_Adlo_Retarget = new("SCHPvP_Adlo_Retarget");

            public static UserInt
               SCHPvP_DiabrosisThreshold = new("SCHPvP_DiabrosisThreshold"),
               SCHPvP_AdloThreshold = new("SCHPvP_AdloThreshold"),
               SCHPvP_AllyAdloThreshold = new("SCHPvP_AllyAdloThreshold", 50);

            internal static void Draw(CustomComboPreset preset)
            {
                switch (preset)
                {
                    case CustomComboPreset.SCHPvP_Diabrosis:
                        UserConfig.DrawSliderInt(0, 100, SCHPvP_DiabrosisThreshold,
                            "Target HP% to use Diabrosis");

                        break;

                    case CustomComboPreset.SCHPvP_Selfcare:
                        UserConfig.DrawSliderInt(1, 100, SCHPvP_AdloThreshold,
                            "為自身使用鼓舞激勵之策的生命值百分比閾值");

                        break;

                    case CustomComboPreset.SCHPvP_AllyAdlo:
                        UserConfig.DrawAdditionalBoolChoice(SCHPvP_Adlo_Retarget, "重新指定目標", "改對治療堆疊（插件設定裡的治療目標堆疊）放鼓舞激勵之策；沒勾＝對友方目標，沒有就對自己。");
                        UserConfig.DrawSliderInt(1, 100, SCHPvP_AllyAdloThreshold, "對象 HP% 在這個值以下才放鼓舞激勵之策");
                        break;
                }
            }
        }

        #endregion
          
       internal class SCHPvP_Burst : CustomCombo
        {
            protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.SCHPvP_Burst;

            protected override uint Invoke(uint actionID)
            {
                if (actionID is Broil && InCombat())
                {
                    // Uses Chain Stratagem when available
                    if (IsEnabled(CustomComboPreset.SCHPvP_ChainStratagem) && IsOffCooldown(ChainStratagem))
                        return ChainStratagem;                    

                    // Uses Expedient when available and target isn't affected with Biolysis
                    if (IsEnabled(CustomComboPreset.SCHPvP_Expedient) && IsOffCooldown(Expedient) && GetCooldownRemainingTime(Biolysis) < 3)
                        return Expedient;

                    // Uses Biolysis on cooldown or with Recitation when Expedient is enabled with safetey for too long of an expedient cooldown. 
                    if (IsEnabled(CustomComboPreset.SCHPvP_Biolysis) && IsOffCooldown(Biolysis))
                    {
                        if (IsEnabled(CustomComboPreset.SCHPvP_Expedient))
                        {
                            if (HasStatusEffect(Buffs.Recitation) || GetCooldownRemainingTime(Expedient) > 5)
                                return Biolysis;
                        } 
                        return Biolysis;
                    }

                    //Uses Diabrosis when below set health
                    if (IsEnabled(CustomComboPreset.SCHPvP_Diabrosis) && PvPHealer.CanDiabrosis() && HasTarget() && PvPCommon.LongCDAllowed() && 
                            GetTargetHPPercent() <= Config.SCHPvP_DiabrosisThreshold)
                        return PvPHealer.Diabrosis;

                    // Uses Deployment Tactics when available
                    if (IsEnabled(CustomComboPreset.SCHPvP_DeploymentTactics) && GetRemainingCharges(DeploymentTactics) > 1 && HasStatusEffect(Debuffs.Biolysis, CurrentTarget))
                        return DeploymentTactics;

                    // Adds Adloquium when at or below threshold, will not Overwrite the 10% damage reduction buff to prevent waste
                    if (IsEnabled(CustomComboPreset.SCHPvP_Selfcare) && !HasStatusEffect(Buffs.Catalyze) && PlayerHealthPercentageHp() <= Config.SCHPvP_AdloThreshold)
                        return Adloquilum;

                    if (IsEnabled(CustomComboPreset.SCHPvP_AllyAdlo))
                    {
                        bool retarget = Config.SCHPvP_Adlo_Retarget;
                        if (PvPCommon.HealCheckTarget(retarget) is { } healTarget && !HasStatusEffect(Buffs.Catalyze, healTarget) &&
                            GetTargetHPPercent(healTarget) <= Config.SCHPvP_AllyAdloThreshold)
                            return retarget ? Adloquilum.Retarget(Broil, PvPCommon.HealStackResolver) : Adloquilum;
                    }
                }

                return actionID;
            }
        }

        internal class SCHPvP_RetargetAdlo : CustomCombo
        {
            protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.SCHPvP_RetargetAdlo;

            protected override uint Invoke(uint actionID) =>
                actionID is Adloquilum ? Adloquilum.Retarget(PvPCommon.HealStackResolver) : actionID;
        }
    }
}