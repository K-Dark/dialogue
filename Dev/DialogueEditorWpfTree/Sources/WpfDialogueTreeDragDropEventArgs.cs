using System;

namespace DialogueEditor.WpfTree
{
    public sealed class WpfDialogueTreeDragDropEventArgs : EventArgs
    {
        public int SourceNodeID { get; private set; }
        public int TargetNodeID { get; private set; }

        public WpfDialogueTreeDragDropEventArgs(int sourceNodeID, int targetNodeID)
        {
            SourceNodeID = sourceNodeID;
            TargetNodeID = targetNodeID;
        }
    }
}
