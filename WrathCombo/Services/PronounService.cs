#region

using System;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using WrathCombo.Extensions;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

// ReSharper disable UnusedType.Global
// ReSharper disable MemberCanBePrivate.Global

#endregion

namespace WrathCombo.Services;

public static unsafe class PronounService
{

    // They/Them, personally

    public static bool FullyReady => PronounsReady && MethodsReady;
    public static bool PronounsReady => PronounModulePointer != IntPtr.Zero;
    public static bool MethodsReady => GetGameObjectPointerFromPronounID != null;

    public static PronounModule* Module => (PronounModule*)PronounModulePointer;

    /// <summary>
    ///     取得 PronounModule 的位址,取不到就回 <see cref="IntPtr.Zero"/>(fail-closed)。
    /// </summary>
    /// <remarks>
    ///     裸解參考 null 原生指標是 <c>try/catch</c> 攔不到的 AccessViolationException ⇒ 每一層都必須事前判空。
    ///     每次呼叫重新解析(逐層判空 ＋ try 把「擲出」轉成「回 Zero」),不跨幀保存原生指標。
    ///     成本是三次指標讀取,可忽略;解析失敗在登入前是常態,所以不寫 log。
    /// </remarks>
    private static IntPtr PronounModulePointer
    {
        get
        {
            try
            {
                var framework = Framework.Instance();
                if (framework == null) return IntPtr.Zero;

                var uiModule = framework->GetUIModule();
                if (uiModule == null) return IntPtr.Zero;

                var ptr = uiModule->GetPronounModule();
                return ptr != null ? (IntPtr)ptr : IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
    }

    // Signature for PronounModule::GetGameObjectByPronounId.
    private static readonly delegate* unmanaged<IntPtr, uint, GameObject*>
        GetGameObjectPointerFromPronounID = InitializePronounDelegate();

    private static delegate* unmanaged<IntPtr, uint, GameObject*>
        InitializePronounDelegate()
    {
        try
        {
            const string signature =
                "E8 ?? ?? ?? ?? 48 8B D8 48 85 C0 0F 85 ?? ?? ?? ?? 8D 4F DD";
            var address = Svc.SigScanner.ScanText(signature);
            return (delegate* unmanaged<IntPtr, uint, GameObject*>)address;
        }
        catch (Exception ex)
        {
            ex.Log();
            return null;
        }
    }

    /// <summary>
    ///     Gets a game object by pronoun ID (e.g., 44–50 for party members 2–8).
    /// </summary>
    /// <param name="id">The pronoun ID.</param>
    /// <returns>An IGameObject if found; otherwise, null.</returns>
    public static IGameObject? GetIGameObjectFromPronounID(int id)
    {
        if (!FullyReady) return null;

        try
        {
            var uID = (uint)id;
            // FullyReady 已經解析過一次,這裡重新取一次是為了避免拿到過期值;
            // 兩次都在同一個框架執行緒的呼叫堆疊內,不會被切走。
            var modulePtr = PronounModulePointer;
            if (modulePtr == IntPtr.Zero) return null;

            return GameObjectExtensions.GetObjectFrom(
                GetGameObjectPointerFromPronounID(modulePtr, uID));
        }
        catch (Exception ex)
        {
            ex.Log();
            return null;
        }
    }
}
