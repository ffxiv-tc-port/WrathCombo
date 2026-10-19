using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using System;
using System.Collections.Concurrent;

namespace WrathCombo.Data
{
    /// <summary> Cached conditional combo logic. </summary>
    internal partial class CustomComboCache : IDisposable
    {
        private const uint InvalidObjectID = 0xE000_0000;

        // 不必清：CooldownData 不存任何遊戲狀態（見該類別），同一個技能一直用同一個實例。
        private readonly ConcurrentDictionary<uint, CooldownData> cooldownCache = new();

        // Do not invalidate these
        private readonly ConcurrentDictionary<Type, JobGaugeBase> jobGaugeCache = new();

        /// <summary> Initializes a new instance of the <see cref="CustomComboCache"/> class. </summary>
        public CustomComboCache() => Svc.Framework.Update += Framework_Update;

        private delegate IntPtr GetActionCooldownSlotDelegate(IntPtr actionManager, int cooldownGroup);

        //private unsafe IntPtr CSAddress => (nint)(&JobGaugeManager.Instance()->EmptyGauge); //Save for a rainy day

        /// <inheritdoc/>
        public void Dispose() => Svc.Framework.Update -= Framework_Update;

        /// <summary> Gets a job gauge. </summary>
        /// <typeparam name="T"> Type of job gauge. </typeparam>
        /// <returns> The job gauge. </returns>
        internal T GetJobGauge<T>() where T : JobGaugeBase => Svc.Gauges.Get<T>();

        /// <summary> Gets the cooldown data for an action. </summary>
        /// <param name="actionID"> Action ID to check. </param>
        /// <returns> Cooldown data. </returns>
        internal unsafe CooldownData GetCooldown(uint actionID)
        {
            if (cooldownCache.TryGetValue(actionID, out var found))
                return found;

            return cooldownCache.GetOrAdd(actionID, static id => new CooldownData(id));
        }

        /// <summary> Get the maximum number of charges for an action. </summary>
        /// <param name="actionID"> Action ID to check. </param>
        /// <returns> Max number of charges at current level. </returns>
        internal unsafe ushort GetMaxCharges(uint actionID) => GetCooldown(actionID).MaxCharges;

        /// <summary> Get the resource cost of an action. </summary>
        /// <param name="actionID"> Action ID to check. </param>
        /// <returns> Returns the resource cost of an action. </returns>
        internal static unsafe int GetResourceCost(uint actionID)
        {
            ActionManager* actionManager = ActionManager.Instance();
            if (actionManager == null)
                return 0;

            int cost = ActionManager.GetActionCost(ActionType.Action, actionID, 0, 0, 0, 0);

            return cost;
        }

        /// <summary> Triggers when the game framework updates. Clears the status cache. </summary>
        private void Framework_Update(IFramework framework)
        {
            lock (statusCacheLock)
                statusCache.Clear();
        }
    }
}
