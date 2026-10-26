# Sublime 剪貼簿備份自動恢復

## 問題與重現

使用者回報 Sublime 選字連續顯示「剪貼簿來源未提供格式資料，請稍後再試」，其後回覆目前已恢復。原截圖當時的剪貼簿狀態未保留，不能唯一判定當時是哪個格式失敗。本輪以真實 Win32 延遲呈現格式重現相同錯誤類型：owner 已宣告註冊格式，但第一次 `WM_RENDERFORMAT` 不提供資料，第二次才準備完成。舊版立即回傳 `clipboard-format-unavailable`，需要使用者另一次觸發。

## 修復

只對 `clipboard-format-unavailable` 最多額外重試兩次，等待 40／80 ms，等待時不持有剪貼簿鎖。每次重新驗證輸入／前景，並要求剪貼簿序號符合上次備份結束時的序號；原 Paste 序號前提亦保留。鎖內延遲呈現造成的合法序號變動可接受，等待期間的新內容則取消，避免把使用者的新剪貼簿當成舊資料。

`ClipboardBackup.Capture()` 重試前釋放上次的部分副本，完整成功才標記可還原；失敗或已消耗的備份不能呼叫 `EmptyClipboard()`。沒有略過任意格式、沒有無備份複製，也沒有改動 Sublime 設定或安裝外掛。永久不可用、特殊格式與超過 64 MiB 仍拒絕讀取，改顯示「自動重試後仍未就緒；已保留原內容，未讀取選字」。

## 驗證

Release／自含式發布 0 警告、0 錯誤。本輪 **250 項檢查通過**：212 項核心、13 項剪貼簿往返、17 項原生競態、6 項 probe 協定／結束，以及 2 項真實 Sublime 檢查；其餘功能沿用前輪 310 項基線，本輪未將未重跑的案例重計。

- 四項新增競態在修復前全部失敗，修復後通過：暫時不可用自動恢復且只送一次複製；永久不可用最多三次呈現、不發出複製；等待中剪貼簿更新保留新內容；輸入失效阻止下一次呈現與複製。完整原文字串、註冊格式逐位元組及新呈現格式均核對還原。
- 兩項新增往返保護確認不完整備份與已消耗備份都不會清空剪貼簿。測試工具亦加入短暫鎖競爭的有限重試，避免被桌面剪貼簿管理器的占用誤判為產品失敗。
- Sublime 獨立開啟 `tests/fixtures/sublime-actions.txt`，選取 `selection`。注入第一次不可用的備份格式後，控制器自動恢復並顯示 9 字可編輯選取，還原預期剪貼簿，接著 Copy 成功；檔案內容、選取與未修改狀態經畫面核對。此測試經控制器入口，未宣稱實體點擊浮窗。
- 測試完成還原原桌面剪貼簿，未讀取／修改使用者的 Sublime 文件。沒有宣稱所有來源永久不可用格式均能恢復。

```powershell
./scripts/build.ps1 -Publish
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --clipboard-roundtrip
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --clipboard-races
# 僅限專用 Sublime fixture 已在前景並選取 selection 時執行
./.tools/dotnet/dotnet.exe run --project tests/SnapBar.Windows.Tests -c Release --no-build -- --sublime-action Copy --delayed-backup
```
