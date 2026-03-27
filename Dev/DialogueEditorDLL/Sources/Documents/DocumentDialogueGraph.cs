using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace DialogueEditor
{
    public class DocumentDialogueGraph : DockContent, IDocument
    {
        private readonly GraphCanvas canvas;

        public Dialogue Dialogue { get; private set; }
        public event Action<DocumentDialogueGraph, DialogueNode> NodeSelected;

        public DocumentDialogueGraph(Dialogue dialogue)
        {
            ThemeManager.ApplyTheme(this);

            Dialogue = dialogue;
            canvas = new GraphCanvas(dialogue);
            canvas.Dock = DockStyle.Fill;
            canvas.NodeSelected += OnCanvasNodeSelected;
            Controls.Add(canvas);

            RefreshDocument();
            RefreshTitle();
        }

        public void RefreshDocument()
        {
            canvas.RebuildLayout();
        }

        public void RefreshTitle()
        {
            Text = Dialogue.GetName() + " [Graph]";
            if (ResourcesHandler.IsDirty(Dialogue))
            {
                Text += "*";
            }
        }

        public void SelectNode(DialogueNode node)
        {
            canvas.SelectNode(node != null ? node.ID : DialogueNode.ID_NULL, false);
        }

        private void OnCanvasNodeSelected(DialogueNode node)
        {
            NodeSelected?.Invoke(this, node);
        }

        private sealed class GraphCanvas : Panel
        {
            private sealed class VisualNode
            {
                public DialogueNode Node;
                public Rectangle Bounds;
            }

            private enum EdgeType
            {
                Next,
                Reply,
                Branch,
                Goto,
            }

            private struct VisualEdge
            {
                public int FromId;
                public int ToId;
                public EdgeType Type;
            }

            private struct LayoutConstraint
            {
                public int FromId;
                public int ToId;
            }

            private sealed class PointerResolution
            {
                public DialogueNode FlowTarget;
                public HashSet<int> GotoTargets = new HashSet<int>();
            }

            private readonly Dialogue dialogue;
            private readonly Dictionary<int, VisualNode> visualNodes = new Dictionary<int, VisualNode>();
            private readonly List<VisualEdge> visualEdges = new List<VisualEdge>();
            private readonly List<LayoutConstraint> layoutConstraints = new List<LayoutConstraint>();
            private readonly HashSet<int> visibleNodeIds = new HashSet<int>();

            private int selectedNodeId = DialogueNode.ID_NULL;
            private float zoom = 1.0f;
            private readonly float minZoom = 0.45f;
            private readonly float maxZoom = 2.4f;
            private Size worldGraphSize = Size.Empty;

            private bool isMouseDown = false;
            private bool isPanning = false;
            private MouseButtons panButton = MouseButtons.None;
            private Point panStartClient = Point.Empty;
            private Point panStartScroll = Point.Empty;
            private int pendingClickNodeId = DialogueNode.ID_NULL;

            public event Action<DialogueNode> NodeSelected;

            public GraphCanvas(Dialogue inDialogue)
            {
                dialogue = inDialogue;

                DoubleBuffered = true;
                AutoScroll = true;
                BackColor = ThemeManager.Current.WindowBackground;
            }

            public void RebuildLayout()
            {
                visualNodes.Clear();
                visualEdges.Clear();
                layoutConstraints.Clear();
                visibleNodeIds.Clear();

                if (dialogue == null || dialogue.ListNodes == null || dialogue.ListNodes.Count == 0)
                {
                    selectedNodeId = DialogueNode.ID_NULL;
                    Invalidate();
                    return;
                }

                const int nodeWidth = 220;
                int nodeHeight = 64;
                if (EditorCore.Settings.DisplayContext)
                    nodeHeight += 18;
                if (EditorCore.Settings.DisplayComments)
                    nodeHeight += 18;
                const int columnSpacing = 54;
                const int rowSpacing = 56;
                const int margin = 28;

                foreach (var node in dialogue.ListNodes)
                {
                    if (IsVisibleNode(node))
                        visibleNodeIds.Add(node.ID);
                }

                BuildEdges();
                BuildLayoutConstraints();

                var nodeLanes = new Dictionary<int, int>();
                var nodeRows = new Dictionary<int, int>();
                BuildInitialLayout(nodeLanes, nodeRows);
                ApplyLayoutConstraints(nodeRows);
                ResolveRowLaneCollisions(nodeLanes, nodeRows);

                var rows = new Dictionary<int, List<int>>();
                foreach (var nodeId in visibleNodeIds.OrderBy(item => item))
                {
                    int depth = nodeRows.ContainsKey(nodeId) ? nodeRows[nodeId] : 0;
                    if (!rows.ContainsKey(depth))
                    {
                        rows[depth] = new List<int>();
                    }
                    rows[depth].Add(nodeId);
                }

                int maxRight = 0;
                int maxBottom = 0;

                foreach (var row in rows.OrderBy(item => item.Key))
                {
                    int y = margin + row.Key * (nodeHeight + rowSpacing);
                    foreach (var nodeId in row.Value.OrderBy(id => nodeLanes.ContainsKey(id) ? nodeLanes[id] : int.MaxValue).ThenBy(id => id))
                    {
                        var node = dialogue.GetNodeByID(nodeId);
                        if (node == null)
                        {
                            continue;
                        }

                        int lane = nodeLanes.ContainsKey(nodeId) ? nodeLanes[nodeId] : 0;
                        int x = margin + lane * (nodeWidth + columnSpacing);
                        var bounds = new Rectangle(x, y, nodeWidth, nodeHeight);

                        visualNodes[nodeId] = new VisualNode
                        {
                            Node = node,
                            Bounds = bounds,
                        };

                        maxRight = Math.Max(maxRight, bounds.Right);
                        maxBottom = Math.Max(maxBottom, bounds.Bottom);
                    }
                }

                if (selectedNodeId != DialogueNode.ID_NULL && !visualNodes.ContainsKey(selectedNodeId))
                {
                    selectedNodeId = DialogueNode.ID_NULL;
                }

                worldGraphSize = new Size(maxRight + margin, maxBottom + margin);
                UpdateAutoScrollMinSize();
                Invalidate();
            }

            public void SelectNode(int nodeId, bool raiseEvent)
            {
                int resolved = ResolveSelectableNodeId(nodeId);
                if (resolved != DialogueNode.ID_NULL && !visualNodes.ContainsKey(resolved))
                {
                    return;
                }

                if (selectedNodeId == resolved)
                {
                    return;
                }

                selectedNodeId = resolved;
                Invalidate();

                if (raiseEvent)
                {
                    NodeSelected?.Invoke(dialogue.GetNodeByID(selectedNodeId));
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(BackColor);

                Point scroll = AutoScrollPosition;
                DrawEdges(e.Graphics, scroll);
                DrawNodes(e.Graphics, scroll);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);

                if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)
                {
                    return;
                }

                isMouseDown = true;
                isPanning = false;
                panButton = e.Button;
                panStartClient = e.Location;
                panStartScroll = GetScrollOffset();
                pendingClickNodeId = HitTestNodeId(e.Location);
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);

                if (!isMouseDown || (panButton != MouseButtons.Left && panButton != MouseButtons.Right))
                {
                    return;
                }

                int deltaX = e.X - panStartClient.X;
                int deltaY = e.Y - panStartClient.Y;

                if (!isPanning)
                {
                    const int moveThreshold = 4;
                    isPanning = Math.Abs(deltaX) >= moveThreshold || Math.Abs(deltaY) >= moveThreshold;
                }

                if (isPanning)
                {
                    SetScrollOffset(new Point(panStartScroll.X - deltaX, panStartScroll.Y - deltaY));
                }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);

                if (e.Button != panButton)
                {
                    return;
                }

                if (!isPanning && e.Button == MouseButtons.Left && pendingClickNodeId != DialogueNode.ID_NULL)
                {
                    SelectNode(pendingClickNodeId, true);
                }

                isMouseDown = false;
                isPanning = false;
                panButton = MouseButtons.None;
                pendingClickNodeId = DialogueNode.ID_NULL;
                Cursor = Cursors.Default;
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);

                if (!isMouseDown)
                {
                    Cursor = Cursors.Default;
                }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    float delta = e.Delta > 0 ? 0.10f : -0.10f;
                    UpdateZoom(zoom + delta, e.Location);
                    return;
                }

                base.OnMouseWheel(e);
            }

            private void BuildInitialLayout(Dictionary<int, int> nodeLanes, Dictionary<int, int> nodeRows)
            {
                var occupied = new HashSet<string>();

                if (dialogue.RootNode != null && IsVisibleNode(dialogue.RootNode))
                {
                    LayoutFlow(dialogue.RootNode, 0, 0, nodeLanes, nodeRows, occupied);
                }

                int laneOffset = nodeLanes.Count > 0 ? nodeLanes.Values.Max() + 1 : 0;
                foreach (var node in dialogue.ListNodes.Where(IsVisibleNode).OrderBy(item => item.ID))
                {
                    if (!nodeRows.ContainsKey(node.ID))
                    {
                        LayoutFlow(node, laneOffset, 0, nodeLanes, nodeRows, occupied);
                        laneOffset = nodeLanes.Values.Max() + 1;
                    }
                }
            }

            private int LayoutFlow(
                DialogueNode node,
                int requestedLane,
                int requestedRow,
                Dictionary<int, int> nodeLanes,
                Dictionary<int, int> nodeRows,
                HashSet<string> occupied)
            {
                if (!IsVisibleNode(node))
                {
                    return requestedRow - 1;
                }

                if (nodeRows.ContainsKey(node.ID))
                {
                    return nodeRows[node.ID];
                }

                int lane = ReserveLaneForRow(occupied, requestedLane, requestedRow);
                nodeLanes[node.ID] = lane;
                nodeRows[node.ID] = requestedRow;
                occupied.Add(GetGridKey(requestedRow, lane));

                int maxRow = requestedRow;

                if (node is DialogueNodeChoice choice)
                {
                    int continuationId = DialogueNode.ID_NULL;
                    var continuation = ResolvePointer(choice.Next).FlowTarget;
                    if (continuation != null)
                    {
                        continuationId = continuation.ID;
                    }

                    int maxReplyRow = requestedRow;
                    int replyOffset = 0;
                    foreach (var reply in choice.Replies)
                    {
                        if (!IsVisibleNode(reply))
                        {
                            continue;
                        }

                        int replyLane = lane + replyOffset;
                        int replyEndRow = LayoutFlow(reply, replyLane, requestedRow + 1, nodeLanes, nodeRows, occupied);
                        maxReplyRow = Math.Max(maxReplyRow, replyEndRow);
                        ++replyOffset;
                    }

                    maxRow = Math.Max(maxRow, maxReplyRow);
                    if (continuation != null && continuation.ID != continuationId)
                    {
                        continuation = dialogue.GetNodeByID(continuationId);
                    }

                    if (continuation != null)
                    {
                        int continuationEndRow = LayoutFlow(continuation, lane, maxReplyRow + 1, nodeLanes, nodeRows, occupied);
                        maxRow = Math.Max(maxRow, continuationEndRow);
                    }
                }
                else if (node is DialogueNodeBranch branch)
                {
                    var branchTarget = ResolvePointer(branch.Branch).FlowTarget;
                    if (branchTarget != null)
                    {
                        int endBranch = LayoutFlow(branchTarget, lane + 1, requestedRow + 1, nodeLanes, nodeRows, occupied);
                        maxRow = Math.Max(maxRow, endBranch);
                    }

                    var nextTarget = ResolvePointer(node.Next).FlowTarget;
                    if (nextTarget != null)
                    {
                        int endNext = LayoutFlow(nextTarget, lane, requestedRow + 1, nodeLanes, nodeRows, occupied);
                        maxRow = Math.Max(maxRow, endNext);
                    }
                }
                else
                {
                    var nextTarget = ResolvePointer(node.Next).FlowTarget;
                    if (nextTarget != null)
                    {
                        int endNext = LayoutFlow(nextTarget, lane, requestedRow + 1, nodeLanes, nodeRows, occupied);
                        maxRow = Math.Max(maxRow, endNext);
                    }
                }

                return maxRow;
            }

            private void BuildEdges()
            {
                var dedup = new HashSet<string>();

                foreach (var node in dialogue.ListNodes.Where(IsVisibleNode))
                {
                    int sourceId = node.ID;
                    var nextResolution = ResolvePointer(node.Next);

                    if (nextResolution.FlowTarget != null)
                    {
                        AddEdge(sourceId, nextResolution.FlowTarget.ID, EdgeType.Next, dedup);
                    }

                    foreach (var gotoTarget in nextResolution.GotoTargets)
                    {
                        AddEdge(sourceId, gotoTarget, EdgeType.Goto, dedup);
                    }

                    if (node is DialogueNodeChoice choice)
                    {
                        foreach (var reply in choice.Replies)
                        {
                            if (reply != null && IsVisibleNode(reply))
                            {
                                AddEdge(sourceId, reply.ID, EdgeType.Reply, dedup);
                            }
                        }
                    }
                    else if (node is DialogueNodeBranch branch)
                    {
                        var branchResolution = ResolvePointer(branch.Branch);
                        if (branchResolution.FlowTarget != null)
                        {
                            AddEdge(sourceId, branchResolution.FlowTarget.ID, EdgeType.Branch, dedup);
                        }

                        foreach (var gotoTarget in branchResolution.GotoTargets)
                        {
                            AddEdge(sourceId, gotoTarget, EdgeType.Goto, dedup);
                        }
                    }
                }
            }

            private void BuildLayoutConstraints()
            {
                var dedup = new HashSet<string>();

                foreach (var edge in visualEdges)
                {
                    AddLayoutConstraint(edge.FromId, edge.ToId, dedup);
                }

                foreach (var choice in dialogue.ListNodes.OfType<DialogueNodeChoice>())
                {
                    if (!IsVisibleNode(choice))
                    {
                        continue;
                    }

                    var continuation = ResolvePointer(choice.Next).FlowTarget;
                    if (continuation == null || !IsVisibleNode(continuation))
                    {
                        continue;
                    }

                    var replyNodes = CollectChoiceBranchNodes(choice, continuation.ID);
                    foreach (var replyNodeId in replyNodes)
                    {
                        AddLayoutConstraint(replyNodeId, continuation.ID, dedup);
                    }
                }
            }

            private void AddLayoutConstraint(int fromId, int toId, HashSet<string> dedup)
            {
                if (!visibleNodeIds.Contains(fromId) || !visibleNodeIds.Contains(toId))
                {
                    return;
                }

                if (fromId == toId)
                {
                    return;
                }

                string key = fromId.ToString() + ">" + toId.ToString();
                if (dedup.Contains(key))
                {
                    return;
                }

                dedup.Add(key);
                layoutConstraints.Add(new LayoutConstraint
                {
                    FromId = fromId,
                    ToId = toId,
                });
            }

            private HashSet<int> CollectChoiceBranchNodes(DialogueNodeChoice choice, int stopNodeId)
            {
                var result = new HashSet<int>();
                var visited = new HashSet<int>();
                var queue = new Queue<DialogueNode>();

                foreach (var reply in choice.Replies)
                {
                    if (IsVisibleNode(reply))
                    {
                        queue.Enqueue(reply);
                    }
                }

                while (queue.Count > 0)
                {
                    var node = queue.Dequeue();
                    if (!IsVisibleNode(node))
                    {
                        continue;
                    }

                    if (!visited.Add(node.ID))
                    {
                        continue;
                    }

                    if (node.ID == stopNodeId)
                    {
                        continue;
                    }

                    result.Add(node.ID);
                    foreach (var child in EnumerateVisibleFlowChildren(node))
                    {
                        if (child != null && child.ID != stopNodeId)
                        {
                            queue.Enqueue(child);
                        }
                    }
                }

                return result;
            }

            private IEnumerable<DialogueNode> EnumerateVisibleFlowChildren(DialogueNode node)
            {
                var nextTarget = ResolvePointer(node.Next).FlowTarget;
                if (IsVisibleNode(nextTarget))
                {
                    yield return nextTarget;
                }

                if (node is DialogueNodeChoice choice)
                {
                    foreach (var reply in choice.Replies)
                    {
                        if (IsVisibleNode(reply))
                        {
                            yield return reply;
                        }
                    }
                }
                else if (node is DialogueNodeBranch branch)
                {
                    var branchTarget = ResolvePointer(branch.Branch).FlowTarget;
                    if (IsVisibleNode(branchTarget))
                    {
                        yield return branchTarget;
                    }
                }
            }

            private void ApplyLayoutConstraints(Dictionary<int, int> nodeRows)
            {
                int maxIterations = Math.Max(1, visibleNodeIds.Count * 4);
                int maxDepthCap = Math.Max(8, visibleNodeIds.Count * 8);

                for (int i = 0; i < maxIterations; ++i)
                {
                    bool changed = false;

                    foreach (var constraint in layoutConstraints)
                    {
                        if (!nodeRows.ContainsKey(constraint.FromId) || !nodeRows.ContainsKey(constraint.ToId))
                        {
                            continue;
                        }

                        int desiredRow = Math.Min(maxDepthCap, nodeRows[constraint.FromId] + 1);
                        if (desiredRow > nodeRows[constraint.ToId])
                        {
                            nodeRows[constraint.ToId] = desiredRow;
                            changed = true;
                        }
                    }

                    if (!changed)
                    {
                        break;
                    }
                }
            }

            private void ResolveRowLaneCollisions(Dictionary<int, int> nodeLanes, Dictionary<int, int> nodeRows)
            {
                var occupied = new HashSet<string>();
                foreach (var nodeId in visibleNodeIds.OrderBy(id => nodeRows.ContainsKey(id) ? nodeRows[id] : 0).ThenBy(id => nodeLanes.ContainsKey(id) ? nodeLanes[id] : 0).ThenBy(id => id))
                {
                    int lane = nodeLanes.ContainsKey(nodeId) ? nodeLanes[nodeId] : 0;
                    int row = nodeRows.ContainsKey(nodeId) ? nodeRows[nodeId] : 0;

                    while (occupied.Contains(GetGridKey(row, lane)))
                    {
                        ++row;
                    }

                    nodeRows[nodeId] = row;
                    occupied.Add(GetGridKey(row, lane));
                }
            }

            private static int ReserveLaneForRow(HashSet<string> occupied, int requestedLane, int row)
            {
                int lane = requestedLane;
                while (occupied.Contains(GetGridKey(row, lane)))
                {
                    ++lane;
                }
                return lane;
            }

            private static string GetGridKey(int row, int lane)
            {
                return row.ToString() + "|" + lane.ToString();
            }

            private void AddEdge(int fromId, int toId, EdgeType type, HashSet<string> dedup)
            {
                if (!visibleNodeIds.Contains(fromId) || !visibleNodeIds.Contains(toId))
                    return;

                if (fromId == toId)
                    return;

                string key = fromId.ToString() + "|" + toId.ToString() + "|" + (int)type;
                if (dedup.Contains(key))
                    return;

                dedup.Add(key);
                visualEdges.Add(new VisualEdge
                {
                    FromId = fromId,
                    ToId = toId,
                    Type = type,
                });
            }

            private PointerResolution ResolvePointer(DialogueNode pointerNode)
            {
                var resolution = new PointerResolution();
                var visitedGotos = new HashSet<int>();
                DialogueNode current = pointerNode;
                int guard = 0;

                while (current is DialogueNodeGoto)
                {
                    var nodeGoto = current as DialogueNodeGoto;

                    if (!visitedGotos.Add(nodeGoto.ID))
                    {
                        break;
                    }

                    DialogueNode visibleGotoTarget = ResolveVisibleTargetFromGoto(nodeGoto.Goto);
                    if (visibleGotoTarget != null)
                    {
                        resolution.GotoTargets.Add(visibleGotoTarget.ID);
                    }

                    current = nodeGoto.Next;
                    ++guard;
                    if (guard > dialogue.ListNodes.Count + 4)
                    {
                        break;
                    }
                }

                if (current != null && IsVisibleNode(current))
                {
                    resolution.FlowTarget = current;
                }

                return resolution;
            }

            private DialogueNode ResolveVisibleTargetFromGoto(DialogueNode target)
            {
                var visitedGotos = new HashSet<int>();
                DialogueNode current = target;
                int guard = 0;

                while (current is DialogueNodeGoto)
                {
                    var nodeGoto = current as DialogueNodeGoto;
                    if (!visitedGotos.Add(nodeGoto.ID))
                    {
                        return null;
                    }

                    current = nodeGoto.Goto != null ? nodeGoto.Goto : nodeGoto.Next;
                    ++guard;
                    if (guard > dialogue.ListNodes.Count + 4)
                    {
                        return null;
                    }
                }

                if (current != null && IsVisibleNode(current))
                {
                    return current;
                }

                return null;
            }

            private bool IsVisibleNode(DialogueNode node)
            {
                return node != null && !(node is DialogueNodeGoto);
            }

            private int ResolveSelectableNodeId(int nodeId)
            {
                if (nodeId == DialogueNode.ID_NULL)
                    return DialogueNode.ID_NULL;

                DialogueNode node = dialogue.GetNodeByID(nodeId);
                if (IsVisibleNode(node))
                    return node.ID;

                if (node is DialogueNodeGoto nodeGoto)
                {
                    DialogueNode target = ResolveVisibleTargetFromGoto(nodeGoto.Goto);
                    if (target != null)
                        return target.ID;
                }

                return DialogueNode.ID_NULL;
            }

            private void DrawEdges(Graphics graphics, Point scroll)
            {
                foreach (var edge in visualEdges)
                {
                    if (!visualNodes.ContainsKey(edge.FromId) || !visualNodes.ContainsKey(edge.ToId))
                    {
                        continue;
                    }

                    Rectangle fromRect = visualNodes[edge.FromId].Bounds;
                    Rectangle toRect = visualNodes[edge.ToId].Bounds;

                    var fromWorld = new Point(fromRect.Left + fromRect.Width / 2, fromRect.Bottom);
                    var toWorld = new Point(toRect.Left + toRect.Width / 2, toRect.Top);
                    var from = WorldToScreen(fromWorld, scroll);
                    var to = WorldToScreen(toWorld, scroll);

                    var color = Color.FromArgb(103, 129, 162);
                    var width = Math.Max(1f, 1.8f * zoom);
                    var dash = DashStyle.Solid;

                    switch (edge.Type)
                    {
                        case EdgeType.Reply:
                            color = Color.FromArgb(171, 95, 95);
                            break;
                        case EdgeType.Branch:
                            color = Color.FromArgb(174, 122, 69);
                            break;
                        case EdgeType.Goto:
                            color = Color.FromArgb(128, 128, 128);
                            dash = DashStyle.Dash;
                            break;
                    }

                    using (var pen = new Pen(color, width))
                    using (var arrow = new AdjustableArrowCap(4, 6))
                    {
                        pen.DashStyle = dash;
                        pen.CustomEndCap = arrow;

                        if (edge.Type == EdgeType.Goto)
                        {
                            int bend = (int)(48 * zoom);
                            var cp1 = new Point(from.X + bend, from.Y + (int)(18 * zoom));
                            var cp2 = new Point(to.X + bend, to.Y - (int)(18 * zoom));
                            graphics.DrawBezier(pen, from, cp1, cp2, to);
                        }
                        else
                        {
                            graphics.DrawLine(pen, from, to);
                        }
                    }
                }
            }

            private void DrawNodes(Graphics graphics, Point scroll)
            {
                float fontSize = Math.Max(7f, ThemeManager.Current.UiFontSmall.Size * zoom);
                using (var baseFont = new Font(ThemeManager.Current.UiFontSmall.FontFamily, fontSize, FontStyle.Regular, GraphicsUnit.Point))
                using (var commentFont = new Font(ThemeManager.Current.UiFontSmall.FontFamily, fontSize, FontStyle.Italic, GraphicsUnit.Point))
                {
                    foreach (var visualNode in visualNodes.Values)
                    {
                        Rectangle bounds = ScaleRect(visualNode.Bounds, scroll);

                        bool selected = visualNode.Node.ID == selectedNodeId;

                        var fill = GetNodeFillColor(visualNode.Node);
                        var border = selected ? ThemeManager.Current.SelectionBackground : ThemeManager.Current.ControlBorder;
                        var borderWidth = selected ? Math.Max(2f, 2.6f * zoom) : Math.Max(1f, 1.1f * zoom);

                        using (var fillBrush = new SolidBrush(fill))
                        using (var borderPen = new Pen(border, borderWidth))
                        {
                            graphics.FillRectangle(fillBrush, bounds);
                            graphics.DrawRectangle(borderPen, bounds);
                        }

                        int padX = Math.Max(6, (int)(8 * zoom));
                        int padY = Math.Max(5, (int)(7 * zoom));
                        var textBounds = new Rectangle(bounds.X + padX, bounds.Y + padY, Math.Max(16, bounds.Width - padX * 2), Math.Max(16, bounds.Height - padY * 2));
                        Font nodeFont = visualNode.Node is DialogueNodeComment ? commentFont : baseFont;
                        Color textColor = visualNode.Node is DialogueNodeComment ? Color.DimGray : Color.Black;
                        TextRenderer.DrawText(
                            graphics,
                            BuildNodeLabel(visualNode.Node),
                            nodeFont,
                            textBounds,
                            textColor,
                            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak
                        );
                    }
                }
            }

            private void UpdateAutoScrollMinSize()
            {
                if (worldGraphSize.Width <= 0 || worldGraphSize.Height <= 0)
                {
                    AutoScrollMinSize = Size.Empty;
                    return;
                }

                int width = Math.Max(1, (int)Math.Ceiling(worldGraphSize.Width * zoom));
                int height = Math.Max(1, (int)Math.Ceiling(worldGraphSize.Height * zoom));
                AutoScrollMinSize = new Size(width, height);
            }

            private void UpdateZoom(float requestedZoom, Point cursorClient)
            {
                float newZoom = Math.Max(minZoom, Math.Min(maxZoom, requestedZoom));
                if (Math.Abs(newZoom - zoom) < 0.001f)
                {
                    return;
                }

                Point oldScroll = GetScrollOffset();
                float worldX = (cursorClient.X + oldScroll.X) / zoom;
                float worldY = (cursorClient.Y + oldScroll.Y) / zoom;

                zoom = newZoom;
                UpdateAutoScrollMinSize();

                int newScrollX = (int)Math.Round(worldX * zoom - cursorClient.X);
                int newScrollY = (int)Math.Round(worldY * zoom - cursorClient.Y);
                SetScrollOffset(new Point(newScrollX, newScrollY));

                Invalidate();
            }

            private Point GetScrollOffset()
            {
                Point scroll = AutoScrollPosition;
                return new Point(-scroll.X, -scroll.Y);
            }

            private void SetScrollOffset(Point offset)
            {
                int maxX = Math.Max(0, AutoScrollMinSize.Width - ClientSize.Width);
                int maxY = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height);

                int x = Math.Max(0, Math.Min(maxX, offset.X));
                int y = Math.Max(0, Math.Min(maxY, offset.Y));

                AutoScrollPosition = new Point(x, y);
            }

            private int HitTestNodeId(Point clientPoint)
            {
                Point scroll = AutoScrollPosition;
                foreach (var visualNode in visualNodes.Values)
                {
                    Rectangle screenBounds = ScaleRect(visualNode.Bounds, scroll);
                    if (screenBounds.Contains(clientPoint))
                    {
                        return visualNode.Node.ID;
                    }
                }

                return DialogueNode.ID_NULL;
            }

            private Rectangle ScaleRect(Rectangle worldRect, Point scroll)
            {
                int x = (int)Math.Round(worldRect.X * zoom) + scroll.X;
                int y = (int)Math.Round(worldRect.Y * zoom) + scroll.Y;
                int w = Math.Max(8, (int)Math.Round(worldRect.Width * zoom));
                int h = Math.Max(8, (int)Math.Round(worldRect.Height * zoom));
                return new Rectangle(x, y, w, h);
            }

            private Point WorldToScreen(Point worldPoint, Point scroll)
            {
                int x = (int)Math.Round(worldPoint.X * zoom) + scroll.X;
                int y = (int)Math.Round(worldPoint.Y * zoom) + scroll.Y;
                return new Point(x, y);
            }

            private string BuildNodeLabel(DialogueNode node)
            {
                if (node is DialogueNodeRoot)
                {
                    return AppendDisplayNotes($"[{node.ID}] Root", dialogue != null ? dialogue.Context : null, dialogue != null ? dialogue.Comment : null);
                }

                if (node is DialogueNodeSentence sentence)
                {
                    return AppendDisplayNotes($"[{node.ID}] Sentence\n{sentence.Sentence}", sentence.Context, sentence.Comment);
                }

                if (node is DialogueNodeChoice choice)
                {
                    return $"[{node.ID}] Choice\n{choice.Choice}";
                }

                if (node is DialogueNodeReply reply)
                {
                    return $"[{node.ID}] Reply\n{reply.Reply}";
                }

                if (node is DialogueNodeBranch branch)
                {
                    return $"[{node.ID}] Branch\n{branch.Workstring}";
                }

                if (node is DialogueNodeReturn)
                {
                    return $"[{node.ID}] Return";
                }

                if (node is DialogueNodeComment comment)
                {
                    string text = "Comment";
                    if (EditorCore.Settings.DisplayComments && !string.IsNullOrWhiteSpace(comment.Comment))
                        text = comment.Comment;
                    return $"[{node.ID}] Comment\n{text}";
                }

                return $"[{node.ID}] Node";
            }

            private static Color GetNodeFillColor(DialogueNode node)
            {
                if (node is DialogueNodeRoot)
                {
                    return Color.FromArgb(238, 242, 248);
                }

                if (node is DialogueNodeSentence)
                {
                    return Color.FromArgb(243, 250, 255);
                }

                if (node is DialogueNodeChoice || node is DialogueNodeBranch || node is DialogueNodeReturn)
                {
                    return Color.FromArgb(255, 246, 235);
                }

                if (node is DialogueNodeReply)
                {
                    return Color.FromArgb(255, 240, 240);
                }

                if (node is DialogueNodeComment)
                {
                    return Color.FromArgb(238, 238, 238);
                }

                return Color.White;
            }

            private string AppendDisplayNotes(string text, string context, string comment)
            {
                StringBuilder builder = new StringBuilder(text);

                if (EditorCore.Settings.DisplayContext)
                {
                    string contextText = FormatDisplayNote(context);
                    if (!string.IsNullOrEmpty(contextText))
                        builder.AppendFormat("\n(Context) {0}", contextText);
                }

                if (EditorCore.Settings.DisplayComments)
                {
                    string commentText = FormatDisplayNote(comment);
                    if (!string.IsNullOrEmpty(commentText))
                        builder.AppendFormat("\n(Comment) {0}", commentText);
                }

                return builder.ToString();
            }

            private static string FormatDisplayNote(string note)
            {
                if (string.IsNullOrWhiteSpace(note))
                    return "";

                string formatted = note.Replace("\r\n", " | ").Replace("\n", " | ").Trim();
                const int maxLength = 160;
                if (formatted.Length > maxLength)
                    formatted = formatted.Substring(0, maxLength) + "...";
                return formatted;
            }
        }
    }
}
