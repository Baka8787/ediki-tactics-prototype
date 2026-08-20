using System;
using System.Collections.Generic;

namespace Ediki.Core
{
    /// <summary>
    /// Static unit stats from data (units.txt). Shared and immutable —
    /// UnitState holds a reference, clones share it.
    ///
    /// Action AP costs live here (not in a global rules table) so different
    /// units can differ without any extra machinery — see OD-01 route C.
    /// </summary>
    public sealed class UnitDef
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly int MaxHp;
        public readonly int Atk;
        public readonly int Def;
        public readonly int Move;      // see OD-04 implementation note: not enforced in the current baseline
        /// <summary>AP ceiling. Unspent AP carries over up to this value.</summary>
        public readonly int MaxAp;

        /// <summary>
        /// AP regained at the start of the unit's phase, added to whatever it kept.
        /// Regen &lt; MaxAp is what makes banking AP a real choice: hold some back
        /// this turn and next turn you can afford something you otherwise could not.
        /// </summary>
        public readonly int ApRegen;

        public readonly int AttackRange;
        public readonly int AttackApCost;
        public readonly int GuardApCost;

        /// <summary>
        /// AP that must still be UNSPENT when this unit is attacked for it to strike
        /// back once. 0 = never counterattacks.
        ///
        /// JA2-style "reserve AP" rather than XCOM-style "spend an action to arm it":
        /// arming costs nothing, but every point held back is a point not spent
        /// attacking — so each turn is a real trade. The counter consumes the
        /// reserve, which stops a unit from countering forever.
        /// </summary>
        public readonly int CounterApCost;

        /// <summary>Rest: cheap, heals a slice of max HP, and ends the unit's turn.</summary>
        public readonly int RestApCost;
        public readonly int RestHealPercent;

        /// <summary>
        /// ATK gained per completed round. 0 = the baseline, a unit whose threat
        /// never changes.
        ///
        /// Deliberately derived from the round counter instead of stored on
        /// UnitState: growth is then a pure function of state that already
        /// exists, so it adds nothing to clone, nothing to the state hash, and
        /// cannot drift out of sync with the turn structure.
        /// </summary>
        public readonly int AtkGrowth;

        /// <summary>
        /// AP to purify. 0 = this unit cannot purify.
        /// GDD: Momotaro is the only 神之容器 and the only one who can.
        /// </summary>
        public readonly int PurifyApCost;

        /// <summary>Manhattan radius purified. GDD 仙照・淺淨: radius 2, which is 13 cells.</summary>
        public readonly int PurifyRadius;

        /// <summary>
        /// Contamination added per round to every cell within ContaminateRadius.
        /// 0 = this unit does not contaminate. GDD 晦氣 關鍵被動「穢氣滲流」.
        /// </summary>
        public readonly int ContaminatePerTurn;
        public readonly int ContaminateRadius;

        // ---------------------------------------------------------- control kit
        //
        // Three skills that all change WHEN an enemy can reach you, rather than
        // how hard anyone hits. That is deliberate: every measured lever that
        // moved the win rate a long way was a release-time lever (range +51,
        // terrain cost +15 and non-monotonic, growth), and every damage lever was
        // either tiny or a cliff. A pure damage skill also fails the standard test
        // for an interesting choice — better than the basic attack makes it the
        // new only answer, worse makes it dead weight.
        //
        // All default to 0 = the unit does not have the skill, so the existing
        // roster is unchanged and so is every baseline measured before them.

        /// <summary>AP to taunt. 0 = cannot taunt.</summary>
        public readonly int TauntApCost;

        /// <summary>Manhattan radius of enemies pulled onto this unit.</summary>
        public readonly int TauntRadius;

        /// <summary>AP to slow. 0 = cannot slow.</summary>
        public readonly int SlowApCost;
        public readonly int SlowRange;

        /// <summary>AP to push. 0 = cannot push.</summary>
        public readonly int PushApCost;
        public readonly int PushRange;

        /// <summary>
        /// Immune to being pushed. GDD gives this to Zhengshou (免疫擊退) and to
        /// Genjin's 金剛不壞之軀 — it exists so that a wall is actually a wall.
        /// </summary>
        public readonly bool ImmuneToPush;

        /// <summary>
        /// 破甲: AP to strip armour off a target. 0 = cannot armour-break.
        ///
        /// The one skill here that is NOT a release-time lever, and it earns the
        /// exception by moving the HIT COUNT rather than the damage number. This
        /// project measured the hit-count ladder as a cliff, not a slope
        /// (2 hits 68% -> 3 hits 1%), so a flat damage buff is worth nothing until
        /// it crosses a step and then worth everything. Cutting DEF is the only
        /// player-side handle on which step a target sits on, which makes it a
        /// setup action: worthless alone, and worth a whole action when it turns
        /// somebody else's two hits into one.
        /// </summary>
        public readonly int ArmorBreakApCost;
        public readonly int ArmorBreakRange;

        /// <summary>
        /// DEF removed from the target while the break lasts. Data-driven rather
        /// than a constant in the rule layer (A5): the amount only means anything
        /// relative to the roster's ATK values, and those live in data too.
        /// </summary>
        public readonly int ArmorBreakAmount;

        /// <summary>
        /// Attacks this unit may make in one round. **0 = no limit**, which is the
        /// baseline and leaves every existing unit behaving exactly as before.
        ///
        /// Exists because AP alone stopped being the whole budget once carry-over
        /// arrived. `Ap = min(kept + 8, 10)`, so banking 2 AP opens a 10 AP round —
        /// and a 5 AP attacker then gets TWO attacks out of a cost that was priced
        /// to allow one. A cap is the only thing that closes that without either
        /// re-pricing the attack (measured: pure loss) or capping actions globally
        /// (fights the "AP is the budget" architecture).
        ///
        /// At attackCost 4 the cap never binds — 4x3 = 12 exceeds the 10 ceiling
        /// anyway — so the shipped roster is untouched by construction.
        /// </summary>
        public readonly int AttacksPerRound;

        /// <summary>
        /// Skill activations this unit may make in one round, counting the control
        /// kit and 破甲 together. **0 = no limit.**
        ///
        /// ONE counter for all skills rather than one per skill: no unit in the
        /// current roster carries two skills, so per-skill counters would be extra
        /// state that no data can currently exercise — and state that nothing
        /// exercises is state that silently rots.
        ///
        /// The problem it closes is sharper than the attack one: skills are priced
        /// at 1 AP, so the rule layer currently permits eight to ten activations in
        /// a single round. That has never been measured because no scripted
        /// strategy tries it, which is a gap in strategy coverage, not evidence of
        /// safety.
        /// </summary>
        public readonly int SkillUsesPerRound;

        public bool CanPurify => PurifyApCost > 0;
        public bool Contaminates => ContaminatePerTurn > 0;
        public bool CanTaunt => TauntApCost > 0;
        public bool CanSlow => SlowApCost > 0;
        public bool CanPush => PushApCost > 0;
        public bool CanArmorBreak => ArmorBreakApCost > 0;

        /// <summary>ATK on the given round. Round 1 is always the base value.</summary>
        public int AtkOnRound(int turnIndex)
        {
            if (AtkGrowth == 0) return Atk;
            int rounds = turnIndex - 1;
            if (rounds < 0) rounds = 0;
            return Atk + AtkGrowth * rounds;
        }

        public UnitDef(string id, string displayName, int maxHp, int atk, int def, int move,
                       int maxAp, int apRegen, int attackRange, int attackApCost, int guardApCost,
                       int counterApCost, int restApCost, int restHealPercent, int atkGrowth = 0,
                       int purifyApCost = 0, int purifyRadius = 2,
                       int contaminatePerTurn = 0, int contaminateRadius = 1,
                       int tauntApCost = 0, int tauntRadius = 2,
                       int slowApCost = 0, int slowRange = 1,
                       int pushApCost = 0, int pushRange = 1, bool immuneToPush = false,
                       int armorBreakApCost = 0, int armorBreakRange = 1, int armorBreakAmount = 0,
                       int attacksPerRound = 0, int skillUsesPerRound = 0)
        {
            AttacksPerRound = attacksPerRound;
            SkillUsesPerRound = skillUsesPerRound;
            ArmorBreakApCost = armorBreakApCost;
            ArmorBreakRange = armorBreakRange;
            ArmorBreakAmount = armorBreakAmount;
            TauntApCost = tauntApCost;
            TauntRadius = tauntRadius;
            SlowApCost = slowApCost;
            SlowRange = slowRange;
            PushApCost = pushApCost;
            PushRange = pushRange;
            ImmuneToPush = immuneToPush;
            AtkGrowth = atkGrowth;
            PurifyApCost = purifyApCost;
            PurifyRadius = purifyRadius;
            ContaminatePerTurn = contaminatePerTurn;
            ContaminateRadius = contaminateRadius;
            Id = id;
            DisplayName = displayName;
            MaxHp = maxHp;
            Atk = atk;
            Def = def;
            Move = move;
            MaxAp = maxAp;
            ApRegen = apRegen > 0 ? apRegen : maxAp;
            AttackRange = attackRange;
            AttackApCost = attackApCost;
            GuardApCost = guardApCost;
            CounterApCost = counterApCost;
            RestApCost = restApCost;
            RestHealPercent = restHealPercent;
        }

        public bool CanCounter => CounterApCost > 0;
        public bool CanRest => RestApCost > 0;

        /// <summary>Integer-only, and always at least 1 HP if the unit can rest at all.</summary>
        public int RestHealAmount
        {
            get
            {
                int amount = MaxHp * RestHealPercent / 100;
                return amount < 1 ? 1 : amount;
            }
        }

        public override string ToString() => DisplayName;
    }

    public sealed class UnitCatalog
    {
        private readonly Dictionary<string, UnitDef> _byId;

        public UnitCatalog(IEnumerable<UnitDef> defs)
        {
            _byId = new Dictionary<string, UnitDef>(StringComparer.OrdinalIgnoreCase);
            foreach (UnitDef d in defs)
            {
                if (_byId.ContainsKey(d.Id))
                    throw new ArgumentException("Duplicate unit id '" + d.Id + "'.");
                _byId.Add(d.Id, d);
            }
        }

        public bool TryGet(string id, out UnitDef def) => _byId.TryGetValue(id, out def);

        public UnitDef Get(string id)
        {
            UnitDef d;
            if (!_byId.TryGetValue(id, out d))
                throw new KeyNotFoundException("Unknown unit id '" + id + "'.");
            return d;
        }
    }
}
