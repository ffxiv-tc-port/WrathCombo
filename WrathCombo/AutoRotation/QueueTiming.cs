namespace WrathCombo.AutoRotation;

/// <summary> 自動循環「提前排隊」的判定；純函式，不碰遊戲狀態。 </summary>
internal static class QueueTiming
{
    internal const float DefaultWindow = 0.1f;
    internal const float MaxWindow = 0.5f;

    /// <summary> 設定檔可能被手改成負值、過大或 NaN；NaN 會讓下面的比較恆假＝永遠不出招，所以當 0。 </summary>
    internal static float Sanitize(float window) =>
        float.IsNaN(window) ? 0f : System.Math.Clamp(window, 0f, MaxWindow);

    /// <summary> 現在送出會不會落在遊戲的排隊範圍內；window＝0 時等同「GCD／動畫鎖已歸零」。 </summary>
    internal static bool CanSend(bool isAbility, float remainingGcd, float animationLock, float window) =>
        isAbility ? animationLock <= window : remainingGcd <= window;
}
