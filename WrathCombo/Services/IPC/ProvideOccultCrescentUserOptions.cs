#region

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ECommons.DalamudServices;
using ECommons.EzIpcManager;
using WrathCombo.Attributes;
using WrathCombo.Combos;
using TS = System.TimeSpan;

#endregion

namespace WrathCombo.Services.IPC;

// 本檔契約：這兩個端點讀寫的是「使用者自己存的設定」，與租約是兩條路。
// 租約的值只存在 Leasing，不會經這裡進設定檔；這裡寫的值也不會變成租約。
// 範圍只有帶 OccultCrescent 標註的選項（新月島輔助職業），其他選項一律拒絕。
public partial class Provider
{
    /// <summary> 寫入端點整批拒絕時的回傳值；0 以上是實際改變的選項數。 </summary>
    internal const int
        UserOptionsRefusedThread = -1,
        UserOptionsRefusedEmpty = -2,
        UserOptionsRefusedUnknownName = -3,
        UserOptionsRefusedNotOccultCrescent = -4;

    private static readonly FrozenSet<string> AllPresetNames =
        Enum.GetNames<CustomComboPreset>().ToFrozenSet(StringComparer.Ordinal);

    /// <summary> 內部名 → 選項與它是不是隱藏功能。只看列舉欄位上的標註，建好後不再變。 </summary>
    private static readonly FrozenDictionary<string, (CustomComboPreset Preset, bool Hidden)>
        OccultCrescentOptions = typeof(CustomComboPreset)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => Attribute.IsDefined(field, typeof(OccultCrescentAttribute)))
            .ToFrozenDictionary(
                field => field.Name,
                field => ((CustomComboPreset)field.GetValue(null)!,
                    Attribute.IsDefined(field, typeof(HiddenAttribute))),
                StringComparer.Ordinal);

    /// <summary>
    ///     使用者自己存的新月島輔助職業選項開關，不受任何租約影響。
    /// </summary>
    /// <returns>
    ///     選項內部名 → 使用者自己的值，每次回一份新的字典；設定還沒載入時回 <c>null</c>。
    /// </returns>
    /// <remarks> 任何執行緒都可以呼叫：只讀已啟用清單的唯讀快照。 </remarks>
    [EzIPC]
    public Dictionary<string, bool>? GetOccultCrescentUserOptionStates()
    {
        var config = Service.Configuration;
        if (config is null)
            return null;

        var enabled = config.EnabledActions;
        var showHidden = config.ShowHiddenFeatures;
        var states = new Dictionary<string, bool>(
            OccultCrescentOptions.Count, StringComparer.Ordinal);
        foreach (var (name, option) in OccultCrescentOptions)
            states[name] = enabled.Contains(option.Preset) &&
                           (!option.Hidden || showHidden);
        return states;
    }

    /// <summary>
    ///     改使用者自己存的新月島輔助職業選項開關，並照一般流程存檔。
    /// </summary>
    /// <param name="states">
    ///     選項內部名 → 要設成的值；只收 <see cref="GetOccultCrescentUserOptionStates" /> 列得出來的名稱。
    /// </param>
    /// <param name="requestedBy"> 呼叫端的名稱，只用在記錄檔。 </param>
    /// <returns>
    ///     0 以上＝實際改變的選項數。負值＝整批拒絕、什麼都沒改：-1 不在框架執行緒上或插件卸載中、
    ///     -2 沒有給任何選項、-3 有不認得的選項名、-4 有不屬於新月島輔助職業的選項。
    /// </returns>
    /// <remarks>
    ///     只改點名的選項：不連帶開上層選項、不關衝突選項。<br />
    ///     只在框架執行緒上作答；刻意不轉派，別條執行緒打進來就拒絕。
    /// </remarks>
    [EzIPC]
    public int SetOccultCrescentUserOptionStates
        (Dictionary<string, bool> states, string requestedBy)
    {
        if (UnloadToken.IsCancellationRequested ||
            !Svc.Framework.IsInFrameworkUpdateThread)
        {
            if (_ipcThrottle.Throttle("ipcOccultUserOptionsThread", TS.FromMinutes(1)))
                Logging.Information(
                    "新月島輔助職業選項的寫入只在框架執行緒上作答（插件卸載中也不作答），" +
                    "這次沒有改任何設定。");
            return UserOptionsRefusedThread;
        }

        var config = Service.Configuration;
        if (config is null || states is not { Count: > 0 })
            return UserOptionsRefusedEmpty;

        // 先整批驗完再動手：有一項不合格就什麼都不改。
        var targets = new List<(string Name, CustomComboPreset Preset, bool On)>(states.Count);
        foreach (var (name, on) in states)
        {
            if (name is null || !OccultCrescentOptions.TryGetValue(name, out var option))
            {
                var known = name is not null && AllPresetNames.Contains(name);
                Logging.Information(
                    $"拒絕 '{requestedBy}' 改使用者設定的請求：'{name}' " +
                    (known ? "不是新月島輔助職業的選項。" : "不是認得的選項名。"));
                return known
                    ? UserOptionsRefusedNotOccultCrescent
                    : UserOptionsRefusedUnknownName;
            }

            targets.Add((name, option.Preset, on));
        }

        List<string> turnedOn = [], turnedOff = [];
        foreach (var (name, preset, on) in targets)
        {
            if (!(on ? config.EnableAction(preset) : config.DisableAction(preset)))
                continue;
            (on ? turnedOn : turnedOff).Add(name);
            DebugFile.AddLog($"Set {preset} to {on} (IPC: {requestedBy})");
        }

        var changed = turnedOn.Count + turnedOff.Count;
        if (changed == 0)
            return 0;

        config.Save();
        Logging.Information(
            $"'{requestedBy}' 透過 IPC 改了你自己的新月島輔助職業選項並存檔：" +
            $"開 {turnedOn.Count} 項（{string.Join("、", turnedOn)}）、" +
            $"關 {turnedOff.Count} 項（{string.Join("、", turnedOff)}）。");
        return changed;
    }
}
