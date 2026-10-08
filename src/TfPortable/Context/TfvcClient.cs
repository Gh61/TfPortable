using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Common;

namespace TfPortable.Context
{
    /// <summary>Implementation of <see cref="ITfvcClient"/> over Microsoft.TeamFoundation.VersionControl.Client.</summary>
    internal sealed class TfvcClient : ITfvcClient
    {
        private readonly TfsTeamProjectCollection connection;

        public TfvcClient(string normalizedCollectionUrl, TfsTeamProjectCollection connection)
        {
            CollectionUrl = normalizedCollectionUrl;
            this.connection = connection;
        }

        public string CollectionUrl { get; }

        private VersionControlServer VersionControl => connection.GetService<VersionControlServer>();

        public string AuthorizedUser => VersionControl.AuthorizedUser;

        public IdentityInfo Authenticate()
        {
            connection.EnsureAuthenticated();
            var identity = connection.AuthorizedIdentity;
            return new IdentityInfo(identity.DisplayName, identity.UniqueName, identity.TeamFoundationId.ToString());
        }

        public ITfvcWorkspace? GetWorkspace(string name, string? owner)
        {
            var vcs = VersionControl;
            string ownerName = string.IsNullOrEmpty(owner) ? vcs.AuthorizedUser : owner!;
            try
            {
                // The local cache first (no server round trip when tf.exe or VS created the workspace), then the server.
                var cached = Workstation.Current.GetLocalWorkspaceInfo(vcs, name, ownerName);
                var workspace = cached != null ? vcs.GetWorkspace(cached) : vcs.GetWorkspace(name, ownerName);
                return new TfvcWorkspace(workspace);
            }
            catch (WorkspaceNotFoundException)
            {
                return null;
            }
        }

        public IReadOnlyList<ITfvcWorkspace> QueryWorkspacesOnThisComputer()
        {
            var vcs = VersionControl;
            return vcs.QueryWorkspaces(null, vcs.AuthorizedUser, Environment.MachineName).Select(w => (ITfvcWorkspace)new TfvcWorkspace(w)).ToList();
        }

        public IReadOnlyList<WorkspaceSummary> QueryWorkspaces(WorkspaceQuery query)
        {
            return VersionControl.QueryWorkspaces(query.Name, query.Owner, query.Computer).Select(ToSummary).ToList();
        }

        public void UpdateWorkspaceCache()
        {
            var vcs = VersionControl;
            Workstation.Current.UpdateWorkspaceInfoCache(vcs, vcs.AuthorizedUser);
        }

        public IReadOnlyList<string> RemoveCachedWorkspaces(IReadOnlyList<string>? names)
        {
            var vcs = VersionControl;
            if (names == null)
            {
                return Workstation.Current.RemoveCachedWorkspaceInfo(vcs).Select(i => i.Name).ToList();
            }

            var removed = new List<string>();
            foreach (string name in names)
            {
                string[] parts = name.Split(new[] { ';' }, 2);
                removed.AddRange(Workstation.Current.RemoveCachedWorkspaceInfo(connection.Uri, parts[0], parts.Length > 1 ? parts[1] : vcs.AuthorizedUser).Select(i => i.Name));
            }

            return removed;
        }

        public IReadOnlyList<PendingSetInfo> QueryPendingSets(IReadOnlyList<ItemQuery> items, string? workspaceName, string? user)
        {
            var specs = items.Select(ToItemSpec).ToArray();
            return VersionControl.QueryPendingSets(specs, workspaceName, user, false, false).Select(ToPendingSet).ToList();
        }

        public IReadOnlyList<PendingSetInfo> QueryShelvedChanges(string shelvesetName, string? owner, IReadOnlyList<ItemQuery> items, bool includeDownloadInfo = false)
        {
            var vcs = VersionControl;
            var specs = items.Count == 0 ? null : items.Select(ToItemSpec).ToArray();
            return vcs.QueryShelvedChanges(shelvesetName, string.IsNullOrEmpty(owner) ? vcs.AuthorizedUser : owner, specs, includeDownloadInfo).Select(ToPendingSet).ToList();
        }

        public IReadOnlyList<ChangesetInfo> QueryHistory(HistoryQuery query)
        {
            return VersionControl
                .QueryHistory(query.Path, query.Version, query.DeletionId, query.Recursion, query.User, query.From, query.To, query.MaxCount, query.IncludeChanges, query.SlotMode, false, query.Ascending)
                .Cast<Changeset>()
                .Select(cs => ToChangeset(cs, query.IncludeChanges, query.IncludeWorkItems))
                .ToList();
        }

        public ChangesetInfo GetChangeset(int id) => ToChangeset(VersionControl.GetChangeset(id, true, false, true), true, true);

        public IReadOnlyCollection<int> GetWorkItemChangesets(int workItemId)
        {
            // One linking call from the work item side instead of one call per candidate changeset.
            var linking = connection.GetService<Microsoft.TeamFoundation.ILinking>();
            var artifacts = linking.GetReferencingArtifacts(new[] { $"vstfs:///WorkItemTracking/WorkItem/{workItemId}" }, null);
            const string prefix = "vstfs:///VersionControl/Changeset/";
            var ids = new HashSet<int>();
            if (artifacts == null)
            {
                return ids;
            }

            foreach (var artifact in artifacts)
            {
                string? uri = artifact?.Uri;
                if (uri != null && uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(uri.Substring(prefix.Length), out int id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        public int GetLatestChangesetId() => VersionControl.GetLatestChangesetId();

        public IReadOnlyList<ItemInfo> GetItems(ItemQuery item, VersionSpec version, bool includeDeleted, bool foldersOnly)
        {
            var set = VersionControl.GetItems(ToItemSpec(item), version, includeDeleted ? DeletedState.Any : DeletedState.NonDeleted, foldersOnly ? ItemType.Folder : ItemType.Any, GetItemsOptions.None);
            return set.Items.Select(ToItem).ToList();
        }

        public ItemInfo? GetItem(string serverPath, VersionSpec version, int deletionId)
        {
            var item = VersionControl.GetItem(serverPath, version, deletionId, GetItemsOptions.None);
            return item == null ? null : ToItem(item);
        }

        public ItemContent Download(string serverPath, VersionSpec version, int deletionId)
        {
            var item = VersionControl.GetItem(serverPath, version, deletionId, GetItemsOptions.Download);
            if (item == null)
            {
                throw new ItemNotFoundException($"The item {serverPath} does not exist at the specified version.");
            }

            if (item.ItemType == ItemType.Folder)
            {
                return new ItemContent(ToItem(item), Array.Empty<byte>());
            }

            return new ItemContent(ToItem(item), ReadAll(item.DownloadFile()));
        }

        public IReadOnlyList<ItemContent> DownloadItems(ItemQuery item, VersionSpec version, bool includeDeleted)
        {
            // One listing with download information, then one download per file: no metadata round trip per item.
            var set = VersionControl.GetItems(ToItemSpec(item), version, includeDeleted ? DeletedState.Any : DeletedState.NonDeleted, ItemType.File, GetItemsOptions.Download);
            return set.Items.Select(found => new ItemContent(ToItem(found), ReadAll(found.DownloadFile()))).ToList();
        }

        public ItemContent? DownloadShelved(string shelvesetName, string? owner, string serverPath)
        {
            var vcs = VersionControl;
            var sets = vcs.QueryShelvedChanges(shelvesetName, string.IsNullOrEmpty(owner) ? vcs.AuthorizedUser : owner, new[] { new ItemSpec(serverPath, RecursionType.None) }, true);
            var change = sets.SelectMany(s => s.PendingChanges).FirstOrDefault(c => string.Equals(c.ServerItem, serverPath, StringComparison.OrdinalIgnoreCase));
            return change == null ? null : ShelvedContent(change);
        }

        public ItemContent DownloadShelved(PendingChangeInfo change)
        {
            var handle = change.Handle as PendingChange ?? throw new ArgumentException("The shelved change carries no library handle; query it with download information.", nameof(change));
            return ShelvedContent(handle);
        }

        /// <summary>The shelved content as a stream of the library; no temp file.</summary>
        private static ItemContent ShelvedContent(PendingChange change)
        {
            byte[] bytes = ReadAll(change.DownloadShelvedFile());
            var info = new ItemInfo(change.ServerItem, change.ItemType == ItemType.Folder, change.Version, change.DeletionId, EncodingName(change.Encoding), bytes.Length, change.CreationDate, change.ItemId);
            return new ItemContent(info, bytes);
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (stream)
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        public IReadOnlyList<LabelInfo> QueryLabels(string? name, string? scope, string? owner, bool includeItems)
        {
            return VersionControl.QueryLabels(name, scope, owner, includeItems).Select(l => new LabelInfo(
                l.Name, l.Scope, l.OwnerDisplayName ?? l.OwnerName, l.OwnerName, l.LastModifiedDate, string.IsNullOrEmpty(l.Comment) ? null : l.Comment,
                includeItems ? (l.Items ?? Array.Empty<Item>()).Select(i => new LabelItemInfo(i.ServerItem, i.ChangesetId, i.DeletionId)).ToList() : null)).ToList();
        }

        public IReadOnlyList<BranchInfo> GetBranchHistory(string serverPath, VersionSpec version)
        {
            var trees = VersionControl.GetBranchHistory(new[] { new ItemSpec(serverPath, RecursionType.None) }, version);
            var roots = new List<BranchInfo>();
            foreach (var set in trees)
            {
                foreach (var root in set)
                {
                    roots.Add(ToBranch(root, null));
                }
            }

            return roots;
        }

        private static BranchInfo ToBranch(BranchHistoryTreeItem node, string? parent)
        {
            string path = node.Relative?.BranchToItem?.ServerItem ?? string.Empty;
            int from = node.Relative?.BranchFromItem?.ChangesetId ?? 0;
            var children = new List<BranchInfo>();
            foreach (BranchHistoryTreeItem child in node.Children)
            {
                children.Add(ToBranch(child, path));
            }

            return new BranchInfo(path, parent, from, node.Relative?.IsRequestedItem ?? false, children);
        }

        public IReadOnlyList<MergeCandidateInfo> GetMergeCandidates(string sourcePath, string targetPath, RecursionType recursion)
        {
            return VersionControl.GetMergeCandidates(sourcePath, targetPath, recursion).Select(c => new MergeCandidateInfo(ToChangeset(c.Changeset, false), c.Partial)).ToList();
        }

        public void Dispose() => connection.Dispose();

        internal static ItemSpec ToItemSpec(ItemQuery i) => new ItemSpec(i.Path, i.Recursion, i.DeletionId);

        internal static ChangesetInfo ToChangeset(Changeset cs) => ToChangeset(cs, false, false);

        internal static ChangesetInfo ToChangeset(Changeset cs, bool includeChanges) => ToChangeset(cs, includeChanges, false);

        /// <summary>
        /// Work items are read only on request: `Changeset.AssociatedWorkItems` is a linking call to the server on every access,
        /// so including them for every row of a history listing would cost one round trip per changeset.
        /// </summary>
        internal static ChangesetInfo ToChangeset(Changeset cs, bool includeChanges, bool includeWorkItems)
        {
            IReadOnlyList<ChangeInfo>? changes = null;
            IReadOnlyList<WorkItemInfo>? workItems = null;
            if (includeChanges)
            {
                changes = (cs.Changes ?? Array.Empty<Change>()).Select(c => new ChangeInfo(ChangeText(c.ChangeType), c.Item.ServerItem, c.Item.ChangesetId, c.Item.DeletionId,
                    (c.ChangeType & (ChangeType.Rename | ChangeType.Branch | ChangeType.Merge)) != 0 ? c.MergeSources?.FirstOrDefault()?.ServerItem : null,
                    c.Item.ItemType == ItemType.Folder, c.Item.ItemId, c.Item.ItemType == ItemType.Folder ? null : EncodingName(c.Item.Encoding))).ToList();
            }

            if (includeWorkItems)
            {
                try
                {
                    workItems = (cs.AssociatedWorkItems ?? Array.Empty<AssociatedWorkItemInfo>()).Select(w => new WorkItemInfo(w.Id, w.Title)).ToList();
                }
                catch (Exception)
                {
                    workItems = Array.Empty<WorkItemInfo>();
                }
            }

            return new ChangesetInfo(cs.ChangesetId, cs.OwnerDisplayName ?? cs.Owner, cs.Owner, cs.CreationDate, cs.Comment, changes, workItems);
        }

        internal static ItemInfo ToItem(Item item) =>
            new ItemInfo(item.ServerItem, item.ItemType == ItemType.Folder, item.ChangesetId, item.DeletionId,
                item.ItemType == ItemType.Folder ? null : EncodingName(item.Encoding), item.ItemType == ItemType.Folder ? null : (long?)item.ContentLength, item.CheckinDate, item.ItemId);

        internal static PendingSetInfo ToPendingSet(PendingSet set) =>
            new PendingSetInfo(set.Name, set.OwnerDisplayName ?? set.OwnerName, set.OwnerName, set.Computer, set.Type == PendingSetType.Shelveset,
                set.PendingChanges.Select(p => ToPendingChange(p, false)).ToList());

        internal static WorkspaceSummary ToSummary(Workspace workspace)
        {
            return new WorkspaceSummary(
                workspace.Name,
                workspace.OwnerDisplayName,
                workspace.OwnerName,
                workspace.Computer,
                string.IsNullOrEmpty(workspace.Comment) ? null : workspace.Comment,
                CollectionUrlOf(workspace),
                workspace.Location == WorkspaceLocation.Local,
                workspace.PermissionsProfile?.Name?.ToLowerInvariant(),
                workspace.LastAccessDate,
                workspace.Folders.Select(f => new MappingInfo(f.ServerItem, f.IsCloaked ? null : f.LocalItem, f.IsCloaked, f.Depth == RecursionType.OneLevel)).ToList());
        }

        private static string CollectionUrlOf(Workspace workspace)
        {
            try
            {
                return Auth.CollectionUrl.Normalize(workspace.VersionControlServer.TeamProjectCollection.Uri.AbsoluteUri);
            }
            catch (Exception)
            {
                return workspace.VersionControlServer.TeamProjectCollection.Uri.AbsoluteUri;
            }
        }

        internal static PendingChangeInfo ToPendingChange(PendingChange change, bool detected)
        {
            return new PendingChangeInfo(
                ChangeText(change.ChangeType),
                change.ServerItem,
                change.LocalItem,
                change.ItemType == ItemType.Folder,
                change.Version,
                change.IsRename || change.IsBranch || change.IsMerge ? change.SourceServerItem : null,
                LockText(change.LockLevel),
                change.ItemType == ItemType.Folder ? null : EncodingName(change.Encoding),
                change.CreationDate,
                change.PendingSetOwner,
                change.PendingSetName,
                detected,
                change);
        }

        /// <summary>Change types in the English wording of the client library ("edit", "merge, edit"), lower-case like tf.exe.</summary>
        internal static string ChangeText(ChangeType type) => PendingChange.GetLocalizedStringForChangeType(type).ToLowerInvariant();

        internal static string LockText(LockLevel level) => level == LockLevel.CheckOut ? "checkout" : level.ToString().ToLowerInvariant();

        internal static string? EncodingName(int codePage)
        {
            if (codePage == -1)
            {
                return "binary";
            }

            if (codePage <= 0)
            {
                return null;
            }

            try
            {
                return System.Text.Encoding.GetEncoding(codePage).WebName;
            }
            catch (Exception)
            {
                return codePage.ToString();
            }
        }

        /// <summary>Code page of an `/encoding:` value (`utf-8`, `windows-1250`, `binary`, or a number).</summary>
        internal static ConflictInfo ToConflict(Conflict c)
        {
            string type = c.Type.ToString().ToLowerInvariant();
            string? resolution = c.Resolution == Microsoft.TeamFoundation.VersionControl.Client.Resolution.None ? null : CanonicalResolution(c.Resolution);
            string message;
            try
            {
                message = c.GetBriefMessage();
            }
            catch (Exception)
            {
                message = c.ToString();
            }

            return new ConflictInfo(type, c.ServerPath ?? c.YourServerItem ?? c.TheirServerItem ?? string.Empty, c.LocalPath, c.YourVersion, c.TheirVersion, c.BaseVersion,
                ChangeText(c.YourChangeType), ChangeText(c.TheirChangeType), resolution, c.CanMergeContent, message, c.IsResolved, c);
        }

        /// <summary>docs/json-schema.md Resolution names for the library's enumeration.</summary>
        internal static string CanonicalResolution(Resolution resolution)
        {
            switch (resolution)
            {
                case Microsoft.TeamFoundation.VersionControl.Client.Resolution.AcceptMerge: return "automerge";
                case Microsoft.TeamFoundation.VersionControl.Client.Resolution.AcceptTheirs: return "taketheirs";
                case Microsoft.TeamFoundation.VersionControl.Client.Resolution.AcceptYours: return "keepyours";
                case Microsoft.TeamFoundation.VersionControl.Client.Resolution.OverwriteLocal: return "overwritelocal";
                case Microsoft.TeamFoundation.VersionControl.Client.Resolution.DeleteConflict: return "deleteconflict";
                case Microsoft.TeamFoundation.VersionControl.Client.Resolution.AcceptYoursRenameTheirs: return "keepyoursrenametheirs";
                default: return resolution.ToString().ToLowerInvariant();
            }
        }

        /// <summary>The library's enumeration for a canonical name (`automerge`, `automergeforced`, `taketheirs`, ...).</summary>
        internal static Resolution ToResolution(string canonical)
        {
            switch (canonical.ToLowerInvariant())
            {
                case "automerge":
                case "automergeforced": return Microsoft.TeamFoundation.VersionControl.Client.Resolution.AcceptMerge;
                case "taketheirs": return Microsoft.TeamFoundation.VersionControl.Client.Resolution.AcceptTheirs;
                case "keepyours": return Microsoft.TeamFoundation.VersionControl.Client.Resolution.AcceptYours;
                case "overwritelocal": return Microsoft.TeamFoundation.VersionControl.Client.Resolution.OverwriteLocal;
                case "deleteconflict": return Microsoft.TeamFoundation.VersionControl.Client.Resolution.DeleteConflict;
                case "keepyoursrenametheirs": return Microsoft.TeamFoundation.VersionControl.Client.Resolution.AcceptYoursRenameTheirs;
                default: throw new ArgumentException($"Unknown resolution '{canonical}'.", nameof(canonical));
            }
        }

        private static string OperationName(OperationStatus status)
        {
            switch (status)
            {
                case OperationStatus.Getting: return "getting";
                case OperationStatus.Replacing: return "replacing";
                case OperationStatus.Deleting: return "deleting";
                case OperationStatus.Conflict: return "conflict";
                case OperationStatus.SourceWritable:
                case OperationStatus.TargetWritable: return "writable";
                case OperationStatus.TargetLocalPending: return "localpending";
                case OperationStatus.SourceDirectoryNotEmpty: return "directorynotempty";
                case OperationStatus.TargetIsDirectory: return "targetisdirectory";
                default: return status.ToString().ToLowerInvariant();
            }
        }

        private static FailureInfo ToFailure(Failure f) =>
            new FailureInfo(f.LocalItem, f.ServerItem, f.Message ?? f.GetFormattedMessage(), f.Code, f.Severity == SeverityType.Warning);

        private sealed class TfvcWorkspace : ITfvcWorkspace
        {
            private readonly Workspace workspace;
            private WorkspaceSummary? summary;

            public TfvcWorkspace(Workspace workspace)
            {
                this.workspace = workspace;
            }

            public WorkspaceSummary Summary => summary ??= ToSummary(workspace);

            private VersionControlServer Vcs => workspace.VersionControlServer;

            public bool IsLocalPathMapped(string localPath) => workspace.IsLocalPathMapped(localPath);

            public string GetServerItemForLocalItem(string localPath) => workspace.GetServerItemForLocalItem(localPath);

            public string? GetLocalItemForServerItem(string serverPath)
            {
                try
                {
                    return workspace.IsServerPathMapped(serverPath) ? workspace.GetLocalItemForServerItem(serverPath) : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            public PendingChangeSet GetPendingChanges(IReadOnlyList<ItemQuery> items, bool detectCandidates)
            {
                var specs = items.Select(ToItemSpec).ToArray();
                bool detect = detectCandidates && workspace.Location == WorkspaceLocation.Local;
                PendingChange[] candidates = Array.Empty<PendingChange>();
                PendingChange[] pending = detect
                    ? workspace.GetPendingChangesWithCandidates(specs, false, out candidates)
                    : workspace.GetPendingChanges(specs);
                var detected = candidates != null ? candidates.Select(c => ToPendingChange(c, true)).ToList() : new List<PendingChangeInfo>();
                return new PendingChangeSet(pending.Select(p => ToPendingChange(p, false)).ToList(), detected);
            }

            public PendResult PendAdd(IReadOnlyList<string> localPaths, bool recursive, string? encoding, bool applyIgnore)
            {
                using var collector = new EventCollector(Vcs);
                int count = workspace.PendAdd(localPaths.ToArray(), recursive, FileType(encoding), LockLevel.None, false, false, applyIgnore);
                return collector.ToPendResult(count);
            }

            public PendResult PendEdit(IReadOnlyList<string> paths, RecursionType recursion, string? encoding)
            {
                using var collector = new EventCollector(Vcs);
                int count = workspace.PendEdit(paths.ToArray(), recursion, FileType(encoding), LockLevel.None, false);
                return collector.ToPendResult(count);
            }

            public PendResult PendDelete(IReadOnlyList<string> paths, RecursionType recursion)
            {
                using var collector = new EventCollector(Vcs);
                int count = workspace.PendDelete(paths.ToArray(), recursion, LockLevel.None, true, false);
                return collector.ToPendResult(count);
            }

            public PendResult PendRename(string oldPath, string newPath)
            {
                using var collector = new EventCollector(Vcs);
                int count = workspace.PendRename(oldPath, newPath, LockLevel.None, true, true, false);
                return collector.ToPendResult(count);
            }

            public PendResult Undo(IReadOnlyList<ItemQuery> items)
            {
                // The UndonePendingChange event reports a rename under its restored (old) name; tf.exe lists the pending
                // change as it was, so the changes are read before the undo and reported when the undo took them all.
                var specs = items.Select(ToItemSpec).ToArray();
                var before = workspace.GetPendingChanges(specs).Select(p => ToPendingChange(p, false)).ToList();
                using var collector = new EventCollector(Vcs);
                int count = workspace.Undo(specs, true);
                var undone = count > 0 && count == before.Count ? before : collector.Undone;
                return new PendResult(undone, collector.Failures, count);
            }

            public GetResult Get(IReadOnlyList<ItemQuery>? items, VersionSpec version, GetFlags flags)
            {
                var options = GetOptions.None;
                if (flags.HasFlag(GetFlags.All)) { options |= GetOptions.GetAll; }
                if (flags.HasFlag(GetFlags.Overwrite)) { options |= GetOptions.Overwrite; }
                if (flags.HasFlag(GetFlags.Preview)) { options |= GetOptions.Preview; }
                if (flags.HasFlag(GetFlags.Remap)) { options |= GetOptions.Remap; }
                if (flags.HasFlag(GetFlags.NoAutoResolve)) { options |= GetOptions.NoAutoResolve; }

                using var collector = new EventCollector(Vcs);
                GetStatus status;
                Conflict[]? conflicts = null;
                if (items == null || items.Count == 0)
                {
                    status = workspace.Get(version, options);
                }
                else
                {
                    var requests = items.Select(i => new GetRequest(ToItemSpec(i), version)).ToArray();
                    status = workspace.Get(requests, options, null, null, null, out conflicts);
                }

                var failures = new List<FailureInfo>(collector.Failures);
                failures.AddRange(status.GetFailures().Select(ToFailure));
                var conflictInfos = (conflicts ?? Array.Empty<Conflict>()).Select(ToConflict).ToList();
                if (conflictInfos.Count == 0 && status.NumConflicts > 0 && !flags.HasFlag(GetFlags.Preview))
                {
                    try
                    {
                        conflictInfos = workspace.QueryConflicts(items?.Select(i => i.Path).ToArray(), true).Select(ToConflict).ToList();
                    }
                    catch (Exception)
                    {
                        // The conflict list is informative; the counts still tell the truth.
                    }
                }

                return new GetResult(collector.Operations, conflictInfos, failures, status.NoActionNeeded, status.NumOperations, status.NumConflicts, status.NumWarnings);
            }

            public IReadOnlyList<ConflictInfo> QueryConflicts(IReadOnlyList<string> paths, bool recursive)
            {
                return workspace.QueryConflicts(paths.Count == 0 ? null : paths.ToArray(), recursive).Select(ToConflict).ToList();
            }

            public ResolveResult Resolve(ConflictInfo info, string resolution, string? newPath, bool forced)
            {
                var conflict = (Conflict)(info.Handle ?? throw new ArgumentException("The conflict has no handle.", nameof(info)));
                conflict.Resolution = ToResolution(resolution);
                if (newPath != null)
                {
                    conflict.ResolutionOptions = new ResolutionOptions { NewPath = newPath };
                }

                if (forced)
                {
                    conflict.ResolutionOptions = conflict.ResolutionOptions ?? new ResolutionOptions();
                    conflict.ResolutionOptions.AcceptMergeWithConflicts = true;
                }

                string resolutionName = conflict.Resolution.ToString();
                if (conflict.Resolution == Microsoft.TeamFoundation.VersionControl.Client.Resolution.AcceptYours)
                {
                    resolutionName = "KeepYours";
                }
                else if (conflict.Resolution == Microsoft.TeamFoundation.VersionControl.Client.Resolution.AcceptTheirs)
                {
                    resolutionName = "TakeTheirs";
                }

                using var collector = new EventCollector(Vcs);
                try
                {
                    workspace.ResolveConflict(conflict);
                }
                catch (Microsoft.TeamFoundation.TeamFoundationServerException ex)
                {
                    return new ResolveResult(false, resolutionName, MergeSummaryText(conflict), ex.Message);
                }

                bool resolved = conflict.IsResolved;
                string? why = resolved ? null : (collector.Failures.FirstOrDefault()?.Message ?? info.Message);
                return new ResolveResult(resolved, resolutionName, MergeSummaryText(conflict), why, collector.Operations);
            }

            private static string? MergeSummaryText(Conflict conflict)
            {
                var s = conflict.ContentMergeSummary;
                return s == null ? null : $"local: {s.TotalModified}, server: {s.TotalLatest}, both: {s.TotalBoth}, conflicting: {s.TotalConflicting}";
            }

            public IReadOnlyList<LocalVersionInfo> GetLocalVersions(IReadOnlyList<ItemQuery> items)
            {
                var result = new List<LocalVersionInfo>();
                foreach (var set in workspace.GetLocalVersions(items.Select(ToItemSpec).ToArray(), true))
                {
                    foreach (var v in set)
                    {
                        string? server = workspace.TryGetServerItemForLocalItem(v.Item);
                        result.Add(new LocalVersionInfo(v.Item, server, v.Version, Directory.Exists(v.Item)));
                    }
                }

                return result;
            }

            public IReadOnlyList<ExtendedItemInfo> GetExtendedItems(IReadOnlyList<ItemQuery> items)
            {
                var result = new List<ExtendedItemInfo>();
                foreach (var set in workspace.GetExtendedItems(items.Select(ToItemSpec).ToArray(), DeletedState.NonDeleted, ItemType.Any))
                {
                    foreach (var e in set)
                    {
                        result.Add(new ExtendedItemInfo(e.TargetServerItem ?? e.SourceServerItem, e.LocalItem, e.ItemType == ItemType.Folder, e.VersionLocal, e.VersionLatest,
                            e.ChangeType == ChangeType.None ? "none" : ChangeText(e.ChangeType), LockText(e.LockStatus),
                            string.IsNullOrEmpty(e.LockOwnerDisplayName) ? (string.IsNullOrEmpty(e.LockOwner) ? null : e.LockOwner) : e.LockOwnerDisplayName,
                            e.DeletionId, e.ItemType == ItemType.Folder ? null : EncodingName(e.Encoding), e.CheckinDate, e.IsInWorkspace, e.HasOtherPendingChange));
                    }
                }

                return result;
            }

            private static string? FileType(string? encoding) => string.IsNullOrWhiteSpace(encoding) ? null : encoding!.Trim();
        }

        /// <summary>Collects the events the library raises during one operation (new/undone pending changes, get operations, non-fatal errors).</summary>
        private sealed class EventCollector : IDisposable
        {
            private readonly VersionControlServer vcs;

            public EventCollector(VersionControlServer vcs)
            {
                this.vcs = vcs;
                vcs.NewPendingChange += OnNewPendingChange;
                vcs.UndonePendingChange += OnUndonePendingChange;
                vcs.NonFatalError += OnNonFatalError;
                vcs.Getting += OnGetting;
            }

            public List<PendingChangeInfo> Changes { get; } = new List<PendingChangeInfo>();

            public List<PendingChangeInfo> Undone { get; } = new List<PendingChangeInfo>();

            public List<FailureInfo> Failures { get; } = new List<FailureInfo>();

            public List<GetOperationInfo> Operations { get; } = new List<GetOperationInfo>();

            public PendResult ToPendResult(int count) => new PendResult(Changes, Failures, count);

            private void OnNewPendingChange(object sender, PendingChangeEventArgs e)
            {
                if (e.PendingChange != null)
                {
                    Changes.Add(ToPendingChange(e.PendingChange, false));
                }
            }

            private void OnUndonePendingChange(object sender, PendingChangeEventArgs e)
            {
                if (e.PendingChange != null)
                {
                    Undone.Add(ToPendingChange(e.PendingChange, false));
                }
            }

            private void OnNonFatalError(object sender, ExceptionEventArgs e)
            {
                if (e.Failure != null)
                {
                    Failures.Add(ToFailure(e.Failure));
                }
                else if (e.Exception != null)
                {
                    Failures.Add(new FailureInfo(null, null, e.Exception.Message, e.Exception.GetType().Name, false));
                }
            }

            private void OnGetting(object sender, GettingEventArgs e)
            {
                Operations.Add(new GetOperationInfo(OperationName(e.Status), e.TargetLocalItem ?? e.SourceLocalItem, e.ServerItem, e.Version, e.ItemType == ItemType.Folder, e.IsDelete));
            }

            public void Dispose()
            {
                vcs.NewPendingChange -= OnNewPendingChange;
                vcs.UndonePendingChange -= OnUndonePendingChange;
                vcs.NonFatalError -= OnNonFatalError;
                vcs.Getting -= OnGetting;
            }
        }
    }
}
