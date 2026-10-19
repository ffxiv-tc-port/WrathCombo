#region

using System;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Logging;
using WrathCombo.Data;

#endregion

namespace WrathCombo.CustomComboNS.Functions;

internal abstract partial class CustomComboFunctions
{
    /// <summary>可被復原清除、而且到時就無法戰鬥（死亡宣告、窒息這類）的負面狀態。</summary>
    public static bool HasLethalCleansableDebuff(IGameObject? target) =>
        StatusCache.HasLethalCleansableDebuff(target);

    private const float EsunaHealRange = 30f;
    private static long _esunaLoggedAt;

    /// <summary>
    ///     治療組合裡「這一下放不放復原」。致命狀態一律放；否則要通過該職業原本的目標血量檢查，
    ///     而且 30 米內沒有隊友（含目標）血量低於 <paramref name="criticalHpPercent" />（0＝不檢查）。
    ///     只在框架執行緒呼叫（連段的 Invoke）。
    /// </summary>
    public static bool EsunaAllowed(IGameObject? target, bool targetHpOk, int criticalHpPercent, bool includeShields)
    {
        if (HasLethalCleansableDebuff(target))
        {
            if ((!targetHpOk || CriticalAlly(target, criticalHpPercent, includeShields) is not null) && EsunaLogDue())
                PluginLog.Information($"[WrathCombo][復原] {target?.Name} 身上有到時就無法戰鬥的狀態，血量危急也先放復原");
            return true;
        }

        if (!targetHpOk)
            return false;

        if (CriticalAlly(target, criticalHpPercent, includeShields) is not { } ally)
            return true;

        if (EsunaLogDue())
            PluginLog.Information($"[WrathCombo][復原] {ally.Name} 血量 {GetTargetHPPercent(ally, includeShields):0}% 低於 {criticalHpPercent}%，先補血、延後復原");
        return false;
    }

    private static IBattleChara? CriticalAlly(IGameObject? target, int criticalHpPercent, bool includeShields)
    {
        if (criticalHpPercent <= 0)
            return null;

        if (IsCriticalAlly(target, criticalHpPercent, includeShields))
            return (IBattleChara)target!;

        foreach (var member in GetPartyMembers())
            if (IsCriticalAlly(member.BattleChara, criticalHpPercent, includeShields))
                return member.BattleChara;

        return null;
    }

    private static bool IsCriticalAlly(IGameObject? obj, int criticalHpPercent, bool includeShields) =>
        obj is IBattleChara { IsDead: false, IsTargetable: true } chara &&
        chara.CurrentHp > 0 &&
        GetTargetDistance(chara) <= EsunaHealRange &&
        EsunaPriority.IsCritical(GetTargetHPPercent(chara, includeShields), criticalHpPercent);

    // 先判再組字串：連段每幀都會跑到這裡。
    private static bool EsunaLogDue()
    {
        var now = Environment.TickCount64;
        if (now - _esunaLoggedAt < 10_000)
            return false;

        _esunaLoggedAt = now;
        return true;
    }
}
