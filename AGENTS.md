# 白龍店帳 MCP 工具索引

用途是讓接手的助手知道眼前能做什麼、哪一步交給玩家，以及開發下一個工具時可沿用哪個遊戲入口。本文件記錄白龍店帳 **0.9.4**、遊戲內部版本 **0.46D**。舊版以白龍當鋪 **0.1.0**（`white_dragon_pawnshop`）啟用 SQLite 的完整工具集為準，兩個帳本工具只在啟用帳本時註冊。

## 接手營業時怎麼查

| 當下要做的事 | 0.9.4 可呼叫的工具 |
|---|---|
| 先看人在哪裡 | `get_scene`。轉場時用 `wait_for_scene` 等待抵達。當鋪以外目前只提供這兩個工具，拾荒時不查店況。 |
| 看現金、收租、垃圾桶、當晚費用 | `get_shop_status`，只查桶內明細可用 `get_trash`。日曆的 `event_scans` 是本次讀檔共重建幾次兩週日曆。 |
| 按名稱、位置、歸屬找貨，或查批發回款 | `query_inventory`、`query_purchase_lots`。批發回款以原批次核算。 |
| 歇業或晨間搬貨、看昨晚的淨水器 | `move_item`、`move_items`、`inspect_purifiers`、`put_purifier_bottle`、`take_purifier_bottle`。取瓶與驗水分開。 |
| 查固定檔案箱、存入或取回文件 | `inspect_archive` 可唯讀、開箱或關箱。取得箱號、槽位及 token 後，用 `transfer_slot_item` 存取；看 `actual_area` 和 `same_item_at_destination` 確認位置。 |
| 查機台、放料、取貨、執行機台操作 | 用 `inspect_item_window` 查物品內容視窗；`transfer_slot_item` 按原生槽位搬貨，`press_machine_button` 按當頁列出的按鈕。 |
| 開門、接客、等對方講完 | `get_counter` → `open_shop` → `ring_customer`，回執仍在等同一人時用 `wait_for_opening`。只查店況時停在唯讀查詢。 |
| 當日自動接客或打烊 | `set_auto_receive` 只在空櫃檯接下一位。用 `close_shop` 打烊，提前打烊要先讀剩餘人數，再帶回新 token 和人數確認。 |
| 買貨或賣貨 | 整批買入用 `buy_merchant_batch`；逐件賣出先 `get_buyer_context` 再 `sell_order`。插話時原售單會等候，使用含最初版本的相同參數取回進度或最終回執。 |
| 委託助手用水或耗材 | `water_use` 先預覽來源和目標，決定執行才用同一張 `use_token` 提交。玩家要親手驗水時，等玩家開啟試紙面板。 |
| 玩家抽完試紙後 | 用 `water_panel` 對回瓶號、檢測結果與試紙餘量。自有瓶才可選擇更新瓶貼。 |
| 買情報 | `get_counter` 或 `wait_for_opening` 讀 `info_receipt`。要買才呼叫 `pay_info_receipt`，核對 `paid` 和 `cash_after`。 |
| 電話與歇業後就寢 | `get_phone_contacts` 查可聯絡對象；決定撥號才用 `dial_contact`，通話中用 `advance_phone_dialogue` 或 `choose_phone_service`。歇業後的 `sleep` 會提交當晚費用並推進日期。 |

**回執與決策。** 首次或換會話可查完整內容視窗。同一視窗後續觀察傳回上次的 `window_token` 作為 `since_token`，只讀差異。搬貨或按鈕回執附上有效變動，連續進度留在遊戲內，真正要決策時再查。遇到 `window_changed` 要重看眼前的槽位或按鈕。

**買家要多種貨時。** 先看 `get_counter.lines`。例如遊客說「我想多要几种」，就按 `kind` 每種先挑一件。0.9.4 實機一張單列四件 `scrap_metal`，首件以 14 成交、另外三件逐件遭拒。現版售單的每列只附拒收狀態；需要理解新台詞時，分單讀同一位客人的櫃檯回執。

**玩家參與。** 來客像不像通緝照、包裝有沒有破綻、鑑定畫面上看到了什麼，都由玩家判斷。遇到這些情況就停在當前物品或來客，說清楚需要看哪裡，等玩家回覆再接著交易。兩個相關舊工具保留在索引，目視辨識與影像功能暫緩。原生 UI 已顯示的文字、瓶貼和數值仍可用文字回執呈現。

## 舊版逐項對照

42 個舊工具中，**13 個已接、18 個部分接上、9 個待接、2 個歸玩家協作**。按舊工具的操作範圍逐列統計，改名或拆成多個 MCP 工具都算在原來那一列。「已接」表示新版有對應的主要操作；實機與回放的驗證範圍見 [README](README.md#開發與驗證)。

右欄的「原生」表示已有遊戲方法或可見 UI 動作可呼叫，仍要接 MCP 和驗證結果。`ModHelper`、`ModHook` 是模組介面；其餘具名的管理器、按鈕與槽位屬於 **0.46D 遊戲實作**，升級時核對簽名和行為。「組合」表示須把幾個原生來源串成可操作流程；帳本、差異摘要、訂單與圖片回執則由橋接端負責。

### 查帳與交易

| 舊版 MCP 工具 | 0.9.4 | 新版入口與差異 | 遊戲入口及橋接工作 |
|---|---|---|---|
| `get_shop_ledger` | 部分 | `query_inventory` 依名稱、貨區、歸屬找貨；`query_purchase_lots` 查批次。舊帳還能離線列未完單與計畫。 | 自寫帳本。按存檔與來客保存原單、逐項結果及最後確認步驟。 |
| `dismiss_trade_ticket` | 待接 | 新版可用原參數查部分動作的既有回執，沒有列單後依單號及版本結案的工具。 | 自寫帳本。核對單號與版本後只結案，不重送遊戲動作。 |
| `get_shop_updates` | 部分 | `query_inventory` 匯入庫存增量後回傳符合條件的現況。缺舊版的 `since` 差異、開關門檢查點和隨動作回傳的差異摘要。 | 自寫回執。沿用已有 generation、revision 與庫存日誌。 |
| `get_shop_state` | 部分 | `get_shop_status` 查日曆、現金、垃圾與夜費；`get_counter` 查隊列、台詞和貨單；`query_inventory` 查四貨區。舊版的全店單張快照還含視窗與互動選項。 | 組合讀取。優先查對應工具，新增可見欄位時才補快照。 |
| `get_trade_context` | 部分 | `get_counter` 有開場台詞、賣方貨單和可見瓶貼；`get_buyer_context` 有來客和可用預算。缺舊版固定需求摘要、逐瓶容量與最近的試紙讀數。 | 自寫摘要；預算讀遊戲，試紙結果須由已顯示的驗水結果取得。 |
| `wait_for_shop_event` | 部分 | `wait_for_opening` 等同一來客，會翻普通頁並等交易面板；`get_counter` 可查看隊列。缺獨立等待貨物擺出、下一客可接的模式。 | 組合等待。觀察櫃檯狀態與原生隊列，區分就緒、換客、需輸入和超時。 |
| `trade_batch` | 部分 | `sell_order` 可逐件設底價；`buy_merchant_batch` 為整批買入上限。缺同單混合買賣與任選多件逐件設買入上限。 | 自寫訂單。成交沿用原生交易，逐件核對歸屬與金額。 |
| `confirm_trade` | 部分 | 單件出售可用一列 `sell_order`；買入只接整張賣方貨單。舊工具可按眼前單件的確切報價確認買或賣。 | 原生 `NegociationUIManager.BuyItem/SellItem`；橋接核對報價、物件與當次來客。 |
| `buy_wholesale` | 已接 | `buy_merchant_batch` 綁定當次整批貨物、開價與買入上限；`query_purchase_lots` 追蹤該批回款。 | 原生 `NegociationUIManager.OnWholesaleClick`；回款由店帳累計。 |

### 櫃檯、對話與日結

| 舊版 MCP 工具 | 0.9.4 | 新版入口與差異 | 遊戲入口及橋接工作 |
|---|---|---|---|
| `open_shop` | 已接 | `open_shop` 只在可開門的早晨操作，回執確認營業階段與人數。 | 原生 `StoreShutterButton.OnClick`。 |
| `call_next_customer` | 已接 | `ring_customer` 在空櫃檯按一次鈴並等開場，使用眼前隊列人數。 | 原生 `StoreBell.RingBell`。 |
| `dismiss_customer` | 已接 | 用當次 visit 送客，回執帶客人與剩餘人數。劇情攔下時交回對話。 | 原生 `PlayerStore.TryPlayerDismissCurrentClient`。綁定當次來客，送走後別連帶按鈴。 |
| `advance_dialogue` | 部分 | `wait_for_opening` 接續開場普通頁；`advance_phone_dialogue` 管電話。缺其他櫃檯對話的單頁指令。 | 原生 `DialogUIManager.OnAdvancePressed`。核對當前頁正在等輸入。 |
| `choose_dialogue` | 待接 | 能停在選項前；`choose_phone_service` 僅能選電話業務。 | 原生 `DialogChoiceBubble.OnDialogChoiceClick`。列出可見文字與可選狀態，核對同一頁再選。 |
| `response_choice` | 待接 | 缺以物品呈現的劇情回應選項。 | 原生 `GameItem.ActivateSlotItem`。先列眼前選項 ID 與文字。 |
| `close_shop` | 已接 | `close_shop` 在空櫃檯提交打烊，回執確認店門狀態。 | 原生 `StoreShutterButton.OnClick`。檢查櫃檯與來客，回報是否彈出提早打烊確認。 |
| `confirm_close_shop` | 已接 | `close_shop` 顯示剩餘人數，再以新 token 和準確人數確認。 | 原生 `StoreShutterButton.EndAndClose`。確認必須對應眼前的提早打烊提示。 |
| `prepare_sleep` | 部分 | `get_shop_status` 可唯讀看夜費，`sleep` 提交時會開睡袋。缺只打開帳單供玩家挑服務的模式。 | 原生 `StoreBedroll.OnClick`，開完帳單就回執。 |
| `night_service` | 待接 | `get_shop_status` 有已選服務總額，缺單項清單及切換。 | 原生 UI `TabletCheckbox.Toggle`；組合服務 ID、費用、必選與冷卻狀態。 |
| `confirm_sleep` | 已接 | `sleep` 核對日期、版本和已選費用。`sleep_submitted` 表示送出，`slept` 才表示看見隔日。 | 原生 `EndOfDayUIManager.OnEndDayButtonClick`。跨夜目前為回放驗證。 |

### 貨區、物品與機台

| 舊版 MCP 工具 | 0.9.4 | 新版入口與差異 | 遊戲入口及橋接工作 |
|---|---|---|---|
| `move_item` | 部分 | `move_item`、`move_items` 支援晨間或歇業時自有貨在庫存與展示櫃互搬；前櫃與交易櫃的任意單件搬運未開放。 | 原生 `ModHelper` 搬貨及 `GraphUtils.TryAcceptOnce`，加貨區與交易歸屬核對。 |
| `container_move` | 部分 | `take_purifier_bottle` 能把淨水器的瓶取回庫存；`put_purifier_bottle` 可從庫存或展示櫃直接放瓶。其他自有容器可用通用內容視窗與槽位；限庫存和展示櫃的頂層容器，巢狀容器尚未接入。 | 原生 `MachineHelper.GetBodyStorage`、`GraphUtils.TryAcceptOnce`；回執核對來源與目標槽。 |
| `move_furnace_item` | 部分 | 通用槽位支援原生面板公開的原料、產出等角色；已回放原料存取與產出槽禁放，實機各槽待驗。 | 組合原生機台槽和 `GraphUtils.TryAcceptOnce`；橋接需對應槽位，產出槽僅取回。 |
| `get_tool_windows` | 部分 | `inspect_purifiers` 能讀淨水器、瓶貼、電力和試紙數。通用工具可按物品 ID 讀內容視窗的當前分頁、槽位、按鈕、文字和進度。情報收據透過櫃檯讀取。全視窗清單與專屬能耗欄位仍待補。 | 組合 `PixelWindow`、機台控件與 `PowerHelper.GetAvailableEnergyFromItem`；自寫可見控件和欄位對照。 |
| `press_machine_button` | 部分 | 同名工具能按本次視窗列出的具名啟用鍵；回傳按下結果及當下變動，第 4 欄已從產出槽差異確認新增小瓶。其他機台按鍵仍逐項實測。 | 原生機台按鈕 `Select()`；橋接重驗物品、控件、文字與可用性，列印後核對新增物品。 |
| `pay_info_receipt` | 已接 | 開場結束後從櫃檯讀可見開價，決定購買才提交同一收據與金額。付款回執含實扣與現金餘額。 | 原生 `NegociationUIManager.OnOfferAcceptClick`；橋接核對情報商、可見價格與實扣。 |
| `wine_start` | 部分 | 若發酵鍵以具名機台按鈕顯示，可用通用 `press_machine_button`；酒瓶實機待驗。 | 原生機台按鈕 `Select()`；按畫面文字與可用狀態定位發酵鍵。 |
| `water_use` | 已接 | 倒水、補給、藥片、攜帶式淨水器及新試紙。預覽後明確提交，逐項回報水量、耗材、充能或試紙關聯瓶號。 | 沿用原生選取與指定目標流程，先派送 `ItemBehaviourManager.Target` 再重驗 `GameItem.MayTarget/CanTarget`。實測結果只取已顯示的同瓶面板。 |
| `water_panel` | 部分 | 玩家驗水後可讀面板、自有瓶套用瓶貼及關閉面板。淨水器提示的確認仍待接。 | 原生 `WaterStripUIManager.currentWaterContainer`、面板文字、`CanMatch`、`OnMatch` 與 `Close`。核對本次面板與瓶號，回傳實際瓶貼。 |
| `archive` | 已接 | `inspect_archive` 開關固定檔案箱；`transfer_slot_item` 存取指定文件。箱內增減與實際目的地在同張回執，第 4 欄已驗證。 | 組合原生容器視窗與 `GraphUtils.TryAcceptOnce`，沿用箱子的限制。 |
| `inspect_item` | 玩家協作 | 舊版含讀提示、打開／關閉、放大、翻頁。新版只回現有品名與瓶貼等欄位。看細節、鑑定真偽由玩家處理。 | 暫緩目視流程。日後若單需文字提示，可獨立接物品文字與 `ItemMouseDoubleClickHandler.OpenExamineAction`；不需加圖像推理。 |
| `compare_wanted_portraits` | 玩家協作 | 舊版能並排展示客人與海報原圖或高清參考。新版未接；辨人由玩家接手。 | 暫緩。原生有來客立繪、通緝單與 `WantedElement`，拼圖及 MCP 圖片回傳要自寫。 |

### 電話、人脈與場景

| 舊版 MCP 工具 | 0.9.4 | 新版入口與差異 | 遊戲入口及橋接工作 |
|---|---|---|---|
| `phone_call` | 已接 | 先 `get_phone_contacts`，再 `dial_contact` 撥可見聯絡人。通話本身和選業務是兩個動作。 | 原生 `PhoneUIManager.OpenUI/AutoCall`；核對聯絡人與 `phone_token`。 |
| `phone_advance` | 已接 | `advance_phone_dialogue` 僅翻可前進的普通電話頁。 | 原生 `DialoguePhoneUIManager.OnAdvancePressed`。 |
| `phone_choice` | 已接 | `choose_phone_service` 選可見且可用的通話選項。獨立人脈服務視窗另見下一列。 | 原生對話按鈕搭配當頁 `phone_token`。 |
| `wild_network` | 待接 | 可撥電話進入喬的人脈業務；缺人脈視窗的分頁、選取、購買與關閉。 | 組合 `WildNetworkUI` 視窗、目前選項、費用與原生按鈕。付款後核對實扣和升級。 |
| `phone_dial` | 部分 | `dial_contact` 限清單上的號碼；缺按遊戲電話鍵盤逐位輸入其他已知號碼。 | 原生 `PhoneUIManager.PressNumber`。 |
| `activity` | 部分 | `dial_contact` 會打開電話，`get_scene` 可見轉場。缺獨立開關電話、關報告／地圖、出門選包、拾荒與返店動作。 | 依場景組合 `StoreDoor.OnClick`、`ScavHelper.CanScavenge`、`MapUIManager.ScavengeButtonClick`、`ModHelper.ReturnFromAfterhour` 與各視窗開關。 |
| `travel` | 待接 | `get_scene`、`wait_for_scene` 能確認去向與抵達；缺列出已發現地點並主動出發。 | 原生 `AfterhourLocation.GetDiscovered`、`MapUIManager.Travel`。核對名稱和可前往狀態，沿用場景回執。 |
| `move_exploration` | 待接 | 缺外出時地面、左右口袋與背包裝備槽的搬運。 | 組合夜間貨區與 `GraphUtils.TryAcceptOnce`；建立該場景的物品位置回執。 |
| `exploration_container` | 待接 | 缺外出背包的開關、存入和取出。 | 組合原生容器、容量規則與 `GraphUtils.TryAcceptOnce`；區分內嵌格子和獨立背包視窗。 |
| `shotgun` | 待接 | 舊版有遊戲內的拿起、收回和對眼前來客確認操作。新版未提供。 | 原生遊戲互動與角色狀態已有，橋接需核對來客與玩家的明確選擇。 |

## 實機驗收表

物品編號跟著讀檔重新分配，以下編號只用來識別當次實測。接手新會話時先確認存檔和場景。

| 功能 | 最近一次證據 | 下次需要的場景或條件 |
|---|---|---|
| 水務操作 | 0.9.2 回放覆蓋倒水、龍頭與排水、配給、藥片、攜帶式淨水器成功及提示、商人換客與試紙歸瓶。預覽零消耗，重送同一操作沿用原回執。 | 各耗材與設施待實機。第 4 欄第一瓶由玩家親手驗，這次只驗下列面板；之後另找適合的水瓶驗工具動作。 |
| 試紙面板、更新瓶貼與關閉 | 0.9.1 回放確認商人瓶可讀、自有瓶更新到指定瓶號、換瓶拒絕舊操作，試紙餘量相同。 | 0.9.4 第 4 欄第 10 天，玩家親驗拾荒客的 43 號大瓶。面板回瓶號 43，實測與瓶貼均為基礎水，純度 98.23%，商人所有，試紙剩 5 張，與晨檢工具一致。MCP 關閉同瓶面板成功。自有瓶實際改貼及庫存增量仍待驗。 |
| 情報商報價與付款 | 0.9.0 遊戲端回放已驗。開價 13、現金 100，付款回執實扣 13、餘額 87；重送只扣一次。 | 待第 4 欄自然遇到情報商，聽完讀價。若決定購買，再對照實扣與遊戲上的剩餘現金。 |
| 固定檔案箱開關與文件往返 | 0.8.1 第 4 欄早晨。5 號情報紙條（存檔 ID 285）從庫存進 28 號箱，再回庫存。回執各報一件增減。續查無差異，已關箱，盤點 1 次。 | 已實機驗收。 |
| 通用機台視窗、取出產物與按鈕 | 0.8.0 第 4 欄的 14 號列印機。打開後讀到電池、輸出槽和三個按鈕。續查無差異；29 號小瓶取回庫存，按一次小瓶鍵後差異新增 30 號小瓶。再查輸出槽一致。 | 小瓶已實測。中、大瓶鍵當時未啟用，待已解鎖且有電的列印機。 |
| 其他專精機台 | 熔爐原料存取及產出槽禁放已通過回放。 | 有熔爐與可用礦料時實測各槽；有備妥材料的酒瓶時實測發酵按鈕及後續狀態。 |
| 晨間查帳與電話 | 0.8.1 第 4 欄查店況、垃圾桶、櫃檯、買家資料、批發帳、淨水器和電話簿均成功。日曆 1 輪／14 次，1 台淨水器、6 張試紙、2 位聯絡人。 | 電話撥號與選業務待可接聽的聯絡人及營業安排；回放已驗。 |
| 自動接客、提早打烊、分批交易 | 0.9.2 第 4 欄第 10 天開門有 9 位排隊。啟用自動接客後，首位「口渴的星際客」自行入場，隊列剩 8 位。客人開場明說要買基礎水，櫃檯另列兩瓶合計 9 的非自有物品，交易方向要看台詞。開門及啟用回執的盤點數均為 1。 | 0.9.3 已確認現客交易後仍在場時按鈴回 `occupied`，自動接客維持等候。0.9.4 第 4 欄首兩位買家成交後，以各自 visit 送客成功，重查同一回執，自動接客迎入下一位。拒絕送走劇情角色另有回放。0.9.4 第 4 欄第 10 天完整接待 8 位，排隊歸零後正常打烊，放棄 0 人。開門現金 218、打烊 418，全日盤點 1 次。尚待有意提前打烊時驗人數確認。批發回款待實際進貨和後續售出，同款退貨待有預算不足的買家。 |
| 出售上櫃後客人插話 | 0.9.2 第 4 欄的前兩位買家均在展示瓶上櫃後收到 `buyer_or_ui_changed`。第一筆由遊戲按鈕成交 58。第二筆 25 號仍在後櫃，現金 276；MCP 等到平板再開，接回同瓶卻回 `source_changed`。 | 0.9.3 第 4 欄重開後，25 號（存檔 ID 203）底價 25、遊戲報價 41，拾荒客拒收，瓶子回展示櫃。26 號基礎水插話後按原單成交，實收 58，店款 218→276，預算 130→72。兩張原單分別重查，回執相同，盤點 1 次。0.9.4 重跑同欄，第二位星際客以 41 購買 25 號幽靈水，原底價 25。預算 114→73，店款 276→317，原單重查完全相同，下一客店況確認現金 317。單獨誤關平板與換客拒賣另有回放；0.9.2 現場關閉原因未定。 |
| 跨夜垃圾、就寢與夜間轉場 | 已有回放；晨間只核對店況。 | 用已歇業且可推進日期的測試檔驗垃圾及就寢。夜間往返另待玩家實際出門。 |

## 維護這份索引

- 工具註冊以 [pawnshop_tools.py](pawnshop_tools.py)、[shop_day.py](shop_day.py)、[scene.py](scene.py) 為準。0.9.4 共 33 個，當鋪就緒時全數列出。場景工具的可見性由 [scene_server.py](scene_server.py) 控制。
- 原生入口按責任查找。讀檔與貨區變動看 [LedgerMod.cs](mod/LedgerMod.cs)，搬貨與取瓶看 [Move.cs](mod/Move.cs)，日曆看 [Calendar.cs](mod/Calendar.cs)，垃圾桶與睡覺看 [ShopDay.cs](mod/ShopDay.cs)，轉場看 [Scenes.cs](mod/Scenes.cs)。
- 櫃檯、訂單與人脈分別看 [Counter.cs](mod/Counter.cs)、[SaleOrder.cs](mod/SaleOrder.cs)、[Phone.cs](mod/Phone.cs)。情報商收據在 [InformationReceipt.cs](mod/InformationReceipt.cs)。批發及晨檢觀察在 [ShopDesk.cs](mod/ShopDesk.cs)，SQLite 庫存查詢在 [ledger.py](ledger.py)。
- 舊版依據為白龍當鋪 0.1.0 的 `probably_stolen_mcp.py:create_server`。本機封存的 `local-0.12.2.13` 註冊了同名 42 項；更早的 `backups/before-local-0.12.2.1` 有 32 項，均包含在表內。這裡記錄工具行為與遊戲入口，供新版獨立實作。
- 工具新增、拆分、改名或操作範圍改變時，同步改對應列與統計。純查詢、回放、實際進遊戲操作的證據分開記錄。睡覺、打烊和付款等動作的實測使用另行指定的測試檔。
