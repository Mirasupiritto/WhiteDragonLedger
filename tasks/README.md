# 白龍店帳 0.28 工作包

來源：[MCP 審閱 0.27](../白龍店帳-MCP審閱-0.27.md)。每個包都能獨立開發，改動的檔案互不重疊（共用的登記檔見下方）。完成後由大小姐（Claude）審閱，再合併。

## 建議分工

| 包 | 內容 | 建議負責 | 分支名 |
|---|---|---|---|
| [A](A-液體與水判定.md) | 混合液體不再算成水、過濾預覽 | Gemini4 Hydrogen Medium | `task/a-liquid` |
| [B](B-議價與以物易物.md) | 議價工具、以物易物的客人 | Codex 6.2 Astra High | `task/b-bargain` |
| [C](C-風險與劇情資訊.md) | 證據等級、通緝名單、存檔資訊、劇情分支提示 | 第二輪（誰先做完誰接） | `task/c-risk` |
| [D](D-化學家流程.md) | 成型機按名稱選配方、蒸發器狀態、純度估價、`chem_finish` | 大白龍 | `task/d-chem` |
| [E](E-回執精簡與錯誤原因.md) | 回執減重、舊 token 與錯誤附原因 | 第二輪 | `task/e-receipts` |

C 和 E 都會動到 `mod/Counter.cs`，不要同時做這兩包。

## 共同規則

1. **起點**：從 `chem-workflow` 分支（`99b3807`）開自己的分支，不要直接改 `main` 或 `chem-workflow`。
2. **只改自己包裡列出的檔案**。需要碰清單外的檔案時，先在回報裡說明原因。
3. **共用登記檔**：`scripts/build.ps1` 的 `$testSources` 清單，以及 `tests/DeskReplay.cs` 的 `Run()` 呼叫清單。新增測試檔時各加一行就好，不要重排或改動別人的行，合併衝突由審閱者處理。
4. **查遊戲原生 API**：用 `white-dragon-native-api` MCP（見 [docs/native-api.md](../docs/native-api.md)）。本包說明裡的原生名稱都已查證存在，但呼叫方式和效果要自己用 `find_native_references` 確認。
5. **建置與測試**（PowerShell 7，遊戲要先關掉才能換 DLL）：
   ```powershell
   ./scripts/build.ps1 -GameDir 'E:/SteamLibrary/steamapps/common/Probably Stolen' -OutputDir '<repo 外的資料夾>/build' -TestDesk
   & 'D:/tools/WhiteDragonLedger-products/venv/Scripts/python.exe' -B -m unittest discover -s tests -p 'test_*.py'
   ```
   `-TestDesk` 會編譯並執行所有 C# 回放測試，最後要看到全部 `... replay passed`。
6. **回放測試**：每個新行為至少一條 `Expect(...)`，寫在對應的 `tests/*Replay.cs`。測試只用 stub，不需要遊戲。
7. **回執風格**：沿用現有寫法，`status` 用 snake_case 英文短語，失敗時附 `reason`，給 AI 的提示用 `next_tool`／`next_action`。不要新增會出現在每個回執裡的大欄位。
8. **工具說明**：改了 MCP 工具的行為，要同步更新 `mcp/*.py` 裡的 docstring；玩家可見的流程有變，要同步更新 `AGENTS.md`（繁體中文）。
9. **Commit**：訊息用繁體中文，第一行一句話講清楚改了什麼，下面條列細節。一個包可以分成多個 commit。
10. **不要做的事**：不要推到遠端、不要改版本號、不要碰 `tasks/` 以外別人的工作包說明。

## 回報格式（完成後貼給大小姐）

- 分支名與 commit 清單
- 改了哪些檔案、為什麼
- 新增的回放測試名稱，以及 `-TestDesk` 的最後輸出
- 沒做完或沒驗證的部分（特別是需要進遊戲才能確認的）
- 對原生 API 的發現或疑問
