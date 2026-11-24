# 白龍店帳 · White Dragon Ledger

白龍店帳讓你和 AI 助手一起經營《深空當鋪》（Probably Stolen）。你可以請它接一位客人、替一批貨設買入上限，也可以把整天的店務交給它。助手透過本機 MCP 讀取遊戲提供的台詞、交易平板和庫存。遇到通緝照、可疑包裝或需要看外觀鑑定的物品，再由你幫忙認。

**0.30.0 · Windows · 適用遊戲版本 0.46D · MIT**

助手支援本機 stdio MCP 即可使用，Computer Use 為選配。

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

## 第一次讓助手接手

**已經有店**，先載入想玩的存檔，再告訴助手停在哪一步。例如：

> 先不開門。看一下我在哪個欄位、手上多少現金、下次哪天交租，再找找昨晚帶回來的零件。

讀檔時模組會盤點一次，把庫存記進本機 SQLite 帳本。之後搬貨、增減數量會更新帳本。換一個助手接班時，可以讓它先核對存檔和未完交易。

**想開新店**，在主選單點「新遊戲」，停在專精頁就能交給助手。請它比較起始設備與玩法、挑特性、說說為什麼選這個難度。到難度頁選好後，由你按「開始遊戲」。想先自己比較，可以看[專精](docs/specializations.md)、[特性](docs/starting-perks.md)和[難度](docs/difficulties.md)。

## 想讓助手做什麼？

| 你可以這樣說 | 助手會用到的資訊 |
|---|---|
| 「等商人說完，問濾水器實際賣多少。最多花 25。」 | 貨單估值、上櫃後的實際報價、你的買入上限，以及成交後實付多少。 |
| 「按平板配貨。這位想要種類多樣，每種只拿一件。」 | 平板列出的需求、限制和剩餘預算，以及這位客人已買過的物品種類。 |
| 「找找我自己的槍械零件，連腰包裡的也算。告訴我去哪拿。」 | 物品編號、數量、整個包所在的貨區、從外到內的包名和格位。若貨在展示櫃的腰包裡，助手會先辨認包和格位，要出貨時再取出。 |
| 「幫我看加熱板上的容器，維持 45 到 50 °C 一分鐘，這段時間繼續接客。」 | 容器、溫度與機台等級。啟動後由模組控溫，到時關火；固定火力機型可能短暫超過設定溫區。 |
| 「打電話給喬，看人情和現金各要付多少，選好先別買。」 | 人脈網路的服務、所選項目的價格和餘額。歇業時可以打電話。 |
| 「打烊後帶腰包去拾荒，裝好找到的東西就回店，先別睡。」 | 背包容量、目的地、當地可搜的物品及返店位置。返店後可以接著查包裡的貨。 |

## 店裡常用的操作

- **收貨與整理**可以一次把空櫃檯後方的多件自有貨收回庫存，也能對同一容器批次存取。主庫存有空格卻放不下大件時，請助手整理一次，再看最大的連續空位。「MCP 主庫存整理」預設為 `auto`，偵測到支援的整理模組時會讓該模組接手。
- **文件與物品**可以請助手讀報紙、採購單、便條或多頁手冊。只有圖的頁面由你看。刮刮卡可以指定只刮左、中、右其中一格，助手只報揭開的符號。
- **機台與耗材**可以先請助手查看當前機台的槽位、按鈕與讀值，再決定是否操作。例如查淨水器裡是哪一瓶和剩多少電、核對全店試紙數量，或先看成型機能產出什麼。
- **睡前與隔天早晨**可以請助手核對房租、垃圾與夜間服務費，再決定是否睡覺。隔天可讀夜報，查看傷勢、警告和昨晚的費用。

想知道助手怎麼對單、查包、操作機台或從中斷處接手，可看[助手操作指南](AGENTS.md)。開發用的資料另見[原生 API 索引](docs/native-api.md)與[驗收表](docs/acceptance.md)。

## 來源與授權

用 MCP 操作當鋪的構想受 Yideng 與貢獻者的 *Probably Stolen MCP Bridge* 啟發。白龍店帳依店務需求及本機遊戲介面獨立撰寫，建置時引用玩家已安裝的遊戲程序集。原始碼與文件採用 [MIT License](LICENSE)。
