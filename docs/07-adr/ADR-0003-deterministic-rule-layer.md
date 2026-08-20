# ADR-0003 — 規則層只用整數，狀態雜湊用自訂 canonical 序列化

**Status**：`Accepted`
**Date**：2026-08-13
**來源**：SPEC v0.1 §6.6

---

## Context

[prototype-charter §3](../01-vision/prototype-charter.md)：

> **Prototype 成功 = 我們能用數據回答五個問題。不是「好玩」，是「可測量」。**

「可測量」推導出「自動化跑 1000 場」。而跑分數據要有意義，
必須保證同一組輸入永遠得到同一個結果 —— 否則跑出來的差異可能來自
浮點誤差或列舉順序，而不是設計變更。

**確定性在本專案是需求（R6），不是工程偏好。**

## Problem

.NET / Unity 環境有三個會悄悄破壞確定性的來源：

1. **float 不保證跨平台／跨編譯設定位元一致**
   （IL2CPP vs Mono、不同最佳化等級、不同 CPU）
2. **`Dictionary` / `HashSet` 不保證列舉順序**
3. **.NET Core 的 `string.GetHashCode()` 有 randomization** ——
   同一個字串在不同 process 得到不同雜湊值

第 3 點特別危險：**它在同一次執行內看起來完全正常**，
只有跨 process 比對時才會爆炸 —— 也就是 CI 上才會爆炸。

## Options

### A. 不管，需要時再修
最省事，但「需要時」就是跑分數據已經不可信的時候。
而且不確定性 bug 的定位成本極高（症狀是「偶爾不一樣」）。

### B. 用 float 但加 epsilon 比較
治標不治本。狀態雜湊無法用 epsilon。

### C. 規則層只用整數 ＋ 明確禁止不確定的迭代 ＋ 自訂 canonical 雜湊

## Decision

**採 C：採用「確定性三戒」，並用測試守住。**

三戒的完整內容、適用邊界與常見陷阱定義在
**[04-architecture/determinism.md](../04-architecture/determinism.md)**（該檔是三戒的 Source of Truth）。
摘要：**只用整數** ／ **不依賴 hashmap 列舉順序** ／ **狀態雜湊不用 `GetHashCode()`**。

驗收方式：架構測試 **A4** 用 **golden hash 常數比對**，不是「跑兩次比較」。

## Rationale

- **整數消除了問題來源**，而不是繞過它
- **戒二的成本很低**：改用 `List` 或先排序 key，寫法幾乎一樣
- **golden hash 常數比對能抓到跨 process、跨平台、跨編譯設定的漂移**；
  跑兩次比較只能抓到同一個 process 內的問題 —— 而那不是真正的風險所在
- 三戒都可以被測試守住，不依賴人的記憶

### 為什麼選 golden hash 而不是「跑兩次」

跑兩次比較的失敗模式：`string.GetHashCode()` 的 randomization
在**同一個 process 內是穩定的**，所以跑兩次會通過，
上了 CI（不同 process）才失敗。那時候已經很難定位。

## Consequences

### 正面
- 跑分數據可信
- 確定性 bug 在**引入的當下**就被抓到，不是幾週後
- 若 [OD-05](../OPEN-DECISIONS.md#od-05) 選封包 2（必中），Core 根本不需要亂數，
  戒一自動滿足，測試變成純粹的「同指令串 → 同雜湊」

### 負面 / 代價
- **GDD 的數值是小數**（`HIT 0.80` / `EVA 0.20`），需要在資料載入邊界轉整數
  （[R-DATA-05](../03-spec/SPEC-unit-data.md)）
- **傷害公式的整數捨去必須明確定義**（[R-COMBAT-05](../03-spec/SPEC-combat.md) 規定無條件捨去）
- **要自己寫序列化**，不能用現成的雜湊
- **golden hash 在規則或資料合法變更時會失敗，必須手動更新。**
  這是刻意的 —— 它強迫你注意到「這次改動改變了模擬結果」，
  而那正是我們想要的信號。更新流程見 [workflows.md](../05-development/workflows.md)
- **Dijkstra 的 tie-break 必須明確定義**（[R-MOVE-11](../03-spec/SPEC-movement.md)），
  不能依賴優先佇列的預設行為

### 適用邊界
**表現層不受三戒約束。** 動畫插值、UI 位置當然可以用 float。
三戒只約束會影響狀態或雜湊的計算。

### 對其他決策的影響
- 建立在 [ADR-0002](ADR-0002-single-command-funnel.md) 的明確輸入輸出邊界之上
- [ADR-0004](ADR-0004-hand-written-clone.md) 的 canonical 序列化與本 ADR 的雜湊共用同一套機制
