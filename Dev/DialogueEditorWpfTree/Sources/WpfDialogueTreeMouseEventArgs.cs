using System;
using System.Windows;

namespace DialogueEditor.WpfTree
{
    public sealed class WpfDialogueTreeMouseEventArgs : EventArgs
    {
        public int NodeID { get; private set; }
        public Point Position { get; private set; }

        public WpfDialogueTreeMouseEventArgs(int nodeID, Point position)
        {
            NodeID = nodeID;
            Position = position;
        }
    }
}
