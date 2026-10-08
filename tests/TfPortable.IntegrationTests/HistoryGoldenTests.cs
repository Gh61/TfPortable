using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using TfPortable.IntegrationTests.Golden;
using Xunit;

namespace TfPortable.IntegrationTests
{
    /// <summary>
    /// Golden cases of milestone 4 (`docs/KICKOFF-CLOUD.md`): history, changeset, view, difference, dir, properties,
    /// labels, branches and merge /candidate of `index.json`, read-only against the seeded project. Listings whose
    /// server state the later golden runs extended (newer changesets in Main, extra folders, labels and branches)
    /// compare shape and subsets; stable ones compare line by line (docs/golden-format.md §6–8). `properties` cases skip
    /// the stream check: the original prints a deprecation warning on stderr that TfPortable leaves out (compat.md §1.3).
    /// </summary>
    [Collection(GoldenCollection.Name)]
    public class HistoryGoldenTests
    {
        private readonly GoldenWorkspaceFixture fixture;

        public HistoryGoldenTests(GoldenWorkspaceFixture fixture)
        {
            this.fixture = fixture;
        }

        private const GoldenChecks PropertiesExact = GoldenChecks.ExitCode | GoldenChecks.Lines;
        private const GoldenChecks PropertiesData = GoldenChecks.ExitCode | GoldenChecks.Items | GoldenChecks.Changesets;

        public static IEnumerable<object[]> Cases()
        {
            // history/brief: the 300 padding changesets of the `long-default` capture push the seed changesets past the 256 limit.
            yield return new object[] { "history/brief", GoldenChecks.Shape };
            yield return new object[] { "history/detailed-stopafter", GoldenChecks.Shape };
            yield return new object[] { "history/renamed-file", GoldenChecks.Shape | GoldenChecks.Changesets | GoldenChecks.ChangeTypes };
            yield return new object[] { "history/range", GoldenChecks.Shape | GoldenChecks.Changesets };
            yield return new object[] { "history/short-version", GoldenChecks.Shape | GoldenChecks.Changesets | GoldenChecks.ChangeTypes };
            yield return new object[] { "history/release-branch", GoldenChecks.Shape | GoldenChecks.ChangesetsSubset };
            // history/not-found: exit 0 with the message on stdout; message texts are never compared.
            yield return new object[] { "history/not-found", GoldenChecks.Shape };
            yield return new object[] { "history/vc-dash", GoldenChecks.Shape | GoldenChecks.ChangesetsSubset };
            yield return new object[] { "changeset/initial", GoldenChecks.Exact };
            yield return new object[] { "changeset/edit", GoldenChecks.Exact };
            yield return new object[] { "changeset/rename", GoldenChecks.Exact };
            yield return new object[] { "changeset/delete", GoldenChecks.Exact };
            yield return new object[] { "changeset/branch", GoldenChecks.Exact };
            yield return new object[] { "changeset/merge", GoldenChecks.Exact };
            yield return new object[] { "changeset/latest", GoldenChecks.Shape };
            yield return new object[] { "changeset/not-found", GoldenChecks.Shape };
            yield return new object[] { "changeset/vc-dash", GoldenChecks.Exact };
            yield return new object[] { "view/server-version", GoldenChecks.Exact };
            yield return new object[] { "view/semicolon-version", GoldenChecks.Exact };
            yield return new object[] { "view/changeset-no-c", GoldenChecks.Exact };
            yield return new object[] { "view/label-version", GoldenChecks.Exact };
            yield return new object[] { "view/local-path", GoldenChecks.Shape | GoldenChecks.ItemsSubset };
            yield return new object[] { "view/forward-slashes", GoldenChecks.Shape | GoldenChecks.ItemsSubset };
            yield return new object[] { "view/utf8-bom", GoldenChecks.Exact };
            yield return new object[] { "view/cp1250", GoldenChecks.Exact };
            yield return new object[] { "view/czech-name", GoldenChecks.Shape };
            yield return new object[] { "view/deleted-file", GoldenChecks.Exact };
            yield return new object[] { "view/not-found", GoldenChecks.Shape };
            yield return new object[] { "difference/server-unified", GoldenChecks.Exact };
            yield return new object[] { "difference/server-brief", GoldenChecks.Shape };
            yield return new object[] { "difference/no-local-change", GoldenChecks.Exact };
            yield return new object[] { "dir/folder", GoldenChecks.Shape | GoldenChecks.ItemsSubset };
            yield return new object[] { "dir/recursive", GoldenChecks.Shape | GoldenChecks.ItemsSubset };
            yield return new object[] { "dir/folders-only", GoldenChecks.Shape | GoldenChecks.ItemsSubset };
            yield return new object[] { "dir/deleted", GoldenChecks.Shape | GoldenChecks.ItemsSubset };
            yield return new object[] { "dir/at-version", GoldenChecks.Exact };
            yield return new object[] { "dir/not-found", GoldenChecks.Shape };
            yield return new object[] { "properties/local-file", PropertiesData };
            yield return new object[] { "properties/info-alias", PropertiesData };
            yield return new object[] { "properties/server-version", PropertiesExact };
            yield return new object[] { "properties/czech-name", PropertiesExact };
            // properties/recursive: Main\data\counter.txt is edited by the padding changesets of the `long-default` capture.
            yield return new object[] { "properties/recursive", GoldenChecks.ExitCode | GoldenChecks.Items };
            yield return new object[] { "labels/all", GoldenChecks.Shape };
            yield return new object[] { "labels/detailed", GoldenChecks.Exact };
            yield return new object[] { "branches/main", GoldenChecks.Shape | GoldenChecks.ItemsSubset };
            // merge/candidate: changeset 83 of the capture was merged to Release by a later golden run (changeset 91).
            yield return new object[] { "merge/candidate", GoldenChecks.Shape };
        }

        [IntegrationTheory]
        [MemberData(nameof(Cases))]
        public void Read_only_case_matches_the_original(string id, GoldenChecks checks)
        {
            fixture.EnsureReady();
            var golden = GoldenCase.Load(id);
            string cwd = golden.Cwd.Replace("<WORKSPACE_ROOT>", fixture.WorkspaceRoot).Replace("<LOCAL_ROOT>", fixture.LocalRoot);
            string arguments = golden.Arguments.Replace("<COLLECTION>", fixture.Collection.TrimEnd('/')).Replace("<WORKSPACE_ROOT>", fixture.WorkspaceRoot).Replace("<LOCAL_ROOT>", fixture.LocalRoot);
            var result = TfpRunner.Run(arguments, cwd);
            var differences = GoldenComparison.Compare(golden, result, golden.ExitCode, checks, fixture.Placeholders, cwd);
            Assert.True(differences.Count == 0, $"{id} ({golden.Command}):\n{string.Join("\n", differences)}");
        }

        [IntegrationFact]
        public void Local_edit_diff_matches_the_original()
        {
            fixture.EnsureReady();
            string program = Path.Combine(fixture.WorkspaceRoot, "Main", "src", "Program.cs");
            var golden = GoldenCase.Load("difference/local-edit");
            try
            {
                File.SetAttributes(program, FileAttributes.Normal);
                File.AppendAllText(program, "// local edit for golden capture\r\n", new UTF8Encoding(false));
                var result = TfpRunner.Run(golden.Arguments, fixture.WorkspaceRoot);
                var differences = GoldenComparison.Compare(golden, result, golden.ExitCode, GoldenChecks.Exact, fixture.Placeholders, fixture.WorkspaceRoot);
                Assert.True(differences.Count == 0, string.Join("\n", differences));
            }
            finally
            {
                TfpRunner.Run("undo Main\\src\\Program.cs /noprompt", fixture.WorkspaceRoot);
            }
        }

        [IntegrationFact]
        public void Changeset_diff_and_view_json_are_valid_envelopes()
        {
            fixture.EnsureReady();
            var diff = TfpRunner.Run("changeset 76 /diff /format:json", fixture.WorkspaceRoot);
            Assert.True(diff.ExitCode == 0, diff.Stderr);
            var envelope = JObject.Parse(diff.Stdout);
            Assert.Equal(76, (int)envelope["data"]!["changeset"]!["id"]!);
            Assert.NotEmpty((JArray)envelope["data"]!["changeset"]!["changes"]!);
            Assert.Contains("+++ Server:", (string)envelope["data"]!["diff"]!);

            var view = TfpRunner.Run("view $/TfsPortableTestRepo/Main/data/utf8-bom.txt /format:json", fixture.WorkspaceRoot);
            Assert.True(view.ExitCode == 0, view.Stderr);
            var viewEnvelope = JObject.Parse(view.Stdout);
            Assert.Contains("Příliš žluťoučký", (string)viewEnvelope["data"]!["content"]!);
            Assert.Equal("utf-8", (string)viewEnvelope["data"]!["item"]!["encoding"]!);

            var binary = TfpRunner.Run("view $/TfsPortableTestRepo/Main/assets/logo.png", fixture.WorkspaceRoot);
            Assert.Equal(100, binary.ExitCode);
            Assert.StartsWith("ERROR TFP0303:", binary.Stderr);

            string target = Path.Combine(Path.GetTempPath(), "tfp-golden-logo.png");
            var saved = TfpRunner.Run($"view $/TfsPortableTestRepo/Main/assets/logo.png /output:\"{target}\"", fixture.WorkspaceRoot);
            Assert.True(saved.ExitCode == 0, saved.Stderr);
            Assert.True(File.Exists(target));
            File.Delete(target);
        }
    }
}
