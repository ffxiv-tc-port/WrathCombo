using ECommons.DalamudServices;
using Newtonsoft.Json;
using System.IO;

namespace WrathCombo.Data
{
    public class RepoCheck
    {
        public string? InstalledFromUrl { get; set; }
    }

    public static class RepoCheckFunctions
    {
        public static RepoCheck? FetchCurrentRepo()
        {
            FileInfo? f = Svc.PluginInterface.AssemblyLocation;

            // Flag as self-built if in dev mode
            if (Svc.PluginInterface.IsDev)
            {
                return new RepoCheck
                {
                    InstalledFromUrl = "!! Self-Built !!"
                };
            }
            var manifest = Path.Join(f.DirectoryName, "WrathCombo.json");

            // Load the manifest
            try
            {
                RepoCheck? repo = JsonConvert.DeserializeObject<RepoCheck>(File.ReadAllText(manifest));

                // Check if we were able to read the manifest and its repo URL
                return repo?.InstalledFromUrl is null ? null : repo;
            }
            catch
            {
                // ignored
            }

            return null;
        }

        /// <summary>
        /// 不要把下面那個網址改成本 org 的 feed：這個方法回答的是「是不是從 PunishXIV 官方庫裝的」,對台服 fork 的使用者而言正確答案就是 false。
        /// 改成我方 feed 會讓它對我們的使用者恆為 true;這段目前不執行,語意反轉沒有徵兆,要到有人(或從上游合併)接上呼叫點才爆出來。
        /// 保留原判斷 ⇒ 恆 false ⇒ 不論上游拿它當「官方庫使用者才給的提示」還是「非官方來源警告」的閘門都落在安全側,也不會和上游產生合併衝突。
        /// </summary>
        public static bool IsFromPunishRepo()
        {
            RepoCheck? repo = FetchCurrentRepo();
            if (repo is null) return false;

            if (repo.InstalledFromUrl is null) return false;

            if (repo.InstalledFromUrl == "https://love.puni.sh/ment.json")
                return true;
            else
                return false;
        }
    }
}
