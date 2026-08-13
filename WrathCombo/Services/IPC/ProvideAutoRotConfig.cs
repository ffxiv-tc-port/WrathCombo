#region

using System;
using System.Collections.Generic;
using ECommons.EzIpcManager;
using arcOption = WrathCombo.Services.IPC.AutoRotationConfigOption;
using EZ = ECommons.Throttlers.EzThrottler;

#endregion

namespace WrathCombo.Services.IPC;

public partial class Provider
{
    /// <summary>
    ///     本 fork 沒有對應設定欄位、也沒有對應行為的自動循環設定選項。
    /// </summary>
    /// <remarks>
    ///     列舉值本身**必須存在**（少一個後面的編號就整排位移，呼叫端會拿到別的選項），
    ///     但這裡不能假裝設得動：<br />
    ///     · <c>Get</c> 回 <see langword="null" />，也就是「這個選項在這裡不存在」，
    ///     而不是回一個看起來合法的 <c>false</c>／<c>0</c>。<br />
    ///     · <c>Set</c> 回 <see cref="SetResult.InvalidConfiguration" />，不收下租約。<br />
    ///     🔴 刻意**不**採用「收下但不生效」：那會讓呼叫端 Set 拿到 Okay、Get 拿到它剛設的值，
    ///     實際行為卻沒有任何改變 —— 是完全靜默的假象，比明講不支援糟糕得多。
    /// </remarks>
    /// <seealso cref="AutoRotationConfigOption" />
    internal static readonly HashSet<arcOption> UnsupportedConfigOptions =
    [
        arcOption.SingleTargetExcogHPP,
        arcOption.AutoRezDPSJobsHealersOnly,
        arcOption.DPSAlwaysHardTarget,
        arcOption.HealerAlwaysHardTarget,
        arcOption.IgnoreRangeInBoss,
        arcOption.UnTargetAndDisableForPenalty,
        arcOption.IncludeShields,
    ];

    /// <summary>
    ///     Get the state of Auto-Rotation Configuration in Wrath Combo.
    /// </summary>
    /// <param name="passedOption">
    ///     The option to check the value of.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <returns>The correctly-typed value of the configuration.</returns>
    [EzIPC]
    public object? GetAutoRotationConfigState(object passedOption)
    {
        // Try to cast the input to an Auto-Rotation Configuration option
        arcOption option;
        try
        {
            option = (arcOption)Convert.ToInt32(passedOption);

            // 本 fork 不支援的選項：回 null（＝「不知道／沒有這個東西」），
            // 不要回一個看起來合法的值把呼叫端騙過去。
            if (UnsupportedConfigOptions.Contains(option))
            {
                // 這個端點可能被輪詢，節流以免洗版；EzThrottler 首次一定放行。
                if (EZ.Throttle($"WrathIPCUnsupportedARConfigGet{option}", 60_000))
                    Logging.Information(
                        $"自動循環設定選項 '{option}' 在本 fork 尚未實作，查詢一律回傳 null。");
                return null;
            }

            // Check if the config is overriden by a lease
            var checkControlled = Leasing.CheckAutoRotationConfigControlled(option);
            if (checkControlled is not null)
            {
                var type = Helper.GetAutoRotationConfigType(option);
                return type.IsEnum
                    ? checkControlled.Value
                    : Convert.ChangeType(checkControlled.Value, type);
            }
        }
        catch (Exception)
        {
            Logging.Warn("Invalid or not-yet-implemented `option` of " +
                          $"'{passedOption}'. Please refer to " +
                          "WrathCombo.Services.IPC.AutoRotationConfigOption");
            return null;
        }

        // Otherwise, return the actual config value
        var arc = Service.Configuration.RotationConfig;
        var arcD = Service.Configuration.RotationConfig.DPSSettings;
        var arcH = Service.Configuration.RotationConfig.HealerSettings;
        try
        {
            return option switch
            {
                arcOption.InCombatOnly => arc.InCombatOnly,
                arcOption.DPSRotationMode => arc.DPSRotationMode,
                arcOption.HealerRotationMode => arc.HealerRotationMode,
                arcOption.FATEPriority => arcD.FATEPriority,
                arcOption.QuestPriority => arcD.QuestPriority,
                arcOption.SingleTargetHPP => arcH.SingleTargetHPP,
                arcOption.AoETargetHPP => arcH.AoETargetHPP,
                arcOption.SingleTargetRegenHPP => arcH.SingleTargetRegenHPP,
                arcOption.ManageKardia => arcH.ManageKardia,
                arcOption.AutoRez => arcH.AutoRez,
                arcOption.AutoRezDPSJobs => arcH.AutoRezDPSJobs,
                arcOption.AutoRezOutOfParty => arcH.AutoRezOutOfParty,
                arcOption.AutoCleanse => arcH.AutoCleanse,
                arcOption.IncludeNPCs => arcH.IncludeNPCs,
                arcOption.OnlyAttackInCombat => arcD.OnlyAttackInCombat,
                arcOption.OrbwalkerIntegration => arc.OrbwalkerIntegration,
                arcOption.DPSAoETargets => arcD.DPSAoETargets,
                arcOption.BypassQuest => arc.BypassQuest,
                arcOption.BypassFATE => arc.BypassFATE,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(passedOption), passedOption, null)
            };
        }
        catch (Exception)
        {
            Logging.Error($"Invalid `option` of '{passedOption}'. Please refer to " +
                          "WrathCombo.Services.IPC.AutoRotationConfigOption");
            return null;
        }
    }

    /// <summary>
    ///     Set the state of Auto-Rotation Configuration in Wrath Combo.
    /// </summary>
    /// <param name="lease">Your lease ID from <see cref="RegisterForLease(string,string)" /></param>
    /// <param name="passedOption">
    ///     The Auto-Rotation Configuration option you want to set.<br />
    ///     This is a subset of the Auto-Rotation options, flattened into a single
    ///     enum.
    /// </param>
    /// <param name="value">
    ///     The value you want to set the option to.<br />
    ///     All valid options can be parsed from an int, or the exact expected types.
    /// </param>
    /// <returns>
    ///     The <see cref="SetResult" /> status code indicating the result of the
    ///     operation.
    /// </returns>
    /// <seealso cref="AutoRotationConfigOption"/>
    [EzIPC]
    public SetResult SetAutoRotationConfigState
        (Guid lease, object passedOption, object value)
    {
        // Bail for standard conditions
        if (Helper.CheckForBailConditionsAtSetTime(out var result, lease))
            return result;

        // Try to cast the input to an Auto-Rotation Configuration option
        arcOption option;
        Type? type;
        TypeCode? typeCode;
        try
        {
            option = (arcOption)Convert.ToInt32(passedOption);

            // Try to convert the value to the correct type
            type = Helper.GetAutoRotationConfigType(option);
            typeCode = Type.GetTypeCode(type);
        }
        catch (Exception)
        {
            Logging.Warn("Invalid or not-yet-implemented `option` of " +
                          $"'{passedOption}'. Please refer to " +
                          "WrathCombo.Services.IPC.AutoRotationConfigOption");
            return SetResult.InvalidConfiguration;
        }

        // 本 fork 不支援的選項：明講設不了，不要收下一個永遠不會生效的租約。
        if (UnsupportedConfigOptions.Contains(option))
        {
            Logging.Information(
                $"自動循環設定選項 '{option}' 在本 fork 尚未實作，" +
                "設定請求被拒絕（回 InvalidConfiguration）。");
            return SetResult.InvalidConfiguration;
        }

        object convertedValue;
        try
        {
            // Handle enum values as any number type, and convert it to the real enum
            if (type.IsEnum && typeCode is >= TypeCode.SByte and <= TypeCode.UInt64)
                convertedValue = Enum.ToObject(type, value);
            // Convert anything else directly
            else
                convertedValue = Convert.ChangeType(value, type);
        }
        catch (Exception e)
        {
            Logging.Error("Failed to convert value to correct type.\n" +
                          "Value likely out of range for option that wanted an enum. " +
                          $"Expected type: {type}.\n" +
                          e.Message);
            return SetResult.InvalidValue;
        }

        // Handle converting bool->int, which doesn't work for some reason, despite
        // int->bool working fine.
        if (type == typeof(bool))
            convertedValue = (bool)convertedValue ? 1 : 0;

        return Leasing.AddRegistrationForAutoRotationConfig(
            lease, option, (int)convertedValue);
    }
}
