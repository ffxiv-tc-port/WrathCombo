using System.Collections.Generic;

namespace WrathCombo.AutoRotation;

// 本檔假設與契約（只在框架執行緒用，不加鎖、不用 EzThrottler）：BMR 手動佇列接管時，它的佇列每個主冷卻群組只留一項，
// 後送的換掉先送的。玩家自己按、被 BMR 收進佇列的那一項，在同群組有技能送出（SendAction）或照 BMR 的期限過期前，
// 本插件自己的送出不碰同一群組。群組鍵＝Action 表 CooldownGroup（BMR 主群組＝它減 1，相等關係不變）；
// 期限與 BmrOwnPushGuard 同一套（GCD 1 秒、能力技 3 秒，再加 0.1 秒）。

/// <summary> 玩家手動排進 BMR 佇列、還沒放出的技能（每個冷卻群組一筆）。 </summary>
internal static class BmrManualPushGuard
{
    private readonly record struct Entry(uint Action, ulong Target, long At, long ExpireAt);

    private static readonly Dictionary<int, Entry> Pending = [];

    internal static bool HasPending => Pending.Count > 0;

    /// <summary> 玩家自己送出、BMR 接管下回傳 false（＝已收進它的佇列）時記一筆；同群組的舊紀錄被取代（BMR 也是換掉）。 </summary>
    internal static void NoteManual(uint actionId, int cooldownGroup, ulong targetId, bool isGcd, long now) =>
        Pending[cooldownGroup] = new Entry(actionId, targetId, now,
            now + (isGcd ? BmrOwnPushGuard.GcdExpireMs : BmrOwnPushGuard.OgcdExpireMs) + BmrOwnPushGuard.ExpireMarginMs);

    /// <summary> 同群組有技能真的送出＝那一項已放出（或被同群組的別招頂掉），解除。 </summary>
    internal static void NoteSent(int cooldownGroup) => Pending.Remove(cooldownGroup);

    /// <returns> true＝同群組有玩家手動排的技能在等，這次自己的送出先不送。 </returns>
    internal static bool ShouldHold(int cooldownGroup, long now, out uint manualAction, out long ageMs)
    {
        manualAction = 0;
        ageMs = 0;
        if (!Pending.TryGetValue(cooldownGroup, out var e))
            return false;

        if (now < e.ExpireAt)
        {
            manualAction = e.Action;
            ageMs = now - e.At;
            return true;
        }

        Pending.Remove(cooldownGroup);
        return false;
    }

    internal static void Clear() => Pending.Clear();
}
