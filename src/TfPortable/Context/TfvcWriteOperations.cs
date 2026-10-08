using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TfPortable.Context
{
    /// <summary>What stops a checkin before it is committed: the server's conflicts and note failures, plus tfp's own policy pass.</summary>
    internal sealed class CheckinEvaluation
    {
        public CheckinEvaluation(IReadOnlyList<CheckinConflictInfo> conflicts, IReadOnlyList<string> noteFailures, IReadOnlyList<PolicyResultInfo> policies)
        {
            Conflicts = conflicts;
            NoteFailures = noteFailures;
            Policies = policies;
        }

        public IReadOnlyList<CheckinConflictInfo> Conflicts { get; }

        /// <summary>Messages of the required checkin notes that are missing.</summary>
        public IReadOnlyList<string> NoteFailures { get; }

        /// <summary>Every enabled checkin policy of the team projects touched, with tfp's verdict.</summary>
        public IReadOnlyList<PolicyResultInfo> Policies { get; }

        /// <summary>Policies tfp evaluated and that failed (TFP0407), plus the missing notes.</summary>
        public IEnumerable<string> PolicyFailures => Policies.Where(p => p.Status == PolicyStatus.Failed).Select(p => p.Message!).Concat(NoteFailures);

        /// <summary>Policies tfp cannot evaluate (TFP0409).</summary>
        public IEnumerable<string> UnknownPolicies => Policies.Where(p => p.Status == PolicyStatus.Unknown).Select(p => p.Message!);

        /// <summary>Everything an `/override` covers (conflicts are never overridden).</summary>
        public IEnumerable<string> OverridableFailures => PolicyFailures.Concat(UnknownPolicies);
    }

    internal enum PolicyStatus
    {
        Passed,
        Failed,
        Unknown,
    }

    /// <summary>A checkin policy of a team project and what tfp made of it.</summary>
    internal sealed class PolicyResultInfo
    {
        public PolicyResultInfo(string name, PolicyStatus status, string? message)
        {
            Name = name;
            Status = status;
            Message = message;
        }

        public string Name { get; }

        public PolicyStatus Status { get; }

        /// <summary>The failure text (`Failed`) or why tfp cannot evaluate it (`Unknown`); null when it passed.</summary>
        public string? Message { get; }

        public JObject ToJson() => new JObject
        {
            ["name"] = Name,
            ["status"] = Status.ToString().ToLowerInvariant(),
            ["message"] = Message,
        };
    }

    /// <summary>An enabled checkin policy as the server stores it: its display type and the class that implements it in Visual Studio.</summary>
    internal sealed class CheckinPolicyInfo
    {
        public CheckinPolicyInfo(string typeName, string? className)
        {
            TypeName = typeName;
            ClassName = className;
        }

        public string TypeName { get; }

        public string? ClassName { get; }
    }

    /// <summary>A checkin conflict: a newer version on the server, a lock of another workspace, ...</summary>
    internal sealed class CheckinConflictInfo
    {
        public CheckinConflictInfo(string serverPath, string message, bool resolvable)
        {
            ServerPath = serverPath;
            Message = message;
            Resolvable = resolvable;
        }

        public string ServerPath { get; }

        public string Message { get; }

        /// <summary>Whether `resolve` can handle it (a version conflict) or not (a lock).</summary>
        public bool Resolvable { get; }

        public JObject ToJson() => new JObject
        {
            ["serverPath"] = ServerPath,
            ["message"] = Message,
            ["resolvable"] = Resolvable,
        };
    }

    /// <summary>A shelveset header (`shelvesets`).</summary>
    internal sealed class ShelvesetInfo
    {
        public ShelvesetInfo(string name, string owner, string? ownerUniqueName, DateTime date, string? comment)
        {
            Name = name;
            Owner = owner;
            OwnerUniqueName = ownerUniqueName;
            Date = date;
            Comment = comment;
        }

        public string Name { get; }

        /// <summary>Display name of the owner.</summary>
        public string Owner { get; }

        public string? OwnerUniqueName { get; }

        public DateTime Date { get; }

        public string? Comment { get; }

        public JObject ToJson() => new JObject
        {
            ["name"] = Name,
            ["owner"] = Owner,
            ["ownerUniqueName"] = OwnerUniqueName,
            ["date"] = Output.OutputWriter.IsoDate(Date),
            ["comment"] = Comment,
        };
    }

    /// <summary>Result of an unshelve: the restored changes and the conflicts the merge left open.</summary>
    internal sealed class UnshelveResult
    {
        public UnshelveResult(IReadOnlyList<PendingChangeInfo> changes, IReadOnlyList<ConflictInfo> conflicts, IReadOnlyList<FailureInfo> failures)
        {
            Changes = changes;
            Conflicts = conflicts;
            Failures = failures;
        }

        public IReadOnlyList<PendingChangeInfo> Changes { get; }

        public IReadOnlyList<ConflictInfo> Conflicts { get; }

        public IReadOnlyList<FailureInfo> Failures { get; }
    }

    /// <summary>One item of a `label` request: the item at a version, included or excluded.</summary>
    internal sealed class LabelItemQuery
    {
        public LabelItemQuery(ItemQuery item, Microsoft.TeamFoundation.VersionControl.Client.VersionSpec version, bool exclude)
        {
            Item = item;
            Version = version;
            Exclude = exclude;
        }

        public ItemQuery Item { get; }

        public Microsoft.TeamFoundation.VersionControl.Client.VersionSpec Version { get; }

        public bool Exclude { get; }
    }

    /// <summary>What the server did with a label: `created`, `updated` or `deleted`.</summary>
    internal sealed class LabelResultInfo
    {
        public LabelResultInfo(string name, string scope, string status)
        {
            Name = name;
            Scope = scope;
            Status = status;
        }

        public string Name { get; }

        public string Scope { get; }

        public string Status { get; }

        public JObject ToJson() => new JObject
        {
            ["name"] = Name,
            ["scope"] = Scope,
            ["status"] = Status,
        };
    }

    /// <summary>Results and per-item failures of a label operation.</summary>
    internal sealed class LabelOperationResult
    {
        public LabelOperationResult(IReadOnlyList<LabelResultInfo> results, IReadOnlyList<FailureInfo> failures)
        {
            Results = results;
            Failures = failures;
        }

        public IReadOnlyList<LabelResultInfo> Results { get; }

        public IReadOnlyList<FailureInfo> Failures { get; }
    }

    /// <summary>Result of a merge or a rollback: the pended changes, the conflicts the server left open and the per-item failures.</summary>
    internal sealed class MergeResult
    {
        public MergeResult(IReadOnlyList<PendingChangeInfo> changes, IReadOnlyList<ConflictInfo> conflicts, IReadOnlyList<FailureInfo> failures, int numOperations, int numConflicts, bool noActionNeeded)
        {
            Changes = changes;
            Conflicts = conflicts;
            Failures = failures;
            NumOperations = numOperations;
            NumConflicts = numConflicts;
            NoActionNeeded = noActionNeeded;
        }

        public IReadOnlyList<PendingChangeInfo> Changes { get; }

        public IReadOnlyList<ConflictInfo> Conflicts { get; }

        public IReadOnlyList<FailureInfo> Failures { get; }

        public int NumOperations { get; }

        public int NumConflicts { get; }

        public bool NoActionNeeded { get; }
    }

    [Flags]
    internal enum MergeFlags
    {
        None = 0,
        Force = 1,
        Baseless = 2,
        Discard = 4,
        NoAutoResolve = 8,
        NoImplicitBaseless = 16,
        Conservative = 32,
        /// <summary>The server's preview (`NoMerge`): the operations are reported, nothing is pended.</summary>
        Preview = 64,
    }

    /// <summary>One row of `merges`: a source changeset merged into a target changeset.</summary>
    internal sealed class MergeHistoryInfo
    {
        public MergeHistoryInfo(int sourceChangeset, ChangesetInfo targetChangeset, bool partial)
        {
            SourceChangeset = sourceChangeset;
            TargetChangeset = targetChangeset;
            Partial = partial;
        }

        public int SourceChangeset { get; }

        public ChangesetInfo TargetChangeset { get; }

        /// <summary>Only part of the source changeset was merged.</summary>
        public bool Partial { get; }

        public JObject ToJson(bool detailed) => new JObject
        {
            ["sourceChangeset"] = SourceChangeset,
            ["targetChangeset"] = TargetChangeset.ToJson(detailed),
            ["partial"] = Partial,
        };
    }
}
