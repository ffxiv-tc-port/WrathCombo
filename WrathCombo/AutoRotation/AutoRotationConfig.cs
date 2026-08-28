using System.Collections.Generic;

namespace WrathCombo.AutoRotation
{
    public class AutoRotationConfig
    {
        public bool Enabled;
        public bool InCombatOnly;
        public bool BypassQuest;
        public bool BypassFATE;
        public bool BypassBuffs;
        public int CombatDelay = 1;
        public bool EnableInInstance;
        public bool DisableAfterInstance;
        public DPSRotationMode DPSRotationMode;
        public HealerRotationMode HealerRotationMode;
        public HealerSettings HealerSettings = new();
        public DPSSettings DPSSettings = new();
        public int Throttler = 50;
        public bool OrbwalkerIntegration;
    }

    public class DPSSettings
    {
        public bool FATEPriority = false;
        public bool QuestPriority = false;
        public int? DPSAoETargets = 3;

        /// <summary>
        ///     逐職業覆寫「AoE 傷害功能所需目標數」。key ＝ ClassJob RowId
        ///     (已正規化成職業 id，見 <see cref="DPSSettingsIPCWrapper.NormalizeJobId" />)。
        ///     <para>
        ///     鍵不存在 ＝ 沿用全域的 <see cref="DPSAoETargets" />；
        ///     鍵存在但值為 <c>null</c> ＝ 該職業關閉 AoE 傷害功能(與全域欄位取消勾選同義)。
        ///     這兩種狀態必須分得出來，所以查詢一律看 <see cref="TryGetJobOverride" /> 的
        ///     bool 回傳值，不要用「值是不是 null」去猜。
        ///     </para>
        ///     <para>
        ///     既有使用者的設定檔沒有這個鍵，Newtonsoft 會保留欄位初始值(空字典)
        ///     ⇒ 沒有任何職業有覆寫 ⇒ 行為與加入這個功能之前完全相同。
        ///     </para>
        /// </summary>
        public Dictionary<uint, int?> DPSAoETargetsPerJob = new();

        public bool PreferNonCombat = false;
        public bool OnlyAttackInCombat = false;
        public bool AlwaysSelectTarget = true;
        public float MaxDistance = 25;
        public bool AoEIgnoreManual = false;

        /// <summary>
        ///     查某個職業有沒有自己的設定。每幀會被呼叫，刻意用字典查詢而不是 LINQ。
        /// </summary>
        /// <param name="jobId">
        ///     已正規化的職業 id(呼叫端請先過
        ///     <see cref="DPSSettingsIPCWrapper.NormalizeJobId" />)。
        /// </param>
        /// <param name="value">該職業的值；<c>null</c> 代表該職業關閉 AoE。</param>
        /// <returns><c>true</c> ＝ 這個職業有自己的設定。</returns>
        public bool TryGetJobOverride(uint jobId, out int? value)
        {
            if (jobId == 0)
            {
                value = null;
                return false;
            }

            return DPSAoETargetsPerJob.TryGetValue(jobId, out value);
        }
    }

    public class HealerSettings
    {
        public int SingleTargetHPP = 70;
        public int AoETargetHPP = 80;
        public int SingleTargetRegenHPP = 60;
        public int? AoEHealTargetCount = 2;
        public int HealDelay = 1;
        public bool ManageKardia = false;
        public bool KardiaTanksOnly = false;
        public bool AutoRez = false;
        public bool AutoRezRequireSwift = false;
        public bool AutoRezDPSJobs = false;
        public bool AutoRezOutOfParty = false;
        public bool AutoCleanse = false;
        public bool PreEmptiveHoT = false;
        public bool IncludeNPCs = false;

    }

    /// <summary>
    ///     「AoE 傷害功能所需目標數」這一格的生效值是從哪裡來的。純診斷用。
    /// </summary>
    public enum AoETargetsSource
    {
        /// <summary>被其他插件用 IPC 租約接管。</summary>
        Lease,

        /// <summary>目前職業有自己的覆寫值。</summary>
        Job,

        /// <summary>沿用全域設定。</summary>
        Global,
    }
}
