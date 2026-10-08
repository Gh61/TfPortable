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
            string comment = CommentText.Required(commandLine, toolName, "checkin Main\\src /recursive /comment:\"Fix the parser\"");
            var workItems = WorkItemIds(commandLine, toolName);
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
                throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError, "No pending changes match the given items. Nothing was checked in.",
                    $"'{toolName} status' lists the pending changes; pass /recursive for a folder.");
            }

            var evaluation = workspace.EvaluateCheckin(changes, comment);
            output.Lines(WorkspaceCommandSupport.GroupedByFolder(changes, c => $"{(validate ? "Validating" : "Checking in")} {c.ChangeText}: {WorkspaceCommandSupport.NameOf(c)}"));

            if (evaluation.Conflicts.Count > 0)
            {
                output.Line();
                foreach (var conflict in evaluation.Conflicts)
                {
                    output.Line($"Conflict: {conflict.Message}");
                }

                throw new TfpException(ErrorCodes.CheckinConflict, ExitCodes.GeneralError,
                    $"{evaluation.Conflicts.Count} conflict(s) stop the checkin. Nothing was checked in.",
                    $"Run '{toolName} get' and '{toolName} resolve' for a version conflict; a lock needs the lock owner.");
            }

            var failures = evaluation.OverridableFailures.ToList();
            if (failures.Count > 0 && overrideReason == null)
            {
                output.Line();
                foreach (string failure in failures)
                {
                    output.Line($"Policy: {failure}");
                }

                throw new TfpException(ErrorCodes.CheckinPolicy, ExitCodes.GeneralError,
                    $"{failures.Count} checkin policy or note requirement(s) failed. Nothing was checked in.",
                    $"Fix them, or run '{toolName} checkin ... /override:<reason>' at protection level 3.");
            }

            if (validate)
            {
                output.Line();
                output.Line(failures.Count == 0
                    ? $"The checkin of {changes.Count} change(s) is valid. Nothing was checked in."
                    : $"The checkin of {changes.Count} change(s) needs the override of {failures.Count} policy failure(s). Nothing was checked in.");
                output.Success(ToJson(null, changes, evaluation, workItems.Select(id => new JObject { ["id"] = id, ["linked"] = false }), true, overrideReason != null));
                return ExitCodes.Success;
            }

            int changeset = workspace.CheckIn(changes, comment, commandLine.Value("author"), failures.Count > 0 ? overrideReason : null, evaluation, commandLine.Has("noautoresolve"));
            if (changeset <= 0)
            {
                throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError, "The server committed nothing: the content of the changes is unchanged.",
                    $"Pass /force to check in unchanged content, or '{toolName} undo' the changes.");
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

            output.Success(ToJson(changeset, changes, evaluation, links, false, failures.Count > 0 && overrideReason != null));
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
            ["policyFailures"] = new JArray(evaluation.OverridableFailures.Cast<object>().ToArray()),
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
                return File.ReadAllText(path).TrimEnd('\r', '\n');
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/{switchName}:@{path} cannot be read: {ex.Message}",
                    $"Give the text directly: '{toolName} ... /{switchName}:\"text\"'.", inner: ex);
            }
        }
    }
}
