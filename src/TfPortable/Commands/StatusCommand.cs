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
    /// <summary>
    /// `tfp status [itemspec ...] [/recursive] [/format:brief|detailed|json] [/user:name|*] [/shelveset:name[;owner]] [/nodetect]`:
    /// pending changes and, in a local workspace, detected changes (candidates) in a separate section. Shape per
    /// docs/compat.md §4: brief table grouped by server folder, detailed blocks, `/user` adds a User column and a
    /// warning, `/shelveset` replaces the local path by the shelveset name.
    /// </summary>
    internal sealed class StatusCommand : ICommand
    {
        public string Name => "status";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            bool detailed = string.Equals(commandLine.Value("format"), "detailed", StringComparison.OrdinalIgnoreCase);
            string? user = commandLine.Value("user");
            string? shelveset = commandLine.Value("shelveset");
            if (user != null && shelveset != null)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/user and /shelveset cannot be combined.", "Use one of them.");
            }

            var items = commandLine.Positionals.Select(p => ItemSpecParser.Parse(p)).ToList();
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.OneLevel;

            if (shelveset != null)
            {
                using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
                string[] parts = shelveset.Split(new[] { ';' }, 2);
                var queries = items.Select(i => new ItemQuery(context.Workspace == null || i.IsServerPath ? i.Path : context.ServerPathOf(i, toolName), recursion, i.DeletionId)).ToList();
                var sets = context.Client.QueryShelvedChanges(parts[0], parts.Length > 1 ? parts[1] : null, queries);
                var changes = sets.SelectMany(s => s.Changes).ToList();
                output.Lines(detailed ? RenderDetailed(changes, context.Workspace?.Summary) : RenderShelveset(changes));
                output.Success(ToJson(new PendingChangeSet(changes, Array.Empty<PendingChangeInfo>())));
                return ExitCodes.Success;
            }

            if (user != null)
            {
                using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
                var queries = items.Count == 0
                    ? new List<ItemQuery> { new ItemQuery(VersionControlPath.RootFolder, RecursionType.Full) }
                    : items.Select(i => new ItemQuery(context.Workspace == null || i.IsServerPath ? i.Path : context.ServerPathOf(i, toolName), recursion, i.DeletionId)).ToList();
                string? workspaceFilter = commandLine.Value("workspace")?.Split(';')[0];
                var sets = context.Client.QueryPendingSets(queries, workspaceFilter, user == "*" ? null : user);
                var changes = sets.SelectMany(s => s.Changes).ToList();
                if (workspaceFilter == null)
                {
                    output.Warning("Changes of local workspaces are not shown with /user unless the workspace is given with /workspace or lies on another computer.",
                        hint: "Pass /workspace:<name>[;<owner>] to see the pending changes of a local workspace.");
                }

                output.Lines(detailed ? RenderDetailed(changes, context.Workspace?.Summary) : RenderAllUsers(changes, context.Workspace?.Summary));
                output.Success(ToJson(new PendingChangeSet(changes, Array.Empty<PendingChangeInfo>())));
                return ExitCodes.Success;
            }

            using (var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: true))
            {
                var workspace = context.RequireWorkspace(toolName);
                var queries = WorkspaceCommandSupport.Queries(items, recursion, context);
                var changes = workspace.GetPendingChanges(queries, detectCandidates: !commandLine.Has("nodetect"));
                output.Lines(detailed ? RenderDetailedSet(changes, workspace.Summary) : Render(changes));
                output.Success(ToJson(changes));
                return ExitCodes.Success;
            }
        }

        internal static IEnumerable<string> Render(PendingChangeSet changes)
        {
            var pending = changes.Pending;
            var candidates = changes.Candidates;
            if (pending.Count == 0 && candidates.Count == 0)
            {
                yield return "There are no pending changes.";
                yield break;
            }

            if (pending.Count > 0)
            {
                foreach (string line in RenderTable(pending))
                {
                    yield return line;
                }
            }

            if (candidates.Count > 0)
            {
                if (pending.Count > 0)
                {
                    yield return string.Empty;
                }

                yield return TextBlocks.Separator;
                yield return "Detected changes:";
                yield return TextBlocks.Separator;
                foreach (string line in RenderTable(candidates))
                {
                    yield return line;
                }
            }

            yield return string.Empty;
            yield return Trailer(pending.Count, candidates.Count);
        }

        internal static string Trailer(int pending, int detected) => detected > 0 ? $"Changes: {pending}, detected changes: {detected}" : $"Changes: {pending}";

        internal static IEnumerable<string> RenderDetailedSet(PendingChangeSet changes, WorkspaceSummary workspace)
        {
            if (changes.Pending.Count == 0 && changes.Candidates.Count == 0)
            {
                yield return "There are no pending changes.";
                yield break;
            }

            foreach (string line in Blocks(changes.Pending, workspace))
            {
                yield return line;
            }

            if (changes.Candidates.Count > 0)
            {
                yield return TextBlocks.Separator;
                yield return "Detected changes:";
                yield return TextBlocks.Separator;
                foreach (string line in Blocks(changes.Candidates, workspace))
                {
                    yield return line;
                }
            }

            yield return Trailer(changes.Pending.Count, changes.Candidates.Count);
        }

        /// <summary>Detailed blocks for a plain list (shelveset or other users' changes).</summary>
        internal static IEnumerable<string> RenderDetailed(IReadOnlyList<PendingChangeInfo> changes, WorkspaceSummary? workspace)
        {
            if (changes.Count == 0)
            {
                yield return "There are no pending changes.";
                yield break;
            }

            foreach (string line in Blocks(changes, workspace))
            {
                yield return line;
            }

            yield return $"Changes: {changes.Count}";
        }

        /// <summary>One block per item (docs/compat.md §4 `pending-detailed`): `$/path;C83`, then User, Date, Lock, Change, Workspace, Source item, Local item, File type.</summary>
        private static IEnumerable<string> Blocks(IReadOnlyList<PendingChangeInfo> changes, WorkspaceSummary? workspace)
        {
            foreach (var change in Ordered(changes))
            {
                yield return change.Version > 0 ? $"{change.ServerPath};C{change.Version}" : change.ServerPath;
                var fields = new List<KeyValuePair<string, string?>>
                {
                    new KeyValuePair<string, string?>("User", OwnerDisplay(change, workspace)),
                    new KeyValuePair<string, string?>("Date", OutputWriter.IsoDate(change.Date)),
                    new KeyValuePair<string, string?>("Lock", change.Lock),
                    new KeyValuePair<string, string?>("Change", change.ChangeText),
                    new KeyValuePair<string, string?>("Workspace", change.Workspace),
                };
                if (change.SourceServerPath != null)
                {
                    fields.Add(new KeyValuePair<string, string?>("Source item", change.SourceServerPath));
                }

                if (change.LocalPath != null)
                {
                    string computer = workspace != null && string.Equals(workspace.Name, change.Workspace, StringComparison.OrdinalIgnoreCase) ? workspace.Computer : Environment.MachineName;
                    fields.Add(new KeyValuePair<string, string?>("Local item", $"[{computer}] {change.LocalPath}"));
                }

                if (!change.IsFolder && change.Encoding != null)
                {
                    fields.Add(new KeyValuePair<string, string?>("File type", change.Encoding == "binary" ? "Binary" : change.Encoding));
                }

                foreach (string line in TextBlocks.Fields(fields))
                {
                    yield return "  " + line;
                }

                yield return string.Empty;
            }
        }

        private static string? OwnerDisplay(PendingChangeInfo change, WorkspaceSummary? workspace)
        {
            if (workspace != null && change.Owner != null && string.Equals(change.Owner, workspace.OwnerUniqueName, StringComparison.OrdinalIgnoreCase))
            {
                return workspace.Owner;
            }

            return change.Owner;
        }

        internal static JObject ToJson(PendingChangeSet changes)
        {
            return new JObject
            {
                ["changes"] = new JArray(changes.Pending.Select(p => p.ToJson()).Cast<object>().ToArray()),
                ["detectedChanges"] = new JArray(changes.Candidates.Select(p => p.ToJson()).Cast<object>().ToArray()),
                ["counts"] = new JObject { ["changes"] = changes.Pending.Count, ["detected"] = changes.Candidates.Count },
            };
        }

        private static List<PendingChangeInfo> Ordered(IEnumerable<PendingChangeInfo> changes) => changes
            .OrderBy(c => VersionControlPath.GetFolderName(c.ServerPath), StringComparer.InvariantCultureIgnoreCase)
            .ThenBy(c => VersionControlPath.GetFileName(c.ServerPath), StringComparer.InvariantCultureIgnoreCase)
            .ToList();

        /// <summary>Table "File name / Change / Local path" grouped by server folder, as tf.exe prints it.</summary>
        internal static IEnumerable<string> RenderTable(IReadOnlyList<PendingChangeInfo> changes) =>
            Table(changes, new[] { "File name", "Change", "Local path" }, c => new[] { VersionControlPath.GetFileName(c.ServerPath), c.ChangeText, c.LocalPath ?? string.Empty });

        /// <summary>`/user`: an extra User column; changes of other workspaces are marked with `* ` (docs/compat.md §4 `all-users`).</summary>
        internal static IEnumerable<string> RenderAllUsers(IReadOnlyList<PendingChangeInfo> changes, WorkspaceSummary? workspace)
        {
            if (changes.Count == 0)
            {
                return new[] { "There are no pending changes." };
            }

            var lines = Table(changes, new[] { "File name", "Change", "User", "Local path" }, c => new[]
            {
                VersionControlPath.GetFileName(c.ServerPath),
                workspace != null && string.Equals(c.Workspace, workspace.Name, StringComparison.OrdinalIgnoreCase) ? c.ChangeText : "* " + c.ChangeText,
                OwnerDisplay(c, workspace) ?? string.Empty,
                c.LocalPath ?? string.Empty,
            }).ToList();
            lines.Add(string.Empty);
            lines.Add($"Changes: {changes.Count}");
            return lines;
        }

        /// <summary>`/shelveset`: the Shelveset column replaces the local path (docs/compat.md §4 `shelveset`).</summary>
        internal static IEnumerable<string> RenderShelveset(IReadOnlyList<PendingChangeInfo> changes)
        {
            if (changes.Count == 0)
            {
                return new[] { "There are no pending changes." };
            }

            var lines = Table(changes, new[] { "File name", "Change", "Shelveset" }, c => new[] { VersionControlPath.GetFileName(c.ServerPath), c.ChangeText, c.Workspace ?? string.Empty }).ToList();
            lines.Add(string.Empty);
            lines.Add($"Changes: {changes.Count}");
            return lines;
        }

        private static IEnumerable<string> Table(IReadOnlyList<PendingChangeInfo> changes, string[] headers, Func<PendingChangeInfo, string[]> cells)
        {
            var ordered = Ordered(changes);
            var rows = ordered.Select(cells).ToList();
            int[] widths = headers.Select((h, i) => Math.Max(h.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length))).ToArray();

            yield return string.Join(" ", headers.Select((h, i) => i == headers.Length - 1 ? h : h.PadRight(widths[i]))).TrimEnd();
            yield return string.Join(" ", widths.Select(w => new string('-', w)));

            string? currentFolder = null;
            for (int r = 0; r < ordered.Count; r++)
            {
                string folder = VersionControlPath.GetFolderName(ordered[r].ServerPath);
                if (!string.Equals(folder, currentFolder, StringComparison.OrdinalIgnoreCase))
                {
                    if (currentFolder != null)
                    {
                        yield return string.Empty;
                    }

                    yield return folder;
                    currentFolder = folder;
                }

                yield return string.Join(" ", rows[r].Select((c, i) => i == rows[r].Length - 1 ? c : c.PadRight(widths[i]))).TrimEnd();
            }
        }
    }
}
