# Development — Coding Guidelines

| | |
|---|---|
| **Purpose** | 這個專案寫程式的硬性規則 |
| **Audience** | 程式、Claude Code |
| **Source of Truth** | 本檔 |
| **Dependencies** | [04-architecture/](../04-architecture/) |
| **Related** | [definition-of-done.md](definition-of-done.md)、[workflows.md](workflows.md) |

> 本檔只寫**這個專案特有**的規則。一般的 C# 風格（命名、括號位置）
> 交給 IDE 與 `.editorconfig`，不在這裡重述。

---

## 1. 硬性規則（違反 = 編譯失敗或測試失敗）

| # | 規則 | 守門機制 |
|---|---|---|
| 1 | `Ediki.Core` 不得引用 `UnityEngine` / `UnityEditor` | asmdef **No Engine References**（編譯期）＋ 測試 **A1** |
| 2 | 規則層不得出現 AP 成本、地形成本、單位數值的**字面值** | 測試 **A5** |
| 3 | 規則層不得使用 `float` / `double` | 測試 **A5** 的延伸 ＋ code review |
| 4 | 規則層不得直接呼叫 `System.Random` / `UnityEngine.Random` | 只能透過 `IRandomSource` |
| 5 | 狀態雜湊不得使用 `GetHashCode()` | 測試 **A4** |
| 6 | 影響模擬或雜湊的迭代不得依賴 `Dictionary` / `HashSet` 的列舉順序 | code review ＋ 測試 **A4** |
| 7 | 所有狀態變更必須走 `BattleSimulator.Execute` | 沒有其他公開的修改路徑 |
| 8 | 表現層不得反向引用 Core 的可變狀態 | 測試 **A7** |

理由全部在 [04-architecture/determinism.md](../04-architecture/determinism.md) 與
[07-adr/](../07-adr/)。**不要因為「這次只是暫時的」而繞過任何一條。**

---

## 2. Core 層的替代品對照

因為 Core 不能用 UnityEngine：

| 不能用 | 用什麼 |
|---|---|
| `Vector2Int` / `Vector3` | 自訂 `Coord`（[R-GRID-03](../03-spec/SPEC-grid-terrain.md)） |
| `Debug.Log` | 自訂的除錯抽象，或直接不 log（規則層應該用回傳值溝通，不用 log） |
| `ScriptableObject` | 純資料型別（見 [OD-11](../OPEN-DECISIONS.md#od-11)） |
| `UnityEngine.Random` | `IRandomSource` |
| `Mathf` | `System.Math` 的整數版本 |
| `Time.*` | 規則層沒有時間概念。**如果你需要它，代表設計錯了** |

---

## 3. 語言版本

🔴 **這一節目前不可信，需要查證。**

SPEC v0.1 §6.1 記載 Unity 6.0 / 6.3 為 **C# 9.0**，並列出不可用特性：

> `record`（需手動宣告 `IsExternalInit`，且 Unity 序列化不支援）、
> `init` setter、`required` members、file-scoped namespace、
> collection expressions、global using

**但專案實際使用 Unity 6000.5.1f1**，不在該版本範圍內 →
[CONFLICT-06](../CONFLICTS.md#conflict-06)。

### 查證方式

建一個暫時檔測試，看是否編譯通過：

```csharp
// 暫時檔，查證後刪除
namespace Ediki.Temp;              // file-scoped namespace
public record Probe(int X, int Y); // record
public class C { public required int V { get; init; } } // required + init
```

查證完成後，**回來更新本節並在 [CHANGELOG](../CHANGELOG.md) 記錄**，
同時更新 [CONFLICT-06](../CONFLICTS.md#conflict-06) 的狀態。

### 無論語言版本如何都成立的規則

| 規則 | 為什麼 |
|---|---|
| **`BattleState` 不得使用 `record` + `with` 做複製** | `with` 是**淺複製**，巢狀 `List<Unit>` 會被兩個 state 共用。與語言版本無關 → [ADR-0004](../07-adr/ADR-0004-hand-written-clone.md) |
| 不得使用 `JsonUtility` | 不支援 Dictionary、不支援多型／介面。與語言版本無關 |

---

## 4. 命名與 ID 慣例

| 東西 | 慣例 | 例 |
|---|---|---|
| Command | `<Verb>Command` | `MoveCommand` |
| Effect | 過去式 / 已發生 | `UnitMoved`、`ApSpent`、`AttackResolved` |
| 介面 | `I` 前綴 | `IGridTopology` |
| 測試方法 | `<被測對象>_<情境>_<預期>` | `Execute_InsufficientAp_RejectsAndLeavesStateUnchanged` |

**Effect 命名用過去式是刻意的** —— 它提醒你 Effect 描述「發生了什麼」，
不是「要做什麼」。命令式的 Effect 名稱（`MoveUnit`）會誘導人把判斷邏輯寫進表現層。

---

## 5. 註解

| 該註解 | 不該註解 |
|---|---|
| **為什麼**這樣寫（尤其是繞過直覺做法時） | 程式碼已經說清楚的**做什麼** |
| 確定性相關的約束（「這裡必須排序，否則 A4 會壞」） | 逐行翻譯 |
| 指向 Open Decision 的暫時實作 | |

暫時實作必須留可搜尋的標記：

```csharp
// OPEN DECISION → OD-06：防禦效果未定義。此處僅消耗 AP，不套用任何效果。
// docs/OPEN-DECISIONS.md#od-06
```

**格式固定為 `OPEN DECISION → OD-xx`**，讓 `grep "OPEN DECISION"` 能找出所有卡住的地方。

同理：`CONFLICT → CONFLICT-xx`、`ODD-xx`。

---

## 6. 不要做的事

| 不要 | 為什麼 |
|---|---|
| 為 GDD 裡有但 Prototype 不做的系統預留擴充點 | 見 [extension-points §6](../04-architecture/extension-points.md)。**「GDD 裡有」不是預留的理由** |
| 實作第二個 `IGridTopology`（六角格） | [R-GRID-01](../03-spec/SPEC-grid-terrain.md) 明確禁止 |
| 「先寫個簡單版本之後再改」Exposure 計算 | 它是主假說的量測工具，算錯會讓整個 Prototype 的數據無效 |
| 把選取狀態、動畫進度放進 `BattleState` | 會污染狀態雜湊，讓 A4 隨機失敗 |
| 自行決定任何 [OPEN DECISION](../OPEN-DECISIONS.md) | 那是企劃的權限 |
| 因為某個規則卡住就先猜一個值填進去 | 猜的值會被當成規格。要嘛留標記不實作，要嘛去問 |
