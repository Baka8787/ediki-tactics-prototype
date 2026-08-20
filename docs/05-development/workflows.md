# Development — Workflows

| | |
|---|---|
| **Purpose** | 常見工作的標準流程：新增系統、新增單位、改規則、改資料、更新規格 |
| **Audience** | 程式、企劃、Claude Code |
| **Source of Truth** | 本檔 |
| **Dependencies** | [definition-of-done.md](definition-of-done.md)、[99-governance/documentation-rules.md](../99-governance/documentation-rules.md) |

---

## 0. 開工前的檢查（每次都做）

```
1. 我要碰的東西，在 OPEN-DECISIONS.md 有沒有 BLOCKER？
       有 → 停。去問。不要猜。
2. 在 CONFLICTS.md 有沒有相關衝突？
       有 → 停。不要自行選一個答案。
3. 我要做的東西，在 prototype-charter.md §4 的非目標清單裡嗎？
       在 → 停。預設答案是「不做」。
4. 我要實作的規則，在 03-spec/ 找得到對應的 R-xxx 嗎？
       找不到 → 那是缺口，登錄到 OPEN-DECISIONS.md，不要自己補。
```

**這四步不是形式。** 本專案有 7 個 BLOCKER 與 8 個 CONFLICT，
跳過檢查而寫出的程式碼很可能整段作廢。

---

## 1. 新增一個 Gameplay System

```
1. Design requirement 存在嗎？
       ← 02-design/ 或 00-source/ 找得到需求嗎？
       找不到 → 這個系統不該存在。停。

2. 需要 Specification 嗎？
       系統有可驗收的行為 → 需要。寫進 03-spec/，給 R-xxx ID。
       純內部技術結構 → 不需要，寫進 04-architecture/。

3. 需要 Architecture 嗎？
       跨越模組邊界、改變依賴方向、新增 assembly → 需要。
       只在既有模組內 → 不需要。

4. 需要 ADR 嗎？
       看 07-adr/README.md 的五個條件。
       多數情況：不需要。

5. 需要 Tests 嗎？
       有 R-xxx → 一定需要。每條 R-xxx 至少一個測試。
       沒有 R-xxx → 回到第 2 步，你可能漏了規格。

6. 更新 DOCUMENT-MAP.md 的 Traceability 表。
```

---

## 2. 新增一個 Unit

Stage 01 只有桃太郎與小耗，但流程要固定：

```
1. 單位數值來自 GDD → 對照 03-spec/SPEC-unit-data.md 的 schema
2. GDD 缺欄位（例如小耗沒有 DEF/HIT/EVA）→ 登錄 CONFLICT 或 OD，不要自己填
3. 建資料檔（格式見 OD-11），不寫程式
4. 若新單位需要新的行為 → 那不是「新增單位」，是「新增系統」，走 §1
5. 更新 SPEC-unit-data.md 的名單表
```

> **新增單位不應該需要寫程式。** 如果需要，代表資料驅動沒做好（違反 A5）。

---

## 3. 新增或修改一條 Battle Rule

```
1. 這條規則有來源嗎？（GDD / SPEC v0.1 / 企劃的裁決）
       沒有 → 停。它是缺口，不是規則。

2. 更新 03-spec/ 的對應 SPEC 檔：
       - 新增 R-xxx（ID 不重用）
       - 填 Statement / Source / Status / Acceptance
       - Acceptance 寫不出來 → 規格不夠精確，回去補

3. 檢查 02-design/：這條規則改變了玩家體驗嗎？
       有 → 更新 Design（但 Design 描述意圖，不抄規則）

4. 檢查 06-validation/：
       - 新增／更新測試
       - 這條規則會改變模擬結果嗎？→ golden hash 要更新（見 §6）

5. 檢查 04-architecture/ 與 07-adr/：
       - 這條規則需要新的模組邊界或介面嗎？
       - 它推翻了某個 ADR 的前提嗎？（例如推翻傷害公式會影響 OD-06 的限制）

6. 記進 CHANGELOG.md
```

---

## 4. 新增或修改資料（ScriptableObject / JSON）

```
1. 只改值 → 不需要改任何文件，但：
       - 若是數值平衡調整 → 附 TTK 表（R-COMBAT-08）
       - golden hash 會失敗 → 更新（見 §6）

2. 改 schema（新增欄位）→ 更新 03-spec/SPEC-unit-data.md 的 schema 表

3. 新增資料檔類型 → 檢查 OD-11 的格式決定是否還適用
```

---

## 5. 裁決一個 Open Decision（企劃）

**這是本專案目前最重要的流程 —— 有 7 個 BLOCKER 在等。**

```
1. 在 OPEN-DECISIONS.md 找到 OD-xx，讀完所有選項與影響範圍
2. 做決定。把決定寫下來，含理由。
3. 把結論寫進對應的 03-spec/ SPEC 檔：
       - 把 Status 從 `OPEN → OD-xx` 改成 `STABLE`
       - 補上具體內容與 Acceptance
4. 回到 OPEN-DECISIONS.md：
       - 狀態改 `DECIDED`
       - 填裁決者與日期
       - **不要刪除條目**
5. 這個決定符合 ADR 的五個條件嗎？
       符合 → 建立 ADR（見 07-adr/README.md 的建議清單）
6. 檢查有沒有其他 OD / CONFLICT 依賴這一項
       （例如 OD-06 依賴 CONFLICT-07；OD-08 依賴 OD-05）
7. 記進 CHANGELOG.md
```

## 5b. 解決一個 Conflict

流程同上，但多一步：

```
0. 確認你是有權裁決的人（CONFLICTS.md 每條都寫了「需要誰裁決」）
...
4. CONFLICTS.md 狀態改 RESOLVED，**保留原本雙方的說法**
   —— 未來需要知道規格為什麼長這樣
5. 若裁決結果與某份 00-source/ 文件不符：
   **不要修改 00-source/**。那是封存的既有文件。
   在 CONFLICTS.md 記錄「以裁決為準，00-source/XXX 該段已作廢」
```

---

## 6. 更新 golden hash

架構測試 **A4** 用常數比對狀態雜湊。合法變更會讓它失敗 —— **這是刻意的**。

```
1. A4 失敗時，先問：這次改動應該改變模擬結果嗎？
       不應該 → 你引入了 bug 或不確定性。**不要更新 hash，去修 bug。**
       應該   → 繼續

2. 確認變更是預期的（規則改了？資料改了？）
3. 更新常數
4. 在 commit / CHANGELOG 說明為什麼結果變了
```

> **「A4 壞了就更新常數」是最危險的習慣。**
> 它會讓這條測試從「確定性守門員」退化成「橡皮圖章」。
> 每次更新都必須先回答第 1 步。

---

## 7. 何時建立 ADR

見 [07-adr/README.md](../07-adr/README.md)。五個條件：
影響整體架構、跨多個系統、難以逆轉、存在明顯 trade-off、未來工程師需要知道理由。

**快速判斷**：新工程師看到程式碼會問「為什麼不用更簡單的 X？」
而答案不顯而易見 → 值得一個 ADR。

---

## 8. Git

`TBD` —— 專案目前**不是 git repository**，也沒有既有的版本控制規則。

在專案負責人決定版本控制策略之前：
- **不進行任何 git 操作**
- 這一節保持 TBD

> 這不是遺漏，是尊重「不進行 Git 操作，除非專案既有規則明確允許」的指示。
