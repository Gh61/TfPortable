using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Common;
using Microsoft.VisualStudio.Services.Common;
using Xunit;

namespace TfPortable.IntegrationTests.Golden
{
    /// <summary>
    /// The local workspace the golden tests replay in (docs/golden-format.md §6): created with the client library
    /// (never with tfp) in a temporary folder, mapped like the golden capture (`$/TfsPortableTestRepo` → `<WORKSPACE_ROOT>`),
    /// filled with `Get`, and deleted at the end. The PAT is stored for tfp.exe through `tfp login /pat:-` and removed
    /// with `tfp logout`. Nothing is created until <see cref="EnsureReady"/> is called, so skipped tests cost nothing.
    /// </summary>
    public sealed class GoldenWorkspaceFixture : IDisposable
    {
        private readonly object sync = new object();
        private TfsTeamProjectCollection? connection;
        private Workspace? workspace;
        private bool patStored;

        public const string ServerRoot = "$/TfsPortableTestRepo";

        /// <summary>Parent of the workspace root (`<LOCAL_ROOT>`).</summary>
        public string LocalRoot { get; } = Path.Combine(Path.GetTempPath(), "tfp-golden-" + Guid.NewGuid().ToString("N").Substring(0, 8));

        /// <summary>Local root of the mapping (`<WORKSPACE_ROOT>`).</summary>
        public string WorkspaceRoot => Path.Combine(LocalRoot, "TfsPortableTestRepo");

        public string WorkspaceName { get; } = "tfp-golden-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        public string Collection => TestEnvironment.Collection ?? throw new InvalidOperationException(TestEnvironment.SkipReason);

        public string OwnerDisplayName { get; private set; } = "<OWNER>";

        public bool IsReady => workspace != null;

        /// <summary>Values of the golden placeholders in this run (actual value → placeholder), for normalization (§7).</summary>
        public IReadOnlyDictionary<string, string> Placeholders
        {
            get
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [WorkspaceRoot] = "<WORKSPACE_ROOT>",
                    [LocalRoot] = "<LOCAL_ROOT>",
                    [WorkspaceName] = "<WORKSPACE>",
                    [Environment.MachineName] = "<COMPUTER>",
                    [Environment.UserName] = "<USER>",
                };
                if (TestEnvironment.Collection != null)
                {
                    map[TestEnvironment.Collection.TrimEnd('/')] = "<COLLECTION>";
                }

                if (OwnerDisplayName != "<OWNER>")
                {
                    map[OwnerDisplayName] = "<OWNER>";
                }

                return map;
            }
        }

        /// <summary>Creates the workspace and stores the PAT for tfp.exe. Throws when the secrets are missing.</summary>
        public void EnsureReady()
        {
            lock (sync)
            {
                if (workspace != null)
                {
                    return;
                }

                if (!TestEnvironment.IsConfigured)
                {
                    throw new InvalidOperationException(TestEnvironment.SkipReason);
                }

                string pat = Environment.GetEnvironmentVariable("TFP_TEST_PAT")!;
                Directory.CreateDirectory(WorkspaceRoot);

                var credentials = new VssCredentials(new Microsoft.VisualStudio.Services.Common.WindowsCredential(false), new VssBasicCredential(string.Empty, pat), CredentialPromptType.DoNotPrompt);
                connection = new TfsTeamProjectCollection(new Uri(Collection.TrimEnd('/')), credentials);
                connection.EnsureAuthenticated();
                OwnerDisplayName = connection.AuthorizedIdentity.DisplayName;

                var vcs = connection.GetService<VersionControlServer>();
                var parameters = new CreateWorkspaceParameters(WorkspaceName)
                {
                    Location = WorkspaceLocation.Local,
                    Comment = "TfPortable golden tests (temporary)",
                    Folders = new[] { new WorkingFolder(ServerRoot, WorkspaceRoot) },
                };
                workspace = vcs.CreateWorkspace(parameters);
                workspace.Get();

                // tfp.exe reads the PAT from the Credential Manager; store it the way a user would, through login.
                var login = TfpRunner.Run($"login /collection:{Collection.TrimEnd('/')} /pat:-", WorkspaceRoot, pat + Environment.NewLine);
                Assert.True(login.ExitCode == 0, $"tfp login failed with exit {login.ExitCode}: {login.Stderr}");
                patStored = true;
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (patStored)
                {
                    try
                    {
                        TfpRunner.Run($"logout /collection:{Collection.TrimEnd('/')}", Path.GetTempPath());
                    }
                    catch (Exception)
                    {
                        // Best effort; the CI runner is ephemeral.
                    }
                }

                if (workspace != null)
                {
                    try
                    {
                        workspace.Delete();
                    }
                    catch (Exception)
                    {
                        // Best effort: the test collection may keep a stale workspace; the name is unique per run.
                    }

                    workspace = null;
                }

                connection?.Dispose();
                connection = null;

                try
                {
                    if (Directory.Exists(LocalRoot))
                    {
                        foreach (string file in Directory.GetFiles(LocalRoot, "*", SearchOption.AllDirectories))
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                        }

                        Directory.Delete(LocalRoot, true);
                    }
                }
                catch (Exception)
                {
                    // Temp folder; leave it.
                }
            }
        }
    }

    [CollectionDefinition(Name)]
    public sealed class GoldenCollection : ICollectionFixture<GoldenWorkspaceFixture>
    {
        public const string Name = "golden";
    }
}
