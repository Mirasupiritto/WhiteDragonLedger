# 深空當鋪原生 API 查詢

開發白龍店帳功能前，先用獨立的 white-dragon-native-api MCP 查遊戲已有的型別、簽名和使用位置。它讀取本機安裝的 Assembly-CSharp.dll 建立索引，遊戲關閉時也能查。

| 工具 | 用法 |
|---|---|
| native_api_info | 核對 Steam build ID、程序集版本、MVID 和 SHA-256 |
| search_native_api | 用不區分大小寫的關鍵字找型別、方法、欄位或屬性，例如 GraphUtils.TryAcceptOnce，也可以分開輸入 waterstrip currentwatercontainer |
| get_native_symbol | 把搜尋回傳的 id 原樣帶入，查看成員簽名或型別自己宣告的成員。kind 可縮小成員範圍 |
| find_native_references | 用方法或欄位的 id 查傳入的呼叫或存取。direction=outgoing 可查該方法直接使用的其他簽名 |

例如先找 GraphUtils.TryAcceptOnce，再查它的傳入引用，找遊戲搬貨時哪些方法用它。屬性的用途可沿 getter 或 setter 的 id 查。工具回傳 next_offset 時可帶入 offset 繼續查。

引用來自 Assembly-CSharp.dll 方法內的靜態 IL，包括直接呼叫、建構、方法指標和欄位存取。Unity 場景綁定、反射、與虛擬方法在執行時分派到哪個實作，要另外從遊戲行為判定。外部程序集只列被用到的引用簽名。

## ModScanner 反射規則（W0 調查）

- 依據遊戲 `ModScanner.cs`、`ModCapability.cs` 與 `ModSecurityDebug.cs`：
  - `ModCapability.Retiring` 包含 `"reflection-external"`、`"native"`、`"dynamic-code"`、`"threads"`。目前 `REFUSE_UNSCANNABLE_CAPABILITIES` 為 `false`，預定於 **2026-12-10** 改為拒絕載入（`Hard`）。
  - 動態／計算型別反射（例如 `obj.GetType().GetProperty(...)`）無法靜態解析型別來源，會觸發 `ReflectionExternal(... "on a computed type")`（`reflection-external`）。
  - 靜態 `typeof(遊戲型別).GetField/GetMethod(...)` 可由 `TryResolveTypeSource` 解析為遊戲內型別（`IsExternalScope` 為 `false`），不會觸發 `reflection-external`、`Pending` 或 `Hard`；但仍應優先使用公開原生 API，僅於無等效公開成員時保留並註記。

## 重建與註冊

用 PowerShell 7 執行。索引和遊戲程序集留在本機，產品資料夾在 Git 儲存庫外。

~~~powershell
./scripts/build-native-api.ps1 -GameDir 'E:/SteamLibrary/steamapps/common/Probably Stolen' -OutputDir 'D:/workspace/WhiteDragonLedger-products/native-api'

codex mcp add white-dragon-native-api -- 'D:/workspace/WhiteDragonLedger-products/venv/Scripts/python.exe' -B 'D:/tools/WhiteDragonLedger/mcp/native_api_mcp.py' --index 'D:/workspace/WhiteDragonLedger-products/native-api/native-api-index.json'
~~~

完成註冊後，新開 Codex 聊天即可使用。Python 環境沿用根目錄 requirements.txt 的 MCP 依賴。遊戲更新或搬家後，同路徑重建索引並重新連線。服務啟動時會比對程序集 SHA-256，檔案變更後需要重建才能啟動。
