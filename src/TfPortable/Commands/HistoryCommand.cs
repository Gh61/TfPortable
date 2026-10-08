using System;
using System.Collections.Generic;
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
    /// `tfp history itemspec [/version:spec[~spec]] [/stopafter:n] [/recursive] [/user:name] [/sort:ascending|descending]
    /// [/slotmode] [/itemmode] [/format:brief|detailed|json] [/files] [/comment:text|/regex/] [/changetype:a,b] [/workitem:id]`.
    /// Brief table (with a Change column for a single file), detailed blocks, the search extensions of SPEC.md
    /// "Vyhledávání v historii" (client-side filtering, scan limit 5000), default slot mode, 256 entries without
    /// /stopafter (docs/compat.md §9).
    /// </summary>
    internal sealed class HistoryCommand : ICommand
    {
        internal const int DefaultLimit = 256;
        internal const int ScanLimit = 5000;

        public string Name => "history";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            bool detailed = string.Equals(commandLine.Value("format"), "detailed", StringComparison.OrdinalIgnoreCase);
            if (commandLine.Positionals.Count != 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "history needs exactly one itemspec.", $"Example: '{toolName} history $/Project/Main /recursive /stopafter:20'.");
            }

            int? stopAfter = null;
            if (commandLine.Has("stopafter"))
            {
                if (!int.TryParse(commandLine.Value("stopafter"), out int n) || n <= 0)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/stopafter needs a positive number.", "Example: /stopafter:20.");
                }

                stopAfter = n;
            }

            bool ascending = string.Equals(commandLine.Value("sort"), "ascending", StringComparison.OrdinalIgnoreCase);
            var filter = SearchFilter.Parse(commandLine);
            bool files = commandLine.Has("files");

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var workspace = context.Workspace?.Summary;
            var item = ItemSpecParser.Parse(commandLine.Positionals[0], workspace?.Name, workspace?.Owner);
            string path = context.ServerPathOf(item, toolName);

            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            bool slotMode = !commandLine.Has("itemmode");
            VersionSpec? from = null;
            VersionSpec itemVersion = item.Version ?? VersionSpec.Latest;
            VersionSpec? to = item.Version;
            string? versionText = commandLine.Value("version");
            if (!string.IsNullOrEmpty(versionText))
            {
                var range = VersionSpecParser.ParseRange(versionText!, workspace?.Name, workspace?.Owner);
                from = range.From;
                to = range.To;
                itemVersion = range.To;
            }

            // A single file gets the Change column of tf.exe; an unknown path is an empty result (compat.md §9).
            ItemInfo? target;
            try
            {
                target = context.Client.GetItem(path, itemVersion, item.DeletionId);
            }
            catch (Exception ex) when (ErrorMapper.Map(ex, toolName, context.CollectionUrl).Code == ErrorCodes.ItemNotFound)
            {
                target = null;
            }

            bool singleFile = target != null && !target.IsFolder;
            bool includeChanges = detailed || files || singleFile || filter.NeedsChanges;
            int limit = stopAfter ?? DefaultLimit;

            IReadOnlyList<ChangesetInfo> changesets;
            int scanned = 0;
            bool scanLimitHit = false;
            if (target == null)
            {
                changesets = Array.Empty<ChangesetInfo>();
            }
            else if (filter.IsEmpty)
            {
                changesets = Query(context.Client, path, itemVersion, item.DeletionId, recursion, commandLine.Value("user"), from, to, limit, slotMode, ascending, includeChanges);
                scanned = changesets.Count;
            }
            else
            {
                // Filtering happens on the client: /stopafter counts matches, the scan stops after ScanLimit changesets.
                var all = Query(context.Client, path, itemVersion, item.DeletionId, recursion, commandLine.Value("user"), from, to, ScanLimit, slotMode, ascending, includeChanges);
                scanned = all.Count;
                scanLimitHit = all.Count >= ScanLimit;
                changesets = all.Where(cs => filter.Matches(cs, context.Client)).Take(limit).ToList();
            }

            bool truncated = stopAfter == null && changesets.Count >= DefaultLimit;
            output.Lines(detailed ? RenderDetailed(changesets, truncated) : Render(changesets, truncated, singleFile ? path : null, files));
            if (truncated || scanLimitHit)
            {
                int oldest = changesets.Count == 0 ? 1 : changesets.Min(c => c.Id);
                string hint = scanLimitHit && !truncated
                    ? $"The search stopped after {ScanLimit} changesets; continue with /version:C{Math.Max(oldest - 1, 1)}."
                    : $"Use /stopafter:<n> or /version:C{Math.Max(oldest - 1, 1)} to continue.";
                output.Truncated = new JObject { ["returned"] = changesets.Count, ["limit"] = scanLimitHit && !truncated ? ScanLimit : DefaultLimit, ["hint"] = hint };
                if (scanLimitHit && !truncated)
                {
                    output.Line(string.Empty);
                    output.Line(hint);
                }
            }

            output.Success(ToJson(changesets, slotMode, includeChanges, filter.IsEmpty ? (int?)null : scanned));
            return ExitCodes.Success;
        }

        private static IReadOnlyList<ChangesetInfo> Query(ITfvcClient client, string path, VersionSpec version, int deletionId, RecursionType recursion, string? user,
            VersionSpec? from, VersionSpec? to, int limit, bool slotMode, bool ascending, bool includeChanges)
        {
            // Sorting is done by the server before the limit applies, as in tf.exe (golden history/sort-ascending: the oldest five).
            return client.QueryHistory(new HistoryQuery(path, version, recursion, user, from, to, limit, slotMode, ascending, deletionId, includeChanges));
        }

        /// <summary>Brief table: Changeset, [Change], User, Date, Comment; with /files the changed items follow each row, indented.</summary>
        internal static IEnumerable<string> Render(IReadOnlyList<ChangesetInfo> changesets, bool truncated, string? singleFilePath, bool files)
        {
            if (changesets.Count == 0)
            {
                yield return "No history entries were found for the item and version combination.";
                yield break;
            }

            var table = singleFilePath != null
                ? new TextTable("Changeset", "Change", "User", "Date", "Comment")
                : new TextTable("Changeset", "User", "Date", "Comment");
            foreach (var cs in changesets)
            {
                string date = OutputWriter.IsoDate(cs.Date) ?? string.Empty;
                if (singleFilePath != null)
                {
                    table.Add(cs.Id.ToString(), ChangeOf(cs, singleFilePath), cs.Owner, date, FirstLine(cs.Comment));
                }
                else
                {
                    table.Add(cs.Id.ToString(), cs.Owner, date, FirstLine(cs.Comment));
                }
            }

            var rows = table.Render().ToList();
            yield return rows[0];
            yield return rows[1];
            for (int i = 0; i < changesets.Count; i++)
            {
                yield return rows[i + 2];
                if (files && changesets[i].Changes != null)
                {
                    foreach (var change in changesets[i].Changes!)
                    {
                        yield return $"  {change.ChangeText} {change.DisplayPath}";
                    }
                }
            }

            if (truncated)
            {
                int oldest = changesets.Min(c => c.Id);
                yield return string.Empty;
                yield return $"Showing the newest {DefaultLimit} changesets. Use /stopafter:<n> for more, or /version:C{Math.Max(oldest - 1, 1)} to continue from the next one.";
            }
        }

        /// <summary>Detailed: a dashed separator, then the block of `changeset` for every entry (docs/compat.md §9).</summary>
        internal static IEnumerable<string> RenderDetailed(IReadOnlyList<ChangesetInfo> changesets, bool truncated)
        {
            if (changesets.Count == 0)
            {
                yield return "No history entries were found for the item and version combination.";
                yield break;
            }

            foreach (var cs in changesets)
            {
                yield return TextBlocks.Separator;
                foreach (string line in ChangesetCommand.RenderBlock(cs))
                {
                    yield return line;
                }

                yield return string.Empty;
            }

            if (truncated)
            {
                int oldest = changesets.Min(c => c.Id);
                yield return $"Showing the newest {DefaultLimit} changesets. Use /stopafter:<n> for more, or /version:C{Math.Max(oldest - 1, 1)} to continue from the next one.";
            }
        }

        /// <summary>The change type of one item within a changeset (the Change column of a single-file history).</summary>
        private static string ChangeOf(ChangesetInfo cs, string path)
        {
            var change = cs.Changes?.FirstOrDefault(c => string.Equals(c.ServerPath, path, StringComparison.OrdinalIgnoreCase)) ?? cs.Changes?.FirstOrDefault();
            return change?.ChangeText ?? string.Empty;
        }

        internal static JObject ToJson(IReadOnlyList<ChangesetInfo> changesets, bool slotMode, bool includeChanges, int? scanned)
        {
            var data = new JObject
            {
                ["changesets"] = new JArray(changesets.Select(cs => cs.ToJson(includeChanges)).Cast<object>().ToArray()),
                ["count"] = changesets.Count,
                ["mode"] = slotMode ? "slot" : "item",
            };
            if (scanned != null)
            {
                data["scanned"] = scanned;
            }

            return data;
        }

        internal static string FirstLine(string? comment)
        {
            if (string.IsNullOrEmpty(comment))
            {
                return string.Empty;
            }

            int i = comment!.IndexOfAny(new[] { '\r', '\n' });
            return (i < 0 ? comment : comment.Substring(0, i)).TrimEnd();
        }

        /// <summary>The search extensions: `/comment:text` or `/comment:/regex/`, `/changetype:a,b`, `/workitem:id`.</summary>
        internal sealed class SearchFilter
        {
            private readonly Regex? comment;
            private readonly HashSet<string> changeTypes;
            private readonly int? workItem;

            private SearchFilter(Regex? comment, HashSet<string> changeTypes, int? workItem)
            {
                this.comment = comment;
                this.changeTypes = changeTypes;
                this.workItem = workItem;
            }

            public bool IsEmpty => comment == null && changeTypes.Count == 0 && workItem == null;

            public bool NeedsChanges => changeTypes.Count > 0;

            public static SearchFilter Parse(CommandLine commandLine)
            {
                Regex? comment = null;
                string? text = commandLine.Value("comment");
                if (!string.IsNullOrEmpty(text))
                {
                    try
                    {
                        comment = text!.Length > 2 && text[0] == '/' && text[text.Length - 1] == '/'
                            ? new Regex(text.Substring(1, text.Length - 2), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                            : new Regex(Regex.Escape(text), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/comment:{text} is not a valid .NET regular expression: {ex.Message}", "Write the pattern between slashes, e.g. /comment:/BUG-\\d+/.", inner: ex);
                    }
                }

                var types = new HashSet<string>(commandLine.Values("changetype").Select(t => t.ToLowerInvariant()), StringComparer.Ordinal);
                string[] known = { "add", "edit", "delete", "rename", "branch", "merge", "undelete", "rollback", "lock", "encoding", "property", "source rename" };
                var unknown = types.Where(t => !known.Contains(t)).ToList();
                if (unknown.Count > 0)
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/changetype:{string.Join(",", unknown)} is not a change type.", $"Use {string.Join(", ", known)}.");
                }

                int? workItem = null;
                if (commandLine.Has("workitem"))
                {
                    if (!int.TryParse(commandLine.Value("workitem"), out int id) || id <= 0)
                    {
                        throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "/workitem needs a work item id.", "Example: /workitem:1234.");
                    }

                    workItem = id;
                }

                return new SearchFilter(comment, types, workItem);
            }

            public bool Matches(ChangesetInfo cs, ITfvcClient client)
            {
                if (comment != null && !comment.IsMatch(cs.Comment ?? string.Empty))
                {
                    return false;
                }

                if (changeTypes.Count > 0 && !(cs.Changes ?? Array.Empty<ChangeInfo>()).Any(c => c.ChangeTypes.Any(changeTypes.Contains)))
                {
                    return false;
                }

                if (workItem != null)
                {
                    var workItems = cs.WorkItems;
                    if (workItems == null)
                    {
                        workItems = client.GetChangeset(cs.Id).WorkItems ?? Array.Empty<WorkItemInfo>();
                    }

                    if (!workItems.Any(w => w.Id == workItem.Value))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
