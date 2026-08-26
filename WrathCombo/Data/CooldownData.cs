using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using WrathCombo.Services.ActionRequestIPC;

namespace WrathCombo.Data
{
    internal class CooldownData
    {
        /// <summary>
        ///     其他外掛透過 <c>WrathCombo.ActionRequest.RequestBlacklist</c> 加上的人工冷卻（秒）。
        /// </summary>
        /// <remarks>
        ///     沒有任何外掛送過封鎖請求時，封鎖清單是空的，這裡固定回 <c>0</c>，
        ///     底下三個屬性的結果因此與加這段之前完全相同。
        /// </remarks>
        private float ArtificialCooldown =>
            ActionRequestIPCProvider.GetArtificialCooldown(ActionType.Action, ActionID);

        /// <summary> Gets a value indicating whether the action is on cooldown. </summary>
        public bool IsCooldown
        {
            get
            {
                return CooldownRemaining > 0;
            }
        }

        /// <summary> Gets the action ID on cooldown. </summary>
        public uint ActionID;

        /// <summary> Gets the elapsed cooldown time. </summary>
        public unsafe float CooldownElapsed => ActionManager.Instance()->GetRecastTimeElapsed(ActionType.Action, ActionID);

        /// <summary> Gets the total cooldown time. </summary>
        public unsafe float CooldownTotal => Math.Max(ActionManager.Instance()->GetRecastTime(ActionType.Action, ActionID), (BaseCooldownTotal) * MaxCharges);

        /// <summary> Includes Skill/Spell Speed modifiers along with any job trait modifiers </summary>
        public unsafe float BaseCooldownTotal => ActionManager.GetAdjustedRecastTime(ActionType.Action, ActionID) / 1000f;

        /// <summary> Gets the cooldown time remaining. </summary>
        public unsafe float CooldownRemaining =>
            Math.Max(ArtificialCooldown,
                CooldownElapsed == 0 ? 0 : Math.Max(0, CooldownTotal - CooldownElapsed));

        /// <summary> Gets the maximum number of charges for an action at the current level. </summary>
        /// <returns> Number of charges. </returns>
        public ushort MaxCharges => ActionManager.GetMaxCharges(ActionID, 0);

        /// <summary> Gets a value indicating whether the action has charges, not charges available. </summary>
        public bool HasCharges => MaxCharges > 1;

        /// <summary> Gets the remaining number of charges for an action. </summary>
        public unsafe uint RemainingCharges
        {
            get
            {
                if (MaxCharges == 1)
                    return CooldownRemaining == 0 ? 1 : 0u;

                return ActionManager.Instance()->GetCurrentCharges(ActionID);
            }
        }

        /// <summary> Gets the cooldown time remaining until the next charge. </summary>
        public float ChargeCooldownRemaining
        {
            get
            {
                // 取餘數會把人工冷卻抹掉，所以要再套一次 Max（與上游一致）。
                return Math.Max(ArtificialCooldown,
                    CooldownRemaining % (CooldownTotal / MaxCharges));
            }
        }
    }
}
