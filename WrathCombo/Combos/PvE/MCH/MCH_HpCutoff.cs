using System;

// 本檔只放純函式、不參考遊戲與 Dalamud（離線測試直接 Compile Include 這一份）。
namespace WrathCombo.Combos.PvE;

internal partial class MCH
{
    /// <summary>
    ///     大招的目標血量門檻：目標血量 ≤ <paramref name="hpCutoff" />% 時不放。
    ///     <paramref name="hpCutoff" /> ≤ 0＝不檢查，兩個委派都不呼叫（與沒有這道門檻逐字相同）。
    ///     <paramref name="bossScope" />：0＝頭目照常放、只擋非頭目；1＝所有目標都擋。
    /// </summary>
    internal static bool HpCutoffAllows(int hpCutoff, int bossScope, Func<bool> isBoss, Func<float> targetHpPercent) =>
        hpCutoff <= 0 || bossScope == 0 && isBoss() || targetHpPercent() > hpCutoff;
}
