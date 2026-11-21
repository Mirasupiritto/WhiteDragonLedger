# 白龍店帳 · White Dragon Ledger

讓 AI 助手透過 MCP 在《深空當鋪》查帳、搬貨和做生意。開門前，助手能從一張店況回執讀到現金、收租倒數和日曆事件。開門後，它能等客人說完需求，核對貨單和預算，再決定怎麼交易。

目前版本 **0.6.1**，對應 Windows 遊戲版本 **0.46D**。白龍店帳仍在早期開發階段，現階段優先支援店內查帳、搬貨和櫃檯交易，後續版本會逐步補上其他玩法。

## 第一次連線可以先查什麼？

載入存檔後，先呼叫 `get_scene()`。看到 `status=ready` 和 `location=pawnshop`，再呼叫 `get_shop_status()`。這兩步都是唯讀，開門前就能做。

以下是 0.6.0 在第 4 欄測試存檔的一次晨間回執。金額與事件會隨存檔改變。

| 開門前要決定的事 | 店況回執 |
|---|---|
| 今天要留多少房租？ | 第 10 天，星期三。現金 218，第 14 天收租 209，還有 4 天。現在留足房租，可動用 9 點。 |
| 哪幾天適合賣水？ | 供水危機列在第 10 至 12 天，遊客湧入列在第 10、11 天。 |
| 今晚服務費負擔得起嗎？ | 已選服務合計 24 點。如果選項不變，今天的現金需要淨增 15 點，才能同時留足房租與服務費。 |
| 垃圾桶要整理嗎？ | 桶內 0 件，垃圾堆積值 0。 |

## 哪些事可以交給 AI 助手？

**店務工具不依賴 Computer Use。** 白龍店帳在本機執行。只要宿主（harness）能啟動 stdio MCP 服務，讓助手呼叫工具並讀取文字回執，原則上就能使用下列店務功能。即使助手只能處理文字，也能從貨單讀出「第 29 號、小瓶水、瓶貼寫著沟渠水」，再決定這一批最多願意花多少。其他宿主可用下方的第一次查帳步驟驗證連線。

| 遇到的情況 | 建議分工 |
|---|---|
| 查帳、搬貨、淨水器晨檢、接客與限價買賣 | 交給助手。遊戲直接回報物品位置、可見瓶貼、客人台詞和實際成交金額。 |
| 鑑定物品、從包裝細節辨認仿冒品、比對人物照片 | 由玩家看畫面判斷。若要助手協助，需使用具備圖像理解能力的模型，並由玩家提供截圖或由宿主提供畫面。目前仍需從遊戲畫面取得這些影像。 |
| 打電話給聯絡人、更換業務 | 助手可查電話簿與自有名片、撥號、讀取通話選單，再明確選擇業務。喬的電話可進入獨立的人脈服務視窗，該視窗內的操作仍由玩家接手。 |
| 夜間外出拾荒 | 助手可辨識轉場與抵達地點。外出、搜尋與取物目前由玩家操作。 |

**建議日常店務交給助手，目視鑑定由玩家接手。** 反覆傳整張遊戲畫面、放大細節再比對，會增加影像 token 用量，也會占用可留給交易的上下文。遇到拿不準的物品時，再傳一張包含關鍵細節的局部截圖。像水瓶這樣的物品，助手能讀出瓶貼上的字，瓶內的實際水質仍要在遊戲裡驗。

## 場景切換

先用 `get_scene()` 查目前的位置。店內會列出店務工具；開始移動後，工具清單只保留 `get_scene`、`wait_for_scene`。宿主可在收到 MCP 的 `notifications/tools/list_changed` 後重讀清單。仍顯示舊按鈕的宿主呼叫店務時，也會收到場景回執。夜間地點的專用操作會在後續版本陸續加入。

遊戲確認目的地時，`transitioning` 回執的 `message` 會顯示「前往」及遊戲內的地名。要等候抵達，可將轉場前或轉場中的 `session` 和 `revision` 傳給 `wait_for_scene(session, after_revision)`。抵達後回傳 `ready`、目前地名和 `last_transition`；即使兩次查詢間已走完黑幕，也能讀到最近一次行程。回店會更新店務版本，搬貨與交易請使用本次回店後的回執。

## 日曆、垃圾桶與睡覺

`get_shop_status()` 一次查看現金、日曆、垃圾桶與夜間服務費。`get_counter()` 和 `query_inventory()` 也附日曆及垃圾桶回執。日曆有當日與星期、遊戲已公開的本週及下週事件，以及每七天到期的房租。看 `due_day`、`days_until_due`、`amount` 和 `cash_shortfall` 就能安排營收；若已轉為房貸或付清，`payment_type` 會跟著改變。

兩週事件在讀檔後建立一次，跨天或事件資料變動時重讀。遊戲提供星期與每週天數，現金與租金缺口仍依當下店況更新。`calendar.event_scans` 記錄本次讀檔重建了幾次兩週日曆，`calendar.event_day_reads` 是實際向遊戲逐日查詢的累計次數。一週七天時，一次完整讀取對應 14 次逐日查詢。

`get_trash()` 列出垃圾桶頂層每件物品的編號、品名及數量。桶內物品放入、取回或堆疊改變時由遊戲端記錄，下一次查帳時同步到該存檔欄位獨立的 SQLite `trash_items` 表。隔夜按遊戲實際清理結果更新，危險廢物等仍在桶內的物品會繼續列出。垃圾堆積值 `garbage_level` 和桶內堆疊數 `stacks` 分開顯示。

歇業時，拿當次店況的 `generation`、`revision`、`day` 與 `sleep.service_cost` 呼叫 `sleep`。工具走睡袋及日結按鈕，核對當晚已選服務的費用；歇業前、服務費變動或現金不足都會停下。`sleep_submitted` 表示已送出，`slept` 表示已觀察到隔天早晨，回執附新日曆及清理後的垃圾桶。

## 店帳的優勢

- **換會話也能接著查帳。** 每次讀檔完整盤點一次，之後按物品變動更新 SQLite。下一位助手可以按品名、貨區或歸屬查到現況，省去重讀對話和反覆盤點的時間。
- **一張回執就能核對多件貨。** 同名水瓶各有編號。一批搬完，助手能逐件確認哪瓶進了庫存、哪瓶還在展示櫃；中途停手也能看出哪些尚未處理。
- **交易照看過的貨單和價格執行。** 購買綁定當次來客與貨單，貨品或報價改變會要求重新確認。成交再核對實扣金額和逐件歸屬。賣貨可逐件設底價，也能在客人預算不足時收起餘下同款。
- **批發回款查得到原批次。** 每批各記一筆實付，售出時依物品識別碼累計回款。即使店裡有同名舊貨，也能查出這批還剩幾件、收回多少。

## 一張貨單怎麼用

一次實機交易裡，拾荒客進門後先說「我正在囤水」，接著說自己有 149 信用點。他擺出一瓶小瓶水和一瓶標準水，瓶貼都是「沟渠水」，整批開價 9。助手把買入上限設為 9，成交回執確認實付 9、店家現金從 218 變成 209。兩瓶都進了庫存，遊戲端的完整盤點次數仍是 1。

1. 晨間呼叫 `get_counter()`。若 `can_open_shop=true`，可將 `generation` 和 `revision` 交給 `open_shop` 開門；回執會確認 `phase=OPEN` 和排隊人數。玩家也可自行開門。櫃檯空著時，把回執裡的 `generation`、`revision`、`waiting` 傳給 `ring_customer`。
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

## 電話聯絡人

`get_phone_contacts()` 是唯讀查詢。在電話簿與店內自有名片中找到的聯絡人會合併為一列，附上來源、號碼、業務概述、是否會接聽，以及冷卻天數。`service_summary` 說明聯絡人通常能辦的事；當次實際可選的業務以通話選單為準。

決定撥號後，把同一張回執的 `number`、`generation`、`revision`、`phone_token` 傳給 `dial_contact`。再查一次電話，查看 `line` 和已顯示的 `choices`。普通對話若顯示 `can_advance`，用 `advance_phone_dialogue` 翻頁；只有明確提交 `choose_phone_service` 才會由助手點選業務。頁面或選項已變動時要重新查詢。

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

0.6.1 在第 4 欄第 10 天早晨，每隔 2.5 秒查一次店況，共三次。`event_scans` 三次皆為 1，`event_day_reads` 皆為 14。同日事件新增、改期、可見性改變及跨天重讀另由原生回放驗證。

0.6.0 已在第 4 欄實機確認當鋪就緒，以及店況回執內的日曆、租金、空垃圾桶和晨間就寢狀態。23 項 Python 自動測試與遊戲端回放已通過。睡覺推進日期、隔夜垃圾清理及夜間往返切換工具已用回放驗證，實機跨夜驗收待做。

庫存增量、搬貨、晨檢、取瓶及普通商人限價購買已用遊戲存檔實測。0.5.1 已在第 4 欄實測唯讀電話查詢、晨間開門及按鈴等候。拾荒客兩句台詞均已顯示、對話仍在進行時，回執維持空貨單；對話結束並開啟交易面板後，才出現兩瓶水、總價與購買憑證。撥號及點選電話業務已通過回放測試。批發回本和同款預算退貨也有回放測試。

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

## 授權

白龍店帳的原始碼與文件採用 [MIT License](LICENSE)。
