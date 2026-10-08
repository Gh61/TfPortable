using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.VersionControl.Client;
using TfPortable.Auth;
using TfPortable.Cli;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Context
{
    /// <summary>Resolved collection and (optionally) workspace for the current command.</summary>
    internal sealed class WorkspaceContext : IDisposable
    {
        public WorkspaceContext(string collectionUrl, ITfvcClient client, StoredPat? storedPat, ITfvcWorkspace? workspace, IReadOnlyList<string> searchedPaths)
        {
            CollectionUrl = collectionUrl;
            Client = client;
            StoredPat = storedPat;
            Workspace = workspace;
            SearchedPaths = searchedPaths;
        }

        /// <summary>Normalized collection URL (credential key form).</summary>
        public string CollectionUrl { get; }

        public ITfvcClient Client { get; }

        /// <summary>The stored PAT in use; null when the PAT came from /login.</summary>
        public StoredPat? StoredPat { get; }

        public ITfvcWorkspace? Workspace { get; }

        public IReadOnlyList<string> SearchedPaths { get; }

        /// <summary>`name;owner` as tf.exe prints it (display name of the owner, golden `<WORKSPACE>;<OWNER>`), or empty.</summary>
        public string WorkspaceSpec => Workspace?.Summary.Spec ?? string.Empty;

        /// <summary>
        /// Server path of an itemspec: server paths are used as given, local paths are mapped through the workspace;
        /// TFP0203 (exit 120) when the path is not mapped or there is no workspace.
        /// </summary>
        public string ServerPathOf(ParsedItemSpec item, string toolName)
        {
            if (item.IsServerPath)
            {
                return item.Path;
            }

            if (Workspace == null || !Workspace.IsLocalPathMapped(item.Path))
            {
                throw new TfpException(ErrorCodes.PathNotMapped, ExitCodes.WorkspaceNotFound,
                    Workspace == null
                        ? $"{item.Path} is a local path, but no workspace is known here."
                        : $"{item.Path} is not mapped in workspace {WorkspaceSpec}.",
                    $"Use a server path ($/Project/...) or run the command inside a mapped folder; '{toolName} workfold' shows the mappings.", item: item.Path);
            }

            return Workspace.GetServerItemForLocalItem(item.Path);
        }

        /// <summary>The workspace, or TFP0201 when the command needs one and none was found.</summary>
        public ITfvcWorkspace RequireWorkspace(string toolName)
        {
            if (Workspace == null)
            {
                throw ContextResolver.WorkspaceNotFound(toolName, Directory.GetCurrentDirectory(), SearchedPaths, null);
            }

            return Workspace;
        }

        public void Dispose() => Client.Dispose();
    }

    /// <summary>
    /// Detection of collection and workspace in the order of SPEC.md "Detekce workspace a kolekce":
    /// explicit switches, local workspace cache, TFP_COLLECTION or the single stored PAT plus a server query, otherwise TFP0201.
    /// </summary>
    internal static class ContextResolver
    {
        /// <summary>Opens a client for a collection URL and PAT; tests replace it with a fake.</summary>
        public static Func<string, string, ITfvcClient> ClientFactory { get; set; } =
            (url, pat) => new TfvcClient(CollectionUrl.Normalize(url), ConnectionFactory.Open(url, pat));

        /// <summary>
        /// Resolves the context for a command. Fills <see cref="OutputWriter.Collection"/> and <see cref="OutputWriter.Workspace"/>
        /// as soon as they are known so that error envelopes carry them too. With <paramref name="requireWorkspace"/> false
        /// (collection-only commands such as history or whoami) a missing workspace is not an error.
        /// </summary>
        public static WorkspaceContext Resolve(CommandLine commandLine, OutputWriter output, string toolName, bool requireWorkspace)
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

            if (info != null && explicitCollection != null && !string.Equals(CollectionUrl.Normalize(info.ServerUri.AbsoluteUri), CollectionUrl.Normalize(explicitCollection), StringComparison.OrdinalIgnoreCase))
            {
                // The folder belongs to a workspace of another collection; /collection wins and the server is asked.
                info = null;
            }

            string? collectionUrl = explicitCollection ?? info?.ServerUri.AbsoluteUri;
            if (collectionUrl == null)
            {
                // Step 3: environment, then the single stored PAT.
                collectionUrl = Environment.GetEnvironmentVariable("TFP_COLLECTION");
                if (string.IsNullOrWhiteSpace(collectionUrl))
                {
                    var stored = CredentialStore.EnumerateAll();
                    collectionUrl = stored.Count == 1 ? stored[0].Collection : null;
                }
            }

            if (collectionUrl == null)
            {
                throw WorkspaceNotFound(toolName, cwd, searched, null);
            }

            string normalized = CollectionUrl.Normalize(collectionUrl);
            output.Collection = normalized;

            // PAT: /login:user,pat for this run only (tf.exe compatibility), otherwise the stored one or the interactive prompt.
            StoredPat? storedPat = null;
            string pat;
            string? login = commandLine.Value("login");
            if (!string.IsNullOrEmpty(login))
            {
                pat = ParseLoginSwitch(login!, toolName);
                output.Warning("The PAT passed with /login is used for this run only and stays in the command history of your shell.",
                    hint: $"Prefer '{toolName} login /collection:{normalized}', which stores the token in the Windows Credential Manager.");
            }
            else
            {
                pat = ConnectionFactory.ResolvePat(collectionUrl, toolName, out storedPat);
            }

            var client = ClientFactory(collectionUrl, pat);
            ITfvcWorkspace? workspace = null;
            try
            {
                // Authenticate here so that every command fails at one place with one mapping.
                client.Authenticate();

                if (explicitWorkspace != null)
                {
                    string[] parts = explicitWorkspace.Split(new[] { ';' }, 2);
                    workspace = client.GetWorkspace(parts[0], parts.Length > 1 ? parts[1] : null);
                    if (workspace == null)
                    {
                        throw new TfpException(ErrorCodes.WorkspaceNotFound, ExitCodes.WorkspaceNotFound,
                            $"Workspace '{explicitWorkspace}' does not exist in {normalized} (or belongs to someone else).",
                            $"'{toolName} workspaces /collection:{normalized} /owner:*' lists the workspaces; pass /workspace:<name>;<owner> for another owner's workspace.");
                    }
                }
                else if (info != null)
                {
                    workspace = client.GetWorkspace(info.Name, info.OwnerName);
                }

                if (workspace == null && info == null && explicitWorkspace == null)
                {
                    // Step 3 continued: ask the server for this computer's workspaces and refresh the cache for next time.
                    var candidates = client.QueryWorkspacesOnThisComputer();
                    workspace = candidates.FirstOrDefault(w => w.IsLocalPathMapped(cwd));
                    if (workspace != null)
                    {
                        try
                        {
                            client.UpdateWorkspaceCache();
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

                if (workspace != null)
                {
                    output.Workspace = workspace.Summary.Spec;
                    if (!workspace.Summary.IsLocal)
                    {
                        throw new TfpException(ErrorCodes.ServerWorkspaceNotSupported, ExitCodes.WorkspaceNotFound,
                            $"Workspace {workspace.Summary.Spec} is a server workspace; {toolName} supports local workspaces only.",
                            "Convert it in Visual Studio: Manage Workspaces > Edit > Advanced > Location: Local.");
                    }
                }
                else if (requireWorkspace)
                {
                    throw WorkspaceNotFound(toolName, cwd, searched, null);
                }

                return new WorkspaceContext(normalized, client, storedPat, workspace, searched);
            }
            catch (Exception ex)
            {
                client.Dispose();
                var mapped = ErrorMapper.Map(ex, toolName, normalized);
                if (mapped.Code == ErrorCodes.PatRejected)
                {
                    var scope = ConnectionFactory.ExplainSoapRejection(collectionUrl, normalized, pat, toolName, ex);
                    if (scope != null)
                    {
                        throw scope;
                    }
                }

                throw;
            }
        }

        /// <summary>`/login:user,pat` of tf.exe: the user part is ignored (the PAT identifies the user), the PAT must not be empty.</summary>
        internal static string ParseLoginSwitch(string value, string toolName)
        {
            int comma = value.IndexOf(',');
            string pat = (comma < 0 ? value : value.Substring(comma + 1)).Trim();
            if (pat.Length == 0)
            {
                throw new TfpException(ErrorCodes.PatMissing, ExitCodes.Authentication,
                    "/login needs the form user,pat with a non-empty PAT.",
                    $"Prefer '{toolName} login /collection:<url>' in your own terminal; /login exists for tf.exe compatibility only.");
            }

            return pat;
        }

        internal static TfpException WorkspaceNotFound(string toolName, string cwd, IReadOnlyList<string> searched, IReadOnlyList<ITfvcWorkspace>? onThisComputer)
        {
            string found = onThisComputer == null
                ? "The server was not queried (no collection known)."
                : onThisComputer.Count == 0
                    ? "No workspaces of yours exist on this computer in that collection."
                    : "Workspaces on this computer: " + string.Join("; ", onThisComputer.Select(w => $"{w.Summary.Name} -> {string.Join(", ", w.Summary.Mappings.Where(m => !m.Cloaked).Select(m => m.LocalPath))}"));
            return new TfpException(ErrorCodes.WorkspaceNotFound, ExitCodes.WorkspaceNotFound,
                $"No local workspace maps {cwd}. Searched: {string.Join(", ", searched)}. {found}",
                $"Run the command inside a mapped folder, or pass /collection:<url> (and /workspace:<name>). '{toolName} workspaces /collection:<url>' lists them.");
        }
    }
}
