# PVP Chronicle Recorder

[English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md) | [한국어](README.ko.md) | [Deutsch](README.de.md) | [Français](README.fr.md)

PVP Chronicle Recorder 是 PVP Chronicle 的可選本地錄屏伴侶：進入支援的 PvP 戰局後透過 OBS 自動錄製，並把本機錄影與戰局時間軸關聯，在覆盤頁面中同步檢視影片、地圖和戰鬥事件。

> [!IMPORTANT]
> 本外掛不能獨立識別戰局，必須同時啟用 PVP Chronicle。錄影檔案只儲存在本機，不會上傳到 PVP Chronicle 伺服器。

## 主要功能

- 進入支援的紛爭前線或水晶衝突戰局後，透過 OBS WebSocket v5 自動開始錄製。
- 正常結算後自動停止錄製，並使用戰局 ID 將錄影關聯到對應覆盤。
- 在 PVP Chronicle 的“錄屏回放”頁中直接播放本地錄影，不需要外部播放器。
- 影片、覆盤時間軸和戰場地圖使用同一個時間遊標並保持同步。
- 支援點選畫面播放或暫停、前後跳轉10秒、0.5×至2×倍速以及多檔畫質。
- 回放頁面採用雙欄佈局：左側播放影片，右側顯示同步地圖和當前時間點事件。
- OBS 密碼使用當前 Windows 使用者的 DPAPI 加密儲存。
- Flyleaf 播放執行庫按需下載，並在解壓前校驗固定檔案大小和 SHA-256。

## 快速開始

1. 安裝並啟動 XIVLauncher，確認 Dalamud 已啟用。
2. 開啟 **Dalamud 設定 → 測試版**。

3. 新增下面的自定義外掛倉庫地址：

   ```text
   https://raw.githubusercontent.com/chefiffer/dalamud-plugins/main/repo.json
   ```

4. 開啟外掛安裝器，安裝並啟用 PVP Chronicle 和 PVP Chronicle Recorder。

5. 在 OBS 中啟用 WebSocket 伺服器，並設定密碼。
6. 在遊戲聊天欄輸入：

   ```text
   /pvprec
   ```

7. 填寫 OBS WebSocket 地址和密碼，儲存後點選“測試 OBS 連線”。
8. 點選“安裝官方 Flyleaf 播放執行庫”，等待下載、校驗和解壓完成。
9. 保持 OBS 開啟，然後進入一場受支援的 PvP 戰局進行測試。

OBS 場景、裁剪、遊戲獨佔音訊和故障排查的完整步驟見：[OBS 使用教程](./OBSSetupGuide.zh-TW.md)。

## 配置說明

| 選項 | 建議值 | 作用 |
| --- | --- | --- |
| 介面語言 | 自動 | 跟隨系統語言，也可以在 Recorder 視窗中單獨選擇 |
| 自動錄製 | 開啟 | 收到 PVP Chronicle 的戰局開始和結束事件後控制 OBS |
| OBS WebSocket 地址 | `ws://127.0.0.1:4455` | 連線同一臺電腦上的 OBS；迴環地址不會經過系統代理 |
| OBS WebSocket 密碼 | 與 OBS 設定一致 | 只用於本機 WebSocket 身份驗證，並由 DPAPI 加密儲存 |
| Flyleaf FFmpeg 目錄 | 使用安裝按鈕自動配置 | 提供本地影片解碼能力 |
| 首幀校準 | 預設 `0 ms` | 修正錄影首幀與戰局時間軸之間的固定偏差 |

## 錄屏與回放流程

```mermaid
flowchart LR
    A[PVP Chronicle<br/>識別戰局和時間軸] -->|版本化 IPC| B[Recorder]
    B -->|OBS WebSocket| C[OBS 本機錄製]
    C --> D[本地錄影檔案]
    B --> E[本地錄影索引]
    D --> F[Flyleaf 本地解碼]
    E --> F
    F -->|本機共享幀緩衝| G[PVP Chronicle<br/>錄屏回放頁面]
    A --> G
```

兩個外掛只透過版本化 IPC 交換戰局 ID、時間和播放狀態。錄影畫素透過本機共享幀緩衝進入 PVP Chronicle 的 ImGui 紋理，不會傳送到網路，也不是用獨立視窗覆蓋在覆盤頁面上。

## 回放操作

- 點選影片畫面：播放或暫停。
- `-10s` / `+10s`：影片、地圖和覆盤時間同時前後跳轉10秒。
- “對齊當前覆盤時間”：把影片跳轉到覆盤時間軸當前選擇的位置。
- 倍速：支援0.5×、1×、1.5×和2×。
- 畫質：支援流暢、高畫質、清晰和原畫；預設使用原畫。
- 離開錄屏分頁或關閉覆盤視窗：播放器自動暫停，後臺不會繼續播放聲音。

## 本地資料與隱私

| 資料 | 儲存位置 | 是否上傳伺服器 |
| --- | --- | --- |
| OBS 錄影 | 使用者在 OBS 中配置的錄影目錄 | 否 |
| 錄影與戰局關聯索引 | Dalamud 外掛配置目錄 | 否 |
| OBS WebSocket 密碼 | Dalamud 外掛配置，DPAPI 加密 | 否 |
| Flyleaf FFmpeg 執行庫 | Recorder 外掛配置目錄 | 否 |
| 戰局分析資料 | 由 PVP Chronicle 按其自身規則處理 | Recorder 不負責上傳 |

## 已知邊界

- 遊戲崩潰或強制結束時，PVP Chronicle 可能收不到正常的戰局結束事件，因此無法保證自動停止和關聯本場錄影。
- 當前不提供崩潰前後多段錄影的自動拼接；下一場新戰局會重新建立獨立錄影。
- 移動或刪除 OBS 錄影後，歷史戰績仍然存在，但對應回放無法開啟。
- Recorder 未啟用時，PVP Chronicle 的採集和普通覆盤不受影響。
- PVP Chronicle 未啟用時，Recorder 收不到戰局事件，不會自動錄製。

## 第三方元件

- [Flyleaf](https://github.com/SuRGeoNix/Flyleaf)：本地影片解碼和播放。
- [FFmpeg](https://ffmpeg.org/)：由 Flyleaf 官方執行庫提供。
- [SharpCompress](https://github.com/adamhathcock/sharpcompress)：解壓經過校驗的執行庫壓縮包。

具體許可和來源見 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。
