# 系統匣與基本設定保存

日期：2026-10-24。依使用者同意的第一個 P1 工作包實作：系統匣、暫停／恢復與基本設定保存。

## 使用體驗

首次執行仍顯示控制／設定視窗。關閉此視窗時隱藏到系統匣，輸入監聽與手動快捷鍵繼續運作。第一次關閉時提示如何返回與結束；視窗內一直顯示關閉規則。

系統匣左鍵開啟設定，右鍵提供「開啟設定」、「啟用／暫停自動偵測」與「結束 SnapBar」。圖示、提示文字、視窗按鈕使用同一份狀態。暫停只停用自動偵測，手動快捷鍵仍可使用。重複啟動會通知既有執行個體重新顯示視窗，不另開控制器。獨立 fixture 不佔用主程式的單一執行個體鎖。

## 設定與失敗行為

保存 `%LOCALAPPDATA%/SnapBar/settings.json`，schemaVersion 為 1，僅含 automaticDetectionEnabled；初次使用預設 false。寫入同目錄暫存檔後替換，避免先截斷原設定。讀取上限 64 KiB。損壞、無法讀取或較新版本的檔案採安全預設且保留原檔，不自動覆寫；介面說明本次設定無法保存。設定不包含原文、剪貼簿或診斷紀錄。

Sublime 相容模式維持每次明確啟用，不持久化同意、不在啟動時改寫 Sublime 設定。此工作包不提供登入自啟動、改鍵、排除 App 或新文字動作。

## 元件與生命週期

Core 的 SettingsStore 負責 JSON 版本與檔案交易；App 的 TrayIcon 封裝原生通知區圖示與選單；MainWindow 統一變更偏好及控制器狀態。沿用 WPF，使用 Windows Forms NotifyIcon，不新增第三方套件。

一般關閉只隱藏。明確結束先拒絕新輸入並取消過期選取，保留 owner HWND 等候現有剪貼簿交易結束，再釋放監聽、helper、浮窗與系統匣圖示。Windows 工作階段結束走應用程式結束流程；強制終止仍不能保證剪貼簿還原。

## 驗證

核心測試涵蓋缺檔、保存／重讀、無 BOM、損壞、未知版本、過大檔案及寫入失敗保留原檔。桌面驗證涵蓋關閉後仍存活、重複啟動返回、系統匣選單與主視窗狀態同步、重新啟動保存、fixture 與主程式共存、明確結束清理。設定測試只使用專用暫存目錄。

依據：[PopClip 設定入口](https://www.popclip.app/guide/settings) 以選單列作為常駐入口；[Microsoft NotifyIcon 選單文件](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/how-to-associate-a-shortcut-menu-with-a-windows-forms-notifyicon-component)。本案採 Windows 通知區對應，不複製 macOS 介面。
