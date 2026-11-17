using WrathCombo.Core;
using WrathCombo.CustomComboNS;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Window.Functions;

namespace WrathCombo.Combos.PvP
{
    internal static class WHMPvP
    {
        #region IDS

        public const byte ClassID = 6;
        public const byte JobID = 24;

        internal class Role : PvPHealer;

        public const uint
            Glare = 29223,
            Cure2 = 29224,
            Cure3 = 29225,
            AfflatusMisery = 29226,
            Aquaveil = 29227,
            MiracleOfNature = 29228,
            SeraphStrike = 29229,
            AfflatusPurgation = 29230,
            Glare4 = 41499;

        internal class Buffs
        {
            internal const ushort
                Cure3Ready = 3083,
                SacredSight = 4326;
        }

        #endregion

        #region Config
        public static class Config
        {
            public static UserBool
               WHMPvP_Burst_HealsRetarget = new("WHMPvP_Burst_HealsRetarget");

            public static UserBoolArray
               WHMPvP_Burst_Heals_Options = new("WHMPvP_Burst_Heals_Options"),
               WHMPvP_Heals_Options = new("WHMPvP_Heals_Options");

            public static UserInt
               WHMPvP_Burst_HealsThreshold = new("WHMPvP_Burst_HealsThreshold", 50),
               WHMPvP_PurgationThreshold = new("WHMPvP_PurgationThreshold"),
               WHMPvP_DiabrosisThreshold = new("WHMPvP_DiabrosisThreshold");

            internal static void Draw(CustomComboPreset preset)
            {
                switch (preset)
                {                    
                    case CustomComboPreset.WHMPvP_Diabrosis:
                        UserConfig.DrawSliderInt(1, 100, WHMPvP_DiabrosisThreshold,
                            "Target HP% to use Diabrosis");

                        break;

                    case CustomComboPreset.WHMPvP_Burst_Heals:
                        UserConfig.DrawAdditionalBoolChoice(WHMPvP_Burst_HealsRetarget, "重新指定目標", "改對治療堆疊（插件設定裡的治療目標堆疊）放；沒勾＝對友方目標，沒有就對自己。");
                        UserConfig.DrawSliderInt(1, 100, WHMPvP_Burst_HealsThreshold, "對象 HP% 在這個值以下才放");
                        UserConfig.DrawHorizontalMultiChoice(WHMPvP_Burst_Heals_Options, "中療傷", "加入中療傷", 3, 0);
                        UserConfig.DrawHorizontalMultiChoice(WHMPvP_Burst_Heals_Options, "大療傷", "大療傷預備時加入大療傷", 3, 1);
                        UserConfig.DrawHorizontalMultiChoice(WHMPvP_Burst_Heals_Options, "水流幕", "在能力技空檔加入水流幕", 3, 2);
                        break;

                    case CustomComboPreset.WHMPvP_Heals:
                        UserConfig.DrawHorizontalMultiChoice(WHMPvP_Heals_Options, "重新指定中療傷", "中療傷改對治療堆疊（插件設定裡的治療目標堆疊）放", 3, 0);
                        UserConfig.DrawHorizontalMultiChoice(WHMPvP_Heals_Options, "重新指定大療傷", "大療傷改對治療堆疊放", 3, 1);
                        UserConfig.DrawHorizontalMultiChoice(WHMPvP_Heals_Options, "重新指定水流幕", "水流幕改對治療堆疊放", 3, 2);
                        break;

                    case CustomComboPreset.WHMPvP_AfflatusPurgation:
                        UserConfig.DrawSliderInt(1, 100, WHMPvP_PurgationThreshold,
                            "Target HP% to use Line Aoe Limit Break");

                        break;
                }
            }
        }

        #endregion       

    internal class WHMPvP_Burst : CustomCombo
        {
            protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.WHMPvP_Burst;

            protected override uint Invoke(uint actionID)
            {
                if (actionID is Glare)
                {
                    if (!PvPCommon.TargetImmuneToDamage())
                    {
                        //Limit break, with health slider
                        if (IsEnabled(CustomComboPreset.WHMPvP_AfflatusPurgation) && IsLB1Ready && GetTargetHPPercent() <= Config.WHMPvP_PurgationThreshold && PvPCommon.LongCDAllowed())
                            return AfflatusPurgation;

                        // Seraph Strike if enabled and off cooldown
                        if (IsEnabled(CustomComboPreset.WHMPvP_Seraph_Strike) && IsOffCooldown(SeraphStrike) && (!PvPCommon.GapCloserRuleOn(SeraphStrike) || PvPCommon.GapCloserInWindow(SeraphStrike)))
                            return SeraphStrike;

                        // Weave conditions
                        if (CanWeave())
                        {
                            //Role Action Diabrosis Role action
                            if (IsEnabled(CustomComboPreset.WHMPvP_Diabrosis) && PvPHealer.CanDiabrosis() && HasTarget() && PvPCommon.LongCDAllowed() &&
                            GetTargetHPPercent() <= Config.WHMPvP_DiabrosisThreshold)
                                return PvPHealer.Diabrosis;

                            // Miracle of Nature if enabled and off cooldown and inrange 
                            if (IsEnabled(CustomComboPreset.WHMPvP_Mirace_of_Nature) && IsOffCooldown(MiracleOfNature) && InActionRange(MiracleOfNature) && PvPCommon.CCAllowed())
                                return MiracleOfNature;
                        }

                        // Afflatus Misery if enabled and off cooldown
                        if (IsEnabled(CustomComboPreset.WHMPvP_Afflatus_Misery) && IsOffCooldown(AfflatusMisery))
                            return AfflatusMisery;
                    }
                    // Prevent waste cure 3 option
                    if (IsEnabled(CustomComboPreset.WHMPvP_NoWasteCure) && HasStatusEffect(Buffs.Cure3Ready) && GetStatusEffectRemainingTime(Buffs.Cure3Ready) < 6)
                        return Cure3;

                    // 閃颯預備中不插補血，先把閃颯打完。
                    if (IsEnabled(CustomComboPreset.WHMPvP_Burst_Heals) && !HasStatusEffect(Buffs.SacredSight))
                    {
                        bool retarget = Config.WHMPvP_Burst_HealsRetarget;
                        if (PvPCommon.HealCheckTarget(retarget) is { } healTarget && GetTargetHPPercent(healTarget) <= Config.WHMPvP_Burst_HealsThreshold)
                        {
                            if (Config.WHMPvP_Burst_Heals_Options[1] && HasStatusEffect(Buffs.Cure3Ready))
                                return retarget ? Cure3.Retarget(Glare, PvPCommon.HealStackResolver) : Cure3;

                            if (Config.WHMPvP_Burst_Heals_Options[2] && ActionReady(Aquaveil) && CanWeave())
                                return retarget ? Aquaveil.Retarget(Glare, PvPCommon.HealStackResolver) : Aquaveil;

                            if (Config.WHMPvP_Burst_Heals_Options[0] && ActionReady(Cure2))
                                return retarget ? Cure2.Retarget(Glare, PvPCommon.HealStackResolver) : Cure2;
                        }
                    }
                }

                return actionID;
            }
        }
      
       internal class WHMPvP_Aquaveil : CustomCombo
        {
            protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.WHMPvP_Heals;

            protected override uint Invoke(uint actionID)
            {
                if (actionID is not Cure2)
                    return actionID;

                var options = Config.WHMPvP_Heals_Options;

                if (IsEnabled(CustomComboPreset.WHMPvP_Cure3) && HasStatusEffect(Buffs.Cure3Ready))
                    return options[1] ? Cure3.Retarget(Cure2, PvPCommon.HealStackResolver) : Cure3;

                if (IsEnabled(CustomComboPreset.WHMPvP_Aquaveil) && IsOffCooldown(Aquaveil))
                    return options[2] ? Aquaveil.Retarget(Cure2, PvPCommon.HealStackResolver) : Aquaveil;

                // 大療傷預備時遊戲自己把中療傷換成大療傷，照實際會放的技能挑勾選。
                uint cure = OriginalHook(Cure2);
                bool retarget = cure == Cure3 ? options[1] : options[0];
                return retarget ? cure.Retarget(Cure2, PvPCommon.HealStackResolver) : actionID;
            }
        }

        internal class WHMPvP_Seraphstrike : CustomCombo
        {
            protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.WHMPvP_Seraphstrike;

            protected override uint Invoke(uint actionID) =>
                actionID is SeraphStrike && HasStatusEffect(Buffs.SacredSight) ? Glare4 : actionID;
        }
    }
}