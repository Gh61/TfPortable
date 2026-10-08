using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>
    /// `tfp resolve [itemspec ...] [/auto:...] [/preview] [/recursive] [/newname:path]` (docs/compat.md §7): without
    /// /auto the conflicts are listed on stderr (exit 1 while any remain); with /auto each is resolved and reported as
    /// `Item <path> is resolved as <Name>.`; a failed content merge prints the merge summary and stays (exit 1).
    /// </summary>
    internal sealed class ResolveCommand : ICommand
    {
        public string Name => "resolve";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, true, "resolve /auto:TakeTheirs");
            string? auto = commandLine.Value("auto");
            string? resolution = auto == null ? null : Canonical(auto);
            bool forced = auto != null && auto.Equals("AutoMergeForced", StringComparison.OrdinalIgnoreCase);
            bool preview = commandLine.Has("preview") || resolution == null;

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var paths = items.Count == 0 ? new List<string>() : WorkspaceCommandSupport.LocalPaths(items, context, toolName);
            var conflicts = workspace.QueryConflicts(paths, commandLine.Has("recursive") || items.Count == 0);

            var resolved = new JArray();
            var remaining = new List<ConflictInfo>();
            if (conflicts.Count == 0)
            {
                output.Line("There are no conflicts to resolve.");
            }

            foreach (var conflict in conflicts)
            {
                if (preview)
                {
                    remaining.Add(conflict);
                    output.ItemError(new TfpException(ErrorCodes.GetConflicts, ExitCodes.PartialSuccess, $"{conflict.Path}: {conflict.Message}",
                        $"Resolve it with '{toolName} resolve {conflict.Path} /auto:TakeTheirs|KeepYours|AutoMerge'.", item: conflict.Path));
                    continue;
                }

                var result = workspace.Resolve(conflict, resolution!, commandLine.Value("newname"), forced);
                if (result.Resolved)
                {
                    output.Line($"Item {conflict.Path} is resolved as {result.ResolutionName}.");
                    if (result.Operations.Count > 0)
                    {
                        output.Lines(GetCommand.Render(new GetResult(result.Operations, Array.Empty<ConflictInfo>(), Array.Empty<FailureInfo>(), false, result.Operations.Count, 0, 0)));
                    }

                    resolved.Add(new JObject { ["localPath"] = conflict.LocalPath, ["serverPath"] = conflict.ServerPath, ["resolution"] = resolution });
                }
                else
                {
                    remaining.Add(conflict);
                    if (result.MergeSummary != null)
                    {
                        output.Line($"{conflict.Path}: {result.MergeSummary}.");
                    }

                    output.ItemError(new TfpException(ErrorCodes.GetConflicts, ExitCodes.PartialSuccess, $"{conflict.Path}: {result.Message ?? conflict.Message}",
                        "Resolve it with /auto:TakeTheirs or /auto:KeepYours, or merge the file and run /auto:AutoMergeForced.", item: conflict.Path));
                }
            }

            output.Success(new JObject
            {
                ["conflicts"] = new JArray(remaining.Select(c => c.ToJson()).Cast<object>().ToArray()),
                ["resolved"] = resolved,
                ["preview"] = preview,
            });
            return ExitCodes.Success;
        }

        /// <summary>Canonical lower-case resolution (docs/json-schema.md) for the help's names and the documentation's `Accept*` aliases.</summary>
        internal static string Canonical(string auto)
        {
            switch (auto.ToLowerInvariant())
            {
                case "automerge":
                case "acceptmerge": return "automerge";
                case "automergeforced": return "automerge";
                case "taketheirs":
                case "accepttheirs": return "taketheirs";
                case "keepyours":
                case "acceptyours": return "keepyours";
                case "overwritelocal": return "overwritelocal";
                case "deleteconflict": return "deleteconflict";
                case "keepyoursrenametheirs":
                case "acceptyoursrenametheirs": return "keepyoursrenametheirs";
                default:
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"'{auto}' is not a valid value of /auto.",
                        "Use AutoMerge, AutoMergeForced, TakeTheirs, KeepYours, OverwriteLocal, DeleteConflict or KeepYoursRenameTheirs.");
            }
        }
    }
}
