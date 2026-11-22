# 白龍店帳 · 助手操作指南

## 接手時

1. 呼叫 `get_scene()`，核對場景和存檔欄位。依目前 MCP 工具清單行動。玩家交代「先不開店」「不睡覺」或「我來驗」時，就停在對應動作之前。
2. 在當鋪先用 `get_shop_updates()` 取得庫存基準，記下 `cursor`。同條連線後續店務動作會附上變動（無變動時精簡為 `{"status":"unchanged",...}`）；重新連線可帶 `since=cursor` 接續。要看現金、交租倒數、夜間報告、店鋪吸引力或剩餘客數，再查 `get_shop_status` 或 `get_counter`。
3. 中途接班時，用實際欄位呼叫 `get_shop_ledger(slot=…)`。有未完單就用同一個 `ticket_id` 查原參數與最新回執。讀檔後重新取得 `generation` 和物品編號。
4. 使用回執提供的版本、來訪編號與 token。不確定交易是否成交，先用原單和原參數查結果。若已換客或畫面已變，重讀當前場景，再決定下一步。

## 接一位客人

- **接客。** 玩家要開始營業時用 `open_shop` 開門。營業時看 `get_counter` 的排隊人數與櫃檯狀態，空櫃檯才用 `ring_customer`。開場仍在進行，用 `wait_for_opening` 等同一位說完。再用 `get_trade_context` 讀台詞、剩餘預算、買賣方向與報價。
- **配貨。** 以交易平板的 `demand` 為準；條件區未顯示就保留未知。用 `find_stock` 按需求分類、瓶貼、位置及自有狀態找貨，一批編號用 `get_stock_items` 查。「種類多樣」看 `kind_groups`，每種只出一件，客人買過的種類就收起來。
- **成交。** 向商人整批買用 `buy_merchant_batch` 設總價上限；逐件賣用 `sell_order` 設底價，整批一次賣可加 `bundle=True`；混合買賣用 `trade_batch`。遇插話或 `sale_pending`，沿用原單。成交後看實收／實付、物品位置和剩餘預算。
- **交換或買情報。** `barter.options` 裡的 `give` 是店家付出、`receive` 是換回。先用 `barter_offer` 選方案，把付款物品移到交易檯，再用新 `barter_token` 確認。情報商則先看 `info_receipt` 開價，決定買才用 `pay_info_receipt`。
- **送客。** 用 `dismiss_customer` 送走該次 `visit`。收到 `dismiss_unavailable` 就等同一位可以離開，再用原參數重試；`visit_changed` 表示已換人，停手。

## 其他常用操作

| 場景 | 怎麼做 |
|---|---|
| 新遊戲 | 玩家點「新遊戲」後，依序用 `get_specializations`、`choose_specialization`、`starting_perks` 和 `starting_difficulty`。配點後看剩餘點數、格數與互斥。難度的 `select` 只選中卡片。 |
| 後櫃收貨 | 開店報紙、便條與來客名片會自動收進庫存。收不下時看 `document_cleanup`。需手動收貨時，用 `query_inventory(area="front", owned=True)` 或後櫃 `back` 查一次。將選好的 `item_ids` 和該次的 `generation`、`revision` 傳給 `move_items`，用實際來源與 `destination="inventory"`。有客時傳當前 `visit`，空櫃檯省略。打烊回執的 `reason` 是 `trade_table_not_empty` 時也走這條路。 |
| 讀搬貨回執 | 逐件看 `items[].status` 和 `actual_area`。堆疊只搬了一部分時，`observed_quantity` 是實到數量。`visit_changed` 表示來客了，先停手；`not_attempted` 的項目留待櫃檯空下來再查。 |
| 機台與容器 | `inspect_purifiers` 查瓶貼、電力和試紙。`inspect_item_window` 讀機台或容器，同一槽多件用 `transfer_slot_items` 傳一次編號清單、來源和視窗 token。玩家親手驗水後，用 `water_panel` 核對瓶號與結果。 |
| 文件 | 讀自有報紙、便條或地下交易所清單，用 `inspect_item_window` 看 `window.document`。 |
| 聲望與人脈 | `feedback_box()` 預設（`verbose=False`）回精簡聲望、啟用效果與最近正負門檻，後續回差異；要完整未啟用清單加 `full=True, verbose=True`。用 `get_phone_contacts` 查聯絡人與業務，決定聯絡才撥號。喬的人脈網路用 `wild_network`。進入或切頁後 `read` 取清單。用 `select` 看 `selected` 的報價、可購狀態及餘額。要買就用該回執的 `generation`、`revision`、`panel_token`，帶選中 ID 和原價。買後沿用新回執。 |
| 打烊與過夜 | `close_shop` 提早打烊時會列剩餘客數。歇業後用 `prepare_sleep` 看當晚帳單，`night_service` 改服務；要推進到隔天才用 `sleep`（回執與 `get_shop_status` 均可看當早的 `night_report.day`、逐行文字和通緝來訪提醒 `alerts`，另附 `appeal` 與 `status_summary`）。 |
| 刮刮卡 | 從庫存明細取 `item_id`（帳本編號）與 `save_id`。依序呼叫 `scratch_card` 的 `read`、`open`、`reveal`（position 1／2／3 為左／中／右）或 `reveal_all`（一次刮完剩餘格），最後 `close` 關面板；沿用上一張回執的 session、revision 和 view_token。 |
| 夜間拾荒 | `shop_activity` 看門口與出門確認；`door`／`confirm_packing` 若立即開出選包提示會直接附 `page` 與 `response_choices`，接 `response_choice`。`travel_menu` 的 `select`／`previous`／`next` 直接回最新 `view_token` 與 `view`，也可直接 `depart(location_id=…)` 一步出發，`wait_for_scene` 等抵達。`explore` 搬貨或搜索後直接沿用回執的 `view_token` 和 `view.slots`；必要時可用 `mode="return", abandon_ground=True` 捨棄地面物返店。返店後再查店帳。 |

通緝照、可疑包裝或鑑定畫面需要玩家辨認時，指出是哪位客人或哪件物品，等玩家判斷。原生台詞、平板文字、瓶貼與數值優先用 MCP 讀。

開發新工具前先查[遊戲原生 API](docs/native-api.md)。驗收結果與待驗項目記在[實機驗收表](docs/acceptance.md)。
