namespace Ediki.Core
{
    /// <summary>
    /// A request to change state. Commands never validate themselves and never
    /// mutate anything — BattleSimulator.Execute does both (R-CMD-01).
    /// </summary>
    public interface ICommand
    {
        string Describe();
    }

    /// <summary>
    /// Carries the FULL path, not just a destination: cost depends on which
    /// cells are crossed, so the destination alone cannot be validated (R-MOVE-06).
    /// </summary>
    public sealed class MoveCommand : ICommand
    {
        public readonly int UnitId;
        public readonly Coord[] Path;   // excludes the unit's current cell

        public MoveCommand(int unitId, Coord[] path)
        {
            UnitId = unitId;
            Path = path;
        }

        public string Describe()
        {
            string last = Path != null && Path.Length > 0 ? Path[Path.Length - 1].ToString() : "(none)";
            return "Move u" + UnitId + " -> " + last + " (" + (Path == null ? 0 : Path.Length) + " steps)";
        }
    }

    public sealed class AttackCommand : ICommand
    {
        public readonly int AttackerId;
        public readonly int TargetId;

        public AttackCommand(int attackerId, int targetId)
        {
            AttackerId = attackerId;
            TargetId = targetId;
        }

        public string Describe() => "Attack u" + AttackerId + " -> u" + TargetId;
    }

    public sealed class GuardCommand : ICommand
    {
        public readonly int UnitId;
        public GuardCommand(int unitId) { UnitId = unitId; }
        public string Describe() => "Guard u" + UnitId;
    }

    /// <summary>
    /// Rest: spend a little AP to recover a slice of max HP, then stand down for
    /// the turn. Cheap enough to be worth doing with leftover AP, and ending the
    /// activation is the cost that stops it from being a free top-up mid-fight.
    /// </summary>
    public sealed class RestCommand : ICommand
    {
        public readonly int UnitId;
        public RestCommand(int unitId) { UnitId = unitId; }
        public string Describe() => "Rest u" + UnitId;
    }

    /// <summary>
    /// 仙照・淺淨: strip one level of contamination from every cell within the
    /// unit's purify radius. GDD: 4 AP, radius 2 — thirteen cells on a
    /// four-neighbour grid, and only Momotaro can do it.
    ///
    /// It takes no target: the area is always centred on the caster, so there is
    /// nothing to aim and nothing to validate beyond "can this unit purify".
    /// </summary>
    public sealed class PurifyCommand : ICommand
    {
        public readonly int UnitId;
        public PurifyCommand(int unitId) { UnitId = unitId; }
        public string Describe() => "Purify u" + UnitId;
    }

    /// <summary>
    /// 【不動明王】: stand as the thing worth hitting. Every enemy that would
    /// otherwise pick its own target picks this unit instead, until this unit's
    /// next turn.
    ///
    /// Takes no target — it changes who the ENEMY targets, not who this unit hits.
    /// That is the whole point: with one player unit "who do they attack" has no
    /// answer, and adding a second unit only refunded damage (the party round
    /// measured M5 separation 23 -> 3). Taunt is what makes "who stands in front"
    /// a decision instead of an accident.
    /// </summary>
    public sealed class TauntCommand : ICommand
    {
        public readonly int UnitId;
        public TauntCommand(int unitId) { UnitId = unitId; }
        public string Describe() => "Taunt u" + UnitId;
    }

    /// <summary>
    /// 【遲滯】: the target pays +1 AP for every cell it enters, for one round.
    ///
    /// GDD gives this to Kagemaru verbatim ("移動 AP+1"). It is here because every
    /// lever this project has measured that moves the win rate a long way changes
    /// WHEN the enemy can reach you — range (+51), terrain cost (non-monotonic
    /// +15), growth — and every lever that only changes how hard someone hits was
    /// small or a cliff. Slow is the cheapest possible player-side handle on that
    /// variable.
    /// </summary>
    public sealed class SlowCommand : ICommand
    {
        public readonly int ActorId;
        public readonly int TargetId;

        public SlowCommand(int actorId, int targetId)
        {
            ActorId = actorId;
            TargetId = targetId;
        }

        public string Describe() => "Slow u" + ActorId + " -> u" + TargetId;
    }

    /// <summary>
    /// 擊退: shove the target one cell directly away. No damage.
    ///
    /// The one verb this project has never had. Into the Breach is built almost
    /// entirely on it — most of its mechs cannot kill what is in front of them, so
    /// the question every turn is "where does this thing end up" rather than "who
    /// do I hit". The GDD already assumes displacement exists (Zhengshou is
    /// 免疫擊退, Genjin 免疫位移), it just never says who does it.
    ///
    /// Deliberately fails rather than fizzles when the destination is blocked:
    /// a push that silently does nothing would spend AP for no effect, and the
    /// player cannot see the difference.
    /// </summary>
    public sealed class PushCommand : ICommand
    {
        public readonly int ActorId;
        public readonly int TargetId;

        public PushCommand(int actorId, int targetId)
        {
            ActorId = actorId;
            TargetId = targetId;
        }

        public string Describe() => "Push u" + ActorId + " -> u" + TargetId;
    }

    /// <summary>
    /// 破甲: strip DEF off the target for a round. No damage of its own.
    ///
    /// A setup action, and deliberately a bad one to use alone. What it buys is
    /// a step on the hit-count ladder for SOMEBODY ELSE — this project measured
    /// that ladder as a cliff (two hits 68%, three hits 1%), so the only thing
    /// worth paying for is crossing a step, and nothing else in the kit can move
    /// one. That makes it the first skill here whose value is defined by what a
    /// different unit does next, which is the point: a party where one unit's
    /// best action depends on another's is the thing the AP experiments failed
    /// to produce twice.
    ///
    /// Refuses to stack for the same reason Slow does — re-applying would be a
    /// way to burn AP that looks productive to the action-mix metric.
    /// </summary>
    public sealed class ArmorBreakCommand : ICommand
    {
        public readonly int ActorId;
        public readonly int TargetId;

        public ArmorBreakCommand(int actorId, int targetId)
        {
            ActorId = actorId;
            TargetId = targetId;
        }

        public string Describe() => "ArmorBreak u" + ActorId + " -> u" + TargetId;
    }

    /// <summary>Unit voluntarily ends its own activation, keeping any unspent AP visible.</summary>
    public sealed class WaitCommand : ICommand
    {
        public readonly int UnitId;
        public WaitCommand(int unitId) { UnitId = unitId; }
        public string Describe() => "Wait u" + UnitId;
    }

    public sealed class EndTurnCommand : ICommand
    {
        public readonly Faction Faction;
        public EndTurnCommand(Faction faction) { Faction = faction; }
        public string Describe() => "EndTurn " + Faction;
    }
}
