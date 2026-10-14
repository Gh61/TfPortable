using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.TeamFoundation.VersionControl.Client;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>
    /// `tfp checkin [itemspec ...] /comment:text [/associate:id] [/validate] [/override:reason] [/recursive]` (SPEC.md "Fáze 3"):
    /// evaluates the changes (conflicts, policies, notes), checks them in, links the work items over REST. Nothing pending
    /// is TFP0301 (exit 100, as tf.exe), conflicts TFP0406, policy or note failures TFP0407 unless overridden.
    /// </summary>
    internal sealed class CheckinCommand : ICommand
    {
        public string Name => "checkin";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Has("shelveset"))
            {
                return CheckInShelveset(commandLine, output, toolName);
            }

            string comment = CommentText.Required(commandLine, toolName, "checkin Main\\src /recursive /comment:\"Fix the parser\"");
            var workItems = WorkItemIds(commandLine, toolName);
            var notes = ChangesetCommand.Notes(commandLine, toolName);
            bool validate = commandLine.Has("validate");
            string? overrideReason = commandLine.Value("override");
            if (overrideReason != null && overrideReason.StartsWith("@", StringComparison.Ordinal))
            {
                overrideReason = CommentText.FromFile(overrideReason, "override", toolName);
            }

            if (overrideReason != null && overrideReason.Trim().Length == 0)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/override needs a reason.", "Example: /override:\"Hotfix approved by the team lead\".");
            }

            var items = WorkspaceCommandSupport.Items(commandLine, toolName, true, "checkin Main\\src /recursive /comment:\"Fix the parser\"");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            var changes = workspace.GetPendingChanges(WorkspaceCommandSupport.Queries(items, recursion, context), false).Pending;
            if (changes.Count == 0)
            {
                throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError, "Nothing to check in: the given items have no pending changes.",
                    $"'{toolName} status' lists the pending changes. Pass /recursive for a folder.");
            }

            var policies = CheckinPolicies.Evaluate(context.Client.GetCheckinPolicies(changes.Select(c => c.ServerPath).Distinct().ToList()), comment, workItems);
            var evaluation = workspace.EvaluateCheckin(changes, comment, policies, notes);
            output.Lines(WorkspaceCommandSupport.GroupedByFolder(changes, c => $"{(validate ? "Validating" : "Checking in")} {c.ChangeText}: {WorkspaceCommandSupport.NameOf(c)}"));

            // Every conflict and policy failure is an item error (stderr in text mode, `errors[]` in JSON) before the summary.
            foreach (var conflict in evaluation.Conflicts)
            {
                output.ItemError(new TfpException(ErrorCodes.CheckinConflict, ExitCodes.GeneralError, $"Conflict: {conflict.Message}",
                    conflict.Resolvable ? $"Run '{toolName} get' and '{toolName} resolve' for the item." : "The lock owner can release the item.", item: conflict.ServerPath));
            }

            var failures = evaluation.OverridableFailures.ToList();
            bool overridden = failures.Count > 0 && overrideReason != null;
            if (overrideReason == null)
            {
                foreach (string failure in evaluation.PolicyFailures)
                {
                    output.ItemError(new TfpException(ErrorCodes.CheckinPolicy, ExitCodes.GeneralError, $"Policy: {failure}",
                        $"Fix it, or run '{toolName} checkin ... /override:<reason>' to record why it is bypassed."));
                }

                foreach (string unknown in evaluation.UnknownPolicies)
                {
                    output.ItemError(new TfpException(ErrorCodes.CheckinPolicyUnknown, ExitCodes.GeneralError, $"Policy: {unknown}",
                        $"Check it in Visual Studio, or run '{toolName} checkin ... /override:<reason>' to record why it is bypassed."));
                }
            }
            else
            {
                foreach (string failure in failures)
                {
                    output.Warning($"Policy overridden: {failure}", hint: $"Reason recorded in the changeset: {overrideReason}");
                }
            }

            bool blocked = evaluation.Conflicts.Count > 0 || (failures.Count > 0 && overrideReason == null);
            if (validate)
            {
                output.Line();
                output.Line(blocked
                    ? $"The checkin of {changes.Count} change(s) is blocked by {evaluation.Conflicts.Count} conflict(s) and {(overridden ? 0 : failures.Count)} policy failure(s). Nothing was checked in."
                    : overridden
                        ? $"The checkin of {changes.Count} change(s) is valid with the override of {failures.Count} policy finding(s). Nothing was checked in."
                        : $"The checkin of {changes.Count} change(s) is valid. Nothing was checked in.");
                int exit = blocked ? ExitCodes.GeneralError : ExitCodes.Success;
                output.Success(ToJson(null, changes, evaluation, workItems.Select(id => new JObject { ["id"] = id, ["linked"] = false }), true, overridden), exit);
                return exit;
            }

            if (evaluation.Conflicts.Count > 0)
            {
                throw new TfpException(ErrorCodes.CheckinConflict, ExitCodes.GeneralError,
                    $"{evaluation.Conflicts.Count} conflict(s) stop the checkin. Nothing was checked in.",
                    $"Run '{toolName} get' and '{toolName} resolve' for a version conflict. A lock needs the lock owner.");
            }

            if (failures.Count > 0 && overrideReason == null)
            {
                int unknownCount = evaluation.UnknownPolicies.Count();
                throw new TfpException(unknownCount == failures.Count ? ErrorCodes.CheckinPolicyUnknown : ErrorCodes.CheckinPolicy, ExitCodes.GeneralError,
                    $"{failures.Count - unknownCount} checkin policy or note requirement(s) failed and {unknownCount} policy(ies) cannot be evaluated by tfp. Nothing was checked in.",
                    $"Fix them, or run '{toolName} checkin ... /override:<reason>' to record why they are bypassed.");
            }

            int changeset = workspace.CheckIn(changes, comment, commandLine.Value("author"), overridden ? overrideReason : null, failures, commandLine.Has("noautoresolve"), commandLine.Has("force"), notes);
            if (changeset <= 0)
            {
                // tf.exe exits 1 here and the server drops the unchanged edit (golden checkin/unchanged-edit). An item error, not a
                // thrown one: a thrown TFP0301 of checkin is exit 100 (checkin/nothing-pending).
                output.ItemError(new TfpException(ErrorCodes.ItemNotFound, ExitCodes.PartialSuccess, "The server committed nothing: the content of the changes is unchanged.",
                    $"Pass /force to check in unchanged content, or '{toolName} undo' the changes."));
                output.Success(ToJson(null, changes, evaluation, Array.Empty<JObject>(), false, overridden), ExitCodes.PartialSuccess);
                return ExitCodes.PartialSuccess;
            }

            output.Line();
            output.Line($"Changeset #{changeset} checked in.");
            var links = new List<JObject>();
            foreach (int id in workItems)
            {
                try
                {
                    context.Client.LinkWorkItemToChangeset(id, changeset);
                    output.Line($"Work item {id} linked to changeset {changeset}.");
                    links.Add(new JObject { ["id"] = id, ["linked"] = true });
                }
                catch (Exception ex) when (!(ex is TfpException))
                {
                    var mapped = ErrorMapper.Map(ex, toolName, context.CollectionUrl);
                    output.ItemError(new TfpException(ErrorCodes.ServerError, ExitCodes.PartialSuccess,
                        $"Changeset {changeset} was checked in, but work item {id} could not be linked: {mapped.Message}",
                        "Link it in the web portal, or check the work item id.", mapped.TfCode, ex.Message, item: id.ToString()));
                    links.Add(new JObject { ["id"] = id, ["linked"] = false });
                }
            }

            output.Success(ToJson(changeset, changes, evaluation, links, false, overridden));
            return ExitCodes.Success;
        }

        /// <summary>`checkin /shelveset:name[;owner]`: the shelveset becomes a changeset with the shelveset's comment; the server deletes it (phase 4).</summary>
        private static int CheckInShelveset(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count > 0 || commandLine.Has("comment") || commandLine.Has("validate") || commandLine.Has("override") || commandLine.Has("notes"))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "checkin /shelveset takes no itemspec, /comment, /notes, /validate or /override: the shelveset is checked in as it is.",
                    $"Example: '{toolName} checkin /shelveset:MyWork'.");
            }

            var (name, owner) = ShelvesetSpec.Parse(commandLine.Value("shelveset")!, toolName);
            var workItems = WorkItemIds(commandLine, toolName);
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            string ownerName = owner ?? context.Client.AuthorizedUser;
            int changeset = context.Client.CheckInShelveset(name, ownerName, commandLine.Value("author"), commandLine.Has("noautoresolve"), commandLine.Has("force"));
            output.Line($"Changeset #{changeset} checked in.");
            output.Line($"Shelveset {name};{ownerName} deleted.");

            var links = new List<JObject>();
            foreach (int id in workItems)
            {
                try
                {
                    context.Client.LinkWorkItemToChangeset(id, changeset);
                    output.Line($"Work item {id} linked to changeset {changeset}.");
                    links.Add(new JObject { ["id"] = id, ["linked"] = true });
                }
                catch (Exception ex) when (!(ex is TfpException))
                {
                    var mapped = ErrorMapper.Map(ex, toolName, context.CollectionUrl);
                    output.ItemError(new TfpException(ErrorCodes.ServerError, ExitCodes.PartialSuccess,
                        $"Changeset {changeset} was checked in, but work item {id} could not be linked: {mapped.Message}",
                        "Link it in the web portal, or check the work item id.", mapped.TfCode, ex.Message, item: id.ToString()));
                    links.Add(new JObject { ["id"] = id, ["linked"] = false });
                }
            }

            output.Success(new JObject
            {
                ["changeset"] = changeset,
                ["shelveset"] = ShelvesetSpec.ToJson(name, ownerName),
                ["workItems"] = new JArray(links.Cast<object>().ToArray()),
            });
            return ExitCodes.Success;
        }

        /// <summary>`/associate:1,2`: positive integers.</summary>
        internal static List<int> WorkItemIds(CommandLine commandLine, string toolName)
        {
            var ids = new List<int>();
            foreach (string value in commandLine.Values("associate"))
            {
                if (!int.TryParse(value, out int id) || id <= 0)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/associate:{value} is not a work item id.", $"Example: '{toolName} checkin /comment:\"...\" /associate:1234'.");
                }

                ids.Add(id);
            }

            return ids;
        }

        private static JObject ToJson(int? changeset, IReadOnlyList<PendingChangeInfo> changes, CheckinEvaluation evaluation, IEnumerable<JObject> workItems, bool validated, bool overridden) => new JObject
        {
            ["changeset"] = changeset,
            ["validated"] = validated,
            ["changes"] = new JArray(changes.Select(c => c.ToJson()).Cast<object>().ToArray()),
            ["conflicts"] = new JArray(evaluation.Conflicts.Select(c => c.ToJson()).Cast<object>().ToArray()),
            ["policies"] = new JArray(evaluation.Policies.Select(p => p.ToJson()).Cast<object>().ToArray()),
            ["policyFailures"] = new JArray(evaluation.PolicyFailures.Cast<object>().ToArray()),
            ["unknownPolicies"] = new JArray(evaluation.UnknownPolicies.Cast<object>().ToArray()),
            ["overridden"] = overridden,
            ["workItems"] = new JArray(workItems.Cast<object>().ToArray()),
        };
    }

    /// <summary>`/comment:text` or `/comment:@file` of checkin, shelve and label.</summary>
    internal static class CommentText
    {
        public static string Required(CommandLine commandLine, string toolName, string example)
        {
            string? comment = Optional(commandLine, toolName);
            if (string.IsNullOrWhiteSpace(comment))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"{commandLine.Command} needs /comment:<text>.",
                    $"Example: '{toolName} {example}'. tfp never opens the checkin dialog.");
            }

            return comment!;
        }

        public static string? Optional(CommandLine commandLine, string toolName)
        {
            string? comment = commandLine.Value("comment");
            if (comment != null && comment.StartsWith("@", StringComparison.Ordinal))
            {
                comment = FromFile(comment, "comment", toolName);
            }

            return comment;
        }

        /// <summary>`@file` form: the file's text, trimmed of a trailing newline.</summary>
        public static string FromFile(string value, string switchName, string toolName)
        {
            string path = value.Substring(1);
            try
            {
                return File.ReadAllText(path, System.Text.Encoding.UTF8).TrimEnd('\r', '\n');
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/{switchName}:@{path} cannot be read: {ex.Message}",
                    $"Give the text directly: '{toolName} ... /{switchName}:\"text\"'.", inner: ex);
            }
        }
    }
}
