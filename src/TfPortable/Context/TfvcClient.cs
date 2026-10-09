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

        public ItemContent? DownloadShelved(PendingChangeInfo change)
        {
            var handle = change.Handle as PendingChange ?? throw new ArgumentException("The pending change carries no library handle (every change of a server query has one).", nameof(change));
            return ShelvedContent(handle);
        }

        /// <summary>The shelved content as a stream of the library (no temp file); null when the change has no shelved file.</summary>
        private static ItemContent? ShelvedContent(PendingChange change)
        {
            byte[] bytes;
            try
            {
                bytes = ReadAll(change.DownloadShelvedFile());
            }
            catch (InvalidOperationException)
            {
                // The library's "PendingChangeIsNotAShelvedFile": a rename-only, lock or property change.
                return null;
            }

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

        public IReadOnlyList<MergeHistoryInfo> QueryMerges(string? sourcePath, string targetPath, RecursionType recursion, bool showAll)
        {
            return VersionControl.QueryMerges(sourcePath, sourcePath == null ? null : VersionSpec.Latest, targetPath, VersionSpec.Latest, null, null, recursion, showAll)
                .Select(m => new MergeHistoryInfo(m.SourceVersion, ToChangeset(m.TargetChangeset, false), m.Partial))
                .ToList();
        }

        public IReadOnlyList<MergedItemInfo> QueryMergedItems(string? sourcePath, string targetPath, RecursionType recursion, bool showAll)
        {
            var details = VersionControl.QueryMergesWithDetails(sourcePath, sourcePath == null ? null : VersionSpec.Latest, 0, targetPath, VersionSpec.Latest, 0, null, null, recursion, showAll);
            return (details?.MergedItems ?? Array.Empty<ItemMerge>())
                .Select(m => new MergedItemInfo(m.SourceServerItem, m.SourceVersionFrom, m.TargetServerItem, m.TargetVersionFrom))
                .ToList();
        }

        public IReadOnlyList<ShelvesetInfo> QueryShelvesets(string? name, string? owner)
        {
            return VersionControl.QueryShelvesets(string.IsNullOrEmpty(name) ? null : name, string.IsNullOrEmpty(owner) ? null : owner)
                .Select(s => new ShelvesetInfo(s.Name, s.OwnerDisplayName ?? s.OwnerName, s.OwnerName, s.CreationDate, string.IsNullOrEmpty(s.Comment) ? null : s.Comment))
                .ToList();
        }

        public void DeleteShelveset(string name, string? owner)
        {
            var vcs = VersionControl;
            vcs.DeleteShelveset(name, string.IsNullOrEmpty(owner) ? vcs.AuthorizedUser : owner);
        }

        public LabelOperationResult CreateLabel(string name, string scope, string? comment, IReadOnlyList<LabelItemQuery> items, string childOption)
        {
            var vcs = VersionControl;
            var label = new VersionControlLabel(vcs, name, vcs.AuthorizedUser, scope, comment);
            var specs = items.Select(i => new LabelItemSpec(ToItemSpec(i.Item), i.Version, i.Exclude)).ToArray();
            var option = string.Equals(childOption, "replace", StringComparison.OrdinalIgnoreCase) ? LabelChildOption.Replace
                : string.Equals(childOption, "merge", StringComparison.OrdinalIgnoreCase) ? LabelChildOption.Merge
                : LabelChildOption.Fail;
            var results = vcs.CreateLabel(label, specs, option, out Failure[] failures);
            return new LabelOperationResult(results.Select(ToLabelResult).ToList(), (failures ?? Array.Empty<Failure>()).Select(ToFailure).ToList());
        }

        public LabelOperationResult DeleteLabel(string name, string scope)
        {
            return new LabelOperationResult(VersionControl.DeleteLabel(name, scope).Select(ToLabelResult).ToList(), Array.Empty<FailureInfo>());
        }

        public LabelOperationResult UnlabelItems(string name, string scope, IReadOnlyList<ItemQuery> items)
        {
            // The version to remove is the label's own (the library's documented choice).
            var results = VersionControl.UnlabelItem(name, scope, items.Select(ToItemSpec).ToArray(), new LabelVersionSpec(name, scope), null, out Failure[] failures);
            return new LabelOperationResult(results.Select(ToLabelResult).ToList(), (failures ?? Array.Empty<Failure>()).Select(ToFailure).ToList());
        }

        public IReadOnlyList<CheckinPolicyInfo> GetCheckinPolicies(IReadOnlyList<string> serverPaths)
        {
            // PolicyEnvelope.Type is the display name the policy had when it was configured (localized by that Visual Studio);
            // the implementing class name is stable but not public, so it is read through reflection when available.
            var field = typeof(PolicyEnvelope).GetField("m_className", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var result = new List<CheckinPolicyInfo>();
            string[] paths = serverPaths.ToArray();
            // Current Visual Studio versions store the policies under the "client policies" annotation; the library's own
            // evaluation reads that key first and falls back to the legacy key, so does tfp.
            var envelopes = VersionControl.GetCheckinClientPoliciesForServerPaths(paths);
            if (envelopes == null || envelopes.Length == 0)
            {
#pragma warning disable CS0618 // The legacy annotation of older servers and clients.
                envelopes = VersionControl.GetCheckinPoliciesForServerPaths(paths);
#pragma warning restore CS0618
            }

            foreach (var envelope in envelopes ?? Array.Empty<PolicyEnvelope>())
            {
                if (!envelope.Enabled)
                {
                    continue;
                }

                string? className = field?.GetValue(envelope) as string;
                result.Add(new CheckinPolicyInfo(envelope.Type ?? className ?? "unknown", className));
            }

            return result;
        }

        public void LinkWorkItemToChangeset(int workItemId, int changesetId)
        {
            // The TFVC (SOAP) checkin links work items only through the work item tracking object model, which tfp does
            // not embed. The REST work item API adds the same "Fixed in Changeset" artifact link in one PATCH.
            var client = connection.GetClient<Microsoft.TeamFoundation.WorkItemTracking.WebApi.WorkItemTrackingHttpClient>();
            var patch = new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchDocument
            {
                new Microsoft.VisualStudio.Services.WebApi.Patch.Json.JsonPatchOperation
                {
                    Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
                    Path = "/relations/-",
                    Value = new
                    {
                        rel = "ArtifactLink",
                        url = $"vstfs:///VersionControl/Changeset/{changesetId}",
                        attributes = new { name = "Fixed in Changeset" },
                    },
                },
            };
            client.UpdateWorkItemAsync(patch, workItemId).GetAwaiter().GetResult();
        }

        private static LabelResultInfo ToLabelResult(LabelResult r) => new LabelResultInfo(r.Label, r.Scope, r.Status.ToString().ToLowerInvariant());

        public DestroyResult Destroy(ItemQuery item, VersionSpec? itemVersion, bool preview, bool keepHistory, VersionSpec? stopAt, bool startCleanup, bool silent)
        {
            var flags = DestroyFlags.None;
            if (keepHistory) { flags |= DestroyFlags.KeepHistory; }
            if (startCleanup) { flags |= DestroyFlags.StartCleanup; }
            if (silent) { flags |= DestroyFlags.Silent; }
            var spec = ToItemSpec(item);
            var version = itemVersion ?? VersionSpec.Latest;

            // The AffectedChanges flag (the pending and shelved changes a destroy touches) is accepted only with Preview and then empties
            // the item list; the test collection refused it with TF203086, so the destroy asks for the items only, as tf.exe prints them.
            var destroyed = VersionControl.Destroy(spec, version, stopAt, preview ? flags | DestroyFlags.Preview : flags);
            return new DestroyResult((destroyed ?? Array.Empty<Item>()).Select(i => new DestroyedItemInfo(i.ServerItem, i.DeletionId, i.ItemType == ItemType.Folder)).ToList());
        }

        public IReadOnlyList<ItemPermissionInfo> GetPermissions(IReadOnlyList<string> serverPaths, RecursionType recursion)
        {
            var securities = VersionControl.GetPermissions(serverPaths.ToArray(), recursion) ?? Array.Empty<ItemSecurity>();
            return securities.Select(s => new ItemPermissionInfo(s.ServerItem, s.Inherit,
                (s.Entries ?? Array.Empty<AccessEntry>()).Select(e => new PermissionEntryInfo(e.IdentityName,
                    e.Allow ?? Array.Empty<string>(), e.Deny ?? Array.Empty<string>(), e.AllowInherited ?? Array.Empty<string>(), e.DenyInherited ?? Array.Empty<string>())).ToList())).ToList();
        }

        public IReadOnlyList<PermissionChangeRequest> SetPermissions(IReadOnlyList<PermissionChangeRequest> changes)
        {
            var securityChanges = new List<SecurityChange>();
            foreach (var change in changes)
            {
                if (change.Inherit != null)
                {
                    securityChanges.Add(new InheritanceChange(change.ServerItem, change.Inherit.Value));
                }

                if (change.Identity != null && (change.Allow.Count > 0 || change.Deny.Count > 0 || change.Remove.Count > 0))
                {
                    securityChanges.Add(new PermissionChange(change.ServerItem, change.Identity, change.Allow.ToArray(), change.Deny.ToArray(), change.Remove.ToArray()));
                }
            }

            if (securityChanges.Count == 0)
            {
                return Array.Empty<PermissionChangeRequest>();
            }

            var applied = VersionControl.SetPermissions(securityChanges.ToArray()) ?? Array.Empty<SecurityChange>();
            var result = new List<PermissionChangeRequest>();
            foreach (var change in applied)
            {
                if (change is PermissionChange permission)
                {
                    result.Add(new PermissionChangeRequest(permission.Item, permission.IdentityName, permission.Allow ?? Array.Empty<string>(), permission.Deny ?? Array.Empty<string>(), permission.Remove ?? Array.Empty<string>(), null));
                }
                else if (change is InheritanceChange inheritance)
                {
                    result.Add(new PermissionChangeRequest(inheritance.Item, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), inheritance.Inherit));
                }
            }

            return result;
        }

        public WorkspaceSummary CreateWorkspace(WorkspaceDefinition definition)
        {
            var vcs = VersionControl;
            var parameters = new CreateWorkspaceParameters(definition.Name)
            {
                OwnerName = vcs.AuthorizedUser,
                Comment = definition.Comment ?? string.Empty,
                Computer = definition.Computer ?? Environment.MachineName,
                Location = WorkspaceLocation.Local,
                Folders = (definition.Mappings ?? Array.Empty<MappingInfo>()).Select(ToWorkingFolder).ToArray(),
                PermissionProfile = PermissionProfile(definition.Permission ?? "Private"),
                WorkspaceOptions = definition.FileTimeCheckin == true ? WorkspaceOptions.SetFileTimeToCheckin : WorkspaceOptions.None,
            };
            var workspace = vcs.CreateWorkspace(parameters);
            return ToSummary(workspace);
        }

        public WorkspaceSummary UpdateWorkspace(string name, string? owner, string? newName, WorkspaceDefinition changes)
        {
            var vcs = VersionControl;
            var workspace = vcs.GetWorkspace(name, string.IsNullOrEmpty(owner) ? vcs.AuthorizedUser : owner);
            var parameters = new UpdateWorkspaceParameters
            {
                WorkspaceName = newName,
                Comment = changes.Comment,
                Computer = changes.Computer,
                PermissionProfile = PermissionProfile(changes.Permission),
                WorkspaceOptions = changes.FileTimeCheckin == null ? (WorkspaceOptions?)null : changes.FileTimeCheckin.Value ? WorkspaceOptions.SetFileTimeToCheckin : WorkspaceOptions.None,
            };
            workspace.Update(parameters);
            return ToSummary(vcs.GetWorkspace(newName ?? name, workspace.OwnerName));
        }

        public bool DeleteWorkspace(string name, string? owner)
        {
            var vcs = VersionControl;
            string ownerName = string.IsNullOrEmpty(owner) ? vcs.AuthorizedUser : owner!;
            bool deleted = vcs.DeleteWorkspace(name, ownerName);
            try
            {
                Workstation.Current.RemoveCachedWorkspaceInfo(connection.Uri, name, ownerName);
            }
            catch (Exception)
            {
                // The cache entry is an optimization; the server is the truth.
            }

            return deleted;
        }

        public IReadOnlyList<ItemPropertiesInfo> GetItemProperties(IReadOnlyList<ItemQuery> items, VersionSpec version)
        {
            // GetItems with a property filter returns the versioned-item properties on every item (the dedicated property query is obsolete).
            var sets = VersionControl.GetItems(items.Select(ToItemSpec).ToArray(), version, DeletedState.Any, ItemType.Any, GetItemsOptions.None, new[] { "*" }, null)
                ?? Array.Empty<ItemSet>();
            var result = new List<ItemPropertiesInfo>();
            foreach (var item in sets.Where(s => s?.Items != null).SelectMany(s => s.Items))
            {
                var properties = (item.PropertyValues ?? (IList<Microsoft.TeamFoundation.Framework.Client.PropertyValue>)new List<Microsoft.TeamFoundation.Framework.Client.PropertyValue>())
                    .Select(p => new KeyValuePair<string, string?>(p.PropertyName, p.Value?.ToString()))
                    .ToList();
                result.Add(new ItemPropertiesInfo(item.ServerItem, properties));
            }

            return result;
        }

        public int CheckInShelveset(string name, string? owner, string? author, bool noAutoResolve, bool allowUnchangedContent)
        {
            var vcs = VersionControl;
            var parameters = new ShelvesetCheckInParameters(name, string.IsNullOrEmpty(owner) ? vcs.AuthorizedUser : owner!)
            {
                NoAutoResolve = noAutoResolve,
                AllowUnchangedContent = allowUnchangedContent,
                DeleteShelveset = true,
            };
            if (!string.IsNullOrEmpty(author))
            {
                parameters.Author = author;
            }

            return vcs.CheckInShelveset(parameters);
        }

        public ChangesetInfo UpdateChangeset(int id, string? comment, IReadOnlyList<KeyValuePair<string, string>>? notes)
        {
            var vcs = VersionControl;
            var changeset = vcs.GetChangeset(id, false, false);
            if (comment != null)
            {
                changeset.Comment = comment;
            }

            if (notes != null)
            {
                changeset.CheckinNote = new CheckinNote(notes.Select(n => new CheckinNoteFieldValue(n.Key, n.Value)).ToArray());
            }

            changeset.Update();
            return GetChangeset(id);
        }

        private static WorkingFolder ToWorkingFolder(MappingInfo mapping) =>
            mapping.Cloaked
                ? new WorkingFolder(mapping.ServerPath, null, WorkingFolderType.Cloak)
                : new WorkingFolder(mapping.ServerPath, mapping.LocalPath, WorkingFolderType.Map, mapping.OneLevel ? RecursionType.OneLevel : RecursionType.Full);

        /// <summary>The built-in profile by its tf.exe name (`Private`, `PublicLimited`, `Public`), or null.</summary>
        private static WorkspacePermissionProfile? PermissionProfile(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var profiles = WorkspacePermissionProfile.BuiltInProfiles;
            var byName = profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
            {
                return byName;
            }

            // The built-in profiles are Private, PublicLimited, Public in this order (tf.exe `/permission:`); their display names may be localized.
            int index = Array.FindIndex(new[] { "Private", "PublicLimited", "Public" }, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index < profiles.Length)
            {
                return profiles[index];
            }

            throw new ArgumentException($"Unknown workspace permission profile '{name}'.");
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
                item.ItemType == ItemType.Folder ? null : EncodingName(item.Encoding), item.ItemType == ItemType.Folder ? null : (long?)item.ContentLength, item.CheckinDate, item.ItemId,
                item.ItemType == ItemType.Folder || item.HashValue == null || item.HashValue.Length == 0 ? null : BitConverter.ToString(item.HashValue).Replace("-", string.Empty).ToLowerInvariant());

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
                change,
                (change.MergeSources == null ? new List<MergeSourceInfo>() : change.MergeSources.Select(m => new MergeSourceInfo(m.ServerItem, m.VersionFrom, m.VersionTo, m.IsRename)).ToList()));
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

            public void Map(string serverPath, string localPath)
            {
                workspace.Map(serverPath, localPath);
                summary = null;
            }

            public bool Unmap(string path)
            {
                var folder = workspace.Folders.FirstOrDefault(f => string.Equals(f.ServerItem, path, StringComparison.OrdinalIgnoreCase)
                    || (!f.IsCloaked && f.LocalItem != null && string.Equals(f.LocalItem.TrimEnd(Path.DirectorySeparatorChar), path.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)));
                if (folder == null)
                {
                    return false;
                }

                workspace.DeleteMapping(folder);
                summary = null;
                return true;
            }

            public void Cloak(string serverPath)
            {
                workspace.Cloak(serverPath);
                summary = null;
            }

            public string? ResolvePath(string path, bool isServerPath)
            {
                try
                {
                    if (isServerPath)
                    {
                        return workspace.IsServerPathMapped(path) ? workspace.GetLocalItemForServerItem(path) : null;
                    }

                    return workspace.IsLocalPathMapped(path) ? workspace.GetServerItemForLocalItem(path) : null;
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

            public CheckinEvaluation EvaluateCheckin(IReadOnlyList<PendingChangeInfo> changes, string comment, IReadOnlyList<PolicyResultInfo> policies)
            {
                // Policies are left out: the library would load them from the registry of a Visual Studio installation,
                // which a single tfp.exe does not have. tfp evaluates the policies it knows itself (CheckinPolicies).
                var pending = Handles(changes);
                CheckinEvaluationResult result = workspace.EvaluateCheckin2(CheckinEvaluationOptions.Conflicts | CheckinEvaluationOptions.Notes, pending, comment, null, null);
                var conflicts = (result.Conflicts ?? Array.Empty<CheckinConflict>()).Select(c => new CheckinConflictInfo(c.ServerItem, c.Message, c.Resolvable)).ToList();
                var notes = (result.NoteFailures ?? Array.Empty<CheckinNoteFailure>()).Select(n => n.Message).ToList();
                return new CheckinEvaluation(conflicts, notes, policies);
            }

            public int CheckIn(IReadOnlyList<PendingChangeInfo> changes, string comment, string? author, string? overrideReason, IReadOnlyList<string> overriddenFailures, bool noAutoResolve, bool allowUnchangedContent)
            {
                var parameters = new WorkspaceCheckInParameters(Handles(changes), comment)
                {
                    NoAutoResolve = noAutoResolve,
                    AllowUnchangedContent = allowUnchangedContent,
                };
                if (!string.IsNullOrEmpty(author))
                {
                    parameters.Author = author;
                }

                if (overrideReason != null)
                {
                    parameters.PolicyOverride = new PolicyOverrideInfo(overrideReason, overriddenFailures.Select(f => new PolicyFailure(f)).ToArray());
                }

                return workspace.CheckIn(parameters);
            }

            public void Shelve(string name, string? comment, IReadOnlyList<PendingChangeInfo> changes, bool replace, bool move)
            {
                var shelveset = new Shelveset(Vcs, name, Vcs.AuthorizedUser) { Comment = comment ?? string.Empty };
                var options = ShelvingOptions.None;
                if (replace) { options |= ShelvingOptions.Replace; }
                if (move) { options |= ShelvingOptions.Move; }
                workspace.Shelve(shelveset, Handles(changes), options);
            }

            public UnshelveResult Unshelve(string name, string? owner, IReadOnlyList<ItemQuery>? items, bool merge, bool noAutoResolve)
            {
                using var collector = new EventCollector(Vcs);
                var specs = items == null || items.Count == 0 ? null : items.Select(ToItemSpec).ToArray();
                workspace.Unshelve(name, string.IsNullOrEmpty(owner) ? Vcs.AuthorizedUser : owner, specs, null, null, null, merge, noAutoResolve,
                    out PendingChange[] changes, out Conflict[] conflicts);
                var restored = (changes ?? Array.Empty<PendingChange>()).Select(c => ToPendingChange(c, false)).ToList();
                if (restored.Count == 0 && collector.Changes.Count > 0)
                {
                    restored = collector.Changes;
                }

                var open = (conflicts ?? Array.Empty<Conflict>()).Where(c => !c.IsResolved).Select(ToConflict).ToList();
                var failures = new List<FailureInfo>(collector.Failures);
                foreach (var op in collector.Operations.Where(o => o.Operation == "writable"))
                {
                    // A writable local file in the way of the unshelved item (golden `unshelve/local-file-in-the-way`, exit 1).
                    failures.Add(new FailureInfo(op.LocalPath, op.ServerPath, $"{op.LocalPath ?? op.ServerPath} was not written: a writable file with the same name exists locally.", "TargetWritable", false));
                }

                return new UnshelveResult(restored, open, failures);
            }

            public PendResult SetLock(IReadOnlyList<string> paths, LockLevel level, RecursionType recursion)
            {
                using var collector = new EventCollector(Vcs);
                int count = workspace.SetLock(paths.ToArray(), level, recursion, false);
                var changes = level == LockLevel.None && collector.Changes.Count == 0 ? collector.Undone : collector.Changes;
                return new PendResult(changes, collector.Failures, count);
            }

            public MergeResult Merge(ItemQuery source, string targetPath, VersionSpec? versionFrom, VersionSpec? versionTo, MergeFlags flags)
            {
                var options = MergeOptionsEx.None;
                if (flags.HasFlag(MergeFlags.Force)) { options |= MergeOptionsEx.ForceMerge; }
                if (flags.HasFlag(MergeFlags.Baseless)) { options |= MergeOptionsEx.Baseless; }
                if (flags.HasFlag(MergeFlags.Discard)) { options |= MergeOptionsEx.AlwaysAcceptMine; }
                if (flags.HasFlag(MergeFlags.Preview)) { options |= MergeOptionsEx.NoMerge; }
                if (flags.HasFlag(MergeFlags.NoAutoResolve)) { options |= MergeOptionsEx.NoAutoResolve; }
                if (flags.HasFlag(MergeFlags.NoImplicitBaseless)) { options |= MergeOptionsEx.NoImplicitBaseless; }
                if (flags.HasFlag(MergeFlags.Conservative)) { options |= MergeOptionsEx.Conservative; }

                using var collector = new EventCollector(Vcs);
                GetStatus status = workspace.Merge(ToItemSpec(source), targetPath, versionFrom, versionTo, LockLevel.None, options);
                var result = ToMergeResult(status, collector, new[] { targetPath }, source.Recursion != RecursionType.None);
                if (flags.HasFlag(MergeFlags.Discard) && result.Changes.Count == 0)
                {
                    // A discard merge raises no pending-change events in the library: the pending merges under the target are the result.
                    var pended = workspace.GetPendingChanges(new[] { new ItemSpec(targetPath, source.Recursion) })
                        .Where(p => (p.ChangeType & ChangeType.Merge) != 0)
                        .Select(p => ToPendingChange(p, false))
                        .ToList();
                    return new MergeResult(pended, result.Conflicts, result.Failures, Math.Max(result.NumOperations, pended.Count), result.NumConflicts, result.NoActionNeeded && pended.Count == 0, result.ConflictLines);
                }

                return result;
            }

            public PendResult PendBranch(string sourcePath, string targetPath, VersionSpec version, bool updateDisk, bool silent)
            {
                using var collector = new EventCollector(Vcs);
                int count = workspace.PendBranch(sourcePath, targetPath, version, LockLevel.None, updateDisk, true, silent);
                if (silent && collector.Changes.Count == 0 && count > 0)
                {
                    // The silent form raises no events; the pending branch exists all the same (golden `branch/silent`).
                    var pended = workspace.GetPendingChanges(new[] { new ItemSpec(targetPath, RecursionType.Full) })
                        .Where(p => (p.ChangeType & ChangeType.Branch) != 0)
                        .Select(p => ToPendingChange(p, false))
                        .ToList();
                    return new PendResult(pended, collector.Failures, count);
                }

                return collector.ToPendResult(count);
            }

            public PendResult PendUndelete(IReadOnlyList<ItemQuery> items, bool updateDisk)
            {
                using var collector = new EventCollector(Vcs);
                int count = workspace.PendUndelete(items.Select(ToItemSpec).ToArray(), LockLevel.None, updateDisk, true);
                return collector.ToPendResult(count);
            }

            public MergeResult Rollback(IReadOnlyList<ItemQuery>? items, VersionSpec? itemVersion, VersionSpec? versionFrom, VersionSpec versionTo, bool toVersion, bool keepMergeHistory, bool noAutoResolve)
            {
                var options = RollbackOptions.None;
                if (toVersion) { options |= RollbackOptions.ToVersion; }
                if (keepMergeHistory) { options |= RollbackOptions.KeepMergeHistory; }
                if (noAutoResolve) { options |= RollbackOptions.NoAutoResolve; }

                // The library needs both versions in both modes.
                versionFrom ??= VersionSpec.Latest;
                using var collector = new EventCollector(Vcs);
                collector.ConflictChangeText = "rollback";
                GetStatus status;
                if (items == null || items.Count == 0)
                {
                    // Without items the library's workspace-wide overload pends nothing on a local workspace:
                    // the mapped folders of the workspace, recursively, are the scope instead (as `rollback /changeset:C85` of tf.exe).
                    var roots = workspace.Folders.Where(f => !f.IsCloaked).Select(f => f.ServerItem).ToArray();
                    status = workspace.Rollback(roots, RecursionType.Full, null, versionFrom, versionTo, LockLevel.None, options, null);
                }
                else
                {
                    var recursion = items.Max(i => i.Recursion);
                    status = workspace.Rollback(items.Select(i => i.Path).ToArray(), recursion, itemVersion, versionFrom, versionTo, LockLevel.None, options, null);
                }

                return ToMergeResult(status, collector, items?.Select(i => i.Path).ToArray(), items != null && items.Any(i => i.Recursion != RecursionType.None));
            }

            private MergeResult ToMergeResult(GetStatus status, EventCollector collector, string[]? conflictPaths, bool recursive)
            {
                // The library raises the NonFatalError event for the failures it also returns: keep each one once.
                var failures = new List<FailureInfo>(collector.Failures);
                foreach (var failure in status.GetFailures().Select(ToFailure))
                {
                    if (!failures.Any(f => f.Message == failure.Message && f.ServerPath == failure.ServerPath && f.LocalPath == failure.LocalPath))
                    {
                        failures.Add(failure);
                    }
                }

                var conflicts = new List<ConflictInfo>();
                if (status.NumConflicts > 0)
                {
                    try
                    {
                        conflicts = workspace.QueryConflicts(conflictPaths, recursive).Where(c => !c.IsResolved).Select(ToConflict).ToList();
                    }
                    catch (Exception)
                    {
                        // The conflict list is informative; the counts still tell the truth.
                    }
                }

                return new MergeResult(collector.Changes, conflicts, failures, status.NumOperations, status.NumConflicts, status.NoActionNeeded, collector.ConflictLines);
            }

            /// <summary>The library's pending changes behind the infos (from a query of this workspace).</summary>
            private static PendingChange[] Handles(IReadOnlyList<PendingChangeInfo> changes)
            {
                var handles = changes.Select(c => c.Handle as PendingChange).Where(h => h != null).Select(h => h!).ToArray();
                if (handles.Length != changes.Count)
                {
                    throw new InvalidOperationException("A pending change without its library handle cannot be written to the server.");
                }

                return handles;
            }
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
                vcs.Merging += OnMerging;
            }

            public List<PendingChangeInfo> Changes { get; } = new List<PendingChangeInfo>();

            public List<PendingChangeInfo> Undone { get; } = new List<PendingChangeInfo>();

            public List<FailureInfo> Failures { get; } = new List<FailureInfo>();

            /// <summary>The conflicting merge or rollback operations (the Merging event with a conflict status), for the `Conflict (...)` lines.</summary>
            public List<PendingChangeInfo> ConflictLines { get; } = new List<PendingChangeInfo>();

            /// <summary>Change text for a conflict whose event carries no change type ("merge" or "rollback").</summary>
            public string ConflictChangeText { get; set; } = "merge";

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

            /// <summary>
            /// Merge and rollback operations arrive through the Merging event (the library raises neither NewPendingChange nor Getting
            /// for them): a pended change when there is one, else the operation as the event describes it (a preview).
            /// Conflicts are left to the conflict query and the counts.
            /// </summary>
            private void OnMerging(object sender, MergeEventArgs e)
            {
                if (e.IsConflict || e.Status == OperationStatus.Conflict)
                {
                    if (!string.IsNullOrEmpty(e.TargetServerItem))
                    {
                        var conflictSources = string.IsNullOrEmpty(e.SourceServerItem)
                            ? new List<MergeSourceInfo>()
                            : new List<MergeSourceInfo> { new MergeSourceInfo(e.SourceServerItem, e.SourceVersionFrom?.ChangesetId ?? 0, e.SourceVersionTo?.ChangesetId ?? 0, false) };
                        string changeText = e.ChangeType == ChangeType.None ? ConflictChangeText : ChangeText(e.ChangeType);
                        ConflictLines.Add(new PendingChangeInfo(changeText, e.TargetServerItem, e.TargetLocalItem, false, e.TargetVersionPended?.ChangesetId ?? 0,
                            conflictSources.Count > 0 ? e.SourceServerItem : null, "none", null, DateTime.UtcNow, string.Empty, string.Empty, false, null, conflictSources));
                    }

                    return;
                }

                if (e.PendingChange != null)
                {
                    Changes.Add(ToPendingChange(e.PendingChange, false));
                    return;
                }

                if (string.IsNullOrEmpty(e.TargetServerItem) || e.ChangeType == ChangeType.None)
                {
                    return;
                }

                var sources = string.IsNullOrEmpty(e.SourceServerItem)
                    ? new List<MergeSourceInfo>()
                    : new List<MergeSourceInfo> { new MergeSourceInfo(e.SourceServerItem, e.SourceVersionFrom?.ChangesetId ?? 0, e.SourceVersionTo?.ChangesetId ?? 0, false) };
                Changes.Add(new PendingChangeInfo(ChangeText(e.ChangeType), e.TargetServerItem, e.TargetLocalItem, false, e.TargetVersionPended?.ChangesetId ?? 0,
                    sources.Count > 0 ? e.SourceServerItem : null, "none", null, DateTime.UtcNow, string.Empty, string.Empty, false, null, sources));
            }

            public void Dispose()
            {
                vcs.NewPendingChange -= OnNewPendingChange;
                vcs.UndonePendingChange -= OnUndonePendingChange;
                vcs.NonFatalError -= OnNonFatalError;
                vcs.Getting -= OnGetting;
                vcs.Merging -= OnMerging;
            }
        }
    }
}
