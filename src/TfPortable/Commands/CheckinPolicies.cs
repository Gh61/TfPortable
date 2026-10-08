using System;
using System.Collections.Generic;
using System.Linq;
using TfPortable.Context;

namespace TfPortable.Commands
{
    /// <summary>
    /// tfp's own pass over the checkin policies of a team project (SPEC.md "Fáze 3"). Visual Studio evaluates policies on
    /// the client with assemblies tfp does not carry, so tfp evaluates the two built-in policies it can decide from the
    /// command line (work items, changeset comments) and reports every other policy as one it cannot evaluate (TFP0409).
    /// </summary>
    internal static class CheckinPolicies
    {
        public static IReadOnlyList<PolicyResultInfo> Evaluate(IReadOnlyList<CheckinPolicyInfo> policies, string comment, IReadOnlyList<int> workItemIds)
        {
            var result = new List<PolicyResultInfo>();
            foreach (var policy in policies)
            {
                if (IsWorkItemPolicy(policy))
                {
                    result.Add(workItemIds.Count > 0
                        ? new PolicyResultInfo(policy.TypeName, PolicyStatus.Passed, null)
                        : new PolicyResultInfo(policy.TypeName, PolicyStatus.Failed, $"{policy.TypeName}: the checkin must be associated with a work item. Pass /associate:<id>."));
                }
                else if (IsCommentPolicy(policy))
                {
                    result.Add(string.IsNullOrWhiteSpace(comment)
                        ? new PolicyResultInfo(policy.TypeName, PolicyStatus.Failed, $"{policy.TypeName}: the checkin needs a comment.")
                        : new PolicyResultInfo(policy.TypeName, PolicyStatus.Passed, null));
                }
                else
                {
                    result.Add(new PolicyResultInfo(policy.TypeName, PolicyStatus.Unknown, $"{policy.TypeName}: tfp cannot evaluate this policy. It runs inside Visual Studio only."));
                }
            }

            return result;
        }

        private static bool IsWorkItemPolicy(CheckinPolicyInfo policy) =>
            Contains(policy.ClassName, "WorkItemPolicy") || Contains(policy.TypeName, "Work Item");

        private static bool IsCommentPolicy(CheckinPolicyInfo policy) =>
            Contains(policy.ClassName, "CheckForCommentsPolicy") || Contains(policy.TypeName, "Comment");

        private static bool Contains(string? text, string value) => text != null && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
