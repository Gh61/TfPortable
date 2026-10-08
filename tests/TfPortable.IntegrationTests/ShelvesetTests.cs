using System;
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
    /// Live tests of the shelveset reading commands against the permanent shelveset `tfp-golden-shelveset` of the test
    /// collection (created by the developer on 2026-10-08, any owner). Until `shelve` exists (phase 3) the tests cannot
    /// create one themselves. Nothing is written: no unshelve, no delete.
    /// </summary>
    [Collection(GoldenCollection.Name)]
    public class ShelvesetTests
    {
        private const string Name = "tfp-golden-shelveset";

        private readonly GoldenWorkspaceFixture fixture;

        public ShelvesetTests(GoldenWorkspaceFixture fixture)
        {
            this.fixture = fixture;
        }

        private TfpResult Run(string arguments) => TfpRunner.Run(arguments, fixture.WorkspaceRoot);

        /// <summary>`name;owner` of the permanent shelveset, found through the client library so that the owner need not be known.</summary>
        private string Spec()
        {
            string pat = Environment.GetEnvironmentVariable("TFP_TEST_PAT")!;
            var credentials = new VssCredentials(new Microsoft.VisualStudio.Services.Common.WindowsCredential(false), new VssBasicCredential(string.Empty, pat), CredentialPromptType.DoNotPrompt);
            using (var connection = new TfsTeamProjectCollection(new Uri(fixture.Collection.TrimEnd('/')), credentials))
            {
                connection.EnsureAuthenticated();
                var vcs = connection.GetService<VersionControlServer>();
                var found = vcs.QueryShelvesets(Name, null).FirstOrDefault();
                if (found == null)
                {
                    string visible = string.Join(", ", vcs.QueryShelvesets(null, vcs.AuthorizedUser).Select(s => s.Name));
                    throw new Xunit.Sdk.XunitException($"Shelveset {Name} was not found (any owner). Shelvesets of the CI identity: [{visible}].");
                }

                return $"{found.Name};{found.OwnerName}";
            }
        }

        [IntegrationFact]
        public void Status_difference_and_view_read_the_permanent_shelveset()
        {
            fixture.EnsureReady();
            string spec = Spec();

            var status = Run($"status /shelveset:{spec} /recursive /format:json");
            Assert.True(status.ExitCode == 0, status.Stderr);
            var changes = (JArray)JObject.Parse(status.Stdout)["data"]!["changes"]!;
            Assert.NotEmpty(changes);

            var diff = Run($"difference /shelveset:{spec} /format:unified /noprompt");
            Assert.True(diff.ExitCode == 0, diff.Stderr);
            Assert.Contains("Shelved change:", diff.Stdout);
            Assert.Contains("File:", diff.Stdout);

            var brief = Run($"difference /shelveset:{spec} /format:brief /noprompt");
            Assert.True(brief.ExitCode == 0, brief.Stderr);

            // A change with content can be viewed; one without (delete, rename-only) is TFP0301 with exit 1.
            var withContent = changes.FirstOrDefault(c => c["changeTypes"]!.Any(t => (string?)t == "edit" || (string?)t == "add"));
            if (withContent != null)
            {
                var view = Run($"view /shelveset:{spec} {(string)withContent["serverPath"]!} /noprompt");
                Assert.True(view.ExitCode == 0, view.Stderr);
                Assert.NotEqual(string.Empty, view.Stdout.Trim());
            }

            var withoutContent = changes.FirstOrDefault(c => !c["changeTypes"]!.Any(t => (string?)t == "edit" || (string?)t == "add" || (string?)t == "branch" || (string?)t == "merge" || (string?)t == "undelete"));
            if (withoutContent != null)
            {
                var view = Run($"view /shelveset:{spec} {(string)withoutContent["serverPath"]!} /noprompt");
                Assert.Equal(1, view.ExitCode);
                Assert.Contains("TFP0301", view.Stderr);
            }
        }
    }
}
