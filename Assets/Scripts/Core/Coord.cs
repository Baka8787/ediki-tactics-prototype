using System;

namespace Ediki.Core
{
    /// <summary>
    /// Grid coordinate. Core owns its own type because Ediki.Core has
    /// No Engine References and cannot use UnityEngine.Vector2Int (ADR-0001).
    /// </summary>
    public readonly struct Coord : IEquatable<Coord>, IComparable<Coord>
    {
        public readonly int X;
        public readonly int Y;

        public Coord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(Coord other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Coord c && Equals(c);

        /// <summary>
        /// Only for dictionary bucketing inside a single process.
        /// NEVER use this for world-state hashing (determinism rule 3) —
        /// see StateHasher.
        /// </summary>
        public override int GetHashCode() => (X * 397) ^ Y;

        /// <summary>Deterministic ordering: row-major. Used as tie-break key.</summary>
        public int CompareTo(Coord other)
        {
            if (Y != other.Y) return Y.CompareTo(other.Y);
            return X.CompareTo(other.X);
        }

        public static bool operator ==(Coord a, Coord b) => a.Equals(b);
        public static bool operator !=(Coord a, Coord b) => !a.Equals(b);

        public static int ManhattanDistance(Coord a, Coord b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        public override string ToString() => "(" + X + "," + Y + ")";
    }
}
