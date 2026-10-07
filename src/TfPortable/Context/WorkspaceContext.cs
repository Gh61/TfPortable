using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Common;
using TfPortable.Auth;
using TfPortable.Cli;
using TfPortable.Errors;

namespace TfPortable.Context
{
    /// <summary>Resolved collection and (optionally) workspace for the current command.</summary>
    internal sealed class WorkspaceContext : IDisposable
    {
        public WorkspaceContext(string collectionUrl, TfsTeamProjectCollection connection, StoredPat? storedPat, Workspace? workspace, IReadOnlyList<string> searchedPaths)
        {
            CollectionUrl = collectionUrl;
            Connection = connection;
            StoredPat = storedPat;
            Workspace = workspace;
            SearchedPaths = searchedPaths;
        }

        /// <summary>Normalized collection URL (credential key form).</summary>
        public string CollectionUrl { get; }

        public TfsTeamProjectCollection Connection { get; }

        public StoredPat? StoredPat { get; }

        public Workspace? Workspace { get; }

        public IReadOnlyList<string> SearchedPaths { get; }

        public VersionControlServer VersionControl => Connection.GetService<VersionControlServer>();

        public string WorkspaceSpec => Workspace == null ? string.Empty : $"{Workspace.Name};{Workspace.OwnerName}";

        public void Dispose() => Connection.Dispose();
    }

    /// <summary>
    /// Detection of collection and workspace in the order of SPEC.md "Detekce workspace a kolekce":
    /// explicit switches, local workspace cache, TFP_COLLECTION or the single stored PAT plus a server query, otherwise TFP0201.
    /// </summary>
    internal static class ContextResolver
    {
        public static WorkspaceContext Resolve(CommandLine commandLine, string toolName, bool requireWorkspace)
        {
            string cwd = Directory.GetCurrentDirectory();
            var searched = new List<string>();

            string? explicitCollection = commandLine.Value("collection");
            string? explicitWorkspace = commandLine.Value("workspace");

            // Step 2: local cache of the client libraries (also walks up to the drive root).
            WorkspaceInfo? info = null;
            if (explicitWorkspace == null)
            {
                string? dir = cwd;
                while (dir != null)
                {
                    searched.Add(dir);
                    info = Workstation.Current.GetLocalWorkspaceInfo(dir);
                    if (info != null)
                    {
                        break;
                    }

                    dir = Path.GetDirectoryName(dir);
                }
            }

            string? collectionUrl = explicitCollection ?? info?.ServerUri.AbsoluteUri;
            if (collectionUrl == null)
            {
                // Step 3: environment, then the single stored PAT.
                collectionUrl = Environment.GetEnvironmentVariable("TFP_COLLECTION");
                if (string.IsNullOrWhiteSpace(collectionUrl))
                {
                    var stored = CredentialStore.EnumerateAll();
                    if (stored.Count == 1)
                    {
                        collectionUrl = stored[0].Collection;
                    }
                    else
                    {
                        collectionUrl = null;
                    }
                }
            }

            if (collectionUrl == null)
            {
                throw WorkspaceNotFound(toolName, cwd, searched, null);
            }

            var connection = ConnectionFactory.Connect(collectionUrl, toolName, out var storedPat);
            string normalized = CollectionUrl.Normalize(collectionUrl);
            Workspace? workspace = null;
            try
            {
                var vcs = connection.GetService<VersionControlServer>();

                if (explicitWorkspace != null)
                {
                    string[] parts = explicitWorkspace.Split(new[] { ';' }, 2);
                    string owner = parts.Length > 1 ? parts[1] : vcs.AuthorizedUser;
                    workspace = vcs.GetWorkspace(parts[0], owner);
                }
                else if (info != null)
                {
                    workspace = info.GetWorkspace(connection);
                }
                else if (requireWorkspace || true)
                {
                    // Step 3 continued: ask the server for this computer's workspaces and refresh the cache for next time.
                    Workspace[] candidates = vcs.QueryWorkspaces(null, vcs.AuthorizedUser, Environment.MachineName);
                    workspace = candidates.FirstOrDefault(w => w.IsLocalPathMapped(cwd));
                    if (workspace != null)
                    {
                        try
                        {
                            Workstation.Current.UpdateWorkspaceInfoCache(vcs, vcs.AuthorizedUser);
                        }
                        catch (Exception)
                        {
                            // Cache refresh is an optimization only.
                        }
                    }
                    else if (requireWorkspace)
                    {
                        throw WorkspaceNotFound(toolName, cwd, searched, candidates);
                    }
                }

                if (workspace != null && workspace.Location == WorkspaceLocation.Server)
                {
                    throw new TfpException(ErrorCodes.ServerWorkspaceNotSupported, ExitCodes.WorkspaceNotFound,
                        $"Workspace {workspace.Name};{workspace.OwnerName} is a server workspace; {toolName} supports local workspaces only.",
                        "Convert it in Visual Studio: Manage Workspaces > Edit > Advanced > Location: Local.");
                }

                return new WorkspaceContext(normalized, connection, storedPat, workspace, searched);
            }
            catch (Exception)
            {
                connection.Dispose();
                throw;
            }
        }

        private static TfpException WorkspaceNotFound(string toolName, string cwd, IReadOnlyList<string> searched, Workspace[]? onThisComputer)
        {
            string found = onThisComputer == null
                ? "The server was not queried (no collection known)."
                : onThisComputer.Length == 0
                    ? "No workspaces of yours exist on this computer in that collection."
                    : "Workspaces on this computer: " + string.Join("; ", onThisComputer.Select(w => $"{w.Name} -> {string.Join(", ", w.Folders.Select(f => f.LocalItem))}"));
            return new TfpException(ErrorCodes.WorkspaceNotFound, ExitCodes.WorkspaceNotFound,
                $"No local workspace maps {cwd}. Searched: {string.Join(", ", searched)}. {found}",
                $"Run the command inside a mapped folder, or pass /collection:<url> (and /workspace:<name>). '{toolName} workspaces /collection:<url>' lists them.");
        }
    }
}
