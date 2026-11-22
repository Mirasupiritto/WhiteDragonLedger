# 白龍店帳 · White Dragon Ledger

白龍店帳讓 AI 助手透過本機 MCP 經營《深空當鋪》（Probably Stolen）。它能聽完客人的需求、查庫存、談價買賣，也能幫你決定今晚要留多少錢交租。玩家可以只交代一件事，例如「這批水最多花 90，買完告訴我實付多少」，也可以讓助手經營一天。

**0.18.0 · Windows · 遊戲版本 0.46D · MIT**。支援本機 stdio MCP 的助手就能使用，Computer Use 為選配。遊戲提供台詞、交易條件、物品位置和成交金額；通緝照比對、包裝驗偽與目視鑑定由玩家協助。

## 第一次讓助手看店

> 看一下我在哪個存檔、今天是第幾天、離收租還有幾天。再看看淨水器裡是哪瓶、剩幾張試紙。先不開店。

助手先確認場景與欄位，再讀店況與機台。模組在讀檔時盤點一次，之後記錄貨物移動和數量變化。批次搬貨只需一張回執。SQLite 帳本保留庫存與未完交易，換會話也能按存檔欄位接手。

| 想讓助手做的事 | 可以看到的結果 |
|---|---|
| 配新角色 | 玩家從主選單點「新遊戲」，助手比較專精、分配特性點、選難度。選好停在難度頁，由玩家按「開始遊戲」。見[專精](docs/specializations.md)、[特性](docs/starting-perks.md)、[難度](docs/difficulties.md)比較表。 |
| 接客與交易 | 讀交易平板的需求、限制和預算，按瓶貼或分類找貨。可設整批買入上限、逐件售價底線，或比較拾荒客的三個交換方案。成交回執列出實收或實付。 |
| 整貨與查帳 | 空櫃檯可將多件自有貨從後櫃一次收回庫存。也可批次搬貨到展示櫃，查機台槽位、電力或瓶貼。按批次查進貨成本、已收回金額與剩貨。 |
| 經營人脈 | 查電話聯絡人與可辦業務。反饋箱首查列出派系聲望、效果與門檻；續查只回差異，相同時回「無變動」。 |
| 歇業後安排 | 核對房租、垃圾與夜間服務費，或選包、外出拾荒並把物品帶回店裡。打烊、出門、睡覺各自操作。 |

### 打烊前收後櫃

可以交代助手「等客人離開，把後櫃的自有貨一起收回庫存」。助手用 `move_items` 送出一批編號，一張回執列出各件的實際位置。若搬到一半來客，回執會標出停在哪件、哪些還沒動。後櫃批量收貨已通過回放，待實機驗收。

### 容器、文件與刮刮卡

- 同一個容器槽位可用 `transfer_slot_items` 批次存取，回執列出各件實際搬入的數量或停手原因。
- 閱讀自有報紙或地下交易所清單時，`inspect_item_window` 可讀出本期新聞或收購條件。
- 刮刮卡可指定左、中、右其中一格。第 5 欄已實測只刮中間，回執只報該格符號，左右兩格仍有覆層。
- 夜間搜索或搬貨後，`explore` 會附上最新槽位與操作憑證，可直接接下一步。

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
