using System;
using System.Collections.Generic;
using WrathCombo.Combos.PvE;
using WrathCombo.Combos.PvP;
using WrathCombo.Extensions;

namespace WrathCombo.Resources.Dictionary.Chinese.Description
{
    public static class VariantSkills
    {
        public static IEnumerable<KeyValuePair<string, string>> GetSkills()
        {
            return
            [
                // Manually added
                #region Variant                                
                KeyValuePair.Create("Use Variant", "使用多變"),
                KeyValuePair.Create("whenever the debuff is not present or less than 3s.", "（目標沒有精神鏢DOT或DOT剩餘時間少於3秒）"),
                KeyValuePair.Create("when HP is below set threshold.", "（生命值低於設定的閾值）"),
                KeyValuePair.Create("on cooldown.", "（冷卻即用）"),
                KeyValuePair.Create("Spirit Dart", "精神鏢"),
                KeyValuePair.Create("Ultimatum", "最後通牒"),
                KeyValuePair.Create("Variant", "多變"),
                #endregion
            ];
        }
    }
}
