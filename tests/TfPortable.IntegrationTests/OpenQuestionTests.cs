using System;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.Client;
using Microsoft.VisualStudio.Services.Common;
using Newtonsoft.Json.Linq;
using TfPortable.Context;
using TfPortable.IntegrationTests.Golden;
using Xunit;

namespace TfPortable.IntegrationTests
{
    /// <summary>
    /// Behavior the golden files do not cover (docs/PROGRESS.md "Open questions"), verified against the test collection:
    /// a rejected PAT, `/encoding` values, `reconcile /promote` without `/adds`, a recursive `add` with `.tfignore`
    /// matches, `get` of an older version over a modified file, and the linking service behind `history /workitem`.
    /// Every test leaves the workspace clean.
    /// </summary>
    [Collection(GoldenCollection.Name)]
    public class OpenQuestionTests
    {
        private readonly GoldenWorkspaceFixture fixture;

        public OpenQuestionTests(GoldenWorkspaceFixture fixture)
        {
            this.fixture = fixture;
        }

        private TfpResult Run(string arguments) => TfpRunner.Run(arguments, fixture.WorkspaceRoot);

        private string Scratch(string name) => Path.Combine(fixture.WorkspaceRoot, "Main", name);

        private static JArray Changes(TfpResult status) => (JArray)JObject.Parse(status.Stdout)["data"]!["changes"]!;

        private static string? ServerPath(JToken change) => (string?)change["serverPath"];

        [IntegrationFact]
        public void Rejected_pat_is_TFP0102_with_exit_110_and_is_never_echoed()
        {
            fixture.EnsureReady();
            var result = TfpRunner.Run($"whoami /collection:{fixture.Collection.TrimEnd('/')} /login:nobody,not-a-valid-pat", Path.GetTempPath());
            Assert.Equal(110, result.ExitCode);
            // The /login warning ("used for this run only") precedes the error on stderr.
            Assert.Contains("ERROR TFP0102", result.Stderr);
            Assert.DoesNotContain("not-a-valid-pat", result.Stdout + result.Stderr);
        }

        [IntegrationFact]
        public void Add_encoding_accepts_a_name_a_code_page_and_binary()
        {
            fixture.EnsureReady();
            string scratch = Scratch("_encoding");
            try
            {
                Directory.CreateDirectory(scratch);
                File.WriteAllText(Path.Combine(scratch, "name.txt"), "a\r\n");
                File.WriteAllText(Path.Combine(scratch, "page.txt"), "b\r\n");
                File.WriteAllText(Path.Combine(scratch, "bin.txt"), "c\r\n");
                File.WriteAllBytes(Path.Combine(scratch, "utf8.txt"), new System.Text.UTF8Encoding(false).GetBytes("Příliš žluťoučký kůň\r\n"));
                File.WriteAllBytes(Path.Combine(scratch, "ansi.txt"), System.Text.Encoding.GetEncoding(1250).GetBytes("Příliš žluťoučký kůň\r\n"));

                var byName = Run("add Main\\_encoding\\name.txt /encoding:windows-1250 /noprompt");
                Assert.True(byName.ExitCode == 0, byName.Stderr);
                var byPage = Run("add Main\\_encoding\\page.txt /encoding:1250 /noprompt");
                Assert.True(byPage.ExitCode == 0, byPage.Stderr);
                var binary = Run("add Main\\_encoding\\bin.txt /encoding:binary /noprompt");
                Assert.True(binary.ExitCode == 0, binary.Stderr);
                // Without /encoding the client library records utf-8 for every BOM-less text file, the windows-1250 one included
                // (observed 2026-10-14; docs/compat.md §5). tfp reads such a file by its bytes, not by that label.
                var detected = Run("add Main\\_encoding\\utf8.txt Main\\_encoding\\ansi.txt /noprompt /format:json");
                Assert.True(detected.ExitCode == 0, detected.Stderr + detected.Stdout);
                Assert.Equal(2, Changes(detected).Count);

                var changes = Changes(Run("status Main\\_encoding /recursive /format:json"));
                Assert.Equal("windows-1250", (string?)changes.First(c => ServerPath(c)!.EndsWith("/name.txt", StringComparison.Ordinal))["encoding"]);
                Assert.Equal("windows-1250", (string?)changes.First(c => ServerPath(c)!.EndsWith("/page.txt", StringComparison.Ordinal))["encoding"]);
                Assert.Equal("binary", (string?)changes.First(c => ServerPath(c)!.EndsWith("/bin.txt", StringComparison.Ordinal))["encoding"]);
                Assert.Equal("utf-8", (string?)changes.First(c => ServerPath(c)!.EndsWith("/utf8.txt", StringComparison.Ordinal))["encoding"]);
                Assert.Equal("utf-8", (string?)changes.First(c => ServerPath(c)!.EndsWith("/ansi.txt", StringComparison.Ordinal))["encoding"]);

                // difference reads the BOM-less UTF-8 file as UTF-8 whatever encoding the server recorded for it, and the
                // windows-1250 file without U+FFFD (the machine's ANSI code page, since the label utf-8 does not fit the bytes).
                var diff = Run("difference Main\\_encoding\\utf8.txt /format:json");
                Assert.True(diff.ExitCode == 0 || diff.ExitCode == 1, diff.Stderr + diff.Stdout);
                Assert.Contains("Příliš žluťoučký kůň", diff.Stdout);
                var diffAnsi = Run("difference Main\\_encoding\\ansi.txt /format:json");
                Assert.True(diffAnsi.ExitCode == 0 || diffAnsi.ExitCode == 1, diffAnsi.Stderr + diffAnsi.Stdout);
                Assert.DoesNotContain("\uFFFD", diffAnsi.Stdout);
            }
            finally
            {
                Run("undo Main\\_encoding /recursive /noprompt");
                DeleteScratch(scratch);
            }
        }

        [IntegrationFact]
        public void Reconcile_promote_pends_detected_adds_without_the_adds_switch()
        {
            fixture.EnsureReady();
            string scratch = Scratch("_promote");
            try
            {
                Directory.CreateDirectory(scratch);
                File.WriteAllText(Path.Combine(scratch, "new.txt"), "new\r\n");

                var preview = Run("reconcile /promote Main\\_promote /recursive /preview /noprompt");
                Assert.True(preview.ExitCode == 0, preview.Stderr);
                Assert.Contains("Would promote add: new.txt", preview.Stdout);
                Assert.Empty(Changes(Run("status Main\\_promote /recursive /nodetect /format:json")));

                var promote = Run("reconcile /promote Main\\_promote /recursive /noprompt");
                Assert.True(promote.ExitCode == 0, promote.Stderr);
                Assert.Contains("Promoting add: new.txt", promote.Stdout);

                var pending = Changes(Run("status Main\\_promote /recursive /nodetect /format:json"));
                Assert.Contains(pending, c => ServerPath(c) == "$/TfsPortableTestRepo/Main/_promote/new.txt");
            }
            finally
            {
                Run("undo Main\\_promote /recursive /noprompt");
                DeleteScratch(scratch);
            }
        }

        [IntegrationFact]
        public void Recursive_add_skips_tfignore_matches_and_exits_0()
        {
            fixture.EnsureReady();
            string scratch = Scratch("_ignored");
            try
            {
                Directory.CreateDirectory(Path.Combine(scratch, "bin"));
                File.WriteAllText(Path.Combine(scratch, "keep.txt"), "kept\r\n");
                File.WriteAllText(Path.Combine(scratch, "bin", "skipped.txt"), "ignored by Main\\.tfignore\r\n");

                var add = Run("add Main\\_ignored /recursive /noprompt");
                Assert.True(add.ExitCode == 0, add.Stderr);

                var pending = Changes(Run("status Main\\_ignored /recursive /nodetect /format:json")).Select(ServerPath).ToList();
                Assert.Contains("$/TfsPortableTestRepo/Main/_ignored/keep.txt", pending);
                Assert.DoesNotContain("$/TfsPortableTestRepo/Main/_ignored/bin/skipped.txt", pending);
            }
            finally
            {
                Run("undo Main\\_ignored /recursive /noprompt");
                DeleteScratch(scratch);
            }
        }

        [IntegrationFact]
        public void Get_of_an_older_version_over_a_locally_modified_file_reports_a_conflict()
        {
            fixture.EnsureReady();
            string counter = Path.Combine(fixture.WorkspaceRoot, "Main", "data", "counter.txt");
            try
            {
                File.AppendAllText(counter, "local change\r\n");
                var get = Run("get Main\\data\\counter.txt /version:C75 /noprompt");
                Assert.Equal(1, get.ExitCode);
                Assert.Contains("TFP0403", get.Stderr);
            }
            finally
            {
                Run("resolve Main\\data\\counter.txt /auto:TakeTheirs /noprompt");
                Run("undo Main\\data\\counter.txt /noprompt");
                Run("get Main\\data\\counter.txt /force /noprompt");
            }

            // The read-only golden cases need the file at the latest version with no conflict and no pending change left.
            Assert.Empty((JArray)JObject.Parse(Run("resolve Main\\data\\counter.txt /preview /format:json").Stdout)["data"]!["conflicts"]!);
            Assert.Empty(Changes(Run("status Main\\data\\counter.txt /format:json")));
        }

        [IntegrationFact]
        public void Work_item_links_are_resolved_through_the_linking_service()
        {
            fixture.EnsureReady();
            string pat = Environment.GetEnvironmentVariable("TFP_TEST_PAT")!;
            var credentials = new VssCredentials(new Microsoft.VisualStudio.Services.Common.WindowsCredential(false), new VssBasicCredential(string.Empty, pat), CredentialPromptType.DoNotPrompt);
            using (var connection = new TfsTeamProjectCollection(new Uri(fixture.Collection.TrimEnd('/')), credentials))
            {
                connection.EnsureAuthenticated();
                var client = new TfvcClient(TfPortable.Auth.CollectionUrl.Normalize(fixture.Collection), connection);
                // A work item that does not exist links to nothing; the call itself must be answered by the service.
                Assert.Empty(client.GetWorkItemChangesets(999999999));
            }

            var history = Run("history $/TfsPortableTestRepo /recursive /workitem:999999999 /stopafter:3 /noprompt");
            Assert.True(history.ExitCode == 0, history.Stderr);
            Assert.Empty((JArray)JObject.Parse(Run("history $/TfsPortableTestRepo /recursive /workitem:999999999 /stopafter:3 /format:json").Stdout)["data"]!["changesets"]!);
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
                // Best effort; the fixture deletes everything at the end.
            }
        }
    }
}
