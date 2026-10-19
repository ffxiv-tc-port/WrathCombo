using FFXIVClientStructs.FFXIV.Client.Game;
using System;

namespace WrathCombo.Services
{
    internal unsafe static class BlueMageService
    {
        private const int SlotCount = 25;

        // 每幀都跑（標題畫面也是）：先讀進堆疊上的緩衝區，清單內容真的變了才動它。
        public static void PopulateBLUSpells()
        {
            Span<uint> current = stackalloc uint[SlotCount];
            var count = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                var id = ActionManager.Instance()->GetActiveBlueMageActionInSlot(i);
                if (id != 0)
                    current[count++] = id;
            }

            var active = Service.Configuration.ActiveBLUSpells;
            var unchanged = active.Count == count;
            for (var i = 0; unchanged && i < count; i++)
                unchanged = active[i] == current[i];
            if (unchanged)
                return;

            // 只有出現原本沒有的技能才存檔；少了技能或只是換位置不存。
            var added = false;
            for (var i = 0; !added && i < count; i++)
                added = !active.Contains(current[i]);

            active.Clear();
            for (var i = 0; i < count; i++)
                active.Add(current[i]);

            if (added)
                Service.Configuration.Save();
        }
    }
}
