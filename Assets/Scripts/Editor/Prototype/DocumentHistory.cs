using System.Collections.Generic;

namespace Ediki.Editor.Prototype
{
    /// <summary>
    /// Undo/redo by whole-document snapshot.
    ///
    /// Chosen over a command/diff system on purpose. An EncounterDocument for a
    /// 64x64 map is one 4096-int array plus a short spawn list — a few tens of
    /// kilobytes — so 64 snapshots cost less than one texture, and every edit
    /// (paint, drag, resize, place, delete, retype a stat) is covered by the same
    /// three lines instead of needing its own inverse operation. A command系統
    /// would be the larger framework the brief explicitly asks not to build
    /// quietly.
    ///
    /// The cost is that undo granularity is whatever the caller decides to push,
    /// which is why a drag-paint pushes ONE snapshot on mouse-down rather than
    /// one per cell — a stroke is what a person thinks they did.
    /// </summary>
    public sealed class DocumentHistory
    {
        public const int Capacity = 64;

        private readonly List<EncounterDocument> _undo = new List<EncounterDocument>();
        private readonly List<EncounterDocument> _redo = new List<EncounterDocument>();

        /// <summary>Label of the step Undo would take back, for the menu item.</summary>
        private readonly List<string> _undoLabels = new List<string>();
        private readonly List<string> _redoLabels = new List<string>();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public string NextUndoLabel => _undoLabels.Count > 0 ? _undoLabels[_undoLabels.Count - 1] : null;
        public string NextRedoLabel => _redoLabels.Count > 0 ? _redoLabels[_redoLabels.Count - 1] : null;

        /// <summary>
        /// Call BEFORE mutating, with the document as it is now. Redo is dropped,
        /// which is the standard contract: a new edit after an undo forks history
        /// and the abandoned branch is not something anyone asks for back.
        /// </summary>
        public void Push(EncounterDocument current, string label)
        {
            if (current == null) return;

            _undo.Add(current.Clone());
            _undoLabels.Add(label ?? "編輯");

            if (_undo.Count > Capacity)
            {
                _undo.RemoveAt(0);
                _undoLabels.RemoveAt(0);
            }

            _redo.Clear();
            _redoLabels.Clear();
        }

        /// <summary>Hands back the previous document; give it the CURRENT one to keep for redo.</summary>
        public EncounterDocument Undo(EncounterDocument current)
        {
            if (_undo.Count == 0) return null;

            int last = _undo.Count - 1;
            EncounterDocument previous = _undo[last];
            string label = _undoLabels[last];
            _undo.RemoveAt(last);
            _undoLabels.RemoveAt(last);

            _redo.Add(current.Clone());
            _redoLabels.Add(label);
            return previous;
        }

        public EncounterDocument Redo(EncounterDocument current)
        {
            if (_redo.Count == 0) return null;

            int last = _redo.Count - 1;
            EncounterDocument next = _redo[last];
            string label = _redoLabels[last];
            _redo.RemoveAt(last);
            _redoLabels.RemoveAt(last);

            _undo.Add(current.Clone());
            _undoLabels.Add(label);
            return next;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _undoLabels.Clear();
            _redoLabels.Clear();
        }
    }
}
