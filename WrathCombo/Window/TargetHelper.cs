using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using WrathCombo.CustomComboNS;
using WrathCombo.Services;

namespace WrathCombo.Window;

internal class TargetHelper : Dalamud.Interface.Windowing.Window
{
    internal TargetHelper() : base("###WrathComboTargeteHelper", ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.AlwaysUseWindowPadding | ImGuiWindowFlags.AlwaysAutoResize, true)
    {
        this.IsOpen = true;
        this.RespectCloseHotkey = false;
    }

    private int _cardTargetFrame = -1;
    private ulong? _cardTargetId;

    /// <summary>
    ///     占星卡片目標的 GameObjectId，同一個 ImGui 幀只算一次。
    /// </summary>
    /// <remarks>
    ///     視窗的 Draw 在新的一幀裡先算；之後 Framework.Update 那次呼叫沿用
    ///     （它畫進前景清單的東西在下一幀開頭就被清掉，看不到）。存 id 不存物件。
    /// </remarks>
    private ulong? CardTargetIdThisFrame()
    {
        if (ImGui.GetCurrentContext().IsNull)
            return Combos.PvE.AST.CardTarget?.GameObjectId;

        var frame = ImGui.GetFrameCount();
        if (frame != _cardTargetFrame)
        {
            _cardTargetId = Combos.PvE.AST.CardTarget?.GameObjectId;
            _cardTargetFrame = frame;
        }

        return _cardTargetId;
    }

    internal unsafe void DrawTargetHelper()
    {
        if (CardTargetIdThisFrame() is { } cardTargetId)
        {
            IntPtr partyPTR = Svc.GameGui.GetAddonByName("_PartyList", 1);
            if (partyPTR == IntPtr.Zero)
                return;

            // 直接讀原生結構，不把整個 0x1590 bytes 複製一份到受管理堆積。
            var plist = (AddonPartyList*)partyPTR;
            if (!plist->IsVisible) return;

            for (int i = 1; i <= 8; i++)
            {
                IGameObject? slot = SimpleTarget.GetPartyMemberInSlotSlot(i);
                if (slot is null) continue;
                if (slot.GameObjectId == cardTargetId)
                {
                    var member = i switch
                    {
                        1 => plist->PartyMembers[0].TargetGlow,
                        2 => plist->PartyMembers[1].TargetGlow,
                        3 => plist->PartyMembers[2].TargetGlow,
                        4 => plist->PartyMembers[3].TargetGlow,
                        5 => plist->PartyMembers[4].TargetGlow,
                        6 => plist->PartyMembers[5].TargetGlow,
                        7 => plist->PartyMembers[6].TargetGlow,
                        8 => plist->PartyMembers[7].TargetGlow,
                        _ => plist->PartyMembers[0].TargetGlow,
                    };

                    DrawOutline(member->AtkResNode.PrevSiblingNode);

                }
            }
        }
    }

    private unsafe void DrawOutline(AtkResNode* node)
    {
        var position = GetNodePosition(node);
        var scale = GetNodeScale(node);
        var size = new Vector2(node->Width, node->Height) * scale;

        position += ImGuiHelpers.MainViewport.Pos;

        var colour = Service.Configuration.TargetHighlightColor;
        ImGui.GetForegroundDrawList(ImGuiHelpers.MainViewport).AddRect(position, position + size, ImGui.GetColorU32(colour), 0, ImDrawFlags.RoundCornersAll, 2);
    }
    public unsafe Vector2 GetNodePosition(AtkResNode* node)
    {
        var pos = new Vector2(node->X, node->Y);
        var par = node->ParentNode;
        while (par != null)
        {
            pos *= new Vector2(par->ScaleX, par->ScaleY);
            pos += new Vector2(par->X, par->Y);
            par = par->ParentNode;
        }

        return pos;
    }

    public unsafe Vector2 GetNodeScale(AtkResNode* node)
    {
        if (node == null) return new Vector2(1, 1);
        var scale = new Vector2(node->ScaleX, node->ScaleY);
        while (node->ParentNode != null)
        {
            node = node->ParentNode;
            scale *= new Vector2(node->ScaleX, node->ScaleY);
        }

        return scale;
    }

    public override void Draw()
    {
        var start = DrawHitchDiag.Begin();
        DrawTargetHelper();
        DrawHitchDiag.End(DrawHitchDiag.Section.TargetHelper, start);
    }
}
