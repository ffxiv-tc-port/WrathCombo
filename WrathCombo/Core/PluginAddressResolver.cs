using Dalamud.Game;
using ECommons.DalamudServices;
using System;

namespace WrathCombo.Core
{
    /// <summary> Plugin address resolver. </summary>
    internal class PluginAddressResolver
    {
        /// <summary> Gets the address of fpIsIconReplacable. <see cref="IntPtr.Zero" />＝特徵碼對不上。 </summary>
        public IntPtr IsActionIdReplaceable { get; private set; }

        /// <inheritdoc/>
        public unsafe void Setup(ISigScanner scanner)
        {
            // 對不上時留 Zero、不擲：ActionReplacer 看到 Zero 就不建立這個 hook 並印 Warning。
            IsActionIdReplaceable = scanner.TryScanText("40 53 48 83 EC 20 8B D9 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 1F", out var address)
                ? address
                : IntPtr.Zero;

            Svc.Log.Debug("===== W R A T H   C O M B O =====");
            Svc.Log.Debug($"{nameof(IsActionIdReplaceable)} 0x{IsActionIdReplaceable:X}");
        }
    }
}
