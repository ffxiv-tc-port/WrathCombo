#region

using System;
using System.Linq;

#endregion

namespace WrathCombo.Services.IPC;

public partial class Leasing
{
    /// <summary>
    ///     Checks if Auto-Rotation's state is controlled by a lease.
    /// </summary>
    /// <param name="option">
    ///     The Auto-Rotation configuration option to check.
    /// </param>
    /// <returns>
    ///     The state the Auto-Rotation configuration is controlled to, or
    ///     <c>null</c> if it is not.
    /// </returns>
    /// <seealso cref="Provider.GetAutoRotationConfigState" />
    internal int? CheckAutoRotationConfigControlled
        (AutoRotationConfigOption option)
    {
        lock (_gate)
        {
            var lease = _registrations.Values
                .Where(l => l.AutoRotationConfigsControlled.ContainsKey(option))
                .OrderByDescending(l => l.LastUpdated)
                .FirstOrDefault();

            return lease?.AutoRotationConfigsControlled[option];
        }
    }

    /// <summary>
    ///     Adds a registration for Auto-Rotation Config control to a lease.
    /// </summary>
    /// <param name="lease">
    ///     Your lease ID from <see cref="Provider.RegisterForLease(string,string)" />
    /// </param>
    /// <param name="option">The Auto-Rotation option to set.</param>
    /// <param name="value">The type-juggled value to set it to.</param>
    /// <seealso cref="Provider.SetAutoRotationConfigState" />
    internal SetResult AddRegistrationForAutoRotationConfig
        (Guid lease, AutoRotationConfigOption option, int value)
    {
        string pluginName;
        lock (_gate)
        {
            var registration = _registrations[lease];
            pluginName = registration.PluginName;

            if (registration.AutoRotationConfigsControlled
                    .TryGetValue(option, out var existing) &&
                existing == value)
                return SetResult.Duplicate;

            registration.AutoRotationConfigsControlled[option] = value;

            registration.LastUpdated = DateTime.Now;
            AutoRotationConfigsUpdated = DateTime.Now;
        }

        // 記錄留到鎖外印：Logging 會建一個 StackTrace，不該握著鎖跑。
        Logging.Log($"{pluginName}: Registered Auto-Rotation Config ({option} to {value})");
        return SetResult.Okay;
    }
}

public partial class Helper
{
    /// <summary>
    ///     Determine the type the value for a given option should be.
    /// </summary>
    /// <param name="option">The option to check the value type for.</param>
    /// <returns>
    ///     The type as defined in <see cref="AutoRotationConfigOption" /> in the
    ///     <see cref="ConfigValueTypeAttribute" />.
    /// </returns>
    public static Type GetAutoRotationConfigType(AutoRotationConfigOption option)
    {
        var type = option.GetType()
            .GetField(option.ToString())!
            .GetCustomAttributes(typeof(ConfigValueTypeAttribute), false)
            .Cast<ConfigValueTypeAttribute>()
            .First().ValueType;

        return type;
    }
}
