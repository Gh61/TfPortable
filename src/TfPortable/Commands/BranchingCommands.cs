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
    /// Phase 2 (SPEC.md "Fáze 2"): `merge`, `branch`, `rollback` and `undelete` pend changes in the local workspace and
    /// write nothing to the server until `checkin`; `merges` reads the merge history. The merge-like commands print one
    /// line per pended change in the form of tf.exe (`merge, edit: $/src;C83~C83 -> $/dst;C80`).
    /// </summary>
    internal static class BranchingCommands
    {
        /// <summary>`tfp merge source destination [/recursive] [/version:v[~v]] [/preview] [/force] [/baseless] [/discard] [/noautoresolve] [/noimplicitbaseless] [/conservative]`.</summary>
        public static int Merge(CommandLine commandLine, OutputWriter output, string toolName)
        {
            WorkspaceCommandSupport.RejectServerLocks(commandLine);
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var summary = context.Workspace?.Summary;
            var items = commandLine.Positionals.Select(p => ItemSpecParser.Parse(p, summary?.Name, summary?.Owner)).ToList();
            string source = context.ServerPathOf(items[0], toolName);
            string target = context.ServerPathOf(items[1], toolName);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;

            VersionSpec? from = null;
            VersionSpec? to = null;
            string? versionText = commandLine.Value("version");
            if (!string.IsNullOrEmpty(versionText))
            {
                var range = VersionSpecParser.ParseRange(versionText!, summary?.Name, summary?.Owner);
                from = range.IsRange ? range.From : null;
                to = range.To;
            }

            if (commandLine.Has("preview"))
            {
                // The client library has no preview of a merge: the candidates (the changesets not yet merged) and their
                // changes are listed in the form a merge would pend them (docs/compat.md §15).
                var candidates = context.Client.GetMergeCandidates(source, target, recursion);
                var preview = new List<JObject>();
                var lines = new List<string>();
                foreach (var candidate in candidates)
                {
                    var changeset = context.Client.GetChangeset(candidate.Changeset.Id);
                    foreach (var change in (changeset.Changes ?? Array.Empty<ChangeInfo>()).Where(c => IsUnder(c.ServerPath, source)))
                    {
                        string targetPath = target + change.ServerPath.Substring(source.Length);
                        lines.Add($"merge, {change.ChangeText}: {change.ServerPath};C{changeset.Id} -> {targetPath}");
                        preview.Add(new JObject
                        {
                            ["changeTypes"] = new JArray(change.ChangeTypes.Cast<object>().ToArray()),
                            ["sourcePath"] = change.ServerPath,
                            ["sourceVersion"] = changeset.Id,
                            ["targetPath"] = targetPath,
                            ["partial"] = candidate.Partial,
                        });
                    }
                }

                if (lines.Count == 0)
                {
                    output.Line("There are no changes to merge.");
                }

                output.Lines(lines);
                output.Success(new JObject
                {
                    ["source"] = source,
                    ["target"] = target,
                    ["preview"] = true,
                    ["changes"] = new JArray(preview.Cast<object>().ToArray()),
                    ["conflicts"] = new JArray(),
                });
                return ExitCodes.Success;
            }

            var flags = MergeFlags.None;
            if (commandLine.Has("force")) { flags |= MergeFlags.Force; }
            if (commandLine.Has("baseless")) { flags |= MergeFlags.Baseless; }
            if (commandLine.Has("discard")) { flags |= MergeFlags.Discard; }
            if (commandLine.Has("noautoresolve")) { flags |= MergeFlags.NoAutoResolve; }
            if (commandLine.Has("noimplicitbaseless")) { flags |= MergeFlags.NoImplicitBaseless; }
            if (commandLine.Has("conservative")) { flags |= MergeFlags.Conservative; }

            var result = workspace.Merge(new ItemQuery(source, recursion), target, from, to, flags);
            ReportMergeResult(output, result, "There are no changes to merge.", toolName);
            output.Success(new JObject
            {
                ["source"] = source,
                ["target"] = target,
                ["preview"] = false,
                ["changes"] = new JArray(result.Changes.Select(c => c.ToJson()).Cast<object>().ToArray()),
                ["conflicts"] = new JArray(result.Conflicts.Select(c => c.ToJson()).Cast<object>().ToArray()),
            });
            return ExitCodes.Success;
        }

        /// <summary>Lines of a merge-like result and its conflicts (TFP0403, exit 1) and failures.</summary>
        internal static void ReportMergeResult(OutputWriter output, MergeResult result, string nothingText, string toolName)
        {
            foreach (var change in result.Changes)
            {
                output.Line(change.MergeLine);
            }

            if (result.Changes.Count == 0 && result.Conflicts.Count == 0 && result.Failures.Count == 0)
            {
                output.Line(nothingText);
            }

            foreach (var conflict in result.Conflicts)
            {
                output.ItemError(new TfpException(ErrorCodes.GetConflicts, ExitCodes.PartialSuccess,
                    $"Conflict ({conflict.Type}): {conflict.Message}",
                    $"Resolve it with '{toolName} resolve {conflict.Path} /auto:TakeTheirs|KeepYours|AutoMerge'.", item: conflict.Path));
            }

            foreach (var failure in result.Failures)
            {
                output.ItemError(WorkspaceCommandSupport.ToError(failure, toolName));
            }
        }

        private static bool IsUnder(string path, string folder) =>
            string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>`tfp branch olditem newitem [/version:v] [/noget] [/silent]`: the branch is a pending change until `checkin`.</summary>
    internal sealed class BranchCommand : ICommand
    {
        public string Name => "branch";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            WorkspaceCommandSupport.RejectServerLocks(commandLine);
            if (commandLine.Positionals.Count != 2)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "branch needs the source item and the new item.", $"Example: '{toolName} branch $/Project/Main $/Project/Feature-x'.");
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var summary = context.Workspace?.Summary;
            var source = ItemSpecParser.Parse(commandLine.Positionals[0], summary?.Name, summary?.Owner);
            var target = ItemSpecParser.Parse(commandLine.Positionals[1], summary?.Name, summary?.Owner);
            string? versionText = commandLine.Value("version");
            VersionSpec version = source.Version ?? (versionText == null ? VersionSpec.Latest : VersionSpecParser.ParseSingle(versionText, summary?.Name, summary?.Owner));

            var result = workspace.PendBranch(context.ServerPathOf(source, toolName), target.Path, version, !commandLine.Has("noget"), commandLine.Has("silent"));
            output.Line(target.Path);
            output.Line();
            output.Lines(WorkspaceCommandSupport.GroupedByFolder(result.Changes, WorkspaceCommandSupport.NameOf));
            var skipped = WorkspaceCommandSupport.ReportFailures(output, result.Failures, toolName);
            var json = WorkspaceCommandSupport.ChangesJson(result.Changes, skipped);
            json["source"] = context.ServerPathOf(source, toolName);
            json["target"] = target.Path;
            output.Success(json);
            return ExitCodes.Success;
        }
    }

    /// <summary>`tfp undelete itemspec[;deletionId] ... [/recursive] [/noget]`: a deleted item comes back as a pending undelete.</summary>
    internal sealed class UndeleteCommand : ICommand
    {
        public string Name => "undelete";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            WorkspaceCommandSupport.RejectServerLocks(commandLine);
            var items = WorkspaceCommandSupport.Items(commandLine, toolName, false, "undelete $/Project/Main/old.txt;X12");
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;

            var result = workspace.PendUndelete(items.Select(i => new ItemQuery(i.Path, recursion, i.DeletionId)).ToList(), !commandLine.Has("noget"));
            output.Lines(WorkspaceCommandSupport.GroupedByFolder(result.Changes, WorkspaceCommandSupport.NameOf));
            var skipped = WorkspaceCommandSupport.ReportFailures(output, result.Failures, toolName);
            output.Success(WorkspaceCommandSupport.ChangesJson(result.Changes, skipped));
            return ExitCodes.Success;
        }
    }

    /// <summary>
    /// `tfp rollback /changeset:C85[~C90] [itemspec ...]` or `tfp rollback /toversion:v itemspec ... [/recursive]` (with `/keepmergehistory`,
    /// `/noautoresolve`, `/version:v` for the item paths): the reverse of the changesets as pending changes.
    /// </summary>
    internal sealed class RollbackCommand : ICommand
    {
        public string Name => "rollback";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            WorkspaceCommandSupport.RejectServerLocks(commandLine);
            string? changesetText = commandLine.Value("changeset");
            string? toVersionText = commandLine.Value("toversion");
            if ((changesetText == null) == (toVersionText == null))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "rollback needs exactly one of /changeset:<c>[~<c>] and /toversion:<version>.",
                    $"Examples: '{toolName} rollback /changeset:C85', '{toolName} rollback /toversion:C84 Main\\\\src /recursive'.");
            }

            var items = WorkspaceCommandSupport.Items(commandLine, toolName, true, "rollback /changeset:C85");
            if (toVersionText != null && items.Count == 0)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "rollback /toversion needs at least one itemspec.", $"Example: '{toolName} rollback /toversion:C84 Main\\\\src /recursive'.");
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true);
            var workspace = context.RequireWorkspace(toolName);
            var summary = context.Workspace?.Summary;
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            var queries = items.Count == 0 ? null : items.Select(i => new ItemQuery(i.Path, recursion, i.DeletionId)).ToList();
            string? itemVersionText = commandLine.Value("version");
            VersionSpec? itemVersion = itemVersionText == null ? null : VersionSpecParser.ParseSingle(itemVersionText, summary?.Name, summary?.Owner);

            VersionSpec? from;
            VersionSpec to;
            bool toVersion = toVersionText != null;
            if (toVersion)
            {
                from = null;
                to = VersionSpecParser.ParseSingle(toVersionText!, summary?.Name, summary?.Owner);
            }
            else
            {
                var range = VersionSpecParser.ParseRange(changesetText!, summary?.Name, summary?.Owner);
                from = range.IsRange ? range.From : range.To;
                to = range.To;
            }

            var result = workspace.Rollback(queries, itemVersion, from, to, toVersion, commandLine.Has("keepmergehistory"), commandLine.Has("noautoresolve"));
            BranchingCommands.ReportMergeResult(output, result, "There is nothing to roll back.", toolName);
            output.Success(new JObject
            {
                ["mode"] = toVersion ? "toversion" : "changeset",
                ["from"] = from?.DisplayString,
                ["to"] = to.DisplayString,
                ["changes"] = new JArray(result.Changes.Select(c => c.ToJson()).Cast<object>().ToArray()),
                ["conflicts"] = new JArray(result.Conflicts.Select(c => c.ToJson()).Cast<object>().ToArray()),
            });
            return ExitCodes.Success;
        }
    }

    /// <summary>`tfp merges [source] destination [/recursive] [/showall] [/format:brief|detailed]`: which changesets were merged into the destination.</summary>
    internal sealed class MergesCommand : ICommand
    {
        public string Name => "merges";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count == 0 || commandLine.Positionals.Count > 2)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "merges needs a destination and optionally a source.", $"Example: '{toolName} merges $/Project/Main $/Project/Release /recursive'.");
            }

            bool detailed = string.Equals(commandLine.Value("format"), "detailed", StringComparison.OrdinalIgnoreCase);
            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var summary = context.Workspace?.Summary;
            var items = commandLine.Positionals.Select(p => ItemSpecParser.Parse(p, summary?.Name, summary?.Owner)).ToList();
            string? source = items.Count == 2 ? context.ServerPathOf(items[0], toolName) : null;
            string target = context.ServerPathOf(items[items.Count - 1], toolName);
            var merges = context.Client.QueryMerges(source, target, commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None, commandLine.Has("showall"));

            output.Lines(RenderTable(merges));
            if (detailed)
            {
                // The detailed form adds the block of every target changeset with its items (one call per distinct changeset).
                var detailedMerges = new List<MergeHistoryInfo>();
                var fetched = new Dictionary<int, ChangesetInfo>();
                foreach (var merge in merges)
                {
                    if (!fetched.TryGetValue(merge.TargetChangeset.Id, out var changeset))
                    {
                        changeset = context.Client.GetChangeset(merge.TargetChangeset.Id);
                        fetched[changeset.Id] = changeset;
                    }

                    detailedMerges.Add(new MergeHistoryInfo(merge.SourceChangeset, changeset, merge.Partial));
                }

                merges = detailedMerges;
                output.Line();
                output.Lines(HistoryCommand.RenderDetailed(fetched.Values.OrderBy(c => c.Id).ToList(), false));
            }

            output.Success(new JObject
            {
                ["source"] = source,
                ["target"] = target,
                ["merges"] = new JArray(merges.Select(m => m.ToJson(detailed)).Cast<object>().ToArray()),
            });
            return ExitCodes.Success;
        }

        internal static IEnumerable<string> RenderTable(IReadOnlyList<MergeHistoryInfo> merges)
        {
            if (merges.Count == 0)
            {
                yield return "No merge history was found.";
                yield break;
            }

            var table = new TextTable("Changeset", "Merged in Changeset", "Author", "Date");
            foreach (var merge in merges.OrderBy(m => m.SourceChangeset).ThenBy(m => m.TargetChangeset.Id))
            {
                table.Add(merge.SourceChangeset + (merge.Partial ? "*" : string.Empty), merge.TargetChangeset.Id.ToString(), merge.TargetChangeset.Owner, OutputWriter.IsoDate(merge.TargetChangeset.Date) ?? string.Empty);
            }

            foreach (string line in table.Render())
            {
                yield return line;
            }
        }
    }
}
