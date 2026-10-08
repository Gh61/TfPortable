using System;
using System.Collections.Generic;

namespace TfPortable.Context
{
    /// <summary>
    /// A workspace of the collection. Every method that talks to the server throws the client library's exceptions;
    /// the caller maps them through ErrorMapper.
    /// </summary>
    internal interface ITfvcWorkspace
    {
        WorkspaceSummary Summary { get; }

        bool IsLocalPathMapped(string localPath);

        /// <summary>Server path of a mapped local path (the caller checks <see cref="IsLocalPathMapped"/> first).</summary>
        string GetServerItemForLocalItem(string localPath);

        /// <summary>Pending changes and, when asked, the changes detected on disk (local workspaces only).</summary>
        PendingChangeSet GetPendingChanges(IReadOnlyList<ItemQuery> items, bool detectCandidates);
    }

    /// <summary>
    /// The only door to the Microsoft client libraries (SPEC.md "Požadavky na implementaci"): commands talk to this
    /// interface and get plain data back, so that parsing, formatting and error mapping have tests without a server.
    /// </summary>
    internal interface ITfvcClient : IDisposable
    {
        /// <summary>Normalized collection URL (credential key form).</summary>
        string CollectionUrl { get; }

        /// <summary>Authenticates (EnsureAuthenticated) and returns the identity the server sees.</summary>
        IdentityInfo Authenticate();

        /// <summary>Unique name of the authenticated user, for owner defaults.</summary>
        string AuthorizedUser { get; }

        /// <summary>A workspace by name and owner (unique or display name; null owner = the authenticated user). Null when it does not exist.</summary>
        ITfvcWorkspace? GetWorkspace(string name, string? owner);

        /// <summary>Workspaces of the authenticated user on this computer.</summary>
        IReadOnlyList<ITfvcWorkspace> QueryWorkspacesOnThisComputer();

        /// <summary>Refreshes the local workspace cache so that the next run finds the workspace without the server.</summary>
        void UpdateWorkspaceCache();

        IReadOnlyList<ChangesetInfo> QueryHistory(HistoryQuery query);
    }
}
