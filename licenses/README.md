# 第三方授權維護

使用者入口為根目錄的 `THIRD-PARTY-NOTICES.md`；發布時本目錄對應套件內的 `Licenses/`。上游授權文字保持原樣，不以 SnapBar 的 MIT 聲明取代。

## 已核對的來源

- Fluent Icons：版本與來源記錄於 `src/SnapBar.App/Assets/FluentIcons/SOURCE.md`，原始 LICENSE 與 NOTICE 隨套件交付。
- Tesseract wrapper、引擎、模型、Leptonica、InteropDotNet：來源記錄於 `src/SnapBar.Windows.Ocr/OcrData/SOURCE.md`，原始授權隨套件交付。
- OCR 內含影像程式庫：由 NuGet `Tesseract/5.2.0/x64/leptonica-1.82.0.dll` 的 `getImagelibVersions()` 直接取得版本。原文下載 URL 與 SHA-256 記錄於 `Ocr/SOURCES.json`。GIFLIB 來源為官方 SourceForge Git 的 `5.2.1` tag，commit `52b62de83d5facbbbde042b85bf3f61182e3bebd`；libtiff 授權取自 libsdl-org 的 `v4.4.0` 鏡像。
- liblzma：DLL 含有 `TIFFInitLZMA` 等編碼器實作字串，但未暴露可核實的精確版本。保留同時期 XZ Utils `v5.2.7/COPYING` 的完整授權說明，作為 5.2.x liblzma 公有領域來源的補充聲明，並不聲稱二進位版本為 5.2.7。若日後重建 OCR DLL，應保存完整依賴版本與建置清單。
- libtiff：另收錄 `v4.4.0` 函式庫原始碼檔首的版權與授權註解，包含 Pixar 等個別貢獻者的條款。依檔名排序後加入區段標題，原註解不改寫；來源壓縮檔與輸出文件雜湊均記錄於 `Ocr/SOURCES.json`。保守保留選用編碼器的來源聲明，不表示每個編碼器都已啟用。
- .NET：`Directory.Build.targets` 在 App 與 Probe 的自含式發布完成時，從實際解析到的 runtime packs 複製 LICENSE 與 THIRD-PARTY-NOTICES，並記錄版本。缺少 runtime 授權或 Core runtime 的第三方聲明會中止發布。不得以 SDK 根目錄的授權檔取代 runtime pack 內的文件。
- Windows Desktop runtime pack 未附獨立第三方清單，另保留上游 WPF 與 Windows Forms `v10.0.15` 的原始第三方聲明，涵蓋 zlib、Json.NET 與 Ookii.Dialogs 的致謝與條款；此清單可能包含 SnapBar 未使用的上游元件。來源與雜湊在 `WindowsDesktop/SOURCES.json`。已核對兩份上游文件與原收錄 `v10.0.13` 的檔案 SHA-256 相同。`Directory.Build.props` 固定自含式發布的 runtime 為 `10.0.15`，避免 SDK patch 差異讓本機與 CI 選到不同版本；發布目標仍會在未核對的 Windows Desktop runtime 版本被指定時中止。升級時須重新核對此處文件並同步更新版本設定與限制。

## 更新與交付

更新任何依賴時，重新核對實際二進位、上游授權及適用的 NOTICE；同步更新索引與來源雜湊。修改上游 Apache 授權檔案時須在修改檔案中明確標示。新增依賴不代表可以沿用既有元件的授權判斷。

套件封裝檢查會拒絕漏附已知必要聲明的套件。驗證時另外比對 runtime 聲明與實際 runtime pack 的內容，並核對來源雜湊。這些檢查防止文件遺漏，不能證明所有法律風險均已排除，尤其無法單靠套件名稱推知所有原生靜態依賴。
