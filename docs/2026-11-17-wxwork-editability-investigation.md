# 企業微信輸入框：可編輯性與寫入能力驗證

## 結果

使用者實體鍵盤測試確認：目前企業微信測試草稿的選取文字可以被普通字元替換，且 Ctrl+Z 已復原。未按 Enter，未送出訊息。因此輸入框具備實際編輯能力；目前 SnapBar 顯示「不提供寫入」來自相容來源的固定唯讀政策，不能據此把該輸入框判定為唯讀。

本輪以獨立、唯讀診斷程式檢查前景企業微信的原生焦點、系統 caret、MSAA 角色／狀態及 UIA pattern 可用性。不讀取控制項名稱、值、選字或剪貼簿內容。診斷程式位於被忽略的 `artifacts/investigations/wxwork-editability/`，建置零警告、零錯誤；產品原始碼及寫入政策未改動。

## 實際訊號

在目前視窗與配置下觀察到：

| 狀態 | UIA Text／Value pattern | 原生焦點／滑鼠 HWND | MSAA 角色／狀態 | 原生 caret |
| --- | --- | --- | --- | --- |
| 輸入框鍵盤選字、雙擊選字、拖選 | 均無 | 同一 WeWorkWindow | client 10／0x100004 | 有，位於草稿第一行，高 19 |
| 聊天訊息選字 | 均無 | 同一 WeWorkWindow | client 10／0x100004 | hwndCaret=0、空矩形；MSAA caret 為 invisible 0x8000 |
| 搜尋框聚焦 | 均無 | 同一 WeWorkWindow | client 10／0x100004 | 有，位置移到搜尋框 |

已核對的原始紀錄：`input-keyboard-selected.json`、`input-double-click-selected.json`、`input-pointer-selected-verified.json`、`message-selected-verified.json`、`search-focus-verified.json`，均在上述 artifacts 目錄，不含文字內容。

草稿 caret 的 client Y 範圍約 650–669，搜尋框約 27–46，兩者 HWND 相同。這些數字只是本機量測結果，不能成為硬編碼的輸入框位置。應用程式移動、縮放、DPI 或版面改變時須重新定位與驗證。

目前 NativeClipboardSourceReader.SameSource 比對 HWND、類別、角色及 child ID。以上輸入框與搜尋框會產生相同來源欄位，因此現有「讀取來源未變」驗證不足以授權寫入。直接將 Editable 改為 true 或移除 clipboard 寫入限制，會遺漏這個來源區別。

## 測試途中與限制

- 自動化文字投遞沒有形成可確認的草稿替換，單鍵投遞曾出現輸入法組字，已用 Esc 取消；未將自動化輸入回傳成功當作實際寫入成功。實際寫入及復原的正向證據來自使用者回覆「能替換且已復原」。
- 早期座標操作包含帶陰影的 composite screenshot；輸入框一次拖曳未得到可靠選取，搜尋框首次也未實際聚焦。這些紀錄不作正向證據。後續改以 1080×760、origin 0 的主客戶區 screenshot，並以選取反白／搜尋框邊框與原生 caret 位置核對後重測。
- 一次使用者回覆後，滑鼠位於企業微信以外，NativeClipboardSource 為 null；這是原有滑鼠歸屬檢查結果，不代表輸入游標消失。不能用該筆資料證明原生來源驗證成功。
- [GUITHREADINFO 文件](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-guithreadinfo) 指出 GUI_CARETBLINKING 是當下可見／閃爍狀態，rcCaret 相對 hwndCaret 的 client 座標。不能只因閃爍旗標暫時為零便判定唯讀，座標比較也須正確轉換。
- caret 不是所有 App 都可編輯的證明；例如 [WPF 明確支援唯讀文字框顯示 caret](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.primitives.textboxbase.isreadonlycaretvisible)。上述對照只支持目前企業微信配置存在可用區別，尚未證明通用可靠性。
- 尚未驗證剪下、貼上、SnapBar 替換動作、多行／RTL、其他企業微信版本、登入欄位、混合 DPI、視窗變形及非同步結果返回後的寫回。沒有把未通過的情境當作已支援。

## 後續實作依據

建議保留 UIA 的原有可編輯性判斷，對已驗證的企業微信來源另建立與單次選取綁定的寫入資格。來源焦點、原生 caret 與選取手勢位置須互相符合；寫入前須重新核對來源位置及選字，切到聊天區、搜尋框或其他 App 後拒絕原資格。讀取成功本身仍不授權寫入。

三種方向的取捨：全面以 caret 授權無法排除唯讀欄位；畫面／固定座標判斷易受版面改變影響；以標準可編輯證據為主、少數已驗證來源作有界適配，可沿用共用讀取流程而保持來源重驗。下一階段應先驗證來源綁定和失效拒絕，再以可還原草稿測試剪下、貼上及替換。此次只完成能力調查，未開放產品寫入。
