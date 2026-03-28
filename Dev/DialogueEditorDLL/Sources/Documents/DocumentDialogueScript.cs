using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using WeifenLuo.WinFormsUI.Docking;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCursor = System.Windows.Input.Cursors;
using WpfDockPanel = System.Windows.Controls.DockPanel;
using WpfExpander = System.Windows.Controls.Expander;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfGrid = System.Windows.Controls.Grid;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRowDefinition = System.Windows.Controls.RowDefinition;
using WpfScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextTrimming = System.Windows.TextTrimming;
using WpfTextWrapping = System.Windows.TextWrapping;
using WpfThickness = System.Windows.Thickness;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace DialogueEditor
{
    public class DocumentDialogueScript : DockContent, IDocument
    {
        private sealed class ScriptBlock
        {
            public int NodeID;
            public bool IsContainer;
            public bool Bold;
            public bool Italic;
            public bool IsTitle;
            public string Text;
            public Color Color;
            public List<ScriptBlock> Children = new List<ScriptBlock>();
        }

        private sealed class NodeVisualState
        {
            public WpfBorder Row;
            public List<WpfExpander> AncestorExpanders = new List<WpfExpander>();
        }

        private sealed class WpfDialogueScriptView : WpfUserControl
        {
            private const double IndentPerLevel = 18.0;

            private readonly WpfStackPanel contentPanel;
            private readonly WpfScrollViewer scrollViewer;
            private readonly WpfBrush selectionBrush;
            private readonly WpfBrush titleBrush;
            private readonly Dictionary<int, NodeVisualState> nodeStates = new Dictionary<int, NodeVisualState>();
            private readonly Dictionary<int, WpfExpander> containerExpanders = new Dictionary<int, WpfExpander>();
            private readonly List<WpfExpander> allExpanders = new List<WpfExpander>();
            private int selectedNodeID = DialogueNode.ID_NULL;

            public event Action<int> NodeClicked;

            public WpfDialogueScriptView()
            {
                selectionBrush = ConvertBrush(ThemeManager.Current.SelectionBackground);
                titleBrush = ConvertBrush(Color.FromArgb(120, 120, 120));

                WpfGrid root = new WpfGrid();
                root.RowDefinitions.Add(new WpfRowDefinition() { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Auto) });
                root.RowDefinitions.Add(new WpfRowDefinition() { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

                WpfDockPanel toolbar = BuildToolbar();
                System.Windows.Controls.Grid.SetRow(toolbar, 0);
                root.Children.Add(toolbar);

                contentPanel = new WpfStackPanel();
                contentPanel.Orientation = WpfOrientation.Vertical;

                scrollViewer = new WpfScrollViewer();
                scrollViewer.VerticalScrollBarVisibility = WpfScrollBarVisibility.Auto;
                scrollViewer.HorizontalScrollBarVisibility = WpfScrollBarVisibility.Disabled;
                scrollViewer.Content = contentPanel;
                System.Windows.Controls.Grid.SetRow(scrollViewer, 1);
                root.Children.Add(scrollViewer);

                Content = root;
            }

            private WpfDockPanel BuildToolbar()
            {
                WpfDockPanel toolbar = new WpfDockPanel();
                toolbar.LastChildFill = false;
                toolbar.Margin = new WpfThickness(8, 6, 8, 4);

                WpfButton buttonExpandAll = new WpfButton();
                buttonExpandAll.Content = "Expand All";
                buttonExpandAll.Padding = new WpfThickness(10, 2, 10, 2);
                buttonExpandAll.Margin = new WpfThickness(0, 0, 6, 0);
                buttonExpandAll.Click += (sender, e) => ExpandAllContainers();
                toolbar.Children.Add(buttonExpandAll);

                WpfButton buttonCollapseAll = new WpfButton();
                buttonCollapseAll.Content = "Collapse All";
                buttonCollapseAll.Padding = new WpfThickness(10, 2, 10, 2);
                buttonCollapseAll.Margin = new WpfThickness(0, 0, 12, 0);
                buttonCollapseAll.Click += (sender, e) => CollapseAllContainers();
                toolbar.Children.Add(buttonCollapseAll);

                WpfTextBlock legend = new WpfTextBlock();
                legend.Text = "Script view (foldable choices/replies)";
                legend.Foreground = titleBrush;
                legend.VerticalAlignment = WpfVerticalAlignment.Center;
                toolbar.Children.Add(legend);

                return toolbar;
            }

            public HashSet<int> CaptureExpandedContainerNodeIDs()
            {
                HashSet<int> expanded = new HashSet<int>();
                foreach (var item in containerExpanders)
                {
                    if (item.Value.IsExpanded)
                        expanded.Add(item.Key);
                }

                return expanded;
            }

            public void SetBlocks(IList<ScriptBlock> blocks, int selectedNode, HashSet<int> expandedContainers)
            {
                selectedNodeID = selectedNode;
                contentPanel.Children.Clear();
                nodeStates.Clear();
                containerExpanders.Clear();
                allExpanders.Clear();

                if (blocks != null)
                {
                    foreach (ScriptBlock block in blocks)
                    {
                        AddBlock(contentPanel, block, 0, new List<WpfExpander>(), expandedContainers);
                    }
                }

                ApplySelectionHighlight();
                EnsureSelectedNodeVisible();
            }

            public void SetSelectedNode(int selectedNode)
            {
                selectedNodeID = selectedNode;
                ApplySelectionHighlight();
                EnsureSelectedNodeVisible();
            }

            private void ExpandAllContainers()
            {
                foreach (WpfExpander expander in allExpanders)
                    expander.IsExpanded = true;
            }

            private void CollapseAllContainers()
            {
                foreach (WpfExpander expander in allExpanders)
                    expander.IsExpanded = false;
            }

            private void AddBlock(
                WpfStackPanel parent,
                ScriptBlock block,
                int depth,
                List<WpfExpander> ancestors,
                HashSet<int> expandedContainers)
            {
                if (block == null)
                    return;

                if (block.IsContainer)
                {
                    WpfExpander expander = new WpfExpander();
                    expander.Margin = new WpfThickness(depth * IndentPerLevel + 4.0, 3.0, 8.0, 3.0);
                    expander.HorizontalAlignment = WpfHorizontalAlignment.Stretch;
                    expander.Header = CreateRowBorder(block, depth, true, ancestors);

                    bool expandByDefault = expandedContainers == null || expandedContainers.Count <= 0;
                    expander.IsExpanded = expandByDefault || expandedContainers.Contains(block.NodeID);
                    parent.Children.Add(expander);

                    if (block.NodeID != DialogueNode.ID_NULL)
                        containerExpanders[block.NodeID] = expander;

                    allExpanders.Add(expander);

                    WpfStackPanel childPanel = new WpfStackPanel();
                    childPanel.Orientation = WpfOrientation.Vertical;
                    expander.Content = childPanel;

                    List<WpfExpander> childAncestors = new List<WpfExpander>(ancestors);
                    childAncestors.Add(expander);
                    foreach (ScriptBlock child in block.Children)
                    {
                        AddBlock(childPanel, child, depth + 1, childAncestors, expandedContainers);
                    }

                    return;
                }

                WpfBorder row = CreateRowBorder(block, depth, false, ancestors);
                parent.Children.Add(row);

                foreach (ScriptBlock child in block.Children)
                {
                    AddBlock(parent, child, depth + 1, ancestors, expandedContainers);
                }
            }

            private WpfBorder CreateRowBorder(ScriptBlock block, int depth, bool isHeader, List<WpfExpander> ancestors)
            {
                WpfBorder row = new WpfBorder();
                row.Padding = new WpfThickness(8, 3, 8, 3);
                row.Margin = new WpfThickness(depth * IndentPerLevel + (isHeader ? 0.0 : 4.0), 1.0, 8.0, 1.0);
                row.Background = WpfBrushes.Transparent;
                row.CornerRadius = new System.Windows.CornerRadius(3);

                WpfTextBlock text = new WpfTextBlock();
                text.Text = block.Text ?? string.Empty;
                text.TextWrapping = WpfTextWrapping.Wrap;
                text.TextTrimming = WpfTextTrimming.None;
                text.Foreground = ConvertBrush(block.Color);
                text.FontFamily = new WpfFontFamily("Consolas");
                text.FontWeight = block.Bold ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
                text.FontStyle = block.Italic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal;
                text.FontSize = block.IsTitle ? 13.0 : 12.0;
                row.Child = text;

                if (block.NodeID != DialogueNode.ID_NULL)
                {
                    row.Cursor = WpfCursor.Hand;
                    row.MouseLeftButtonDown += (sender, e) => OnNodeRowClicked(block.NodeID, e);

                    if (!nodeStates.ContainsKey(block.NodeID))
                    {
                        NodeVisualState state = new NodeVisualState();
                        state.Row = row;
                        state.AncestorExpanders = new List<WpfExpander>(ancestors);
                        nodeStates.Add(block.NodeID, state);
                    }
                }

                return row;
            }

            private void OnNodeRowClicked(int nodeID, WpfMouseButtonEventArgs e)
            {
                selectedNodeID = nodeID;
                ApplySelectionHighlight();
                EnsureSelectedNodeVisible();
                NodeClicked?.Invoke(nodeID);
                e.Handled = false;
            }

            private void ApplySelectionHighlight()
            {
                foreach (var item in nodeStates)
                {
                    item.Value.Row.Background = (item.Key == selectedNodeID) ? selectionBrush : WpfBrushes.Transparent;
                }
            }

            private void EnsureSelectedNodeVisible()
            {
                if (selectedNodeID == DialogueNode.ID_NULL || !nodeStates.ContainsKey(selectedNodeID))
                    return;

                NodeVisualState state = nodeStates[selectedNodeID];
                if (state == null || state.Row == null)
                    return;

                foreach (WpfExpander expander in state.AncestorExpanders)
                    expander.IsExpanded = true;

                state.Row.BringIntoView();
            }

            private static WpfBrush ConvertBrush(Color color)
            {
                WpfSolidColorBrush brush = new WpfSolidColorBrush(
                    System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B));
                brush.Freeze();
                return brush;
            }
        }

        private readonly ElementHost host;
        private readonly WpfDialogueScriptView scriptView;
        private readonly HashSet<int> expandedContainerNodeIDs = new HashSet<int>();
        private int selectedNodeID = DialogueNode.ID_NULL;

        public Dialogue Dialogue { get; private set; }
        public event Action<DocumentDialogueScript, DialogueNode> NodeSelected;

        public DocumentDialogueScript(Dialogue dialogue)
        {
            ThemeManager.ApplyTheme(this);

            Dialogue = dialogue;

            scriptView = new WpfDialogueScriptView();
            scriptView.NodeClicked += OnScriptNodeClicked;

            host = new ElementHost();
            host.Dock = DockStyle.Fill;
            host.Child = scriptView;
            Controls.Add(host);

            RefreshDocument();
            RefreshTitle();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (scriptView != null)
                    scriptView.NodeClicked -= OnScriptNodeClicked;
            }

            base.Dispose(disposing);
        }

        public void RefreshDocument()
        {
            HashSet<int> previousExpanded = scriptView.CaptureExpandedContainerNodeIDs();
            if (previousExpanded != null)
            {
                expandedContainerNodeIDs.Clear();
                foreach (int nodeID in previousExpanded)
                    expandedContainerNodeIDs.Add(nodeID);
            }

            List<ScriptBlock> blocks = BuildScriptBlocks();
            scriptView.SetBlocks(blocks, selectedNodeID, expandedContainerNodeIDs);
        }

        public void RefreshTitle()
        {
            Text = (Dialogue != null ? Dialogue.GetName() : string.Empty) + " [Script]";
            if (Dialogue != null && ResourcesHandler.IsDirty(Dialogue))
                Text += "*";
        }

        public void SelectNode(DialogueNode node)
        {
            selectedNodeID = (node != null) ? node.ID : DialogueNode.ID_NULL;
            scriptView.SetSelectedNode(selectedNodeID);
        }

        public DialogueNode GetSelectedDialogueNode()
        {
            if (Dialogue == null || selectedNodeID == DialogueNode.ID_NULL)
                return null;

            return Dialogue.GetNodeByID(selectedNodeID);
        }

        private void OnScriptNodeClicked(int nodeID)
        {
            selectedNodeID = nodeID;
            DialogueNode node = (Dialogue != null) ? Dialogue.GetNodeByID(nodeID) : null;
            NodeSelected?.Invoke(this, node);
        }

        private List<ScriptBlock> BuildScriptBlocks()
        {
            List<ScriptBlock> blocks = new List<ScriptBlock>();
            blocks.Add(CreateLineBlock(DialogueNode.ID_NULL, "Scene: " + (Dialogue != null ? Dialogue.GetName() : ""), Color.Black, true, false, true));

            if (Dialogue != null && Dialogue.RootNode != null)
            {
                string lastSpeaker = null;
                BuildFlowBlocks(blocks, Dialogue.RootNode.Next, new HashSet<int>(), ref lastSpeaker);
            }

            return blocks;
        }

        private void BuildFlowBlocks(List<ScriptBlock> parentBlocks, DialogueNode node, HashSet<int> visited, ref string lastSpeaker)
        {
            while (node != null)
            {
                if (node.ID != DialogueNode.ID_NULL)
                {
                    if (visited.Contains(node.ID))
                    {
                        parentBlocks.Add(CreateLineBlock(DialogueNode.ID_NULL, "[LOOP] Node " + node.ID + " already visited.", Color.Gray, false, true, false));
                        return;
                    }

                    visited.Add(node.ID);
                }

                DialogueNodeSentence sentence = node as DialogueNodeSentence;
                if (sentence != null)
                {
                    parentBlocks.Add(CreateSentenceBlock(sentence, ref lastSpeaker));
                    node = sentence.Next;
                    continue;
                }

                DialogueNodeComment comment = node as DialogueNodeComment;
                if (comment != null)
                {
                    parentBlocks.Add(CreateLineBlock(comment.ID, "[COMMENT] " + ToInlineText(comment.Comment), Color.DimGray, false, true, false));
                    node = comment.Next;
                    continue;
                }

                DialogueNodeChoice choice = node as DialogueNodeChoice;
                if (choice != null)
                {
                    lastSpeaker = null;
                    ScriptBlock choiceContainer = CreateContainerBlock(choice.ID, "[CHOICE] " + ToInlineText(choice.Choice), Color.FromArgb(186, 82, 0));

                    if (choice.Replies != null)
                    {
                        foreach (DialogueNodeReply reply in choice.Replies)
                        {
                            if (reply == null)
                                continue;

                            ScriptBlock replyContainer = CreateContainerBlock(reply.ID, "[REPLY] " + ToInlineText(reply.Reply), Color.FromArgb(176, 0, 0));
                            HashSet<int> replyVisited = new HashSet<int>(visited);
                            string branchSpeaker = null;
                            BuildFlowBlocks(replyContainer.Children, reply.Next, replyVisited, ref branchSpeaker);

                            if (replyContainer.Children.Count <= 0)
                            {
                                replyContainer.Children.Add(CreateLineBlock(DialogueNode.ID_NULL, "(empty reply flow)", Color.Gray, false, true, false));
                            }

                            choiceContainer.Children.Add(replyContainer);
                        }
                    }

                    if (choiceContainer.Children.Count <= 0)
                    {
                        choiceContainer.Children.Add(CreateLineBlock(DialogueNode.ID_NULL, "(empty choice)", Color.Gray, false, true, false));
                    }

                    parentBlocks.Add(choiceContainer);
                    node = choice.Next;
                    continue;
                }

                DialogueNodeReply replyDirect = node as DialogueNodeReply;
                if (replyDirect != null)
                {
                    parentBlocks.Add(CreateContainerBlock(replyDirect.ID, "[REPLY] " + ToInlineText(replyDirect.Reply), Color.FromArgb(176, 0, 0)));
                    node = replyDirect.Next;
                    continue;
                }

                DialogueNodeBranch branch = node as DialogueNodeBranch;
                if (branch != null)
                {
                    lastSpeaker = null;
                    string header = string.IsNullOrWhiteSpace(branch.Workstring)
                        ? "[BRANCH]"
                        : "[BRANCH] " + ToInlineText(branch.Workstring);

                    ScriptBlock branchContainer = CreateContainerBlock(branch.ID, header, Color.FromArgb(186, 82, 0));
                    HashSet<int> branchVisited = new HashSet<int>(visited);
                    string branchSpeaker = null;
                    BuildFlowBlocks(branchContainer.Children, branch.Branch, branchVisited, ref branchSpeaker);

                    if (branchContainer.Children.Count <= 0)
                    {
                        branchContainer.Children.Add(CreateLineBlock(DialogueNode.ID_NULL, "(empty branch flow)", Color.Gray, false, true, false));
                    }

                    parentBlocks.Add(branchContainer);
                    node = branch.Next;
                    continue;
                }

                DialogueNodeGoto nodeGoto = node as DialogueNodeGoto;
                if (nodeGoto != null)
                {
                    lastSpeaker = null;
                    parentBlocks.Add(CreateLineBlock(nodeGoto.ID, "[GOTO] " + DescribeNode(nodeGoto.Goto), Color.Gray, false, false, false));
                    return;
                }

                DialogueNodeReturn nodeReturn = node as DialogueNodeReturn;
                if (nodeReturn != null)
                {
                    lastSpeaker = null;
                    parentBlocks.Add(CreateLineBlock(nodeReturn.ID, "[RETURN]", Color.FromArgb(130, 90, 45), true, false, false));
                    return;
                }

                DialogueNodeRoot root = node as DialogueNodeRoot;
                if (root != null)
                {
                    node = root.Next;
                    continue;
                }

                parentBlocks.Add(CreateLineBlock(DialogueNode.ID_NULL, "[UNSUPPORTED] " + node.GetType().Name, Color.Gray, false, true, false));
                return;
            }
        }

        private ScriptBlock CreateSentenceBlock(DialogueNodeSentence sentence, ref string lastSpeaker)
        {
            string speaker = ResolveSpeakerName(sentence);
            string text = GetLocalizedSentenceText(sentence);
            bool showSpeaker = lastSpeaker == null || !string.Equals(lastSpeaker, speaker, StringComparison.Ordinal);
            string prefix = showSpeaker ? (speaker + ": ") : string.Empty;

            ScriptBlock line = CreateLineBlock(sentence.ID, prefix + ToInlineText(text), Color.Black, showSpeaker, false, false);

            if (!string.IsNullOrWhiteSpace(sentence.Context))
            {
                line.Children.Add(CreateLineBlock(sentence.ID, "(Context) " + ToInlineText(sentence.Context), Color.FromArgb(95, 95, 95), false, true, false));
            }

            if (!string.IsNullOrWhiteSpace(sentence.Comment))
            {
                line.Children.Add(CreateLineBlock(sentence.ID, "(Comment) " + ToInlineText(sentence.Comment), Color.FromArgb(95, 95, 95), false, true, false));
            }

            if (!string.IsNullOrWhiteSpace(sentence.VoiceIntensity))
            {
                line.Children.Add(CreateLineBlock(sentence.ID, "(Intensity) " + ToInlineText(sentence.VoiceIntensity), Color.FromArgb(95, 95, 95), false, true, false));
            }

            lastSpeaker = speaker;
            return line;
        }

        private ScriptBlock CreateContainerBlock(int nodeID, string text, Color color)
        {
            ScriptBlock block = new ScriptBlock();
            block.NodeID = nodeID;
            block.IsContainer = true;
            block.Bold = true;
            block.Italic = false;
            block.IsTitle = false;
            block.Text = text ?? string.Empty;
            block.Color = color;
            return block;
        }

        private ScriptBlock CreateLineBlock(int nodeID, string text, Color color, bool bold, bool italic, bool isTitle)
        {
            ScriptBlock block = new ScriptBlock();
            block.NodeID = nodeID;
            block.IsContainer = false;
            block.Bold = bold;
            block.Italic = italic;
            block.IsTitle = isTitle;
            block.Text = text ?? string.Empty;
            block.Color = color;
            return block;
        }

        private string ResolveSpeakerName(DialogueNodeSentence sentence)
        {
            if (sentence == null || ResourcesHandler.Project == null)
                return "<Undefined>";

            string speaker = ResourcesHandler.Project.GetActorName(sentence.SpeakerID);
            if (string.IsNullOrWhiteSpace(speaker))
                return "<Undefined>";

            return speaker;
        }

        private string GetLocalizedSentenceText(DialogueNodeSentence sentence)
        {
            if (sentence == null)
                return string.Empty;

            string text = sentence.Sentence ?? string.Empty;
            Language language = EditorHelper.CurrentLanguage;

            if (language != null && language != EditorCore.LanguageWorkstring && Dialogue != null && Dialogue.Translations != null)
            {
                TranslationEntry entry = Dialogue.Translations.GetNodeEntry(sentence, language);
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Text))
                    text = entry.Text;
            }

            text = EditorHelper.FormatTextEntry(text, language);
            if (string.IsNullOrWhiteSpace(text))
                return "(empty)";

            return text;
        }

        private string DescribeNode(DialogueNode node)
        {
            if (node == null)
                return "null";

            DialogueNodeSentence sentence = node as DialogueNodeSentence;
            if (sentence != null)
                return ResolveSpeakerName(sentence) + ": " + ToInlineText(GetLocalizedSentenceText(sentence));

            DialogueNodeChoice choice = node as DialogueNodeChoice;
            if (choice != null)
                return "[CHOICE] " + ToInlineText(choice.Choice);

            DialogueNodeReply reply = node as DialogueNodeReply;
            if (reply != null)
                return "[REPLY] " + ToInlineText(reply.Reply);

            DialogueNodeBranch branch = node as DialogueNodeBranch;
            if (branch != null)
                return string.IsNullOrWhiteSpace(branch.Workstring) ? "[BRANCH]" : "[BRANCH] " + ToInlineText(branch.Workstring);

            DialogueNodeComment comment = node as DialogueNodeComment;
            if (comment != null)
                return "[COMMENT] " + ToInlineText(comment.Comment);

            DialogueNodeReturn nodeReturn = node as DialogueNodeReturn;
            if (nodeReturn != null)
                return "[RETURN]";

            return node.GetType().Name + " #" + node.ID;
        }

        private static string ToInlineText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "(empty)";

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalized.Split('\n');
            List<string> compact = new List<string>();
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                    compact.Add(trimmed);
            }

            if (compact.Count <= 0)
                return "(empty)";

            string result = string.Join(" / ", compact.ToArray());
            const int maxLength = 260;
            if (result.Length > maxLength)
                result = result.Substring(0, maxLength) + "...";

            return result;
        }
    }
}
