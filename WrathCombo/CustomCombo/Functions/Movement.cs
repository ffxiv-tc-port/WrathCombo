using Dalamud.Hooking;
using Dalamud.Utility;
using Dalamud.Utility.Signatures;
using ECommons.DalamudServices;
using ECommons.EzHookManager;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Data.Parsing.Layer;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using WrathCombo.Services;

namespace WrathCombo.CustomComboNS.Functions
{
    internal abstract partial class CustomComboFunctions
    {
        private static DateTime? movementStarted;
        private static DateTime? movementStopped;

        /// <summary> Checks if the player is moving. </summary>
        public static unsafe bool IsMoving()
        {
            var agentMap = AgentMap.Instance();
            if (agentMap is null)
                return false;

            bool isMoving = agentMap->IsPlayerMoving || Player.IsJumping;

            if (isMoving)
            {
                if (movementStarted is null)
                    movementStarted = DateTime.Now;

                movementStopped = null;
            }
            else
            {
                if (movementStopped is null)
                    movementStopped = DateTime.Now;

                movementStarted = null;
            }

            return isMoving && TimeMoving.TotalSeconds >= Service.Configuration.MovementLeeway;
        }

        /// <summary>
        /// Last dash state sampled inside the RMIWalk detour (see <see cref="MovementHook"/>).
        /// False whenever the hook is not running - never a stale native pointer read.
        /// </summary>
        public static bool IsDashing() => MovementHook.Dashing;

        public static TimeSpan TimeMoving => movementStarted is null ? TimeSpan.Zero : (DateTime.Now - movementStarted.Value);

        public static TimeSpan TimeStoodStill => movementStopped is null ? TimeSpan.Zero : (DateTime.Now - movementStopped.Value);
    }

    internal unsafe class MovementHook : IDisposable
    {
        // 只在 detour 裡解參考 self（那一刻保證有效），跨幀留下的只有一個 bool：不保存 MoveControllerSubMemberForMine*。
        // 跨幀持有的原生指標在換區或重新載入插件後會失效，解參考是 try/catch 攔不到的 AccessViolationException；
        // RMIWalk 的 self 沒有 ClientStructs 對應型別、也沒有 Instance() 可以每次重取，所以存值不存指標。
        private static bool dashing;

        /// <summary>Dash state as of the last RMIWalk call. False while the hook is not running.</summary>
        public static bool Dashing => dashing;

        private delegate void RMIWalkDelegate(MoveControllerSubMemberForMine* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);
        // 刻意設成 Fallible：MovementHook 在插件建構子裡建，特徵碼對不上時不可以擲 SignatureException 讓整個插件載不起來；
        // 對不上時只退化成 IsDashing() 恆為 false。
        [Signature("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D", DetourName = nameof(RMIWalkDetour), Fallibility = Fallibility.Fallible)]
        private readonly Hook<RMIWalkDelegate>? _rmiWalkHook;

        // fail-closed：detour 是原生碼直接呼叫的受管函式，逸出的受管例外會穿過沒有處理常式的原生堆疊。
        // Original() 之外加的東西都包在 try 裡；失敗時只是不更新自己的狀態，遊戲自己的移動處理照常。
        // 這擋不住 AccessViolationException：那一種靠的是根本不保存指標（見 dashing 的說明）。
        private static long detourErrors;
        private static DateTime lastDetourErrorLog = DateTime.MinValue;

        private static void OnDetourError(Exception ex)
        {
            ++detourErrors;
            // 每幀都會走到：一律節流。要使用者回報的診斷。
            var now = DateTime.UtcNow;
            if (now - lastDetourErrorLog < TimeSpan.FromSeconds(30))
                return;
            lastDetourErrorLog = now;
            Svc.Log.Information($"MovementHook: RMIWalk detour threw, dash state not updated this frame (total {detourErrors}): {ex}");
        }

        private void RMIWalkDetour(MoveControllerSubMemberForMine* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk)
        {
            _rmiWalkHook!.OriginalDisposeSafe(self, sumLeft, sumForward, sumTurnLeft, haveBackwardOrStrafe, a6, bAdditiveUnk);

            try
            {
                dashing = self != null && self->Dashing == 1;
            }
            catch (Exception ex)
            {
                OnDetourError(ex);
            }
        }

        public void Dispose()
        {
            _rmiWalkHook?.Dispose();
            dashing = false;
        }

        internal MovementHook()
        {
            Svc.Hook.InitializeFromAttributes(this);
            if (_rmiWalkHook != null)
                _rmiWalkHook.Enable();
            else
                Svc.Log.Warning("RMIWalk signature not found - IsDashing() will always report false");
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct MoveControllerSubMemberForMine
    {
        [FieldOffset(0x10)] public Vector3 Direction;
        [FieldOffset(0x28)] public float Unk_0x28;
        [FieldOffset(0x38)] public float Unk_0x38;
        [FieldOffset(0x3C)] public byte Moved; // 1 when the character has moved
        [FieldOffset(0x3D)] public byte Rotated; // 1 when the character has rotated
        [FieldOffset(0x3E)] public byte MovementLock;
        [FieldOffset(0x3F)] public byte Unk_0x3F; // non-zero when moving with LMB+RMB
        [FieldOffset(0x40)] public byte Unk_0x40;
        [FieldOffset(0x44)] public float MoveSpeed;
        [FieldOffset(0x50)] public float* MoveSpeedMaximums;
        [FieldOffset(0x80)] public Vector3 ZoningPosition;
        [FieldOffset(0x90)] public float MoveDir;
        [FieldOffset(0x94)] public byte Unk_0x94;
        [FieldOffset(0xA0)] public Vector3 MoveForward; // direction output by MovementUpdate
        [FieldOffset(0xB0)] public float Unk_0xB0;
        [FieldOffset(0xB4)] public byte Unk_0xB4;
        [FieldOffset(0xF2)] public byte Dashing;
        [FieldOffset(0xF3)] public byte Unk_0xF3;
        [FieldOffset(0xF4)] public byte Unk_0xF4;
        [FieldOffset(0xF5)] public byte Unk_0xF5;
        [FieldOffset(0xF6)] public byte Unk_0xF6;
        [FieldOffset(0x104)] public byte Unk_0x104;
        [FieldOffset(0x110)] public Int32 WishdirChanged;
        [FieldOffset(0x114)] public float Wishdir_Horizontal;
        [FieldOffset(0x118)] public float Wishdir_Vertical;
        [FieldOffset(0x120)] public byte Unk_0x120;
        [FieldOffset(0x121)] public byte Rotated1;
        [FieldOffset(0x122)] public byte Unk_0x122;
        [FieldOffset(0x123)] public byte Unk_0x123;
        [FieldOffset(0x125)] public byte Unk_0x125;
        [FieldOffset(0x12A)] public byte Unk_0x12A;
    }
}
