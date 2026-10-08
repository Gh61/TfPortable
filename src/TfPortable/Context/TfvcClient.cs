using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Common;
using TfPortable.Auth;

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

        public void UpdateWorkspaceCache()
        {
            var vcs = VersionControl;
            Workstation.Current.UpdateWorkspaceInfoCache(vcs, vcs.AuthorizedUser);
        }

        public IReadOnlyList<ChangesetInfo> QueryHistory(HistoryQuery query)
        {
            return VersionControl
                .QueryHistory(query.Path, query.Version, query.DeletionId, query.Recursion, query.User, query.From, query.To, query.MaxCount, false, query.SlotMode, false, query.Ascending)
                .Cast<Changeset>()
                .Select(ToChangeset)
                .ToList();
        }

        public void Dispose() => connection.Dispose();

        internal static ChangesetInfo ToChangeset(Changeset cs) =>
            new ChangesetInfo(cs.ChangesetId, cs.OwnerDisplayName ?? cs.Owner, cs.Owner, cs.CreationDate, cs.Comment);

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
                ChangeText(change),
                change.ServerItem,
                change.LocalItem,
                change.ItemType == ItemType.Folder,
                change.Version,
                change.IsRename || change.IsBranch || change.IsMerge ? change.SourceServerItem : null,
                change.LockLevel.ToString().ToLowerInvariant(),
                change.ItemType == ItemType.Folder ? null : EncodingName(change.Encoding),
                change.CreationDate,
                change.PendingSetOwner,
                change.PendingSetName,
                detected);
        }

        /// <summary>Change types in the English wording of the client library ("edit", "merge, edit"), lower-case like tf.exe.</summary>
        internal static string ChangeText(PendingChange change) => PendingChange.GetLocalizedStringForChangeType(change.ChangeType).ToLowerInvariant();

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

        private sealed class TfvcWorkspace : ITfvcWorkspace
        {
            private readonly Workspace workspace;
            private WorkspaceSummary? summary;

            public TfvcWorkspace(Workspace workspace)
            {
                this.workspace = workspace;
            }

            public WorkspaceSummary Summary => summary ??= ToSummary(workspace);

            public bool IsLocalPathMapped(string localPath) => workspace.IsLocalPathMapped(localPath);

            public string GetServerItemForLocalItem(string localPath) => workspace.GetServerItemForLocalItem(localPath);

            public PendingChangeSet GetPendingChanges(IReadOnlyList<ItemQuery> items, bool detectCandidates)
            {
                var specs = items.Select(i => new ItemSpec(i.Path, i.Recursion, i.DeletionId)).ToArray();
                bool detect = detectCandidates && workspace.Location == WorkspaceLocation.Local;
                PendingChange[] pending = workspace.GetPendingChangesWithCandidates(specs, detect, out PendingChange[] candidates);
                var detected = detect && candidates != null ? candidates.Select(c => ToPendingChange(c, true)).ToList() : new List<PendingChangeInfo>();
                return new PendingChangeSet(pending.Select(p => ToPendingChange(p, false)).ToList(), detected);
            }
        }
    }
}
