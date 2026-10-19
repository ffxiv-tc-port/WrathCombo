using FFXIVClientStructs.FFXIV.Client.Game;

namespace WrathCombo.Services.ActionRequestIPC;

/// <summary>
///     一個動作的身分（動作類型 + 動作 ID）。
/// </summary>
/// <remarks>
///     以 <see langword="record struct" /> 實作，讓 <c>==</c> 直接是值比較，
///     供 <see cref="ActionRequestIPCProvider" /> 的清單比對使用。
/// </remarks>
public readonly record struct ActionDescriptor
{
    public readonly ActionType ActionType;
    public readonly uint ActionID;

    public ActionDescriptor(ActionType actionType, uint actionID)
    {
        ActionType = actionType;
        ActionID = actionID;
    }
}
