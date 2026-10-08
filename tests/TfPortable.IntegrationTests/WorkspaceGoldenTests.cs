using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using TfPortable.IntegrationTests.Golden;
using Xunit;

namespace TfPortable.IntegrationTests
{
    /// <summary>
    /// Golden cases of milestone 3 (`docs/KICKOFF-CLOUD.md`): the read-only cases and the pending-change scenario of
    /// `index.json`, replayed in index order inside the fixture's workspace and finished with `undo`. Which checks apply
    /// per case follows docs/golden-format.md §6–8: listings compare line by line (Czech → English), cases whose server
    /// state later golden runs extended compare shape and item subsets, error cases compare exit code and streams.
    /// </summary>
    [Collection(GoldenCollection.Name)]
    public class WorkspaceGoldenTests
    {
        private readonly GoldenWorkspaceFixture fixture;

        public WorkspaceGoldenTests(GoldenWorkspaceFixture fixture)
        {
            this.fixture = fixture;
        }

        private sealed class Step
        {
            public Step(string id, GoldenChecks checks, Action<GoldenWorkspaceFixture>? before = null)
            {
                Id = id;
                Checks = checks;
                Before = before;
            }

            public string Id { get; }

            public GoldenChecks Checks { get; }

            public Action<GoldenWorkspaceFixture>? Before { get; }
        }

        /// <summary>The scenario, in the order of `index.json` (docs/golden-format.md §6 and tools/capture-tf-golden.ps1).</summary>
        private static readonly Step[] Scenario =
        {
            // Read-only cases on the seeded state. Later golden runs added folders and changesets (README.md is past C83),
            // so the listings that show versions or every item compare shape and item subsets, not lines.
            new Step("workspaces/list", GoldenChecks.Shape),
            new Step("workspaces/detailed", GoldenChecks.Shape),
            new Step("workspaces/other-computer", GoldenChecks.Shape),
            new Step("workfold/current", GoldenChecks.Exact),
            new Step("status/clean", GoldenChecks.Exact),
            new Step("status/clean-detailed", GoldenChecks.Exact),
            new Step("status/server-path", GoldenChecks.Exact),
            new Step("localversions/recursive", GoldenChecks.Shape | GoldenChecks.ItemsSubset),
            new Step("localversions/detailed", GoldenChecks.Shape | GoldenChecks.ItemsSubset),
            new Step("get/preview", GoldenChecks.Shape),
            new Step("get/up-to-date", GoldenChecks.Shape),
            new Step("resolve/no-conflicts", GoldenChecks.Exact),

            // The pending-change scenario: the capture script created two files under Main\_golden first.
            new Step("status/detected-adds", GoldenChecks.Exact, f =>
            {
                string scratch = Path.Combine(f.WorkspaceRoot, "Main", "_golden");
                Directory.CreateDirectory(scratch);
                File.WriteAllText(Path.Combine(scratch, "new-file.txt"), "new file\r\n", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(scratch, "file with spaces.txt"), "spaces\r\n", new UTF8Encoding(false));
            }),
            new Step("add/folder-recursive", GoldenChecks.Exact),
            new Step("add/already-versioned", GoldenChecks.Shape),
            new Step("add/ignored-file", GoldenChecks.Exact, f =>
            {
                string ignored = Path.Combine(f.WorkspaceRoot, "Main", "src", "bin", "Debug");
                Directory.CreateDirectory(ignored);
                File.WriteAllText(Path.Combine(ignored, "ignored.txt"), "ignored\r\n", new UTF8Encoding(false));
            }),
            new Step("add/not-found", GoldenChecks.Shape),
            new Step("checkout/file", GoldenChecks.Exact),
            new Step("checkout/edit-alias", GoldenChecks.Exact),
            new Step("rename/file", GoldenChecks.Exact),
            new Step("rename/not-found", GoldenChecks.Shape),
            new Step("delete/file", GoldenChecks.Exact),
            new Step("delete/not-found", GoldenChecks.Shape),
            new Step("status/pending", GoldenChecks.Exact, f =>
            {
                // The capture appended a line to Program.cs between checkout and status (difference/local-edit); the
                // local workspace detects the edit, so the table shows Program.cs as edited.
                string program = Path.Combine(f.WorkspaceRoot, "Main", "src", "Program.cs");
                File.SetAttributes(program, FileAttributes.Normal);
                File.AppendAllText(program, "// local edit for golden capture\r\n", new UTF8Encoding(false));
            }),
            // The detailed blocks carry a Lock label that the change-word mapping would read as a change type; counts suffice.
            new Step("status/pending-detailed", GoldenChecks.Shape | GoldenChecks.Counts),
            new Step("get/with-pending", GoldenChecks.Exact),
            new Step("undo/recursive", GoldenChecks.Exact),
            new Step("undo/nothing-to-undo", GoldenChecks.Shape),
            new Step("status/after-undo", GoldenChecks.Exact),
        };

        /// <summary>Deviations of docs/compat.md: case id → expected exit code of tfp.</summary>
        private static readonly Dictionary<string, int> ExitCodeDeviations = new Dictionary<string, int>(StringComparer.Ordinal);

        [IntegrationFact]
        public void Workspace_scenario_matches_the_original()
        {
            fixture.EnsureReady();
            var failures = new List<string>();
            try
            {
                foreach (var step in Scenario)
                {
                    var golden = GoldenCase.Load(step.Id);
                    step.Before?.Invoke(fixture);
                    string cwd = golden.Cwd.Replace("<WORKSPACE_ROOT>", fixture.WorkspaceRoot).Replace("<LOCAL_ROOT>", fixture.LocalRoot);
                    string arguments = golden.Arguments.Replace("<COLLECTION>", fixture.Collection.TrimEnd('/')).Replace("<WORKSPACE_ROOT>", fixture.WorkspaceRoot).Replace("<LOCAL_ROOT>", fixture.LocalRoot);
                    var result = TfpRunner.Run(arguments, cwd);
                    int expected = ExitCodeDeviations.TryGetValue(step.Id, out int exit) ? exit : golden.ExitCode;
                    var differences = GoldenComparison.Compare(golden, result, expected, step.Checks, fixture.Placeholders, cwd);
                    if (differences.Count > 0)
                    {
                        failures.Add($"{step.Id} ({golden.Command}):\n{string.Join("\n", differences)}");
                    }
                }
            }
            finally
            {
                // Leave the workspace as the read-only cases expect it: no pending changes, no scratch files.
                TfpRunner.Run("undo Main /recursive /noprompt", fixture.WorkspaceRoot);
                DeleteScratch(Path.Combine(fixture.WorkspaceRoot, "Main", "_golden"));
                DeleteScratch(Path.Combine(fixture.WorkspaceRoot, "Main", "src", "bin"));
            }

            Assert.True(failures.Count == 0, string.Join("\n\n", failures));
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
                // Best effort; the fixture deletes the whole folder at the end anyway.
            }
        }

        [IntegrationFact]
        public void Status_json_after_the_scenario_is_a_valid_envelope()
        {
            fixture.EnsureReady();
            var result = TfpRunner.Run("status /recursive /format:json", fixture.WorkspaceRoot);
            Assert.True(result.ExitCode == 0, result.Stderr);
            var envelope = JObject.Parse(result.Stdout);
            Assert.True((bool)envelope["ok"]!);
            Assert.Equal("status", (string)envelope["command"]!);
            Assert.NotNull(envelope["data"]!["changes"]);
            Assert.NotNull(envelope["data"]!["detectedChanges"]);
            Assert.NotNull(envelope["data"]!["counts"]!["changes"]);
        }

        [IntegrationFact]
        public void Workfold_and_workspaces_json_describe_the_fixture_workspace()
        {
            fixture.EnsureReady();
            var workfold = TfpRunner.Run("workfold /format:json", fixture.WorkspaceRoot);
            Assert.True(workfold.ExitCode == 0, workfold.Stderr);
            var mapping = JObject.Parse(workfold.Stdout)["data"]!["workspace"]!["mappings"]![0]!;
            Assert.Equal(GoldenWorkspaceFixture.ServerRoot, (string)mapping["serverPath"]!);
            Assert.Equal(fixture.WorkspaceRoot, (string)mapping["localPath"]!, ignoreCase: true);

            var workspaces = TfpRunner.Run($"workspaces /collection:{fixture.Collection.TrimEnd('/')} /format:json", Path.GetTempPath());
            Assert.True(workspaces.ExitCode == 0, workspaces.Stderr);
            var list = (JArray)JObject.Parse(workspaces.Stdout)["data"]!["workspaces"]!;
            Assert.Contains(list, w => (string)w["name"]! == fixture.WorkspaceName && (string)w["location"]! == "local");

            var none = TfpRunner.Run($"workspaces /collection:{fixture.Collection.TrimEnd('/')} /computer:NoSuchComputer /format:json", Path.GetTempPath());
            Assert.Equal(1, none.ExitCode);
            Assert.Empty((JArray)JObject.Parse(none.Stdout)["data"]!["workspaces"]!);
        }
    }
}
