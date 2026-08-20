using System;
using System.Collections.Generic;
using Ediki.Core;
using Ediki.Core.Data;
using UnityEditor;
using UnityEngine;

namespace Ediki.Editor.Prototype
{
    public sealed partial class PrototypeEditorWindow
    {
        /// <summary>
        /// Everything Save and Play need, computed WITHOUT touching a single file.
        ///
        /// The order this enforces is the whole point. Materialising first and
        /// validating afterwards meant a rejected playtest still appended rows to
        /// units.txt and rewrote the unit ids in the open document — a refusal
        /// that left permanent changes behind, on the file every encounter in the
        /// project reads. Now nothing is written until the canonical loader has
        /// accepted the exact bytes that would land on disk.
        /// </summary>
        private sealed class PreparedEncounter
        {
            /// <summary>Variant ids the pending stat edits resolve to.</summary>
            public UnitVariantWriter.Plan Plan;

            /// <summary>Full units.txt text to write, or null when no new row is needed.</summary>
            public string UnitsText;

            /// <summary>The validated encounter text.</summary>
            public string EncounterText;

            /// <summary>A COPY of the document with the plan applied. The live one is untouched.</summary>
            public EncounterDocument Document;
        }

        /// <summary>
        /// Resolves pending stat edits, validates the result, and reports.
        /// Returns false having changed nothing at all — no file, no document.
        /// </summary>
        private bool TryPrepare(string what, out PreparedEncounter prepared)
        {
            prepared = null;

            UnitVariantWriter.Plan plan = UnitVariantWriter.BuildPlan(_doc, _data.Units, _data.UnitIds);

            // A provisional catalog: units.txt as it WOULD be, parsed in memory.
            // Validation has to see the new variants — the document is about to
            // reference them — but seeing them must not mean creating them.
            UnitCatalog units = _data.Units;
            List<string> unitIds = _data.UnitIds;
            string unitsText = null;

            if (plan.NewUnitLines.Count > 0)
            {
                string unitsAsset = EditorDataFiles.AssetPath(EditorDataFiles.UnitsFile);
                string existing = EditorDataFiles.ReadOrNull(unitsAsset);
                if (existing == null)
                {
                    SetStatus(what + "：找不到 " + unitsAsset + "，無法建立新角色。", MessageType.Error);
                    return false;
                }

                unitsText = UnitVariantWriter.AppendBlock(existing, plan.NewUnitLines);
                try
                {
                    units = UnitLoader.Parse(unitsText);
                }
                catch (Exception ex)
                {
                    SetStatus(what + "：新角色的數值寫不出合法的資料，沒有任何檔案被改動。\n" + ex.Message,
                              MessageType.Error);
                    return false;
                }

                unitIds = new List<string>(_data.UnitIds);
                foreach (KeyValuePair<int, string> rewrite in plan.SpawnIdRewrites)
                    if (!unitIds.Contains(rewrite.Value)) unitIds.Add(rewrite.Value);
            }

            // Validate a COPY. If the gate refuses, the document the planner is
            // looking at still has its pending edits and its original ids, so
            // nothing they typed is lost and nothing silently moved.
            EncounterDocument candidate = _doc.Clone();
            UnitVariantWriter.ApplyToDocument(candidate, plan);

            EncounterValidation.GateResult gate =
                EncounterValidation.Gate(candidate, _data.Terrain, units, _data.AiProfiles, _data.Roster);
            ApplyIssues(gate.Issues);

            if (!gate.Ok)
            {
                ReportGateFailure(gate, what);
                return false;
            }

            prepared = new PreparedEncounter
            {
                Plan = plan,
                UnitsText = unitsText,
                EncounterText = gate.Text,
                Document = candidate
            };
            return true;
        }

        /// <summary>
        /// Writes what TryPrepare approved.
        ///
        /// units.txt goes first because the encounter references the ids it
        /// introduces; if that write fails nothing else is attempted, so the two
        /// files cannot end up disagreeing about which units exist.
        /// </summary>
        private bool Commit(PreparedEncounter prepared, string encounterAssetPath, string what)
        {
            if (prepared.UnitsText != null)
            {
                string unitsAsset = EditorDataFiles.AssetPath(EditorDataFiles.UnitsFile);
                string error = EditorDataFiles.Write(unitsAsset, prepared.UnitsText);
                if (error != null)
                {
                    SetStatus(what + "：寫入角色資料失敗，關卡沒有被儲存。\n" + error, MessageType.Error);
                    return false;
                }
            }

            string encounterError = EditorDataFiles.Write(encounterAssetPath, prepared.EncounterText);
            if (encounterError != null)
            {
                SetStatus(what + "：寫入關卡失敗。\n" + encounterError, MessageType.Error);
                return false;
            }

            // Only now does the live document adopt the new ids.
            UnitVariantWriter.ApplyToDocument(_doc, prepared.Plan);

            if (prepared.UnitsText != null) ReloadCatalogs();
            Revalidate();

            return true;
        }

        private string PlanNotes(PreparedEncounter prepared)
        {
            return prepared.Plan.Notes.Count == 0
                ? string.Empty
                : "\n" + string.Join("\n", prepared.Plan.Notes.ToArray());
        }
    }
}
