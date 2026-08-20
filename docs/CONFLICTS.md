# Documentation Conflicts

**這份文件登錄既有文件之間互相矛盾的地方。**

規則：**不自行選一個當正確答案。** 每一條都標記 `CONFLICT`，列出雙方說法、影響範圍、
需要誰裁決，以及在裁決前 implementation 應如何避開。

裁決後：把結論寫進對應的 Specification，在本表把狀態改成 `RESOLVED`，
並在 [CHANGELOG.md](CHANGELOG.md) 留下記錄。**不要刪除已解決的條目**，
未來需要知道規格為什麼長這樣。

| ID | 主題 | 狀態 | 嚴重度 |
|---|---|---|---|
| [CONFLICT-01](#conflict-01) | 移動成本模型：固定 1 AP vs 地形成本 | ✅ **RESOLVED**（OD-04，2026-08-13） | — |
| [CONFLICT-02](#conflict-02) | Stage 01 地形組成 | ✅ **RESOLVED**（OD-02，2026-08-13） | — |
| [CONFLICT-03](#conflict-03) | 小耗數值 | ⚠️ **PARTIAL** — 封包作廢，但 `DEF 20` 仍無來源 | Low |
| [CONFLICT-04](#conflict-04) | 命中模型：HIT/EVA vs 必中 | ✅ **RESOLVED**（OD-05，2026-08-13） | — |
| [CONFLICT-05](#conflict-05) | 剩餘 AP 是否影響下回合 | ✅ **RESOLVED**（OD-21，2026-08-14） | — |
| [CONFLICT-06](#conflict-06) | Unity 版本與 C# 語言版本 | ✅ **RESOLVED**（查證，2026-08-13） | — |
| [CONFLICT-07](#conflict-07) | 傷害公式無來源 | ✅ **RESOLVED**（OD-05 取代公式，2026-08-13） | — |
| [CONFLICT-08](#conflict-08) | 死亡語意：昏厥 vs 消滅 | OPEN（Stage 01 無行為差別） | Low |

---

## CONFLICT-01

### 移動成本模型：固定 1 AP/格 vs 每格地形成本

> ## ✅ RESOLVED — 2026-08-13，專案負責人
> **採文件 B（地形成本）。** 見 [OD-04](OPEN-DECISIONS.md#od-04)。
> Open/Road = 1、Forest/Highland = 2、Blocking = impassable，4-directional。
> **GDD 的「移動：1 AP」在 Stage 01 Prototype 視為已被此裁決取代。**
> 依 [governance §5](99-governance/documentation-rules.md)，`00-source/` 不修改。

**文件 A — GDD**
- 〈三、核心戰鬥系統 / 1. AP 行動點系統〉：「移動：1 AP」
- 〈Stage 01 / 二、玩法設計與學習機制〉：「練習基礎移動：消耗 1 AP / 格」

GDD 全文**沒有任何地形移動成本表**。GDD 的〈3. 地形系統 (Terrain Bonus)〉只講
「特定角色具備地形加成」（正守碎石、玄真森林、影丸高地），是**戰鬥加成**，不是移動成本。

**文件 B — SPEC v0.1 §3.2**
- 主張「原始規格同時寫了『移動 1 AP/格（受 MOVE 上限）』和『地形成本 道路 1 / 碎石 2 / 森林 2 / 高地 3』」
- 並自行採用讀法 (A)：「進入一格消耗該格的地形成本 AP；MOVE 是該回合可移動的格數上限」

**追加問題：SPEC v0.1 所稱的「原始規格」在本專案封存的文件中不存在。**
封存的 GDD 沒有那張地形成本表。地形成本 `道路1/碎石2/森林2/高地3` 目前
**沒有任何可查證的來源文件**。

### 影響範圍
- 移動範圍計算（Dijkstra flood fill 的 edge weight）
- 威脅範圍計算 → 直接影響 Exposure，也就是整個 Prototype 的核心命題
- Stage 01 地圖是否成立（SPEC v0.1 §5.3 的「黃金格」推導完全建立在地形成本上）
- SPEC v0.1 §3.2、§3.3、§4 的所有推算數字

### 需要誰裁決
**企劃（GDD 作者）。** 這是設計問題，不是技術問題。

### 裁決前如何避免依賴
1. `IMovementCostModel`（或等價的資料查詢介面）必須從第一天就存在，成本一律從資料讀，
   不得在程式碼寫死 `1`。見 [SPEC-movement.md](03-spec/SPEC-movement.md)。
2. 兩種模型都要能只靠改資料切換：`FlatCost`（全部 1）與 `PerTerrainCost`。
3. **不要實作任何依賴特定成本值的關卡驗證或平衡邏輯**，直到裁決完成。

---

## CONFLICT-02

### Stage 01 的地形組成

> ## ✅ RESOLVED — 2026-08-13，專案負責人
> **採定位 B。** [OD-02](OPEN-DECISIONS.md#od-02) 裁定 Stage 01 存在 Blocking Terrain，
> 地形集合為 `Open / Road / Forest / Highland / Blocking`。
> GDD 的「環境純粹為森林與道路」在 Stage 01 Prototype 視為已被此裁決取代。
> Prototype 地圖見 `Assets/_Project/Resources/Data/stage01.encounter.txt`。

**文件 A — GDD〈Stage 01 / 三、初期戰場配置〉**
> 地圖規模：小型戰場，無神社、無村莊，環境純粹為森林與道路

以及〈二、玩法設計〉：「雖然此關地形簡單」。

**文件 B — SPEC v0.1 §5.2 灰盒地圖**
12×10，含 `#` 阻擋、`f` 森林、`r` 碎石、`^` 高地、`▓` 隘口、`.` 道路 —— 六種地形，
且大量牆體構成的隘口是地圖的核心。

### 影響範圍
- Stage 01 地圖資料
- 「黃金格 (5,5) Exposure = 1」這個 Prototype 主要驗證目標能否成立
- 教學意圖：GDD 認為 Stage 01 是「無壓力操作試煉」，SPEC 認為是「Exposure 教學關」

### 需要誰裁決
**企劃。** 這是「Stage 01 到底要教什麼」的定位衝突，牽動 Prototype 的成功定義。

### 裁決前如何避免依賴
- 地圖一律用外部資料（見 [SPEC-grid-terrain.md](03-spec/SPEC-grid-terrain.md)），
  不得寫死在場景或程式碼裡。
- 至少準備兩張地圖資料：GDD 版（純道路＋森林）與 SPEC 版（含隘口）。
  兩張都能跑，才有辦法用數據回答「Exposure 是不是勝負的主要解釋變數」。
- **不要把任何一張地圖當成「正式的 Stage 01」**，在文件與資料檔名上都保持中立。

---

## CONFLICT-03

### 小耗數值

> ## ⚠️ PARTIAL — 2026-08-13
> [OD-05](OPEN-DECISIONS.md#od-05) 把傷害公式改成 `max(1, ATK-DEF)`，
> **兩個數值封包（封包 1／封包 2）連同其所有推算全部作廢** ——
> 它們建立在除法公式上。Stage 01 因此回到 **GDD 原始數值**：`HP 80 / ATK 100 / MOVE 3`。
>
> **仍未解決**：`DEF 20` 在 GDD 中不存在，Prototype 沿用 SPEC v0.1 的值。
> `HIT 0.60` / `EVA 0.12` 因採 Deterministic Hit 已無用，不再構成衝突。
>
> **待企劃確認**：小耗的 DEF 應該是多少？（目前 20，影響桃太郎的 TTK：`60-20=40` → 2 刀）

| 來源 | HP | ATK | DEF | MOVE | HIT | EVA |
|---|---|---|---|---|---|---|
| **GDD**（凶星鬼階層 + Stage 01） | 80 | 100 | — | 3 | — | — |
| **SPEC v0.1 §4.1 封包 1** | **50** | 100 | 20 | 3 | 0.60 | 0.12 |
| **SPEC v0.1 §4.2 封包 2** | 80 | **50** | 20 | 3 | — | — |

兩個封包都偏離 GDD，且偏離的方向相反。

**另外**：`DEF 20`、`HIT 0.60`、`EVA 0.12` 這三個值在 GDD 中不存在，
是 SPEC v0.1 引入的新數值，**沒有來源**。

### 影響範圍
- Stage 01 的可勝性。SPEC v0.1 §4 已證明照 GDD 原數值跑是**必輸**的
  （清場需 11.4 回合，即使 Exposure 1 也會累積 360 傷 > 300 HP）。
- 所有 TTK 推算
- 手感驗收指標的基準線

### 需要誰裁決
**企劃。** 且必須與 [CONFLICT-04](#conflict-04)（命中模型）**一起裁決**，
因為封包 1／封包 2 是綁定的組合，混用會產生第三種未經檢算的數值。

### 裁決前如何避免依賴
- 單位數值全部進資料資產，程式碼零字面值（架構測試 A5 強制）。
- 兩個封包都建成資料檔，切換不需重編譯。
- **不要用任何一組數值做平衡結論或關卡調整。**

---

## CONFLICT-04

### 命中模型：HIT/EVA 隨機命中 vs 攻擊必中

> ## ✅ RESOLVED — 2026-08-13，專案負責人
> **採 Deterministic Hit（文件 B）。** 合法 Attack → 必定 Hit → Damage。
> 不加入 HIT / EVA / Critical RNG。見 [OD-05](OPEN-DECISIONS.md#od-05)。
>
> **後果**：GDD 的 `HIT` / `EVA` 欄位在 Stage 01 Prototype **不使用**。
> `IRandomSource` 介面**目前不建立** —— 沒有第二個實作要跑，建立它違反
> [extension-points 的三項判準](04-architecture/extension-points.md)。
> 未來若恢復隨機命中，再引入介面（那時才有兩個實作）。

**文件 A — GDD**
每個單位都有 HIT 與 EVA（桃太郎 HIT 80% / EVA 20%；荒世主 HIT 0.90 / EVA 0.05 …），
且多個技能明確依賴命中機率（正守封行拳套「30% 機率暈眩」、白虎狂暴「攻擊必中」作為特例）。
**GDD 的整套技能與裝備體系建立在隨機命中之上。**

**文件 B — SPEC v0.1 §4.2 / §8.5**
建議封包 2：移除命中骰，攻擊必中。理由是命中骰會污染 AP 手感的測量。
標記為 `[BLOCKER]`，**尚未拍板**。

**文件 C — RESEARCH（僅為建議，無權威）**
建議必中或 FE 式 2RN。

### 影響範圍
- 傷害/命中規格（[SPEC-combat.md](03-spec/SPEC-combat.md)）
- 是否需要 RNG → 直接決定確定性測試的複雜度（SPEC v0.1 §6.6：封包 2 下 Core 完全不需要亂數）
- 與 GDD 全遊戲的相容性：若 Prototype 採必中，未來要把 GDD 的技能體系接回來會有系統性落差
- 五個驗收問題中的 Q4（「命中骰有沒有讓玩家把失敗歸因到運氣」）本身就預設了要測兩種

### 需要誰裁決
**企劃 + 程式共同。** 這是設計哲學與技術策略的交叉決策。

### 裁決前如何避免依賴
- `IRandomSource` 介面從第一天就存在，兩個實作（`AlwaysHit` / `Seeded`）都要有。
- 規則層不得直接呼叫任何 RNG，一律透過介面。
- **不要在表現層或 UI 寫死「一定會命中」的假設**（例如省略 miss 的播放路徑）。

---

## CONFLICT-05

### 剩餘 AP 是否影響下回合

> ## ✅ RESOLVED — 2026-08-14，專案負責人
> **AP 跨回合保留**，上限 10、每回合恢復 8。見 [OD-21](OPEN-DECISIONS.md#od-21)。
> GDD Stage 01 的「AP 剩餘點數對下一回合的影響」因此有了具體機制，
> 文件 A 與文件 B 的矛盾消失。
> 附帶：新增「休息」動作（2 AP，回復 10% HP，進入待機）。

**文件 A — GDD〈Stage 01 / 五、玩家獲取與獎勵〉**
> 戰術成長：理解 AP 剩餘點數對下一回合的影響（如防禦或道具保留）

這句話暗示剩餘 AP 有跨回合意義，但**沒有定義機制**。

**文件 B — SPEC v0.1 §3.1 / §8.4**
> 每個單位每回合 8 AP，回合開始時重設（**不跨回合保留**）

§8.4 把「要不要保留」列為待決，建議 Stage 01 先不保留。

### 影響範圍
- 回合與 AP 規格
- 「殘 AP 浪費率 < 15%」這個驗收指標的意義（若不保留，浪費率高就是設計問題；若保留，浪費就不是浪費）
- AP 制產生決策深度的三條件之一

### 需要誰裁決
**企劃。**

### 裁決前如何避免依賴
- 依 SPEC v0.1 的「不保留」實作，但把「回合開始時的 AP 補充規則」做成資料驅動的策略點，
  不要把 `ap = 8` 直接寫在回合開始的程式碼裡。
- GDD 那句話也可能只是在講「這回合留 3 AP 做防禦」的回合內配置，而非跨回合。
  裁決時請一併澄清原意。

---

## CONFLICT-06

### Unity 版本與 C# 語言版本

> ## ✅ RESOLVED — 2026-08-13，查證結果
> 查 `Assembly-CSharp.csproj`：
> ```
> <LangVersion>9.0</LangVersion>
> <TargetFrameworkVersion>v4.7.1</TargetFrameworkVersion>
> ```
> **Unity 6000.5.1f1 的 C# 語言版本確實是 9.0。**
> SPEC v0.1 §6.1 的**版本號記載錯誤**（寫 6000.0 / 6000.3），
> 但**語言版本結論正確**，其列出的不可用特性清單（`record`、`init`、`required`、
> file-scoped namespace、collection expressions、global using）**成立**。
>
> 附帶發現：API 相容層是 **.NET Framework 4.7.1**，不是 SPEC v0.1 記載的 .NET Standard 2.1。
> 這不影響 Prototype，但若日後要做「Core 用 `dotnet test` 在 Unity 外跑」，
> 需要把 Core 的 API 相容層改成 .NET Standard 2.1。**目前不做。**

**文件 A — SPEC v0.1 §6.1〈環境事實（已核對官方文件）〉**
> Unity 6.0 或 6.3 LTS（6000.0 / 6000.3）／ C# 語言版本 **9.0**

並據此列出不可用的語言特性（`record`、`init`、`required`、file-scoped namespace、
collection expressions、global using），且推導出「`BattleState` 用一般 class，不要用 record」。

**文件 B — 實際專案**
`ProjectSettings/ProjectVersion.txt`：
```
m_EditorVersion: 6000.5.1f1
```

專案實際使用 **Unity 6000.5.1f1**，不在 SPEC v0.1 記載的版本範圍內。

### 影響範圍
- Coding guidelines 的「不可用語言特性」清單可能不正確
- ADR-0004（手寫 Clone 而非 record）的理由之一建立在 C# 9 限制上

### 需要誰裁決
**程式。** 這不是設計決策，是一個**可驗證的事實**，不需要討論，只需要查證。

### 裁決前如何避免依賴
- 在 Unity 6000.5.1f1 下實測 C# 語言版本（建一個含 `record` 的暫時檔看是否編譯通過），
  更新 [coding-guidelines.md](05-development/coding-guidelines.md)。
- 即使 `record` 可用，ADR-0004 的**主要**理由仍然成立：
  `with` 是淺複製，巢狀 `List<Unit>` 會被兩個 state 共用，AI 推演會污染真實狀態。
  語言版本只是次要理由。

---

## CONFLICT-07

### 傷害公式沒有可查證的來源

> ## ✅ RESOLVED — 2026-08-13，專案負責人
> **孤證公式 `ATK × 100/(100+DEF)` 被取代。**
> Stage 01 Prototype 採 **`Damage = max(1, ATK - DEF)`**（減法）。
> 見 [OD-05](OPEN-DECISIONS.md#od-05)。
>
> **連帶作廢的內容**（都建立在除法公式上）：
> - SPEC v0.1 §3.4 的 DEF 減傷率表（20→16.7%…）
> - SPEC v0.1 §4 的兩個數值封包與全部 TTK / 承傷推算
> - 「防禦不能做成 DEF 加成」的**推導理由**（結論碰巧仍與裁決一致，見 [OD-06](OPEN-DECISIONS.md#od-06)）
>
> ⚠️ **減法公式有已知的設計特性**（RESEARCH §6）：它製造「懸崖」——
> 防禦低於門檻幾乎無敵、高於門檻幾乎無意義。目前數值下：
>
> | 方向 | 計算 | TTK |
> |---|---|---|
> | 桃 → 小 | `60 − 20 = 40` | 小耗 80 HP → **2 刀** |
> | 小 → 桃 | `100 − 50 = 50` | 桃太郎 300 HP → **6 刀** |
>
> 攻擊 4 AP ⇒ 相鄰時雙方每回合可攻擊 2 次 ⇒
> 桃太郎每回合恰好擊殺 1 隻小耗（清場 4 回合）；
> 每隻相鄰小耗每回合造成 100 傷（桃太郎撐 3 回合 @ Exposure 1）。
> **這組數字下 Exposure 1 仍然會輸**，是裁決的直接推導結果。
> 記錄於此供跑分後評估，**不在本輪自行調整數值**。

**文件 A — SPEC v0.1 §3.4**
> **傷害公式**（確定）：`傷害 = ATK × 100 / (100 + DEF)`，無條件捨去

標記為「確定」。

**文件 B — GDD**
**全文沒有任何傷害公式。** GDD 只給了 ATK / DEF 的數值，沒有說它們怎麼組合。

**文件 C — RESEARCH §6**
> 你選的百分比減傷(ATK×100/(100+DEF))

以「你選的」稱之，即它是從別處得知的既有設定，RESEARCH 本身不是來源。

### 為什麼這是 CONFLICT 而不只是缺口
SPEC v0.1 把它標成「確定」，會讓讀者以為它有 GDD 背書。實際上它是**孤證**：
唯一的書面來源是 SPEC v0.1 自己。整份 §4 數值封包、所有 TTK 推算、
「防禦不能做成 DEF buff」的結論全部建立在這條公式上。

### 影響範圍
幾乎所有戰鬥數值。

### 需要誰裁決
**企劃確認。** 若確認無誤，請把公式補進 GDD 或明確授權 SPEC 層作為來源，
讓它不再是孤證。

### 裁決前如何避免依賴
- 傷害計算做成單一可替換的函式，不要散落在多處。
- 在 [SPEC-combat.md](03-spec/SPEC-combat.md) 標註其來源狀態為「未經 GDD 確認」。

---

## CONFLICT-08

### 死亡語意：昏厥 vs 消滅

**文件 A — GDD〈狀態列表 / 3. 生存狀態定義〉**
- 昏厥 (ZERO/UNCONSCIOUS)：**人類單位**生命值歸零時的狀態
- 消滅 (ZERO/ELIMINATED)：**鬼族單位**生命值歸零時直接從戰場移除

兩者是不同的狀態，且 GDD 有關卡（Stage 09/11）把「特定角色昏厥」當成失敗條件。

**文件 B — SPEC v0.1 §6.3 Effect 清單**
只有一個 `UnitDied`，沒有區分。§3.5 勝敗條件也只寫「桃太郎 HP 歸零」。

### 影響範圍
- Effect 粒度與表現層播放（昏厥與消滅的演出不同）
- 未來關卡的失敗條件（Stage 09「影丸昏厥」、Stage 11「玄真昏厥」）

### 需要誰裁決
**程式可自行決定 Effect 粒度**，但需要企劃確認語意差異在 Stage 01 是否有實際行為差別
（Stage 01 只有桃太郎與小耗，人類/鬼族各一方，行為上可能無差別）。

### 裁決前如何避免依賴
- `UnitDied` Effect 帶上足以區分的欄位（例如 `unitId` 可查到陣營），
  讓表現層日後能分流，而不需要改規則層。
- **不要**在 Stage 01 就實作兩套狀態機。
