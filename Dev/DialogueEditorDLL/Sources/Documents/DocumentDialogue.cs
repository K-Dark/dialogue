using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace DialogueEditor
{
    public partial class DocumentDialogue : DockContent, IDocument
    {
        //--------------------------------------------------------------------------------------------------------------
        // Helper Class

        protected enum EDisplayRowKind
        {
            None,
            Context,
            Comment,
        }

        protected class NodeWrap
        {
            public DialogueNode DialogueNode;
            public bool IsDisplayRow;
            public EDisplayRowKind DisplayRowKind;
            public TreeNode OwnerTreeNode;

            public NodeWrap(DialogueNode dialogueNode)
            {
                DialogueNode = dialogueNode;
                IsDisplayRow = false;
                DisplayRowKind = EDisplayRowKind.None;
                OwnerTreeNode = null;
            }

            public NodeWrap(DialogueNode dialogueNode, EDisplayRowKind displayRowKind, TreeNode ownerTreeNode)
            {
                DialogueNode = dialogueNode;
                IsDisplayRow = true;
                DisplayRowKind = displayRowKind;
                OwnerTreeNode = ownerTreeNode;
            }
        }

        protected class State
        {
            public string Content;
            public int NodeID = DialogueNode.ID_NULL;
        }

        //--------------------------------------------------------------------------------------------------------------
        // Public vars

        public Dialogue Dialogue;
        public event Action<DocumentDialogue, DialogueNode> SelectedNodeChanged;
        public event Action<DocumentDialogue> DialogueChanged;

        public bool ForceClose = false;

        //--------------------------------------------------------------------------------------------------------------
        // Internal vars

        protected bool lockCheckDisplayEvents = false;
        protected int copyReference = -1;

        protected bool pendingDirty = false;
        protected List<State> previousStates = new List<State>();
        protected int indexState = 0;
        protected int displayNodeKeyCounter = 0;
        private NumericUpDown numericWpfGapDefault;
        private NumericUpDown numericWpfGapSameSpeaker;
        private Label labelWpfGapDefault;
        private Label labelWpfGapSameSpeaker;
        private ToolTip toolTipWpfGap;

        //--------------------------------------------------------------------------------------------------------------
        // Class Methods

        public DocumentDialogue(Dialogue inDialogue)
        {
            InitializeComponent();
            ThemeManager.ApplyTheme(this);

            EditorHelper.AbsorbMouseWheelEvent(comboBoxLanguages);

            Dialogue = inDialogue;
            Name = Dialogue.GetName();
            tree.ImageList = EditorCore.DefaultImageList;
            InitializeWpfTreeHost();
            InitializeWpfGapControls();

            //Use this to have multiple colors on a single node.
            //If there are visual glitches, you can try commenting this block.
            //Note: Allowing default rendering will allow drag&drop with left mouse button.
            tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            tree.DrawNode += OnTreeViewDrawNode;
            tree.FullRowSelect = true;
            tree.MouseDown += OnTreeMouseDown;

            //Ensure custom properties were generated for this dialogue
            Dialogue.GenerateCustomProperties();

            SaveState();

            ResyncDisplayOptions();
            ResyncDocument(useWpfDialogueTree);
            SelectRootNode();
            RefreshTitle();
        }

        public virtual void SetDirty()
        {
            pendingDirty = false;
            ResourcesHandler.SetDirty(Dialogue);

            RefreshTitle();

            SaveState();
            RefreshWpfTreeHost();
            DialogueChanged?.Invoke(this);
        }

        public void SetPendingDirty()
        {
            pendingDirty = true;
        }

        public void CancelPendingDirty()
        {
            pendingDirty = false;
        }

        public void ResolvePendingDirty()
        {
            if (pendingDirty)
            {
                EditorCore.Properties?.OnResolvePendingDirty();
                EditorCore.CustomProperties?.OnResolvePendingDirty();

                if (pendingDirty)   //OnResolvePendingDirty may call CancelPendingDirty
                {
                    SetDirty();    //Raise dirty + store Undo State
                }
            }
        }

        public void OnPostSave()
        {
            if (pendingDirty)
            {
                pendingDirty = false;
                SaveState();    //No need to raise dirty since we just saved, but the Undo State needs to be stored
            }
        }

        public void OnPostReload()
        {
            pendingDirty = false;
            //ResetStates();
            SaveState();

            ResyncDocument();
            SelectRootNode();
            RefreshTitle();
            DialogueChanged?.Invoke(this);
        }

        public void RefreshDocument()
        {
            ResyncDisplayOptions();
            RefreshAllTreeNodes();
            ResyncSelectedNode();
        }

        public void RefreshTitle()
        {
            Text = Dialogue.GetName();
            if (ResourcesHandler.IsDirty(Dialogue))
                Text += "*";
        }

        public void ResyncDocument(bool redraw = true)
        {
            WIN32.StopRedraw(this);
            tree.BeginUpdate();

            Clear();

            if (Dialogue.RootNode != null)
            {
                TreeNode newTreeNodeRoot = tree.Nodes.Add(GetNodeKey(Dialogue.RootNode.ID));
                newTreeNodeRoot.Tag = new NodeWrap(Dialogue.RootNode);
                newTreeNodeRoot.ContextMenuStrip = contextMenu;
                EditorHelper.SetNodeIcon(newTreeNodeRoot, ENodeIcon.Dialogue);

                AddTreeNodeChild(Dialogue.RootNode.Next, newTreeNodeRoot);
                newTreeNodeRoot.Expand();
            }

            tree.EndUpdate();
            WIN32.ResumeRedraw(this);

            if (redraw)
            {
                RefreshAllTreeNodes();
            }

            RefreshWpfTreeHost();
        }

        public void CreateNodeSentence(TreeNode treeNodeFrom, bool branch)
        {
            if (branch && (!(GetDialogueNode(treeNodeFrom) is DialogueNodeBranch)))
                return;

            DialogueNodeSentence nodeSentence = new DialogueNodeSentence();
            Dialogue.AddNode(nodeSentence);

            TreeNode newTreeNode = AddNodeSentence(treeNodeFrom, nodeSentence, branch);
            SelectTreeNode(newTreeNode);

            if (EditorCore.Properties != null)
                EditorCore.Properties.ForceFocus();

            SetDirty();
        }

        public void CreateNodeChoice(TreeNode treeNodeFrom, bool branch)
        {
            if (branch && (!(GetDialogueNode(treeNodeFrom) is DialogueNodeBranch)))
                return;

            DialogueNodeChoice nodeChoice = new DialogueNodeChoice();
            Dialogue.AddNode(nodeChoice);

            TreeNode newTreeNode = AddNodeChoice(treeNodeFrom, nodeChoice, branch);
            SelectTreeNode(newTreeNode);

            if (EditorCore.Properties != null)
                EditorCore.Properties.ForceFocus();

            SetDirty();
        }

        public void CreateNodeReply(TreeNode treeNodeFrom)
        {
            if (!IsTreeNodeChoice(tree.SelectedNode))
                return;

            DialogueNodeReply nodeReply = new DialogueNodeReply();
            Dialogue.AddNode(nodeReply);

            TreeNode newTreeNode = AddNodeReply(treeNodeFrom, nodeReply);
            SelectTreeNode(newTreeNode);

            if (EditorCore.Properties != null)
                EditorCore.Properties.ForceFocus();

            SetDirty();
        }

        public void CreateNodeGoto(TreeNode treeNodeFrom, bool branch)
        {
            if (branch && (!(GetDialogueNode(treeNodeFrom) is DialogueNodeBranch)))
                return;

            DialogueNodeGoto nodeGoto = new DialogueNodeGoto();
            Dialogue.AddNode(nodeGoto);

            TreeNode newTreeNode = AddNodeGoto(treeNodeFrom, nodeGoto, branch);
            SelectTreeNode(newTreeNode);

            if (EditorCore.Properties != null)
                EditorCore.Properties.ForceFocus();

            SetDirty();
        }

        public void CreateNodeBranch(TreeNode treeNodeFrom, bool branch)
        {
            if (branch && (!(GetDialogueNode(treeNodeFrom) is DialogueNodeBranch)))
                return;

            DialogueNodeBranch nodeBranch = new DialogueNodeBranch();
            Dialogue.AddNode(nodeBranch);

            TreeNode newTreeNode = AddNodeBranch(treeNodeFrom, nodeBranch, branch);
            SelectTreeNode(newTreeNode);

            if (EditorCore.Properties != null)
                EditorCore.Properties.ForceFocus();

            SetDirty();
        }

        public void CreateNodeReturn(TreeNode treeNodeFrom, bool branch)
        {
            if (branch && (!(GetDialogueNode(treeNodeFrom) is DialogueNodeBranch)))
                return;

            DialogueNodeReturn nodeReturn = new DialogueNodeReturn();
            Dialogue.AddNode(nodeReturn);

            TreeNode newTreeNode = AddNodeReturn(treeNodeFrom, nodeReturn, branch);
            SelectTreeNode(newTreeNode);

            if (EditorCore.Properties != null)
                EditorCore.Properties.ForceFocus();

            SetDirty();
        }

        public void CreateNodeComment(TreeNode treeNodeFrom, bool branch)
        {
            if (branch && (!(GetDialogueNode(treeNodeFrom) is DialogueNodeBranch)))
                return;

            DialogueNodeComment nodeComment = new DialogueNodeComment();
            Dialogue.AddNode(nodeComment);

            TreeNode newTreeNode = AddNodeComment(treeNodeFrom, nodeComment, branch);
            SelectTreeNode(newTreeNode);

            if (EditorCore.Properties != null)
                EditorCore.Properties.ForceFocus();

            SetDirty();
        }

        public virtual TreeNode AddNodeSentence(TreeNode treeNodeFrom, DialogueNodeSentence sentence, bool branch)
        {
            treeNodeFrom = GetRealTreeNode(treeNodeFrom);
            if (treeNodeFrom == null || sentence == null)
                return null;

            WIN32.StopRedraw(this);
            tree.BeginUpdate();
            StripDisplayRows(tree.Nodes);

            TreeNode newTreeNode = null;
            if (branch || IsTreeNodeRoot(treeNodeFrom) || IsTreeNodeReply(treeNodeFrom))
            {
                newTreeNode = AddTreeNodeChild(sentence, treeNodeFrom);
                treeNodeFrom.Expand();
            }
            else
            {
                newTreeNode = AddTreeNodeSibling(sentence, treeNodeFrom);
            }

            ResolvePostNodeInsertion(treeNodeFrom, sentence, branch);
            RefreshDisplayRows();

            tree.EndUpdate();
            WIN32.ResumeRedraw(this);
            this.Refresh();

            return newTreeNode;
        }

        public virtual TreeNode AddNodeChoice(TreeNode treeNodeFrom, DialogueNodeChoice choice, bool branch)
        {
            treeNodeFrom = GetRealTreeNode(treeNodeFrom);
            if (treeNodeFrom == null || choice == null)
                return null;

            WIN32.StopRedraw(this);
            tree.BeginUpdate();
            StripDisplayRows(tree.Nodes);

            TreeNode newTreeNode = null;
            if (branch || IsTreeNodeRoot(treeNodeFrom) || IsTreeNodeReply(treeNodeFrom))
            {
                newTreeNode = AddTreeNodeChild(choice, treeNodeFrom);
                treeNodeFrom.Expand();
            }
            else
            {
                newTreeNode = AddTreeNodeSibling(choice, treeNodeFrom);
            }

            ResolvePostNodeInsertion(treeNodeFrom, choice, branch);
            RefreshDisplayRows();

            tree.EndUpdate();
            WIN32.ResumeRedraw(this);
            this.Refresh();

            return newTreeNode;
        }

        public virtual TreeNode AddNodeReply(TreeNode treeNodeFrom, DialogueNodeReply reply)
        {
            treeNodeFrom = GetRealTreeNode(treeNodeFrom);
            if (!IsTreeNodeChoice(treeNodeFrom) || reply == null)
                return null;

            WIN32.StopRedraw(this);
            tree.BeginUpdate();
            StripDisplayRows(tree.Nodes);

            TreeNode newTreeNode = null;
            newTreeNode = AddTreeNode(reply, treeNodeFrom, treeNodeFrom.LastNode);
            treeNodeFrom.Expand();

            var nodeDialogueFrom = GetDialogueNode(treeNodeFrom) as DialogueNodeChoice;
            nodeDialogueFrom.Replies.Add(reply);
            RefreshDisplayRows();

            tree.EndUpdate();
            WIN32.ResumeRedraw(this);
            this.Refresh();

            return newTreeNode;
        }

        public virtual TreeNode AddNodeGoto(TreeNode treeNodeFrom, DialogueNodeGoto nodeGoto, bool branch)
        {
            treeNodeFrom = GetRealTreeNode(treeNodeFrom);
            if (treeNodeFrom == null || nodeGoto == null)
                return null;

            WIN32.StopRedraw(this);
            tree.BeginUpdate();
            StripDisplayRows(tree.Nodes);

            TreeNode newTreeNode = null;
            if (branch || IsTreeNodeRoot(treeNodeFrom) || IsTreeNodeReply(treeNodeFrom))
            {
                newTreeNode = AddTreeNodeChild(nodeGoto, treeNodeFrom);
                treeNodeFrom.Expand();
            }
            else
            {
                newTreeNode = AddTreeNodeSibling(nodeGoto, treeNodeFrom);
            }

            ResolvePostNodeInsertion(treeNodeFrom, nodeGoto, branch);
            RefreshDisplayRows();

            tree.EndUpdate();
            WIN32.ResumeRedraw(this);
            this.Refresh();

            return newTreeNode;
        }

        public virtual TreeNode AddNodeBranch(TreeNode treeNodeFrom, DialogueNodeBranch nodeBranch, bool branch)
        {
            treeNodeFrom = GetRealTreeNode(treeNodeFrom);
            if (treeNodeFrom == null || nodeBranch == null)
                return null;

            WIN32.StopRedraw(this);
            tree.BeginUpdate();
            StripDisplayRows(tree.Nodes);

            TreeNode newTreeNode = null;
            if (branch || IsTreeNodeRoot(treeNodeFrom) || IsTreeNodeReply(treeNodeFrom))
            {
                newTreeNode = AddTreeNodeChild(nodeBranch, treeNodeFrom);
                treeNodeFrom.Expand();
            }
            else
            {
                newTreeNode = AddTreeNodeSibling(nodeBranch, treeNodeFrom);
            }

            ResolvePostNodeInsertion(treeNodeFrom, nodeBranch, branch);
            RefreshDisplayRows();

            tree.EndUpdate();
            WIN32.ResumeRedraw(this);
            this.Refresh();

            return newTreeNode;
        }

        public virtual TreeNode AddNodeReturn(TreeNode treeNodeFrom, DialogueNodeReturn nodeReturn, bool branch)
        {
            treeNodeFrom = GetRealTreeNode(treeNodeFrom);
            if (treeNodeFrom == null || nodeReturn == null)
                return null;

            WIN32.StopRedraw(this);
            tree.BeginUpdate();
            StripDisplayRows(tree.Nodes);

            TreeNode newTreeNode = null;
            if (branch || IsTreeNodeRoot(treeNodeFrom) || IsTreeNodeReply(treeNodeFrom))
            {
                newTreeNode = AddTreeNodeChild(nodeReturn, treeNodeFrom);
                treeNodeFrom.Expand();
            }
            else
            {
                newTreeNode = AddTreeNodeSibling(nodeReturn, treeNodeFrom);
            }

            ResolvePostNodeInsertion(treeNodeFrom, nodeReturn, branch);
            RefreshDisplayRows();

            tree.EndUpdate();
            WIN32.ResumeRedraw(this);
            this.Refresh();

            return newTreeNode;
        }

        public virtual TreeNode AddNodeComment(TreeNode treeNodeFrom, DialogueNodeComment nodeComment, bool branch)
        {
            treeNodeFrom = GetRealTreeNode(treeNodeFrom);
            if (treeNodeFrom == null || nodeComment == null)
                return null;

            WIN32.StopRedraw(this);
            tree.BeginUpdate();
            StripDisplayRows(tree.Nodes);

            TreeNode newTreeNode = null;
            if (branch || IsTreeNodeRoot(treeNodeFrom) || IsTreeNodeReply(treeNodeFrom))
            {
                newTreeNode = AddTreeNodeChild(nodeComment, treeNodeFrom);
                treeNodeFrom.Expand();
            }
            else
            {
                newTreeNode = AddTreeNodeSibling(nodeComment, treeNodeFrom);
            }

            ResolvePostNodeInsertion(treeNodeFrom, nodeComment, branch);
            RefreshDisplayRows();

            tree.EndUpdate();
            WIN32.ResumeRedraw(this);
            this.Refresh();

            return newTreeNode;
        }

        private void ResolvePostNodeInsertion(TreeNode treeNodeFrom, DialogueNode newNode, bool branch)
        {
            var nodeDialogueFrom = GetDialogueNode(treeNodeFrom);

            //Find the last node of the inserted sequence
            var lastNode = newNode;
            while (lastNode.Next != null)
            {
                lastNode = lastNode.Next;
            }

            if (branch)
            {
                var nodeBranch = nodeDialogueFrom as DialogueNodeBranch;
                lastNode.Next = nodeBranch.Branch;
                nodeBranch.Branch = newNode;
            }
            else
            {
                lastNode.Next = nodeDialogueFrom.Next;
                nodeDialogueFrom.Next = newNode;
            }
        }

        private TreeNode AddTreeNodeChild(DialogueNode node, TreeNode parentTreeNode)
        {
            if (node == null || parentTreeNode == null)
                return null;

            return AddTreeNode(node, parentTreeNode, null);
        }

        private TreeNode AddTreeNodeSibling(DialogueNode node, TreeNode previousTreeNode)
        {
            if (node == null || previousTreeNode == null)
                return null;

            return AddTreeNode(node, previousTreeNode.Parent, previousTreeNode);
        }

        private TreeNode AddTreeNode(DialogueNode node, TreeNode parentTreeNode, TreeNode previousTreeNode)
        {
            if (node == null || parentTreeNode == null)
                return null;

            TreeNode newTreeNode = null;
            int insertIndex = 0;
            if (previousTreeNode != null)
            {
                insertIndex = parentTreeNode.Nodes.IndexOf(previousTreeNode);
                if (insertIndex == -1)
                    insertIndex = 0;
                else
                    ++insertIndex;
            }

            if (node is DialogueNodeSentence)
            {
                DialogueNodeSentence nodeSentence = node as DialogueNodeSentence;

                newTreeNode = parentTreeNode.Nodes.Insert(insertIndex, GetNodeKey(node.ID), "");
                newTreeNode.Tag = new NodeWrap(node);
                newTreeNode.ContextMenuStrip = contextMenu;
                EditorHelper.SetNodeIcon(newTreeNode, ENodeIcon.Sentence);

                AddTreeNodeSibling(node.Next, newTreeNode);
            }
            else if (node is DialogueNodeChoice)
            {
                DialogueNodeChoice nodeChoice = node as DialogueNodeChoice;

                newTreeNode = parentTreeNode.Nodes.Insert(insertIndex, GetNodeKey(node.ID), "");
                newTreeNode.Tag = new NodeWrap(node);
                newTreeNode.ContextMenuStrip = contextMenu;
                EditorHelper.SetNodeIcon(newTreeNode, ENodeIcon.Choice);

                foreach (DialogueNodeReply reply in nodeChoice.Replies)
                {
                    AddTreeNode(reply, newTreeNode, newTreeNode.LastNode);
                }

                AddTreeNodeSibling(node.Next, newTreeNode);
                newTreeNode.Expand();
            }
            else if (node is DialogueNodeReply)
            {
                DialogueNodeReply nodeReply = node as DialogueNodeReply;

                newTreeNode = parentTreeNode.Nodes.Insert(insertIndex, GetNodeKey(node.ID), "");
                newTreeNode.Tag = new NodeWrap(node);
                newTreeNode.ContextMenuStrip = contextMenu;
                EditorHelper.SetNodeIcon(newTreeNode, ENodeIcon.Reply);

                AddTreeNodeChild(node.Next, newTreeNode);
                newTreeNode.Expand();
            }
            else if (node is DialogueNodeGoto)
            {
                DialogueNodeGoto nodeGoto = node as DialogueNodeGoto;

                newTreeNode = parentTreeNode.Nodes.Insert(insertIndex, GetNodeKey(node.ID), "");
                newTreeNode.Tag = new NodeWrap(node);
                newTreeNode.ContextMenuStrip = contextMenu;
                EditorHelper.SetNodeIcon(newTreeNode, ENodeIcon.Goto);

                AddTreeNodeSibling(node.Next, newTreeNode);
            }
            else if (node is DialogueNodeBranch)
            {
                DialogueNodeBranch nodeBranch = node as DialogueNodeBranch;

                newTreeNode = parentTreeNode.Nodes.Insert(insertIndex, GetNodeKey(node.ID), "");
                newTreeNode.Tag = new NodeWrap(node);
                newTreeNode.ContextMenuStrip = contextMenu;
                EditorHelper.SetNodeIcon(newTreeNode, ENodeIcon.Branch);

                AddTreeNodeChild(nodeBranch.Branch, newTreeNode);

                AddTreeNodeSibling(node.Next, newTreeNode);
                newTreeNode.Expand();
            }
            else if (node is DialogueNodeReturn)
            {
                DialogueNodeReturn nodeReturn = node as DialogueNodeReturn;

                newTreeNode = parentTreeNode.Nodes.Insert(insertIndex, GetNodeKey(node.ID), "");
                newTreeNode.Tag = new NodeWrap(node);
                newTreeNode.ContextMenuStrip = contextMenu;
                EditorHelper.SetNodeIcon(newTreeNode, ENodeIcon.Goto);

                AddTreeNodeSibling(node.Next, newTreeNode);
            }
            else if (node is DialogueNodeComment)
            {
                newTreeNode = parentTreeNode.Nodes.Insert(insertIndex, GetNodeKey(node.ID), "");
                newTreeNode.Tag = new NodeWrap(node);
                newTreeNode.ContextMenuStrip = contextMenu;
                EditorHelper.SetNodeIcon(newTreeNode, ENodeIcon.Comment);

                AddTreeNodeSibling(node.Next, newTreeNode);
            }

            if (newTreeNode != null)
            {
                RefreshTreeNode_Impl(newTreeNode);
            }

            return newTreeNode;
        }

        public void RemoveNode(TreeNode treeNode)
        {
            TreeNode selectedNode = GetRealTreeNode(treeNode);
            DialogueNode dialogueNode = GetDialogueNode(selectedNode);
            RemoveNode(dialogueNode, selectedNode);
        }

        public virtual void RemoveNode(DialogueNode node, TreeNode treeNode)
        {
            treeNode = GetRealTreeNode(treeNode);
            if (node == null || treeNode == null)
                return;

            if (treeNode == GetRootNode())
                return;

            if (copyReference == node.ID)
                copyReference = -1;

            StripDisplayRows(tree.Nodes);
            Dialogue.RemoveNode(node);
            treeNode.Remove();

            RefreshAllTreeNodes();

            SetDirty();
        }

        public void RemoveAllNodes()
        {
            copyReference = -1;

            StripDisplayRows(tree.Nodes);
            Dialogue.ListNodes.RemoveAll(item => item != Dialogue.RootNode);
            Dialogue.RootNode.Next = null;

            GetRootNode().Nodes.Clear();

            RefreshAllTreeNodes();

            SetDirty();
        }

        private enum EMoveTreeNode
        {
            Sibling,
            Drop,
            DropSpecial,
        }

        private bool MoveTreeNode(TreeNode nodeMove, TreeNode nodeTarget, EMoveTreeNode moveType)
        {
            nodeMove = GetRealTreeNode(nodeMove);
            nodeTarget = GetRealTreeNode(nodeTarget);

            if (nodeMove == null || nodeTarget == null || nodeMove == nodeTarget)
                return false;

            if (IsTreeNodeRoot(nodeMove))
                return false;

            if (IsTreeNodeReply(nodeMove) && !IsTreeNodeReply(nodeTarget) && !IsTreeNodeChoice(nodeTarget))
                return false;

            //Check we are not attaching a node on a depending node (loop)
            List<DialogueNode> dependendingNodes = new List<DialogueNode>();
            Dialogue.GetDependingNodes(GetDialogueNode(nodeMove), ref dependendingNodes);
            if (dependendingNodes.Contains(GetDialogueNode(nodeTarget)))
                return false;

            StripDisplayRows(tree.Nodes);

            if (IsTreeNodeReply(nodeMove))
            {
                TreeNode nodeChoiceFrom = nodeMove.Parent;
                DialogueNodeReply dialogueNodeMove = GetDialogueNode(nodeMove) as DialogueNodeReply;
                DialogueNodeChoice dialogueNodeChoiceFrom = GetDialogueNode(nodeChoiceFrom) as DialogueNodeChoice;

                if (IsTreeNodeReply(nodeTarget))
                {
                    TreeNode nodeChoiceTo = nodeTarget.Parent;
                    DialogueNodeReply dialogueNodeTarget = GetDialogueNode(nodeTarget) as DialogueNodeReply;
                    DialogueNodeChoice dialogueNodeChoiceTo = GetDialogueNode(nodeChoiceTo) as DialogueNodeChoice;

                    //remove reply from its choice
                    dialogueNodeChoiceFrom.Replies.Remove(dialogueNodeMove);
                    nodeChoiceFrom.Nodes.Remove(nodeMove);

                    //insert reply after another reply inside a choice
                    dialogueNodeChoiceTo.Replies.Insert(dialogueNodeChoiceTo.Replies.IndexOf(dialogueNodeTarget) + 1, dialogueNodeMove);
                    nodeChoiceTo.Nodes.Insert(nodeChoiceTo.Nodes.IndexOf(nodeTarget) + 1, nodeMove);
                }
                else if (IsTreeNodeChoice(nodeTarget))
                {
                    TreeNode nodeChoiceTo = nodeTarget;
                    DialogueNodeChoice dialogueNodeChoiceTo = GetDialogueNode(nodeChoiceTo) as DialogueNodeChoice;

                    //remove reply from its choice
                    dialogueNodeChoiceFrom.Replies.Remove(dialogueNodeMove);
                    nodeChoiceFrom.Nodes.Remove(nodeMove);

                    //insert reply as first reply of a choice
                    dialogueNodeChoiceTo.Replies.Insert(0, dialogueNodeMove);
                    nodeChoiceTo.Nodes.Insert(0, nodeMove);
                }
                else
                {
                    RefreshDisplayRows();
                    return false;   //this should not happen, the case is checked above
                }
            }
            else
            {
                TreeNode nodeParentFrom = nodeMove.Parent;
                TreeNode nodePrev = nodeMove.PrevNode;
                DialogueNode dialogueNodeMove = GetDialogueNode(nodeMove);
                
                bool branchTarget = false;
                if (IsTreeNodeBranch(nodeTarget))
                {
                    //We are using a move up/down on a node inside a targetted branch
                    //Or using a special drop to force the branch target
                    if ((moveType == EMoveTreeNode.Sibling && nodeMove.Parent == nodeTarget)
                    ||   moveType == EMoveTreeNode.DropSpecial)
                    {
                        branchTarget = true;
                    }
                }

                //remove node from current position
                if (nodePrev != null)
                {
                    DialogueNode dialogueNodePrev = GetDialogueNode(nodePrev);
                    dialogueNodePrev.Next = dialogueNodeMove.Next;
                    dialogueNodeMove.Next = null;
                    nodeParentFrom.Nodes.Remove(nodeMove);
                }
                else
                {
                    DialogueNode dialogueNodeParentFrom = GetDialogueNode(nodeParentFrom);
                    if (IsTreeNodeBranch(nodeParentFrom) && nodeParentFrom.FirstNode == nodeMove)
                    {
                        //node is a branch child, we need to redirect the branch
                        ((DialogueNodeBranch)dialogueNodeParentFrom).Branch = dialogueNodeMove.Next;
                        dialogueNodeMove.Next = null;
                        nodeParentFrom.Nodes.Remove(nodeMove);
                    }
                    else
                    {
                        dialogueNodeParentFrom.Next = dialogueNodeMove.Next;
                        dialogueNodeMove.Next = null;
                        nodeParentFrom.Nodes.Remove(nodeMove);
                    }
                }

                //insert node on new position
                DialogueNode dialogueNodeTarget = GetDialogueNode(nodeTarget);
                if (branchTarget)
                {
                    dialogueNodeMove.Next = ((DialogueNodeBranch)dialogueNodeTarget).Branch;
                    ((DialogueNodeBranch)dialogueNodeTarget).Branch = dialogueNodeMove;
                }
                else
                {
                    dialogueNodeMove.Next = dialogueNodeTarget.Next;
                    dialogueNodeTarget.Next = dialogueNodeMove;
                }

                if (IsTreeNodeRoot(nodeTarget)
                ||  IsTreeNodeReply(nodeTarget)
                ||  branchTarget)
                {
                    nodeTarget.Nodes.Insert(0, nodeMove);
                }
                else
                {
                    TreeNode nodeParentTo = nodeTarget.Parent;
                    nodeParentTo.Nodes.Insert(nodeParentTo.Nodes.IndexOf(nodeTarget) + 1, nodeMove);
                }
            }

            SelectTreeNode(nodeMove);
            RefreshDisplayRows();

            return true;
        }

        public void SelectNode(int id)
        {
            SelectTreeNode(GetTreeNode(id));
        }

        public void SelectNode(DialogueNode node)
        {
            SelectTreeNode(GetTreeNode(node));
        }

        public void SelectRootNode()
        {
            SelectTreeNode(GetRootNode());
        }

        public virtual void SelectTreeNode(TreeNode treeNode)
        {
            treeNode = GetRealTreeNode(treeNode);
            if (treeNode == null)
                return;

            tree.SelectedNode = treeNode;
            SyncWpfTreeSelection();
        }

        public void ResyncSelectedNode()
        {
            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);
            if (selectedNode == null)
                return;
            
            //tree.BeginUpdate();   //Doesnt seem necessary since we will not edit much most of the time, and keeping it adds a permanent flickering

            UnHighlightAll();

            DialogueNodeGoto nodeGoto = ((NodeWrap)selectedNode.Tag).DialogueNode as DialogueNodeGoto;
            if (nodeGoto != null)
            {
                Highlight(nodeGoto.Goto);

                List<DialogueNode> gotos = Dialogue.GetGotoReferencesOnNode(nodeGoto.Goto);
                Highlight(gotos, Color.DarkGray);
            }
            else
            {
                List<DialogueNode> gotos = Dialogue.GetGotoReferencesOnNode(GetSelectedDialogueNode());
                if (gotos.Count > 0)
                {
                    Highlight(gotos, Color.DarkGray);
                }
            }

            EditorCore.Properties?.ShowDialogueNodeProperties(this, selectedNode, ((NodeWrap)selectedNode.Tag).DialogueNode);
            EditorCore.CustomProperties?.ShowDialogueNodeProperties(this, selectedNode, ((NodeWrap)selectedNode.Tag).DialogueNode);
            RefreshWpfTreeHost();

            //tree.EndUpdate();
        }

        public void RefreshAllTreeNodes()
        {
            TreeNode rootNode = GetRootNode();
            if (rootNode != null)
                RefreshAllTreeNodes(rootNode);
            else
                RefreshWpfTreeHost();
        }

        public void RefreshAllTreeNodes(TreeNode parent)
        {
            TreeNode rootParent = GetRealTreeNode(parent);
            if (rootParent == null)
                return;
            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);

            WIN32.StopRedraw(this);
            tree.BeginUpdate();

            StripDisplayRows(tree.Nodes);

            RefreshTreeNode_Impl(rootParent);
            RefreshAllTreeNodes_Impl(rootParent);

            RefreshDisplayRows(selectedNode);

            tree.EndUpdate();
            WIN32.ResumeRedraw(this);
            this.Refresh();
            RefreshWpfTreeHost();
        }

        public void RefreshTreeNode(TreeNode treeNode)
        {
            TreeNode realNode = GetRealTreeNode(treeNode);
            if (realNode == null)
                return;

            WIN32.StopRedraw(this);
            tree.BeginUpdate();

            StripDisplayRows(tree.Nodes);

            RefreshTreeNode_Impl(realNode);

            RefreshDisplayRows(realNode);

            tree.EndUpdate();
            WIN32.ResumeRedraw(this);
            this.Refresh();
            RefreshWpfTreeHost();
        }

        public void RefreshTreeNodeForWorkstringEdit(TreeNode treeNode)
        {
            if (EditorCore.Settings.RefreshTreeViewOnEdit)
            {
                RefreshTreeNode(treeNode);
            }
        }

        public void RefreshTreeNodeForWorkstringValidation(TreeNode treeNode)
        {
            if (!EditorCore.Settings.RefreshTreeViewOnEdit)
            {
                RefreshTreeNode(treeNode);
            }
        }

        public void RefreshSelectedTreeNode()
        {
            RefreshTreeNode(tree.SelectedNode);
        }

        private void RefreshAllTreeNodes_Impl(TreeNode parent)
        {
            foreach (TreeNode node in parent.Nodes)
            {
                if (IsDisplayTreeNode(node))
                    continue;

                RefreshTreeNode_Impl(node, true);
                RefreshAllTreeNodes_Impl(node);
            }
        }

        private void RefreshTreeNode_Impl(TreeNode treeNode, bool isTreeRefresh = false)
        {
            if (treeNode == null)
                return;

            DialogueNode dialogueNode = ((NodeWrap)treeNode.Tag).DialogueNode;

            //Refresh Goto nodes targeting this node (only if not inside a recursive parsing)
            if (!isTreeRefresh)
            {
                List<DialogueNode> gotos = Dialogue.GetGotoReferencesOnNode(dialogueNode);
                foreach (var nodeGoto in gotos)
                    RefreshTreeNode_Impl(GetTreeNode(nodeGoto));
            }

            //Style
            FontStyle style = FontStyle.Regular;

            if (dialogueNode is DialogueNodeRoot
            || dialogueNode is DialogueNodeChoice
            || dialogueNode is DialogueNodeGoto
            || dialogueNode is DialogueNodeBranch
            || dialogueNode is DialogueNodeReturn
            || dialogueNode is DialogueNodeComment)
            {
                style |= FontStyle.Italic;
            }

            if (Dialogue.IsNodeReferencedByGoto(dialogueNode))
            {
                style |= FontStyle.Bold;
            }

            Color color = GetTreeNodeColorContent(dialogueNode);

            treeNode.NodeFont = new Font(tree.Font, style);
            treeNode.ForeColor = color;
            treeNode.BackColor = tree.BackColor;

            //Text (I need to fill the line for the drag & drop to work)
            {
                string textID = GetTreeNodeTextID(dialogueNode);
                string textAttributes = GetTreeNodeTextAttributes(dialogueNode);
                string textActors = GetTreeNodeTextActors(dialogueNode);
                string textContent = GetTreeNodeTextContent(dialogueNode, treeNode.Level);

                treeNode.Text = textID + textAttributes + textActors + textContent;
            }
        }

        public Color GetTreeNodeColorContent(DialogueNode dialogueNode)
        {
            if (dialogueNode is DialogueNodeRoot)
            {
                return Color.Black;
            }
            else if (dialogueNode is DialogueNodeSentence)
            {
                if (EditorCore.Settings.UseActorColors)
                {
                    Actor speaker = ResourcesHandler.Project.GetActorFromID((dialogueNode as DialogueNodeSentence).SpeakerID);
                    if (speaker != null)
                        return Color.FromArgb(speaker.Color);
                }
            }
            else if (dialogueNode is DialogueNodeChoice)
            {
                return Color.FromArgb(220, 100, 0);   //Orange
            }
            else if (dialogueNode is DialogueNodeReply)
            {
                return Color.FromArgb(220, 0, 0);     //Red
            }
            else if (dialogueNode is DialogueNodeGoto)
            {
                if ((dialogueNode as DialogueNodeGoto).Goto != null)
                    return GetTreeNodeColorContent((dialogueNode as DialogueNodeGoto).Goto);
            }
            else if (dialogueNode is DialogueNodeBranch)
            {
                return Color.FromArgb(220, 100, 0);   //Orange
            }
            else if (dialogueNode is DialogueNodeReturn)
            {
                return Color.FromArgb(220, 100, 0);   //Orange
            }
            else if (dialogueNode is DialogueNodeComment)
            {
                return Color.FromArgb(125, 125, 125); //Grey
            }

            return Color.FromArgb(0, 0, 220);   //Blue
        }

        public string GetTreeNodeTextID(DialogueNode dialogueNode)
        {
            //if (dialogueNode is DialogueNodeRoot)
            //    return "";

            if (EditorCore.Settings.DisplayID)
                return String.Format("[{0}] ", dialogueNode.ID);

            return "";
        }

        public string GetTreeNodeTextAttributes(DialogueNode dialogueNode)
        {
            //if (dialogueNode is DialogueNodeRoot)
            //    return "";

            StringBuilder stringBuilder = new StringBuilder();

            if (EditorCore.Settings.DisplayConditions)
            {
                foreach (NodeCondition condition in dialogueNode.Conditions)
                    stringBuilder.AppendFormat("[{0}] ", condition.GetDisplayText());
            }

            if (EditorCore.Settings.DisplayActions)
            {
                foreach (NodeAction action in dialogueNode.Actions)
                    stringBuilder.AppendFormat("[{0}] ", action.GetDisplayText());
            }

            if (EditorCore.Settings.DisplayFlags)
            {
                foreach (NodeFlag flag in dialogueNode.Flags)
                    stringBuilder.AppendFormat("[{0}] ", flag.GetDisplayText());
            }

            return stringBuilder.ToString();
        }

        public string GetTreeNodeTextActors(DialogueNode dialogueNode)
        {
            if (dialogueNode is DialogueNodeSentence)
            {
                DialogueNodeSentence nodeSentence = dialogueNode as DialogueNodeSentence;
                StringBuilder stringBuilder = new StringBuilder();

                if (EditorCore.Settings.DisplaySpeaker)
                    stringBuilder.AppendFormat("[{0}] ", ResourcesHandler.Project.GetActorName(nodeSentence.SpeakerID));
                if (EditorCore.Settings.DisplayListener)
                    stringBuilder.AppendFormat("[{0}] ", ResourcesHandler.Project.GetActorName(nodeSentence.ListenerID));

                return stringBuilder.ToString();
            }
            return "";
        }

        private string GetTreeNodeTextContent(DialogueNode dialogueNode, int nodeLevel = 0)
        {
            if (dialogueNode is DialogueNodeRoot)
                return "Root";

            if (!EditorCore.Settings.DisplayText)
                return "";

            if (dialogueNode is DialogueNodeSentence)
            {
                DialogueNodeSentence nodeSentence = dialogueNode as DialogueNodeSentence;

                if (EditorHelper.CurrentLanguage != EditorCore.LanguageWorkstring)
                {
                    var entry = Dialogue.Translations.GetNodeEntry(dialogueNode, EditorHelper.CurrentLanguage);
                    if (entry != null)
                    {
                        if (EditorCore.Settings.UseConstants)
                            return EditorHelper.FormatTextEntry(entry.Text, EditorHelper.CurrentLanguage);
                        else
                            return entry.Text;
                    }
                }
                else
                {
                    if (EditorCore.Settings.UseConstants)
                        return EditorHelper.FormatTextEntry(nodeSentence.Sentence, EditorCore.LanguageWorkstring);
                    else
                        return nodeSentence.Sentence;
                }
            }
            else if (dialogueNode is DialogueNodeChoice)
            {
                DialogueNodeChoice nodeChoice = dialogueNode as DialogueNodeChoice;
                return String.Format("Choice > {0}", nodeChoice.Choice);
            }
            else if (dialogueNode is DialogueNodeReply)
            {
                DialogueNodeReply nodeReply = dialogueNode as DialogueNodeReply;
                if (EditorHelper.CurrentLanguage != EditorCore.LanguageWorkstring)
                {
                    var entry = Dialogue.Translations.GetNodeEntry(dialogueNode, EditorHelper.CurrentLanguage);
                    if (entry != null)
                    {
                        if (EditorCore.Settings.UseConstants)
                            return EditorHelper.FormatTextEntry(entry.Text, EditorHelper.CurrentLanguage);
                        else
                            return entry.Text;
                    }
                }
                else
                {
                    if (EditorCore.Settings.UseConstants)
                        return EditorHelper.FormatTextEntry(nodeReply.Reply, EditorCore.LanguageWorkstring);
                    else
                        return nodeReply.Reply;
                }
            }
            else if (dialogueNode is DialogueNodeGoto)
            {
                DialogueNodeGoto nodeGoto = dialogueNode as DialogueNodeGoto;
                if (nodeGoto.Goto == null)
                    return "Goto > Undefined";
                else if (EditorCore.Settings.DisplayID)
                    return String.Format("Goto > [{0}] {1}", nodeGoto.Goto.ID, GetTreeNodeTextContent(nodeGoto.Goto));
                else
                    return String.Format("Goto > {0}", GetTreeNodeTextContent(nodeGoto.Goto));
            }
            else if (dialogueNode is DialogueNodeBranch)
            {
                DialogueNodeBranch nodeBranch = dialogueNode as DialogueNodeBranch;
                return String.Format("Branch > {0}", nodeBranch.Workstring);
            }
            else if (dialogueNode is DialogueNodeReturn)
            {
                return String.Format("Return");
            }
            else if (dialogueNode is DialogueNodeComment)
            {
                if (EditorCore.Settings.DisplayComments)
                {
                    DialogueNodeComment nodeComment = dialogueNode as DialogueNodeComment;
                    List<string> lines = GetWrappedDisplayNoteLines(nodeComment.Comment, nodeLevel);
                    if (lines.Count > 0)
                        return lines[0];
                }
                return "Comment";
            }

            return "";
        }

        private string FormatDisplayNote(string note, int maxLength = 200)
        {
            if (String.IsNullOrWhiteSpace(note))
                return "";

            string formatted = note.Replace("\r\n", " | ").Replace("\n", " | ").Trim();
            if (formatted.Length > maxLength)
                formatted = formatted.Substring(0, maxLength) + "...";
            return formatted;
        }

        private Color GetDisplayRowColor(EDisplayRowKind rowKind)
        {
            if (rowKind == EDisplayRowKind.Context)
                return Color.SlateBlue;
            if (rowKind == EDisplayRowKind.Comment)
                return Color.Gray;
            return Color.Gray;
        }

        private int EstimateDisplayWrapLength(int nodeLevel)
        {
            int availableWidth = Math.Max(120, tree.ClientSize.Width - 80 - (nodeLevel * tree.Indent));
            int avgCharWidth = Math.Max(6, TextRenderer.MeasureText("W", tree.Font).Width - 2);
            return Math.Max(20, availableWidth / avgCharWidth);
        }

        private List<string> GetWrappedDisplayNoteLines(string note, int nodeLevel)
        {
            return WrapDisplayNote(note, EstimateDisplayWrapLength(nodeLevel));
        }

        private List<string> WrapDisplayNote(string note, int maxCharsPerLine)
        {
            List<string> wrappedLines = new List<string>();

            if (String.IsNullOrWhiteSpace(note))
                return wrappedLines;

            string[] blocks = note.Replace("\r\n", "\n").Split('\n');
            foreach (string block in blocks)
            {
                string text = block.Trim();
                if (String.IsNullOrEmpty(text))
                    continue;

                StringBuilder lineBuilder = new StringBuilder();
                string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string word in words)
                {
                    if (lineBuilder.Length == 0)
                    {
                        lineBuilder.Append(word);
                        continue;
                    }

                    if (lineBuilder.Length + 1 + word.Length <= maxCharsPerLine)
                    {
                        lineBuilder.Append(' ');
                        lineBuilder.Append(word);
                    }
                    else
                    {
                        wrappedLines.Add(lineBuilder.ToString());
                        lineBuilder.Clear();
                        lineBuilder.Append(word);
                    }
                }

                if (lineBuilder.Length > 0)
                    wrappedLines.Add(lineBuilder.ToString());
            }

            return wrappedLines;
        }

        private void AppendDisplayRows(ref List<Tuple<EDisplayRowKind, string>> rows, EDisplayRowKind rowKind, string text, int nodeLevel)
        {
            if (String.IsNullOrWhiteSpace(text))
                return;

            foreach (string line in GetWrappedDisplayNoteLines(text, nodeLevel))
            {
                rows.Add(Tuple.Create(rowKind, line));
            }
        }

        private List<Tuple<EDisplayRowKind, string>> GetDisplayRowsForNode(DialogueNode dialogueNode, int nodeLevel)
        {
            List<Tuple<EDisplayRowKind, string>> rows = new List<Tuple<EDisplayRowKind, string>>();
            if (dialogueNode == null)
                return rows;

            string context = "";
            string comment = "";

            if (dialogueNode is DialogueNodeSentence)
            {
                DialogueNodeSentence sentence = dialogueNode as DialogueNodeSentence;
                context = sentence.Context;
                comment = sentence.Comment;
            }
            else if (dialogueNode is DialogueNodeRoot)
            {
                context = Dialogue.Context;
                comment = Dialogue.Comment;
            }
            else if (dialogueNode is DialogueNodeComment)
            {
                comment = (dialogueNode as DialogueNodeComment).Comment;

                if (EditorCore.Settings.DisplayComments)
                {
                    List<string> lines = GetWrappedDisplayNoteLines(comment, nodeLevel);
                    int startIndex = 0;
                    if (EditorCore.Settings.DisplayText && lines.Count > 0)
                        startIndex = 1; // First line is displayed in the main node row

                    for (int i = startIndex; i < lines.Count; ++i)
                        rows.Add(Tuple.Create(EDisplayRowKind.Comment, lines[i]));
                }

                return rows;
            }

            if (EditorCore.Settings.DisplayContext)
                AppendDisplayRows(ref rows, EDisplayRowKind.Context, context, nodeLevel);

            if (EditorCore.Settings.DisplayComments)
                AppendDisplayRows(ref rows, EDisplayRowKind.Comment, comment, nodeLevel);

            return rows;
        }

        private void StripDisplayRows(TreeNodeCollection nodes)
        {
            for (int i = nodes.Count - 1; i >= 0; --i)
            {
                TreeNode node = nodes[i];
                if (IsDisplayTreeNode(node))
                {
                    nodes.RemoveAt(i);
                }
                else
                {
                    StripDisplayRows(node.Nodes);
                }
            }
        }

        private void RebuildDisplayRows(TreeNodeCollection nodes)
        {
            for (int i = 0; i < nodes.Count; ++i)
            {
                TreeNode node = nodes[i];
                if (IsDisplayTreeNode(node))
                    continue;

                DialogueNode dialogueNode = GetDialogueNode(node);
                List<Tuple<EDisplayRowKind, string>> rows = GetDisplayRowsForNode(dialogueNode, node.Level);
                if (rows.Count > 0)
                {
                    bool insertAfterNode = dialogueNode is DialogueNodeComment;
                    for (int rowIndex = 0; rowIndex < rows.Count; ++rowIndex)
                    {
                        int insertIndex = insertAfterNode
                            ? (i + 1 + rowIndex)
                            : (i + rowIndex);
                        TreeNode displayNode = nodes.Insert(insertIndex, GetDisplayNodeKey(dialogueNode.ID), rows[rowIndex].Item2);
                        displayNode.Tag = new NodeWrap(dialogueNode, rows[rowIndex].Item1, node);
                        displayNode.ContextMenuStrip = contextMenu;
                        displayNode.BackColor = tree.BackColor;
                        displayNode.ForeColor = GetDisplayRowColor(rows[rowIndex].Item1);
                        displayNode.ImageIndex = -1;
                        displayNode.SelectedImageIndex = -1;
                        displayNode.StateImageIndex = -1;
                        displayNode.ImageKey = String.Empty;
                        displayNode.SelectedImageKey = String.Empty;
                        displayNode.StateImageKey = String.Empty;
                    }

                    i += rows.Count;
                }

                RebuildDisplayRows(node.Nodes);
            }
        }

        private void RefreshDisplayRows(TreeNode preferredSelectedNode = null)
        {
            TreeNode selectedNode = GetRealTreeNode(preferredSelectedNode ?? tree.SelectedNode);

            StripDisplayRows(tree.Nodes);
            displayNodeKeyCounter = 0;
            RebuildDisplayRows(tree.Nodes);

            if (selectedNode != null)
                SelectTreeNode(selectedNode);
        }

        private void OnTreeViewDrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            e.DrawDefault = false;

            var node = e.Node;
            var dialogueNode = GetDialogueNode(node);
            bool isDisplayRow = IsDisplayTreeNode(node);

            Font nodeFont = node.NodeFont;
            if (nodeFont == null)
                nodeFont = tree.Font;

            Rectangle bounds = node.Bounds;
            bounds.Width = tree.Width;
            Rectangle paintBounds = bounds;
            if (isDisplayRow)
            {
                paintBounds.X = 0;
                paintBounds.Width = tree.ClientSize.Width;
            }

            if ((e.State & TreeNodeStates.Selected) != 0)
            {
                using (var brush = new SolidBrush(ThemeManager.Current.SelectionBackground))
                {
                    e.Graphics.FillRectangle(brush, paintBounds);
                }
            }
            else
            {
                using (var brush = new SolidBrush(node.BackColor))
                {
                    e.Graphics.FillRectangle(brush, paintBounds);
                }
            }

            if (isDisplayRow)
            {
                var wrap = node.Tag as NodeWrap;
                FontStyle style = FontStyle.Italic;
                if ((nodeFont.Style & FontStyle.Bold) == FontStyle.Bold)
                    style |= FontStyle.Bold;

                float displayFontSize = Math.Max(6.0f, nodeFont.Size - 1.0f);
                using (var displayFont = new Font(nodeFont.FontFamily, displayFontSize, style, nodeFont.Unit))
                {
                    int x = Math.Max(0, bounds.Location.X - GetDisplayRowIconOffset());
                    int y = bounds.Location.Y + (IsDisplayRowInsertedBeforeOwner(node) ? 3 : 1);
                    Point location = new Point(x, y);
                    DrawText(ref location, node.Text, e.Graphics, displayFont, GetDisplayRowColor(wrap.DisplayRowKind));
                }
                return;
            }

            // Retrieve texts
            string textID = GetTreeNodeTextID(dialogueNode);
            string textAttributes = GetTreeNodeTextAttributes(dialogueNode);
            string textActors = GetTreeNodeTextActors(dialogueNode);
            string textContent = GetTreeNodeTextContent(dialogueNode, node.Level);

            int mainYOffset = 1;
            if (HasAttachedDisplayRowsAbove(node))
                mainYOffset = 0;
            else if (HasAttachedDisplayRowsBelow(node))
                mainYOffset = 3;

            Point locationMain = new Point(bounds.Location.X, bounds.Location.Y + mainYOffset);
            DrawText(ref locationMain, textID, e.Graphics, nodeFont, Color.Black);
            DrawText(ref locationMain, textAttributes, e.Graphics, nodeFont, Color.MediumOrchid);
            DrawText(ref locationMain, textActors, e.Graphics, nodeFont, Color.DimGray);
            DrawText(ref locationMain, textContent, e.Graphics, nodeFont, GetTreeNodeColorContent(dialogueNode));
        }

        private void DrawText(ref Point location, string text, Graphics g, Font font, Color color)
        {
            if (text != null && text != String.Empty)
            {
                int width = TextRenderer.MeasureText(g, text, font).Width - 6;  //Hack to adjust positions offset
                TextRenderer.DrawText(g, text, font, location, color);          //This rendering seems better than g.DrawString
                //int width = g.MeasureString(text, font).ToSize().Width;
                //SolidBrush brush = new SolidBrush(color);
                //g.DrawString(text, font, brush, location);
                location.X += width;
            }
        }

        public void RefreshFont()
        {
            if (EditorCore.Settings.DialogueTreeViewFont == null)
            {
                EditorCore.Settings.DialogueTreeViewFont = tree.Font;
            }
            else if (tree.Font != EditorCore.Settings.DialogueTreeViewFont)
            {
                tree.Font = EditorCore.Settings.DialogueTreeViewFont;
            }

            labelFont.Text = String.Format("{0} {1}", tree.Font.Name, tree.Font.Size);
            UpdateTreeItemHeight();
        }

        private void UpdateTreeItemHeight()
        {
            int lineHeight = Math.Max(16, TextRenderer.MeasureText("Ag", tree.Font).Height);
            tree.ItemHeight = Math.Max(16, lineHeight);
        }

        private void InitializeWpfGapControls()
        {
            if (!useWpfDialogueTree)
                return;

            const int widthOffset = 90;
            int baselineClientWidth = 775;
            if (ClientSize.Width < baselineClientWidth + widthOffset)
                ClientSize = new Size(baselineClientWidth + widthOffset, ClientSize.Height);

            label2.Location = new Point(625 + widthOffset, 4);
            comboBoxLanguages.Location = new Point(633 + widthOffset, 12);
            comboBoxLanguages.Size = new Size(130, 21);

            labelWpfGapDefault = new Label();
            labelWpfGapDefault.AutoSize = true;
            labelWpfGapDefault.Location = new Point(630, 10);
            labelWpfGapDefault.Name = "labelWpfGapDefault";
            labelWpfGapDefault.Size = new Size(16, 13);
            labelWpfGapDefault.Text = "G";
            labelWpfGapDefault.TextAlign = ContentAlignment.MiddleRight;

            labelWpfGapSameSpeaker = new Label();
            labelWpfGapSameSpeaker.AutoSize = true;
            labelWpfGapSameSpeaker.Location = new Point(630, 27);
            labelWpfGapSameSpeaker.Name = "labelWpfGapSameSpeaker";
            labelWpfGapSameSpeaker.Size = new Size(14, 13);
            labelWpfGapSameSpeaker.Text = "S";
            labelWpfGapSameSpeaker.TextAlign = ContentAlignment.MiddleRight;

            numericWpfGapDefault = new NumericUpDown();
            numericWpfGapDefault.DecimalPlaces = 1;
            numericWpfGapDefault.Increment = 0.1M;
            numericWpfGapDefault.Minimum = 0.0M;
            numericWpfGapDefault.Maximum = 12.0M;
            numericWpfGapDefault.Location = new Point(648, 7);
            numericWpfGapDefault.Name = "numericWpfGapDefault";
            numericWpfGapDefault.Size = new Size(49, 20);
            numericWpfGapDefault.TabIndex = 26;
            numericWpfGapDefault.ValueChanged += new EventHandler(this.OnWpfGapSettingsChanged);

            numericWpfGapSameSpeaker = new NumericUpDown();
            numericWpfGapSameSpeaker.DecimalPlaces = 1;
            numericWpfGapSameSpeaker.Increment = 0.1M;
            numericWpfGapSameSpeaker.Minimum = 0.0M;
            numericWpfGapSameSpeaker.Maximum = 12.0M;
            numericWpfGapSameSpeaker.Location = new Point(648, 24);
            numericWpfGapSameSpeaker.Name = "numericWpfGapSameSpeaker";
            numericWpfGapSameSpeaker.Size = new Size(49, 20);
            numericWpfGapSameSpeaker.TabIndex = 27;
            numericWpfGapSameSpeaker.ValueChanged += new EventHandler(this.OnWpfGapSettingsChanged);

            toolTipWpfGap = new ToolTip(components);
            toolTipWpfGap.SetToolTip(labelWpfGapDefault, "WPF tree: regular line gap");
            toolTipWpfGap.SetToolTip(numericWpfGapDefault, "WPF tree: regular line gap");
            toolTipWpfGap.SetToolTip(labelWpfGapSameSpeaker, "WPF tree: same speaker line gap");
            toolTipWpfGap.SetToolTip(numericWpfGapSameSpeaker, "WPF tree: same speaker line gap");

            Controls.Add(labelWpfGapDefault);
            Controls.Add(labelWpfGapSameSpeaker);
            Controls.Add(numericWpfGapDefault);
            Controls.Add(numericWpfGapSameSpeaker);

            labelWpfGapDefault.BringToFront();
            labelWpfGapSameSpeaker.BringToFront();
            numericWpfGapDefault.BringToFront();
            numericWpfGapSameSpeaker.BringToFront();
        }

        private void ResyncWpfGapControls()
        {
            if (numericWpfGapDefault == null || numericWpfGapSameSpeaker == null || labelWpfGapDefault == null || labelWpfGapSameSpeaker == null)
                return;

            bool enabled = useWpfDialogueTree;
            labelWpfGapDefault.Visible = enabled;
            labelWpfGapSameSpeaker.Visible = enabled;
            numericWpfGapDefault.Visible = enabled;
            numericWpfGapSameSpeaker.Visible = enabled;

            if (EditorCore.Settings == null)
                return;

            double gapDefault = Math.Max(0.0, EditorCore.Settings.WpfTreeGapDefault);
            double gapSameSpeaker = Math.Max(0.0, EditorCore.Settings.WpfTreeGapSameSpeaker);

            numericWpfGapDefault.Value = (decimal)Math.Min(12.0, gapDefault);
            numericWpfGapSameSpeaker.Value = (decimal)Math.Min(12.0, gapSameSpeaker);
        }

        private void OnWpfGapSettingsChanged(object sender, EventArgs e)
        {
            if (lockCheckDisplayEvents || !useWpfDialogueTree || EditorCore.Settings == null)
                return;

            EditorCore.Settings.WpfTreeGapDefault = (double)numericWpfGapDefault.Value;
            EditorCore.Settings.WpfTreeGapSameSpeaker = (double)numericWpfGapSameSpeaker.Value;
            RefreshWpfTreeHost();
        }

        public void Highlight(TreeNode node)
        {
            Highlight(node, Color.DarkGray);
        }

        public void Highlight(TreeNode node, Color color)
        {
            if (node != null && node.BackColor != color)   //Avoid tree redraw
                node.BackColor = color;
        }

        public void Highlight(DialogueNode node)
        {
            Highlight(GetTreeNode(node));
        }

        public void Highlight(DialogueNode node, Color color)
        {
            Highlight(GetTreeNode(node), color);
        }
        
        public void Highlight(List<DialogueNode> nodes, Color color)
        {
            foreach (DialogueNode node in nodes)
            {
                Highlight(node, color);
            }
        }

        public void UnHighlight(TreeNode node)
        {
            if (node != null && node.BackColor != tree.BackColor)   //Avoid tree redraw
                node.BackColor = tree.BackColor;
        }

        public void UnHighlightAll()
        {
            UnHighlightAll(tree.Nodes);
        }

        public void UnHighlightAll(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                UnHighlight(node);
                UnHighlightAll(node.Nodes);
            }
        }

        public void Clear()
        {
            tree.Nodes.Clear();
        }

        protected string GetNodeKey(int nodeID)
        {
            return "Node_" + nodeID.ToString();
        }

        protected string GetDisplayNodeKey(int nodeID)
        {
            ++displayNodeKeyCounter;
            return String.Format("Display_{0}_{1}", nodeID, displayNodeKeyCounter);
        }

        protected bool IsDisplayTreeNode(TreeNode node)
        {
            if (node == null || node.Tag == null)
                return false;

            NodeWrap wrap = node.Tag as NodeWrap;
            return wrap != null && wrap.IsDisplayRow;
        }

        protected TreeNode GetDisplayRowOwner(TreeNode node)
        {
            if (!IsDisplayTreeNode(node))
                return null;

            NodeWrap wrap = node.Tag as NodeWrap;
            return wrap?.OwnerTreeNode;
        }

        protected bool IsDisplayRowInsertedBeforeOwner(TreeNode node)
        {
            TreeNode owner = GetDisplayRowOwner(node);
            if (owner == null)
                return false;

            return node.Parent == owner.Parent && node.Index < owner.Index;
        }

        protected bool HasAttachedDisplayRowsAbove(TreeNode node)
        {
            TreeNode current = node?.PrevNode;
            while (current != null && IsDisplayTreeNode(current))
            {
                if (GetDisplayRowOwner(current) == node)
                    return true;
                current = current.PrevNode;
            }

            return false;
        }

        protected bool HasAttachedDisplayRowsBelow(TreeNode node)
        {
            TreeNode current = node?.NextNode;
            while (current != null && IsDisplayTreeNode(current))
            {
                if (GetDisplayRowOwner(current) == node)
                    return true;
                current = current.NextNode;
            }

            return false;
        }

        protected int GetDisplayRowIconOffset()
        {
            if (tree.ImageList == null)
                return 0;

            return Math.Max(0, tree.ImageList.ImageSize.Width + 3);
        }

        protected TreeNode GetRealTreeNode(TreeNode node)
        {
            if (!IsDisplayTreeNode(node))
                return node;

            NodeWrap wrap = node.Tag as NodeWrap;
            return wrap?.OwnerTreeNode;
        }

        protected TreeNode GetPreviousRealSibling(TreeNode node)
        {
            TreeNode current = node?.PrevNode;
            while (current != null && IsDisplayTreeNode(current))
                current = current.PrevNode;
            return current;
        }

        protected TreeNode GetNextRealSibling(TreeNode node)
        {
            TreeNode current = node?.NextNode;
            while (current != null && IsDisplayTreeNode(current))
                current = current.NextNode;
            return current;
        }

        protected TreeNode GetRootNode()
        {
            foreach (TreeNode node in tree.Nodes)
            {
                if (!IsDisplayTreeNode(node) && node.Tag != null && ((NodeWrap)node.Tag).DialogueNode is DialogueNodeRoot)
                    return node;
            }
            return null;
        }

        public TreeNode GetTreeNode(DialogueNode dialogueNode)
        {
            if (dialogueNode != null)
            {
                return GetTreeNode(dialogueNode.ID);
            }
            return null;
        }

        public TreeNode GetTreeNode(int dialogueID)
        {
            if (dialogueID != -1 && tree.Nodes.Count > 0)
            {
                TreeNode[] result = tree.Nodes.Find(GetNodeKey(dialogueID), true);
                if (result.Count() > 0)
                    return result.First();
            }
            return null;
        }

        public DialogueNode GetDialogueNode(TreeNode node)
        {
            node = GetRealTreeNode(node);
            if (node != null && node.Tag != null)
            {
                return ((NodeWrap)node.Tag).DialogueNode;
            }
            return null;
        }

        public DialogueNode GetSelectedDialogueNode()
        {
            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);
            if (selectedNode != null && selectedNode.Tag != null)
            {
                return ((NodeWrap)selectedNode.Tag).DialogueNode;
            }
            return null;
        }

        protected bool IsTreeNodeRoot(TreeNode node)
        {
            return GetDialogueNode(node) is DialogueNodeRoot;
        }

        protected bool IsTreeNodeSentence(TreeNode node)
        {
            return GetDialogueNode(node) is DialogueNodeSentence;
        }

        protected bool IsTreeNodeChoice(TreeNode node)
        {
            return GetDialogueNode(node) is DialogueNodeChoice;
        }

        protected bool IsTreeNodeReply(TreeNode node)
        {
            return GetDialogueNode(node) is DialogueNodeReply;
        }

        protected bool IsTreeNodeGoto(TreeNode node)
        {
            return GetDialogueNode(node) is DialogueNodeGoto;
        }

        protected bool IsTreeNodeBranch(TreeNode node)
        {
            return GetDialogueNode(node) is DialogueNodeBranch;
        }

        protected bool IsTreeNodeComment(TreeNode node)
        {
            return GetDialogueNode(node) is DialogueNodeComment;
        }

        private void ResyncDisplayOptions()
        {
            lockCheckDisplayEvents = true;

            if (ResourcesHandler.Project != null)
            {
                var listLanguages = new List<Language>() { EditorCore.LanguageWorkstring };
                listLanguages.AddRange(ResourcesHandler.Project.ListLanguages);

                if (EditorHelper.CurrentLanguage == null)
                    EditorHelper.CurrentLanguage = EditorCore.LanguageWorkstring;

                comboBoxLanguages.DataSource = new BindingSource(listLanguages, null);
                comboBoxLanguages.DisplayMember = "Name";
                comboBoxLanguages.SelectedItem = EditorHelper.CurrentLanguage;
            }

            RefreshFont();

            checkBoxDisplaySpeaker.Checked = EditorCore.Settings.DisplaySpeaker;
            checkBoxDisplayListener.Checked = EditorCore.Settings.DisplayListener;
            checkBoxDisplayID.Checked = EditorCore.Settings.DisplayID;
            checkBoxDisplayConditions.Checked = EditorCore.Settings.DisplayConditions;
            checkBoxDisplayActions.Checked = EditorCore.Settings.DisplayActions;
            checkBoxDisplayFlags.Checked = EditorCore.Settings.DisplayFlags;
            checkBoxDisplayText.Checked = EditorCore.Settings.DisplayText;
            checkBoxDisplayContext.Checked = EditorCore.Settings.DisplayContext;
            checkBoxDisplayComments.Checked = EditorCore.Settings.DisplayComments;
            checkBoxUseActorColors.Checked = EditorCore.Settings.UseActorColors;
            checkBoxUseConstants.Checked = EditorCore.Settings.UseConstants;
            ResyncWpfGapControls();

            lockCheckDisplayEvents = false;
        }

        private void SaveState()
        {
            //Remove all States following the current State
            if (indexState < previousStates.Count - 1)
            {
                previousStates.RemoveRange(indexState + 1, previousStates.Count - indexState - 1);
            }

            //Append the new State
            previousStates.Add(new State()
            {
                Content = ExporterJson.SaveDialogueToString(ResourcesHandler.Project, Dialogue),
                NodeID = ((tree.SelectedNode != null) ? GetSelectedDialogueNode().ID : DialogueNode.ID_NULL)
            } );

            indexState = previousStates.Count - 1;

            //Shrink list if needed by removing older States
            int maxStates = EditorCore.Settings.MaxUndoLevels + 1;  //I add +1 because the first state stored is the original file
            if (previousStates.Count > maxStates)
            {
                previousStates.RemoveRange(0, previousStates.Count - maxStates);
                indexState = maxStates - 1;
            }
        }

        private void ResetStates()
        {
            previousStates.Clear();
            indexState = 0;
        }

        public void UndoState()
        {
            if (previousStates.Count >= 2 && indexState > 0)
            {
                //TODO: should be cleaner to clear properties and tree before reloading, but I need to handle the flicker

                State currentState = previousStates.ElementAt(indexState);
                State previousState = previousStates.ElementAt(indexState - 1);
                indexState -= 1;

                ResourcesHandler.ReloadDialogueFromString(Dialogue, previousState.Content);

                ResyncDocument();
                RefreshTitle();
                SelectNode(currentState.NodeID);
                if (tree.SelectedNode == null)
                    SelectRootNode();
            }
        }

        public void RedoState()
        {
            if (previousStates.Count >= 2 && indexState < previousStates.Count - 1)
            {
                //TODO: should be cleaner to clear properties and tree before reloading, but I need to handle the flicker

                //State currentState = previousStates.ElementAt(indexState);
                State nextState = previousStates.ElementAt(indexState + 1);
                indexState += 1;

                ResourcesHandler.ReloadDialogueFromString(Dialogue, nextState.Content);

                ResyncDocument();
                RefreshTitle();
                SelectNode(nextState.NodeID);
                if (tree.SelectedNode == null)
                    SelectRootNode();
            }
        }

        //--------------------------------------------------------------------------------------------------------------
        // Events

        public bool ProcessCmdKey_Impl(Keys keyData)
        {
            bool treeFocused = IsDialogueTreeFocused();

            if (treeFocused && (keyData == (Keys.F2) || keyData == (Keys.Enter)))
            {
                // Move the focus from the document TreeView to the Properties.
                if (tree.SelectedNode != null)
                {
                    if (IsTreeNodeSentence(tree.SelectedNode)
                    || IsTreeNodeChoice(tree.SelectedNode)
                    || IsTreeNodeReply(tree.SelectedNode)
                    || IsTreeNodeBranch(tree.SelectedNode)
                    || IsTreeNodeComment(tree.SelectedNode))
                    {
                        if (EditorCore.Properties != null)
                            EditorCore.Properties.ForceFocus();

                        return true;
                    }
                }
            }
            else if (!treeFocused && keyData == (Keys.F2))
            {
                // Move the focus back from the Properties to the document TreeView.
                FocusDialogueTreeControl();
                return true;
            }
            else if (!treeFocused && (keyData == (Keys.Enter) || keyData == (Keys.Shift | Keys.Enter)))
            {
                // Validate edited workstring, then move the focus back from the Properties to the document TreeView.
                if (tree.SelectedNode != null && EditorCore.Properties != null && EditorCore.Properties.IsEditingWorkstring())
                {
                    EditorCore.Properties.ValidateEditedWorkstring();

                    FocusDialogueTreeControl();
                    return true;
                }
            }
            else if (keyData == (Keys.Control | Keys.Enter) || keyData == (Keys.Control | Keys.Shift | Keys.Enter))
            {
                if (tree.SelectedNode != null && IsTreeNodeSentence(tree.SelectedNode))
                {
                    DialogueNodeSentence newSentence = new DialogueNodeSentence();
                    Dialogue.AddNode(newSentence);

                    var newTreeNode = AddNodeSentence(tree.SelectedNode, newSentence, false);
                    var prevSentence = GetSelectedDialogueNode() as DialogueNodeSentence;

                    if (keyData == (Keys.Control | Keys.Enter))
                    {
                        newSentence.SpeakerID = prevSentence.SpeakerID;
                        newSentence.ListenerID = prevSentence.ListenerID;
                    }
                    else
                    {
                        newSentence.SpeakerID = prevSentence.ListenerID;
                        newSentence.ListenerID = prevSentence.SpeakerID;
                    }

                    RefreshTreeNode(newTreeNode);
                    SelectTreeNode(newTreeNode);

                    if (EditorCore.Properties != null)
                        EditorCore.Properties.ForceFocus();

                    SetDirty();
                    return true;
                }
            }

            return false;   //let the caller forward the message to the base class
        }

        protected virtual void OnKeyDown(object sender, KeyEventArgs e)
        {
            var dialogueNode = GetSelectedDialogueNode();
            if (dialogueNode == null)
                return;

            if (e.Control && (e.KeyCode == Keys.C || e.KeyCode == Keys.X))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                if (dialogueNode is DialogueNodeRoot)
                {
                    var tempDialogue = new Dialogue(Dialogue);
                    tempDialogue.RootNode = dialogueNode.Clone() as DialogueNodeRoot;
                    EditorHelper.Clipboard = tempDialogue;
                    EditorHelper.ClipboardInfos = new ClipboardInfos() { sourceDialogue = Dialogue.GetName() };
                }
                else
                {
                    EditorHelper.Clipboard = dialogueNode.Clone();
                    EditorHelper.ClipboardInfos = new ClipboardInfos() { sourceDialogue = Dialogue.GetName(), sourceNodeID = dialogueNode.ID };
                }

                if (e.KeyCode == Keys.X)
                {
                    if (dialogueNode is DialogueNodeRoot)
                    {
                        RemoveAllNodes();
                    }
                    else
                    {
                        RemoveNode(dialogueNode, tree.SelectedNode);
                    }
                }
            }
            else if (e.Control && e.KeyCode == Keys.V)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                bool asBranch = false;

                //Special paste
                if (e.Shift)
                {
                    if (dialogueNode is DialogueNodeGoto)
                    {
                        if (EditorHelper.Clipboard is DialogueNode && EditorHelper.ClipboardInfos != null && EditorHelper.ClipboardInfos.sourceDialogue == Dialogue.GetName())
                        {
                            DialogueNode nodeTarget = Dialogue.GetNodeByID(EditorHelper.ClipboardInfos.sourceNodeID);
                            if (nodeTarget != null)
                            {
                                (dialogueNode as DialogueNodeGoto).Goto = nodeTarget;
                                ResyncSelectedNode();

                                SetDirty();
                            }
                        }

                        return;
                    }
                    else if (dialogueNode is DialogueNodeBranch)
                    {
                        asBranch = true;
                    }
                    else
                    {
                        //Ignore the paste if trying to special-copy an undefined case
                        return;
                    }
                }

                //if (EditorCore.Clipboard is DialogueNodeRoot)
                if (EditorHelper.Clipboard is Dialogue)
                {
                    //var newRoot = (EditorCore.Clipboard as DialogueNodeRoot).Clone() as DialogueNodeRoot;

                    var tempDialogue = EditorHelper.Clipboard as Dialogue;
                    var newRoot = tempDialogue.RootNode.Clone() as DialogueNodeRoot;

                    Package previousPackage = Dialogue.Package;

                    //Only Copy parameters if we copy a root on another root
                    if (dialogueNode is DialogueNodeRoot)
                    {
                        Dialogue.Copy(tempDialogue);
                    }

                    //Insert from the first child, and discard the new root
                    var firsNode = newRoot.Next;
                    Dialogue.AddNode(firsNode);

                    TreeNode newTreeNode = null;
                    if (firsNode is DialogueNodeSentence)
                        newTreeNode = AddNodeSentence(tree.SelectedNode, firsNode as DialogueNodeSentence, asBranch);
                    else if (firsNode is DialogueNodeChoice)
                        newTreeNode = AddNodeChoice(tree.SelectedNode, firsNode as DialogueNodeChoice, asBranch);
                    else if (firsNode is DialogueNodeReply)
                        newTreeNode = AddNodeReply(tree.SelectedNode, firsNode as DialogueNodeReply);
                    else if (firsNode is DialogueNodeGoto)
                        newTreeNode = AddNodeGoto(tree.SelectedNode, firsNode as DialogueNodeGoto, asBranch);
                    else if (firsNode is DialogueNodeBranch)
                        newTreeNode = AddNodeBranch(tree.SelectedNode, firsNode as DialogueNodeBranch, asBranch);
                    else if (firsNode is DialogueNodeReturn)
                        newTreeNode = AddNodeReturn(tree.SelectedNode, firsNode as DialogueNodeReturn, asBranch);
                    else if (firsNode is DialogueNodeComment)
                        newTreeNode = AddNodeComment(tree.SelectedNode, firsNode as DialogueNodeComment, asBranch);

                    if (dialogueNode is DialogueNodeRoot)
                    {
                        ResyncSelectedNode();   //root node is already selected, we just need a resync

                        if (EditorCore.ProjectExplorer != null)
                            EditorCore.ProjectExplorer.ResyncFile(Dialogue, previousPackage, true);
                    }
                    else
                    {
                        SelectTreeNode(newTreeNode);
                    }

                    SetDirty();
                }
                else if (EditorHelper.Clipboard is DialogueNodeSentence)
                {
                    var newNode = (EditorHelper.Clipboard as DialogueNodeSentence).Clone() as DialogueNodeSentence;
                    Dialogue.AddNode(newNode);

                    var newTreeNode = AddNodeSentence(tree.SelectedNode, newNode, asBranch);
                    SelectTreeNode(newTreeNode);

                    SetDirty();
                }
                else if (EditorHelper.Clipboard is DialogueNodeChoice)
                {
                    var newNode = (EditorHelper.Clipboard as DialogueNodeChoice).Clone() as DialogueNodeChoice;
                    Dialogue.AddNode(newNode);

                    var newTreeNode = AddNodeChoice(tree.SelectedNode, newNode, asBranch);
                    SelectTreeNode(newTreeNode);

                    SetDirty();
                }
                else if (EditorHelper.Clipboard is DialogueNodeReply)
                {
                    if (IsTreeNodeChoice(tree.SelectedNode))
                    {
                        var newNode = (EditorHelper.Clipboard as DialogueNodeReply).Clone() as DialogueNodeReply;
                        Dialogue.AddNode(newNode);

                        var newTreeNode = AddNodeReply(tree.SelectedNode, newNode);
                        SelectTreeNode(newTreeNode);

                        SetDirty();
                    }
                }
                else if (EditorHelper.Clipboard is DialogueNodeGoto)
                {
                    var newNode = (EditorHelper.Clipboard as DialogueNodeGoto).Clone() as DialogueNodeGoto;
                    Dialogue.AddNode(newNode);

                    var newTreeNode = AddNodeGoto(tree.SelectedNode, newNode, asBranch);
                    SelectTreeNode(newTreeNode);

                    SetDirty();
                }
                else if (EditorHelper.Clipboard is DialogueNodeBranch)
                {
                    var newNode = (EditorHelper.Clipboard as DialogueNodeBranch).Clone() as DialogueNodeBranch;
                    Dialogue.AddNode(newNode);

                    var newTreeNode = AddNodeBranch(tree.SelectedNode, newNode, asBranch);
                    SelectTreeNode(newTreeNode);

                    SetDirty();
                }
                else if (EditorHelper.Clipboard is DialogueNodeReturn)
                {
                    var newNode = (EditorHelper.Clipboard as DialogueNodeReturn).Clone() as DialogueNodeReturn;
                    Dialogue.AddNode(newNode);

                    var newTreeNode = AddNodeReturn(tree.SelectedNode, newNode, asBranch);
                    SelectTreeNode(newTreeNode);

                    SetDirty();
                }
                else if (EditorHelper.Clipboard is DialogueNodeComment)
                {
                    var newNode = (EditorHelper.Clipboard as DialogueNodeComment).Clone() as DialogueNodeComment;
                    Dialogue.AddNode(newNode);

                    var newTreeNode = AddNodeComment(tree.SelectedNode, newNode, asBranch);
                    SelectTreeNode(newTreeNode);

                    SetDirty();
                }
            }
            else if (e.Control && e.KeyCode == Keys.Z)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                UndoState();
            }
            else if (e.Control && e.KeyCode == Keys.Y)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                RedoState();
            }
            else if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                RemoveNode(dialogueNode, tree.SelectedNode);
            }
        }

        private void OnNodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            TreeNode clickedNode = ResolveTreeNodeFromPoint(e.Location) ?? GetRealTreeNode(e.Node);
            if (clickedNode != null)
                SelectTreeNode(clickedNode);     //Will trigger OnNodeSelect
        }

        private void OnTreeMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
                return;

            TreeNode clickedNode = ResolveTreeNodeFromPoint(e.Location);
            if (clickedNode != null)
                SelectTreeNode(clickedNode);
        }

        private TreeNode ResolveTreeNodeFromPoint(Point location)
        {
            TreeNode hitNode = tree.GetNodeAt(location);
            if (hitNode == null && location.Y >= 0 && location.Y < tree.ClientSize.Height)
            {
                // Keep the same row selection even when clicking outside the label bounds.
                int probeX = Math.Max(1, tree.Indent + 1);
                hitNode = tree.GetNodeAt(probeX, location.Y);
            }

            return GetRealTreeNode(hitNode);
        }

        private void OnNodeSelect(object sender, TreeViewEventArgs e)
        {
            TreeNode selectedNode = GetRealTreeNode(e.Node);
            if (selectedNode != e.Node)
            {
                SelectTreeNode(selectedNode);
                return;
            }

            SyncWpfTreeSelection();
            ResyncSelectedNode();
            SelectedNodeChanged?.Invoke(this, GetSelectedDialogueNode());
        }

        private void OnNodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            var dialogueNode = GetSelectedDialogueNode();
            if (dialogueNode == null)
                return;

            if (dialogueNode is DialogueNodeGoto)
            {
                SelectNode((dialogueNode as DialogueNodeGoto).Goto);
            }
        }

        private void OnNodeCollapse(object sender, TreeViewCancelEventArgs e)
        {
            TreeNode node = GetRealTreeNode(e.Node);
            if (node == null)
                return;

            //Forbid the collapsing of desired nodes
            if (((NodeWrap)node.Tag).DialogueNode is DialogueNodeRoot)
            {
                e.Cancel = true;
            }
        }

        private void OnContextMenuOpened(object sender, EventArgs e)
        {
            separatorRoot.Visible = false;
            menuItemOpenDirectory.Visible = false;
            menuItemCopyName.Visible = false;

            separatorReply.Visible = false;
            menuItemAddReply.Visible = false;

            separatorBranch.Visible = false;
            menuItemBranch.Visible = false;

            menuItemAddSentence.Visible = false;
            menuItemAddChoice.Visible = false;
            menuItemAddGoto.Visible = false;
            menuItemAddBranch.Visible = false;
            menuItemAddComment.Visible = false;
            menuItemAddReturn.Visible = false;

            separatorReference.Visible = false;
            menuItemCopyReference.Visible = false;
            menuItemPasteReference.Visible = false;
            menuItemCopyID.Visible = false;

            separatorMove.Visible = false;
            menuItemMoveNodeUp.Visible = false;
            menuItemMoveNodeDown.Visible = false;

            separatorDelete.Visible = false;
            menuItemDelete.Visible = false;

            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);
            if (selectedNode == null)
                return;
            SelectTreeNode(selectedNode);

            DialogueNode node = ((NodeWrap)selectedNode.Tag).DialogueNode;

            //Node Insertion
            if (node is DialogueNodeRoot
            || node is DialogueNodeSentence
            || node is DialogueNodeChoice
            || node is DialogueNodeReply
            || node is DialogueNodeGoto
            || node is DialogueNodeBranch
            || node is DialogueNodeComment
            || node is DialogueNodeReturn)
            {
                menuItemAddSentence.Visible = true;
                menuItemAddChoice.Visible = true;
                menuItemAddGoto.Visible = true;
                menuItemAddBranch.Visible = true;
                menuItemAddComment.Visible = true;
                menuItemAddReturn.Visible = true;
            }

            //Root
            if (node is DialogueNodeRoot)
            {
                separatorRoot.Visible = true;
                menuItemOpenDirectory.Visible = true;
                menuItemCopyName.Visible = true;
            }

            //Choice
            if (node is DialogueNodeChoice)
            {
                separatorReply.Visible = true;
                menuItemAddReply.Visible = true;
            }

            //Branch
            if (node is DialogueNodeBranch)
            {
                separatorBranch.Visible = true;
                menuItemBranch.Visible = true;
            }

            //Reference copy/paste + Copy ID
            if (node is DialogueNodeGoto)
            {
                separatorReference.Visible = true;
                menuItemPasteReference.Visible = true;
                menuItemPasteReference.Enabled = (copyReference != -1);
                menuItemCopyID.Visible = true;
            }
            else if (node is DialogueNodeSentence
                || node is DialogueNodeChoice
                || node is DialogueNodeBranch
                || node is DialogueNodeComment
                || node is DialogueNodeReturn)
            {
                separatorReference.Visible = true;
                menuItemCopyReference.Visible = true;
                menuItemCopyID.Visible = true;
            }
            else if (node is DialogueNodeReply)
            {
                separatorReference.Visible = true;
                menuItemCopyID.Visible = true;
            }

            //Move
            if (!(node is DialogueNodeRoot))
            {
                separatorMove.Visible = true;
                menuItemMoveNodeUp.Visible = true;
                menuItemMoveNodeDown.Visible = true;
            }

            //Delete
            if (!(node is DialogueNodeRoot))
            {
                separatorDelete.Visible = true;
                menuItemDelete.Visible = true;
            }
        }

        private void OnOpenDirectory(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start(System.IO.Path.Combine(EditorHelper.GetProjectDirectory(), Dialogue.GetFilePath()));
        }

        private void OnCopyName(object sender, EventArgs e)
        {
            Clipboard.SetText(Dialogue.GetName());
        }

        private void OnAddNodeSentence(object sender, EventArgs e)
        {
            CreateNodeSentence(tree.SelectedNode, false);
        }

        private void OnAddNodeChoice(object sender, EventArgs e)
        {
            CreateNodeChoice(tree.SelectedNode, false);
        }

        private void OnAddNodeReply(object sender, EventArgs e)
        {
            if (!IsTreeNodeChoice(tree.SelectedNode))
                return;

            CreateNodeReply(tree.SelectedNode);
        }

        private void OnAddNodeGoto(object sender, EventArgs e)
        {
            CreateNodeGoto(tree.SelectedNode, false);
        }

        private void OnAddNodeBranch(object sender, EventArgs e)
        {
            CreateNodeBranch(tree.SelectedNode, false);
        }

        private void OnAddNodeReturn(object sender, EventArgs e)
        {
            CreateNodeReturn(tree.SelectedNode, false);
        }

        private void OnAddNodeComment(object sender, EventArgs e)
        {
            CreateNodeComment(tree.SelectedNode, false);
        }

        private void OnBranchNodeSentence(object sender, EventArgs e)
        {
            DialogueNodeBranch nodeBranch = GetDialogueNode(tree.SelectedNode) as DialogueNodeBranch;
            if (nodeBranch == null)
                return;

            CreateNodeSentence(tree.SelectedNode, true);
        }

        private void OnBranchNodeChoice(object sender, EventArgs e)
        {
            DialogueNodeBranch nodeBranch = GetDialogueNode(tree.SelectedNode) as DialogueNodeBranch;
            if (nodeBranch == null)
                return;

            CreateNodeChoice(tree.SelectedNode, true);
        }

        private void OnBranchNodeGoto(object sender, EventArgs e)
        {
            DialogueNodeBranch nodeBranch = GetDialogueNode(tree.SelectedNode) as DialogueNodeBranch;
            if (nodeBranch == null)
                return;

            CreateNodeGoto(tree.SelectedNode, true);
        }

        private void OnBranchNodeBranch(object sender, EventArgs e)
        {
            DialogueNodeBranch nodeBranch = GetDialogueNode(tree.SelectedNode) as DialogueNodeBranch;
            if (nodeBranch == null)
                return;

            CreateNodeBranch(tree.SelectedNode, true);
        }

        private void OnBranchNodeComment(object sender, EventArgs e)
        {
            DialogueNodeBranch nodeBranch = GetDialogueNode(tree.SelectedNode) as DialogueNodeBranch;
            if (nodeBranch == null)
                return;

            CreateNodeComment(tree.SelectedNode, true);
        }

        protected void OnDeleteNode(object sender, EventArgs e)
        {
            RemoveNode(tree.SelectedNode);
        }

        protected virtual void OnCopyReference(object sender, EventArgs e)
        {
            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);
            if (selectedNode == null)
                return;

            DialogueNode dialogueNode = ((NodeWrap)selectedNode.Tag).DialogueNode;
            copyReference = dialogueNode.ID;
        }

        protected virtual void OnPasteReference(object sender, EventArgs e)
        {
            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);
            if (!IsTreeNodeGoto(selectedNode))
                return;

            DialogueNodeGoto nodeGoto = ((NodeWrap)selectedNode.Tag).DialogueNode as DialogueNodeGoto;

            DialogueNode newTarget = Dialogue.GetNodeByID(copyReference);
            if (newTarget != null)
            {
                DialogueNode oldTarget = nodeGoto.Goto;
                nodeGoto.Goto = newTarget;

                RefreshTreeNode(selectedNode);
                RefreshTreeNode(GetTreeNode(oldTarget));
                RefreshTreeNode(GetTreeNode(newTarget));

                SetDirty();
            }
        }

        protected virtual void OnMoveNodeUp(object sender, EventArgs e)
        {
            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);
            if (selectedNode == null)
                return;

            bool moved = false;
            TreeNode previousRealNode = GetPreviousRealSibling(selectedNode);
            if (previousRealNode != null)
            {
                TreeNode previousPreviousRealNode = GetPreviousRealSibling(previousRealNode);
                if (previousPreviousRealNode != null)
                {
                    moved = MoveTreeNode(selectedNode, previousPreviousRealNode, EMoveTreeNode.Sibling);
                }
                else
                {
                    moved = MoveTreeNode(selectedNode, selectedNode.Parent, EMoveTreeNode.Sibling);
                }
            }

            if (moved)
                SetDirty();
        }

        protected virtual void OnMoveNodeDown(object sender, EventArgs e)
        {
            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);
            if (selectedNode == null)
                return;

            bool moved = false;
            TreeNode nextRealNode = GetNextRealSibling(selectedNode);
            if (nextRealNode != null)
            {
                moved = MoveTreeNode(selectedNode, nextRealNode, EMoveTreeNode.Sibling);
            }

            if (moved)
                SetDirty();
        }

        private void OnCheckDisplayOptions(object sender, EventArgs e)
        {
            if (lockCheckDisplayEvents)
                return;

            EditorCore.Settings.DisplaySpeaker = checkBoxDisplaySpeaker.Checked;
            EditorCore.Settings.DisplayListener = checkBoxDisplayListener.Checked;
            EditorCore.Settings.DisplayConditions = checkBoxDisplayConditions.Checked;
            EditorCore.Settings.DisplayActions = checkBoxDisplayActions.Checked;
            EditorCore.Settings.DisplayFlags = checkBoxDisplayFlags.Checked;
            EditorCore.Settings.DisplayID = checkBoxDisplayID.Checked;
            EditorCore.Settings.DisplayText = checkBoxDisplayText.Checked;
            EditorCore.Settings.DisplayContext = checkBoxDisplayContext.Checked;
            EditorCore.Settings.DisplayComments = checkBoxDisplayComments.Checked;
            EditorCore.Settings.UseActorColors = checkBoxUseActorColors.Checked;
            EditorCore.Settings.UseConstants = checkBoxUseConstants.Checked;

            UpdateTreeItemHeight();
            RefreshAllTreeNodes();
            DialogueChanged?.Invoke(this);
        }

        private void OnChangeFont(object sender, EventArgs e)
        {
            if (lockCheckDisplayEvents)
                return;

            var dialog = new FontDialog();
            dialog.Font = tree.Font;

            var result = dialog.ShowDialog();
            if (result == DialogResult.OK)
            {
                EditorCore.Settings.DialogueTreeViewFont = dialog.Font;

                RefreshFont();
                RefreshAllTreeNodes();
            }
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (lockCheckDisplayEvents)
                return;

            var language = comboBoxLanguages.SelectedItem as Language;
            EditorHelper.CurrentLanguage = language;

            RefreshAllTreeNodes();
        }

        private void OnClose(object sender, FormClosingEventArgs e)
        {
            //UserClosing : cross, middle click, Application.Exit
            //MdiFormClosing : app form close, alt+f4
            if (EditorCore.MainWindow != null && e.CloseReason == CloseReason.UserClosing)
            {
                if (!EditorCore.MainWindow.OnDocumentDialogueClosed(this, ForceClose))
                {
                    e.Cancel = true;
                }
            }
        }

        private void OnCopyID(object sender, EventArgs e)
        {
            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);
            if (selectedNode == null)
                return;

            DialogueNode dialogueNode = ((NodeWrap)selectedNode.Tag).DialogueNode;
            Clipboard.SetText(EditorHelper.GetPrettyNodeID(Dialogue, dialogueNode));
        }

        private void OnTreeItemDrag(object sender, ItemDragEventArgs e)
        {
            //EditorCore.LogInfo("Start Dragging");
            TreeNode nodeMove = GetRealTreeNode(e.Item as TreeNode);
            if (nodeMove == null)
                return;

            if (!IsTreeNodeRoot(nodeMove))
            {
                DoDragDrop(nodeMove, DragDropEffects.Move);
            }
        }

        private void OnTreeDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
            
            /*TreeNode nodeMove = (TreeNode)e.Data.GetData(typeof(TreeNode));

            Point targetPoint = tree.PointToClient(new Point(e.X, e.Y));
            TreeNode nodeTarget = tree.GetNodeAt(targetPoint);

            if (nodeMove != null && nodeTarget != null)
            {
                EditorCore.LogInfo(String.Format("Check node {0} drop on node {1}", GetDialogueNode(nodeMove).ID, GetDialogueNode(nodeTarget).ID));

                if (CanMoveTreeNode(nodeMove, nodeTarget))
                    e.Effect = DragDropEffects.Move;
            }*/
        }

        private bool CanMoveTreeNode(TreeNode nodeMove, TreeNode nodeTarget)
        {
            nodeMove = GetRealTreeNode(nodeMove);
            nodeTarget = GetRealTreeNode(nodeTarget);

            if (nodeMove == null || nodeTarget == null || nodeMove == nodeTarget)
                return false;

            if (IsTreeNodeRoot(nodeMove))
                return false;

            if (IsTreeNodeReply(nodeMove) && !IsTreeNodeReply(nodeTarget) && !IsTreeNodeChoice(nodeTarget))
                return false;

            //Check we are not attaching a node on a depending node (loop)
            List<DialogueNode> dependendingNodes = new List<DialogueNode>();
            Dialogue.GetDependingNodes(GetDialogueNode(nodeMove), ref dependendingNodes);
            if (dependendingNodes.Contains(GetDialogueNode(nodeTarget)))
                return false;

            return true;
        }

        private void OnTreeDragDrop(object sender, DragEventArgs e)
        {
            //EditorCore.LogInfo("Start Dropping");

            TreeNode nodeMove = GetRealTreeNode((TreeNode)e.Data.GetData(typeof(TreeNode)));

            Point targetPoint = tree.PointToClient(new Point(e.X, e.Y));
            TreeNode nodeTarget = GetRealTreeNode(tree.GetNodeAt(targetPoint));

            //e.Effect = DragDropEffects.Move;

            if (nodeMove != null && nodeTarget != null)
            {
                //EditorCore.LogInfo(String.Format("Node {0} dropped on node {1}", GetDialogueNode(nodeMove).ID, GetDialogueNode(nodeTarget).ID));

                if (MoveTreeNode(nodeMove, nodeTarget, EMoveTreeNode.Drop))
                {
                    SetDirty();

                    //EditorCore.LogInfo("Drop Success");
                }
            }
        }
    }
}
