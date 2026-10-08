using System;
using System.Collections.Generic;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace TfPortable.Context
{
    /// <summary>
    /// A workspace of the collection. Every method that talks to the server throws the client library's exceptions;
    /// the caller maps them through ErrorMapper. Per-item problems that do not abort an operation come back as
    /// <see cref="FailureInfo"/> entries.
    /// </summary>
    internal interface ITfvcWorkspace
    {
        WorkspaceSummary Summary { get; }

        bool IsLocalPathMapped(string localPath);

        /// <summary>Server path of a mapped local path (the caller checks <see cref="IsLocalPathMapped"/> first).</summary>
        string GetServerItemForLocalItem(string localPath);

        /// <summary>Local path of a server path, or null when it is not mapped.</summary>
        string? GetLocalItemForServerItem(string serverPath);

        /// <summary>Pending changes and, when asked, the changes detected on disk (local workspaces only).</summary>
        PendingChangeSet GetPendingChanges(IReadOnlyList<ItemQuery> items, bool detectCandidates);

        /// <summary>Pends adds for local paths (wildcards allowed); `applyIgnore` honors .tfignore during expansion.</summary>
        PendResult PendAdd(IReadOnlyList<string> localPaths, bool recursive, string? encoding, bool applyIgnore);

        PendResult PendEdit(IReadOnlyList<string> paths, RecursionType recursion, string? encoding);

        PendResult PendDelete(IReadOnlyList<string> paths, RecursionType recursion);

        PendResult PendRename(string oldPath, string newPath);

        /// <summary>Undoes the pending changes of the items; the result lists the undone changes.</summary>
        PendResult Undo(IReadOnlyList<ItemQuery> items);

        /// <summary>Gets the items (null = the whole workspace) at a version; `flags` mirror the tf.exe switches.</summary>
        GetResult Get(IReadOnlyList<ItemQuery>? items, VersionSpec version, GetFlags flags);

        IReadOnlyList<ConflictInfo> QueryConflicts(IReadOnlyList<string> paths, bool recursive);

        /// <summary>Resolves one conflict with a canonical resolution name (`automerge`, `taketheirs`, ...).</summary>
        ResolveResult Resolve(ConflictInfo conflict, string resolution, string? newPath, bool forced);

        IReadOnlyList<LocalVersionInfo> GetLocalVersions(IReadOnlyList<ItemQuery> items);

        /// <summary>Local and server state of items (`properties`): pending change, lock, local and latest version.</summary>
        IReadOnlyList<ExtendedItemInfo> GetExtendedItems(IReadOnlyList<ItemQuery> items);
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

        /// <summary>Workspaces matching the filter (`workspaces` command); null filter fields mean "any".</summary>
        IReadOnlyList<WorkspaceSummary> QueryWorkspaces(WorkspaceQuery query);

        /// <summary>Refreshes the local workspace cache so that the next run finds the workspace without the server.</summary>
        void UpdateWorkspaceCache();

        /// <summary>Removes workspaces from the local cache (`workspaces /remove`); returns the names removed.</summary>
        IReadOnlyList<string> RemoveCachedWorkspaces(IReadOnlyList<string>? names);

        /// <summary>Pending changes of other workspaces (`status /user`): null user = everyone, null workspace = every workspace.</summary>
        IReadOnlyList<PendingSetInfo> QueryPendingSets(IReadOnlyList<ItemQuery> items, string? workspaceName, string? user);

        /// <summary>Changes of a shelveset (`status /shelveset`); null owner = the authenticated user.</summary>
        IReadOnlyList<PendingSetInfo> QueryShelvedChanges(string shelvesetName, string? owner, IReadOnlyList<ItemQuery> items);

        IReadOnlyList<ChangesetInfo> QueryHistory(HistoryQuery query);

        /// <summary>A changeset with its changes and work items; the library's exception for an unknown number.</summary>
        ChangesetInfo GetChangeset(int id);

        /// <summary>Ids of the changesets linked to a work item (one linking call).</summary>
        IReadOnlyCollection<int> GetWorkItemChangesets(int workItemId);

        int GetLatestChangesetId();

        /// <summary>Items at a version; `deletedState` true includes deleted items; folders only when asked.</summary>
        IReadOnlyList<ItemInfo> GetItems(ItemQuery item, VersionSpec version, bool includeDeleted, bool foldersOnly);

        /// <summary>One item at a version, or null when it does not exist there.</summary>
        ItemInfo? GetItem(string serverPath, VersionSpec version, int deletionId);

        /// <summary>Content of a file item at a version.</summary>
        ItemContent Download(string serverPath, VersionSpec version, int deletionId);

        /// <summary>Content of a file in a shelveset (null owner = the authenticated user), with the server item it is based on.</summary>
        ItemContent? DownloadShelved(string shelvesetName, string? owner, string serverPath);

        /// <summary>Labels matching the filters (null = any); items only when asked.</summary>
        IReadOnlyList<LabelInfo> QueryLabels(string? name, string? scope, string? owner, bool includeItems);

        /// <summary>Branch hierarchy of an item at a version: the roots of the tree that contains it.</summary>
        IReadOnlyList<BranchInfo> GetBranchHistory(string serverPath, VersionSpec version);

        IReadOnlyList<MergeCandidateInfo> GetMergeCandidates(string sourcePath, string targetPath, RecursionType recursion);
    }
}
