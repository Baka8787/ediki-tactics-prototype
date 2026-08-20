using Ediki.Core;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// 企劃模式的字典。
    ///
    /// 這一層只換字，不換資料。工程欄位名稱（EffectiveDef、CounterCost、
    /// Exposure、BattleQuery）對讀規則的人是精確的，對讀關卡的人是噪音；
    /// 反過來說，「反擊消耗」對企劃是清楚的，寫進 units.txt 卻會讓 loader
    /// 讀不懂。所以兩種說法都保留，由 UI 決定顯示哪一種，資料永遠只有一種。
    ///
    /// 一個欄位如果在這裡找不到中文，代表它是工程用的，企劃模式就不該顯示它 ——
    /// 這是刻意的過濾器，不是遺漏。
    /// </summary>
    public static class PlannerVocabulary
    {
        // ---------------------------------------------------------------- stats

        public const string Hp = "生命";
        public const string Atk = "攻擊";
        public const string Def = "防禦";
        public const string Move = "移動";
        public const string Ap = "行動力";
        public const string ApRegen = "每回合回復";
        public const string Range = "射程";

        public const string AttackCost = "消耗";
        public const string AttackDamageHint = "傷害";
        public const string GuardCost = "格擋消耗";
        public const string CounterCost = "反擊消耗";
        public const string RestCost = "休息消耗";
        public const string RestHeal = "休息回復";
        public const string AtkGrowth = "每回合攻擊成長";

        public const string AttacksPerRound = "每回合攻擊次數上限";
        public const string SkillUsesPerRound = "每回合技能次數上限";
        public const string ImmuneToPush = "免疫擊退";

        // --------------------------------------------------------------- skills

        public const string SkillPush = "擊退";
        public const string SkillSlow = "減速";
        public const string SkillTaunt = "引誘";
        public const string SkillArmorBreak = "破甲";
        public const string SkillPurify = "淨化";
        public const string SkillContaminate = "穢氣滲流";

        public const string SkillCost = "消耗";
        public const string SkillRange = "射程";
        public const string SkillRadius = "半徑";
        public const string SkillDistance = "距離";
        public const string SkillAmount = "數值";

        // ------------------------------------------------------------ factions

        public static string FactionName(Faction f) => f == Faction.Player ? "我方" : "敵方";

        // ----------------------------------------------------------- objectives

        public static string ObjectiveName(ObjectiveKind kind)
        {
            switch (kind)
            {
                case ObjectiveKind.Reach: return "抵達指定地點";
                case ObjectiveKind.Survive: return "存活到回合結束";
                case ObjectiveKind.Defend: return "守住目標";
                case ObjectiveKind.Kill: return "擊殺指定敵人";
                default: return "殲滅所有敵人";
            }
        }

        public static string ObjectiveHint(ObjectiveKind kind)
        {
            switch (kind)
            {
                case ObjectiveKind.Reach: return "我方任一單位站上指定格子就獲勝。可以另外設回合上限。";
                case ObjectiveKind.Survive: return "撐過設定的回合數就獲勝。一定要設回合上限。";
                case ObjectiveKind.Defend: return "設為「要保護的目標」的單位全部存活到時限就獲勝。一定要設回合上限。";
                case ObjectiveKind.Kill: return "殺掉標記為目標的那一個敵人就獲勝，其他敵人可以不理。";
                default: return "把敵人全部清光就獲勝。可以另外設回合上限。";
            }
        }

        public static readonly ObjectiveKind[] AllObjectives =
        {
            ObjectiveKind.Rout, ObjectiveKind.Kill, ObjectiveKind.Reach,
            ObjectiveKind.Defend, ObjectiveKind.Survive
        };

        // ----------------------------------------------------------------- ai

        /// <summary>
        /// 敵人行為。id 來自 ai-profiles.txt；這裡只替已知的三個配中文，
        /// 資料新增的 profile 會原樣顯示 id ——猜一個中文名比顯示 id 更糟。
        /// </summary>
        public static string AiName(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return "預設";
            switch (profileId.ToLowerInvariant())
            {
                case "rusher": return "衝鋒";
                case "cautious": return "謹慎";
                case "hunter": return "獵殺弱者";
                default: return profileId;
            }
        }

        // ------------------------------------------------------------- terrain

        /// <summary>
        /// 地形的中文說明。名稱來自 terrain.txt，所以未知名稱直接沿用原名，
        /// 後面補上它「實際會做什麼」——那是從 TerrainDef 算出來的，不是猜的。
        /// </summary>
        public static string TerrainName(TerrainDef def)
        {
            switch (def.Name)
            {
                case "Open": return "空地";
                case "Road": return "道路";
                case "Forest": return "樹林";
                case "Highland": return "高地";
                case "Blocking": return "障礙物";
                case "Mire": return "泥沼";
                case "Chasm": return "深坑";
                default: return def.Name;
            }
        }

        public static string TerrainEffect(TerrainDef def)
        {
            if (def.BlocksMovement) return "不能通過，也擋住視野以外的一切";
            if (def.IsLethal) return "站進去就死。單位不會自己走進來，只能被擊退推進去";

            int cost = def.MovementCostHundredths;
            string costText = cost % 100 == 0
                ? "移動消耗 " + (cost / 100)
                : "移動消耗 " + (cost / 100f).ToString("0.##");

            if (cost >= 300) return costText + "，非常難走";
            if (cost >= 200) return costText + "，走起來比較慢";
            return costText + "，一般地面";
        }

        // -------------------------------------------------------------- damage

        public static string DamageModelName(DamageModel model)
        {
            return model == DamageModel.Percentage ? "百分比減傷" : "固定扣減";
        }

        public static string DamageModelHint(DamageModel model)
        {
            return model == DamageModel.Percentage
                ? "傷害 = 攻擊 x (100 - 防禦)%"
                : "傷害 = 攻擊 - 防禦（最少 1）";
        }
    }
}
