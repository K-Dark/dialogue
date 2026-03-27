using System.Drawing;
using System.Windows.Forms;

namespace DialogueEditor
{
    public sealed class ThemePalette
    {
        public readonly Color WindowBackground = Color.FromArgb(246, 247, 249);
        public readonly Color PanelBackground = Color.White;
        public readonly Color ControlBackground = Color.White;
        public readonly Color ControlBorder = Color.FromArgb(209, 214, 222);
        public readonly Color SelectionBackground = Color.FromArgb(209, 228, 255);
        public readonly Color SelectionText = Color.Black;
        public readonly Font UiFont = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point);
        public readonly Font UiFontSmall = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
    }

    public static class ThemeManager
    {
        public static readonly ThemePalette Current = new ThemePalette();

        public static void ApplyTheme(Control root)
        {
            if (root == null)
            {
                return;
            }

            ApplyThemeRecursive(root);
        }

        private static void ApplyThemeRecursive(Control control)
        {
            if (control == null)
            {
                return;
            }

            if (control is Form)
            {
                control.BackColor = Current.WindowBackground;
                control.Font = Current.UiFont;
            }
            else if (control is TreeView treeView)
            {
                treeView.BackColor = Current.ControlBackground;
                treeView.Font = Current.UiFont;
            }
            else if (control is ListBox listBox)
            {
                listBox.BackColor = Current.ControlBackground;
                listBox.Font = Current.UiFont;
            }
            else if (control is RichTextBox richTextBox)
            {
                richTextBox.BackColor = Current.ControlBackground;
                richTextBox.BorderStyle = BorderStyle.FixedSingle;
                richTextBox.Font = Current.UiFont;
            }
            else if (control is TextBox textBox)
            {
                textBox.BackColor = Current.ControlBackground;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.Font = Current.UiFont;
            }
            else if (control is ComboBox comboBox)
            {
                comboBox.BackColor = Current.ControlBackground;
                comboBox.Font = Current.UiFont;
            }
            else if (control is CheckBox checkBox)
            {
                checkBox.BackColor = Color.Transparent;
                checkBox.Font = Current.UiFontSmall;
            }
            else if (control is Button button)
            {
                button.Font = Current.UiFont;
            }
            else if (control is GroupBox groupBox)
            {
                groupBox.BackColor = Current.PanelBackground;
                groupBox.Font = Current.UiFont;
            }
            else if (control is Panel panel)
            {
                panel.BackColor = Current.PanelBackground;
            }
            else if (control is TabControl tabControl)
            {
                tabControl.Font = Current.UiFont;
            }
            else if (control is TabPage tabPage)
            {
                tabPage.BackColor = Current.PanelBackground;
                tabPage.Font = Current.UiFont;
            }
            else if (control is Label label)
            {
                label.Font = Current.UiFont;
            }

            foreach (Control child in control.Controls)
            {
                ApplyThemeRecursive(child);
            }

            if (control is MenuStrip menuStrip)
            {
                menuStrip.BackColor = Current.PanelBackground;
                menuStrip.Font = Current.UiFont;
            }
            else if (control is StatusStrip statusStrip)
            {
                statusStrip.BackColor = Current.PanelBackground;
                statusStrip.Font = Current.UiFontSmall;
            }
            else if (control is ToolStrip toolStrip)
            {
                toolStrip.BackColor = Current.PanelBackground;
                toolStrip.Font = Current.UiFont;
            }
        }
    }
}
