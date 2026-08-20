# Architecture — Simulation Core

| | |
|---|---|
| **Purpose** | 定義規則層的內部結構：單一漏斗、狀態複製、狀態雜湊 |
| **Audience** | 程式 |
| **Source of Truth** | 本檔（源自 SPEC v0.1 §6.3、§6.5、§6.7、§6.8） |
| **Dependencies** | [overview.md](overview.md) |
| **Related** | [determinism.md](determinism.md)、[ADR-0002](../07-adr/ADR-0002-single-command-funnel.md)、[ADR-0004](../07-adr/ADR-0004-hand-written-clone.md) |

---

## 1. 單一漏斗

```
Command  →  Validate  →  Effect[]  →  Apply
```

**所有**狀態變更都走這條路（[R-CMD-01](../03-spec/SPEC-battle-flow.md)）。
沒有例外，AI 也不例外。

邊界簽章（**這是契約，不是實作**）：

```csharp
public readonly struct ExecuteResult
{
    public readonly BattleState State;
    public readonly EffectLog   Log;
    public readonly bool        Ok;
    public readonly string      RejectReason;
}

public static class BattleSimulator
{
    // 純函式：同輸入同輸出、無副作用、不改動傳入的 state
    public static ExecuteResult Execute(BattleState state, ICommand command);
}
```

理由與取捨見 [ADR-0002](../07-adr/ADR-0002-single-command-funnel.md)。

### 1.1 四個階段的責任

| 階段 | 責任 | 不負責 |
|---|---|---|
| **Command** | 表達「想做什麼」＋參數 | 判斷合不合法 |
| **Validate** | 判斷合法性，**不信任呼叫端** | 改變狀態 |
| **Effect[]** | 描述「發生了什麼」，順序即因果 | 播放 |
| **Apply** | 依 Effect 產生新 state | 判斷 |

**關鍵性質**：Validate 失敗時 `Log` 為空、`State` 是傳入的原 state
（[R-CMD-04](../03-spec/SPEC-battle-flow.md)）。
「試著做但做不到」不會留下痕跡。

### 1.2 Effect 粒度原則

> **一個 Effect = 表現層可獨立播放的一個原子事件。**

一個 Command 展開成有序的多個 Effect，順序即因果順序。
規則層在瞬間求值時就決定好完整 log，**表現層不做任何判斷，只按序播**。

這條原則是 A7 能成立的前提：如果 Effect 粒度太粗
（例如只有一個 `AttackHappened` 包含所有資訊），表現層就必須自己拆解、自己判斷，
A7 就形同虛設。

Command 與 Effect 的完整清單在
[SPEC-battle-flow §5](../03-spec/SPEC-battle-flow.md)（**那是契約，屬 Specification**）。

---

## 2. 移動範圍 vs 尋路（不要搞混）

| 用途 | 演算法 | 理由 |
|---|---|---|
| 玩家移動範圍、威脅範圍 | **Dijkstra flood fill** | 要「所有可達格 + 成本」，目標未知 |
| AI 點對點路徑 | **A\*** | 目標已知，有啟發函數，較快 |

- 地形成本進 **edge weight**，不寫死在拓撲裡
- Dijkstra 的**展開順序必須確定**，包含 tie-break（見 [determinism.md](determinism.md)）
- 移動範圍與威脅範圍**共用同一份可達性實作**
  （[R-THR-02](../03-spec/SPEC-threat-activation.md)）

> **共用是刻意的。** 兩份實作會漂移，然後玩家看到的危險區與實際能被打到的格子不一致 ——
> 那會直接摧毀「完全資訊」的信任，而完全資訊是這個 Prototype 的前提之一。

---

## 3. 深複製

| 用途 | 為什麼需要 |
|---|---|
| AI 推演 | AI 要試算「如果我走這裡會怎樣」 |
| 傷害預覽 | UI 要顯示「打下去會掉多少血」 |
| Undo | [OD-12](../OPEN-DECISIONS.md#od-12) |
| 純函式保證 | `Execute` 不改動傳入的 state |

**做法：手寫 `Clone()`，配一條「Clone 隔離」測試（架構測試 A3）。**

理由與被排除的選項見 [ADR-0004](../07-adr/ADR-0004-hand-written-clone.md)。

### 3.1 Clone 隔離的驗收

A3 要求：`state.Clone()` 後改動副本，原本不受影響，**含所有巢狀集合**。

> **「含所有巢狀集合」是重點。** 最常見的 bug 是 `List<Unit>` 被淺複製 ——
> 兩個 state 共用同一批 `Unit` 物件，AI 推演直接污染真實狀態，
> 而且症狀會延遲出現、極難除錯。
>
> 測試必須逐層驗證，不能只驗頂層。

---

## 4. 狀態雜湊

**用途**：確定性驗證（A4：同 seed + 同指令串 → 同世界狀態雜湊）。

**做法**：手寫 canonical `BinaryWriter` → FNV-1a。

| 禁止 | 為什麼 |
|---|---|
| 內建 `GetHashCode()` | .NET Core 的 `string.GetHashCode()` 有 randomization，同字串跨 process 不同值 |
| 依賴 `Dictionary` / `HashSet` 的列舉順序 | .NET 不保證 hashmap 列舉順序 |
| 把 UI 狀態納入雜湊 | 選取中的單位、動畫進度不屬於遊戲狀態 |

詳見 [determinism.md](determinism.md)。

### 4.1 Canonical 的意思

「正規化」= **同一個邏輯狀態必定產生同一串位元組**。

實務上代表：
- 集合先排序（依穩定的 key），再序列化
- 欄位順序固定
- 不寫入任何與邏輯狀態無關的東西（時間戳、物件參考、暫存欄位）

---

## 5. 白送的兩個功能

單一漏斗 + 可深複製狀態一做完，這兩個成本大幅降低：

| 功能 | 規則層成本 | 表現層成本 | 狀態 |
|---|---|---|---|
| **Replay** | 幾乎為零（記錄 seed + 指令串就能重跑整場） | 零（跑分不需要畫面） | **自動化跑分必須有這個能力** |
| **Undo** | 低（存快照或反向重跑） | **不低**（要把播完的動畫倒回去） | [OD-12](../OPEN-DECISIONS.md#od-12) |

> ⚠️ SPEC v0.1 §6.8 稱兩者都「白送」。**這對 Replay 成立，對 Undo 只有一半成立。**
> 規則層免費，表現層不免費。文件已在 [OD-12](../OPEN-DECISIONS.md#od-12) 修正這個說法。

**Replay 不是選配。** [playtest-metrics](../06-validation/playtest-metrics.md)
的自動化跑分本質上就是 Replay 能力的應用；
沒有它，Prototype 的成功標準（「可測量」）達不到。

---

## 6. 序列化

| 用途 | 做法 | 狀態 |
|---|---|---|
| 深複製 | 手寫 `Clone()` | 已決（[ADR-0004](../07-adr/ADR-0004-hand-written-clone.md)） |
| 狀態雜湊 | 手寫 canonical `BinaryWriter` → FNV-1a | 已決 |
| 資料載入 | 見 [OD-11](../OPEN-DECISIONS.md#od-11) | **未決** |
| 存檔 | **Prototype 非目標** | 不做 |
| 除錯 dump | 可延後 | 未決 |

**明確不用的**：

| 不用 | 為什麼 |
|---|---|
| `JsonUtility` | 不支援 Dictionary、不支援多型／介面 |
| record + `with` | `with` 只做淺複製，巢狀 `List<Unit>` 會被兩個 state 共用 |
| MemoryPack | 是**選項不是決定**。SPEC v0.1 §6.7 明確寫「prototype 階段先手寫」。**目前沒有需求** |

---

## 7. 尚未定義的部分

以下屬於本層責任，但目前**沒有足夠資料寫出來**：

| 缺口 | 阻擋原因 |
|---|---|
| AI 的結構（utility 評分？固定策略？） | [OD-10](../OPEN-DECISIONS.md#od-10) — AI 行為規格不存在 |
| 跑分工具的形式（EditMode 測試？獨立 console？） | 依賴 [OD-11](../OPEN-DECISIONS.md#od-11)（資料格式）與 §7.1 的 Unity 外測試選擇 |
| Session / Game Loop 的結構 | [ODD-02](../DOCUMENT-MAP.md#odd-02) |

**不要為了「架構看起來完整」而先設計它們。** 沒有需求的架構是過度設計。
