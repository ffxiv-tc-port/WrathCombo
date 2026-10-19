using System;
using System.Text;

// 本檔假設與契約：
// - 只放純函式與純狀態（不碰遊戲、不寫 log），所有成員只在框架執行緒使用。
// - 「不出招原因」的判定、log 節流、開打依據的歸因都在這裡，離線向量直接打建置產物驗證；
//   SimulateLog 是離線測試入口，執行期不呼叫。

namespace WrathCombo.AutoRotation;

/// <summary> 自動循環這一輪停在哪一段。 </summary>
internal enum NoActStage : byte
{
    Rotation = 0,
    Disabled,
    Unavailable,
    Dead,
    Occupied,
    Mounted,
    NotInCombat,
    Busy,
    Healing,
    Processed,
}

/// <summary> 攻擊技能這一輪沒送出的原因；同一輪多個連擊功能取數值最大者。範圍技能只記「僅於戰鬥中」閘門。 </summary>
internal enum NoActStOutcome : byte
{
    None = 0,
    Transient,
    MovingCaster,
    OutOfRange,
    TargetNotAttackable,
    NoTarget,
    StGate,
    NotFateTarget,
}

internal enum NoActReason : byte
{
    None = 0,
    Acting,
    Idle,
    Disabled,
    Unavailable,
    Dead,
    Occupied,
    Mounted,
    NotInCombat,
    NoAutoPresets,
    Transcendent,
    PvpBlocked,
    StGate,
    NoTarget,
    TargetNotAttackable,
    OutOfRange,
    MovingCaster,
    NotFateTarget,
}

/// <summary> 「僅於戰鬥中」閘門是因為什麼而放行。 </summary>
internal enum NoActGateSource : byte
{
    Unknown = 0,
    InCombatOnlyOff,
    PlayerInCombat,
    SelfFlagOnly,
    PartySlotMember,
    NonSlotMember,
    OutOfPartyNpc,
    FateBypass,
    QuestBypass,
}

internal static class NoActDiagnostics
{
    internal static NoActReason ClassifyRaw(NoActStage stage, bool fired,
        NoActStOutcome stOutcome, int dpsSeen, int dpsPvpBlocked, bool transcendent) =>
        stage switch
        {
            NoActStage.Disabled => NoActReason.Disabled,
            NoActStage.Unavailable => NoActReason.Unavailable,
            NoActStage.Dead => NoActReason.Dead,
            NoActStage.Occupied => NoActReason.Occupied,
            NoActStage.Mounted => NoActReason.Mounted,
            NoActStage.NotInCombat => NoActReason.NotInCombat,
            NoActStage.Busy or NoActStage.Healing => NoActReason.Acting,
            NoActStage.Processed =>
                ClassifyProcessed(fired, stOutcome, dpsSeen, dpsPvpBlocked, transcendent),
            _ => NoActReason.None,
        };

    private static NoActReason ClassifyProcessed(bool fired, NoActStOutcome stOutcome,
        int dpsSeen, int dpsPvpBlocked, bool transcendent)
    {
        if (fired) return NoActReason.Acting;
        if (dpsSeen <= 0) return NoActReason.NoAutoPresets;
        if (transcendent) return NoActReason.Transcendent;
        if (dpsPvpBlocked >= dpsSeen) return NoActReason.PvpBlocked;

        // 冷卻中、GCD 轉動中、忍者結印鎖定等是輪轉中的常態，不算不出招。
        return stOutcome switch
        {
            NoActStOutcome.StGate => NoActReason.StGate,
            NoActStOutcome.NotFateTarget => NoActReason.NotFateTarget,
            NoActStOutcome.NoTarget => NoActReason.NoTarget,
            NoActStOutcome.TargetNotAttackable => NoActReason.TargetNotAttackable,
            NoActStOutcome.OutOfRange => NoActReason.OutOfRange,
            NoActStOutcome.MovingCaster => NoActReason.MovingCaster,
            _ => NoActReason.Acting,
        };
    }

    /// <summary> 沒有敵對目標、自己也不在戰鬥中時，本來就不需要出招 ⇒ 不當成「不出招」。 </summary>
    /// <remarks> 「目標不是 FATE 怪」除外：自動選目標時使用者手上常沒有目標，但自動循環確實選了目標要出招。 </remarks>
    internal static NoActReason ApplyRelevance(NoActReason raw, bool hasHostileTarget,
        bool playerInCombat) =>
        IsComplaint(raw) && raw != NoActReason.NotFateTarget && !hasHostileTarget && !playerInCombat
            ? NoActReason.Idle
            : raw;

    internal static bool IsComplaint(NoActReason reason) =>
        !IsQuiet(reason) && reason != NoActReason.Unavailable;

    internal static bool IsQuiet(NoActReason reason) =>
        reason is NoActReason.None or NoActReason.Acting or NoActReason.Idle
            or NoActReason.Disabled;

    /// <summary> 順序與 NotInCombat／CombatBypass 的組成一致：先看設定，再看誰在戰鬥，最後看繞過。 </summary>
    internal static NoActGateSource ClassifyGateSource(bool inCombatOnly,
        bool engageDelayBlocking, bool selfFlag, bool playerInCombat, bool slotMemberFlag,
        bool nonSlotMemberFlag, bool outOfPartyNpcFlag, bool fateBypass, bool questBypass)
    {
        if (!inCombatOnly) return NoActGateSource.InCombatOnlyOff;
        if (!engageDelayBlocking)
        {
            if (selfFlag)
                return playerInCombat
                    ? NoActGateSource.PlayerInCombat
                    : NoActGateSource.SelfFlagOnly;
            if (slotMemberFlag) return NoActGateSource.PartySlotMember;
            if (nonSlotMemberFlag) return NoActGateSource.NonSlotMember;
            if (outOfPartyNpcFlag) return NoActGateSource.OutOfPartyNpc;
        }

        if (fateBypass) return NoActGateSource.FateBypass;
        if (questBypass) return NoActGateSource.QuestBypass;
        return NoActGateSource.Unknown;
    }

    /// <summary> 使用者沒有設定、也不是自己或隊友在戰鬥卻放行＝會替使用者開打，要留紀錄。 </summary>
    internal static bool IsSuspiciousGateSource(NoActGateSource source) =>
        source is NoActGateSource.Unknown or NoActGateSource.SelfFlagOnly
            or NoActGateSource.NonSlotMember or NoActGateSource.OutOfPartyNpc;

    /// <summary> 離線測試入口：以全新的節流器依序餵入，回傳「時間:原因[+略過數]」以逗號串接。 </summary>
    internal static string SimulateLog(long[] times, NoActReason[] reasons)
    {
        if (times.Length != reasons.Length)
            throw new ArgumentException("times 與 reasons 長度不同");

        var gate = new NoActLogGate();
        var sb = new StringBuilder();
        for (var i = 0; i < times.Length; i++)
        {
            if (!gate.Offer(times[i], reasons[i], out var emitted, out var skipped))
                continue;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(times[i]).Append(':').Append(emitted);
            if (skipped > 0) sb.Append('+').Append(skipped);
        }

        return sb.ToString();
    }

    internal static string Describe(NoActReason reason) => reason switch
    {
        NoActReason.Acting => "恢復正常出招",
        NoActReason.Idle => "已不需出招（沒有敵對目標，自己也不在戰鬥中）",
        NoActReason.Disabled => "自動循環已關閉",
        NoActReason.Dead => "角色已無法戰鬥",
        NoActReason.Occupied => "角色忙碌中（對話、過場、交易、製作等）",
        NoActReason.Mounted => "騎乘中（自動循環在坐騎上整段跳過）",
        NoActReason.NotInCombat => "不在戰鬥中，「僅於戰鬥中」限制未被繞過",
        NoActReason.NoAutoPresets => "目前職業沒有任何開啟自動模式的攻擊連擊",
        NoActReason.Transcendent => "「生還」狀態中（出招會解除無敵，暫不出招）",
        NoActReason.PvpBlocked => "技能在此區域無法使用（PvP 區域）",
        NoActReason.StGate => "單體技能的戰鬥判定未放行",
        NoActReason.NoTarget => "沒有目標",
        NoActReason.TargetNotAttackable => "目標無法攻擊",
        NoActReason.OutOfRange => "目標超出射程或不在視線內",
        NoActReason.MovingCaster => "移動中，詠唱技能無法施放",
        NoActReason.NotFateTarget => "目標不是 FATE 魔物（FATE 目標繞過只對 FATE 魔物開打）",
        _ => reason.ToString(),
    };

    internal static string DescribeGateSource(NoActGateSource source) => source switch
    {
        NoActGateSource.InCombatOnlyOff => "未開「僅於戰鬥中」",
        NoActGateSource.PlayerInCombat => "自己在戰鬥中",
        NoActGateSource.SelfFlagOnly => "自己的角色資料標記為戰鬥中，但遊戲的戰鬥狀態未開",
        NoActGateSource.PartySlotMember => "隊伍欄位裡的隊友在戰鬥中",
        NoActGateSource.NonSlotMember => "隊伍名單裡有已不在隊伍欄位的角色在戰鬥中（可能是前隊友）",
        NoActGateSource.OutOfPartyNpc => "隊伍名單裡的隊外 NPC 在戰鬥中",
        NoActGateSource.FateBypass => "FATE 目標繞過",
        NoActGateSource.QuestBypass => "任務目標繞過",
        _ => "找不到放行依據",
    };
}

/// <summary> 「不出招」log 的節流器：原因穩定才寫、只在換類時寫、整體有頻率上限。 </summary>
/// <remarks>
///     原因要連續維持 <see cref="DebounceMs" /> 才算數；正常出招、閒置、關閉之間互換不寫。
///     頻率上限是權杖桶：最多連寫 <see cref="Burst" /> 行，之後每 <see cref="RefillMs" /> 補一行
///     ⇒ 每小時最多約 126 行。被上限擋掉、到被別的穩定狀態取代都沒寫出的次數，附在下一行。
/// </remarks>
internal struct NoActLogGate
{
    public const long DebounceMs = 3000;
    public const int Burst = 6;
    public const long RefillMs = 30_000;

    private bool _primed;
    private NoActReason _candidate;
    private long _candidateSince;
    private NoActReason _stable;
    private bool _stablePending;
    private NoActReason _reported;
    private int _skipped;
    private double _tokens;
    private long _refilledAt;

    /// <returns> 這一刻要不要寫一行；要寫時 <paramref name="emitted" /> 是該寫的原因。 </returns>
    public bool Offer(long now, NoActReason reason, out NoActReason emitted, out int skipped)
    {
        emitted = NoActReason.None;
        skipped = 0;
        if (reason == NoActReason.Unavailable) return false;

        if (!_primed)
        {
            _primed = true;
            _tokens = Burst;
            _refilledAt = now;
        }

        if (reason != _candidate)
        {
            _candidate = reason;
            _candidateSince = now;
        }

        if (now - _candidateSince < DebounceMs) return false;

        // 新的穩定狀態開始時，上一個穩定狀態若一直沒寫出，就算一次略過。
        if (_candidate != _stable)
        {
            if (_stablePending) _skipped++;
            _stable = _candidate;
            _stablePending = false;
        }

        if (_candidate == _reported) return false;
        if (NoActDiagnostics.IsQuiet(_candidate) && NoActDiagnostics.IsQuiet(_reported))
        {
            _reported = _candidate;
            return false;
        }

        if (now > _refilledAt)
        {
            _tokens = Math.Min(Burst, _tokens + (now - _refilledAt) / (double)RefillMs);
            _refilledAt = now;
        }

        if (_tokens < 1)
        {
            _stablePending = true;
            return false;
        }

        _tokens -= 1;
        _stablePending = false;
        skipped = _skipped;
        _skipped = 0;
        _reported = _candidate;
        emitted = _candidate;
        return true;
    }
}
