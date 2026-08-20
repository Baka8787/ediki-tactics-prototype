using System.Collections.Generic;
using System.Text;

namespace Ediki.Core
{
    /// <summary>
    /// One atomic thing that happened. Granularity rule: an Effect is something
    /// the presentation layer can play on its own, with no decisions of its own (A7).
    /// Order within an EffectLog is causal order.
    /// </summary>
    public abstract class Effect
    {
        public abstract string Describe();
        public override string ToString() => Describe();
    }

    public sealed class TurnStarted : Effect
    {
        public readonly int TurnIndex;
        public readonly Faction Faction;
        public TurnStarted(int turnIndex, Faction faction) { TurnIndex = turnIndex; Faction = faction; }
        public override string Describe() => "TurnStarted T" + TurnIndex + " " + Faction;
    }

    public sealed class TurnEnded : Effect
    {
        public readonly int TurnIndex;
        public readonly Faction Faction;
        public TurnEnded(int turnIndex, Faction faction) { TurnIndex = turnIndex; Faction = faction; }
        public override string Describe() => "TurnEnded T" + TurnIndex + " " + Faction;
    }

    public sealed class ApSpent : Effect
    {
        public readonly int UnitId;
        public readonly int Amount;
        public readonly int Remaining;
        public ApSpent(int unitId, int amount, int remaining) { UnitId = unitId; Amount = amount; Remaining = remaining; }
        public override string Describe() => "ApSpent u" + UnitId + " -" + Amount + " (=" + Remaining + ")";
    }

    public sealed class ApReset : Effect
    {
        public readonly int UnitId;
        public readonly int NewAp;
        public readonly int Gained;

        /// <summary>
        /// Regen that spilled over the cap and was lost. With AP carrying over this
        /// is the only genuine waste — leftover AP is banked, not thrown away.
        /// </summary>
        public readonly int Wasted;

        public ApReset(int unitId, int newAp, int gained, int wasted)
        { UnitId = unitId; NewAp = newAp; Gained = gained; Wasted = wasted; }

        public override string Describe() =>
            "ApReset u" + UnitId + " +" + Gained + " =" + NewAp + (Wasted > 0 ? " (lost " + Wasted + ")" : "");
    }

    public sealed class UnitMoved : Effect
    {
        public readonly int UnitId;
        public readonly Coord From;
        public readonly Coord To;
        public readonly Coord[] Path;
        public UnitMoved(int unitId, Coord from, Coord to, Coord[] path)
        { UnitId = unitId; From = from; To = to; Path = path; }
        public override string Describe() => "UnitMoved u" + UnitId + " " + From + "->" + To;
    }

    public sealed class AttackResolved : Effect
    {
        public readonly int AttackerId;
        public readonly int TargetId;
        public readonly bool Hit;   // always true under the deterministic-hit baseline (OD-05)
        public AttackResolved(int attackerId, int targetId, bool hit)
        { AttackerId = attackerId; TargetId = targetId; Hit = hit; }
        public override string Describe() => "AttackResolved u" + AttackerId + "->u" + TargetId + (Hit ? " HIT" : " MISS");
    }

    public enum HpChangeCause { Attack = 0, Counter = 1, Terrain = 2, Rest = 3, Status = 4 }

    public sealed class HpChanged : Effect
    {
        public readonly int UnitId;
        public readonly int Delta;
        public readonly int NewHp;
        public readonly HpChangeCause Cause;
        public HpChanged(int unitId, int delta, int newHp, HpChangeCause cause)
        { UnitId = unitId; Delta = delta; NewHp = newHp; Cause = cause; }
        public override string Describe() => "HpChanged u" + UnitId + " " + Delta + " (=" + NewHp + ")";
    }

    public sealed class StatusApplied : Effect
    {
        public readonly int UnitId; public readonly StatusKind Kind; public readonly int RemainingPhases; public readonly int Magnitude;
        public StatusApplied(int unitId, StatusKind kind, int phases, int magnitude)
        { UnitId = unitId; Kind = kind; RemainingPhases = phases; Magnitude = magnitude; }
        public override string Describe() => "StatusApplied u" + UnitId + " " + Kind + " " + RemainingPhases + "p @" + Magnitude;
    }

    public sealed class StatusExpired : Effect
    {
        public readonly int UnitId; public readonly StatusKind Kind;
        public StatusExpired(int unitId, StatusKind kind) { UnitId = unitId; Kind = kind; }
        public override string Describe() => "StatusExpired u" + UnitId + " " + Kind;
    }

    public sealed class UnitDied : Effect
    {
        public readonly int UnitId;
        public readonly Faction Faction;   // lets presentation tell 昏厥 from 消滅 later (CONFLICT-08)
        public UnitDied(int unitId, Faction faction) { UnitId = unitId; Faction = faction; }
        public override string Describe() => "UnitDied u" + UnitId + " (" + Faction + ")";
    }

    public sealed class GuardApplied : Effect
    {
        public readonly int UnitId;
        public readonly int DamagePercent;
        public GuardApplied(int unitId, int damagePercent) { UnitId = unitId; DamagePercent = damagePercent; }
        public override string Describe() => "GuardApplied u" + UnitId + " dmg%" + DamagePercent;
    }

    // Control statuses carry the turn they lapse ON, not a duration. There is no
    // matching Expired effect for the same reason there is no tick: the status is
    // a stamp compared against TurnIndex, so nothing happens when it ends and the
    // presentation layer can see for itself whether it is still live.

    public sealed class TauntApplied : Effect
    {
        public readonly int UnitId;
        public readonly int Radius;
        public readonly int UntilTurn;
        public TauntApplied(int unitId, int radius, int untilTurn)
        { UnitId = unitId; Radius = radius; UntilTurn = untilTurn; }
        public override string Describe() => "TauntApplied u" + UnitId + " r" + Radius + " thru T" + UntilTurn;
    }

    public sealed class SlowApplied : Effect
    {
        public readonly int UnitId;
        public readonly int UntilTurn;
        public SlowApplied(int unitId, int untilTurn) { UnitId = unitId; UntilTurn = untilTurn; }
        public override string Describe() => "SlowApplied u" + UnitId + " thru T" + UntilTurn;
    }

    public sealed class ArmorBreakApplied : Effect
    {
        public readonly int UnitId;
        public readonly int Amount;
        public readonly int UntilTurn;
        public ArmorBreakApplied(int unitId, int amount, int untilTurn)
        { UnitId = unitId; Amount = amount; UntilTurn = untilTurn; }
        public override string Describe() =>
            "ArmorBreakApplied u" + UnitId + " -" + Amount + " DEF thru T" + UntilTurn;
    }

    public sealed class UnitPushed : Effect
    {
        public readonly int UnitId;
        public readonly Coord From;
        public readonly Coord To;
        public readonly int ByUnitId;
        public UnitPushed(int unitId, Coord from, Coord to, int byUnitId)
        { UnitId = unitId; From = from; To = to; ByUnitId = byUnitId; }
        public override string Describe() => "UnitPushed u" + UnitId + " " + From + " -> " + To + " by u" + ByUnitId;
    }

    /// <summary>
    /// A unit died because of where it ended up, not because of damage. Separate
    /// from HpChanged + UnitDied on purpose: the presentation layer needs to tell
    /// "fell into the pit" from "was killed by a hit", and so does the telemetry —
    /// a terrain kill costs the killer no damage output at all.
    /// </summary>
    public sealed class UnitFellIntoHazard : Effect
    {
        public readonly int UnitId;
        public readonly Coord Cell;
        public readonly string TerrainName;
        public UnitFellIntoHazard(int unitId, Coord cell, string terrainName)
        { UnitId = unitId; Cell = cell; TerrainName = terrainName; }
        public override string Describe() =>
            "UnitFellIntoHazard u" + UnitId + " at " + Cell + " (" + TerrainName + ")";
    }

    public sealed class GuardExpired : Effect
    {
        public readonly int UnitId;
        public GuardExpired(int unitId) { UnitId = unitId; }
        public override string Describe() => "GuardExpired u" + UnitId;
    }

    public sealed class UnitRested : Effect
    {
        public readonly int UnitId;
        public readonly int HealAmount;
        public UnitRested(int unitId, int healAmount) { UnitId = unitId; HealAmount = healAmount; }
        public override string Describe() => "UnitRested u" + UnitId + " +" + HealAmount;
    }

    public sealed class UnitWaited : Effect
    {
        public readonly int UnitId;
        public readonly int UnusedAp;
        public UnitWaited(int unitId, int unusedAp) { UnitId = unitId; UnusedAp = unusedAp; }
        public override string Describe() => "UnitWaited u" + UnitId + " unused=" + UnusedAp;
    }

    /// <summary>
    /// Per-unit activation, not per-faction.
    ///
    /// SPEC v0.1 used a faction-level FactionActivated, which contradicts the
    /// "pull one at a time" design intent (R-THR-06). Per-unit is the reading
    /// that matches OD-10's "Perception".
    /// </summary>
    public sealed class UnitActivated : Effect
    {
        public readonly int UnitId;
        public readonly int TriggeredByUnitId;
        public UnitActivated(int unitId, int triggeredByUnitId)
        { UnitId = unitId; TriggeredByUnitId = triggeredByUnitId; }
        public override string Describe() => "UnitActivated u" + UnitId + " by u" + TriggeredByUnitId;
    }

    public sealed class CounterAttacked : Effect
    {
        public readonly int DefenderId;
        public readonly int AttackerId;
        public CounterAttacked(int defenderId, int attackerId)
        { DefenderId = defenderId; AttackerId = attackerId; }
        public override string Describe() => "CounterAttacked u" + DefenderId + " -> u" + AttackerId;
    }

    public sealed class UnitSpawned : Effect
    {
        public readonly int UnitId;
        public readonly Faction Faction;
        public readonly Coord Position;
        public UnitSpawned(int unitId, Faction faction, Coord position)
        { UnitId = unitId; Faction = faction; Position = position; }
        public override string Describe() => "UnitSpawned u" + UnitId + " (" + Faction + ") at " + Position;
    }

    /// <summary>One cell's contamination changed. One effect per cell, so the
    /// presentation layer can animate a spreading edge rather than a whole area.</summary>
    public sealed class TerrainContaminated : Effect
    {
        public readonly Coord Cell;
        public readonly int Level;
        public readonly int SourceUnitId;   // -1 when it was purified rather than spread

        public TerrainContaminated(Coord cell, int level, int sourceUnitId)
        { Cell = cell; Level = level; SourceUnitId = sourceUnitId; }

        public override string Describe() =>
            (SourceUnitId < 0 ? "TerrainPurified " : "TerrainContaminated ") + Cell + " =" + Level;
    }

    public sealed class BattleEnded : Effect
    {
        public readonly BattleOutcome Outcome;
        public BattleEnded(BattleOutcome outcome) { Outcome = outcome; }
        public override string Describe() => "BattleEnded " + Outcome;
    }

    /// <summary>Ordered, immutable-after-construction list of Effects for one command.</summary>
    public sealed class EffectLog
    {
        private readonly List<Effect> _effects = new List<Effect>();

        public static readonly EffectLog Empty = new EffectLog();

        public IReadOnlyList<Effect> Effects => _effects;
        public int Count => _effects.Count;
        public Effect this[int i] => _effects[i];

        public void Add(Effect e) => _effects.Add(e);

        public bool Contains<T>() where T : Effect
        {
            for (int i = 0; i < _effects.Count; i++)
                if (_effects[i] is T) return true;
            return false;
        }

        public T First<T>() where T : Effect
        {
            for (int i = 0; i < _effects.Count; i++)
                if (_effects[i] is T t) return t;
            return null;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _effects.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(_effects[i].Describe());
            }
            return sb.ToString();
        }
    }
}
