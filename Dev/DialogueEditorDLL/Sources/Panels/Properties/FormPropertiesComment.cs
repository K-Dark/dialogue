using System;
using System.Windows.Forms;

namespace DialogueEditor
{
    public partial class FormPropertiesComment : UserControl, IFormProperties
    {
        //--------------------------------------------------------------------------------------------------------------
        // Internal vars

        protected DocumentDialogue document;
        protected TreeNode treeNode;
        protected DialogueNodeComment dialogueNode;

        protected bool ready = false;

        //--------------------------------------------------------------------------------------------------------------
        // Class Methods

        public FormPropertiesComment()
        {
            InitializeComponent();

            Dock = DockStyle.Fill;
        }

        public void Clear()
        {
            ready = false;

            Dispose();
        }

        public void ForceFocus()
        {
            textBoxComment.Focus();
            textBoxComment.Select(textBoxComment.TextLength, 0);
        }

        public bool IsEditingWorkstring()
        {
            return textBoxComment.Focused;
        }

        public void ValidateEditedWorkstring()
        {
            document.RefreshTreeNodeForWorkstringValidation(treeNode);
            document.ResolvePendingDirty();
        }

        public void OnResolvePendingDirty()
        {
        }

        public void Init(DocumentDialogue inDocument, TreeNode inTreeNode, DialogueNode inDialogueNode)
        {
            document = inDocument;
            treeNode = inTreeNode;
            dialogueNode = inDialogueNode as DialogueNodeComment;

            textBoxComment.Text = dialogueNode.Comment;

            ready = true;
        }

        //--------------------------------------------------------------------------------------------------------------
        // Events

        private void OnCommentChanged(object sender, EventArgs e)
        {
            if (!ready)
                return;

            dialogueNode.Comment = textBoxComment.Text;

            document.RefreshTreeNodeForWorkstringEdit(treeNode);
            document.SetPendingDirty();
        }

        private void OnCommentValidated(object sender, EventArgs e)
        {
            ValidateEditedWorkstring();
        }
    }
}
