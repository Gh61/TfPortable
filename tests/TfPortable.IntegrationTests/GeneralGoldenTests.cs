using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TfPortable.IntegrationTests.Golden;
using Xunit;

namespace TfPortable.IntegrationTests
{
    /// <summary>
    /// Golden cases of the `general` group (docs/KICKOFF-CLOUD.md, milestone 2). The cases that need no server run on
    /// every Windows build of tfp.exe; the `status` cases run inside the fixture's workspace when the secrets exist.
    /// Expected exit codes follow docs/compat.md §2 (`outside-workspace` is 120 instead of the original's 100).
    /// </summary>
    [Collection(GoldenCollection.Name)]
    public class GeneralGoldenTests
    {
        private readonly GoldenWorkspaceFixture fixture;

        public GeneralGoldenTests(GoldenWorkspaceFixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>Deviations of docs/compat.md: case id → expected exit code of tfp.</summary>
        private static readonly Dictionary<string, int> ExitCodeDeviations = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["general/outside-workspace"] = 120,
        };

        public static IEnumerable<object[]> ServerlessCases()
        {
            yield return new object[] { "general/no-arguments" };
            yield return new object[] { "general/help" };
            yield return new object[] { "general/help-status" };
            yield return new object[] { "general/unknown-command" };
            yield return new object[] { "general/unknown-switch" };
            yield return new object[] { "general/outside-workspace" };
        }

        public static IEnumerable<object[]> WorkspaceCases()
        {
            yield return new object[] { "general/vc-prefix" };
            yield return new object[] { "general/dash-switches" };
            yield return new object[] { "general/short-switches" };
        }

        [Theory]
        [MemberData(nameof(ServerlessCases))]
        public void Serverless_case_matches_the_original(string id)
        {
            var golden = GoldenCase.Load(id);
            string cwd = Cwd(golden, serverless: true);
            var result = TfpRunner.Run(golden.Arguments, cwd);
            var differences = GoldenComparison.Compare(golden, result, Expected(golden), GoldenChecks.Shape);
            Assert.True(differences.Count == 0, $"{id}:\n{string.Join("\n", differences)}");
        }

        [IntegrationTheory]
        [MemberData(nameof(WorkspaceCases))]
        public void Workspace_case_matches_the_original(string id)
        {
            fixture.EnsureReady();
            var golden = GoldenCase.Load(id);
            string cwd = Cwd(golden, serverless: false);
            var result = TfpRunner.Run(golden.Arguments, cwd);
            var differences = GoldenComparison.Compare(golden, result, Expected(golden), GoldenChecks.All, fixture.Placeholders);
            Assert.True(differences.Count == 0, $"{id}:\n{string.Join("\n", differences)}");
        }

        [Fact]
        public void Outside_a_workspace_the_error_is_TFP0201_in_text_and_json()
        {
            string cwd = EmptyTempDirectory();
            var text = TfpRunner.Run("status /recursive /noprompt", cwd);
            Assert.Equal(120, text.ExitCode);
            Assert.StartsWith("ERROR TFP0201:", text.Stderr);

            var json = TfpRunner.Run("status /recursive /noprompt /format:json", cwd);
            Assert.Equal(120, json.ExitCode);
            Assert.Equal(string.Empty, json.Stderr);
            var envelope = JObject.Parse(json.Stdout);
            Assert.False((bool)envelope["ok"]!);
            Assert.Equal("status", (string)envelope["command"]!);
            Assert.Equal("TFP0201", (string)envelope["errors"]![0]!["code"]!);
        }

        [Fact]
        public void Help_json_from_the_exe_is_a_valid_envelope()
        {
            var result = TfpRunner.Run("help /format:json", EmptyTempDirectory());
            Assert.Equal(0, result.ExitCode);
            var envelope = JObject.Parse(result.Stdout);
            Assert.True((bool)envelope["ok"]!);
            Assert.NotEmpty((JArray)envelope["data"]!["commands"]!);
        }

        [IntegrationFact]
        public void Whoami_in_the_fixture_workspace_reports_identity_and_mapping()
        {
            fixture.EnsureReady();
            var result = TfpRunner.Run("whoami /format:json", fixture.WorkspaceRoot);
            Assert.True(result.ExitCode == 0, result.Stderr);
            var envelope = JObject.Parse(result.Stdout);
            Assert.True((bool)envelope["ok"]!);
            Assert.Equal($"{fixture.WorkspaceName};{fixture.OwnerDisplayName}", (string)envelope["workspace"]!);
            var data = envelope["data"]!;
            Assert.Equal(fixture.OwnerDisplayName, (string)data["identity"]!["displayName"]!);
            Assert.NotNull((string?)data["patStoredAt"]);
            Assert.Equal("local", (string)data["workspace"]!["location"]!);
            Assert.Equal(GoldenWorkspaceFixture.ServerRoot, (string)data["workspace"]!["mappings"]![0]!["serverPath"]!);
            Assert.DoesNotContain(Environment.GetEnvironmentVariable("TFP_TEST_PAT")!, result.Stdout + result.Stderr);
        }

        private static int Expected(GoldenCase golden) => ExitCodeDeviations.TryGetValue(golden.Id, out int exit) ? exit : golden.ExitCode;

        private string Cwd(GoldenCase golden, bool serverless)
        {
            string cwd = golden.Cwd;
            if (cwd.IndexOf("<USER>", StringComparison.Ordinal) >= 0)
            {
                // `C:\Users\<USER>\AppData\Local\Temp`: any folder outside a workspace.
                return EmptyTempDirectory();
            }

            if (serverless)
            {
                return EmptyTempDirectory();
            }

            return cwd.Replace("<WORKSPACE_ROOT>", fixture.WorkspaceRoot).Replace("<LOCAL_ROOT>", fixture.LocalRoot);
        }

        private static string EmptyTempDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "tfp-golden-empty");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
