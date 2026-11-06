# SnapBar 離線查詞首版

選取英文單字或短語後，從 SnapBar 的「⋯」點「查詞」，在選取處附近顯示詞條與釋義。這份設計取代 GoldenDict-ng 外部小窗原型的產品路線。SnapBar 直接讀取使用者匯入的詞典。首版採匯入方式，因為目前抽測的兩份資料還沒有同時達到繁中用語與散布來源的要求。

## 詞典抽測

同一組 16 筆包含常用字、不規則詞形、片語及 Windows 技術詞彙。以小寫精確比對詞頭，尚未做詞形還原。

| 候選 | 實測大小 | 命中 | 觀察 |
| --- | --- | --- | --- |
| [XDICT 英漢辭典修正版](https://stardict.uber.space/zh_TW/index.html) | 下載 3,581,717 bytes。索引 3,451,031 bytes，詞文解壓後 4,808,736 bytes | 14／16 | 缺 screenshot 和 clipboard history。部分詞採「軟件」、「剪貼板」、「鼠標」等用語，latency 只有一般義項。目錄標示 GPL，檔內中繼資料列出作者與轉檔沿革，未提供足以確認再散布權利的授權文字。 |
| [ECDICT 專案的基礎 CSV](https://github.com/skywind3000/ECDICT) | 66,009,548 bytes | 15／16 | 有 screenshot 與 latency 的技術義項，缺 clipboard history。資料含簡體中文，部分詞需要台灣用語校訂。上游專案標示 MIT。此處量的是基礎 CSV，發行版 StarDict 壓縮包未量測。 |

測試詞為 run、running、went、children、take off、in spite of、clipboard、screenshot、software、mouse、cache、on the fly、look up、privacy、latency、clipboard history。

## 匯入與查詢

- 設定提供「離線詞典」入口。使用者選取一個已解封裝的 StarDict 2.4.2 `.ifo` 檔，SnapBar 從同目錄讀取同名 `.idx` 與 `.dict` 或 `.dict.dz`。先支援 UTF-8 純文字釋義及音標，包含 XDICT 抽測檔使用的 `m`、`t` 欄位。可替換與移除詞典。
- SnapBar 驗證索引長度、詞文範圍和資料類型，限制檔案與解壓後大小。匯入成功後才切換目前詞典。遇到不支援的格式、損毀或無法讀取，顯示原因並保留前一份可用詞典。
- 優先查精確詞頭，接受大小寫差異、選取前後空白與常見外側標點。單字未命中時，才試有限的常見英文屈折變化，且必須命中詞典才顯示。短語用完整詞頭查詢。
- 沿用既有選取驗證與取消機制。結果以純文字顯示，可複製。未匯入時提供設定入口，查無詞條時顯示「詞典未收錄」。查詢全程在本機，線上翻譯留待另案處理。

## 驗收

以小型測試詞典覆蓋精確詞頭、短語、詞形還原、損毀索引、解壓上限與匯入失敗後保留前一份詞典。以真實詞典確認可從選字工具列查詞、結果出現在選取處附近、可複製釋義，以及切換焦點後不顯示過時結果。記錄第一次查詢與載入後查詢的時間。
