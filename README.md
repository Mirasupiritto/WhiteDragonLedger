# 白龍店帳 · White Dragon Ledger

白龍店帳讓 AI 助手透過本機 MCP 經營《深空當鋪》（Probably Stolen）。它能聽完客人的需求、查庫存、談價買賣，也能幫你決定今晚要留多少錢交租。玩家可以只交代一件事，例如「這批水最多花 90，買完告訴我實付多少」，也可以讓助手經營一天。

**0.21.0 · Windows · 遊戲版本 0.46D · MIT**。支援本機 stdio MCP 的助手就能使用，Computer Use 為選配。遊戲提供台詞、交易條件、物品位置和成交金額；通緝照比對、包裝驗偽與目視鑑定由玩家協助。

## 第一次讓助手看店

> 看一下我在哪個存檔、今天是第幾天、離收租還有幾天。再看看淨水器裡是哪瓶、剩幾張試紙。先不開店。

助手先確認場景與欄位，再讀店況與機台。模組在讀檔時盤點一次，之後記錄貨物移動和數量變化。批次搬貨只需一張回執。SQLite 帳本保留庫存與未完交易，換會話也能按存檔欄位接手。

| 想讓助手做的事 | 可以看到的結果 |
|---|---|
| 配新角色 | 玩家從主選單點「新遊戲」，助手比較專精、分配特性點、選難度。選好停在難度頁，由玩家按「開始遊戲」。見[專精](docs/specializations.md)、[特性](docs/starting-perks.md)、[難度](docs/difficulties.md)比較表。 |
| 接客與交易 | 讀交易平板的需求、限制和預算，按瓶貼或分類找貨（含 `size`/`slot`/`rotatable` 與 `estimated_value` 參考估值）。可設整批買入上限、逐件或整批（`bundle=True`）售價底線，或比較拾荒客的三個交換方案。成交回執列出實收或實付；驗出贗品等身分變更時附 `identity_changed`；無變動時 `shop_update` 精簡為 `unchanged`。 |
| 整貨與查帳 | 空櫃檯可將多件自有貨從後櫃一次收回庫存，也可批次搬貨到展示櫃；附區域容量與剩餘最大矩形 `{w,h,free_cells,largest_free}`，放不下會回報 `no_space`／`showcase_full`。可查機台槽位、微型淨水器餘量（`liters_remaining`）、空水瓶（`water_ml:0`/`empty:true`，按 `WATER` 找貨不遺漏）、藥片待反應說明與批次損益。 |
| 經營人脈 | 喬的人脈網路首次列出服務，選中後只回該項詳情、報價與餘額，可直接決定是否購買。付款回執分列人情與現金。反饋箱首查預設精簡列出派系聲望、啟用效果與最近正負門檻（`verbose=True` 看全表）；續查只回差異，相同時回「無變動」。 |
| 歇業後安排 | `get_shop_status` 與 `sleep` 隔日回執提供跟隨遊戲語系的夜間報告與多種 `alerts`（通緝來客、垃圾警告、未付保險、傷勢癒合等）、店鋪吸引力明細、狀態摘要（含傷勢 `injury`）、店鋪容量與核廢料統計；可核對房租、垃圾與夜間服務費，或帶上各背包容量資訊選包外出拾荒並監控傷勢與阻塞原因。打烊、出門、睡覺各自操作。 |

開店收到的報紙、便條、情報紙條、聯絡人名片與夜間返店的腰包／背包會自動收進庫存；`sell_order`／`trade_batch`／`dismiss_customer` 也會先自動清空檯上非本單雜物（核廢料除外），`counter_unavailable`、`stale_order`、`dismiss_unavailable` 統一回報 `reason` 與 `blocking_items`。遇到必收核廢料對話時，`get_trade_context` 會標註 `forced_choice: true` 與後果說明，檯上核廢料擋住交易時會回報 `nuclear_waste_on_counter` 與處理指引。庫存滿時可在 `get_counter` 查到留在交易檯的文件編號。過夜後，`sleep` 等到當晚晨報就緒才回傳文字（逾時仍未完成時回 `awaiting_report` 而不沿用舊報）。

### 打烊前收後櫃

可以交代助手「等客人離開，把後櫃的自有貨一起收回庫存」。助手用 `move_items` 送出一批編號，一張回執列出各件的實際位置。若搬到一半來客，回執會標出停在哪件、哪些還沒動。後櫃批量收貨已通過回放，待實機驗收。

### 容器、文件與刮刮卡

- 同一個容器槽位可用 `transfer_slot_items` 批次存取，回執列出各件實際搬入的數量或停手原因。
- 閱讀自有報紙或地下交易所清單時，`inspect_item_window` 可讀出本期新聞或收購條件。
- 刮刮卡支援逐格 `reveal`（左／中／右）、一次刮完剩餘格的 `reveal_all` 與 `close` 關閉面板。第 5 欄已實測只刮中間，回執只報該格符號，左右兩格仍有覆層。
- 夜間出門（`shop_activity`）若立即開出選包介面會直接附上 `page` 與 `response_choices`；`travel_menu` 選卡／翻頁會附最新憑證，也可一步 `depart`；`explore` 搜索或搬貨後附最新槽位，必要時可帶 `abandon_ground=True` 放棄地面物直接返店。

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
2. 啟動遊戲，啟用「白龍店帳 · White Dragon Ledger」。要接手舊店就載入存檔，要配新角色就從主選單點「新遊戲」。升級模組後請重啟遊戲再讀檔。
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

第一次連線先呼叫 `get_scene()`。看到 `location=pawnshop` 和預期的 `slot` 後，用 `get_shop_status()` 查店況。SQLite 預設路徑是 `%LOCALAPPDATA%/WhiteDragonLedger/account.sqlite3`；服務參數 `--database` 可指定帳本，`--journal` 可指定遊戲觀察目錄。

## 操作與開發

助手接班、處理未完單和核對回執的方式見[助手操作指南](AGENTS.md)。按功能查實機結果與待驗場景，見[實機驗收表](docs/acceptance.md)。開發模組前，可先查[遊戲原生 API 索引](docs/native-api.md)。

沿用安裝步驟中的 PowerShell 變數，將測試產物放在倉庫外。

```powershell
$env:LEDGER_TEST_TEMP = "$work/tests"
New-Item -ItemType Directory -Force $env:LEDGER_TEST_TEMP | Out-Null
& $python -B -m unittest discover -s tests -p 'test_*.py'
./scripts/build.ps1 -GameDir $game -OutputDir "$work/build" -TestDesk
```

## 來源與授權

用 MCP 操作當鋪的構想受 Yideng 與貢獻者的 *Probably Stolen MCP Bridge* 啟發。白龍店帳依店務需求及本機遊戲介面獨立撰寫，建置時引用玩家已安裝的遊戲程序集。原始碼與文件採用 [MIT License](LICENSE)。
