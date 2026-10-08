using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TfPortable.IntegrationTests.Golden;
using Xunit;

namespace TfPortable.IntegrationTests
{
    /// <summary>
    /// Live tests of the phase 2 commands. `branch`, `merge`, `rollback` and `undelete` pend changes only and end with `undo`;
    /// the checkins the rollback and undelete tests need go into `$/TfsPortableTestRepo/CI/<run>-rollback` and end with a
    /// delete checkin. `merges`, `folderdiff` and `annotate` read the seeded branches Main and Release.
    /// </summary>
    [Collection(GoldenCollection.Name)]
    public class Phase2Tests
    {
        private static readonly string RunId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        private const string Main = GoldenWorkspaceFixture.ServerRoot + "/Main";
        private const string Release = GoldenWorkspaceFixture.ServerRoot + "/Release";

        private readonly GoldenWorkspaceFixture fixture;

        public Phase2Tests(GoldenWorkspaceFixture fixture)
        {
            this.fixture = fixture;
        }

        private TfpResult Run(string arguments) => TfpRunner.Run(arguments, fixture.WorkspaceRoot);

        private static JToken Data(TfpResult result) => JObject.Parse(result.Stdout)["data"]!;

        private static JArray Changes(TfpResult status) => (JArray)JObject.Parse(status.Stdout)["data"]!["changes"]!;

        private const string CheckinExtra = " /associate:1 /override:\"CI phase 2 test\" /noprompt";

        [IntegrationFact]
        public void Branch_pends_the_tree_and_undo_removes_it()
        {
            fixture.EnsureReady();
            string target = GoldenWorkspaceFixture.ServerRoot + "/CI/" + RunId + "-branch";
            string local = Path.Combine(fixture.WorkspaceRoot, "CI", RunId + "-branch");
            try
            {
                var branch = Run($"branch {Main}/data {target} /noprompt /format:json");
                Assert.True(branch.ExitCode == 0, branch.Stderr);
                var changes = (JArray)Data(branch)["changes"]!;
                Assert.Contains(changes, c => ((string)c["serverPath"]!).EndsWith("/counter.txt", StringComparison.Ordinal) && c["changeTypes"]!.Any(t => (string?)t == "branch"));
                Assert.True(File.Exists(Path.Combine(local, "counter.txt")), "the branched file was not downloaded");

                var pending = Changes(Run($"status CI\\{RunId}-branch /recursive /nodetect /format:json"));
                Assert.NotEmpty(pending);
            }
            finally
            {
                Run($"undo CI\\{RunId}-branch /recursive /noprompt");
                DeleteScratch(local);
            }
        }

        [IntegrationFact]
        public void Merge_preview_and_merge_leave_no_pending_change_after_undo()
        {
            fixture.EnsureReady();
            try
            {
                var candidates = Run($"merge /candidate {Main} {Release} /recursive /format:json");
                Assert.True(candidates.ExitCode == 0, candidates.Stderr);
                int candidateCount = ((JArray)Data(candidates)["candidates"]!).Count;

                var preview = Run($"merge {Main} {Release} /recursive /preview /noprompt /format:json");
                Assert.True(preview.ExitCode == 0, preview.Stderr);
                Assert.True((bool)Data(preview)["preview"]!);
                Assert.Equal(candidateCount == 0, ((JArray)Data(preview)["changes"]!).Count == 0);
                Assert.Empty(Changes(Run($"status {Release} /recursive /nodetect /format:json")));

                var merge = Run($"merge {Main} {Release} /recursive /noprompt /format:json");
                Assert.True(merge.ExitCode == 0 || merge.ExitCode == 1, merge.Stderr);
                Assert.False((bool)Data(merge)["preview"]!);
                if (candidateCount > 0)
                {
                    Assert.NotEmpty(Changes(Run($"status {Release} /recursive /nodetect /format:json")));
                }

                // /discard pends the merge without taking the content (the changes are marked as merged).
                Assert.True(Run($"undo {Release} /recursive /noprompt").ExitCode == 0 || candidateCount == 0);
                var discard = Run($"merge {Main} {Release} /recursive /discard /noprompt /format:json");
                Assert.True(discard.ExitCode == 0 || discard.ExitCode == 1, discard.Stderr);
                if (candidateCount > 0)
                {
                    var pending = Changes(Run($"status {Release} /recursive /nodetect /format:json"));
                    Assert.Contains(pending, c => c["changeTypes"]!.Any(t => (string?)t == "merge"));
                }
            }
            finally
            {
                Run($"undo {Release} /recursive /noprompt");
                Run($"get {Release} /recursive /noprompt");
            }

            Assert.Empty(Changes(Run($"status {Release} /recursive /nodetect /format:json")));
        }

        [IntegrationFact]
        public void Rollback_and_undelete_pend_the_reverse_of_checkins()
        {
            fixture.EnsureReady();
            string argument = "CI\\" + RunId + "-rollback";
            string serverFolder = GoldenWorkspaceFixture.ServerRoot + "/CI/" + RunId + "-rollback";
            string folder = Path.Combine(fixture.WorkspaceRoot, "CI", RunId + "-rollback");
            bool committed = false;
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "r.txt"), "one\r\n");
                Assert.True(Run($"add {argument} /recursive /noprompt").ExitCode == 0);
                var first = Run($"checkin {argument} /recursive /comment:\"CI rollback test: add ({RunId})\"{CheckinExtra} /format:json");
                committed = true;
                Assert.True(first.ExitCode == 0, first.Stderr);

                Assert.True(Run($"checkout {argument}\\r.txt /noprompt").ExitCode == 0);
                File.WriteAllText(Path.Combine(folder, "r.txt"), "one\r\ntwo\r\n");
                var second = Run($"checkin {argument}\\r.txt /comment:\"CI rollback test: edit ({RunId})\"{CheckinExtra} /format:json");
                Assert.True(second.ExitCode == 0, second.Stderr);
                int edit = (int)Data(second)["changeset"]!;

                // Roll back the edit: a pending "edit, rollback" that restores the first content.
                var rollback = Run($"rollback /changeset:C{edit} {argument}\\r.txt /noprompt /format:json");
                Assert.True(rollback.ExitCode == 0, rollback.Stderr);
                Assert.Contains("rollback", rollback.Stdout);
                var pending = Changes(Run($"status {argument} /recursive /nodetect /format:json"));
                Assert.Contains(pending, c => c["changeTypes"]!.Any(t => (string?)t == "rollback"));
                Assert.Equal("one\r\n", File.ReadAllText(Path.Combine(folder, "r.txt")));
                Assert.True(Run($"undo {argument} /recursive /noprompt").ExitCode == 0);

                // Without an itemspec the whole workspace is rolled back (golden rollback/changeset).
                var whole = Run($"rollback /changeset:C{edit} /noprompt /format:json");
                Assert.True(whole.ExitCode == 0, whole.Stderr);
                Assert.Contains((JArray)Data(whole)["changes"]!, c => c["changeTypes"]!.Any(t => (string?)t == "rollback"));
                Assert.True(Run($"undo {argument} /recursive /noprompt").ExitCode == 0);

                // /toversion back to the first changeset gives the same pending change.
                var toVersion = Run($"rollback /toversion:C{(int)Data(first)["changeset"]!} {argument} /recursive /noprompt");
                Assert.True(toVersion.ExitCode == 0, toVersion.Stderr);
                Assert.Contains("rollback", toVersion.Stdout);
                Assert.True(Run($"undo {argument} /recursive /noprompt").ExitCode == 0);

                // Delete and check in, then undelete pends the item back.
                Assert.True(Run($"delete {argument}\\r.txt /noprompt").ExitCode == 0);
                var deleted = Run($"checkin {argument} /recursive /comment:\"CI rollback test: delete ({RunId})\"{CheckinExtra}");
                Assert.True(deleted.ExitCode == 0, deleted.Stderr);
                var undelete = Run($"undelete {serverFolder}/r.txt /noprompt /format:json");
                Assert.True(undelete.ExitCode == 0, undelete.Stderr);
                Assert.Contains((JArray)Data(undelete)["changes"]!, c => c["changeTypes"]!.Any(t => (string?)t == "undelete"));
                Assert.True(File.Exists(Path.Combine(folder, "r.txt")));
                Assert.True(Run($"undo {argument} /recursive /noprompt").ExitCode == 0);

                var missing = Run($"undelete {serverFolder}/no-such-{RunId}.txt /noprompt");
                Assert.Equal(1, missing.ExitCode);
                Assert.Contains("TFP0301", missing.Stderr);
            }
            finally
            {
                Run($"undo {argument} /recursive /noprompt");
                if (committed)
                {
                    Run($"delete {argument} /recursive /noprompt");
                    var cleanup = Run($"checkin {argument} /recursive /comment:\"CI cleanup ({RunId})\" /override:\"CI cleanup\" /noprompt");
                    if (cleanup.ExitCode != 0)
                    {
                        Run($"undo {argument} /recursive /noprompt");
                    }
                }

                DeleteScratch(folder);
            }
        }

        [IntegrationFact]
        public void Merges_folderdiff_and_annotate_read_the_seeded_branches()
        {
            fixture.EnsureReady();

            var merges = Run($"merges {Main} {Release} /recursive /format:json");
            Assert.True(merges.ExitCode == 0, merges.Stderr);
            Assert.NotEmpty((JArray)Data(merges)["merges"]!);
            var text = Run($"merges {Main} {Release} /recursive");
            Assert.Contains("Merged in Changeset", text.Stdout);
            var detailed = Run($"merges {Main} {Release} /recursive /format:detailed");
            Assert.True(detailed.ExitCode == 0, detailed.Stderr);
            Assert.Contains("Changeset:", detailed.Stdout);

            var diff = Run($"folderdiff {Main} {Release} /recursive /noprompt /format:json");
            Assert.True(diff.ExitCode == 0, diff.Stderr);
            Assert.True((int)Data(diff)["counts"]!["files"]! > 0);
            Assert.Contains("Summary:", Run($"folderdiff {Main} {Release} /recursive /noprompt").Stdout);
            var filtered = Run($"folderdiff {Main} {Release} /recursive /filter:*.md /noprompt /format:json");
            Assert.True(filtered.ExitCode == 0, filtered.Stderr);
            Assert.All(((JArray)Data(filtered)["sourceOnly"]!).Concat((JArray)Data(filtered)["targetOnly"]!), p => Assert.EndsWith(".md", (string)p!, StringComparison.OrdinalIgnoreCase));
            var missing = Run($"folderdiff {Main}/no-such-{RunId} {Release} /recursive /noprompt");
            Assert.Equal(100, missing.ExitCode);
            Assert.Contains("TFP0301", missing.Stderr);

            // One path: the local folder against its server folder; the workspace is up to date, nothing differs.
            var local = Run("folderdiff Main\\data /recursive /noprompt /format:json");
            Assert.True(local.ExitCode == 0, local.Stderr);
            Assert.Empty((JArray)Data(local)["different"]!);
            Assert.Empty((JArray)Data(local)["sourceOnly"]!);

            var annotate = Run($"annotate {Main}/README.md /format:json");
            Assert.True(annotate.ExitCode == 0, annotate.Stderr);
            var lines = (JArray)Data(annotate)["lines"]!;
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.True((int)l["changeset"]! > 0));
            var view = Run($"view {Main}/README.md /noprompt");
            Assert.Equal(view.Stdout.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length, lines.Count);
            Assert.Contains("Changeset", Run($"annotate {Main}/README.md").Stdout);
        }

        private static void DeleteScratch(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(directory, true);
                }
            }
            catch (Exception)
            {
                // Temp folder; leave it.
            }
        }
    }
}
