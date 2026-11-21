# 白龍店帳 · White Dragon Ledger

白龍店帳讓 AI 助手透過 MCP 經營《深空當鋪》（Probably Stolen）。玩家載入存檔後，可以直接問「今天要留多少房租？」或「這批貨賣回了多少錢？」。要助手接客時，它能讀客人的原話、看剩餘預算、逐件設底價，成交後從遊戲回執確認實收。

**模組 0.9.5 · Windows · 遊戲版本 0.46D · MIT**。店務透過本機 stdio MCP 提供，宿主支援工具呼叫和文字回執就能使用，無需 Computer Use。物品鑑定、包裝驗偽和通緝照比對留給玩家，日常交易也就省下反覆傳送截圖的影像 token。

## 先看一筆實際交易

第 4 欄測試檔的第 10 天，店裡有 218 點。口渴的星際客說想喝基礎水，預算 96。助手挑了展示櫃的 26 號基礎水，底價設為 35。

| 遊戲回執 | 這筆交易的結果 |
|---|---|
| 報價與實收 | 報價 58，實收 58 |
| 雙方餘額 | 店款 218 → 276，客人預算 96 → 38 |
| 物品去向 | 26 號瓶交給買家 |
| 盤點次數 | 本次讀檔完整盤點 1 次 |

下一位客人明說不喝幽靈水。助手從櫃檯讀到這句台詞，也從售單確認 25 號瓶已退回展示櫃，店款仍為 276。這是 0.9.5 的實機紀錄。各項功能的驗收條件收在[工具索引與實機驗收表](AGENTS.md#實機驗收表)。

## 安裝與第一次連線

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

第一次連線可以讓助手做一次唯讀晨檢。

> 先用 `get_scene` 確認我載入了哪一欄。人在當鋪時，查一下今天第幾天、現金多少、離收租還有幾天。先不開門。

`get_scene()` 回到 `status=ready`、`location=pawnshop` 時，呼叫 `get_shop_status()`。回執的 `slot` 是存檔欄位，`phase` 是營業階段。要找貨可用 `query_inventory` 按品名、貨區及歸屬篩選。帳本預設為 `%LOCALAPPDATA%/WhiteDragonLedger/account.sqlite3`。需要另存帳本可指定 `--database`，自訂遊戲觀察目錄則用 `--journal`。

## 一天能做哪些事

| 時機 | 工具與回執 |
|---|---|
| 開門前算帳 | `get_shop_status` 讀現金、星期、兩週事件、交租倒數及夜間服務費。`get_trash` 分開列出桶內物品和垃圾堆積值。 |
| 整理貨架與機台 | `move_items` 一次搬一批自有貨，逐件回報實際位置。`inspect_item_window` 讀槽位與按鈕，`transfer_slot_item` 存取，`press_machine_button` 操作機台。固定檔案箱用 `inspect_archive`。 |
| 查昨晚放進去的水 | `inspect_purifiers` 把機號、瓶號、瓶貼、電力和全店剩餘試紙放在同一張回執。配貨可直接看淨化後的瓶貼。玩家決定抽試紙並打開結果時，助手可用 `water_panel` 對照同一瓶。 |
| 開門接客 | `open_shop` 開門，`ring_customer` 按鈴，也可用 `set_auto_receive` 自動接下一位。`wait_for_opening` 等開場說完，先看 `get_counter` 的台詞，再判斷客人想買、想賣或想交換。 |
| 向商人進貨 | `buy_merchant_batch` 依當次貨單與整批買入上限成交，回報實付和餘款。`query_purchase_lots` 可按供應商、品名或批次查實付、回款、剩貨，售完再看整批毛利。 |
| 把貨賣給客人 | `get_buyer_context` 查剩餘預算，`sell_order` 逐件設底價。客人插話時保留原單。若回 `sale_pending`，沿用原參數查結果。可以設定同款首件因預算不足被退回，就收起其餘同款。 |
| 聯絡人與情報 | `get_phone_contacts` 查聯絡人和業務，再決定是否撥號。遇到情報商，先看收據報價，再用 `pay_info_receipt` 付款，回執附實扣與餘款。 |
| 送客與打烊 | `dismiss_customer` 送走當前來客。`close_shop` 在提早打烊前先回報剩餘人數，確認後才放棄隊列。歇業後另用 `sleep` 確認服務費並推進一天。 |

## 場景與玩家分工

**夜間選地。** 玩家打開選地畫面，`get_scene` 回報 `selecting_destination`。此時呼叫 `get_travel_options` 可讀當頁每張地點卡的名稱、事件文字、可選狀態，以及目前選中的地點和出發按鈕狀態。玩家翻頁後可再查。出發後用 `wait_for_scene(session, after_revision)` 等抵達。途中和拾荒地點只提供場景工具。0.9.5 的選地查詢已通過回放，仍待與實際遊戲畫面對照。選地出發、夜間搜尋與取物目前由玩家操作。

**以物易物。** 血袋拾荒者在遊戲裡會給三個交換報價。0.9.5 實測能讀到他想換食物的台詞，但櫃檯回執是 `not_selling`，缺少各選項要交出及可換回的物品。遇到這位客人，先留在櫃檯讓玩家看報價。交換選項的讀取與選擇待接。

**目視判斷。** 來客像不像通緝照、包裝細節是否可疑，由玩家看畫面決定。若希望助手一起看，可把關鍵部位的截圖交給具備圖像理解能力的模型。喬的獨立人脈服務視窗也仍需玩家操作。

## 店帳如何更新

讀檔時，模組完整盤點庫存、展示櫃、前櫃及後櫃一次。之後記錄物品的移入、移出與數量變化。MCP 查帳時只把新增紀錄匯入 SQLite，換會話仍可查同一本帳。兩週日曆在讀檔、跨天或事件變動時重讀。機台視窗可帶上次的 `window_token` 作為 `since_token`，只取有變動的欄位。

| 回執欄位 | 何時用 |
|---|---|
| `generation`、`revision` | 識別本次讀檔和觀察版本。重新讀檔後從新回執取得物品編號。 |
| `load_scans`、`full_scans` | 分別檢查本次讀檔、這次模組啟動以來完整盤點了幾次。 |
| `applied_records` | 本次查帳新匯入 SQLite 的紀錄數。 |
| `visit`、`offer_token` | 對回同一位客人與同一張買入報價。遇到貨單變動，先重讀再決定。 |

## 開發與驗證

0.9.5 已使用遊戲 0.46D 的程序集編譯，30 項 Python 測試與遊戲端回放通過。要看各工具已在哪個存檔實測、還缺哪種來客或場景，請查[實機驗收表](AGENTS.md#實機驗收表)。

沿用安裝步驟的 PowerShell 變數，產物和測試資料會寫到倉庫外。

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

## 來源與授權

用 MCP 操作當鋪的構想受 Yideng 與貢獻者的 *Probably Stolen MCP Bridge* 啟發。白龍店帳依店務需求及本機遊戲介面獨立撰寫，建置時引用玩家已安裝的遊戲程序集。原始碼與文件採用 [MIT License](LICENSE)。
