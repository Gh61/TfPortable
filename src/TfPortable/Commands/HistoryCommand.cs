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
    /// `tfp history itemspec [/version:spec[~spec]] [/stopafter:n] [/recursive] [/user:name] [/sort:ascending|descending]
    /// [/slotmode] [/itemmode] [/format:brief|json]`. Brief table and JSON; detailed format and the search extensions
    /// come with milestone 4. Default slot mode, 256 entries without /stopafter (SPEC.md, compat.md §9).
    /// </summary>
    internal sealed class HistoryCommand : ICommand
    {
        internal const int DefaultLimit = 256;

        public string Name => "history";

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            string? format = commandLine.Value("format");
            if (format != null && format.Equals("detailed", StringComparison.OrdinalIgnoreCase))
            {
                throw new TfpException(ErrorCodes.NotSupported, ExitCodes.GeneralError, "/format:detailed is not available in this build.", "Use /format:brief or /format:json; detailed comes with milestone 4.");
            }

            foreach (string extension in new[] { "comment", "changetype", "workitem", "files" })
            {
                if (commandLine.Has(extension))
                {
                    throw new TfpException(ErrorCodes.NotSupported, ExitCodes.GeneralError, $"/{extension} is not available in this build.", "The history search extensions come with milestone 4.");
                }
            }

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

            int limit = stopAfter ?? DefaultLimit;

            // Sorting is done by the server before the limit applies, as in tf.exe (golden history/sort-ascending: the oldest five).
            var changesets = context.Client.QueryHistory(new HistoryQuery(path, itemVersion, recursion, commandLine.Value("user"), from, to, limit, slotMode, ascending));
            bool truncated = stopAfter == null && changesets.Count >= DefaultLimit;

            output.Lines(Render(changesets, truncated));
            if (truncated)
            {
                int oldest = changesets.Min(c => c.Id);
                output.Truncated = new JObject { ["returned"] = changesets.Count, ["limit"] = DefaultLimit, ["hint"] = $"Use /stopafter:<n> or /version:C{Math.Max(oldest - 1, 1)} to continue." };
            }

            output.Success(ToJson(changesets, slotMode));
            return ExitCodes.Success;
        }

        internal static IEnumerable<string> Render(IReadOnlyList<ChangesetInfo> changesets, bool truncated)
        {
            if (changesets.Count == 0)
            {
                yield return "No history entries were found for the item and version combination.";
                yield break;
            }

            var table = new TextTable("Changeset", "User", "Date", "Comment");
            foreach (var cs in changesets)
            {
                table.Add(cs.Id.ToString(), cs.Owner, OutputWriter.IsoDate(cs.Date), FirstLine(cs.Comment));
            }

            foreach (string line in table.Render())
            {
                yield return line;
            }

            if (truncated)
            {
                int oldest = changesets.Min(c => c.Id);
                yield return string.Empty;
                yield return $"Showing the newest {DefaultLimit} changesets. Use /stopafter:<n> for more, or /version:C{Math.Max(oldest - 1, 1)} to continue from the next one.";
            }
        }

        internal static JObject ToJson(IReadOnlyList<ChangesetInfo> changesets, bool slotMode)
        {
            return new JObject
            {
                ["changesets"] = new JArray(changesets.Select(cs => cs.ToJson()).Cast<object>().ToArray()),
                ["count"] = changesets.Count,
                ["mode"] = slotMode ? "slot" : "item",
            };
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
    }
}
