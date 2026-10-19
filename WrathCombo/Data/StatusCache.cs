using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Statuses;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using static WrathCombo.CustomComboNS.Functions.CustomComboFunctions;
using CSPlayerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;
using CSStatus = FFXIVClientStructs.FFXIV.Client.Game.Status;
using Status = Dalamud.Game.ClientState.Statuses.Status; // conflicts with structs if not defined

namespace WrathCombo.Data
{
    internal partial class CustomComboCache : IDisposable
    {
        private const uint InvalidStatusID = 0;

        //Invalidate this
        // 每幀清空；Dictionary.Clear 留著容量，同一幀重填不再每筆配置節點。鎖只包查表／寫入。
        private readonly Dictionary<(uint StatusID, ulong? TargetID, ulong? SourceID), Status?> statusCache = new();
        private readonly Lock statusCacheLock = new();

        /// <summary> Finds a status on the given object. </summary>
        /// <param name="statusID"> Status effect ID. </param>
        /// <param name="obj"> Object to look for effects on. </param>
        /// <param name="sourceID"> Source object ID. </param>
        /// <returns> Status object or null. </returns>
        internal Status? GetStatus(uint statusID, IGameObject? obj, ulong? sourceID)
        {
            if (obj is null)
                return null;

            var key = (statusID, obj.GameObjectId, sourceID);

            lock (statusCacheLock)
            {
                if (statusCache.TryGetValue(key, out var found))
                    return found;
            }

            var result = FindStatus(statusID, obj, sourceID);
            lock (statusCacheLock)
                statusCache[key] = result;
            return result;
        }

        // 與走訪 Dalamud StatusList 同樣的格子、順序與條件，只替命中的那一格建包裝。
        private static unsafe Status? FindStatus(uint statusID, IGameObject obj, ulong? sourceID)
        {
            if (obj is not IBattleChara chara)
                return null;

            var slots = StatusCache.LiveSlots(chara);
            for (var i = 0; i < slots.Length; i++)
            {
                ref var status = ref slots[i];
                if (status.StatusId == InvalidStatusID)
                    continue;

                var sourceId = status.SourceObject.ObjectId;
                if (status.StatusId == statusID &&
                    (!sourceID.HasValue || sourceId == 0 || sourceId == InvalidObjectID || sourceId == sourceID))
                {
                    return StatusList.CreateStatusReference((nint)Unsafe.AsPointer(ref status));
                }
            }

            return null;
        }
    }

    internal class StatusCache
    {
        /// <summary>
        /// Lumina Status Sheet Dictionary
        /// </summary>
        private static readonly FrozenDictionary<uint, Lumina.Excel.Sheets.Status> StatusSheet = 
            Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Status>()
                .ToFrozenDictionary(i => i.RowId);

        private static readonly FrozenDictionary<uint, Lumina.Excel.Sheets.Status> ENStatusSheet =
            Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Status>(Dalamud.Game.ClientLanguage.English)
                .ToFrozenDictionary(i => i.RowId);

        private static readonly HashSet<uint> DamageDownStatuses =
            ENStatusSheet.TryGetValue(62, out var refRow)
                ? [.. ENStatusSheet
                .Where(x => x.Value.Name.ToString().Equals(refRow.Name.ToString(), StringComparison.CurrentCultureIgnoreCase))
                .Select(x => x.Key)] : [];

        public static bool HasDamageDown(IGameObject? target) => HasStatusInCacheList(DamageDownStatuses, target);

        private static readonly HashSet<uint> DamageUpStatuses =
            ENStatusSheet.TryGetValue(61, out var refRow)
                 ? [.. ENStatusSheet
                .Where(x => x.Value.Name.ToString().Contains(refRow.Name.ToString(), StringComparison.CurrentCultureIgnoreCase))
                .Select(x => x.Key)] : [];

        public static bool HasDamageUp(IGameObject? target) => HasStatusInCacheList(DamageUpStatuses, target);
        
        public static bool HasEvasionUp(IGameObject? target) => HasStatusInCacheList(EvasionUpStatuses.Ids, target);

        // 第一次查詢時才算，之後唯讀（型別初始化保證只算一次、執行緒安全）。
        private static class EvasionUpStatuses
        {
            internal static readonly HashSet<uint> Ids =
                ENStatusSheet.TryGetValue(31, out var refRow)
                    ? [.. ENStatusSheet
                        .Where(x => x.Value.Name.ToString().Contains(refRow.Name.ToString(), StringComparison.CurrentCultureIgnoreCase))
                        .Select(x => x.Key)]
                    : [];
        }

        /// <summary>
        /// A cached set of dispellable status IDs for quick lookup.
        /// </summary>
        private static readonly HashSet<uint> DispellableStatuses = [..
            StatusSheet
                .Where(kvp => kvp.Value.CanDispel)
                .Select(kvp => kvp.Key)];

        public static bool HasCleansableDebuff(IGameObject? target) => HasStatusInCacheList(DispellableStatuses, target);

        public static bool HasLethalCleansableDebuff(IGameObject? target) => HasStatusInCacheList(LethalCleansableStatuses.Ids, target);

        // 第一次查詢時才算，之後唯讀；只讀可清除那幾百筆的說明文字。
        private static class LethalCleansableStatuses
        {
            internal static readonly HashSet<uint> Ids = EsunaPriority.LethalCleansable(
                StatusSheet.Where(x => x.Value.CanDispel).Select(x => (x.Key, x.Value.Description.ToString())),
                id => StatusSheet.TryGetValue(id, out var row) ? row.Description.ToString() : null);
        }

        /// <summary>
        /// A cached set of beneficial status IDs for quick lookup.
        /// </summary>
        private static readonly HashSet<uint> BeneficialStatuses = [..
            StatusSheet
                .Where(kvp => kvp.Value.StatusCategory == 1)
                .Select(kvp => kvp.Key)];

        public static bool HasBeneficialStatus(IGameObject? targt) => HasStatusInCacheList(BeneficialStatuses, targt);

        /// <summary>
        /// A set of status effect IDs that grant general invincibility.
        /// </summary>
        /// <remarks>
        /// Includes statuses like Hallowed Ground (151), Living Dead (325), etc.
        /// </remarks>
        internal static readonly HashSet<uint> InvincibleStatuses = GenerateInv();

        private static HashSet<uint> GenerateInv()
        {
            //Search by Invincibility Icon
            uint targetIcon = 215024; //StatusSheet.FirstOrDefault(row => row.Value.RowId == 325).Value.Icon;
            //Svc.Log.Debug($"Invincible Icon: {targetIcon}");

            var invincibles = StatusSheet.Where(row => row.Value.Icon == targetIcon).Select(row => row.Key).ToHashSet();
            //Add Random Invulnerabilities not yet assigned to TerritoryType
            invincibles.UnionWith([151, 198, 469, 592, 1240, 1302, 1303, 1567, 1936, 2413, 2654, 3012, 3039, 3052, 3054, 4175]);
            return invincibles;
        }

        

        /// <summary>
        /// Looks up the name of a Status by ID in Lumina Sheets
        /// </summary>
        /// <param name="id">Status ID</param>
        /// <returns></returns>
        public static string GetStatusName(uint id) => StatusSheet.TryGetValue(id, out var status) ? status.Name.ToString() : "Unknown Status";

        /// <summary>
        /// Returns an uint List of Status IDs based on Name.
        /// </summary>
        /// <param name="status"></param>
        /// <returns></returns>
        public static List<uint>? GetStatusesByName(string status)
        {
            if (string.IsNullOrEmpty(status))
                return null;
            var statusIds = StatusSheet
                .Where(x => x.Value.Name.ToString().Equals(status, StringComparison.CurrentCultureIgnoreCase))
                .Select(x => x.Key)
                .ToList();
            return statusIds.Count != 0 ? statusIds : null;
        }

        /// <summary>
        /// Checks a GameObject's Status list against a set of Status IDs
        /// </summary>
        /// <param name="statusList">Hashset of Status IDs to check</param>
        /// <param name="gameObject">GameObject to check</param>
        /// <returns></returns>
        internal static bool HasStatusInCacheList(HashSet<uint> statusList, IGameObject? gameObject = null)
        {
            if (gameObject is not IBattleChara chara || statusList.Count == 0)
                return false;

            foreach (ref readonly var status in LiveSlots(chara))
            {
                if (status.StatusId != 0 && statusList.Contains(status.StatusId))
                    return true;
            }

            return false;
        }

        /// <summary> 身上有沒有 <paramref name="statusId" />（不論施放者）；同 <c>StatusList.Any(x =&gt; x.StatusId == id)</c>，但不建包裝。 </summary>
        internal static bool HasStatusId(IBattleChara chara, uint statusId)
        {
            foreach (ref readonly var status in LiveSlots(chara))
            {
                if (status.StatusId != 0 && status.StatusId == statusId)
                    return true;
            }

            return false;
        }

        /// <summary> 有狀態的格數；同 <c>StatusList.Count()</c>，但不建包裝。 </summary>
        internal static int CountStatuses(IBattleChara chara)
        {
            var count = 0;
            foreach (ref readonly var status in LiveSlots(chara))
            {
                if (status.StatusId != 0)
                    count++;
            }

            return count;
        }

        /// <summary>
        ///     Dalamud <c>StatusList</c> 列舉看的那幾格：同一個 <c>GetStatusManager()</c>、前 NumValidStatuses 格，PlayerState 沒載入時一格都沒有。
        ///     StatusId 為 0 的空格由呼叫端略過。只在取得 <paramref name="chara" /> 的同一幀用，不可保存。
        /// </summary>
        internal static unsafe Span<CSStatus> LiveSlots(IBattleChara chara)
        {
            if (chara.Address == nint.Zero)
                return default;

            var manager = ((Character*)chara.Address)->GetStatusManager();
            if (manager == null)
                return default;

            var slots = manager->Status;
            var count = Math.Min((int)manager->NumValidStatuses, slots.Length);
            // Dalamud 也是有格子才問 PlayerState（同一個 CS PlayerState.IsLoaded）。
            if (count == 0)
                return default;

            var playerState = CSPlayerState.Instance();
            if (playerState == null || !playerState->IsLoaded)
                return default;

            return slots[..count];
        }

        /// <summary>
        /// Compares two hashsets, in this case, used to compare a cached set of status IDs against a character's StatusID list
        /// </summary>
        /// <param name="statusList"></param>
        /// <param name="charaStatusList"></param>
        /// <returns></returns>
        internal static bool CompareLists(HashSet<uint> statusList, HashSet<uint> charaStatusList) => 
            charaStatusList.Any(id => statusList.Contains(id));
    }
}
