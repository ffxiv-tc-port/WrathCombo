#region

using System.Collections.Frozen;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     <c>RaidWideCasting()</c> 的詠唱判準：敵方詠唱的招式形狀像全場傷害（範圍型、半徑 ≥30）才算，
///     但排除「詠唱完不會放出來、從不造成傷害」的招式（主線劇情戰之類），免得治療與坦克白放盾和減傷。
/// </summary>
/// <remarks>只收有實機觀察或副本資料佐證的招式；每一項註明出處。</remarks>
internal static class NonResolvingCasts
{
    private static readonly FrozenSet<uint> Ids = FrozenSet.ToFrozenSet<uint>(
    [
        28510, // 處決風刃（究極武器破壞作戰，CFC 830）：使用者實機觀察詠唱完不會放出來
        29435, // 土神的激怒（同上）：IINACT 戰鬥紀錄顯示詠唱中被取消（Cancelled）、從不結算
    ]);

    internal static bool Contains(uint actionId) => Ids.Contains(actionId);

    /// <summary>判準與原本相同（Action 表 CastType 為 2 或 5、EffectRange ≥30），再扣掉上表。</summary>
    internal static bool IsRaidwideCast(uint actionId, byte castType, byte effectRange) =>
        castType is 2 or 5 && effectRange >= 30 && !Ids.Contains(actionId);
}
