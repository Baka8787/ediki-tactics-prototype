using System;
using System.Collections.Generic;

namespace Ediki.Core
{
    public enum StatusKind
    {
        None = 0, Poison = 1, Bleed = 2, Might = 3, Weaken = 4, Fortify = 5, Frail = 6
    }

    public readonly struct ActiveStatus
    {
        public readonly StatusKind Kind;
        public readonly int RemainingPhases;
        public readonly int Magnitude;
        public ActiveStatus(StatusKind kind, int remainingPhases, int magnitude)
        { Kind = kind; RemainingPhases = remainingPhases; Magnitude = magnitude; }
        public bool IsBeneficial => Kind == StatusKind.Might || Kind == StatusKind.Fortify;
        public bool IsHarmful => Kind != StatusKind.None && !IsBeneficial;
    }

    /// <summary>Runtime state of one unit. Mutable; deep-copied by BattleState.Clone().</summary>
    public sealed class UnitState
    {
        public readonly int Id;
        public readonly UnitDef Def;      // shared, immutable
        public readonly Faction Faction;

        public Coord Position;
        public int Hp;
        public int Ap;
        public bool IsGuarding;
        public bool HasEndedTurn;
        public bool IsActivated;          // AI perception latch — observability only
        public bool HasCounteredThisRound;

        /// <summary>
        /// Cells moved so far this turn. MOVE is a per-TURN budget, not a per-action
        /// one — otherwise chaining move actions would make the cap meaningless.
        /// </summary>
        public int MoveUsedThisTurn;

        /// <summary>
        /// 遲滯 is active while TurnIndex &lt;= this. 0 = never slowed.
        ///
        /// A turn STAMP, not a countdown. A countdown has to be decremented
        /// somewhere, and every place to put that tick is wrong for one of the two
        /// statuses: ticking at the start of a phase expires a slow before the
        /// slowed unit has moved, ticking at the end of one expires a taunt before
        /// the enemy has chosen a target. A stamp compared against TurnIndex has
        /// no tick and therefore no ordering to get wrong.
        /// </summary>
        public int SlowedUntilTurn;

        /// <summary>
        /// 嘲諷 is active while TurnIndex &lt;= this. 0 = not taunting.
        ///
        /// Stored on the TAUNTER rather than on each enemy: it is one fact about
        /// one unit instead of N facts scattered across the other side, so it
        /// cannot desynchronise and it costs the state hash one field per unit
        /// rather than one per pair.
        /// </summary>
        public int TauntingUntilTurn;

        /// <summary>
        /// 破甲 is active while TurnIndex &lt;= this. 0 = armour intact.
        /// Same turn STAMP as SlowedUntilTurn above, for the same reason: there is
        /// no correct place to tick a countdown that works for every status.
        /// </summary>
        public int ArmorBrokenUntilTurn;

        /// <summary>
        /// DEF removed while the break lasts. Stored on the TARGET rather than
        /// looked up from the breaker: by the time damage is computed the breaker
        /// may be dead, out of range, or not the one attacking, and a rule that
        /// reaches for another unit's stats at resolution time is a rule that
        /// changes answer depending on who is still alive.
        /// </summary>
        public int ArmorBrokenAmount;

        /// <summary>
        /// Attacks and skill activations used so far this round. Reset alongside
        /// AP and <see cref="MoveUsedThisTurn"/> at the unit's phase start.
        ///
        /// Counters rather than flags because the caps are data (`attacksPerRound`
        /// = 2 is a legitimate thing to want), and because a flag could not tell
        /// "used its one attack" from "has a cap of one" — the reset would then
        /// have to know the cap.
        /// </summary>
        public int AttacksThisRound;
        public int SkillUsesThisRound;

        public List<ActiveStatus> Statuses;

        public void ApplyStatus(StatusKind kind, int remainingPhases, int magnitude)
        {
            if (kind == StatusKind.None || remainingPhases <= 0 || magnitude <= 0) return;
            if (Statuses == null) Statuses = new List<ActiveStatus>();
            for (int i = 0; i < Statuses.Count; i++)
            {
                if (Statuses[i].Kind != kind) continue;
                ActiveStatus old = Statuses[i];
                Statuses[i] = new ActiveStatus(kind,
                    Math.Max(old.RemainingPhases, remainingPhases), Math.Max(old.Magnitude, magnitude));
                return;
            }
            Statuses.Add(new ActiveStatus(kind, remainingPhases, magnitude));
        }

        public int StatusMagnitude(StatusKind kind)
        {
            if (Statuses == null) return 0;
            for (int i = 0; i < Statuses.Count; i++)
                if (Statuses[i].Kind == kind && Statuses[i].RemainingPhases > 0) return Statuses[i].Magnitude;
            return 0;
        }

        /// <summary>
        /// Losing this unit loses the battle outright (Defend objectives).
        /// A shrine or a villager, not a fighter — excluded from "all units dead".
        /// </summary>
        public readonly bool MustSurvive;

        /// <summary>
        /// Killing this unit wins the battle outright (Kill objectives).
        /// The mirror image of MustSurvive, and marked the same way — on the
        /// spawn, because the objective names a unit rather than a cell.
        /// </summary>
        public readonly bool IsObjectiveTarget;

        public UnitState(int id, UnitDef def, Faction faction, Coord position, bool mustSurvive = false,
                         bool isObjectiveTarget = false)
        {
            Id = id;
            Def = def;
            Faction = faction;
            Position = position;
            Hp = def.MaxHp;
            Ap = def.MaxAp;
            IsGuarding = false;
            HasEndedTurn = false;
            IsActivated = false;
            HasCounteredThisRound = false;
            MoveUsedThisTurn = 0;
            SlowedUntilTurn = 0;
            TauntingUntilTurn = 0;
            ArmorBrokenUntilTurn = 0;
            ArmorBrokenAmount = 0;
            AttacksThisRound = 0;
            SkillUsesThisRound = 0;
            MustSurvive = mustSurvive;
            IsObjectiveTarget = isObjectiveTarget;
        }

        private UnitState(UnitState src)
        {
            Id = src.Id;
            Def = src.Def;
            Faction = src.Faction;
            Position = src.Position;
            Hp = src.Hp;
            Ap = src.Ap;
            IsGuarding = src.IsGuarding;
            HasEndedTurn = src.HasEndedTurn;
            IsActivated = src.IsActivated;
            HasCounteredThisRound = src.HasCounteredThisRound;
            MoveUsedThisTurn = src.MoveUsedThisTurn;
            SlowedUntilTurn = src.SlowedUntilTurn;
            TauntingUntilTurn = src.TauntingUntilTurn;
            ArmorBrokenUntilTurn = src.ArmorBrokenUntilTurn;
            ArmorBrokenAmount = src.ArmorBrokenAmount;
            AttacksThisRound = src.AttacksThisRound;
            SkillUsesThisRound = src.SkillUsesThisRound;
            Statuses = src.Statuses == null ? null : new List<ActiveStatus>(src.Statuses);
            MustSurvive = src.MustSurvive;
            IsObjectiveTarget = src.IsObjectiveTarget;
        }

        public bool IsAlive => Hp > 0;

        /// <summary>
        /// NOTE (ADR-0004): every field added above MUST be copied here, written by
        /// StateHasher, and covered by test A3. Missing one is the known failure mode.
        /// </summary>
        public UnitState Clone() => new UnitState(this);
    }

    /// <summary>
    /// A wave spawn waiting for its turn. Mutable (`Spawned`), so BattleState.Clone
    /// must deep-copy it and StateHasher must cover it.
    /// </summary>
    public sealed class PendingReinforcement
    {
        public readonly int Turn;
        public readonly Faction Faction;
        public readonly UnitDef Def;
        public readonly Coord Position;
        public readonly string AiProfileId;

        public bool Spawned;

        public PendingReinforcement(int turn, Faction faction, UnitDef def, Coord position, string aiProfileId)
        {
            Turn = turn;
            Faction = faction;
            Def = def;
            Position = position;
            AiProfileId = aiProfileId;
        }

        public PendingReinforcement Clone()
        {
            return new PendingReinforcement(Turn, Faction, Def, Position, AiProfileId) { Spawned = Spawned };
        }
    }

    /// <summary>
    /// The complete state of one battle.
    ///
    /// Only BattleSimulator.Execute may mutate it (R-CMD-01). Anything the UI
    /// owns — selection, animation progress, camera — must NOT live here, or the
    /// state hash (A4) becomes non-deterministic.
    /// </summary>
    public sealed class BattleState
    {
        public readonly BattleMap Map;          // immutable, shared across clones
        public readonly ObjectiveDef Objective; // immutable, shared across clones
        public readonly RuleSet Rules;          // immutable, shared across clones

        private readonly List<UnitState> _units;   // ordered by Id, never reordered (determinism rule 2)
        private readonly List<PendingReinforcement> _reinforcements;
        private int _nextUnitId;

        public int TurnIndex;
        public Faction CurrentFaction;
        public BattleOutcome Outcome;

        /// <summary>
        /// Contamination level per cell, row-major. NULL until something actually
        /// contaminates something.
        ///
        /// This is the project's first piece of MUTABLE terrain, and it lives on
        /// the state rather than on BattleMap on purpose: BattleMap is shared by
        /// every clone, so putting it there would let one line of play rewrite the
        /// terrain of all the others. Kept null while unused so a battle without
        /// contamination clones and hashes exactly as it did before this existed
        /// (ADR-0004 predicted this cost and asked for exactly this).
        /// </summary>
        private int[] _contamination;

        /// <summary>Ceiling on a cell's contamination. Unbounded stacking would make
        /// a late battle unplayable for reasons no designer chose.</summary>
        public const int MaxContamination = 3;

        public int ContaminationAt(Coord c)
        {
            if (_contamination == null || !Map.Contains(c)) return 0;
            return _contamination[c.Y * Map.Width + c.X];
        }

        public bool HasContamination => _contamination != null;

        /// <summary>Returns the new level, or the old one when nothing changed.</summary>
        public int AddContamination(Coord c, int delta)
        {
            if (!Map.Contains(c) || !Map.IsPassable(c)) return 0;

            if (_contamination == null)
            {
                if (delta <= 0) return 0;
                _contamination = new int[Map.Width * Map.Height];
            }

            int i = c.Y * Map.Width + c.X;
            int level = _contamination[i] + delta;
            if (level < 0) level = 0;
            if (level > MaxContamination) level = MaxContamination;
            _contamination[i] = level;
            return level;
        }

        public BattleState(BattleMap map, IEnumerable<UnitState> units, ObjectiveDef objective = null,
                           IEnumerable<PendingReinforcement> reinforcements = null, RuleSet rules = null)
        {
            Map = map;
            Objective = objective ?? ObjectiveDef.Rout;
            Rules = rules ?? RuleSet.Default;
            _units = new List<UnitState>(units);
            _units.Sort((a, b) => a.Id.CompareTo(b.Id));
            _reinforcements = reinforcements == null
                ? new List<PendingReinforcement>()
                : new List<PendingReinforcement>(reinforcements);
            _nextUnitId = 1;
            for (int i = 0; i < _units.Count; i++)
                if (_units[i].Id >= _nextUnitId) _nextUnitId = _units[i].Id + 1;
            TurnIndex = 1;
            CurrentFaction = Faction.Player;
            Outcome = BattleOutcome.InProgress;
        }

        private BattleState(BattleState src)
        {
            Map = src.Map;
            Objective = src.Objective;
            Rules = src.Rules;
            _units = new List<UnitState>(src._units.Count);
            for (int i = 0; i < src._units.Count; i++)
                _units.Add(src._units[i].Clone());
            _reinforcements = new List<PendingReinforcement>(src._reinforcements.Count);
            for (int i = 0; i < src._reinforcements.Count; i++)
                _reinforcements.Add(src._reinforcements[i].Clone());
            _nextUnitId = src._nextUnitId;
            TurnIndex = src.TurnIndex;
            CurrentFaction = src.CurrentFaction;
            Outcome = src.Outcome;

            // Deep copy — the whole point of keeping it off BattleMap.
            if (src._contamination != null)
            {
                _contamination = new int[src._contamination.Length];
                System.Array.Copy(src._contamination, _contamination, src._contamination.Length);
            }
        }

        public BattleState Clone() => new BattleState(this);

        /// <summary>Stable, id-ordered. Safe to iterate for simulation and hashing.</summary>
        public IReadOnlyList<UnitState> Units => _units;

        /// <summary>Declaration-ordered; never reordered.</summary>
        public IReadOnlyList<PendingReinforcement> Reinforcements => _reinforcements;

        public bool HasPendingReinforcements(Faction f)
        {
            for (int i = 0; i < _reinforcements.Count; i++)
                if (!_reinforcements[i].Spawned && _reinforcements[i].Faction == f) return true;
            return false;
        }

        /// <summary>
        /// Adds a unit mid-battle (reinforcement wave). Ids keep climbing so the
        /// list stays id-ordered without a re-sort.
        /// Only BattleSimulator should call this.
        /// </summary>
        internal UnitState AddUnit(UnitDef def, Faction faction, Coord position)
        {
            UnitState unit = new UnitState(_nextUnitId++, def, faction, position);
            _units.Add(unit);
            return unit;
        }

        public UnitState FindUnit(int id)
        {
            for (int i = 0; i < _units.Count; i++)
                if (_units[i].Id == id) return _units[i];
            return null;
        }

        /// <summary>Living unit standing on this cell, or null. Occupancy per OD-03.</summary>
        public UnitState UnitAt(Coord c)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                UnitState u = _units[i];
                if (u.IsAlive && u.Position == c) return u;
            }
            return null;
        }

        public bool IsOccupied(Coord c) => UnitAt(c) != null;

        /// <summary>Terrain passable AND not occupied by a living unit (OD-02 + OD-03).</summary>
        public bool CanUnitEnter(Coord c) => Map.IsPassable(c) && !IsOccupied(c);

        /// <summary>
        /// The unit a Kill objective names, alive or dead, or null when nothing is
        /// marked. Returns the first marked unit in id order, so a file that marks
        /// two resolves the same way every run (determinism rule 2).
        /// </summary>
        public UnitState ObjectiveTarget()
        {
            for (int i = 0; i < _units.Count; i++)
                if (_units[i].IsObjectiveTarget) return _units[i];
            return null;
        }

        /// <summary>
        /// True while anything on the field is slowed or taunting. Gates the
        /// matching fold in StateHasher, so a battle that never uses the control
        /// kit hashes exactly as it did before the kit existed.
        /// </summary>
        public bool HasControlStatus
        {
            get
            {
                for (int i = 0; i < _units.Count; i++)
                    if (_units[i].SlowedUntilTurn != 0 || _units[i].TauntingUntilTurn != 0) return true;
                return false;
            }
        }

        /// <summary>
        /// True while anything on the field has its armour broken. Separate gate
        /// from HasControlStatus rather than folded into it, so a battle that only
        /// ever taunts hashes exactly as it did before 破甲 existed — and so does
        /// one that uses neither. Same discipline as contamination and kill
        /// targets: new state costs the golden constants nothing until it is used.
        /// </summary>
        public bool HasArmorBreak
        {
            get
            {
                for (int i = 0; i < _units.Count; i++)
                    if (_units[i].ArmorBrokenUntilTurn != 0) return true;
                return false;
            }
        }

        public bool HasStatuses
        {
            get
            {
                for (int i = 0; i < _units.Count; i++)
                    if (_units[i].Statuses != null && _units[i].Statuses.Count > 0) return true;
                return false;
            }
        }

        public int EffectiveAtk(UnitState unit)
        {
            if (unit == null) return 0;
            int atk = unit.Def.AtkOnRound(TurnIndex);
            int might = unit.StatusMagnitude(StatusKind.Might);
            int weaken = unit.StatusMagnitude(StatusKind.Weaken);
            if (might > 0) atk = atk * might / 100;
            if (weaken > 0) atk = atk * weaken / 100;
            return atk < 0 ? 0 : atk;
        }

        public bool IsArmorBroken(UnitState unit) => unit != null && TurnIndex <= unit.ArmorBrokenUntilTurn;

        /// <summary>
        /// True while any unit on the field is subject to a per-round cap. Gates
        /// the matching fold in StateHasher, so a roster where every unit is
        /// uncapped (the shipped one) hashes exactly as it did before the caps
        /// existed. Same discipline as contamination, kill targets and 破甲.
        ///
        /// Keyed off the DEFS, not the counters: a capped unit that has not acted
        /// yet still differs from an uncapped one, because what it may do next is
        /// different.
        /// </summary>
        public bool HasPerRoundCaps
        {
            get
            {
                for (int i = 0; i < _units.Count; i++)
                    if (_units[i].Def.AttacksPerRound > 0 || _units[i].Def.SkillUsesPerRound > 0) return true;
                return false;
            }
        }

        /// <summary>May this unit attack again this round? Uncapped units always may.</summary>
        public bool CanAttackAgain(UnitState unit) =>
            unit != null && (unit.Def.AttacksPerRound <= 0 || unit.AttacksThisRound < unit.Def.AttacksPerRound);

        /// <summary>May this unit use another skill this round? Uncapped units always may.</summary>
        public bool CanUseSkillAgain(UnitState unit) =>
            unit != null && (unit.Def.SkillUsesPerRound <= 0 || unit.SkillUsesThisRound < unit.Def.SkillUsesPerRound);

        /// <summary>
        /// The DEF this unit actually defends with right now. Never below zero:
        /// a negative DEF would turn 破甲 into a damage multiplier under the
        /// subtractive model and into nonsense under the percentage one.
        ///
        /// Every damage computation must come through here. Reading Def.Def
        /// directly is the bug this method exists to prevent.
        /// </summary>
        public int EffectiveDef(UnitState unit)
        {
            if (unit == null) return 0;
            int def = unit.Def.Def;
            int fortify = unit.StatusMagnitude(StatusKind.Fortify);
            int frail = unit.StatusMagnitude(StatusKind.Frail);
            if (fortify > 0) def = def * fortify / 100;
            if (frail > 0) def = def * frail / 100;
            if (IsArmorBroken(unit)) def -= unit.ArmorBrokenAmount;
            return def < 0 ? 0 : def;
        }

        public bool IsSlowed(UnitState unit) => unit != null && TurnIndex <= unit.SlowedUntilTurn;

        public bool IsTaunting(UnitState unit) => unit != null && TurnIndex <= unit.TauntingUntilTurn;

        /// <summary>
        /// The living unit of this faction currently taunting, or null. First in
        /// id order when several taunt at once, so ties resolve the same way every
        /// run (determinism rule 2).
        /// </summary>
        public UnitState ActiveTaunter(Faction faction)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                UnitState u = _units[i];
                if (u.IsAlive && u.Faction == faction && IsTaunting(u)) return u;
            }
            return null;
        }

        /// <summary>
        /// Last round a status applied NOW should still be active for the given
        /// side.
        ///
        /// A round is Player phase then Enemy phase under one TurnIndex. So a
        /// status the player puts on an enemy during the player phase covers the
        /// enemy phase that immediately follows and then lapses; but one an enemy
        /// puts on the player during the enemy phase has to survive into the next
        /// round, because the player has already moved this one. Without this the
        /// same skill would be worth a full turn in one direction and nothing at
        /// all in the other.
        /// </summary>
        public int StatusExpiryTurnFor(Faction affected)
        {
            bool affectedSideAlreadyActed = CurrentFaction == Faction.Enemy && affected == Faction.Player;
            return TurnIndex + (affectedSideAlreadyActed ? 1 : 0);
        }

        /// <summary>True when any spawn was marked as a Kill target. Gates the
        /// matching fold in StateHasher, so encounters without one hash exactly as
        /// they did before Kill existed.</summary>
        public bool HasObjectiveTarget => ObjectiveTarget() != null;

        public IEnumerable<UnitState> LivingUnitsOf(Faction f)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                UnitState u = _units[i];
                if (u.IsAlive && u.Faction == f) yield return u;
            }
        }

        /// <summary>Living units of this faction that are expected to fight (excludes protected objectives).</summary>
        public int CountLivingCombatants(Faction f)
        {
            int n = 0;
            for (int i = 0; i < _units.Count; i++)
                if (_units[i].IsAlive && _units[i].Faction == f && !_units[i].MustSurvive) n++;
            return n;
        }

        public int CountLiving(Faction f)
        {
            int n = 0;
            for (int i = 0; i < _units.Count; i++)
                if (_units[i].IsAlive && _units[i].Faction == f) n++;
            return n;
        }
    }
}
