# Ediki-Tactics-Prototype — Documentation

**目前狀態：可執行、可遊玩、可測量。**

按 Unity 的 **Play** 就能玩 —— 開場載入 `gym-big-split`，
目前唯一一張自動化指標全部合格的地圖。

**下一步是真人 playtest** → [06-validation/playtest-guide.md](06-validation/playtest-guide.md)

> 📋 **接手新會話請先讀 [HANDOFF-NEXT-SESSION.md](HANDOFF-NEXT-SESSION.md)**

---

## 先讀這三份

不管你是誰，開始之前先讀完這三份：

1. **[01-vision/prototype-charter.md](01-vision/prototype-charter.md)** — 這個 Prototype 要證明什麼、不做什麼
2. **[OPEN-DECISIONS.md](OPEN-DECISIONS.md)** — 哪些事還沒拍板
   （原本的 7 個 BLOCKER 已全部裁決；目前主要未決是 OD-19 與 OD-23）
3. **[CONFLICTS.md](CONFLICTS.md)** — 既有文件互相矛盾的地方

**這三份決定了你能做什麼、不能做什麼。**

---

## 依角色的閱讀路徑

### 企劃

| 順序 | 文件 | 為什麼 |
|---|---|---|
| 1 | [01-vision/prototype-charter.md](01-vision/prototype-charter.md) | Prototype 的目標與非目標 |
| 2 | [02-design/exposure.md](02-design/exposure.md) | **整個 Prototype 的軸心概念** |
| 3 | [02-design/battle-experience.md](02-design/battle-experience.md) | 一個回合應該是什麼感覺 |
| 4 | [02-design/stage-01.md](02-design/stage-01.md) | Stage 01 要教什麼 |
| 5 | [OPEN-DECISIONS.md](OPEN-DECISIONS.md) | **你要拍板的清單** |
| 6 | [CONFLICTS.md](CONFLICTS.md) | 你要裁決的矛盾 |
| 7 | [06-validation/playtest-guide.md](06-validation/playtest-guide.md) | **怎麼試玩、要觀察什麼** |
| 8 | [06-validation/playtest-metrics.md](06-validation/playtest-metrics.md) | 怎麼判斷 Prototype 成功了 |
| 9 | [05-development/experiment-playbook.md](05-development/experiment-playbook.md) | **一次實驗怎麼做**：流程、看哪些指標、地形與數值怎麼配、踩過哪些坑 |

`03-spec/` 也值得讀 —— 它是「你的設計被翻譯成什麼」。
`05-development/` 只有 `experiment-playbook.md` 需要看，
其餘技術細節（`04-architecture/`、`coding-guidelines`）可以跳過。

### 程式

全部。建議順序：
`01-vision/` → `02-design/` → `03-spec/` → `04-architecture/` → `07-adr/` → `05-development/` → `06-validation/`

開工前必須確認：你要碰的系統**沒有**依賴任何 OPEN 的 BLOCKER。

### 新工程師 onboarding

1. [01-vision/](01-vision/) 全部（15 分鐘）
2. [02-design/exposure.md](02-design/exposure.md)（10 分鐘）—— 不懂這個就不懂這個專案在幹嘛
3. [04-architecture/overview.md](04-architecture/overview.md)（15 分鐘）
4. [07-adr/](07-adr/) 全部（30 分鐘）—— 這裡回答「為什麼不用 X」
5. [05-development/](05-development/) 全部
6. 需要時再查 [03-spec/](03-spec/)

### Claude Code / AI 協作

從 [DOCUMENT-MAP.md](DOCUMENT-MAP.md) 開始 —— 它定義了誰有權說什麼。
接著檢查 [OPEN-DECISIONS.md](OPEN-DECISIONS.md)。
**任何未在文件中的規則，一律視為缺口，不得自行補完。**

---

## 目錄結構

```
docs/
├── README.md                    ← 你在這裡
├── DOCUMENT-MAP.md              ← 權威關係、Source of Truth、追蹤關係
├── OPEN-DECISIONS.md            ← 所有 TBD（唯一權威清單）
├── CONFLICTS.md                 ← 既有文件的矛盾
├── CHANGELOG.md
│
├── 00-source/                   ← 既有文件封存（唯讀）
├── 01-vision/                   ← 我們在做什麼
├── 02-design/                   ← 玩家該有什麼體驗
├── 03-spec/                     ← 什麼必須為真（可驗收）
├── 04-architecture/             ← 責任怎麼分
├── 05-development/              ← 工程師怎麼工作
├── 06-validation/               ← 怎麼證明規格被滿足
├── 07-adr/                      ← 為什麼選這個
└── 99-governance/               ← 文件怎麼維護
```

## 四層的分工

| 層 | 回答 | 不該出現什麼 |
|---|---|---|
| **Design** | What are we building? | implementation detail、資料結構、演算法 |
| **Specification** | What must be true? | 意圖說明、設計理由（那是 Design／ADR 的事） |
| **Architecture** | How are responsibilities divided? | 具體 implementation code |
| **ADR** | Why did we choose this? | 一般實作細節（那不值得 ADR） |

分不清該寫哪一層時，看 [99-governance/documentation-rules.md](99-governance/documentation-rules.md)。

---

## 標記約定

| 標記 | 意思 |
|---|---|
| `OPEN DECISION → OD-xx` | 未拍板，**不得當成既定事實實作** |
| `CONFLICT → CONFLICT-xx` | 既有文件互相矛盾，**不得自行選一個** |
| `ODD-xx` | 缺 Source of Truth，見 [DOCUMENT-MAP](DOCUMENT-MAP.md#open-documentation-decisions) |
| `R-<DOMAIN>-nn` | 可驗收的規格條目 |
| `A1`–`A7` | 架構回歸測試斷言 |
| `M1`–`M6` / `Q1`–`Q6` | 手感驗收指標／要回答的問題（M6 尚未實作，見 HANDOFF） |
| `未經 GDD 確認` | 該規則的唯一書面來源是 SPEC v0.1，GDD 沒有背書 |

---

## 專案環境事實

| 項目 | 值 | 查證位置 |
|---|---|---|
| Unity | **6000.5.1f1** | `ProjectSettings/ProjectVersion.txt` |
| 渲染管線 | URP 17.5.0 | `Packages/manifest.json` |
| Test Framework | 1.7.0（已安裝） | `Packages/manifest.json` |
| Newtonsoft JSON | **未安裝** | `Packages/manifest.json` |
| 版本控制 | **不是 git repo** | — |
| 規則層 | `Ediki.Core`（No Engine References） | `Assets/Scripts/Core/` |
| 跑分工具 | `Ediki.Sim`（零引擎依賴） | `Assets/Scripts/Sim/` |
| 表現層 | `Ediki.Unity`（灰盒，可拋棄） | `Assets/Scripts/Unity/` |
| 測試 | **109 EditMode 測試** | `Assets/_Project/Tests/EditMode/` |
| 遊戲資料 | 純文字，企劃可直接編輯 | `Assets/_Project/Resources/Data/` |

> SPEC v0.1 §6.1 記載的 Unity 版本是 6000.0 / 6000.3，與實際不符 →
> [CONFLICT-06](CONFLICTS.md#conflict-06)。
