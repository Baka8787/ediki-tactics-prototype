# Development — Definition of Done

| | |
|---|---|
| **Purpose** | 一件事做到什麼程度才算做完 |
| **Audience** | 程式、Claude Code |
| **Source of Truth** | 本檔 |
| **Related** | [workflows.md](workflows.md)、[coding-guidelines.md](coding-guidelines.md)、[06-validation/test-strategy.md](../06-validation/test-strategy.md) |

---

## 通用 DoD（任何程式變更）

- [ ] 沒有繞過 [coding-guidelines §1](coding-guidelines.md) 的 8 條硬性規則
- [ ] 所有架構回歸測試（**A1–A7**）通過
- [ ] 沒有引入未登錄的 TBD。暫時實作留了 `OPEN DECISION → OD-xx` 標記
- [ ] 沒有自行決定任何 [Open Decision](../OPEN-DECISIONS.md)
- [ ] 相關文件已更新（依 [workflows.md](workflows.md) 的對應流程）

---

## 依變更類型的追加 DoD

### 新增 / 修改 Battle Rule

- [ ] `03-spec/` 有對應的 `R-xxx`，Status 不是 `OPEN` 或 `CONFLICT`
- [ ] `R-xxx` 的 **Acceptance 欄位有內容**，且已有對應測試
- [ ] golden hash 若失敗，**已確認變更是預期的**（見 [workflows §6](workflows.md)）
- [ ] `CHANGELOG.md` 已記錄

### 修改 `BattleState`（新增／移除欄位）

🔴 **這是最容易出錯的變更類型**（[ADR-0004](../07-adr/ADR-0004-hand-written-clone.md) 的已知風險）：

- [ ] `Clone()` 已同步更新
- [ ] canonical 序列化（雜湊用）已同步更新
- [ ] **A3（Clone 隔離）測試已涵蓋新欄位**，含巢狀集合
- [ ] 新欄位確實屬於「遊戲狀態」，不是 UI 狀態
      （選取中的單位、動畫進度、滑鼠位置**不屬於** `BattleState`）
- [ ] golden hash 已更新

### 數值平衡調整

- [ ] **附 TTK 表**（[R-COMBAT-08](../03-spec/SPEC-combat.md)）。
      回合制平衡看「幾次攻擊能擊殺」，不看單發傷害
- [ ] 只改了資料，沒改程式（若改了程式 → 違反 A5）
- [ ] 若跨越了兩個數值封包的界線 → 停，那是 [OD-05](../OPEN-DECISIONS.md#od-05)

### 新增 Effect 型別

- [ ] 粒度符合原則：**一個 Effect = 表現層可獨立播放的一個原子事件**
- [ ] 命名用過去式（`UnitMoved` 而非 `MoveUnit`）
- [ ] 在 [SPEC-battle-flow §5.3](../03-spec/SPEC-battle-flow.md) 的 Effect 清單登錄
- [ ] 表現層有對應的播放路徑（**或明確標記為 Prototype 不播**）
- [ ] 表現層播放它時**不需要做任何判斷**（否則違反 A7）

### 新增 Command 型別

- [ ] Validate 完整，**不信任呼叫端**
- [ ] 拒絕時 `Log` 為空且 state 不變（[R-CMD-04](../03-spec/SPEC-battle-flow.md)）
- [ ] 在 [SPEC-battle-flow §5.2](../03-spec/SPEC-battle-flow.md) 的 Command 清單登錄
- [ ] 至少一個「合法路徑」測試 + 每個拒絕條件各一個反例測試

### 新增介面（Extension Point）

- [ ] 通過 [extension-points.md](../04-architecture/extension-points.md) 的三項判準之一：
      已知有第二個實作要跑 / 是未決事項的技術落實 / 是換維度換引擎的切割線
- [ ] 三項都不符合 → **不要建立這個介面**

### 更新資料 schema

- [ ] [SPEC-unit-data.md](../03-spec/SPEC-unit-data.md) 的 schema 表已更新
- [ ] 若引入 float → 停，違反[確定性戒一](../04-architecture/determinism.md)

---

## Prototype 專屬：跑分能力的 DoD

Prototype 的成功標準是「可測量」。因此當戰鬥系統可運作時，
以下是**及格線而非加分項**：

- [ ] 能不開 Unity Editor 跑完一整場戰鬥
- [ ] 同 seed + 同指令串跑兩次結果完全相同（A4）
- [ ] 能蒐集 [playtest-metrics.md](../06-validation/playtest-metrics.md) 的 M1–M5
- [ ] 跑分有 timeout 保護（[R-WIN-04](../03-spec/SPEC-battle-flow.md)：規則層沒有回合上限）

---

## 明確**不在** DoD 裡的東西

避免 DoD 通膨：

| 不要求 | 為什麼 |
|---|---|
| 美術品質 | 灰盒，明確非目標 |
| 效能最佳化 | 1 + 4 個單位、120 格。**沒有效能問題** |
| 完整的錯誤處理 UI | Prototype 階段 `RejectReason` 印出來就夠 |
| 存檔相容性 | 不做存檔 |
| 音效 | 明確非目標 |
| PlayMode 測試覆蓋率 | 測試重心在 EditMode 的規則層，見 [test-strategy.md](../06-validation/test-strategy.md) |
