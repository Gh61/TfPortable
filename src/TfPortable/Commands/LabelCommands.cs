using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Common;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>`labelname[@scope]` of label and unlabel; without a scope, the team project of the first item.</summary>
    internal static class LabelSpec
    {
        public static (string Name, string? Scope) Parse(string text, string toolName)
        {
            string[] parts = text.Split(new[] { '@' }, 2);
            string name = parts[0].Trim();
            if (name.Length == 0)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "The label name is empty.", $"Example: '{toolName} label v1.0@$/Project Main /recursive'.");
            }

            string? scope = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : null;
            if (scope != null && !VersionControlPath.IsServerItem(scope))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"The label scope {scope} is not a server path.", "The scope starts with $/, for example v1.0@$/Project.");
            }

            return (name, scope);
        }

        /// <summary>Server paths of the items (through the workspace for local paths) and the default scope: their team project.</summary>
        public static List<ItemQuery> ServerQueries(IReadOnlyList<ParsedItemSpec> items, RecursionType recursion, WorkspaceContext context, string toolName)
        {
            return items.Select(i => new ItemQuery(context.ServerPathOf(i, toolName), recursion, i.DeletionId)).ToList();
        }

        public static string DefaultScope(IReadOnlyList<ItemQuery> queries)
        {
            try
            {
                return VersionControlPath.GetTeamProject(queries[0].Path);
            }
            catch (Exception)
            {
                return VersionControlPath.RootFolder;
            }
        }

        public static IEnumerable<string> Render(LabelOperationResult result)
        {
            foreach (var r in result.Results)
            {
                string verb = r.Status == "created" ? "Created" : r.Status == "deleted" ? "Deleted" : "Updated";
                yield return $"{verb} label {r.Name}@{r.Scope}";
            }
        }

        public static JObject ToJson(string name, string scope, LabelOperationResult result, JArray skipped) => new JObject
        {
            ["label"] = new JObject { ["name"] = name, ["scope"] = scope },
            ["results"] = new JArray(result.Results.Select(r => r.ToJson()).Cast<object>().ToArray()),
            ["skipped"] = skipped,
        };
    }

    /// <summary>
    /// `tfp label labelname[@scope] itemspec ... [/version:v] [/comment:text] [/child:replace|merge] [/recursive]`, and `label /delete`
    /// (protection level 3). Local paths are labelled at the workspace version, server paths at the latest, unless /version says otherwise.
    /// </summary>
    internal sealed class LabelCommand : ICommand
    {
        public string Name => "label";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count == 0)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "label needs the label name and at least one itemspec.", $"Example: '{toolName} label v1.0@$/Project Main /recursive'.");
            }

            var (name, scope) = LabelSpec.Parse(commandLine.Positionals[0], toolName);
            var items = commandLine.Positionals.Skip(1).Select(p => ItemSpecParser.Parse(p)).ToList();
            string? comment = CommentText.Optional(commandLine, toolName);

            if (commandLine.Has("delete"))
            {
                if (items.Count > 0)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "label /delete takes only the label name with its scope.", $"Example: '{toolName} label /delete v1.0@$/Project'.");
                }

                if (scope == null)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "label /delete needs the scope of the label.", $"Example: '{toolName} label /delete v1.0@$/Project'.");
                }

                using var deleteContext = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
                var deleted = deleteContext.Client.DeleteLabel(name, scope);
                output.Lines(LabelSpec.Render(deleted));
                output.Success(LabelSpec.ToJson(name, scope, deleted, new JArray()));
                return ExitCodes.Success;
            }

            if (items.Count == 0)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "label needs at least one itemspec.", $"Example: '{toolName} label v1.0@$/Project Main /recursive'.");
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: items.Any(i => !i.IsServerPath));
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            var queries = LabelSpec.ServerQueries(items, recursion, context, toolName);
            scope ??= LabelSpec.DefaultScope(queries);

            var summary = context.Workspace?.Summary;
            string? versionText = commandLine.Value("version");
            VersionSpec? explicitVersion = versionText == null ? null : VersionSpecParser.ParseSingle(versionText, summary?.Name, summary?.Owner);
            var labelItems = new List<LabelItemQuery>();
            for (int i = 0; i < items.Count; i++)
            {
                VersionSpec version = items[i].Version ?? explicitVersion
                    ?? (!items[i].IsServerPath && summary != null ? new WorkspaceVersionSpec(summary.Name, summary.Owner) : VersionSpec.Latest);
                labelItems.Add(new LabelItemQuery(queries[i], version, false));
            }

            var result = context.Client.CreateLabel(name, scope, comment, labelItems, commandLine.Value("child") ?? "fail");
            if (result.Results.Count == 0 && result.Failures.Count > 0)
            {
                throw WorkspaceCommandSupport.ToError(result.Failures[0], toolName);
            }

            output.Lines(LabelSpec.Render(result));
            var skipped = WorkspaceCommandSupport.ReportFailures(output, result.Failures, toolName);
            output.Success(LabelSpec.ToJson(name, scope, result, skipped));
            return ExitCodes.Success;
        }
    }

    /// <summary>`tfp unlabel labelname[@scope] itemspec ... [/recursive]`: an item that is not in the label is TFP0301 (exit 1, as tf.exe).</summary>
    internal sealed class UnlabelCommand : ICommand
    {
        public string Name => "unlabel";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count < 2)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "unlabel needs the label name and at least one itemspec.", $"Example: '{toolName} unlabel v1.0@$/Project Main\\old.txt'.");
            }

            var (name, scope) = LabelSpec.Parse(commandLine.Positionals[0], toolName);
            var items = commandLine.Positionals.Skip(1).Select(p => ItemSpecParser.Parse(p)).ToList();
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: items.Any(i => !i.IsServerPath));
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            var queries = LabelSpec.ServerQueries(items, recursion, context, toolName);
            scope ??= LabelSpec.DefaultScope(queries);

            var result = context.Client.UnlabelItems(name, scope, queries);
            output.Lines(LabelSpec.Render(result));
            var skipped = WorkspaceCommandSupport.ReportFailures(output, result.Failures, toolName);
            output.Success(LabelSpec.ToJson(name, scope, result, skipped));
            return ExitCodes.Success;
        }
    }

    /// <summary>`tfp lock itemspec ... /lock:none|checkout|checkin [/recursive]`: the locked (or unlocked) items grouped by folder.</summary>
    internal sealed class LockCommand : ICommand
    {
        public string Name => "lock";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            string? level = commandLine.Value("lock");
            if (level == null)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "lock needs /lock:none, /lock:checkout or /lock:checkin.", $"Example: '{toolName} lock Main\\src\\App.cs /lock:checkin'.");
            }

            LockLevel lockLevel = level.Equals("none", StringComparison.OrdinalIgnoreCase) ? LockLevel.None
                : level.Equals("checkout", StringComparison.OrdinalIgnoreCase) ? LockLevel.CheckOut
                : LockLevel.Checkin;
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, false, "lock Main\\src\\App.cs /lock:checkin");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var paths = WorkspaceCommandSupport.LocalPaths(items, context, toolName);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;

            PendResult result;
            try
            {
                result = workspace.SetLock(paths, lockLevel, recursion);
            }
            catch (Exception ex) when (!(ex is TfpException) && IsCheckoutLockRefusal(ex.Message))
            {
                // TF400022: checkout locks do not exist in local workspaces (golden `lock/checkout-lock`, exit 1).
                output.ItemError(CheckoutLockRefused(ex.Message, paths.Count == 1 ? paths[0] : null));
                output.Success(new JObject { ["lock"] = "checkout", ["changes"] = new JArray(), ["count"] = 0, ["skipped"] = new JArray() });
                return ExitCodes.Success;
            }

            foreach (var failure in result.Failures.Where(f => IsCheckoutLockRefusal(f.Message)).ToList())
            {
                output.ItemError(CheckoutLockRefused(failure.Message, failure.LocalPath ?? failure.ServerPath));
            }

            result = new PendResult(result.Changes, result.Failures.Where(f => !IsCheckoutLockRefusal(f.Message)).ToList(), result.Count);
            if (result.Changes.Count == 0 && result.Failures.Count == 0 && result.Count > 0)
            {
                // The library reported no pending change event (an unlock); list what was asked for.
                foreach (string path in paths)
                {
                    output.Line(path);
                }
            }
            else
            {
                output.Lines(WorkspaceCommandSupport.GroupedByFolder(result.Changes, WorkspaceCommandSupport.NameOf));
            }

            var skipped = WorkspaceCommandSupport.ReportFailures(output, result.Failures, toolName);
            output.Success(new JObject
            {
                ["lock"] = lockLevel == LockLevel.None ? "none" : lockLevel == LockLevel.CheckOut ? "checkout" : "checkin",
                ["changes"] = new JArray(result.Changes.Select(c => c.ToJson()).Cast<object>().ToArray()),
                ["count"] = result.Count,
                ["skipped"] = skipped,
            });
            return ExitCodes.Success;
        }

        private static bool IsCheckoutLockRefusal(string? message) => message != null && message.IndexOf("TF400022", StringComparison.Ordinal) >= 0;

        private static TfpException CheckoutLockRefused(string serverMessage, string? item) =>
            new TfpException(ErrorCodes.ServerError, ExitCodes.PartialSuccess, "Checkout locks are not supported in local workspaces.",
                "Use /lock:checkin, which local workspaces support.", "TF400022", serverMessage, item);
    }
}
