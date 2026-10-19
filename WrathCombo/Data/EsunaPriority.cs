#region

using System;
using System.Collections.Generic;

#endregion

namespace WrathCombo.Data;

/// <summary>
///     「復原」與補血的先後：有隊友血量危急時先補血；但倒數結束就無法戰鬥、補血救不了的負面狀態一律先清。
///     只放與遊戲狀態無關的判斷，讓離線探針能直接呼叫。
/// </summary>
internal static class EsunaPriority
{
    /// <summary>死亡宣告（210）與窒息（700）的說明文字當參照：同說明＝同一類，改版新增的也認得出來。</summary>
    private static readonly uint[] ReferenceRows = [210, 700];

    /// <summary>台服 7.25 Status 表裡已知的幾筆明列，參照比對失效時仍認得；2965 說明文字與參照不同，只能明列。</summary>
    private static readonly uint[] ExtraIds =
    [
        700, 910, 3364, 4558,
        2965, // 致死腐爛毒素
    ];

    /// <param name="dispellable">可被復原清除的狀態（CanDispel）與其說明文字。</param>
    /// <param name="describe">任一狀態 ID 的說明文字；查不到回 <see langword="null" />。</param>
    internal static HashSet<uint> LethalCleansable(
        IEnumerable<(uint Id, string Description)> dispellable, Func<uint, string?> describe)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ReferenceRows)
            if (describe(id) is { Length: > 0 } d)
                refs.Add(d);

        var result = new HashSet<uint>();
        foreach (var (id, desc) in dispellable)
            if (Array.IndexOf(ExtraIds, id) >= 0 || refs.Contains(desc))
                result.Add(id);
        return result;
    }

    /// <summary>血量百分比低於門檻＝危急；門檻 ≤0＝停用（永不危急）。</summary>
    internal static bool IsCritical(float hpPercent, int criticalHpPercent) =>
        criticalHpPercent > 0 && hpPercent < criticalHpPercent;
}
