# 白龍店帳 · White Dragon Ledger

讓 AI 助手透過 MCP 在《深空當鋪》查帳、搬貨和做生意。遊戲模組讀取櫃檯台詞、商品和機台狀態，透過文字回執告訴助手發生了什麼。支援 stdio MCP 的宿主就能使用，包含沒有 Computer Use 的環境。

目前版本 **0.4.1**，對應 Windows 遊戲版本 **0.46D**。

## 一張貨單怎麼用

一次實機交易裡，拾荒客進門後先說「我正在囤水」，接著說自己有 149 信用點。他擺出一瓶小瓶水和一瓶標準水，瓶貼都是「沟渠水」，整批開價 9。助手把買入上限設為 9，成交回執確認實付 9、店家現金從 218 變成 209。兩瓶都進了庫存，遊戲端的完整盤點次數仍是 1。

1. 玩家開門後，呼叫 `get_counter()` 看排隊人數。櫃檯空著時，把回執裡的 `generation`、`revision`、`waiting` 傳給 `ring_customer`。
2. **0.4.1 起，貨單要等開場對話結束才成立。** 即使貨已擺好，入場動畫、台詞打字和最後一句仍在顯示時，都不會發出購買憑證。普通翻頁會接續，遇到選項或對話暫停則交回控制。若回執為 `still_waiting`，用 `wait_for_opening(generation, visit)` 繼續等同一位客人。
3. 讀取貨單上的品名、逐件編號、數量、可見瓶貼和整批開價。把這張單的 `generation`、`revision`、`visit`、`offer_token` 與自訂的 `max_total` 傳給 `buy_merchant_batch`。普通商人按整批成交價加總，批發商採用遊戲的折扣總價。
4. 收到 `purchased` 後，從 `paid` 看實付、`cash_after` 看餘額。模組會核對現金差額和每件貨的歸屬。貨單變動、超過上限或餘額不足時會停手。

## 安裝與第一次查帳

需要已安裝的遊戲、Windows、.NET SDK、Python 3.11 以上，以及支援 stdio 的 MCP 宿主。在倉庫根目錄開啟 PowerShell，將 `$game` 改成自己的 Steam 安裝路徑。下例把編譯產物和 Python 環境放在倉庫旁邊。

```powershell
$game = 'E:/SteamLibrary/steamapps/common/Probably Stolen'
$work = Join-Path (Split-Path -Parent (Get-Location).Path) 'WhiteDragonLedger-products'
./scripts/build.ps1 -GameDir $game -OutputDir "$work/build"
python -m venv "$work/venv"
$python = "$work/venv/Scripts/python.exe"
& $python -m pip --cache-dir "$work/pip-cache" install -r requirements.txt
```

1. 將 `$work/build/white_dragon_ledger` 資料夾複製到遊戲的 `Mods` 裡。確認 `Mods/white_dragon_ledger/` 直接包含 `manifest.xml` 和 `WhiteDragonLedger.dll`。
2. 啟動遊戲，啟用「白龍店帳 · White Dragon Ledger」並載入存檔。升級模組後請重啟遊戲再讀檔。
3. 在 MCP 宿主新增 stdio 服務。以下為採用 `mcpServers` 格式的設定範例，請替換兩個絕對路徑。其他宿主使用相同的命令和參數。

```json
{
  "mcpServers": {
    "white-dragon-ledger": {
      "command": "D:/tools/WhiteDragonLedger-products/venv/Scripts/python.exe",
      "args": ["-B", "D:/tools/WhiteDragonLedger/ledger_mcp.py"]
    }
  }
}
```

第一次可在歇業時呼叫 `query_inventory(name="水", owned=true)`。回執中的 `slot` 是存檔欄位，`phase` 是營業階段，`items` 列出符合條件的物品。帳本預設位於 `%LOCALAPPDATA%/WhiteDragonLedger/account.sqlite3`。進階設定可用 `--database` 指定帳本，或用 `--journal` 指向遊戲的 `ModData/white_dragon_ledger` 觀察目錄。

## 其他店務工具

| 想做的事 | MCP 工具 | 回執能確認什麼 |
|---|---|---|
| 按品名、貨區或歸屬找貨 | `query_inventory` | 逐件編號、數量、位置和可見瓶貼。四個貨區為庫存、展示櫃、前櫃及後櫃的頂層。 |
| 歇業時搬一件或一批貨 | `move_item`、`move_items` | 在庫存與展示櫃間移動自有物品。批次回執逐件列出實際位置，遇到未確認的動作就停。 |
| 晨檢、從淨水器取瓶 | `inspect_purifiers`、`take_purifier_bottle` | 瓶號、瓶貼、電力、與昨晚是否同一瓶、可否取出，以及全店可用試紙數。取瓶會核對原槽已空、瓶子已到庫存。 |
| 分批賣貨給買家 | `get_buyer_context`、`sell_order` | 先讀剩餘預算，再逐件設底價。`stop_same_kind_on_budget=true` 可在首件因預算不足退回時收起餘下同款，其他品種繼續談。 |
| 查一批進貨收回多少 | `query_purchase_lots` | 按供應商、品名或批次查整批實付、累計回款及在庫件數。整批售完才列毛利。 |

批發帳依物品的存檔識別碼追蹤。舉例來說，四件共花 9，賣出第一件收 10 時，回執為 `paid=9`、`recovered=10`、`in_stock=3`。若最後共收 40，整批毛利就是 31。未能核實的付款會標成 `payment_status=unconfirmed`；未售且目前找不到的物品另列為 `unlocated`。

## 為什麼連查三次只盤點一次

讀檔時，遊戲端列舉四個貨區一次，寫下庫存基準。之後按物品變動發布增減記錄。MCP 查帳時只將尚未匯入的記錄寫入 SQLite，再從本機帳本查詢。即使換了 AI 會話，也可以接著讀同一份帳。遊戲退出後，查到的是最後發布的狀態。

| 欄位 | 用途 |
|---|---|
| `generation`、`revision` | 分別識別本次讀檔和觀察版本。重新讀檔後，請從新回執取得物品編號。 |
| `load_scans`、`full_scans` | 分別是本次讀檔和本次模組啟動以來，遊戲實際完整盤點的次數。 |
| `applied_records` | 本次查帳新匯入 SQLite 的記錄數。 |
| `phase` | `MORNING` 為尚未開門，`OPEN` 為營業中，`CLOSED` 為歇業。 |

若收到 `stale_observation`，先重查目前的版本和位置。若提交後回執中斷，請保留原參數，單件搬貨可在送出前指定 `command_id`，批次搬貨與買貨則可用同一組參數讀回既有結果。

## 開發與驗證

庫存增量、搬貨、晨檢、取瓶及普通商人限價購買已用遊戲存檔實測。0.4.1 的開場完成判定已通過流程回放，仍待新版實機確認。批發回本和同款預算退貨也有回放測試。

沿用安裝步驟的 PowerShell 變數即可重跑測試。

```powershell
$env:LEDGER_TEST_TEMP = "$work/tests"
New-Item -ItemType Directory -Force $env:LEDGER_TEST_TEMP | Out-Null
& $python -B -m unittest discover -s tests -p 'test_*.py'
./scripts/build.ps1 -GameDir $game -OutputDir "$work/build" -TestDesk
```

要量遊戲端的盤點次數，先載入歇業測試檔，執行以下兩步，中途手動將一瓶自有的水移到另一個貨區。`before` 連查三次，`after` 核對同一瓶的位置變更及盤點計數。證據檔寫入指定的外部目錄。

```powershell
$journal = Join-Path $env:USERPROFILE 'AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData/white_dragon_ledger'
$measure = @('--journal', $journal, '--database', "$work/state/account.sqlite3", '--output', "$work/tests/live")
& $python -X utf8 -B tests/measure.py before @measure
# 在遊戲裡移動一瓶水後再執行下一行
& $python -X utf8 -B tests/measure.py after @measure
```

## 來源

用 MCP 操作當鋪的構想受 Yideng 與貢獻者的 *Probably Stolen MCP Bridge* 啟發。白龍店帳依店務需求及本機遊戲介面獨立撰寫，建置時引用玩家已安裝的遊戲程序集。
