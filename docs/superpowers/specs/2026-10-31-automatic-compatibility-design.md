# 自動選字相容讀取

使用者已同意實作通用剪貼簿回退與持久化相容偏好。延續現有 UIA 優先與剪貼簿備份交易，不將 UIA 的錯誤、密碼或明確空選取當作複製許可。

## 設定與介面

新增預設啟用的「選字相容模式」偏好，關閉亦跨重啟保存。Sublime 仍使用條件快捷鍵適配器；已存在且有效的 SnapBar 綁定代表過去明確啟用，首次遷移恢復此選擇。使用者關閉 Sublime 支援後保存 false，不再因綁定存在而重開。新安裝不在啟動時寫入 Sublime 設定；由 Sublime 支援開關首次設定。啟動只檢查有效性，損壞時顯示原因。設定 schema 6，版本 1–5 僅在記憶體升級；損壞與未來版本保留原檔，停用回退。

## 讀取流程

UIA 原生讀取與 bounded readiness 重試仍優先。只有 unsupported 可走回退。Probe 為確定屬於目標、可用且非密碼的焦點建立短期候選 token；動作前後驗證 token、焦點 runtime ID、視窗／程序身分與密碼祖先。缺少可驗證焦點或敏感屬性不回退。終端機與主控台排除一般 Ctrl+C 回退，避免中斷程式。

通用適配器使用 Ctrl+C，沿用備份、串行、修飾鍵、輸入代次、序號／擁有者與還原檢查。辨識 VS Code 空選取中繼資料、MSDEVLineSelect 與 Visual Studio 整行標記，還原後拒絕將整行當成選取。沒有新剪貼簿回應則不顯示。未知 App 自訂複製行為沒有完整可移植的選取範圍證明，仍需逐 App 實測，不能宣稱所有 Electron 均支援。

Chromium 的 Web Custom MIME Data Format 依 Pickle 的 item count 與對齊 UTF-16 key/value 解析，總量最多 1 MiB、128 組；截斷、無效長度、重複編輯器資料與空選取旗標均拒絕。已知 VS Code 衍生程式需要明確編輯器中繼資料。依據：[VS Code clipboardUtils](https://github.com/microsoft/vscode/blob/main/src/vs/editor/browser/controller/editContext/clipboardUtils.ts)、[Chromium custom_data_helper](https://github.com/chromium/chromium/blob/main/ui/base/clipboard/custom_data_helper.cc)。候選 token 有效 30 秒，僅原焦點可重新複製；一般回退無法分辨內容完全相同的不同選取範圍，因此不授予來源寫入能力。

通用快照的可編輯狀態為未知，只提供複製、搜尋、開啟與不寫入來源的自訂動作。每次執行重新確認焦點與複製內容；不提供剪下／貼上或結果套用。Sublime 已驗證的條件寫入能力保留。既有排除清單阻止自動觸發，明確手動快捷鍵維持可用。

## 驗證

以不公開 TextPattern 的獨立 WPF fixture 驗證真正 Ctrl+C 回退、Unicode、空選取、整行標記、焦點變動、密碼、來源失效、關閉偏好及唯讀動作限制。核心測試涵蓋 schema 遷移、偏好開關與中繼資料判定；既有剪貼簿競態、Sublime、原生 UIA、系統匣及 UI runner 回歸。記錄實測範圍，不把 fixture 等同所有 Electron。

## Review 修正

相容與 Sublime 開關以 Checked／Unchecked 同步，涵蓋 UIA TogglePattern；初始化先設定值再訂閱，狀態回復與程式化同步有重入保護。只有明確啟用且現有綁定無效時才設定 Sublime，開關測試使用注入的安裝及就緒檢查，不碰使用者快捷鍵檔案。

一般候選仍預設 30 秒。通用來源的腳本通過初始重新複製比對後，執行前可申請「設定的逾時秒數＋10 秒驗證餘裕」的期限，最多 310 秒；申請必須通過原有期限、焦點與密碼檢查。執行結束、失敗或取消均釋放，使剩餘期限回到至多 30 秒；清理不延長過期或新候選。完成後仍重新驗證來源與內容，不因延長期限取得寫入能力。以實際等待 35 秒的回歸測試及執行中焦點變更驗證。
