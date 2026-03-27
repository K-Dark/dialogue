using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using DialogueEditor.WpfTree;

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

        private Thickness GetRowPadding(TreeNode node, bool isDisplayRow)
        {
            if (isDisplayRow)
            {
                return new Thickness(4, 0, 4, 0);
            }

            double top = HasAttachedDisplayRowsAbove(node) ? 0 : 1;
            double bottom = HasAttachedDisplayRowsBelow(node) ? 0 : 1;
            return new Thickness(4, top, 4, bottom);
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
