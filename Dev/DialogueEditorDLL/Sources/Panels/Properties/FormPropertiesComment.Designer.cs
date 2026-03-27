namespace DialogueEditor
{
    partial class FormPropertiesComment
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.Windows.Forms.GroupBox groupBox1;
            System.Windows.Forms.Label label1;
            System.Windows.Forms.Label label2;
            this.textBoxComment = new System.Windows.Forms.RichTextBox();
            groupBox1 = new System.Windows.Forms.GroupBox();
            label1 = new System.Windows.Forms.Label();
            label2 = new System.Windows.Forms.Label();
            groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // groupBox1
            // 
            groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            groupBox1.Controls.Add(this.textBoxComment);
            groupBox1.Controls.Add(label1);
            groupBox1.Controls.Add(label2);
            groupBox1.Location = new System.Drawing.Point(3, 3);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new System.Drawing.Size(321, 137);
            groupBox1.TabIndex = 0;
            groupBox1.TabStop = false;
            groupBox1.Text = "Comment Properties";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(6, 26);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(57, 13);
            label1.TabIndex = 0;
            label1.Text = "Comment :";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(6, 112);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(104, 13);
            label2.TabIndex = 2;
            label2.Text = "(Will not be voiced)";
            // 
            // textBoxComment
            // 
            this.textBoxComment.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.textBoxComment.Location = new System.Drawing.Point(6, 42);
            this.textBoxComment.Name = "textBoxComment";
            this.textBoxComment.Size = new System.Drawing.Size(309, 67);
            this.textBoxComment.TabIndex = 1;
            this.textBoxComment.Text = "";
            this.textBoxComment.TextChanged += new System.EventHandler(this.OnCommentChanged);
            this.textBoxComment.Validated += new System.EventHandler(this.OnCommentValidated);
            // 
            // FormPropertiesComment
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(groupBox1);
            this.Name = "FormPropertiesComment";
            this.Size = new System.Drawing.Size(327, 144);
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.RichTextBox textBoxComment;
    }
}
