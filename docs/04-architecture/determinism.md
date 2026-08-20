# Architecture — 確定性契約

| | |
|---|---|
| **Purpose** | 定義「同輸入 → 同輸出」必須怎麼保證 |
| **Audience** | 程式 |
| **Source of Truth** | 本檔（源自 SPEC v0.1 §6.6） |
| **Dependencies** | [simulation-core.md](simulation-core.md) |
| **Related** | [ADR-0003](../07-adr/ADR-0003-deterministic-rule-layer.md)、[06-validation/test-strategy.md](../06-validation/test-strategy.md) |

---

## 為什麼確定性是需求，不是「好的工程實踐」

[prototype-charter](../01-vision/prototype-charter.md) 定義的成功標準是：

> **Prototype 成功 = 我們能用數據回答五個問題。不是「好玩」，是「可測量」。**

「可測量」推導出「可自動化跑 1000 場」，而自動化跑分要有意義，
必須保證：**同一組輸入永遠得到同一個結果**。

否則跑出來的差異可能來自浮點誤差或列舉順序，而不是設計變更。
**那樣的數據沒有價值，Prototype 就失敗了。**

> 這就是為什麼確定性在本專案是**需求 R6**，而不是工程偏好。
> 見 [DOCUMENT-MAP §3.1](../DOCUMENT-MAP.md#31-可建立完整鏈條的需求)。

---

## 三戒

| 戒 | 內容 | 為什麼 |
|---|---|---|
| **一** | 規則層**只用整數**。命中用整數 RNG（0–99）比較整數化命中率 | float 跨平台／跨編譯設定不保證位元一致 |
| **二** | 任何影響模擬或雜湊的迭代，**不得依賴 `Dictionary` / `HashSet` 的列舉順序**。改用 `List` 或先排序 key | .NET 不保證 hashmap 列舉順序 |
| **三** | 世界狀態雜湊**絕不用**內建 `GetHashCode()`。序列化成 canonical bytes 後算 FNV-1a 或 SHA-256 | .NET Core 的 `string.GetHashCode()` 有 randomization，同字串跨 process 不同值 |

---

## 戒一的實際範圍

「只用整數」在本專案的具體落點：

| 項目 | 規則 |
|---|---|
| 傷害計算 | 整數運算，無條件捨去（[R-COMBAT-06](../03-spec/SPEC-combat.md)） |
| 命中率 | 資料中以整數儲存（[R-DATA-05](../03-spec/SPEC-unit-data.md)） |
| 移動成本 | 整數 |
| RNG | `IRandomSource.NextInt(int exclusiveMax)` —— **回傳整數，不用 float** |
| GDD 的小數（`HIT 0.80`） | 在**資料載入邊界**一次轉成整數；Core 內部只看到整數 |

> **表現層不受戒一約束。** 動畫插值、UI 位置當然可以用 float。
> 戒一只約束**會影響狀態或雜湊的計算**。

### 封包 2 的技術紅利

若 [OD-05](../OPEN-DECISIONS.md#od-05) 裁決為封包 2（攻擊必中），
**Core 根本不需要亂數**，戒一自動滿足，確定性測試變成純粹的
「同指令串 → 同雜湊」，不需要 seed 管理。

**但 `IRandomSource` 介面仍必須存在**（[R-COMBAT-11](../03-spec/SPEC-combat.md)），
因為封包 1 是要跑的變體。

---

## 戒二的常見陷阱

不是只有「遍歷 Dictionary」會中招。以下都算：

| 陷阱 | 說明 |
|---|---|
| `foreach (var u in unitsByID)` | 直接中招 |
| `HashSet<Coord>` 當作「已訪問集合」後再遍歷它 | 只做 `Contains` 沒問題；**遍歷就有問題** |
| Dijkstra 的優先佇列 tie-break | 相同成本的格子誰先展開？必須明確定義（[R-MOVE-11](../03-spec/SPEC-movement.md)） |
| LINQ 的 `GroupBy` / `ToDictionary` 後遍歷 | 同上 |
| `IGridTopology.Neighbors` 的回傳順序 | 必須固定（[R-GRID-02](../03-spec/SPEC-grid-terrain.md)，架構測試 A6） |

**判斷準則**：問「這個順序會不會影響最終 state 或 log？」
會 → 必須確定。不會（例如純粹的統計加總）→ 可以放寬，但建議一律確定，
因為「不會影響」這個判斷本身很容易出錯。

---

## 戒三：狀態雜湊

**做法**：canonical `BinaryWriter` → FNV-1a。

### 什麼該進雜湊

| 進 | 不進 |
|---|---|
| 單位位置、HP、AP、存活狀態 | 選取中的單位（UI 狀態） |
| 回合數、當前陣營 | 動畫播放進度 |
| 陣營啟動狀態 | 滑鼠位置 |
| 防禦等暫時效果的剩餘時間 | 攝影機位置 |
| RNG 的當前狀態（若使用） | 任何 `Time.time` |

> **把 UI 狀態放進 `BattleState` 是最常見的破壞方式。**
> 一旦進去了，A4 會開始隨機失敗，而且原因極難定位。
> 見 [overview.md §4 Data Ownership](overview.md)。

### Canonical 的三個要求

1. **集合先排序再序列化**（依穩定 key，例如 unitId）
2. **欄位順序固定**
3. **不寫入與邏輯狀態無關的東西**（時間戳、物件參考、暫存欄位）

---

## 驗收

確定性由架構回歸測試守住，見
[06-validation/test-strategy.md](../06-validation/test-strategy.md)：

| 測試 | 斷言 |
|---|---|
| **A2** | `Execute` 呼叫前後，傳入的 state 物件雜湊不變（純函式） |
| **A3** | `Clone()` 後改動副本，原本不受影響（含所有巢狀集合） |
| **A4** | 同 seed + 同指令串 → 同世界狀態雜湊（**golden hash，常數比對**） |
| **A6** | `IGridTopology.Neighbors` 的回傳順序在同輸入下固定 |

> **A4 用「golden hash 常數比對」而不是「跑兩次比較」。**
> 跑兩次只能抓到同一個 process 內的不確定性；
> 常數比對能抓到跨 process、跨平台、跨編譯設定的漂移 —— 那才是真正的風險。
>
> **代價**：規則或資料合法變更時 golden hash 會失敗，必須手動更新。
> 這是刻意的：**它強迫你注意到「這次改動改變了模擬結果」**，
> 而那正是我們想要的信號。更新流程見
> [05-development/workflows.md](../05-development/workflows.md)。
