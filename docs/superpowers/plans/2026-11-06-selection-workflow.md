# SnapBar 選字工作流程計畫

2026-11-06 確認首版方向。依序交付動作快捷鍵、原地翻譯和框選 OCR。翻譯採獨立憑證設定，OCR 優先驗證 Tesseract 的免安裝 ZIP 路線。

## 前置驗證

- **翻譯尚未通過發佈條件。** [Google Cloud Translation](https://docs.cloud.google.com/translate/docs/authentication)、[Azure Translator](https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/authentication) 和 [LibreTranslate 官方託管](https://docs.libretranslate.com/guides/faq/) 都需要驗證。MyMemory 提供免金鑰查詢，但每次最多 500 UTF-8 bytes，必須指定來源和目標語言，匿名用量也有限。現有資料不足以採為預設服務。建議優先評估由使用者選填正式翻譯服務的憑證，保留獨立小窗；免填金鑰的公開版需另定供應與營運方式。[MyMemory API](https://mymemory.translated.net/doc/spec.php) · [用量限制](https://mymemory.translated.net/doc/usagelimits.php)
- **OCR 已通過獨立 ZIP 探針，仍需在 SnapBar 發佈包驗收。** 本機用 .NET 10 self-contained win-x64 探針從 ZIP 解壓執行。Windows OCR 在無套件識別時可執行，英文正確，繁中漏掉首字「繁」。微軟的正式支援範圍要求桌面 App 具備套件識別。[Windows OCR 文件](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr)
- **Tesseract 是現行 ZIP 路線的優先候選。** 使用 Tesseract 5.2.0 與 `tessdata_fast` 4.1.0，以單行模式辨識相同的兩張合成圖，英文與繁中均正確。單次測試含引擎初始化分別為 93 ms 和 71 ms。兩個模型及兩個原生 DLL 合計約 12.8 MiB，為未壓縮大小。整合前仍須在乾淨 Windows 驗證執行階段依賴、第三方授權及真實素材。[Tesseract 文件](https://tesseract-ocr.github.io/tessdoc/) · [語言模型](https://github.com/tesseract-ocr/tessdata_fast)

## 第一階段：動作快捷鍵

- [x] 在「一般 → 選字快捷鍵」沿用既有錄製器，提供「搜尋」與「複製」的選填綁定。新綁定預設空白，既有設定載入後維持原有快捷鍵。
- [x] 延伸 `SettingsStore`、`GlobalHotkey` 與 `InputMonitor`。驗證重複按鍵和系統占用，沿用先註冊再保存的流程。失敗時保留原註冊與偏好，錄製按鍵期間暫停執行。
- [x] 在 `SelectionController` 將快捷鍵導向現有的選字與動作執行流程。每次按鍵取得當前選取，沿用密碼欄位拒絕、焦點驗證與過期取消。成功時直接執行。
- [x] 通過快捷鍵專項桌面驗收。Core 465、Lifecycle 72、WebActions 21 和 Compatibility 21 項檢查通過。WebActions 覆蓋 Windows 熱鍵訊息直接搜尋唯讀選字、實體 Ctrl + Alt + B 複製、切換焦點取消與無選取時取消。複製案例使用測試文字並還原剪貼簿。Keyboard 與 Actions 各有一項「更多」選單失敗，使用同一份既有選單改動、移除本階段快捷鍵變更的基線也能重現。

## 第二階段：原地翻譯

- [x] 首版接 Azure Translator Text API，省略來源語言以使用自動判斷，預設目標語言為繁體中文。採使用者自備資源金鑰，翻譯設定獨立以 Windows 加密保存。[API 規格](https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/v3/translate) · [用量限制](https://learn.microsoft.com/en-us/azure/ai-services/translator/service-limits)
- [x] 新增獨立的「翻譯」動作，預設收在「⋯」，可綁定動作快捷鍵。介面標示供應商，使用者點擊翻譯或按下專屬快捷鍵後才傳送選字。缺少憑證時提供設定入口。免填金鑰留待供應方式確立後評估。
- [x] 在選字附近顯示精簡譯文小窗，提供複製與替換。來源可編輯且重新驗證通過時才允許替換。重用選取驗證、取消及寫回機制。翻譯動作可在 AI 服務尚未設定時使用。
- [x] 以測試服務驗收未設定 AI 也能翻譯、語言參數、傳送時機、取消、逾時、唯讀來源及選取變動後的替換限制。WebActions 31／0，Appearance 167／0，Keyboard 26／0，Lifecycle 72／0，AiRuntime 117／0，AiPanel 251／0。
- [ ] 使用有效 Azure 資源執行正式服務煙霧測試，確認英譯繁中及實際資源區域設定。桌面測試使用模擬 HTTP 回應。

## 第三階段：框選 OCR

### 2026-11-06 發佈可行性驗收

- 本機通過。將目前工作區的 self-contained win-x64 SnapBar App、獨立 OCR 探針、原生 DLL 和兩個語言模型放入同一個暫存 ZIP。解壓後，SnapBar 通過 `test-startup.ps1`，獨立探針從系統目錄啟動也能載入引擎。
- 同一組繁中與英文多行合成文字，以 PNG 及經驗證無文字層的影像 PDF 渲染圖分別送入探針。兩者忽略空白後均符合預期，繁中輸出仍有額外空格。
- 現存舊版 ZIP 的啟動檢查逾時。通過的是目前工作區另行發佈的暫存 ZIP。本機沒有可直接使用的乾淨 Windows 測試環境。正式發佈包、實際掃描素材、框選介面與混合 DPI 仍待驗收。
- 補測三份公開掃描 PDF（`epson.pdf`、`ccitt.pdf`、`c03-29.pdf`）。確認測試頁無文字層，渲染後各裁切一至兩行英文，三個裁切均讀對。繁中仍只有合成圖證據。[OCRmyPDF 測試素材](https://github.com/ocrmypdf/OCRmyPDF/tree/main/tests/resources)
- 暫存 ZIP 的 OCR 探針、模型與原生檔案合計約增加 6.6 MiB 壓縮資料。PE 匯入表與執行中的模組都確認需要 `MSVCP140.dll`、`VCRUNTIME140.dll` 和 `VCRUNTIME140_1.dll`。本機從 System32 載入這些 DLL，因此這輪只證明有既存 VC++ 執行階段的機器能跑。要維持解壓即用，下一道門檻是在乾淨 Windows 驗證隨包提供合規的 app-local DLL，或驗證不依賴系統 VC++ Redistributable 的建置。第三方聲明需涵蓋實際採用的 wrapper、Tesseract、Leptonica、模型與其隨附依賴。[Microsoft 部署文件](https://learn.microsoft.com/en-us/cpp/windows/deployment-in-visual-cpp) · [再分發條件](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files)

- [ ] 先驗證 Tesseract 與英文、繁中模型在乾淨 Windows 能隨 SnapBar x64 ZIP 解壓即用。核對原生執行階段依賴、再分發授權、授權聲明及 ZIP 增量，再接框選介面。
- [ ] 從系統匣或使用者自訂快捷鍵啟動框選。首版支援在任一螢幕內框選，並處理混合 DPI。以實際掃描 PDF 和圖片驗證繁中、英文及多行辨識。
- [ ] 擷取與辨識放在 Windows 層，框選與可編輯結果放在 App 層。以結果面板的文字作為動作來源，提供複製、搜尋、翻譯。OCR 來源沒有原始文字欄位，因此結果面板不提供寫回原畫面的操作。
- [ ] 驗收取消框選、無辨識結果、語言不可用、混合 DPI、掃描 PDF 與圖片。擷取內容留在本機工作階段，使用者主動執行翻譯時才沿用第二階段的傳送流程。

## 共通驗收

- [ ] 每階段執行 `scripts/check.ps1` 與對應的桌面套件。三階段整合後跑完整 Release 桌面套件，並把跨 App、DPI 與手動驗證結果記入 `docs/p0-validation.md`。
- [ ] 檢查快捷鍵、翻譯與 OCR 的取消和失敗路徑。診斷紀錄不包含選取文字、剪貼簿內容或擷取影像。

首版將離線字典、朗讀、QR Code、筆記和單位換算留待獨立評估。
