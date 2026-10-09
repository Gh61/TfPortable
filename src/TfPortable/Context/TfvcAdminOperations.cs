using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TfPortable.Context
{
    /// <summary>One item a destroy removed (or would remove with /preview).</summary>
    internal sealed class DestroyedItemInfo
    {
        public DestroyedItemInfo(string serverPath, int deletionId, bool isFolder)
        {
            ServerPath = serverPath;
            DeletionId = deletionId;
            IsFolder = isFolder;
        }

        public string ServerPath { get; }

        public int DeletionId { get; }

        public bool IsFolder { get; }

        public JObject ToJson() => new JObject { ["serverPath"] = ServerPath, ["deletionId"] = DeletionId, ["isFolder"] = IsFolder };
    }

    /// <summary>Result of a destroy: the items and the pending or shelved changes of other workspaces the destroy touches.</summary>
    internal sealed class DestroyResult
    {
        public DestroyResult(IReadOnlyList<DestroyedItemInfo> items, IReadOnlyList<PendingSetInfo> affectedPendingChanges, IReadOnlyList<PendingSetInfo> affectedShelvedChanges)
        {
            Items = items;
            AffectedPendingChanges = affectedPendingChanges;
            AffectedShelvedChanges = affectedShelvedChanges;
        }

        public IReadOnlyList<DestroyedItemInfo> Items { get; }

        public IReadOnlyList<PendingSetInfo> AffectedPendingChanges { get; }

        public IReadOnlyList<PendingSetInfo> AffectedShelvedChanges { get; }
    }

    /// <summary>The ACL entry of one identity on an item (`permission` listing).</summary>
    internal sealed class PermissionEntryInfo
    {
        public PermissionEntryInfo(string identity, IReadOnlyList<string> allow, IReadOnlyList<string> deny, IReadOnlyList<string> allowInherited, IReadOnlyList<string> denyInherited)
        {
            Identity = identity;
            Allow = allow;
            Deny = deny;
            AllowInherited = allowInherited;
            DenyInherited = denyInherited;
        }

        public string Identity { get; }

        public IReadOnlyList<string> Allow { get; }

        public IReadOnlyList<string> Deny { get; }

        public IReadOnlyList<string> AllowInherited { get; }

        public IReadOnlyList<string> DenyInherited { get; }

        public JObject ToJson() => new JObject
        {
            ["identity"] = Identity,
            ["allow"] = new JArray(Allow.Cast<object>().ToArray()),
            ["deny"] = new JArray(Deny.Cast<object>().ToArray()),
            ["allowInherited"] = new JArray(AllowInherited.Cast<object>().ToArray()),
            ["denyInherited"] = new JArray(DenyInherited.Cast<object>().ToArray()),
        };
    }

    /// <summary>The ACL of one server item.</summary>
    internal sealed class ItemPermissionInfo
    {
        public ItemPermissionInfo(string serverItem, bool inherit, IReadOnlyList<PermissionEntryInfo> entries)
        {
            ServerItem = serverItem;
            Inherit = inherit;
            Entries = entries;
        }

        public string ServerItem { get; }

        public bool Inherit { get; }

        public IReadOnlyList<PermissionEntryInfo> Entries { get; }

        public JObject ToJson() => new JObject
        {
            ["serverItem"] = ServerItem,
            ["inherit"] = Inherit,
            ["entries"] = new JArray(Entries.Select(e => e.ToJson()).Cast<object>().ToArray()),
        };
    }

    /// <summary>One ACL change to make: allows, denies and removes of an identity on an item, or a change of the inheritance.</summary>
    internal sealed class PermissionChangeRequest
    {
        public PermissionChangeRequest(string serverItem, string? identity, IReadOnlyList<string> allow, IReadOnlyList<string> deny, IReadOnlyList<string> remove, bool? inherit)
        {
            ServerItem = serverItem;
            Identity = identity;
            Allow = allow;
            Deny = deny;
            Remove = remove;
            Inherit = inherit;
        }

        public string ServerItem { get; }

        /// <summary>Null for an inheritance-only change.</summary>
        public string? Identity { get; }

        public IReadOnlyList<string> Allow { get; }

        public IReadOnlyList<string> Deny { get; }

        public IReadOnlyList<string> Remove { get; }

        public bool? Inherit { get; }

        public JObject ToJson() => new JObject
        {
            ["serverItem"] = ServerItem,
            ["identity"] = Identity,
            ["allow"] = new JArray(Allow.Cast<object>().ToArray()),
            ["deny"] = new JArray(Deny.Cast<object>().ToArray()),
            ["remove"] = new JArray(Remove.Cast<object>().ToArray()),
            ["inherit"] = Inherit,
        };
    }

    /// <summary>The versioned-item properties of one server item (`property`).</summary>
    internal sealed class ItemPropertiesInfo
    {
        public ItemPropertiesInfo(string serverPath, IReadOnlyList<KeyValuePair<string, string?>> properties)
        {
            ServerPath = serverPath;
            Properties = properties;
        }

        public string ServerPath { get; }

        public IReadOnlyList<KeyValuePair<string, string?>> Properties { get; }

        public JObject ToJson()
        {
            var values = new JObject();
            foreach (var property in Properties)
            {
                values[property.Key] = property.Value;
            }

            return new JObject { ["serverPath"] = ServerPath, ["properties"] = values };
        }
    }

    /// <summary>What `workspace /new` and the workspace edit need: null fields keep the current value (edit) or the default (new).</summary>
    internal sealed class WorkspaceDefinition
    {
        public WorkspaceDefinition(string name, string? comment, string? computer, IReadOnlyList<MappingInfo>? mappings, string? permission, bool? fileTimeCheckin)
        {
            Name = name;
            Comment = comment;
            Computer = computer;
            Mappings = mappings;
            Permission = permission;
            FileTimeCheckin = fileTimeCheckin;
        }

        public string Name { get; }

        public string? Comment { get; }

        public string? Computer { get; }

        public IReadOnlyList<MappingInfo>? Mappings { get; }

        /// <summary>`private`, `publiclimited` or `public` (case-insensitive), or null.</summary>
        public string? Permission { get; }

        /// <summary>True = `/filetime:checkin`, false = `/filetime:current`, null = unchanged.</summary>
        public bool? FileTimeCheckin { get; }
    }
}
