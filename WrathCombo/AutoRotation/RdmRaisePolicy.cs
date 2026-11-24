using System;

namespace WrathCombo.AutoRotation;

/// <summary>
///     赤魔自動復活「拉誰、要不要中斷輸出」的純判斷：不碰遊戲物件，離線探針直接編譯本檔。
/// </summary>
/// <remarks>
///     成員清單不含自己；同一優先序內照清單順序取第一個（呼叫端放小隊成員在前）。
///     「全倒」只看小隊成員：該職能有人、沒有人站著、沒有人已在被拉，而且至少一人可拉。
/// </remarks>
internal static class RdmRaisePolicy
{
    internal enum Role : byte { Dps, Tank, Healer }

    /// <param name="Raisable"> 死亡且通過自動復活的所有既有條件（距離、狀態、節流、沒有別人正在拉）。 </param>
    /// <param name="Pending"> 死亡但已有人處理（身上有「復活」待接受、別人正在詠唱復活、或自己剛送出）。 </param>
    internal readonly record struct Member(Role Role, bool InParty, bool Alive, bool Raisable, bool Pending);

    /// <param name="Index"> 要拉的成員索引；-1＝這一輪不拉。 </param>
    /// <param name="Emergency"> 拉的是「小隊坦克或治療全倒」的那一職能，呼叫端要先拉人再輸出。 </param>
    /// <param name="LastShot"> 這一發放完 MP 就不夠下一發，而且這次因此只考慮坦克與治療職業。 </param>
    internal readonly record struct Decision(int Index, bool Emergency, bool LastShot);

    /// <summary> 放完這一發後 MP 不夠再放一發。消耗讀不到（≤0）時不算最後一發，不擋任何人。 </summary>
    internal static bool IsLastShot(uint currentMp, int cost) =>
        cost > 0 && (long)currentMp - cost < cost;

    internal static bool PartyHasTankOrHealer(ReadOnlySpan<Member> members)
    {
        foreach (var m in members)
            if (m.InParty && m.Role is not Role.Dps)
                return true;
        return false;
    }

    internal static bool RoleDown(ReadOnlySpan<Member> members, Role role)
    {
        var any = false;
        var raisable = false;
        foreach (var m in members)
        {
            if (!m.InParty || m.Role != role)
                continue;
            if (m.Alive || m.Pending)
                return false;
            any = true;
            raisable |= m.Raisable;
        }
        return any && raisable;
    }

    /// <param name="reserveLastShot"> 規則 A：最後一發只給坦克與治療職業（小隊裡沒有坦克／治療職業時不保留）。 </param>
    /// <param name="emergency"> 規則 B：小隊坦克或治療職業全倒時只拉那一職能，治療職業先。 </param>
    /// <param name="rolePriority"> 規則 C：治療職業、坦克、DPS 依序；同職能小隊成員先於小隊外。 </param>
    internal static Decision Decide(ReadOnlySpan<Member> members, uint currentMp, int cost,
        bool reserveLastShot, bool emergency, bool rolePriority)
    {
        if (cost > 0 && currentMp < cost)
            return new(-1, false, false);

        var lastShot = reserveLastShot && IsLastShot(currentMp, cost) && PartyHasTankOrHealer(members);
        var healerDown = emergency && RoleDown(members, Role.Healer);
        var tankDown = emergency && RoleDown(members, Role.Tank);
        var anyDown = healerDown || tankDown;

        var best = -1;
        var bestRank = int.MaxValue;
        for (var i = 0; i < members.Length; i++)
        {
            var m = members[i];
            if (!m.Raisable || (lastShot && m.Role is Role.Dps))
                continue;

            int rank;
            if (anyDown)
            {
                if (!m.InParty) continue;
                if (m.Role is Role.Healer && healerDown) rank = 0;
                else if (m.Role is Role.Tank && tankDown) rank = 1;
                else continue;
            }
            else if (rolePriority)
                rank = (m.Role switch { Role.Healer => 0, Role.Tank => 1, _ => 2 }) * 2 + (m.InParty ? 0 : 1);
            else
                rank = 0;

            if (rank < bestRank)
            {
                best = i;
                bestRank = rank;
            }
        }

        return new(best, anyDown && best >= 0, lastShot);
    }
}
