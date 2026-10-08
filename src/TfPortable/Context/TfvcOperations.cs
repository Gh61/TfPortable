using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TeamFoundation.VersionControl.Client;
using Newtonsoft.Json.Linq;

namespace TfPortable.Context
{
    /// <summary>A per-item failure reported by the client library during an operation (NonFatalError, GetStatus failures).</summary>
    internal sealed class FailureInfo
    {
        public FailureInfo(string? localPath, string? serverPath, string message, string? code, bool isWarning)
        {
            LocalPath = localPath;
            ServerPath = serverPath;
            Message = message;
            Code = code;
            IsWarning = isWarning;
        }

        public string? LocalPath { get; }

        public string? ServerPath { get; }

        /// <summary>The library's message, verbatim (English, the UI culture is en-US).</summary>
        public string Message { get; }

        /// <summary>Failure code of the server (`ItemExistsException`, `ItemNotFoundException`, ...) or null.</summary>
        public string? Code { get; }

        public bool IsWarning { get; }

        public string Item => LocalPath ?? ServerPath ?? string.Empty;
    }

    /// <summary>Result of a pend/undo operation: the changes the library reported and the per-item failures.</summary>
    internal sealed class PendResult
    {
        public PendResult(IReadOnlyList<PendingChangeInfo> changes, IReadOnlyList<FailureInfo> failures, int count)
        {
            Changes = changes;
            Failures = failures;
            Count = count;
        }

        /// <summary>Changes pended or undone, in the order the library reported them.</summary>
        public IReadOnlyList<PendingChangeInfo> Changes { get; }

        public IReadOnlyList<FailureInfo> Failures { get; }

        /// <summary>The count the library returned.</summary>
        public int Count { get; }
    }

    /// <summary>One line of a get: `getting`, `replacing`, `deleting`, or a problem (`conflict`, `writable`, ...).</summary>
    internal sealed class GetOperationInfo
    {
        public GetOperationInfo(string operation, string? localPath, string serverPath, int version, bool isFolder, bool isDelete)
        {
            Operation = operation;
            LocalPath = localPath;
            ServerPath = serverPath;
            Version = version;
            IsFolder = isFolder;
            IsDelete = isDelete;
        }

        /// <summary>`getting`, `replacing`, `deleting`, `conflict`, `writable`, `localpending`, `directorynotempty`, `targetisdirectory`, `unabletorefresh`.</summary>
        public string Operation { get; }

        public string? LocalPath { get; }

        public string ServerPath { get; }

        public int Version { get; }

        public bool IsFolder { get; }

        public bool IsDelete { get; }

        public JObject ToJson() => new JObject
        {
            ["operation"] = Operation,
            ["localPath"] = LocalPath,
            ["serverPath"] = ServerPath,
            ["version"] = Version,
            ["isFolder"] = IsFolder,
        };
    }

    /// <summary>Flags of a get, mirroring the tf.exe switches.</summary>
    [Flags]
    internal enum GetFlags
    {
        None = 0,
        All = 1,
        Overwrite = 2,
        Preview = 4,
        Remap = 8,
        NoAutoResolve = 16,
    }

    internal sealed class GetResult
    {
        public GetResult(IReadOnlyList<GetOperationInfo> operations, IReadOnlyList<ConflictInfo> conflicts, IReadOnlyList<FailureInfo> failures, bool noActionNeeded, int numOperations, int numConflicts, int numWarnings)
        {
            Operations = operations;
            Conflicts = conflicts;
            Failures = failures;
            NoActionNeeded = noActionNeeded;
            NumOperations = numOperations;
            NumConflicts = numConflicts;
            NumWarnings = numWarnings;
        }

        public IReadOnlyList<GetOperationInfo> Operations { get; }

        public IReadOnlyList<ConflictInfo> Conflicts { get; }

        public IReadOnlyList<FailureInfo> Failures { get; }

        public bool NoActionNeeded { get; }

        public int NumOperations { get; }

        public int NumConflicts { get; }

        public int NumWarnings { get; }
    }

    /// <summary>Conflict of docs/json-schema.md. <see cref="Handle"/> is the library object, needed to resolve it.</summary>
    internal sealed class ConflictInfo
    {
        public ConflictInfo(string type, string serverPath, string? localPath, int yourVersion, int theirVersion, int baseVersion,
            string yourChangeText, string theirChangeText, string? resolution, bool canAutoMerge, string message, bool isResolved, object? handle)
        {
            Type = type;
            ServerPath = serverPath;
            LocalPath = localPath;
            YourVersion = yourVersion;
            TheirVersion = theirVersion;
            BaseVersion = baseVersion;
            YourChangeText = yourChangeText;
            TheirChangeText = theirChangeText;
            Resolution = resolution;
            CanAutoMerge = canAutoMerge;
            Message = message;
            IsResolved = isResolved;
            Handle = handle;
        }

        /// <summary>`get`, `checkin`, `merge`, `local`.</summary>
        public string Type { get; }

        public string ServerPath { get; }

        public string? LocalPath { get; }

        public int YourVersion { get; }

        public int TheirVersion { get; }

        public int BaseVersion { get; }

        public string YourChangeText { get; }

        public string TheirChangeText { get; }

        /// <summary>Lower-case canonical resolution name, or null while unresolved.</summary>
        public string? Resolution { get; }

        public bool CanAutoMerge { get; }

        /// <summary>The library's brief description of the conflict.</summary>
        public string Message { get; }

        public bool IsResolved { get; }

        public object? Handle { get; }

        public string Path => LocalPath ?? ServerPath;

        public JObject ToJson() => new JObject
        {
            ["type"] = Type,
            ["serverPath"] = ServerPath,
            ["localPath"] = LocalPath,
            ["yourVersion"] = YourVersion,
            ["theirVersion"] = TheirVersion,
            ["baseVersion"] = BaseVersion,
            ["yourChangeTypes"] = new JArray(Split(YourChangeText).Cast<object>().ToArray()),
            ["theirChangeTypes"] = new JArray(Split(TheirChangeText).Cast<object>().ToArray()),
            ["resolution"] = Resolution,
            ["canAutoMerge"] = CanAutoMerge,
            ["message"] = Message,
        };

        private static string[] Split(string text) => text.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Outcome of resolving one conflict.</summary>
    internal sealed class ResolveResult
    {
        public ResolveResult(bool resolved, string resolutionName, string? mergeSummary, string? message)
        {
            Resolved = resolved;
            ResolutionName = resolutionName;
            MergeSummary = mergeSummary;
            Message = message;
        }

        public bool Resolved { get; }

        /// <summary>The server's resolution name as tf.exe prints it (`KeepYours`, `TakeTheirs`, `AcceptMerge`, ...).</summary>
        public string ResolutionName { get; }

        /// <summary>`local: a, server: b, both: c, conflicting: d` after a content merge, or null.</summary>
        public string? MergeSummary { get; }

        /// <summary>Why the conflict is still open, or null.</summary>
        public string? Message { get; }
    }

    internal sealed class LocalVersionInfo
    {
        public LocalVersionInfo(string localPath, string? serverPath, int version, bool isFolder)
        {
            LocalPath = localPath;
            ServerPath = serverPath;
            Version = version;
            IsFolder = isFolder;
        }

        public string LocalPath { get; }

        public string? ServerPath { get; }

        public int Version { get; }

        public bool IsFolder { get; }

        public JObject ToJson() => new JObject
        {
            ["localPath"] = LocalPath,
            ["serverPath"] = ServerPath,
            ["changesetId"] = Version,
            ["isFolder"] = IsFolder,
        };
    }

    /// <summary>Pending changes of one workspace or shelveset, as returned by a pending-set query (`status /user`, `status /shelveset`).</summary>
    internal sealed class PendingSetInfo
    {
        public PendingSetInfo(string name, string owner, string? ownerUniqueName, string computer, bool isShelveset, IReadOnlyList<PendingChangeInfo> changes)
        {
            Name = name;
            Owner = owner;
            OwnerUniqueName = ownerUniqueName;
            Computer = computer;
            IsShelveset = isShelveset;
            Changes = changes;
        }

        public string Name { get; }

        public string Owner { get; }

        public string? OwnerUniqueName { get; }

        public string Computer { get; }

        public bool IsShelveset { get; }

        public IReadOnlyList<PendingChangeInfo> Changes { get; }
    }

    /// <summary>Filter of a workspace query (`workspaces`): null means "any".</summary>
    internal sealed class WorkspaceQuery
    {
        public WorkspaceQuery(string? name, string? owner, string? computer)
        {
            Name = name;
            Owner = owner;
            Computer = computer;
        }

        public string? Name { get; }

        public string? Owner { get; }

        public string? Computer { get; }
    }
}
