# ADR-0004 — 手寫 `Clone()`，不用 record `with`、不用序列化往返

**Status**：`Accepted`
**Date**：2026-08-13
**來源**：SPEC v0.1 §6.7

---

## Context

[ADR-0002](ADR-0002-single-command-funnel.md) 要求 `Execute(state, cmd)` 是純函式，
**不改動傳入的 state**。這代表每次執行都需要一份獨立的狀態複本。

深複製同時服務四個用途：AI 推演、傷害預覽、Undo、純函式保證。

## Problem

「深複製一個含巢狀集合的狀態物件」在 C# 有好幾種寫法，
而**其中最直覺的那個是錯的**。

`BattleState` 至少包含 `List<Unit>`，而 `Unit` 是可變的（HP、AP、位置會變）。

## Options

### A. `record` + `with`
C# 的慣用寫法，一行搞定。

**致命問題：`with` 只做淺複製。** 巢狀的 `List<Unit>` 會被兩個 state 共用 ——
AI 推演會直接污染真實狀態。而且**症狀會延遲出現**（AI 推演完之後畫面才不對），
極難定位。

次要問題：Unity 的序列化系統不支援 record，且在 SPEC v0.1 記載的 C# 9 環境下
`record` 需要手動宣告 `IsExternalInit`。
（⚠️ 此環境前提需重新查證 → [CONFLICT-06](../CONFLICTS.md#conflict-06)）

### B. 序列化往返（JSON / MemoryPack）
序列化再反序列化，自然得到深複製。

問題：
- `JsonUtility` 不支援 Dictionary、不支援多型／介面 → 直接出局
- Newtonsoft 目前**未安裝**在專案中
- MemoryPack（Cysharp，source generator、無反射、IL2CPP 友善）是可行選項，
  官方宣稱 Unity 下比 `JsonUtility` 快 3–10 倍
- 但**每次 Execute 都做一次序列化往返，成本高於手寫 Clone**

### C. 手寫 `Clone()`
逐欄位複製，巢狀集合逐元素複製。

## Decision

**採 C：手寫 `Clone()`，配一條「Clone 隔離」測試（架構測試 A3）。**

A3 的斷言：`state.Clone()` 後改動副本，原本不受影響 —— **含所有巢狀集合**。

## Rationale

- **A 的淺複製問題是致命的**，而且不是「小心一點就好」——
  `with` 的語意本來就是淺複製，沒有辦法讓它變成深複製
- **B 的成本與依賴都不划算**：Prototype 階段引入序列化框架，
  換來的只是省下一個手寫函式
- **C 的主要風險是「手寫容易漏欄位」**，而這個風險可以被測試消除 ——
  A3 就是為此存在的
- SPEC v0.1 §6.7 明確寫「**prototype 階段先手寫**」，MemoryPack 是「框架選項（不急）」

> **注意 Decision 的理由順序**：
> 主要理由是**淺複製語意**，次要理由才是 C# 版本限制。
> 即使 [CONFLICT-06](../CONFLICTS.md#conflict-06) 查證後發現 Unity 6000.5.1f1
> 支援 record，**本決策仍然成立** —— 因為 `with` 依然是淺複製。

## Consequences

### 正面
- 沒有額外套件依賴
- 複製成本最低（不經過序列化）
- canonical 序列化（[ADR-0003](ADR-0003-deterministic-rule-layer.md) 的狀態雜湊）
  可以與 Clone 共用同一份「欄位清單」的知識，兩者一起維護

### 負面 / 代價
- **新增欄位時很容易忘記加進 `Clone()`。**
  這是本決策唯一的真正風險，緩解手段是 A3 測試 ——
  但 A3 也需要在新增欄位時更新
- 因此 [definition-of-done.md](../05-development/definition-of-done.md) 必須包含：
  **「`BattleState` 新增欄位時，同步更新 `Clone()`、canonical 序列化、A3 測試」**
- 若未來 `BattleState` 大幅成長，手寫維護成本會上升 →
  屆時重新評估 MemoryPack（本 ADR 可被 supersede）

### 重新評估的觸發條件
- `BattleState` 欄位數超過手寫可靠維護的規模
- A3 測試曾經真的抓到漏欄位（代表風險已實現）
- Prototype 階段結束，進入正式製作

---

## 後續變更（Clone 契約的實際欄位清單）

本 ADR 的成本項「新增欄位時很容易忘記加進 `Clone()`」已經被行使多次。
**以下記錄每次擴充，以及它對本決策的影響。**

| 日期 | 新增到 `UnitState` / `BattleState` | 對 Clone 的處理 |
|---|---|---|
| 2026-08-15 | `BattleState._contamination`（可變地形） | **深拷貝**（未使用時保持 `null`）。這是本 ADR 預測過的那一類成本 |
| 2026-08-15 | `UnitState.SlowedUntilTurn` / `TauntingUntilTurn` | 逐欄複製 |
| 2026-08-15 | `UnitState.IsObjectiveTarget`（唯讀） | 逐欄複製 |
| **2026-08-17** | **`UnitState.ArmorBrokenUntilTurn`**（破甲回合戳記）<br>**`UnitState.ArmorBrokenAmount`**（DEF 扣減量） | 逐欄複製，見 [OD-34](../OPEN-DECISIONS.md#od-34) |

### 2026-08-17 的破甲欄位：為什麼是兩個而不是一個

**戳記單獨存在不足以還原狀態。** 兩個扣減量不同的破甲會產生相同的戳記，
如果只複製／只雜湊戳記，兩個實際上不同的世界狀態會碰撞成同一個雜湊 ——
那正是 [ADR-0003](ADR-0003-deterministic-rule-layer.md) 的確定性保證要排除的情況。
`ArmorBreakStateIsClonedAndHashed` 直接斷言這件事：改動副本的 `ArmorBrokenAmount`
之後雜湊**必須**改變。

**扣減量存在目標身上，不是查施術者。** 結算傷害時施術者可能已死、已離場或不是攻擊者本身；
一條需要伸手去讀別的單位當前數值的規則，會隨著誰還活著而給出不同答案。

### 本 ADR 的狀態

`[A]` **三個重新評估的觸發條件目前都尚未成立** ——
特別是第二條：**A3 至今沒有抓到過漏欄位**，
每次擴充都是在新增當下就同步了 Clone、StateHasher 與測試。
**手寫方案仍然成立，本 ADR 維持 Accepted。**

> ⚠️ 但代價已經可見：**每新增一個欄位就要同時記得三個地方**
> （`Clone()`、`StateHasher`、A3／專屬測試）。
> 目前靠的是紀律與 code comment，不是機制。
> **若 `UnitState` 再成長一輪，應重新評估 MemoryPack。**
