# 白龍店帳操作手冊（智能體遊玩指南）

白龍店帳透過本機 MCP 讓你直接操作並遊玩《深空當鋪》。**你就是這家當鋪的店主與玩家**：開局路線、每日營運、進貨定價、議價成交、舉報通緝、劇情抉擇、加工生產與夜間外出，預設都由你自主判斷與推進。使用者通常不會干預經營，而是坐在旁邊一起看畫面、聊天吐槽，並在需要「眼睛看畫面」或「手動點 UI」時幫你一把。若對方當次有指定存檔、遊玩天數、預算或「這客結束先停一下」等節奏安排，再照約定停手或討論。

用 MCP 讀台詞、瓶貼、文件、價格和物品位置。**只有 MCP 看不到或目前沒有工具的項目，才暫停交給旁邊看畫面的人協助：**
- **目視鑑定**：僅姓名相符時的通緝照比對（`wanted_name_match`）、改造體外觀鑑別與開槍自衛、可疑包裝驗偽、未鑑定針劑辨識，以及需要看圖鑑或外觀判斷真偽、品相、轉售價值的情況（含 `player_visual_check`、`image_only_guide`）。告訴對方是哪位客人、哪件物品或哪一頁，等對方看完再接著操作。
- **目前沒有自衛開槍工具**：若對話措辭疑似改造體，把台詞與來訪編號交給對方目視確認，**不要叫治安部**（改造體會逃走）。
- **手動 UI 操作**：難度頁按「開始遊戲」、關閉對方手動開啟的面板（`item_panel_open`）。

## 開新局

1. 對方在主選單點「新遊戲」後，用 `get_scene` 確認進入 `selecting_specialization`，再用 `get_specializations` 讀可選專精、起始物品、提示與可用狀態。
2. 挑一個你想玩的專精，簡短說一下這局打算怎麼經營。學徒適合從日用品交易起步，銷贓人適合經營地下人脈，化學家適合研究手冊與加工，水培農場主、養鼠人或水產養殖戶適合經營生產，水商適合探索水質與設備。即時可用名單以遊戲回執為準。選定後，把 `scenario_id` 與同一張回執的 `session`、`revision`、`view_token` 交給 `choose_specialization`。
3. 進入特性頁用 `starting_perks` 讀點數、格數、效果與互斥；帶當頁憑證及 `perk_id` 逐個 `add`／`remove`，每次看剩餘點數，選好後用 `continue` 前往難度頁。若選了哨兵、成癮或勒索特性，進店後記得先查 `get_shop_status` 的 `health`。
4. 用 `starting_difficulty` 比較各難度，`mode="select"` 加 `difficulty_id` 選卡，選完請對方按「開始遊戲」。進店後先盤點開局物資、手冊與營運成本，再決定第一天的計畫。

[專精](docs/specializations.md) · [特性](docs/starting-perks.md) · [難度](docs/difficulties.md)

## 接手存檔與查帳

- 用 `get_scene` 核對場景與存檔欄位。若附 `version_mismatch`（`dll` 與 `mcp` commit 不同，或舊版 DLL 未帶 `dll_commit` 而附 `reason="dll_without_commit"`），工具仍可執行，但記得提醒對方更新或重載以免欄位對不上。若回 `tool_status="unavailable_in_scene"`，確認目前畫面及轉場進度；若回 `tool_status="game_over"`，僅剩 `get_scene` 與離線帳本工具可用。
- 當鋪內先讀一次 `get_shop_updates` 建立庫存基準並記下 `cursor`，之後寫入型操作回執會附 `shop_update` 變動（純讀取子模式如 `item_menu(read/targets)`、`item_panel(read)`、`water_use(preview)`、`water_panel(read)`、`control_heat(status)`、`inspect_item_window(open_if_closed=False)`、`inspect_archive(read)` 不會重複附帶；`report_client(read)` 僅在 `arrest_finished` 時補附）。重連可帶 `since=cursor`；重新讀檔後以新的 `generation` 和當局物品編號為準。遇 `cursor_reset` 時，`shop_update` 只回件數、現金與日數摘要，需要完整基準時再呼叫 `get_shop_updates`。
- 用 `get_shop_status` 看日數、營業、房租、垃圾、夜間服務、傷勢與店鋪狀況。店況、`get_counter` 或 `query_inventory` 會省略未變動的日曆、垃圾與睡眠欄位，要看完整資訊就帶 `verbose=True`。用 `query_inventory` 依名稱、位置和所有權找貨，`find_stock` 依原生分類、種類、限制和瓶貼配貨，`get_stock_items` 查一批當局物品編號。
- 用 `get_shop_ledger(slot=目前欄位)` 接手舊局。帶 `inventory=True` 可讀該欄位最後入帳的庫存，即使遊戲已關閉。實際出貨前仍以遊戲當前位置與所有權為準。
- 遇未完交易、插話或 `sale_pending`，用原 `ticket_id` 查原工具、參數及最新回執。核對是否仍為同一位客人、原單是否已成交，以及還有哪些步驟。恢復交易回執時沿用原工具的相同參數，避免另開一單；`replayed: true` 代表取回既有結果。若回 `outcome_unknown` 或 `reason="receipt_corrupt"`，先核對帳本、現金、貨物及櫃檯，無法確定結果就先停下該單。

店內動作通常用 `generation`、`revision`，接客交易還會用 `visit`；選角及外出通常用 `session`、`revision` 和 `view_token`。面板 token、來訪編號及物品 ID 都取自回執。遇版本過期、換客、換頁或重新開窗，先重讀當下狀態再做下一步。

## 營業與接客流程

1. 準備好開店就用 `open_shop` 開門；`next_customer` 會自動等前一位離場、叫鈴並等開場與平板打開（共用最多 20 秒上限，逾時回 `opening_timeout`、`customer_departing` 或 `bell_unavailable`），並自動關閉工具開出的物品面板（手動開啟的面板會回 `item_panel_open` 與 `after_close_next_tool`）。剛叫進來會回 `status="called"`，呼叫前客人已在場才回 `status="already_present"`，沿用回執的 `visit` 和 `stage`。
2. 遇 `poster_match` 且 `arrested=false` 時可舉報領賞；但先檢查 `get_shop_status` 的 `security.contraband`（暗格外有無違禁品），以及對方是否給過名片或屬劇情人物，再決定報或不報。舉報前先結清或取消交易並收回留貨，用 `feedback_box` 記下聲望，再讀 `report_client` 燈號並以本次憑證舉報。若回 `report_intercepted`，用 `get_trade_context` 等攔截台詞結束後，再重讀 `report_client(mode="read")` 取得新憑證與 `choice_hint`，自行決定要再按一次舉報還是送客放走；舉報成功則用原 `visit` 重讀至 `arrest_finished`，核對台詞、現金與聲望變化。只有 `wanted_name_match` 時交給旁邊看畫面的人比對通緝照；遇 `story_branch`（`consequence="unknown"` 表示後果未確認）可自行抉擇或跟對方聊聊再選。需要細讀開場、選項或平板時用 `get_trade_context`；大貨單帶 `verbose=True` 取逐項明細，賣水客的 `seller_water` 只列 `count` 與 `ids`（明細看 `offer.items`），客人說話中（`stage="speaking"`）會保留上次需求並標 `stale_during_speech=True`。對話用目前的 `page` 和選項 ID；台詞要求把貨放上櫃檯時，先用 `move_items` 把貨放上 `back`，核對 `counter_request.table_items` 的件數和種類再選選項；標示 `forced_choice: true` 時留意後果再選。
3. 盤查選項若帶 `inspection_exposure`，先看暗格外的自有貨物件數；若 `get_shop_status` 的 `security.poster.pulled` 為 `true`（海報拉起），盤查前提醒放下海報以藏好隱藏格物品。`client_role="buyer_seller"` 表示同時買貨與賣貨，買水客自帶空瓶時也要看需求。按平板顯示的需求、限制及剩餘預算配貨，買家沒顯示標籤時仍要看限制（例如 `exclude_contraband`）。用 `find_stock(owned=True, ...)` 找合條件的貨。遇「種類多樣」，按 `kind_groups` **每個原生 `kind` 只放一件**，同一位客人買過的種類先收起來。查 `WATER` 會含空容器，配水選 `contains_water=true`（水占比至少一半）；`water_ml` 是實際水量，`liquid_main` 顯示主成分。
4. 按當次交易類型下單，並以實際回執核算成本與庫存。

| 交易 | 操作與核對 |
|---|---|
| 議價 | 用 `bargain(mode="read")` 查依類型分組的百分比、聲望、預算影響與離場機率（需逐鈕明細時帶 `verbose=True`），再用 `offer` 提交同頁的類型、百分比和 `bargain_token`。先看抬價成功後的預算削減率。送出後若回 `bargain_pending`，等對話結束，用同一來訪編號及最新版本重讀反應。聲望倍率影響聲望，成交金額看實際上櫃報價。 |
| 整批採購 | 用 `quoted_total` 評估，取本次 `offer_token` 呼叫 `buy_merchant_batch`（例如最多花 90 就帶 `max_total=90`），成交後核對實付、買進的 `items` 與 `shop_update`。 |
| 單件採購 | 用 `request_supplier_quote` 帶本次 `visit` 和商品 `item_id`，取上櫃報價並核對原件已回到前櫃，再決定上限；可在 `trade_batch` 帶單筆買入。 |
| 賣貨 | 帳面 `estimated_value` 用於估值，實際上櫃看 `quote.price`。用 `sell_order` 的 `lines` 設逐件底價，如 `[{"item_id": 42, "min_price": 18}]`；整批合計底價成交帶 `bundle=True`。走私艙（`smuggler_bay`／`smuggler_bay_mini`）內的貨可直接賣出，遭拒時會優先退回原走私艙（僅在艙滿時退回主庫存並標註 `returned_to="inventory"` 與 `contraband_outside_bay`）。同來訪若有未售貨物仍在自有庫存，新版本可重下；已全售或離庫則重送會回原成交回執。 |
| 同客混合買賣 | 用 `trade_batch` 的 `orders`，每筆帶 `item_id`、`side` 和 `price_limit`（`side="buy"` 為買入上限，`side="sell"` 為最低售價），逐筆查成交結果。 |
| 以物易物 | 遇 `barter_unsupported` 表示這位客人的原生交換流程尚未接入。可交換時先看各方案 `give`（店家付出）和 `receive`（店家取得）；用 `barter_offer(mode="select")` 選方案，按要求將付款物移到交易檯，重讀報價後帶新 `barter_token`、同一方案的 `option` 及 `payment_item_id` 接受。 |
| 購買情報 | 先讀 `info_receipt`，決定購買時用 `pay_info_receipt` 帶當頁 `receipt_token` 和顯示的 `price`，核對現金及情報紙條。 |

5. 成交後核對實收、實付、餘額、剩餘預算及各件去向。若回執列出 `identity_changed`，用新的身分和名稱記帳。用 `finish_customer` 帶該客人的 `visit` 送客；`customer_left` 或 `customer_replaced` 表示原客已離開或換人，回執帶新客時從新 `visit` 和 `stage` 接手。
6. 交易或送客受阻時看 `reason`、`blocking_items` 及 `next_tool`。一般檯上雜物可能自動收回庫存，查看 `cleared_items` 和來客狀態。報紙、便條、情報紙條、名片及返店背包平時會自動收納，庫存滿時可用 `get_counter` 找留在交易檯的物品。若顯示 `trade_table_not_empty`，依實際來源把自有留貨搬回庫存；若顯示 `nuclear_waste_on_counter`，先移到庫存或展示櫃（垃圾桶無法消除核廢料）。

## 搬貨、文件與機台

- 卡槽或物品右鍵回執若有 `removal.reason="module_stuck"`，保持機台視窗開在該槽。找到自有的普通或進階模組拆卸器，用拆卸器自己的編號讀 `item_menu`，再用它的 `menu_token` 選 `use`，以 `removal.target_id` 指定卡片（普通型要有剩餘次數）。回執的 `module_stuck=false` 表示卡已鬆開；重讀機台槽位，用新的 `window_token` 取回庫存。
- 查貨時若有 `container_path`，按列出的順序從外包找到內包，`parent_id` 是直接裝著該件貨的包。包內貨的 `area` 跟隨整包；`unpack_first` 要先取出，`container_locked` 先核對容器狀態，直接出貨前再看實際位置。
- 用 `move_items` 同批處理相同 `source`、`destination` 的貨，逐件核對 `status`、`actual_area`、`from_container` 和堆疊的 `observed_quantity`。`source`／`destination` 也支援 `"bay:<owner_id>"` 直接與走私艙互搬或送交櫃檯 `back`（`bay → back` 須帶 `visit`）。單件空間不足時會列入 `skipped_ids` 並繼續試後面的貨；其他阻礙仍會停下。若回 `already_in_destination`，該件已在目標位置；遇換客停下先查當前狀態再處理 `not_attempted`。遇 `no_space` 或 `showcase_full`，比對物品尺寸、可旋轉性與 `largest_free`；必要時用 `sort_inventory` 整理主庫存，若回 `external_sorter_active` 則由已啟用的整理模組處理。
- 用 `item_menu(mode="read")` 看物品的原生右鍵選項。帶上一個 `menu_token` 可看變動；過期或已用的 token 會回 `stale_menu_token` 並附新 token 供直接重試。工具能作用於多件候選貨時，用 `mode="targets"` 和 `target_ids` 先試配預覽，選定後用 `mode="choose"`、`verb="use"` 與 `target_id`。用化學光譜儀掃描會直接回 `scan` 並自動關閉工具開出的面板（`panel_closed=True`）。
- 用物品**自己的當局編號**作為 `inspect_item_window` 的 `owner_id`，需開窗時帶 `open_if_closed=True`。讀 `window.document`、槽位容量、可見控制項及 `window_token`。從同一槽批次存取用 `transfer_slot_items`，指定回執的 `control` 和 `window_token`（取出帶 `source="slot"`，放入帶 `inventory` 或 `showcase`），逐件檢查實到數量。
- 按鈕、單選與分頁用 `press_machine_button`，換頁帶當頁給出的標題與從 0 起算的 `tab_index`。滑桿用 `adjust_machine_control` 傳 `values=[數值]`，範圍滑桿傳 `[下限, 上限]`，再讀遊戲實際套用的值。化學成型機的輸出按鈕支援 `quantity` 1 至 64，對照 `produced` 和停止原因取貨。
- 化學蒸發後段可用 `chem_finish` 串接：從蒸發器取出容器（或於成型機內接續）、用光譜儀確認沒有水或酒精殘留（可帶 `min_purity` 先把關估計純度）、放進成型機、在 `chemical_finisher` 面板依 `recipe_id` 或名稱選配方、批次輸出、收回庫存並抽檢純度與分級（`purity`、`grade`），有指定 `stash_ids` 或 `stash_count` 時再放進走私暗格；中途任一步失敗即停下並回報容器位置。萃取後蒸發前務必先過濾：使用帶 `FILTER_CONTAINER_TAG` 的承接容器（例如水罐），先放入濾材（滤水器或韧棉织物），再用 `water_use` 把溶液倒進去。水罐為 3×4，用完記得從成型機或蒸發器取出，避免卡住庫存空間與下次的輸入槽。蒸發器視窗也會回報電池電量、容器液量主成分與 `distillate_capture` 條件。
- 回執指向 `item_panel` 時先讀第二層面板。用當頁 `panel_token` 和啟用的 `control` 操作，結果和新 token 見 `after`。遇 `select_panel` 就從 `panels` 選名稱，以 `panel` 指定要讀哪一張，再依可用控制聚焦或關閉。遇 `dedicated_panel` 看 `next`。若掃描後需回同一位客人的交易平板，用 `reopen_trade_tablet` 帶當次 `visit`。
- 水務可用 `inspect_purifiers` 查瓶貼、電力及微型淨水器耐久。用 `water_use` 裝水時以容器為 `source_id`、`"faucet"` 為 `target_id`；手持淨水器則以機器為來源、瓶子為目標。裝水、淨化、試紙可用 `apply_now`，排水、倒水、藥片與口糧先 `preview` 再 `apply`；多瓶空瓶一次裝水並用手持淨水器淨化可用 `fill_and_purify_bottles`，遇提示、沒電或關水龍頭即停下。試紙與藥片須先從容器取出單件。
- 自有刮刮樂用 `scratch_card` 讀取、開啟、`reveal`（位置 1–3）或 `reveal_all` 一次刮開，刮完關閉面板再換下一張。

### 等待機台讀值

先開機台視窗並取得當前憑證。加熱板以 `control="current_c"` 等容器實測溫度，其他支援的原生進度條用 `inspect_item_window` 回報的控制項路徑和數值單位。舉例，等加熱板達 45 °C 時，呼叫 `wait_for_machine` 帶加熱板的 `owner_id`、當前 `generation`、`revision`、`control="current_c"`、`direction="at_least"`、`threshold=45`、`timeout=20`（降溫用 `at_most`）。逾時可設 0 至 60 秒，預設 10 秒。

收到 `threshold_reached` 或 `timed_out` 時看 `value`、`elapsed_ms`。遇 `window_closed`、`owner_changed`、`vessel_changed` 或 `reading_unavailable`，先確認機台及容器再重新觀察。等待只讀取數值，結束後機台沿原設定運轉；要改溫度或停機，操作當前原生控制項。原版 0 級與 1 級加熱板分別固定 99 °C 和 160 °C，2 級才有 ±5 °C 按鈕。

### 背景控溫

先打開自有加熱板的視窗，確認機台在庫存或展示櫃、加熱槽裡是要處理的非空容器。從視窗回執取用機台 ID 和槽內容器 ID，使用 `control_heat`。

- `mode="start"` 必帶 `owner_id`、`vessel_id`、`min_c`、`max_c`、`seconds`、`generation`、`revision`（範圍 26 至 160 °C，時長為正整數）。開始回執會帶 `heat.run_id` 和策略，記下該 ID 即可關窗繼續接客。結束結果（含 `peak_c`、`reached_range`、`in_range_seconds`，未進入目標溫區時附 `warning="range_not_reached"`）會隨後續 `shop_update.fields.heat` 或櫃檯回執通知一次（跨工具與重連皆不重複）。同一版本下要再跑完全相同的設定，把前次結束的 `run_id` 帶進新一次 `start`。
- `mode="status"` 查目前或最近一次工作，從 `heat` 看狀態及可用溫度。機台可能使用遊戲原生目標溫度，也可能在低熱與關閉之間切換；這種切換可能有溫度超越，依實測讀值判斷成品或下一道工序。
- `mode="stop"` 必帶該次工作回傳的 `run_id`。收到 `already_running` 先看既有工作狀態；遇 `manual_override`、`vessel_changed`、`target_unavailable` 或 `off_unconfirmed`，回查原生視窗確認容器與實際模式再繼續。以 `finished` 或 `stopped` 和 `mode_index` 核對這次工作的結束。

## 打烊、過夜與外出

睡前核對 `health` 與 `alerts`。哨兵距上次注射滿 6 天時警告，滿 7 天起累積傷害，池值達 20 會死亡。成癮的交易懲罰與戒斷進度以目前讀值為準。勒索者第 8 天起每 7 天來收費，用當頁對話選項決定。需要攝取或注射時，讀 `usable_items` 的物品編號，再以 `item_menu` 的 `activate` 消耗；未鑑定的針劑交給旁邊看畫面的人辨識。

用 `get_shop_status(verbose=True)` 查看證據、違禁品分區件數、海報狀態（`poster.pulled`）、通緝名單和 `last_save`（`security.evidence_display` 在打烊後等時段可能依遊戲原生顯示「不適用」）。使用隱藏格藏貨流程：先 `toggle_poster(mode="up")` 拉起海報 → `move_items` 搬進 `hidden` → **再 `toggle_poster(mode="down")` 放下海報**才算藏好；開店前若 `security.poster.pulled` 為 `true`，記得先放下海報（營業中有客人在場時禁止操作海報）。過夜後遊戲先進入次日早晨才存檔。

- 打烊前看剩餘客數及檯上留貨，用目前的 `reception_token` 呼叫 `close_shop`。需要提前打烊時核對剩餘客數，再以 `confirm_remaining` 確認。歇業後可以留店收貨、外出或睡覺。
- 要過夜時，用當天 `bill_token` 呼叫 `prepare_sleep` 查帳單；需要時用 `night_service` 調整服務。沿用最新憑證和帳單的 `service_cost` 呼叫 `sleep`。回 `awaiting_report` 時等當晚報告，改查店況及日數確認進度。收到報告後核對租金、垃圾、服務費、餘額與 `alerts`，並留意下次到期日、傷勢及保險。
- 外出先用 `shop_activity` 讀門口並按當前選項操作。如果回執開出選包提示，用 `response_choices` 的容量、`page` 和選項 ID 交給 `response_choice`。用 `travel_menu` 查可達地點，`mode="depart"` 帶可用的 `location_id` 出發；轉場用 `wait_for_scene` 帶出發前的 `session` 與 `after_revision` 等抵達。
- 抵達後用 `explore` 讀傷勢、左右手、背包與地面空間，用回執提供的物品 ID、槽位與 token 搬貨或搜尋。遇 `can_scavenge=false` 查 `scavenge_blocked_reason` 和 `notification`。返店前先處理地面物；決定放棄時用 `mode="return", abandon_ground=True`。返店後查帳及傷勢，再安排當晚。

## 人脈、聲望與服務

- 電話到貨時，從 `shop_update.arrivals` 核對本次物品編號和聯絡人。`contraband_outside_bay` 是到貨當下暗格外的自有違禁品件數。
- 用 `get_phone_contacts` 看聯絡人和業務。歇業後讀電話，用 `dial_contact` 帶目前的 `phone_token`、聯絡人號碼及店況版本撥號，或用 `phone_keypad` 開關電話與手動撥 1–9 位號碼（保留前導零）；用 `advance_phone_dialogue` 或 `choose_phone_service` 接續通話（選擇後會自動等候狀態穩定，並回報 `cash_delta` 與 `cooldown_days`），每步使用最新 token。若回 `phone_unavailable`、`reason="shop_open"`，等打烊後再撥。
- 喬的人脈面板開啟時，用 `wild_network` 讀目錄，大清單加 `verbose=True` 看明細。用 `mode="select", target=服務ID` 看 `selected`、`favor_to_spend`、`cash_to_pay` 和餘額；確定購買時帶新 `panel_token`、`generation`、`revision`，以 `mode="buy"`、`target=選中的服務ID` 和原報價 `cost` 購買，核對 `favor_spent` 和 `cash_paid`。
- 用 `feedback_box` 看派系聲望、啟用效果及最近的正負門檻。預設續查回差異；想查完整門檻及未啟用項目時用 `full=True, verbose=True`。

遇到工具異常或卡點時，記下當時場景、想做的動作、工具、關鍵參數及原回執方便排查。工具入口與安裝方式見 [README](README.md)，實機驗收與待驗場景見[驗收表](docs/acceptance.md)。
