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
    ///     <para>
    ///         🔴 <b>「沒裝」必須確定回 <c>false</c>。</b>原本的寫法是
    ///         <c>InstalledVersion &gt;= ValidVersion || InstalledVersion == new Version(0,0,0,0)</c>,
    ///         但 <see cref="InstalledVersion" /> 在外掛<b>沒裝／沒載入</b>時回的是
    ///         <c>0.0.0.1</c> —— 那是「查不到版本」的哨兵值,跟「裝了但讀不出組件版本」
    ///         共用同一個回傳值,單看它分不出是哪一種。
    ///         於是只要 <see cref="ValidVersion" /> 是預設的 <c>0.0.0.0</c>
    ///         (建構子第二個參數可以省略),<c>0.0.0.1 &gt;= 0.0.0.0</c> 成立
    ///         ⇒ 對方外掛根本沒裝也回 <c>true</c>,後續的 IPC／反射呼叫全部踩空。
    ///     </para>
    ///     <para>
    ///         📌 目前每個消費端(BossMod／ReAction／MOAction／Redirect)都明確傳了非零的
    ///         validVersion 才躲過這條,所以這是<b>潛伏的未爆彈</b>而不是已發作的 bug ——
    ///         下一個省略參數的呼叫端就會踩到,而且失敗形式是靜默的。
    ///     </para>
    ///     <para>
    ///         🔑 正解是先問「載入了沒」,而不是拿版本號去反推有沒有裝。
    ///         已裝的情境行為完全不變:載入成功時走的仍是原本那兩條版本判斷。
    ///     </para>
    ///     <para>
    ///         ⚠️ 反射查詢刻意只做一次。這個屬性在逐幀路徑上被呼叫
    ///         (見 <c>ConflictingPluginsChecks</c> 那則「一定要快取」的註解),
    ///         原本兩個子運算式各查一次、最多兩次;改後固定一次,不會比原本貴。
    ///     </para>
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