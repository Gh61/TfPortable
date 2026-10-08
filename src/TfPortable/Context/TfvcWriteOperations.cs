using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TfPortable.Context
{
    /// <summary>What the server found wrong with a checkin before it is committed (`checkin`, `checkin /validate`).</summary>
    internal sealed class CheckinEvaluation
    {
        public CheckinEvaluation(IReadOnlyList<CheckinConflictInfo> conflicts, IReadOnlyList<string> policyFailures, IReadOnlyList<string> noteFailures, string? policyEvaluationError)
        {
            Conflicts = conflicts;
            PolicyFailures = policyFailures;
            NoteFailures = noteFailures;
            PolicyEvaluationError = policyEvaluationError;
        }

        public IReadOnlyList<CheckinConflictInfo> Conflicts { get; }

        /// <summary>Messages of the checkin policies that failed.</summary>
        public IReadOnlyList<string> PolicyFailures { get; }

        /// <summary>Messages of the required checkin notes that are missing.</summary>
        public IReadOnlyList<string> NoteFailures { get; }

        /// <summary>The policies could not be evaluated at all (the policy assemblies are not available to tfp).</summary>
        public string? PolicyEvaluationError { get; }

        public bool IsClean => Conflicts.Count == 0 && PolicyFailures.Count == 0 && NoteFailures.Count == 0 && PolicyEvaluationError == null;

        /// <summary>The policy and note findings an `/override` covers (conflicts are never overridden).</summary>
        public IEnumerable<string> OverridableFailures => PolicyFailures.Concat(NoteFailures).Concat(PolicyEvaluationError == null ? Array.Empty<string>() : new[] { PolicyEvaluationError });

        /// <summary>The library's policy failures, needed to build the override; never serialized.</summary>
        public object? PolicyHandles { get; set; }
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
}
