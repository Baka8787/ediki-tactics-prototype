# Documentation Changelog

只記錄**文件體系**的變更。遊戲程式的變更不記在這裡。

必須記錄的事件（見 [99-governance/documentation-rules.md](99-governance/documentation-rules.md)）：
- Open Decision 被裁決
- Conflict 被解決
- 新增／移除規格條目（`R-*`）
- 新增／變更 ADR 狀態
- 既有文件（`00-source/`）更新版本
- docs 結構調整

---

## 2026-08-13 — 文件體系建立（初版）

### 新增

- 建立 `docs/` 完整結構（00-source / 01-vision / 02-design / 03-spec /
  04-architecture / 05-development / 06-validation / 07-adr / 99-governance）
- `00-source/`：封存三份既有文件
  - GDD《穢土紀企畫書 暫定》（PDF ＋ 抽出的純文字）
  - 《Stage 01 Prototype 設計與規格文檔 v0.1》
  - 《戰棋 SRPG 設計資源總整理》
  - **自此本專案內副本視為正本**，`C:\Users\USER\Downloads\` 的原檔視為過期
- `DOCUMENT-MAP.md`：Document Authority Matrix、Source of Truth Map、
  Open Documentation Decisions（ODD-01..04）、Requirements Traceability（R1..R15）
- `CONFLICTS.md`：登錄 8 項既有文件衝突（CONFLICT-01..08）
- `OPEN-DECISIONS.md`：登錄 15 項未決事項（OD-01..15），其中 7 項為 BLOCKER
- `03-spec/`：6 份可驗收規格
- `04-architecture/`：4 份架構文件
- `07-adr/`：5 份 ADR（全部 Accepted，來源為 SPEC v0.1 §6 與專案負責人指示）
- `05-development/`、`06-validation/`、`99-governance/`

### 發現（本輪分析的新增結論，非既有文件記載）

- **CONFLICT-01 加重**：SPEC v0.1 §3.2 所稱的「原始規格」地形成本表
  （道路1/碎石2/森林2/高地3）在封存的 GDD 中**不存在**。
  GDD 只寫「移動：1 AP」。該成本表目前沒有任何可查證來源。
- **CONFLICT-06 新增**：專案實際使用 Unity 6000.5.1f1，
  SPEC v0.1 §6.1 記載為 6000.0 / 6000.3，C# 語言版本的結論需重新查證。
- **CONFLICT-07 新增**：傷害公式 `ATK × 100/(100+DEF)` 在 GDD 中不存在，
  唯一書面來源是 SPEC v0.1 自己，但被標記為「確定」。
- **OD-10 新增（BLOCKER）**：敵方 AI 的決策規則從未定義，
  但三個驗收問題依賴它。既有文件未將此標為 BLOCKER。
- **OD-09 新增**：Stage 01 有「道具 2 AP」的成本與 `UseItemCommand`，
  但沒有任何道具被定義過。

### 待辦（下一次文件更新時）

- 7 個 BLOCKER 全部需要裁決 → 見 [OPEN-DECISIONS.md](OPEN-DECISIONS.md)
- CONFLICT-06 只需查證，不需討論 → 在 Unity 6000.5.1f1 實測 C# 語言版本

---

## 2026-08-13 — Stage 01 Prototype 實作（Phase 1–7）

### 裁決落地

專案負責人裁決 OD-01～06、OD-10 為 **Prototype Baseline**；程式裁決 OD-11。
全部已實作。詳見 [OPEN-DECISIONS.md](OPEN-DECISIONS.md)。

| OD | 結論 |
|---|---|
| OD-01 | 攻擊 = **4 AP**（AP 上限 8 → 可一回合攻擊兩次） |
| OD-02 | Blocking Terrain 存在；`Open/Road/Forest/Highland/Blocking` |
| OD-03 | 單位佔格互相阻擋；不做 push/pull/phasing |
| OD-04 | 地形成本模型，4-directional |
| OD-05 | Deterministic Hit ＋ `Damage = max(1, ATK - DEF)` |
| OD-06 | Guard = 3 AP，受傷 ×0.5，持續到該單位下一回合 |
| OD-10 | Goal / utility-lite AI ＋ 資料驅動 AI Profile |
| OD-11 | 資料格式採**純文字行導向 `.txt`**（非 ScriptableObject、非 JSON） |

### Conflict 狀態變更

| ID | 變更 |
|---|---|
| CONFLICT-01 | ✅ RESOLVED（OD-04 採地形成本） |
| CONFLICT-02 | ✅ RESOLVED（OD-02，採含隘口的地圖） |
| CONFLICT-03 | ⚠️ PARTIAL — 兩個數值封包作廢，回到 GDD 原始數值；小耗 `DEF 20` **仍無來源** |
| CONFLICT-04 | ✅ RESOLVED（Deterministic Hit） |
| CONFLICT-06 | ✅ RESOLVED（查證：Unity 6000.5.1f1 = **C# 9.0**，SPEC v0.1 的版本號有誤但語言結論正確） |
| CONFLICT-07 | ✅ RESOLVED（除法公式被減法公式取代；SPEC v0.1 §4 的所有推算隨之作廢） |

### 新增

- `docs/03-spec/SPEC-ai-behaviour.md` — 關閉 [ODD-01](DOCUMENT-MAP.md#odd-01)
- **OD-16（新，High）** — Exposure-1 站位造成永久僵局。實作 Phase 6 跑分時發現
- `Assets/Scripts/Core/` — `Ediki.Core`（No Engine References），15 個檔
- `Assets/Scripts/Unity/` — `Ediki.Unity` 灰盒表現層，3 個檔
- `Assets/_Project/Resources/Data/` — terrain / units / ai-profiles / stage01.encounter
- `Assets/_Project/Tests/EditMode/` — 7 個測試檔

### 實作過程中對既有規格的修正

| 規格 | 修正 | 理由 |
|---|---|---|
| **R-THR-06** | 啟動改為逐單位 `UnitActivated`，取代陣營層級 `FactionActivated` | 陣營層級啟動會讓「踩到一隻等於拉全部」，與「一次只拉一隻」的設計意圖直接矛盾 |
| **R-THR-01** | 威脅範圍的射程展開只計入**可通行格** | 牆不是可被攻擊的位置；把牆納入只會讓危險區顯示變成雜訊 |
| **R-MOVE-09** | **不實作 A\*** | 120 格網格上 flood fill 已足夠；第二套尋路會與 R-THR-02「共用同一套可達性」牴觸 |
| **R-COMBAT-11/12**、`IRandomSource` | **不建立介面** | Deterministic Hit 裁決後沒有第二個實作要跑，建立它違反 extension-points 的三項判準 |
| **ITurnOrder** | 未建立 | 依 extension-points §4 的建議 |

### 驗證結果

- `Ediki.Core` 在 **C# 9 / LangVersion 9.0** 下編譯：0 errors、0 warnings
- EditMode 規則測試：**64 / 64 通過**（架構回歸 A1–A7 ＋ 移動／戰鬥／回合／Exposure／AI）
- Stage 01 端到端無頭跑分：可完整跑完並判定勝敗，重跑雜湊一致（確定性成立）

---

## 2026-08-13 — OD-16 / MOVE 落地 ＋ Metrics 工具

### 裁決落地

| OD | 結論 | 落地位置 |
|---|---|---|
| **OD-16** | 敵人主動接敵：沒有可攻擊目標的 AI 不得永久 Idle，改為朝玩家移動；不建立回合數上限、不做 LKP/FoW/Patrol/Search/Memory | `EnemyAi.DecideNext`、[SPEC-ai-behaviour R-AI-05](03-spec/SPEC-ai-behaviour.md) |
| **OD-04**（追加） | `MOVE = 單次 Move Action 可移動的最大 Grid Cell 數`；與 AP 是兩個不同限制 | `MovementCalculator`、`BattleQueries.ThreatRange` |

**規格連帶變更**：

- **R-THR-03 被取代** —— 「敵人在玩家進入威脅範圍前不啟動」不再成立
- **R-AI-05 改寫**，新增 **R-AI-05b**：`IsActivated` 降級為**純觀測閂鎖**，不再控制行為
- **R-MOVE-04 由 `OPEN` 改為 `BASELINE`**

### 實作時的必要修正

**可達性搜尋改為對 `(格, 已用步數)` 搜尋。**
最便宜的路徑不一定是格數最少的路徑（繞過森林省 AP、但用掉更多 MOVE）。
原本只以成本排序的實作會回報「到不了」，而 `ValidatePath` 卻接受一條較短較貴的路徑
—— 顯示的移動範圍與模擬器實際接受的範圍會不一致。
已加測試 `Reachability_PrefersACheapRouteButStaysWithinTheStepCap` 鎖住。

**威脅範圍的射程展開只計可通行格**（已於前一輪加入，此輪未變）。

### 新增：Metrics / Simulation Runner

- `Assets/Scripts/Sim/`（`Ediki.Sim`，**零引擎依賴**，可在 Unity 外執行）
  - `DeterministicRandom` — 取樣層專用的 seeded xorshift32（**規則層仍然零亂數**）
  - `Strategies` — `corridor-hold` / `charge` 兩個固定策略 ＋ `LegalCommands` 列舉器
  - `SimulationRunner` — 單場與批次執行、M1–M5 蒐集、CSV 匯出
  - `MetricsBatch` — 地圖 × 策略矩陣、門檻檢查報告
- `Assets/Scripts/Editor/` — Unity 選單 `Ediki > Run Metrics`，輸出到 `<專案>/SimResults/`
- `Assets/_Project/Resources/Data/stage01-open.encounter.txt` — **M5 的地圖對照組**
  （GDD 版：無隘口，純道路＋森林）。關閉了 [ODD-03](DOCUMENT-MAP.md#odd-03) 的對照組缺口
- `Assets/_Project/Tests/EditMode/SimulationTests.cs` — 10 個測試

### 首次量測結果

完整數據見 [06-validation/playtest-metrics.md §6](06-validation/playtest-metrics.md)。

| 地圖 / 策略 | M5 勝率 | M4 平均 Exposure |
|---|---|---|
| stage01 / corridor-hold | **66%** | **0.20** |
| stage01 / charge | 7% | 0.74 |
| stage01-open / corridor-hold | 0% | 1.42 |
| stage01-open / charge | 0% | 2.35 |

**Exposure 與勝率的排序完全一致 → Q3 目前的答案是「是」。**

### 新的 Prototype Finding

| ID | 內容 | 嚴重度 |
|---|---|---|
| **OD-17** | MOVE 只限制單次動作，可串接多次 Move Action 繞過 → **Q5 仍無法回答**；且威脅範圍因此**低報**，違反完全資訊哲學 | Medium |
| **OD-18** | **AI 位置評分用曼哈頓距離，看不見牆** → 隘口地圖上雙方各自貼牆卡住，6.5% 場次跑不完。這是 OD-16 沒有完全解決僵局的真正原因 | **High** |

兩者**都未自行修改規則**，依 Prototype 原則記錄後等待裁決。

### 驗證結果

- `Ediki.Core` / `Ediki.Sim` 在 C# 9 下編譯：0 errors、0 warnings
- `Ediki.Unity` 對真實 Unity 6000.5.1f1 DLL 編譯：0 errors、0 warnings
- EditMode 測試：**81 / 81 通過**（前一輪 64 → 新增 MOVE 3 條、AI 3 條、Sim 10 條，並改寫 4 條過時測試）
- Metrics 批次：800 場，同 seed 可完整重現

---

## 2026-08-14 — 破除單調：目標 / 異質敵人 / 增援 / 反擊

觸發：專案負責人提出「目前太過單調」，並提供
《Stage 01 改版研究報告》（破除隘口車輪戰的機制對策）。

### 新增規則機制（全部資料驅動，**預設不改變既有 Stage 01**）

| 機制 | 語法 | 檔案 |
|---|---|---|
| 非全滅目標 | `objective type=rout\|reach\|survive\|defend [x= y=] turns=` | `Core/Objective.cs`、`EncounterLoader` |
| 保護目標 | `spawn ... protect=true` → `UnitState.MustSurvive` | `BattleState` |
| 增援波次 | `spawn ... turn=N` → `PendingReinforcement` | `BattleState`、`BattleSimulator` |
| 保留 AP 反擊 | `unit ... counterCost=N` | `UnitDef`、`BattleSimulator.ResolveCounterAttack` |

新 Effect：`CounterAttacked`、`UnitSpawned`。
`StateHasher` 已涵蓋新欄位（A4 golden hash 因此更新，這是它該有的行為）。

### 新增資料

- `units.txt`：`kohaku_bow`（射程 2）、`kohaku_fast`（MOVE 5）、
  `momotaro_ripo`（`counterCost=3`）、`shrine`（保護目標）
- 六張 **gym** 變體地圖（`gym-a` ~ `gym-f`），**刻意與敘事 Stage 01 分離**
  （研究報告 §四.4：用教學關的約束驗證核心機制深度是結構性錯誤）

### 新增指標

M1 加上 **distinct mixes** 與 **top-3 %** —— 直接量「單調」。

### 主要發現

- **OD-19**（新）：目標一改，龜縮的三個指紋同時消失
  （Exposure 0.20→1.7、殘 AP 32%→2%、死鎖 13→0），但立刻變成必敗
- **OD-20**（新，最重要）：**小耗 ATK 100 讓所有非龜縮玩法都不可行**。
  4 隻 × 2 次攻擊 × 50 傷 = 400 > 桃太郎 300 HP。時限給 8 或 26 回合毫無差別，
  玩家第 3 回合就死。**單調不是選擇問題，是數值只留下一種能活的打法**
- **兩個槓桿必須一起動**：`reach + ATK 70` 是目前唯一
  M3/M4/M5 同時合理的組態（82% / 0 死鎖 / expo 1.44 / 殘 AP 4% / 23 種動作組合）
- 敵人異質化**單獨做會讓龜縮更強**（66%→82%）
- 反擊只觸發 13/200 場：**未受測，不是無效**。需要一個會保留 AP 的策略才能評估

**未自行調整任何 GDD 數值。** ATK 70 只存在於跑分的臨時覆寫。

### 驗證

- Core / Sim 在 C# 9 編譯：0 errors、0 warnings
- Unity 層對真實 Unity 6000.5.1f1 DLL 編譯：0 errors、0 warnings
- EditMode 測試：**95 / 95 通過**（81 → 95，新增 14 條涵蓋目標／反擊／增援）

---

## 2026-08-14（下午）— AP 經濟改制 ／ 休息動作 ／ Spawn 威脅面

### 裁決落地（OD-21）

| 項目 | 內容 |
|---|---|
| AP 經濟 | 上限 **10**、每回合恢復 **8**，未用完的 AP **跨回合保留**，溢出部分損失 |
| 新動作 | **休息**：2 AP → 回復 10% 最大 HP → 該單位進入待機 |

**解決了兩個長期懸案**：[OD-07](OPEN-DECISIONS.md#od-07) 與
[CONFLICT-05](CONFLICTS.md#conflict-05)（GDD 說剩餘 AP 影響下回合，
SPEC v0.1 說不保留）。

**與 GDD 的刻意偏離**：GDD 三.1 寫「最大 8 點 AP」，本裁決改為 10。
理由是 8/8 讓「剩餘 AP」永遠沒有意義。

### M2 的定義因此改變

AP 可跨回合後，回合結束時剩下的 AP **是存款不是浪費**。
唯一真正的浪費是**恢復時溢出上限的部分**。
`ApReset` Effect 新增 `Gained` / `Wasted`，跑分改用「溢出 ÷ 總恢復」。

### 新增

- `RestCommand` / `UnitRested` Effect
- `UnitDef.ApRegen` / `RestApCost` / `RestHealPercent`
- 三張 **spawn 變體地圖**（`gym-s1-north` / `gym-s2-split` / `gym-s3-surround`）
  —— 同一張地圖、同樣的敵人，只移動出生點
- Unity 端 `H` 鍵 = 休息
- `ApEconomyTests.cs`（12 條）

### 主要發現

**OD-21 的效果**：動作組合種類 21 → **36**，多樣性大幅上升。
但在龜縮地圖上，休息讓龜縮**更強**（安全時免費回血），死鎖 13 → **63**。
且 AP 上限 10 讓 `reach` 目標被輕易破解（1.7 回合通關，最常見動作 `Movex3`），
**[OD-17](OPEN-DECISIONS.md#od-17)（MOVE 串接）從 Medium 升為必須處理**。

**OD-22（新）**：**Spawn 配置是目前最強的威脅面槓桿，而且不需要任何規則改動。**

| 變體（ATK 70） | 勝率 | 未解 | 回合 | Exposure | 種類（top3） |
|---|---|---|---|---|---|
| S1 全部在隘口北側 | 61% | 72 | 24.2 | 0.08 | 36（76%） |
| **S2 分置兩側** | **84%** | **4** | **7.0** | **0.80** | **42（54%）** |
| S3 散佈玩家半場 | 75% | 37 | 15.6 | 0.15 | 40（70%） |

**S2 是整個專案至今量到最好的一格** —— M3 首次落在 6–10 區間內，
動作多樣性最高，死鎖幾乎消失。原因很單純：隘口不再把兩邊分開。

### 驗證

- Core / Sim 在 C# 9 編譯：0 errors、0 warnings
- Unity 層對真實 Unity 6000.5.1f1 DLL：0 errors、0 warnings
- EditMode 測試：**107 / 107 通過**（95 → 107）

---

## 2026-08-14（晚）— Prototype 目的重述 ＋ 三項底層修正

### 目的重述（專案負責人）

> **Prototype 要證明的是：只靠底層機制就能產生有意義的決策。**

底層機制 = 移動 / 攻擊 / 防禦 / 休息 / AP / 地形 / 佔格。
**不靠**技能道具裝備，也**不靠**非全滅目標／增援／反擊這類內容層。

**這個重述讓先前的結論作廢一半**：用內容層壓低單調度是有效的，
但那證明的是「加東西有用」，不是「底層機制夠好」。
本輪因此固定 **rout 目標 / 同質敵人 / 無反擊 / 無增援**，只調底層。

同時指示：地圖可以大一點、玩家 MOVE 固定 4、數值保持合理範圍即可。

### 三項底層修正

| ID | 修正 |
|---|---|
| **OD-17** ✅ | MOVE 改為**每回合總格數上限**，串接不能繞過（新增 `UnitState.MoveUsedThisTurn`） |
| **OD-18** ✅ | AI 與策略改用**路徑距離**（新增 `MovementCalculator.TerrainDistanceField`），曼哈頓距離看不見牆 |
| **OD-20** ✅ | **維持 GDD 的 ATK 100** —— 先前「致命度太高」的結論是前兩個 bug 的假象 |

### 新增

- `gym-big-north.encounter.txt` / `gym-big-split.encounter.txt`
  —— 18×12，**三條路線**（西 1 寬 / 中 2 寬 / 東 1 寬），讓「走哪條」成為決策
- **OD-23**：地圖尺寸與 spawn 結構

### 主要發現

**修完 OD-17 / OD-18 之後重跑致命度掃描，結論完全反轉：**

| 小耗 ATK | 守勢 | 衝鋒 | 差距 |
|---|---|---|---|
| 70 | 99% | 98% | 1 |
| **100（GDD 原值）** | **50%** | **2%** | **48** |

ATK 70 讓衝鋒也能贏 —— **站位變得不重要，那才是真正的失敗**。
GDD 的 ATK 100 反而產生最大技術落差。**數值一直是對的，錯的是機制實作。**

**四張地圖，同一套規則與數值（rout / 同質 / ATK 100）：**

| 地圖 | 守勢 | 衝鋒 | 差距 | 回合 | AP浪費 | 種類(top3) |
|---|---|---|---|---|---|---|
| stage01（12×10 單隘口） | 1% | 0% | 1 | 6.1 | 17% | 38(57%) |
| gym-s2-split | 6% | 1% | 5 | 5.5 | 20% | 39(54%) |
| gym-big-north（三路線，敵全北） | 7% | 8% | −1 | 6.5 | 15% | 40(46%) |
| **gym-big-split（三路線，敵兩側）** | **50%** | **2%** | **48** | **7.3** | **13%** | **42(46%)** |

**`gym-big-split` 是至今唯一一格 M1–M5 全部合格的組態。**
未解場次全部歸零（先前 63–72 / 200）。

### 驗證

- Core / Sim（C# 9）：0 errors、0 warnings
- Unity 層對真實 6000.5.1f1 DLL：0 errors、0 warnings
- EditMode 測試：**109 / 109 通過**（新增／改寫 MOVE 每回合上限的行為測試）
- 同 seed 重現：STABLE

---

## 2026-08-14（夜）— 進入真人 Playtest

專案負責人裁示：接受 OD-17 / OD-18 / ATK 100；接受 `gym-big-split`
作為 Mechanics Validation Map 並設為開場預設；**暫停新增內容層**；先做真人 playtest。
OD-23 暫不最終裁決，OD-19 繼續延後。

### 變更（全部是操作與觀察，**沒有任何 gameplay 規則改動**）

| 項目 | 內容 |
|---|---|
| 開場地圖 | `PrototypeBootstrap.EncounterName` → `gym-big-split.encounter` |
| **HUD 顯示 MOVE 剩餘** | MOVE 已改成每回合預算（OD-17），但 HUD 沒跟上 —— 玩家會不知道為何走不動 |
| HUD 顯示目標與剩餘敵人 | |
| 地圖切換 | `1`–`4` 在四張地圖間切換，同場次可直接對照 |
| 相機 | 依地圖長寬與畫面比例自動框住，18×12 不會被裁切 |
| `BattleView.Build` 可重入 | 重建前先清掉自己建的物件，換圖時不會同幀存在兩個 view |

> **`gym-big-split` 設為預設 ≠ 裁定 Stage 01 就是這張圖。**
> OD-23 仍未最終裁決；這只是把驗證用的地圖放到最方便試玩的位置。

### 新增

- `docs/06-validation/playtest-guide.md` — 操作、四張地圖的差異、
  **要觀察什麼（Q2 / Q4 ＋ 新增的 Q6「每回合有沒有真的在想」）**、記錄範本、
  以及「不用回報的已知現象」清單

### 驗證

- Unity 層對真實 6000.5.1f1 DLL：0 errors、0 warnings
- EditMode：**109 / 109 通過**
- 四張可切換地圖全部載入通過，`gym-big-split` 三條路線的 exposure 為
  西 2 ／ 中 3 ／ 東 2，玩家起點在第一回合不被任何敵人威脅

---

## 2026-08-14（深夜）— 首次真人 Playtest 回饋與量測

### 回饋原文

> 感覺及距離短，有時候要先 move 1 格再攻擊。
> 衝鋒打完兩隻敵人差不多也靠近了，可選擇拉打或沖 1 格打對面。
> 尚未無傷全滅對面過。

### 處理方式：拆成三件不同性質的事

| 回饋 | 性質 | 處理 |
|---|---|---|
| 「要先 move 1 格再攻擊」 | **一半是操作、一半是設計** | 操作面直接修；設計面量測後登錄 [OD-24](OPEN-DECISIONS.md#od-24) |
| 「可選擇拉打或衝一格」 | 決策開始出現 | 用數據確認是否加深 |
| 污染地形／淨化 | **內容層，與 charter 非目標衝突** | 不實作，登錄 [OD-25](OPEN-DECISIONS.md#od-25) 並附成本分析 |

### 操作修正（純 UI，**不改任何規則**）

點擊射程外的敵人，現在會自動走到**威脅最少的**可攻擊格再攻擊
（不是最近的 —— 這是一個講站位的遊戲，自動選一個危險的格子等於替玩家做了真正的決策）。
會在 log 標明「(auto) step to (x,y) — threatened by N — then attack」。

### 量測：射程與敵人異質化（300 場/格）

| 變體 | 守勢 | 衝鋒 | 差距 | 回合 | Exposure | AP 溢出 | 種類(top3) |
|---|---|---|---|---|---|---|---|
| 基準 雙方射程 1 | 50% | 2% | 48 | 7.3 | 0.54 | 13% | 42(46%) |
| **玩家 2 / 敵人 1** | **82%** | 6% | **76** | 6.8 | **0.36** | **9%** | 39(48%) |
| 雙方射程 2 | 11% | 1% | 10 | 5.8 | 0.88 | 13% | 33(47%) |
| 敵人異質（同數值） | 24% | 1% | 23 | 6.4 | 0.68 | 13% | 41(**33%**) |
| 異質 ＋ 玩家 2 | 73% | 5% | 68 | 6.9 | 0.44 | 13% | 41(48%) |

**發現並修正了自己造成的統計假象**：第一版異質敵人（`kohaku_bow` / `kohaku_fast`）
HP 與 ATK 都比 `kohaku` 低，於是「異質化」看起來獎勵衝鋒（charge 54%）。
改成只差射程／移動、其餘數值相同之後，衝鋒掉回 1% ——
**先前的結論是數值artefact，不是異質化的性質。**

### 結論

1. **不對稱射程（玩家 2 / 敵人 1）讓站位更重要，不是更不重要** —— 差距 48 → **76**
2. **回合的性質改變**：守勢最常見組合從 `Move+Guard+Wait` 變成 `Move+Attackx2+Wait`，
   正是 playtest 描述的「拉打」
3. **雙方都給射程 2 是陷阱** —— 差距崩到 10。重點是**不對稱**，不是射程數字
4. 異質敵人（數值對等）給出最低的 top-3 集中度（33%），是最不單調的一組

### 新增

- `units.txt`：`momotaro_r2` / `kohaku_r2` / `kohaku_bowfair` / `kohaku_fastfair`
- 六張射程與異質化變體地圖（`gym-r-*`）
- **OD-24**（射程不對稱）、**OD-25**（污染地形，含成本分析與 charter 衝突說明）

### 驗證

- Unity 層對真實 6000.5.1f1 DLL：0 errors、0 warnings
- EditMode：**109 / 109 通過**（本輪無規則改動，故無測試變更）

---

## 2026-08-14（交接）— 下一會話任務移交

context 用盡，任務移交至 [HANDOFF-NEXT-SESSION.md](HANDOFF-NEXT-SESSION.md)。

### 本輪未實作，僅記錄

| 任務 | 內容 |
|---|---|
| A | 加入**正守**（GDD 數值：HP 435 / ATK 33 / DEF 70 / MOVE 3；射程 1、AP 10/8）。**只需改 `units.txt` 一行，不需程式** |
| B | **敵方遠程單位取代污染地形** —— 專案負責人改變方向，理由是不脫離底層邏輯。`kohaku_bowfair` 已存在 |
| C | 更大的地圖以容納更多單位 |
| D | **新增 M6 路線利用率** —— 記錄每場首次穿越 `y=5` 那道牆時的 x |

### 方法論結論

專案負責人問：「先定義本關要玩家做什麼決策，再反推地圖，這在加內容層前做得到嗎？」

**做得到。** `gym-big-split` 隱含的決策已經很具體
——「走哪條路，何時停下來打」，完全不需要內容層。
**M6 正是這個決策的驗收條件**，因此建議先做 M6、用結果反推地圖，
再加正守與遠程敵人。一次只動一個變因。

### 仍等裁決

[OD-24](OPEN-DECISIONS.md#od-24)（射程不對稱）已量測完畢但**尚未裁決**。
下一輪所有任務都可在射程未定的情況下進行。

---

## 2026-08-14（續）— M6 路線利用率上線、地圖改版、正守與遠程敵人

執行 [HANDOFF-NEXT-SESSION.md](HANDOFF-NEXT-SESSION.md) 的任務 A–D，
依該文件 §3 的建議順序（先 M6 → 用結果反推地圖 → 再加單位）。

### 新增規格／指標

- **M6（路線利用率）實作完成**，定義與實作細節寫入
  [06-validation/playtest-metrics.md §8](06-validation/playtest-metrics.md)，
  首次量測結果寫入 §9
- **[OD-26](OPEN-DECISIONS.md#od-26) 新增**：M6 的判讀門檻沒有來源；
  固定策略只能證偽不能確認；兩個單位讓 M1 的種類數不可比

### 量測發現（新結論）

- **`gym-big-split` 的「三條路線」是兩條路線加一個 4 格死巷。**
  中路缺口 x=7,8 只打開 4 格、可達北側敵人 0 隻。
  `corridor-hold` 在 `gym-big-north` 上 93% 的「穿越」其實是躲進那個口袋。
  → [OD-23](OPEN-DECISIONS.md#od-23) 的描述已加註修正
- **rout 目標下 175 / 200 場從未踏上分隔牆那一列。**
  根因是 [OD-16](OPEN-DECISIONS.md#od-16)（敵人一定主動接敵），不是地圖
- **正守把 M5 的策略分離度從 23 個百分點壓到 3 個**
  → 記入 [SPEC-unit-data §3.2b](03-spec/SPEC-unit-data.md)
- **遠程敵人是目前唯一同時保住 M5 分離度與 M6 分布的槓桿**
  （44% vs 25%，路線 26 / 38 / 35）

### 修正

- **`CorridorHoldStrategy` 的目標錨點項改用路徑距離**
  （原本用曼哈頓，與 [OD-18](OPEN-DECISIONS.md#od-18) 的裁決不一致）。
  只影響 reach / defend 地圖；**[playtest-metrics §7.2](06-validation/playtest-metrics.md)
  的 `gym-b` / `gym-c` / `gym-d` 三行需要重量**

### 新增資料（不涉及規則）

- `units.txt`：`zhengshou`（正守，GDD RuleLock 數值）
- `gym-lanes.encounter.txt`（24×16，三條真路線，分隔牆固定在 y=8）
- `gym-lanes-pair.encounter.txt`（＋正守）
- `gym-lanes-bow.encounter.txt`（＋兩隻射程 2 的小耗）
- `gym-lanes-reach.encounter.txt`（同圖，只改目標，用來分離「地圖」與「目標」）

### 未做（刻意）

- **沒有裁決 [OD-24](OPEN-DECISIONS.md#od-24)（射程）** —— 仍等專案負責人
- **沒有實作污染系統**（[OD-25](OPEN-DECISIONS.md#od-25)）
- **沒有動 `Ediki.Core` 的任何規則** —— A4 golden hash 全部維持原值
- **沒有更改開場預設地圖**：`gym-big-split` 仍是按 Play 時載入的圖。
  `gym-lanes` 系列掛在數字鍵 2 / 3 / 4，理由見 HANDOFF

---

## 2026-08-14（深夜）— 指標框架落地：EP / M7 / M1b、tpa-order 策略、四座實驗場

依兩份研究報告
（[Metrics Framework](NOTE-2026-08-14-戰棋數值設計-Metrics-Framework-研究報告.md)、
[敵人刀數與目標優先級](NOTE-2026-08-14-敵人刀數與目標優先級數值模型.md)）
訂製實驗場地並擴充關卡指標。**兩份研究本身不裁決任何 OD，本輪也沒有。**

### 新增指標

- **EP（Encounter Profile）— 衍生指標**，`Assets/Scripts/Sim/EncounterProfile.cs`。
  不跑戰鬥，只用 `units.txt` ＋ 地圖算出 `刀數 / 每回合傷害 / TPA / residue /
  lethal exposure / 接近成本`，印在每張圖跑分報告的最前面
- **M7 — 接觸回合（release time）**。每隻敵人第一次能打到玩家的回合、
  接觸回合數、回合 1 接觸數。對應 Framework §15 的 H1
- **M1b — 每單位動作組合**。M1 的組合鍵會被單位數灌水（[OD-26](OPEN-DECISIONS.md#od-26)），
  M1b 是唯一能跨隊伍人數比較的版本。**M1 保留未動**
- [playtest-metrics §2](06-validation/playtest-metrics.md) 的指標總表改寫為
  「結果指標 / 衍生指標」兩類，並加註「先看衍生」

### 新增量測儀器

- **`ThreatPriorityStrategy`（`tpa-order`）** —— 繼承 `CorridorHoldStrategy`，
  **只覆寫目標選擇**（Smith's Rule / WSPT）。
  在此之前本專案兩個策略都不做目標選擇，**過去所有跑分都沒有測過目標優先級**

### 新增實驗場地（16×9 全開放，一次只差一個變因）

| 檔案 | 變因 |
|---|---|
| `gym-arena-contact.encounter.txt` | 基準：4 隻小耗全部開場就在打擊範圍內 |
| `gym-arena-stagger.encounter.txt` | 只改到達時間（3/6/9/12 格） |
| `gym-arena-residue.encounter.txt` | 只改整除性（3 隻 1 刀 ＋ 1 隻 3 刀） |
| `gym-arena-backline.encounter.txt` | 只改菁英位置（移到 12 格外） |

`units.txt` 新增 gym 專用的 `kohaku_3hit`（HP 120）與 `kohaku_1hit`（HP 40 / ATK 60）。

### 量測發現

- **選對打誰最多值 5 個百分點；選對站哪最多值 49 個百分點。**
  同質敵人下目標優先級的價值是 **0**，與刀數模型 §6 / §8.3 的窮舉結論一致
- **Framework 的 H1 被證偽**：M5 的落差不是由「回合 1 接觸數」解釋。
  但活下來一條更強的結論：**所有守勢大勝的場次，回合 1 接觸數都是 0**
- **`arena-stagger` 打翻了地圖檔裡自己寫下的預測**：分批抵達時衝鋒反贏 17 個百分點。
  M7 解釋了原因（守勢的接觸回合是衝鋒的 10 倍），**而 M4 在這裡是誤導的**
- EP 自動算出的 `residue 0 / lethal exposure 3` 與刀數模型 §0③、
  [OD-20](OPEN-DECISIONS.md#od-20) 的手算完全一致

### 未做（刻意）

- **沒有動 `Ediki.Core`**，A4 golden hash 維持原值
- **沒有改小耗數值** —— 刀數模型 §11 建議 1「不要動小耗的 HP」，本輪遵守。
  新增的 `kohaku_1hit` / `kohaku_3hit` 是 gym 專用的**新單位**，不是調整
- 沒有實作威脅衰減（門檻式弱化）—— 那是規則改動，依刀數模型 §11 註記應先登錄 OD

---

## 2026-08-15 — 六個變因的實驗場：射程／刀數／減傷公式／怪的性質／地塊成本

**專案負責人指示**：把射程、擊殺刀數、減傷公式、各種性質的怪、地塊移動 AP
全部做成實驗。

### 新增規格條目

- **R-COMBAT-24**：傷害公式改為**每場戰鬥的資料**（`rules damage=`），
  未指定 = [OD-05](OPEN-DECISIONS.md#od-05) 的減法基線
- **R-COMBAT-25**：百分比減傷模式 `ATK × (100 − min(DEF,90))%`，下限 1
- **R-DATA-08**：`atkGrowth` 是**回合數的純函數**，不得存成單位狀態
- `SPEC-unit-data` schema 新增 `atkGrowth` 欄位

### 新增未決事項

- **[OD-27](OPEN-DECISIONS.md#od-27)** 傷害公式：減法 vs 百分比減傷
- **[OD-28](OPEN-DECISIONS.md#od-28)** 成長型敵人要不要進 Stage 01 規則

### 規則層擴充（**預設行為逐位元不變，A4 golden hash 未動**）

| 擴充 | 做法 | 為什麼安全 |
|---|---|---|
| 傷害公式 | `RuleSet` 走 encounter 資料，與 `BattleMap`／`ObjectiveDef` 同級的不可變設定 | 不是全域可變狀態；不進狀態雜湊（它是設定不是狀態） |
| 成長型敵人 | `UnitDef.AtkGrowth` ＋ `AtkOnRound(turnIndex)` | **回合數的純函數**，不進 `Clone`、不進雜湊、`0` 完全等價於原行為 |

### 新增資料

- 單位原型：`kohaku_sniper`（ATK 140／射程 3／40 HP）、
  `kohaku_growth`（每回合 +20 ATK）、`kohaku_tank`（DEF 45）
- 地形：`Mire`（成本 3）
- 場地：`gym-arena-terrain`、`gym-arena-growth`、`gym-arena-sniper`、
  `gym-rules-subtractive` ＋ `gym-rules-percent`（只差一行 `rules`）

### 量測發現（完整見 [playtest-metrics §11](06-validation/playtest-metrics.md)）

- **射程差 +1 值 51 個百分點**（19% → 70%）。**其他所有旋鈕加起來都沒有它大**
- **刀數 2 → 3 是懸崖不是斜坡**：三張性質完全不同的地圖上都掉到 ≤1%
- **減傷公式改變的不是難度，是「誰能參與」**：
  正守打 DEF 45 的目標，減法下要 **120 刀**（保底 1 傷），百分比下 7 刀
- **成長型是唯一不需要回合上限的反龜縮機制**：g=20 把守勢從 90% 打到 13%
- **地塊成本非單調**：成本 2 → 3 讓玩家**變好過**（19% → 34%），
  因為過了「一回合 AP 預算」的門檻之後，地形擋敵人比擋玩家更多
- **EP 對成長型完全失效**（評為 TPA 0 = 全場最低威脅）。工具已加警告自陳

### 未做（刻意）

- **沒有改變任何基線**：減法仍是預設，`atkGrowth` 預設 0
- 沒有裁決 OD-27 / OD-28 / OD-24 / OD-26
- 掃描用的資料變體（射程 3×3、HP 五檔、成長率六檔、地形四檔）
  **在 scratchpad 產生，沒有寫進專案**

---

## 2026-08-15（續）— 補刀策略 ＋ 實驗程序 E01 / E06 / E09

依《戰棋數值設計與決策分析筆記 v0.1》的實驗程序執行。

### 新增量測儀器

- **`ResidueAwareStrategy`（`residue-aware`）** —— 繼承 `tpa-order`，
  只改「目標必須是本回合行動收得掉的」。
  規則：`H ≤ A` 收它；`H > A 且 H > N` 打它（下回合也收不掉）；否則這一刀是零頭
- `ThreatPriorityStrategy` 的刀數／TPA 計算抽成 `protected static`，兩個策略共用

### 新增場地

- `gym-arena-conflict` —— 近處的成長型（TPA 0）＋ 遠處的普通敵人（TPA 50）。
  筆記的 Experiment 09

### 量測發現

- **零頭補刀的價值 ≈ 1 點殘餘 HP，不是紙上模型預測的 12%。**
  1000 場：`tpa-order` 98% / 133 HP vs `residue-aware` 97% / 134 HP。
  沒有零頭的地圖上兩者**逐位元相同**（儀器正確性的證據）
- **E01 HP Breakpoint**：HP 40 → 41 讓勝率從 **86% 掉到 19%**；
  HP 41 → 80 完全沒有差別。**設計師調 HP，玩家感受到的是刀數**
- **E06 AP Breakpoint 不在攻擊成本的倍數上**：最大的一跳在 5 → 7 AP，
  那裡攻擊次數沒變（都是 1 次）。打開新 Action Set 的是**移動預算**
- **E09 局部最優 vs 全局最優**：三個按威脅排序的策略全部輸給 `charge` **32 個百分點**，
  因為衝鋒剛好先殺了成長型。**沒有時間維度的啟發式會輸給沒在思考的啟發式**
- **noise 0 時 `corridor-hold` 會退到安全格拖到回合上限**（30 回合 / Exposure 0.10）
  —— 確定性設定不是紙上模型的設定，是退化的龜縮

### 下一輪最值得投資的單一項目

**局面求解器**（純 Sim 層，不動規則）。E05 Horizon / E07 NOAR / E08 Regret
三個實驗全部卡在同一件事：「這個局面的最佳解是什麼」。
一旦有了它，NOAR、Regret、Priority Reversal Point 一起解鎖。

---

## 2026-08-15（深夜）— 局面求解器：E05 / E07 / E08 / E10 全部解鎖

### 新增工具

- **`Assets/Scripts/Sim/PositionSolver.cs`** —— 列舉某單位的每一個合法指令，
  各自推演到 horizon 並打分。純 `Ediki.Sim`，**不動規則、不動資料**
- 目標函數 = horizon 內承傷 ＋ horizon 結束時殘存威脅一回合的量。
  **第二項是必要的**：純承傷是退化目標（走開就是滿分）
- 對稱合併、NOAR 容忍帶有絕對下限、Regret 用 HP 預算正規化、
  玩家死亡當 Hard Constraint 不打分 —— 四項都直接對應 Metrics Framework 的失效案例

### 量測發現

- **E05 Horizon**：同一個局面，horizon 1 時 **spread 1.00 / NOAR 100%（沒有決策）**，
  horizon 8 時 spread **5.49**，而且**最佳解換成走向成長型敵人**。
  **決策不是局面的性質，是「看多遠」的性質**
- **E07/E08 四象限**：十張圖裡只有 `arena-conflict`（NOAR 55% / spread 1.83）與
  **`gym-big-split`（57% / 1.77）**落在「真決策」象限 ——
  而 `gym-big-split` 正是 M5 落差最大的那張，**兩個獨立指標指向同一張圖**
- **`gym-lanes` 落在「無關緊要」象限**（NOAR 80% / spread 1.20）。
  **路線多樣 ≠ 決策密度高**
- **`arena-contact` 的開場已經輸了**：5 個相異行動全部通向死亡
- **對稱合併把行動數壓掉 60–80%**（26→5、30→9），Framework 反例 4 實測成立
- **E10 兩個版本都失敗**：六因子 0% 勝率、削到四因子 2%。
  **成長型在組合關卡裡直接支配其他所有因素**
- **意外收穫：大地圖的開場回合是「空決策」**（spread 1.00）——
  **有效決策長度遠短於戰鬥長度**，這是關卡尺寸的設計問題

### 新增場地

- `gym-integrated`（E10 整合關卡，已依實測從六隻削到四隻）

### 下一輪實驗（本輪自行設計，見 [playtest-metrics §14](06-validation/playtest-metrics.md)）

| # | 問題 | 成本 |
|---|---|---|
| **F04** | 換一個目標函數，最佳解會不會換？（**建議最先做**，它檢驗工具本身） | 低 |
| F01 | 一場戰鬥裡真正有決策的是哪幾回合？ | 低 |
| F02 | 反擊的價值是「多打幾下」還是「改變整除性」？（四因子筆記 H7） | 零程式 |
| F03 | 玩家側缺少位移是決策密度低的主因嗎？（四因子筆記 H6） | 低 |

---

## 2026-08-15（續二）— 污染／淨化、固守敵人、分數移動成本

專案負責人指示補上缺口盤點的機制。**三個規則層擴充，A4 golden hash 全程未動。**

### 新增規格條目

- **R-MOVE-12**：地形成本可為分數（資料寫 `cost=1.5`），**累積後只進位一次**；
  規則層存百分之一 AP 的整數，不出現浮點數
- **R-TERRAIN-01**：污染度是**每格的可變狀態**，存在 `BattleState`（非 `BattleMap`），
  無污染時陣列為 null → 沒有污染的戰鬥逐位元等同於此機制存在之前
- **R-SKILL-01**：`PurifyCommand`（仙照・淺淨）4 AP、曼哈頓半徑 2（13 格）、污染度 −1
- **R-ENEMY-01**：`contaminates` 每回合結束在周圍加污染（晦氣【穢氣滲流】）

### 新增未決事項

- **[OD-29](OPEN-DECISIONS.md#od-29)** 污染的效果強度與淨化的定價 —— **High**

### 新增單位（`units.txt`）

| 單位 | 來源 |
|---|---|
| `huiqi` 晦氣 | **GDD 逐字**（HP 95 / ATK 85 / DEF 20），MOVE 2 依專案負責人 |
| `momotaro_pure` | 桃太郎 ＋ 仙照・淺淨（GDD 4 AP / 半徑 2） |
| `test_turret` / `test_blocker` / `test_tax` | **無 GDD 來源、刻意不命名**，各測一個結構性質 |

### 新增策略

- **`PurifyingHoldStrategy`（`purify-hold`）** —— 沒有它，淨化就是「有機制沒使用者」，
  跑分會回報它沒作用，而那會是假結論（反擊機制吃過這個虧）

### 量測發現

- **固守敵人（`move=0`）給出全專案最大的策略落差：80% vs 15%＝65 點**，
  超過先前紀錄的 `gym-big-split`（49 點）。**而且它零成本**
- **淨化是負收益**：會淨化的策略 **0%**，不淨化的 31%。
  4 AP 換每擊少吃 10 點，要擋 4 次以上才回本，而戰鬥只有 5 回合
- **淨化是「退款型」動作** —— 與正守、hold 目標同類。
  **這是第三次量到「退款型改動降低決策密度」**
- 分數移動成本：森林 1.5 → 25%、2 → 19%

### 抓到並修掉的自製 bug

`TerrainDistanceField` 改存百分之一 AP 後，`DistanceIn` 的回傳值變成 100 倍，
**悄悄把每個策略的距離權重放大了 100 倍**。症狀：同一張圖勝率 19% → 9%。
修法是讓 `DistanceIn` 回傳進位後的 AP。修好後 `gym-lanes` 67/44、
`gym-big-split` 52/3 全部回到原值。

> **換單位比換公式危險：公式改錯會編譯失敗，單位改錯只會讓數字變得不對。**

---

## 2026-08-15（續三）— 斬首目標：目標優先級的價格是勝利條件決定的

觸發：專案負責人裁決「**GDD 明訂的 Stage 01 勝利條件覆蓋掉先不管**」，
解除了 [HANDOFF §3 缺口表](HANDOFF-NEXT-SESSION.md) 裡 `kill-specific` 那一格
唯一標「要你裁決」的封鎖。

### Open Decision

- **新增 [OD-30](OPEN-DECISIONS.md#od-30)**（斬首目標的範圍）。
  其中「可以覆蓋 GDD 勝利條件」與「可以解除 charter 的固定 rout」
  **已由本次裁決結案**；「Stage 01 本關要不要採用」仍 OPEN。

### 規格

- **新增 [R-WIN-06](03-spec/SPEC-battle-flow.md)**：勝利條件由 encounter 的
  `objective` 指定，可覆蓋 R-WIN-01。
- **R-WIN-01 降級為「預設值」**，Source 欄的 GDD 依據只描述預設值。
- R-WIN-04（規則層無回合上限）**不受影響** —— `kill` 與 `rout` 同樣預設無時限。

### 受影響的既有文件

| 文件 | 改了什麼 |
|---|---|
| [02-design/stage-01.md](02-design/stage-01.md) | §2「勝利條件＝全滅」標為已授權覆蓋 |
| [01-vision/prototype-charter.md](01-vision/prototype-charter.md) | §2「固定 rout 目標」標為部分解除，並寫明界線 |

### 新增規則機制

| 機制 | 語法 | 檔案 |
|---|---|---|
| 斬首目標 | `objective type=kill` | `Core/Objective.cs` |
| 目標標記 | `spawn ... target=true` → `UnitState.IsObjectiveTarget` | `EncounterLoader`、`BattleState` |

四條新的資料驗證：`kill` 沒有標記、標記在玩家身上、標記在增援上、
以及**標記但目標不是 `kill`**（那會是只影響雜湊的無聲資料錯誤）一律拒絕。

> **A4 golden hash 未動。** `IsObjectiveTarget` 只在確實有標記時才折進雜湊，
> 沿用污染欄位的同一套做法，所以沒有標記的場地逐位元不變。

### 新增儀器與資料

- **`DecapitateStrategy`（`decapitate`）**：繼承 `corridor-hold`，**只改目標選擇**。
  在沒有標記的地圖上與 `corridor-hold` 逐位元相同（有測試）。
- `units.txt`：`test_boss4`（160 HP）、`test_boss6`（240 HP）——
  kohaku 但只有 HP 不同，構成刀數 2/3/4/6 的階梯（刀數 3 沿用既有的 `kohaku_3hit`）。
- 12 張場地：`gym-big-split` 與 `gym-lanes` 的斬首變體，
  **每張 `-kill` 都配一張同組成的 rout 對照**，否則分不清是組成還是目標造成的。

### 量測發現（完整數據 [playtest-metrics §16](06-validation/playtest-metrics.md)）

- **目標優先級的價格從 0–5 個百分點跳到 25**，而本輪一行都沒有動敵人組成的多樣性。
  **它是勝利條件的函數，不是敵人組成的函數。**
- **只看 `corridor-hold` 完全看不到**：刀數 4 那一列 rout 9% → kill 11%，
  同一列 `decapitate` 是 31%。
- **TPA 在斬首目標下系統性錯誤**，且越硬越錯（被標記者永遠 TPA 最低）。
  這是 EP 的第二個已知失效模式，與成長型同一類。
- **刀數懸崖沒有移動**：斬首把 74/41/31/1% 對上 rout 的 52/14/9/1% ——
  整條抬高約 20 點，形狀不變。
- **Exposure 命題沒有被抵消**：M6 路線 27/43/30 → 29/44/27，M4 0.32 → 0.34。
  這是它與 `reach` 的關鍵差別 —— `reach` 指定玩家站哪，`kill` 只指定誰必須死。

### 被推翻的預測

[目標類型筆記 §3](NOTE-2026-08-15-目標類型如何改變決策.md) 預測弱目標（2 刀）
是結構性糟糕的唯一解。實測它是**目標優先級開始值錢的第一格**（+12），
行動組合多樣性也沒有塌。原因是模型假設玩家會走過去，
而 [OD-16](OPEN-DECISIONS.md#od-16) 讓敵人全部自己壓上來 ——
**紙上算的是「要不要走過去」，引擎裡發生的是「要不要忍住」。**

### 表現層

- 單位列表顯示 `«TARGET»`，否則斬首目標對真人不可玩（規則層照樣解得出來）。
- **地圖選單鍵位改為從清單生成。** 原本是寫死字串，且已經過期四格
  （寫著「2 = big-north」，實際是 `gym-lanes` 已經兩輪了）。
- 新增第 10 格（`0` 鍵）= `gym-big-split-boss4-kill`，
  唯一一張「打誰」對真人有意義的地圖。

### 驗證

- EditMode **171 / 171 通過**（新增 10 條斬首目標、3 條 `decapitate`）
- Core / Sim / Unity / Editor 四層**零警告**編譯
- **A4 golden hash 三條全部原值通過** —— 本輪未改變任何既有模擬結果

---

## 2026-08-15（續四）— 實驗流程手冊

觸發：專案負責人要求「目前製作實驗的流程，包括要看哪些指標、
關卡地形配置、敵我方數量和數值配置」。

### 新增

- **[05-development/experiment-playbook.md](05-development/experiment-playbook.md)**
  —— 一次實驗的完整流程。**歸納既有做法，不新增任何規定**：
  每條紀律都註明是哪一輪、因為什麼數字學到的。
  - 六步流程、對照組怎麼設計（一次只動一個變因）
  - **指標的閱讀順序**：EP → M7/M5 → M4/M6 → M1/M2/M3 → 求解器
  - 地形全表與四條幾何經驗、`units.txt` schema 與整份 roster 的導出數值
  - encounter 完整語法、跑分參數的意義
  - §9「你沒問但一定會踩的」：八項全部有出處
- [README.md](README.md) 的閱讀順序加入本檔（企劃第 9 項）

### 順手記下的既有缺陷（未修）

- **`spawn ... group=` 完全沒有作用。** 解析進 `SpawnDef.Group` 之後
  沒有任何程式讀它。`stage01.encounter` 的 `group=north` / `group=east`
  看起來像 XCOM pod，但啟動是逐單位的（OD-10 / R-THR-03）。
  已寫進 playbook §6 的警告，**程式與資料都未改動**。

---

## 2026-08-15（續五）— 控制技能、四人隊、候選開場關

觸發：專案負責人提供**真人打法**（本專案一直缺的那筆資料）並指示解禁內容層。

> 「移動到小怪 4 格附近等他靠近，接著下回合兩刀劈死。保留跟不保留都差不多。」

### Open Decision

- **新增 [OD-31](OPEN-DECISIONS.md#od-31)**（內容層解禁）。
  「技能可以做」「上限 4 人」「AP 自訂」**已由本次指示結案**；
  **charter 主命題要不要改寫仍 OPEN**，且列為 Highest。

### 新增規則機制（全部資料驅動，預設關閉）

| 機制 | 語法 | 檔案 |
|---|---|---|
| 【不動明王】嘲諷 | `unit ... tauntCost= tauntRadius=` | `Commands`、`BattleSimulator`、`EnemyAi` |
| 【遲滯】 | `unit ... slowCost= slowRange=` | 同上 ＋ `MovementCalculator.StepCostHundredths` |
| 擊退 | `unit ... pushCost= pushRange=` / `immuneToPush=` | `Commands`、`BattleSimulator` |

新 Effect：`TauntApplied`、`SlowApplied`、`UnitPushed`。

> **狀態存成「回合戳記」而不是倒數計時。**
> 倒數必須在某處遞減，而每個位置都會弄壞其中一個技能：
> 在 phase 開始遞減會讓遲滯在目標移動前就失效，
> 在 phase 結束遞減會讓嘲諷在敵人選目標前就失效。
> 戳記跟 `TurnIndex` 比大小，沒有 tick 就沒有順序可以弄錯。
> **這個 bug 在寫測試前就抓到了**，`Slow_SurvivesIntoTheTargetsOwnPhase` 是它的迴歸測試。

> **A4 golden hash 未動。** 狀態欄位只在真的有人帶著狀態時才折進雜湊，
> 沿用污染與斬首標記的同一套做法。

### 新增資料

- `units.txt`：**玄真**（ATK 90，**數值由我選定**，理由是改變整除性）、
  **影丸**（HP 195 / ATK 42 / DEF 30 / MOVE 4 / 射程 3，**GDD 逐字**）、
  `momotaro_push` / `zhengshou_taunt` / `kagemaru_plain`
- `gym-opening` ＋ 兩張對照（`-noskill` 技能關掉、`-solo` 單人）

### 新增儀器

- **`ControlHoldStrategy`（`control-hold`）** —— 沒有它，技能就是「有機制沒使用者」，
  跑分會回報它們沒作用（反擊與淨化都吃過這個虧）

### 量測發現

- **M5 首次翻轉**：守勢 29% vs 衝鋒 56%，**−27**。
  **全專案第一張「站定不動等」不是正解的地圖** —— 也就是真人打法在這裡會輸。
- **M6 首次三條路線全部落在 20–45% 門檻內**（23/39/39），且 184/200 場真的穿越
  （`gym-lanes` 最好的一次只有 67/200）。
- **四人隊值 29 點**（單人 0% → 四人 29%），
  **控制技能值 −12 點**（`control-hold` 17% vs `corridor-hold` 29%）。
- **M2 從 13% 惡化到 28%** —— 四人隊等 MOVE 3 的正守，先前沒量過的隱性成本。

> **控制技能的 −12 很可能是策略的問題，不是技能的問題。**
> 在 `gym-opening-noskill` 上 `control-hold` 與 `corridor-hold` **逐位元相同**，
> 所以儀器是對的。這是第三次同一個模式（反擊、淨化、控制）：
> **機制有了，好的使用者沒有。腳本策略評不了「什麼時候該放技能」。**

### 表現層

- 地圖選單延伸到 12 格（`-` 與 `=`），新增 `gym-opening` 與 `gym-opening-noskill`
  **並排**，因為技能值多少只有真人 A/B 得出來。

### 驗證

- EditMode **187 / 187 通過**（新增 15 條控制技能測試）
- Core / Sim / Unity / Editor 四層**零警告**編譯
- **A4 golden hash 三條全部原值通過**

### ⚠️ 已知的量測缺陷（寫下來，不要下一輪重新發現）

1. **腳本策略不會為了打砲塔而前進。** 無時鐘版本 33% 場次撞回合上限。
   時鐘把它們轉成敗場，但**沒有製造決策**。這張圖的核心決策現有策略全都不做。
2. **最後一次調整同時動了兩個變因**（敵人 8 → 6 ＋ 20 回合上限）。那是調參不是量測，
   §17.3 的數字下一輪要用單變因重測。

---

## 2026-08-15（續六）— UI 關閉、Console 化、單位視覺差異化

觸發：專案負責人「我不常玩戰棋，給我常見的幾個思路，以及給角色和敵人做基本差異化
讓我分辨，UI 先關掉，操作寫在 console」。

### 表現層

- **IMGUI 覆蓋層預設關閉**（`F1` 叫回）。灰盒棋盤本來就小，面板蓋掉大半。
- **Console 變成主要介面**：載入時印操作表 ＋ 圖形對照 ＋ 完整名單；
  每回合開始印一次盤面；**被拒絕的指令一定印出原因**
  （UI 關掉之後，靜靜失敗跟按鍵沒接是分不出來的）。
- **`F2`** 重印盤面。
- 盤面每隻敵人附 **「你選的單位要幾刀砍死它」** ——
  對沒玩過戰棋的人，這是全畫面最有用的一個數字，而且別處看不到。
- **技能終於有鍵位**：`T` 嘲諷（自身）、`F` 遲滯、`V` 擊退。
  後兩者作用在**滑鼠指著的敵人**，沿用點擊已有的「指著目標」慣例，
  不必在沒有 UI 的情況下再發明一套目標循環。

### 單位視覺差異化（三個獨立通道，全部由 UnitDef 推導）

| 通道 | 規則 |
|---|---|
| **形狀** | `Move == 0` → 圓柱（不會過來）／`AttackRange >= 2` → 膠囊／否則方塊 |
| **大小** | 底面積隨 MaxHp 分三段。**底面積不是高度** —— 攝影機正上方俯視，高度看不見 |
| **顏色** | 依陣營色帶內的**索引平均分佈**。玩家綠→紫，敵人紅→琥珀 |
| 額外 | 未醒的敵人整體變暗（**保留身分色**）；嘲諷／防禦黃調；遲滯淡藍調；斬首目標長高 |

> **顏色第一版用名稱雜湊，實測會撞色**：`zhengshou_taunt` 落在 hue 0.741、
> `kagemaru` 落在 0.740 —— 人眼看是同一個顏色。
> 雜湊是「平均而言」分得開，而這裡是一份四個人的固定名單，「平均而言」買不到東西。
> **改成依索引平均分佈**，保證用上所有可用的色彩間距。
> 代價是顏色隨 spawn 順序改變 —— 所以 Console 每次載入都印名單，不靠記憶。

> 玩家色帶後來從「青→紫」拉寬到「綠→紫」：四人隊在窄色帶裡有兩隻只差一階，
> 而桃太郎與正守都是大方塊，**只剩顏色可以分**。

### 文件

- **改寫 [06-validation/playtest-guide.md](06-validation/playtest-guide.md)**。
  舊版說「四張地圖、沒有技能、UI 在畫面上」，三項全部過期。
  新增 **§2「戰棋的常見思路」** —— 給沒玩過戰棋的人的八條，
  每條都標了它在本專案量到多少（站位 65 點、刀數整除性、砲塔決定接觸時機…），
  以及 §2.3「新手最常見的陷阱」＝ 專案負責人自己回報的那個打法，
  附上它在新關卡會輸的數據。
- 新增 **Q7：技能該什麼時候放** —— 機器連續三次答錯的那個問題。

### 驗證

- Core / Sim / Unity / Editor 四層零警告；EditMode 187 / 187

---

## 2026-08-15（續七）— 移動範圍 vs 攻擊範圍分色；Console 補上規則

觸發：專案負責人「把操作規則一開始寫在 console，移動範圍跟攻擊範圍怎麼區分（雙方）」。

### 🔴 修掉一個真的缺陷：最關鍵的格子是隱形的

舊的 `Refresh` 先畫危險區再畫可移動區，**所以藍色永遠蓋掉紅色** ——
「我走得到、而且他們打得到」這種格子看起來跟「我走得到、而且很安全」一模一樣。

**那是這個遊戲每一個決定真正在講的東西**，而它在畫面上不存在。

### 四種範圍分開上色（雙方各兩種）

| 顏色 | 意思 |
|---|---|
| 藍 | 我方可站（移動範圍） |
| 青 | 我方打得到（移動＋攻擊）。藍色外面那圈就是射程 |
| 紅 | 敵方打得到（移動＋攻擊）＝ Exposure |
| 琥珀 | 敵方走得到但**打不到** |
| **洋紅** | **兩者皆是** —— 直接指定顏色，不用混色 |

洋紅**不混色**：藍疊紅會變成混濁的紫，而那跟「淡淡上了色的格子」長得一樣。
這種格子重要到不能靠色深分辨。

### 新增查詢（Core，唯讀）

- `BattleQueries.MoveRange(state, unit, fullBar)` —— 走得到的格子，不含攻擊擴張。
  `fullBar` 用滿 AP 計算：對**敵人**才是對的問法（輪到它時本來就是滿的）。
- `BattleQueries.StrikeRange(state, unit)` —— 不移動就打得到的格子。
- `BattleQueries.EnemyMoveZone(state, faction)` —— 敵方移動範圍的聯集。

### 操作

- **`TAB` 改成三段循環**：敵方威脅 → ＋敵方移動 → 關。
  兩者的差就是射程，用一個 bool 表達不出來。
- **`Z`** 切換我方範圍。
- **把滑鼠移到某隻敵人身上，紅／琥珀縮成只有那一隻。**
  聯集回答「還有哪裡安全」，單獨一隻回答「這東西要對我幹嘛」。

### Console 的開場訊息補上「規則」而不只是鍵位

新增 `RULES` 段：AP 與 MOVE 是兩個獨立預算、**未用 AP 會累積**（存 2 點下回合有 10 點
＝兩次攻擊外加一個技能）、地形進入成本、傷害公式 `max(1, ATK-DEF)` 無命中骰
（**低攻打高防只有保底 1 傷，先看刀數**）、防禦效果、單位互相阻擋、
敵人會主動過來**但圓柱體永遠不會**、清場在任何目標下都算贏。

新增 `CELLS` 段解釋上面五種顏色。

### 驗證

- Core / Sim / Unity / Editor 四層零警告；EditMode 187 / 187

---

## 2026-08-15（續八）— ESC 操作說明、單位資訊面板、頭上血條

觸發：專案負責人指定的三個表現層需求。

### ESC 操作說明（＋ F1 備援）

`ESC` 開關全螢幕操作表：滑鼠、行動、顯示、格子顏色、棋子形狀、
以及「兩個一定會搞混的」（AP 會累積、傷害必中且有保底 1）。

> **ESC 不完全是我們的鍵。** 編輯器 Game view 用它解除鎖定的游標，
> 獨立執行檔用它離開全螢幕。本專案從不鎖游標所以實際不衝突，
> 但**說明鍵有可能安靜地沒反應**，所以 `F1` 綁同一個功能當保險。
> 原本 `F1` 的完整除錯面板移到 **`F3`**。

### 單位資訊面板（左下角，一直顯示）

- 我方：**剩餘 AP / MOVE**、這格被幾隻打得到、
  **每個動作的 AP 消耗與說明**，前面 `✔ / ✘` 表示現在按不按得動
- 敵方：MOVE（0 會標「永遠不會移動」）、每回合可攻擊次數、
  **你選的單位要幾刀殺得掉它**、**它每刀打你多少**
- **右鍵任何單位＝查看，不會動手。** 必須跟左鍵分開：左鍵點敵人是攻擊，
  「點它看資料」跟「點它打它」不能是同一個手勢。右鍵空地取消釘選。

> **技能說明從 `UnitDef` 生成，不是手寫文案** ——
> 顯示的數字必然就是模擬器會用的數字。手寫的提示文字離「數值改了但沒改文案」只有一步。

### 頭上血條（雙方）

每個單位兩個扁平方塊（暗色槽 ＋ 彩色填充），躺在 XZ 平面上。
綠 → 琥珀 → 紅，**由左緣往右縮**（20% 的血條應該在左邊，不是浮在中間）。

- 不做 billboard、不用 Canvas：攝影機正上方俯視，平躺的長條本來就正對鏡頭，
  而世界空間方塊跟其他東西是同一套灰盒慣例（ADR-0005）
- 血條顏色**刻意不用任何一方的色盤** —— 它講的是數字不是身分

> **抓到一個高度 bug**：斬首目標的方塊高 1.5，頂端在 y=1.5，
> 而血條原本固定在 y=1.30 —— 正交俯視下會被整個吃掉。
> 改成 `BarYFor(unit)` 由單位本身的高度推導。

### 驗證

- Core / Sim / Unity / Editor 四層零警告；EditMode 187 / 187

---

## 2026-08-15（續九）— 防禦不是太強；真正的漏洞是傷害路由

觸發：專案負責人試玩回報四個假設。完整數據 [playtest-metrics §18](06-validation/playtest-metrics.md)。

### 新增儀器

- **`SustainHoldStrategy`（`sustain-hold`）** —— 把零頭 AP 花在防禦而不是第二次攻擊。
  它存在的理由和前四次一樣：**`corridor-hold` 有 8 AP 就攻擊兩次，只在無法攻擊時防禦，
  所以本專案發表過的每一個勝率，都是由一個從不「攻擊＋防禦」的策略量出來的** ——
  而那是真人第一個會做的事。第五次「機制有了，使用者沒有」。
- `gym-opening-guard5` ＋ 四個 `*_g5` 單位（`guardCost` 3 → 5，**只改這一個數字**）。

### 量測發現

- **「防禦太強」被推翻**：`sustain-hold` 10% vs `corridor-hold` 29%，
  而且**殘 HP 更少**（372 vs 550）。`guardCost` 3 → 5 幾乎不動（29% → 26%）。
- **我的算術錯在只算一個回合**。防禦降的是傷害「速率」，擊殺移除的是傷害「來源」；
  少打的那一刀會把戰鬥拉長（M3 19.31 → 20.05），於是多挨好幾輪。
  **AP 效率不能用單回合算。**
- **真正的漏洞是傷害路由**：`gym-opening` 六隻敵人裡**五隻打「最近的」**，
  所以「把正守放最近」就把全部傷害導進 DEF 70 / HP 435 的單位 ——
  隊伍有效 HP 約為平均分攤的 **4–5 倍**。而六隻裡只有兩隻（射程 2 與 3）繞得過肉盾。
- **這解釋了為什麼技能可有可無**：嘲諷付 2 AP 買「敵人打正守」，
  而敵人本來就打最近的 —— **站位免費做到了同一件事**。

### 沒有做的事

- **沒有加晦氣。** §15.3 已量到淨化是負收益（0% vs 31%），
  而且「逼玩家按某個鍵」不等於「讓那個鍵變成決策」。
  可行的版本是把汙染當地形壓力而不是技能稅，且需要先重定價（OD-29）。
- **沒有改防禦成本的基線**（OD-06）。`guard5` 只是變體檔。

---

## 2026-08-15（續十）— 用「三層分工」回頭檢視進度

依 [NOTE 技能機制、數值、關卡設計的分工](NOTE-2026-08-15-技能機制與數值-關卡的分工.md)
的五條判準檢視 §16–§18。完整結果 [playtest-metrics §19](06-validation/playtest-metrics.md)。

### 框架預測對了本輪最大的失敗

判準 1 是「這個技能豁免哪一條限制？那條限制現在綁得緊嗎？」

**嘲諷豁免的是「敵人打誰不是你能決定的」，而那條限制完全不綁人** ——
六隻敵人裡五隻打最近的，站位免費做到同一件事。
框架預測它會退化成嚴格升級；**實測是 −12 個百分點**。

**H16 明說那是「純文件工作，不需要實作」。跳過那一步，換來一輪的實作與一個負結果。**

### 對框架的兩個修正（本輪量到的）

1. **判準 1 失敗時可能是嚴格降級而不只是嚴格升級。**
   豁免券模型要加上「券本身的價格」——
   嘲諷付 2 AP 買站位免費給你的東西，所以是淨損失。
2. **「豁免致命度」靠減傷會反過來更糟。**
   §18.2：多防禦少 19 點且殘 HP 更少。減傷降速率、擊殺移除來源，
   少打的那刀把戰鬥拉長。有效的那條路是**改變誰承受**，不是降低承受量。

### 框架的一條結論已過期

原文「整除性與 Objective Pressure 這兩個更上游的旋鈕還沒動過」——
**兩個都動過了**：Objective Pressure（斬首 ＋ 砲塔／時鐘）買到目標優先級 0–5 → 25
與 M5 首次翻轉；整除性**只轉了一半**（桃太郎 residue 1，玄真仍是 0）。

### 框架尚未涵蓋的情況

**四人隊讓整除性因單位而異。** 同一張圖同一批敵人，
EP 對玄真印「focus fire is the only play」、對桃太郎印「at least one spare action」。
框架的模型假設單一 APT。

### 新增第五條限制

框架列四條（致命度／EAtK／release／整除性）。
§18.4 量到第五條：**傷害路由 —— 誰承受傷害**。
它是目前最鬆的一條，而嘲諷正是它的豁免券。

---

## 2026-08-16 — 傷害路由的兩個對策、一個更正、以及 H16 檢核

依 [playtest-metrics §19.6](06-validation/playtest-metrics.md) 的兩個行動項，兩個都做了。

### 🔴 更正：§17.4 ① 的「M5 首次翻轉」是時鐘造成的，不是地圖

把 `gym-opening` 的回合上限 20 改成 30（**只改這一個數字**）：

| 策略 | 上限 20 | 上限 30 |
|---|---|---|
| corridor-hold | 29% | **51%** |
| tpa-order | 32% | **58%** |
| **charge** | **56%** | **56%**（完全不動） |

**衝鋒 10.68 回合就打完，時鐘從來咬不到它；其他每一個都漲 11–26 點。**
守勢不是在輸戰鬥，是在輸賽跑 —— 佐證是殘 HP **550/1150**（hunter 那張甚至 648）。

§17.4 ① 已加更正標記，原文保留存查。

### 新增儀器

- **`ShieldWallStrategy`（`shield-wall`）** —— 最耐打的站最前、其餘退後，
  模擬專案負責人的「站位輪流扛傷」。
  為此在 `CorridorHoldStrategy` 加了 `PositionBonus` 虛擬掛鉤
  （與 `ChooseTarget` 同一個模式：讓子類表達**陣型**而不動 exposure 權重）。
- `gym-opening-ranged4`（遠程 2/6 → 4/6）、`gym-opening-hunter`（全部改打 HP 最低）、
  `gym-opening-clock30`（上限 20 → 30）。**每張只改一個變因。**

### 量測發現

- **兩個對策都沒打中**：ranged4 幾乎無變化（+4，方向還是變簡單）；
  hunter 兩極化（守勢 −10、衝鋒 **+26**，M5 落差惡化到 −63）。
- **原因**：腳本策略根本沒在做傷害路由，所以沒有東西可以被收緊。
- **`shield-wall` 比不擺陣型差 23–26 點**，不管時鐘鬆緊 ——
  它**無條件**退後，脆皮不攻擊 → 戰鬥拉長。
  **真人做得到而它做不到的是「一邊路由一邊保持節奏」，那需要前瞻。**
  **第六次「機制有了、好的使用者沒有」**，這次記成已知的量測邊界。

### 三條新的工作紀律

1. **看勝率一定要一起看殘 HP。** 殘 550/1150 還輸 71% = 超時，不是被打死。
2. **時鐘不是拿來修僵持的工具。** 僵持是策略不肯前進的儀器問題，
   時鐘會連帶壓平所有花時間的決策。
3. **收緊一條限制之前，先確認有策略真的在利用它。**

### 新增 [NOTE H16 — GDD 技能的豁免券檢核](NOTE-2026-08-16-H16-GDD技能豁免券檢核.md)

補做三層分工筆記的 H16（**上一輪跳過它，換來 −12**）。純文件，涵蓋四個角色 17 個技能。

- **17 個技能只有 1 個（影丸【龍】）在目前的限制配置下無條件通過。**
- **桃太郎四個全部不通過**：討鬼斬是換皮的無條件增傷、淨化豁免不存在的限制、
  號令走已知為負的減傷路。
- **最有價值的一格**：我把正守【不動明王】實作成嘲諷（GDD 原版是減傷）——
  **方向改對了（路由 > 減傷），但改到一條不綁人的限制上**。
  而 GDD 自己就有純路由的【金剛夜叉明王】分擔傷害，**且比嘲諷更好**
  （嘲諷要站進半徑，分擔傷害不需要位置配合）。
- **玄真的技能有可算門檻**：要把 `kohaku_3hit` 從 2 刀壓成 1 刀需要單次 ≥120 傷害。
  **對著刀數調，不是對著倍率調。**
- 給出「先收緊限制、再發豁免券」的實作順序表。

---

## 2026-08-16（續）— 技能成本掃描：沒有任何價位讓它們划算

依「根據實驗調整技能消耗 AP」。完整數據 [playtest-metrics §21](06-validation/playtest-metrics.md)。

### 掃描

三個技能統一成本 1–5 AP，建在 `clock30` 上（20 回合的時鐘會壓垮任何花節奏的東西）。

| 成本 | 0（無） | 1 | 2 | 3（原價） | 4 | 5 |
|---|---|---|---|---|---|---|
| **control-hold** | **51%** | **52%** | 44% | **39%** | 38% | 33% |

**1 AP 是損益兩平點，不是最佳點。沒有任何價位是正的。**

儀器對照通過：`skill0` 那列 `corridor-hold` 與 `control-hold` 都是 51%。

### 已套用

`units.txt` 三個技能載體 2/3/3 → **1 AP**。

> ⚠️ **這不是加強。** 損益兩平＝技能現在什麼都不做，而不是變好了。
> 原價每回合課 12 個百分點，現在課 0。**價格從來不是問題** ——
> 它們豁免的限制不綁人（H16 / §19.2），券賣多便宜都沒人需要買。

### 作廢的數字

`gym-opening` / `-ranged4` / `-hunter` / `-clock30` 的 **`control-hold` 那一列**需重量。
`corridor-hold` / `charge` / `shield-wall` 不受影響（從不使用技能）。

### 順帶記錄一個持續存在的儀器缺陷

`corridor-hold` 在這個掃描裡應該是平的（它從不用技能），實際在 48–58% 之間晃。
**15% 取樣雜訊從合法指令集抽，而技能成本會改變哪些技能負擔得起。**
→ 跨列比較 `control-hold`，不要同列相減。

---

## 2026-08-16（續二）— 重寫 HANDOFF：本會話全部統整 ＋ 資訊分級

沿用既有的 0–6 結構並擴充。**沒有改動任何 gameplay code 或資料。**

### 新增 §0.0 資訊分級

每一條資訊標 `[A]` repo/實驗確認 ／ `[B]` 本會話提出但未完整核對 ／ `[C]` 設計推論。
**`[B]` `[C]` 不得寫成規格。**

### 本輪對 repo 的核對結果

| 查了什麼 | 結果 |
|---|---|
| 是否為 git repository | **否**（`git rev-parse` 失敗）。沒有 diff 可看 |
| `units.txt` 技能成本 | **已是 1 AP**（taunt / slow / push），與本會話摘要一致 |
| Sim 的 noise 實作 | **確認** `Decide` 對 `LegalCommands.For` 均勻抽樣 → 技能成本會改變雜訊分布 |
| 玄真 vs `kohaku_3hit` 刀數 | **確認** ATK 90 / DEF 20 → 70 每刀，120 HP → 2 刀；壓成 1 刀需單次 ≥120 |
| GDD【不動明王】/【金剛夜叉明王】 | **確認原文**（`00-source` 264/268/270 行）：前者減傷、後者分擔傷害且為「反應」 |
| 策略／encounter／unit 數 | 9 策略、61 encounter、47 unit、187 測試 |

### 標記為已過期的既有文件

- [NOTE 移動力射程與地圖幾何 §7](NOTE-2026-08-15-移動力射程與地圖幾何.md)
  列「推撞／位移 ❌ 無」，**本會話已實作 `PushCommand`**。
- [NOTE 技能與數值分工 §10](NOTE-2026-08-15-技能機制與數值-關卡的分工.md)
  「整除性與 Objective Pressure 還沒動過」，**兩個都動過了**。

### 新增的最高優先未決事項

**`gym-opening` 的 clock 尚未定案**（20 / 30 / 無，三者各有代價）。
**尚未登錄成正式 OD** —— 它同時是設計問題與儀器問題，需要專案負責人決定歸類。

---

## 2026-08-16（續三）— M/R 矩陣對照診斷 ＋ Strategy Depth Experiment Plan

**沒有改動任何 gameplay code 或資料。** 純文件。

### 新增 [06-validation/strategy-depth-plan.md](06-validation/strategy-depth-plan.md)

含 M/R 矩陣對照診斷、D0–D6 階梯、第一個實驗的完整設計（否證條件 ＋ 只差一變因的對照組）。

### 🔴 檢視時發現的新儀器缺陷：技能使用率從未被量過

`SimulationRunner.TryClassify` 只認得
`Move / Attack / Guard / Rest / Wait`，其餘 `return false`。
**`Taunt` / `Slow` / `Push` / `Purify` 因此從來沒有進入 M1 的動作組合統計。**

> **「技能是負的」與「技能根本沒被使用」在現有遙測下無法區分。**
> §17.3 / §20 / §21 的每一個技能結論都少了一半證據。

列為計畫裡**唯一允許的 metric 擴充**（`ActionKind` 與分類函式都已存在，
只是沒涵蓋新指令），且排在第一順位。

### M/R 矩陣對照的四個發現

1. **本專案只有約 26% 的戰鬥在「接敵」** —— `gym-opening` trickle 窗口回合 2–6，
   而 M3 是 19.31。**M/R 設計的是前面那 1/4**；後面 74% 的纏鬥不是血厚，
   是**策略大多數回合沒有在攻擊**（M2 浪費 28%）。
2. **M+R 分布過度集中**：47 個 unit 有 2/3 擠在 M+R = 4–5（威脅 41–61 格）。
   **「高 M ＋ 低 R」象限完全沒有** —— 最高 M 是 5，沒有任何單位能一回合穿透陣型。
3. **影丸 M4/R3 ＝ M+R 7 ＝ 113 格，是玩家側最大的威脅圈（小耗的 2.75 倍），
   但 ATK 42 殺一隻小耗要 4 刀** —— 幾何最強、輸出最弱，這個張力從未被測過。
4. **ZOC 缺席與 §18.4 的傷害路由發現直接相關**：
   「把正守放最前」有效是**倚賴敵人 AI 打最近的**（6 隻裡 5 隻），
   那是 **AI 的性質不是規則的性質**。改打 HP 最低時守勢立刻掉 10 點。

### 計畫的第一個實驗：D1 路線決策

**刻意繞過 clock 而不是先解決它** —— 把 encounter 縮到 M3 落在 6–10，clock 就咬不到。
**M3 才是根因，clock 是症狀。**

候選：14×10、3 條路線、**2 個玩家單位（刻意選 M+R 差 2 的桃太郎與影丸）**、
3 隻會移動的敵人（不放砲塔，避免僵持）、`rout` 無 clock。

**對照組是一張三條路線做成完全相同的圖** ——
這也是 [OD-26](OPEN-DECISIONS.md#od-26) 一直缺的那一組：
M6 只能證偽，而「固定策略永遠有固定偏好」只有拿路線相同的圖來比才知道分布裡有多少是地圖給的。

---

## 2026-08-16（續四）— 執行 Strategy Depth Plan：順序 0 ＋ D1 實驗

**依 [strategy-depth-plan §4](06-validation/strategy-depth-plan.md) 的 gating 順序執行。**
動了程式（只有 `Ediki.Sim` 與表現層）與資料（三張新 `gym-*` 地圖）。

### 順序 0：補上技能動作分類 —— 計畫裡唯一允許的 metric 擴充

`Ediki.Sim` 三處：

- `ActionKind` 加入 `Taunt = 5` / `Slow = 6` / `Push = 7` / `Purify = 8`
- `SimulationRunner.TryClassify` 認得四個技能指令（`EndTurnCommand` 仍然 `false`，
  它是 phase 切換不是單位的行動）
- `BattleResult.ComposeKey` 的計數陣列改由**名稱表長度**決定，所以
  **下一個沒有名字的 `ActionKind` 會當場丟例外，而不是被靜默丟掉**
  —— 那正是讓整組控制技能對 M1 隱形兩輪的失敗模式

報告新增一行 **`M1b skill use`**（有技能的 unit-turn 佔比）。
**它是既有 M1b 鍵的聚合，不是新量測** —— `Describe` 只印前三名組合，
一個只用 4% 的技能會躺在 `CompositionCounts` 裡永遠不出現。

> **五個既有列舉值的數值沒有動。** 組合鍵依列舉順序輸出，
> 所以沒有技能的戰鬥產生**逐位元相同**的字串，
> §16–§21 的每一個 M1 數字仍然可比。
>
> **`Ediki.Core` 一個字都沒改，A4 golden hash 沒有動。**

### D1 實驗：路線是不是真的決策 → [playtest-metrics §22](06-validation/playtest-metrics.md)

新增三張地圖（全部 `gym-*` 實驗場，`stage01*` 未動）：

| 場地 | 中路 | 東路閘口 | 角色 |
|---|---|---|---|
| `gym-d1-routes` | 3 格寬 | 森林 2 AP | 實驗組 |
| `gym-d1-noforest` | 3 格寬 | 道路 | **只差地形**的中間階 |
| `gym-d1-flat` | 1 格寬 | 道路 | 計畫指定的對照組 |

**與計畫的兩處偏離，寫在地圖檔頭裡而不是藏起來**：

1. **敵人組成換過**。§3.2 想要的 `kohaku_bowfair` ＋ M3/R1 rusher 都是 ATK 100，
   會讓影丸的 lethal exposure ＝ 2，**一場都還沒跑就踩到 I2**。
   過得了 I2 的區間是 ATK ≤ 75。
2. **兩個玩家疊在同一欄 spawn**（不是並排）。並排會讓一個單位天生離西路近、
   另一個離東路近，**F2 會在 M+R 什麼都沒做的情況下自己成立**。

### 結果：H-D1 在站位策略上成立，在 `charge` 上被否證

| 否證條件 | corridor-hold | tpa-order | charge |
|---|---|---|---|
| F1 最常用路線 > 70% | 66.7% 未觸發 | 64.3% 未觸發 | **92.3% 觸發** |
| F2 兩玩家分布差 < 10 點 | 最大 24.0 未觸發 | 最大 33.1 未觸發 | — |
| F3 對照組差 < 10 點 | 最大 20.9 未觸發 | 最大 18.8 未觸發 | **最大 4.4 觸發** |

**四個有效性閘門全過**（lethal exposure 3/8、residue 非零、路線成本差 12.5%、
M3 ≤ 11.45 且 unresolved 0%）。

### 🔴 但 M6 與 M5 講的話相反 —— 本輪最重要的一句

> **M5 落差（守勢 − 衝鋒）＝ −1。**
> 衝鋒勝率一樣、回合數少 6.5、**殘 HP 多 88**。
> 依判讀順序第二層的門檻，**這張圖「沒教到東西」**，
> 儘管它的 M6 直方圖對幾何反應強烈。

### 三個新的量測邊界（登錄進計畫 §5）

- **攻擊射程無視牆壁**（`AttackableTargets` 是純曼哈頓距離，無視線判定）
  → **M+R 威脅圈在任何有牆的地圖上高估高 R 單位**。已由程式碼確認
- **M6 百分比是「有穿越的場次」的條件分布** → 穿越率差 18 點的兩張圖不能相減
- **兩邊擠同樣的缺口時，局部地形編輯不是局部的**
  → 兩格森林把 M3 從 11.03 改成 7.33、穿越率從 99/200 改成 63/200

### 沒有做的事

- **沒有裁決任何 OD**，clock 懸案原封不動
- **沒有新增單位、技能或規則**，`units.txt` 一個字都沒改
- **沒有進 D2**：計畫的 gating 說 D1 通過才進，而 D1 只部分通過。
  下一步改為順序 3（先查是幾何問題還是策略問題）
- **沒有為了讓 M5 不飽和而反覆調數值**。一次 40 場 pilot 就看出飽和，照實記下來

### 測試

**187 → 193，全綠。** 新增 6 個：5 個技能分類迴歸（含一個儀器對照：
同一張圖上 `control-hold` 的 skill use > 0%、`corridor-hold` ＝ 0%）
＋ 1 個 D1 三張圖的幾何守門測試。
Core / Sim / Unity / Editor 四層編譯零警告。

### 補：分類修好之後量到的第一個技能使用率

`gym-opening` / `gym-opening-clock30`，200 場/格，`units.txt` 已是 1 AP 版：

| 場地 | corridor-hold | **control-hold** |
|---|---|---|
| `gym-opening` | 29%、skill use **2%** | 22%、skill use **13%** |
| `gym-opening-clock30` | 58%、skill use **1%** | 52%、skill use **11%** |

**§1.5 問的那個問題有答案了：技能被用了 13% 的 unit-turn，
所以「技能是負的」是「用了而且輸」，不是「根本沒用到」。**
同時清掉 §21.3 四列 stale 中的兩列（`gym-opening`、`-clock30`）。

**🔴 而且量到雜訊污染的一個更尖銳的形式**：
`corridor-hold` 的 skill use **不是 0，是 1–2%**，而它的程式碼裡
**沒有任何一行會發出技能指令** —— 15% 取樣雜訊從含技能的合法指令集裡均勻抽，
**會替一個沒有技能邏輯的策略把技能放出去**。

> **所以 `corridor-hold` 不是乾淨的無技能對照。**
> 乾淨的對照是 `gym-opening-noskill`，或像 D1 那樣直接用身上沒有技能的單位
> （D1 九格的 skill use 全部是 0%）。

---

## 2026-08-16（續五）— 異常 Seed 自動篩選 ＋ 單局 Replay（診斷層）

**只動 `Ediki.Sim`、測試專案、`Ediki.Editor`。`Ediki.Core` 一個字都沒改，A4 golden hash 未動。**
**沒有新增 CLI 或 `Main()`** —— 本專案沒有 shipped CLI，入口沿用 `Ediki` 選單。

### 新增：anomaly detection

`Sim/BattleAnomaly.cs`：`FailureReason` / `AnomalyThresholds` / `BattleAnomaly` /
`AnomalyDetector` / `AnomalyReport`。純觀測，只讀 `BattleResult`，不重跑任何戰鬥。

| FailureReason | 判定 |
|---|---|
| `TIMEOUT_UNRESOLVED` | 收工時 outcome 仍是 `InProgress` |
| `ABORTED_BY_REJECTED_COMMAND` | 同上，但停在**被拒絕的指令**而不是回合上限 |
| `DEFEAT_WITH_HIGH_REMAINING_HP` | `Defeat` 且殘 HP / 隊伍總 HP **> 40%**（整數運算，嚴格大於） |
| `UNEXPECTED_SKILL_USAGE` | 呼叫端宣告「這個策略不該放技能」而**策略**仍放了 |

一場可同時帶多個 reason，**不覆蓋**。

### 🔴 兩個必須寫下來的語意發現

**1. 兩個時鐘不是同一件事。**
`objective type=rout turns=20` 是**規則**：`TurnIndex` 超過就判 `Defeat`，
**是已解決的結果**。`SimulationConfig.MaxRounds`（60）是**跑分工具的安全網**（R-WIN-04），
踩到它 outcome 仍是 `InProgress`。**只有後者算 unresolved。**

實測佐證：`gym-opening` 被標記的 20 場全部是 `rounds: 20 / round_cap: 60 / result: Defeat`
—— 咬到的是地圖自己的 20 回合，不是安全網。

**2. `HitRoundCap` 目前無法區分「時鐘到了」與「指令被拒」。**
它是由 `outcome == InProgress` 推出來的，而 runner 在 `EndTurnCommand` 被拒時也會 `break`。
兩者都落進同一個 Unresolved 桶，**只有一個是逾時，另一個是引擎層故障**。
新增 `BattleResult.EndedByRejectedCommand` 與 `ABORTED_BY_REJECTED_COMMAND` 把它們分開。

### 🔴 技能歸因：`skill_use > 0` 不等於「策略放了技能」

15% 取樣雜訊從 `LegalCommands.For` 均勻抽，而那份清單**含技能**，
所以 `corridor-hold`（程式碼裡沒有一行會發技能）也會放技能。
因此 `BattleResult` 把 `StrategySkillActions` 與 `NoiseSkillActions` **分開計**，
`UNEXPECTED_SKILL_USAGE` **只看策略那一欄**。實測 replay 表頭會印
`skills: 0 by strategy, 2 by sampling noise`。

> ⚠️ **`UNEXPECTED_SKILL_USAGE` 目前沒有被任何一格啟用。**
> `IPlayerStrategy` 只有 `Name` 與 `DecideNext`，**沒有技能政策的 metadata**，
> 而從名字推論就是硬編規則。所以改成**呼叫端宣告**
> （`SimulationConfig.ExpectNoStrategySkillUse`，預設 `false` = 不檢查），
> 等專案負責人裁決要對哪些格子開啟。

### 新增：deterministic replay

- `Sim/BattleTranscript.cs`：`IBattleObserver`（唯讀掛鉤）＋ `BattleTranscript` ＋ `ReplayRunner`
- **走同一個 `SimulationRunner.RunOne` 入口**，沒有第二套戰鬥邏輯
- 逐行輸出由**既有的 `EffectLog`** 產生，沒有第二套 action pipeline
- 實測：`gym-opening` seed 1 corridor-hold，batch 與 replay 的 `state_hash` 同為 **840736733**

**已知界線**：敵方 phase 由 `EnemyAi.RunFactionTurn` 內部跑完，
所以**敵方個別指令的 rejection 從 Sim 看不到**（要看得到就得改 Core，本輪禁止）。
玩家側的 rejection 完整記錄。

### 新增：`Sim/ReplayRequest.cs`

`StrategyCatalog`（name → strategy 的**單一**對應表，批次與 replay 共用）＋
`ReplayRequest.TryParse`。四種錯誤都有句子，不丟未處理例外：
encounter 不存在（附近似名稱）／strategy 不存在（附清單）／seed 非整數／參數數量不對。

### Editor

- `SimulationMenu` 跑完批次後寫 **`SimResults/anomalies.json`**（固定檔名，方便 diff），
  並在文字報告尾端加上各 reason 的計數
- 策略清單改由 `StrategyCatalog` 解析（**集合與順序不變**，避免既有格子變成不同批次）
- 新增 `Ediki > Replay Battle…` 視窗，吃 `--replay <encounter> <seed> <strategy>` 一行

### 測試

**193 → 224 全綠。** 新增 31 個：anomaly 判定（含 40% 邊界、除以零、可調門檻）、
多重 reason 不覆蓋、JSON 穩定性／欄位／跳脫／空報告、replay 與 batch 逐位元相同、
觀測者不改變戰鬥、transcript 內容、四種參數錯誤、catalog 一致性。
A1–A7 與 A4 golden hash 全數通過。

---

## 2026-08-16（續六）— Spatial Heatmap（occupancy / clash 診斷層）

**只動 `Ediki.Sim` 與測試專案。`Ediki.Core` 零修改、`Ediki.Unity` 未動、沒有新增 CLI。**

### 新增 `Sim/BattleHeatmap.cs`

| 型別 | 內容 |
|---|---|
| `SpatialGrid` | 每格一個計數。**扁平 `int[]`**，`[x,y]` 索引。尺寸由**實際地圖**決定 |
| `BattleHeatmap` | `Occupancy` ＋ `Clash` 兩張 grid ＋ `Map` 參照 ＋ ASCII 輸出 |
| `HeatmapObserver` | 透過既有 `IBattleObserver` 累加，**只讀，不重算任何戰鬥** |

**Occupancy**：每回合結束，每個**存活**單位在其座標 +1（雙方都算）。
一個單位一回合最多 +1 —— 按行動計數會變成「誰比較忙」的圖，不是「人在哪」的圖。

**Clash**：只認 `HpChanged` 且 `Delta < 0`（規則層自己記的「HP 真的少了」）。
**不是** attack command 數、不是嘗試數、不是 target 數。
被拒絕的指令沒有 effect，自然不計；治療是正 delta，不計。

### 一個 action 造成多處傷害怎麼算

`[A]` 本專案目前唯一的多受害者行動是**反擊**：一個 `AttackCommand` 會產生
兩個 `HpChanged`（目標、以及被反擊的攻擊者）。
規則是**每一個造成傷害的 effect 各 +1，記在該受害者當下所站的格**，
所以未來真的出現多目標技能時不必改。

`[A]` **位置是沿著 effect 追出來的，不是從某一個 state 讀的**：
effect 只說「哪個單位」不說「在哪」，而敵方 phase 的 log 交過來時單位早就移動過了。
觀測者在每個 log 開頭從 state 重新對齊全部座標，再依
`UnitMoved` / `UnitPushed` / `UnitSpawned` 往前推。
測試 `Clash_CreditsTheCellTheVictimMovedTo_NotTheOneThePhaseStartedIn`
同時驗證推撞後的目標與移動後被反擊的攻擊者。

### 兩個 observer 介面的必要擴充

- 新增 `RoundEnded(round, state)` —— occupancy 的取樣點。
  **每個實際打過的回合恰好一次，包含戰鬥結束的那一回合**（它是從 `break` 離開迴圈的）
- `PhaseResolved` 加上 `before` 參數 —— 沒有它就無法還原傷害發生當下的座標
- 順帶補上一個既有漏洞：**`endEnemy.Log` 以前只有 `Tally` 看得到，從未送進 observer**

### 邊界語意

`SpatialGrid.Add` 對地圖外的座標**回傳 false，不 clamp、不 wrap**，
並累進 `BattleHeatmap.OutOfBoundsSamples`。
理論上永遠是 0；**計數而不是忽略**，才能讓真的出問題時看得見。
阻擋格一律顯示 `#`，且有測試確認阻擋格永遠不會累加。

### ASCII 輸出

`. = 0`、`1-9 = 該數字`、`* = 10 以上`、`# = 阻擋`。
**row 0 印在最上面**，與 encounter 檔的 map 區塊同向，兩者可以並排對照。
另有 `RenderRow(grid, y)` 供測試直接斷言，**測試不去 parse 報告排版**。

實例（`gym-d1-routes`，corridor-hold，60 場）：三條車道與中央阻擋塊清晰可見。

```
      0123456789012
    0  #############
    1  #5*********5#
    2  ##*##*.*##1##
    3  ##6##*1*##*##
    4  ##*##81*##*##
    5  ##*##*4*##3##
    6  #*****8***66#
    7  #***#####*58#
    8  #5**#####*26#
    9  #*********26#
   10  #******8*959#
   11  #############
```

### 批次接線

每個 (map, strategy) 格**一個 observer 累加全部種子**，跑完掛到
`SimulationSummary.Heatmap`，由 `Describe()` 印在該格最後。

- **CSV 完全沒動**（有測試逐字比對表頭與列數）
- 既有 metric 意義未變
- 兩次獨立批次的文字報告（含兩張 heatmap）**逐位元相同**

### 沒有做的事

- **沒有碰 `TopRoutePercent`**（已知的 per-x reporting bug，另案處理）。
  有一個測試明確固定住它現在的語意
- 沒有新增 `MapTopology` 抽象、沒有動 `IGridTopology` / `SquareGrid4`
- 沒有引入平行化、`ConcurrentDictionary` 或 `ThreadLocal`

### 測試

**226 → 254 全綠**（新增 28）。A1–A7 與 A4 golden hash 全數通過。
batch 與 replay determinism 另以實跑核對：
`gym-d1-routes` seed 1 corridor-hold，批次與 replay 同為 `state_hash 3377116958`。

---

## 2026-08-16（續七）— Strategy Persona Matrix ＋ 支配性啟發式偵測

**只動 `Ediki.Sim` 與測試專案。`Ediki.Core` 零修改、`TopRoutePercent` 未動、CSV 未動。**

### 先盤點既有 metrics，只補兩個缺口

`[A]` 可直接重用：`WinRatePercent` / `MeanTurnsX100` / `Unresolved` / `Runs` / `MeanFinalPlayerHp`。

`[A]` **缺的兩項，per-battle 資料早就有、批次層從未彙總**：

| 新增到 `SimulationSummary` | 為什麼不能用既有欄位 |
|---|---|
| `MeanRemainingHpPerMille` | `MeanFinalPlayerHp` 是絕對值，跨 roster 不可比。**−1 = 無法取得，不是 0** |
| `StrategySkillActions` / `NoiseSkillActions` | `UnitTurnsWithSkillPercent` 讀的是組合鍵，**分不出是策略放的還是雜訊放的** |
| `ResolvedRuns` | 見下 |

> 🔴 **`MeanTurnsX100 == 0` 的意思是「沒有任何一場打完」，不是「0 回合」**
> （`resolved == 0 ? 0 : ...`）。所以 persona 用 `ResolvedRuns` 判定該指標
> **available / unavailable**，而不是把 0 當成一個很快的戰鬥。

### 新增 `Sim/StrategyAnalysis.cs`

`StrategyMetric`（值 ＋ 方向 ＋ **是否取得**）／`StrategyPersona`／`PersonaMatrix`／
`MetricRule`／`StrategyComparer`／`AnalysisOptions`／`StrategyAnalysis`。

- **比較鍵是 (Encounter, Strategy)**，`routes` 與 `flat` 永遠分開，不跨場地合併
- **`Compare(challenger, incumbent, options)` 不認得任何策略名稱**；
  規則是資料（`MetricRule`），要換指標或換條件不必改比較器
- 報告只對 `charge vs corridor-hold` 發警報，那是 `AnalysisOptions.AlertPairs` 的預設值

**啟發式**：勝率 `>=`、平均回合 `<`（唯一不接受平手的條件）、殘 HP 比例 `>=`。

> ⚠️ **刻意不叫 strict dominance。** 這是三個批次平均值的篩選啟發式，
> 不是賽局理論意義下的支配。有一個測試把
> `strictly dominates` / `strict dominance` 這兩個字串擋在輸出之外。

### Sample size 與缺值

- `MinimumSampleCount` 是 **nullable，預設 null（不啟用）**。
  「幾場才夠」是實驗政策，本專案沒有裁決過，**偵測器不自己挑一個數字**
- N 一律顯示，讓讀的人自己判斷
- 缺值**既不算 Pass 也不算 Fail**，而是 `Unavailable`，並讓啟發式直接不成立
- 缺策略：不 crash、不發警報，報告印 `comparison unavailable`

### D1 實測（200 場/格）

`[A]` **兩張圖各自獨立成立**：

| 場地 | 策略 | N | Win% | AvgRounds | AvgHP% |
|---|---|---|---|---|---|
| routes | **charge** | 200 | **100%** | **4.58** | **82.2%** |
| routes | corridor-hold | 200 | 99% | 11.03 | 64.5% |
| routes | tpa-order | 200 | 99% | 10.99 | 64.6% |
| flat | **charge** | 200 | **100%** | **4.50** | **83.6%** |
| flat | corridor-hold | 200 | 100% | 11.45 | 65.2% |
| flat | tpa-order | 200 | 100% | 11.35 | 65.3% |

**兩張圖都觸發 `DOMINANT_STRATEGY_HEURISTIC`。**
這與 [§22.4](06-validation/playtest-metrics.md) 記的「M5 落差 = −1」一致，
現在三個維度都量化了：**衝鋒不只贏得一樣多，還快 6.5 回合、多留 18 個百分點的 HP。**

> `[A]` flat 的警報是**由 flat 自己的數字**得出的，不是被 routes 帶出來的。
> 隔離性由 `AnAlertOnOneEncounterDoesNotFireOnAnother` 用「flat 不成立」的
> 資料組固定住。

### 測試

**254 → 281 全綠**（新增 27）。A1–A7 與 A4 golden hash 全通過。
batch／replay／heatmap determinism 皆以實跑核對：
兩次獨立批次的報告（警報＋矩陣＋熱度圖）**逐位元相同**；
`gym-d1-routes` seed 1 corridor-hold 批次與 replay 同為 `state_hash 3377116958`。

---

## 2026-08-16（續八）— 正守 Counter 經濟實驗 ＋ 玄真 B RULE-GAP

**只動 `Ediki.Sim`、測試專案、`units.txt`、兩張新 `gym-*` 圖。**
**`Ediki.Core` 零修改、A4 未動、`TopRoutePercent` 未動、CSV 未動。**

### 🔴 RULE-GAP：玄真 B（破防削弱 / Armor Break）在現行規則下無法表達

`[A]` 清點過 `UnitDef` 全部 25 個欄位與 `units.txt` 全部可解析鍵：
**沒有任何欄位能降低目標 DEF 或放大對特定目標的傷害。**
規則層唯一的傷害倍率是 `ApplyContamination`，而它綁死在地形污染 ＋ 固定陣營條件
（`attacker.Faction == Enemy` / `target.Faction == Player`），**玩家無法對敵人施放**。

要真正實作需要：新 `ArmorBreakCommand`、新 `UnitState` 欄位、改傷害解析、改 `StateHasher`
→ **A4 / A4b / A4c Golden Hash 會改變**。

> **本輪不實作，也不做名稱相近、語意不同的代用技能**（會污染實驗結論）。
> 標記為 **RULE-GAP / UNEXPRESSIBLE**，待專案負責人決定是否開放規則層擴充。

### 正守 B：counter 的 AP 經濟實驗

`[A]` **兩個只差一個欄位的單位**（`attackCost` 4 vs 5；HP/ATK/DEF/MOVE/range/
guardCost/counterCost/ap/apRegen 全部相同）＋ 兩張只差該單位的圖。

`[A]` **一個關鍵的規則事實**：`EndTurn` 只對**incoming faction** 重設 AP，
所以玩家沒花完的 AP **真的會留到敵方回合**——機制是可達的。

### 🔴 兩個推翻預期的結果

**1. baseline（4 AP）的 counter 不是 0。** 預期是「幾乎不觸發」，實測 corridor-hold
觸發 250 次、charge 279 次。成因：AP 上限 10／回復 8 會**累積**，而接敵前的回合
沒有攻擊目標，AP 自然留下來——**那是副產品，不是決策**。

**2. 5 AP 是否製造 decision space，取決於有沒有別的行動能吃掉那 3 AP。**

| | counter 觸發 | 每回合 counter | Guard 花掉的 AP |
|---|---|---|---|
| base / corridor-hold | 250 | 0.18 | 1614 |
| **v5 / corridor-hold** | 286 | **0.16（更低）** | **2907（幾乎翻倍）** |
| base / charge | 279 | 0.30 | 75 |
| **v5 / charge** | **478** | **0.46** | 54 |

> **Guard 的價格剛好就是那 3 AP。** corridor-hold 會把殘餘拿去 Guard，
> 於是 5 AP 反而讓 counter 率從 12% 掉到 10%；
> charge 從不 Guard，殘餘活下來，counter 傷害 3627 → 6214（+71%）。
>
> **結論：5 AP 製造的是 residue，不是 decision。**
> 而且**沒有任何策略是「選擇」保留 AP** ——現有九個策略都沒有這個概念。

### 變體**移除**了一個既有支配，而不是製造新的

`[A]` baseline 上 charge 通過全部三條啟發式條件（觸發 alert）；
5 AP 版 charge 在殘 HP 上失敗（61.3% vs 65.5%）→ **heuristic FALSE**。

### 新增觀測層（只讀）

`Sim/RoleMetrics.cs`：`ApResidue`（attack/move/skill/guard/rest/counter/reserved/wasted）、
`RoleMetrics`（attacks/kills/damage/counter opportunities/activations/counter damage）、
`RoleMetricsObserver`、`CompositeObserver`。
`UnitPositionTracker` 由 heatmap 抽出共用，避免兩邊對「傷害發生當下在哪」給出不同答案。

> **counter opportunity 的定義**：規則檢查的每一條都成立、**只差 AP** 的那種攻擊。
> 所以射程 2 的敵人不算（反擊要求攻擊者在防守者射程內），
> 一回合內第一次反擊之後的攻擊也不算（`HasCounteredThisRound`）。

### 測試

**281 → 296 全綠**（新增 15）。其中一個測試抓到觀測器真實 bug：
`UnitPositionTracker.Follow` 會吃掉 `UnitPushed`，導致 push 從未被計數。
A1–A7、A4 Golden Hash 全通過；batch／replay／heatmap determinism 實跑核對。

---

## 2026-08-16（續九）— counter-reserve 儀器策略 ＋ 2×2 機會成本實驗

**只動 `Ediki.Sim` 與測試專案。`Ediki.Core` 零修改、A4 未動、`TopRoutePercent` 未動、
既有九個策略一行都沒改。**

### 新增 `CounterReserveStrategy`（實驗儀器，不是 AI）

規則是能稱得上誠實的最小版本：

```
攻擊   只在 Ap - attackCost >= counterCost 時
其他   結束該單位的行動，把剩下的 AP 留著
```

**不 Guard**（Guard 3 AP 剛好等於 reserve，會花掉正在量的東西）、
**不移動**（明確的儀器限制：在需要主動接敵的地圖上它永遠不會抵達，
只在敵人會自己過來的 encounter 上有效）。
`counterCost = 0` 的單位自動退化成「負擔得起就攻擊」，不需要特例。

**未加入 `SimulationMenu` 的標準批次**，只註冊進 `StrategyCatalog` 供 replay 使用。

### 2×2 結果（同一批 encounter／seed／敵人／場次，各 200 場）

| | A 4AP normal | B 5AP normal | **C 4AP reserve** | **D 5AP reserve** |
|---|---|---|---|---|
| 回合 | 6.81 | 9.25 | **5.11** | **5.19** |
| 殘 HP | 292 | 285 | **307** | **302** |
| 攻擊次數 | 1350 | 1314 | **738** | **740** |
| 反擊觸發 | 250 | 286 | **862** | **860** |
| 觸發率 | 12% | 10% | **95%** | **89%** |
| 反擊傷害 | 3250 | 3718 | **11206** | **11180** |
| 每回合保留 AP | 2.24 | 1.99 | **5.69** | **5.22** |
| 受到傷害 | 29489 | 32446 | **26192** | **27128** |

### 三個結論

**1. 儀器有效**：反擊觸發率 12% → 95%，每回合反擊 0.18 → 0.84。

**2. 機會成本是負的**：少打 45% 的攻擊，回合數反而**少 25%**、殘 HP **更高**、
受傷**更少**。成因是反擊在**敵方回合**產生傷害事件——它不佔玩家行動，
而且把本來會溢出浪費的 AP 換成傷害。
`attacks×13 + counters×13 = 20800` 在四格全部成立，數字自洽。

**3. 🔴 5 AP 對 reserve 結構沒有幫助**：C 與 D 幾乎完全相同
（738 vs 740 次攻擊、862 vs 860 次反擊）。
在 AP 上限 10／回復 8 之下，**兩者的穩定狀態都是「每回合 1 攻擊 ＋ 1 次反擊機會」**：

```
4 AP: 10 → 攻擊 → 6（≥3 ✓）→ 攻擊 → 2（<3 ✗）   停，留 6
5 AP: 10 → 攻擊 → 5（≥3 ✓）→ 攻擊 → 0（<3 ✗）   停，留 5
```

> **產生 reserve 的是決策規則，不是主行動的價格。**
> 上一輪「5 AP 製造 residue」的結論仍然成立，但那個 residue 只在
> **貪婪策略**下才有意義；一旦策略會自己保留，attackCost 4 與 5 沒有差別。

### ⚠️ 必須跟數字一起讀的限制

- **corridor-hold 在這張圖上會後退**（move 1802 AP），把戰鬥拉長。
  C/D 相對 A/B 的優勢**有一部分來自「沒有亂走」而不是反擊本身**
- **總輸出被構造固定成 20800**（2×40 HP、每擊 13、溢傷確定），
  所以「造成傷害」與「擊殺數」在本實驗**完全不具鑑別力**，真正的貨幣是回合數與殘 HP
- 四格勝率都是 100%，M5 飽和
- 反擊每回合上限 1 次，所以觸發率的天花板是結構性的

### 測試

**296 → 304 全綠**（新增 8）：不變式（攻擊後 AP 永不低於 counterCost）、
不 Guard／不移動／不 Rest、無反擊能力時的退化行為、決定性、
以及「儀器確實比 baseline 保留更多 AP」的對照。
A1–A7、A4 Golden Hash 全通過；batch／replay determinism 實跑核對
（`gym-ctr-v5` seed 1 counter-reserve，兩邊同為 `state_hash 567945887`）。

---

## 2026-08-16（續十）— OD-33 裁決：Prototype Experimental Loop

**性質**：**純文件輪。沒有修改任何程式、資料、encounter 或既有規格。**

### Open Decision

- **[OD-33](OPEN-DECISIONS.md#od-33) 正式登錄並裁決**（專案負責人，2026-08-16）。
  內容：Prototype／Stage／Gym 的範圍界定與迭代流程。
  - **範圍裁決 9 條**：核心是 **Prototype ⊃ Stage**（不再把 Stage 01 視為整個 Prototype）、
    Gym 產 candidate range 不產最終數值、Stage 可推翻 Gym、兩者是迭代非線性、
    Freeze 為暫時且不約束 Production、`stage01.encounter` baseline 不動
  - **方法論裁決 4 條**：依賴真人的變數不強行掃 Gym；Stage playtest 先記錄不評分；
    M1–M7 門檻降為 tripwire（**但暫不改寫正式定義**）；Freeze 所需的 context 數量**刻意不固定**
  - **證據權重**：context-dependent action value 與 trade-off 是核心；
    dominant action 是診斷訊號；**決策數量只是觀察素材**
  - **三層分離**：Engineering correctness / Experimental validity / Gameplay quality
- **OD-32 標記為未使用**（編號保留不重用）

### 新增文件

- **[05-development/prototype-loop.md](05-development/prototype-loop.md)** —— 完整方法論。
  A Gym Exit／B Stage Entry／C Stage Playtest 觀察框架／D Stage Exit／E Prototype Exit，
  加上三種紀錄格式（Candidate Record／Stage Log／Freeze Record）。
  **刻意不含任何數值門檻**
- **[06-validation/candidate-records.md](06-validation/candidate-records.md)** ——
  既有 Gym 結果整理成 **11 筆候選紀錄**（6 筆已交出候選、4 筆 `Stage-only`、
  1 筆重新歸類為儀器）、**8 個 Gym 明確答不了的問題**、
  **第一批 4 張 Stage candidates**（全部使用既有 encounter，零新增內容）

### 本輪的兩個主要發現

- **本專案量到最大的槓桿（`gym-turret`，落差 65）正好是 Gym 跑不動的那一個** ——
  腳本策略不會為了打不動的敵人前進，無 clock 時 33% 僵持。**它從來沒有被真人測過**
- **clock 重新歸類為第 2 層儀器，不是設計旋鈕**。根因是 M3（19–20 回合，目標 6–10），
  clock 只是症狀；D1 已證實縮小 encounter 後 unresolved 為 0%。
  這解決了 HANDOFF §1.1 與 strategy-depth-plan §3.0 的長期不一致
- **量測缺口**：AP 6 從來沒有量過，而候選區間 6–7 正是最可能有東西的一段

### 未處理（明確留給後續）

- charter §2／§4 與 DOCUMENT-MAP 第 42／46／47 列的「Prototype 只做 Stage 01」——
  **等 [OD-31](OPEN-DECISIONS.md#od-31) 裁決後再動**
- experiment-playbook／strategy-depth-plan／playtest-guide 的接線與引用
- 污染系統的三方矛盾、`Shipped*` 測試的分類、Q1 的「5 AP」過期前提
- **`gym-turret` 與 `gym-arena-conflict` 不在 `PrototypeBootstrap.SelectableEncounters` 裡**，
  無法用鍵盤選取。本輪未改程式，已列為待授權事項

---

## 2026-08-16（續十一）— 第一個 Stage 實驗建置：AP economy A/B

**性質**：依 [OD-33](OPEN-DECISIONS.md#od-33) 的 [prototype-loop](05-development/prototype-loop.md) 執行的第一個 Stage 實驗。
**本輪只建置與鎖定預測，尚未進行真人 playtest。**

### 問題

**每回合 3 AP 的零頭，會不會產生 4 AP 模式下不存在的取捨？**
兩臂同為每回合 8 AP（上限 10，[OD-21](OPEN-DECISIONS.md#od-21)），只差攻擊成本：
4 AP 整除（2 攻擊、餘 0）／5 AP 不整除（1 攻擊、**餘 3**）。
零頭的競爭者是**下一回合的第二次攻擊**（留 ≥2 AP 補到上限 10 可雙攻），
**所以取捨從回合內移到跨回合** —— 這是 strategy-depth-plan 的 D3 層目前唯一能被既有旋鈕觸發的形式。

### 為什麼是 `gym-turret`

逐張檢查過 14 張可選 encounter：**除 `gym-opening`（4 人＋技能＋clock，變因太多）外，
沒有任何一張有不會移動的敵人**。其餘全是 rout ＋ 敵人自己過來（[OD-16](OPEN-DECISIONS.md#od-16)）
→ 移動是選配 → **3 AP 零頭沒有去處**。砲塔 `move=0` 是專案唯一把 release 交還玩家的結構。

### 改動（三項，全部最小）

- `units.txt` 新增 **`momotaro_ap5`** —— `momotaro` 的複製，**只有 `attackCost` 4 → 5**。
  無技能、無數值調整。**未採用 `Momotaro_B`**，因為它帶 `pushCost=3`（會變成第二個變因）
- 新增 **`gym-turret-ap5.encounter.txt`** —— `gym-turret` 複製，**只換玩家單位 id**。
  已 diff 確認：地圖／敵人／spawn／AI／objective 逐字相同
- `PrototypeBootstrap.SelectableEncounters` 的 `[` `]` 由 D1 配對改為 turret 配對。
  **D1 兩檔未刪除**，改為 batch-only（其結論已寫入 playtest-metrics §22）

### 新增文件

- **[06-validation/stage-log-2026-08-16-ap-economy.md](06-validation/stage-log-2026-08-16-ap-economy.md)**
  —— 預測已於玩之前鎖定（含「什麼結果代表這張 Stage 測不了」三條），兩場紀錄表待填

### 測試

**317 全綠**（新增 0，既有 0 失敗）。
A1／A1b／A3／A4／A4b／A4c／A4d／A6／A6b 全過，
**A4 golden 常數 `3080245196` / `3711821134` / `1561619701` 未更動**。
`EveryShippedEncounter_LoadsAndValidates`、`ShippedRoster_IsStillPerfectlyDivisible`、
`ShippedStage01Data_LoadsAndValidates` 均通過。
**`Ediki.Core` 零改動**；`stage01.encounter` 與 `gym-turret.encounter` 逐位元未動。

> ⚠️ **[HANDOFF-NEXT-SESSION](HANDOFF-NEXT-SESSION.md) §0 的「193 個測試」已過期，實測 317。**

### 本輪沒有做的事

- **沒有跑任何跑分**，也沒有向專案負責人透露 `gym-turret` 的既有勝率（盲測）
- **沒有 freeze 任何數值**；**攻擊 4 AP 仍是基線**（[OD-01](OPEN-DECISIONS.md#od-01)）

---

## 2026-08-17 — Core 規則層擴充：破甲 ＋ 即死地形，4×2 補齊

**性質**：**本輪修改了 `Ediki.Core`**（專案負責人 2026-08-17 解禁）。
補齊先前登錄為 RULE-GAP 的兩項機制。

### Core 新增

- **`ArmorBreakCommand`**（破甲）：actor ＋ target，射程與成本來自 `UnitDef`。
  - `UnitDef`：`ArmorBreakApCost` / `ArmorBreakRange` / `ArmorBreakAmount` ＋ `CanArmorBreak`
  - `UnitState`：`ArmorBrokenUntilTurn`（**回合戳記**，沿用 Slow／Taunt 的模式）
    ＋ `ArmorBrokenAmount`（扣減值存在**目標**身上，因為結算時施術者可能已死）
  - `BattleState.EffectiveDef(unit)`：**所有傷害計算的唯一入口**，下限 0。
    `ExecuteAttack` 與 `ResolveCounterAttack` 都改走它
  - 不可疊加（與 Slow 同理：重複施放會讓動作組合指標把空轉當成產出）
- **即死地形**：`TerrainDef.IsLethal` ＋ `BattleMap.IsLethal(Coord)`
  - **可通行、非阻擋** —— 進不去的危害就只是一面牆。移動與 Push 結算後判定
  - `MovementCalculator` **排除即死格作為目的地**。
    🔴 **這條是承重的**：`LegalCommands` 由它列舉，而跑分的 15% 雜訊是均勻抽樣，
    留著自殺移動會讓單位在任何有危害的地圖上隨機死亡，污染該地圖上的全部量測
  - `Effects`：`ArmorBreakApplied`、`UnitFellIntoHazard`（與 `UnitDied` 分開，
    因為地形擊殺不算任何人的輸出）
  - 載入器拒絕 `blocks=true lethal=true`（永遠進不去，資料無意義）

### Sim

- `ActionKind.ArmorBreak = 9`（排在 `Taunt` 之後，`IsSkill` 不需改動）
- `TryClassify` ＋ `ComposeKey` 的 `ActionNames` ＋ `LegalCommands` 列舉

### 資料

- `terrain.txt`：新增 `Chasm symbol=x cost=1 lethal=true`。
  **附加在清單最後** —— `TerrainDef.Index` 依宣告順序指派，插在中間會讓既有地圖全部重新編號
- `units.txt`：新增 **`Genjin_B`**（`attackCost=4` ＋ `armorBreakCost=3` / `Amount=20`）。
  **4 角色 × 2 定位至此補齊 8 個變體**

### 🔴 A4 Golden Hash：**沒有重新基準化，也不需要**

`3080245196` / `3711821134` / `1561619701` **一個字都沒改，A4 / A4b / A4c / A4d 全過。**

做法是沿用污染陣列與斬首標記那一套：**新狀態只在真正被用到時才折進雜湊**
（`BattleState.HasArmorBreak` 獨立 gate，不併進 `HasControlStatus`）。
所以沒有破甲的戰鬥**逐位元等同於這個機制存在之前**。

> **這正是 [workflows §6](05-development/workflows.md) 要求先問的那個問題**：
> 「這次改動應該改變模擬結果嗎？」——**不應該**（沒用到新機制的戰鬥不該有任何差別），
> 而測試證實它沒有。**A4 因此仍然是確定性守門員，不是橡皮圖章。**

### 測試

**317 → 330 全綠**（新增 13）。
新檔 `ArmorBreakAndHazardTests.cs`（11 條）＋ `DataTests` 兩條（8 變體載入、破甲跨刀數階梯）。
A1–A7 全過；`Ediki.Core` 零引擎相依（A1 確認）。

> `[A]` **一條既有測試抓到了我的疏漏**：`ComposeKey_NamesEverySkill` 會走訪整個
> `ActionKind` 列舉並要求每個都有名字 —— 我加了列舉值卻忘了 `ActionNames`，它立刻失敗。
> **那條測試就是為了這個失敗模式寫的，它做到了。**

### ⚠️ 尚未完成的治理工作（本輪未做，需另行處理）

- **沒有登錄 OD**。[playbook §1.1](05-development/experiment-playbook.md) 要求
  「新的動作 → 動 `Ediki.Core` → **先登錄 OD**」。本輪依專案負責人直接授權執行，
  **但 OD 條目仍然缺席**
- **沒有新增 `R-xxx` 規格條目**。破甲與即死地形目前**只有程式與測試，沒有規格**，
  違反 [documentation-rules §1](99-governance/documentation-rules.md) 與
  [test-strategy §3](06-validation/test-strategy.md)「每條 R-xxx 至少一個測試」的反向要求
- **`ADR-0004` 的敘述未更新**（它描述的 Clone 契約現在多了兩個欄位）
- **沒有任何 encounter 使用 `Chasm` 或 `Genjin_B`** —— 機制在線，尚未進入任何實驗

---

## 2026-08-17（續一）— 治理補齊 ＋ 4 人全陣容考卷 `gym-squad-crucible`

### 治理（補登錄前一輪的 Core 擴充）

- **[OD-34](OPEN-DECISIONS.md#od-34) 登錄**：破甲與即死地形。
  記錄實作決定（回合戳記、扣減量存目標身上、`EffectiveDef` 為唯一入口、
  尋路排除即死格）、A4 未變的理由，以及**五項仍待裁決的參數**
  （破甲量 20／時效一回合／破甲該不該是玄真的／即死地形要不要留／「停下來才判定」的語意）。
  > ⚠️ 本 OD 明記「流程順序被違反了」—— playbook 要求先登錄再實作，本輪是先實作後登錄。
- **[R-COMBAT-26/27/28](03-spec/SPEC-combat.md)**：破甲的傷害結算、時效、不可疊加。
  > ⚠️ **原指示要求 `R-COMBAT-24`，但該 ID 已被「傷害公式資料化」佔用。**
  > 依 [workflows §3](05-development/workflows.md)「ID 不重用」順延為 26–28，兩份文件都註明了。
- **[R-TERR-07..10](03-spec/SPEC-grid-terrain.md)**：即死地塊的結算、通行性、**尋路排除**、不受減傷影響。
  R-TERR-09 明記它是「量測有效性」規則而非玩家體驗規則。
- **[ADR-0004](07-adr/ADR-0004-hand-written-clone.md)**：新增「後續變更」章節，
  列出四次 Clone 契約擴充，並說明**破甲為什麼需要兩個欄位而不是一個**
  （只有戳記會讓兩個扣減量不同的狀態雜湊碰撞）。
  `[A]` 三個重新評估觸發條件皆未成立，本 ADR 維持 Accepted。

### `gym-squad-crucible` —— 第一張非全滅目標的可玩關卡

`objective type=defend turns=6`，16×11，三路複合 ＋ 連續增援。

| | |
|---|---|
| **玩家** | `Momotaro_B`（5AP＋擊退）／`Genjin_B`（4AP＋破甲）／`Kagemaru_A`（R3＋遲滯）／`Masamori_A`（DEF70＋嘲諷）—— **4×2 全套首次同場** |
| **守護目標** | `sc_shrine` HP 100，`protect=true`，位於 (8,9) |
| **威脅排程** | T1 西路 `sc_bruiser`(180HP) ＋ 中路 `sc_shield`(140HP/DEF35)；**T2** 東路 `sc_assassin`(MOVE5/ATK95)；**T3** 北側 `sc_sniper`(R3/ATK60) |
| **地形** | 西路 1 格 `Chasm`(即死) ／ 中路平原 2 格缺口 ／ 東路森林＋泥沼（2–3 AP/格） |

**設計原則：一路一解，且沒有一個解能覆蓋兩路。**
擊退對應深坑（一個動作跳過 180 HP）／破甲對應 DEF 35（影丸 7 傷 → 27 傷）／
遲滯對應 MOVE 5（無 ZOC，走不掉也擋不住）／嘲諷對應射程 3（唯一來得及的答案）。

**狙擊手 `attackCost=5` 是承重的**：4 的話它一回合兩發 100 傷，抵達當回合直接抹掉 100 HP 神龕
—— 那是即死不是決策。5 讓神龕撐得住兩發，玩家有一回合可以反應。

`[A]` **新增 5 個單位**（`sc_` 前綴，**無 GDD 來源，刻意不取有設計意涵的名字**）。

### 測試

**330 全綠**（與前一輪同數，本輪未新增測試）。
A4/A4b/A4c/A4d 全過，**golden 常數再次未更動** —— 新增 encounter 與資料不影響規則層。
`EveryShippedEncounter_LoadsAndValidates` 涵蓋新關卡（含連通性）。

### 尚未完成

- **`Chasm` 與 `Genjin_B` 現在有使用者了，但 `Kagemaru_B` / `Masamori_B` / `Momotaro_A` / `Genjin_A` 仍未進任何 encounter**
- **OD-34 的五項參數全部未裁決** —— 本關卡是在未裁決的參數上跑的，結論要連同這一點一起讀

---

## 2026-08-17（續二）— 三項缺陷修復 ＋ 每回合行動上限

依 2026-08-17 五份研究筆記（T1 行動時序／T2 意圖預告／T3 碰撞阻擋／裝備被動／雙資源池）的 §6 回報執行。
**三項缺陷全部是讀程式碼發現的，沒有一項是在跑分中觀察到的。**

### 第 1 階段：缺陷修復

**🔴 增援死鎖（T3 §6）** —— 最嚴重的一項：
波次宣告於回合 N，若該格當時被**任一陣營**的單位佔住，`Spawned` 永遠維持 false
（舊碼比對 `r.Turn != TurnIndex`，下一回合就再也不試），
而 `HasPendingReinforcements` 只讀 `Spawned`、勝利判定又綁在它上面
→ **玩家清空戰場後戰鬥永不結束**。R-WIN-04 明文無回合上限、無平手，規則層沒有逃生口。

兩道獨立防線（缺一都留洞）：
1. **逾期重試**：`r.Turn > TurnIndex` 才跳過 —— 這是舊註解宣稱、但程式碼沒做的行為
2. **就近落地**：`TryFindSpawnCell` 以 BFS（`Neighbors` 順序由 A6 釘死，故決定性）找最近可用格，
   半徑上限 2，排除即死格。**只有重試會對「永遠不動的佔位者」再次死鎖** —— 例如玩家把單位停在增援點上，
   那是完全合法而且相當明顯的破壞方式

**目標距離看不見牆（T2 §6）**：`SelectTarget` 用曼哈頓距離、`SelectPosition` 用路徑距離。
[OD-18](OPEN-DECISIONS.md#od-18) 在 2026-08-14 修好了後者，**同一個修正從未套用到前者** ——
單位會選一個「隔著牆很近」的目標，然後繞遠路過去。已統一為路徑距離
（只在 `Nearest` 偏好時計算，另外兩種偏好不看位置）。

**SPEC 漂移回寫（T1 §6）**：`SPEC-battle-flow.md` 的 R-AP-01/02/03、§2.1、§2.2、R-TURN-04
停留在 [OD-21](OPEN-DECISIONS.md#od-21)（08-14）與 [OD-01](OPEN-DECISIONS.md#od-01)（08-13）之前。
**Source of Truth 是錯的，正確資訊只存在於 `units.txt` 與 OD 索引列。** 已全部回寫，新增 R-AP-07（攻擊 4 AP）。

### 第 2 階段：每回合行動上限

`UnitDef.AttacksPerRound` / `SkillUsesPerRound`（**0 = 不限制**，故出貨 roster 行為零改變）
＋ `UnitState.AttacksThisRound` / `SkillUsesThisRound`（隨 AP 一起在階段開始重置）。

**它關的是 AP 關不住的洞**：`Ap = min(殘留 + 8, 10)`，所以存 2 AP 就讓 5 AP 攻擊者
在「定價只允許一發」的成本下打出兩發。技能側更尖銳 —— 1 AP 定價下規則層容許單回合 8–10 次激活。

- 五個動詞（嘲諷／遲滯／擊退／淨化／破甲）**共用一個計數器**
- 🔴 **`LegalCommands` 同步排除**：那份清單是跑分 15% 均勻雜訊的抽樣來源，
  留在裡面的非法指令不只是失敗，而是**燒掉該單位整個激活**並被記成策略做的決定
- `StateHasher` 閘門以「有沒有單位帶上限」為條件，**不是以計數器非零為條件**
- `units.txt`：`Momotaro_B`／`Genjin_A`／`Kagemaru_A` 加 `attacksPerRound=1`；
  `Momotaro_B`／`Genjin_B`／`Kagemaru_A`／`Masamori_A` 加 `skillUsesPerRound=1`

### 測試

**330 → 338 全綠**（新增 8，新檔 `HardeningTests.cs`）。
**A4 golden 常數再次未更動** —— 三項修正沒有一項改變既有 golden 場景的模擬結果。
（`SelectTarget` 的修正**有可能**改變 A4；實測沒有，因為 golden 場景裡 AI 與目標之間沒有牆。）

> `[A]` **我自己的測試錯了一次**：波次在**自己陣營的階段**開始時到達，
> 所以敵方 turn=2 的增援是在第 2 回合的**敵方**階段落地，不是玩家階段。修的是測試，不是程式。

### ⚠️ 第 3 階段的一個前提不成立

指示要求驗證「攻擊 1 刀 ＋ 保留 3 AP 反擊」的權衡。**`gym-squad-crucible` 裡沒有任何單位有反擊**
（用的是 `Masamori_A` 嘲諷版，`counterCost` 在 `Masamori_B` 上）。
且 T1 §4.5 已算出：正守反擊 ATK 33 對 DEF 20 是 13 傷、對 DEF 35 是保底 1 傷 —— **穿不過任何刀數階梯**。
**「攻擊 ＋ 擊退」的權衡成立且可測（`Momotaro_B` 10 AP 回合＝攻擊 5 ＋ 擊退 3 ＋ 餘 2），
「攻擊 ＋ 保留反擊」不成立，要等反擊本身被修好。**

---

## 2026-08-18 — Prototype Editor（企劃用關卡編輯器）＋ 正守 id 換手

**性質**：新增一個 Editor-only 工具，並依專案負責人裁決做了一次會影響量測基線的資料換手。
**未修改 `Ediki.Core`，未修改 `StateHasher`。A4 三個常數 `3080245196` / `3711821134` /
`1561619701` 一個字都沒動。**

### 新增：`editor-roster.txt`（Editor-only metadata）

`Assets/_Project/Resources/Data/editor-roster.txt`。

**為什麼需要它**：反查確認專案**沒有任何地方記錄「哪個 unit 屬於哪一方」**——
`UnitDef` 沒有 faction 欄位，`UnitLoader` 不讀陣營，陣營只存在於 encounter 的
`spawn faction=` 那一行。這對規則層是正確的（規則層不需要知道），但讓工具沒有東西
可以過濾選單。

- 格式：`character id= name= faction=player|enemy|objective` ＋ `variant char= unit= label= note=`
- **只寫 id 與分組，沒有任何數值**，所以不可能與 `units.txt` 漂移
- **遊戲、`Ediki.Sim`、測試套件都不讀它**；沒列進去的 unit 照常載入與遊玩
- 未列入的例子：`e4_backline`（`gym-e4-protection` 把它當**我方**單位放，
  `units.txt` 說它是「always the lowest-HP party member」）——量測夾具不該被工具判定陣營

四名我方角色的來源（三處互相印證，非推測）：
GDD `extracted.txt:34` 的討鬼團名單、GDD 的四人定位表、
`SquadMatrix.Characters`、`units.txt:320`「complete at 4 characters x 2 roles」。

### 🔴 資料換手：`zhengshou` → `Masamori_A`（**會移動量測基線**）

專案負責人 2026-08-18 裁決。**只換了三個檔**：

| 檔案 | 換手理由 |
|---|---|
| `gym-lanes-pair` | 檔頭自述「a second player unit (正守)」——正守是以角色身分在場 |
| `gym-rules-percent` | 同上，且兩檔仍互為 byte-identical 對照 |
| `gym-rules-subtractive` | 同上 |

`HP 435 / ATK 33 / DEF 70 / MOVE 3` 逐欄不變，所以三個檔關於「DEF 70 擋下 30 傷」
的推論仍然成立。**新增的是【嘲諷】3 AP、免疫擊退、每回合技能 1 次。**

> ⚠️ **換手前跑出來的數字不能與之後的直接比較。** 三個檔內都加了註記說明。

### ⚠️ 刻意未換的檔案（換了會讓檔案自我矛盾）

| 檔案 | 為什麼不換 |
|---|---|
| `gym-opening-noskill` | 檔頭自述「**the control kit switched OFF. The CONTROL, not a stage**」。`Masamori_A` 帶嘲諷，**換了就等於給無技能對照組一個技能** |
| `gym-opening-skill0` | 「the control kit OFF. **The zero point of the skill-cost sweep**」。同上 |
| `zhengshou_taunt`（4 檔） | `tauntCost=1`；`Masamori_A` 是 3。`gym-opening` 換了就與 `gym-opening-skill3` 重合，等於刪掉一個資料點 |
| `zhengshou_taunt_c1..c5` / `_g5` / `zhengshou_ctr4/5`（9 檔） | 這些是**參數階梯上的一格**，不是角色。換掉等於毀掉整條掃描 |

> 這些檔案裡的 `zhengshou*` 是**儀器**，不是正守這個角色。
> 它們在編輯器裡會顯示「（不在名單內）」，這是正確的分類結果。

### 未完成的治理

- **沒有登錄 OD**。這次換手影響 `gym-opening` 系列與 `gym-rules` 對照的可比較性，
  依 [experiment-playbook](05-development/experiment-playbook.md) 應該有一條紀錄
- **`SPEC-unit-data.md` 未更新** —— 正守目前有兩組 id（`zhengshou` / `Masamori_*`），
  **改名原因在專案裡完全沒有記錄（UNKNOWN）**，規格也沒說哪一組是 canonical

---

## 2026-08-20（續一）— OD-35：狀態系統規則語意裁決

**性質**：規格與治理，**無程式碼變更**。

### 裁決

[OD-35](OPEN-DECISIONS.md#od-35)：16 題一次裁決，建立狀態系統的完整規則語意。
`prototype-charter.md §4` 的狀態列表否決權**部分解除**（六個狀態納入範圍，
暈眩／加速／緩速仍在範圍外），**原列保留並劃線標註**。

### 新增

- [`03-spec/SPEC-status-effects.md`](03-spec/SPEC-status-effects.md)：R-STATUS-01..14。
  13 條 `STABLE`、1 條 `OPEN`（清除優先序，依裁決另定）
- **Prototype 偏離登記簿**（SPEC §0）：五條 Prototype 與 GDD 的刻意差異，各附回收條件。
  **「必中」僅為 Prototype 實驗規則，GDD 為設計正本、不因此修改**

### 兩處與既有設計刻意相反（皆有記錄）

- **`RemainingPhases` 倒數**取代既有的回合戳記。戳記以 round 為單位，
  無法表達裁決要求的 phase 語意。既有四個狀態不改寫
- **重複施加＝刷新**，與 Slow／破甲／引誘的「拒絕」相反。
  既有理由（防止把 AP 倒進假動作而污染量測）對地形來源不適用

### 修正一項先前的錯誤敘述

本規格前一版稱「重排 `StatusKind` 列舉會讓 A4 與所有存檔失效」。**該敘述已修正。**
只要 `HasStatuses=false` 的 gate 正確，無狀態的戰鬥就不折入狀態資料，
雜湊逐位元不變 —— A4 三個常數建立在無狀態的 `TestWorld` 上，**不會受列舉值影響**。
真正受影響的是含狀態的雜湊（未來的 golden 常數、跨版本 replay）。

### 順帶發現，未修（需另開決議）

`BattleHeatmap.CountDamage` 以「`HpChanged` 且 `Delta < 0`」為傷害訊號，
而致命地形也發負的 `HpChanged`。**被推進深坑的單位目前在熱圖上登記為該格的一筆大額傷害。**
屬既有缺陷，與狀態無關；修正會改變已發表數字。
