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

        /// <summary>Asks the server what would stop a checkin of the changes: conflicts, policy and note failures (`checkin /validate`).</summary>
        CheckinEvaluation EvaluateCheckin(IReadOnlyList<PendingChangeInfo> changes, string comment);

        /// <summary>
        /// Checks the changes in and returns the changeset number (0 when the server committed nothing). `overrideReason`
        /// with the evaluation's policy handles records a policy override; the library's checkin exceptions propagate.
        /// </summary>
        int CheckIn(IReadOnlyList<PendingChangeInfo> changes, string comment, string? author, string? overrideReason, CheckinEvaluation? evaluation, bool noAutoResolve);

        /// <summary>Shelves the changes under `name` for the authenticated user; `replace` overwrites an existing shelveset, `move` undoes the local changes.</summary>
        void Shelve(string name, string? comment, IReadOnlyList<PendingChangeInfo> changes, bool replace, bool move);

        /// <summary>Restores a shelveset (null owner = the authenticated user, null items = everything) into the workspace.</summary>
        UnshelveResult Unshelve(string name, string? owner, IReadOnlyList<ItemQuery>? items, bool merge, bool noAutoResolve);

        /// <summary>Locks or unlocks items; the result lists the pending lock changes made or undone.</summary>
        PendResult SetLock(IReadOnlyList<string> paths, LockLevel level, RecursionType recursion);
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

        /// <summary>Changes of a shelveset (`status /shelveset`); null owner = the authenticated user. With download info the changes can be downloaded through <see cref="DownloadShelved(PendingChangeInfo)"/>.</summary>
        IReadOnlyList<PendingSetInfo> QueryShelvedChanges(string shelvesetName, string? owner, IReadOnlyList<ItemQuery> items, bool includeDownloadInfo = false);

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

        /// <summary>Content of every file item matching the query at a version, in one listing call plus one download per file.</summary>
        IReadOnlyList<ItemContent> DownloadItems(ItemQuery item, VersionSpec version, bool includeDeleted);

        /// <summary>Content of a file in a shelveset (null owner = the authenticated user), with the server item it is based on.</summary>
        ItemContent? DownloadShelved(string shelvesetName, string? owner, string serverPath);

        /// <summary>Content of a shelved change returned by <see cref="QueryShelvedChanges"/>; null when the change carries no content (a rename or a property change).</summary>
        ItemContent? DownloadShelved(PendingChangeInfo change);

        /// <summary>Labels matching the filters (null = any); items only when asked.</summary>
        IReadOnlyList<LabelInfo> QueryLabels(string? name, string? scope, string? owner, bool includeItems);

        /// <summary>Branch hierarchy of an item at a version: the roots of the tree that contains it.</summary>
        IReadOnlyList<BranchInfo> GetBranchHistory(string serverPath, VersionSpec version);

        IReadOnlyList<MergeCandidateInfo> GetMergeCandidates(string sourcePath, string targetPath, RecursionType recursion);

        /// <summary>Shelvesets by name and owner (null = any); the owner may be a unique or a display name.</summary>
        IReadOnlyList<ShelvesetInfo> QueryShelvesets(string? name, string? owner);

        /// <summary>Deletes a shelveset (null owner = the authenticated user); the library's exception when it does not exist.</summary>
        void DeleteShelveset(string name, string? owner);

        /// <summary>Creates or updates a label with the items; `childOption` is `fail`, `replace` or `merge`.</summary>
        LabelOperationResult CreateLabel(string name, string scope, string? comment, IReadOnlyList<LabelItemQuery> items, string childOption);

        LabelOperationResult DeleteLabel(string name, string scope);

        /// <summary>Removes items from a label; an item that is not in the label is a failure.</summary>
        LabelOperationResult UnlabelItems(string name, string scope, IReadOnlyList<ItemQuery> items);

        /// <summary>Links a work item to a changeset (the "Fixed in Changeset" artifact link, REST). Exceptions propagate.</summary>
        void LinkWorkItemToChangeset(int workItemId, int changesetId);
    }
}
