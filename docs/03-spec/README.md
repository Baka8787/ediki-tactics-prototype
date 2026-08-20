# 03-spec — Specification 層

**規格回答一個問題：What must be true?**

不是「我們想做什麼」（那是 Design），不是「怎麼分工」（那是 Architecture），
不是「為什麼選這個」（那是 ADR）。

---

## 規格條目的格式

每一條規則都有一個穩定 ID：`R-<DOMAIN>-<nn>`

| 欄位 | 說明 |
|---|---|
| **ID** | `R-AP-01` 這種。**一旦發出就不重用、不重編號。** 條目作廢時標記 `DEPRECATED`，不刪除 |
| **Statement** | 一句可判斷真假的敘述 |
| **Source** | 來自哪份既有文件的哪一節。若沒有來源 → 它不該存在於本層 |
| **Status** | 見下表 |
| **Acceptance** | 怎麼驗。**寫不出來就代表這條規格還不夠精確** |

### Status 值

| Status | 意思 | 可否實作 |
|---|---|---|
| `STABLE` | 有來源、無衝突、可驗收 | ✅ 可以 |
| `OPEN → OD-xx` | 依賴未決事項 | ❌ **不可實作** |
| `CONFLICT → CONFLICT-xx` | 既有文件互相矛盾 | ❌ **不可實作** |
| `DERIVED` | 從其他規則推導，非獨立來源 | ✅ 可以，但上游變動時必須重檢 |
| `UNSOURCED` | 唯一書面來源是 SPEC v0.1，GDD 未背書 | ⚠️ 可實作，但要隔離成可替換的單點 |
| `DEPRECATED` | 已作廢，保留供追溯 | ❌ |

---

## 本層的紀律

1. **不得產生既有文件沒有的規則。**
   找不到來源的規則就是缺口 → 登錄 [OPEN-DECISIONS](../OPEN-DECISIONS.md)，不要自己補。

2. **不得把未決事項寫成既定事實。**
   依賴未決事項的條目一律標 `OPEN → OD-xx`，並描述**當它被決定後會變成什麼**，
   而不是先假設一個答案。

3. **每條規格都要能被測試或驗收。**
   如果 Acceptance 欄寫不出東西，那條規格不夠精確，或者它其實屬於 Design 層。

4. **不寫意圖，不寫理由。**
   「為什麼要有 Exposure」在 [02-design/exposure.md](../02-design/exposure.md)。
   本層只寫「Exposure 怎麼算」。

5. **數值不寫死在規格文字裡。**
   規格描述**結構**（「攻擊消耗該單位的攻擊 AP 成本」），
   數值放資料（見 [SPEC-unit-data.md](SPEC-unit-data.md)）。
   例外：GDD 明確定下且不打算改的常數（例如 AP 上限 8）可以寫，但要標明來源。

---

## 檔案

| 檔案 | 涵蓋 |
|---|---|
| [SPEC-battle-flow.md](SPEC-battle-flow.md) | 回合結構、AP、動作成本、勝敗條件、Command/Effect 契約 |
| [SPEC-grid-terrain.md](SPEC-grid-terrain.md) | 座標、鄰接、地形、Exposure 計算 |
| [SPEC-movement.md](SPEC-movement.md) | 可達性、路徑成本、MOVE 上限、單位阻擋 |
| [SPEC-combat.md](SPEC-combat.md) | 攻擊、傷害、命中、防禦、死亡 |
| [SPEC-threat-activation.md](SPEC-threat-activation.md) | 威脅範圍、敵人啟動、危險區可見性 |
| [SPEC-unit-data.md](SPEC-unit-data.md) | 單位數值 schema、Stage 01 名單、資料驅動要求 |

**尚不存在但需要的規格**：`SPEC-ai-behaviour.md`（[ODD-01](../DOCUMENT-MAP.md#odd-01)）、
`SPEC-session-loop.md`（[ODD-02](../DOCUMENT-MAP.md#odd-02)）。
兩者都因為缺乏來源而**刻意未建立**。

---

## 目前可實作性總覽（2026-08-13 更新）

OD-01～06、OD-10、OD-11 已裁決為 Prototype Baseline，**全部已實作**。

| 規格 | 實作狀態 | 仍未決 |
|---|---|---|
| SPEC-battle-flow | ✅ 已實作 | 道具（OD-09，未實作）、AP 保留（CONFLICT-05，沿用不保留） |
| SPEC-grid-terrain | ✅ 已實作 | — |
| SPEC-movement | ✅ 已實作 | `MOVE` 屬性語意（不生效）、A\*（刻意不做） |
| SPEC-combat | ✅ 已實作 | 小耗 `DEF 20` 仍無來源（CONFLICT-03）、地形戰鬥修正（OD-08，不做） |
| SPEC-threat-activation | ✅ 已實作 | UI 呈現形式（OD-14，用最簡版）、**僵局（OD-16）** |
| SPEC-unit-data | ✅ 已實作 | 死亡語意（CONFLICT-08，Stage 01 無差別） |
| **SPEC-ai-behaviour** | ✅ 已實作（本輪新建） | — |

**尚未建立的規格**：`SPEC-session-loop.md`（[ODD-02](../DOCUMENT-MAP.md#odd-02)）——
Prototype 目前用 `BattleRunner` 直接跑一場戰鬥，還沒有需要 session 抽象的需求。

> **注意各 SPEC 檔內文的個別 `Status` 欄位可能還寫著 `OPEN → OD-xx`。**
> 每份檔案開頭的 `PROTOTYPE BASELINE` 橫幅是較新的資訊，以橫幅為準。
> 逐列改寫留待下次規格整理，避免這一輪產生大量無實質意義的 diff。
