using System.Collections.Generic;

namespace WrathCombo.AutoRotation;

// 本檔假設與契約（只在框架執行緒用，不加鎖、不用 EzThrottler）：BMR 手動佇列接管時，本插件自己送的技能被收進 BMR 佇列；
// 同一招同一目標還沒放出前再送，BMR 當成玩家連按而進緊急模式：清掉佇列裡其他所有項目（含玩家剛手動按的）、
// 不管會不會拖到 GCD 都先放。期限照 BMR 的佇列期限（GCD 1 秒、能力技 3 秒，從第一次送進去算）再多等一點：
// BMR 在畫面階段才丟過期項，過期但還沒丟的那幾毫秒內重送一樣會撞到那一項而進緊急模式。

/// <summary> 自己送進 BMR 手動佇列、還沒放出的技能；期限內同一招同一目標不再重送。 </summary>
internal static class BmrOwnPushGuard
{
    internal const long GcdExpireMs = 1000;
    internal const long OgcdExpireMs = 3000;
    internal const long ExpireMarginMs = 100;

    private readonly record struct Entry(ulong Target, long At, long ExpireAt);

    private static readonly Dictionary<uint, Entry> Pending = [];

    /// <param name="usedAt"> 技能 ID → 最近一次真的放出的時間（與 <paramref name="now" /> 同一時鐘）。 </param>
    /// <returns> true＝這一招已在 BMR 佇列裡等著放出，這次不要再送。 </returns>
    internal static bool ShouldHold(uint actionId, ulong targetId, long now, IReadOnlyDictionary<uint, long> usedAt)
    {
        if (!Pending.TryGetValue(actionId, out var e))
            return false;

        if (now < e.ExpireAt && e.Target == targetId &&
            !(usedAt.TryGetValue(actionId, out var used) && used >= e.At))
            return true;

        Pending.Remove(actionId);
        return false;
    }

    /// <summary> 自己送出、BMR 接管下回傳 false（＝已收進它的佇列）時記一筆。 </summary>
    internal static void NoteQueued(uint actionId, ulong targetId, bool isGcd, long now) =>
        Pending[actionId] = new Entry(targetId, now, now + (isGcd ? GcdExpireMs : OgcdExpireMs) + ExpireMarginMs);

    internal static void Clear() => Pending.Clear();
}
