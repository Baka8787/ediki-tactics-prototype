namespace Ediki.Core
{
    public enum ObjectiveKind
    {
        /// <summary>Kill everything. The original Stage 01 condition.</summary>
        Rout = 0,

        /// <summary>Stand on a target cell before the turn limit runs out.</summary>
        Reach = 1,

        /// <summary>Stay alive until the turn limit passes.</summary>
        Survive = 2,

        /// <summary>Keep every protected unit alive until the turn limit passes.</summary>
        Defend = 3,

        /// <summary>
        /// Kill one designated enemy. The rest of the field may be left standing.
        ///
        /// The only objective here that does NOT specify where the player stands
        /// (contrast Reach's cell and Defend's prop), which is why it is the only
        /// one that can add a decision without taxing the Exposure question the
        /// prototype exists to ask. What it adds is "which of these do I even have
        /// to fight" — under Rout that question does not exist, because the answer
        /// is always "all of them".
        ///
        /// The target is marked on the spawn (target=true), not by coordinate: it
        /// is a unit, and it moves.
        /// </summary>
        Kill = 4
    }

    /// <summary>
    /// What this battle is actually about.
    ///
    /// Rout with no time pressure degrades into turtling: if holding a corridor is
    /// always optimal, nothing else in the design ever gets used. Every other kind
    /// here exists to give the player a reason to move.
    ///
    /// Per-encounter data. R-WIN-04 ("the rules have no turn limit") still holds
    /// for Rout — the limit belongs to the objective, not to the rule layer.
    /// </summary>
    public sealed class ObjectiveDef
    {
        public readonly ObjectiveKind Kind;

        /// <summary>Destination for Reach. Ignored otherwise.</summary>
        public readonly Coord Target;

        /// <summary>Rounds allowed. 0 = no limit (only valid for Rout).</summary>
        public readonly int TurnLimit;

        public ObjectiveDef(ObjectiveKind kind, Coord target, int turnLimit)
        {
            Kind = kind;
            Target = target;
            TurnLimit = turnLimit;
        }

        public static readonly ObjectiveDef Rout = new ObjectiveDef(ObjectiveKind.Rout, default, 0);

        public bool HasTurnLimit => TurnLimit > 0;

        public string Describe()
        {
            switch (Kind)
            {
                case ObjectiveKind.Reach:
                    return "reach " + Target + (HasTurnLimit ? " within " + TurnLimit + " turns" : "");
                case ObjectiveKind.Survive:
                    return "survive " + TurnLimit + " turns";
                case ObjectiveKind.Defend:
                    return "defend for " + TurnLimit + " turns";
                case ObjectiveKind.Kill:
                    return "kill the marked enemy" + (HasTurnLimit ? " within " + TurnLimit + " turns" : "");
                default:
                    return "defeat all enemies";
            }
        }
    }
}
