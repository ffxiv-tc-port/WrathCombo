using System;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using WrathCombo.CustomComboNS;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Data;

namespace WrathCombo.Extensions;

public static class GameObjectExtensions
{
    /// <summary>
    ///     Converts a GameObject pointer to an IGameObject from the object table.
    /// </summary>
    /// <param name="ptr">The GameObject pointer to convert.</param>
    /// <returns>An IGameObject if found in the object table; otherwise, null.</returns>
    public static unsafe IGameObject? GetObjectFrom(GameObject* ptr)
    {
        if (ptr == null)
            return null;

        var address = (IntPtr)ptr;
        foreach (var obj in Svc.Objects)
            if (obj.Address == address)
                return obj;

        return null;
    }

    /// <summary>
    ///     物件表裡第一個 <c>GameObjectId</c> 等於 <paramref name="gameObjectId" /> 的物件；找不到回 <see langword="null" />。
    /// </summary>
    /// <remarks>
    ///     走訪順序與物件表的列舉相同。回的是物件表的共用包裝：只在這一幀用，不要存。
    /// </remarks>
    internal static IGameObject? FindObjectById(ulong gameObjectId)
    {
        foreach (var obj in Svc.Objects)
            if (obj.GameObjectId == gameObjectId)
                return obj;

        return null;
    }

    #region Target Classification

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not friendly.
    /// </summary>
    /// <remarks>
    ///     See <see cref="SimpleTarget.Stack.AllyToHeal"/> for a use case.
    /// </remarks>
    public static IGameObject? IfFriendly (this IGameObject? obj) =>
        obj != null && CustomComboFunctions.TargetIsFriendly(obj) ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not in the player's party.
    /// </summary>
    public static IGameObject? IfInParty (this IGameObject? obj) =>
        obj != null &&
        CustomComboFunctions.GetPartyMembers()
            .Any(x => x.GameObjectId == obj.GameObjectId) ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not hostile.
    /// </summary>
    public static IGameObject? IfHostile (this IGameObject? obj) =>
        obj != null && obj.IsHostile() ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not a boss.
    /// </summary>
    public static IGameObject? IfBoss (this IGameObject? obj) =>
        obj != null && CustomComboFunctions.TargetIsBoss(obj) ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not a quest mob
    /// </summary>
    public static IGameObject? IfQuestMob (this IGameObject? obj) =>
        obj != null && CustomComboFunctions.IsQuestMob(obj) ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target does not need positionals.
    /// </summary>
    public static IGameObject? IfNeedsPositionals (this IGameObject? obj) =>
        obj != null && CustomComboFunctions.TargetNeedsPositionals(obj) ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is the player.
    /// </summary>
    public static IGameObject? IfNotThePlayer (this IGameObject? obj) =>
        obj != null && obj.GameObjectId != Player.Object.GameObjectId ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not within range.
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="range">The range to check against. Defaults to 25 yalms.</param>
    public static IGameObject? IfWithinRange
        (this IGameObject? obj, float range = 25) =>
        obj != null && CustomComboFunctions.IsInRange(obj, range) ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not below 99% HP.
    /// </summary>
    public static IGameObject? IfMissingHP (this IGameObject? obj) =>
        obj is IBattleChara battle &&
        CustomComboFunctions.GetTargetHPPercent(battle) < 99
            ? obj
            : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not invulnerable/invincible.
    /// </summary>
    public static IGameObject? IfNotInvincible (this IGameObject? obj) =>
        obj != null && !CustomComboFunctions.TargetIsInvincible(obj) ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target does not have a cleansable
    ///     debuff.
    /// </summary>
    public static IGameObject? IfHasCleansable (this IGameObject? obj) =>
        obj != null && CustomComboFunctions.HasCleansableDebuff(obj) ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is dead.
    /// </summary>
    public static IGameObject? IfAlive (this IGameObject? obj) =>
        obj != null && !obj.IsDead ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not dead enough.
    /// </summary>
    /// <seealso cref="IsDeadEnoughToRaise"/>
    public static IGameObject? IfDead (this IGameObject? obj) =>
        obj != null && IsDeadEnoughToRaise(obj) ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not targetable.
    /// </summary>
    public static IGameObject? IfTargetable (this IGameObject? obj) =>
        obj != null && obj.IsTargetable ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not a real player.
    /// </summary>
    public static IGameObject? IfAPlayer (this IGameObject? obj) =>
        obj != null && obj is IPlayerCharacter ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target is not still loaded in the player's
    ///     game.
    /// </summary>
    public static IGameObject? IfStillAround (this IGameObject? obj) =>
        obj != null &&
        Svc.Objects
            .Any(x => x.GameObjectId == obj.GameObjectId) ? obj : null;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it return
    ///     <see langword="null" /> if the target cannot be affected by the action.
    /// </summary>
    public static unsafe IGameObject? IfCanUseOn(this IGameObject? obj, uint actionId) =>
        obj != null && ActionManager.CanUseActionOnTarget(actionId, obj.Struct()) ? obj : null;

    #endregion

    #region Target Checking (same as above, but returns a boolean)

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the target is friendly.
    /// </summary>
    public static bool IsFriendly(this IGameObject? obj) =>
        obj != null && CustomComboFunctions.TargetIsFriendly(obj);

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the target is in the player's party.
    /// </summary>
    public static bool IsInParty(this IGameObject? obj) =>
        obj != null &&
        CustomComboFunctions.GetPartyMembers()
            .Any(x => x.GameObjectId == obj.GameObjectId);

    // `IsHostile` already exists, and works the exact same as we would write here

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the target is a boss.
    /// </summary>
    public static bool IsBoss(this IGameObject? obj) =>
        obj != null && CustomComboFunctions.TargetIsBoss(obj);

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the target is a quest mob.
    /// </summary>
    public static bool IsQuestMob(this IGameObject? obj) =>
        obj != null && CustomComboFunctions.IsQuestMob(obj);

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the target needs positionals.
    /// </summary>
    public static bool NeedsPositionals(this IGameObject? obj) =>
        obj != null && CustomComboFunctions.TargetNeedsPositionals(obj);

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the object is the player.
    /// </summary>
    public static bool IsNotThePlayer(this IGameObject? obj) =>
        obj != null && obj.GameObjectId != Player.Object.GameObjectId;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the target is within range.
    /// </summary>
    public static bool IsWithinRange(this IGameObject? obj, float range = 25) =>
        obj != null && CustomComboFunctions.IsInRange(obj, range);

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the target is below 99% HP.
    /// </summary>
    public static bool IsMissingHP(this IGameObject? obj) =>
        obj is IBattleChara battle && battle.CurrentHp / battle.MaxHp * 100 < 99;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the object is not invulnerable/invincible.
    /// </summary>
    public static bool IsNotInvincible(this IGameObject? obj) =>
        obj != null && !CustomComboFunctions.TargetIsInvincible(obj);

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the object has a cleansable debuff.
    /// </summary>
    public static bool IsCleansable(this IGameObject? obj) =>
        obj != null && CustomComboFunctions.HasCleansableDebuff(obj);

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the object is dead enough.
    /// </summary>
    /// <seealso cref="IsDeadEnoughToRaise"/>
    public static bool IsDead(this IGameObject? obj) =>
        obj != null && IsDeadEnoughToRaise(obj);

    /// <summary>
    ///     同 <see cref="IsDead" />，但「無法復活」的死者也算（挑活著的隊友、判「還有沒有人活著」用）。
    /// </summary>
    internal static bool IsDeadEvenIfUnraisable(this IGameObject? obj) =>
        obj != null && IsDeadEnoughToRaise(obj, true);

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the object is a player.
    /// </summary>
    public static bool IsAPlayer(this IGameObject? obj) =>
        obj != null && obj is IPlayerCharacter;

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the object is still loaded in the player's game.
    /// </summary>
    public static bool IsStillAround(this IGameObject? obj) =>
        obj != null &&
        Svc.Objects
            .Any(x => x.GameObjectId == obj.GameObjectId);

    /// <summary>
    ///     Can be chained onto a <see cref="IGameObject" /> to make it a quick
    ///     boolean check for if the object can be affected by the action.
    /// </summary>
    public static unsafe bool CanUseOn(this IGameObject? obj, uint actionId) =>
        obj != null && ActionManager.CanUseActionOnTarget(actionId, obj.Struct());

    #endregion

    /// <summary>
    ///     Checks if the object is dead, and should be raised.<br />
    ///     Checks for them being dead but targetable, not having Transcendence or
    ///     a Raise, and having been dead for more than 2 seconds.
    /// </summary>
    private static bool IsDeadEnoughToRaise(this IGameObject? obj, bool includeUnraisable = false)
    {
        return obj.IsDead &&
               obj.IsAPlayer() &&
               !RaiseStatuses.HasAny(obj, RaiseStatuses.Transcendent) &&
               !RaiseStatuses.HasAny(obj, RaiseStatuses.Raised) &&
               (includeUnraisable || !RaiseStatuses.HasAny(obj, RaiseStatuses.Unraisable)) &&
               obj.IsTargetable &&
               (CustomComboFunctions.TimeSpentDead(obj.GameObjectId)
                   .TotalSeconds > 2 || !obj.IsInParty());
    }
}
