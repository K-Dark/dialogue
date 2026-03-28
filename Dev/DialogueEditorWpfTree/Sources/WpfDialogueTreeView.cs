using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace DialogueEditor.WpfTree
{
    public sealed class WpfDialogueTreeView : UserControl
    {
        private readonly ObservableCollection<WpfDialogueTreeRow> rows = new ObservableCollection<WpfDialogueTreeRow>();
        private readonly ListBox listBox = new ListBox();

        private const double RowLeftPadding = 4.0;
        private const double ExpanderHitWidth = 14.0;

        private bool lockSelectionChange;
        private WpfDialogueTreeRow dragSourceRow;
        private Point dragStartPosition;

        public event EventHandler<int> NodeSelected;
        public event EventHandler<int> NodeExpansionToggled;
        public event EventHandler<WpfDialogueTreeMouseEventArgs> NodeContextMenuRequested;
        public event EventHandler<WpfDialogueTreeDragDropEventArgs> NodeDragDropRequested;
        public event EventHandler<KeyEventArgs> NodeKeyDown;

        public WpfDialogueTreeView()
        {
            listBox.BorderThickness = new Thickness(0);
            listBox.Background = Brushes.Transparent;
            listBox.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            listBox.AllowDrop = true;
            listBox.ItemsSource = rows;
            listBox.SelectionChanged += OnSelectionChanged;
            listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            listBox.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
            listBox.PreviewMouseRightButtonDown += OnPreviewMouseRightButtonDown;
            listBox.PreviewMouseMove += OnPreviewMouseMove;
            listBox.DragOver += OnDragOver;
            listBox.Drop += OnDrop;
            listBox.PreviewKeyDown += OnPreviewKeyDown;

            // This keeps scrolling smooth on large trees.
            listBox.SetValue(ScrollViewer.CanContentScrollProperty, true);
            listBox.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, true);
            listBox.SetValue(VirtualizingStackPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);

            listBox.ItemTemplate = BuildItemTemplate();
            listBox.ItemContainerStyle = BuildItemContainerStyle();
            Content = listBox;
        }

        public void SetRows(IList<WpfDialogueTreeRow> newRows, int selectedNodeID)
        {
            lockSelectionChange = true;

            rows.Clear();
            if (newRows != null)
            {
                for (int i = 0; i < newRows.Count; ++i)
                    rows.Add(newRows[i]);
            }

            SetSelectedNode_Internal(selectedNodeID, true);

            lockSelectionChange = false;
        }

        public void SetSelectedNode(int selectedNodeID)
        {
            lockSelectionChange = true;
            SetSelectedNode_Internal(selectedNodeID, false);
            lockSelectionChange = false;
        }

        public bool IsTreeFocused
        {
            get
            {
                return listBox.IsKeyboardFocusWithin;
            }
        }

        public void FocusTree()
        {
            listBox.Focus();
        }

        private void SetSelectedNode_Internal(int selectedNodeID, bool forceScroll)
        {
            WpfDialogueTreeRow selectedRow = null;
            for (int i = 0; i < rows.Count; ++i)
            {
                WpfDialogueTreeRow candidate = rows[i];
                if (!candidate.IsDisplayRow && candidate.NodeID == selectedNodeID)
                {
                    selectedRow = candidate;
                    break;
                }
            }

            listBox.SelectedItem = selectedRow;
            if (forceScroll && selectedRow != null)
                listBox.ScrollIntoView(selectedRow);
        }

        private Style BuildItemContainerStyle()
        {
            Style style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            return style;
        }

        private DataTemplate BuildItemTemplate()
        {
            DataTemplate template = new DataTemplate(typeof(WpfDialogueTreeRow));

            FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetBinding(Border.MarginProperty, new Binding("RowMargin"));
            borderFactory.SetBinding(Border.PaddingProperty, new Binding("RowPadding"));
            borderFactory.SetBinding(Border.BackgroundProperty, new Binding("BackgroundBrush"));

            FrameworkElementFactory panelFactory = new FrameworkElementFactory(typeof(StackPanel));
            panelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            FrameworkElementFactory indentFactory = new FrameworkElementFactory(typeof(Border));
            indentFactory.SetBinding(FrameworkElement.WidthProperty, new Binding("IndentWidth"));
            panelFactory.AppendChild(indentFactory);

            FrameworkElementFactory expanderFactory = new FrameworkElementFactory(typeof(TextBlock));
            expanderFactory.SetValue(FrameworkElement.WidthProperty, ExpanderHitWidth);
            expanderFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            expanderFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            expanderFactory.SetBinding(TextBlock.TextProperty, new Binding("ExpanderGlyph"));
            expanderFactory.SetBinding(TextBlock.ForegroundProperty, new Binding("ForegroundBrush"));
            expanderFactory.SetBinding(TextBlock.FontWeightProperty, new Binding("RowFontWeight"));
            expanderFactory.SetBinding(TextBlock.FontStyleProperty, new Binding("RowFontStyle"));
            expanderFactory.SetBinding(TextBlock.FontSizeProperty, new Binding("RowFontSize"));
            expanderFactory.SetBinding(TextBlock.FontFamilyProperty, new Binding("RowFontFamilyName"));
            panelFactory.AppendChild(expanderFactory);

            panelFactory.AppendChild(CreateSegmentTextBlock("SegmentIDText", "SegmentIDBrush"));
            panelFactory.AppendChild(CreateSegmentTextBlock("SegmentAttributesText", "SegmentAttributesBrush"));
            panelFactory.AppendChild(CreateSegmentTextBlock("SegmentActorsText", "SegmentActorsBrush"));
            panelFactory.AppendChild(CreateSegmentTextBlock("SegmentContentText", "SegmentContentBrush", true));

            borderFactory.AppendChild(panelFactory);
            template.VisualTree = borderFactory;

            return template;
        }

        private FrameworkElementFactory CreateSegmentTextBlock(string textBindingPath, string brushBindingPath, bool trimText = false)
        {
            FrameworkElementFactory textFactory = new FrameworkElementFactory(typeof(TextBlock));
            if (trimText)
                textFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

            textFactory.SetBinding(TextBlock.TextProperty, new Binding(textBindingPath));
            textFactory.SetBinding(TextBlock.ForegroundProperty, new Binding(brushBindingPath));
            textFactory.SetBinding(TextBlock.FontWeightProperty, new Binding("RowFontWeight"));
            textFactory.SetBinding(TextBlock.FontStyleProperty, new Binding("RowFontStyle"));
            textFactory.SetBinding(TextBlock.FontSizeProperty, new Binding("RowFontSize"));
            textFactory.SetBinding(TextBlock.FontFamilyProperty, new Binding("RowFontFamilyName"));
            return textFactory;
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            dragSourceRow = null;

            ListBoxItem item = GetItemFromEventSource(e.OriginalSource);
            if (item == null)
                return;

            WpfDialogueTreeRow row = item.Content as WpfDialogueTreeRow;
            if (row == null)
                return;

            listBox.Focus();

            if (listBox.SelectedItem != row)
                listBox.SelectedItem = row;

            if (row.HasChildren)
            {
                Point click = e.GetPosition(item);
                double expanderStartX = RowLeftPadding + row.IndentWidth;
                bool hitExpanderZone = click.X >= expanderStartX && click.X <= expanderStartX + ExpanderHitWidth;
                if (hitExpanderZone)
                {
                    if (row.CanToggle)
                        NodeExpansionToggled?.Invoke(this, row.NodeID);

                    e.Handled = true;
                    return;
                }
            }

            dragSourceRow = row;
            dragStartPosition = e.GetPosition(listBox);
        }

        private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            dragSourceRow = null;
        }

        private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            dragSourceRow = null;

            ListBoxItem item = GetItemFromEventSource(e.OriginalSource);
            if (item == null)
                return;

            WpfDialogueTreeRow row = item.Content as WpfDialogueTreeRow;
            if (row == null)
                return;

            listBox.Focus();

            if (listBox.SelectedItem != row)
                listBox.SelectedItem = row;

            NodeContextMenuRequested?.Invoke(this, new WpfDialogueTreeMouseEventArgs(row.NodeID, e.GetPosition(this)));
            e.Handled = true;
        }

        private void OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragSourceRow == null)
                return;

            Point currentPosition = e.GetPosition(listBox);
            if (Math.Abs(currentPosition.X - dragStartPosition.X) < SystemParameters.MinimumHorizontalDragDistance
             && Math.Abs(currentPosition.Y - dragStartPosition.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            int sourceNodeID = dragSourceRow.NodeID;
            dragSourceRow = null;

            DataObject dragData = new DataObject(typeof(int), sourceNodeID);
            DragDrop.DoDragDrop(listBox, dragData, DragDropEffects.Move);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            int sourceNodeID;
            if (!TryGetDraggedNodeID(e.Data, out sourceNodeID))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            WpfDialogueTreeRow targetRow = GetRowFromPoint(e.GetPosition(listBox));
            e.Effects = (targetRow != null && targetRow.NodeID != sourceNodeID)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            int sourceNodeID;
            if (!TryGetDraggedNodeID(e.Data, out sourceNodeID))
            {
                e.Handled = true;
                return;
            }

            WpfDialogueTreeRow targetRow = GetRowFromPoint(e.GetPosition(listBox));
            if (targetRow == null || targetRow.NodeID == sourceNodeID)
            {
                e.Handled = true;
                return;
            }

            NodeDragDropRequested?.Invoke(this, new WpfDialogueTreeDragDropEventArgs(sourceNodeID, targetRow.NodeID));
            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            NodeKeyDown?.Invoke(this, e);
        }

        private bool TryGetDraggedNodeID(IDataObject dataObject, out int nodeID)
        {
            nodeID = -1;
            if (dataObject == null || !dataObject.GetDataPresent(typeof(int)))
                return false;

            object data = dataObject.GetData(typeof(int));
            if (!(data is int))
                return false;

            nodeID = (int)data;
            return true;
        }

        private ListBoxItem GetItemFromEventSource(object originalSource)
        {
            DependencyObject source = originalSource as DependencyObject;
            if (source == null)
                return null;

            return ItemsControl.ContainerFromElement(listBox, source) as ListBoxItem;
        }

        private WpfDialogueTreeRow GetRowFromPoint(Point point)
        {
            DependencyObject hit = listBox.InputHitTest(point) as DependencyObject;
            if (hit == null)
                return null;

            ListBoxItem item = ItemsControl.ContainerFromElement(listBox, hit) as ListBoxItem;
            return item?.Content as WpfDialogueTreeRow;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lockSelectionChange)
                return;

            WpfDialogueTreeRow row = listBox.SelectedItem as WpfDialogueTreeRow;
            if (row != null)
                NodeSelected?.Invoke(this, row.NodeID);
        }
    }
}
