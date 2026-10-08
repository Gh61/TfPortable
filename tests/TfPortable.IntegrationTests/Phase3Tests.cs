using System;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.VisualStudio.Services.Common;
using Newtonsoft.Json.Linq;
using TfPortable.IntegrationTests.Golden;
using Xunit;

namespace TfPortable.IntegrationTests
{
    /// <summary>
    /// Live tests of the phase 3 write path against the test collection. Checkins go into the dedicated folder
    /// `$/TfsPortableTestRepo/CI/&lt;run&gt;-&lt;test&gt;` (agreed with the developer on 2026-10-08) and end with a delete checkin;
    /// shelvesets and labels carry the run id in their name and are deleted at the end (through tfp at protection level 3,
    /// with the client library as the fallback). Every test leaves the workspace clean.
    /// </summary>
    [Collection(GoldenCollection.Name)]
    public class Phase3Tests
    {
        private static readonly string RunId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? "local-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        private readonly GoldenWorkspaceFixture fixture;

        public Phase3Tests(GoldenWorkspaceFixture fixture)
        {
            this.fixture = fixture;
        }

        private TfpResult Run(string arguments) => TfpRunner.Run(arguments, fixture.WorkspaceRoot);

        private static JToken Data(TfpResult result) => JObject.Parse(result.Stdout)["data"]!;

        private string CiFolder(string test) => Path.Combine(fixture.WorkspaceRoot, "CI", RunId + "-" + test);

        private static string CiArgument(string test) => "CI\\" + RunId + "-" + test;

        [IntegrationFact]
        public void Checkin_validates_commits_links_a_work_item_and_reports_nothing_pending()
        {
            fixture.EnsureReady();
            string folder = CiFolder("checkin");
            string argument = CiArgument("checkin");
            bool committed = false;
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "a.txt"), "a\r\n");
                File.WriteAllText(Path.Combine(folder, "b.txt"), "b\r\n");
                var add = Run($"add {argument} /recursive /noprompt");
                Assert.True(add.ExitCode == 0, add.Stderr);

                var noComment = Run($"checkin {argument} /recursive /noprompt");
                Assert.Equal(100, noComment.ExitCode);
                Assert.Contains("TFP0002", noComment.Stderr);

                // The policies of the test project decide the shape: a work item policy makes a checkin without /associate fail
                // with TFP0407, a policy tfp cannot evaluate (builds, code analysis) needs /override. Without any policy the
                // policy assertions are inconclusive and skipped.
                var validate = Run($"checkin {argument} /recursive /comment:\"CI validate ({RunId})\" /validate /noprompt /format:json");
                var validated = Data(validate);
                var policies = (JArray)validated["policies"]!;
                bool workItemPolicy = policies.Any(p => (string)p["name"]! == "Work Items" || ((string?)p["message"] ?? string.Empty).Contains("/associate"));
                bool unknownPolicy = policies.Any(p => (string)p["status"]! == "unknown");
                string overrideSwitch = unknownPolicy ? $" /override:\"CI run {RunId}: policies that need Visual Studio\"" : string.Empty;
                Assert.True((bool)validated["validated"]!);
                Assert.Null(((JValue)validated["changeset"]!).Value);
                Assert.Empty((JArray)validated["conflicts"]!);
                Assert.NotEmpty((JArray)validated["changes"]!);
                if (workItemPolicy)
                {
                    Assert.Equal(100, validate.ExitCode);
                    Assert.Contains("TFP0407", validate.Stderr + validate.Stdout);
                    Assert.Contains((JArray)validated["policyFailures"]!, f => ((string)f!).Contains("/associate"));
                }
                else if (unknownPolicy)
                {
                    Assert.Equal(100, validate.ExitCode);
                }
                else
                {
                    Assert.True(validate.ExitCode == 0, validate.Stderr);
                }

                var validOverridden = Run($"checkin {argument} /recursive /comment:\"CI validate ({RunId})\" /validate /associate:1{overrideSwitch} /noprompt /format:json");
                Assert.True(validOverridden.ExitCode == 0, validOverridden.Stderr);
                Assert.Equal(unknownPolicy, (bool)Data(validOverridden)["overridden"]!);
                Assert.NotEmpty(Changes(Run($"status {argument} /recursive /nodetect /format:json")));

                // Work item 1 (a bug) exists in the test project for this association (developer, 2026-10-08).
                var checkin = Run($"checkin {argument} /recursive /comment:\"CI checkin: add files ({RunId})\" /associate:1{overrideSwitch} /noprompt /format:json");
                committed = true;
                Assert.True(checkin.ExitCode == 0, checkin.Stderr);
                var data = Data(checkin);
                int changeset = (int)data["changeset"]!;
                Assert.True(changeset > 0);
                Assert.False((bool)data["validated"]!);
                Assert.True(((JArray)data["changes"]!).Count >= 3, "the folder and two files (plus CI on the first run)");
                var link = ((JArray)data["workItems"]!).Single();
                Assert.Equal(1, (int)link["id"]!);
                Assert.True((bool)link["linked"]!, checkin.Stderr);
                Assert.Empty(Changes(Run($"status {argument} /recursive /nodetect /format:json")));

                var text = Run($"changeset {changeset} /noprompt");
                Assert.True(text.ExitCode == 0, text.Stderr);
                Assert.Contains($"CI checkin: add files ({RunId})", text.Stdout);
                var shown = Data(Run($"changeset {changeset} /format:json"))["changeset"]!;
                Assert.Contains((JArray)shown["changes"]!, c => ((string)c["serverPath"]!).EndsWith("/a.txt", StringComparison.Ordinal));
                var workItems = shown["workItems"] as JArray;
                Assert.True(workItems != null && workItems.Any(w => (int)w["id"]! == 1), "The changeset does not list work item 1: " + shown["workItems"]);

                // Second changeset: an edit and a delete, text output.
                var checkout = Run($"checkout {argument}\\a.txt /noprompt");
                Assert.True(checkout.ExitCode == 0, checkout.Stderr);
                File.AppendAllText(Path.Combine(folder, "a.txt"), "more\r\n");
                var delete = Run($"delete {argument}\\b.txt /noprompt");
                Assert.True(delete.ExitCode == 0, delete.Stderr);
                var second = Run($"checkin {argument} /recursive /comment:\"CI checkin: edit and delete ({RunId})\" /associate:1{overrideSwitch} /noprompt");
                Assert.True(second.ExitCode == 0, second.Stderr);
                Assert.Contains("Checking in edit: a.txt", second.Stdout);
                Assert.Contains("Checking in delete: b.txt", second.Stdout);
                Assert.Contains("Changeset #", second.Stdout);
                Assert.Contains(" checked in.", second.Stdout);

                var nothing = Run($"checkin {argument} /recursive /comment:\"nothing\" /associate:1{overrideSwitch} /noprompt");
                Assert.Equal(100, nothing.ExitCode);
                Assert.StartsWith("ERROR TFP0301: Nothing to check in: the given items have no pending changes.", nothing.Stderr);
            }
            finally
            {
                Run($"undo {argument} /recursive /noprompt");
                if (committed)
                {
                    Run($"delete {argument} /recursive /noprompt");
                    var cleanup = Run($"checkin {argument} /recursive /comment:\"CI cleanup ({RunId})\" /associate:1 /override:\"CI cleanup\" /noprompt");
                    if (cleanup.ExitCode != 0)
                    {
                        Run($"undo {argument} /recursive /noprompt");
                    }
                }

                DeleteScratch(folder);
            }
        }

        [IntegrationFact]
        public void Shelve_shelvesets_and_unshelve_round_trip_and_delete_needs_level_3()
        {
            fixture.EnsureReady();
            string folder = CiFolder("shelve");
            string argument = CiArgument("shelve");
            string name = "tfp-ci-" + RunId;
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "shelved.txt"), "shelved\r\n");
                var add = Run($"add {argument} /recursive /noprompt");
                Assert.True(add.ExitCode == 0, add.Stderr);

                var shelve = Run($"shelve {name} {argument} /recursive /comment:\"CI shelveset ({RunId})\" /noprompt /format:json");
                Assert.True(shelve.ExitCode == 0, shelve.Stderr);
                var data = Data(shelve);
                Assert.Equal(name, (string)data["shelveset"]!["name"]!);
                Assert.False((bool)data["replaced"]!);
                Assert.Contains((JArray)data["changes"]!, c => ((string)c["serverPath"]!).EndsWith("/shelved.txt", StringComparison.Ordinal));
                Assert.NotEmpty(Changes(Run($"status {argument} /recursive /nodetect /format:json")));

                var list = Run($"shelvesets {name}");
                Assert.True(list.ExitCode == 0, list.Stderr);
                Assert.Contains(name, list.Stdout);
                var detailed = Run($"shelvesets {name} /format:json");
                Assert.True(detailed.ExitCode == 0, detailed.Stderr);
                var found = ((JArray)Data(detailed)["shelvesets"]!).Single();
                Assert.Equal($"CI shelveset ({RunId})", (string)found["comment"]!);
                Assert.Equal(100, Run("shelvesets no-such-shelveset-" + RunId).ExitCode);

                var duplicate = Run($"shelve {name} {argument} /recursive /noprompt");
                Assert.Equal(100, duplicate.ExitCode);
                Assert.StartsWith("ERROR TFP0408", duplicate.Stderr);

                var replace = Run($"shelve {name} {argument} /recursive /replace /move /comment:\"CI shelveset replaced ({RunId})\" /noprompt");
                Assert.True(replace.ExitCode == 0, replace.Stderr);
                Assert.Contains($"Shelveset {name} replaced.", replace.Stdout);
                Assert.Empty(Changes(Run($"status {argument} /recursive /nodetect /format:json")));

                var unshelve = Run($"unshelve {name} /noprompt /format:json");
                Assert.True(unshelve.ExitCode == 0, unshelve.Stderr);
                Assert.Contains((JArray)Data(unshelve)["changes"]!, c => ((string)c["serverPath"]!).EndsWith("/shelved.txt", StringComparison.Ordinal));
                Assert.NotEmpty(Changes(Run($"status {argument} /recursive /nodetect /format:json")));

                var missing = Run($"unshelve no-such-shelveset-{RunId} /noprompt");
                Assert.Equal(100, missing.ExitCode);
                Assert.StartsWith("ERROR TFP0301", missing.Stderr);

                using (ProtectionLevelScope.Set(2))
                {
                    var blocked = Run($"shelve /delete {name} /noprompt");
                    Assert.Equal(100, blocked.ExitCode);
                    Assert.StartsWith("ERROR TFP0004: Switch '/delete' of 'shelve' needs protection level 3.", blocked.Stderr);
                }

                using (ProtectionLevelScope.Set(3))
                {
                    var deleted = Run($"shelve /delete {name} /noprompt /format:json");
                    Assert.True(deleted.ExitCode == 0, deleted.Stderr);
                    Assert.True((bool)Data(deleted)["deleted"]!);
                }

                Assert.Equal(100, Run($"shelvesets {name}").ExitCode);
            }
            finally
            {
                Run($"undo {argument} /recursive /noprompt");
                DeleteShelvesetQuietly(name);
                DeleteScratch(folder);
            }
        }

        [IntegrationFact]
        public void Label_unlabel_and_delete_at_level_3()
        {
            fixture.EnsureReady();
            string name = "tfp-ci-" + RunId;
            string spec = name + "@" + GoldenWorkspaceFixture.ServerRoot;
            try
            {
                var create = Run($"label {spec} Main\\data /recursive /comment:\"CI label ({RunId})\" /noprompt");
                Assert.True(create.ExitCode == 0, create.Stderr);
                Assert.Contains($"Created label {spec}", create.Stdout);

                var labels = Run($"labels {name} /format:json");
                Assert.True(labels.ExitCode == 0, labels.Stderr);
                Assert.Contains((JArray)Data(labels)["labels"]!, l => (string)l["name"]! == name);

                var missing = Run($"label {spec} {GoldenWorkspaceFixture.ServerRoot}/Main/data/no-such-{RunId}.txt /noprompt");
                Assert.Equal(100, missing.ExitCode);
                Assert.StartsWith("ERROR TFP0301", missing.Stderr);

                var unlabel = Run($"unlabel {spec} Main\\data\\counter.txt /noprompt /format:json");
                Assert.True(unlabel.ExitCode == 0, unlabel.Stderr);
                Assert.Contains((JArray)Data(unlabel)["results"]!, r => (string)r["status"]! == "updated");

                var again = Run($"unlabel {spec} Main\\data\\counter.txt /noprompt");
                Assert.Equal(1, again.ExitCode);
                Assert.Contains("TFP0301", again.Stderr);

                using (ProtectionLevelScope.Set(2))
                {
                    var blocked = Run($"label /delete {spec} /noprompt");
                    Assert.Equal(100, blocked.ExitCode);
                    Assert.StartsWith("ERROR TFP0004: Switch '/delete' of 'label' needs protection level 3.", blocked.Stderr);
                }

                using (ProtectionLevelScope.Set(3))
                {
                    var deleted = Run($"label /delete {spec} /noprompt");
                    Assert.True(deleted.ExitCode == 0, deleted.Stderr);
                    Assert.Contains($"Deleted label {spec}", deleted.Stdout);
                }

                Assert.DoesNotContain((JArray)Data(Run($"labels {name} /format:json"))["labels"]!, l => (string)l["name"]! == name);
            }
            finally
            {
                DeleteLabelQuietly(name, GoldenWorkspaceFixture.ServerRoot);
            }
        }

        [IntegrationFact]
        public void Lock_and_unlock_an_item()
        {
            // The twin CI runs of one commit (push and pull_request) run at the same time against the same server, so the
            // locked item must belong to this run: a file checked in under the CI folder, deleted at the end.
            fixture.EnsureReady();
            string folder = CiFolder("lock");
            string argument = CiArgument("lock");
            string file = argument + "\\locked.txt";
            bool committed = false;
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "locked.txt"), "to be locked\r\n");
                var add = Run($"add {argument} /recursive /noprompt");
                Assert.True(add.ExitCode == 0, add.Stderr);
                var checkin = Run($"checkin {argument} /recursive /comment:\"CI lock test ({RunId})\" /associate:1 /override:\"CI lock test\" /noprompt");
                committed = true;
                Assert.True(checkin.ExitCode == 0, checkin.Stderr);

                var lockIt = Run($"lock {file} /lock:checkin /noprompt");
                Assert.True(lockIt.ExitCode == 0, lockIt.Stderr);
                Assert.Contains("locked.txt", lockIt.Stdout);

                var properties = Run($"properties {file} /format:json");
                Assert.True(properties.ExitCode == 0, properties.Stderr);
                var server = ((JArray)Data(properties)["items"]!).Single()["server"]!;
                Assert.Equal("checkin", (string)server["lock"]!);

                var pending = Run($"status {file} /nodetect /format:json");
                var unlock = Run($"lock {file} /lock:none /noprompt /format:json");
                Assert.True(unlock.ExitCode == 0, unlock.Stderr);
                Assert.Equal("none", (string)Data(unlock)["lock"]!);
                var afterStatus = Run($"status {file} /nodetect /format:json");
                var after = ((JArray)Data(Run($"properties {file} /format:json"))["items"]!).Single()["server"]!;
                Assert.True("none" == (string)after["lock"]!,
                    $"The lock stayed. Status while locked: {pending.Stdout} Unlock output: {unlock.Stdout} Status after: {afterStatus.Stdout}");
                Assert.Empty(Changes(afterStatus));
            }
            finally
            {
                Run($"lock {file} /lock:none /noprompt");
                Run($"undo {argument} /recursive /noprompt");
                if (committed)
                {
                    Run($"delete {argument} /recursive /noprompt");
                    var cleanup = Run($"checkin {argument} /recursive /comment:\"CI cleanup ({RunId})\" /associate:1 /override:\"CI cleanup\" /noprompt");
                    if (cleanup.ExitCode != 0)
                    {
                        Run($"undo {argument} /recursive /noprompt");
                    }
                }

                DeleteScratch(folder);
            }
        }

        private static JArray Changes(TfpResult status) => (JArray)JObject.Parse(status.Stdout)["data"]!["changes"]!;

        private VersionControlServer Connect(out TfsTeamProjectCollection connection)
        {
            string pat = Environment.GetEnvironmentVariable("TFP_TEST_PAT")!;
            var credentials = new VssCredentials(new Microsoft.VisualStudio.Services.Common.WindowsCredential(false), new VssBasicCredential(string.Empty, pat), CredentialPromptType.DoNotPrompt);
            connection = new TfsTeamProjectCollection(new Uri(fixture.Collection.TrimEnd('/')), credentials);
            connection.EnsureAuthenticated();
            return connection.GetService<VersionControlServer>();
        }

        private void DeleteShelvesetQuietly(string name)
        {
            try
            {
                var vcs = Connect(out var connection);
                using (connection)
                {
                    foreach (var shelveset in vcs.QueryShelvesets(name, vcs.AuthorizedUser))
                    {
                        vcs.DeleteShelveset(shelveset);
                    }
                }
            }
            catch (Exception)
            {
                // Already deleted by the test, or the server is gone; the name is unique per run.
            }
        }

        private void DeleteLabelQuietly(string name, string scope)
        {
            try
            {
                var vcs = Connect(out var connection);
                using (connection)
                {
                    if (vcs.QueryLabels(name, scope, null, false).Length > 0)
                    {
                        vcs.DeleteLabel(name, scope);
                    }
                }
            }
            catch (Exception)
            {
                // Already deleted by the test; the name is unique per run.
            }
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

    /// <summary>
    /// Writes the protection level into the settings file tfp.exe reads (`%APPDATA%\TfPortable\settings.json`) for the
    /// duration of a using block and restores the previous file afterwards. The CI runner is ephemeral; locally the
    /// developer's own level comes back when the block ends. A test process killed inside the block leaves the file at
    /// the test's level: check `tfp protection` after an aborted local run.
    /// </summary>
    internal sealed class ProtectionLevelScope : IDisposable
    {
        private readonly string path;
        private readonly string? previous;

        private ProtectionLevelScope(string path, string? previous)
        {
            this.path = path;
            this.previous = previous;
        }

        public static ProtectionLevelScope Set(int level)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TfPortable", "settings.json");
            string? previous = File.Exists(path) ? File.ReadAllText(path) : null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ \"protectionLevel\": " + level + " }\n");
            return new ProtectionLevelScope(path, previous);
        }

        public void Dispose()
        {
            if (previous == null)
            {
                File.Delete(path);
            }
            else
            {
                File.WriteAllText(path, previous);
            }
        }
    }
}
