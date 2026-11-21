# 白龍店帳

《深空當鋪》0.46D 的本機 Mod 與 stdio MCP。店帳觀察庫存、展示櫃、前櫃和後櫃。遊戲模組使用公開讀檔及貨區回呼，MCP 從 SQLite 查詢最後一份已發布的店帳。

## 查帳

`query_inventory` 支援品名子字串、位置與歸屬交叉篩選。位置可填 `inventory`、`showcase`、`front`、`back`。例如 `{"name":"水","area":"showcase","owned":true}`。兩瓶同名的水各有自己的編號，編號跟隨該物品直到下次讀檔。同欄位重讀也會得到新的 `generation`。

讀檔完成後，模組列舉四個貨區一次，寫出基準。放入、取出或堆疊數量變更時，模組在該幀結束前只讀取收到通知的物品。跨幀拖曳可能先移出、再放入。SQLite 一次交易會依序套用上次查帳後尚未匯入的記錄。遊戲退出後，查到的是最後發布的狀態。這一版只追蹤頂層物品，歸屬及顯示名在觀察到該物品時取樣。

| 回執欄位 | 如何解讀 |
|---|---|
| `load_scans` | 模組這次讀檔實際完整盤點的次數，由遊戲端盤點入口計數 |
| `full_scans` | 這個模組實例啟動以來的完整盤點次數 |
| `revision` | 這次讀檔最新發布的庫存或營業階段記錄編號 |
| `applied_records` | 本次查詢新匯入 SQLite 的記錄數 |
| `phase` | 遊戲原生營業階段，`MORNING` 為尚未開門，`CLOSED` 為歇業 |

## 歇業搬貨

0.2.0 新增 `move_item`。從 `query_inventory` 取這次讀檔的 `generation`、最新 `revision` 和物品編號，填入 `item_id`、`source`、`destination` 即可搬動一件自有物品。庫存（`inventory`）和展示櫃（`showcase`）可雙向移動。遊戲處於 `MORNING` 或 `CLOSED` 時才會執行。

遊戲在主執行緒呼叫原生搬貨方法，更新逐件帳本，再核對該物品的實際貨區和數量。`status="moved"` 表示已確認抵達，回執內的 `actual_area`、`revision`、`load_scans` 和 `full_scans` 直接來自這次操作。讀檔的完整盤點次數可從回執直接查看。

每次命令有獨立的 `command_id`。客戶端可以預先指定 UUID 的 32 位小寫十六進位字串，以便連線中斷後用原參數查回執。收到 `unconfirmed` 時先用原命令編號核對；收到 `stale_observation` 時重新查當前位置和版本。已送出而延遲的命令有六秒有效期。現有遊戲程序升級後需重啟並重新載入存檔。

### 一次搬多件

`move_items` 接受依序排列的 `item_ids`（例如 `[25,26]`），其餘參數與單件搬貨相同。一次 MCP 呼叫會取得一張回執；每列包含物品編號、結果和遊戲確認的 `actual_area`。回執的 `load_scans` 為本次讀檔的盤點次數，`full_scans` 為模組本次啟動的累計值。

每件使用上一件回執的版本號。遇到拒絕或結果未確認時，已搬的物品留在目的地，後續項目標記為 `not_attempted`。用完全相同的欄位與原始版本重送，可取回已完成項目的回執，並接續尚可執行的項目。

## 建置與安裝

從專案根目錄執行以下 PowerShell 命令。需要 Windows、.NET SDK、Python 3.11 以上及自己的遊戲安裝。下例將建置、測試、快取和執行環境放在倉庫外。

```powershell
$work = 'D:/workspace/WhiteDragonLedger-products'
$game = 'E:/SteamLibrary/steamapps/common/Probably Stolen'
./scripts/build.ps1 -GameDir $game -OutputDir "$work/build"
python -m venv "$work/venv"
$python = "$work/venv/Scripts/python.exe"
& $python -m pip --cache-dir "$work/pip-cache" install -r requirements.txt
```

將外部建置目錄裡的 `white_dragon_ledger` 放進遊戲的 `Mods`，啟用「白龍店帳」並載入存檔。遊戲觀察記錄寫在遊戲自己的 `ModData/white_dragon_ledger`。模組使用獨立 ID，無須開網路連線。

在 MCP 宿主新增 stdio 服務，命令為上述 Python，參數依序為 `-B`、本倉庫 `ledger_mcp.py` 的絕對路徑。可用 `--database` 指定倉庫外的 SQLite 檔，預設為 `%LOCALAPPDATA%/WhiteDragonLedger/account.sqlite3`。用 `--journal` 可指定其他遊戲觀察資料夾。

## 重跑驗收

```powershell
$env:LEDGER_TEST_TEMP = "$work/tests"
New-Item -ItemType Directory -Force $env:LEDGER_TEST_TEMP | Out-Null
& $python -B -m unittest discover -s tests -p 'test_*.py'
$journal = Join-Path $env:USERPROFILE 'AppData/LocalLow/Questing Goose Studio/Probably Stolen/ModData/white_dragon_ledger'
$measure = @('--journal', $journal, '--database', "$work/state/account.sqlite3", '--output', "$work/tests/live")
& $python -X utf8 -B tests/measure.py before @measure
# 在遊戲裡手動把一瓶自有的瓶裝水挪到另一個貨區。
& $python -X utf8 -B tests/measure.py after @measure
```

第一步發出三次 MCP 查詢，核對篩選結果和遊戲端計數。第二步比對整份前後庫存，檢查只有同一瓶水的位置改變，並再次核對盤點計數。兩份實測回執寫在 `--output` 指定的位置。

## 來源

用 MCP 操作當鋪的構想受 Yideng 與貢獻者的 Probably Stolen MCP Bridge 啟發。本倉庫依店務需求及本機遊戲介面獨立撰寫，建置時引用玩家安裝的遊戲程序集。
