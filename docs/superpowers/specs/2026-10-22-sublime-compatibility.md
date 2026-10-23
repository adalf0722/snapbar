# Sublime 剪貼簿相容模式

使用者確認採用「不裝外掛，使用有條件的剪貼簿相容模式」。本修正延續 P0，僅針對標準安裝的 Sublime Text，不擴大至其他 App。

本機 Sublime Build 4180 能顯示選取，但 UI Automation 樹沒有編輯器文字或選取範圍。一般 Ctrl+C 在空選取時可能複製整行，因此不能直接拿來判斷有無選取。

## 實作決策

- 主視窗新增明確啟用開關，預設關閉，每次啟動需重新勾選。
- 啟用時，在 Sublime User 的 Windows 快捷鍵設定中加入兩條 Ctrl+Alt+Shift+F12 綁定：預設 noop；單段、非空、非搜尋面板／快速面板時才執行內建 copy。只新增 JSON 設定，不安裝 Python 外掛，不改 Ctrl+C。
- 保留既有設定文字；已存在的檔案先備份。發現快捷鍵衝突或 JSON 格式錯誤就不啟用。
- 讀取前保存剪貼簿資料至記憶體，保留格式與原始位元組；支援文字、DIB/DIBV5、GDI bitmap、palette、enhanced metafile、檔案清單及可備份的註冊格式，總量最多 64 MiB。GDI 控制代碼用對應的原生複製／釋放方式處理，不當成記憶體位元組。無法備份的格式直接略過。
- 請求串行，輪詢複製結果並確認序號穩定 35ms。檢查原視窗與程序身分、輸入代次、剪貼簿序號與擁有者；在剪貼簿鎖內讀取與還原。使用者輸入使請求失效時不再覆寫剪貼簿。
- 實測 Sublime 複製後 GetClipboardOwner() 為 NULL，這是 Win32 允許的情況；無擁有者時仍要求新的穩定序號、同一前景與未改變的輸入代次。已知其他程序擁有者或序號再變動則拒絕。無擁有者不能證明資料一定來自 Sublime，同時發生的無擁有者寫入仍有無法區分的競態。
- 相容快照僅提供 Copy，Editable 未知；Copy 前再次讀取並比對原文。Cut/Paste 保持停用。
- 沒有選取、多游標、控制項拒絕快捷鍵、逾時均不顯示工具列。相同內容的不同範圍無法識別，但不進行文件寫入。

## 已知取捨

剪貼簿相容模式會真的複製文字。Windows／第三方剪貼簿歷程可能記錄暫存內容；還原目前剪貼簿不會抹除歷程。極慢的目標若在等待結束後才處理快捷鍵，或讀取中有使用者輸入、並行寫入、SnapBar 被強制終止，不能保證還原。這些情況不得宣稱交易具有原子性。

標準安裝設定路徑：`%APPDATA%/Sublime Text/Packages/User/Default (Windows).sublime-keymap`。攜帶版 Data 目錄不適用。取消勾選即停止使用；移除檔案中 SnapBar 註解下方兩條綁定可完整撤回設定。

## 驗證工作

1. 核心測試：安裝冪等、保留註解／尾逗號／中文、衝突拒絕、條件遭修改或順序顛倒時拒絕；未知編輯狀態禁止寫入。
2. 建置與既有 helper 協定測試，確保 UIA 路徑仍可運作。
3. Sublime 實機：單字選取、空選取、手動與自動觸發、原剪貼簿還原；保留原始使用者文件。

官方格式依據：[Sublime Key Bindings](https://www.sublimetext.com/docs/key_bindings.html)、[GetClipboardOwner](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardowner)、[OleDuplicateData](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-oleduplicatedata)。結果記錄於 P0 驗證報告。
