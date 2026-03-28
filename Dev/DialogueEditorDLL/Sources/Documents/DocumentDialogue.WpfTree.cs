using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using DialogueEditor.WpfTree;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKeyInterop = System.Windows.Input.KeyInterop;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace DialogueEditor
{
    public partial class DocumentDialogue
    {
        private bool useWpfDialogueTree;
        private bool lockWpfSelectionSync;
        private ElementHost wpfTreeHost;
        private WpfDialogueTreeView wpfTreeView;

        private void InitializeWpfTreeHost()
        {
            useWpfDialogueTree = EditorCore.Settings != null && EditorCore.Settings.UseWpfDialogueTree;
            if (!useWpfDialogueTree)
                return;

            wpfTreeView = new WpfDialogueTreeView();
            wpfTreeView.NodeSelected += OnWpfTreeNodeSelected;
            wpfTreeView.NodeExpansionToggled += OnWpfTreeNodeExpansionToggled;
            wpfTreeView.NodeContextMenuRequested += OnWpfTreeNodeContextMenuRequested;
            wpfTreeView.NodeDragDropRequested += OnWpfTreeNodeDragDropRequested;
            wpfTreeView.NodeKeyDown += OnWpfTreeNodeKeyDown;

            wpfTreeHost = new ElementHost();
            wpfTreeHost.Name = "wpfTreeHost";
            wpfTreeHost.Anchor = tree.Anchor;
            wpfTreeHost.Location = tree.Location;
            wpfTreeHost.Size = tree.Size;
            wpfTreeHost.BackColor = tree.BackColor;
            wpfTreeHost.Child = wpfTreeView;

            Controls.Add(wpfTreeHost);
            wpfTreeHost.BringToFront();

            tree.Visible = false;
        }

        private void RefreshWpfTreeHost()
        {
            if (!useWpfDialogueTree || wpfTreeView == null)
                return;

            int selectedNodeID = DialogueNode.ID_NULL;
            TreeNode selectedTreeNode = GetRealTreeNode(tree.SelectedNode);
            if (selectedTreeNode != null)
            {
                NodeWrap selectedWrap = selectedTreeNode.Tag as NodeWrap;
                if (selectedWrap != null && selectedWrap.DialogueNode != null)
                    selectedNodeID = selectedWrap.DialogueNode.ID;
            }

            wpfTreeView.SetRows(BuildWpfTreeRows(selectedTreeNode), selectedNodeID);
        }

        private void SyncWpfTreeSelection()
        {
            if (!useWpfDialogueTree || wpfTreeView == null || lockWpfSelectionSync)
                return;

            int selectedNodeID = DialogueNode.ID_NULL;
            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);
            if (selectedNode != null)
            {
                NodeWrap selectedWrap = selectedNode.Tag as NodeWrap;
                if (selectedWrap != null && selectedWrap.DialogueNode != null)
                    selectedNodeID = selectedWrap.DialogueNode.ID;
            }

            wpfTreeView.SetSelectedNode(selectedNodeID);
        }

        private List<WpfDialogueTreeRow> BuildWpfTreeRows(TreeNode selectedTreeNode)
        {
            List<WpfDialogueTreeRow> rows = new List<WpfDialogueTreeRow>();
            BuildWpfTreeRows(rows, tree.Nodes, selectedTreeNode);
            return rows;
        }

        private void BuildWpfTreeRows(List<WpfDialogueTreeRow> rows, TreeNodeCollection nodes, TreeNode selectedTreeNode)
        {
            if (nodes == null)
                return;

            foreach (TreeNode node in nodes)
            {
                if (node == null || node.Tag == null)
                    continue;

                NodeWrap wrap = node.Tag as NodeWrap;
                if (wrap == null || wrap.DialogueNode == null)
                    continue;

                string text = node.Text;
                if (String.IsNullOrWhiteSpace(text))
                    text = GetTreeNodeTextContent(wrap.DialogueNode, node.Level);

                string textID = string.Empty;
                string textAttributes = string.Empty;
                string textActors = string.Empty;
                string textContent = text;

                SolidColorBrush idBrush = null;
                SolidColorBrush attributesBrush = null;
                SolidColorBrush actorsBrush = null;
                SolidColorBrush contentBrush = null;

                if (!wrap.IsDisplayRow)
                {
                    textID = GetTreeNodeTextID(wrap.DialogueNode);
                    textAttributes = GetTreeNodeTextAttributes(wrap.DialogueNode);
                    textActors = GetTreeNodeTextActors(wrap.DialogueNode);
                    textContent = GetTreeNodeTextContent(wrap.DialogueNode, node.Level);
                    text = textID + textAttributes + textActors + textContent;

                    idBrush = ConvertBrush(System.Drawing.Color.Black);
                    attributesBrush = ConvertBrush(System.Drawing.Color.MediumOrchid);
                    actorsBrush = ConvertBrush(System.Drawing.Color.DimGray);
                    contentBrush = ConvertBrush(GetTreeNodeColorContent(wrap.DialogueNode));
                }
                else
                {
                    contentBrush = ConvertBrush(node.ForeColor);
                    idBrush = contentBrush;
                    attributesBrush = contentBrush;
                    actorsBrush = contentBrush;
                }

                bool hasChildren = !wrap.IsDisplayRow && HasRealChildNodes(node);
                bool isExpanded = hasChildren && node.IsExpanded;
                bool canToggle = hasChildren && !(wrap.DialogueNode is DialogueNodeRoot);
                System.Drawing.Color rowBackColor = node.BackColor;
                if (!wrap.IsDisplayRow && selectedTreeNode != null && node == selectedTreeNode)
                    rowBackColor = ThemeManager.Current.SelectionBackground;
                Font nodeFont = node.NodeFont ?? tree.Font;
                float rowFontSizePoints = nodeFont.Size;
                bool isBold = (nodeFont.Style & System.Drawing.FontStyle.Bold) == System.Drawing.FontStyle.Bold;
                bool isItalic = (nodeFont.Style & System.Drawing.FontStyle.Italic) == System.Drawing.FontStyle.Italic;

                if (wrap.IsDisplayRow)
                {
                    rowFontSizePoints = Math.Max(6.0f, rowFontSizePoints - 1.0f);
                    isItalic = true;
                }

                rows.Add(new WpfDialogueTreeRow(
                    wrap.DialogueNode.ID,
                    text,
                    node.Level,
                    wrap.IsDisplayRow,
                    hasChildren,
                    isExpanded,
                    canToggle,
                    contentBrush,
                    ConvertBrush(rowBackColor),
                    isBold ? FontWeights.Bold : FontWeights.Normal,
                    isItalic ? FontStyles.Italic : FontStyles.Normal,
                    ConvertPointsToDip(rowFontSizePoints),
                    nodeFont.Name,
                    GetRowMargin(node, wrap.IsDisplayRow),
                    GetRowPadding(node, wrap.IsDisplayRow),
                    textID,
                    textAttributes,
                    textActors,
                    textContent,
                    idBrush,
                    attributesBrush,
                    actorsBrush,
                    contentBrush));

                if (!wrap.IsDisplayRow && (node.IsExpanded || wrap.DialogueNode is DialogueNodeRoot))
                    BuildWpfTreeRows(rows, node.Nodes, selectedTreeNode);
            }
        }

        private bool HasRealChildNodes(TreeNode node)
        {
            if (node == null)
                return false;

            foreach (TreeNode child in node.Nodes)
            {
                if (!IsDisplayTreeNode(child))
                    return true;
            }

            return false;
        }

        private void OnWpfTreeNodeSelected(object sender, int selectedNodeID)
        {
            TreeNode selectedNode = GetTreeNode(selectedNodeID);
            if (selectedNode == null)
                return;

            lockWpfSelectionSync = true;
            try
            {
                SelectTreeNode(selectedNode);
                ResyncSelectedNode();
                SelectedNodeChanged?.Invoke(this, GetSelectedDialogueNode());
            }
            finally
            {
                lockWpfSelectionSync = false;
            }
        }

        private void OnWpfTreeNodeExpansionToggled(object sender, int selectedNodeID)
        {
            TreeNode node = GetRealTreeNode(GetTreeNode(selectedNodeID));
            if (node == null || IsTreeNodeRoot(node))
                return;

            lockWpfSelectionSync = true;
            try
            {
                if (node.IsExpanded)
                    node.Collapse();
                else
                    node.Expand();
            }
            finally
            {
                lockWpfSelectionSync = false;
            }

            RefreshWpfTreeHost();
        }

        private void OnWpfTreeNodeContextMenuRequested(object sender, WpfDialogueTreeMouseEventArgs e)
        {
            TreeNode node = GetRealTreeNode(GetTreeNode(e.NodeID));
            if (node == null || contextMenu == null || wpfTreeHost == null)
                return;

            lockWpfSelectionSync = true;
            try
            {
                SelectTreeNode(node);
            }
            finally
            {
                lockWpfSelectionSync = false;
            }

            System.Drawing.Point menuPosition = wpfTreeHost.PointToClient(System.Windows.Forms.Cursor.Position);
            if (menuPosition.X < 0 || menuPosition.Y < 0)
            {
                int x = (int)Math.Max(0.0, Math.Round(e.Position.X));
                int y = (int)Math.Max(0.0, Math.Round(e.Position.Y));
                menuPosition = new System.Drawing.Point(x, y);
            }

            contextMenu.Show(wpfTreeHost, menuPosition);
        }

        private void OnWpfTreeNodeDragDropRequested(object sender, WpfDialogueTreeDragDropEventArgs e)
        {
            TreeNode nodeMove = GetRealTreeNode(GetTreeNode(e.SourceNodeID));
            TreeNode nodeTarget = GetRealTreeNode(GetTreeNode(e.TargetNodeID));
            if (nodeMove == null || nodeTarget == null)
                return;

            if (!CanMoveTreeNode(nodeMove, nodeTarget))
                return;

            if (MoveTreeNode(nodeMove, nodeTarget, EMoveTreeNode.Drop))
                SetDirty();
        }

        private void OnWpfTreeNodeKeyDown(object sender, WpfKeyEventArgs e)
        {
            WinFormsKeys keyData = ConvertToFormsKeys(e);
            if (HandleWpfTreeNavigationKey(keyData))
            {
                e.Handled = true;
                return;
            }

            if (!IsTreeKeyDownCommand(keyData))
                return;

            var args = new System.Windows.Forms.KeyEventArgs(keyData);
            OnKeyDown(tree, args);
            if (args.Handled || args.SuppressKeyPress)
                e.Handled = true;
        }

        private WinFormsKeys ConvertToFormsKeys(WpfKeyEventArgs e)
        {
            WpfKey key = (e.Key == WpfKey.System) ? e.SystemKey : e.Key;
            int virtualKey = WpfKeyInterop.VirtualKeyFromKey(key);
            WinFormsKeys result = (WinFormsKeys)virtualKey;

            WpfModifierKeys modifiers = WpfKeyboard.Modifiers;
            if ((modifiers & WpfModifierKeys.Control) != 0)
                result |= WinFormsKeys.Control;
            if ((modifiers & WpfModifierKeys.Shift) != 0)
                result |= WinFormsKeys.Shift;
            if ((modifiers & WpfModifierKeys.Alt) != 0)
                result |= WinFormsKeys.Alt;

            return result;
        }

        private bool IsTreeKeyDownCommand(WinFormsKeys keyData)
        {
            WinFormsKeys keyCode = keyData & WinFormsKeys.KeyCode;
            bool control = (keyData & WinFormsKeys.Control) == WinFormsKeys.Control;

            if (keyCode == WinFormsKeys.Delete)
                return true;

            return control
                && (keyCode == WinFormsKeys.C
                 || keyCode == WinFormsKeys.X
                 || keyCode == WinFormsKeys.V
                 || keyCode == WinFormsKeys.Z
                 || keyCode == WinFormsKeys.Y);
        }

        private bool HandleWpfTreeNavigationKey(WinFormsKeys keyData)
        {
            if ((keyData & WinFormsKeys.Modifiers) != WinFormsKeys.None)
                return false;

            TreeNode selectedNode = GetRealTreeNode(tree.SelectedNode);
            if (selectedNode == null)
                return false;

            WinFormsKeys keyCode = keyData & WinFormsKeys.KeyCode;
            if (keyCode == WinFormsKeys.Left)
            {
                if (selectedNode.IsExpanded && HasRealChildNodes(selectedNode) && !IsTreeNodeRoot(selectedNode))
                {
                    lockWpfSelectionSync = true;
                    try
                    {
                        selectedNode.Collapse();
                    }
                    finally
                    {
                        lockWpfSelectionSync = false;
                    }

                    RefreshWpfTreeHost();
                    return true;
                }

                TreeNode parentNode = GetRealTreeNode(selectedNode.Parent);
                DialogueNode parentDialogueNode = GetDialogueNode(parentNode);
                if (parentDialogueNode != null)
                {
                    OnWpfTreeNodeSelected(this, parentDialogueNode.ID);
                    return true;
                }
            }
            else if (keyCode == WinFormsKeys.Right)
            {
                if (HasRealChildNodes(selectedNode) && !selectedNode.IsExpanded)
                {
                    lockWpfSelectionSync = true;
                    try
                    {
                        selectedNode.Expand();
                    }
                    finally
                    {
                        lockWpfSelectionSync = false;
                    }

                    RefreshWpfTreeHost();
                    return true;
                }

                TreeNode firstChild = GetFirstRealChildNode(selectedNode);
                DialogueNode childDialogueNode = GetDialogueNode(firstChild);
                if (childDialogueNode != null)
                {
                    OnWpfTreeNodeSelected(this, childDialogueNode.ID);
                    return true;
                }
            }

            return false;
        }

        private TreeNode GetFirstRealChildNode(TreeNode node)
        {
            if (node == null)
                return null;

            foreach (TreeNode child in node.Nodes)
            {
                if (!IsDisplayTreeNode(child))
                    return child;
            }

            return null;
        }

        private bool IsDialogueTreeFocused()
        {
            if (tree.Focused)
                return true;

            if (!useWpfDialogueTree || wpfTreeHost == null || wpfTreeView == null || !wpfTreeHost.Visible)
                return false;

            return wpfTreeHost.ContainsFocus || wpfTreeView.IsTreeFocused;
        }

        private void FocusDialogueTreeControl()
        {
            if (useWpfDialogueTree && wpfTreeHost != null && wpfTreeView != null && wpfTreeHost.Visible)
            {
                wpfTreeHost.Focus();
                wpfTreeView.FocusTree();
                return;
            }

            tree.Focus();
        }

        private Thickness GetRowPadding(TreeNode node, bool isDisplayRow)
        {
            if (isDisplayRow)
                return new Thickness(4, 0, 4, 0);

            return new Thickness(4, 0, 4, 0);
        }

        private Thickness GetRowMargin(TreeNode node, bool isDisplayRow)
        {
            if (node == null)
                return new Thickness(0);

            TreeNode blockOwner = GetDisplayTreeBlockOwner(node);
            if (blockOwner == null)
                return new Thickness(0, 0, 0, 0);

            if (!IsDisplayTreeBlockStartRow(node, isDisplayRow))
                return new Thickness(0, 0, 0, 0);

            return new Thickness(0, GetGapAbove(blockOwner), 0, 0);
        }

        private TreeNode GetDisplayTreeBlockOwner(TreeNode node)
        {
            if (node == null)
                return null;

            if (!IsDisplayTreeNode(node))
                return node;

            return GetDisplayRowOwner(node);
        }

        private bool IsDisplayTreeBlockStartRow(TreeNode node, bool isDisplayRow)
        {
            if (node == null)
                return false;

            if (!isDisplayRow)
                return !HasAttachedDisplayRowsAbove(node);

            if (!IsDisplayRowInsertedBeforeOwner(node))
                return false;

            TreeNode owner = GetDisplayRowOwner(node);
            if (owner == null)
                return false;

            TreeNode previous = node.PrevNode;
            if (previous == null || !IsDisplayTreeNode(previous))
                return true;

            NodeWrap previousWrap = previous.Tag as NodeWrap;
            return previousWrap == null
                || previousWrap.OwnerTreeNode != owner
                || !IsDisplayRowInsertedBeforeOwner(previous);
        }

        private double GetGapAbove(TreeNode node)
        {
            if (node == null)
                return 0.0;

            TreeNode previousNode = GetPreviousRealSibling(node);
            if (previousNode == null)
                return 0.0;

            if (previousNode != null && IsSameSpeakerSentence(previousNode, node))
                return GetWpfTreeGap(true);

            return GetWpfTreeGap(false);
        }

        private bool IsSameSpeakerSentence(TreeNode previousNode, TreeNode currentNode)
        {
            DialogueNodeSentence previousSentence = GetDialogueNode(previousNode) as DialogueNodeSentence;
            DialogueNodeSentence currentSentence = GetDialogueNode(currentNode) as DialogueNodeSentence;
            if (previousSentence == null || currentSentence == null)
                return false;

            return previousSentence.SpeakerID == currentSentence.SpeakerID;
        }

        private double GetWpfTreeGap(bool sameSpeaker)
        {
            double fallback = sameSpeaker ? 1.0 : 2.0;
            if (EditorCore.Settings == null)
                return fallback;

            double value = sameSpeaker ? EditorCore.Settings.WpfTreeGapSameSpeaker : EditorCore.Settings.WpfTreeGapDefault;
            if (Double.IsNaN(value) || Double.IsInfinity(value))
                return fallback;

            return Math.Max(0.0, value);
        }

        private SolidColorBrush ConvertBrush(System.Drawing.Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }

        private double ConvertPointsToDip(float points)
        {
            return Math.Max(8.0, points * (96.0 / 72.0));
        }
    }
}
