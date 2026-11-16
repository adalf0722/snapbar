# 統一選字流程驗證

## 實作結果

SelectionCapturePipeline 統一執行 UIA 快速讀取、已驗證候選的 Ctrl+C 備援、目標綁定、過期取消與自動退避。UIA 成功不會查詢或執行任何剪貼簿適配；明確無選取、密碼、逾時、失去焦點及 provider 錯誤不會轉成複製資格。相容模式關閉偏好、Sublime 的明確設定、單次複製及相容結果唯讀權限保留。

未知 App 不需新增名稱名單：除原有 UIA Edit／Document／Text／Pane 候選外，也可用原生 HWND 與 MSAA 正向能力建立候選。滑鼠和焦點須屬於同一個目標控制項，文字／文件角色及 focused 狀態須齊全；密碼、受保護、隱藏、停用與不同輸入框均拒絕。未知 opaque Window 缺乏證據時仍不複製。企業微信保留自繪根視窗適配，Sublime 保留條件快捷鍵；兩者只提供來源／讀取計畫，共用同一協調流程。

原生互通改用 Windows Desktop 隨附的 Accessibility.IAccessible，避免自行宣告 COM 介面的呼叫差異。實測原生 EDIT 的代理可提供自身角色與狀態，但不一定提供 accHitTest 子物件；因此先以 WindowFromPoint 的實際 HWND 讀取控制項自身能力，再優先採用可取得的子物件資訊。這不會將未知 Window 視為文字。父物件的受保護狀態不會被子物件覆寫。原生焦點、滑鼠 HWND、角色及可用的 child ID 與候選綁定並重驗；不讀取 MSAA 名稱或值。

## 自動驗證

- Core 605 項通過，包括 UIA 成功零複製、終止狀態零複製、單次備援、異目標／過期結果拒絕、偏好關閉與退避隔離。
- 最終桌面 NativeClipboard 9、Compatibility 21、UiaReadiness 2、UiaDeepTree 2、Clipboard 13、ClipboardRaces 17，共 64 項，另有 Preflight 1，全部通過。報告：`artifacts/checks/desktop/20261116T221153641Z-4246c2efccc640a9a63b80ee146060ad/report.json`。
- `scripts/check.ps1 -RequireActionRuntimes` 通過，Core 605、ActionRunner 37，零略過。入口紀錄：`artifacts/checks/unified-selection-check.log`；runner 報告：`artifacts/checks/action-runner/20261116T220806622Z-f463564b99b44553845c538a82877841/report.json`。
- 本次重新建立自含式隔離 Probe，乾淨環境協定 smoke 11 項通過，包含診斷 opt-in、錯誤 JSON、過期候選與 EOF 結束。輸出：`artifacts/investigations/unified-selection/probe/`。

## 途中發現與限制

本次開發版另在實際企業微信既有訊息執行兩次讀取：自動拖選成功約 590 ms，隨後手動快捷鍵成功約 149 ms，均包含複製前後驗證與還原。兩次各只有一次原生 unsupported 探測，直接使用同一備援流程；MSAA 實際提供 client 角色 10，仍由企業微信來源適配驗證。這是自動化操作的小樣本，不當作使用者實體手勢成功率。純階段紀錄：`artifacts/investigations/unified-selection/selection-trace.jsonl`，不含選字或剪貼簿文字。

初期 NativeClipboard 真實控制項測試曾失敗，先修正互通及來源解析再通過，未降低文字角色、焦點或密碼的斷言。首輪 Compatibility 因剪貼簿在穩定觀察期間再次變動而安全取消；未修改序號檢查，後續回歸通過。首次完整 check 另遇到既有 PID 標記 fixture 的讀寫競態，僅將測試端寫入改為有界占用重試，產品 runner 不變。

最終回歸前另有兩份因 fixture 未取得前景而失敗的報告（`20261116T220856656Z-9d2d66c31616475a905f29132f903c4e`、`20261116T221035634Z-fa0baa02c04f43afabb50efb96747317`），未當作通過證據。最終執行只啟用當前測試視窗，不操作其他 App、不預讀 UIA 樹；同一份報告的六組套件全數通過。

先前使用者的企業微信訊息／輸入框與一般微信訊息各 3/3，屬前一階段實機證據，不能當作本次重構後新的實體手勢統計。未知 App 的自動證據使用真實原生 EDIT／密碼框／BUTTON fixture，不代表所有第三方自繪介面均相容。尚未執行完整 Release、35 秒慢速腳本或混合 DPI；此次交付開發版，未發布或替換 artifacts/p0。
