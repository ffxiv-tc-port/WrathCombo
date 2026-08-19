using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Memory;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;
using System.Linq;
using WrathCombo.Combos.PvE;
using WrathCombo.Data;
using GameMain = FFXIVClientStructs.FFXIV.Client.Game.GameMain;

namespace WrathCombo.CustomComboNS.Functions
{
    internal abstract partial class CustomComboFunctions
    {
        /// <summary> Gets the player or null. </summary>
        public static IPlayerCharacter? LocalPlayer => Svc.Objects.LocalPlayer;

        /// <summary> Find if the player has a certain condition. </summary>
        /// <param name="flag"> Condition flag. </param>
        /// <returns> A value indicating whether the player is in the condition. </returns>
        public static bool HasCondition(ConditionFlag flag) => Svc.Condition[flag];

        /// <summary> Find if the player is in combat. </summary>
        /// <returns> A value indicating whether the player is in combat. </returns>
        public static bool InCombat() => Svc.Condition[ConditionFlag.InCombat];

        /// <summary> Find if the player is bound by duty. </summary>
        /// <returns> A value indicating whether the player is bound by duty. </returns>
        public static unsafe bool InDuty() => GameMain.Instance()->CurrentContentFinderConditionId > 0;

        /// <summary> Find if the player has a pet present. </summary>
        /// <returns> A value indicating whether the player has a pet (fairy/carbuncle) present. </returns>
        public static bool HasPetPresent() => Svc.Buddies.PetBuddy != null;

        /// <summary> Find if the player has a companion (chocobo) present. </summary>
        /// <returns> A value indicating whether the player has a companion (chocobo). </returns>
        public static bool HasCompanionPresent() => Svc.Buddies.CompanionBuddy?.GameObject != null;

        /// <summary> Checks if the player is in a PVP enabled zone. </summary>
        /// <returns> A value indicating whether the player is in a PVP enabled zone. </returns>
        public static bool InPvP() => GameMain.IsInPvPArea() || GameMain.IsInPvPInstance();

        /// <summary> Checks if the player has completed the required job quest for the ability. </summary>
        /// <returns> A value indicating a quest has been completed for a job action.</returns>
        public static unsafe bool IsActionUnlocked(uint id)
        {
            var unlockLink = ActionWatching.ActionSheet[id].UnlockLink.RowId;
            return unlockLink == 0 || UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(unlockLink);
        }

        public static unsafe bool InFATE()
        {
            // FateManager.Instance() 在 CS 裡是 [StaticAddress(..., isPointer: true)] —— 讀的是「指標的位址」,
            // 遊戲還沒把它配起來(登入前、換區中)時那個槽就是 0,回來的是貨真價實的 null。
            // 解參考就是攔不到的 AVE,而這支在自動輪替的判定路徑上(AutoRotationController 的 CombatBypass
            // 與 FATE 目標優先)會被反覆呼叫。讀不到回 false —— 對「還沒進場」來說「不在 FATE 裡」就是正確答案。
            var fateManager = FateManager.Instance();
            if (fateManager == null)
                return false;
            var currentFate = fateManager->CurrentFate;
            return currentFate is not null && LocalPlayer.Level <= currentFate->MaxLevel;
        }

        public static bool PlayerHasTankStance()
        {
            return LocalPlayer.ClassJob.RowId switch
            {
                PLD.JobID or PLD.ClassID => HasStatusEffect(PLD.Buffs.IronWill),
                WAR.JobID or WAR.ClassID => HasStatusEffect(WAR.Buffs.Defiance),
                DRK.JobID => HasStatusEffect(DRK.Buffs.Grit),
                GNB.JobID => HasStatusEffect(GNB.Buffs.RoyalGuard),
                BLU.JobID => HasStatusEffect(BLU.Buffs.TankMimicry),
                _ => false
            };
        }

        public static unsafe bool InBossEncounter()
        {
            if (NearbyBosses.Count() == 0)
                return false;

            foreach (var boss in NearbyBosses)
            {
                if (boss.Struct()->InCombat && boss.GetNameplateKind() == NameplateKind.HostileEngagedSelfDamaged)
                    return true;
            }

            return false;
        }

        public static unsafe AllianceGroup GetAllianceGroup()
        {
            if (GroupManager.Instance()->MainGroup.IsAlliance)
            {
                // UIModule.Instance() 是手寫包裝(UIModule 未建立時合法回 null),整條四跳鏈逐節判空;
                // 取不到=回 NotInAlliance(視為不在聯盟團),不解參考。StringArrays[3] 的槽位元素也可為 null。
                var uiModule = UIModule.Instance();
                if (uiModule == null) return AllianceGroup.NotInAlliance;
                var atkModule = uiModule->GetRaptureAtkModule();
                if (atkModule == null) return AllianceGroup.NotInAlliance;
                var stringArrayData = atkModule->AtkModule.AtkArrayDataHolder.StringArrays[3];
                if (stringArrayData == null || stringArrayData->StringArray == null) return AllianceGroup.NotInAlliance;
                var array = stringArrayData->StringArray[4];
                if (array == null) return AllianceGroup.NotInAlliance;
                var str = MemoryHelper.ReadSeStringNullTerminated(new System.IntPtr(array));
                if (str.TextValue.Length == 0) return AllianceGroup.NotInAlliance;
                var lastChar = str.TextValue.Last();

                return lastChar switch
                {
                    'A' => AllianceGroup.GroupA,
                    'B' => AllianceGroup.GroupB,
                    'C' => AllianceGroup.GroupC,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
            return AllianceGroup.NotInAlliance;
        }
    }
}
