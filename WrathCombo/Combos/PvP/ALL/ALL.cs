using System;
using Dalamud.Game.ClientState.Objects.Types;
using System.Linq;
using WrathCombo.Combos.PvE;
using WrathCombo.CustomComboNS;
using WrathCombo.CustomComboNS.Functions;
using static WrathCombo.CustomComboNS.Functions.CustomComboFunctions;
using static WrathCombo.Window.Functions.UserConfig;
using System.Collections.Frozen;
using System.Text;
using Dalamud.Bindings.ImGui;
using WrathCombo.Extensions;

namespace WrathCombo.Combos.PvP;

internal static class PvPCommon
{
    // MP 消耗與恢復力照台服 Action／ActionTransient 表；台服 7.3 起自癒、淨化都是 2000。
    const uint RecuperateCost = 2000; // Recuperate MP Cost
    const int PurifyCost = 2000; // Purify MP Cost
    const uint RecuperateAmount = 15000; // Recuperate Base Heal

    public const uint
        Teleport = 5,
        Return = 6,
        StandardElixir = 29055,
        Recuperate = 29711,
        Purify = 29056,
        Guard = 29054,
        Sprint = 29057,
        PvPRoleAction = 43259;

    internal class Buffs
    {
        public const ushort
            RidingMecha = 1420, // Rival Wings
            Soaring = 1465,     // Rival Wings
            FlyingHigh = 1730,  // Rival Wings
            Sprint = 1342,
            Guard = 3054,
            Resilience = 3248,
            WeakenedGuard = 3673;
    }

    internal class Debuffs
    {
        public const ushort
            Silence = 1347,
            Bind = 1345,
            Stun = 1343,
            HalfAsleep = 3022,  // Unused
            Sleep = 1348,       // Unused
            DeepFreeze = 3219,
            Heavy = 1344,
            Unguarded = 3021,
            MiracleOfNature = 3085;
    }

    internal class Config
    {
        public static UserInt
            EmergencyHealThreshold = new("EmergencyHealThreshold"),
            EmergencyGuardThreshold = new("EmergencyGuardThreshold"),
            PurifyMPThreshold = new("PurifyMPThreshold"),
            GapCloserMinDistance = new("PvP_GapCloserMinDistance", 10);

        public static UserBoolArray
            QuickPurifyStatuses = new("QuickPurifyStatuses");

        internal static void Draw(CustomComboPreset preset)
        {
            switch (preset)
            {
                case CustomComboPreset.PvP_EmergencyHeals:
                    string baseMessage = $"Uses Recuperate when at or under the threshold.\n" +
                         $"Calculated from (MaxHP - {RecuperateAmount:N0}) to prevent overhealing.";

                    if (LocalPlayer is { } player && player.MaxHp > RecuperateAmount)
                    {
                        var adjustedMaxHP = player.MaxHp - RecuperateAmount;
                        var thresholdHP = adjustedMaxHP / 100f * EmergencyHealThreshold;

                        DrawSliderInt(1, 100, EmergencyHealThreshold, $"{baseMessage}\nThreshold: {thresholdHP:N0} HP");
                    }
                    else
                    {
                        DrawSliderInt(1, 100, EmergencyHealThreshold, baseMessage);
                    }
                    break;

                case CustomComboPreset.PvP_EmergencyGuard:
                    DrawSliderInt(1, 100, EmergencyGuardThreshold, "Uses Guard when at or under:");
                    break;

                case CustomComboPreset.PvP_QuickPurify:
                    DrawSliderInt(PurifyCost, 10000, PurifyMPThreshold, "Do not use Purify below set MP", sliderIncrement: 100);
                    DrawPvPStatusMultiChoice(QuickPurifyStatuses);
                    break;

                case CustomComboPreset.PvP_CCOnlyOnPlayers:
                    DrawAppliesTo(ref _ccTooltip, CCActions,
                        "以控場為主、附帶傷害很低或沒有傷害的技能。傷害為主、順帶控場的技能（例如山崩、空氣錨、發炎III）不受影響。\n" +
                        "戰士的獻身原本就只在目標是玩家或狂魂預備時放，不受這個開關影響。");
                    break;

                case CustomComboPreset.PvP_LongCDOnlyOnPlayers:
                    DrawAppliesTo(ref _longCdTooltip, LongCDActions,
                        "冷卻時間 30 秒以上的攻擊技能（含補師職能技能潰破）與極限爆發。\n" +
                        "防禦、治療類技能，以及極限爆發之後的追擊（英雄的返場餘音、掠影的蔑視等）不受影響；" +
                        "蛇鱗術、本輪等單一按鍵功能裡的用法也不受影響。");
                    break;

                case CustomComboPreset.PvP_GapCloserMinDistance:
                    DrawSliderInt(0, 25, GapCloserMinDistance, "目標距離至少幾米才放位移技能（扣掉雙方碰撞圈；PvP 近戰射程是 5 米）");
                    DrawAppliesTo(ref _gapCloserTooltip, GapCloserActions,
                        "衝到目標面前或把目標拉過來的技能；距離量法同連擊判斷近戰射程（水平距離扣掉雙方碰撞圈）。\n" +
                        "技能已變成追擊（盡毀、狂魂、深紅強襲等）時不算位移，照常放。\n" +
                        "不算：往前或往後的自身位移（本輪、地獄入境、速塗、前衝步、後躍射擊、迴避跳躍、移轉、縮地）、" +
                        "對隊友的位移、腐穢大地（主要是自身恢復／減傷場地）、極限爆發（隕石衝擊）。");
                    break;
            }
        }

        private static string? _ccTooltip, _longCdTooltip, _gapCloserTooltip;

        // 清單第一次畫的時候才組（技能名讀台服 Action 表），之後沿用同一個字串。
        private static void DrawAppliesTo(ref string? cache, (string Job, uint Action)[] actions, string note)
        {
            ImGui.TextDisabled("適用技能 (?)");
            if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                return;

            if (cache is null)
            {
                var sb = new StringBuilder(note).Append("\n");
                foreach (var (job, action) in actions)
                    sb.Append("\n・").Append(job).Append("：").Append(GetActionName(action));
                cache = sb.ToString();
            }

            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1f);
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 32f);
            ImGui.TextUnformatted(cache);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
            ImGui.PopStyleVar();
        }
    }

    /// <summary> 目前目標（或指定目標）是玩家角色；沒有目標＝不是。 </summary>
    public static bool TargetIsPlayer(IGameObject? optionalTarget = null) =>
        (optionalTarget ?? CurrentTarget).IsAPlayer();

    /// <summary> 控場技能這一刻可以放：沒開「目標不是玩家時不放控場技能」，或目標是玩家。 </summary>
    public static bool CCAllowed() =>
        !IsEnabled(CustomComboPreset.PvP_CCOnlyOnPlayers) || TargetIsPlayer();

    /// <summary> 長冷卻技能（冷卻 30 秒以上的攻擊技能與極限爆發）這一刻可以放；條件同 <see cref="CCAllowed" />。 </summary>
    public static bool LongCDAllowed() =>
        !IsEnabled(CustomComboPreset.PvP_LongCDOnlyOnPlayers) || TargetIsPlayer();

    /// <summary>
    ///     位移技能 <paramref name="gapCloser" /> 這一刻可以放：沒開「目標太近時不放位移技能」、
    ///     它現在已變成別的技能（追擊，不是位移），或目標距離（<see cref="GetTargetDistance" />）達到設定值。
    ///     開關沒勾時第一項就成立，不讀遊戲狀態。
    /// </summary>
    public static bool GapCloserAllowed(uint gapCloser) =>
        !IsEnabled(CustomComboPreset.PvP_GapCloserMinDistance) ||
        OriginalHook(gapCloser) != gapCloser ||
        GetTargetDistance() >= Config.GapCloserMinDistance;

    // 只給設定頁的說明用；實際判斷寫在各職業檔的出招條件裡（加 CCAllowed／LongCDAllowed 的地方）。
    // 分類依據台服 Action 表的冷卻時間（Recast100ms）與 ActionTransient 的技能說明。
    internal static readonly (string Job, uint Action)[]
        CCActions =
        [
            ("吟遊詩人", BRDPvP.SilentNocturne),
            ("黑魔道士", BLMPvP.Lethargy),
            ("武士", SAMPvP.Mineuchi),
            ("白魔道士", WHMPvP.MiracleOfNature),
            ("武僧", MNKPvP.Meteordrive),
        ],
        LongCDActions =
        [
            ("占星術師", ASTPvP.Macrocosmos),
            ("機工士", MCHPvP.MarksmanSpite),
            ("機工士", MCHPvP.FullMetalField),
            ("武僧", MNKPvP.Meteordrive),
            ("忍者", NINPvP.SeitonTenchu),
            ("忍者", NINPvP.Bunshin),
            ("奪魂者", RPRPvP.PlentifulHarvest),
            ("武士", SAMPvP.Zantetsuken),
            ("賢者", SGEPvP.Pneuma),
            ("毒蛇劍士", VPRPvP.RattlingCoil),
            ("白魔道士", WHMPvP.AfflatusPurgation),
            ("占星術師、學者、賢者、白魔道士", PvPHealer.Diabrosis),
            ("戰士", WARPvP.PrimalScream),
            ("戰士", PvPTank.Rampage),
        ],
        // 只列連擊裡真的會放的；實際判斷是各職業檔加 GapCloserAllowed 的地方。
        GapCloserActions =
        [
            ("騎士", PLDPvP.Intervene),
            ("戰士", WARPvP.Onslaught),
            ("戰士", WARPvP.PrimalRend),
            ("戰士", WARPvP.Blota),
            ("暗黑騎士", DRKPvP.Plunge),
            ("絕槍戰士", GNBPvP.RoughDivide),
            ("白魔道士", WHMPvP.SeraphStrike),
            ("武僧", MNKPvP.Thunderclap),
            ("龍騎士", DRGPvP.HighJump),
            ("武士", SAMPvP.Soten),
            ("召喚士", SMNPvP.CrimsonCyclone),
            ("赤魔道士", RDMPvP.CorpsACorps),
            ("毒蛇劍士", VPRPvP.Slither),
        ];

    /// <summary> Checks if the target is immune to damage. Optionally, include buffs that provide significant damage reduction. </summary>
    /// <param name="includeReductions"> Includes buffs that provide significant damage reduction. </param>
    /// <param name="optionalTarget"> Optional target to check. </param>
    public static bool TargetImmuneToDamage(bool includeReductions = true, IGameObject? optionalTarget = null)
    {
        var t = optionalTarget ?? CurrentTarget;
        if (t is null || !InPvP()) return false;

        bool targetHasReductions = HasStatusEffect(Buffs.Guard, t, true) || HasStatusEffect(Buffs.WeakenedGuard, t, true) || HasStatusEffect(VPRPvP.Buffs.HardenedScales, t, true);
        bool targetHasImmunities = HasStatusEffect(DRKPvP.Buffs.UndeadRedemption, t, true) || HasStatusEffect(PLDPvP.Buffs.HallowedGround, t, true);

        return includeReductions
            ? targetHasReductions || targetHasImmunities
            : targetHasImmunities;
    }

    // Lists of Excluded Actions 
    internal static readonly FrozenSet<uint>
        CommonActions = [Teleport, Return, Guard, Recuperate, Purify, StandardElixir, Sprint, PvPRoleAction],
        MovementActions = [
            PLDPvP.Intervene, PLDPvP.Guardian,
            WARPvP.Onslaught, WARPvP.PrimalRend,
            DRKPvP.Plunge,
            GNBPvP.RoughDivide,
            WHMPvP.SeraphStrike,
            ASTPvP.Epicycle, ASTPvP.Retrograde,
            SGEPvP.Icarus,
            MNKPvP.Thunderclap,
            DRGPvP.ElusiveJump, DRGPvP.HighJump,
            NINPvP.Shukuchi, NINPvP.ForkedRaiju, NINPvP.FleetingRaiju,
            RPRPvP.HellsIngress, RPRPvP.Regress,
            VPRPvP.Slither,
            SAMPvP.Soten,
            DNCPvP.EnAvant,
            BRDPvP.RepellingShot,
            PCTPvP.Smudge,
            SMNPvP.CrimsonCyclone,
            BLMPvP.AetherialManipulation,
            RDMPvP.CorpsACorps, RDMPvP.Displacement];

    internal class GlobalEmergencyHeals : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.PvP_EmergencyHeals;

        protected override uint Invoke(uint actionID)
        {
            if ((HasStatusEffect(Buffs.Guard) || JustUsed(Guard)) && IsEnabled(CustomComboPreset.PvP_MashCancel))
            {
                if (actionID is Guard)
                    return Guard;

                return All.SavageBlade;
            }

            if (Execute() && InPvP() &&
                !CommonActions.Contains(actionID) &&
                !MovementActions.Contains(actionID))
                return OriginalHook(Recuperate);

            return actionID;
        }

        public static bool Execute()
        {
            if (LocalPlayer is not { } player || player.IsDead || player.CurrentMp < RecuperateCost) return false;

            // Special States
            if (HasStatusEffect(DRGPvP.Buffs.SkyHigh) ||
                HasStatusEffect(VPRPvP.Buffs.HardenedScales) ||
                HasStatusEffect(DRKPvP.Buffs.UndeadRedemption) ||
                HasStatusEffect(Buffs.RidingMecha, anyOwner: true))
                return false;

            var adjustedCurrentHP = player.CurrentHp * 100;
            var adjustedMaxHP = player.MaxHp - RecuperateAmount;

            return adjustedCurrentHP <= Config.EmergencyHealThreshold * adjustedMaxHP;
        }
    }

    internal class GlobalEmergencyGuard : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.PvP_EmergencyGuard;

        protected override uint Invoke(uint actionID)
        {
            if ((HasStatusEffect(Buffs.Guard) || JustUsed(Guard)) && IsEnabled(CustomComboPreset.PvP_MashCancel))
            {
                if (actionID is Guard)
                {
                    var player = LocalPlayer;

                    if (IsEnabled(CustomComboPreset.PvP_MashCancelRecup) && !JustUsed(Guard, 2f) &&
                        player.CurrentMp >= RecuperateCost && player.CurrentHp <= player.MaxHp - RecuperateAmount) 
                        return Recuperate;

                    return Guard;
                }

                return All.SavageBlade;
            }

            if (Execute() && InPvP() &&
                !CommonActions.Contains(actionID) &&
                !MovementActions.Contains(actionID))
                return OriginalHook(Guard);

            return actionID;
        }

        public static bool Execute()
        {
            if (LocalPlayer is not { } player || player.IsDead || IsOnCooldown(Guard)) return false;

            // Special States
            if (HasStatusEffect(DRGPvP.Buffs.SkyHigh) ||
                HasStatusEffect(WARPvP.Buffs.InnerRelease) ||
                HasStatusEffect(VPRPvP.Buffs.HardenedScales) ||
                HasStatusEffect(PLDPvP.Buffs.HallowedGround) ||
                HasStatusEffect(DRKPvP.Buffs.UndeadRedemption) ||
                HasStatusEffect(Debuffs.Unguarded, anyOwner: true) ||
                HasStatusEffect(Buffs.RidingMecha, anyOwner: true))
                return false;

            var adjustedCurrentHP = player.CurrentHp * 100;
            var adjustedThreshold = Config.EmergencyGuardThreshold * player.MaxHp;

            return adjustedCurrentHP <= adjustedThreshold;
        }
    }

    internal class QuickPurify : CustomCombo
    {
        protected internal override CustomComboPreset Preset { get; } = CustomComboPreset.PvP_QuickPurify;

        public static (ushort debuff, string label)[] Statuses =
        [
            (Debuffs.Stun, "Stun"),
            (Debuffs.DeepFreeze, "Deep Freeze"),
            (Debuffs.HalfAsleep, "Half Asleep"), // todo: remove, reset cfg
            (Debuffs.Sleep, "Sleep"), // todo: remove, reset cfg
            (Debuffs.Bind, "Bind"),
            (Debuffs.Heavy, "Heavy"),
            (Debuffs.Silence, "Silence"),
            (Debuffs.MiracleOfNature, "Miracle of Nature"),
        ];

        protected override uint Invoke(uint actionID)
        {
            if ((HasStatusEffect(Buffs.Guard) || JustUsed(Guard)) && IsEnabled(CustomComboPreset.PvP_MashCancel))
            {
                if (actionID is Guard)
                    return Guard;

                return All.SavageBlade;
            }

            if (Execute() && InPvP() &&
                !CommonActions.Contains(actionID))
                return OriginalHook(Purify);

            return actionID;
        }

        public static bool Execute()
        {
            if (LocalPlayer is not { } player || player.IsDead || IsOnCooldown(Purify)) return false;

            // MP 不足時遊戲不讓放，回傳淨化只會讓按鈕卡住；門檻設得比消耗低時以消耗為準。
            if (player.CurrentMp < Math.Max(PurifyCost, Config.PurifyMPThreshold)) return false;

            bool[] selectedStatuses = Config.QuickPurifyStatuses;

            // Bail if nothing is enabled
            if (selectedStatuses.Length == 0) return false;

            // Make sure new statuses are supported
            Array.Resize(ref selectedStatuses, Statuses.Length);

            // Don't purify if under some buffs
            if (HasStatusEffect(DRGPvP.Buffs.SkyHigh) ||
                HasStatusEffect(VPRPvP.Buffs.HardenedScales) ||
                HasStatusEffect(Buffs.RidingMecha, anyOwner: true))
                return false;

            // Check if the status is present and one the user wants purified
            return selectedStatuses.Where((t, i) => t && HasStatusEffect(Statuses[i].debuff, anyOwner: true)).Any();
        }
    }
}