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
        public static readonly ConcurrentDictionary<uint, IDalamudTextureWrap> CachedModdedIcons = new();
        private static readonly ConcurrentDictionary<uint, byte> LoadingModdedIcons = new();

        // 背景讀檔失敗過的替換後路徑，不再重試；鍵是路徑不是 iconId：換圖示包或開關模組後路徑會變，自然會重試。
        private static readonly ConcurrentDictionary<string, byte> FailedModdedPaths = new();

        // 卸載期只印一次通知用的旗標（Interlocked，不用鎖）。
        private static int ModdedIconUnloadNoticeLogged;
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
                // 圖集的 RGBA 資料快取在 UldWrapper 實例上：每格各建一個，就會在繪製執行緒把整張 HD 圖集重新轉換、配置一次。
                var uld = Svc.PluginInterface.UiBuilder.LoadUld("ui/uld/MKDSupportJob.uld");
                for (int i = 0; i <= 25; i++)
                    OccultIcons[i] = uld.LoadTexturePart("ui/uld/MKDSupportJob_hr1.tex", i);
                for (int i = 30; i <= 55; i++)
                    OccultIcons[i] = uld.LoadTexturePart("ui/uld/MKDSupportJob_hr1.tex", i);
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

            // 沒有替換＝遊戲路徑：GetFromFile 是讀磁碟、必定讀不到，背景讀檔也只會在執行緒池擲例外、下一幀再來一次。
            if (resolvePath == path)
                return Svc.Texture.GetFromGame(path).GetWrapOrDefault();

            var wrap = Svc.Texture.GetFromFile(resolvePath);
            if (wrap.TryGetWrap(out var icon, out _))
                return icon;

            if (CachedModdedIcons.TryGetValue(iconId, out IDalamudTextureWrap? cachedIcon))
                return cachedIcon;

            if (!FailedModdedPaths.ContainsKey(resolvePath))
                StartModdedIconLoad(iconId, resolvePath);

            return Svc.Texture.GetFromGame(path).GetWrapOrDefault();
        }

        // 拆出來：捕獲參數的閉包在方法入口就配置，留在上面那支會讓每次呼叫都配置。
        private static void StartModdedIconLoad(uint iconId, string resolvePath)
        {
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
                        var spec = RawImageSpecification.Rgba32(tex.Header.Width, tex.Header.Height);
                        var data = tex.GetRgbaImageData();
                        // CreateFromRaw 不核對長度、直接讓 D3D 讀 Pitch×Height 位元組：資料不夠就當讀檔失敗。
                        if (data.Length < (long)spec.Pitch * spec.Height)
                        {
                            FailedModdedPaths.TryAdd(resolvePath, 0);
                            return;
                        }
                        Svc.Framework.RunOnFrameworkThread(() =>
                        {
                            // 外掛已卸載（排進來之後才 Dispose）：做出來的 wrap 沒人畫、也沒人釋放。
                            if (UnloadToken.IsCancellationRequested)
                                return;

                            // 卸載期 RunOnFrameworkThread 不排隊，會就地在這條執行緒池執行緒上跑。
                            // CreateFromRaw 要碰 Dalamud 的貼圖管理員與 D3D 裝置，而那時候它們正在被拆掉；
                            // 就算做出來，這個 wrap 也不會再有人畫到，只會變成一個沒人釋放的資源。
                            if (Svc.Framework.IsFrameworkUnloading)
                            {
                                if (System.Threading.Interlocked.Exchange(
                                        ref ModdedIconUnloadNoticeLogged, 1) == 0)
                                    Svc.Log.Information(
                                        "The framework is unloading, so a " +
                                        "modded icon was not uploaded to the " +
                                        "GPU.");
                                return;
                            }

                            var output = Svc.Texture.CreateFromRaw(spec, data);
                            if (output != null)
                                CachedModdedIcons[iconId] = output;
                        });
                    }
                    catch
                    {
                        FailedModdedPaths.TryAdd(resolvePath, 0);
                    }
                    finally
                    {
                        LoadingModdedIcons.TryRemove(iconId, out _);
                    }
                });
            }
        }
    }
}
