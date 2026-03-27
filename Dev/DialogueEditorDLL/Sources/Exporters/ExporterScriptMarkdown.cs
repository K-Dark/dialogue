using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DialogueEditor
{
    // Exports 1 Markdown file per Dialogue (per language).
    public static class ExporterScriptMarkdown
    {
        // --------------------------------------------------------------------
        // Compile-time options

        // If true, prints Context/Comment/Intensity notes BEFORE the spoken block.
        // If false, prints them AFTER the spoken block.
        private static readonly bool NOTES_BEFORE_LINE_BLOCK = true;

        // --------------------------------------------------------------------
        // Entry

        public static bool ExportAll()
        {
            Project project = ResourcesHandler.Project;

            string projectDirectory = EditorHelper.GetProjectDirectory();
            // Reuse the Voicing export path for UX consistency
            string exportDirectory = Path.Combine(projectDirectory, EditorCore.Settings.DirectoryExportVoicing);

            var dialog = new DialogExport(
                "Export Markdown Scripts",
                path: exportDirectory,
                defaultDateDirectory: true,
                defaultPackageDirectory: false,
                allowConstants: false,
                allowLanguages: true,
                allowWorkstringFallback: true,
                allowDateFrom: false,                  // not needed for script export
                dateFrom: Utility.GetCurrentTime()
            );

            var result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.Cancel)
                return false;

            EditorCore.Settings.DirectoryExportVoicing =
                Utility.GetRelativePath(dialog.ExportPath, projectDirectory);

            string baseDir = dialog.ExportPath;
            if (dialog.UseDateDirectory)
                baseDir = Path.Combine(baseDir, Utility.GetDateTimeAsString(Utility.GetCurrentTime()));

            if (!Directory.Exists(baseDir))
                Directory.CreateDirectory(baseDir);

            // Same grouping logic as voicing exporter
            var packageGroups = new List<List<Package>>();
            if (dialog.UsePackagesDirectory)
            {
                foreach (var pkg in dialog.ListPackages)
                    packageGroups.Add(new List<Package> { pkg });
            }
            else
            {
                packageGroups.Add(dialog.ListPackages);
            }

            // Languages
            var listLanguages = new List<Language>() { EditorCore.LanguageWorkstring };
            if (!dialog.WorkstringOnly)
                listLanguages = dialog.ListLanguages;

            foreach (var packageGroup in packageGroups)
            {
                string packageDir = baseDir;
                if (dialog.UsePackagesDirectory)
                    packageDir = Path.Combine(packageDir, packageGroup[0].Name);

                var dialogues = dialog.SelectedDialogues;
                if (!dialog.UseCustomSelection)
                    dialogues = ResourcesHandler.GetAllDialoguesFromPackages(packageGroup);

                foreach (var language in listLanguages)
                {
                    string langDir = Path.Combine(packageDir, language.VoicingCode);
                    if (!Directory.Exists(langDir))
                        Directory.CreateDirectory(langDir);

                    foreach (var dlg in dialogues)
                    {
                        ExportDialogueToMarkdownFile(
                            langDir,
                            project,
                            dlg,
                            language,
                            dialog.WorkstringOnly,
                            dialog.WorkstringFallback
                        );
                    }
                }
            }

            System.Diagnostics.Process.Start(baseDir);
            return true;
        }

        // --------------------------------------------------------------------
        // One file per dialogue

        private static void ExportDialogueToMarkdownFile(
            string directory,
            Project project,
            Dialogue dialogue,
            Language language,
            bool workstringOnly,
            bool workstringFallback)
        {
            string fileName = MakeSafeFileName(dialogue.GetName()) + ".md";
            string path = Path.Combine(directory, fileName);

            using (var file = new StreamWriter(path, false, Encoding.UTF8))
            {
                // Scene Title
                file.WriteLine($"# Scene: {EditorHelper.SanitizeText(dialogue.GetName())}");
                file.WriteLine();
                file.WriteLine();
                file.WriteLine();

                // Traverse starting after the Root
                var visited = new HashSet<int>();
                string lastSpeaker = null; // track last non-comment speaker to suppress label when appropriate
                WriteNodeFlow(
                    file, project, dialogue,
                    dialogue.RootNode?.Next,
                    language, workstringOnly, workstringFallback,
                    visited, ref lastSpeaker
                );
            }
        }

        // --------------------------------------------------------------------
        // Graph traversal

        private static void WriteNodeFlow(
            StreamWriter file,
            Project project,
            Dialogue dialogue,
            DialogueNode node,
            Language language,
            bool workstringOnly,
            bool workstringFallback,
            HashSet<int> visited,
            ref string lastSpeaker)
        {
            while (node != null)
            {
                if (node.ID != DialogueNode.ID_NULL)
                {
                    if (visited.Contains(node.ID))
                    {
                        file.WriteLine($"*Loop detected to Node {node.ID}, skipping to avoid repetition.*");
                        file.WriteLine();
                        return;
                    }
                    visited.Add(node.ID);
                }

                if (node is DialogueNodeSentence s)
                {
                    WriteSentenceBlock(
                        file, project, dialogue, s,
                        language, workstringOnly, workstringFallback,
                        ref lastSpeaker
                    );
                    node = s.Next;
                }
                else if (node is DialogueNodeComment comment)
                {
                    WriteCommentBlock(file, comment.Comment);
                    node = comment.Next;
                }
                else if (node is DialogueNodeChoice choice)
                {
                    // Structural break → reset lastSpeaker so labels resume after the choice
                    lastSpeaker = null;

                    file.WriteLine("---");
                    file.WriteLine($"## Choice BEGIN: {EditorHelper.SanitizeText(choice.Choice)}");
                    file.WriteLine();

                    foreach (var reply in choice.Replies)
                    {
                        // Reply is a structural segment — header + fresh speaker context
                        file.WriteLine($"### => Reply: {EditorHelper.SanitizeText(reply.Reply)}");
                        file.WriteLine();

                        var branchVisited = new HashSet<int>(visited);
                        string childLast = null; // start fresh inside the reply
                        WriteNodeFlow(
                            file, project, dialogue,
                            reply.Next,
                            language, workstringOnly, workstringFallback,
                            branchVisited, ref childLast
                        );
                    }

                    file.WriteLine($"## Choice END: {EditorHelper.SanitizeText(choice.Choice)}");
                    file.WriteLine("---");
                    file.WriteLine();

                    // After a choice block, also reset before continuing
                    lastSpeaker = null;
                    node = choice.Next;
                }
                else if (node is DialogueNodeReply reply)
                {
                    // If ever reached directly, treat it as structural and reset speakers
                    lastSpeaker = null;

                    file.WriteLine($"### => Reply: {EditorHelper.SanitizeText(reply.Reply)}");
                    file.WriteLine();

                    node = reply.Next;
                }
                else if (node is DialogueNodeGoto g)
                {
                    lastSpeaker = null;

                    file.WriteLine("---");
                    string targetLabel = DescribeNode(project, dialogue, g.Goto, language, workstringOnly, workstringFallback);
                    file.WriteLine($"> ***Goto*** → _{targetLabel}_"); // Node {(g.Goto != null ? g.Goto.ID : DialogueNode.ID_NULL)}
                    file.WriteLine("---");
                    file.WriteLine();
                    return; // do not auto-follow to avoid infinite loops
                }
                else if (node is DialogueNodeReturn)
                {
                    lastSpeaker = null;

                    file.WriteLine("> ***Return***");
                    file.WriteLine("---");
                    file.WriteLine();
                    return; // branch ends
                }
                else if (node is DialogueNodeBranch b)
                {
                    // Consider Branch a structural break too.
                    lastSpeaker = null;

                    file.WriteLine("---");
                    if (!string.IsNullOrWhiteSpace(b.Workstring))
                        file.WriteLine($"***Branch:*** {b.Workstring}");
                    else
                        file.WriteLine("***Branch***");
                    file.WriteLine();

                    var branchVisited = new HashSet<int>(visited);
                    string childLast = null;
                    WriteNodeFlow(
                        file, project, dialogue,
                        b.Branch,
                        language, workstringOnly, workstringFallback,
                        branchVisited, ref childLast
                    );

                    file.WriteLine("---");
                    file.WriteLine();

                    // Reset before continuing after the branch
                    lastSpeaker = null;
                    node = b.Next;
                }
                else if (node is DialogueNodeRoot r)
                {
                    lastSpeaker = null; // start of scene
                    node = r.Next;
                }
                else
                {
                    file.WriteLine($"*Unsupported node type: {node.GetType().Name}*");
                    file.WriteLine();
                    return;
                }
            }
        }

        // --------------------------------------------------------------------
        // Sentence output

        private static void WriteSentenceBlock(
            StreamWriter file,
            Project project,
            Dialogue dialogue,
            DialogueNodeSentence s,
            Language language,
            bool workstringOnly,
            bool workstringFallback,
            ref string lastSpeaker)
        {
            string speaker = project.GetActorName(s.SpeakerID);
            if (string.IsNullOrEmpty(speaker))
                speaker = "<Undefined>";

            string text = GetLocalizedSentence(dialogue, s, language, workstringOnly, workstringFallback);

            bool isCommentSpeaker = speaker.IndexOf("comment", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isCommentSpeaker)
            {
                // Comment blocks → italics only; DO NOT alter lastSpeaker.
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                    file.WriteLine(string.IsNullOrWhiteSpace(line) ? "" : $"*{line.TrimEnd()}*");

                file.WriteLine();
                return;
            }

            // Decide whether to print the [Speaker] label:
            // - Print if this is the first spoken line in a while (lastSpeaker == null)
            // - Or if the speaker has changed since last spoken line
            bool printLabel = (lastSpeaker == null) ||
                              !string.Equals(lastSpeaker, speaker, StringComparison.Ordinal);

            // Spoken block
            if (printLabel)
                file.WriteLine($"**[{speaker}]**\n");

            // Notes before?
            if (NOTES_BEFORE_LINE_BLOCK)
                WriteNotesBlock(file, s);

            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                file.WriteLine(line.TrimEnd());
            file.WriteLine();

            // Notes after?
            if (!NOTES_BEFORE_LINE_BLOCK)
                WriteNotesBlock(file, s);

            // Update last non-comment speaker
            lastSpeaker = speaker;
        }

        private static void WriteCommentBlock(StreamWriter file, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                file.WriteLine("*Comment*");
                file.WriteLine();
                return;
            }

            WriteItalicLines(file, text);
            file.WriteLine();
        }

        // --------------------------------------------------------------------
        // Notes helpers (Context / Comment / Intensity)

        private static bool WriteItalicLines(StreamWriter file, string text)
        {
            if (text == null) return false;

            bool wroteAny = false;
            var lines = text.Replace("\r\n", "\n").Split('\n');

            foreach (var raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    file.WriteLine(); // true blank line for empty note lines
                else
                    file.WriteLine($"*{raw.TrimEnd()}*");

                wroteAny = true;
            }
            return wroteAny;
        }

        private static void WriteNotesBlock(StreamWriter file, DialogueNodeSentence s)
        {
            bool any = false;
            any |= WriteItalicLines(file, s.Context);
            any |= WriteItalicLines(file, s.Comment);

            if (!string.IsNullOrWhiteSpace(s.VoiceIntensity))
            {
                file.WriteLine($"*Intensity: {s.VoiceIntensity}*");
                any = true;
            }

            if (any) file.WriteLine(); // spacing after notes
        }

        // --------------------------------------------------------------------
        // Utilities

        private static string DescribeNode(
            Project project,
            Dialogue dialogue,
            DialogueNode node,
            Language language,
            bool workstringOnly,
            bool workstringFallback)
        {
            if (node == null) return "null";

            if (node is DialogueNodeSentence s)
            {
                string spk = project.GetActorName(s.SpeakerID);
                string sentence = GetLocalizedSentence(dialogue, s, language, workstringOnly, workstringFallback)
                                  .Replace("\n", " ").Trim();
                if (sentence.Length > 70) sentence = sentence.Substring(0, 70) + "…";
                return $"{spk}: {sentence}";
            }
            if (node is DialogueNodeChoice choiceNode)
                return $"Choice: {choiceNode.Choice}";
            if (node is DialogueNodeReturn)
                return "Return";
            if (node is DialogueNodeReply r)
                return $"Reply: {r.Reply}";
            if (node is DialogueNodeBranch b)
                return string.IsNullOrWhiteSpace(b.Workstring) ? "Branch" : $"Branch: {b.Workstring}";
            if (node is DialogueNodeComment commentNode)
            {
                string text = commentNode.Comment.Replace("\n", " ").Trim();
                if (text.Length > 70) text = text.Substring(0, 70) + "...";
                return string.IsNullOrWhiteSpace(text) ? "Comment" : $"Comment: {text}";
            }

            return node.GetType().Name;
        }

        private static string GetLocalizedSentence(
            Dialogue dialogue,
            DialogueNodeSentence s,
            Language language,
            bool workstringOnly,
            bool workstringFallback)
        {
            string voicedText = s.Sentence; // workstring by default
            if (!workstringOnly)
            {
                var entry = dialogue.Translations.GetNodeEntry(s, language);
                if (entry == null)
                {
                    if (!workstringFallback)
                        voicedText = "<Missing Translation>";
                }
                else
                {
                    voicedText = entry.Text;
                }
            }

            // Force constant replacement like in voicing CSV export
            voicedText = EditorHelper.FormatTextEntry(voicedText, null);
            return voicedText;
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
