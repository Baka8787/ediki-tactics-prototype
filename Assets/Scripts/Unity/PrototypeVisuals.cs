using Ediki.Core;
using UnityEngine;

namespace Ediki.Unity
{
    /// <summary>Second visual channel: what the unit DOES. Never its faction.</summary>
    public enum UnitArchetype
    {
        /// <summary>Cannot move at all — a shrine, a turret, the thing being defended.</summary>
        Prop = 0,
        Support = 1,
        Ranged = 2,
        Mobile = 3,
        Heavy = 4,
        Melee = 5
    }

    /// <summary>Terrain shown as geometry, derived from what the terrain DOES.</summary>
    public enum TileStyle
    {
        Normal = 0,
        Rough = 1,
        Swamp = 2,
        Chasm = 3,
        Obstacle = 4
    }

    /// <summary>
    /// The prototype's visual grammar, in one place, derived from data only.
    ///
    /// Shared by the editor's map view and the playtest board on purpose: a map
    /// that reads one way while you build it and another way while you play it
    /// is a map you have to learn twice. This file is the single description of
    /// what a chasm looks like and what shape a ranged unit is, and it lives in
    /// Ediki.Unity because the game cannot depend on the editor while the editor
    /// already depends on the game.
    ///
    /// Three independent channels, so no channel has to carry two meanings at
    /// once: faction picks the COLOUR family, archetype picks the SHAPE, and
    /// state adds MARKERS on top of both. Identity is text, not hue — which is
    /// why both views label their units.
    ///
    /// Everything here reads UnitDef and TerrainDef fields, never an id or a
    /// name: a unit added to units.txt shows up correctly with no code change (A5).
    /// </summary>
    public static class PrototypeVisuals
    {
        // ------------------------------------------------------------- shapes

        /// <summary>
        /// Ordered by how much the answer changes what you must do about the unit.
        /// "It never moves" beats everything, because a prop is a position rather
        /// than a threat; "it hits from outside melee" beats "it is tough",
        /// because reach decides where you may stand and toughness only decides
        /// how long it takes.
        /// </summary>
        public static UnitArchetype ArchetypeOf(UnitDef def)
        {
            if (def == null) return UnitArchetype.Melee;
            if (def.Move <= 0) return UnitArchetype.Prop;
            if (def.CanPurify) return UnitArchetype.Support;
            if (def.AttackRange >= 2) return UnitArchetype.Ranged;
            if (def.Move >= 5) return UnitArchetype.Mobile;
            if (def.ImmuneToPush || def.MaxHp >= 300) return UnitArchetype.Heavy;
            return UnitArchetype.Melee;
        }

        public static TileStyle StyleOf(TerrainDef terrain)
        {
            if (terrain == null) return TileStyle.Normal;
            if (terrain.BlocksMovement) return TileStyle.Obstacle;
            if (terrain.IsLethal) return TileStyle.Chasm;
            if (terrain.MovementCostHundredths >= 300) return TileStyle.Swamp;
            if (terrain.MovementCostHundredths >= 200) return TileStyle.Rough;
            return TileStyle.Normal;
        }

        /// <summary>
        /// Height the tile's top face sits at, in cells. Negative sinks it.
        ///
        /// This is the whole reason terrain is legible without art: a chasm is a
        /// hole you can see into, a swamp is below the ground you walk on, and a
        /// wall is something you cannot see past.
        /// </summary>
        public static float TileTopHeight(TileStyle style)
        {
            switch (style)
            {
                case TileStyle.Obstacle: return 0.90f;
                case TileStyle.Rough: return 0.26f;
                case TileStyle.Swamp: return 0.04f;
                case TileStyle.Chasm: return -0.55f;
                default: return 0.14f;
            }
        }

        // ------------------------------------------------------------ colours

        public static readonly Color PlayerBody = new Color(0.30f, 0.55f, 0.88f);
        public static readonly Color EnemyBody = new Color(0.84f, 0.29f, 0.26f);

        /// <summary>
        /// The thing being defended, or the one enemy that must die. Neither
        /// side's soldier — it is the objective, and that is the only case where
        /// colour is allowed to mean something other than faction.
        /// </summary>
        public static readonly Color ObjectiveGold = new Color(0.92f, 0.74f, 0.24f);

        public static readonly Color UnknownUnit = new Color(0.45f, 0.45f, 0.48f);

        public static readonly Color GroundPlane = new Color(0.10f, 0.11f, 0.13f);
        public static readonly Color ChasmFloor = new Color(0.06f, 0.06f, 0.09f);
        public static readonly Color ChasmWall = new Color(0.16f, 0.16f, 0.21f);

        public static Color BodyColor(Faction faction, bool isObjective)
        {
            if (isObjective) return ObjectiveGold;
            return faction == Faction.Player ? PlayerBody : EnemyBody;
        }

        /// <summary>
        /// Named terrain gets a hand-picked hue so the shipped maps look the way
        /// the team already reads them. Anything terrain.txt adds later still
        /// gets a sensible colour from its style.
        /// </summary>
        public static Color TileColor(TerrainDef def)
        {
            if (def != null)
            {
                switch (def.Name)
                {
                    case "Open": return new Color(0.44f, 0.52f, 0.38f);
                    case "Road": return new Color(0.60f, 0.56f, 0.46f);
                    case "Forest": return new Color(0.23f, 0.42f, 0.26f);
                    case "Highland": return new Color(0.56f, 0.47f, 0.33f);
                    case "Mire": return new Color(0.32f, 0.35f, 0.26f);
                    case "Blocking": return new Color(0.20f, 0.21f, 0.25f);
                }
            }

            switch (StyleOf(def))
            {
                case TileStyle.Obstacle: return new Color(0.20f, 0.21f, 0.25f);
                case TileStyle.Swamp: return new Color(0.32f, 0.35f, 0.26f);
                case TileStyle.Rough: return new Color(0.34f, 0.44f, 0.30f);
                case TileStyle.Chasm: return ChasmFloor;
                default: return new Color(0.46f, 0.50f, 0.44f);
            }
        }

        // -------------------------------------------------------------- naming

        public static string PlannerNameOf(UnitArchetype a)
        {
            switch (a)
            {
                case UnitArchetype.Prop: return "設施";
                case UnitArchetype.Support: return "輔助";
                case UnitArchetype.Ranged: return "遠程";
                case UnitArchetype.Mobile: return "機動";
                case UnitArchetype.Heavy: return "重裝";
                default: return "近戰";
            }
        }

        public static string PlannerNameOf(TileStyle s)
        {
            switch (s)
            {
                case TileStyle.Obstacle: return "障礙";
                case TileStyle.Rough: return "難行";
                case TileStyle.Swamp: return "泥沼";
                case TileStyle.Chasm: return "深坑";
                default: return "平地";
            }
        }
    }
}
