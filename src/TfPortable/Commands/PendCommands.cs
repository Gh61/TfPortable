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
    /// <summary>`tfp add itemspec ... [/recursive] [/lock:none] [/encoding:x] [/noignore]` (docs/compat.md §5).</summary>
    internal sealed class AddCommand : ICommand
    {
        public string Name => "add";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var lockLevel = WorkspaceCommandSupport.LockLevelOf(commandLine);
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, false, "add Main\\src\\New.cs");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var paths = WorkspaceCommandSupport.LocalPaths(items, context, toolName);

            // .tfignore applies to wildcard and /recursive expansion only; an explicitly named file is always added (docs/compat.md §5).
            bool expands = commandLine.Has("recursive") || items.Any(i => i.HasWildcard);
            // Without /encoding the client library detects the type itself: a BOM, binary, else utf-8 (verified in CI 2026-10-14).
            var result = workspace.PendAdd(paths, commandLine.Has("recursive"), commandLine.Value("encoding"), expands && !commandLine.Has("noignore"), lockLevel);
            output.Lines(WorkspaceCommandSupport.GroupedByFolder(result.Changes, WorkspaceCommandSupport.NameOf));
            var skipped = WorkspaceCommandSupport.ReportFailures(output, result.Failures, toolName);
            output.Success(WorkspaceCommandSupport.ChangesJson(result.Changes, skipped));
            return ExitCodes.Success;
        }
    }

    /// <summary>`tfp checkout itemspec ... [/recursive] [/lock:none] [/encoding:x]` (alias `edit`).</summary>
    internal sealed class CheckoutCommand : ICommand
    {
        public string Name => "checkout";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var lockLevel = WorkspaceCommandSupport.LockLevelOf(commandLine);
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, false, "checkout Main\\src\\App.cs");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var paths = WorkspaceCommandSupport.LocalPaths(items, context, toolName);

            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            var result = workspace.PendEdit(paths, recursion, commandLine.Value("encoding"), lockLevel);
            output.Lines(WorkspaceCommandSupport.GroupedByFolder(result.Changes, WorkspaceCommandSupport.NameOf));
            var skipped = WorkspaceCommandSupport.ReportFailures(output, result.Failures, toolName);
            output.Success(WorkspaceCommandSupport.ChangesJson(result.Changes, skipped));
            return ExitCodes.Success;
        }
    }

    /// <summary>`tfp delete itemspec ... [/recursive] [/lock:none] [/detect]` (aliases `del`, `rm`).</summary>
    internal sealed class DeleteCommand : ICommand
    {
        public string Name => "delete";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var lockLevel = WorkspaceCommandSupport.LockLevelOf(commandLine);
            bool detect = commandLine.Has("detect");
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, detect, "delete Main\\src\\Old.cs");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;

            var paths = WorkspaceCommandSupport.LocalPaths(items, context, toolName);
            if (detect)
            {
                // Files already missing from disk become pending deletes (candidates of the detection).
                var candidates = workspace.GetPendingChanges(WorkspaceCommandSupport.Queries(items, recursion, context), true).Candidates;
                paths = candidates.Where(c => c.ChangeTypes.Contains("delete") && c.LocalPath != null).Select(c => c.LocalPath!).ToList();
                if (paths.Count == 0)
                {
                    output.Line("No deleted files were detected.");
                    output.Success(WorkspaceCommandSupport.ChangesJson(Array.Empty<PendingChangeInfo>(), new JArray()));
                    return ExitCodes.Success;
                }

                recursion = RecursionType.None;
            }

            var result = workspace.PendDelete(paths, recursion, lockLevel);
            output.Lines(WorkspaceCommandSupport.GroupedByFolder(result.Changes, WorkspaceCommandSupport.NameOf));
            var skipped = WorkspaceCommandSupport.ReportFailures(output, result.Failures, toolName);
            output.Success(WorkspaceCommandSupport.ChangesJson(result.Changes, skipped));
            return ExitCodes.Success;
        }
    }

    /// <summary>`tfp rename olditem newitem [/lock:none]` (aliases `move`, `ren`). Not found is exit 100 (docs/compat.md §1.1).</summary>
    internal sealed class RenameCommand : ICommand
    {
        public string Name => "rename";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var lockLevel = WorkspaceCommandSupport.LockLevelOf(commandLine);
            if (commandLine.Positionals.Count != 2)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "rename needs exactly two items: the old and the new path.", $"Example: '{toolName} rename Main\\src\\Old.cs Main\\src\\New.cs'.");
            }

            var items = commandLine.Positionals.Select(p => ItemSpecParser.Parse(p)).ToList();
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var paths = WorkspaceCommandSupport.LocalPaths(items, context, toolName);

            var result = workspace.PendRename(paths[0], paths[1], lockLevel);
            if (result.Failures.Count > 0 && result.Changes.Count == 0)
            {
                // The original stops with exit 100 when the source does not exist (rename/not-found).
                throw WorkspaceCommandSupport.ToError(result.Failures[0], toolName);
            }

            output.Lines(WorkspaceCommandSupport.GroupedByFolder(result.Changes, WorkspaceCommandSupport.NameOf));
            var skipped = WorkspaceCommandSupport.ReportFailures(output, result.Failures, toolName);
            output.Success(WorkspaceCommandSupport.ChangesJson(result.Changes, skipped));
            return ExitCodes.Success;
        }
    }

    /// <summary>`tfp undo itemspec ... [/recursive]`: `Undoing <change>: <name>` grouped by folder; nothing to undo is exit 1.</summary>
    internal sealed class UndoCommand : ICommand
    {
        public string Name => "undo";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, false, "undo Main\\src\\App.cs");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;

            var result = workspace.Undo(WorkspaceCommandSupport.Queries(items, recursion, context));
            output.Lines(WorkspaceCommandSupport.GroupedByFolder(result.Changes, c => $"Undoing {c.ChangeText}: {WorkspaceCommandSupport.NameOf(c)}"));

            var notFound = new JArray();
            foreach (var failure in result.Failures)
            {
                output.ItemError(WorkspaceCommandSupport.ToError(failure, toolName));
                notFound.Add(failure.Item);
            }

            if (result.Changes.Count == 0 && result.Failures.Count == 0)
            {
                foreach (var item in items)
                {
                    output.ItemError(new TfpException(ErrorCodes.ItemNotFound, ExitCodes.PartialSuccess, $"No pending changes were found for {item.Path}.",
                        $"'{toolName} status' lists the pending changes.", item: item.Path));
                    notFound.Add(item.Path);
                }
            }

            output.Success(new JObject
            {
                ["undone"] = new JArray(result.Changes.Select(c => c.ToJson()).Cast<object>().ToArray()),
                ["notFound"] = notFound,
            });
            return ExitCodes.Success;
        }
    }
}
