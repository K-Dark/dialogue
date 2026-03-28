using System.Windows;
using System.Windows.Media;

namespace DialogueEditor.WpfTree
{
    public sealed class WpfDialogueTreeRow
    {
        public const double IndentUnit = 14.0;

        public int NodeID { get; private set; }
        public string Text { get; private set; }
        public int Level { get; private set; }
        public bool IsDisplayRow { get; private set; }
        public bool HasChildren { get; private set; }
        public bool IsExpanded { get; private set; }
        public bool CanToggle { get; private set; }
        public Brush ForegroundBrush { get; private set; }
        public Brush BackgroundBrush { get; private set; }
        public FontWeight RowFontWeight { get; private set; }
        public FontStyle RowFontStyle { get; private set; }
        public double RowFontSize { get; private set; }
        public string RowFontFamilyName { get; private set; }
        public string TypeTagText { get; private set; }
        public Brush TypeTagBrush { get; private set; }
        public Brush AccentBrush { get; private set; }
        public Brush BranchBackgroundBrush { get; private set; }
        public Brush BranchBorderBrush { get; private set; }
        public Thickness BranchBorderThickness { get; private set; }
        public Brush BranchRailBrush1 { get; private set; }
        public Brush BranchRailBrush2 { get; private set; }
        public Brush BranchRailBrush3 { get; private set; }
        public Brush BranchRailBrush4 { get; private set; }
        public Thickness RowMargin { get; private set; }
        public Thickness RowPadding { get; private set; }
        public string SegmentIDText { get; private set; }
        public string SegmentAttributesText { get; private set; }
        public string SegmentActorsText { get; private set; }
        public string SegmentContentText { get; private set; }
        public Brush SegmentIDBrush { get; private set; }
        public Brush SegmentAttributesBrush { get; private set; }
        public Brush SegmentActorsBrush { get; private set; }
        public Brush SegmentContentBrush { get; private set; }

        public double IndentWidth
        {
            get
            {
                return Level * IndentUnit;
            }
        }

        public string ExpanderGlyph
        {
            get
            {
                if (!HasChildren)
                    return string.Empty;
                return IsExpanded ? "v" : ">";
            }
        }

        public WpfDialogueTreeRow(
            int nodeID,
            string text,
            int level,
            bool isDisplayRow,
            bool hasChildren,
            bool isExpanded,
            bool canToggle,
            Brush foregroundBrush,
            Brush backgroundBrush,
            FontWeight rowFontWeight,
            FontStyle rowFontStyle,
            double rowFontSize,
            string rowFontFamilyName,
            string typeTagText,
            Brush typeTagBrush,
            Brush accentBrush,
            Brush branchBackgroundBrush,
            Brush branchBorderBrush,
            Thickness branchBorderThickness,
            Brush branchRailBrush1,
            Brush branchRailBrush2,
            Brush branchRailBrush3,
            Brush branchRailBrush4,
            Thickness rowMargin,
            Thickness rowPadding,
            string segmentIDText,
            string segmentAttributesText,
            string segmentActorsText,
            string segmentContentText,
            Brush segmentIDBrush,
            Brush segmentAttributesBrush,
            Brush segmentActorsBrush,
            Brush segmentContentBrush)
        {
            NodeID = nodeID;
            Text = text ?? string.Empty;
            Level = level;
            IsDisplayRow = isDisplayRow;
            HasChildren = hasChildren;
            IsExpanded = isExpanded;
            CanToggle = canToggle;
            ForegroundBrush = foregroundBrush ?? Brushes.Black;
            BackgroundBrush = backgroundBrush ?? Brushes.Transparent;
            RowFontWeight = rowFontWeight;
            RowFontStyle = rowFontStyle;
            RowFontSize = rowFontSize;
            RowFontFamilyName = string.IsNullOrWhiteSpace(rowFontFamilyName) ? "Segoe UI" : rowFontFamilyName;
            TypeTagText = typeTagText ?? string.Empty;
            TypeTagBrush = typeTagBrush ?? Brushes.DimGray;
            AccentBrush = accentBrush ?? Brushes.Transparent;
            BranchBackgroundBrush = branchBackgroundBrush ?? Brushes.Transparent;
            BranchBorderBrush = branchBorderBrush ?? Brushes.Transparent;
            BranchBorderThickness = branchBorderThickness;
            BranchRailBrush1 = branchRailBrush1 ?? Brushes.Transparent;
            BranchRailBrush2 = branchRailBrush2 ?? Brushes.Transparent;
            BranchRailBrush3 = branchRailBrush3 ?? Brushes.Transparent;
            BranchRailBrush4 = branchRailBrush4 ?? Brushes.Transparent;
            RowMargin = rowMargin;
            RowPadding = rowPadding;
            SegmentIDText = segmentIDText ?? string.Empty;
            SegmentAttributesText = segmentAttributesText ?? string.Empty;
            SegmentActorsText = segmentActorsText ?? string.Empty;
            SegmentContentText = segmentContentText ?? string.Empty;
            SegmentIDBrush = segmentIDBrush ?? ForegroundBrush;
            SegmentAttributesBrush = segmentAttributesBrush ?? ForegroundBrush;
            SegmentActorsBrush = segmentActorsBrush ?? ForegroundBrush;
            SegmentContentBrush = segmentContentBrush ?? ForegroundBrush;
        }
    }
}
