# Governance — Documentation Change Rules

| | |
|---|---|
| **Purpose** | 定義文件什麼時候該改、誰改、改哪些 |
| **Audience** | 全體，包含 Claude Code |
| **Source of Truth** | 本檔 |
| **Related** | [DOCUMENT-MAP.md](../DOCUMENT-MAP.md)、[05-development/workflows.md](../05-development/workflows.md) |

---

## 0. 三條不可違反的原則

### 原則一：每個重要規則只有一個 Source of Truth

**不重複保存同一規則。** 需要在別處提到時，用連結，不用複製。

複製會製造第二個真相來源，然後兩邊會漂移，然後沒有人知道該信哪個。

### 原則二：不把 TBD 寫成既定事實

未決的事情一律標 `OPEN DECISION → OD-xx`。

**猜一個值填進去是最嚴重的違規**，因為猜的值會被下一個人當成規格。

### 原則三：不為了文件完整而創造需求

「GDD 裡有」不是「Prototype 要做」的理由。
「一般戰棋通常有」更不是。

---

## 1. Gameplay Rule 改變時

**觸發**：企劃改了規則，或裁決了一個 Open Decision。

必做：

- [ ] 更新對應的 **Specification**（`03-spec/`）—— 這是規則的 SoT
- [ ] 檢查 **Design**（`02-design/`）：玩家體驗變了嗎？
      變了 → 更新意圖描述（但**不要把規則抄進 Design**）
- [ ] 檢查 **Tests**：新增／更新測試；golden hash 是否需要更新
- [ ] 檢查相關 **Architecture / ADR**：
      這條規則推翻了某個 ADR 的前提嗎？
- [ ] 若來自裁決 → 更新 [OPEN-DECISIONS.md](../OPEN-DECISIONS.md) 或
      [CONFLICTS.md](../CONFLICTS.md) 的狀態
- [ ] 記進 [CHANGELOG.md](../CHANGELOG.md)

---

## 2. Architecture 改變時

必做：

- [ ] 更新 **Architecture**（`04-architecture/`）
- [ ] **建立或更新 ADR** —— 架構變更幾乎一定值得記錄理由
- [ ] 檢查受影響的 **Specifications**：
      架構變更改變了某條規格的可驗收方式嗎？
- [ ] 檢查 **Tests**：A1–A7 是否需要調整？
- [ ] 記進 [CHANGELOG.md](../CHANGELOG.md)

**被取代的 ADR 標 `Superseded by ADR-xxxx`，不刪除。**

---

## 3. Implementation 改變時

> **Implementation 改變不應自動修改 Design。**

| 情況 | 要不要改文件 |
|---|---|
| 重構、改演算法、改命名 | **不用**。行為沒變 |
| 效能最佳化 | **不用** |
| 修 bug（讓行為符合既有規格） | **不用**。規格本來就是對的 |
| 修 bug 後發現規格本身錯了 | **要**。走 §1 |
| **改變了既定 behaviour / contract** | **要**。更新 Specification / Architecture |

**判準**：問「外部觀察得到的行為變了嗎？」
變了 → 那不是純 implementation 改變。

---

## 4. 新增系統時

必須逐項確認（見 [workflows §1](../05-development/workflows.md)）：

- [ ] 是否有 Design requirement？**找不到 → 這個系統不該存在**
- [ ] 是否需要 Specification？（有可驗收行為 → 需要）
- [ ] 是否需要 Architecture？（跨模組邊界 → 需要）
- [ ] 是否需要 ADR？（看五個條件）
- [ ] 是否需要 Tests？（有 R-xxx → 一定需要）
- [ ] 更新 [DOCUMENT-MAP.md](../DOCUMENT-MAP.md) 的 Traceability

---

## 5. `00-source/` 的處理規則

| 規則 | 說明 |
|---|---|
| **不編輯** | `00-source/` 是封存區，唯讀 |
| 既有文件出新版 | 放進 `00-source/`，**保留舊版**，在 [CHANGELOG](../CHANGELOG.md) 記錄，重新檢查所有 CONFLICT |
| 裁決結果與 `00-source/` 不符 | **不改 `00-source/`**。在 [CONFLICTS.md](../CONFLICTS.md) 記錄「以裁決為準，該段已作廢」 |
| PDF 更新 | 必須重新產生 `.extracted.txt`（指令在 [00-source/README.md](../00-source/README.md)） |

---

## 6. 各層該寫什麼、不該寫什麼

| 層 | 回答 | **不該**出現 |
|---|---|---|
| **Design** | What are we building? | implementation detail、資料結構、演算法、精確數值 |
| **Specification** | What must be true? | 意圖說明、設計理由、「因為這樣比較好玩」 |
| **Architecture** | How are responsibilities divided? | 具體 implementation code |
| **ADR** | Why did we choose this? | 一般實作細節 |
| **Development** | How should engineers work? | 遊戲規則 |
| **Validation** | 怎麼證明規格被滿足 | 新的規則（測試驗證規格，不定義規格） |

### 分不清時的判斷順序

```
1. 這是「必須為真」的可驗收敘述嗎？   → Specification
2. 這是「玩家該有什麼體驗」嗎？        → Design
3. 這是「誰負責什麼」嗎？             → Architecture
4. 這是「為什麼不用另一個方案」嗎？     → ADR
5. 這是「工程師該怎麼操作」嗎？        → Development
6. 以上都不是                       → 它可能不需要被寫下來
```

---

## 7. 稽核節奏

| 時機 | 做什麼 |
|---|---|
| 每次裁決一個 Open Decision | 更新受影響的規格與測試（§1） |
| 每個 BLOCKER 清空時 | 重跑 [Phase 9 稽核清單](#8-稽核清單) 的相關項 |
| 進入實作階段前 | 完整重跑稽核清單 |
| 既有文件出新版 | 重新檢查所有 CONFLICT 是否仍成立 |

---

## 8. 稽核清單

完整版見最終報告。每次稽核至少檢查：

1. 是否存在重複規則？（同一條規則寫在兩個地方）
2. 是否存在互相矛盾的規格？
3. 是否有重要規則沒有 Source of Truth？
4. 是否有 Design 與 Technical Specification 混在一起？
5. 是否有 Architecture 沒有對應需求？（過度設計）
6. 是否有 Specification 無法被測試或驗收？
7. 是否有文件把尚未決定的事寫成既定事實？
8. 是否有不必要的文件？
9. 文件是否足以讓另一名程式工程師理解如何開始實作？
10. 文件是否足以讓企劃理解系統限制？
11. 是否能從需求追蹤到規格、架構與測試？
12. 是否存在 Circular Dependency？
13. 是否有文件依賴已過時的資訊？
14. **是否有歷史／全遊戲文件可能被誤認為目前規格？**

> 第 14 項在本專案風險特別高 —— GDD 描述的是一款 20 關、4 角色、
> 有污染系統與四魔將的完整遊戲，而 Prototype 只驗證一個假說。
> [DOCUMENT-MAP §1.3](../DOCUMENT-MAP.md#13-明確標記為不是目前規格的內容)
> 就是為此存在的。

---

## 9. Claude Code / AI 協作的額外規則

| 規則 | 為什麼 |
|---|---|
| 任何未在文件中的規則，一律視為缺口，**不得自行補完** | AI 很擅長「補上看起來合理的東西」，而那正是最危險的行為 |
| 遇到 `OPEN DECISION` / `CONFLICT` 標記 → **停下來問** | |
| 不因「一般戰棋通常有 X」而新增需求 | |
| 修改規格文件時必須同步檢查本檔 §1–4 的清單 | |
| 產生的程式碼中，暫時實作必須留 `OPEN DECISION → OD-xx` 標記 | 讓 `grep` 能找出所有卡住的地方 |
