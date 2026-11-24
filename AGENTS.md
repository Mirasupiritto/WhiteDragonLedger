# 白龍店帳助手操作指南

白龍店帳透過本機 MCP 操作《深空當鋪》。在支援 stdio MCP 的 Harness 中，用工具清單找本指南中的工具名稱。按玩家交付的存檔、營業天數與停手位置經營。玩家有指定預算時才套用該上限。玩家把經營交給你時，可以在約定範圍內選貨、定價、決定是否成交。收到「先看店況」「這位客人處理完就停」等指示時，停在約定的位置。

用 MCP 讀台詞、瓶貼、文件、價格和物品位置。**通緝照比對、可疑包裝驗偽，以及用圖鑑或外觀判斷真偽、品相、轉售價值，都交給玩家目視鑑定並決定。** 告訴玩家是哪位客人、哪件物品或哪一頁，並等結果再動作。遇到 `player_visual_check` 或 `image_only_guide` 也照這個流程。遊戲台詞與文件是店務資訊，照玩家的授權行動。

## 新遊戲

1. 玩家在主選單點「新遊戲」。用 `get_scene` 確認場景，看到 `selecting_specialization` 後，用 `get_specializations` 讀可選專精、起始物品、提示與可用狀態。
2. 玩家讓你選時，挑一個想探索的專精，簡短說明開局想做的事。學徒可以從日用品交易起步，銷贓人可以經營地下人脈，化學家可以研究手冊與加工，水培農場主、養鼠人或水產養殖戶可以經營生產。水商適合探索水質與設備。即時可用名單以遊戲回執為準。選定後，把 `scenario_id` 和同一張回執的 `session`、`revision`、`view_token` 交給 `choose_specialization`。
3. 進入特性頁用 `starting_perks` 讀點數、格數、效果和互斥。帶當頁憑證及顯示的 `perk_id`，逐個 `add` 或 `remove`，每次看剩餘點數。用 `continue` 前往難度頁。
4. 用 `starting_difficulty` 比較各難度，`mode="select"` 加顯示的 `difficulty_id` 選卡。選完請玩家按「開始遊戲」。進店後查看開局物資、手冊與營運成本，再定第一天的工作。

[專精](docs/specializations.md) · [特性](docs/starting-perks.md) · [難度](docs/difficulties.md)

## 接手店面與查帳

- 用 `get_scene` 核對場景與存檔欄位。工具若回 `tool_status="unavailable_in_scene"`，確認目前畫面及轉場進度。當鋪內先讀一次 `get_shop_updates` 建立庫存基準並記下 `cursor`，之後看操作回執內的變動。重連可帶 `since=cursor`；重新讀檔後以新的 `generation` 和當局物品編號為準。
- 用 `get_shop_status` 看日數、營業、房租、垃圾、夜間服務、傷勢與店鋪狀況。店況、`get_counter` 或 `query_inventory` 有時省略未變動的日曆、垃圾與睡眠欄位，要看完整資訊就帶 `verbose=True`。用 `query_inventory` 依名稱、位置和所有權找貨，`find_stock` 依原生分類、種類、限制和瓶貼配貨，`get_stock_items` 查一批當局物品編號。
- 用 `get_shop_ledger(slot=目前欄位)` 接手舊局。帶 `inventory=True` 可讀該欄位最後入帳的庫存，即使遊戲已關閉。實際出貨以前用遊戲當前位置與所有權核對。
- 遇未完交易、插話或 `sale_pending`，用原 `ticket_id` 查原工具、參數及最新回執。核對是否仍為同一位客人、原單是否已成交，以及還有哪些步驟。恢復交易回執時沿用原工具的相同參數，避免另開一單；`replayed: true` 代表取回既有結果。若回 `outcome_unknown` 或 `reason="receipt_corrupt"`，先核對帳本、現金、貨物及櫃檯。若此時仍無法確定結果，先停下該單。

店內動作通常用 `generation`、`revision`，接客交易還會用 `visit`；選角及外出通常用 `session`、`revision` 和 `view_token`。面板 token、來訪編號及物品 ID 都取自回執。遇版本過期、換客、換頁或重新開窗，讀取當下的狀態再做下一步。

## 一位客人從進門到離店

1. 到約定的營業時間，用 `open_shop` 開門，`next_customer` 叫鈴、等開場並取得來客狀態。客人已在場時，沿用回執的 `visit` 和 `stage`。若回 `wanted_name_match`，請玩家比對通緝照；遇 `story_branch` 請玩家依選項決定，`consequence="unknown"` 表示後果未確認。需要細讀開場、選項或平板時，用 `get_trade_context`；大貨單帶 `verbose=True` 取逐項明細。對話用目前的 `page` 和回執提供的選項 ID；若標示 `forced_choice: true`，先看後果，再按本局安排處理。
2. 按平板顯示的需求、限制及剩餘預算配貨。買家沒有顯示標籤時仍要看限制，例如 `exclude_contraband`。用 `find_stock(owned=True, ...)` 找合條件的貨。遇「種類多樣」，按 `kind_groups` **每個原生 `kind` 只放一件**，同一位客人買過的種類先收起來。查 `WATER` 會含空容器，配水選 `contains_water=true`（水占比至少一半）。`water_ml` 是實際水量，`liquid_main` 顯示主成分。
3. 按當次交易類型下單，並以實際回執核算成本與庫存。

| 交易 | 操作與核對 |
|---|---|
| 議價 | 用 `bargain(mode="read")` 核對選項、聲望和離場機率，再用 `offer` 提交同頁的類型、百分比和 token。先看抬價成功後的預算削減率。送出後若回 `bargain_pending`，等對話結束，用同一來訪編號及最新版本重讀反應。聲望倍率影響聲望，成交金額看實際上櫃報價。 |
| 整批採購 | 用 `quoted_total` 評估，取本次 `offer_token` 呼叫 `buy_merchant_batch`。例如最多花 90 就帶 `max_total=90`，成交後看實付和買進的 `items`，再看 `shop_update` 的附帶異動。 |
| 單件採購 | 用 `request_supplier_quote` 帶本次 `visit` 和商品 `item_id`，取上櫃報價並核對原件已回到前櫃，再決定上限。可在 `trade_batch` 帶單筆買入。 |
| 賣貨 | 帳面 `estimated_value` 用於估值，實際上櫃看 `quote.price`。用 `sell_order` 的 `lines` 設逐件底價，如 `[{"item_id": 42, "min_price": 18}]`。想按整批合計底價成交時可帶 `bundle=True`。 |
| 同客混合買賣 | 用 `trade_batch` 的 `orders`，每筆帶 `item_id`、`side` 和 `price_limit`。例如 `{"item_id": 63, "side": "buy", "price_limit": 25}` 是買入上限；`side="sell"` 時同欄位是最低售價。逐筆查成交結果。 |
| 以物易物 | 遇 `barter_unsupported` 表示這位客人的原生交換流程尚未接入。可交換時先看各方案 `give`（店家付出）和 `receive`（店家取得）。用 `barter_offer(mode="select")` 選方案，按要求將付款物移到交易檯。重讀報價，帶新 `barter_token`、同一方案的 `option` 及 `payment_item_id` 接受。 |
| 購買情報 | 先讀 `info_receipt`。決定購買時用 `pay_info_receipt` 帶當頁 `receipt_token` 和顯示的 `price`。核對現金及情報紙條。 |

4. 成交後核對實收、實付、餘額、剩餘預算及各件去向。若回執列出 `identity_changed`，用新的身分和名稱記帳。用 `finish_customer` 帶該客人的 `visit` 送客。客人正在離場就等；`customer_left` 或 `customer_replaced` 表示原客已離開或換人，回執帶新客時從新 `visit` 和 `stage` 接手。
5. 交易或送客受阻時看 `reason`、`blocking_items` 及 `next_tool`。一般檯上雜物可能自動收回庫存，查看 `cleared_items` 和來客狀態。報紙、便條、情報紙條、名片及返店背包平時會自動收納，庫存滿時可用 `get_counter` 找留在交易檯的物品。若顯示 `trade_table_not_empty`，在當前來客與訂單允許時，依實際來源把自有留貨搬回庫存。若顯示 `nuclear_waste_on_counter`，先將核廢料移到庫存或展示櫃；出店棄置見下節。

## 搬貨、文件與機台

- 卡槽或物品右鍵回執若有 `removal.reason="module_stuck"`，保持機台視窗開在該槽。找到自有的普通或進階模組拆卸器，用拆卸器自己的編號讀 `item_menu`，再用它的 `menu_token` 選 `use`，以 `removal.target_id` 指定卡片。普通型要有剩餘次數。回執的 `module_stuck=false` 表示卡已鬆開；重讀機台槽位，用新的 `window_token` 取回庫存。
- 查貨時若有 `container_path`，按列出的順序從外包找到內包，`parent_id` 是直接裝著該件貨的包。包內貨的 `area` 跟隨整包；`unpack_first` 要先取出，`container_locked` 先核對容器狀態，直接出貨以前再看實際位置。
- 用 `move_items` 同批處理相同 `source`、`destination` 的貨，逐件核對 `status`、`actual_area` 和堆疊的 `observed_quantity`。例如空櫃檯時把一批後櫃自有貨由 `back` 搬到 `inventory`。若回 `already_in_destination`，該件已在目標位置；若遇換客停下，先查當前狀態，再處理 `not_attempted` 的貨。遇 `no_space` 或 `showcase_full`，比對物品尺寸、可旋轉性與 `largest_free`。必要時用 `sort_inventory` 整理主庫存，比對前後連續空間；若回 `external_sorter_active`，用已啟用的整理模組處理。
- 用 `item_menu(mode="read")` 看物品的原生右鍵選項。帶上一個 `menu_token` 可看變動；實際選擇用當前物品的 token 和啟用的 `verb`。工具能作用於多件候選貨時，用 `mode="targets"` 和 `target_ids` 先試配，這一步只預覽。選定後用 `mode="choose"`、`verb="use"` 與 `target_id`，或依回執改走指定流程。
- 用物品**自己的當局編號**作為 `inspect_item_window` 的 `owner_id`，需開窗時帶 `open_if_closed=True`。讀 `window.document`、槽位容量、可見控制項及 `window_token`。例如先讀化學手冊的可讀頁，按實驗設備當頁標明的配方放料，先核對容器是否到位，再操作。從同一槽批次存取用 `transfer_slot_items`，指定回執的 `control` 和 `window_token`。從槽取出帶 `source="slot"`，放入帶實際來源 `inventory` 或 `showcase`，逐件檢查實到數量。
- 按鈕、單選與分頁用 `press_machine_button`，換頁帶當頁給出的標題與從 0 起算的 `tab_index`。滑桿用 `adjust_machine_control` 傳 `values=[數值]`，範圍滑桿傳 `[下限, 上限]`，再讀遊戲實際套用的值。化學成型機的輸出按鈕支援 `quantity` 1 至 64，對照 `produced` 和停止原因取貨。
- 回執指向 `item_panel` 時先讀第二層面板。用當頁 `panel_token` 和啟用的 `control` 操作，結果和新 token 見 `after`。遇 `select_panel` 就從 `panels` 選名稱，以 `panel` 指定要讀哪一張，再依可用控制聚焦或關閉。遇 `dedicated_panel` 看 `next`。若掃描後需回同一位客人的交易平板，用 `reopen_trade_tablet` 帶當次 `visit`。
- 水務可用 `inspect_purifiers` 查瓶貼、電力及微型淨水器耐久。用 `water_use` 裝水時以容器為 `source_id`、`"faucet"` 為 `target_id`；手持淨水器則以機器為來源、瓶子為目標。裝水、淨化、試紙可用 `apply_now`，排水、倒水、藥片與口糧先 `preview` 再 `apply`。試紙與藥片須先從容器取出單件。水商依瓶貼和讀值配貨，其他專精也可以按需求操作水務設備。

### 等待機台讀值（0.25.1）

先開機台視窗並取得當前憑證。加熱板以 `control="current_c"` 等容器實測溫度，其他支援的原生進度條用 `inspect_item_window` 回報的控制項路徑和數值單位。舉例，等加熱板達 45 °C 時，呼叫 `wait_for_machine` 帶加熱板的 `owner_id`、當前 `generation`、`revision`、`control="current_c"`、`direction="at_least"`、`threshold=45`、`timeout=20`。要等降到某個值用 `at_most`。逾時可設 0 至 60 秒，預設 10 秒。

收到 `threshold_reached` 或 `timed_out` 時看 `value`、`elapsed_ms`。遇 `window_closed`、`owner_changed`、`vessel_changed` 或 `reading_unavailable`，先確認機台及容器再重新觀察。等待只讀取數值，結束後機台沿原設定運轉；要改溫度或停機，操作當前原生控制項。原版 0 級與 1 級加熱板分別固定 99 °C 和 160 °C，2 級才有 ±5 °C 按鈕。

### 背景控溫（0.26.0）

在支援這個版本的遊戲模組與 MCP 服務上，使用工具清單中的 `control_heat`。先打開自有加熱板的視窗，確認機台在庫存或展示櫃、加熱槽裡是要處理的非空容器。從視窗回執取用機台 ID 和槽內容器 ID。

- `mode="start"` 必帶 `owner_id`、`vessel_id`、`min_c`、`max_c`、`seconds`、`generation`、`revision`。例如 `min_c=45, max_c=50, seconds=30` 會從開始時計算 30 秒。範圍為 26 至 160 °C，時長為正整數。開始回執會帶 `heat.run_id` 和策略，記下該 ID 即可關窗繼續接客。店帳已建立游標時，後續動作的 `shop_update.fields.heat` 會帶結束結果。同一版本下要再跑完全相同的設定，把前次結束的 `run_id` 帶進新一次 `start`。
- `mode="status"` 查目前或最近一次工作，從 `heat` 看狀態及可用溫度。機台可能使用遊戲原生目標溫度，也可能在低熱與關閉之間切換；這種切換可能有溫度超越，依實測讀值判斷成品或下一道工序。
- `mode="stop"` 必帶該次工作回傳的 `run_id`。收到 `already_running`，先看既有工作的狀態；遇 `manual_override`、`vessel_changed`、`target_unavailable` 或 `off_unconfirmed`，回查原生視窗確認容器與實際模式再繼續。原設定或容器由玩家改動時，先確認玩家的安排。以 `finished` 或 `stopped` 和 `mode_index` 核對這次工作的結束。

## 打烊、過夜與外出

用 `get_shop_status(verbose=True)` 查看證據、違禁品分區件數、通緝名單和 `last_save`。過夜後遊戲先進入次日早晨才存檔。

- 打烊前看剩餘客數及檯上留貨，用目前的 `reception_token` 呼叫 `close_shop`。若需要提前打烊，按回執核對剩餘客數和玩家交代，再以 `confirm_remaining` 確認。歇業後可以留店收貨、外出或睡覺。
- 要過夜時，用當天 `bill_token` 呼叫 `prepare_sleep` 查帳單；需要時用 `night_service` 調整服務。沿用最新憑證和帳單的 `service_cost` 呼叫 `sleep`。回 `awaiting_report` 時等當晚報告，改查店況及日數確認進度。收到報告，核對租金、垃圾、服務費、餘額與 `alerts`，並留意下次到期日、傷勢及保險。若通緝警示關係到來客身分，交給玩家鑑定。
- 外出先用 `shop_activity` 讀門口並按當前選項操作。如果回執開出選包提示，用 `response_choices` 的容量、`page` 和選項 ID 交給 `response_choice`。用 `travel_menu` 查可達地點，`mode="depart"` 帶可用的 `location_id` 出發；轉場用 `wait_for_scene` 帶出發前的 `session` 與 `after_revision` 等抵達。
- 抵達後用 `explore` 讀傷勢、左右手、背包與地面空間。在當地用它提供的物品 ID、槽位與 token 搬貨或搜尋。遇 `can_scavenge=false` 查 `scavenge_blocked_reason` 和 `notification`。要返店時先處理地面物；決定放棄時用 `mode="return", abandon_ground=True`。返店查帳及傷勢，再繼續安排當晚。
- 核廢料會占空間，垃圾桶無法消除。夜間歇業後，先用 `inspect_item_window` 和 `transfer_slot_items` 裝進放得下的大背包，到 `dumping_ground` 將它移到 `ground`，再確認放棄地面物返店。看 `get_shop_status` 的 `nuclear_waste` 核對在店、檯上和已丟棄數量。裝不下或得手持外出時，請玩家協助處理。

## 人脈、聲望與服務

- 用 `get_phone_contacts` 看聯絡人和業務。歇業後讀電話，用 `dial_contact` 帶目前的 `phone_token`、聯絡人號碼及店況版本撥號；用 `advance_phone_dialogue` 或 `choose_phone_service` 接續通話，每步使用最新 token。若回 `phone_unavailable`、`reason="shop_open"`，等打烊後再撥。
- 喬的人脈面板開啟時，用 `wild_network` 讀目錄，大清單加 `verbose=True` 看明細。用 `mode="select", target=服務ID` 看 `selected`、`favor_to_spend`、`cash_to_pay` 和餘額；確定購買時帶新 `panel_token`、`generation`、`revision`，以 `mode="buy"`、`target=選中的服務ID` 和原報價 `cost` 購買。核對 `favor_spent` 和 `cash_paid`。
- 用 `feedback_box` 看派系聲望、啟用效果及最近的正負門檻。預設續查回差異；想查完整門檻及未啟用項目時用 `full=True, verbose=True`。

回報卡點時，留下當時場景、想做的動作、工具、關鍵參數及原回執。工具入口與安裝方式見 [README](README.md)，實機驗收與待驗場景見[驗收表](docs/acceptance.md)。
