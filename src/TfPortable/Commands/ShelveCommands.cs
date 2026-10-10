using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TeamFoundation.VersionControl.Client;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>`name;owner` of a shelveset argument; a missing owner means the authenticated user.</summary>
    internal static class ShelvesetSpec
    {
        public static (string Name, string? Owner) Parse(string text, string toolName)
        {
            string[] parts = text.Split(new[] { ';' }, 2);
            string name = parts[0].Trim();
            if (name.Length == 0)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "The shelveset name is empty.", $"Example: '{toolName} unshelve MyWork;user@example.com'.");
            }

            string? owner = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : null;
            return (name, owner);
        }

        public static JObject ToJson(string name, string owner) => new JObject { ["name"] = name, ["owner"] = owner };
    }

    /// <summary>
    /// `tfp shelve shelvesetname [itemspec ...] [/comment:text] [/replace] [/move] [/recursive]`, and `shelve /delete name[;owner]`
    /// (protection level 3). The owner part of the name is accepted only when it is the authenticated user.
    /// </summary>
    internal sealed class ShelveCommand : ICommand
    {
        public string Name => "shelve";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count == 0)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "shelve needs the shelveset name.", $"Example: '{toolName} shelve MyWork Main\\src /recursive /comment:\"Parser rewrite\"'.");
            }

            var (name, owner) = ShelvesetSpec.Parse(commandLine.Positionals[0], toolName);
            string? comment = CommentText.Optional(commandLine, toolName);

            if (commandLine.Has("delete"))
            {
                if (commandLine.Positionals.Count > 1 || commandLine.Has("replace") || commandLine.Has("move"))
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "shelve /delete takes only the shelveset name.", $"Example: '{toolName} shelve /delete MyWork'.");
                }

                using var deleteContext = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
                string deleteOwner = owner ?? deleteContext.Client.AuthorizedUser;
                deleteContext.Client.DeleteShelveset(name, deleteOwner);
                output.Line($"Shelveset {name};{deleteOwner} deleted.");
                output.Success(new JObject { ["shelveset"] = ShelvesetSpec.ToJson(name, deleteOwner), ["deleted"] = true });
                return ExitCodes.Success;
            }

            var items = commandLine.Positionals.Skip(1).Select(p => ItemSpecParser.Parse(p)).ToList();
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            string me = context.Client.AuthorizedUser;
            if (owner != null && !string.Equals(owner, me, StringComparison.OrdinalIgnoreCase))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"A shelveset can be created only for the authenticated user, not for {owner}.",
                    $"Drop the owner: '{toolName} shelve {name}'.");
            }

            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            var changes = workspace.GetPendingChanges(WorkspaceCommandSupport.Queries(items, recursion, context), false).Pending;
            if (changes.Count == 0)
            {
                throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError, "Nothing to shelve: the given items have no pending changes.",
                    $"'{toolName} status' lists the pending changes. Pass /recursive for a folder.");
            }

            var validation = new JArray();
            if (commandLine.Has("validate"))
            {
                // The checkin policies and notes as `checkin /validate` sees them; a failure is reported, the shelveset is still created.
                var policies = CheckinPolicies.Evaluate(context.Client.GetCheckinPolicies(changes.Select(c => c.ServerPath).Distinct().ToList()), comment ?? string.Empty, new List<int>());
                var evaluation = workspace.EvaluateCheckin(changes, comment ?? string.Empty, policies, null);
                foreach (var conflict in evaluation.Conflicts)
                {
                    output.ItemError(new TfpException(ErrorCodes.CheckinConflict, ExitCodes.PartialSuccess, $"Conflict: {conflict.Message}", $"Run '{toolName} get' and '{toolName} resolve' for the item before the checkin.", item: conflict.ServerPath));
                    validation.Add(conflict.Message);
                }

                foreach (string failure in evaluation.PolicyFailures)
                {
                    output.ItemError(new TfpException(ErrorCodes.CheckinPolicy, ExitCodes.PartialSuccess, $"Policy: {failure}", "The checkin of this work will need the policy satisfied or an /override reason."));
                    validation.Add(failure);
                }

                foreach (string unknown in evaluation.UnknownPolicies)
                {
                    output.ItemError(new TfpException(ErrorCodes.CheckinPolicyUnknown, ExitCodes.PartialSuccess, $"Policy: {unknown}", "Check it in Visual Studio before the checkin."));
                    validation.Add(unknown);
                }
            }

            bool replace = commandLine.Has("replace");
            bool existed = replace && context.Client.QueryShelvesets(name, me).Count > 0;
            workspace.Shelve(name, comment, changes, replace, commandLine.Has("move"));

            output.Lines(WorkspaceCommandSupport.GroupedByFolder(changes, c => $"Shelving {c.ChangeText}: {WorkspaceCommandSupport.NameOf(c)}"));
            output.Line();
            output.Line(existed ? $"Shelveset {name} replaced." : $"Shelveset {name} created.");
            if (commandLine.Has("move"))
            {
                output.Line($"The {changes.Count} local pending change(s) were undone.");
            }

            output.Success(new JObject
            {
                ["shelveset"] = ShelvesetSpec.ToJson(name, me),
                ["changes"] = new JArray(changes.Select(c => c.ToJson()).Cast<object>().ToArray()),
                ["replaced"] = existed,
                ["moved"] = commandLine.Has("move"),
                ["validated"] = commandLine.Has("validate"),
                ["validationFailures"] = validation,
            });
            return ExitCodes.Success;
        }
    }

    /// <summary>`tfp shelvesets [name[;owner]] [/owner:name|*] [/format:brief|detailed]`: the authenticated user's shelvesets by default.</summary>
    internal sealed class ShelvesetsCommand : ICommand
    {
        public string Name => "shelvesets";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count > 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "shelvesets takes at most one shelveset name.", $"Example: '{toolName} shelvesets MyWork /format:detailed'.");
            }

            bool detailed = string.Equals(commandLine.Value("format"), "detailed", StringComparison.OrdinalIgnoreCase);
            string? name = null;
            string? owner = commandLine.Value("owner");
            if (commandLine.Positionals.Count == 1)
            {
                var spec = ShelvesetSpec.Parse(commandLine.Positionals[0], toolName);
                name = spec.Name;
                owner = spec.Owner ?? owner;
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            string? ownerFilter = owner == "*" ? null : owner ?? context.Client.AuthorizedUser;
            var shelvesets = context.Client.QueryShelvesets(name, ownerFilter);
            if (shelvesets.Count == 0 && name != null)
            {
                throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError, $"No shelveset named {name} was found for {ownerFilter ?? "any owner"}.",
                    $"'{toolName} shelvesets /owner:*' lists every shelveset.");
            }

            output.Lines(detailed ? RenderDetailed(shelvesets) : RenderBrief(shelvesets));
            output.Success(new JObject { ["shelvesets"] = new JArray(shelvesets.Select(s => s.ToJson()).Cast<object>().ToArray()) });
            return ExitCodes.Success;
        }

        internal static IEnumerable<string> RenderBrief(IReadOnlyList<ShelvesetInfo> shelvesets)
        {
            if (shelvesets.Count == 0)
            {
                yield return "No shelvesets found.";
                yield break;
            }

            var table = new TextTable("Shelveset", "Owner", "Comment");
            foreach (var s in shelvesets.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                table.Add(s.Name, s.Owner, s.Comment ?? string.Empty);
            }

            foreach (string line in table.Render())
            {
                yield return line;
            }
        }

        internal static IEnumerable<string> RenderDetailed(IReadOnlyList<ShelvesetInfo> shelvesets)
        {
            if (shelvesets.Count == 0)
            {
                yield return "No shelvesets found.";
                yield break;
            }

            foreach (var s in shelvesets.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            {
                yield return new string('=', 79);
                foreach (string line in TextBlocks.Fields(new[]
                {
                    new KeyValuePair<string, string?>("Shelveset", s.Name),
                    new KeyValuePair<string, string?>("Owner", s.Owner),
                    new KeyValuePair<string, string?>("Date", OutputWriter.IsoDate(s.Date)),
                    new KeyValuePair<string, string?>("Comment", string.Empty),
                }))
                {
                    yield return line;
                }

                foreach (string line in TextBlocks.Indented(s.Comment))
                {
                    yield return line;
                }

                yield return string.Empty;
            }
        }
    }

    /// <summary>
    /// `tfp unshelve shelvesetname[;owner] [itemspec ...] [/recursive] [/nomerge] [/noautoresolve]`: restores the changes into the
    /// workspace; open conflicts of the merge are TFP0403 (exit 1). The shelveset stays on the server (no /move).
    /// </summary>
    internal sealed class UnshelveCommand : ICommand
    {
        public string Name => "unshelve";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count == 0)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "unshelve needs the shelveset name.", $"Example: '{toolName} unshelve MyWork;user@example.com'. tfp never opens the shelveset dialog.");
            }

            var (name, owner) = ShelvesetSpec.Parse(commandLine.Positionals[0], toolName);
            var items = commandLine.Positionals.Skip(1).Select(p => ItemSpecParser.Parse(p)).ToList();
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            var queries = items.Count == 0 ? null : items.Select(i => new ItemQuery(context.ServerPathOf(i, toolName), recursion, i.DeletionId)).ToList();

            var result = workspace.Unshelve(name, owner, queries, !commandLine.Has("nomerge"), commandLine.Has("noautoresolve"));
            output.Lines(WorkspaceCommandSupport.GroupedByFolder(result.Changes, c => $"Unshelving {c.ChangeText}: {WorkspaceCommandSupport.NameOf(c)}"));
            foreach (var failure in result.Failures)
            {
                output.ItemError(WorkspaceCommandSupport.ToError(failure, toolName));
            }

            bool deleted = false;
            if (commandLine.Has("move"))
            {
                // tf.exe deletes the shelveset after a clean unshelve (phase 4 capture `unshelve/move`); with conflicts or failures it stays.
                if (result.Conflicts.Count == 0 && result.Failures.Count == 0 && queries == null)
                {
                    string deleteOwner = owner ?? context.Client.AuthorizedUser;
                    context.Client.DeleteShelveset(name, deleteOwner);
                    deleted = true;
                    output.Line($"Shelveset {name};{deleteOwner} deleted.");
                }
                else
                {
                    output.Warning($"Shelveset {name} was kept: /move deletes it only after a complete unshelve without conflicts or failures.");
                }
            }

            foreach (var conflict in result.Conflicts)
            {
                output.ItemError(new TfpException(ErrorCodes.GetConflicts, ExitCodes.PartialSuccess,
                    $"Conflict {System.IO.Path.GetFileName(conflict.Path)}: {conflict.Message}",
                    $"Resolve it with '{toolName} resolve {conflict.Path} /auto:TakeTheirs|KeepYours|AutoMerge'.", item: conflict.Path));
            }

            output.Success(new JObject
            {
                ["shelveset"] = ShelvesetSpec.ToJson(name, owner ?? context.Client.AuthorizedUser),
                ["changes"] = new JArray(result.Changes.Select(c => c.ToJson()).Cast<object>().ToArray()),
                ["conflicts"] = new JArray(result.Conflicts.Select(c => c.ToJson()).Cast<object>().ToArray()),
                ["deleted"] = deleted,
            });
            return ExitCodes.Success;
        }
    }
}
