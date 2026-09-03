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
        // This used to be `public static MoveControllerSubMemberForMine* Instance = null!;`, assigned
        // from inside the detour (`Instance = self;`) and never cleared - not even in Dispose(). Every
        // reader (IsDashing) then dereferenced that native pointer on some *later* frame. That is the
        // one thing we never do: a native pointer held across frames goes stale on zone change or
        // plugin reload, and the resulting AccessViolationException is a corrupted-state exception
        // that try/catch cannot intercept.
        //
        // The usual fix is "don't store it, re-fetch it on every use", but there is no getter to
        // re-fetch from here: RMIWalk's `self` has no ClientStructs counterpart and no Instance()
        // accessor - the pointer only exists as the detour's argument. So this takes the other half of
        // the rule (store the value, not the pointer): the pointer is dereferenced only inside the
        // detour, where it is guaranteed live, and what survives across frames is a plain bool.
        private static bool dashing;

        /// <summary>Dash state as of the last RMIWalk call. False while the hook is not running.</summary>
        public static bool Dashing => dashing;

        private delegate void RMIWalkDelegate(MoveControllerSubMemberForMine* self, float* sumLeft, float* sumForward, float* sumTurnLeft, byte* haveBackwardOrStrafe, byte* a6, byte bAdditiveUnk);
        // Fallible on purpose: MovementHook is constructed from the plugin ctor, so an unresolved
        // signature used to throw SignatureException and take the *entire* rotation plugin down -
        // over a flag that only feeds IsDashing(). Now it degrades to "IsDashing() always false".
        [Signature("E8 ?? ?? ?? ?? 80 7B 3E 00 48 8D 3D", DetourName = nameof(RMIWalkDetour), Fallibility = Fallibility.Fallible)]
        private readonly Hook<RMIWalkDelegate>? _rmiWalkHook;

        // fail-closed: a detour is a managed function the *native* code calls directly, so a managed
        // exception escaping it unwinds through native frames that have no handler for it. Everything
        // we add on top of Original() runs inside a try; on failure we simply do not update our own
        // state and let the game's own movement handling pass through untouched.
        // NOTE: this does NOT protect against AccessViolationException - see the note above; the
        // protection against *that* is not holding the pointer in the first place.
        private static long detourErrors;
        private static DateTime lastDetourErrorLog = DateTime.MinValue;

        private static void OnDetourError(Exception ex)
        {
            ++detourErrors;
            // this runs per frame - never log unthrottled. Information (not Debug) because reporting
            // users run at LogLevel 1 - Debug is captured too, but drowned by the 100k+ Debug lines a single log file holds.
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
