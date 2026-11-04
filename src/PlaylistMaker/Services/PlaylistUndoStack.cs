using System.Collections.Generic;

namespace PlaylistMaker.Services
{
    /// <summary>Snapshot based undo/redo: every mutation of the playlist pushes the previous path list.</summary>
    public sealed class PlaylistUndoStack
    {
        private readonly Stack<List<string>> undoStates = new Stack<List<string>>();
        private readonly Stack<List<string>> redoStates = new Stack<List<string>>();

        public bool CanUndo => undoStates.Count > 0;

        public bool CanRedo => redoStates.Count > 0;

        public int UndoDepth => undoStates.Count;

        public int RedoDepth => redoStates.Count;

        /// <summary>Call right BEFORE the playlist changes.</summary>
        public void Record(IEnumerable<string> currentPaths)
        {
            undoStates.Push(new List<string>(currentPaths));
            redoStates.Clear();
        }

        /// <summary>Returns the state to restore, or null when there is nothing to undo.</summary>
        public List<string>? Undo(IEnumerable<string> currentPaths)
        {
            if (undoStates.Count == 0)
            {
                return null;
            }
            redoStates.Push(new List<string>(currentPaths));
            return undoStates.Pop();
        }

        public List<string>? Redo(IEnumerable<string> currentPaths)
        {
            if (redoStates.Count == 0)
            {
                return null;
            }
            undoStates.Push(new List<string>(currentPaths));
            return redoStates.Pop();
        }

        public void Clear()
        {
            undoStates.Clear();
            redoStates.Clear();
        }
    }
}
