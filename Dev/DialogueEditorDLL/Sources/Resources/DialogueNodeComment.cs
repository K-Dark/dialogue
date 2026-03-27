namespace DialogueEditor
{
    public class DialogueNodeComment : DialogueNode
    {
        //--------------------------------------------------------------------------------------------------------------
        // Serialized vars

        public string Comment { get; set; }

        //--------------------------------------------------------------------------------------------------------------
        // Class Methods

        public DialogueNodeComment()
        {
            Comment = "";
        }

        public DialogueNodeComment(DialogueNodeComment other)
            : base(other)
        {
            Comment = other.Comment;
        }

        public override object Clone()
        {
            return new DialogueNodeComment(this);
        }
    }
}
