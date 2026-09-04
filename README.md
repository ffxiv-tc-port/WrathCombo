# Wrath Combo

把連擊與互斥技能整合到單一按鍵上的職業循環插件，PvE、PvP 皆可用。

## 功能

**PvE**
- 多數職業提供「簡易」（單鍵）模式與可自訂複雜度的「高級」模式
- 自動循環：依設定自動執行完整輸出／治療循環
- 多變迷宮專用功能
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

以上兩者啟用時，會自動鎖定必要的連擊設定為「開啟」以確保循環能運作。

## 常用指令

| 指令 | 功能 |
|---|---|
| `/wrath` | 開關主視窗 |
| `/wrath pve` / `pvp` / `settings` / `autosettings` | 開啟主視窗並跳至對應分頁 |
| `/wrath <職業縮寫>` | 開啟主視窗並跳至該職業的 PvE 分頁 |
| `/wrath auto` / `auto <on\|off\|toggle>` | 切換自動循環 |
| `/wrath combo` / `combo <on\|off\|toggle>` | 切換動作替換（關閉時循環仍運作，但不替換技能） |
| `/wrath toggle\|set\|unset <名稱>` | 切換／開啟／關閉指定功能（戰鬥中無效） |
| `/wrath list set\|unset\|all [職業]` | 於聊天視窗列出功能狀態 |
| `/wrath debug [職業\|all]` | 匯出除錯檔到桌面，供回報問題用 |

完整指令與選項請見插件內或 `/wrath` 開啟的視窗。

## 安裝

在 Dalamud 設定的「自訂插件庫」加入
`https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json` 並啟用，
再從插件列表安裝。

## 作者與支援

作者 Team Wrath，現由 [PunishXIV](https://github.com/PunishXIV/WrathCombo) 維護發佈。
支援與討論請至 [Discord](https://discord.gg/Zzrcc8kmvy)。
