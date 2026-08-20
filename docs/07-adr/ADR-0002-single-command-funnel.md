# ADR-0002 — 所有狀態變更走單一漏斗，`Execute` 為純函式

**Status**：`Accepted`
**Date**：2026-08-13
**來源**：SPEC v0.1 §6.3

---

## Context

Prototype 需要四件在一般專案裡通常各自實作的能力：

| 能力 | 誰需要 |
|---|---|
| AI 推演「如果我走這裡會怎樣」 | 敵方 AI |
| 傷害預覽 | UI |
| 自動化跑 1000 場並蒐集指標 | [prototype-charter §3](../01-vision/prototype-charter.md) 的成功標準 |
| 確定性重播與除錯 | [determinism](../04-architecture/determinism.md) |

## Problem

如果狀態變更散落在多處（`Unit.TakeDamage()`、`GridManager.MoveUnit()`、
`TurnManager.EndTurn()` …），以上四件事都要各自處理，而且：

- AI 推演需要一套**不會真的改到狀態**的平行實作 → 兩套規則 = 兩套 bug
- 「這個狀態怎麼變成這樣的」無法回答 → 除錯只能靠斷點
- 表現層必須自己判斷「剛剛發生了什麼」才知道要播什麼動畫
- 確定性無從驗證，因為沒有一個明確的「輸入 → 輸出」邊界

## Options

### A. 傳統 OOP：物件自己改自己
`unit.TakeDamage(50)` 這種。直覺、好寫，但以上四個能力全部落空。

### B. Event Sourcing（完整事件溯源）
狀態完全由事件串重建。能力齊全，但對 prototype 過重
—— 需要事件版本管理、快照策略、重放最佳化。

### C. 單一漏斗 + 純函式模擬器
`Command → Validate → Effect[] → Apply`，`Execute(state, cmd)` 回傳新 state ＋ 事件流。

## Decision

**採 C。**

```
Command  →  Validate  →  Effect[]  →  Apply
```

`BattleSimulator.Execute(state, command)` 是**純函式**：
同輸入同輸出、無副作用、不改動傳入的 state；回傳新 state ＋ 有序事件流。

**沒有例外。AI 也走這條路。**（[R-CMD-01](../03-spec/SPEC-battle-flow.md)）

> 📍 完整的邊界簽章與四階段責任在
> [04-architecture/simulation-core.md](../04-architecture/simulation-core.md)；
> Command / Effect 的清單與契約在
> [03-spec/SPEC-battle-flow.md §5](../03-spec/SPEC-battle-flow.md)。
> 本 ADR 只記錄**為什麼**，不重複那些內容。

## Rationale

- **AI 推演變成 `Clone()` + `Execute()`** —— 不需要第二套規則實作
- **確定性有了明確邊界**：`Execute` 是純函式 → 可以驗證「同輸入同輸出」（A2、A4）
- **表現層退化成播放器**：不查詢規則層、不做判斷（A7）
- **自動化跑分變成一個 while 迴圈**，完全不需要 Unity
- **Replay 幾乎免費**：記錄 seed + 指令串就能重跑整場
- 比 Event Sourcing 輕：不需要事件版本管理與快照策略，
  因為一場戰鬥很短、狀態很小

## Consequences

### 正面
- 四個能力一次拿到
- 「這個狀態怎麼來的」可以從 EffectLog 直接讀出
- 拒絕的 Command 不留痕跡（[R-CMD-04](../03-spec/SPEC-battle-flow.md)），
  「試了但不行」不會污染狀態

### 負面 / 代價
- **每次 `Execute` 都要複製狀態** → 依賴 `Clone()` 正確且不太慢。
  Stage 01 的狀態很小（1 + 4 個單位、120 格），可以接受；
  若未來狀態變大需要重新評估（結構共享或 in-place + undo log）
- **Effect 粒度設計是持續成本**：太粗 → 表現層要自己判斷（A7 失效）；
  太細 → Effect 數量爆炸、播放器複雜
- **Validate 必須完整**，不能信任呼叫端。`MoveCommand` 帶完整 path 就是這個原因
  —— 目標格不足以決定成本，必須驗整條路徑（[R-MOVE-06](../03-spec/SPEC-movement.md)）
- 寫起來比 `unit.TakeDamage(50)` 囉嗦

### 對其他決策的影響
- [ADR-0003](ADR-0003-deterministic-rule-layer.md) 建立在「有明確的輸入輸出邊界」之上
- [ADR-0004](ADR-0004-hand-written-clone.md) 是本 ADR 的直接後果 —— 純函式需要深複製
- 架構測試 A2、A3、A4、A7 全部在守這條線
