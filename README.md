# 白龍店帳 · White Dragon Ledger

**0.30.0 · 適用遊戲版本：0.46D · Windows · MIT License**

白龍店帳是一套為《深空當鋪》（Probably Stolen）打造的本機 Mod + stdio MCP 橋接工具，讓 **AI 智能體直接作為店主／玩家遊玩整款遊戲**。你不必一步步代操作，可以坐在旁邊看畫面、一起討論吐槽，只在需要「人眼看圖」或「手動點 UI」時搭把手。

---

## 核心亮點

- **本機 SQLite 帳本：一次建置基準，多次增量查詢**
  讀檔時由 Mod 盤點一次庫存並同步進本機 SQLite（預設位於 `%LOCALAPPDATA%/WhiteDragonLedger/account.sqlite3`），後續透過 `cursor` 追蹤增量變動（`shop_update`），純讀取子模式不重複附帶庫存快照。交易票據、歷史異動、垃圾桶與已通知的加熱紀錄皆持久化在 SQLite，大幅降低上下文負擔與 token 消耗，斷線重連也能安全續接。
- **不依賴 Computer Use 與圖像識別，換取高快取命中率**
  雖然現在多數智能體都已具備圖像識別能力，但頻繁截圖與圖片輸入會迅速消耗上下文、顯著拉低 Prompt Cache（提示詞快取）命中率。白龍店帳刻意走**純結構化資料路線**——對智能體來說，就像攤開一本條理清楚的對摺帳本與店務表格在玩遊戲，能穩定長程連跑十幾天。
- **完整店務覆蓋（78 個 MCP 工具）**
  涵蓋開新局（專精／特性／難度）、接客與自動等候門鈴冷卻、議價分組預覽、單件／整批／混合買賣、走私暗格直賣直搬、海報隱藏格開關、批次裝水淨化、化學蒸發後段自動化與純度把關（`chem_finish`）、背景加熱控溫（`control_heat`）、電話人脈與派系聲望、過夜帳單與夜間外出拾荒。
- **版本防呆與防重複交易**
  編譯 DLL 時自動嵌入 git commit，MCP 啟動時與本機倉庫比對，不一致即在 `get_scene` 附 `version_mismatch` 警告（不阻擋操作）；寫入型交易具確定性 `command_id` 與重播保護，避免網路抖動重複下單。

---

## 使用前須知與目前限制

- **智能體看不到遊戲畫面（歡迎 Fork 二次開發）**
  為了維持高快取率與低上下文成本，本工具**不截圖、不傳畫面**。所有依賴視覺判斷的情境都需要旁邊看畫面的人協助：
  - 僅姓名相符時的通緝照比對（`wanted_name_match`）
  - 改造體外觀鑑別與開槍自衛（異常瞳孔、詭異表情、外露晶片等）
  - 可疑包裝驗偽、未鑑定針劑、純圖片圖鑑或需看外觀估價的物品（`player_visual_check`、`image_only_guide`）
  - 少數未接入工具的 UI 操作（難度頁按「開始遊戲」、關閉手動開出的面板）
  > 如果你覺得「看不到畫面」的限制較大，或想結合截圖／Computer Use，歡迎直接 **Fork 本倉庫進行二次開發**。
- **不同智能體的能力與專注度差異**
  實測上不同模型的表現差異明顯：有的非常專注嚴謹，有的則比較發散隨性、幻覺較高，容易「想當然」忽略限制（例如忘記先過濾就蒸發、沒看暗格外違禁品就叫治安部）。
  **強烈建議遊玩時載入 [AGENTS.md](AGENTS.md)**，或將其封裝為 Skill／寫入系統提示詞，明確約束工具用法與檢查習慣，能顯著降低失誤與腦補。
- **尚未實作的系統**
  櫃檯自衛開槍工具目前處於研究階段（見 [docs/augs-research.md](docs/augs-research.md)），尚未開放給 MCP 操作。

---

## 安裝與配置

### 環境需求

- Windows 10 / 11
- 已安裝《深空當鋪》（Probably Stolen，適用版本 **0.46D**）
- [.NET SDK](https://dotnet.microsoft.com/)（編譯 Mod DLL 用）
- Python **3.11 以上**
- 支援本機 **stdio MCP** 的智能體宿主（如 Claude Code、Codex、Antigravity 、Deepseek Harness 等）

### 方式一：直接交給編程智能體幫你安裝（推薦）

如果你手邊已有具終端權限的編程智能體，可以直接把倉庫交給它下載、編譯、建環境並寫入設定，例如：

> 請幫我 clone `https://github.com/Mirasupiritto/WhiteDragonLedger.git`，照 `README.md` 的步驟把編譯產物和 Python venv 建在倉庫外的 `WhiteDragonLedger-products` 資料夾。我的遊戲安裝路徑是 `E:/SteamLibrary/steamapps/common/Probably Stolen`，建完後把 `white_dragon_ledger` 複製到遊戲的 `Mods` 資料夾，並幫我配好 stdio MCP 服務 `white-dragon-ledger`。

### 方式二：手動安裝與配置

在倉庫根目錄開啟 PowerShell，將 `$game` 改為自己的 Steam 遊戲路徑（編譯產物與 Python 虛擬環境會放在倉庫外的 `WhiteDragonLedger-products`，避免污染倉庫）：

```powershell
$game = 'E:/SteamLibrary/steamapps/common/Probably Stolen'
$work = Join-Path (Split-Path -Parent (Get-Location).Path) 'WhiteDragonLedger-products'
./scripts/build.ps1 -GameDir $game -OutputDir "$work/build"
python -m venv "$work/venv"
$python = "$work/venv/Scripts/python.exe"
& $python -m pip --cache-dir "$work/pip-cache" install -r requirements.txt
```

1. **安裝 Mod**：將 `$work/build/white_dragon_ledger` 資料夾複製到 `<遊戲目錄>/Mods/` 下，確認 `Mods/white_dragon_ledger/` 內直接包含 `manifest.xml` 與 `WhiteDragonLedger.dll`。
2. **啟用 Mod**：啟動遊戲，在模組選單啟用「白龍店帳 · White Dragon Ledger」並授權。升級 DLL 後需重啟遊戲再讀檔。
3. **設定 stdio MCP**：在你的 MCP 宿主設定檔加入以下項目（替換成實際的絕對路徑）：

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

> 可選參數：`--database <路徑>` 指定 SQLite 帳本位置；`--journal <路徑>` 指定遊戲 `ModData/white_dragon_ledger` 目錄。

4. **驗證連線**：載入存檔（或停在新遊戲專精頁）後，讓智能體呼叫 `get_scene()`；確認 `location=pawnshop`、無 `version_mismatch` 後，再呼叫 `get_shop_updates()` 建立基準與 `get_shop_status()` 查看店況即可開玩。

---

## 開始讓智能體玩

- **接手既有存檔**：載入存檔後直接交給智能體即可，例如：「這局交給你經營，先看看店況跟手頭庫存，今天打算怎麼開局？」
- **從頭開新局**：在主選單點「新遊戲」停在專精頁，讓智能體自己看 [專精](docs/specializations.md)、[特性](docs/starting-perks.md)、[難度](docs/difficulties.md) 挑路線；選完難度卡後由你按「開始遊戲」進店。
- **一起討論或設節奏**：平時可以讓它自由營業、加工、拾荒，也可以隨時插話吐槽，或交代「這客送完先停一下」「今天先別睡，我們討論一下要不要買這批貨」。

---

## 文件導覽

- [AGENTS.md](AGENTS.md)：給智能體讀的完整操作手冊（遊玩定位、工具規則與目視求助邊界）
- [skills/probably-stolen/SKILL.md](skills/probably-stolen/SKILL.md)：內附的《深空當鋪》遊戲攻略 Skill 與 Wiki 參考資料（職業特性、議價定價、機台合成、違禁品與事件等）
- [docs/specializations.md](docs/specializations.md) · [docs/starting-perks.md](docs/starting-perks.md) · [docs/difficulties.md](docs/difficulties.md)：開局專精、特性與難度參考
- [docs/augs-research.md](docs/augs-research.md)：改造體危機與櫃檯自衛原生機制研究
- [docs/native-api.md](docs/native-api.md) · [docs/acceptance.md](docs/acceptance.md)：開發用原生 API 索引與實機驗收表

---

## 來源與授權

用 MCP 操作當鋪的構想受 Yideng 與貢獻者的 *Probably Stolen MCP Bridge* 啟發。白龍店帳依店務需求與本機遊戲介面獨立撰寫，建置時引用玩家已安裝的遊戲程序集。原始碼與文件採用 [MIT License](LICENSE)。
