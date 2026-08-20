# 00-source — 既有文件封存區

**這個資料夾是唯讀的。不要在這裡編輯任何檔案。**

本資料夾存放專案在建立 Living Documentation 之前就已存在的三份原始文件。
它們是整套 docs/ 的知識來源；docs/ 其餘各層是對它們的**整理、拆分與補完**，不是取代。

## 為什麼要複製進專案

原始檔案原本只存在於 `C:\Users\USER\Downloads\`。Downloads 是易失目錄，
一旦被清理，整條 Source of Truth 就會斷掉。因此在此建立專案內封存。

**自本次封存起，本資料夾的副本視為正本。** Downloads 內的檔案視為已過期副本。
如果原始文件有新版本，請依 [documentation-rules.md](../99-governance/documentation-rules.md)
的流程更新此處，並在 [CHANGELOG.md](../CHANGELOG.md) 記錄。

## 檔案清單

| 檔案 | 原始檔名 | 封存日期 | 版本標記 |
|---|---|---|---|
| `GDD-穢土紀企畫書-暫定.pdf` | `穢土紀企畫書 暫定.pdf` | 2026-08-13 | 「暫定」（文件本身未標版本號） |
| `GDD-穢土紀企畫書-暫定.extracted.txt` | — | 2026-08-13 | 衍生物，非正本 |
| `SPEC-Stage01-設計與規格文檔-v0.1.md` | `穢土紀_Stage01_Prototype_設計與規格文檔_v0.1.md` | 2026-08-13 | v0.1（草案） |
| `RESEARCH-戰棋SRPG設計資源總整理.md` | `戰棋 SRPG 設計資源總整理：8AP 和風神話 Prototype 參考.md` | 2026-08-13 | 無版本號 |

### 關於 `.extracted.txt`

`GDD-穢土紀企畫書-暫定.extracted.txt` 是用 `pdftotext -layout` 從 PDF 抽出的純文字，
目的是讓 grep / Claude Code 能檢索 GDD 內容。

**它是衍生物，不是正本。** 兩者不一致時以 PDF 為準。
PDF 更新時必須重新產生此檔：

```bash
pdftotext -enc UTF-8 -layout "docs/00-source/GDD-穢土紀企畫書-暫定.pdf" "docs/00-source/GDD-穢土紀企畫書-暫定.extracted.txt"
```

## 三份文件的定位（摘要）

完整的權威關係請看 [DOCUMENT-MAP.md](../DOCUMENT-MAP.md)。

- **GDD（企畫書）** — 全遊戲的 Vision / 世界觀 / 角色 / 敵人 / 20 關進度 / 狀態表。
  標記「暫定」。是**世界觀與角色數值**的唯一權威，但**不含傷害公式、不含地形移動成本**。
- **SPEC v0.1（設計與規格文檔）** — Stage 01 Prototype 的規則規格與技術架構。
  草案，含 5 個 `[BLOCKER]`。是**技術架構**的權威；其戰鬥規則章節部分與 GDD 衝突，見
  [CONFLICTS.md](../CONFLICTS.md)。
- **RESEARCH（戰棋 SRPG 設計資源總整理）** — 外部研究彙整與建議。
  **不具備規則權威。** 它是「為什麼這樣設計」的論據來源，不是「規則是什麼」的來源。
  引用它時必須指出它只是建議。
