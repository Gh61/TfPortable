using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.TeamFoundation.VersionControl.Client;
using Newtonsoft.Json.Linq;
using TfPortable.Cli;
using TfPortable.Context;
using TfPortable.Errors;
using TfPortable.Output;

namespace TfPortable.Commands
{
    /// <summary>
    /// `tfp reconcile /promote [/adds] [/deletes] ...` pends the changes detected on disk; `tfp reconcile /clean ...`
    /// deletes files that are not under version control (needs /preview or /noprompt, TFP0405). docs/compat.md §7.
    /// </summary>
    internal sealed class ReconcileCommand : ICommand
    {
        public string Name => "reconcile";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            bool clean = commandLine.Has("clean");
            bool promote = commandLine.Has("promote");
            if (clean == promote)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "reconcile needs exactly one of /clean or /promote.", $"Example: '{toolName} reconcile /promote /recursive /preview'.");
            }

            bool preview = commandLine.Has("preview");
            if (clean && !preview && !commandLine.Has("noprompt"))
            {
                throw new TfpException(ErrorCodes.CleanNeedsConfirmation, ExitCodes.GeneralError,
                    "reconcile /clean deletes files that are not under version control; it needs an explicit confirmation.",
                    $"Run '{toolName} reconcile /clean ... /preview' to see what would be deleted, then repeat it with /noprompt.");
            }

            var items = WorkspaceCommandSupport.Items(commandLine, toolName, true, "reconcile /promote /recursive");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.OneLevel;
            // Without an itemspec the scope is the current folder (never the whole workspace): /clean deletes files.
            var queries = items.Count == 0
                ? new List<ItemQuery> { new ItemQuery(Directory.GetCurrentDirectory(), recursion) }
                : WorkspaceCommandSupport.Queries(items, recursion, context);

            var exclude = commandLine.Values("exclude").Select(Pattern).ToList();
            var candidates = workspace.GetPendingChanges(queries, true).Candidates
                .Where(c => c.LocalPath != null && !exclude.Any(e => e.IsMatch(Path.GetFileName(c.LocalPath!)) || e.IsMatch(c.LocalPath!)))
                .ToList();

            return promote ? Promote(commandLine, output, workspace, candidates, preview, toolName) : Clean(output, candidates, preview);
        }

        private static int Promote(CommandLine commandLine, OutputWriter output, ITfvcWorkspace workspace, List<PendingChangeInfo> candidates, bool preview, string toolName)
        {
            bool addsOnly = commandLine.Has("adds") && !commandLine.Has("deletes");
            bool deletesOnly = commandLine.Has("deletes") && !commandLine.Has("adds");
            var adds = deletesOnly ? new List<string>() : candidates.Where(c => c.ChangeTypes.Contains("add")).Select(c => c.LocalPath!).ToList();
            var deletes = addsOnly ? new List<string>() : candidates.Where(c => c.ChangeTypes.Contains("delete")).Select(c => c.LocalPath!).ToList();

            var promoted = new List<PendingChangeInfo>();
            var failures = new List<FailureInfo>();
            if (preview)
            {
                promoted.AddRange(candidates.Where(c => adds.Contains(c.LocalPath!) || deletes.Contains(c.LocalPath!)));
            }
            else
            {
                if (adds.Count > 0)
                {
                    var result = workspace.PendAdd(adds, false, null, !commandLine.Has("noignore"));
                    promoted.AddRange(result.Changes);
                    failures.AddRange(result.Failures);
                }

                if (deletes.Count > 0)
                {
                    var result = workspace.PendDelete(deletes, RecursionType.None);
                    promoted.AddRange(result.Changes);
                    failures.AddRange(result.Failures);
                }
            }

            if (promoted.Count == 0 && failures.Count == 0)
            {
                output.Line("No matching pending changes were found.");
            }
            else
            {
                output.Lines(WorkspaceCommandSupport.GroupedByFolder(promoted, c => $"{(preview ? "Would promote" : "Promoting")} {c.ChangeText}: {WorkspaceCommandSupport.NameOf(c)}"));
            }

            WorkspaceCommandSupport.ReportFailures(output, failures, toolName);
            output.Success(new JObject
            {
                ["promoted"] = new JArray(promoted.Select(c => c.ToJson()).Cast<object>().ToArray()),
                ["deleted"] = new JArray(),
                ["differences"] = new JArray(candidates.Select(c => new JObject { ["localPath"] = c.LocalPath, ["status"] = c.ChangeTypes.Contains("add") ? "added" : c.ChangeTypes.Contains("delete") ? "deleted" : "modified" }).Cast<object>().ToArray()),
                ["preview"] = preview,
            });
            return ExitCodes.Success;
        }

        private static int Clean(OutputWriter output, List<PendingChangeInfo> candidates, bool preview)
        {
            // Untracked files are the candidates for add; folders are removed after their files, deepest first.
            var untracked = candidates.Where(c => c.ChangeTypes.Contains("add")).OrderByDescending(c => c.LocalPath!.Length).ToList();
            var deleted = new JArray();
            var lines = new List<KeyValuePair<string, string>>();
            foreach (var item in untracked)
            {
                string path = item.LocalPath!;
                if (!preview)
                {
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            if (Directory.EnumerateFileSystemEntries(path).Any())
                            {
                                continue;
                            }

                            Directory.Delete(path);
                        }
                        else if (File.Exists(path))
                        {
                            File.SetAttributes(path, FileAttributes.Normal);
                            File.Delete(path);
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        output.ItemError(new TfpException(ErrorCodes.Unexpected, ExitCodes.PartialSuccess, $"{path}: {ex.Message}", "Close the file or check its permissions.", item: path, inner: ex));
                        continue;
                    }
                }

                deleted.Add(path);
                lines.Add(new KeyValuePair<string, string>(Path.GetDirectoryName(path) ?? path, $"Deleting {Path.GetFileName(path)}"));
            }

            if (lines.Count == 0)
            {
                output.Line("There are no untracked files to delete.");
            }
            else
            {
                output.Lines(TextBlocks.Grouped(lines.OrderBy(l => l.Key, StringComparer.InvariantCultureIgnoreCase).ThenBy(l => l.Value, StringComparer.InvariantCultureIgnoreCase)));
            }

            output.Success(new JObject
            {
                ["promoted"] = new JArray(),
                ["deleted"] = deleted,
                ["differences"] = new JArray(candidates.Select(c => new JObject { ["localPath"] = c.LocalPath, ["status"] = c.ChangeTypes.Contains("add") ? "added" : c.ChangeTypes.Contains("delete") ? "deleted" : "modified" }).Cast<object>().ToArray()),
                ["preview"] = preview,
            });
            return ExitCodes.Success;
        }

        /// <summary>Wildcard pattern (`*`, `?`) of /exclude as a regex matched against the file name or the whole path.</summary>
        internal static Regex Pattern(string wildcard) =>
            new Regex("^" + Regex.Escape(wildcard).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
