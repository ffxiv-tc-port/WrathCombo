using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ECommons;
using ECommons.Automation.LegacyTaskManager;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Newtonsoft.Json.Linq;
using PunishLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ECommons.Logging;
using WrathCombo.Attributes;
using WrathCombo.AutoRotation;
using WrathCombo.Combos;
using WrathCombo.Core;
using WrathCombo.CustomComboNS;
using WrathCombo.CustomComboNS.Functions;
using WrathCombo.Data;
using WrathCombo.Services;
using WrathCombo.Services.ActionRequestIPC;
using WrathCombo.Services.IPC;
using WrathCombo.Window;
using WrathCombo.Window.Tabs;
using WrathCombo.Data.Conflicts;
using WrathCombo.Services.IPC_Subscriber;

namespace WrathCombo;

/// <summary> Main plugin implementation. </summary>
public sealed partial class WrathCombo : IDalamudPlugin
{
    internal static TaskManager? TM;
    internal readonly ConfigWindow ConfigWindow;
    private readonly MajorChangesWindow _majorChangesWindow;
    private readonly TargetHelper TargetHelper;
    internal static DateTime LastPresetDeconflictTime = DateTime.MinValue;
    internal static WrathCombo? P;

    /// <summary>
    ///     Dispose 時取消。外掛層級的 RunOnTick 等排程在外掛卸載時不會被取消，
    ///     帶延遲的排程要帶它，否則會在 Dispose（<c>P = null</c>）之後照跑。
    /// </summary>
    private static readonly CancellationTokenSource UnloadCts = new();

    internal static CancellationToken UnloadToken => UnloadCts.Token;

    private readonly WindowSystem ws;
    private readonly IDtrBarEntry DtrBarEntry;
    internal Provider IPC;
    internal Search IPCSearch = null!;
    internal UIHelper UIHelper = null!;
    internal ActionRetargeting ActionRetargeting = new();
    internal MovementHook MoveHook;

    private static uint? jobID;
    private static bool inInstancedContent;

    public static readonly List<uint> DisabledJobsPVE =
    [
        //ADV.JobID,
        //AST.JobID,
        //BLM.JobID,
        //BLU.JobID,
        //BRD.JobID,
        //DNC.JobID,
        //DOL.JobID,
        //DRG.JobID,
        //DRK.JobID,
        //GNB.JobID,
        //MCH.JobID,
        //MNK.JobID,
        //NIN.JobID,
        //PCT.JobID,
        //PLD.JobID,
        //RDM.JobID,
        //RPR.JobID,
        //SAM.JobID,
        //SCH.JobID,
        //SGE.JobID,
        //SMN.JobID,
        //VPR.JobID,
        //WAR.JobID,
        //WHM.JobID
    ];

    public static readonly List<uint> DisabledJobsPVP = [];

    public static uint? JobID
    {
        get => jobID;
        private set
        {
            if (jobID != value && value != null)
                UpdateCaches(jobID != null, false, jobID == null);
            jobID = value;
        }
    }

    public static void UpdateCaches
        (bool onJobChange, bool onTerritoryChange, bool firstRun)
    {
        // 換職業／換地圖時把別的外掛送進來的動作請求與封鎖清空：
        // 那些請求都是針對「當下那個情境」下的，換了情境再照著放會是錯的。
        // ⚠️ 這裡刻意在 TM 佇列**之外**同步執行，因為佇列會延遲 1 秒才跑。
        ActionRequestIPCProvider.ResetAllBlacklist();
        ActionRequestIPCProvider.ResetAllRequests();

        TM.DelayNext(1000);
        TM.Enqueue(() =>
        {
            if (!Player.Available)
                return false;

            WrathOpener.SelectOpener();
            P.ActionRetargeting.ClearCachedRetargets();
            if (onJobChange)
                PvEFeatures.OpenToCurrentJob(true);
            if (onJobChange || firstRun)
            {
                Service.ActionReplacer.UpdateFilteredCombos();
                Svc.Framework.RunOnTick(Provider.BuildCachesAction(),
                    cancellationToken: UnloadToken);
                P.IPCSearch.UpdateActiveJobPresets();
                P.IPC.Leasing.SuspendLeases(CancellationReason.JobChanged);
            }

            if (onTerritoryChange)
            {
                if (Service.Configuration.RotationConfig.EnableInInstance && Content.InstanceContentRow?.RowId > 0 && !inInstancedContent)
                {
                    Service.Configuration.RotationConfig.Enabled = true;
                    inInstancedContent = true;
                }

                if (Service.Configuration.RotationConfig.DisableAfterInstance && Content.InstanceContentRow?.RowId == 0 && inInstancedContent)
                {
                    Service.Configuration.RotationConfig.Enabled = false;
                    inInstancedContent = false;
                }
            }

            return true;
        }, "UpdateCaches");
    }

    /// <summary> Initializes a new instance of the <see cref="WrathCombo"/> class. </summary>
    /// <param name="pluginInterface"> Dalamud plugin interface. </param>
    public WrathCombo(IDalamudPluginInterface pluginInterface)
    {
        P = this;
        pluginInterface.Create<Service>();
        ECommonsMain.Init(pluginInterface, this, Module.All);
        // 讓「呼叫了對方沒有的 IPC 方法」不再完全靜默。
        // 訂閱越早越好：事件只在 IPC **呼叫**當下才被查閱，在這裡訂閱就涵蓋往後所有呼叫。
        EzIpcFailureLog.Enable();
        PunishLibMain.Init(pluginInterface, "Wrath Combo",
            new AboutPlugin { IconPath = LocalIconPath(pluginInterface) });
        ECommons.LanguageHelpers.Localization.Init("ChineseTraditional");
        // 動作請求／封鎖的 IPC 供應端（WrathCombo.ActionRequest.*）。
        // 與 Provider.Init() 分開註冊，因為它是 static 型別、前綴也不同。
        ActionRequestIPCProvider.Initialize();

        TM = new();
        RemoveNullAutos(); 
        Service.Configuration = pluginInterface.GetPluginConfig() as PluginConfiguration ?? new PluginConfiguration();
        Service.Address = new PluginAddressResolver();
        Service.Address.Setup(Svc.SigScanner);
        MoveHook = new();
        PresetStorage.Init();

        Service.ComboCache = new CustomComboCache();
        Service.ActionReplacer = new ActionReplacer();
        ActionWatching.Enable();
        IPC = Provider.Init();
        ConflictingPluginsChecks.Begin();

        ConfigWindow = new ConfigWindow();
        _majorChangesWindow = new MajorChangesWindow();
        TargetHelper = new();
        ws = new();
        ws.AddWindow(ConfigWindow);
        ws.AddWindow(_majorChangesWindow);
        ws.AddWindow(TargetHelper);

        Svc.PluginInterface.UiBuilder.Draw += DrawWindows;
        Svc.PluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;
        Svc.PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;

        RegisterCommands();

        DtrBarEntry ??= Svc.DtrBar.Get("Wrath Combo");
        DtrBarEntry.OnClick = ev =>
        {
            // 右鍵開關設定視窗（再按一次關閉）；左鍵維持原本的自動循環切換。
            if (ev.ClickType == MouseClickType.Right)
                ConfigWindow.IsOpen ^= true;
            else
                ToggleAutoRotation(!Service.Configuration.RotationConfig.Enabled);
        };
        DtrBarEntry.Tooltip = new SeString(
        new TextPayload(DtrClickHelp + "\n"),
        new TextPayload("可在 /xlsettings → 伺服器資訊欄中隱藏此圖示"));

        Svc.ClientState.Login += PrintLoginMessage;
        if (Svc.ClientState.IsLoggedIn) ResetFeatures();

        Svc.Framework.Update += OnFrameworkUpdate;
        Svc.ClientState.TerritoryChanged += ClientState_TerritoryChanged;

        if (DateTime.UtcNow - LastPresetDeconflictTime > TimeSpan.FromSeconds(3))
        {
            KillRedundantIDs();
            HandleConflictedCombos();
            LastPresetDeconflictTime = DateTime.UtcNow;
        }
        CustomComboFunctions.TimerSetup();

        // Starts Retarget list cleaning process after a delay
        Svc.Framework.RunOnTick(ActionRetargeting.ClearOldRetargets,
            TimeSpan.FromSeconds(60), cancellationToken: UnloadToken);

#if DEBUG
        ConfigWindow.IsOpen = true;
        Svc.Framework.RunOnTick(() =>
        {
            if (Service.Configuration.OpenToCurrentJob && Player.Available)
                HandleOpenCommand([""], forceOpen:true);
        });
#endif
    }

    /// <summary>
    ///     出貨包裡的圖示檔；不存在時回 <c>null</c>（關於分頁沿用 manifest 的 IconUrl）。
    ///     路徑字串與設定視窗側欄的相同，兩處共用同一份貼圖快取。
    /// </summary>
    private static string? LocalIconPath(IDalamudPluginInterface pluginInterface)
    {
        var dir = pluginInterface.AssemblyLocation.Directory?.FullName;
        if (dir is null) return null;
        var path = Path.Combine(dir, "images\\wrathcombo.png");
        return File.Exists(path) ? path : null;
    }

    private void RemoveNullAutos()
    {
        try
        {
            var save = false;
            if (!Svc.PluginInterface.ConfigFile.Exists) return;

            var json = JObject.Parse(File.ReadAllText(Svc.PluginInterface.ConfigFile.FullName));
            if (json["AutoActions"] is JObject autoActions)
            {
                var clone = autoActions.JSONClone();
                foreach (var a in clone)
                {
                    if (a.Key == "$type")
                        continue;

                    if (Enum.TryParse(typeof(CustomComboPreset), a.Key, out _))
                        continue;

                    Svc.Log.Debug($"Couldn't find {a.Key}");
                    autoActions[a.Key].Parent.Remove();
                    save = true;
                }
            }
            if (save)
                File.WriteAllText(Svc.PluginInterface.ConfigFile.FullName, json.ToString());
        }
        catch (Exception e)
        {
            e.Log();
        }
    }

    private void ClientState_TerritoryChanged(ushort obj)
    {
        UpdateCaches(false, true, false);

        // ClientState_TerritoryChanged 跑在框架執行緒上；CheckForIPCControl 只做兩件受管理的事（把重試計數歸零、排一個
        // 帶延遲的 RunOnTick），沒有任何阻塞等待，直接呼叫、不繞執行緒池：_stancePartnerRunTries 因此只有框架執行緒會碰。
        StancePartner.CheckForIPCControl();
    }

    public const string OptionControlledByIPC =
        "(being overwritten by another plugin, check the setting in /wrath)";

    private static void HandleConflictedCombos()
    {
        var enabledCopy = Service.Configuration.EnabledActions.ToHashSet(); //Prevents issues later removing during enumeration
        foreach (var preset in enabledCopy)
        {
            if (!PresetStorage.IsEnabled(preset)) continue;

            var conflictingCombos = preset.GetAttribute<ConflictingCombosAttribute>();
            if (conflictingCombos == null) continue;

            foreach (var conflict in conflictingCombos.ConflictingPresets)
                if (PresetStorage.IsEnabled(conflict))
                    if (Service.Configuration.DisableAction(conflict))
                    {
                        PluginLog.Debug($"Removed {conflict} due to conflict with {preset}");
                        Service.Configuration.Save();
                    }
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        // 放最前面：本方法後段擲例外時快照照樣更新。
        Helper.PublishClassJobSnapshot(Svc.Objects.LocalPlayer);

        if (Svc.Objects.LocalPlayer is not null)
        {
            JobID = Svc.Objects.LocalPlayer?.ClassJob.RowId;
            CustomComboFunctions.IsMoving(); //Hacky workaround to ensure it's always running
        }

        BlueMageService.PopulateBLUSpells();
        TargetHelper.Draw();
        AutoRotationController.Run();
        PluginConfiguration.ProcessSaveQueue();

        Service.Configuration.SetActionChanging();

        if (Player.Available && Player.IsDead)
            ActionRetargeting.Retargets.Clear();

        UpdateDtrBar();
    }

    /// <summary> 上一次寫進資訊列時的輸入；<c>null</c>＝還沒寫過。只在框架執行緒讀寫。 </summary>
    private (bool AutoOn, bool Locked, int ActivePresets)? _dtrLastState;

    private void UpdateDtrBar()
    {
        // Skip the IPC checking if hidden
        if (DtrBarEntry.UserHidden) return;

        var autoOn = IPC.GetAutoRotationState();
        var locked = P.UIHelper.AutoRotationStateControlled() is not null;
        var activePresets = autoOn ? P.IPCSearch.ActiveJobPresets : 0;

        // 文字與提示只由這三個值決定，沒變就不重設；節點重建或重新顯示時 Dalamud 會用保存的 Text 自己重寫。
        if (_dtrLastState == (autoOn, locked, activePresets)) return;
        _dtrLastState = (autoOn, locked, activePresets);

        var icon = new IconPayload(autoOn
            ? BitmapFontIcon.SwordUnsheathed
            : BitmapFontIcon.SwordSheathed);

        // DTR 空間很擠，而且圖示本身已經帶了開關資訊（出鞘＝開、入鞘＝關），
        // 再寫一次「: on / : off」是重複的。詳細狀態改放滑鼠提示。
        DtrBarEntry.Text = new SeString(icon);
        DtrBarEntry.Tooltip = new SeString(new TextPayload(
            $"Wrath Combo — 自動輪換\n目前：{(autoOn ? "開啟（劍出鞘圖示）" : "關閉（劍入鞘圖示）")}"
            + (autoOn ? $"\n生效中的職業組合：{activePresets}" : "")
            + (locked ? "\n⚠ 已被其他插件鎖定" : "")
            + "\n\n" + DtrClickHelp));
    }

    private const string DtrClickHelp = "左鍵：切換 Wrath Combo 的自動循環開關\n右鍵：開啟／關閉設定視窗";

    private static void KillRedundantIDs()
    {
        var redundantIDs = Service.Configuration.EnabledActions.Where(x => int.TryParse(x.ToString(), out _)).OrderBy(x => x).Cast<int>().ToList();
        foreach (var id in redundantIDs)
            Service.Configuration.RemoveEnabledActionsWhere(x => (int)x == id);

        Service.Configuration.Save();
    }

    private static void ResetFeatures()
    {
        // Enumerable.Range is a start and count, not a start and end.
        // Enumerable.Range(Start, Count)
        Service.Configuration.ResetFeatures("v3.0.17.0_NINRework", Enumerable.Range(10000, 100).ToArray());
        Service.Configuration.ResetFeatures("v3.0.17.0_DRGCleanup", Enumerable.Range(6100, 400).ToArray());
        Service.Configuration.ResetFeatures("v3.0.18.0_GNBCleanup", Enumerable.Range(7000, 700).ToArray());
        Service.Configuration.ResetFeatures("v3.0.18.0_PvPCleanup", Enumerable.Range(80000, 11000).ToArray());
        Service.Configuration.ResetFeatures("v3.0.18.1_PLDRework", Enumerable.Range(11000, 100).ToArray());
        Service.Configuration.ResetFeatures("v3.1.0.1_BLMRework", Enumerable.Range(2000, 100).ToArray());
        Service.Configuration.ResetFeatures("v3.1.1.0_DRGRework", Enumerable.Range(6000, 800).ToArray());
        Service.Configuration.ResetFeatures("1.0.0.6_DNCRework", Enumerable.Range(4000, 150).ToArray());
        Service.Configuration.ResetFeatures("1.0.0.11_DRKRework", Enumerable.Range(5000, 200).ToArray()); 
        Service.Configuration.ResetFeatures("1.0.1.11_RDMRework", Enumerable.Range(13000, 999).ToArray());
    }

    // ws.Draw 加上卡頓診斷：一幀超過門檻才組上下文、寫一行（見 DrawHitchDiag）。
    private void DrawWindows()
    {
        DrawHitchDiag.BeginFrame();
        ws.Draw();
        if (DrawHitchDiag.EndFrame(out var elapsed))
            DrawHitchDiag.Report(elapsed, ConfigWindow.DescribeForHitch() +
                                          (_majorChangesWindow.IsOpen ? "、新版變更視窗開著" : ""));
    }

    private void DrawUI()
    {
        _majorChangesWindow.Draw();
        ConfigWindow.Draw();
    }

    private void PrintLoginMessage()
    {
        Task.Delay(TimeSpan.FromSeconds(5), UnloadToken)
            .ContinueWith(_ => ResetFeatures(), UnloadToken);

        // 刻意不抓登入公告（MotD）：來源在第三方網域，內容會原樣印進使用者的聊天欄。
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Used for non-static only window initialization")]
    public string Name => "Wrath Combo";

    /// <inheritdoc/>
    public void Dispose()
    {
        UnloadCts.Cancel();
        ActionRetargeting.Dispose();
        ConfigWindow.Dispose();
        Debug.Dispose();

        // Try to force a config save if there are some pending
        // 鎖內只做佇列操作、鎖外才寫檔：整併與存檔都在
        // PluginConfiguration.FlushQueuedSavesOnDispose() 裡完成。
        if (PluginConfiguration.SaveQueue.Count > 0)
            Service.Configuration.FlushQueuedSavesOnDispose();

        ws.RemoveAllWindows();
        Svc.DtrBar.Remove("Wrath Combo");
        Svc.Framework.Update -= OnFrameworkUpdate;
        Svc.ClientState.TerritoryChanged -= ClientState_TerritoryChanged;
        Svc.PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        Svc.PluginInterface.UiBuilder.Draw -= DrawUI;
        
        Service.ActionReplacer.Dispose();
        Service.ComboCache.Dispose();
        ActionWatching.Dispose();
        CustomComboFunctions.TimerDispose();
        IPC.Dispose();
        MoveHook?.Dispose();

        ConflictingPluginsChecks.Dispose();
        ActionRequestIPCProvider.Dispose();
        AllStaticIPCSubscriptions.Dispose();
        Svc.ClientState.Login -= PrintLoginMessage;
        EzIpcFailureLog.Disable();
        ECommonsMain.Dispose();
        P = null;
    }

    private void OnOpenMainUi() =>
        HandleOpenCommand(forceOpen: true);

    internal void OnOpenConfigUi() =>
        HandleOpenCommand(tab: OpenWindow.Settings, forceOpen: true);
}
