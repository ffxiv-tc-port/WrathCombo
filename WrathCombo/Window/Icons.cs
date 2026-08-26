using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Utility;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using Lumina.Data.Files;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using WrathCombo.Combos.PvE;
using WrathCombo.Combos.PvE.Content;

namespace WrathCombo.Window
{
    internal static class Icons
    {
        // 🔴 這兩個集合會被兩條執行緒同時碰：繪製執行緒讀取／加入，Task.Run 的背景執行緒移除。
        // 原本是 Dictionary 與 HashSet，兩者都不是執行緒安全的——並行寫入會弄壞內部陣列，
        // 症狀是 HashSet.AddIfNotPresent 丟 IndexOutOfRangeException，整個設定視窗變成
        // 「繪製此視窗時發生錯誤」（2026-08-01 實機，PvEFeatures 分頁）。
        // ⚠️ 這是我們自己在 02afc550「Fix main-thread stutter when opening config window」
        // 把圖示載入移到背景執行緒時引入的，不是上游的問題。
        public static readonly ConcurrentDictionary<uint, IDalamudTextureWrap> CachedModdedIcons = new();
        private static readonly ConcurrentDictionary<uint, byte> LoadingModdedIcons = new();
        public static Dictionary<int, IDalamudTextureWrap?> OccultIcons = [];
        private static int OccultIdx = -1; // Instead of 0 to show Freelancer
        public static IDalamudTextureWrap? GetJobIcon(uint jobId)
        {
            switch (jobId)
            {
                case All.JobID: jobId = 62146; break; //Adventurer / General
                case > All.JobID and <= 42: jobId += 62100; break; //Classes
                case DOL.JobID: jobId = 82096; break;
                case OccultCrescent.JobID: return GetOccultIcon();
                default: return null; //Unknown, return null
            }
            return GetTextureFromIconId(jobId);
        }

        private static IDalamudTextureWrap? GetOccultIcon()
        {
            if (OccultIcons.Count < 26)
            {
                for (int i = 0; i <= 25; i++)
                {
                    var uld = Svc.PluginInterface.UiBuilder.LoadUld("ui/uld/MKDSupportJob.uld");
                    OccultIcons[i] = uld.LoadTexturePart("ui/uld/MKDSupportJob_hr1.tex", i);
                }
                for (int i = 30; i <= 55; i++)
                {
                    var uld = Svc.PluginInterface.UiBuilder.LoadUld("ui/uld/MKDSupportJob.uld");
                    OccultIcons[i] = uld.LoadTexturePart("ui/uld/MKDSupportJob_hr1.tex", i);
                }
            }

            if (EzThrottler.Throttle("OccultAnimateIdx", 800))
                OccultIdx++;

            if (OccultIdx == 13) // Only cycle through the current ones, set to 26 after new ones added
                OccultIdx = 0;

            return OccultIcons[OccultIdx];
        }

        private static string ResolvePath(string path) => Svc.TextureSubstitution.GetSubstitutedPath(path);

        public static IDalamudTextureWrap? GetTextureFromIconId(uint iconId, uint stackCount = 0, bool hdIcon = true)
        {
            GameIconLookup lookup = new(iconId + stackCount, false, hdIcon);
            string path = Svc.Texture.GetIconPath(lookup);
            string resolvePath = ResolvePath(path);

            var wrap = Svc.Texture.GetFromFile(resolvePath);
            if (wrap.TryGetWrap(out var icon, out _))
                return icon;

            if (CachedModdedIcons.TryGetValue(iconId, out IDalamudTextureWrap? cachedIcon))
                return cachedIcon;

            // Fallback for modded icons not resolvable via GetFromFile: load off the
            // main thread so a burst of uncached icons (e.g. the job list on first
            // window open) doesn't block Draw() with synchronous disk reads.
            if (LoadingModdedIcons.TryAdd(iconId, 0))
            {
                Task.Run(() =>
                {
                    try
                    {
                        var tex = Svc.Data.GameData.GetFileFromDisk<TexFile>(resolvePath);
                        var spec = RawImageSpecification.Rgba32(tex.Header.Width, tex.Header.Width);
                        var data = tex.GetRgbaImageData();
                        Svc.Framework.RunOnFrameworkThread(() =>
                        {
                            var output = Svc.Texture.CreateFromRaw(spec, data);
                            if (output != null)
                                CachedModdedIcons[iconId] = output;
                        });
                    }
                    catch { }
                    finally
                    {
                        LoadingModdedIcons.TryRemove(iconId, out _);
                    }
                });
            }

            return Svc.Texture.GetFromGame(path).GetWrapOrDefault();
        }
    }
}
