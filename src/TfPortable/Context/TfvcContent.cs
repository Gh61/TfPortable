using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TfPortable.Output;

namespace TfPortable.Context
{
    /// <summary>Change of docs/json-schema.md: one item of a changeset.</summary>
    internal sealed class ChangeInfo
    {
        public ChangeInfo(string changeText, string serverPath, int changesetId, int deletionId, string? sourceServerPath, bool isFolder, int itemId, string? encoding)
        {
            ChangeText = changeText;
            ServerPath = serverPath;
            ChangesetId = changesetId;
            DeletionId = deletionId;
            SourceServerPath = sourceServerPath;
            IsFolder = isFolder;
            ItemId = itemId;
            Encoding = encoding;
        }

        /// <summary>Change types in tf.exe wording, lower-case, comma-separated (`merge, edit`; `delete, source rename`).</summary>
        public string ChangeText { get; }

        public string[] ChangeTypes => ChangeText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

        public string ServerPath { get; }

        /// <summary>Version of the item in this changeset.</summary>
        public int ChangesetId { get; }

        public int DeletionId { get; }

        public string? SourceServerPath { get; }

        public bool IsFolder { get; }

        public int ItemId { get; }

        public string? Encoding { get; }

        /// <summary>`$/path;X18` for deleted items, else the path.</summary>
        public string DisplayPath => DeletionId > 0 ? $"{ServerPath};X{DeletionId}" : ServerPath;

        public JObject ToJson() => new JObject
        {
            ["changeTypes"] = new JArray(ChangeTypes.Cast<object>().ToArray()),
            ["serverPath"] = ServerPath,
            ["changesetId"] = ChangesetId,
            ["deletionId"] = DeletionId,
            ["sourceServerPath"] = SourceServerPath,
        };
    }

    internal sealed class WorkItemInfo
    {
        public WorkItemInfo(int id, string? title)
        {
            Id = id;
            Title = title;
        }

        public int Id { get; }

        public string? Title { get; }

        public JObject ToJson() => new JObject { ["id"] = Id, ["title"] = Title };
    }

    /// <summary>Item of docs/json-schema.md (server side).</summary>
    internal sealed class ItemInfo
    {
        public ItemInfo(string serverPath, bool isFolder, int changesetId, int deletionId, string? encoding, long? size, DateTime lastModified, int itemId, string? hash = null)
        {
            Hash = hash;
            ServerPath = serverPath;
            IsFolder = isFolder;
            ChangesetId = changesetId;
            DeletionId = deletionId;
            Encoding = encoding;
            Size = size;
            LastModified = lastModified;
            ItemId = itemId;
        }

        public string ServerPath { get; }

        public bool IsFolder { get; }

        public int ChangesetId { get; }

        public int DeletionId { get; }

        /// <summary>`utf-8`, `windows-1250`, `binary`, null for folders.</summary>
        public string? Encoding { get; }

        public long? Size { get; }

        public DateTime LastModified { get; }

        public int ItemId { get; }

        /// <summary>MD5 of the content as the server stores it (lower-case hex), null for folders or when the server has none.</summary>
        public string? Hash { get; }

        public bool IsBinary => Encoding == "binary";

        public string DisplayPath => DeletionId > 0 ? $"{ServerPath};X{DeletionId}" : ServerPath;

        public JObject ToJson(string? localPath = null) => new JObject
        {
            ["serverPath"] = ServerPath,
            ["localPath"] = localPath,
            ["isFolder"] = IsFolder,
            ["changesetId"] = ChangesetId,
            ["deletionId"] = DeletionId,
            ["encoding"] = Encoding,
            ["size"] = IsFolder ? null : (JToken?)Size,
            ["lastModified"] = OutputWriter.IsoDate(LastModified),
        };
    }

    /// <summary>Server and local state of an item in a workspace (`properties`).</summary>
    internal sealed class ExtendedItemInfo
    {
        public ExtendedItemInfo(string serverPath, string? localPath, bool isFolder, int versionLocal, int versionLatest, string changeText, string lockLevel,
            string? lockOwner, int deletionId, string? encoding, DateTime checkinDate, bool isInWorkspace, bool hasOtherPendingChange)
        {
            ServerPath = serverPath;
            LocalPath = localPath;
            IsFolder = isFolder;
            VersionLocal = versionLocal;
            VersionLatest = versionLatest;
            ChangeText = changeText;
            Lock = lockLevel;
            LockOwner = lockOwner;
            DeletionId = deletionId;
            Encoding = encoding;
            CheckinDate = checkinDate;
            IsInWorkspace = isInWorkspace;
            HasOtherPendingChange = hasOtherPendingChange;
        }

        public string ServerPath { get; }

        public string? LocalPath { get; }

        public bool IsFolder { get; }

        public int VersionLocal { get; }

        public int VersionLatest { get; }

        /// <summary>Pending change of the current workspace (`none` when there is none).</summary>
        public string ChangeText { get; }

        public string Lock { get; }

        public string? LockOwner { get; }

        public int DeletionId { get; }

        public string? Encoding { get; }

        public DateTime CheckinDate { get; }

        public bool IsInWorkspace { get; }

        public bool HasOtherPendingChange { get; }
    }

    /// <summary>Content of an item downloaded from the server: raw bytes plus the server's encoding.</summary>
    internal sealed class ItemContent
    {
        public ItemContent(ItemInfo item, byte[] bytes)
        {
            Item = item;
            Bytes = bytes;
        }

        public ItemInfo Item { get; }

        public byte[] Bytes { get; }
    }

    internal sealed class LabelItemInfo
    {
        public LabelItemInfo(string serverPath, int changesetId, int deletionId)
        {
            ServerPath = serverPath;
            ChangesetId = changesetId;
            DeletionId = deletionId;
        }

        public string ServerPath { get; }

        public int ChangesetId { get; }

        public int DeletionId { get; }

        public string DisplayPath => DeletionId > 0 ? $"{ServerPath};X{DeletionId}" : ServerPath;

        public JObject ToJson() => new JObject { ["serverPath"] = ServerPath, ["changesetId"] = ChangesetId, ["deletionId"] = DeletionId };
    }

    internal sealed class LabelInfo
    {
        public LabelInfo(string name, string scope, string owner, string? ownerUniqueName, DateTime date, string? comment, IReadOnlyList<LabelItemInfo>? items)
        {
            Name = name;
            Scope = scope;
            Owner = owner;
            OwnerUniqueName = ownerUniqueName;
            Date = date;
            Comment = comment;
            Items = items;
        }

        public string Name { get; }

        public string Scope { get; }

        public string Owner { get; }

        public string? OwnerUniqueName { get; }

        public DateTime Date { get; }

        public string? Comment { get; }

        /// <summary>Null when the items were not requested (brief listing).</summary>
        public IReadOnlyList<LabelItemInfo>? Items { get; }

        public JObject ToJson(bool detailed) => new JObject
        {
            ["name"] = Name,
            ["scope"] = Scope,
            ["owner"] = Owner,
            ["ownerUniqueName"] = OwnerUniqueName,
            ["date"] = OutputWriter.IsoDate(Date),
            ["comment"] = Comment,
            ["items"] = detailed && Items != null ? new JArray(Items.Select(i => i.ToJson()).Cast<object>().ToArray()) : null,
        };
    }

    /// <summary>One node of a branch hierarchy (`branches`).</summary>
    internal sealed class BranchInfo
    {
        public BranchInfo(string serverPath, string? parent, int branchedFromVersion, bool isRequested, IReadOnlyList<BranchInfo> children)
        {
            ServerPath = serverPath;
            Parent = parent;
            BranchedFromVersion = branchedFromVersion;
            IsRequested = isRequested;
            Children = children;
        }

        public string ServerPath { get; }

        public string? Parent { get; }

        public int BranchedFromVersion { get; }

        public bool IsRequested { get; }

        public IReadOnlyList<BranchInfo> Children { get; }

        public JObject ToJson() => new JObject
        {
            ["serverPath"] = ServerPath,
            ["parent"] = Parent,
            ["branchedFromVersion"] = BranchedFromVersion == 0 ? null : (JToken?)BranchedFromVersion,
            ["children"] = new JArray(Children.Select(c => c.ToJson()).Cast<object>().ToArray()),
        };
    }

    internal sealed class MergeCandidateInfo
    {
        public MergeCandidateInfo(ChangesetInfo changeset, bool partial)
        {
            Changeset = changeset;
            Partial = partial;
        }

        public ChangesetInfo Changeset { get; }

        public bool Partial { get; }
    }
}
