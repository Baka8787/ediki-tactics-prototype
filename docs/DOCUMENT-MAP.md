# Document Map

**這份文件定義：誰有權力說什麼。**

當兩份文件對同一件事有不同描述時，用這裡的權威關係判斷該信誰；
如果權威關係本身不清楚，那就是一個 [CONFLICT](CONFLICTS.md) 或
[Open Documentation Decision](#open-documentation-decisions)，不要自己選一個。

---

## 1. Document Authority Matrix

### 1.1 既有文件（docs/00-source/）

| 文件 | 主要用途 | 主要讀者 | 權威範圍 | 可覆蓋其他文件？ |
|---|---|---|---|---|
| **GDD**<br>`00-source/GDD-穢土紀企畫書-暫定.pdf` | 全遊戲 Vision、世界觀、角色與敵人設定、20 關進度、狀態表 | 企劃、美術、劇本、程式 | **世界觀、角色設定、單位基礎數值、關卡敘事與目標、狀態定義、進度結構**<br>標記「暫定」 | **可覆蓋** SPEC v0.1 的設計主張。<br>**不可覆蓋** SPEC v0.1 的技術架構章節（§6）——GDD 不涉及技術 |
| **SPEC v0.1**<br>`00-source/SPEC-Stage01-設計與規格文檔-v0.1.md` | Stage 01 Prototype 的規則規格與技術架構 | 程式（全部）、企劃（§1–5、§8） | **技術架構（§6）、測試與驗收（§7）、待決清單（§8）** 的唯一權威。<br>§1–2（目標與 Exposure 命題）是 Prototype 專屬的設計權威 | **可覆蓋** GDD 的技術與 Prototype 範圍界定。<br>**不可覆蓋** GDD 的世界觀與角色數值（§3–5 與 GDD 衝突處一律走 [CONFLICTS](CONFLICTS.md)） |
| **RESEARCH**<br>`00-source/RESEARCH-戰棋SRPG設計資源總整理.md` | 外部案例研究與設計建議 | 企劃、程式 | **無規則權威。**<br>只是「為什麼這樣設計」的論據來源與外部參照 | **不可覆蓋任何文件。**<br>引用時必須寫明它只是建議 |

### 1.2 三份文件的層級分工

| 層級 | 負責文件 |
|---|---|
| Game Vision | **GDD** |
| Game Design（全遊戲） | **GDD** |
| Game Design（Prototype 專屬） | **SPEC v0.1 §1–2** |
| Gameplay Rules | **GDD**（數值與定義）＋ **SPEC v0.1 §3**（Prototype 的規則細化）— **衝突最多的地帶** |
| Technical Requirements | **SPEC v0.1 §6.1** |
| Architecture | **SPEC v0.1 §6.2–6.9** |
| 背景資料 / 論據 | **RESEARCH** |
| 歷史／暫定資訊 | **GDD 標記「暫定」**；**SPEC v0.1 標記 v0.1 草案** — 兩份都不是定稿 |

### 1.3 明確標記為「不是目前規格」的內容

以下內容存在於既有文件中，但**不是 Stage 01 Prototype 的規格**，
引用時必須註明。這是為了避免「歷史／全遊戲文件被誤認為目前規格」：

| 內容 | 出處 | 為什麼不是目前規格 |
|---|---|---|
| 四魔將、荒世主、凶星鬼（小耗以外）全部設定 | GDD | SPEC v0.1 §1.2 明確非目標 |
| Stage 02–20 的所有關卡設定 | GDD | Prototype 只做 Stage 01 |
| 裝備演進、飾品系統 | GDD | SPEC v0.1 §1.2 明確非目標 |
| 污染與淨化系統 | GDD 核心系統之一 | SPEC v0.1 §1.2 明確非目標 |
| 狀態列表（強壯/脫力/中毒/暈眩…） | GDD | Stage 01 無任何技能或裝備會施加狀態 |
| 正守、玄真、影丸、仙子的全部設定 | GDD | Stage 01 只有桃太郎與小耗 |
| 桃太郎的四個技能（斬／討鬼斬／仙照・淺淨／號令・不屈） | GDD | Stage 01 只有通用「攻擊」動作 |
| 結局分歧 | GDD | 與 Prototype 無關 |
| RESEARCH 的全部建議 | RESEARCH | 建議不是規格 |
| SPEC v0.1 §4 的兩個數值封包 | SPEC v0.1 | 兩者互斥且未拍板（[OD-05](OPEN-DECISIONS.md#od-05)） |

> **這張表的用途**：新工程師或 Claude Code 讀到 GDD 裡的「污染擴散」「四魔將」時，
> 應該立刻在這裡查到「不在本輪範圍」，而不是開始實作。

### 1.4 本輪建立的文件（docs/ 其餘各層）

本輪建立的文件**不是新的知識來源**，是對既有文件的整理層。原則：

- **整理層不得產生既有文件沒有的規則。** 若某條規則在既有文件找不到，
  它就是缺口，登錄到 [OPEN-DECISIONS](OPEN-DECISIONS.md) 或本檔的 ODD 區。
- **整理層可以產生既有文件沒有的「文件結構」與「驗收條件」**，
  因為那是文件工程的職責，不是遊戲設計。

| 層 | 回答的問題 | 權威來源 | 可否覆蓋 00-source |
|---|---|---|---|
| `01-vision/` | 我們在做什麼、Prototype 要證明什麼 | GDD、SPEC v0.1 §1 | 否 |
| `02-design/` | 玩家該有什麼體驗、規則的意圖是什麼 | GDD、SPEC v0.1 §1–2、§5.1 | 否 |
| `03-spec/` | **什麼必須為真**（可測試、可驗收） | GDD §三、SPEC v0.1 §3–5 | 否；衝突處指向 CONFLICTS |
| `04-architecture/` | 責任怎麼分、依賴怎麼流 | SPEC v0.1 §6 | 否 |
| `05-development/` | 工程師該怎麼在這個專案工作 | 本層自訂（文件工程職責） | 不適用 |
| `06-validation/` | 怎麼證明規格被滿足了 | SPEC v0.1 §7 | 否 |
| `07-adr/` | **為什麼選這個** | 本層自訂，引用 SPEC v0.1 §6 | 不適用 |
| `99-governance/` | 文件怎麼維護 | 本層自訂 | 不適用 |

---

## 2. Source of Truth Map

**每個重要規則只能有一個主要 Source of Truth。**

| Knowledge | Source of Truth | 狀態 |
|---|---|---|
| Game Vision（全遊戲） | `00-source/GDD`（經 [01-vision/vision.md](01-vision/vision.md) 導覽） | 穩定（標記「暫定」） |
| Prototype 目標與非目標 | [01-vision/prototype-charter.md](01-vision/prototype-charter.md)（源自 SPEC v0.1 §1） | 穩定 |
| Exposure 的定義與地位 | [02-design/exposure.md](02-design/exposure.md)（源自 SPEC v0.1 §2） | 穩定 |
| Stage 01 的教學意圖 | [02-design/stage-01.md](02-design/stage-01.md) | **衝突**（[CONFLICT-02](CONFLICTS.md#conflict-02)） |
| 回合結構與 AP | [03-spec/SPEC-battle-flow.md](03-spec/SPEC-battle-flow.md) | 部分未決（OD-01, OD-07, OD-09） |
| 勝敗條件 | [03-spec/SPEC-battle-flow.md](03-spec/SPEC-battle-flow.md) | **穩定**（GDD 與 SPEC 一致） |
| 格子拓撲與地形 | [03-spec/SPEC-grid-terrain.md](03-spec/SPEC-grid-terrain.md) | **衝突**（CONFLICT-01/02；OD-02） |
| Exposure 的計算方式 | [03-spec/SPEC-grid-terrain.md](03-spec/SPEC-grid-terrain.md) | 依賴 OD-02, OD-03 |
| 移動規則 | [03-spec/SPEC-movement.md](03-spec/SPEC-movement.md) | **衝突**（CONFLICT-01；OD-03, OD-04） |
| 傷害與命中 | [03-spec/SPEC-combat.md](03-spec/SPEC-combat.md) | **衝突**（CONFLICT-07；OD-05, OD-06） |
| 威脅範圍與敵人啟動 | [03-spec/SPEC-threat-activation.md](03-spec/SPEC-threat-activation.md) | 穩定（推導自其他規則） |
| 敵方 AI 行為 | **無**（[ODD-01](#odd-01)） | **缺口**（OD-10） |
| 單位數值 schema | [03-spec/SPEC-unit-data.md](03-spec/SPEC-unit-data.md) | 穩定 |
| 單位數值**值** | `00-source/GDD` 為主，Prototype 封包見 OD-05 | **衝突**（CONFLICT-03） |
| Command / Effect 契約 | [03-spec/SPEC-battle-flow.md](03-spec/SPEC-battle-flow.md) §Command-Effect | 穩定 |
| Technical Constraints | [04-architecture/overview.md](04-architecture/overview.md) | 需查證（[CONFLICT-06](CONFLICTS.md#conflict-06)） |
| Architecture | [04-architecture/](04-architecture/) | 穩定 |
| 確定性契約 | [04-architecture/determinism.md](04-architecture/determinism.md) | 穩定 |
| 架構決策的理由 | [07-adr/](07-adr/) | 穩定 |
| 測試策略與架構回歸斷言 | [06-validation/test-strategy.md](06-validation/test-strategy.md) | 穩定 |
| 手感驗收指標 | [06-validation/playtest-metrics.md](06-validation/playtest-metrics.md) | 穩定 |
| UI 需求 | [03-spec/SPEC-threat-activation.md](03-spec/SPEC-threat-activation.md)（需求）<br>UI 形式見 OD-14 | 部分缺口 |
| **Implementation** | **Code**（尚不存在） | — |
| **Test Behaviour** | **Tests**（尚不存在） | — |
| 所有未決事項 | [OPEN-DECISIONS.md](OPEN-DECISIONS.md) | — |
| 所有文件衝突 | [CONFLICTS.md](CONFLICTS.md) | — |

### 2.1 明確**沒有** Source of Truth 的知識

以下在 Stage 01 範圍內、但目前沒有任何文件負責。這些不是 TBD（設計未定），
而是 **ODD — Open Documentation Decision**（不知道該由哪一層文件負責）。

---

## Open Documentation Decisions

### ODD-01
**缺少什麼**：敵方 AI 的行為規格。
**為什麼需要**：Prototype 的五個驗收問題有三個依賴 AI 行為。AI 未定義，
自動化跑分的數字沒有意義（見 [OD-10](OPEN-DECISIONS.md#od-10)）。
**建議由哪一層負責**：`03-spec/SPEC-ai-behaviour.md`（新增）。
AI 的**意圖**（「AI 要讓玩家覺得站錯地方會被圍」）屬 Design；
AI 的**決策規則**（評估函式、tie-break、順序）屬 Specification，因為它必須可測試。
**本輪為何不建立**：建立會等同於自行決定 OD-10。

### ODD-02
**缺少什麼**：「一個 Prototype session 是什麼」的規格 —— 進入戰鬥前後發生什麼、
重開一局的流程、戰鬥結束後顯示什麼。
**為什麼需要**：自動化跑 1000 場（SPEC v0.1 §7.2）需要一個可程式化的
「開始一場 → 跑完 → 收集數據 → 重來」迴圈。這是 **Game Loop**，不是 Battle Loop。
**建議由哪一層負責**：`03-spec/SPEC-battle-flow.md` 已涵蓋 Battle Loop；
Session/Game Loop 建議放 `03-spec/SPEC-session-loop.md`（新增），
或在跑分工具實作時併入 `06-validation/`。
**本輪為何不建立**：範圍取決於 OD-12（Replay）與跑分工具的形式，資料不足以寫出可驗收的規格。

### ODD-03
**缺少什麼**：Stage 01 地圖的權威資料檔（不是文件裡的 ASCII 圖，而是實際會被讀取的資料）。
**為什麼需要**：SPEC v0.1 §5.2 的 ASCII 地圖是**文件**，一旦程式端建了資料檔，
兩者立刻會漂移，而且沒有機制偵測。
**建議由哪一層負責**：**資料檔本身是 Source of Truth，文件只放意圖。**
地圖資料放 `Assets/_Project/Data/Maps/`（依 OD-11 決定格式），
`02-design/stage-01.md` 只描述設計意圖與必須成立的性質（例如「必須存在至少一格 Exposure 1」），
由測試驗證資料檔滿足這些性質。
**本輪為何不建立資料檔**：那是實作，本輪不進入實作。

### ODD-04
**缺少什麼**：桃太郎「攻擊」這個動作對應 GDD 的哪一個技能。
**為什麼需要**：GDD 給桃太郎四個技能（斬／討鬼斬／仙照・淺淨／號令・不屈），
沒有「通用攻擊」。SPEC v0.1 的 `AttackCommand` 是一個抽象動作，
在 GDD 中最接近的是「斬 (Slash)：基礎近戰物理攻擊」。
但 GDD 沒說「斬」花幾 AP，SPEC 說「攻擊/技能 5 AP」。
**建議由哪一層負責**：`03-spec/SPEC-unit-data.md` 的「Stage 01 動作對照」節
（本輪已建立，內容標記為推定並指向本 ODD）。
**需要誰確認**：企劃。這是低風險確認題，不是設計題。

---

## 3. Requirements Traceability

追蹤鏈：**Requirement → Design → Specification → Architecture → Implementation → Tests**

目前 Implementation 與 Tests 皆不存在，因此所有鏈條都在 Architecture 處截斷。
**截斷是事實，不是缺陷。** 下面誠實標記 `—（未實作）`。

### 3.1 可建立完整鏈條的需求

| # | Requirement | Design | Specification | Architecture | Impl | Tests |
|---|---|---|---|---|---|---|
| R1 | 驗證「AP 8 / 攻擊 5 / 移動」能否產生有意義的空間決策<br>*(SPEC v0.1 §1.1)* | [prototype-charter](01-vision/prototype-charter.md)<br>[battle-experience](02-design/battle-experience.md) | [SPEC-battle-flow](03-spec/SPEC-battle-flow.md) R-AP-01..05 | [simulation-core](04-architecture/simulation-core.md) | — | [playtest-metrics](06-validation/playtest-metrics.md) M1, M2, M3 |
| R2 | Exposure 是一級概念，不是分析比喻<br>*(SPEC v0.1 §2.3)* | [exposure](02-design/exposure.md) | [SPEC-grid-terrain](03-spec/SPEC-grid-terrain.md) R-GRID-05..07 | [simulation-core](04-architecture/simulation-core.md) | — | M4 |
| R3 | 威脅範圍必須對玩家可見<br>*(GDD 無；SPEC v0.1 §3.3)* | [exposure](02-design/exposure.md) | [SPEC-threat-activation](03-spec/SPEC-threat-activation.md) R-THR-05 | [overview](04-architecture/overview.md)（表現層只讀 EffectLog／查詢服務） | — | — |
| R4 | 敵人在玩家進入其威脅範圍前不啟動<br>*(SPEC v0.1 §3.3)* | [battle-experience](02-design/battle-experience.md) | [SPEC-threat-activation](03-spec/SPEC-threat-activation.md) R-THR-03..04 | [simulation-core](04-architecture/simulation-core.md) | — | — |
| R5 | 勝：全滅 4 隻小耗；負：桃太郎 HP 歸零<br>*(GDD Stage 01 四；SPEC v0.1 §3.5)*<br>⚠️ 勝利條件已授權覆蓋 → [OD-30](OPEN-DECISIONS.md#od-30) | [stage-01](02-design/stage-01.md) | [SPEC-battle-flow](03-spec/SPEC-battle-flow.md) R-WIN-01..03、**R-WIN-06** | [simulation-core](04-architecture/simulation-core.md) | — | — |
| R6 | 同 seed + 同指令串 → 同世界狀態<br>*(SPEC v0.1 §6.6)* | — *(純技術需求，無 Design 層)* | — *(契約寫在 Architecture)* | [determinism](04-architecture/determinism.md) | — | [test-strategy](06-validation/test-strategy.md) A4 |
| R7 | 數值全部從資料讀，程式碼零字面值<br>*(SPEC v0.1 §6.4)* | — *(純技術需求)* | [SPEC-unit-data](03-spec/SPEC-unit-data.md) R-DATA-01..03 | [ADR-0001](07-adr/ADR-0001-core-unity-assembly-split.md) | — | A5 |
| R8 | 規則層與表現層分離，表現層只讀 EffectLog<br>*(SPEC v0.1 §6.9)* | — *(純技術需求)* | [SPEC-battle-flow](03-spec/SPEC-battle-flow.md) §Command-Effect | [ADR-0001](07-adr/ADR-0001-core-unity-assembly-split.md)<br>[ADR-0002](07-adr/ADR-0002-single-command-funnel.md) | — | A1, A7 |
| R9 | 未來可換維度／換引擎，規則層不受影響<br>*(專案負責人 2026-08-13)* | [prototype-charter](01-vision/prototype-charter.md) | — | [ADR-0001](07-adr/ADR-0001-core-unity-assembly-split.md)<br>[ADR-0005](07-adr/ADR-0005-grayblock-3d-prototype-shell.md) | — | A1 |
| R10 | 六角格要留介面但不實作<br>*(SPEC v0.1 §6.4)* | — | [SPEC-grid-terrain](03-spec/SPEC-grid-terrain.md) R-GRID-01 | [extension-points](04-architecture/extension-points.md) | — | A6 |
| R16 | 地圖提供的「走哪條路」是真的選擇，不是唯一解<br>*(專案負責人 2026-08-14)* | [stage-01](02-design/stage-01.md) | — *(這是地圖的屬性，不是規則；沒有對應的 R-xxx)* | — | — | [playtest-metrics](06-validation/playtest-metrics.md) **M6** |

### 3.2 追蹤鏈**斷掉**的需求

| # | Requirement | 斷在哪一層 | 原因 |
|---|---|---|---|
| R11 | 玩家該煩惱的是「回合結束時站在哪」<br>*(SPEC v0.1 §1.1)* | Specification | 這是體驗目標，只能靠 M4/M5 間接驗證，無法寫成單一可驗收規格。**這是正常的**，不是缺陷 |
| R12 | 敵方 AI 要能讀 Exposure 並據以站位<br>*(SPEC v0.1 §2.3)* | Specification | [ODD-01](#odd-01) — AI 規格不存在 |
| R13 | 地形讓玩家一次只拉一隻<br>*(SPEC v0.1 §3.3)* | Specification | 依賴 CONFLICT-01（移動成本）與 OD-02（阻擋地形），全部未決 |
| R14 | 防禦是有意義的第三個選項<br>*(GDD 三.1；SPEC v0.1 §8.3)* | Specification | [OD-06](OPEN-DECISIONS.md#od-06) — 效果從未定義 |
| R15 | 「AP 剩餘點數對下一回合的影響」<br>*(GDD Stage 01 五)* | Design | [CONFLICT-05](CONFLICTS.md#conflict-05) — 與 SPEC v0.1 直接矛盾 |

### 3.3 有 Architecture 但**沒有對應需求**的部分

稽核時要特別注意這一類，它們是過度設計的候選：

| Architecture 元素 | 需求來源 | 判定 |
|---|---|---|
| `ITurnOrder` 介面 | SPEC v0.1 §6.4 提及「未來可換個體行動序」 | **無 Stage 01 需求**。Stage 01 只有陣營輪流。保留理由僅為未來彈性 → 見 [extension-points](04-architecture/extension-points.md) 的 YAGNI 檢查 |
| `IGridTopology` 介面 | R10（SPEC v0.1 §6.4 明確要求留介面不實作六角格） | **有需求**，且明確禁止實作第二個拓撲 |
| `IRandomSource` 介面 | R7 + [OD-05](OPEN-DECISIONS.md#od-05)（兩個封包都要能跑） | **有需求**。這個介面正是「不自行決定 OD-05」的技術落實 |
| Undo | SPEC v0.1 §6.8「建議做」 | **無明確需求**，是 [OD-12](OPEN-DECISIONS.md#od-12) |
| Replay | SPEC v0.1 §6.8 + §7.2 自動化跑分 | **有需求**（跑分需要確定性重跑），但玩家可見的 Replay UI 無需求 |
| MemoryPack | SPEC v0.1 §6.7「框架選項（不急）」 | **無需求**。文件已明確寫「prototype 階段先手寫」 |

---

## 4. 依賴方向檢查（Circular Dependency）

文件之間允許的引用方向：

```
00-source  ←──────────────────────────────────┐
    ↑                                          │
    │ (引用來源)                                │
    │                                          │
01-vision ──→ 02-design ──→ 03-spec ──→ 04-architecture ──→ (code)
    │              │            │                │
    └──────────────┴────────────┴────────────────┘
                   ↓
              06-validation  (驗證 03-spec，可引用 04-architecture)
                   
07-adr ──→ 引用 03-spec / 04-architecture，被 04-architecture 引用回來
99-governance ──→ 引用所有層（治理層在最外圈）
OPEN-DECISIONS / CONFLICTS ──→ 被所有層引用，本身不引用規則內容
```

**規則**：
1. **下游可以引用上游，上游不可以依賴下游的內容。**
   Design 不得因為 Architecture 方便就改寫意圖。
2. `07-adr` 與 `04-architecture` 是**雙向引用**，這是刻意的：
   Architecture 說「是什麼」，ADR 說「為什麼」，兩者互指不構成循環依賴，
   因為它們不會互相定義內容。
3. `OPEN-DECISIONS` 與 `CONFLICTS` 是**葉節點**：它們被引用，但它們不定義任何規則。
   **如果哪天需要從 OPEN-DECISIONS 抄一段規則出來用，那代表那一項已經被決定了，
   應該走裁決流程搬進 Specification。**

已知的**唯一**環狀風險：
[OD-06](OPEN-DECISIONS.md#od-06)（防禦效果）的限制條件依賴
[CONFLICT-07](CONFLICTS.md#conflict-07)（傷害公式）的結論，而 CONFLICT-07 尚未解決。
已在 OD-06 內明確標注。
