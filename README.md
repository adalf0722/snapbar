# SnapBar

**選取文字，動作就在手邊。**

SnapBar 是 Windows 的文字選取工具列。在正在使用的 App 裡選一段文字，就能在選取附近複製、搜尋、開啟連結，或改寫後貼回原處。

[下載 SnapBar（Windows x64）](https://github.com/adalf0722/snapbar/releases) · [回報問題](https://github.com/adalf0722/snapbar/issues)

## 開始使用

1. 到 [GitHub Releases](https://github.com/adalf0722/snapbar/releases)，從版本的 **Assets** 下載 SnapBar 的 Windows x64 ZIP。GitHub 另列的 **Source code** 是原始碼壓縮檔。
2. 完整解壓縮，開啟 `SnapBar.App.exe`。套件已包含 .NET 執行環境。
3. 在瀏覽器或文字編輯器選取文字，按 **Ctrl + Alt + V** 叫出工具列。也可在 SnapBar 設定中啟用自動偵測，讓工具列在選字後顯示。

關閉設定視窗後，SnapBar 會留在系統匣。點擊圖示可重新開啟設定，按右鍵可暫停自動偵測或結束程式。更新時請先結束 SnapBar，再解壓縮新版的完整套件。目前提供免安裝的未簽章套件。

## 讓常用動作跟著選取文字

- **複製、剪下、貼上與搜尋**。唯讀文字也能複製或搜尋。選取完整的 HTTP／HTTPS 網址可開啟網頁，選取完整路徑可在檔案總管中顯示檔案或資料夾。
- **把工具列排成自己的順序**。在「管理動作」釘選最多六個動作，其餘可放入「⋯」選單或隱藏。也可自訂搜尋網址，以及新增網址、PowerShell 或 JavaScript 動作。JavaScript 動作需要 Node.js，腳本會以目前的 Windows 使用者權限執行。
- **改寫並比較結果**。在「設定 AI 服務」填入自己的服務端點與模型，再從「⋯」開啟「AI 改寫」。可在原文與結果間切換、接著修訂，或複製結果。來源仍可編輯且原選取通過重新驗證時，可直接替換原文。

## 相容性與資料處理

SnapBar 會依來源 App 提供的選取與編輯資訊顯示可用動作。密碼欄位會略過。選取或焦點變更時，需要重新選字才能寫回。若不希望工具列在特定 App 自動出現，可將該 App 加入排除清單，仍可用快捷鍵手動叫出。

SnapBar 優先透過 Windows UI Automation 讀取選取文字。部分 App 需使用選字相容模式，該模式可能暫借剪貼簿，並在讀取後嘗試還原。若使用 Sublime Text，可在設定中啟用專用支援，首次設定會加入 SnapBar 使用的快捷鍵綁定。

SnapBar 的一般設定檔不保存選取文字或剪貼簿內容。執行搜尋、網址或腳本動作時，所選文字會交給對應的瀏覽器或腳本。只有執行 AI 指令時，SnapBar 才會將文字送往你設定的 AI 服務。AI 金鑰由 Windows 加密保存，改寫結果保留於當次面板。

## 開發與驗證

建置、測試與發布流程請見 [開發指南](docs/development.md)。各 App 的實測結果及已知限制請見 [相容性驗證紀錄](docs/p0-validation.md)。
