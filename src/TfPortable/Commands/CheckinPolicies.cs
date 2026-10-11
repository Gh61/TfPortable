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
                        ? new PolicyResultInfo(policy.TypeName, PolicyStatus.Passed, null, policy.ClassName)
                        : new PolicyResultInfo(policy.TypeName, PolicyStatus.Failed, $"{policy.TypeName}: the checkin must be associated with a work item. Pass /associate:<id>.", policy.ClassName));
                }
                else if (IsCommentPolicy(policy))
                {
                    result.Add(string.IsNullOrWhiteSpace(comment)
                        ? new PolicyResultInfo(policy.TypeName, PolicyStatus.Failed, $"{policy.TypeName}: the checkin needs a comment.", policy.ClassName)
                        : new PolicyResultInfo(policy.TypeName, PolicyStatus.Passed, null, policy.ClassName));
                }
                else
                {
                    result.Add(new PolicyResultInfo(policy.TypeName, PolicyStatus.Unknown, $"{policy.TypeName}: tfp cannot evaluate this policy. It runs inside Visual Studio only.", policy.ClassName));
                }
            }

            return result;
        }

        // The implementing class decides when it is known (a "Work Item Query Policy" or a custom "Code Review Comments" policy
        // must stay unknown); the display name counts only when the class could not be read, and only as an exact match.
        private static bool IsWorkItemPolicy(CheckinPolicyInfo policy) => Is(policy, ".WorkItemPolicy", "Work Items");

        private static bool IsCommentPolicy(CheckinPolicyInfo policy) => Is(policy, ".CheckForCommentsPolicy", "Changeset Comments Policy");

        private static bool Is(CheckinPolicyInfo policy, string classSuffix, string displayName)
        {
            if (!string.IsNullOrEmpty(policy.ClassName))
            {
                return policy.ClassName!.EndsWith(classSuffix, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(policy.TypeName, displayName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
