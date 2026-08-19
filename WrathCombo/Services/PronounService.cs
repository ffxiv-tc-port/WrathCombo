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
    ///     🔴 原本是 <c>Lazy&lt;IntPtr&gt;</c>,有兩個獨立的問題:
    ///     <para>
    ///     ① <b>Lazy 把失敗永久記住。</b>預設的 <c>ExecutionAndPublication</c> 模式下,
    ///     工廠擲出的例外會被快取並在之後每次存取重擲;回 <c>IntPtr.Zero</c> 時也一樣永遠是
    ///     Zero。而這條鏈在登入前必定失敗 —— 只要有任何一次存取發生在登入前,整個代名詞
    ///     功能就永久死掉,而且完全靜默。
    ///     </para>
    ///     <para>
    ///     ② <b>整條鏈是裸的。</b><c>Framework.Instance()</c> 宣告為
    ///     <c>[StaticAddress(..., isPointer: true)]</c>:產生器讀「指標的位址」再解參考一層,
    ///     所以它<b>會回 null</b>(不帶 isPointer 的那種才保證非 null),特徵碼失配時則會擲;
    ///     <c>GetUIModule()</c>／<c>GetPronounModule()</c> 是 <c>[MemberFunction]</c>,
    ///     特徵碼失配時擲 <c>InvalidOperationException</c>,而且各自也可能回 null。
    ///     裸解參考 null 原生指標是 AccessViolationException,在 .NET Core 屬
    ///     corrupted-state exception,<c>try/catch</c> 完全攔不到 ⇒ 每一層都必須事前判空。
    ///     </para>
    ///     <para>
    ///     改成每次呼叫重新解析(逐層判空 ＋ try 把「擲出」轉成「回 Zero」),
    ///     順帶也不再跨幀保存原生指標。成本是三次指標讀取,可忽略;
    ///     解析失敗在登入前是常態,所以不寫 log。
    ///     </para>
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
