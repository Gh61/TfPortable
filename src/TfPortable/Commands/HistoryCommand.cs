using System;
using System.Collections.Generic;
using System.IO;
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
    /// `tfp history itemspec [/version:spec[~spec]] [/stopafter:n] [/recursive] [/user:name] [/sort:ascending|descending]
    /// [/slotmode] [/itemmode] [/format:brief|json]`. Spike version: brief table and JSON; detailed format and the
    /// search extensions come with milestone 4. Default slot mode, 256 entries without /stopafter (SPEC.md, compat.md §9).
    /// </summary>
    internal sealed class HistoryCommand : ICommand
    {
        private const int DefaultLimit = 256;

        public string Name => "history";

        public string[] Switches => new[] { "version", "stopafter", "recursive", "user", "sort", "slotmode", "itemmode" };

        public int Run(CommandLine commandLine, OutputWriter output, string toolName)
        {
            string? format = commandLine.Value("format");
            if (format != null && !format.Equals("brief", StringComparison.OrdinalIgnoreCase) && !format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/format:{format} is not supported in this build.", "Use /format:brief or /format:json (detailed comes with milestone 4).");
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

            bool ascending = false;
            string? sort = commandLine.Value("sort");
            if (sort != null)
            {
                if (sort.Equals("ascending", StringComparison.OrdinalIgnoreCase))
                {
                    ascending = true;
                }
                else if (!sort.Equals("descending", StringComparison.OrdinalIgnoreCase))
                {
                    throw new TfpException(ErrorCodes.InvalidSwitch, ExitCodes.GeneralError, $"/sort:{sort} is not valid.", "Use /sort:ascending or /sort:descending.");
                }
            }

            using var context = ContextResolver.Resolve(commandLine, toolName, requireWorkspace: false);
            output.Collection = context.CollectionUrl;
            output.Workspace = context.Workspace == null ? null : context.WorkspaceSpec;
            var vcs = context.VersionControl;

            string item = ResolveItem(commandLine.Positionals[0], context, toolName);
            var recursion = commandLine.Has("recursive") ? RecursionType.Full : RecursionType.None;
            bool slotMode = !commandLine.Has("itemmode");
            ParseVersions(commandLine.Value("version"), vcs.AuthorizedUser, out VersionSpec itemVersion, out VersionSpec? from, out VersionSpec? to);
            int limit = stopAfter ?? DefaultLimit;

            // Sorting is done by the server before the limit applies, as in tf.exe (golden history/sort-ascending: the oldest five).
            var changesets = vcs.QueryHistory(item, itemVersion, 0, recursion, commandLine.Value("user"), from, to, limit, false, slotMode, false, ascending)
                .Cast<Changeset>()
                .ToList();

            bool truncated = stopAfter == null && changesets.Count >= DefaultLimit;

            if (changesets.Count == 0)
            {
                output.Line("No history entries were found for the item and version combination.");
            }
            else
            {
                var table = new TextTable("Changeset", "User", "Date", "Comment");
                foreach (var cs in changesets)
                {
                    table.Add(cs.ChangesetId.ToString(), cs.OwnerDisplayName ?? cs.Owner, OutputWriter.IsoDate(cs.CreationDate), FirstLine(cs.Comment));
                }

                foreach (string line in table.Render())
                {
                    output.Line(line);
                }

                if (truncated)
                {
                    int oldest = changesets.Min(c => c.ChangesetId);
                    output.Line();
                    output.Line($"Showing the newest {DefaultLimit} changesets. Use /stopafter:<n> for more, or /version:C{Math.Max(oldest - 1, 1)} to continue from the next one.");
                }
            }

            if (truncated)
            {
                int oldest = changesets.Min(c => c.ChangesetId);
                output.Truncated = new JObject { ["returned"] = changesets.Count, ["limit"] = DefaultLimit, ["hint"] = $"Use /stopafter:<n> or /version:C{Math.Max(oldest - 1, 1)} to continue." };
            }

            output.Success(new JObject
            {
                ["changesets"] = new JArray(changesets.Select(cs => new JObject
                {
                    ["id"] = cs.ChangesetId,
                    ["owner"] = cs.OwnerDisplayName ?? cs.Owner,
                    ["ownerUniqueName"] = cs.Owner,
                    ["date"] = OutputWriter.IsoDate(cs.CreationDate),
                    ["comment"] = cs.Comment,
                }).Cast<object>().ToArray()),
                ["count"] = changesets.Count,
                ["mode"] = slotMode ? "slot" : "item",
            });
            return ExitCodes.Success;
        }

        /// <summary>Server paths are used as given; local paths are mapped through the workspace (TFP0203 when not mapped).</summary>
        private static string ResolveItem(string itemSpec, WorkspaceContext context, string toolName)
        {
            if (VersionControlPath.IsServerItem(itemSpec))
            {
                return itemSpec;
            }

            string local = Path.GetFullPath(itemSpec.Replace('/', Path.DirectorySeparatorChar));
            if (context.Workspace == null || !context.Workspace.IsLocalPathMapped(local))
            {
                throw new TfpException(ErrorCodes.PathNotMapped, ExitCodes.WorkspaceNotFound,
                    $"{local} is not mapped in a local workspace.",
                    $"Use a server path ($/Project/...) or run the command inside a mapped folder; '{toolName} workfold' shows the mappings.", item: local);
            }

            return context.Workspace.GetServerItemForLocalItem(local);
        }

        /// <summary>`C81`, `81`, `T`, `Lname`, `Ddate`, or a range `a~b` (SPEC.md "Versionspec").</summary>
        private static void ParseVersions(string? text, string user, out VersionSpec itemVersion, out VersionSpec? from, out VersionSpec? to)
        {
            itemVersion = VersionSpec.Latest;
            from = null;
            to = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string[] parts = text!.Split(new[] { '~' }, 2);
            try
            {
                if (parts.Length == 2)
                {
                    from = ParseSingle(parts[0], user);
                    to = ParseSingle(parts[1], user);
                    itemVersion = to;
                }
                else
                {
                    to = ParseSingle(parts[0], user);
                    itemVersion = to;
                }
            }
            catch (Exception ex) when (ex is not TfpException)
            {
                throw new TfpException(ErrorCodes.InvalidVersionSpec, ExitCodes.GeneralError, $"'{text}' is not a valid version specification.",
                    "Use C<changeset>, a bare changeset number, T (latest), L<label>[@scope], D<date> (ISO 8601), W[workspace;owner], or a range <from>~<to>.", inner: ex);
            }
        }

        private static VersionSpec ParseSingle(string text, string user)
        {
            string t = text.Trim();
            if (t.Length > 0 && char.IsDigit(t[0]))
            {
                t = "C" + t;
            }

            return VersionSpec.ParseSingleSpec(t, user);
        }

        private static string FirstLine(string? comment)
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
