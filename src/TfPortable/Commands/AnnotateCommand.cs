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
    /// <summary>
    /// `tfp annotate itemspec [/version:v]` (TfPortable extension, tf.exe has no such command): which changeset, owner and date
    /// last changed every line. The file's history is walked from the oldest changeset to the requested version; the lines a
    /// version keeps unchanged (by the same diff `difference` uses) keep their attribution, new lines get the changeset.
    /// </summary>
    internal sealed class AnnotateCommand : ICommand
    {
        public string Name => "annotate";

        /// <summary>One attributed line.</summary>
        internal sealed class LineInfo
        {
            public LineInfo(int number, ChangesetInfo changeset, string text)
            {
                Number = number;
                Changeset = changeset;
                Text = text;
            }

            public int Number { get; }

            public ChangesetInfo Changeset { get; }

            public string Text { get; }
        }

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            if (commandLine.Positionals.Count != 1)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, "annotate needs exactly one file.", $"Example: '{toolName} annotate Main\\\\src\\\\App.cs'.");
            }

            using var context = ContextResolver.Resolve(commandLine, output, toolName, requireWorkspace: false);
            var summary = context.Workspace?.Summary;
            var spec = ItemSpecParser.Parse(commandLine.Positionals[0], summary?.Name, summary?.Owner);
            string serverPath = context.ServerPathOf(spec, toolName);
            string? versionText = commandLine.Value("version");
            VersionSpec version = spec.Version ?? (versionText != null ? VersionSpecParser.ParseSingle(versionText, summary?.Name, summary?.Owner)
                : !spec.IsServerPath && summary != null ? new WorkspaceVersionSpec(summary.Name, summary.Owner) : VersionSpec.Latest);

            var item = context.Client.GetItem(serverPath, version, spec.DeletionId);
            if (item == null)
            {
                throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError, $"{serverPath} does not exist at version {version.DisplayString}.", $"Check the path with '{toolName} dir' or the version with '{toolName} history'.", item: serverPath);
            }

            if (item.IsFolder)
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"{serverPath} is a folder. annotate works on files.", "Give a file.");
            }

            if (item.IsBinary)
            {
                throw new TfpException(ErrorCodes.BinaryContent, ExitCodes.GeneralError, $"{serverPath} is binary. annotate works on text files.", "Nothing to annotate.");
            }

            // Every changeset of the file up to the version, oldest first, with the path the file had in that changeset (renames).
            var history = context.Client.QueryHistory(new HistoryQuery(serverPath, version, RecursionType.None, null, null, version, int.MaxValue, false, true, spec.DeletionId, includeChanges: true))
                .OrderBy(c => c.Id).ToList();
            if (history.Count == 0)
            {
                throw new TfpException(ErrorCodes.ItemNotFound, ExitCodes.GeneralError, $"{serverPath} has no history up to version {version.DisplayString}.", $"Check the version with '{toolName} history'.", item: serverPath);
            }

            var lines = Annotate(history, item.ItemId, (path, changesetId) =>
            {
                var content = context.Client.Download(path, new ChangesetVersionSpec(changesetId), 0);
                return TextContent.Decode(content.Bytes, content.Item.Encoding) ?? string.Empty;
            });

            output.Lines(Render(lines));
            output.Success(new JObject
            {
                ["item"] = serverPath,
                ["version"] = item.ChangesetId,
                ["lines"] = new JArray(lines.Select(l => new JObject
                {
                    ["number"] = l.Number,
                    ["changeset"] = l.Changeset.Id,
                    ["owner"] = l.Changeset.Owner,
                    ["date"] = OutputWriter.IsoDate(l.Changeset.Date),
                    ["text"] = l.Text,
                }).Cast<object>().ToArray()),
                ["changesets"] = new JArray(lines.Select(l => l.Changeset).GroupBy(c => c.Id).Select(g => g.First().ToJson(false)).Cast<object>().ToArray()),
            });
            return ExitCodes.Success;
        }

        /// <summary>Walks the versions oldest first; `download(path, changesetId)` returns the text of the file in that changeset.</summary>
        internal static List<LineInfo> Annotate(IReadOnlyList<ChangesetInfo> history, int itemId, Func<string, int, string> download)
        {
            ChangesetInfo[] attribution = Array.Empty<ChangesetInfo>();
            string[] current = Array.Empty<string>();
            string currentText = string.Empty;
            string? lastPath = null;
            foreach (var changeset in history)
            {
                // The path of the item in this changeset: the change with the item's id, else the last known path.
                var change = (changeset.Changes ?? Array.Empty<ChangeInfo>()).FirstOrDefault(c => c.ItemId == itemId);
                string? path = change?.ServerPath ?? lastPath;
                if (path == null)
                {
                    // A changeset without the item (an odd history): nothing to attribute yet.
                    continue;
                }

                lastPath = path;
                if (change != null && change.ChangeTypes.Contains("delete") && !change.ChangeTypes.Contains("undelete"))
                {
                    continue;
                }

                string text = download(path, changeset.Id);
                string[] next = UnifiedDiff.SplitLines(text);
                var nextAttribution = new ChangesetInfo[next.Length];
                for (int i = 0; i < next.Length; i++)
                {
                    nextAttribution[i] = changeset;
                }

                if (current.Length > 0)
                {
                    // Line endings do not change who wrote a line.
                    foreach (var pair in UnifiedDiff.MatchingLines(currentText, text, DiffOptions.IgnoreEol))
                    {
                        if (pair.Key < attribution.Length && pair.Value < nextAttribution.Length)
                        {
                            nextAttribution[pair.Value] = attribution[pair.Key];
                        }
                    }
                }

                current = next;
                currentText = text;
                attribution = nextAttribution;
            }

            return current.Select((text, i) => new LineInfo(i + 1, attribution[i], text)).ToList();
        }

        internal static IEnumerable<string> Render(IReadOnlyList<LineInfo> lines)
        {
            int ownerWidth = Math.Min(24, lines.Count == 0 ? 5 : Math.Max(5, lines.Max(l => l.Changeset.Owner.Length)));
            yield return $"{"Changeset",9} {"Owner".PadRight(ownerWidth)} {"Date",-10} Line";
            foreach (var line in lines)
            {
                string owner = line.Changeset.Owner.Length > ownerWidth ? line.Changeset.Owner.Substring(0, ownerWidth) : line.Changeset.Owner.PadRight(ownerWidth);
                yield return $"{line.Changeset.Id,9} {owner} {line.Changeset.Date:yyyy-MM-dd} {line.Text}";
            }
        }
    }
}
