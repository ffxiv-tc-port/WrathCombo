#region

using System;
using Dalamud.Plugin;
using ECommons;
using ECommons.EzIpcManager;
using ECommons.Reflection;

#endregion

namespace WrathCombo.Services.IPC_Subscriber;

public abstract class ReusableIPC : IDisposable
{
    private IDalamudPlugin? _plugin;
    public EzIPCDisposalToken[] DisposalTokens;
    public string PluginName;
    protected bool ReflectionNotIPC;
    public Version ValidVersion;

    protected ReusableIPC
    (string? pluginName,
        Version? validVersion = null,
        bool reflectionNotIPC = false)
    {
        if (string.IsNullOrWhiteSpace(pluginName))
            throw new ArgumentException("Plugin name cannot be null or empty.",
                nameof(pluginName));

        PluginName = pluginName;
        ValidVersion = validVersion ?? new Version(0, 0, 0, 0);
        ReflectionNotIPC = reflectionNotIPC;
        DisposalTokens = ReflectionNotIPC ? [] : EzIPC.Init(this, PluginName);
    }

    /// <summary>
    ///     對方外掛有沒有裝(而且版本夠新)。
    /// </summary>
    /// <remarks>
    ///         <b>「沒裝」必須確定回 <c>false</c>。</b>先問「載入了沒」,而不是拿版本號去反推有沒有裝。
    ///         反射查詢刻意只做一次:這個屬性在逐幀路徑上被呼叫
    ///         (見 <c>ConflictingPluginsChecks</c> 那則「一定要快取」的註解)。
    /// </remarks>
    public bool IsEnabled
    {
        get
        {
            // 沒裝／沒載入 = 確定 false,不讓哨兵版本號矇混過關。
            if (!DalamudReflector.TryGetDalamudPlugin(
                    PluginName, out var plugin, ignoreCache: true))
                return false;

            var installed = plugin.GetType().Assembly.GetName().Version
                            ?? new Version(0, 0, 0, 1);

            return installed >= ValidVersion || // release version
                   installed == new Version(0, 0, 0, 0); // debug ver for some plugins
        }
    }

    protected bool PluginIsLoaded =>
        DalamudReflector.TryGetDalamudPlugin(
            PluginName, out _plugin, ignoreCache: true);

    protected IDalamudPlugin Plugin
    {
        get
        {
            if (PluginIsLoaded)
                return _plugin!;
            throw new InvalidOperationException(
                "Plugin is not loaded or does not exist. " +
                "(This should be used after a `PluginIsLoaded` check)");
        }
    }

    public Version InstalledVersion =>
        DalamudReflector.TryGetDalamudPlugin(PluginName, out var plugin,
            ignoreCache: true)
            ? plugin.GetType().Assembly.GetName().Version ?? new Version(0, 0, 0, 1)
            : new Version(0, 0, 0, 1); // no version found

    public virtual void Dispose()
    {
        foreach (var token in DisposalTokens)
            try
            {
                token.Dispose();
            }
            catch (Exception ex)
            {
                ex.Log();
            }
    }
}