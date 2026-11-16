# 相容選取的單次寫入資格

使用者已核准原輸入框可還原寫入、聊天訊息拒絕、切換搜尋框／其他 App 失效及非同步舊結果拒絕的實作驗收。沿用 UIA 優先與共用 clipboard fallback，不把所有相容來源改為可寫入。

Probe 僅對已驗證的企業微信原生來源，在系統 caret 與 MSAA caret 均有效、同屬焦點控制項、無已知保護旗標、選字手勢終點與 caret 相符時，建立與候選 token 綁定的資格。資格保存 caret 的 client 位置與 native 來源，不以固定螢幕座標或程序級快取授權。缺少正向證據仍可讀但不可寫入。

剪貼簿讀取前後及寫入前重新核對資格、候選生命週期、目標、焦點、游標位置及選字。位置改變、caret 消失或 source 改變即拒絕，不能從普通驗證重新升級為可寫入。Core 要求相容選取同時帶有明確 Editable=true 及相符候選寫入 token。App 不自行從 HWND 推定可編輯。

標準 UIA Edit 來源若不提供 TextPattern，但明確提供 ValuePattern.IsReadOnly=false，可依同一 token 流程取得資格；仍要求焦點、可見、啟用、非密碼且 runtime ID 重驗。這不需要 App 名單，也不把未知 opaque Window 視為可寫入。測試使用獨立程序的標準 UIA Value provider，驗證真正的剪下、貼上及替換路徑。

Paste 使用讀取前剪貼簿序號防止驗證期間第三方更新；驗證暫時複製仍完整還原。寫入前另執行 Probe 資格重驗，並再檢查目前選取的 generation 及前景。AI／自訂替換沿用資格和原文重驗。缺少資格或驗證失敗不送 Ctrl+X／Ctrl+V。

測試分為 Core 正反例、原生 caret 量測、App 寫入及非同步失效案例、既有剪貼簿相容回歸，以及企業微信可還原草稿的實機驗收。此能力以已驗證的來源適配為界，不對未知自繪 App 全面開放。
