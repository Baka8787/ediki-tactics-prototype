namespace Ediki.Core
{
    public enum Faction
    {
        Player = 0,
        Enemy = 1
    }

    public enum BattleOutcome
    {
        InProgress = 0,
        Victory = 1,
        Defeat = 2
    }

    public static class FactionExtensions
    {
        public static Faction Opponent(this Faction f)
        {
            return f == Faction.Player ? Faction.Enemy : Faction.Player;
        }
    }
}
