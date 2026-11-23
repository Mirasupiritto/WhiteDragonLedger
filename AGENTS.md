# 白龍店帳 · 助手操作指南

## 接手時

1. 呼叫 `get_scene()`，核對場景和存檔欄位。MCP 工具在所有場景維持常駐註冊，在非對應場景呼叫會回 `tool_status:"unavailable_in_scene"`。玩家交代「先不開店」「不睡覺」或「我來驗」時，就停在對應動作之前。
2. 在當鋪先用 `get_shop_updates()` 取得庫存基準，記下 `cursor`。同條連線後續店務動作會附上變動（無變動時精簡為 `{"status":"unchanged",...}`）；重新連線可帶 `since=cursor` 接續。`get_shop_status`、`get_counter` 與 `query_inventory` 在 `calendar`/`trash`/`sleep` 未變動時預設省略（帶 `verbose=True` 取全貌）。
3. 中途接班時，用實際欄位呼叫 `get_shop_ledger(slot=…)`。有未完單就用同一個 `ticket_id` 查原參數與最新回執。讀檔後重新取得 `generation` 和物品編號。
4. 使用回執提供的版本、來訪編號與 token。不確定交易是否成交，先用原單和原參數查結果。若已換客或畫面已變，重讀當前場景，再決定下一步。

## 接一位客人

- **接客。** 玩家要開始營業時用 `open_shop` 開門。營業中優先用 `next_customer` 一次完成叫鈴（回 `status:"called"`）、等開場與讀平板（若已在場則等至可交易並回 `already_present`）；需分步或細看時再用 `get_counter`、`ring_customer`、`wait_for_opening` 與 `get_trade_context`。大貨單（逾 5 件）預設回分類與總價摘要，要逐件明細加 `verbose=True`。
- **配貨。** 以交易平板的 `demand` 為準；條件區未顯示就保留未知。用 `find_stock` 按需求分類、瓶貼、位置及自有狀態找貨（查 `WATER` 亦含空水容器，見 `water_ml`/`empty`/`contains_water`），一批編號用 `get_stock_items` 查；清單附 `size`/`slot`/`rotatable` 與參考估值 `estimated_value`。「種類多樣」看 `kind_groups`，每種只出一件，客人買過的種類就收起來。物品身分揭露改名（如贗品）時，回執與 `shop_update` 會附 `identity_changed{id,from,to}`。
- **成交。** 向商人整批買用 `buy_merchant_batch` 設總價上限（`shop_update.upsert` 僅列額外異動而不重列 `items`）；逐件賣用 `sell_order` 設底價，整批一次賣可加 `bundle=True`；混合買賣用 `trade_batch`。上架前若交易檯僅剩非本單自有物品（不含核廢料）會先自動收回庫存並附 `cleared_items`；遇 `counter_unavailable` 或 `stale_order` 可依 `reason` 與 `blocking_items` 排除。遇到必收核廢料對話時，`get_trade_context` 會標 `forced_choice: true` 與後果說明，依此選擇或通知玩家。遇插話或 `sale_pending`，沿用原單。成交後優先沿用回執附帶的 `revision`/`reception_token` 與剩餘預算。
- **交換或買情報。** `barter.options` 裡的 `give` 是店家付出、`receive` 是換回。先用 `barter_offer` 選方案，把付款物品移到交易檯，再用新 `barter_token` 確認。情報商則先看 `info_receipt` 開價，決定買才用 `pay_info_receipt`。
- **送客。** 交易後優先用 `finish_customer`（或 `dismiss_customer`）送走該次 `visit`，非本單的自有雜物（不含核廢料）會在送客前自動收回且不再攔截。收到 `dismiss_unavailable` 先看 `reason`、`blocking_items` 與 `reception_token`，等同一位可以離開再用原參數重試；`customer_left`／`customer_replaced` 表示原客已離店或換人，停手。

## 其他常用操作

| 場景 | 怎麼做 |
|---|---|
| 新遊戲 | 玩家點「新遊戲」後，依序用 `get_specializations`、`choose_specialization`、`starting_perks` 和 `starting_difficulty`。配點後看剩餘點數、格數與互斥。難度的 `select` 只選中卡片。 |
| 後櫃收貨 | 開店報紙、便條、情報紙條（`slip`／`sec_slip`）、名片與夜間返店的腰包／背包會自動收進庫存；收不下時看 `document_cleanup`。需手動收貨時，用 `query_inventory(area="front", owned=True)` 或後櫃 `back` 查一次。將選好的 `item_ids` 和該次的 `generation`、`revision` 傳給 `move_items`，用實際來源與 `destination="inventory"`。無進行中訂單時（含 `ready`、僅買／情報／對話等來客階段）可帶當前 `visit` 收回，空櫃檯省略。打烊或交易回執的 `reason` 是 `trade_table_not_empty` 時也走這條路。 |
| 核廢料處理 | `get_shop_status.nuclear_waste` 列出 `{in_shop, on_counter, dumped}`。若因 `nuclear_waste_on_counter` 擋住交易或打烊，先用 `move_items` 把核廢料移進 `inventory` 或 `showcase`；垃圾桶無法消除核廢料，放櫃檯過夜隔早會回到 `front`。要徹底處理須在夜間 `CLOSED` 後，在店內用 `inspect_item_window` ＋ `transfer_slot_items` 將核廢料裝進夠大的背包（如 `6x6` 小背包以上；`3x3` 腰包放不下），帶去 `dumping_ground` 移到 `ground` 後帶 `abandon_ground=True` 返店（丟棄達標會觸發喬治奧來店歸還一次）。若庫存無空間、缺少大背包或須放進雙手帶出門，停下請玩家在遊戲 UI 騰出空間或手動裝包／手持處理。 |
| 讀搬貨回執 | 逐件看 `items[].status` 和 `actual_area`。物品已在目標位置（含前／後櫃自收歸庫）會回 `already_in_destination`。堆疊只搬了一部分時，`observed_quantity` 是實到數量；放不下會回 `no_space` 或 `showcase_full` 並附 `item_size` 與 `target_capacity`。`visit_changed`（空櫃檯進客）或 `customer_left`／`customer_replaced` 表示來客狀態已變，先停手；`not_attempted` 的項目留待櫃檯空下來再查。 |
| 機台與容器 | `inspect_purifiers` 查瓶貼、電力、試紙與微型淨水器（`charge_remaining`／`liters_remaining`）。`inspect_item_window` 讀機台或容器（含槽位 `capacity`），同一槽多件用 `transfer_slot_items` 傳一次編號清單、來源和視窗 token。`water_use` 傳 `target="faucet"` 是從水龍頭裝進 `source_id` 容器，傳 `water_tablet_container` 會提示 `open_container_first`；低風險水務（裝水 `faucet`、淨化 `purify`、試紙 `test`）可設 `mode="apply_now"` 一步完成（排水、瓶對瓶倒水、藥片與口糧仍須 `preview`＋`apply`）。多空瓶批次裝水並淨化可用 `fill_and_purify_bottles`，回每瓶結果與剩餘電量。`purifier_used` 附 `liters_remaining`，`tablet_used` 附 `effect_pending` 說明。玩家親手驗水後，用 `water_panel` 核對瓶號與結果。 |
| 文件 | 讀自有報紙、便條、地下交易所清單與手冊／圖鑑，用 `inspect_item_window` 看 `window.document`（若遊戲手冊頁面純為圖片無可讀文字元件會回 `readable:false, reason:"image_only_guide"`）。 |
| 聲望與人脈 | `feedback_box()` 預設（`verbose=False`）回精簡聲望、啟用效果與最近正負門檻，後續回差異；要完整未啟用清單加 `full=True, verbose=True`。用 `get_phone_contacts` 查聯絡人與業務，決定聯絡才撥號；營業中會回 `phone_unavailable`（`reason:"shop_open"`），操作回執會附最新 `phone_token`。喬的人脈網路用 `wild_network`，大清單預設回分類摘要 `service_summary`（`verbose=True` 看全表）。用 `select` 看 `selected` 的報價、`favor_to_spend`／`cash_to_pay` 分帳、可購狀態及餘額。要買就用該回執的 `generation`、`revision`、`panel_token`，帶選中 ID 和原價。買後沿用新回執。 |
| 打烊與過夜 | `close_shop` 提早打烊時會列剩餘客數。歇業後用 `prepare_sleep` 看當晚帳單，`night_service` 改服務；要推進到隔天才用 `sleep`（等到當晚 `night_report` 就緒才回 `slept`，仍未就緒時回 `awaiting_report` 而不沿用舊報；`alerts` 含通緝來訪、`garbage_warning`、`insurance_lapsed`、`injury_healed` 等模板警示，另附 `appeal`、店鋪 `capacities` 與含 `injury` 的 `status_summary`）。 |
| 刮刮卡 | 從庫存明細取 `item_id`（帳本編號）與 `save_id`。依序呼叫 `scratch_card` 的 `read`、`open`、`reveal`（position 1／2／3 為左／中／右）或 `reveal_all`（一次刮完剩餘格），最後 `close` 關面板；沿用上一張回執的 session、revision 和 view_token。 |
| 夜間拾荒 | `shop_activity` 看門口與出門確認；`door`／`confirm_packing` 若立即開出選包提示會直接附 `page` 與含背包 `capacity` 的 `response_choices`，接 `response_choice`。`travel_menu` 的 `select`／`previous`／`next` 直接回最新 `view_token` 與 `view`，也可直接 `depart(location_id=…)` 一步出發，`wait_for_scene` 等抵達。`explore` 與 `view` 附 `size`/`slot`/`rotatable`、`view.capacities`（左右手、背包、地面）、當前傷勢 `injury`、`can_scavenge=false` 時的 `scavenge_blocked_reason` 及原生提示 `notification`，放不下回 `no_space`（附尺寸與剩餘空間）；搬貨或搜索後直接沿用回執的 `view_token` 和 `view.slots`；必要時可用 `mode="return", abandon_ground=True` 捨棄地面物返店。返店後再查店帳與 `status_summary.injury`。 |

通緝照、可疑包裝或鑑定畫面需要玩家辨認時，指出是哪位客人或哪件物品，等玩家判斷。原生台詞、平板文字、瓶貼與數值優先用 MCP 讀。

開發新工具前先查[遊戲原生 API](docs/native-api.md)。驗收結果與待驗項目記在[實機驗收表](docs/acceptance.md)。
