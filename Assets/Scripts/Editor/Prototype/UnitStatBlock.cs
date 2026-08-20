using System;
using System.Globalization;
using System.Text;
using Ediki.Core;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// A MUTABLE view of one UnitDef, for the inspector to type into.
    ///
    /// Not a second set of gameplay stats — it holds no rules, computes no
    /// damage, and has exactly the fields UnitDef has. It exists because UnitDef
    /// is readonly by design (shared by every UnitState that references it, so a
    /// settable field would let one clone rewrite another's stats), and a text
    /// box needs somewhere to put a half-typed number.
    ///
    /// The round trip is the contract:
    ///     UnitDef -> From(def) -> edit -> ToDataLine(id) -> units.txt
    ///     -> UnitLoader.Parse -> UnitDef
    /// and UnitStatBlockTests asserts it is lossless for every shipped unit.
    /// </summary>
    public sealed class UnitStatBlock
    {
        public string DisplayName;

        public int MaxHp;
        public int Atk;
        public int Def;
        public int Move;
        public int MaxAp;

        /// <summary>Matches UnitDef.ApRegen, which is already resolved (0 in data means "= MaxAp").</summary>
        public int ApRegen;

        public int AttackRange;
        public int AttackApCost;
        public int GuardApCost;
        public int CounterApCost;

        public int RestApCost;
        public int RestHealPercent;
        public int AtkGrowth;

        public int PurifyApCost;
        public int PurifyRadius;

        public int ContaminatePerTurn;
        public int ContaminateRadius;

        public int TauntApCost;
        public int TauntRadius;

        public int SlowApCost;
        public int SlowRange;

        public int PushApCost;
        public int PushRange;
        public bool ImmuneToPush;

        public int ArmorBreakApCost;
        public int ArmorBreakRange;
        public int ArmorBreakAmount;

        public int AttacksPerRound;
        public int SkillUsesPerRound;

        public static UnitStatBlock From(UnitDef d)
        {
            return new UnitStatBlock
            {
                DisplayName = d.DisplayName,
                MaxHp = d.MaxHp,
                Atk = d.Atk,
                Def = d.Def,
                Move = d.Move,
                MaxAp = d.MaxAp,
                ApRegen = d.ApRegen,
                AttackRange = d.AttackRange,
                AttackApCost = d.AttackApCost,
                GuardApCost = d.GuardApCost,
                CounterApCost = d.CounterApCost,
                RestApCost = d.RestApCost,
                RestHealPercent = d.RestHealPercent,
                AtkGrowth = d.AtkGrowth,
                PurifyApCost = d.PurifyApCost,
                PurifyRadius = d.PurifyRadius,
                ContaminatePerTurn = d.ContaminatePerTurn,
                ContaminateRadius = d.ContaminateRadius,
                TauntApCost = d.TauntApCost,
                TauntRadius = d.TauntRadius,
                SlowApCost = d.SlowApCost,
                SlowRange = d.SlowRange,
                PushApCost = d.PushApCost,
                PushRange = d.PushRange,
                ImmuneToPush = d.ImmuneToPush,
                ArmorBreakApCost = d.ArmorBreakApCost,
                ArmorBreakRange = d.ArmorBreakRange,
                ArmorBreakAmount = d.ArmorBreakAmount,
                AttacksPerRound = d.AttacksPerRound,
                SkillUsesPerRound = d.SkillUsesPerRound
            };
        }

        public UnitDef ToDef(string id)
        {
            return new UnitDef(id, string.IsNullOrEmpty(DisplayName) ? id : DisplayName,
                MaxHp, Atk, Def, Move, MaxAp, ApRegen, AttackRange, AttackApCost, GuardApCost,
                CounterApCost, RestApCost, RestHealPercent, AtkGrowth,
                PurifyApCost, PurifyRadius,
                ContaminatePerTurn, ContaminateRadius,
                TauntApCost, TauntRadius,
                SlowApCost, SlowRange,
                PushApCost, PushRange, ImmuneToPush,
                ArmorBreakApCost, ArmorBreakRange, ArmorBreakAmount,
                AttacksPerRound, SkillUsesPerRound);
        }

        public UnitStatBlock Clone() => (UnitStatBlock)MemberwiseClone();

        /// <summary>
        /// True when this block would produce exactly the same UnitDef as the
        /// given one. Drives variant de-duplication: editing a unit back to its
        /// original numbers must not leave a `_v1` behind.
        /// </summary>
        public bool MatchesStatsOf(UnitDef d)
        {
            return MaxHp == d.MaxHp && Atk == d.Atk && Def == d.Def && Move == d.Move
                && MaxAp == d.MaxAp && ApRegen == d.ApRegen
                && AttackRange == d.AttackRange && AttackApCost == d.AttackApCost
                && GuardApCost == d.GuardApCost && CounterApCost == d.CounterApCost
                && RestApCost == d.RestApCost && RestHealPercent == d.RestHealPercent
                && AtkGrowth == d.AtkGrowth
                && PurifyApCost == d.PurifyApCost && PurifyRadius == d.PurifyRadius
                && ContaminatePerTurn == d.ContaminatePerTurn && ContaminateRadius == d.ContaminateRadius
                && TauntApCost == d.TauntApCost && TauntRadius == d.TauntRadius
                && SlowApCost == d.SlowApCost && SlowRange == d.SlowRange
                && PushApCost == d.PushApCost && PushRange == d.PushRange
                && ImmuneToPush == d.ImmuneToPush
                && ArmorBreakApCost == d.ArmorBreakApCost && ArmorBreakRange == d.ArmorBreakRange
                && ArmorBreakAmount == d.ArmorBreakAmount
                && AttacksPerRound == d.AttacksPerRound && SkillUsesPerRound == d.SkillUsesPerRound
                && string.Equals(DisplayName, d.DisplayName, StringComparison.Ordinal);
        }

        /// <summary>
        /// One `unit` line in the shipped units.txt format.
        ///
        /// Optional keys are omitted when they are at their loader default, so a
        /// generated row reads like a hand-written one instead of a wall of
        /// zeroes. apRegen is omitted when it equals the cap, which is exactly
        /// what UnitDef does with a missing apRegen.
        /// </summary>
        public string ToDataLine(string id)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("unit id=").Append(id);
            sb.Append(" name=").Append(string.IsNullOrEmpty(DisplayName) ? id : DisplayName);
            Req(sb, "hp", MaxHp);
            Req(sb, "atk", Atk);
            Req(sb, "def", Def);
            Req(sb, "move", Move);
            Req(sb, "ap", MaxAp);
            if (ApRegen != MaxAp) Req(sb, "apRegen", ApRegen);
            Req(sb, "range", AttackRange);
            Req(sb, "attackCost", AttackApCost);
            Req(sb, "guardCost", GuardApCost);

            Opt(sb, "counterCost", CounterApCost, 0);
            if (RestApCost > 0)
            {
                Req(sb, "restCost", RestApCost);
                Req(sb, "restHealPercent", RestHealPercent);
            }
            Opt(sb, "atkGrowth", AtkGrowth, 0);

            if (PurifyApCost > 0) { Req(sb, "purifyCost", PurifyApCost); Req(sb, "purifyRadius", PurifyRadius); }
            if (ContaminatePerTurn > 0) { Req(sb, "contaminates", ContaminatePerTurn); Req(sb, "contaminateRadius", ContaminateRadius); }
            if (TauntApCost > 0) { Req(sb, "tauntCost", TauntApCost); Req(sb, "tauntRadius", TauntRadius); }
            if (SlowApCost > 0) { Req(sb, "slowCost", SlowApCost); Req(sb, "slowRange", SlowRange); }
            if (PushApCost > 0) { Req(sb, "pushCost", PushApCost); Req(sb, "pushRange", PushRange); }
            if (ImmuneToPush) sb.Append(" immuneToPush=true");
            if (ArmorBreakApCost > 0)
            {
                Req(sb, "armorBreakCost", ArmorBreakApCost);
                Req(sb, "armorBreakRange", ArmorBreakRange);
                Req(sb, "armorBreakAmount", ArmorBreakAmount);
            }
            Opt(sb, "attacksPerRound", AttacksPerRound, 0);
            Opt(sb, "skillUsesPerRound", SkillUsesPerRound, 0);

            return sb.ToString();
        }

        private static void Req(StringBuilder sb, string key, int value)
        {
            sb.Append(' ').Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Opt(StringBuilder sb, string key, int value, int fallback)
        {
            if (value != fallback) Req(sb, key, value);
        }
    }
}
