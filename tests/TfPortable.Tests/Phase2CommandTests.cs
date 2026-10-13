using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TfPortable.Cli;
using TfPortable.Commands;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Output;
using Xunit;

namespace TfPortable.Tests
{
    /// <summary>Phase 2 pieces that need no server: the merge line, the annotate attribution, the folderdiff filters, the merges table, argument errors.</summary>
    [Collection("ToolSettings")]
    public class Phase2CommandTests
    {
        private static (int Exit, string Out, string Err) Run(params string[] args)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            int exit = Program.Run(args, stdout, stderr, "tfp");
            return (exit, stdout.ToString(), stderr.ToString());
        }

        private static PendingChangeInfo Change(string changeText, string serverPath, int version, params MergeSourceInfo[] sources) =>
            new PendingChangeInfo(changeText, serverPath, null, false, version, sources.Length > 0 ? sources[0].ServerPath : null, "none", "utf-8", DateTime.UtcNow, "o", "w", false, null, sources);

        [Fact]
        public void Merge_line_follows_the_original_shape()
        {
            Assert.Equal("merge, branch: $/P/Main/a.txt;C84 -> $/P/Release/a.txt",
                Change("merge, branch", "$/P/Release/a.txt", 0, new MergeSourceInfo("$/P/Main/a.txt", 0, 84, false)).MergeLine);
            Assert.Equal("merge, edit: $/P/Main/README.md;C83~C83 -> $/P/Release/README.md;C80",
                Change("merge, edit", "$/P/Release/README.md", 80, new MergeSourceInfo("$/P/Main/README.md", 83, 83, false)).MergeLine);
            Assert.Equal("rename, edit, rollback: $/P/a.txt;C85~C87 -> $/P/a.txt;C87",
                Change("rename, edit, rollback", "$/P/a.txt", 87, new MergeSourceInfo("$/P/a.txt", 85, 87, true)).MergeLine);
            Assert.Equal("edit: $/P/b.txt;C5", Change("edit", "$/P/b.txt", 5).MergeLine);
            // A source without a version (an operation the event did not version) prints no version.
            Assert.Equal("merge, edit: $/P/Main/a.txt -> $/P/Release/a.txt;C80", Change("merge, edit", "$/P/Release/a.txt", 80, new MergeSourceInfo("$/P/Main/a.txt", 0, 0, false)).MergeLine);
            Assert.NotNull(Change("merge, edit", "$/P/x", 1, new MergeSourceInfo("$/P/y", 1, 1, false)).ToJson()["mergeSources"]);
        }

        [Fact]
        public void Annotate_keeps_the_attribution_of_unchanged_lines()
        {
            var c1 = new ChangesetInfo(10, "Ann", null, new DateTime(2026, 1, 1), "first", new[] { new ChangeInfo("add", "$/P/f.txt", 10, 0, null, false, 7, "utf-8") });
            var c2 = new ChangesetInfo(20, "Bob", null, new DateTime(2026, 2, 1), "second", new[] { new ChangeInfo("edit", "$/P/f.txt", 20, 0, null, false, 7, "utf-8") });
            var c3 = new ChangesetInfo(30, "Cid", null, new DateTime(2026, 3, 1), "rename", new[] { new ChangeInfo("rename", "$/P/g.txt", 30, 0, "$/P/f.txt", false, 7, "utf-8") });
            var texts = new Dictionary<string, string>
            {
                ["$/P/f.txt@10"] = "one\r\ntwo\r\nthree\r\n",
                ["$/P/f.txt@20"] = "one\r\ntwo changed\r\nthree\r\nfour\r\n",
                ["$/P/g.txt@30"] = "zero\r\none\r\ntwo changed\r\nfour\r\n",
            };

            var lines = AnnotateCommand.Annotate(new[] { c1, c2, c3 }, 7, (path, id) => texts[path + "@" + id]);
            Assert.Equal(new[] { "zero", "one", "two changed", "four" }, lines.Select(l => l.Text));
            Assert.Equal(new[] { 30, 10, 20, 20 }, lines.Select(l => l.Changeset.Id));
            Assert.Equal(new[] { 1, 2, 3, 4 }, lines.Select(l => l.Number));

            var rendered = AnnotateCommand.Render(lines).ToList();
            Assert.StartsWith("Changeset", rendered[0]);
            Assert.Contains("30 Cid", rendered[1].Replace("  ", " "));
            Assert.EndsWith(" zero", rendered[1]);
        }

        [Fact]
        public void Folderdiff_filters_include_and_exclude_by_name()
        {
            var filters = FolderDiffCommand.Filters("*.cs;!*.designer.cs");
            Assert.True(FolderDiffCommand.Matches("App.cs", filters));
            Assert.False(FolderDiffCommand.Matches("App.designer.cs", filters));
            Assert.False(FolderDiffCommand.Matches("readme.md", filters));
            Assert.True(FolderDiffCommand.Matches("anything", FolderDiffCommand.Filters(null)));
            Assert.True(FolderDiffCommand.Matches("x.txt", FolderDiffCommand.Filters("!*.cs")));
            Assert.False(FolderDiffCommand.Matches("x.cs", FolderDiffCommand.Filters("!*.cs")));
        }

        [Fact]
        public void Merges_table_marks_partial_merges()
        {
            var target = new ChangesetInfo(80, "Jane", null, new DateTime(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc), "merge");
            var lines = MergesCommand.RenderTable(new[] { new MergeHistoryInfo(76, target, false), new MergeHistoryInfo(75, target, true) }).ToList();
            Assert.StartsWith("Changeset", lines[0]);
            Assert.StartsWith("75*", lines[2]);
            Assert.StartsWith("76 ", lines[3]);
            Assert.Contains("80", lines[2]);
            Assert.Equal(new[] { "No merge history was found." }, MergesCommand.RenderTable(Array.Empty<MergeHistoryInfo>()));
        }

        [Fact]
        public void Argument_errors_of_the_branching_commands_are_TFP0002()
        {
            Assert.StartsWith("ERROR TFP0002: merge needs a source and a destination.", Run("merge", "$/a").Err);
            Assert.StartsWith("ERROR TFP0002: branch needs the source item and the new item.", Run("branch", "$/a").Err);
            Assert.StartsWith("ERROR TFP0002: undelete needs at least one itemspec.", Run("undelete").Err);
            Assert.StartsWith("ERROR TFP0002: rollback needs exactly one of /changeset", Run("rollback").Err);
            Assert.StartsWith("ERROR TFP0002: rollback needs exactly one of /changeset", Run("rollback", "/changeset:C5", "/toversion:C4", "a").Err);
            Assert.StartsWith("ERROR TFP0002: rollback /toversion needs at least one itemspec.", Run("rollback", "/toversion:C4").Err);
            Assert.StartsWith("ERROR TFP0002: merges needs a destination", Run("merges").Err);
            Assert.StartsWith("ERROR TFP0002: folderdiff needs a target path", Run("folderdiff").Err);
            Assert.StartsWith("ERROR TFP0002: annotate needs exactly one file.", Run("annotate").Err);
            Assert.StartsWith("ERROR TFP0002: /view:bogus is not one of", Run("folderdiff", "$/a", "$/b", "/view:bogus").Err);
        }

        [Fact]
        public void Conflicts_are_reported_as_lines_of_the_original_shape()
        {
            // Golden merge/conflict and rollback/range: one `Conflict (<change>): <source> -> <target>` line per conflict, exit 1.
            var conflict = Change("merge, edit", "$/P/dst/a.txt", 93, new MergeSourceInfo("$/P/src/a.txt", 92, 92, false));
            var result = new MergeResult(Array.Empty<PendingChangeInfo>(), Array.Empty<ConflictInfo>(), Array.Empty<FailureInfo>(), 1, 1, false, new[] { conflict });
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var output = new OutputWriter(false, "merge", stdout, stderr);
            BranchingCommands.ReportMergeResult(output, result, "There are no changes to merge.", "tfp");
            Assert.Equal("", stdout.ToString());
            Assert.StartsWith("ERROR TFP0403: Conflict (merge, edit): $/P/src/a.txt;C92~C92 -> $/P/dst/a.txt;C93", stderr.ToString());
            Assert.DoesNotContain("conflict(s) were created", stderr.ToString());

            // Without event lines the conflict query and the count still report.
            var counted = new MergeResult(Array.Empty<PendingChangeInfo>(), Array.Empty<ConflictInfo>(), Array.Empty<FailureInfo>(), 0, 2, false);
            stderr = new StringWriter();
            BranchingCommands.ReportMergeResult(new OutputWriter(false, "merge", new StringWriter(), stderr), counted, "There are no changes to merge.", "tfp", preview: true);
            Assert.StartsWith("ERROR TFP0403: 2 conflict(s) would be created.", stderr.ToString());
        }

        [Fact]
        public void Merges_detailed_lists_the_merged_items()
        {
            // Golden merges/detailed: the detailed form is a list of `source;Cn -> target;Cm` items, not the table.
            var items = new[]
            {
                new MergedItemInfo("$/P/src/b.txt", 90, "$/P/dst/b.txt", 91),
                new MergedItemInfo("$/P/src/a.txt", 88, "$/P/dst/a.txt", 89),
            };
            Assert.Equal(new[] { "Merged items:", "$/P/src/a.txt;C88 -> $/P/dst/a.txt;C89", "$/P/src/b.txt;C90 -> $/P/dst/b.txt;C91" }, MergesCommand.RenderItems(items));
            Assert.Equal(new[] { "No merge history was found." }, MergesCommand.RenderItems(Array.Empty<MergedItemInfo>()));
            Assert.Equal("$/P/src/a.txt", items[1].ToJson()["sourcePath"]!.ToString());
        }

        [Fact]
        public void Annotate_skips_a_changeset_without_the_item()
        {
            var odd = new ChangesetInfo(5, "Ann", null, new DateTime(2026, 1, 1), "other", new[] { new ChangeInfo("add", "$/P/other.txt", 5, 0, null, false, 99, "utf-8") });
            var c1 = new ChangesetInfo(10, "Ann", null, new DateTime(2026, 1, 1), "first", new[] { new ChangeInfo("add", "$/P/f.txt", 10, 0, null, false, 7, "utf-8") });
            var lines = AnnotateCommand.Annotate(new[] { odd, c1 }, 7, (path, id) => "one\r\n");
            Assert.Equal(new[] { 10 }, lines.Select(l => l.Changeset.Id));
        }

        [Fact]
        public void Phase_2_commands_are_implemented_with_their_protection_levels()
        {
            foreach (string name in new[] { "merge", "branch", "undelete", "rollback" })
            {
                Assert.Equal(CommandStatus.Implemented, CommandCatalog.Find(name)!.Status);
                Assert.Equal(1, ProtectionLevels.Of(name));
            }

            foreach (string name in new[] { "merges", "folderdiff", "annotate" })
            {
                Assert.Equal(CommandStatus.Implemented, CommandCatalog.Find(name)!.Status);
                Assert.Equal(0, ProtectionLevels.Of(name));
            }

            var preview = CommandLine.Parse(new[] { "merge", "$/a", "$/b", "/preview" });
            Assert.Equal((1, (string?)null), ProtectionLevels.Required(preview.Spec!, preview));
            var locked = CommandLine.Parse(new[] { "merge", "$/a", "$/b", "/lock:checkout" });
            Assert.Equal((2, "lock"), ProtectionLevels.Required(locked.Spec!, locked));
        }
    }
}
