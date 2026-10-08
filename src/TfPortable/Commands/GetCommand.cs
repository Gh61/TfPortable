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
    /// `tfp get [itemspec ...] [/version:spec] [/all] [/overwrite] [/force] [/remap] [/recursive] [/preview] [/noautoresolve]`.
    /// Output per docs/compat.md §6: `Getting`/`Replacing` lines grouped under absolute folder headers, `Deleting` lines
    /// flat; "All files are up to date." when nothing happened; conflicts are TFP0403 and writable files TFP0402 (exit 1).
    /// </summary>
    internal sealed class GetCommand : ICommand
    {
        public string Name => "get";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, true, "get /recursive");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var summary = workspace.Summary;

            VersionSpec version = VersionSpec.Latest;
            string? versionText = commandLine.Value("version");
            if (!string.IsNullOrEmpty(versionText))
            {
                var range = VersionSpecParser.ParseRange(versionText!, summary.Name, summary.Owner);
                if (range.IsRange)
                {
                    throw new TfpException(ErrorCodes.InvalidVersionSpec, ExitCodes.GeneralError, "get takes a single version, not a range.", "Example: /version:C1234.");
                }

                version = range.To;
            }

            var flags = GetFlags.None;
            if (commandLine.Has("all")) { flags |= GetFlags.All; }
            if (commandLine.Has("overwrite")) { flags |= GetFlags.Overwrite; }
            if (commandLine.Has("force")) { flags |= GetFlags.All | GetFlags.Overwrite; }
            if (commandLine.Has("preview")) { flags |= GetFlags.Preview; }
            if (commandLine.Has("remap")) { flags |= GetFlags.Remap; }
            if (commandLine.Has("noautoresolve")) { flags |= GetFlags.NoAutoResolve; }

            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            var queries = items.Count == 0 ? null : items.Select(i => new ItemQuery(i.Path, recursion, i.DeletionId)).ToList();
            var result = workspace.Get(queries, version, flags);

            output.Lines(Render(result));
            foreach (var conflict in result.Conflicts)
            {
                output.ItemError(new TfpException(ErrorCodes.GetConflicts, ExitCodes.PartialSuccess,
                    $"Conflict {Path.GetFileName(conflict.Path)}: {conflict.Message}",
                    $"Resolve it with '{toolName} resolve {conflict.Path} /auto:TakeTheirs|KeepYours|AutoMerge'.", item: conflict.Path));
            }

            var skipped = new JArray();
            foreach (var op in result.Operations.Where(o => o.Operation == "writable"))
            {
                skipped.Add(new JObject { ["localPath"] = op.LocalPath, ["reason"] = "writable" });
                output.ItemError(new TfpException(ErrorCodes.WritableFileSkipped, ExitCodes.PartialSuccess,
                    $"File {op.LocalPath} is writable and has no pending change; get did not overwrite it.",
                    $"Review local changes with '{toolName} diff'. Use '{toolName} get /overwrite' to overwrite it.", item: op.LocalPath));
            }

            foreach (var failure in result.Failures)
            {
                var error = WorkspaceCommandSupport.ToError(failure, toolName);
                if (failure.IsWarning)
                {
                    output.Warning(failure.Message, error.Code, item: failure.Item);
                }
                else
                {
                    output.ItemError(error);
                }
            }

            if (result.NumConflicts > 0 && result.Conflicts.Count == 0)
            {
                output.ItemError(new TfpException(ErrorCodes.GetConflicts, ExitCodes.PartialSuccess,
                    $"{result.NumConflicts} conflict(s) were created.", $"'{toolName} resolve' lists them."));
            }

            output.Success(ToJson(result, flags.HasFlag(GetFlags.Preview), skipped));
            return ExitCodes.Success;
        }

        /// <summary>Text lines: grouped `Getting`/`Replacing`, flat `Deleting`, conflict folder headers; "All files are up to date." when nothing to do.</summary>
        internal static IEnumerable<string> Render(GetResult result)
        {
            var grouped = result.Operations
                .Where(o => (o.Operation == "getting" || o.Operation == "replacing" || o.Operation == "conflict") && o.LocalPath != null)
                .Select(o => new KeyValuePair<string, string?>(Path.GetDirectoryName(o.LocalPath!) ?? o.LocalPath!, o.Operation == "conflict" ? null : $"{Capitalize(o.Operation)} {Path.GetFileName(o.LocalPath!)}"))
                .ToList();

            var lines = new List<string>();
            string? current = null;
            foreach (var pair in grouped)
            {
                if (!string.Equals(pair.Key, current, StringComparison.OrdinalIgnoreCase))
                {
                    if (current != null)
                    {
                        lines.Add(string.Empty);
                    }

                    lines.Add(pair.Key + ":");
                    current = pair.Key;
                }

                if (pair.Value != null)
                {
                    lines.Add(pair.Value);
                }
            }

            var deleting = result.Operations.Where(o => o.Operation == "deleting").Select(o => $"Deleting {o.LocalPath ?? o.ServerPath}").ToList();
            if (deleting.Count > 0)
            {
                if (lines.Count > 0)
                {
                    lines.Add(string.Empty);
                }

                lines.AddRange(deleting);
            }

            if (lines.Count == 0 && result.Conflicts.Count == 0 && !result.Operations.Any(o => o.Operation == "writable"))
            {
                lines.Add("All files are up to date.");
            }

            return lines;
        }

        internal static JObject ToJson(GetResult result, bool preview, JArray skipped)
        {
            var operations = result.Operations.Where(o => o.Operation == "getting" || o.Operation == "replacing" || o.Operation == "deleting").ToList();
            return new JObject
            {
                ["operations"] = new JArray(operations.Select(o => o.ToJson()).Cast<object>().ToArray()),
                ["conflicts"] = new JArray(result.Conflicts.Select(c => c.ToJson()).Cast<object>().ToArray()),
                ["skipped"] = skipped,
                ["counts"] = new JObject
                {
                    ["getting"] = operations.Count(o => o.Operation == "getting"),
                    ["replacing"] = operations.Count(o => o.Operation == "replacing"),
                    ["deleting"] = operations.Count(o => o.Operation == "deleting"),
                    ["conflicts"] = Math.Max(result.NumConflicts, result.Conflicts.Count),
                    ["skipped"] = skipped.Count,
                },
                ["preview"] = preview,
            };
        }

        private static string Capitalize(string word) => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word.Substring(1);
    }
}
