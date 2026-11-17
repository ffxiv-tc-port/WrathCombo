using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using WrathCombo.Combos;

namespace WrathCombo.Core;

// 本檔假設與契約：
// - 名稱比對不分大小寫（與原本的 Enum.Parse(…, true) 相同），比對前去掉前後空白。
// - 純數字字串只認「有定義的」preset 數值；未定義的數值與逗號組合（"A, B"）一律不認得。
// - 型別初始化不可擲：兩張表都用 TryAdd 建，重名或重值不會讓插件載入失敗。

/// <summary> 字串→preset 的凍結查表，取代每次呼叫 <c>Enum.Parse</c>／線性掃描。執行緒安全（建好後唯讀）。 </summary>
internal static class PresetNames
{
    private static readonly FrozenDictionary<string, CustomComboPreset> ByName = BuildByName();
    private static readonly FrozenSet<int> DefinedValues = BuildDefinedValues();

    private static FrozenDictionary<string, CustomComboPreset> BuildByName()
    {
        var map = new Dictionary<string, CustomComboPreset>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames<CustomComboPreset>())
            map.TryAdd(name, Enum.Parse<CustomComboPreset>(name));
        return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenSet<int> BuildDefinedValues()
    {
        var set = new HashSet<int>();
        foreach (var preset in Enum.GetValues<CustomComboPreset>())
            set.Add((int)preset);
        return set.ToFrozenSet();
    }

    /// <summary> 只認成員名（不分大小寫）。 </summary>
    internal static bool TryGetByName(string? value, out CustomComboPreset preset)
    {
        preset = default;
        return value is not null && ByName.TryGetValue(value.Trim(), out preset);
    }

    /// <summary> 成員名，或有定義的 preset 數值（IPC 文件寫明「也接受 preset 的 ID」）。 </summary>
    internal static bool TryGetByNameOrId(string? value, out CustomComboPreset preset)
    {
        if (TryGetByName(value, out preset))
            return true;

        if (value is not null &&
            int.TryParse(value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id) &&
            DefinedValues.Contains(id))
        {
            preset = (CustomComboPreset)id;
            return true;
        }

        preset = default;
        return false;
    }

    /// <summary> 給原本 <c>Enum.Parse</c> 會擲的呼叫點：不認得照舊擲 <see cref="ArgumentException" />。 </summary>
    internal static CustomComboPreset ParseByNameOrId(string value) =>
        TryGetByNameOrId(value, out var preset)
            ? preset
            : throw new ArgumentException($"Requested preset '{value}' was not found.", nameof(value));
}
