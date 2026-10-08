using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TeamFoundation.VersionControl.Client;
using Newtonsoft.Json.Linq;
using TfPortable.Output;

namespace TfPortable.Context
{
    /// <summary>
    /// Plain data returned by <see cref="ITfvcClient"/>, shaped after docs/json-schema.md §2, so that formatting and
    /// JSON output can be unit-tested without the client library or a server.
    /// </summary>
    internal sealed class IdentityInfo
    {
        public IdentityInfo(string displayName, string? uniqueName, string? id)
        {
            DisplayName = displayName;
            UniqueName = uniqueName;
            Id = id;
        }

        public string DisplayName { get; }

        public string? UniqueName { get; }

        public string? Id { get; }

        public JObject ToJson() => new JObject { ["displayName"] = DisplayName, ["uniqueName"] = UniqueName, ["id"] = Id };
    }

    /// <summary>Mapping of docs/json-schema.md: server path, local path (null when cloaked), recursion.</summary>
    internal sealed class MappingInfo
    {
        public MappingInfo(string serverPath, string? localPath, bool cloaked, bool oneLevel)
        {
            ServerPath = serverPath;
            LocalPath = localPath;
            Cloaked = cloaked;
            OneLevel = oneLevel;
        }

        public string ServerPath { get; }

        public string? LocalPath { get; }

        public bool Cloaked { get; }

        public bool OneLevel { get; }

        public string RecursionType => OneLevel ? "onelevel" : "full";

        /// <summary>The line of a `workfold`/`whoami` listing: ` $/path: local` or ` (cloaked) $/path:`.</summary>
        public string ListingLine => Cloaked ? $" (cloaked) {ServerPath}:" : $" {ServerPath}: {LocalPath}";

        public JObject ToJson() => new JObject
        {
            ["serverPath"] = ServerPath,
            ["localPath"] = Cloaked ? null : LocalPath,
            ["cloaked"] = Cloaked,
            ["recursionType"] = RecursionType,
        };
    }

    /// <summary>Workspace of docs/json-schema.md (from `workspaces /format:detailed` and `workfold`).</summary>
    internal sealed class WorkspaceSummary
    {
        public WorkspaceSummary(string name, string owner, string? ownerUniqueName, string computer, string? comment, string collection,
            bool isLocal, string? permission, DateTime lastAccess, IReadOnlyList<MappingInfo> mappings)
        {
            Name = name;
            Owner = owner;
            OwnerUniqueName = ownerUniqueName;
            Computer = computer;
            Comment = comment;
            Collection = collection;
            IsLocal = isLocal;
            Permission = permission;
            LastAccess = lastAccess;
            Mappings = mappings;
        }

        public string Name { get; }

        /// <summary>Owner display name, as tf.exe prints it (golden `<WORKSPACE>;<OWNER>`).</summary>
        public string Owner { get; }

        public string? OwnerUniqueName { get; }

        public string Computer { get; }

        public string? Comment { get; }

        public string Collection { get; }

        public bool IsLocal { get; }

        public string Location => IsLocal ? "local" : "server";

        /// <summary>`private`, `publiclimited`, `public` or null.</summary>
        public string? Permission { get; }

        public DateTime LastAccess { get; }

        public IReadOnlyList<MappingInfo> Mappings { get; }

        /// <summary>`name;owner` for the JSON envelope and messages.</summary>
        public string Spec => $"{Name};{Owner}";

        public JObject ToJson() => new JObject
        {
            ["name"] = Name,
            ["owner"] = Owner,
            ["ownerUniqueName"] = OwnerUniqueName,
            ["computer"] = Computer,
            ["comment"] = Comment,
            ["collection"] = Collection,
            ["location"] = Location,
            ["permission"] = Permission,
            ["lastAccess"] = OutputWriter.IsoDate(LastAccess),
            ["mappings"] = new JArray(Mappings.Select(m => m.ToJson()).Cast<object>().ToArray()),
        };
    }

    /// <summary>PendingChange of docs/json-schema.md.</summary>
    internal sealed class PendingChangeInfo
    {
        public PendingChangeInfo(string changeText, string serverPath, string? localPath, bool isFolder, int version, string? sourceServerPath,
            string lockLevel, string? encoding, DateTime date, string? owner, string? workspace, bool detected)
        {
            ChangeText = changeText;
            ServerPath = serverPath;
            LocalPath = localPath;
            IsFolder = isFolder;
            Version = version;
            SourceServerPath = sourceServerPath;
            Lock = lockLevel;
            Encoding = encoding;
            Date = date;
            Owner = owner;
            Workspace = workspace;
            Detected = detected;
        }

        /// <summary>Change types in the wording of tf.exe, lower-case, comma-separated: `edit`, `merge, edit`.</summary>
        public string ChangeText { get; }

        public string[] ChangeTypes => ChangeText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

        public string ServerPath { get; }

        public string? LocalPath { get; }

        public bool IsFolder { get; }

        public int Version { get; }

        public string? SourceServerPath { get; }

        public string Lock { get; }

        public string? Encoding { get; }

        public DateTime Date { get; }

        public string? Owner { get; }

        public string? Workspace { get; }

        /// <summary>A candidate detected on disk, not yet pended.</summary>
        public bool Detected { get; }

        public JObject ToJson() => new JObject
        {
            ["changeTypes"] = new JArray(ChangeTypes.Cast<object>().ToArray()),
            ["serverPath"] = ServerPath,
            ["localPath"] = LocalPath,
            ["isFolder"] = IsFolder,
            ["version"] = Version,
            ["sourceServerPath"] = SourceServerPath,
            ["lock"] = Lock,
            ["encoding"] = Encoding,
            ["date"] = OutputWriter.IsoDate(Date),
            ["owner"] = Owner,
            ["workspace"] = Workspace,
            ["detected"] = Detected,
        };
    }

    /// <summary>Pending changes and detected candidates of a workspace query.</summary>
    internal sealed class PendingChangeSet
    {
        public PendingChangeSet(IReadOnlyList<PendingChangeInfo> pending, IReadOnlyList<PendingChangeInfo> candidates)
        {
            Pending = pending;
            Candidates = candidates;
        }

        public IReadOnlyList<PendingChangeInfo> Pending { get; }

        public IReadOnlyList<PendingChangeInfo> Candidates { get; }
    }

    /// <summary>Changeset of docs/json-schema.md (brief form, without changes and work items).</summary>
    internal sealed class ChangesetInfo
    {
        public ChangesetInfo(int id, string owner, string? ownerUniqueName, DateTime date, string? comment)
        {
            Id = id;
            Owner = owner;
            OwnerUniqueName = ownerUniqueName;
            Date = date;
            Comment = comment;
        }

        public int Id { get; }

        public string Owner { get; }

        public string? OwnerUniqueName { get; }

        public DateTime Date { get; }

        public string? Comment { get; }

        public JObject ToJson() => new JObject
        {
            ["id"] = Id,
            ["owner"] = Owner,
            ["ownerUniqueName"] = OwnerUniqueName,
            ["date"] = OutputWriter.IsoDate(Date),
            ["comment"] = Comment,
        };
    }

    /// <summary>An item to query: server or absolute local path, recursion and optional deletion id.</summary>
    internal sealed class ItemQuery
    {
        public ItemQuery(string path, RecursionType recursion, int deletionId = 0)
        {
            Path = path;
            Recursion = recursion;
            DeletionId = deletionId;
        }

        public string Path { get; }

        public RecursionType Recursion { get; }

        public int DeletionId { get; }
    }

    /// <summary>Parameters of a history query (`history` and the search extensions).</summary>
    internal sealed class HistoryQuery
    {
        public HistoryQuery(string path, VersionSpec version, RecursionType recursion, string? user, VersionSpec? from, VersionSpec? to, int maxCount, bool slotMode, bool ascending, int deletionId = 0)
        {
            Path = path;
            DeletionId = deletionId;
            Version = version;
            Recursion = recursion;
            User = user;
            From = from;
            To = to;
            MaxCount = maxCount;
            SlotMode = slotMode;
            Ascending = ascending;
        }

        public string Path { get; }

        /// <summary>Deletion id of a `;X<n>` itemspec suffix, 0 for the live item.</summary>
        public int DeletionId { get; }

        public VersionSpec Version { get; }

        public RecursionType Recursion { get; }

        public string? User { get; }

        public VersionSpec? From { get; }

        public VersionSpec? To { get; }

        public int MaxCount { get; }

        public bool SlotMode { get; }

        public bool Ascending { get; }
    }
}
