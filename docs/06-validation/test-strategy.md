# Validation — Test Strategy

| | |
|---|---|
| **Purpose** | 定義測什麼、怎麼測、以及架構回歸的 7 條斷言 |
| **Audience** | 程式 |
| **Source of Truth** | 本檔（A1–A7 源自 SPEC v0.1 §7.1） |
| **Dependencies** | [03-spec/](../03-spec/)、[04-architecture/](../04-architecture/) |
| **Related** | [playtest-metrics.md](playtest-metrics.md)、[05-development/definition-of-done.md](../05-development/definition-of-done.md) |

---

## 1. 測試的兩個層次

| 層次 | 測什麼 | 在哪跑 | 現況 |
|---|---|---|---|
| **架構回歸**（A1–A7） | 架構的硬性約束沒有被破壞 | EditMode（Core 為主） | 尚未建立 |
| **規格驗收**（R-xxx） | 每條規格條目的行為正確 | EditMode | 尚未建立 |

**Test Framework 1.7.0 已安裝**（`Packages/manifest.json`），但**沒有任何測試存在**。

> **測試重心在 EditMode 的規則層。** PlayMode 測試不是重點 ——
> 表現層是可拋棄品（[ADR-0005](../07-adr/ADR-0005-grayblock-3d-prototype-shell.md)），
> 為它寫大量測試是投資在會被丟掉的東西上。

---

## 2. 架構回歸測試（A1–A7）

**這 7 條是架構的免疫系統。** 它們不測遊戲行為，測的是
「有沒有人不小心把架構弄壞了」。

| 編號 | 斷言 | 守護的決策 |
|---|---|---|
| **A1** | `Ediki.Core` 的任何型別不引用 `UnityEngine` / `UnityEditor` | [ADR-0001](../07-adr/ADR-0001-core-unity-assembly-split.md) |
| **A2** | `Execute(state, cmd)` 呼叫前後，傳入的 `state` 物件雜湊不變（純函式） | [ADR-0002](../07-adr/ADR-0002-single-command-funnel.md) |
| **A3** | `state.Clone()` 後改動副本，原本不受影響（**深複製隔離；含所有巢狀集合**） | [ADR-0004](../07-adr/ADR-0004-hand-written-clone.md) |
| **A4** | 同 seed + 同指令串 → 同世界狀態雜湊（**golden hash，常數比對**） | [ADR-0003](../07-adr/ADR-0003-deterministic-rule-layer.md) |
| **A5** | AP 成本、地形成本、單位數值皆從資料讀取；**程式碼中不得出現這些字面值** | [R-DATA-01](../03-spec/SPEC-unit-data.md) |
| **A6** | `IGridTopology.Neighbors` 的回傳順序在同輸入下固定 | [R-GRID-02](../03-spec/SPEC-grid-terrain.md)、戒二 |
| **A7** | 表現層組件不得反向引用 Core 的**可變狀態**（只能讀 EffectLog 與唯讀查詢） | [ADR-0001](../07-adr/ADR-0001-core-unity-assembly-split.md)、[R-THR-12](../03-spec/SPEC-threat-activation.md) |

### 2.1 實作上的注意事項

| 測試 | 注意 |
|---|---|
| **A1** | asmdef 的 No Engine References 已經是編譯期防線，A1 是**第二層** —— 它防的是「asmdef 設定被誤改」 |
| **A3** | **必須逐層驗證巢狀集合**，不能只驗頂層。最常見的 bug 是 `List<Unit>` 被淺複製 |
| **A4** | 用 **golden hash 常數比對**，不是「跑兩次比較」。跑兩次只能抓同 process 內的問題；`string.GetHashCode()` 的 randomization 在同 process 內是穩定的，跨 process 才爆 |
| **A5** | 實作方式待定（反射掃描？Roslyn analyzer？原始碼掃描？）。**這條是最難自動化的一條**，可能需要退化成「約定 + review」 |
| **A7** | ⚠️ **A7 的原始措辭需要修正** —— 見下方 |

### 2.2 A7 的修正

SPEC v0.1 §7.1 的原始措辭是：

> A7｜表現層組件不得反向引用 Core 的可變狀態（**只能讀 EffectLog**）

但 UI 需求 [R-THR-09/10](../03-spec/SPEC-threat-activation.md) 要求
「hover 任一格顯示 Exposure」，**這無法只靠 EffectLog 滿足** ——
Effect 是事件流，不是空間查詢介面。

**修正後的 A7**：

> 表現層可以：讀 `EffectLog`、呼叫規則層的**唯讀查詢**（回傳值）。
> 表現層不可以：持有 `BattleState` 的可變參考、呼叫任何會改變狀態的方法、
> 自行重算規則層負責的量（Exposure、威脅範圍）。

架構依據見 [04-architecture/overview.md §3「兩條線」](../04-architecture/overview.md)。

> **這是本輪對既有文件的修正**，不是新增需求。
> 原措辭若照字面實作，UI 需求做不出來。

---

## 3. 規格驗收測試

原則：**每條 `R-xxx` 至少一個測試。**

規格條目的 `Acceptance` 欄位就是測試的規格。
Acceptance 寫不出來 → 那條規格不夠精確，或它其實屬於 Design 層。

### 3.1 目前可以寫測試的規格

| 規格 | 可測的 R-xxx | 說明 |
|---|---|---|
| SPEC-battle-flow | R-TURN-01/02/05、R-AP-01/02/04/05/06、R-ACT-01/03、R-WIN-01..04、R-WIN-06、R-CMD-01..04、R-EFF-01..03 | 骨架完整 |
| SPEC-grid-terrain | R-GRID-01..04、R-GRID-07、R-TERR-01/02/06、R-MAP-01/02 | 拓撲可測 |
| SPEC-movement | R-MOVE-02/06/08..11/13 | **演算法可測，規則不可測** |
| SPEC-combat | R-COMBAT-01/03/04/06/11/12/17/18、R-COMBAT-20..23 | 結構可測 |
| SPEC-threat-activation | R-THR-02/03/04、R-THR-11/12 | |
| SPEC-unit-data | R-DATA-01/02/04/05/06 | schema 可測 |

### 3.2 目前**不能**寫測試的規格

| 規格 | 阻擋原因 |
|---|---|
| R-MOVE-01/03/04/05/07/12（成本模型、MOVE 語意、單位阻擋） | [CONFLICT-01](../CONFLICTS.md#conflict-01)、[OD-03](../OPEN-DECISIONS.md#od-03)、[OD-04](../OPEN-DECISIONS.md#od-04) |
| R-COMBAT-05（傷害公式） | [CONFLICT-07](../CONFLICTS.md#conflict-07) — 公式無來源 |
| R-COMBAT-09/10（命中） | [OD-05](../OPEN-DECISIONS.md#od-05) |
| R-COMBAT-14/15/16（防禦效果） | [OD-06](../OPEN-DECISIONS.md#od-06) |
| R-GRID-05/06/08（Exposure 值） | [OD-02](../OPEN-DECISIONS.md#od-02)、[OD-03](../OPEN-DECISIONS.md#od-03) |
| R-TERR-03/04（地形清單） | [CONFLICT-02](../CONFLICTS.md#conflict-02)、[OD-02](../OPEN-DECISIONS.md#od-02) |
| R-THR-01/06/07/08（威脅範圍值、啟動粒度） | 依賴上述 ＋ [OD-10](../OPEN-DECISIONS.md#od-10) |
| R-MAP-03（Stage 01 地圖） | [CONFLICT-02](../CONFLICTS.md#conflict-02) |
| 全部 AI 行為 | [ODD-01](../DOCUMENT-MAP.md#odd-01) — 規格不存在 |

> **這張表就是「哪些程式現在不能寫」的清單。**
> 骨架與演算法可以做，規則本身不行。

---

## 4. 為什麼這個專案的測試特別重要

一般專案的測試是「防止回歸」。**這個專案的測試是「讓實驗有效」。**

`Execute` 是純函式可 seed → 手感指標**全部可以自動化跑 1000 場統計**，
不用手動 playtest。

> **這是強架構在這個專案的最大回報。**

沒有確定性與純函式，Prototype 的成功標準（「用數據回答五個問題」）
根本達不到 —— 只能靠人手動玩幾十場，樣本太小、變異太大、結論不可信。

---

## 5. 尚未決定的測試基礎建設

| 項目 | 狀態 |
|---|---|
| 是否維護 .NET Standard 2.1 的 `.csproj` 讓 Core 能用 `dotnet test` 跑 | 依賴 [OD-11](../OPEN-DECISIONS.md#od-11)（資料格式）。選 ScriptableObject 作為正本 = 放棄這個選項 |
| CI | 專案不是 git repo，無 CI。見 [workflows §8](../05-development/workflows.md) |
| A5 的自動化實作方式 | 未定。反射掃描 / Roslyn analyzer / 原始碼掃描 |
| 跑分工具的形式 | [ODD-02](../DOCUMENT-MAP.md#odd-02) |
