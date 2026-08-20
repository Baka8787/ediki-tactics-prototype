using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// Carries un-materialised stat edits across a domain reload.
    ///
    /// Everything else about a document survives as its canonical encounter
    /// text, which is guaranteed to round-trip. Pending edits cannot: the
    /// format has no per-spawn stat fields and is not being given any, so a
    /// stat the planner has typed but not yet saved lives only in memory — and
    /// memory is exactly what entering play mode throws away.
    ///
    /// The wire format is the same `unit` line units.txt uses, one per edited
    /// spawn, prefixed with the spawn index. Reusing the shipped writer and
    /// reader means there is no second description of what a stat block is, and
    /// no chance of the two disagreeing about a field.
    /// </summary>
    public static class PendingEditsStore
    {
        private const char RecordSeparator = '\n';

        public static string Save(EncounterDocument doc)
        {
            if (doc == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < doc.Spawns.Count; i++)
            {
                SpawnEntry spawn = doc.Spawns[i];
                if (!spawn.HasPendingStats) continue;

                sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(' ');
                sb.Append(spawn.PendingStats.ToDataLine("pending")).Append(RecordSeparator);
            }
            return sb.ToString();
        }

        public static void Load(EncounterDocument doc, string stored)
        {
            if (doc == null || string.IsNullOrEmpty(stored)) return;

            string[] records = stored.Split(RecordSeparator);
            for (int r = 0; r < records.Length; r++)
            {
                string record = records[r].Trim();
                if (record.Length == 0) continue;

                int space = record.IndexOf(' ');
                if (space <= 0) continue;

                int index;
                if (!int.TryParse(record.Substring(0, space), NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out index)) continue;
                if (index < 0 || index >= doc.Spawns.Count) continue;

                try
                {
                    Ediki.Core.UnitCatalog parsed =
                        Ediki.Core.Data.UnitLoader.Parse(record.Substring(space + 1));
                    doc.Spawns[index].PendingStats = UnitStatBlock.From(parsed.Get("pending"));
                }
                catch (Exception)
                {
                    // A record that no longer parses means the stat schema moved
                    // under us. Dropping that one edit is right; refusing to
                    // restore the whole document because of it is not.
                }
            }
        }
    }
}
