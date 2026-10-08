using System;
using System.Linq;
using System.Text;
using TfPortable.Commands;
using TfPortable.Context;
using TfPortable.Output;
using Xunit;

namespace TfPortable.Tests
{
    public class UnifiedDiffTests
    {
        [Fact]
        public void Equal_texts_give_no_diff()
        {
            Assert.Empty(UnifiedDiff.Render("a\nb\nc\n", "a\nb\nc\n", "L", "R"));
            Assert.False(UnifiedDiff.AreEqual("a\r\nb\r\n", "a\nb\n", DiffOptions.None));
            Assert.True(UnifiedDiff.AreEqual("a\r\nb\r\n", "a\nb\n", DiffOptions.IgnoreEol));
        }

        [Fact]
        public void Missing_final_newline_is_a_difference_with_a_marker()
        {
            Assert.False(UnifiedDiff.AreEqual("a\nb\n", "a\nb", DiffOptions.None));
            Assert.True(UnifiedDiff.AreEqual("a\nb\n", "a\nb", DiffOptions.IgnoreEol));
            var lines = UnifiedDiff.Render("a\nb\n", "a\nb", "L", "R").ToList();
            Assert.Equal(new[] { "--- L", "+++ R", "@@ -1,2 +1,2 @@", " a", "-b", "+b", "\\ No newline at end of file" }, lines);
        }

        [Fact]
        public void Large_texts_with_nothing_in_common_are_replaced_whole()
        {
            string before = string.Concat(Enumerable.Range(0, 3100).Select(i => "a" + i + "\n"));
            string after = string.Concat(Enumerable.Range(0, 3100).Select(i => "b" + i + "\n"));
            var lines = UnifiedDiff.Render(before, after, "L", "R").ToList();
            Assert.Equal("@@ -1,3100 +1,3100 @@", lines[2]);
            Assert.Equal(3 + 6200, lines.Count);
            Assert.Equal("-a0", lines[3]);
            Assert.Equal("+b3099", lines[lines.Count - 1]);
        }

        [Fact]
        public void Appended_line_gives_a_tail_hunk_with_three_lines_of_context()
        {
            string before = string.Join("\n", Enumerable.Range(1, 18).Select(i => "line " + i)) + "\n";
            string after = before + "// local edit\n";
            var lines = UnifiedDiff.Render(before, after, "Server: $/x;C81", "Local: x").ToList();
            Assert.Equal("--- Server: $/x;C81", lines[0]);
            Assert.Equal("+++ Local: x", lines[1]);
            Assert.Equal("@@ -16,3 +16,4 @@", lines[2]);
            Assert.Equal(" line 16", lines[3]);
            Assert.Equal(" line 18", lines[5]);
            Assert.Equal("+// local edit", lines[6]);
            Assert.Equal(7, lines.Count);
        }

        [Fact]
        public void Replaced_block_in_the_middle()
        {
            string before = "{\n    {\n        private static void Main(string[] args)\n        {\n            Console.WriteLine(\"Hello\");\n        }\n    }\n}\n";
            string after = "{\n    {\n        private static void Main(string[] args)\n        {\n            Console.WriteLine(Greeting(args));\n        }\n\n        private static string Greeting(string name)\n        {\n            return name;\n        }\n    }\n}\n";
            var lines = UnifiedDiff.Render(before, after, "L", "R").ToList();
            Assert.Equal("@@ -2,7 +2,12 @@", lines[2]);
            Assert.Contains("-            Console.WriteLine(\"Hello\");", lines);
            Assert.Contains("+            Console.WriteLine(Greeting(args));", lines);
            Assert.Contains("+        private static string Greeting(string name)", lines);
        }

        [Fact]
        public void Insertion_before_a_repeated_closing_line_is_reported_before_it()
        {
            var lines = UnifiedDiff.Render("a\n}\n}\n", "a\nb\n}\n}\n}\n", "L", "R").ToList();
            Assert.Equal(new[] { "--- L", "+++ R", "@@ -1,3 +1,5 @@", " a", "+b", "+}", " }", " }" }, lines);
        }

        [Fact]
        public void Empty_source_is_an_add_hunk()
        {
            var lines = UnifiedDiff.Render(string.Empty, "new file in a shelveset\n", "[no source file]", "Shelved change: $/a.txt;S").ToList();
            Assert.Equal("@@ -0,0 +1 @@", lines[2]);
            Assert.Equal("+new file in a shelveset", lines[3]);
        }

        [Fact]
        public void Options_ignore_space_eol_and_case()
        {
            Assert.True(UnifiedDiff.AreEqual("a b\n", "ab\n", DiffOptions.IgnoreSpace));
            Assert.False(UnifiedDiff.AreEqual("a b\n", "ab\n", DiffOptions.None));
            Assert.True(UnifiedDiff.AreEqual("A\n", "a\n", DiffOptions.IgnoreCase));
            Assert.True(UnifiedDiff.AreEqual("a\r\n", "a\n", DiffOptions.IgnoreEol));
        }

        [Fact]
        public void Content_is_decoded_by_the_server_encoding_or_a_bom()
        {
            byte[] cp1250 = Encoding.GetEncoding(1250).GetBytes("Příliš žluťoučký kůň");
            Assert.Equal("Příliš žluťoučký kůň", TextContent.Decode(cp1250, "windows-1250"));
            byte[] bom = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("x")).ToArray();
            Assert.Equal("x", TextContent.Decode(bom, "windows-1250"));
            Assert.Null(TextContent.Decode(new byte[] { 1, 2, 3 }, "binary"));
            Assert.True(TextContent.LooksBinary(new byte[] { 65, 0, 66 }));
        }
    }

    public class ServerRenderingTests
    {
        private static readonly DateTime Date = new DateTime(2026, 10, 7, 19, 7, 27, DateTimeKind.Local);

        [Fact]
        public void Changeset_block_aligns_change_types_and_marks_deleted_items()
        {
            var cs = new ChangesetInfo(78, "Jan Novak", "jan@contoso.com", Date, "Rename StringHelper to TextHelper", new[]
            {
                new ChangeInfo("rename", "$/Repo/Main/src/Utils/TextHelper.cs", 78, 0, "$/Repo/Main/src/Utils/StringHelper.cs", false, 5, "utf-8"),
                new ChangeInfo("delete, source rename", "$/Repo/Main/src/Utils/StringHelper.cs", 78, 17, null, false, 4, "utf-8"),
            }, new[] { new WorkItemInfo(123, "Fix it") });
            var lines = ChangesetCommand.RenderBlock(cs).ToList();
            Assert.Equal("Changeset: 78", lines[0]);
            Assert.Equal("User: Jan Novak", lines[1]);
            Assert.Equal("Comment:", lines[4]);
            Assert.Equal("  Rename StringHelper to TextHelper", lines[5]);
            Assert.Equal("Items:", lines[7]);
            Assert.Equal("  delete, source rename $/Repo/Main/src/Utils/StringHelper.cs;X17", lines[8]);
            Assert.Matches(@"^  rename {16}\$/Repo/Main/src/Utils/TextHelper\.cs$", lines[9]);
            Assert.Equal("Work items:", lines[11]);
            Assert.Equal("  123 Fix it", lines[12]);
        }

        [Fact]
        public void Dir_listing_groups_folders_first_and_counts()
        {
            var items = new[]
            {
                new ItemInfo("$/Repo/Main/README.md", false, 83, 0, "utf-8", 10, Date, 1),
                new ItemInfo("$/Repo/Main/src", true, 75, 0, null, null, Date, 2),
                new ItemInfo("$/Repo/Main/.tfignore", false, 75, 0, "utf-8", 3, Date, 3),
                new ItemInfo("$/Repo/Main/src/Program.cs", false, 81, 0, "utf-8", 100, Date, 4),
                new ItemInfo("$/Repo/Main/docs/obsolete.md", false, 79, 18, "utf-8", 5, Date, 5),
            };
            var lines = DirCommand.Render("$/Repo/Main", items).ToList();
            Assert.Equal(new[] { "$/Repo/Main:", "$src", ".tfignore", "README.md", "", "$/Repo/Main/docs:", "obsolete.md;X18", "", "$/Repo/Main/src:", "Program.cs", "", "Items: 5" }, lines);
        }

        [Fact]
        public void Properties_sections_and_labels_and_branches_and_candidates()
        {
            var local = new ExtendedItemInfo("$/Repo/Main/src/Program.cs", @"C:\Src\Repo\Main\src\Program.cs", false, 81, 81, "none", "none", null, 0, "windows-1250", Date, true, false);
            var server = new ItemInfo("$/Repo/Main/src/Program.cs", false, 81, 0, "windows-1250", 465, Date, 1);
            var lines = PropertiesCommand.Render(local, server).Select(l => System.Text.RegularExpressions.Regex.Replace(l, @":\s+", ": ")).ToList();
            Assert.Equal("Local information:", lines[0]);
            Assert.Equal(@"  Local path: C:\Src\Repo\Main\src\Program.cs", lines[1]);
            Assert.Equal("  Changeset: 81", lines[3]);
            Assert.Equal("  Type: file", lines[5]);
            Assert.Equal("Server information:", lines[6]);
            Assert.Contains("  Deletion ID: 0", lines);
            Assert.Contains("  Lock owner:", lines);
            Assert.Contains("  File type: windows-1250", lines);
            Assert.Equal("  Size: 465", lines.Last());

            var label = new LabelInfo("v1.0", "$/Repo", "Jan Novak", "jan@contoso.com", Date, "First test release", new[]
            {
                new LabelItemInfo("$/Repo/Main/docs/obsolete.md", 79, 18),
                new LabelItemInfo("$/Repo/Main", 75, 0),
            });
            var detailed = LabelsCommand.RenderDetailed(new[] { label }).Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"\s{2,}", " ")).ToList();
            Assert.Equal("Label: v1.0", detailed[0]);
            Assert.Equal("Scope: $/Repo", detailed[1]);
            Assert.Equal("Changeset Item", detailed[6]);
            Assert.Equal("75 $/Repo/Main", detailed[8]);
            Assert.Equal("79 $/Repo/Main/docs/obsolete.md;X18", detailed[9]);
            Assert.Equal("Label Owner Date", System.Text.RegularExpressions.Regex.Replace(LabelsCommand.RenderBrief(new[] { label }).First(), @"\s{2,}", " "));

            var tree = new BranchInfo("$/Repo/Main", null, 0, true, new[] { new BranchInfo("$/Repo/Release", "$/Repo/Main", 75, false, Array.Empty<BranchInfo>()) });
            Assert.Equal(new[] { ">> $/Repo/Main <<", "\t$/Repo/Release\tBranched from version 75" }, BranchesCommand.Render("$/Repo/Main", new[] { tree }));
            var fromChild = new BranchInfo("$/Repo/Main", null, 0, false, new[] { new BranchInfo("$/Repo/Release", "$/Repo/Main", 75, true, Array.Empty<BranchInfo>()) });
            Assert.Equal(new[] { "$/Repo/Main", "\t>> $/Repo/Release <<\tBranched from version 75" }, BranchesCommand.Render("$/Repo/Release", new[] { fromChild }));

            var candidates = new[] { new MergeCandidateInfo(new ChangesetInfo(87, "Jan Novak", null, Date, "partial"), true), new MergeCandidateInfo(new ChangesetInfo(83, "Jan Novak", null, Date, "full"), false) };
            var table = MergeCommand.Render(candidates, false).ToList();
            Assert.StartsWith("87*", table[2]);
            Assert.StartsWith("83 ", table[3]);
            Assert.Equal(new[] { "There are no changes to merge." }, MergeCommand.Render(Array.Empty<MergeCandidateInfo>(), false));
        }

        [Fact]
        public void History_single_file_table_has_a_change_column_and_files_are_listed()
        {
            var cs = new ChangesetInfo(78, "Jan Novak", null, Date, "Rename", new[] { new ChangeInfo("rename", "$/Repo/a.cs", 78, 0, null, false, 1, null) });
            var lines = HistoryCommand.Render(new[] { cs }, false, "$/Repo/a.cs", true).ToList();
            Assert.Equal("Changeset Change User Date Comment", System.Text.RegularExpressions.Regex.Replace(lines[0], @"\s{2,}", " "));
            Assert.StartsWith("78        rename Jan Novak", lines[2]);
            Assert.Equal("  rename $/Repo/a.cs", lines[3]);
            var detailed = HistoryCommand.RenderDetailed(new[] { cs }, false).ToList();
            Assert.Equal(new string('-', 79), detailed[0]);
            Assert.Equal("Changeset: 78", detailed[1]);
        }
    }
}
