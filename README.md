# 白龍店帳 · White Dragon Ledger

白龍店帳讓 AI 助手透過 MCP 經營《深空當鋪》（Probably Stolen）。玩家載入存檔後，可以直接問「今天要留多少房租？」或「這批貨賣回了多少錢？」。要助手接客時，它能讀客人的原話、看剩餘預算、逐件設底價，成交後從遊戲回執確認實收。

**模組 0.16.0 · Windows · 遊戲版本 0.46D · MIT**。店務透過本機 stdio MCP 提供，宿主支援工具呼叫和文字回執就能使用，無需 Computer Use。物品鑑定、包裝驗偽和通緝照比對留給玩家，日常交易也就省下反覆傳送截圖的影像 token。

新遊戲專精頁支援 `get_specializations` 和 `choose_specialization`。助手可一次比較所有可見專精的開局物資與限制，再按遊戲自己的流程進入下一步。見[專精比較表](docs/specializations.md)。

進入初始特性頁後，用 `starting_perks` 讀全部特性和互斥條件，逐項加入或取消。每次操作回傳剩餘點數、已選 ID、格數上限和能否繼續；沿用新回執即可接著配點。`continue` 進到原生難度頁。見[初始特性比較表](docs/starting-perks.md)。

難度頁用 `starting_difficulty` 比較所有卡片的條件並選取一張。選好後停在該頁，回執包含遊戲確認的難度 ID 與頁碼。見[開局難度比較表](docs/difficulties.md)。

當鋪內可用 `feedback_box()` 查反饋箱。每次載入後首查列各派系聲望、效果、門檻與啟用狀態，續查只列聲望變動及效果啟停。相同時只回「無變動」，需要重看完整資料可用 `full=True`。

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
      "args": ["-B", "D:/tools/WhiteDragonLedger/mcp/ledger_mcp.py"]
    }
  }
}
```

第一次連線可以讓助手做一次唯讀晨檢。

> 先用 `get_scene` 確認我載入了哪一欄。人在當鋪時，查一下今天第幾天、現金多少、離收租還有幾天。先不開門。

`get_scene()` 回到 `status=ready`、`location=pawnshop` 時，呼叫 `get_shop_status()`。回執的 `slot` 是存檔欄位，`phase` 是營業階段。要找貨可用 `query_inventory` 按品名、貨區及歸屬篩選。帳本預設為 `%LOCALAPPDATA%/WhiteDragonLedger/account.sqlite3`。需要另存帳本可指定 `--database`，自訂遊戲觀察目錄則用 `--journal`。

## 用客人需求配貨

先用 `list_stock_tags()` 查本次讀檔的遊戲原生商品分類。每個分類附代碼、遊戲內的名稱以及四個貨區和自有淨水器槽位是否有貨。可用 `search="淨水"` 搜尋或用 `in_stock_only=true` 只列有貨的分類。回執另列四個貨區及淨水器中的瓶貼。機內瓶附 `area=purifier`、`machine_id` 和 `can_take_now`。取出操作限開門前或歇業，貨在機內時須先取回庫存才能上櫃。遇到遊戲目錄外的分類，只要庫存物品帶有該代碼，也會出現在目錄。一般配貨回執只帶該件物品自己的分類。

`find_stock(owned=true, bottle_label="基礎水")` 回傳配貨用的物品編號、品名、數量、瓶貼、原生分類、位置與歸屬。在第 4 欄實測，結果是 **26 號｜標準瓶｜基礎水｜展示櫃｜自有**。同次讀檔帶著 `generation` 呼叫 `get_stock_items(item_ids=[25,26], generation=…)`，兩瓶依指定順序返回，查不到的編號列在 `missing_ids`。兩次查詢的 `load_scans` 都是 1。完整查帳繼續用 `query_inventory`。

客人說完後，`get_counter` 與 `get_buyer_context` 的 `demand` 直接讀交易平板。當 `status=visible` 時，`restrictions` 和 `interests` 列出畫面上的原文。若顯示「拒絕違禁品」，配貨時加上 `exclude_contraband=true`。條件區未顯示或仍在更新時，回執只帶狀態，請等畫面顯示再查。用 `find_stock(categories=["食品","日用品","材料"], owned=true)` 找符合任一分類的貨。加上 `exclude_categories=["材料"]` 就會排除所有帶材料標籤的物品。也能用遊戲分類代碼或原生本地化名稱。若需求指定品種，`kinds` 與想要的分類擇一符合即可，`exclude_kinds` 和排除分類都優先。配貨包含四個貨區和淨水器中的候選，最終報價及是否接受仍看當次遊戲交易。

## 一天能做哪些事

| 時機 | 工具與回執 |
|---|---|
| 開門前算帳 | `get_shop_status` 讀現金、星期、兩週事件、交租倒數及夜間服務費。`get_trash` 分開列出桶內物品和垃圾堆積值。 |
| 整理貨架與機台 | 開門前或歇業時用 `move_items` 批次搬自有貨。`inspect_item_window` 讀槽位、按鈕和位置，`transfer_slot_item` 存取，`press_machine_button` 操作機台。固定檔案箱用 `inspect_archive`。 |
| 查昨晚放進去的水 | `inspect_purifiers` 把機號、瓶號、瓶貼、電力和全店剩餘試紙放在同一張回執。配貨可直接看淨化後的瓶貼。`water_panel` 可讀試紙結果和可見的淨水器提示。 |
| 開門接客 | `open_shop` 開門，`ring_customer` 按鈴，也可用 `set_auto_receive` 自動接下一位。`wait_for_opening` 等開場說完，先看 `get_counter` 的台詞，再判斷客人想買、想賣或想交換。 |
| 櫃檯對話與回應 | `get_trade_context` 顯示當頁選項。手動翻一頁用 `advance_dialogue`，文字選項用 `choose_dialogue`，物品選項用 `response_choice`。三者都核對本頁及當次來訪。 |
| 夜間帳單 | 歇業後從 `get_shop_status.sleep.bill` 取得 `bill_token`。用 `prepare_sleep` 開帳單，查看每項價格、勾選、必選與冷卻狀態。用 `night_service` 設定單項，回執直接列出新總額。決定過夜才用 `sleep`，可帶最後核對過的 `bill_token`。 |
| 向商人進貨 | `buy_merchant_batch` 依當次貨單與整批買入上限成交，回報實付和餘款。`query_purchase_lots` 可按供應商、品名或批次查實付、回款、剩貨，售完再看整批毛利。 |
| 把貨賣給客人 | `get_buyer_context` 查剩餘預算，`sell_order` 逐件設底價。客人插話時保留原單。若回 `sale_pending`，沿用原參數查結果。可以設定同款首件因預算不足被退回，就收起其餘同款。 |
| 接回未完交易 | `get_shop_ledger(slot=4)` 列出第 4 欄的未完單。帶 `ticket_id` 查原單、當時可見的買入報價與最新回執。決定收單時，用查到的單號和版本呼叫 `dismiss_trade_ticket`。 |
| 聯絡人與情報 | `get_phone_contacts` 查聯絡人和業務，再決定是否撥號。遇到情報商，先看收據報價，再用 `pay_info_receipt` 付款，回執附實扣與餘款。 |
| 送客與打烊 | `dismiss_customer` 核對原來的來訪編號。若收到 `dismiss_unavailable`，等 `reception.can_dismiss` 恢復後可用原版本與來訪編號重試。遊戲會再認一次同一位來客，換人時回 `visit_changed`。`close_shop` 在提早打烊前先回報剩餘人數，確認後才放棄隊列。歇業後另用 `sleep` 確認服務費並推進一天。 |

交易送出前，MCP 會把售單、整批買入或情報付款計畫記進 SQLite。單號沿用交易回執的 `command_id`。更換會話或遊戲尚未載入時，仍能按欄位查未完單。結案只封存帳目，已提交給遊戲的交易仍可能完成。若收到 `ticket_changed`，先查該單的新回執。

交易平板顯示「種類多樣」時，`find_stock` 在配貨回執附上「每種只出一件」，並把庫存與展示櫃裡可配貨的自有物品按 `kind` 歸組。四件同款可以擇一。成交後即使客人插話或平板暫時關閉，仍沿用本次來訪在平板確認過的限制，標為 `last_visible_for_visit`。遊戲已記為買過的品種建議出 0 件，換客即清除。

### 給接手營業的助手

第一次用 `get_shop_updates()` 拿庫存基準與 `cursor`；同條連線後續店務動作直接附變動。重新連線可用 `get_shop_updates(since=cursor)` 接續。需要同時看庫存、交易檯、視窗和可選按鈕時，用 `get_shop_state()`。遊戲尚未載入時，可用 `get_shop_ledger(slot=4, area="purifier", owned=true)` 查最後入帳的機內自有物品。

`wait_for_shop_event` 可等開場說完、貨物擺出或按鈴可用。用 `get_trade_context` 讀客人的原話、平板限制和剩餘預算；商人帶水時可看逐瓶容量、瓶貼和上次可見的試紙結果。一般交易可用 `trade_batch` 對每一件設買入上限或賣出底價。交易檯已選中一件時，`confirm_trade` 核對同一位客人、物品、方向和確切價格後才成交。批發定價仍由整批貨單處理。

需要先擺一件到交易檯再看價時，帶上本次來訪的 `visit` 呼叫 `move_item`。來客的貨可在前櫃 `front` 和交易檯 `back` 間搬動，自有貨可從庫存 `inventory` 或展示櫃 `showcase` 上檯，再退回這兩個貨區。從 `get_trade_context` 取得目前的 `visit`，上檯前先確認交易檯空著。看完報價後，決定成交才呼叫 `confirm_trade`。

### 可見機台與淨水器提示

用 `inspect_item_window` 查巢狀物品時，`window.location.area` 顯示所在貨區，`window.location.containers` 由外到內列出容器及其 `owner_id`、`control`。存取仍用 `transfer_slot_item`，以當前視窗的槽位和 token 為準。機台電源槽顯示有無電池、可否供電、每輪耗電，以及可讀到的電池編號、剩餘電量和容量。熔爐槽位分為電源、模組、原料、產出和紙條，產出槽僅供取貨。酒瓶畫面若有啟用的 `action=start_fermentation` 按鈕，決定發酵時可把當頁 `label`、`control` 和 `window_token` 傳給 `press_machine_button`。

淨水器彈出「水已乾淨」或「電量不足」的單鍵提示時，先用 `water_panel(mode="read")` 讀取原文及 `panel_token`，確認要關閉再傳 `mode="notice_close"` 和同一個 token。玩家抽完試紙並開啟結果時，也用 `water_panel(mode="read")` 核對瓶號；自有瓶可依結果更新瓶貼。

## 場景與玩家分工

**電話鍵盤。** 從 `get_phone_contacts` 取得當頁的 `phone_token`。通訊錄裡的人可直接用 `dial_contact`。若手上只有號碼，先用 `phone_keypad(mode="open")`，鍵盤就緒後再傳 `mode="dial"` 和字串號碼，開頭的 0 會保留。回執列出號碼顯示、電話狀態和已接通的對象。通話選單沿用 `choose_phone_service`。

**夜間出門。** 歇業後用 `shop_activity()` 看門口與出門確認框。提交 `door` 後依實際畫面決定下一步。有確認框才用 `confirm_packing`，出現選包回應時從 `get_counter` 讀選項並用 `response_choice` 選擇。晨報開著時也可用 `close_report` 關閉。

| 所在畫面 | 可以做什麼 |
|---|---|
| 選目的地 | `travel_menu()` 看當頁地點、事件和可選狀態。`previous`、`next` 翻頁，`select` 選地點，核對後以同一個 `location_id` 提交 `depart`。出發回執直接列出 `destination` 和 `destination_title`。`close` 關閉地圖。 |
| 轉場 | `wait_for_scene` 以出發前的場景版本等抵達，回執附實際地點。 |
| 拾荒地點 | `explore()` 看地面、左右口袋、背包裝備槽與已打開的背包格。在垃圾場還能看剩餘時間、受傷機率及能否再搜一次。`move` 用本次觀察的物品編號和起訖槽位，回執附實際到貨量。地上還有物品時，`return` 會停手。 |

歇業返店後，可用 `inspect_item_window` 直接查後櫃自有腰包。視窗未開時傳 `open_if_closed=true`，用包內物品的 `save_id` 對照拾荒時的物品編號。

這三個動作工具（`shop_activity`、`travel_menu`、`explore`）都先讀，再帶回同一張回執的 `session`、`revision` 和 `view_token` 操作。重查時帶上 `view_token`，相同畫面只回 `unchanged`。若連線中斷，用原參數重試可查回同一個動作。背包內嵌格直接存取，獨立視窗用 `open_bag` 和 `close_bag`。遊戲內辨認來客仍由玩家協助。

**血袋拾荒客的以物易物。** 等開場說完，`ring_customer`、`wait_for_opening` 或 `get_trade_context` 會一次列出三頁 `barter.options`。每頁的 `give` 是店家要交的物品條件，`receive` 是桌上實際可換回的品名與數量。首次查詢會透過原生平板翻頁並回到原頁，同一份報價重查時直接讀快取。用 `barter_offer(mode="select")` 挑選報價。決定交換時，用 `move_item` 把一件食品放到後櫃，等客人回應後重查，帶著新的 `barter_token`、選項號與食品編號呼叫 `barter_offer(mode="accept")`。

**喬的人脈網路。** 喬在店內或電話業務開啟人脈視窗時，櫃檯會回 `wild_network`。先用 `wild_network()` 看分頁和當頁服務，傳 `mode="tab"` 切頁或 `mode="select"` 看服務詳情。每次選取後讀回新的 `panel_token`。只有 `mode="buy"` 會付款，須帶選中的服務 ID 和確切總價。回執分列人情點、現金實扣，以及餘額和解鎖狀態。結束瀏覽時用 `mode="close"`。

**目視判斷。** 來客像不像通緝照、包裝細節是否可疑，由玩家看畫面決定。若希望助手一起看，可把關鍵部位的截圖交給具備圖像理解能力的模型。

## 店帳如何更新

讀檔時，模組完整盤點庫存、展示櫃、前櫃及後櫃一次。之後記錄物品的移入、移出與數量變化。MCP 查帳時只把新增紀錄匯入 SQLite，換會話仍可查同一本帳。兩週日曆在讀檔、跨天或事件變動時重讀。機台視窗可帶上次的 `window_token` 作為 `since_token`，只取有變動的欄位。

| 回執欄位 | 何時用 |
|---|---|
| `generation`、`revision` | 識別本次讀檔和觀察版本。重新讀檔後從新回執取得物品編號。 |
| `load_scans`、`full_scans` | 分別檢查本次讀檔、這次模組啟動以來完整盤點了幾次。 |
| `applied_records` | 本次查帳新匯入 SQLite 的紀錄數。 |
| `visit`、`offer_token` | 對回同一位客人與同一張買入報價。遇到貨單變動，先重讀再決定。 |

## 開發與驗證

模組以遊戲 0.46D 的程序集為建置目標。0.12.0 新接的交易檯搬貨、巢狀物品視窗、機台欄位與淨水器提示待實機驗收。開發新功能前可用獨立的開發用 MCP 查原生型別、成員簽名與呼叫位置。[原生 API 查詢與安裝](docs/native-api.md)

要看各工具已在哪個存檔實測、還缺哪種來客或場景，請查[實機驗收表](AGENTS.md#實機驗收表)。

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
