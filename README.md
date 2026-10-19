# Wrath Combo

[WrathCombo](https://github.com/PunishXIV/WrathCombo) 的台服移植版。把連擊與互斥技能整合到單一按鍵上的職業循環插件，PvE、PvP 皆可用。

## 功能

**PvE**
- 多數職業提供「簡易」（單鍵）模式與可自訂複雜度的「高級」模式
- 自動循環：依設定自動執行完整輸出／治療循環，AoE 所需目標數可逐職業設定
- 多變迷宮專用功能；博茲雅與新月島的專屬技能（新月島輔助職業技能可加入循環）
- 坦克雙重仇恨保護、坦克打斷；治療職業自動復活；
  魔法遠程雙重昏亂保護與復活；近戰雙重牽制與真北保護；
  物理遠程雙重減傷保護與打斷

**PvP**
- 各職業「爆發模式」攻擊、緊急治療、緊急防護、快速淨化、防護取消預防

**其他**
- 無人島衝刺
- 園藝／採礦：優雷卡、定位與真相
- 捕魚：拋竿到上鉤、潛水

## 與其他插件整合

- **Orbwalker**：自動循環模式下選擇施法時自動停下移動，不需手動停止
- **AutoDuty**：可作為副本自動戰鬥引擎
- **Questionable**：可作為跑任務時的戰鬥模組
- **BossMod Reborn**：自動循環放位移技前先問它落點安不安全，危險就改用別的技能

AutoDuty、Questionable 啟用時，會自動鎖定必要的連擊設定為「開啟」以確保循環能運作。

## 常用指令

| 指令 | 功能 |
|---|---|
| `/wrath`（舊別名 `/scombo`） | 開關主視窗 |
| `/wrath pve` / `pvp` / `settings` / `autosettings` | 開啟主視窗並跳至對應分頁 |
| `/wrath <職業縮寫>` | 開啟主視窗並跳至該職業的 PvE 分頁 |
| `/wrath auto` / `auto <on\|off\|toggle>` | 切換自動循環 |
| `/wrath combo` / `combo <on\|off\|toggle>` | 切換動作替換（關閉時循環仍運作，但不替換技能） |
| `/wrath toggle\|set\|unset <名稱>` | 切換／開啟／關閉指定功能（戰鬥中無效）；`/wrath unsetall` 關閉全部 |
| `/wrath list set\|unset\|all [職業]` | 於聊天視窗列出功能狀態 |
| `/wrath ignore` | 把目前選取的敵人加入自動循環的忽略清單 |
| `/wrath debug [職業\|all]` | 匯出除錯檔到桌面，供回報問題用 |

完整指令與選項請見插件內或 `/wrath` 開啟的視窗。

## 台服版的修改

- 介面、技能名稱與說明繁體中文化（台服官方譯名，含新月島輔助職業）。
- 不再連到第三方伺服器取得 IPC 開關與登入公告。
- 赤魔道士新增 F.A.T.E. 接戰、全體傷害前自動昏亂／抗死、沒有治療職業時用赤療傷自補等選項。
- 自動復活不再選身上有「無法復活」狀態的隊友，被伺服器拒絕時不再對同一人連送。
- 自動循環放位移技前先問 BossMod Reborn 落點安不安全；AoE 所需目標數可逐職業設定。
- 機工士的跳彈射擊會掃到未交戰的敵人時不放，避免誤拉。
- 多個插件（AutoDuty、BOCCHI 等）同時接管時，以最近更新的設定為準，停用或更新插件時會通知承租的插件。
- 遊戲改版後某個遊戲函式解不到時，插件照常載入，只停掉對應的那一項功能。
- 多處降低記憶體用量。

## 安裝

在 Dalamud 設定的「自訂插件庫」加入
`https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json` 並啟用，
再從插件列表安裝。

原作者：Team Wrath，上游由 [PunishXIV](https://github.com/PunishXIV/WrathCombo) 維護發佈。
