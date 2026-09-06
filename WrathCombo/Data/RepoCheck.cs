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
        /// 這個方法在本 repo 與上游都是**死碼**:2026-09-06 對 origin/tc-7.20、upstream/main、
        /// cyc/main、cyc/api13-tw 四個 ref 逐一 git grep,IsFromPunishRepo 只出現在本檔;
        /// git log --all -S 排除本檔後零命中 ⇒ 從 2022-10-06 以 IsFromSlothRepo 之名被引入至今,
        /// 從來沒有過呼叫點。本檔真正在跑的是 FetchCurrentRepo(),由 DebugFile.AddPluginInfo()
        /// 呼叫,把安裝來源網址原樣印進使用者的除錯檔——那條路徑不需要改,它本來就會正確印出
        /// 台服使用者實際用的插件庫網址。
        ///
        /// 🔴 **不要把下面那個網址改成本 org 的 feed。** 這個方法回答的是「是不是從 PunishXIV
        /// 官方庫裝的」,對台服 fork 的使用者而言正確答案就是 false。改成我方 feed 會讓它對
        /// 我們的使用者恆為 true,而這段目前不執行 ⇒ 語意反轉現在完全沒有徵兆,只在將來有人
        /// (或從上游合併)接上呼叫點時才爆出來,而且兩種可能的用途都會錯:若上游拿它當
        /// 「官方庫使用者才給的支援/贊助提示」的閘門,我們的使用者會看到不該看到的東西;
        /// 若拿它當「非官方來源警告」的反向閘門,我們的使用者會跳過一個對我們其實成立的警告。
        /// 保留原判斷 ⇒ 恆 false ⇒ 兩種用途都落在安全側,也不會和上游產生合併衝突。
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
