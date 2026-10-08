using System;
using System.IO;
using Newtonsoft.Json.Linq;
using TfPortable.IntegrationTests.Golden;
using Xunit;

namespace TfPortable.IntegrationTests
{
    /// <summary>
    /// The command forms of the existing Claude Code instructions (SPEC.md "Testování"): `vc` prefix, dash switches,
    /// forward-slash local paths in quotes, `-format:json`. They run inside the fixture's workspace against the test
    /// collection and leave it clean (every pending change is undone).
    /// </summary>
    [Collection(GoldenCollection.Name)]
    public class CompatibilityTests
    {
        private readonly GoldenWorkspaceFixture fixture;

        public CompatibilityTests(GoldenWorkspaceFixture fixture)
        {
            this.fixture = fixture;
        }

        private TfpResult Run(string arguments) => TfpRunner.Run(arguments, fixture.WorkspaceRoot);

        [IntegrationFact]
        public void Vc_prefix_and_dash_switches_of_the_claude_code_instructions()
        {
            fixture.EnsureReady();

            var changeset = Run("vc changeset 81 -noprompt");
            Assert.True(changeset.ExitCode == 0, changeset.Stderr);
            Assert.StartsWith("Changeset: 81", changeset.Stdout);

            string readme = fixture.WorkspaceRoot.Replace('\\', '/') + "/Main/README.md";
            var view = Run($"vc view \"{readme}\" -version:C83 -noprompt");
            Assert.True(view.ExitCode == 0, view.Stderr);
            Assert.Contains("TfPortable test project", view.Stdout);

            var workspaces = Run("vc workspaces -noprompt");
            Assert.True(workspaces.ExitCode == 0, workspaces.Stderr);
            Assert.Contains(fixture.WorkspaceName, workspaces.Stdout);

            var status = Run("status -recursive");
            Assert.True(status.ExitCode == 0, status.Stderr);
            Assert.Equal(string.Empty, status.Stderr);

            var diff = Run("diff Main\\README.md");
            Assert.True(diff.ExitCode == 0, diff.Stderr);
            Assert.Contains("Comparing local with latest:", diff.Stdout);

            var whoami = Run("whoami -format:json");
            Assert.True(whoami.ExitCode == 0, whoami.Stderr);
            Assert.True((bool)JObject.Parse(whoami.Stdout)["ok"]!);
        }

        [IntegrationFact]
        public void Add_rename_delete_with_dash_switches_are_reversible()
        {
            fixture.EnsureReady();
            string scratch = Path.Combine(fixture.WorkspaceRoot, "Main", "_compat");
            try
            {
                Directory.CreateDirectory(scratch);
                File.WriteAllText(Path.Combine(scratch, "a.txt"), "a\r\n");

                var add = Run("add Main\\_compat -recursive -noprompt -format:json");
                Assert.True(add.ExitCode == 0, add.Stderr);
                var added = JObject.Parse(add.Stdout);
                Assert.True((bool)added["ok"]!);
                Assert.Equal(2, ((JArray)added["data"]!["changes"]!).Count);

                var rename = Run("rename Main\\_compat\\a.txt Main\\_compat\\b.txt -noprompt");
                Assert.True(rename.ExitCode == 0, rename.Stderr);
                Assert.Contains("b.txt", rename.Stdout);

                var delete = Run("delete Main\\assets\\logo.png -noprompt");
                Assert.True(delete.ExitCode == 0, delete.Stderr);

                var status = Run("status -recursive -format:json");
                var changes = (JArray)JObject.Parse(status.Stdout)["data"]!["changes"]!;
                Assert.Contains(changes, c => (string)c["serverPath"]! == "$/TfsPortableTestRepo/Main/assets/logo.png");
                Assert.Contains(changes, c => (string)c["serverPath"]! == "$/TfsPortableTestRepo/Main/_compat/b.txt");
            }
            finally
            {
                Run("undo Main -recursive -noprompt");
                try
                {
                    if (Directory.Exists(scratch))
                    {
                        Directory.Delete(scratch, true);
                    }
                }
                catch (Exception)
                {
                    // Best effort; the fixture deletes everything at the end.
                }
            }

            var clean = Run("status -recursive -format:json");
            Assert.Equal(0, (int)JObject.Parse(clean.Stdout)["data"]!["counts"]!["changes"]!);
        }
    }
}
