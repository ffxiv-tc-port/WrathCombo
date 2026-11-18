#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.Logging;
using WrathCombo.Combos.PvE;
using WrathCombo.CustomComboNS;
using WrathCombo.Extensions;
using WrathCombo.Services;
using EZ = ECommons.Throttlers.EzThrottler;
using TS = System.TimeSpan;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedParameter.Global

#endregion

namespace WrathCombo.Core;

/// <summary>
///     Action Retargeting capabilities, to remove any dependence on Redirect or
///     Reaction.<br /><br />
///     See
///     <see cref="UIntExtensions.Retarget(uint,IGameObject?,bool)">
///         Retarget(uint,IGameObject)
///     </see>
///     for simple Feature Retargeting.
///     <br />
///     See
///     <see cref="UIntExtensions.Retarget(uint,uint,Func{IGameObject?},bool)">
///         Retarget(uint,uint,TargetResolverDelegate)
///     </see>
///     for more advanced Retargeting in a full combo.
///     <br />
///     See <see cref="TargetResolverAttribute" /> for an example on setting up more
///     advanced Retargeting.
/// </summary>
public class ActionRetargeting : IDisposable
{
    /// List of Retargets for actions, keyed with the action and replaced action(s).
    internal readonly Dictionary<uint, Retargeting> Retargets = [];

    /// <summary>
    ///     Register an action and its replaced actions as one you want Retargeted.
    /// </summary>
    /// <param name="action">The Action to retarget.</param>
    /// <param name="replacedActions">The Actions the combo is replacing</param>
    /// <param name="resolver">
    ///     The <see cref="TargetResolverAttribute">TargetResolver</see> to resolve
    ///     the target.<br />
    ///     Examples:
    ///     a <see cref="SimpleTarget">SimpleTarget</see> (like
    ///     <see cref="SimpleTarget.HardTarget">HardTarget</see>)
    ///     or
    ///     a <see cref="TargetResolverAttribute">custom method</see> (like:
    ///     <see cref="AST.CardResolver">AST.CardsResolver</see>)
    /// </param>
    /// <param name="dontCull">
    ///     Whether this method should be exempt from periodic culls.<br />
    ///     <see cref="Retargeting" /> params for further explanation.
    /// </param>
    /// <returns>
    ///     The <paramref name="action" /> that was registered.<br />
    ///     This only really returns to make
    ///     <see cref="UIntExtensions.Retarget(uint,Func{IGameObject?},bool)">
    ///         (uint).Retarget()
    ///     </see>
    ///     simpler.
    /// </returns>
    /// <remarks>
    ///     Should only be called by
    ///     <see cref="UIntExtensions.Retarget(uint,Func{IGameObject?},bool)">
    ///         (uint).Retarget()
    ///     </see>
    ///     .
    /// </remarks>
    internal uint Register
    (uint action,
        uint[] replacedActions,
        Func<IGameObject?> resolver,
        bool dontCull = false) =>
        // Make sure the action is not in replaced actions,
        // and there are no duplicates
        RegisterDistinct(action, DistinctCopy(replacedActions), resolver, dontCull);

    /// <summary> 只有一個被取代技能時的 <see cref="Register(uint,uint[],Func{IGameObject?},bool)" />，免得呼叫端先配一個單元素陣列。 </summary>
    internal uint Register
    (uint action,
        uint replacedAction,
        Func<IGameObject?> resolver,
        bool dontCull = false) =>
        RegisterDistinct(action, [replacedAction], resolver, dontCull);

    /// <summary> 等同 <c>source.Distinct().ToArray()</c>：首次出現的順序、一定是新陣列；短陣列不經 HashSet。 </summary>
    internal static uint[] DistinctCopy(uint[] source)
    {
        // 空陣列與長陣列照舊走 LINQ：空的時候 ToArray 回不回共用的空陣列，會影響下面用參考比對的覆寫判斷。
        if (source.Length == 0 || source.Length > 16)
            return source.Distinct().ToArray();

        var count = 0;
        for (var i = 0; i < source.Length; i++)
            if (Array.IndexOf(source, source[i], 0, i) < 0)
                count++;

        var result = new uint[count];
        var next = 0;
        for (var i = 0; i < source.Length; i++)
            if (Array.IndexOf(source, source[i], 0, i) < 0)
                result[next++] = source[i];
        return result;
    }

    /// <param name="replacedActions"> 已去重、呼叫端不再持有的新陣列（<see cref="Retargeting.ID" /> 與覆寫判斷都看它的參考）。 </param>
    private uint RegisterDistinct
    (uint action,
        uint[] replacedActions,
        Func<IGameObject?> resolver,
        bool dontCull)
    {
        // Build the Retarget object
        var retarget = new Retargeting(action, replacedActions, resolver, dontCull);

        //// Limit spam from the same actionID, mostly for debugging
        //if (!EZ.Throttle($"retargetFor{retarget.ID}", TS.FromSeconds(1)))
        //    return action;

        #region Replace existing Retargets

        var partialOverwrite = false;
        var overwrites = false;
        string overwritingOld = "", overwritingNew = "";
        Retargeting? oldRetarget = null;
        // 走訪 replacedActions 再加上 action（原本是 Concat）；oldRetarget 每一輪都被 TryGetValue 覆寫，留下的是最後一個鍵的結果。
        for (var i = 0; i <= replacedActions.Length; i++)
        {
            var replacedAction = i < replacedActions.Length ? replacedActions[i] : action;
            if (!Retargets.TryGetValue(replacedAction, out oldRetarget))
                continue;

            // Flag as a partial overwrite if `actionInReplaced` has different values
            if (replacedActions != oldRetarget.ReplacedActions)
                partialOverwrite = true;

            overwrites = true;
            overwritingOld = oldRetarget.ResolverName;
            overwritingNew = retarget.ResolverName;
        }
       
        // Remove the old Retarget
        if (overwrites)
        {
            RemoveRetarget(oldRetarget?.ID ?? 0);

            // Elevate to a debug message if the overwriting method is not equivalent
            var logLevel = overwritingOld == overwritingNew ? 0 : 1;
#if DEBUG
            // Elevate to a warning if not equivalent and on a debug build
            logLevel = logLevel == 1 ? 2 : logLevel;
#endif
            if (!partialOverwrite)
                log("overwriting Retargeting for", logLevel: logLevel,
                    showAction: true,
                    retarget: retarget,
                    messageAfterAction: $"{overwritingOld} -> {overwritingNew}");
        }

        #endregion

        // Save the Retarget
        var throttleTimespan = TS.FromSeconds(partialOverwrite ? 20 : 1);
        if (EZ.Throttle(RetargetForKey(retarget.Action), throttleTimespan))
            log("registering", showAction: true,
                messageAfterAction: "for Retargeting",
                showResolver: true, retarget: retarget);
        AddRetarget(retarget);
        return action;
    }

    // 節流鍵按技能記住，免得每次註冊都組一次字串；只在框架執行緒用（同 EzThrottler）。
    private static readonly Dictionary<uint, string> RetargetForKeys = [];

    private static string RetargetForKey(uint action)
    {
        if (!RetargetForKeys.TryGetValue(action, out var key))
            RetargetForKeys[action] = key = $"retargetFor{action}";
        return key;
    }

    /// <summary>
    ///     Resolve the target for an action via the provided
    ///     <see cref="TargetResolverAttribute">TargetResolver</see>.
    /// </summary>
    /// <param name="action">The Action or Replaced Action to Retarget.</param>
    /// <param name="target">
    ///     The output  <see cref="IGameObject">Game Object</see> of the target, if
    ///     the action was found to be Retargeted.
    /// </param>
    /// <param name="replacedWith"></param>
    /// <returns>
    ///     Whether the action is registered for Retargeting.
    /// </returns>
    public bool TryGetTargetFor(uint action, out IGameObject? target, out uint replacedWith)
    {
        target = null;
        replacedWith = action;
        // Find the Retarget object
        if (!Retargets.TryGetValue(action, out var retarget) || 
            !Service.ActionReplacer.LastActionInvokeFor
            .TryGetValue(action, out var lastAction) ||
            lastAction != retarget.Action)
            return false;

        replacedWith = retarget.Action;
        log("Retargeting", showAction: true,
            showResolver: true, retarget: retarget);

        // Run the target resolver
        if (!retarget.DontCull)
            RemoveRetarget(retarget.ID);
        try
        {
            target = retarget.Resolver.Invoke();
        }
        catch (Exception ex)
        {
            log("error while resolving target for", logLevel: 3, showAction: true,
                showResolver: true, retarget: retarget,
                extraText: $":\n{ex}");
            return false;
        }

        // Return the results
        log("Retargeted", logLevel: 1, showAction: true,
            messageAfterAction: $"to '{target?.Name ?? "null"}'",
            showResolver: true, retarget: retarget);
        return target != null;
    }

    /// <summary>
    ///     A Retarget object, used to accurately store everything about a want to
    ///     Retarget an action.
    /// </summary>
    /// <param name="action">The action wanting to be Retargeted.</param>
    /// <param name="replacedActions">
    ///     The combo's replaced actions; what is actually being pressed on the hotbar
    ///     for the combo where the Retargeted action appears.
    /// </param>
    /// <param name="resolver">
    ///     The <see cref="TargetResolverAttribute">TargetResolver</see> that
    ///     resolves the target for the action.<br />
    ///     Can be a <see cref="SimpleTarget" />, but it gets wrapped in a delegate
    ///     in <see cref="UIntExtensions.Retarget(uint,IGameObject?,bool)" />, or its
    ///     two overloads.
    /// </param>
    /// <param name="dontCull">
    ///     Whether this Retarget should be exempt from periodic culls.<br />
    ///     Should only be set (to <see langword="true" />) for Features; combo
    ///     Retargets should always be marked for culling.
    /// </param>
    internal class Retargeting(
        uint action,
        uint[] replacedActions,
        Func<IGameObject?> resolver,
        bool dontCull = false)
    {
        /// A unique identifier for the Retarget, to help with overwrites and removal.
        public int ID { get; } = HashCode.Combine(action, replacedActions);

        /// The action we actually want to Retarget.
        public uint Action { get; } = action;

        /// The actions that could be what is actually pressed when we want to Retarget the action.
        public uint[] ReplacedActions { get; } = replacedActions;

        /// The target resolver that will decide the new target for the action.
        public Func<IGameObject?> Resolver { get; } = resolver;

        /// The name of the resolver method, to help with debugging.
        public string ResolverName { get; } = MethodNames.GetOrAdd(resolver.Method, GetMethodName);

        /// Whether this Retarget should be removed by periodic culls.
        /// <see cref="ClearOldRetargets" />
        public bool DontCull { get; } = dontCull;

        /// When this was created, to help with age-outs.
        public DateTime Created { get; } = DateTime.Now;

        /// <summary>
        ///     Gets the name for the resolver.
        /// </summary>
        /// <param name="method">The resolver's method we want the name of.</param>
        /// <returns>
        ///     The name of the resolver, whether determined from the class (for
        ///     <see cref="SimpleTarget">Simple Targets</see>) or the name of the
        ///     resolver itself.
        /// </returns>
        // 名稱只取決於方法本身；每次註冊都是新的委派，按方法記住就不必每次重組字串。
        private static readonly ConcurrentDictionary<MethodInfo, string> MethodNames = new();

        private static string GetMethodName(MethodInfo method)
        {
            var resolverName = method.Name;
            var resolverClass = method.DeclaringType?.Name ?? "";

            // Standardize the names, and if a custom resolver is used,
            // try to provide the custom name if one was provided
            resolverName = resolverClass switch
            {
                _ when resolverClass.Contains("<>c") ||
                       resolverClass.Contains("__") =>
                    "<DirectGameObject>",
                _ => resolverName,
            };

            return $"`{resolverName}`";
        }
    }

    #region Utilities

    /// <summary>
    ///     Method to resolve a target for an action using custom logic.<br />
    ///     Should return <c>IGameObject?</c>.
    /// </summary>
    /// <returns>
    ///     The <see langword="null" />able
    ///     <see cref="IGameObject">Game Object</see> of the target.
    /// </returns>
    /// <example>
    ///     <code>
    ///     [ActionRetargeting.TargetResolver]
    ///     public static IGameObject? ExampleTargetResolver () {
    ///         if (Config.Example_ShouldUseFocusTarget)
    ///             return SimpleTarget.FocusTarget;
    ///         return SimpleTarget.Self;
    ///     };
    ///     </code>
    /// </example>
    /// <seealso cref="AST.CardResolver">
    ///     AST.CardsResolver
    /// </seealso>
    /// <seealso cref="DNC.DancePartnerResolver">
    ///     DNC.DancePartnerResolver
    /// </seealso>
    [AttributeUsage(AttributeTargets.Method)]
    public class TargetResolverAttribute : Attribute { }

    /// <summary>
    ///     Prevents the action itself from being retargeted unless excepted,
    ///     which stops retargets from leaking from combos as much.<br />
    ///     Should also add to
    ///     <see cref="CustomCombo._presetsAllowedToReturnUnchanged" /> if the
    ///     action is one that FF changes.
    /// </summary>
    private static readonly uint[] _actionsAllowedToBeReplacedAction =
    [
        // Features that only Retarget themselves
        DNC.ClosedPosition,
        AST.EssentialDignity,
        DRK.BlackestNight,
        DRK.Oblation,
        GNB.HeartOfStone,
        GNB.HeartOfCorundum,
        PLD.Clemency,

        // Resurrection abilities from the "All" feature
        WHM.Raise,
        AST.Ascend,
        SGE.Egeiro,
        SCH.Resurrection,
        BLU.AngelWhisper,
        RDM.Verraise,
        SMN.Resurrection,
    ];

    /// <summary>
    ///     Adds a new Retarget to both lists of Retargets.
    /// </summary>
    /// <param name="retarget">The Retarget to add.</param>
    private void AddRetarget(Retargeting retarget)
    {
        // Only retarget the action as well as the replaced actions if excepted
        if (_actionsAllowedToBeReplacedAction.Contains(retarget.Action))
            Retargets[retarget.Action] = retarget;

        foreach (var replacedAction in retarget.ReplacedActions)
            Retargets[replacedAction] = retarget;
    }

    /// <summary>
    ///     Removes all Retargets for an action.
    /// </summary>
    /// <param name="retargetID">The ID of the Retarget object to remove.</param>
    private void RemoveRetarget(int retargetID)
    {
        Retargeting? retarget = null;
        foreach (var candidate in Retargets.Values)
            if (candidate.ID == retargetID)
            {
                retarget = candidate;
                break;
            }
        if (retarget == null)
            return;

        foreach (var replacedAction in retarget.ReplacedActions)
            Retargets.Remove(replacedAction);
        Retargets.Remove(retarget.Action);
    }

    /// Simple Flag to stop clearing the cache when the plugin is disposed.
    // ReSharper disable once RedundantDefaultMemberInitializer
    private bool CancelCacheClearing { get; set; } = false;

    /// Clears Retargets older than 20 seconds every 11 seconds.
    /// <seealso cref="Retargets" />
    internal Action ClearOldRetargets = () =>
    {
        try
        {
            // This doesn't even need set at Dispose() technically,
            // since it becomes un-reference-able once the plugin is unloaded.
            // Either way, it's an easy check, for sure.
            if (P.ActionRetargeting.CancelCacheClearing)
                return;
        }
        // When unloading the plugin, just kill the task
        catch (NullReferenceException)
        {
            return;
        }

        // Find old Retargets that are allowed to be culled
        var oldRetargets = P.ActionRetargeting.Retargets.Values
            .Where(x => !x.DontCull &&
                        (DateTime.Now - x.Created) > TS.FromSeconds(20))
            .ToList();

        // Cull each unique Retarget
        if (oldRetargets.Count > 0)
            foreach (var old in oldRetargets)
            {
                P.ActionRetargeting.RemoveRetarget(old.ID);
                // Make sure not to spam if the same retarget is culled more than once
                if (!EZ.Throttle($"retargetClr{old.ID}", TS.FromSeconds(3)))
                    log("cleared old Retarget for", showAction: true, retarget: old);
            }

        Svc.Framework.RunOnTick(P.ActionRetargeting.ClearOldRetargets,
            TS.FromSeconds(11), cancellationToken: UnloadToken);
    };

    /// Clears all cached Retargets.
    internal void ClearCachedRetargets()
    {
        Retargets.Clear();
        log("cleared cached Retargets", logLevel: 1);
    }

    public void Dispose()
    {
        Retargets.Clear();
        CancelCacheClearing = true;
    }

    private static void log(string message,
        string messageAfterAction = "",
        int logLevel = 0,
        bool showAction = false,
        bool showResolver = false,
        Retargeting? retarget = null,
        string extraText = "")
    {
        message = $"[ActionRetargeting] {message}";
        if (showAction)
            message += $" '{retarget?.Action.ActionName()}'";
        if (messageAfterAction != "")
            message += $" {messageAfterAction}";
        if (showResolver)
            message += $" (with {retarget?.ResolverName})";
        if (extraText != "")
            message += $"{extraText}";

        sendLog(message, logLevel);

        return;

        void sendLog(string msg, int ll)
        {
            switch (ll)
            {
                case 0:
                    PluginLog.Verbose(msg);
                    break;
                case 1:
                    PluginLog.Debug(msg);
                    break;
                case 2:
                    PluginLog.Warning(msg);
                    break;
                default:
                    PluginLog.Error(msg);
                    break;
            }
        }
    }

    #endregion
}

internal static class FuncIGameObjectExtensions
{
    // 方法上的屬性在執行期不變；每次註冊都讀一次反射會配置屬性陣列。
    private static readonly ConcurrentDictionary<MethodInfo, bool> HasTargetResolverAttr = new();

    /// <summary>
    ///     A simple extension to throw a warning if the target resolver does not
    ///     have the <see cref="ActionRetargeting.TargetResolverAttribute" />.
    /// </summary>
    public static Func<IGameObject?> CheckForAttribute
        (this Func<IGameObject?> target)
    {
        var hasTargetResolverAttr = HasTargetResolverAttr.GetOrAdd(target.Method, static method =>
            method.GetCustomAttributes(typeof(ActionRetargeting.TargetResolverAttribute), false).Length > 0);
        if (!hasTargetResolverAttr &&
            EZ.Throttle("retargetAttributeWarning", TS.FromSeconds(15)))
            PluginLog.Warning(
                "[ActionRetargeting] Custom Target Resolver provided, " +
                "but lacks the `TargetResolver` Attribute.\n" +
                "StackTrace:\n" + Environment.StackTrace);

        return target;
    }
}

internal static class UIntExtensions
{
    // One for "no replaced action specified" (simple features),
    // "one replaced action specified" (simple combos),
    // and "multiple replaced actions specified" (complex combos, like healers),
    // each accepting a direct target or a target resolver.

    /// <summary>
    ///     Retargets the action to the target specified.<br />
    ///     Only works if the <paramref name="action" /> is the Replaced Action
    ///     for the combo (i.e. Features, not generally main Combos).
    /// </summary>
    /// <param name="action">The action ID to Retarget.</param>
    /// <param name="target">
    ///     The target to Retarget the action onto.<br />
    ///     Should be a <see cref="SimpleTarget" /> property.
    /// </param>
    /// <param name="dontCull">
    ///     Whether this method should be exempt from periodic culls.<br />
    ///     See <see cref="ActionRetargeting.Retargeting" /> params for further
    ///     explanation.
    /// </param>
    /// <returns>The <paramref name="action" />.</returns>
    internal static uint Retarget
        (this uint action, IGameObject? target, bool dontCull = false) =>
        P.ActionRetargeting.Register(action, action, () => target, dontCull);

    /// <summary>
    ///     Retargets the action to the target specified.<br />
    ///     Only works if the <paramref name="action" /> is the Replaced Action
    ///     for the combo (i.e. Features, not generally main Combos).
    /// </summary>
    /// <param name="action">The action ID to Retarget.</param>
    /// <param name="target">
    ///     The
    ///     <see cref="ActionRetargeting.TargetResolverAttribute">
    ///         Target Resolver
    ///     </see> that provides the <see cref="IGameObject">target</see> you want.
    /// </param>
    /// <param name="dontCull">
    ///     Whether this method should be exempt from periodic culls.<br />
    ///     See <see cref="ActionRetargeting.Retargeting" /> params for further
    ///     explanation.
    /// </param>
    /// <returns>The <paramref name="action" />.</returns>
    internal static uint Retarget
        (this uint action, Func<IGameObject?> target, bool dontCull = false) =>
        P.ActionRetargeting.Register(action, action,
            target.CheckForAttribute(), dontCull);

    /// <summary>
    ///     Retargets the action to the target specified.
    /// </summary>
    /// <param name="action">The action ID to retarget.</param>
    /// <param name="replaced">The action ID of the combo's Replaced Action.</param>
    /// <param name="target">
    ///     The target to Retarget the action onto.<br />
    ///     Should be a <see cref="SimpleTarget" /> property.
    /// </param>
    /// <param name="dontCull">
    ///     Whether this method should be exempt from periodic culls.<br />
    ///     See <see cref="ActionRetargeting.Retargeting" /> params for further
    ///     explanation.
    /// </param>
    /// <returns>The <paramref name="action" />.</returns>
    /// <remarks>
    ///     Used when the <paramref name="action" /> is not the same as the
    ///     combo's Replaced Action (i.e. main Combos, not usually Features).
    /// </remarks>
    internal static uint Retarget
    (this uint action,
        uint replaced,
        IGameObject? target,
        bool dontCull = false) =>
        P.ActionRetargeting.Register(action, replaced, () => target, dontCull);

    /// <summary>
    ///     Retargets the action to the target specified.
    /// </summary>
    /// <param name="action">The action ID to retarget.</param>
    /// <param name="replaced">The action ID of the combo's Replaced Action.</param>
    /// <param name="target">
    ///     The target to Retarget the action onto.<br />
    ///     Should be a <see cref="SimpleTarget" /> property.
    /// </param>
    /// <param name="dontCull">
    ///     Whether this method should be exempt from periodic culls.<br />
    ///     See <see cref="ActionRetargeting.Retargeting" /> params for further
    ///     explanation.
    /// </param>
    /// <returns>The <paramref name="action" />.</returns>
    /// <remarks>
    ///     Used when the <paramref name="action" /> is not the same as the
    ///     combo's Replaced Action (i.e. main Combos, not usually Features).
    /// </remarks>
    internal static uint Retarget
    (this uint action,
        uint replaced,
        Func<IGameObject?>  target,
        bool dontCull = false) =>
        P.ActionRetargeting.Register(action, replaced,
            target.CheckForAttribute(), dontCull);

    /// <summary>
    ///     Retargets the action to the target specified.
    /// </summary>
    /// <param name="action">The action ID to retarget.</param>
    /// <param name="replaced">The action ID of the combo's Replaced Action.</param>
    /// <param name="target">
    ///     The target to Retarget the action onto.<br />
    ///     Should be a <see cref="SimpleTarget" /> property.
    /// </param>
    /// <param name="dontCull">
    ///     Whether this method should be exempt from periodic culls.<br />
    ///     See <see cref="ActionRetargeting.Retargeting" /> params for further
    ///     explanation.
    /// </param>
    /// <returns>The <paramref name="action" />.</returns>
    /// <remarks>
    ///     Used when the <paramref name="action" /> is not the same as the
    ///     combo's Replaced Actions, and there are multiple options for which
    ///     actions to replace (i.e. main combos on healers, not usually features).
    /// </remarks>
    internal static uint Retarget
    (this uint action,
        uint[] replaced,
        IGameObject? target,
        bool dontCull = false) =>
        P.ActionRetargeting.Register(action, replaced, () => target, dontCull);

    /// <summary>
    ///     Retargets the action to the target specified.
    /// </summary>
    /// <param name="action">The action ID to retarget.</param>
    /// <param name="replaced">The action ID of the combo's Replaced Action.</param>
    /// <param name="target">
    ///     The target to Retarget the action onto.<br />
    ///     Should be a <see cref="SimpleTarget" /> property.
    /// </param>
    /// <param name="dontCull">
    ///     Whether this method should be exempt from periodic culls.<br />
    ///     See <see cref="ActionRetargeting.Retargeting" /> params for further
    ///     explanation.
    /// </param>
    /// <returns>The <paramref name="action" />.</returns>
    /// <remarks>
    ///     Used when the <paramref name="action" /> is not the same as the
    ///     combo's Replaced Actions, and there are multiple options for which
    ///     actions to replace (i.e. main combos on healers, not usually features).
    /// </remarks>
    internal static uint Retarget
    (this uint action,
        uint[] replaced,
        Func<IGameObject?> target,
        bool dontCull = false) =>
        P.ActionRetargeting.Register(action, replaced,
            target.CheckForAttribute(), dontCull);
}
